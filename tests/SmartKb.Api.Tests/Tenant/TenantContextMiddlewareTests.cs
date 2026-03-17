using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Api.Audit;
using SmartKb.Api.Tenant;
using SmartKb.Contracts;
using SmartKb.Contracts.Services;

namespace SmartKb.Api.Tests.Tenant;

public class TenantContextMiddlewareTests
{
    private readonly InMemoryAuditEventWriter _auditWriter =
        new(NullLogger<InMemoryAuditEventWriter>.Instance);

    private readonly TenantContextAccessor _accessor = new();

    private TenantContextMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, NullLogger<TenantContextMiddleware>.Instance);

    [Fact]
    public async Task PassesThrough_WhenNotAuthenticated()
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var ctx = new DefaultHttpContext();

        await middleware.InvokeAsync(ctx, _accessor, _auditWriter);

        Assert.True(called);
        Assert.Null(_accessor.Current);
    }

    [Fact]
    public async Task SetsTenantContext_WhenAuthenticated()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var ctx = CreateAuthenticatedContext("tenant-1", "user-1");

        await middleware.InvokeAsync(ctx, _accessor, _auditWriter);

        Assert.NotNull(_accessor.Current);
        Assert.Equal("tenant-1", _accessor.Current!.TenantId);
        Assert.Equal("user-1", _accessor.Current.UserId);
        Assert.NotEmpty(_accessor.Current.CorrelationId);
    }

    [Fact]
    public async Task Returns403_WhenNoTenantClaim()
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var ctx = CreateAuthenticatedContext(tenantId: null, userId: "user-1");

        await middleware.InvokeAsync(ctx, _accessor, _auditWriter);

        Assert.False(called);
        Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
        Assert.Null(_accessor.Current);
    }

    [Fact]
    public async Task WritesAuditEvent_WhenNoTenantClaim()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var ctx = CreateAuthenticatedContext(tenantId: null, userId: "user-1");

        await middleware.InvokeAsync(ctx, _accessor, _auditWriter);

        var events = _auditWriter.GetEvents();
        Assert.Single(events);
        Assert.Equal(AuditEventTypes.TenantMissing, events[0].EventType);
        Assert.Equal("user-1", events[0].ActorId);
        Assert.Contains("no tenant claim", events[0].Detail);
    }

    [Fact]
    public async Task Returns403_WhenEmptyTenantClaim()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var ctx = CreateAuthenticatedContext(tenantId: "", userId: "user-1");

        await middleware.InvokeAsync(ctx, _accessor, _auditWriter);

        Assert.Equal(StatusCodes.Status403Forbidden, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task UsesSubClaim_WhenOidMissing()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var claims = new[]
        {
            new Claim("sub", "sub-user"),
            new Claim("tid", "tenant-1"),
        };
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
        };

        await middleware.InvokeAsync(ctx, _accessor, _auditWriter);

        Assert.NotNull(_accessor.Current);
        Assert.Equal("sub-user", _accessor.Current!.UserId);
    }

    [Fact]
    public async Task ExtractsUserGroups_FromGroupsAndRolesClaims()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var claims = new[]
        {
            new Claim("tid", "tenant-1"),
            new Claim("oid", "user-1"),
            new Claim("groups", "TeamAlpha"),
            new Claim("groups", "TeamBeta"),
            new Claim("roles", "SupportAgent"),
        };
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
        };

        await middleware.InvokeAsync(ctx, _accessor, _auditWriter);

        Assert.NotNull(_accessor.Current);
        Assert.Equal(3, _accessor.Current!.UserGroups.Count);
        Assert.Contains("TeamAlpha", _accessor.Current.UserGroups);
        Assert.Contains("TeamBeta", _accessor.Current.UserGroups);
        Assert.Contains("SupportAgent", _accessor.Current.UserGroups);
    }

    [Fact]
    public async Task UserGroups_Empty_WhenNoGroupOrRoleClaims()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var ctx = CreateAuthenticatedContext("tenant-1", "user-1");

        await middleware.InvokeAsync(ctx, _accessor, _auditWriter);

        Assert.NotNull(_accessor.Current);
        Assert.Empty(_accessor.Current!.UserGroups);
    }

    [Fact]
    public async Task UserGroups_DeduplicatesCaseInsensitive()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var claims = new[]
        {
            new Claim("tid", "tenant-1"),
            new Claim("oid", "user-1"),
            new Claim("groups", "TeamAlpha"),
            new Claim("roles", "teamalpha"),
        };
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
        };

        await middleware.InvokeAsync(ctx, _accessor, _auditWriter);

        Assert.NotNull(_accessor.Current);
        Assert.Single(_accessor.Current!.UserGroups);
    }

    [Fact]
    public async Task UsesInboundCorrelationIdHeader_WhenPresent()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var ctx = CreateAuthenticatedContext("tenant-1", "user-1");
        ctx.Request.Headers["X-Correlation-Id"] = "custom-corr-123";

        await middleware.InvokeAsync(ctx, _accessor, _auditWriter);

        Assert.NotNull(_accessor.Current);
        Assert.Equal("custom-corr-123", _accessor.Current!.CorrelationId);
    }

    [Fact]
    public async Task FallsBackToActivityId_WhenNoCorrelationHeader()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var ctx = CreateAuthenticatedContext("tenant-1", "user-1");
        // No X-Correlation-Id header set.

        await middleware.InvokeAsync(ctx, _accessor, _auditWriter);

        Assert.NotNull(_accessor.Current);
        Assert.NotEmpty(_accessor.Current!.CorrelationId);
        Assert.NotEqual("custom-corr-123", _accessor.Current.CorrelationId);
    }

    [Fact]
    public async Task EchoesCorrelationId_InResponseHeader()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var ctx = CreateAuthenticatedContext("tenant-1", "user-1");
        ctx.Request.Headers["X-Correlation-Id"] = "echo-test-456";

        // Use a custom response feature to capture OnStarting callbacks.
        var responseFeature = new TestHttpResponseFeature();
        ctx.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(responseFeature);

        await middleware.InvokeAsync(ctx, _accessor, _auditWriter);

        // Fire OnStarting callbacks.
        await responseFeature.FireOnStarting();

        Assert.True(ctx.Response.Headers.ContainsKey("X-Correlation-Id"));
        Assert.Equal("echo-test-456", ctx.Response.Headers["X-Correlation-Id"].ToString());
    }

    private static DefaultHttpContext CreateAuthenticatedContext(string? tenantId, string? userId)
    {
        var claims = new List<Claim>();
        if (userId is not null)
        {
            claims.Add(new Claim("oid", userId));
            claims.Add(new Claim("sub", userId));
        }
        if (tenantId is not null)
        {
            claims.Add(new Claim("tid", tenantId));
        }
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
        };
    }

    /// <summary>
    /// Minimal IHttpResponseFeature that allows capturing and firing OnStarting callbacks.
    /// </summary>
    private sealed class TestHttpResponseFeature : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];

        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => false;

        public void OnStarting(Func<object, Task> callback, object state) =>
            _onStarting.Add((callback, state));

        public void OnCompleted(Func<object, Task> callback, object state) { }

        public async Task FireOnStarting()
        {
            foreach (var (callback, state) in _onStarting)
                await callback(state);
        }
    }
}
