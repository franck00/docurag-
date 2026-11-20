using System.Text.Json;
using Azure.Messaging.ServiceBus;
using DocuRag.Core.Contracts;
using DocuRag.Core.Messaging;
using DocuRag.Core.Models;
using DocuRag.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace DocuRag.Infrastructure.Messaging;

/// <summary>
/// Lit la DLQ de la queue en mode <em>peek</em> (J10) : aucun message n'est retiré,
/// la commande peut donc être relancée sans effet de bord. Nécessite le rôle
/// <c>Azure Service Bus Data Receiver</c> (déjà porté par l'identité partagée).
/// </summary>
public sealed class ServiceBusDeadLetterInspector : IDeadLetterInspector, IAsyncDisposable
{
    /// <summary>Borne haute d'un appel, pour ne pas charger toute la DLQ en mémoire.</summary>
    public const int MaxPeek = 100;

    private readonly ServiceBusReceiver _receiver;

    public ServiceBusDeadLetterInspector(ServiceBusClient client, IOptions<ServiceBusOptions> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        _receiver = client.CreateReceiver(
            options.Value.Queue,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter });
    }

    public async Task<IReadOnlyList<DeadLetterEntry>> PeekAsync(
        int maxMessages, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxMessages, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxMessages, MaxPeek);

        var messages = await _receiver.PeekMessagesAsync(
            maxMessages, cancellationToken: cancellationToken);
        return messages.Select(ToEntry).ToList();
    }

    /// <summary>
    /// Convertit un message reçu en entrée lisible. Tolère un corps illisible : c'est
    /// justement le cas des messages dead-letterés pour <c>DeserializationError</c>.
    /// </summary>
    public static DeadLetterEntry ToEntry(ServiceBusReceivedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        DocumentMessage? payload = null;
        try
        {
            payload = message.Body.ToObjectFromJson<DocumentMessage>();
        }
        catch (JsonException)
        {
            // Corps invalide : on restitue quand même le motif et les métadonnées.
        }

        return new DeadLetterEntry
        {
            MessageId = message.MessageId,
            SequenceNumber = message.SequenceNumber,
            DocumentId = payload?.DocumentId,
            FileName = payload?.FileName,
            Reason = message.DeadLetterReason,
            ErrorDescription = message.DeadLetterErrorDescription,
            DeliveryCount = message.DeliveryCount,
            EnqueuedAt = message.EnqueuedTime,
        };
    }

    public async ValueTask DisposeAsync() => await _receiver.DisposeAsync();
}
