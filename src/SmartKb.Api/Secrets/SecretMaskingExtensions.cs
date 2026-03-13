namespace SmartKb.Api.Secrets;

public static class SecretMaskingExtensions
{
    private const string MaskedValue = "***REDACTED***";
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "apikey", "api_key", "secret", "password", "connectionstring",
        "connection_string", "token", "credential", "client_secret",
        "clientsecret", "access_token", "refresh_token", "private_key"
    };

    public static string MaskSecretValue(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= 4)
        {
            return MaskedValue;
        }

        return string.Concat(value.AsSpan(0, 2), "***", value.AsSpan(value.Length - 2));
    }

    public static bool IsSensitiveKey(string key)
    {
        return SensitiveKeys.Contains(key);
    }
}
