namespace SmartKb.Contracts.Configuration;

public sealed class KeyVaultSettings
{
    public const string SectionName = "KeyVault";

    public string VaultUri { get; set; } = string.Empty;
}
