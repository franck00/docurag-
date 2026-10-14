using Azure.Monitor.OpenTelemetry.Exporter;
using DocuRag.Infrastructure.DependencyInjection;
using DocuRag.Worker;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

// Service Bus (client + nom de queue) et toute la chaîne de traitement d'un document.
builder.Services.AddServiceBusQueue(builder.Configuration);
builder.Services.AddDocumentProcessing(builder.Configuration);
builder.Services.AddHostedService<Worker>();

// Observabilité (J12) : traces + métriques exportées vers Application Insights.
if (!string.IsNullOrEmpty(
        Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing
            .AddSource("DocuRag.Worker")
            .AddHttpClientInstrumentation())
        .WithMetrics(metrics => metrics
            .AddMeter("DocuRag.Worker"))
        .UseAzureMonitorExporter();
}

var host = builder.Build();
host.Run();
