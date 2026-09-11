namespace DocuRag.Core.Documents;

/// <summary>
/// Message publie sur la queue Service Bus "doc-processing" pour declencher
/// l'extraction/chunking cote worker (J4).
/// </summary>
public sealed record DocumentProcessingMessage(string DocumentId, string BlobName, string BlobUrl);
