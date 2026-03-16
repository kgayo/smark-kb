namespace SmartKb.Contracts.Enums;

/// <summary>
/// Access visibility level for evidence records.
/// Used for ACL-based security trimming in retrieval.
/// </summary>
public enum AccessVisibility
{
    Internal,
    Restricted,
    Public
}
