using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Production implementation of <see cref="ISummarizationCompressionService"/>.
///
/// Folds the oldest <em>M</em> chat turns into a single System-role summary
/// <see cref="ChatMessage"/> via a single LLM call to the configured (cheap-tier)
/// summarisation deployment. The output is hard-capped at
/// <see cref="SummarizationCompressionOptions.MaxSummaryTokens"/> tokens so it fits
/// within the NFR-10 reserved slot inside the 8K system-prompt budget.
/// </summary>
/// <remarks>
/// <para>
/// <b>R6 Pillar 7 role</b>: this service is the foundation primitive for task 067
/// (hierarchical memory composition). Task 068 wires it into
/// <see cref="Chat.SprkChatAgentFactory"/> as the budget-overflow handler. Until then,
/// it ships as a standalone, fully unit-tested compression unit.
/// </para>
/// <para>
/// <b>ADR-010</b>: registered as <c>AddScoped&lt;ISummarizationCompressionService,
/// SummarizationCompressionService&gt;()</c> inside the existing
/// <see cref="Sprk.Bff.Api.Infrastructure.DI.AnalysisServicesModule"/>. ZERO new
/// Program.cs lines. The interface seam is justified by ADR-010's "interface required
/// for genuine substitution" carve-out — task 068 will choose between a real impl, a
/// kill-switch Null-Object peer, and a unit-test fake; the interface is the canonical
/// substitution point.
/// </para>
/// <para>
/// <b>ADR-013</b>: this service lives entirely inside <see cref="Memory"/>. It depends
/// on <see cref="IOpenAiClient"/> directly because it is itself AI-internal — no
/// PublicContracts facade is needed for AI-internal collaborators per the refined
/// 2026-05-20 ADR-013 boundary rule.
/// </para>
/// <para>
/// <b>ADR-014</b>: no cache; this service is stateless and the caller (task 068) owns
/// the per-tenant session cache (Redis hot tier). Tenant isolation is preserved
/// transitively because the caller passes only that tenant's messages.
/// </para>
/// <para>
/// <b>ADR-015</b>: the returned <see cref="ChatMessage.Content"/> is the
/// LLM-generated summary ONLY — raw user/assistant text from
/// <paramref name="oldestMessages"/> is sent to the LLM as the compression input but is
/// not echoed back into the output. Task 068 is expected to discard the original
/// messages after substituting the returned summary into the rolling context.
/// </para>
/// <para>
/// <b>Soft-failure behaviour</b>: returns <c>null</c> (NOT a thrown exception) when
/// (a) the kill switch is off, (b) the input is empty, (c) the LLM circuit is broken,
/// or (d) any unexpected exception bubbles out of the LLM call. The chat-prompt
/// assembly path treats <c>null</c> as "skip compression — use raw window". This is the
/// canonical P2 Quiet kill-switch posture for memory infrastructure (per the
/// MatterMemoryService precedent in the same Memory tree).
/// </para>
/// </remarks>
public sealed class SummarizationCompressionService : ISummarizationCompressionService
{
    /// <summary>
    /// Absolute floor and ceiling for the per-call <c>maxSummaryTokens</c> argument.
    /// Mirrors <see cref="SummarizationCompressionOptions.MaxSummaryTokens"/> bounds; used
    /// to defensively clamp caller-supplied values before the LLM call.
    /// </summary>
    internal const int MinSummaryTokens = 128;
    internal const int MaxSummaryTokensCeiling = 1024;

    /// <summary>
    /// Defensive minimum input size — compression is skipped when fewer than this many
    /// messages are supplied (no useful summary can be produced from a single turn). The
    /// caller is the authoritative trigger; this is purely a safety net.
    /// </summary>
    internal const int MinMessagesToCompress = 2;

    /// <summary>
    /// Hard ceiling on the size (in characters) of the formatted LLM input. Prevents a
    /// pathological caller from sending a 1 MB conversation to the cheap-tier model and
    /// blowing the per-call budget. Computed as roughly 16K tokens × 4 chars/token.
    /// </summary>
    internal const int MaxInputCharacters = 65_000;

    private readonly IOpenAiClient _openAiClient;
    private readonly SummarizationCompressionOptions _options;
    private readonly ILogger<SummarizationCompressionService> _logger;

