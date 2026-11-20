using System.Collections.Concurrent;
using DocuRag.Core.Contracts;
using DocuRag.Core.Models;

namespace DocuRag.Infrastructure.Tests;

/// <summary>Téléchargeur factice : renvoie des octets fixes, compte les appels.</summary>
internal sealed class FakeDownloader(byte[]? payload = null) : IDocumentDownloader
{
    public int Calls { get; private set; }
    public Task<byte[]> DownloadAsync(string blobUrl, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(payload ?? [1, 2, 3]);
    }
}

/// <summary>Extracteur factice : renvoie les pages fournies au constructeur.</summary>
internal sealed class FakeExtractor(IReadOnlyList<PageText> pages) : IPdfTextExtractor
{
    public IReadOnlyList<PageText> Extract(byte[] pdfBytes) => pages;
}

/// <summary>
/// Générateur d'embeddings factice : vecteur déterministe par texte, et surtout
/// enregistre la taille de chaque lot pour vérifier le batch.
/// </summary>
internal sealed class FakeEmbeddingGenerator(int dimensions = 4) : IEmbeddingGenerator
{
    public int Dimensions { get; } = dimensions;
    public int MaxBatchSize { get; private set; }
    public int TotalEmbedded { get; private set; }
    public int BatchCalls { get; private set; }

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
    {
        BatchCalls++;
        MaxBatchSize = Math.Max(MaxBatchSize, inputs.Count);
        TotalEmbedded += inputs.Count;
        IReadOnlyList<float[]> vectors = inputs.Select(Vector).ToArray();
        return Task.FromResult(vectors);
    }

    public Task<float[]> EmbedAsync(string input, CancellationToken cancellationToken = default)
        => Task.FromResult(Vector(input));

    private float[] Vector(string text)
    {
        var v = new float[Dimensions];
        var h = (uint)text.GetHashCode();
        for (var i = 0; i < Dimensions; i++)
        {
            v[i] = ((h >> (i * 4)) & 0xF) / 15f;
        }
        return v;
    }
}

/// <summary>Store vectoriel en mémoire : chunks indexés par id (idempotence testable).</summary>
internal sealed class InMemoryVectorStore : IVectorStore
{
    public ConcurrentDictionary<string, DocumentRecord> Documents { get; } = new();
    public ConcurrentDictionary<string, DocumentChunk> Chunks { get; } = new();
    public IReadOnlyList<VectorSearchResult> NextSearchResults { get; set; } = [];

    public Task UpsertDocumentAsync(DocumentRecord document, CancellationToken cancellationToken = default)
    {
        Documents[document.Id] = document;
        return Task.CompletedTask;
    }

    public Task<DocumentRecord?> GetDocumentAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(Documents.GetValueOrDefault(id));

    public Task<IReadOnlyList<DocumentRecord>> ListDocumentsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DocumentRecord>>(Documents.Values.ToArray());

    public Task UpsertChunksAsync(IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken = default)
    {
        foreach (var chunk in chunks)
        {
            Chunks[chunk.Id] = chunk;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryVector, int topK, string? documentType = null, CancellationToken cancellationToken = default)
        => Task.FromResult(NextSearchResults);
}

/// <summary>Chat factice : renvoie une réponse canned et retient le prompt reçu.</summary>
internal sealed class FakeChat(string answer = "Réponse générée.") : IChatCompletionService
{
    public int Calls { get; private set; }
    public string? LastSystemPrompt { get; private set; }
    public string? LastUserPrompt { get; private set; }

    public Task<string> CompleteAsync(
        string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastSystemPrompt = systemPrompt;
        LastUserPrompt = userPrompt;
        return Task.FromResult(answer);
    }
}
