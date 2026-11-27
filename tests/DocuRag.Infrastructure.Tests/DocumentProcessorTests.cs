using System.Diagnostics.Metrics;
using DocuRag.Core.Messaging;
using DocuRag.Core.Models;
using DocuRag.Infrastructure.Processing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DocuRag.Infrastructure.Tests;

public class DocumentProcessorTests
{
    private static DocumentMessage Message(string documentType = "ohada") => new()
    {
        DocumentId = "ohada-acte-ab12cd34",
        BlobUrl = "https://acct.blob.core.windows.net/documents/ohada/acte.pdf",
        FileName = "acte.pdf",
        DocumentType = documentType,
    };

    private static IReadOnlyList<PageText> Pages(int count)
        => Enumerable.Range(1, count)
            .Select(p => new PageText(p, string.Join(" ", Enumerable.Repeat($"page{p}", 400))))
            .ToArray();

    private static DocumentProcessor Build(
        FakeExtractor extractor, FakeEmbeddingGenerator embeddings, InMemoryVectorStore store,
        FakeDownloader? downloader = null)
        => new(
            downloader ?? new FakeDownloader(),
            extractor,
            embeddings,
            store,
            NullLogger<DocumentProcessor>.Instance);

    [Fact]
    public async Task ProcessAsync_IndexesDocument_WithStatusAndCounts()
    {
        var store = new InMemoryVectorStore();
        var embeddings = new FakeEmbeddingGenerator();
        var processor = Build(new FakeExtractor(Pages(2)), embeddings, store);

        await processor.ProcessAsync(Message(), CancellationToken.None);

        var doc = store.Documents["ohada-acte-ab12cd34"];
        Assert.Equal(DocumentStatus.Indexed, doc.Status);
        Assert.Equal(2, doc.PageCount);
        Assert.True(doc.ChunkCount > 0);
        Assert.Equal(doc.ChunkCount, store.Chunks.Count);
        Assert.Null(doc.Error);
    }

    [Fact]
    public async Task ProcessAsync_ChunkIds_AreDeterministicAndEmbedded()
    {
        var store = new InMemoryVectorStore();
        var processor = Build(new FakeExtractor(Pages(1)), new FakeEmbeddingGenerator(), store);

        await processor.ProcessAsync(Message(), CancellationToken.None);

        Assert.All(store.Chunks.Values, c =>
        {
            Assert.Equal($"ohada-acte-ab12cd34-{c.ChunkIndex}", c.Id);
            Assert.Equal("ohada-acte-ab12cd34", c.DocumentId);
            Assert.NotEmpty(c.Embedding);
        });
    }

    [Fact]
    public async Task ProcessAsync_Replayed_IsIdempotent_NoDuplicateChunks()
    {
        var store = new InMemoryVectorStore();
        var processor = Build(new FakeExtractor(Pages(2)), new FakeEmbeddingGenerator(), store);

        await processor.ProcessAsync(Message(), CancellationToken.None);
        var countAfterFirst = store.Chunks.Count;

        // Rejoue le même message : mêmes ids déterministes => ré-écriture, pas de doublons.
        await processor.ProcessAsync(Message(), CancellationToken.None);

        Assert.Equal(countAfterFirst, store.Chunks.Count);
    }

    [Fact]
    public async Task ProcessAsync_EmbedsInBatches_NeverExceeding16()
    {
        var store = new InMemoryVectorStore();
        var embeddings = new FakeEmbeddingGenerator();
        // Beaucoup de pages => nombre de chunks largement supérieur à la taille de lot.
        var processor = Build(new FakeExtractor(Pages(30)), embeddings, store);

        await processor.ProcessAsync(Message(), CancellationToken.None);

        Assert.True(store.Chunks.Count > 16, "le document doit produire plus de 16 chunks");
        Assert.True(embeddings.MaxBatchSize <= 16, "aucun lot d'embeddings ne doit dépasser 16");
        Assert.Equal(store.Chunks.Count, embeddings.TotalEmbedded);
        Assert.True(embeddings.BatchCalls >= 2, "les embeddings doivent être envoyés en plusieurs lots");
    }

