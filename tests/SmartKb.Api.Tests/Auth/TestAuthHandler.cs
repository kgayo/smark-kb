using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SmartKb.Api.Tests.Auth;

public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestScheme";
    public const string AuthenticatedHeader = "X-Test-Auth";
    public const string RolesHeader = "X-Test-Roles";
    public const string TenantHeader = "X-Test-Tenant";
    public const string UserIdHeader = "X-Test-UserId";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey(AuthenticatedHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userId = Request.Headers[UserIdHeader].FirstOrDefault() ?? "test-user";
        var tenantId = Request.Headers[TenantHeader].FirstOrDefault();

        var claims = new List<Claim>
        {
            new("sub", userId),
            new("oid", userId),
            new("name", "Test User"),
        };

        if (!string.IsNullOrEmpty(tenantId))
        {
            claims.Add(new Claim("tid", tenantId));
        }

        var rolesHeader = Request.Headers[RolesHeader].FirstOrDefault();
        if (!string.IsNullOrEmpty(rolesHeader))
        {
            foreach (var role in rolesHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                claims.Add(new Claim("roles", role));
            }
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
