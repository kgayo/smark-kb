using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartKb.Contracts.Enums;
using SmartKb.Contracts.Models;

namespace SmartKb.Api.Tests.Connectors;

public sealed class ConnectorOAuthEndpointTests : IClassFixture<ConnectorOAuthTestFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ConnectorOAuthTestFactory _factory;
    private HttpClient _client = null!;

    public ConnectorOAuthEndpointTests(ConnectorOAuthTestFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.EnsureDatabaseAsync();
        _client = _factory.CreateAuthenticatedClient();
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Authorize_ReturnsUrl_WhenOAuthConnector()
    {
        var connector = await CreateOAuthConnectorAsync();

        var response = await _client.GetAsync($"/api/admin/connectors/{connector.Id}/oauth/authorize");
        response.EnsureSuccessStatusCode();

        var body = await Deserialize<ApiResponse<OAuthAuthorizeUrlResponse>>(response);
        Assert.True(body!.IsSuccess);
        Assert.NotNull(body.Data);
        Assert.Contains("https://app.hubspot.com/oauth/authorize", body.Data.AuthorizeUrl);
        Assert.Contains("response_type=code", body.Data.AuthorizeUrl);
    }

    [Fact]
    public async Task Authorize_Returns404_WhenNotOAuthAuthType()
    {
        var connector = await CreatePatConnectorAsync();

        var response = await _client.GetAsync($"/api/admin/connectors/{connector.Id}/oauth/authorize");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_Returns404_WhenConnectorNotFound()
    {
        var response = await _client.GetAsync($"/api/admin/connectors/{Guid.NewGuid()}/oauth/authorize");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Callback_Returns400_WhenInvalidState()
    {
        var connector = await CreateOAuthConnectorAsync();

        var response = await _client.GetAsync(
            $"/api/admin/connectors/{connector.Id}/oauth/callback?code=test-code&state=invalid-state");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Callback_Returns404_WhenConnectorNotFound()
    {
        var response = await _client.GetAsync(
            $"/api/admin/connectors/{Guid.NewGuid()}/oauth/callback?code=test-code&state=some-state");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_RequiresAuthentication()
    {
        using var anonClient = _factory.CreateClient();
        var response = await anonClient.GetAsync($"/api/admin/connectors/{Guid.NewGuid()}/oauth/authorize");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authorize_RequiresAdminRole()
    {
        using var agentClient = _factory.CreateAuthenticatedClient(roles: "SupportAgent");
        var response = await agentClient.GetAsync($"/api/admin/connectors/{Guid.NewGuid()}/oauth/authorize");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TenantIsolation_CannotAccessOtherTenantConnector()
    {
        var connector = await CreateOAuthConnectorAsync();

        using var otherTenantClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-other");
        var response = await otherTenantClient.GetAsync(
            $"/api/admin/connectors/{connector.Id}/oauth/authorize");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TestConnection_WithOAuthConnector_Works()
    {
        var connector = await CreateOAuthConnectorAsync();

        var response = await _client.PostAsync($"/api/admin/connectors/{connector.Id}/test", null);
        response.EnsureSuccessStatusCode();

        var body = await Deserialize<ApiResponse<TestConnectionResponse>>(response);
        Assert.NotNull(body?.Data);
    }

    // --- Helpers ---

    private async Task<ConnectorResponse> CreateOAuthConnectorAsync()
    {
        var sourceConfig = JsonSerializer.Serialize(new
        {
            portalId = "12345",
            oAuthClientId = "test-oauth-client",
        }, JsonOptions);

        var request = new CreateConnectorRequest
        {
            Name = $"oauth-hubspot-{Guid.NewGuid():N}",
            ConnectorType = ConnectorType.HubSpot,
            AuthType = SecretAuthType.OAuth,
            SourceConfig = sourceConfig,
            KeyVaultSecretName = $"kv-oauth-{Guid.NewGuid():N}",
        };

        // Pre-populate the KV secret with client credentials.
        _factory.SetSecret(request.KeyVaultSecretName!, JsonSerializer.Serialize(new
        {
            client_id = "test-oauth-client",
            client_secret = "test-oauth-secret",
            access_token = "test-access-token",
            refresh_token = "test-refresh-token",
            expires_at = DateTimeOffset.UtcNow.AddHours(1).ToString("o"),
        }));

        var response = await _client.PostAsJsonAsync("/api/admin/connectors", request);
        response.EnsureSuccessStatusCode();

        var body = await Deserialize<ApiResponse<ConnectorResponse>>(response);
        return body!.Data!;
    }

    private async Task<ConnectorResponse> CreatePatConnectorAsync()
    {
        var request = new CreateConnectorRequest
        {
            Name = $"pat-ado-{Guid.NewGuid():N}",
            ConnectorType = ConnectorType.AzureDevOps,
            AuthType = SecretAuthType.Pat,
        };

        var response = await _client.PostAsJsonAsync("/api/admin/connectors", request);
        response.EnsureSuccessStatusCode();

        var body = await Deserialize<ApiResponse<ConnectorResponse>>(response);
        return body!.Data!;
    }

    private static async Task<T?> Deserialize<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
