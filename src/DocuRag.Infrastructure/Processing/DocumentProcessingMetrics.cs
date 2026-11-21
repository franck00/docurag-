using System.Diagnostics.Metrics;

namespace DocuRag.Infrastructure.Processing;

/// <summary>
/// Métriques custom du traitement des documents (J12) : documents traités, chunks générés,
/// latence d'embedding. Le meter porte le nom <c>DocuRag.Worker</c>, déjà abonné par
/// OpenTelemetry dans le worker (<c>AddMeter("DocuRag.Worker")</c>) : les valeurs arrivent
/// dans la table <c>customMetrics</c> d'Application Insights sans configuration supplémentaire.
/// </summary>
public static class DocumentProcessingMetrics
{
    public const string MeterName = "DocuRag.Worker";

    public const string DocumentsProcessedName = "docurag.documents.processed";
    public const string ChunksGeneratedName = "docurag.chunks.generated";
    public const string EmbeddingDurationName = "docurag.embedding.duration";

    /// <summary>Tag de résultat sur <see cref="DocumentsProcessed"/> : <c>indexed</c> ou <c>failed</c>.</summary>
    public const string OutcomeTag = "outcome";
    public const string DocumentTypeTag = "document.type";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>Nombre de documents traités, ventilé par résultat et par type.</summary>
    public static readonly Counter<long> DocumentsProcessed = Meter.CreateCounter<long>(
        DocumentsProcessedName, unit: "{document}",
        description: "Documents traités par le worker (indexés ou en échec).");

    /// <summary>Nombre de chunks écrits dans Cosmos.</summary>
    public static readonly Counter<long> ChunksGenerated = Meter.CreateCounter<long>(
        ChunksGeneratedName, unit: "{chunk}",
        description: "Chunks générés et écrits dans le conteneur vectoriel.");

    /// <summary>Durée d'un appel d'embedding par lot (retries du client inclus).</summary>
    public static readonly Histogram<double> EmbeddingDuration = Meter.CreateHistogram<double>(
        EmbeddingDurationName, unit: "ms",
        description: "Latence d'un appel d'embedding Azure OpenAI (un lot de chunks).");
}