    public SummarizationCompressionService(
        IOpenAiClient openAiClient,
        IOptions<SummarizationCompressionOptions> options,
        ILogger<SummarizationCompressionService> logger)
    {
        ArgumentNullException.ThrowIfNull(openAiClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _openAiClient = openAiClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChatMessage?> CompressAsync(
        IReadOnlyList<ChatMessage> oldestMessages,
        int maxSummaryTokens,
        CancellationToken cancellationToken = default)
    {
        // 1. Kill switch (P2 Quiet — no exception; caller short-circuits).
        if (!_options.Enabled)
        {
            _logger.LogDebug(
                "SummarizationCompressionService: kill switch off (SummarizationCompression:Enabled=false); returning null");
            return null;
        }

        // 2. Empty / insufficient input — no useful summary possible.
        if (oldestMessages is null || oldestMessages.Count < MinMessagesToCompress)
        {
            _logger.LogDebug(
                "SummarizationCompressionService: input too small ({Count} < {MinRequired}); returning null",
                oldestMessages?.Count ?? 0, MinMessagesToCompress);
            return null;
        }

        // 3. Clamp the caller-supplied output budget to the hardened bounds. The interface
        // contract documents the [128, 1024] range; we clamp here defensively rather than
        // throwing, because this service is on the chat hot path and a misconfigured caller
        // must not surface as a 500 to the end user.
        var clampedMaxTokens = Math.Clamp(maxSummaryTokens, MinSummaryTokens, MaxSummaryTokensCeiling);
        if (clampedMaxTokens != maxSummaryTokens)
        {
            _logger.LogWarning(
                "SummarizationCompressionService: caller-supplied maxSummaryTokens={Requested} " +
                "outside [{Min}, {Max}]; clamped to {Clamped}",
                maxSummaryTokens, MinSummaryTokens, MaxSummaryTokensCeiling, clampedMaxTokens);
        }

        // 4. Build the LLM input. Truncate at the input ceiling to protect against
        // pathological callers feeding the entire 1MB conversation buffer.
        var inputText = FormatMessagesForLlm(oldestMessages);
        if (inputText.Length > MaxInputCharacters)
        {
            _logger.LogWarning(
                "SummarizationCompressionService: input text {Length} chars exceeds {Max} chars cap; truncating",
                inputText.Length, MaxInputCharacters);
            inputText = inputText[..MaxInputCharacters];
        }

        if (inputText.Length == 0)
        {
            _logger.LogDebug(
                "SummarizationCompressionService: formatted input is empty (all messages had empty Content); returning null");
            return null;
        }

        var prompt = BuildSummarizationPrompt(inputText);

        // 5. Make the LLM call. Any failure (circuit broken, transient API error,
        // cancellation) returns null per the soft-failure contract. Cancellation is
        // re-raised so the caller can stop the entire chat turn cleanly.
        string llmResponse;
        try
        {
            llmResponse = await _openAiClient.GetCompletionAsync(
                prompt: prompt,
                model: _options.ModelDeploymentOverride,
                maxOutputTokens: clampedMaxTokens,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Propagate cancellation — the entire chat turn is being torn down.
            throw;
        }
        catch (OpenAiCircuitBrokenException ex)
        {
            _logger.LogWarning(ex,
                "SummarizationCompressionService: OpenAI circuit broken; returning null and letting caller short-circuit");
            return null;
        }
        catch (Exception ex)
        {
            // P2 Quiet — log and degrade. Compression is enhancement; raw window still works.
            _logger.LogWarning(ex,
                "SummarizationCompressionService: LLM call failed; returning null and degrading to raw window");
            return null;
        }

        // 6. Defensive output truncation. The model occasionally exceeds the
        // max-output-tokens hint by a few tokens; we trim at clamped budget × charsPerToken
        // to guarantee the NFR-10 reserved slot is respected.
        var outputTokenBudgetChars = (int)Math.Ceiling(clampedMaxTokens * _options.CharsPerToken);
        var trimmed = (llmResponse ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            _logger.LogWarning(
                "SummarizationCompressionService: LLM returned empty response; returning null");
            return null;
        }

        if (trimmed.Length > outputTokenBudgetChars)
        {
            _logger.LogDebug(
                "SummarizationCompressionService: LLM response {Length} chars exceeds budget {Budget} chars; trimming",
                trimmed.Length, outputTokenBudgetChars);
            trimmed = trimmed[..outputTokenBudgetChars];
        }

        // 7. Wrap as a System-role ChatMessage. The leading prefix is canonical so the
        // downstream prompt-assembly path can recognise compressed slots if needed.
        const string SummaryPrefix = "Summary of earlier conversation: ";
        var content = trimmed.StartsWith(SummaryPrefix, StringComparison.Ordinal)
            ? trimmed
            : SummaryPrefix + trimmed;

        var firstSessionId = oldestMessages[0].SessionId;
        return new ChatMessage(
            MessageId: $"summary-{Guid.NewGuid():N}",
            SessionId: firstSessionId,
            Role: ChatMessageRole.System,
            Content: content,
            TokenCount: EstimateTokenCount(content),
            CreatedAt: DateTimeOffset.UtcNow,
            SequenceNumber: 0);
    }

    // =========================================================================
    // Internal helpers (internal for unit test access)
    // =========================================================================

    /// <summary>
    /// Formats the input messages into a single LLM-friendly transcript. Role labels are
    /// rendered in plain English (User / Assistant / System); message ordering is preserved.
    /// Empty-content messages are skipped to avoid wasting input tokens.
    /// </summary>
    internal static string FormatMessagesForLlm(IReadOnlyList<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (var msg in messages)
        {
            if (string.IsNullOrWhiteSpace(msg.Content))
            {
                continue;
            }

            var roleLabel = msg.Role switch
            {
                ChatMessageRole.User => "User",
                ChatMessageRole.Assistant => "Assistant",
                ChatMessageRole.System => "System",
                _ => "Unknown"
            };

            sb.Append(roleLabel).Append(": ").AppendLine(msg.Content.Trim());
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Builds the summarisation prompt sent to the LLM. The prompt is intentionally
    /// concise: the cheap-tier model produces a tighter summary when the instructions
    /// are short. The prompt asks for a factual, third-person summary suitable for
    /// rolling into the system context of the next turn.
    /// </summary>
    internal static string BuildSummarizationPrompt(string transcript)
    {
        return $$"""
            You are a chat-history compression assistant. Summarise the following conversation
            excerpt into a brief, factual third-person summary. Capture: who is asking what, key
            facts established, decisions made, and any open questions. Omit greetings, filler,
            and conversational small-talk. The summary will be injected into the system context
            of a subsequent assistant turn — write it accordingly.

            Output ONLY the summary text. Do not include preamble, markdown headers, or commentary.

            Conversation excerpt:
            {{transcript}}
            """;
    }

    /// <summary>
    /// Conservative token estimate (4 chars per token, matches GPT-4o English-prose tokenisation).
    /// Used for the <see cref="ChatMessage.TokenCount"/> field on the returned summary so
    /// downstream budget arithmetic has an accurate accounting.
    /// </summary>
    private int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return (int)Math.Ceiling(text.Length / _options.CharsPerToken);
    }
}
