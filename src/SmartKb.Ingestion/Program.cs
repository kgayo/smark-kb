using SmartKb.Ingestion;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddHealthChecks();

var host = builder.Build();
host.Run();
