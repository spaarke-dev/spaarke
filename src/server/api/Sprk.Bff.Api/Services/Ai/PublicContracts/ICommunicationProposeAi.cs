namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade (ADR-013) for the FR-09 Job B "propose" capability (the <c>PROPOSE-FIELD-UPDATES</c>
/// catalog Action, <c>sprk_actioncode = "propose-field-updates"</c>, authored + wired by task 030).
/// Given the operator-owned allow-list fields for the record an email is associated to (plus each field's
/// extraction guidance and the email/attachment text, grounded in the already-produced triage output),
/// produces zero-or-more CANDIDATE field-update proposals — each carrying the target field, a proposed new
/// value, a VERBATIM citation, a plain-language reason, and a confidence.
/// </summary>
/// <remarks>
/// <para>
/// <b>Facade discipline (ADR-013 / NFR-03).</b> The Communication enrichment path
/// (<see cref="Sprk.Bff.Api.Services.Communication.CommunicationEnrichmentService"/>) MUST consume this
/// capability through THIS facade — never by injecting the Linear AI Consumer primitives
/// (<c>IActionResolver</c>/<c>IActionRunner</c>) or <c>IOpenAiClient</c>/<c>IPlaybookService</c> directly
/// into Communication code. Internally, this facade resolves + runs the catalog-authored
/// PROPOSE-FIELD-UPDATES Action via those SAME Linear AI Consumer primitives
/// <c>MatterPreFillService</c>/<c>CommunicationTriageAi</c> already use (routed via
/// <c>IConsumerRoutingService</c>/<see cref="ConsumerTypes.EmailPropose"/>).
/// </para>
/// <para>
/// <b>No second classification pass (FR-05/FR-09).</b> This facade has NO dependency on
/// <see cref="ICommunicationClassificationAi"/>/<c>IOpenAiClient</c> — it is STRUCTURALLY incapable of a
/// second classification. The already-produced triage output is passed IN as optional grounding
/// (<see cref="CommunicationProposeRequest.Triage"/>), never re-derived. This is Job B's own single,
/// targeted field-value extraction — the essence of the propose path, not a redundant analysis.
/// </para>
/// <para>
/// <b>Trust boundary (NFR-06 / ADR-015).</b> This facade PRODUCES candidate proposals only. The
/// allow-list re-check, field-type coercion, old-value read, VERIFY-CITED-TEXT drop, no-op skip, and the
/// PENDING (never applied) store are the CALLER's responsibility (task 030's enrichment step) — the model
/// is instructed to cite verbatim, but code is the enforcement.
/// </para>
/// <para>
/// <b>Best-effort (NFR-04).</b> Returns <c>null</c> on any failure (Action not routed, completion failure,
/// malformed output) — callers treat <c>null</c> as "no proposals" and MUST NOT fail capture/enrichment.
/// </para>
/// </remarks>
public interface ICommunicationProposeAi
{
    /// <summary>
    /// Produce candidate field-update proposals for the supplied allow-listed fields. Best-effort: returns
    /// <c>null</c> when the Action is not routed/disabled or the completion fails — never throws for an
    /// expected "no result" outcome. An empty (non-null) list means the model found no proposable value.
    /// </summary>
    Task<IReadOnlyList<ProposedFieldCandidate>?> ProposeAsync(
        CommunicationProposeRequest request,
        CancellationToken ct = default);
}

/// <summary>One allow-listed field the propose Action may extract a value for (from an enabled
/// <c>sprk_emailupdatefield</c> row).</summary>
public sealed record ProposableField
{
    /// <summary>The target field logical name (from <c>sprk_emailupdatefield.sprk_targetfieldlogicalname</c>).</summary>
    public required string FieldLogicalName { get; init; }

    /// <summary>The coercion-hint field type label (Text/Lookup/OptionSet/Number/DateTime/Boolean/Memo/Currency),
    /// mapped from <c>sprk_emailupdatefield.sprk_fieldtype</c>.</summary>
    public required string FieldType { get; init; }

    /// <summary>Optional extraction guidance (<c>sprk_emailupdatefield.sprk_extractionguidance</c>).</summary>
    public string? ExtractionGuidance { get; init; }
}

/// <summary>The already-produced triage grounding + the source text + the allow-listed fields the
/// PROPOSE-FIELD-UPDATES Action extracts over. <see cref="Triage"/> is grounding only — never re-classified.</summary>
public sealed record CommunicationProposeRequest
{
    /// <summary>The target record's entity logical name (context only).</summary>
    public required string EntityLogicalName { get; init; }

    /// <summary>The enabled allow-list fields (the SOLE gate — a field not here is never proposable).</summary>
    public required IReadOnlyList<ProposableField> Fields { get; init; }

    /// <summary>The already-produced triage output for this email — grounding context only (never re-derived).</summary>
    public CommunicationTriageResult? Triage { get; init; }

    /// <summary>Normalized message subject (may be empty).</summary>
    public required string Subject { get; init; }

    /// <summary>Normalized message plain-text body (may be empty); bounded to the Action's declared input cap.</summary>
    public required string BodyText { get; init; }

    /// <summary>Extracted attachment text when present (bounded); null when there are no attachments.</summary>
    public string? AttachmentText { get; init; }

    /// <summary>Tenant id for model deployment resolution.</summary>
    public required string TenantId { get; init; }
}

/// <summary>A single RAW candidate proposal from the PROPOSE-FIELD-UPDATES Action's structured output —
/// BEFORE the caller's allow-list re-check, coercion, old-value read, and verify-cited-text drop.</summary>
public sealed record ProposedFieldCandidate(
    string Field,
    string NewValue,
    ProposalCitation Citation,
    string Reason,
    double Confidence);

/// <summary>The citation attached to a candidate proposal (NFR-06). <see cref="QuotedText"/> MUST be a
/// verbatim excerpt of the source — the caller re-locates it and drops the proposal if it cannot be found.</summary>
public sealed record ProposalCitation(
    string? Source,
    string? Locator,
    string QuotedText);
