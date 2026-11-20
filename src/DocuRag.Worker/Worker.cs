using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using DocuRag.Core.Messaging;
using DocuRag.Infrastructure.Options;
using DocuRag.Infrastructure.Processing;
using Microsoft.Extensions.Options;

namespace DocuRag.Worker;

/// <summary>
/// Consomme la queue <c>doc-processing</c> avec un <see cref="ServiceBusProcessor"/>.
/// - Complete / DeadLetter explicites (J4).
/// - Message non désérialisable → dead-letter immédiat (poison).
/// - Échec de traitement → Abandon : rejoué jusqu'à <c>maxDeliveryCount</c>, puis le broker
///   le dead-letter automatiquement (J10).
/// - Restaure le <c>traceparent</c> transporté par le message pour relier la trace distribuée (J12).
/// </summary>
public sealed class Worker : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("DocuRag.Worker");

    private readonly ServiceBusClient _client;
    private readonly DocumentProcessor _processor;
    private readonly ILogger<Worker> _logger;
    private readonly string _queueName;
    private ServiceBusProcessor? _sbProcessor;

    public Worker(
        ServiceBusClient client,
        DocumentProcessor processor,
        IOptions<ServiceBusOptions> options,
        ILogger<Worker> logger)
    {
        _client = client;
        _processor = processor;
        _logger = logger;
        _queueName = options.Value.Queue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _sbProcessor = _client.CreateProcessor(_queueName, new ServiceBusProcessorOptions
        {
            // On gère l'issue du message nous-mêmes (Complete/Abandon/DeadLetter).
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 4,
            PrefetchCount = 8,
        });

        _sbProcessor.ProcessMessageAsync += OnMessageAsync;
        _sbProcessor.ProcessErrorAsync += OnErrorAsync;

        _logger.LogInformation("Worker démarré, écoute de la queue {Queue}.", _queueName);
        await _sbProcessor.StartProcessingAsync(stoppingToken);

        // Tourne jusqu'à l'arrêt demandé.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal.
        }

        await _sbProcessor.StopProcessingAsync(CancellationToken.None);
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var message = args.Message;

        DocumentMessage? payload;
        try
        {
            payload = message.Body.ToObjectFromJson<DocumentMessage>();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Message illisible {MessageId}, dead-letter.", message.MessageId);
            await args.DeadLetterMessageAsync(
                message, "DeserializationError", ex.Message, args.CancellationToken);
            return;
        }

        if (payload is null)
        {
            await args.DeadLetterMessageAsync(
                message, "EmptyPayload", "Corps du message vide.", args.CancellationToken);
            return;
        }

        using var activity = StartLinkedActivity(message);

        try
        {
            await _processor.ProcessAsync(payload, args.CancellationToken);
            await args.CompleteMessageAsync(message, args.CancellationToken);
        }
        catch (Exception ex)
        {
            // Erreur transitoire possible : on abandonne pour laisser le retry/DLQ jouer.
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex,
                "Échec du traitement de {MessageId} (livraison {Count}).",
                message.MessageId, message.DeliveryCount);
            await args.AbandonMessageAsync(message, cancellationToken: args.CancellationToken);
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Erreur du processeur Service Bus ({Source}).", args.ErrorSource);
        return Task.CompletedTask;
    }

    /// <summary>Reprend le contexte de trace injecté à l'émission (J12), sinon démarre une racine.</summary>
    private static Activity? StartLinkedActivity(ServiceBusReceivedMessage message)
    {
        ActivityContext parent = default;
        if (message.ApplicationProperties.TryGetValue("traceparent", out var tp)
            && tp is string traceparent)
        {
            var state = message.ApplicationProperties.TryGetValue("tracestate", out var ts)
                ? ts as string
                : null;
            ActivityContext.TryParse(traceparent, state, out parent);
        }

        return ActivitySource.StartActivity(
            "ProcessMessage", ActivityKind.Consumer, parent);
    }

    public override void Dispose()
    {
        _sbProcessor?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.Dispose();
    }
}
