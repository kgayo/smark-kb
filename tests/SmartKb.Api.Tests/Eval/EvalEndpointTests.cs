using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Api.Tests.Eval;

public class EvalEndpointTests : IAsyncLifetime
{
    private readonly EvalTestFactory _factory = new();
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

    private static object MakeRequest(string runId = "eval-run-20260319-100000", string runType = "full") => new
    {
        runId,
        runType,
        totalCases = 50,
        successfulCases = 48,
        failedCases = 2,
        metricsJson = """{"groundedness":0.85,"citationCoverage":0.72,"routingAccuracy":0.65,"noEvidenceRate":0.1,"responseTypeAccuracy":0.9,"mustIncludeHitRate":0.88,"safetyPassRate":1.0,"averageConfidence":0.75,"averageDurationMs":3200}""",
        violationsJson = (string?)null,
        baselineComparisonJson = (string?)null,
        hasBlockingRegression = false,
        violationCount = 0,
    };

    #region POST /api/admin/eval/reports

    [Fact]
    public async Task PostEvalReport_ReturnsCreated()
    {
        var response = await _adminClient.PostAsJsonAsync("/api/admin/eval/reports", MakeRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.Equal("eval-run-20260319-100000", data.GetProperty("runId").GetString());
        Assert.Equal("full", data.GetProperty("runType").GetString());
        Assert.Equal(50, data.GetProperty("totalCases").GetInt32());
        Assert.Equal(48, data.GetProperty("successfulCases").GetInt32());
        Assert.False(data.GetProperty("hasBlockingRegression").GetBoolean());
    }

    [Fact]
    public async Task PostEvalReport_RequiresAdminRole()
    {
        var agentClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-1", roles: "SupportAgent");

        var response = await agentClient.PostAsJsonAsync("/api/admin/eval/reports", MakeRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostEvalReport_RequiresAuth()
    {
        var anonClient = _factory.CreateClient();

        var response = await anonClient.PostAsJsonAsync("/api/admin/eval/reports", MakeRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region GET /api/admin/eval/reports

    [Fact]
    public async Task ListEvalReports_Empty_ReturnsEmptyList()
    {
        var response = await _adminClient.GetAsync("/api/admin/eval/reports");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.Equal(0, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, data.GetProperty("reports").GetArrayLength());
    }

    [Fact]
    public async Task ListEvalReports_ReturnsPersisted()
    {
        await _adminClient.PostAsJsonAsync("/api/admin/eval/reports", MakeRequest("run-1"));
        await _adminClient.PostAsJsonAsync("/api/admin/eval/reports", MakeRequest("run-2", "smoke"));

        var response = await _adminClient.GetAsync("/api/admin/eval/reports");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.Equal(2, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, data.GetProperty("reports").GetArrayLength());
    }

    [Fact]
    public async Task ListEvalReports_FiltersByRunType()
    {
        await _adminClient.PostAsJsonAsync("/api/admin/eval/reports", MakeRequest("run-1", "full"));
        await _adminClient.PostAsJsonAsync("/api/admin/eval/reports", MakeRequest("run-2", "smoke"));

        var response = await _adminClient.GetAsync("/api/admin/eval/reports?runType=smoke");

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
        Assert.Equal("smoke", data.GetProperty("reports")[0].GetProperty("runType").GetString());
    }

    [Fact]
    public async Task ListEvalReports_TenantIsolation()
    {
        await _adminClient.PostAsJsonAsync("/api/admin/eval/reports", MakeRequest("run-t1"));

        var otherClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-other", roles: "Admin");
        var response = await otherClient.GetAsync("/api/admin/eval/reports");

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.Equal(0, data.GetProperty("totalCount").GetInt32());
    }

    #endregion

    #region GET /api/admin/eval/reports/{id}

    [Fact]
    public async Task GetEvalReport_ReturnsDetail()
    {
        var createResponse = await _adminClient.PostAsJsonAsync("/api/admin/eval/reports", MakeRequest());
        var createBody = await createResponse.Content.ReadAsStringAsync();
        var createJson = JsonDocument.Parse(createBody);
        var reportId = createJson.RootElement.GetProperty("data").GetProperty("id").GetString();

        var response = await _adminClient.GetAsync($"/api/admin/eval/reports/{reportId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var data = json.RootElement.GetProperty("data");

        Assert.Equal("eval-run-20260319-100000", data.GetProperty("runId").GetString());
        Assert.True(data.TryGetProperty("metrics", out var metrics));
        Assert.True(metrics.GetProperty("groundedness").GetSingle() > 0);
    }

    [Fact]
    public async Task GetEvalReport_NotFound_Returns404()
    {
        var response = await _adminClient.GetAsync($"/api/admin/eval/reports/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetEvalReport_CrossTenant_Returns404()
    {
        var createResponse = await _adminClient.PostAsJsonAsync("/api/admin/eval/reports", MakeRequest());
        var createBody = await createResponse.Content.ReadAsStringAsync();
        var createJson = JsonDocument.Parse(createBody);
        var reportId = createJson.RootElement.GetProperty("data").GetProperty("id").GetString();

        var otherClient = _factory.CreateAuthenticatedClient(tenantId: "tenant-other", roles: "Admin");
        var response = await otherClient.GetAsync($"/api/admin/eval/reports/{reportId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion
}

internal sealed class EvalTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
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
            services.AddSingleton(new SmartKb.Contracts.Configuration.RoutingAnalyticsSettings());
            services.AddScoped<IRoutingRuleService, SmartKb.Data.Repositories.RoutingRuleService>();
            services.AddScoped<IRoutingAnalyticsService, SmartKb.Data.Repositories.RoutingAnalyticsService>();
            services.AddScoped<IRoutingImprovementService, SmartKb.Data.Repositories.RoutingImprovementService>();
            services.AddScoped<IPiiPolicyService, PiiPolicyService>();
            services.AddScoped<IRetentionCleanupService, RetentionCleanupService>();
            services.AddScoped<IDataSubjectDeletionService, DataSubjectDeletionService>();
            services.AddScoped<ITenantCostSettingsService, TenantCostSettingsService>();
            services.AddScoped<ITokenUsageService, TokenUsageService>();
            services.AddSingleton(new SmartKb.Contracts.Configuration.PatternMaintenanceSettings());
            services.AddScoped<IContradictionDetectionService, SmartKb.Data.Repositories.ContradictionDetectionService>();
            services.AddScoped<IPatternMaintenanceService, SmartKb.Data.Repositories.PatternMaintenanceService>();
            services.AddScoped<ISynonymMapService, SmartKb.Data.Repositories.SynonymMapService>();
            services.AddScoped<IEvalReportService, EvalReportService>();
            services.AddScoped<AdoWebhookHandler>();
            services.AddScoped<SharePointWebhookHandler>();
            services.AddScoped<HubSpotWebhookHandler>();
            services.AddScoped<ClickUpWebhookHandler>();
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
