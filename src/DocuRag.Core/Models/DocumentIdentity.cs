using System.Security.Cryptography;
using System.Text;

namespace DocuRag.Core.Models;

/// <summary>
/// Dérive, de façon déterministe, l'identité d'un document à partir du chemin de son
/// blob. Partagé par l'ingestion et le worker pour que les deux côtés calculent le même
/// <c>documentId</c> (indispensable à l'idempotence, J10).
///
/// Convention de nommage des blobs : <c>{type}/{nom-du-fichier}.pdf</c>, par ex.
/// <c>ohada/acte-uniforme-societes.pdf</c>. Le premier segment donne le type de document.
/// </summary>
public static class DocumentIdentity
{
    /// <summary>
    /// Identifiant Cosmos déterministe et valide (ni <c>/ \ ? #</c>) : un slug lisible
    /// dérivé du chemin, suffixé par un hash court du chemin complet pour l'unicité.
    /// </summary>
    public static string ToDocumentId(string blobPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

        var normalized = blobPath.Trim().TrimStart('/');
        var slug = Slugify(normalized);
        var hash = ShortHash(normalized);
        return $"{slug}-{hash}";
    }

    /// <summary>
    /// Type de document = premier segment du chemin (<c>ohada/...</c> → <c>ohada</c>).
    /// Vaut <c>unknown</c> si le blob est à la racine.
    /// </summary>
    public static string ToDocumentType(string blobPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

        var normalized = blobPath.Trim().TrimStart('/');
        var slash = normalized.IndexOf('/');
        return slash > 0 ? normalized[..slash].ToLowerInvariant() : "unknown";
    }

    /// <summary>
    /// Normalise un filtre <c>documentType</c> reçu de l'API pour qu'il corresponde à la
    /// valeur stockée par <see cref="ToDocumentType"/> (minuscules, sans espaces autour) :
    /// « OHADA » ou « Ohada » doivent filtrer comme « ohada ». Vide ou blanc → <c>null</c>
    /// (pas de filtre).
    /// </summary>
    public static string? NormalizeDocumentTypeFilter(string? documentType)
        => string.IsNullOrWhiteSpace(documentType)
            ? null
            : documentType.Trim().ToLowerInvariant();

    /// <summary>
    /// Chemin du blob dans son conteneur à partir de son URL complète
    /// (<c>https://{compte}.blob.core.windows.net/{conteneur}/{chemin}</c> → <c>{chemin}</c>),
    /// décodé (<c>%20</c> → espace, <c>%C3%A9</c> → <c>é</c>) pour que l'identité, le type et le
    /// nom de fichier stockés correspondent au nom réel du blob — comme le fait déjà le
    /// téléchargeur côté worker. Renvoie <c>null</c> si l'URL n'est pas une URL absolue
    /// exploitable ou ne désigne pas un blob (conteneur seul).
    /// </summary>
    public static string? ToBlobPath(string? blobUrl)
    {
        if (string.IsNullOrWhiteSpace(blobUrl)
            || !Uri.TryCreate(blobUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var path = uri.AbsolutePath.TrimStart('/');
        var slash = path.IndexOf('/');
        if (slash < 0 || slash == path.Length - 1)
        {
            return null;
        }

        return Uri.UnescapeDataString(path[(slash + 1)..]);
    }

    /// <summary>Nom de fichier seul (dernier segment du chemin).</summary>
    public static string ToFileName(string blobPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobPath);

        var normalized = blobPath.Trim().TrimEnd('/');
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousDash = false;

        foreach (var c in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                previousDash = false;
            }
            else if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > 80)
        {
            slug = slug[..80].Trim('-');
        }

        return slug.Length == 0 ? "doc" : slug;
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes, 0, 4).ToLowerInvariant();
    }
}
