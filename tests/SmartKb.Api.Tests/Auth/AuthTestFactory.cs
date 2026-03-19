using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartKb.Api.Audit;
using SmartKb.Api.Tests.Connectors;
using SmartKb.Contracts.Configuration;
using SmartKb.Contracts.Services;
using SmartKb.Data;
using SmartKb.Data.Entities;
using SmartKb.Data.Repositories;

namespace SmartKb.Api.Tests.Auth;

public sealed class AuthTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly SqliteConnection _connection;

    public AuthTestFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public async Task InitializeAsync()
    {
        // Force host creation so we can seed the database.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartKbDbContext>();
        await db.Database.EnsureCreatedAsync();

        if (!await db.Tenants.AnyAsync(t => t.TenantId == "tenant-1"))
        {
            var now = DateTimeOffset.UtcNow;
            db.Tenants.AddRange(
                new TenantEntity { TenantId = "tenant-1", DisplayName = "Test Tenant 1", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-other", DisplayName = "Test Tenant Other", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-ret", DisplayName = "Retrieval Test", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-ret-upd", DisplayName = "Retrieval Update Test", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-ret-del", DisplayName = "Retrieval Delete Test", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-ret-nf", DisplayName = "Retrieval NotFound Test", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-ret-rbac", DisplayName = "Retrieval RBAC Test", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-ret-rbac2", DisplayName = "Retrieval RBAC Test 2", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-priv", DisplayName = "Privacy Test", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-priv-ret", DisplayName = "Privacy Retention Test", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-priv-del", DisplayName = "Privacy Deletion Test", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-priv-rbac", DisplayName = "Privacy RBAC Test", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-priv-hist", DisplayName = "Privacy History Test", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-priv-hist2", DisplayName = "Privacy History Test 2", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-priv-hist3", DisplayName = "Privacy History Test 3", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-priv-comp", DisplayName = "Privacy Compliance Test", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-priv-comp2", DisplayName = "Privacy Compliance Test 2", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-priv-comp3", DisplayName = "Privacy Compliance Test 3", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-priv-metric", DisplayName = "Privacy Metric Test", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-priv-metric2", DisplayName = "Privacy Metric Test 2", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-priv-iso1", DisplayName = "Privacy Isolation Test 1", CreatedAt = now },
                new TenantEntity { TenantId = "tenant-priv-iso2", DisplayName = "Privacy Isolation Test 2", CreatedAt = now });
            await db.SaveChangesAsync();
        }
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
            services.AddSingleton<InMemoryAuditEventWriter>();
            services.AddSingleton<IAuditEventWriter>(sp => sp.GetRequiredService<InMemoryAuditEventWriter>());
            services.AddScoped<IAnswerTraceWriter, SqlAnswerTraceWriter>();
            services.AddScoped<ISessionService, SessionService>();
            services.AddSingleton<ISyncJobPublisher, TestSyncJobPublisher>();
            services.AddSingleton(new SessionSettings());
            services.AddSingleton(new EscalationSettings());
            services.AddSingleton<ISecretProvider>(new InMemorySecretProvider());
            services.AddScoped<ITeamPlaybookService, TeamPlaybookService>();
            services.AddScoped<IEscalationDraftService, EscalationDraftService>();
            services.AddScoped<IFeedbackService, FeedbackService>();
            services.AddScoped<IOutcomeService, OutcomeService>();
            services.AddScoped<SmartKb.Api.Connectors.ConnectorAdminService>();
            services.AddSingleton(new SmartKb.Contracts.Configuration.DistillationSettings());
            services.AddScoped<IPatternDistillationService, SmartKb.Data.Repositories.PatternDistillationService>();
            services.AddScoped<IPatternGovernanceService, PatternGovernanceService>();
            services.AddSingleton<IAuditEventQueryService>(sp =>
                new InMemoryAuditEventQueryService(sp.GetRequiredService<InMemoryAuditEventWriter>()));
            services.AddSingleton(new RetrievalSettings());
            services.AddScoped<ITenantRetrievalSettingsService, TenantRetrievalSettingsService>();
            services.AddScoped<IWebhookStatusService, SmartKb.Data.Repositories.WebhookStatusService>();
            services.AddSingleton(new SmartKb.Contracts.Configuration.RoutingAnalyticsSettings());
            services.AddScoped<IRoutingRuleService, SmartKb.Data.Repositories.RoutingRuleService>();
            services.AddScoped<IRoutingAnalyticsService, SmartKb.Data.Repositories.RoutingAnalyticsService>();
            services.AddScoped<IRoutingImprovementService, SmartKb.Data.Repositories.RoutingImprovementService>();
            services.AddScoped<IPiiPolicyService, SmartKb.Data.Repositories.PiiPolicyService>();
            services.AddSingleton<IOptions<RetentionSettings>>(Options.Create(new RetentionSettings()));
            services.AddScoped<IRetentionCleanupService, SmartKb.Data.Repositories.RetentionCleanupService>();
            services.AddScoped<IDataSubjectDeletionService, SmartKb.Data.Repositories.DataSubjectDeletionService>();
            services.AddScoped<ITenantCostSettingsService, SmartKb.Data.Repositories.TenantCostSettingsService>();
            services.AddScoped<ITokenUsageService, SmartKb.Data.Repositories.TokenUsageService>();
            services.AddSingleton(new SmartKb.Contracts.Configuration.PatternMaintenanceSettings());
            services.AddScoped<IContradictionDetectionService, SmartKb.Data.Repositories.ContradictionDetectionService>();
            services.AddScoped<IPatternMaintenanceService, SmartKb.Data.Repositories.PatternMaintenanceService>();
            services.AddScoped<ISynonymMapService, SmartKb.Data.Repositories.SynonymMapService>();
            services.AddSingleton<IOptions<SloSettings>>(Options.Create(new SloSettings()));
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<IRateLimitAlertService, SmartKb.Data.Repositories.RateLimitAlertService>();
        });
    }
}
