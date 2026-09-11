using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using DocuRag.Core.Chunking;
using DocuRag.Core.Documents;

namespace DocuRag.Worker;

/// <summary>
/// Consomme la queue Service Bus "doc-processing" : telecharge le blob PDF,
/// extrait le texte (PdfPig) et le decoupe en chunks. Pour le J4, le resultat
/// est simplement logge (l'ecriture des embeddings dans Cosmos arrive au J5).
/// </summary>
public sealed class DocumentProcessingWorker(
    ServiceBusClient serviceBusClient,
    BlobServiceClient blobServiceClient,
    IPdfTextExtractor textExtractor,
    IChunker chunker,
    ILogger<DocumentProcessingWorker> logger) : BackgroundService
{
    private const string QueueName = "doc-processing";
    private const string ContainerName = "documents";

    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = serviceBusClient.CreateProcessor(QueueName, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 1,
            AutoCompleteMessages = false
        });

        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);

        logger.LogInformation("En ecoute sur la queue '{QueueName}'.", QueueName);

        // Reste actif jusqu'a l'arret demande de l'hote ; le vrai travail se
        // fait dans les callbacks ProcessMessageAsync/ProcessErrorAsync.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Arret normal.
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        DocumentProcessingMessage? message;
        try
        {
            message = args.Message.Body.ToObjectFromJson<DocumentProcessingMessage>();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Message illisible (id {MessageId}), abandon.", args.Message.MessageId);
            await args.AbandonMessageAsync(args.Message);
            return;
        }

        if (message is null)
        {
            logger.LogWarning("Message vide (id {MessageId}), abandon.", args.Message.MessageId);
            await args.AbandonMessageAsync(args.Message);
            return;
        }

        try
        {
            var blobClient = blobServiceClient.GetBlobContainerClient(ContainerName).GetBlobClient(message.BlobName);

            await using var stream = new MemoryStream();
            await blobClient.DownloadToAsync(stream, args.CancellationToken);
            stream.Position = 0;

            var pages = textExtractor.ExtractPages(stream);
            var chunks = chunker.Chunk(pages);

            logger.LogInformation(
                "Document {DocumentId} ({BlobName}) : {PageCount} pages -> {ChunkCount} chunks.",
                message.DocumentId,
                message.BlobName,
                pages.Count,
                chunks.Count);

            foreach (var chunk in chunks)
            {
                var preview = chunk.Text[..Math.Min(80, chunk.Text.Length)].ReplaceLineEndings(" ");
                logger.LogInformation(
                    "  chunk {Index} (page {PageNumber}, {Length} caracteres) : {Preview}...",
                    chunk.Index,
                    chunk.PageNumber,
                    chunk.Text.Length,
                    preview);
            }

            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Echec de traitement du document {DocumentId}, abandon du message.", message.DocumentId);
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(args.Exception, "Erreur du processeur Service Bus ({ErrorSource}).", args.ErrorSource);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
