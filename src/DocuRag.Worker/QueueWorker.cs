using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using DocuRag.Core.Messaging;
using DocuRag.Core.Models;
using DocuRag.Core.Options;
using DocuRag.Core.Pdf;
using DocuRag.Core.Telemetry;
using Microsoft.Extensions.Options;

namespace DocuRag.Worker;

/// <summary>
/// Consommateur de la queue <c>doc-processing</c>.
/// <list type="bullet">
/// <item>Complétion explicite (<c>AutoCompleteMessages = false</c>) : un message n'est retiré qu'une fois le document indexé.</item>
/// <item>Erreur non transitoire (PDF corrompu, message illisible) → dead-letter immédiat avec un motif lisible.</item>
/// <item>Erreur transitoire → abandon ; Service Bus redélivre jusqu'à <c>maxDeliveryCount</c>, puis dead-letter automatique.</item>
/// </list>
/// </summary>
public sealed class QueueWorker(
    ServiceBusClient client,
    IServiceProvider services,
    IOptions<DocuRagOptions> options,
    HealthState health,
    ILogger<QueueWorker> logger) : BackgroundService
{
    private readonly ServiceBusOptions _options = options.Value.ServiceBus;
    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = client.CreateProcessor(_options.QueueName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = _options.MaxConcurrentCalls,
            PrefetchCount = 0,
            // Un gros PDF peut prendre plusieurs minutes (embeddings + throttling) : on renouvelle le verrou.
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(15),
        });
        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);
        health.MarkStarted();
        logger.LogInformation("Écoute de la queue {Queue}", _options.QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        var received = args.Message;

        // Reprise de la trace démarrée par la Function : upload → queue → worker → Cosmos = une seule trace.
        var parent = TraceContextPropagation.Extract(received.ApplicationProperties);
        using var activity = DocuRagTelemetry.ActivitySource.StartActivity(
            "worker.process-document", ActivityKind.Consumer, parent ?? default);
        activity?.SetTag("messaging.system", "servicebus");
        activity?.SetTag("messaging.destination.name", _options.QueueName);
        activity?.SetTag("messaging.message.id", received.MessageId);
        activity?.SetTag("messaging.servicebus.delivery_count", received.DeliveryCount);

        IngestionMessage message;
        try
        {
            message = IngestionMessageSerializer.Deserialize(received.Body);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Message {MessageId} illisible, envoi en dead-letter", received.MessageId);
            await args.DeadLetterMessageAsync(received, "InvalidMessage", Truncate(ex.Message), args.CancellationToken);
            DocuRagTelemetry.DocumentsProcessed.Add(1, new KeyValuePair<string, object?>("status", "dead_lettered"));
            return;
        }

        activity?.SetTag("docurag.document_id", message.DocumentId);
        using var scope = logger.BeginScope(new Dictionary<string, object> { ["DocumentId"] = message.DocumentId });
        var processor = services.GetRequiredService<DocumentProcessor>();

        try
        {
            var count = await processor.ProcessAsync(message, args.CancellationToken);
            await args.CompleteMessageAsync(received, args.CancellationToken);
            DocuRagTelemetry.DocumentsProcessed.Add(1, new KeyValuePair<string, object?>("status", "indexed"));
            logger.LogInformation("Document {DocumentId} indexé : {ChunkCount} extraits", message.DocumentId, count);
        }
        catch (InvalidPdfException ex)
        {
            // Non transitoire : retenter ne réparera pas le fichier.
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogWarning(ex, "PDF invalide {BlobName}, envoi en dead-letter", message.BlobName);
            await processor.MarkFailedAsync(message, ex.Message, CancellationToken.None);
            await args.DeadLetterMessageAsync(received, "InvalidPdf", Truncate(ex.Message), args.CancellationToken);
            DocuRagTelemetry.DocumentsProcessed.Add(1, new KeyValuePair<string, object?>("status", "dead_lettered"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !args.CancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var lastAttempt = received.DeliveryCount >= _options.MaxDeliveryCount;
            logger.LogError(ex, "Échec du traitement de {DocumentId} (tentative {Attempt}/{Max})",
                message.DocumentId, received.DeliveryCount, _options.MaxDeliveryCount);

            if (lastAttempt)
            {
                // Service Bus va passer le message en DLQ (MaxDeliveryCountExceeded) à l'abandon.
                await processor.MarkFailedAsync(message, Truncate(ex.Message), CancellationToken.None);
                DocuRagTelemetry.DocumentsProcessed.Add(1, new KeyValuePair<string, object?>("status", "failed"));
            }

            await args.AbandonMessageAsync(received, new Dictionary<string, object>
            {
                ["lastError"] = Truncate(ex.Message),
            }, CancellationToken.None);
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(args.Exception, "Erreur Service Bus ({Source}) sur {Entity}", args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }

    private static string Truncate(string value) => value.Length <= 1000 ? value : value[..1000];
}
