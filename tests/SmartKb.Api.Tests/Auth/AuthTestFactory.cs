using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
                new TenantEntity { TenantId = "tenant-ret-rbac2", DisplayName = "Retrieval RBAC Test 2", CreatedAt = now });
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
        });
    }
}
