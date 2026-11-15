using Azure.Messaging.ServiceBus;
using Azure.Monitor.OpenTelemetry.Exporter;
using DocuRag.Core;
using DocuRag.Core.Options;
using DocuRag.Core.Telemetry;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

// Active les ActivitySource des SDK Azure (Cosmos, Service Bus) pour qu'ils apparaissent dans la trace.
AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services.AddDocuRagCore(builder.Configuration);

// Un seul sender pour toute la durée de vie de l'instance (il garde la connexion AMQP ouverte).
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<DocuRagOptions>>().Value.ServiceBus;
    return sp.GetRequiredService<ServiceBusClient>().CreateSender(options.QueueName);
});

var otel = builder.Services.AddOpenTelemetry()
    .UseFunctionsWorkerDefaults()
    .WithTracing(tracing => tracing.AddSource(DocuRagTelemetry.Name, "Azure.*"))
    .WithMetrics(metrics => metrics.AddMeter(DocuRagTelemetry.Name));

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    otel.UseAzureMonitorExporter();
}

builder.Build().Run();
