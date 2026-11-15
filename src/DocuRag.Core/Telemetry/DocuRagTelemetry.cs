using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DocuRag.Core.Telemetry;

/// <summary>
/// Source de traces et métriques custom, partagée par les trois services.
/// Côté OpenTelemetry : <c>.AddSource(DocuRagTelemetry.Name)</c> et <c>.AddMeter(DocuRagTelemetry.Name)</c>.
/// Dans Application Insights, les métriques arrivent dans la table <c>customMetrics</c>.
/// </summary>
public static class DocuRagTelemetry
{
    public const string Name = "DocuRag";

    public static readonly ActivitySource ActivitySource = new(Name);

    private static readonly Meter Meter = new(Name);

    /// <summary>Documents traités par le worker, tag <c>status</c> = indexed | failed | dead_lettered.</summary>
    public static readonly Counter<long> DocumentsProcessed =
        Meter.CreateCounter<long>("docurag.documents.processed", unit: "{document}");

    public static readonly Counter<long> ChunksGenerated =
        Meter.CreateCounter<long>("docurag.chunks.generated", unit: "{chunk}");

    public static readonly Histogram<double> EmbeddingDuration =
        Meter.CreateHistogram<double>("docurag.embedding.duration", unit: "ms");

    /// <summary>Tokens consommés, tags <c>model</c> et <c>kind</c> = embedding | prompt | completion.</summary>
    public static readonly Counter<long> TokensUsed =
        Meter.CreateCounter<long>("docurag.tokens", unit: "{token}");

    /// <summary>Recherches dans le cache, tag <c>result</c> = exact | semantic | miss | disabled.</summary>
    public static readonly Counter<long> CacheLookups =
        Meter.CreateCounter<long>("docurag.cache.lookups", unit: "{lookup}");
}
