namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade for external CRUD consumers that need AI text generation for
/// narrative briefings, matter summaries, and similar single-prompt completions.
/// </summary>
/// <remarks>
/// <para>
/// Per refined ADR-013 (2026-05-20), external CRUD code (Workspace, Finance, Jobs,
/// Filters, Endpoints) MUST consume AI through this facade rather than injecting
/// AI-internal types like <see cref="IOpenAiClient"/> directly. This boundary
/// preserves AI-internal flexibility (e.g., model swaps, streaming policy changes)
/// without rippling through CRUD code.
/// </para>
/// <para>
/// Mirrors the canonical facade pattern from ADR-007 (<see cref="SpeFileStore"/>):
/// single concrete class, SDAP-domain shape (no <c>ChatMessage</c> / OpenAI types
/// leaked to callers), narrow surface (only what consumers actually call today).
/// </para>
/// <para>
/// Current consumers (Phase 1 inventory, 2026-05-24):
/// - <c>Services/Workspace/BriefingService.cs</c> — optional AI narrative for the portfolio briefing card
/// - <c>Api/Workspace/WorkspaceMatterEndpoints.cs</c> — SSE matter-summary endpoint
/// </para>
/// </remarks>
public interface IBriefingAi
{
    /// <summary>
    /// Generate a complete narrative response from a single prompt (non-streaming).
    /// Used for short, deterministic narrative enhancement (e.g., portfolio briefings,
    /// daily-briefing summarization, matter AI summary) where the caller assembles the
    /// prompt and consumes the full response as a string.
    /// </summary>
    /// <param name="prompt">The prompt text. The caller is responsible for prompt construction
    /// (the facade does not load playbooks or apply system prompts).</param>
    /// <param name="maxOutputTokens">Optional max-output-tokens cap. Pass <c>null</c> to use
    /// the underlying client default. The daily-briefing consumers pass explicit caps
    /// (300 for summary/channel narration, 500 for TL;DR) to bound response length and cost;
    /// the matter-summary consumer leaves it unset.</param>
    /// <param name="cancellationToken">Cancellation token. Callers typically pair this with
    /// a timeout (e.g., 3 s for the briefing-card path) so that AI failure cannot block CRUD response.</param>
    /// <returns>The generated text response. May be empty if the model produced no output.</returns>
    Task<string> GenerateNarrativeAsync(
        string prompt,
        int? maxOutputTokens = null,
        CancellationToken cancellationToken = default);
}
