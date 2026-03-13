using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SmartKb.Api.Auth;
using SmartKb.Contracts.Enums;

namespace SmartKb.Api.Tests.Auth;

public class PermissionAuthorizationHandlerTests
{
    private readonly PermissionAuthorizationHandler _handler = new();

    [Fact]
    public async Task Succeeds_WhenUserHasMatchingRolePermission()
    {
        var user = CreateUser(("roles", "Admin"));
        var requirement = new PermissionRequirement("connector:manage");
        var context = CreateContext(user, requirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Fails_WhenUserLacksPermission()
    {
        var user = CreateUser(("roles", "SupportAgent"));
        var requirement = new PermissionRequirement("connector:manage");
        var context = CreateContext(user, requirement);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Fails_WhenNoRoleClaims()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "u1") }, "test"));
        var requirement = new PermissionRequirement("chat:query");
        var context = CreateContext(user, requirement);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Succeeds_WithEntraRoleClaimType()
    {
        var user = CreateUser(("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "SecurityAuditor"));
        var requirement = new PermissionRequirement("audit:read");
        var context = CreateContext(user, requirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task IgnoresInvalidRoleClaims()
    {
        var user = CreateUser(("roles", "NonExistentRole"));
        var requirement = new PermissionRequirement("chat:query");
        var context = CreateContext(user, requirement);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task MultipleRoles_SucceedsIfAnyHasPermission()
    {
        var claims = new[]
        {
            new Claim("roles", "SupportAgent"),
            new Claim("roles", "Admin"),
        };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var requirement = new PermissionRequirement("connector:manage");
        var context = CreateContext(user, requirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Theory]
    [InlineData("SupportAgent")]
    [InlineData("SupportLead")]
    [InlineData("Admin")]
    [InlineData("EngineeringViewer")]
    [InlineData("SecurityAuditor")]
    public void GetAppRoles_ParsesAllValidRoles(string roleName)
    {
        var user = CreateUser(("roles", roleName));
        var roles = PermissionAuthorizationHandler.GetAppRoles(user).ToList();

        Assert.Single(roles);
        Assert.Equal(Enum.Parse<AppRole>(roleName), roles[0]);
    }

    [Fact]
    public void GetAppRoles_IsCaseInsensitive()
    {
        var user = CreateUser(("roles", "supportagent"));
        var roles = PermissionAuthorizationHandler.GetAppRoles(user).ToList();

        Assert.Single(roles);
        Assert.Equal(AppRole.SupportAgent, roles[0]);
    }

    private static ClaimsPrincipal CreateUser(params (string type, string value)[] claims)
    {
        var claimList = claims.Select(c => new Claim(c.type, c.value)).ToArray();
        return new ClaimsPrincipal(new ClaimsIdentity(claimList, "test"));
    }

    private static AuthorizationHandlerContext CreateContext(ClaimsPrincipal user, IAuthorizationRequirement requirement)
    {
        return new AuthorizationHandlerContext(new[] { requirement }, user, null);
    }
}
