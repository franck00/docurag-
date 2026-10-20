using Azure.Monitor.OpenTelemetry.Exporter;
using DocuRag.Core.Contracts;
using DocuRag.Core.Models;
using DocuRag.Infrastructure.DependencyInjection;
using DocuRag.Infrastructure.Rag;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Recherche vectorielle + génération (Cosmos + Azure OpenAI). Auth managed identity.
builder.Services.AddRag(builder.Configuration);

// Observabilité (J12).
if (!string.IsNullOrEmpty(
        Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing
            .AddSource("DocuRag.Api")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation())
        .WithMetrics(metrics => metrics
            .AddMeter("DocuRag.Api")
            .AddAspNetCoreInstrumentation())
        .UseAzureMonitorExporter();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Santé (health check pour Container Apps).
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("Health");

// Liste des documents indexés.
app.MapGet("/documents", async (IVectorStore store, CancellationToken ct) =>
{
    var documents = await store.ListDocumentsAsync(ct);
    return Results.Ok(documents);
})
.WithName("ListDocuments");

// Question → réponse sourcée.
app.MapPost("/ask", async (AskRequest request, RagService rag, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "Le champ 'question' est requis." });
    }

    var response = await rag.AskAsync(request, ct);
    return Results.Ok(response);
})
.WithName("Ask");

app.Run();

// Rend le point d'entrée visible aux tests d'intégration (WebApplicationFactory).
public partial class Program { }
