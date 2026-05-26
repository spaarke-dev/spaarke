namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Immutable result produced by <see cref="IOrchestratorPromptBuilder.BuildSystemPrompt"/>.
///
/// The prompt is split into two parts to enable Azure OpenAI prefix caching:
///
///   <see cref="SystemPromptPrefix"/> — stable across all turns that share the same
///   manifest state. Identical byte sequences allow the Azure OpenAI service to reuse
///   the KV cache, reducing cost and latency on subsequent turns.
///
///   <see cref="PerTurnSuffix"/> — varies each turn; contains the selected tool schemas
///   serialised as a JSON block. Never cached.
///
/// Consumers concatenate prefix + suffix to form the complete system prompt string,
/// then pass <see cref="ToolSchemaNames"/> to the chat client for function-calling.
///
/// Thread-safety: record is immutable; safe to share across threads.
/// </summary>
/// <param name="SystemPromptPrefix">
/// Stable prefix (~2000 tokens). Contains: persona, capability index, standing
/// instructions, entity enrichment (if any). Byte-identical for all turns that
/// share the same manifest hash.
/// </param>
/// <param name="PerTurnSuffix">
/// Per-turn suffix (~0–3000 tokens). Contains: serialised tool schema definitions
/// for the tools selected by the capability router (6–8 tools max).
/// Empty string when no tools were selected (unusual but handled gracefully).
/// </param>
/// <param name="ToolSchemaNames">
/// Ordered list of tool names whose schemas appear in <see cref="PerTurnSuffix"/>.
/// The chat client uses this list to enable the correct function definitions.
/// </param>
/// <param name="EstimatedTokens">
/// Rough total token estimate for prefix + suffix combined (chars / 4 heuristic).
/// Callers may use this for telemetry; it is not a contract.
/// </param>
/// <param name="PrefixCacheHit">
/// <c>true</c> when the prefix was served from the in-process cache keyed by
/// manifest hash; <c>false</c> when it was freshly computed. Used for telemetry.
/// </param>
public sealed record OrchestratorPrompt(
    string SystemPromptPrefix,
    string PerTurnSuffix,
    IReadOnlyList<string> ToolSchemaNames,
    int EstimatedTokens,
    bool PrefixCacheHit)
{
    /// <summary>
    /// Concatenates <see cref="SystemPromptPrefix"/> and <see cref="PerTurnSuffix"/>
    /// into the full system prompt string ready to send to the LLM.
    /// </summary>
    public string FullSystemPrompt =>
        string.IsNullOrEmpty(PerTurnSuffix)
            ? SystemPromptPrefix
            : SystemPromptPrefix + "\n\n" + PerTurnSuffix;
}
