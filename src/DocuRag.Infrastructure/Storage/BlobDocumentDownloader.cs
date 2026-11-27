using Azure.Storage.Blobs;
using DocuRag.Core.Contracts;

namespace DocuRag.Infrastructure.Storage;

/// <summary>
/// Télécharge un blob depuis son URL via <see cref="BlobServiceClient"/> (managed identity).
/// </summary>
public sealed class BlobDocumentDownloader : IDocumentDownloader
{
    private readonly BlobServiceClient _service;

    public BlobDocumentDownloader(BlobServiceClient service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public async Task<byte[]> DownloadAsync(
        string blobUrl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobUrl);

        var uri = new Uri(blobUrl);
        // On reconstruit le client de blob à partir du service authentifié (MI) plutôt que
        // d'ouvrir l'URL directement : garde l'auth par identité, pas de SAS dans l'URL.
        var (container, blobName) = SplitBlobUri(uri);
        var blob = _service.GetBlobContainerClient(container).GetBlobClient(blobName);

        var response = await blob.DownloadContentAsync(cancellationToken);
        return response.Value.Content.ToArray();
    }

    private static (string Container, string BlobName) SplitBlobUri(Uri uri)
    {
        // Chemin de la forme /{container}/{blob...}
        var path = uri.AbsolutePath.TrimStart('/');
        var slash = path.IndexOf('/');
        if (slash <= 0)
        {
            throw new ArgumentException($"URL de blob invalide : {uri}", nameof(uri));
        }

        var container = path[..slash];
        var blobName = Uri.UnescapeDataString(path[(slash + 1)..]);
        return (container, blobName);
    }
}
