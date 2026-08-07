namespace Sprk.Bff.Api.Services.Communication.Models;

/// <summary>
/// The <see cref="QueueFeedItem.Kind"/> discriminator values for the FR-17 queue-feed (task 032; create-task
/// added task 034 / FR-D5).
/// </summary>
public static class QueueFeedItemKinds
{
    /// <summary>A communication whose association still needs a human decision — <see cref="Engine.AssociationStatusCodes"/>
    /// PendingReview / Unresolved / Suggested / Ambiguous (anything other than Resolved).</summary>
    public const string AssociationException = "association-exception";

    /// <summary>An OPEN pending Job B field-update proposal (task 030's <c>sprk_emailreviewlog</c> <c>Proposed</c>
    /// row with no later terminal row for the same (communication, target entity, target field)).</summary>
    public const string PendingProposal = "pending-proposal";

    /// <summary>An OPEN pending Job C create-task proposal (task 040's deadline-bearing <c>sprk_emailreviewlog</c>
    /// <c>Proposed</c> row, keyed by the <c>__create_task__:</c> sentinel <c>sprk_targetfield</c>, with no later
    /// terminal row). The task the reconcile tab (task 056 / FR-E5) will create on Approve — POSTed to the create-task
    /// apply endpoint (task 034). The <see cref="QueueFeedItem"/> Task* group carries the extracted
    /// name/description/due-date; base-date/final-due-date/assigned-to/status/completed-date are null in the feed and
    /// supplied by the human at apply time.</summary>
    public const string CreateTask = "create-task";
}

