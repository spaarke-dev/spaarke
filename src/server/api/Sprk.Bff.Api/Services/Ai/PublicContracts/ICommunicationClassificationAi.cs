using Sprk.Bff.Api.Models.Ai.Communication;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade for AI extract+classify over a communication envelope. Given a message subject + plain-text
/// body, returns a <see cref="CommunicationClassificationResult"/> — <c>$choices</c>-constrained candidate
/// record <i>types</i> plus category / urgency / obligations / suggested actions / privilege flag / rationale.
/// </summary>
/// <remarks>
/// <para>
/// Per refined ADR-013 (2026-05-20), external code (here: the Association Engine's rung 5 in
/// <c>Services/Communication</c>) MUST consume AI through this facade rather than injecting
/// <see cref="IOpenAiClient"/> / <see cref="IPlaybookService"/> directly. See ADR-007 for the canonical
/// facade pattern and <see cref="IInvoiceAi"/> / <see cref="IRecordMatchingAi"/> for the sibling facades.
/// </para>
/// <para>
/// <b>Component justification (§11).</b> No existing facade classifies communications for association/triage:
/// <see cref="IInvoiceAi"/> classifies invoice documents (Dataverse-resident playbook + document text),
/// <see cref="IRecordMatchingAi"/> does hybrid record search (no classification). This facade is the ADR-013
/// PublicContracts seam for the new capability; it reuses the existing <see cref="IOpenAiClient"/>
/// structured-completion primitive rather than introducing a new LLM runner.
/// </para>
/// <para>
/// <b>Structured-output, not a Dataverse JPS Action (Path-A deviation).</b> The classification prompt is a
/// self-contained code constant paired with a strict JSON schema, run through
/// <c>IOpenAiClient.GetStructuredCompletionAsync&lt;T&gt;</c> — the same guaranteed-valid-JSON primitive the
/// Finance classification path uses. This works with no Dataverse seeding and is unit-testable. The system
/// prompt MAY later migrate to a Dataverse playbook <c>Description</c> per ADR-014 (follow-up).
/// </para>
/// <para>
/// <b>Privilege is a flag, never a decision (ADR-015).</b> The result may flag privilege; callers surface it
/// to the reviewer and never make a filing/redaction decision on it.
/// </para>
/// </remarks>
public interface ICommunicationClassificationAi
{
    /// <summary>
    /// Classify a communication envelope. Best-effort: returns <c>null</c> when classification is unavailable
    /// (feature disabled) — callers treat a null/empty result as "no signal" (non-fatal, NFR-06).
    /// </summary>
    /// <param name="subject">Message subject (may be empty).</param>
    /// <param name="bodyText">Plain-text message body (may be empty).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The classification result, or <c>null</c> when disabled/unavailable.</returns>
    Task<CommunicationClassificationResult?> ClassifyAsync(
        string subject,
        string bodyText,
        CancellationToken ct = default);
}
