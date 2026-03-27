namespace SmartKb.Contracts.Models;

/// <summary>
/// Shared pagination constants used across list/query endpoints.
/// </summary>
public static class PaginationDefaults
{
    public const int MinPageSize = 1;
    public const int MaxPageSize = 100;
    public const int AuditMaxPageSize = 200;
    public const int DefaultPageSize = 20;
    public const int AuditDefaultPageSize = 50;
    public const int ExportDefaultLimit = 1000;
    public const int DefaultDaysPeriod = 30;
    public const int DefaultMaxMessages = 20;
    public const int DefaultPage = 1;
    public const int DefaultSkip = 0;

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
