namespace SmartKb.Contracts.Models;

/// <summary>
/// Shared pagination constants used across list/query endpoints.
/// </summary>
public static class PaginationDefaults
{
    public const int MinPageSize = 1;
    public const int MaxPageSize = 100;
    public const int AuditMaxPageSize = 200;

    /// <summary>
    /// Clamps the given page size to the standard [MinPageSize, MaxPageSize] range.
    /// </summary>
    public static int ClampPageSize(int pageSize) =>
        Math.Clamp(pageSize, MinPageSize, MaxPageSize);

    /// <summary>
    /// Clamps the given page size to the audit [MinPageSize, AuditMaxPageSize] range.
    /// </summary>
    public static int ClampAuditPageSize(int pageSize) =>
        Math.Clamp(pageSize, MinPageSize, AuditMaxPageSize);
}
