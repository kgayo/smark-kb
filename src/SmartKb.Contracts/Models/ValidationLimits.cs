namespace SmartKb.Contracts.Models;

/// <summary>
/// Shared validation length limits for entity fields across the application.
/// </summary>
public static class ValidationLimits
{
    public const int ConnectorNameMaxLength = 256;
    public const int StopWordMaxLength = 128;
    public const int SpecialTokenMaxLength = 256;
    public const int SynonymRuleMaxLength = 1024;
    public const int BoostFactorMin = 1;
    public const int BoostFactorMax = 10;
}
