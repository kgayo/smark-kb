namespace SmartKb.Contracts.Enums;

/// <summary>
/// Trust level for case patterns in the Pattern Store governance model.
/// Controls visibility and authority weighting in retrieval fusion.
/// </summary>
public enum TrustLevel
{
    Draft,
    Reviewed,
    Approved,
    Deprecated
}
