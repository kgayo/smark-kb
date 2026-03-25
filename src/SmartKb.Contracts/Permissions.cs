namespace SmartKb.Contracts;

/// <summary>
/// Centralized permission string constants used in RBAC enforcement and endpoint authorization.
/// </summary>
public static class Permissions
{
    public const string ChatQuery = "chat:query";
    public const string ChatFeedback = "chat:feedback";
    public const string ChatOutcome = "chat:outcome";
    public const string SessionReadOwn = "session:read_own";
    public const string SessionReadTeam = "session:read_team";
    public const string ConnectorManage = "connector:manage";
    public const string ConnectorSync = "connector:sync";
    public const string PatternApprove = "pattern:approve";
    public const string PatternDeprecate = "pattern:deprecate";
    public const string PatternRead = "pattern:read";
    public const string ReportRead = "report:read";
    public const string AuditRead = "audit:read";
    public const string AuditExport = "audit:export";
    public const string TenantManage = "tenant:manage";
    public const string PrivacyManage = "privacy:manage";
}
