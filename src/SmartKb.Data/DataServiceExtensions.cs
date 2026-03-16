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
        services.AddScoped<IAnswerTraceWriter, SqlAnswerTraceWriter>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IEscalationDraftService, EscalationDraftService>();

        return services;
    }
}
