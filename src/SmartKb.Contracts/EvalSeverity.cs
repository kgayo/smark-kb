namespace SmartKb.Contracts;

/// <summary>
/// Shared constants for eval regression severity levels used in baseline comparison.
/// </summary>
public static class EvalSeverity
{
    public const string Ok = "ok";
    public const string Warning = "warning";
    public const string Blocking = "blocking";
}

/// <summary>
/// Shared constants for threshold comparison direction strings (e.g. ">=" or "<=").
/// </summary>
public static class ThresholdDirection
{
    public const string GreaterThanOrEqual = ">=";
    public const string LessThanOrEqual = "<=";
}

/// <summary>
/// Shared constants for case-card quality issue severity levels.
/// </summary>
public static class QualitySeverity
{
    public const string Warning = "warning";
    public const string Error = "error";
}
