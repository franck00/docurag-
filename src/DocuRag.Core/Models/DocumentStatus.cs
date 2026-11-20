namespace DocuRag.Core.Models;

/// <summary>
/// Cycle de vie d'un document dans le pipeline d'ingestion.
/// </summary>
public enum DocumentStatus
{
    /// <summary>Enregistré par la Function, en attente de traitement par le worker.</summary>
    Pending,

    /// <summary>Le worker a pris le message et extrait / découpe le document.</summary>
    Processing,

    /// <summary>Chunks vectorisés et écrits dans Cosmos : interrogeable.</summary>
    Indexed,

    /// <summary>Le traitement a échoué (PDF corrompu, erreur d'extraction, etc.).</summary>
    Failed
}
