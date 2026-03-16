using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartKb.Contracts.Configuration;
using SmartKb.Ingestion;

namespace SmartKb.Ingestion.Tests;

public class IngestionWorkerTests
{
    [Fact]
    public async Task Worker_StartsAndStops_Gracefully_WithoutServiceBus()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<IngestionWorker>();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var settings = new ServiceBusSettings();

        var worker = new IngestionWorker(scopeFactory, settings, logger);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await worker.StartAsync(cts.Token);
        await Task.Delay(150);
        await worker.StopAsync(CancellationToken.None);
    }
}
