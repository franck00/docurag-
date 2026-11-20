using DocuRag.Core.Messaging;

namespace DocuRag.Core.Contracts;

/// <summary>
/// Publie un document à traiter sur la queue (Service Bus <c>doc-processing</c>).
/// Utilisé par la Function d'ingestion.
/// </summary>
public interface IDocumentQueue
{
    Task EnqueueAsync(DocumentMessage message, CancellationToken cancellationToken = default);
}
