using Sprk.Bff.Api.Models.Ai.Communication;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade (ADR-013) for the FR-05 email-triage capability (the <c>TRIAGE-EMAIL</c> catalog
/// Action, <c>sprk_actioncode = "triage-email"</c>, authored by task 022; wired by task 023).
/// Structures the ALREADY-PRODUCED <see cref="Sprk.Bff.Api.Services.Communication.Engine.Rungs.AiClassificationRung"/>
/// classification signal into the five-field triage output — <c>{category, summary, obligations[],
/// priority, reviewOutcome}</c> — that will be persisted on <c>sprk_communication</c> (persistence
/// itself is task 025's job; this facade only PRODUCES the structured result).
/// </summary>
/// <remarks>
/// <para>
/// <b>Facade discipline (ADR-013 / NFR-03).</b> The Communication enrichment path
/// (<see cref="Sprk.Bff.Api.Services.Communication.CommunicationEnrichmentService"/>) MUST consume this
/// capability through THIS facade — never by injecting the Linear AI Consumer primitives
/// (<c>IActionResolver</c>/<c>IActionRunner</c>) or <see cref="IRagService"/> directly into Communication
/// code. Internally, this facade resolves + runs the catalog-authored TRIAGE-EMAIL Action via those SAME
/// Linear AI Consumer primitives <c>MatterPreFillService</c>/<c>ProjectPreFillService</c> use for their own
/// catalog Actions (routed via <c>IConsumerRoutingService</c>/<see cref="ConsumerTypes.EmailTriage"/>), and
/// optionally grounds the run in the matter's own prior correspondence (FR-06).
/// </para>
/// <para>
/// <b>No second full LLM pass (FR-05).</b> This facade NEVER calls <see cref="ICommunicationClassificationAi"/>
/// itself — callers supply the <see cref="Models.Ai.Communication.CommunicationClassificationResult"/>
/// <see cref="Sprk.Bff.Api.Services.Communication.Engine.Rungs.AiClassificationRung"/> already produced
/// (reconstructed from the persisted <c>sprk_associationprovenance</c> signal — see
/// <c>Services/Communication/PersistedClassificationSignalReader.cs</c>). The Action's own prompt reuses
/// that signal (a cheap Fast-tier structuring/mapping pass); this facade adds only routing/grounding/
/// completion plumbing around it — never a second classification.
/// </para>
/// <para>
/// <b>Best-effort (NFR-04).</b> Returns <c>null</c> on any failure (Action not routed, structured-completion
/// failure, grounding failure) — callers treat <c>null</c> as "no triage result" and MUST NOT fail
/// capture/enrichment on it.
/// </para>
/// </remarks>
public interface ICommunicationTriageAi
{
    /// <summary>
    /// Produce the five-field triage structuring of an already-classified communication. Best-effort:
    /// returns <c>null</c> when the Action is not routed, disabled, or the completion fails — never throws
    /// for an expected "no result" outcome.
    /// </summary>
    Task<CommunicationTriageResult?> TriageAsync(
        CommunicationTriageRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// The ALREADY-PRODUCED classification signal + normalized message + optional matter-scoping data the
/// TRIAGE-EMAIL Action structures. <see cref="Classification"/> MUST come from
/// <see cref="Sprk.Bff.Api.Services.Communication.Engine.Rungs.AiClassificationRung"/>'s output — never a
/// fresh classification call (FR-05).
/// </summary>
public sealed record CommunicationTriageRequest
{
    /// <summary>The already-produced classification signal (reused, never re-derived).</summary>
    public required CommunicationClassificationResult Classification { get; init; }

    /// <summary>Normalized message subject (may be empty).</summary>
    public required string Subject { get; init; }

    /// <summary>Normalized message plain-text body (may be empty); bounded to the Action's declared 20000-char input cap.</summary>
    public required string BodyText { get; init; }

    /// <summary>
    /// The communication's resolved regarding matter id, when one has already been associated — used
    /// ONLY to scope the FR-06 prior-correspondence grounding. Null → the run proceeds context-free
    /// (no matter yet associated, or association targeted a non-matter record type).
    /// </summary>
    public Guid? MatterId { get; init; }

    /// <summary>Tenant id for RAG grounding + model deployment resolution.</summary>
    public required string TenantId { get; init; }
}

/// <summary>The FR-05 five-field triage output. All fields are the Action's raw structured-output values
/// (option-set/lookup LABELS, per the Action's <c>$choices</c> contract) — task 025 resolves them to
/// Dataverse values at persistence time.</summary>
public sealed record CommunicationTriageResult(
    string? Category,
    string Summary,
    IReadOnlyList<string> Obligations,
    string? Priority,
    string? ReviewOutcome);