/// <summary>
/// One item in the FR-17 ranked-exceptions queue-feed (task 032) — the surface-agnostic contract r1 supplies and
/// r5 renders on BOTH surfaces (Code Page + SpaarkeAi widget) without a fork (D-10). <see cref="Kind"/>
/// (<see cref="QueueFeedItemKinds"/>) discriminates which optional group of fields is populated:
/// <list type="bullet">
///   <item><b><c>association-exception</c></b> — <see cref="AssociationStatus"/> is populated (the engine's
///   current ladder status — Suggested/Ambiguous/Pending Review/Unresolved); the proposal-only fields
///   (<see cref="ReviewLogId"/>, <see cref="TargetField"/>, etc.) are null.</item>
///   <item><b><c>pending-proposal</c></b> — the proposal fields (target entity/field, old→new value, citation,
///   confidence, allow-list confirm flag) are populated from task 030's stored <c>sprk_emailreviewlog</c>
///   <c>Proposed</c> row; <see cref="AssociationStatus"/> is null.</item>
/// </list>
/// <para>
/// <b>Ranking (D-08 — no second scoring scheme):</b> <see cref="TriagePriority"/> and <see cref="RiConfidence"/>
/// are ALWAYS the parent communication's already-computed values (tasks 024/025) — a <c>pending-proposal</c> item
/// never carries a competing rank derived from its own <see cref="ProposalConfidence"/> (that field is exposed for
/// display only, e.g. a confidence badge on the confirm card).
/// </para>
/// <para>
/// <b>No over-disclosure:</b> every item in the feed was produced from a communication the caller could already
/// see (impersonated read + the shared <see cref="Access.ICommunicationAccessFilter"/>) — a communication (or a
/// proposal on it) the caller may not read is simply absent from this feed, never returned redacted.
/// </para>
/// </summary>
/// <param name="CommunicationId">The communication the exception/proposal is about.</param>
/// <param name="Kind"><see cref="QueueFeedItemKinds"/> discriminator.</param>
/// <param name="Subject">The communication's <c>sprk_subject</c>; null when unset.</param>
/// <param name="From">The communication's <c>sprk_from</c>; null when unset.</param>
/// <param name="SentAt">The communication's <c>sprk_sentat</c>; null when unset.</param>
/// <param name="TriagePriority">The communication's <c>sprk_triagepriority</c> CHOICE integer (Urgent=100000000 …
/// Low=100000003); null when untriaged. Reused for ranking — never recomputed here (D-08).</param>
/// <param name="RiConfidence">The communication's <c>sprk_riconfidence</c> (tasks 024/025); null when not yet
/// computed. Reused for ranking — never recomputed here (D-08).</param>
/// <param name="AssociationStatus">Populated for <see cref="QueueFeedItemKinds.AssociationException"/> — the
/// engine's current ladder status name (<see cref="Engine.AssociationStatusCodes.Name"/>); null for a
/// pending-proposal item.</param>
/// <param name="ReviewLogId">Populated for <see cref="QueueFeedItemKinds.PendingProposal"/> — the source
/// <c>sprk_emailreviewlog</c> row id (the apply endpoint's target row); null for an association-exception item.</param>
/// <param name="TargetEntity">Proposal target entity logical name (<c>sprk_targetentity</c>); null for an
/// association-exception item.</param>
/// <param name="TargetRecordId">Proposal target record id (<c>sprk_targetrecordid</c>); null for an
/// association-exception item.</param>
/// <param name="TargetField">Proposal target field logical name (<c>sprk_targetfield</c>, allow-listed per
/// <c>sprk_emailupdatefield</c>); null for an association-exception item.</param>
/// <param name="FieldType">Proposal field type hint (e.g. DateTime/Text/Number) from the stored suggestion JSON;
/// null otherwise.</param>
/// <param name="OldValue">Proposal current (old) value at proposal time; null otherwise.</param>
/// <param name="NewValue">Proposal candidate (new) value; null otherwise.</param>
/// <param name="ProposalConfidence">Proposal extraction confidence (task 030's own per-candidate confidence) —
/// DISPLAY ONLY, never used for ranking (D-08). Null for an association-exception item.</param>
/// <param name="CitationSource">Citation source (e.g. "body" or an attachment file name); null otherwise.</param>
/// <param name="CitationLocator">Citation locator (e.g. "body: sentence 1" or "OA_908068.pdf p.1"); null otherwise.</param>
/// <param name="CitationQuotedText">The cited quoted text the proposal was extracted from (NFR-06 verify-cited-text);
/// null otherwise.</param>
/// <param name="Reason">Human-readable reason the Action gave for the proposal; null otherwise.</param>
/// <param name="RequireConfirm">Whether the allow-list row requires explicit human confirm before apply; null for
/// an association-exception item.</param>
/// <param name="PrivilegeFlagged">ADR-015 — carried as a flag only, never decided/suppressed by this feed; null
/// otherwise.</param>
/// <param name="TaskName">Populated for <see cref="QueueFeedItemKinds.CreateTask"/> — the extracted follow-up task
/// subject (Job C candidate <c>subject</c>); null for other kinds. For a create-task item the regarding record is
/// carried in <see cref="TargetEntity"/> + <see cref="TargetRecordId"/> (the confirmed association the task attaches
/// to, NFR-10) and the citation in the Citation* fields.</param>
/// <param name="TaskDescription">Populated for <see cref="QueueFeedItemKinds.CreateTask"/> — the extracted task
/// description; null for other kinds.</param>
/// <param name="TaskBaseDate">Create-task FR-E5 field. NOT extracted by Job C — null in the feed; the human supplies
/// it at apply time (task 056). Exposed here so the reconcile tab binds the full FR-E5 field set on one shape.</param>
/// <param name="TaskDueDate">Populated for <see cref="QueueFeedItemKinds.CreateTask"/> — the extracted deadline
/// (Job C candidate <c>dueDate</c>, the field that made this candidate deadline-bearing and thus human-confirm); null
/// for other kinds.</param>
/// <param name="TaskFinalDueDate">Create-task FR-E5 field. NOT extracted — null in the feed; human-supplied at apply.</param>
/// <param name="TaskAssignedTo">Create-task FR-E5 field (the task Owner / <c>ownerid</c> systemuser). NOT extracted —
/// null in the feed; human-supplied at apply.</param>
/// <param name="TaskStatus">Create-task FR-E5 field (<c>sprk_eventstatus</c> Choice value, e.g. Completed=2 for the
/// create-and-complete case). NOT extracted — null in the feed; human-supplied at apply.</param>
/// <param name="TaskCompletedDate">Create-task FR-E5 field. NOT extracted — null in the feed; human-supplied at apply.</param>
public sealed record QueueFeedItem(
    Guid CommunicationId,
    string Kind,
    string? Subject,
    string? From,
    DateTimeOffset? SentAt,
    int? TriagePriority,
    double? RiConfidence,
    string? AssociationStatus,
    Guid? ReviewLogId,
    string? TargetEntity,
    string? TargetRecordId,
    string? TargetField,
    string? FieldType,
    string? OldValue,
    string? NewValue,
    double? ProposalConfidence,
    string? CitationSource,
    string? CitationLocator,
    string? CitationQuotedText,
    string? Reason,
    bool? RequireConfirm,
    bool? PrivilegeFlagged,
    // ── Create-task (task 034 / FR-D5) group — populated only for QueueFeedItemKinds.CreateTask ──
    string? TaskName = null,
    string? TaskDescription = null,
    DateOnly? TaskBaseDate = null,
    DateOnly? TaskDueDate = null,
    DateOnly? TaskFinalDueDate = null,
    Guid? TaskAssignedTo = null,
    int? TaskStatus = null,
    DateOnly? TaskCompletedDate = null);

/// <summary>
/// FR-17 queue-feed result (task 032): the caller's ranked exceptions for the requested scope, already ranked by
/// the existing triage priority + RI-confidence (ascending priority urgency, then descending RI-confidence, then
/// descending recency) and capped at the requested/default page size. <see cref="Count"/> == <see cref="Items"/>.Count.
/// </summary>
public sealed record QueueFeedResult(
    IReadOnlyList<QueueFeedItem> Items,
    int Count);
