using System.ComponentModel.DataAnnotations;

namespace DocuRag.Infrastructure.Options;

/// <summary>Configuration Blob Storage. Auth par managed identity (Storage Blob Data Reader).</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Endpoint blob du compte, ex. <c>https://moncompte.blob.core.windows.net/</c>.</summary>
    [Required]
    public string BlobEndpoint { get; set; } = string.Empty;

    /// <summary>Conteneur où arrivent les PDF.</summary>
    public string Container { get; set; } = "documents";
}
