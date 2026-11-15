using Azure.Core;
using Azure.Monitor.OpenTelemetry.Exporter;
using DocuRag.Core;
using DocuRag.Core.Telemetry;
using DocuRag.Worker;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// Active les ActivitySource des SDK Azure (Service Bus, Blob, Cosmos) pour qu'ils apparaissent dans la trace.
AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

var builder = Host.CreateApplicationBuilder(args);

// Configuration non sensible centralisée dans App Configuration (lue avec la managed identity).
var appConfigEndpoint = builder.Configuration["AppConfig:Endpoint"];
if (!string.IsNullOrWhiteSpace(appConfigEndpoint))
{
    builder.Configuration.AddAzureAppConfiguration(appConfig => appConfig
        .Connect(new Uri(appConfigEndpoint), new Azure.Identity.DefaultAzureCredential())
        .Select("DocuRag:*"));
}

builder.Services.AddDocuRagCore(builder.Configuration);
builder.Services.AddSingleton<HealthState>();
builder.Services.AddSingleton<DocumentProcessor>();
builder.Services.AddHostedService<QueueWorker>();
builder.Services.AddHostedService<HeartbeatService>();

if (!builder.Configuration.GetValue("DocuRag:ChangeFeed:Disabled", false))
{
    builder.Services.AddHostedService<ChangeFeedService>();
}

var otel = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("docurag-worker"))
    .WithTracing(tracing => tracing.AddSource(DocuRagTelemetry.Name, "Azure.*"))
    .WithMetrics(metrics => metrics.AddMeter(DocuRagTelemetry.Name))
    .WithLogging();

if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    otel.UseAzureMonitorExporter();
}

var host = builder.Build();
host.Run();
