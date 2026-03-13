using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SmartKb.Contracts;
using SmartKb.Contracts.Enums;

namespace SmartKb.Api.Auth;

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    public const string RoleClaimType = "roles";
    public const string EntraRoleClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var roles = GetAppRoles(context.User);

        foreach (var role in roles)
        {
            if (RolePermissions.HasPermission(role, requirement.Permission))
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }
        }

        return Task.CompletedTask;
    }

    public static IEnumerable<AppRole> GetAppRoles(ClaimsPrincipal user)
    {
        var roleClaims = user.FindAll(RoleClaimType)
            .Concat(user.FindAll(EntraRoleClaimType))
            .Concat(user.FindAll(ClaimTypes.Role));

        foreach (var claim in roleClaims)
        {
            if (Enum.TryParse<AppRole>(claim.Value, ignoreCase: true, out var role))
            {
                yield return role;
            }
        }
    }
}
