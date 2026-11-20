using System.Diagnostics;
using System.Text;
using DocuRag.Core.Contracts;
using DocuRag.Core.Models;
using Microsoft.Extensions.Logging;

namespace DocuRag.Infrastructure.Rag;

/// <summary>
/// Pipeline de réponse : embedding de la question → recherche vectorielle Cosmos →
/// construction d'un prompt avec les extraits → <c>gpt-4o-mini</c> → réponse sourcée.
/// </summary>
public sealed class RagService
{
    private const int MaxExcerptLength = 280;
    private static readonly ActivitySource ActivitySource = new("DocuRag.Api");

    private const string SystemPrompt =
        """
        Tu es un assistant spécialisé dans les textes réglementaires publics (douanes CEMAC,
        OHADA, DGI). Réponds UNIQUEMENT à partir des extraits fournis. Pour chaque affirmation,
        cite la source entre crochets au format [nom-du-fichier p.N]. Si les extraits ne
        contiennent pas la réponse, dis-le explicitement au lieu d'inventer. Réponds en français,
        de façon précise et concise.
        """;

    private readonly IEmbeddingGenerator _embeddings;
    private readonly IVectorStore _store;
    private readonly IChatCompletionService _chat;
    private readonly ILogger<RagService> _logger;

    public RagService(
        IEmbeddingGenerator embeddings,
        IVectorStore store,
        IChatCompletionService chat,
        ILogger<RagService> logger)
    {
        _embeddings = embeddings;
        _store = store;
        _chat = chat;
        _logger = logger;
    }

    public async Task<AskResponse> AskAsync(AskRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question);

        using var activity = ActivitySource.StartActivity("Ask");
        var topK = request.TopK <= 0 ? 5 : request.TopK;

        var queryVector = await _embeddings.EmbedAsync(request.Question, cancellationToken);
        var hits = await _store.SearchAsync(
            queryVector, topK, request.DocumentType, cancellationToken);

        activity?.SetTag("ask.hits", hits.Count);

        if (hits.Count == 0)
        {
            return new AskResponse
            {
                Answer = "Aucun extrait pertinent trouvé dans le corpus indexé pour cette question.",
                Sources = [],
            };
        }

        var userPrompt = BuildPrompt(request.Question, hits);
        var answer = await _chat.CompleteAsync(SystemPrompt, userPrompt, cancellationToken);

        var sources = hits.Select(h => new Citation
        {
            FileName = h.Chunk.FileName,
            DocumentType = h.Chunk.DocumentType,
            Page = h.Chunk.Page,
            Score = h.Score,
            Excerpt = Truncate(h.Chunk.Content, MaxExcerptLength),
        }).ToList();

        return new AskResponse { Answer = answer, Sources = sources };
    }

    private static string BuildPrompt(string question, IReadOnlyList<VectorSearchResult> hits)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Extraits du corpus :");
        builder.AppendLine();

        for (var i = 0; i < hits.Count; i++)
        {
            var chunk = hits[i].Chunk;
            builder.AppendLine($"[{i + 1}] (source : {chunk.FileName} p.{chunk.Page})");
            builder.AppendLine(chunk.Content);
            builder.AppendLine();
        }

        builder.AppendLine("Question :");
        builder.AppendLine(question);
        return builder.ToString();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