    [Fact]
    public async Task ProcessAsync_EmptyText_SetsFailed_AndThrows()
    {
        var store = new InMemoryVectorStore();
        var blankPages = new[] { new PageText(1, "   \n\n   ") };
        var processor = Build(new FakeExtractor(blankPages), new FakeEmbeddingGenerator(), store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.ProcessAsync(Message(), CancellationToken.None));

        var doc = store.Documents["ohada-acte-ab12cd34"];
        Assert.Equal(DocumentStatus.Failed, doc.Status);
        Assert.False(string.IsNullOrWhiteSpace(doc.Error));
        Assert.Empty(store.Chunks);
    }

    [Fact]
    public async Task ProcessAsync_Indexed_RecordsCustomMetrics()
    {
        // Type de document propre au test : isole ses mesures des autres tests (meter statique).
        using var recorder = new MetricsRecorder("metrics-indexed");
        var store = new InMemoryVectorStore();
        var embeddings = new FakeEmbeddingGenerator();
        var processor = Build(new FakeExtractor(Pages(30)), embeddings, store);

        await processor.ProcessAsync(Message("metrics-indexed"), CancellationToken.None);

        Assert.Equal(1, recorder.Sum(DocumentProcessingMetrics.DocumentsProcessedName, "indexed"));
        Assert.Equal(0, recorder.Sum(DocumentProcessingMetrics.DocumentsProcessedName, "failed"));
        Assert.Equal(store.Chunks.Count, recorder.Sum(DocumentProcessingMetrics.ChunksGeneratedName));
        // Une mesure de latence par lot d'embeddings.
        Assert.Equal(embeddings.BatchCalls, recorder.Count(DocumentProcessingMetrics.EmbeddingDurationName));
    }

    [Fact]
    public async Task ProcessAsync_Failed_RecordsFailedOutcome_AndNoChunks()
    {
        using var recorder = new MetricsRecorder("metrics-failed");
        var blankPages = new[] { new PageText(1, "   ") };
        var processor = Build(new FakeExtractor(blankPages), new FakeEmbeddingGenerator(), new InMemoryVectorStore());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => processor.ProcessAsync(Message("metrics-failed"), CancellationToken.None));

        Assert.Equal(1, recorder.Sum(DocumentProcessingMetrics.DocumentsProcessedName, "failed"));
        Assert.Equal(0, recorder.Sum(DocumentProcessingMetrics.DocumentsProcessedName, "indexed"));
        Assert.Equal(0, recorder.Sum(DocumentProcessingMetrics.ChunksGeneratedName));
    }

    /// <summary>Collecte les mesures du meter <c>DocuRag.Worker</c> pour un type de document donné.</summary>
    private sealed class MetricsRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, double Value, string? Outcome)> _measurements = [];
        private readonly string _documentType;

        public MetricsRecorder(string documentType)
        {
            _documentType = documentType;
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == DocumentProcessingMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Record(i, v, tags));
            _listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Record(i, v, tags));
            _listener.Start();
        }

        public double Sum(string name, string? outcome = null)
        {
            lock (_measurements)
            {
                return _measurements
                    .Where(m => m.Name == name && (outcome is null || m.Outcome == outcome))
                    .Sum(m => m.Value);
            }
        }

        public int Count(string name)
        {
            lock (_measurements)
            {
                return _measurements.Count(m => m.Name == name);
            }
        }

        public void Dispose() => _listener.Dispose();

        private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            string? type = null, outcome = null;
            foreach (var tag in tags)
            {
                if (tag.Key == DocumentProcessingMetrics.DocumentTypeTag) type = tag.Value as string;
                if (tag.Key == DocumentProcessingMetrics.OutcomeTag) outcome = tag.Value as string;
            }

            if (type != _documentType)
            {
                return;
            }

            lock (_measurements)
            {
                _measurements.Add((instrument.Name, value, outcome));
            }
        }
    }
}
