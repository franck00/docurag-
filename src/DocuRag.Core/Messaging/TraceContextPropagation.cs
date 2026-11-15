using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using DocuRag.Core.Models;

namespace DocuRag.Core.Messaging;

/// <summary>
/// Propagation du contexte de trace W3C à travers Service Bus.
/// Sans ça, la trace se coupe à la queue : la Function et le worker apparaissent comme deux opérations
/// sans lien dans Application Insights. On écrit <c>traceparent</c> / <c>tracestate</c> dans les
/// ApplicationProperties du message, et le worker démarre son activité avec ce parent.
/// </summary>
public static class TraceContextPropagation
{
    public const string TraceParent = "traceparent";
    public const string TraceState = "tracestate";

    public static void Inject(IDictionary<string, object> properties, Activity? activity)
    {
        if (activity is null || activity.IdFormat != ActivityIdFormat.W3C || activity.Id is null)
        {
            return;
        }

        properties[TraceParent] = activity.Id;
        if (!string.IsNullOrEmpty(activity.TraceStateString))
        {
            properties[TraceState] = activity.TraceStateString;
        }
    }

    public static ActivityContext? Extract(IReadOnlyDictionary<string, object> properties)
    {
        if (!properties.TryGetValue(TraceParent, out var parent) || parent is not string traceParent)
        {
            return null;
        }

        properties.TryGetValue(TraceState, out var state);
        return ActivityContext.TryParse(traceParent, state as string, isRemote: true, out var context)
            ? context
            : null;
    }
}

/// <summary>Sérialisation du message d'ingestion (le même contrat des deux côtés de la queue).</summary>
public static class IngestionMessageSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static ServiceBusMessage ToServiceBusMessage(IngestionMessage message, Activity? activity)
    {
        var sbMessage = new ServiceBusMessage(BinaryData.FromObjectAsJson(message, Options))
        {
            ContentType = "application/json",
            Subject = "document.uploaded",
            CorrelationId = message.DocumentId,
            MessageId = message.EventId ?? Guid.NewGuid().ToString("N"),
        };
        TraceContextPropagation.Inject(sbMessage.ApplicationProperties, activity);
        return sbMessage;
    }

    /// <exception cref="JsonException">Message illisible — non transitoire, à envoyer en dead-letter.</exception>
    public static IngestionMessage Deserialize(BinaryData body) =>
        body.ToObjectFromJson<IngestionMessage>(Options)
        ?? throw new JsonException("Message vide.");
}
