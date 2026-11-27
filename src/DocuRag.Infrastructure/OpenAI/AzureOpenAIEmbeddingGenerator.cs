using Azure.AI.OpenAI;
using DocuRag.Core.Contracts;
using DocuRag.Infrastructure.Options;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;

namespace DocuRag.Infrastructure.OpenAI;

/// <summary>
/// Génère les embeddings via Azure OpenAI (<c>text-embedding-3-small</c>).
/// Les lots sont envoyés en un seul appel (plusieurs inputs par requête).
/// La résilience au throttling (429) est portée par la politique de retry du client
/// (configurée à l'enregistrement DI, backoff exponentiel).
/// </summary>
public sealed class AzureOpenAIEmbeddingGenerator : IEmbeddingGenerator
{
    private readonly EmbeddingClient _client;
    private readonly int _dimensions;

    public AzureOpenAIEmbeddingGenerator(
        AzureOpenAIClient client, IOptions<AzureOpenAIOptions> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        var o = options.Value;
        _client = client.GetEmbeddingClient(o.EmbeddingDeployment);
        _dimensions = o.EmbeddingDimensions;
    }

    public int Dimensions => _dimensions;

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
        {
            return [];
        }

        var options = new EmbeddingGenerationOptions { Dimensions = _dimensions };
        var response = await _client.GenerateEmbeddingsAsync(inputs, options, cancellationToken);

        var vectors = new float[response.Value.Count][];
        for (var i = 0; i < response.Value.Count; i++)
        {
            vectors[i] = response.Value[i].ToFloats().ToArray();
        }

        return vectors;
    }

    public async Task<float[]> EmbedAsync(string input, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        var batch = await EmbedBatchAsync([input], cancellationToken);
        return batch[0];
    }
}
