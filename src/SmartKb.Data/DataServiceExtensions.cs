using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartKb.Contracts.Services;
using SmartKb.Data.Repositories;

namespace SmartKb.Data;

public static class DataServiceExtensions
{
    public static IServiceCollection AddSmartKbData(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SmartKbDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(SmartKbDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(maxRetryCount: 3);
            }));

        services.AddScoped<IAuditEventWriter, SqlAuditEventWriter>();
        services.AddScoped<IAuditEventQueryService, AuditEventQueryService>();
        services.AddScoped<IAnswerTraceWriter, SqlAnswerTraceWriter>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IEscalationDraftService, EscalationDraftService>();
        services.AddScoped<IFeedbackService, FeedbackService>();
        services.AddScoped<IOutcomeService, OutcomeService>();
        services.AddScoped<IPatternDistillationService, PatternDistillationService>();
        services.AddScoped<IPatternGovernanceService, PatternGovernanceService>();
        services.AddScoped<ITenantRetrievalSettingsService, TenantRetrievalSettingsService>();
        services.AddScoped<IWebhookStatusService, WebhookStatusService>();
        services.AddScoped<IRoutingRuleService, RoutingRuleService>();
        services.AddScoped<IRoutingAnalyticsService, RoutingAnalyticsService>();
        services.AddScoped<IRoutingImprovementService, RoutingImprovementService>();
        services.AddScoped<IPiiPolicyService, PiiPolicyService>();
        services.AddScoped<IRetentionCleanupService, RetentionCleanupService>();
        services.AddScoped<IDataSubjectDeletionService, DataSubjectDeletionService>();

        return services;
    }
}
