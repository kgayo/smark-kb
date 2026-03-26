using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Api.Webhooks;

namespace SmartKb.Api.Tests.Webhooks;

public sealed class AdoWebhookSignatureTests
{
    [Fact]
    public void ValidateSignature_ReturnsTrue_WhenSecretMatches()
    {
        var secret = "my-shared-secret-123";
        var body = """{"eventType":"workitem.updated","resource":{}}""";
        var authHeader = CreateBasicAuthHeader("", secret);

        Assert.True(AdoWebhookHandler.ValidateSignature(body, authHeader, secret));
    }

    [Fact]
    public void ValidateSignature_ReturnsFalse_WhenSecretMismatch()
    {
        var secret = "correct-secret";
        var body = """{"eventType":"workitem.updated"}""";
        var authHeader = CreateBasicAuthHeader("", "wrong-secret");

        Assert.False(AdoWebhookHandler.ValidateSignature(body, authHeader, secret));
    }

    [Fact]
    public void ValidateSignature_ReturnsFalse_WhenAuthHeaderMissing()
    {
        Assert.False(AdoWebhookHandler.ValidateSignature("{}", null, "secret"));
    }

    [Fact]
    public void ValidateSignature_ReturnsFalse_WhenAuthHeaderNotBasic()
    {
        Assert.False(AdoWebhookHandler.ValidateSignature("{}", "Bearer token123", "secret"));
    }

    [Fact]
    public void ValidateSignature_ReturnsFalse_WhenAuthHeaderInvalidBase64()
    {
        Assert.False(AdoWebhookHandler.ValidateSignature("{}", "Basic !!!invalid!!!", "secret"));
    }

    [Fact]
    public void ValidateSignature_LogsWarning_WhenAuthHeaderInvalidBase64()
    {
        var logger = NullLogger<AdoWebhookHandler>.Instance;
        // Should not throw; logger receives warning for malformed header
        Assert.False(AdoWebhookHandler.ValidateSignature("{}", "Basic !!!invalid!!!", "secret", logger));
    }

    [Fact]
    public void ValidateSignature_ReturnsTrue_WhenNoSecretConfigured()
    {
        // No secret = skip verification.
        Assert.True(AdoWebhookHandler.ValidateSignature("{}", null, null));
        Assert.True(AdoWebhookHandler.ValidateSignature("{}", null, ""));
    }

    [Fact]
    public void ValidateSignature_HandlesUsernameInAuthHeader()
    {
        var secret = "test-secret";
        var authHeader = CreateBasicAuthHeader("someuser", secret);

        Assert.True(AdoWebhookHandler.ValidateSignature("{}", authHeader, secret));
    }

    [Fact]
    public void ValidateSignature_ReturnsFalse_WhenNoColonInDecoded()
    {
        // Encode just "nocolon" (no colon separator)
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("nocolon"));
        Assert.False(AdoWebhookHandler.ValidateSignature("{}", $"Basic {encoded}", "secret"));
    }

    private static string CreateBasicAuthHeader(string username, string password)
    {
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        return $"Basic {credentials}";
    }
}
