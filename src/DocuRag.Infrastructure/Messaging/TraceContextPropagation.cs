using System.Diagnostics;

namespace DocuRag.Infrastructure.Messaging;

/// <summary>
/// Propagation du contexte de trace W3C (<c>traceparent</c> / <c>tracestate</c>) à travers
/// les propriétés applicatives Service Bus (J12). Isolée ici pour que l'émetteur
/// (<see cref="ServiceBusDocumentQueue"/>) et le worker partagent les mêmes clés et que la
/// logique soit testable sans broker.
/// </summary>
public static class TraceContextPropagation
{
    public const string TraceParentKey = "traceparent";
    public const string TraceStateKey = "tracestate";

    /// <summary>
    /// Écrit le contexte de <paramref name="activity"/> dans <paramref name="properties"/>.
    /// Ne fait rien sans activité ou si son identifiant n'est pas au format W3C (un
    /// identifiant hiérarchique ne serait pas relu par <see cref="Extract"/>).
    /// </summary>
    public static void Inject(Activity? activity, IDictionary<string, object> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (activity is null || activity.IdFormat != ActivityIdFormat.W3C || string.IsNullOrEmpty(activity.Id))
        {
            return;
        }

        properties[TraceParentKey] = activity.Id;
        if (!string.IsNullOrEmpty(activity.TraceStateString))
        {
            properties[TraceStateKey] = activity.TraceStateString;
        }
    }

    /// <summary>
    /// Relit le contexte parent depuis <paramref name="properties"/>. Renvoie <c>default</c>
    /// (pas de parent → nouvelle trace racine) si la propriété est absente, n'est pas une
    /// chaîne ou n'est pas un <c>traceparent</c> valide.
    /// </summary>
    public static ActivityContext Extract(IReadOnlyDictionary<string, object> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (!properties.TryGetValue(TraceParentKey, out var tp) || tp is not string traceParent)
        {
            return default;
        }

        var state = properties.TryGetValue(TraceStateKey, out var ts) ? ts as string : null;
        return ActivityContext.TryParse(traceParent, state, isRemote: true, out var parent) ? parent : default;
    }
}
