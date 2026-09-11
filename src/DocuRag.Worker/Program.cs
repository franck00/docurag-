using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using DocuRag.Core.Chunking;
using DocuRag.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
builder.Services.AddSingleton<IChunker, ParagraphChunker>();

builder.Services.AddSingleton(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString("ServiceBus")
        ?? throw new InvalidOperationException(
            "Connection string 'ServiceBus' manquante. Configure-la via 'dotnet user-secrets' en local.");
    return new ServiceBusClient(connectionString);
});

builder.Services.AddSingleton(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString("Storage")
        ?? throw new InvalidOperationException(
            "Connection string 'Storage' manquante. Configure-la via 'dotnet user-secrets' en local.");
    return new BlobServiceClient(connectionString);
});

builder.Services.AddHostedService<DocumentProcessingWorker>();

var host = builder.Build();
host.Run();
