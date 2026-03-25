using SmartKb.Contracts.Enums;

namespace SmartKb.Contracts;

public static class RolePermissions
{
    public static readonly IReadOnlyDictionary<AppRole, IReadOnlySet<string>> Matrix =
        new Dictionary<AppRole, IReadOnlySet<string>>
        {
            [AppRole.SupportAgent] = new HashSet<string>
            {
                Permissions.ChatQuery,
                Permissions.ChatFeedback,
                Permissions.ChatOutcome,
                Permissions.SessionReadOwn,
            },
            [AppRole.SupportLead] = new HashSet<string>
            {
                Permissions.ChatQuery,
                Permissions.ChatFeedback,
                Permissions.ChatOutcome,
                Permissions.SessionReadOwn,
                Permissions.SessionReadTeam,
                Permissions.PatternApprove,
                Permissions.PatternDeprecate,
                Permissions.ReportRead,
            },
            [AppRole.Admin] = new HashSet<string>
            {
                Permissions.ChatQuery,
                Permissions.ChatFeedback,
                Permissions.ChatOutcome,
                Permissions.SessionReadOwn,
                Permissions.SessionReadTeam,
                Permissions.ConnectorManage,
                Permissions.ConnectorSync,
                Permissions.PatternApprove,
                Permissions.PatternDeprecate,
                Permissions.ReportRead,
                Permissions.AuditRead,
                Permissions.AuditExport,
                Permissions.TenantManage,
                Permissions.PrivacyManage,
            },
            [AppRole.EngineeringViewer] = new HashSet<string>
            {
                Permissions.ReportRead,
                Permissions.PatternRead,
            },
            [AppRole.SecurityAuditor] = new HashSet<string>
            {
                Permissions.AuditRead,
                Permissions.AuditExport,
                Permissions.ReportRead,
            },
        };

    public static bool HasPermission(AppRole role, string permission) =>
        Matrix.TryGetValue(role, out var perms) && perms.Contains(permission);
}
