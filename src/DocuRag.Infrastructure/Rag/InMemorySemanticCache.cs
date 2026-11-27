using DocuRag.Core.Contracts;
using DocuRag.Core.Models;
using DocuRag.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace DocuRag.Infrastructure.Rag;

/// <summary>
/// Cache sémantique en mémoire du processus API (J13, première étape avant Redis).
/// Recherche linéaire par similarité cosinus : suffisant pour quelques centaines d'entrées,
/// sans dépendance supplémentaire. Chaque réplica de l'API a son propre cache.
/// Invalidation : expiration (TTL) — pas de notification lors d'une ré-indexation.
/// </summary>
public sealed class InMemorySemanticCache : ISemanticCache
{
    private readonly SemanticCacheOptions _options;
    private readonly TimeProvider _time;
    private readonly List<Entry> _entries = [];
    private readonly Lock _gate = new();

    public InMemorySemanticCache(IOptions<SemanticCacheOptions> options, TimeProvider? time = null)
    {
        _options = options.Value;
        _time = time ?? TimeProvider.System;
    }

    public Task<AskResponse?> TryGetAsync(
        float[] questionVector, string? documentType, int topK, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(questionVector);
        if (!_options.Enabled)
        {
            return Task.FromResult<AskResponse?>(null);
        }

        var now = _time.GetUtcNow();
        AskResponse? best = null;
        var bestScore = _options.SimilarityThreshold;

        lock (_gate)
        {
            _entries.RemoveAll(e => e.ExpiresAt <= now);
            foreach (var entry in _entries)
            {
                if (entry.TopK != topK || !string.Equals(entry.DocumentType, documentType, StringComparison.Ordinal))
                {
                    continue;
                }

                var score = CosineSimilarity(entry.Vector, questionVector);
                if (score >= bestScore)
                {
                    bestScore = score;
                    best = entry.Response;
                }
            }
        }

        return Task.FromResult(best);
    }

    public Task SetAsync(
        float[] questionVector, string? documentType, int topK, AskResponse response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(questionVector);
        ArgumentNullException.ThrowIfNull(response);
        if (!_options.Enabled)
        {
            return Task.CompletedTask;
        }

        var now = _time.GetUtcNow();
        var entry = new Entry(
            (float[])questionVector.Clone(), documentType, topK, response,
            now.AddMinutes(_options.TtlMinutes));

        lock (_gate)
        {
            _entries.RemoveAll(e => e.ExpiresAt <= now);
            _entries.Add(entry);
            // Liste en ordre d'insertion : les plus anciennes sont en tête.
            var overflow = _entries.Count - _options.MaxEntries;
            if (overflow > 0)
            {
                _entries.RemoveRange(0, overflow);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Similarité cosinus ; 0 si les dimensions diffèrent ou si un vecteur est nul.</summary>
    public static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0;
        }

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private sealed record Entry(
        float[] Vector, string? DocumentType, int TopK, AskResponse Response, DateTimeOffset ExpiresAt);
}
