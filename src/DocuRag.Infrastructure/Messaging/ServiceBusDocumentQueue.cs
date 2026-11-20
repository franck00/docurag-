using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using DocuRag.Core.Contracts;
using DocuRag.Core.Messaging;
using DocuRag.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace DocuRag.Infrastructure.Messaging;

/// <summary>
/// Publie les messages de documents sur Service Bus. Injecte le <c>traceparent</c> de
/// l'activité courante dans les propriétés applicatives pour que la trace distribuée
/// survive à la traversée de la queue (restaurée côté worker, J12).
/// </summary>
public sealed class ServiceBusDocumentQueue : IDocumentQueue, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;

    public ServiceBusDocumentQueue(ServiceBusClient client, IOptions<ServiceBusOptions> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        _sender = client.CreateSender(options.Value.Queue);
    }

    public async Task EnqueueAsync(
        DocumentMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        var sbMessage = new ServiceBusMessage(body)
        {
            ContentType = "application/json",
            MessageId = message.DocumentId, // dédoublonnage naturel côté broker
        };

        var traceparent = Activity.Current?.Id;
        if (!string.IsNullOrEmpty(traceparent))
        {
            sbMessage.ApplicationProperties["traceparent"] = traceparent;
            var state = Activity.Current?.TraceStateString;
            if (!string.IsNullOrEmpty(state))
            {
                sbMessage.ApplicationProperties["tracestate"] = state;
            }
        }

        await _sender.SendMessageAsync(sbMessage, cancellationToken);
    }

    public async ValueTask DisposeAsync() => await _sender.DisposeAsync();
}
