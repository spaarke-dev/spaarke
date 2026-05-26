using Sprk.Bff.Api.Services.Ai.Capabilities;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Builds the two-layer system prompt sent to the orchestrator LLM on each turn.
///
/// The prompt is split into:
///   1. A <em>stable prefix</em> (~2000 tokens) that is byte-identical for all turns
///      sharing the same manifest state. This enables Azure OpenAI prompt caching
///      (same token sequence = KV cache reuse = lower cost and latency).
///   2. A <em>per-turn suffix</em> (~0–3000 tokens) containing the tool schemas
///      selected by the capability router for the current turn.
///
/// Total budget: 9000 tokens. When prefix + suffix would exceed this budget the builder
/// automatically trims capability index descriptions and reduces the tool cap.
///
/// Implementations must be thread-safe (singleton lifetime).
/// </summary>
public interface IOrchestratorPromptBuilder
{
    /// <summary>
    /// Builds the orchestrator system prompt for a single conversational turn.
    /// </summary>
    /// <param name="routing">
    /// Result from the capability router identifying which capabilities (and therefore
    /// tools) are active for this turn. Use <see cref="CapabilityRoutingResult.Fallback"/>
    /// when no routing result is available.
    /// </param>
    /// <param name="context">
    /// Session-level context (user, tenant, matter name) used to personalise the
    /// stable prefix. Context fields that change per-turn belong in <paramref name="routing"/>.
    /// </param>
    /// <returns>
    /// An <see cref="OrchestratorPrompt"/> with a stable prefix, a per-turn suffix,
    /// the list of tool schema names injected, and a token budget estimate.
    /// </returns>
    OrchestratorPrompt BuildSystemPrompt(
        CapabilityRoutingResult routing,
        OrchestratorPromptContext context);
}
