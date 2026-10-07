using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Sprk.Bff.Api.Services.Ai.Metering;

/// <summary>
/// The <see cref="AiSpendLimit"/> seam of the <see cref="IChatClient"/> pipeline (task 254). Registered INSIDE
/// <c>UseFunctionInvocation</c>, so each model round-trip of a tool-calling turn is checked before it starts and counted
/// after it ends — the chat agent never reaches <see cref="OpenAiClient"/>, which carries the other seam.
/// </summary>
public sealed class AiSpendLimitChatClient : DelegatingChatClient
{
    private readonly AiSpendLimit _limit;

    public AiSpendLimitChatClient(IChatClient innerClient, AiSpendLimit limit)
        : base(innerClient)
    {
        _limit = limit ?? throw new ArgumentNullException(nameof(limit));
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await _limit.EnsureUnderLimitAsync(cancellationToken).ConfigureAwait(false);
        var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        _limit.RecordUsage(response.Usage?.InputTokenCount ?? 0, response.Usage?.OutputTokenCount ?? 0);
        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await _limit.EnsureUnderLimitAsync(cancellationToken).ConfigureAwait(false);

        // The model reports usage as UsageContent on the final update (the same source ChatEndpoints meters from).
        // Recorded in finally so a stream the caller abandons still counts what it consumed.
        long inputTokens = 0, outputTokens = 0;
        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
            {
                foreach (var content in update.Contents)
                {
                    if (content is UsageContent usage)
                    {
                        inputTokens += usage.Details.InputTokenCount ?? 0;
                        outputTokens += usage.Details.OutputTokenCount ?? 0;
                    }
                }

                yield return update;
            }
        }
        finally
        {
            _limit.RecordUsage(inputTokens, outputTokens);
        }
    }
}
