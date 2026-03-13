using Microsoft.AspNetCore.Authorization;
using SmartKb.Contracts;
using SmartKb.Contracts.Enums;

namespace SmartKb.Api.Auth;

public static class AuthorizationExtensions
{
    public static AuthorizationBuilder AddPermissionPolicies(this AuthorizationBuilder builder)
    {
        var allPermissions = RolePermissions.Matrix.Values
            .SelectMany(p => p)
            .Distinct();

        foreach (var permission in allPermissions)
        {
            builder.AddPolicy(permission, policy =>
                policy.Requirements.Add(new PermissionRequirement(permission)));
        }

        return builder;
    }

    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.RequireAuthorization(permission);
    }
}
