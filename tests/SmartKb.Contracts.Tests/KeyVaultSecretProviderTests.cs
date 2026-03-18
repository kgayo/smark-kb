using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Contracts.Services;

namespace SmartKb.Contracts.Tests;

public class KeyVaultSecretProviderTests
{
    [Fact]
    public async Task GetSecretAsync_ReturnsSecretValue()
    {
        var mockClient = new MockSecretClient();
        mockClient.Secrets["my-secret"] = "super-secret-value";

        var provider = new KeyVaultSecretProvider(mockClient, NullLogger<KeyVaultSecretProvider>.Instance);

        var result = await provider.GetSecretAsync("my-secret");

        Assert.Equal("super-secret-value", result);
    }

    [Fact]
    public async Task GetSecretAsync_PropagatesException_WhenSecretNotFound()
    {
        var mockClient = new MockSecretClient();
        // No secrets stored — will throw.

        var provider = new KeyVaultSecretProvider(mockClient, NullLogger<KeyVaultSecretProvider>.Instance);

        await Assert.ThrowsAsync<RequestFailedException>(
            () => provider.GetSecretAsync("nonexistent-secret"));
    }

    [Fact]
    public async Task SetSecretAsync_StoresSecret()
    {
        var mockClient = new MockSecretClient();

        var provider = new KeyVaultSecretProvider(mockClient, NullLogger<KeyVaultSecretProvider>.Instance);

        await provider.SetSecretAsync("new-secret", "new-value");

        Assert.Contains("new-secret", mockClient.Secrets.Keys);
        Assert.Equal("new-value", mockClient.Secrets["new-secret"]);
    }

    [Fact]
    public async Task DeleteSecretAsync_RemovesSecret()
    {
        var mockClient = new MockSecretClient();
        mockClient.Secrets["to-delete"] = "value";

        var provider = new KeyVaultSecretProvider(mockClient, NullLogger<KeyVaultSecretProvider>.Instance);

        await provider.DeleteSecretAsync("to-delete");

        Assert.Contains("to-delete", mockClient.DeletedSecrets);
    }

    [Fact]
    public async Task GetSecretAsync_DifferentSecrets_ReturnCorrectValues()
    {
        var mockClient = new MockSecretClient();
        mockClient.Secrets["secret-a"] = "value-a";
        mockClient.Secrets["secret-b"] = "value-b";

        var provider = new KeyVaultSecretProvider(mockClient, NullLogger<KeyVaultSecretProvider>.Instance);

        Assert.Equal("value-a", await provider.GetSecretAsync("secret-a"));
        Assert.Equal("value-b", await provider.GetSecretAsync("secret-b"));
    }
}

/// <summary>
/// Mock SecretClient for unit testing KeyVaultSecretProvider.
/// Extends SecretClient (which has virtual methods) to avoid needing the real Azure SDK.
/// </summary>
internal class MockSecretClient : SecretClient
{
    public Dictionary<string, string> Secrets { get; } = new();
    public List<string> DeletedSecrets { get; } = new();

    public MockSecretClient() : base()
    {
    }

    public override Task<Response<KeyVaultSecret>> GetSecretAsync(
        string name, string? version = null, CancellationToken cancellationToken = default)
    {
        if (!Secrets.TryGetValue(name, out var value))
        {
            throw new RequestFailedException(404, $"Secret '{name}' not found.");
        }

        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties(name), value);

        return Task.FromResult(Response.FromValue(secret, new MockResponse()));
    }

    public override Task<Response<KeyVaultSecret>> SetSecretAsync(
        string name, string value, CancellationToken cancellationToken = default)
    {
        Secrets[name] = value;

        var secret = SecretModelFactory.KeyVaultSecret(
            new SecretProperties(name), value);

        return Task.FromResult(Response.FromValue(secret, new MockResponse()));
    }

    public override Task<DeleteSecretOperation> StartDeleteSecretAsync(
        string name, CancellationToken cancellationToken = default)
    {
        DeletedSecrets.Add(name);
        Secrets.Remove(name);
        return Task.FromResult<DeleteSecretOperation>(null!);
    }
}

/// <summary>
/// Minimal mock Response for Azure SDK.
/// </summary>
internal class MockResponse : Response
{
    public override int Status => 200;
    public override string ReasonPhrase => "OK";
    public override Stream? ContentStream { get; set; }
    public override string ClientRequestId { get; set; } = Guid.NewGuid().ToString();

    public override void Dispose() { }

    protected override bool ContainsHeader(string name) => false;
    protected override IEnumerable<Azure.Core.HttpHeader> EnumerateHeaders() => [];
    protected override bool TryGetHeader(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value) { value = null; return false; }
    protected override bool TryGetHeaderValues(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IEnumerable<string>? values) { values = null; return false; }
}
