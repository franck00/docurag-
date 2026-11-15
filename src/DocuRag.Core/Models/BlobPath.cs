using System.Security.Cryptography;
using System.Text;

namespace DocuRag.Core.Models;

/// <summary>
/// Découpe une URL de blob en (conteneur, nom) et en dérive les identifiants métier.
/// Convention : le premier dossier du blob donne le type de document
/// (<c>documents/ohada/acte-uniforme.pdf</c> → <c>ohada</c>), sinon <c>general</c>.
/// </summary>
public sealed record BlobPath(string Container, string BlobName)
{
    public const string DefaultDocType = "general";

    public string FileName => BlobName[(BlobName.LastIndexOf('/') + 1)..];

    public bool IsPdf => BlobName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    public string DocType
    {
        get
        {
            var slash = BlobName.IndexOf('/');
            if (slash <= 0)
            {
                return DefaultDocType;
            }

            var folder = BlobName[..slash].Trim().ToLowerInvariant();
            return folder.Length == 0 ? DefaultDocType : folder;
        }
    }

    /// <summary>
    /// Id déterministe : même blob → même id. C'est la base de l'idempotence (un ré-upload ou un message
    /// rejoué réécrit le même document au lieu d'en créer un second).
    /// </summary>
    public string DocumentId
    {
        get
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{Container}/{BlobName}"));
            return Convert.ToHexStringLower(hash)[..32];
        }
    }

    public static bool TryParse(string? url, out BlobPath? path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var segments = uri.AbsolutePath.TrimStart('/').Split('/', 2);

        // Azurite (émulateur local) utilise le style "path" : /devstoreaccount1/<conteneur>/<blob>
        if (uri.IsLoopback && segments.Length == 2)
        {
            segments = segments[1].Split('/', 2);
        }

        if (segments.Length != 2 || segments[0].Length == 0 || segments[1].Length == 0)
        {
            return false;
        }

        path = new BlobPath(Uri.UnescapeDataString(segments[0]), Uri.UnescapeDataString(segments[1]));
        return true;
    }
}
