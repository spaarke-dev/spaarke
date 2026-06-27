namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Builds the two-layer system prompt sent to the orchestrator LLM on each turn.
///
/// The prompt is split into:
///   1. A <em>stable prefix</em> (~2000 tokens) that is byte-identical for all turns
///      sharing the same playbook context. This enables Azure OpenAI prompt caching
///      (same token sequence = KV cache reuse = lower cost and latency).
///   2. A <em>per-turn suffix</em> (~0–3000 tokens) containing the tool schemas
///      selected by the per-playbook tool-filter for the current turn.
///
/// Total budget: 9000 tokens. When prefix + suffix would exceed this budget the builder
/// automatically trims the tool index descriptions and reduces the tool cap.
///
/// Implementations must be thread-safe (singleton lifetime).
/// </summary>
public interface IOrchestratorPromptBuilder
{
    /// <summary>
    /// Builds the orchestrator system prompt for a single conversational turn.
    /// </summary>
    /// <param name="activeToolNames">
    /// Ordered list of tool names that are active for this turn. Derived from per-playbook
    /// tool filtering (FR-23): matched-playbook tools + always-on conversational set, or
    /// the always-on set alone when no playbook is matched. Pass <see cref="Array.Empty{T}"/>
    /// when no tools are available.
    /// </param>
    /// <param name="context">
    /// Session-level context (user, tenant, matter name, playbook name) used to personalise
    /// the stable prefix.
    /// </param>
    /// <returns>
    /// An <see cref="OrchestratorPrompt"/> with a stable prefix, a per-turn suffix,
    /// the list of tool schema names injected, and a token budget estimate.
    /// </returns>
    OrchestratorPrompt BuildSystemPrompt(
        IReadOnlyList<string> activeToolNames,
        OrchestratorPromptContext context);
}
