namespace Sprk.Bff.Api.Services.Ai.Safety;

/// <summary>
/// Detects prompt injection attacks in user messages and retrieved documents (RAG passages)
/// before they reach the orchestrator LLM.
///
/// Uses Azure AI Content Safety Prompt Shields API to identify:
///   - Direct jailbreak attempts embedded in the user turn (userPromptAttack)
///   - Indirect injection attacks embedded in retrieved documents (documentAttack)
///
/// Implementation contract:
///   - MUST complete in under 100ms (P95) to stay within streaming first-token latency budget.
///   - MUST fail-open on service unavailability (HTTP 429, 5xx, timeout): log warning and allow.
///   - MUST NOT log prompt content or document text (ADR-015: Tier 1 log = identifiers + outcome only).
/// </summary>
public interface IPromptShieldService
{
    /// <summary>
    /// Scans the user message and retrieved document passages for prompt injection attacks.
    /// </summary>
    /// <param name="request">
    /// The scan request containing the user message and optionally retrieved document passages.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="PromptShieldResult"/> indicating whether the request is blocked,
    /// the reason for blocking, and OTEL-compatible latency measurement.
    /// </returns>
    Task<PromptShieldResult> ScanAsync(PromptShieldRequest request, CancellationToken ct = default);
}
