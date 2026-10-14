using Azure.AI.OpenAI;
using DocuRag.Core.Contracts;
using DocuRag.Infrastructure.Options;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace DocuRag.Infrastructure.OpenAI;

/// <summary>Génère la réponse finale via Azure OpenAI (<c>gpt-4o-mini</c>).</summary>
public sealed class AzureOpenAIChatService : IChatCompletionService
{
    private readonly ChatClient _client;

    public AzureOpenAIChatService(AzureOpenAIClient client, IOptions<AzureOpenAIOptions> options)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client.GetChatClient(options.Value.ChatDeployment);
    }

    public async Task<string> CompleteAsync(
        string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        var messages = new ChatMessage[]
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt),
        };

        // Température basse : on veut une réponse fidèle aux extraits, pas de créativité.
        var options = new ChatCompletionOptions { Temperature = 0.1f };

        var completion = await _client.CompleteChatAsync(messages, options, cancellationToken);
        return completion.Value.Content.Count > 0
            ? completion.Value.Content[0].Text
            : string.Empty;
    }
}
