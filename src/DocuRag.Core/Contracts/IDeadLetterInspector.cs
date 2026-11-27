using DocuRag.Core.Models;

namespace DocuRag.Core.Contracts;

/// <summary>
/// Consulte la dead-letter queue de <c>doc-processing</c> en lecture seule (peek) :
/// les messages ne sont ni consommés ni verrouillés.
/// </summary>
public interface IDeadLetterInspector
{
    /// <summary>Retourne au plus <paramref name="maxMessages"/> messages en DLQ, les plus anciens d'abord.</summary>
    Task<IReadOnlyList<DeadLetterEntry>> PeekAsync(
        int maxMessages, CancellationToken cancellationToken = default);
}
