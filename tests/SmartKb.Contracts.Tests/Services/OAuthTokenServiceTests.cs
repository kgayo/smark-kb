using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests.Services;

public sealed class OAuthTokenServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly string TestSigningKey = Convert.ToBase64String(new byte[32]);

    private readonly InMemorySecretProvider _secretProvider = new();
    private readonly OAuthSettings _settings = new()
    {
        CallbackBaseUrl = "https://smartkb.example.com",
        StateSigningKey = TestSigningKey,
    };

    private OAuthTokenService CreateService(HttpMessageHandler? handler = null, TimeProvider? timeProvider = null)
    {
        var factory = new TestHttpClientFactory(handler ?? new FakeTokenHandler());
        return new OAuthTokenService(
            _secretProvider, factory, _settings,
            NullLogger<OAuthTokenService>.Instance,
            timeProvider);
    }

    [Theory]
    [InlineData(ConnectorType.HubSpot, "https://app.hubspot.com/oauth/authorize")]
    [InlineData(ConnectorType.ClickUp, "https://app.clickup.com/api")]
    [InlineData(ConnectorType.AzureDevOps, "https://app.vssps.visualstudio.com/oauth2/authorize")]
    public void BuildAuthorizeUrl_ReturnsCorrectProviderUrl(ConnectorType type, string expectedBase)
    {
        var service = CreateService();
        var sourceConfig = JsonSerializer.Serialize(new { oAuthClientId = "test-client" }, JsonOptions);

        var url = service.BuildAuthorizeUrl(type, Guid.NewGuid(), "tenant-1", sourceConfig);

        Assert.StartsWith(expectedBase, url);
        Assert.Contains("client_id=test-client", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("state=", url);
    }

    [Fact]
    public void BuildAuthorizeUrl_SharePoint_IncludesTenantIdInUrl()
    {
        var service = CreateService();
        var sourceConfig = JsonSerializer.Serialize(new
        {
            entraIdTenantId = "my-entra-tenant",
            oAuthClientId = "sp-client"
        }, JsonOptions);

        var url = service.BuildAuthorizeUrl(ConnectorType.SharePoint, Guid.NewGuid(), "tenant-1", sourceConfig);

        Assert.Contains("login.microsoftonline.com/my-entra-tenant", url);
        Assert.Contains("client_id=sp-client", url);
    }

    [Fact]
    public void BuildAuthorizeUrl_IncludesRedirectUri()
    {
        var service = CreateService();
        var connectorId = Guid.NewGuid();
        var sourceConfig = JsonSerializer.Serialize(new { oAuthClientId = "c1" }, JsonOptions);

        var url = service.BuildAuthorizeUrl(ConnectorType.HubSpot, connectorId, "t1", sourceConfig);

        var expectedRedirect = Uri.EscapeDataString(
            $"https://smartkb.example.com/api/admin/connectors/{connectorId}/oauth/callback");
        Assert.Contains($"redirect_uri={expectedRedirect}", url);
    }

    [Fact]
    public void ValidateState_ValidState_ReturnsTrue()
    {
        var service = CreateService();
        var connectorId = Guid.NewGuid();
        var tenantId = "tenant-1";

        var state = service.GenerateState(connectorId, tenantId);

        Assert.True(service.ValidateState(state, connectorId, tenantId));
    }

    [Fact]
    public void ValidateState_WrongConnectorId_ReturnsFalse()
    {
        var service = CreateService();
        var state = service.GenerateState(Guid.NewGuid(), "tenant-1");

        Assert.False(service.ValidateState(state, Guid.NewGuid(), "tenant-1"));
    }

    [Fact]
    public void ValidateState_WrongTenant_ReturnsFalse()
    {
        var service = CreateService();
        var connectorId = Guid.NewGuid();
        var state = service.GenerateState(connectorId, "tenant-1");

        Assert.False(service.ValidateState(state, connectorId, "tenant-2"));
    }

    [Fact]
    public void ValidateState_ExpiredState_ReturnsFalse()
    {
        var fakeTime = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var service = CreateService(timeProvider: fakeTime);
        var connectorId = Guid.NewGuid();

        var state = service.GenerateState(connectorId, "tenant-1");

        // Advance time past the 10-minute window.
        fakeTime.Advance(TimeSpan.FromMinutes(11));

        Assert.False(service.ValidateState(state, connectorId, "tenant-1"));
    }

    [Fact]
    public void ValidateState_TamperedSignature_ReturnsFalse()
    {
        var service = CreateService();
        var state = service.GenerateState(Guid.NewGuid(), "tenant-1");
        var tampered = state[..^5] + "XXXXX";

        Assert.False(service.ValidateState(tampered, Guid.NewGuid(), "tenant-1"));
    }

    [Fact]
    public void ValidateState_MalformedState_ReturnsFalse()
    {
        var service = CreateService();
        Assert.False(service.ValidateState("not-valid", Guid.NewGuid(), "t1"));
        Assert.False(service.ValidateState("", Guid.NewGuid(), "t1"));
    }

    [Fact]
    public async Task ExchangeCodeAsync_Success_StoresCredentialsInKV()
    {
        var handler = new FakeTokenHandler(new OAuthTokenEndpointResponse
        {
            AccessToken = "new-access-token",
            RefreshToken = "new-refresh-token",
            ExpiresIn = 3600,
        });
        var service = CreateService(handler);

        // Pre-populate KV with client credentials.
        var initialCreds = new OAuthCredentials
        {
            ClientId = "client-1",
            ClientSecret = "secret-1",
        };
        _secretProvider.Secrets["kv-secret"] = JsonSerializer.Serialize(initialCreds, JsonOptions);

        var result = await service.ExchangeCodeAsync(
            Guid.NewGuid(), "tenant-1", "auth-code",
            "kv-secret", null, ConnectorType.HubSpot);

        Assert.True(result);

        // Verify credentials updated in KV.
        var stored = JsonSerializer.Deserialize<OAuthCredentials>(_secretProvider.Secrets["kv-secret"], JsonOptions)!;
        Assert.Equal("new-access-token", stored.AccessToken);
        Assert.Equal("new-refresh-token", stored.RefreshToken);
        Assert.Equal("client-1", stored.ClientId);
        Assert.Equal("secret-1", stored.ClientSecret);
        Assert.NotNull(stored.ExpiresAt);
    }

    [Fact]
    public async Task ExchangeCodeAsync_NoClientSecret_ReturnsFalse()
    {
        var service = CreateService();

        // Pre-populate KV with credentials missing client_secret.
        _secretProvider.Secrets["kv-secret"] = JsonSerializer.Serialize(
            new OAuthCredentials { ClientId = "c1" }, JsonOptions);

        var result = await service.ExchangeCodeAsync(
            Guid.NewGuid(), "t1", "code", "kv-secret", null, ConnectorType.HubSpot);

        Assert.False(result);
    }

    [Fact]
    public async Task ExchangeCodeAsync_NoKvSecret_ReturnsFalse()
    {
        var service = CreateService();

        var result = await service.ExchangeCodeAsync(
            Guid.NewGuid(), "t1", "code", "missing-secret", null, ConnectorType.HubSpot);

        Assert.False(result);
    }

    [Fact]
    public async Task ResolveAccessTokenAsync_ValidToken_ReturnsExisting()
    {
        var service = CreateService();

        var creds = new OAuthCredentials
        {
            ClientId = "c1",
            ClientSecret = "s1",
            AccessToken = "valid-token",
            RefreshToken = "rt",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };
        _secretProvider.Secrets["kv-secret"] = JsonSerializer.Serialize(creds, JsonOptions);

        var token = await service.ResolveAccessTokenAsync("kv-secret", null, ConnectorType.HubSpot);

        Assert.Equal("valid-token", token);
    }

    [Fact]
    public async Task ResolveAccessTokenAsync_ExpiredToken_RefreshesAndUpdatesKV()
    {
        var handler = new FakeTokenHandler(new OAuthTokenEndpointResponse
        {
            AccessToken = "refreshed-token",
            RefreshToken = "new-refresh",
            ExpiresIn = 3600,
        });
        var service = CreateService(handler);

        var creds = new OAuthCredentials
        {
            ClientId = "c1",
            ClientSecret = "s1",
            AccessToken = "expired-token",
            RefreshToken = "old-refresh",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10), // Expired.
        };
        _secretProvider.Secrets["kv-secret"] = JsonSerializer.Serialize(creds, JsonOptions);

        var token = await service.ResolveAccessTokenAsync("kv-secret", null, ConnectorType.HubSpot);

        Assert.Equal("refreshed-token", token);

        // Verify KV updated.
        var stored = JsonSerializer.Deserialize<OAuthCredentials>(_secretProvider.Secrets["kv-secret"], JsonOptions)!;
        Assert.Equal("refreshed-token", stored.AccessToken);
        Assert.Equal("new-refresh", stored.RefreshToken);
    }

    [Fact]
    public async Task ResolveAccessTokenAsync_RefreshFails_ReturnsNull()
    {
        var handler = new FakeTokenHandler(statusCode: HttpStatusCode.BadRequest);
        var service = CreateService(handler);

        var creds = new OAuthCredentials
        {
            ClientId = "c1",
            ClientSecret = "s1",
            AccessToken = "expired-token",
            RefreshToken = "old-refresh",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        };
        _secretProvider.Secrets["kv-secret"] = JsonSerializer.Serialize(creds, JsonOptions);

        var token = await service.ResolveAccessTokenAsync("kv-secret", null, ConnectorType.HubSpot);

        Assert.Null(token);
    }

    [Fact]
    public async Task ResolveAccessTokenAsync_NoSecret_ReturnsNull()
    {
        var service = CreateService();

        var token = await service.ResolveAccessTokenAsync("missing-secret", null, ConnectorType.HubSpot);

        Assert.Null(token);
    }

    [Fact]
    public async Task ResolveAccessTokenAsync_NoAccessToken_ReturnsNull()
    {
        var service = CreateService();

        var creds = new OAuthCredentials { ClientId = "c1", ClientSecret = "s1" };
        _secretProvider.Secrets["kv-secret"] = JsonSerializer.Serialize(creds, JsonOptions);

        var token = await service.ResolveAccessTokenAsync("kv-secret", null, ConnectorType.HubSpot);

        Assert.Null(token);
    }

    [Fact]
    public void OAuthCredentials_SerializationRoundTrip()
    {
        var creds = new OAuthCredentials
        {
            ClientId = "cid",
            ClientSecret = "csecret",
            AccessToken = "at",
            RefreshToken = "rt",
            ExpiresAt = new DateTimeOffset(2026, 3, 19, 12, 0, 0, TimeSpan.Zero),
        };

        var json = JsonSerializer.Serialize(creds, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<OAuthCredentials>(json, JsonOptions)!;

        Assert.Equal(creds.ClientId, deserialized.ClientId);
        Assert.Equal(creds.ClientSecret, deserialized.ClientSecret);
        Assert.Equal(creds.AccessToken, deserialized.AccessToken);
        Assert.Equal(creds.RefreshToken, deserialized.RefreshToken);
        Assert.Equal(creds.ExpiresAt, deserialized.ExpiresAt);
    }

    [Theory]
    [InlineData(ConnectorType.HubSpot, "https://api.hubapi.com/oauth/v1/token")]
    [InlineData(ConnectorType.ClickUp, "https://api.clickup.com/api/v2/oauth/token")]
    [InlineData(ConnectorType.AzureDevOps, "https://app.vssps.visualstudio.com/oauth2/token")]
    public void GetTokenUrl_ReturnsCorrectEndpoint(ConnectorType type, string expected)
    {
        var url = OAuthTokenService.GetTokenUrl(type, null);
        Assert.Equal(expected, url);
    }

    [Fact]
    public void GetTokenUrl_SharePoint_IncludesTenantId()
    {
        var sourceConfig = JsonSerializer.Serialize(new { entraIdTenantId = "abc-tenant" }, JsonOptions);
        var url = OAuthTokenService.GetTokenUrl(ConnectorType.SharePoint, sourceConfig);
        Assert.Contains("abc-tenant", url);
    }

    // --- Test helpers ---

    private sealed class InMemorySecretProvider : ISecretProvider
    {
        public Dictionary<string, string> Secrets { get; } = [];

        public Task<string> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
        {
            if (Secrets.TryGetValue(secretName, out var val))
                return Task.FromResult(val);
            throw new KeyNotFoundException($"Secret '{secretName}' not found.");
        }

        public Task SetSecretAsync(string secretName, string secretValue, CancellationToken cancellationToken = default)
        {
            Secrets[secretName] = secretValue;
            return Task.CompletedTask;
        }

        public Task DeleteSecretAsync(string secretName, CancellationToken cancellationToken = default)
        {
            Secrets.Remove(secretName);
            return Task.CompletedTask;
        }

        public Task<SecretProperties?> GetSecretPropertiesAsync(string secretName, CancellationToken cancellationToken = default) =>
            Task.FromResult<SecretProperties?>(Secrets.ContainsKey(secretName)
                ? new SecretProperties(secretName, DateTimeOffset.UtcNow, null, null, true)
                : null);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public TestHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class FakeTokenHandler : HttpMessageHandler
    {
        private readonly OAuthTokenEndpointResponse? _response;
        private readonly HttpStatusCode _statusCode;

        public FakeTokenHandler(OAuthTokenEndpointResponse? response = null, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _response = response ?? new OAuthTokenEndpointResponse
            {
                AccessToken = "test-access-token",
                RefreshToken = "test-refresh-token",
                ExpiresIn = 3600,
            };
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_statusCode != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent("{\"error\":\"invalid_grant\"}"),
                });
            }

            var json = JsonSerializer.Serialize(_response, JsonOptions);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
