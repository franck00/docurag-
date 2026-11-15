using System.Diagnostics;
using System.Text;
using DocuRag.Core.AI;
using DocuRag.Core.Cosmos;
using DocuRag.Core.Models;
using DocuRag.Core.Options;
using DocuRag.Core.Telemetry;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;

namespace DocuRag.Api;

public sealed record AskRequest(string Question, string? DocType = null, int? TopK = null);

public sealed record AskResponse(
    string Answer,
    IReadOnlyList<SourceCitation> Sources,
    string Cache,
    double? CacheSimilarity,
    long ElapsedMs,
    int PromptTokens,
    int CompletionTokens);

public static class FeatureFlags
{
    /// <summary>Flag App Configuration qui active le cache sémantique.</summary>
    public const string SemanticCache = "SemanticCache";
}

/// <summary>
/// Question → (cache) → embedding → recherche vectorielle Cosmos → prompt avec extraits numérotés
/// → gpt-4o-mini → réponse avec sources et numéros de page.
/// </summary>
public sealed class RagService(
    ITextEmbedder embedder,
    IChatCompleter chat,
    ChunkRepository chunks,
    SemanticCache cache,
    IFeatureManager features,
    IOptions<DocuRagOptions> options,
    ILogger<RagService> logger)
{
    internal const string SystemPrompt = """
        Tu es un assistant juridique qui répond à partir de textes réglementaires publics
        (code des douanes CEMAC, actes uniformes OHADA, notes de la DGI).

        Règles :
        - Réponds uniquement à partir des extraits fournis. N'invente rien, n'utilise pas de connaissances externes.
        - Cite tes sources entre crochets avec le numéro de l'extrait, par exemple [1] ou [2][3], après chaque affirmation.
        - Si les extraits ne permettent pas de répondre, dis-le clairement et n'essaie pas de deviner.
        - Réponds en français, de façon concise et structurée.
        """;

    private const int ExcerptLength = 400;

    public async Task<AskResponse> AskAsync(AskRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var question = request.Question.Trim();
        var docType = string.IsNullOrWhiteSpace(request.DocType) ? null : request.DocType.Trim().ToLowerInvariant();
        var topK = Math.Clamp(request.TopK ?? options.Value.Rag.TopK, 1, 20);
        var cacheEnabled = await features.IsEnabledAsync(FeatureFlags.SemanticCache);

        Activity.Current?.SetTag("docurag.doc_type", docType ?? "*");
        Activity.Current?.SetTag("docurag.cache_enabled", cacheEnabled);

        // 1. Cache exact : lecture ponctuelle Cosmos, pas d'appel au modèle.
        if (cacheEnabled && await cache.GetExactAsync(question, docType, cancellationToken) is { } exact)
        {
            return FromCache(exact, stopwatch);
        }

        // 2. Embedding de la question (sert au cache sémantique ET à la recherche).
        var questionVector = (await embedder.EmbedAsync([question], cancellationToken))[0];

        if (cacheEnabled && await cache.GetSimilarAsync(questionVector, docType, cancellationToken) is { } similar)
        {
            return FromCache(similar, stopwatch);
        }

        DocuRagTelemetry.CacheLookups.Add(1, new KeyValuePair<string, object?>("result", cacheEnabled ? "miss" : "disabled"));

        // 3. Recherche vectorielle top-K.
        var hits = await chunks.SearchAsync(questionVector, topK, docType, cancellationToken);
        if (hits.Count == 0)
        {
            return new AskResponse(
                "Aucun document indexé ne correspond à cette question" + (docType is null ? "." : $" (type « {docType} »)."),
                [], cacheEnabled ? "miss" : "disabled", null, stopwatch.ElapsedMilliseconds, 0, 0);
        }

        // 4. Génération avec les extraits numérotés.
        var answer = await chat.CompleteAsync(SystemPrompt, BuildUserPrompt(question, hits), cancellationToken);
        var sources = hits.Select((hit, i) => new SourceCitation
        {
            Rank = i + 1,
            DocumentId = hit.DocumentId,
            FileName = hit.FileName,
            DocType = hit.DocType,
            PageStart = hit.PageStart,
            PageEnd = hit.PageEnd,
            Score = Math.Round(hit.Score, 4),
            Excerpt = hit.Content.Length <= ExcerptLength ? hit.Content : hit.Content[..ExcerptLength] + "…",
        }).ToList();

        if (cacheEnabled)
        {
            try
            {
                await cache.StoreAsync(question, docType, questionVector, answer.Text, sources, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Le cache est une optimisation : son échec ne doit pas faire échouer la réponse.
                logger.LogWarning(ex, "Écriture du cache impossible");
            }
        }

        return new AskResponse(answer.Text, sources, cacheEnabled ? "miss" : "disabled", null,
            stopwatch.ElapsedMilliseconds, answer.PromptTokens, answer.CompletionTokens);
    }

    internal static string BuildUserPrompt(string question, IReadOnlyList<ChunkHit> hits)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Extraits :");
        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            var pages = hit.PageStart == hit.PageEnd ? $"p. {hit.PageStart}" : $"p. {hit.PageStart}-{hit.PageEnd}";
            sb.AppendLine();
            sb.AppendLine($"[{i + 1}] {hit.FileName}, {pages}");
            sb.AppendLine(hit.Content);
        }

        sb.AppendLine();
        sb.AppendLine($"Question : {question}");
        return sb.ToString();
    }

    private static AskResponse FromCache(CacheHit hit, Stopwatch stopwatch)
    {
        DocuRagTelemetry.CacheLookups.Add(1, new KeyValuePair<string, object?>("result", hit.Kind));
        return new AskResponse(hit.Entry.Answer, hit.Entry.Sources, hit.Kind, Math.Round(hit.Similarity, 4),
            stopwatch.ElapsedMilliseconds, 0, 0);
    }
}
