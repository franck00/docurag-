namespace DocuRag.Core.Documents;

/// <summary>
/// Cycle de vie d'un document depuis son upload jusqu'a son indexation vectorielle.
/// </summary>
public enum DocumentStatus
{
    Pending,
    Processing,
    Indexed,
    Failed
}
