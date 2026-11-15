using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using DocuRag.Api;
using DocuRag.Core;
using DocuRag.Core.Cosmos;
using DocuRag.Core.Options;
using DocuRag.Core.Telemetry;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

var builder = WebApplication.CreateBuilder(args);

// Configuration non sensible + feature flags dans App Configuration, lus avec la managed identity
// et rafraîchis toutes les 30 s (on peut couper le cache sans redéployer).
var appConfigEndpoint = builder.Configuration["AppConfig:Endpoint"];
var useAppConfig = !string.IsNullOrWhiteSpace(appConfigEndpoint);
if (useAppConfig)
{
    builder.Configuration.AddAzureAppConfiguration(appConfig => appConfig
        .Connect(new Uri(appConfigEndpoint!), new DefaultAzureCredential())
        .Select("DocuRag:*")
        .ConfigureRefresh(refresh => refresh.RegisterAll().SetRefreshInterval(TimeSpan.FromSeconds(30)))
        .UseFeatureFlags(flags => flags.SetRefreshInterval(TimeSpan.FromSeconds(30))));
    builder.Services.AddAzureAppConfiguration();
}

builder.Services.AddFeatureManagement();
builder.Services.AddDocuRagCore(builder.Configuration);
builder.Services.AddSingleton<RagService>();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var otel = builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(DocuRagTelemetry.Name, "Azure.*"))
    .WithMetrics(metrics => metrics.AddMeter(DocuRagTelemetry.Name));
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    otel.UseAzureMonitor();
}

var app = builder.Build();

if (useAppConfig)
{
    app.UseAzureAppConfiguration();
}

app.UseExceptionHandler();
app.MapOpenApi();

app.MapGet("/", () => Results.Redirect("/openapi/v1.json")).ExcludeFromDescription();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();

app.MapPost("/ask", async (AskRequest request, RagService rag, CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 2000)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["question"] = ["La question est obligatoire et doit faire moins de 2000 caractères."],
            });
        }

        return Results.Ok(await rag.AskAsync(request, ct));
    })
    .WithName("Ask")
    .WithSummary("Pose une question sur le corpus, réponse sourcée avec numéros de page.");

app.MapGet("/documents", async (DocumentRepository documents, CancellationToken ct) =>
        Results.Ok((await documents.ListAsync(ct)).Select(d => new
        {
            d.Id, d.FileName, d.DocType, Status = d.Status.ToString(), d.PageCount, d.ChunkCount, d.Error, d.UpdatedAt,
        })))
    .WithName("ListDocuments")
    .WithSummary("Liste les documents connus et leur statut d'indexation.");

app.MapGet("/stats", async (StatsRepository stats, CancellationToken ct) =>
    {
        var s = await stats.GetAsync(ct);
        return Results.Ok(new { s.DocumentCount, s.ChunkCount, s.FailedCount, s.DocumentsByType, s.LastIndexedAt });
    })
    .WithName("Stats")
    .WithSummary("Statistiques d'index maintenues par le change feed processor.");

app.MapGet("/admin/dead-letters", async (ServiceBusClient client, IOptions<DocuRagOptions> options, int? max, CancellationToken ct) =>
    {
        await using var receiver = client.CreateReceiver(options.Value.ServiceBus.QueueName,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
        var messages = await receiver.PeekMessagesAsync(Math.Clamp(max ?? 50, 1, 250), cancellationToken: ct);
        return Results.Ok(messages.Select(m => new
        {
            m.MessageId,
            DocumentId = m.CorrelationId,
            m.DeadLetterReason,
            m.DeadLetterErrorDescription,
            m.DeliveryCount,
            m.EnqueuedTime,
            LastError = m.ApplicationProperties.TryGetValue("lastError", out var e) ? e : null,
        }));
    })
    .WithName("DeadLetters")
    .WithSummary("Messages en dead-letter avec leur motif (PDF corrompu, nombre max de tentatives…).");

app.Run();
