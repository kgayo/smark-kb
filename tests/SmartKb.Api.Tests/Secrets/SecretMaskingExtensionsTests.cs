using SmartKb.Api.Secrets;

namespace SmartKb.Api.Tests.Secrets;

public class SecretMaskingExtensionsTests
{
    [Theory]
    [InlineData("sk-1234567890abcdef", "sk***ef")]
    [InlineData("abcde", "ab***de")]
    [InlineData("12345", "12***45")]
    public void MaskSecretValue_LongValues_ShowsFirstAndLastTwo(string input, string expected)
    {
        Assert.Equal(expected, SecretMaskingExtensions.MaskSecretValue(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("abcd")]
    public void MaskSecretValue_ShortValues_ReturnsFullyRedacted(string input)
    {
        Assert.Equal("***REDACTED***", SecretMaskingExtensions.MaskSecretValue(input));
    }

    [Fact]
    public void MaskSecretValue_NullInput_ReturnsFullyRedacted()
    {
        Assert.Equal("***REDACTED***", SecretMaskingExtensions.MaskSecretValue(null!));
    }

    [Theory]
    [InlineData("apikey", true)]
    [InlineData("ApiKey", true)]
    [InlineData("APIKEY", true)]
    [InlineData("api_key", true)]
    [InlineData("secret", true)]
    [InlineData("password", true)]
    [InlineData("connectionstring", true)]
    [InlineData("token", true)]
    [InlineData("credential", true)]
    [InlineData("client_secret", true)]
    [InlineData("access_token", true)]
    [InlineData("refresh_token", true)]
    [InlineData("private_key", true)]
    [InlineData("username", false)]
    [InlineData("email", false)]
    [InlineData("model", false)]
    [InlineData("endpoint", false)]
    public void IsSensitiveKey_CorrectlyIdentifiesSensitiveKeys(string key, bool expected)
    {
        Assert.Equal(expected, SecretMaskingExtensions.IsSensitiveKey(key));
    }
}
