namespace DocuRag.Core.Contracts;

/// <summary>Télécharge le contenu brut d'un blob (le PDF) depuis le Storage.</summary>
public interface IDocumentDownloader
{
    Task<byte[]> DownloadAsync(string blobUrl, CancellationToken cancellationToken = default);
}
