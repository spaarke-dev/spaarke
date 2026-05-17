namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Three-tier capability router (AIPU2-012/013/014).
///
/// Routes an incoming user turn to one or more AI capabilities by applying layers
/// in priority order until a high-confidence result is produced:
///
///   Layer 1 — Keyword classifier (this task, AIPU2-012):
///     Synchronous, zero LLM cost. Scores each <see cref="CapabilityManifestEntry"/>
///     against its <see cref="CapabilityManifestEntry.KeywordHints"/>. If confidence
///     exceeds the configured threshold (default 0.8) the capability is activated
///     immediately without calling Layers 2 or 3.
///
///   Layer 2 — LLM intent classifier (AIPU2-013):
///     Single LLM call to resolve ambiguous turns that Layer 1 could not classify.
///     Only reached when Layer 1 returns <see cref="CapabilityRoutingResult.IsConfident"/> = false.
///
///   Layer 3 — Fallback (AIPU2-014):
///     Safe default selection when Layers 1 and 2 are both inconclusive.
///     Ensures the pipeline always produces a usable result.
///
/// Implementations:
///   - <see cref="CapabilityRouter"/> — production singleton registered in
///     <c>AiCapabilitiesModule.AddAiCapabilitiesModule</c>.
///
/// ADR-015: implementations MUST NOT log or record user turn content; only scores,
/// capability names, layer numbers, and latency are observable.
/// </summary>
public interface ICapabilityRouter
{
    /// <summary>
    /// Runs Layer 1 keyword classification synchronously and returns the result.
    ///
    /// NFR: must complete in under 50ms regardless of manifest size (up to 50 capabilities).
    /// No network I/O, no LLM calls — pure in-memory keyword matching.
    ///
    /// When <see cref="CapabilityRoutingResult.IsConfident"/> is <c>false</c> the caller
    /// is responsible for escalating to Layer 2.
    /// </summary>
    /// <param name="userMessage">
    /// The raw user turn text. Must not be null; may be empty (returns Uncertain).
    /// ADR-015: this value is never logged or stored in OTEL spans by implementations.
    /// </param>
    /// <param name="activePlaybookName">
    /// Optional name of the playbook currently active in the session.
    /// When supplied, capabilities that belong to this playbook receive a bias toward
    /// a lower effective threshold — improving precision for single-playbook sessions.
    /// </param>
    /// <returns>
    /// A <see cref="CapabilityRoutingResult"/> with Layer = 1.
    /// Either <see cref="CapabilityRoutingResult.Confident"/> (activate directly) or
    /// <see cref="CapabilityRoutingResult.Uncertain"/> (escalate to Layer 2).
    /// </returns>
    CapabilityRoutingResult RouteSync(string userMessage, string? activePlaybookName);
}
