using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration for the hybrid LLM intent reranker
/// (chat-routing-redesign-r1 FR-46, task 111R). Bound to the
/// <c>IntentReranker</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// Per ADR-018 (Typed options) the rerank-LLM tuning knobs used by
/// <see cref="Sprk.Bff.Api.Services.Ai.Chat.IntentRerankerService"/>
/// live in a typed options class so they can be tuned per-environment
/// without a code change (e.g., relaxed timeout in <c>bff-dev</c> for
/// evaluation, tightened timeout in production after telemetry-driven
/// calibration).
/// </para>
/// <para>
/// <b>FR-46 contract</b>: hybrid intent extraction uses
/// <see cref="ModelDeploymentName"/> (default <c>gpt-4o-mini</c>) with
/// structured output (JSON schema) constrained to a top-3 subset of the
/// upstream top-5 candidate list. The <see cref="TimeoutMs"/> hard ceiling
/// (default 800 ms) implements the FR-46 latency budget; on timeout the
/// service falls back to top-3-by-confidence rather than throwing.
/// </para>
/// <para>
/// <b>FR-48 invariant</b>: the rerank-LLM never auto-executes a playbook —
/// the configured options influence only WHICH candidates are surfaced to
/// the user. The downstream <c>playbook_options</c> SSE event (task 117a)
/// is the only path to user confirmation.
/// </para>
/// </remarks>
public class IntentRerankerOptions
{
    /// <summary>
    /// Configuration section name. Used by
    /// <c>configuration.GetSection(IntentRerankerOptions.SectionName)</c>.
    /// </summary>
    public const string SectionName = "IntentReranker";

    /// <summary>
    /// Azure OpenAI deployment name to use for rerank classification. The
    /// hybrid pattern (FR-46) uses gpt-4o-mini for cost + latency reasons —
    /// the input is metadata-only (no file content) so a small model is
    /// sufficient. Note: this option is metadata for telemetry / future
    /// per-task model routing; the actual IChatClient binding is owned by
    /// AiModule and uses <c>AzureOpenAI:ChatModelName</c>. A future enhancement
    /// may inject a separately-keyed IChatClient bound to this deployment.
    /// </summary>
    public string ModelDeploymentName { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Hard timeout in milliseconds for the rerank LLM call (FR-46: ~500–800ms
    /// budget). On timeout the service falls back to top-3-by-confidence with
    /// <c>RerankInvoked=true</c> and <c>Reason="timeout-graceful-degrade"</c>.
    /// </summary>
    [Range(100, 5000, ErrorMessage = "TimeoutMs must be between 100 and 5000.")]
    public int TimeoutMs { get; set; } = 800;

    /// <summary>
    /// LLM sampling temperature. Default 0 for deterministic candidate selection
    /// — the rerank task is essentially a classification problem and benefits
    /// from temperature=0 for repeatability under structured-output constraints.
    /// </summary>
    [Range(0.0, 2.0, ErrorMessage = "Temperature must be between 0.0 and 2.0.")]
    public double Temperature { get; set; } = 0.0;
}
