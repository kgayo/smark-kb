using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartKb.Api.Connectors;
using SmartKb.Api.Tests.Auth;
using SmartKb.Api.Tests.Connectors;
using SmartKb.Api.Webhooks;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Models;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Api.Tests.Audit;

public class AuditEndpointTests : IAsyncLifetime
{
    private readonly AuditTestFactory _factory = new();
    private HttpClient _adminClient = null!;
    private HttpClient _auditorClient = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _adminClient = _factory.CreateAuthenticatedClient(roles: "Admin");
        _auditorClient = _factory.CreateAuthenticatedClient(roles: "SecurityAuditor");
        await _factory.EnsureDatabaseAsync();
        await _factory.SeedAuditEventsAsync();
    }

    public async Task DisposeAsync()
    {
        _adminClient.Dispose();
        _auditorClient.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task QueryAuditEvents_Admin_ReturnsSuccess()
    {
        var response = await _adminClient.GetAsync("/api/audit/events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AuditEventListResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.True(body.Data!.TotalCount > 0);
    }

    [Fact]
    public async Task QueryAuditEvents_SecurityAuditor_ReturnsSuccess()
    {
        var response = await _auditorClient.GetAsync("/api/audit/events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AuditEventListResponse>>();
        Assert.True(body!.IsSuccess);
    }

    [Fact]
    public async Task QueryAuditEvents_SupportAgent_ReturnsForbidden()
    {
        var agentClient = _factory.CreateAuthenticatedClient(roles: "SupportAgent");
        var response = await agentClient.GetAsync("/api/audit/events");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        agentClient.Dispose();
    }

    [Fact]
    public async Task QueryAuditEvents_FilterByEventType_ReturnsFilteredResults()
    {
        var response = await _adminClient.GetAsync("/api/audit/events?eventType=connector.created");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AuditEventListResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.All(body.Data!.Events, e => Assert.Equal("connector.created", e.EventType));
    }

    [Fact]
    public async Task QueryAuditEvents_FilterByActorId_ReturnsFilteredResults()
    {
        var response = await _adminClient.GetAsync("/api/audit/events?actorId=actor-a");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AuditEventListResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.All(body.Data!.Events, e => Assert.Equal("actor-a", e.ActorId));
    }

    [Fact]
    public async Task QueryAuditEvents_Pagination_ReturnsCorrectPage()
    {
        var response = await _adminClient.GetAsync("/api/audit/events?page=1&pageSize=3");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AuditEventListResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.Equal(3, body.Data!.Events.Count);
        Assert.Equal(1, body.Data.Page);
        Assert.Equal(3, body.Data.PageSize);
        Assert.True(body.Data.HasMore);
    }

    [Fact]
    public async Task QueryAuditEvents_TenantIsolation_DoesNotLeakCrossTenant()
    {
        var response = await _adminClient.GetAsync("/api/audit/events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<AuditEventListResponse>>();
        Assert.True(body!.IsSuccess);
        Assert.All(body.Data!.Events, e => Assert.Equal("tenant-1", e.TenantId));
    }

    [Fact]
    public async Task ExportAuditEvents_Admin_ReturnsNdjson()
    {
        var response = await _adminClient.GetAsync("/api/audit/events/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 1); // At least 1 event + cursor line
    }

    [Fact]
    public async Task ExportAuditEvents_SupportAgent_ReturnsForbidden()
    {
        var agentClient = _factory.CreateAuthenticatedClient(roles: "SupportAgent");
        var response = await agentClient.GetAsync("/api/audit/events/export");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        agentClient.Dispose();
    }

    [Fact]
    public async Task ExportAuditEvents_ContainsCursorMetadata()
    {
        var response = await _adminClient.GetAsync("/api/audit/events/export?limit=3");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Last line should be cursor metadata
        var lastLine = lines.Last();
        Assert.Contains("__cursor", lastLine);
        Assert.Contains("afterTimestamp", lastLine);
        Assert.Contains("afterId", lastLine);
    }

    [Fact]
    public async Task ExportAuditEvents_FilterByEventType_ReturnsFilteredResults()
    {
        var response = await _adminClient.GetAsync("/api/audit/events/export?eventType=chat.feedback");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // All non-cursor lines should be chat.feedback events
        foreach (var line in lines)
        {
            if (line.Contains("__cursor")) continue;
            Assert.Contains("chat.feedback", line);
        }
    }

    [Fact]
    public async Task ExportAuditEvents_TenantIsolation_DoesNotLeakCrossTenant()
    {
        var response = await _adminClient.GetAsync("/api/audit/events/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("tenant-other", content);
    }
}

internal sealed class AuditTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private SqliteConnection _connection = null!;

    public Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "<test>",
                ["AzureAd:ClientId"] = "<test>",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, _ => { });

            services.AddDbContext<SmartKbDbContext>(options =>
                options.UseSqlite(_connection));
            services.AddScoped<IAuditEventWriter, SqlAuditEventWriter>();
            services.AddScoped<IAuditEventQueryService, AuditEventQueryService>();
            services.AddScoped<IAnswerTraceWriter, SqlAnswerTraceWriter>();
            services.AddScoped<ISessionService, SessionService>();
            services.AddSingleton<ISyncJobPublisher, TestSyncJobPublisher>();
            services.AddSingleton(new WebhookSettings());
            services.AddSingleton(new SessionSettings());
            services.AddSingleton(new EscalationSettings());
            services.AddSingleton<ISecretProvider>(new InMemorySecretProvider());
            services.AddScoped<ITeamPlaybookService, TeamPlaybookService>();
            services.AddScoped<IEscalationDraftService, EscalationDraftService>();
            services.AddScoped<IFeedbackService, FeedbackService>();
            services.AddScoped<IOutcomeService, OutcomeService>();
            services.AddSingleton(new DistillationSettings());
            services.AddScoped<IPatternDistillationService, PatternDistillationService>();
            services.AddScoped<IPatternGovernanceService, PatternGovernanceService>();
            services.AddScoped<ConnectorAdminService>();
            services.AddScoped<AdoWebhookHandler>();
            services.AddScoped<SharePointWebhookHandler>();
            services.AddSingleton(new RetrievalSettings());
            services.AddScoped<ITenantRetrievalSettingsService, TenantRetrievalSettingsService>();
            services.AddScoped<IWebhookStatusService, SmartKb.Data.Repositories.WebhookStatusService>();
            services.AddSingleton(new SmartKb.Contracts.Configuration.RoutingAnalyticsSettings());
            services.AddScoped<IRoutingRuleService, SmartKb.Data.Repositories.RoutingRuleService>();
            services.AddScoped<IRoutingAnalyticsService, SmartKb.Data.Repositories.RoutingAnalyticsService>();
            services.AddScoped<IRoutingImprovementService, SmartKb.Data.Repositories.RoutingImprovementService>();
            services.AddScoped<IPiiPolicyService, SmartKb.Data.Repositories.PiiPolicyService>();
            services.AddScoped<IRetentionCleanupService, SmartKb.Data.Repositories.RetentionCleanupService>();
            services.AddScoped<IDataSubjectDeletionService, SmartKb.Data.Repositories.DataSubjectDeletionService>();
            services.AddScoped<ITenantCostSettingsService, SmartKb.Data.Repositories.TenantCostSettingsService>();
            services.AddScoped<ITokenUsageService, SmartKb.Data.Repositories.TokenUsageService>();
            services.AddSingleton(new SmartKb.Contracts.Configuration.PatternMaintenanceSettings());
            services.AddScoped<IContradictionDetectionService, SmartKb.Data.Repositories.ContradictionDetectionService>();
            services.AddScoped<IPatternMaintenanceService, SmartKb.Data.Repositories.PatternMaintenanceService>();
            services.AddScoped<ISynonymMapService, SmartKb.Data.Repositories.SynonymMapService>();
        });
    }

    public HttpClient CreateAuthenticatedClient(
        string tenantId = "tenant-1",
        string roles = "Admin",
        string userId = "test-user")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthenticatedHeader, "true");
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenantId);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, roles);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId);
        return client;
    }

    public async Task EnsureDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();
        await db.Database.EnsureCreatedAsync();

        if (!await db.Tenants.AnyAsync(t => t.TenantId == "tenant-1"))
        {
            db.Tenants.AddRange(
                new TenantEntity { TenantId = "tenant-1", DisplayName = "Test Tenant 1", CreatedAt = DateTimeOffset.UtcNow },
                new TenantEntity { TenantId = "tenant-other", DisplayName = "Test Tenant Other", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
    }

    public async Task SeedAuditEventsAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();
        var now = DateTimeOffset.UtcNow;

        for (int i = 0; i < 8; i++)
        {
            db.AuditEvents.Add(new AuditEventEntity
            {
                Id = Guid.NewGuid(),
                EventType = i % 2 == 0 ? "connector.created" : "chat.feedback",
                TenantId = "tenant-1",
                ActorId = i < 4 ? "actor-a" : "actor-b",
                CorrelationId = $"corr-{i}",
                Timestamp = now.AddMinutes(-i),
                Detail = $"Detail {i}",
            });
        }

        // Cross-tenant event (should never appear)
        db.AuditEvents.Add(new AuditEventEntity
        {
            Id = Guid.NewGuid(),
            EventType = "connector.created",
            TenantId = "tenant-other",
            ActorId = "other-actor",
            CorrelationId = "other-corr",
            Timestamp = now,
            Detail = "Other tenant detail",
        });

        await db.SaveChangesAsync();
    }
}
