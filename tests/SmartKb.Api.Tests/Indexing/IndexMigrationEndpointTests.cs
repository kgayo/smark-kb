using System.Net;
using System.Text.Json;
using Azure;
using Azure.Search.Documents.Indexes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SmartKb.Api.Connectors;
using SmartKb.Api.Tests.Auth;
using SmartKb.Api.Tests.Connectors;
using SmartKb.Api.Webhooks;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Api.Tests.Indexing;

public class IndexMigrationEndpointTests : IAsyncLifetime
{
    private readonly MigrationTestFactory _factory = new();
    private HttpClient _adminClient = null!;

    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        await _factory.EnsureDatabaseAsync();
        _adminClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-1", roles: "Admin");
    }

    public async Task DisposeAsync()
    {
        _adminClient.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task GetCurrentVersion_NoVersions_Returns404()
    {
        var response = await _adminClient.GetAsync("/api/admin/index-migrations/evidence/current");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListVersions_Empty_ReturnsEmptyList()
    {
        var response = await _adminClient.GetAsync("/api/admin/index-migrations/evidence/versions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(0, data.GetArrayLength());
    }

    [Fact]
    public async Task Bootstrap_CreatesVersionTracking()
    {
        var response = await _adminClient.PostAsync("/api/admin/index-migrations/evidence/bootstrap", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.Equal(1, data.GetProperty("version").GetInt32());
        Assert.Equal("Active", data.GetProperty("status").GetString());
        Assert.Equal("evidence", data.GetProperty("indexType").GetString());
        Assert.Equal("evidence", data.GetProperty("indexName").GetString());
    }

    [Fact]
    public async Task Bootstrap_ThenGetCurrent_ReturnsVersion()
    {
        await _adminClient.PostAsync("/api/admin/index-migrations/evidence/bootstrap", null);
        var response = await _adminClient.GetAsync("/api/admin/index-migrations/evidence/current");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task PlanMigration_NoCurrentVersion_ShowsMigrationNeeded()
    {
        var response = await _adminClient.GetAsync("/api/admin/index-migrations/evidence/plan");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.True(data.GetProperty("migrationNeeded").GetBoolean());
        Assert.Equal(1, data.GetProperty("newVersion").GetInt32());
        Assert.Equal("evidence-v1", data.GetProperty("newIndexName").GetString());
    }

    [Fact]
    public async Task PlanMigration_AfterBootstrap_NoMigrationNeeded()
    {
        await _adminClient.PostAsync("/api/admin/index-migrations/evidence/bootstrap", null);
        var response = await _adminClient.GetAsync("/api/admin/index-migrations/evidence/plan");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.False(data.GetProperty("migrationNeeded").GetBoolean());
    }

    [Fact]
    public async Task DeleteRetired_UnknownId_Returns404()
    {
        var response = await _adminClient.DeleteAsync(
            $"/api/admin/index-migrations/retired/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RequiresAdmin_SupportAgent_Returns403()
    {
        var agentClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-1", roles: "SupportAgent");
        var response = await agentClient.GetAsync("/api/admin/index-migrations/evidence/current");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        agentClient.Dispose();
    }

    [Fact]
    public async Task Rollback_NoVersions_ReturnsError()
    {
        var response = await _adminClient.PostAsync("/api/admin/index-migrations/evidence/rollback", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}

internal sealed class MigrationTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
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
                ["SearchService:Endpoint"] = "https://test.search.windows.net",
                ["SearchService:AdminApiKey"] = "test-key",
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

            // Core services needed by the endpoints under test.
            services.AddScoped<IAuditEventWriter, SqlAuditEventWriter>();
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
            services.AddScoped<IAuditEventQueryService, AuditEventQueryService>();
            services.AddSingleton(new DistillationSettings());
            services.AddScoped<IPatternDistillationService, PatternDistillationService>();
            services.AddScoped<IPatternGovernanceService, PatternGovernanceService>();
            services.AddScoped<ConnectorAdminService>();
            services.AddSingleton(new RetrievalSettings());
            services.AddScoped<ITenantRetrievalSettingsService, TenantRetrievalSettingsService>();
            services.AddScoped<IWebhookStatusService, WebhookStatusService>();
            services.AddSingleton(new RoutingAnalyticsSettings());
            services.AddScoped<IRoutingRuleService, RoutingRuleService>();
            services.AddScoped<IRoutingAnalyticsService, RoutingAnalyticsService>();
            services.AddScoped<IRoutingImprovementService, RoutingImprovementService>();
            services.AddScoped<IPiiPolicyService, PiiPolicyService>();
            services.AddScoped<IRetentionCleanupService, RetentionCleanupService>();
            services.AddScoped<IDataSubjectDeletionService, DataSubjectDeletionService>();
            services.AddScoped<ITenantCostSettingsService, TenantCostSettingsService>();
            services.AddScoped<ITokenUsageService, TokenUsageService>();
            services.AddSingleton(new PatternMaintenanceSettings());
            services.AddScoped<IContradictionDetectionService, ContradictionDetectionService>();
            services.AddScoped<IPatternMaintenanceService, PatternMaintenanceService>();
            services.AddScoped<ISynonymMapService, SynonymMapService>();
            services.AddScoped<AdoWebhookHandler>();
            services.AddScoped<SharePointWebhookHandler>();
            services.AddScoped<HubSpotWebhookHandler>();
            services.AddScoped<ClickUpWebhookHandler>();

            // Index migration service — use a dummy SearchIndexClient pointing at a fake endpoint.
            var searchSettings = new SearchServiceSettings
            {
                EvidenceIndexName = "evidence",
                PatternIndexName = "patterns",
            };
            services.AddSingleton(searchSettings);
            var dummyIndexClient = new SearchIndexClient(
                new Uri("https://test.search.windows.net"),
                new AzureKeyCredential("test-key"));
            services.AddSingleton(dummyIndexClient);
            services.AddSingleton(new AzureSearchIndexingService(
                dummyIndexClient, searchSettings, NullLogger<AzureSearchIndexingService>.Instance));
            services.AddSingleton(new AzureSearchPatternIndexingService(
                dummyIndexClient, searchSettings, NullLogger<AzureSearchPatternIndexingService>.Instance));
            services.AddScoped<IIndexMigrationService, IndexMigrationService>();
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
}
