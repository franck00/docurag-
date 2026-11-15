using System.Diagnostics;
using DocuRag.Core.Options;
using DocuRag.Core.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace DocuRag.Core.AI;

public interface ITextEmbedder
{
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken);
}

public interface IChatCompleter
{
    Task<ChatAnswer> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}

public sealed record ChatAnswer(string Text, int PromptTokens, int CompletionTokens);

/// <summary>Embeddings par lots : un appel pour N extraits, pas un appel par extrait.</summary>
public sealed class AzureOpenAIEmbedder(
    EmbeddingClient client,
    IOptions<DocuRagOptions> options,
    ILogger<AzureOpenAIEmbedder> logger) : ITextEmbedder
{
    private readonly OpenAIOptions _options = options.Value.OpenAI;
    private readonly RetryPolicy _retry = RetryPolicy.ForAzureOpenAI(options.Value.OpenAI.MaxRetries, logger);

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
    {
        var results = new List<float[]>(inputs.Count);
        var embeddingOptions = new EmbeddingGenerationOptions { Dimensions = _options.EmbeddingDimensions };
        var batchSize = Math.Max(1, _options.EmbeddingBatchSize);

        foreach (var batch in inputs.Chunk(batchSize))
        {
            var stopwatch = Stopwatch.StartNew();
            var response = await _retry.ExecuteAsync(
                ct => client.GenerateEmbeddingsAsync(batch, embeddingOptions, ct),
                cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var collection = response.Value;
            DocuRagTelemetry.EmbeddingDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("batch_size", batch.Length));
            DocuRagTelemetry.TokensUsed.Add(collection.Usage.InputTokenCount,
                new KeyValuePair<string, object?>("model", _options.EmbeddingDeployment),
                new KeyValuePair<string, object?>("kind", "embedding"));

            results.AddRange(collection.OrderBy(e => e.Index).Select(e => e.ToFloats().ToArray()));
        }

        return results;
    }
}

public sealed class AzureOpenAIChatCompleter(
    ChatClient client,
    IOptions<DocuRagOptions> options,
    ILogger<AzureOpenAIChatCompleter> logger) : IChatCompleter
{
    private readonly DocuRagOptions _options = options.Value;
    private readonly RetryPolicy _retry = RetryPolicy.ForAzureOpenAI(options.Value.OpenAI.MaxRetries, logger);

    public async Task<ChatAnswer> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        ChatMessage[] messages = [new SystemChatMessage(systemPrompt), new UserChatMessage(userPrompt)];
        var completionOptions = new ChatCompletionOptions
        {
            Temperature = _options.Rag.Temperature,
            MaxOutputTokenCount = _options.Rag.MaxOutputTokens,
        };

        var response = await _retry.ExecuteAsync(
            ct => client.CompleteChatAsync(messages, completionOptions, ct),
            cancellationToken).ConfigureAwait(false);

        var completion = response.Value;
        var text = string.Concat(completion.Content.Select(part => part.Text));
        var model = _options.OpenAI.ChatDeployment;
        DocuRagTelemetry.TokensUsed.Add(completion.Usage.InputTokenCount,
            new KeyValuePair<string, object?>("model", model), new KeyValuePair<string, object?>("kind", "prompt"));
        DocuRagTelemetry.TokensUsed.Add(completion.Usage.OutputTokenCount,
            new KeyValuePair<string, object?>("model", model), new KeyValuePair<string, object?>("kind", "completion"));

        return new ChatAnswer(text, completion.Usage.InputTokenCount, completion.Usage.OutputTokenCount);
    }
}
