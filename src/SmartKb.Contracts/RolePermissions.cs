using SmartKb.Contracts.Enums;

namespace SmartKb.Contracts;

public static class RolePermissions
{
    public static readonly IReadOnlyDictionary<AppRole, IReadOnlySet<string>> Matrix =
        new Dictionary<AppRole, IReadOnlySet<string>>
        {
            [AppRole.SupportAgent] = new HashSet<string>
            {
                "chat:query",
                "chat:feedback",
                "chat:outcome",
                "session:read_own",
            },
            [AppRole.SupportLead] = new HashSet<string>
            {
                "chat:query",
                "chat:feedback",
                "chat:outcome",
                "session:read_own",
                "session:read_team",
                "pattern:approve",
                "pattern:deprecate",
                "report:read",
            },
            [AppRole.Admin] = new HashSet<string>
            {
                "chat:query",
                "chat:feedback",
                "chat:outcome",
                "session:read_own",
                "session:read_team",
                "connector:manage",
                "connector:sync",
                "pattern:approve",
                "pattern:deprecate",
                "report:read",
                "audit:read",
                "audit:export",
                "tenant:manage",
            },
            [AppRole.EngineeringViewer] = new HashSet<string>
            {
                "report:read",
                "pattern:read",
            },
            [AppRole.SecurityAuditor] = new HashSet<string>
            {
                "audit:read",
                "audit:export",
                "report:read",
            },
        };

    public static bool HasPermission(AppRole role, string permission) =>
        Matrix.TryGetValue(role, out var perms) && perms.Contains(permission);
}
