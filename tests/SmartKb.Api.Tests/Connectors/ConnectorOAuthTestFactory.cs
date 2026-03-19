using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartKb.Api.Connectors;
using SmartKb.Api.Tests.Auth;
using SmartKb.Api.Webhooks;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Api.Tests.Connectors;

public sealed class ConnectorOAuthTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private readonly InMemorySecretProvider _secretProvider = new();

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

    public void SetSecret(string name, string value) => _secretProvider.Secrets[name] = value;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "<test>",
                ["AzureAd:ClientId"] = "<test>",
                ["OAuth:CallbackBaseUrl"] = "https://smartkb.test.com",
                ["OAuth:StateSigningKey"] = Convert.ToBase64String(new byte[32]),
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
            services.AddSingleton<ISecretProvider>(_secretProvider);

            // OAuth service registration (P3-019).
            var oauthSettings = new OAuthSettings
            {
                CallbackBaseUrl = "https://smartkb.test.com",
                StateSigningKey = Convert.ToBase64String(new byte[32]),
            };
            services.AddSingleton(oauthSettings);
            services.AddSingleton<IOAuthTokenService, OAuthTokenService>();

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
