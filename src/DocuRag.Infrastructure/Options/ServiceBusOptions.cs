using System.ComponentModel.DataAnnotations;

namespace DocuRag.Infrastructure.Options;

/// <summary>Configuration Service Bus. Auth par managed identity (pas de SAS).</summary>
public sealed class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    /// <summary>Namespace complet, ex. <c>mon-sbus.servicebus.windows.net</c>.</summary>
    [Required]
    public string Namespace { get; set; } = string.Empty;

    [Required]
    public string Queue { get; set; } = "doc-processing";
}
