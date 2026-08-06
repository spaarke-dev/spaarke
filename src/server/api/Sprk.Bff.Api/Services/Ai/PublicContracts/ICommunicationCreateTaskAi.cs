namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade (ADR-013) for the FR-14 Job C "create-task" extraction capability (the
/// <c>CREATE-TASK-FROM-EMAIL</c> catalog Action, <c>sprk_actioncode = "create-task-from-email"</c>,
/// authored + wired by task 040). Given the already-produced triage output for an email (grounding
/// only) plus the email/attachment text and the entity type of the record the email is associated to
/// (task 020), produces zero-or-more CANDIDATE follow-up tasks/events — each carrying a subject,
/// description, an optional due date, a VERBATIM citation, a plain-language reason, and a confidence.
/// </summary>
/// <remarks>
/// <para>
/// <b>Facade discipline (ADR-013 / NFR-03).</b> The Communication enrichment path
/// (<see cref="Sprk.Bff.Api.Services.Communication.CommunicationEnrichmentService"/>) MUST consume this
/// capability through THIS facade — never by injecting the Linear AI Consumer primitives
/// (<c>IActionResolver</c>/<c>IActionRunner</c>) or <c>IOpenAiClient</c>/<c>IPlaybookService</c> directly
/// into Communication code. Internally, this facade resolves + runs the catalog-authored
/// CREATE-TASK-FROM-EMAIL Action via those SAME Linear AI Consumer primitives
/// <c>CommunicationTriageAi</c>/<c>CommunicationProposeAi</c> already use (routed via
/// <c>IConsumerRoutingService</c>/<see cref="ConsumerTypes.EmailCreateTask"/>).
/// </para>
/// <para>
/// <b>No second classification pass (FR-05/FR-14).</b> This facade has NO dependency on
/// <see cref="ICommunicationClassificationAi"/>/<c>IOpenAiClient</c> — it is STRUCTURALLY incapable of a
/// second classification. The already-produced triage output is passed IN as optional grounding
/// (<see cref="CommunicationCreateTaskRequest.Triage"/>), never re-derived. This is Job C's own single,
/// targeted "does this email imply work" extraction — the same precedent as Job B's propose Action, not a
/// redundant analysis.
/// </para>
/// <para>
/// <b>Trust boundary (NFR-06 / ADR-015).</b> This facade PRODUCES candidate tasks only — it NEVER creates
/// or proposes a Dataverse record itself. The verify-cited-text check, the deadline-bearing → human-confirm
/// gate, and the actual create (via <see cref="IActionSeam.CreateTaskAsync"/>) or PENDING store are the
/// CALLER's responsibility (task 040's enrichment step) — the model is instructed to cite verbatim and to
/// report a due date only when one is concretely stated, but code is the enforcement.
/// </para>
/// <para>
/// <b>Best-effort (NFR-04).</b> Returns <c>null</c> on any failure (Action not routed, completion failure,
/// malformed output) — callers treat <c>null</c> as "no candidate tasks" and MUST NOT fail
/// capture/enrichment.
/// </para>
/// </remarks>
public interface ICommunicationCreateTaskAi
{
    /// <summary>
    /// Produce candidate follow-up tasks/events implied by the email. Best-effort: returns <c>null</c> when
    /// the Action is not routed/disabled or the completion fails — never throws for an expected "no result"
    /// outcome. An empty (non-null) list means the model found no implied follow-up work.
    /// </summary>
    Task<IReadOnlyList<TaskCandidate>?> ExtractAsync(
        CommunicationCreateTaskRequest request,
        CancellationToken ct = default);
}

/// <summary>The already-produced triage grounding + the source text + the associated record's entity type
/// the CREATE-TASK-FROM-EMAIL Action extracts over. <see cref="Triage"/> is grounding only — never
/// re-classified.</summary>
public sealed record CommunicationCreateTaskRequest
{
    /// <summary>The associated record's entity logical name (context only — e.g. <c>sprk_matter</c>). The
    /// record the email is associated to, from the identifier rung (task 020).</summary>
    public required string EntityLogicalName { get; init; }

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

/// <summary>A single RAW candidate follow-up task/event from the CREATE-TASK-FROM-EMAIL Action's structured
/// output — BEFORE the caller's verify-cited-text check and the deadline-bearing → confirm gate.</summary>
public sealed record TaskCandidate(
    string Subject,
    string Description,
    DateTime? DueDate,
    ProposalCitation Citation,
    string Reason,
    double Confidence);
