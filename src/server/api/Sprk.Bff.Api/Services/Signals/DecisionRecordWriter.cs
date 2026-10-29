using System.Collections.Frozen;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Services.Workspace;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>The <c>sprk_decisionrecord.sprk_recordclass</c> option-set values (live schema, spaarkedev1 2026-10-10).</summary>
public enum RecordedDecisionClass
{
    Judgement = 100000000,
    Routine = 100000001,
    Dismissal = 100000002,
}

/// <summary>The core record a Decision Record is about: the SAME one its Signal groups under (D-34, D-36).</summary>
/// <param name="EntityLogicalName">One of <see cref="CoreAncestorResolver.CoreRecordEntities"/>.</param>
/// <param name="RecordId">The core record's id (copied to <c>sprk_corerecordid</c>).</param>
/// <param name="RecordTypeRefId">The <c>sprk_recordtype_ref</c> row the Signal's <c>sprk_corerecordtype</c> names (copied).</param>
public sealed record DecisionRecordCore(string EntityLogicalName, Guid RecordId, Guid RecordTypeRefId);

/// <summary>One action the plan OFFERED, taken or skipped. The label comes from the catalog, never from the caller.</summary>
/// <param name="ActionCode">A catalog code that may appear in a plan (<see cref="DecisionActionDefinition.PlanLane"/> set).</param>
/// <param name="Taken">True when the human took it.</param>
/// <param name="Values">The values used (null for a skipped action).</param>
/// <param name="Outcome">What actually happened, from the executor. Required for a taken action.</param>
/// <param name="Written">The rows the action wrote.</param>
public sealed record DecisionRecordStep(
    string ActionCode,
    bool Taken,
    IReadOnlyDictionary<string, object?>? Values,
    string? Outcome,
    IReadOnlyList<DecisionRecordRef>? Written = null);

/// <summary>A Next step the review created, listed in <c>sprk_followons</c>.</summary>
/// <param name="ActionCode">A catalog Next-step code (<see cref="DecisionActionDefinition.IsNextStep"/>).</param>
/// <param name="Entity">The created row's table.</param>
/// <param name="Id">The created row's id.</param>
public sealed record DecisionRecordFollowOn(string ActionCode, string Entity, Guid Id);

/// <summary>
/// ONE review's outcome (D-17), as the commit route (task 043) assembles it after every other write succeeded.
/// </summary>
/// <param name="ReviewId">The client's review id; stored in <c>sprk_steps</c> so a retry can find it.</param>
/// <param name="PolicyVersionId">The immutable policy version the Signal came from.</param>
/// <param name="Core">The Signal's core record; <c>null</c> for a no-core item (D-35, D-39).</param>
/// <param name="ItemSubject">The record the decision is about: the primary Signal's subject (the subject lookup that carries its pair id
/// <c>sprk_regardingrecordid</c>, task 141); required when <paramref name="Core"/> is null, and the core record stands in for it when
/// null. Task 142 (contract C1/C2): the Decision Record is FILED under it (its subject lookup and pair, through the shared regarding
/// builder) and stamped from it (<see cref="CoreAncestorResolver"/>); it and every core it derives are parents uac-r2's
/// <see cref="IRecordOwnershipResolver"/> is asked about, so the record is owned by the team that answer names (D-39 as amended by
/// D-129: owned by teams, never by a person).</param>
/// <param name="ConfirmedByUserId">The confirming human (<c>systemuserid</c>).</param>
/// <param name="DecidedOn">When decided; the writer's clock when null.</param>
/// <param name="Steps">Every action the plan offered, taken or skipped.</param>
/// <param name="FollowOns">The Next steps created, with their ids.</param>
/// <param name="GateTier">The strictest gate tier among taken actions (task 007 <c>sprk_gatetier</c>); null when nothing was taken.</param>
/// <param name="ResolvedSignalIds">Every Signal this review resolves (the primary and any Also-resolve). The caller links them.</param>
/// <param name="FactValues">What the data said at DECISION time. MANDATORY and non-empty.</param>
/// <param name="DeciderRole">The decider's role, recorded in the snapshot (#16, S-10).</param>
/// <param name="Because">The decider's "because" text, recorded in the snapshot.</param>
/// <param name="Reason">The dismissal / denial reason ("code - detail"); required when nothing was taken.</param>
/// <param name="Denied">True when the human denied at the gate; the record then carries outcome Denied.</param>
/// <param name="PrivilegeFlagged">Copied forward from the Signal (ADR-015: flagged, never decided).</param>
/// <param name="UnnamedNextSteps">Next steps that ran but created no row the record can name (a notification email whose
/// communication row was not saved after the send; task 043). Catalog Next-step codes; listed in <c>sprk_followons</c> as
/// follow-ons without an id, and they count as something done (not a dismissal), exactly as a follow-on does.</param>
/// <param name="ReviewHash">The commit route's content hash of the review (task 043, F1), stored in <c>sprk_steps</c> so a repeat
/// that finds this record by its review id can prove it is the same review before closing anything with it.</param>
/// <param name="DeciderContactId">The deciding caller's linked internal contact (D-129: the individual comes from Assigned To
/// Internal, never <c>ownerid</c>), written to <c>sprk_assignedtointernal</c>. Null when the caller has no linked contact: the column
/// stays empty and the commit still succeeds. <c>sprk_confirmedby</c> stays the caller's <c>systemuserid</c>.</param>
/// <param name="SignalSubjects">The subject record of EVERY Signal the review resolves (primary and also-resolve), read app-only from
/// the Signals. They are ownership parents beside the core record (uac-r2 F1): a Signal whose subject sits under a secure record
/// makes the Decision Record the Secure Record Owners team's even when its core is an ordinary matter.</param>
/// <param name="AssigneeSystemUserId">D-134: for a no-core review of a PERSONAL To Do, the one enabled systemuser its Assigned To
/// names (<see cref="PersonalToDoAssignee"/>); null for no single user. Used ONLY when there is no core and every parent's answer is
/// ordinary (item 7: a secure parent wins): the record is then that user's business-unit team's.</param>
public sealed record DecisionRecordRequest(
    Guid ReviewId,
    Guid PolicyVersionId,
    DecisionRecordCore? Core,
    DecisionRecordRef? ItemSubject,
    Guid ConfirmedByUserId,
    DateTimeOffset? DecidedOn,
    IReadOnlyList<DecisionRecordStep> Steps,
    IReadOnlyList<DecisionRecordFollowOn> FollowOns,
    string? GateTier,
    IReadOnlyList<Guid> ResolvedSignalIds,
    IReadOnlyDictionary<string, object?> FactValues,
    string? DeciderRole,
    string? Because,
    string? Reason,
    bool Denied = false,
    bool PrivilegeFlagged = false,
    IReadOnlyList<string>? UnnamedNextSteps = null,
    string? ReviewHash = null,
    Guid? DeciderContactId = null,
    IReadOnlyList<DecisionRecordRef>? SignalSubjects = null,
    Guid? AssigneeSystemUserId = null);

/// <summary>The result of <see cref="DecisionRecordWriter.WriteAsync(DecisionRecordRequest, CancellationToken)"/>.</summary>
/// <param name="RecordId">The new <c>sprk_decisionrecord</c> id: the caller sets it on every resolved Signal.</param>
/// <param name="RecordClass">The class derived from the catalog.</param>
/// <param name="ResolvedSignalIds">The Signals the record covers, echoed for the caller's one link step.</param>
public sealed record DecisionRecordWriteResult(
    Guid RecordId, RecordedDecisionClass RecordClass, IReadOnlyList<Guid> ResolvedSignalIds)
{
    /// <summary>The Secure Record Owners team that owns the row when the create set it; null otherwise.</summary>
    public Guid? SecureOwnerTeamId { get; init; }

    /// <summary>Task 149 (D-139): true when <see cref="DecisionRecordWriter.WriteGateDecisionAsync"/> found the gate decision's record
    /// already written and created nothing; <see cref="RecordId"/> is then the existing record's id. Task 153: the same for
    /// <see cref="DecisionRecordWriter.WriteConditionClearedAsync"/>.</summary>
    public bool AlreadyRecorded { get; init; }

    /// <summary>Task 153 (D-145b): the record's <c>sprk_confirmedby</c> as written (or, when <see cref="AlreadyRecorded"/>, as stored);
    /// null when it names nobody. The job copies it to the Signal's <c>sprk_resolvedby</c>.</summary>
    public Guid? ConfirmedByUserId { get; init; }
}

/// <summary>
/// Task 153 (D-145b, D-139): the ONE idempotency key of the Routine Decision Record written when the nightly pass closes a Do-lane
/// Signal as ConditionCleared, deterministic from the Signal and its episode. Stored as the record's review id in <c>sprk_steps</c>, as
/// <see cref="GateDecisionKey"/> is; its prefix (<c>cleared-dr-</c>) differs from the gate's (<c>gate-dr-</c>), so neither key can hash to
/// the other's value.
/// </summary>
public static class ConditionClearedRecordKey
{
    /// <summary>The key as the log carries it: <c>cleared-dr-{signalId}-{episode}</c>.</summary>
    public static string Of(Guid signalId, int episode) =>
        $"cleared-dr-{signalId.ToString("D", CultureInfo.InvariantCulture)}-{episode.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The key as a <see cref="Guid"/> (the first 16 bytes of its SHA-256), the form <c>sprk_steps</c> keeps a review id in.</summary>
    public static Guid IdOf(Guid signalId, int episode) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(Of(signalId, episode))).AsSpan(0, 16));
}

/// <summary>
/// Task 153 (D-145b): a Do-lane Signal the nightly pass closed as ConditionCleared (its subject is no longer a hit), for
/// <see cref="DecisionRecordWriter.WriteConditionClearedAsync"/>.
/// </summary>
/// <param name="SignalId">The closed Signal (the record's one resolved Signal).</param>
/// <param name="Episode">Its episode (with <paramref name="SignalId"/>, the record's key).</param>
/// <param name="PolicyCode">The policy, for the fact snapshot.</param>
/// <param name="PolicyVersionId">The Signal's <c>sprk_policyversion</c> (copied, D-34); <see cref="Guid.Empty"/> when it has none.</param>
/// <param name="Subject">The Signal's subject (a To Do, an event or a work assignment): the record is filed under it, and the person who
/// last changed it is the attribution.</param>
/// <param name="Core">The Signal's core record (D-34, D-36); null for a no-core item.</param>
/// <param name="ClosedOn">When the Signal was closed (its <c>sprk_resolvedon</c>, or the time the job closed it).</param>
/// <param name="SignalRaisedOn">When the Signal was raised (its <c>createdon</c>); null when unknown.</param>
/// <param name="AssigneeSystemUserId">D-134: for a no-core PERSONAL To Do, the one enabled user its Assigned To names; null otherwise.</param>
/// <param name="PrivilegeFlagged">Copied forward from the Signal (ADR-015).</param>
public sealed record ConditionClearedRecordRequest(
    Guid SignalId,
    int Episode,
    string PolicyCode,
    Guid PolicyVersionId,
    DecisionRecordRef Subject,
    DecisionRecordCore? Core,
    DateTime ClosedOn,
    DateTime? SignalRaisedOn,
    Guid? AssigneeSystemUserId = null,
    bool PrivilegeFlagged = false);

/// <summary>
/// Task 149 (D-139): the ONE idempotency key of a communications-gate Decision Record, deterministic from the pair (communication,
/// gate outcome) and from nothing else. The gate evaluates again on every enrichment retry and each run has its own id, so a run id can
/// never be the key; this one is the same on every run of the same decision. The writer stores <see cref="IdOf"/> as the record's review
/// id in <c>sprk_steps</c> (the column <c>DecisionCommitService</c> already finds a review's record by) and looks it up there before it
/// creates, so a second record for the same decision is never written. A DIFFERENT outcome for the same communication (a deny, then an
/// authorize after a rule change) has a different key and is its own record.
/// </summary>
public static class GateDecisionKey
{
    /// <summary>The key as the job's idempotency key and the log carry it: <c>gate-dr-{communicationId}-{authorized|denied}</c>.</summary>
    public static string Of(Guid communicationId, bool authorize) =>
        $"gate-dr-{communicationId.ToString("D", CultureInfo.InvariantCulture)}-{(authorize ? "authorized" : "denied")}";

    /// <summary>The key as a <see cref="Guid"/> (the first 16 bytes of its SHA-256), the form <c>sprk_steps</c> keeps a review id in.</summary>
    public static Guid IdOf(Guid communicationId, bool authorize) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(Of(communicationId, authorize))).AsSpan(0, 16));
}

/// <summary>
/// Task 042 (FR-23; main-session decision "A+C"): ONE automated gate decision of the communications policy gate
/// (<c>CommunicationRuleGate</c>), for <see cref="DecisionRecordWriter.WriteGateDecisionAsync"/>. The review-shaped
/// <see cref="DecisionRecordRequest"/> cannot carry it: the gate has no confirming human, no <c>sprk_policyversion</c> (its rules are
/// <c>sprk_communicationrule</c> rows), no Signal it resolves and no action in <see cref="DecisionActionCatalog"/>.
/// </summary>
/// <param name="CommunicationId">The assessed <c>sprk_communication</c>: the record the Decision Record is FILED under (its subject).
/// The core record (matter or project, D-34) is derived from it by <see cref="CoreAncestorResolver"/>, never read off the row.</param>
/// <param name="EvaluationId">The id of the gate RUN that decided (an enrichment run), for the writer's logs and the caller's fact snapshot.
/// It is NOT the record's key: a new run of the same decision has a new id, and the record is keyed by <see cref="GateDecisionKey"/> from
/// (<paramref name="CommunicationId"/>, <paramref name="Authorize"/>) alone (task 149, D-139: one record per decision).</param>
/// <param name="Authorize">The gate's outcome: Authorized (true) or Denied (false). Maps to <c>sprk_decisionoutcome</c>.</param>
/// <param name="Reason">The gate's reason code (<c>CommunicationRuleDecision.Reason</c>); required, stored in <c>sprk_reason</c>.</param>
/// <param name="ProposedActionCode">The action the gate proposes, as free text (NOT a catalog code): stored in <c>sprk_actioncode</c> on
/// an authorize only, null on a deny (FR-01). At most 100 characters.</param>
/// <param name="ProposedActionLabel">The same action in words, listed in <c>sprk_proposedaction</c> and <c>sprk_steps</c> on both paths.</param>
/// <param name="Facts">What the gate saw (ids, confidence, threshold, matched rule, priority): MANDATORY and non-empty. Never message content.</param>
/// <param name="PrivilegeFlagged">Copied forward from the gate decision (ADR-015: flagged, never decided).</param>
/// <param name="DirectFiledUnderId">The record the communication is DIRECTLY filed under (its regarding pair id, ADR-024); D-37's first
/// tie-break between the matter and the project it is stamped with. Null when unknown.</param>
/// <param name="DecidedOn">When decided; the writer's clock when null.</param>
public sealed record GateDecisionRecordRequest(
    Guid CommunicationId,
    Guid EvaluationId,
    bool Authorize,
    string Reason,
    string ProposedActionCode,
    string ProposedActionLabel,
    IReadOnlyDictionary<string, object?> Facts,
    bool PrivilegeFlagged = false,
    Guid? DirectFiledUnderId = null,
    DateTimeOffset? DecidedOn = null);

/// <summary>Bounded-cardinality refusal reasons for the Decision Record writer (log and metric dimension).</summary>
public static class DecisionRecordRefusalReason
{
    /// <summary>Task 042: the consumer's gate-decision write failed for a reason the writer did not classify (an unexpected exception
    /// out of <see cref="DecisionRecordWriter.WriteGateDecisionAsync"/>). The writer's own refusals keep their reasons.</summary>
    public const string GateRecordFailed = "decision_gate_record_failed";

    /// <summary>Task 149 (D-144, replacing task 042's <c>gate_dr_skipped_unfiled</c>): a gate decision about a communication that stands alone
    /// (filed under no core record) is owned by the communication's own owning user (D-144 as amended; the Secure Record business unit's
    /// named team when the communication sits there); when that owner, or the unit needed to rule out the Secure Record unit, cannot be read,
    /// nothing is written. Not a failure: logged at Warning (<see cref="DecisionRecordEvents.GateDecisionSkippedNoUnitTeam"/>) and counted on the
    /// SKIP counter (<c>ontology.writer.skips</c>, never <c>ontology.writer.failures</c>) under this tag.</summary>
    public const string GateSkippedNoUnitTeam = "gate_dr_skipped_no_unit_team";

    /// <summary>Task 149 (D-144 as amended, uac-r2 K2): the unfiled communication's owning user is DISABLED, so Dataverse would refuse the create
    /// (a retry meets the same condition and would dead-letter and page the writer-failures alert). Nothing is written; it is a skip, not a
    /// failure: Warning (<see cref="DecisionRecordEvents.GateDecisionSkippedOwnerDisabled"/>) and counted on <c>ontology.writer.skips</c>
    /// under this tag. It never falls through to a team.</summary>
    public const string GateSkippedOwnerDisabled = "gate_dr_skipped_owner_disabled";

    /// <summary>Task 149 (D-139): whether the gate decision's record already exists could not be checked (a Dataverse fault on the lookup), so
    /// nothing was written rather than risk a second record. A retry may succeed.</summary>
    public const string GateRecordLookupFailed = "decision_gate_lookup_failed";

    /// <summary>Task 149 (D-139): the gate decided but its Decision Record job could not be queued (a Service Bus fault or timeout), so no
    /// record will be written for that run. The gate's outcome and the RI action are unchanged.</summary>
    public const string GateEnqueueFailed = "decision_gate_enqueue_failed";

    public const string ReviewInvalid = "decision_review_invalid";
    public const string FactSnapshotMissing = "decision_fact_snapshot_missing";
    public const string RecordClassUnderivable = "decision_record_class_underivable";
    public const string CoreUnsupported = "decision_core_unsupported";
    public const string OwnerMissing = "decision_owner_missing";
    public const string OwnerRefused = "decision_owner_refused";
    public const string OwnerResolutionFailed = "decision_owner_resolution_failed";
    public const string CreateFailed = "decision_create_failed";
    public const string DataverseAccessDenied = "decision_dataverse_access_denied";
    public const string SecureOwnerMismatch = "decision_secure_owner_mismatch";

    /// <summary>Task 153 (D-139, D-145b): whether a condition-cleared closure's Decision Record already exists could not be checked (a
    /// Dataverse fault on the lookup), so nothing was written rather than risk a second record. The next pass retries.</summary>
    public const string ConditionClearedLookupFailed = "decision_cleared_lookup_failed";

    /// <summary>Task 153 (adversarial pass 1 F1): the closed Signal's subject no longer exists (the attribution read by id found no row), so
    /// the closure cannot be filed. Permanent: a counted skip (<see cref="DecisionRecordWriter.ConditionClearedSkipTag"/>), never retried.</summary>
    public const string ConditionClearedSubjectMissing = "decision_cleared_subject_missing";

    /// <summary>Task 142: the record's subject is a table the Decision Record has no subject lookup for (contract C1). No I/O.</summary>
    public const string SubjectUnsupported = "decision_subject_unsupported";

    /// <summary>Task 142: the filing (subject lookup and pair, or the core-ancestor stamp, contract C1/C2) could not be derived, so the
    /// record's place and the parents its owner is asked about are unknown. Nothing was written; a retry may succeed.</summary>
    public const string FilingFailed = "decision_filing_failed";

    /// <summary>Task 142 (adversarial pass 1 F1): the subject cannot be filed for a reason a retry does not change (its lineage names
    /// two different records of one core table, or it no longer exists). A refusal, not an outage.</summary>
    public const string SubjectUnfileable = "decision_subject_unfileable";

    /// <summary>Task 042b (D-169): a read or a grant of an unfiled communication's gate record participant share failed. Not a refusal: the
    /// record is written and owner-visible; nothing is shared beyond the communication's sharees (<see cref="GateDecisionRecordSharing"/>).</summary>
    public const string ParticipantShareFailed = "decision_participant_share_failed";

    /// <summary>Task 042b (D-169): the gate record of a communication an Office save filed could not be re-owned, read back, or have its
    /// shares revoked. The filing stands; a record not re-owned keeps its user owner and its participant shares (<see cref="GateDecisionRecordSharing"/>).</summary>
    public const string FilingReownFailed = "decision_filing_reown_failed";
}

/// <summary>
/// Task 142: the Decision Record's owner as <see cref="DecisionRecordWriter.ResolveOwnerAsync"/> resolved it before the commit's first
/// write, with the parents it was asked about. <see cref="DecisionRecordWriter.WriteAsync(DecisionRecordRequest, DecisionRecordOwner?, CancellationToken)"/>
/// uses the answer only when the record it files sits under exactly those parents; otherwise it asks again, so an owner is never applied
/// to a record filed somewhere the answer was not given for.
/// </summary>
/// <remarks>It also carries the FILING step 1f computed (<see cref="Filing"/>), so the write at step 5 applies it with no Dataverse
/// read: a catalog or stamp read that faults after the route has sent an email would leave a decision sent but not recorded.
/// Component justification (CLAUDE.md section 11). Existing: <see cref="RecordOwnerResolution"/> is the answer. Extension: it
/// carries no record of the question, and adding one there would change uac-r2's shared type for one caller. Cost of doing nothing: a
/// subject re-filed between the route's step 1f and its step 5 would get an owner resolved for parents it no longer has, and every
/// filing read would run again after the sends.</remarks>
public sealed class DecisionRecordOwner
{
    internal DecisionRecordOwner(
        RecordOwnerResolution resolution, IReadOnlyList<RecordOwnershipParent> parents, Guid? assignee, DecisionRecordFiling? filing = null,
        EntityReference? principal = null)
    {
        Principal = principal;
        Resolution = resolution ?? throw new ArgumentNullException(nameof(resolution));
        Parents = parents ?? throw new ArgumentNullException(nameof(parents));
        Assignee = assignee;
        Filing = filing;
    }

    /// <summary>The filing the answer was given for (null when none was computed with it).</summary>
    internal DecisionRecordFiling? Filing { get; }

    /// <summary>Task 149 / D-144 as amended: the owning USER of an unfiled gate communication, applied as the record's owner in place of a
    /// team (null for every other record, whose owner is the resolver's team).</summary>
    internal EntityReference? Principal { get; }

    /// <summary>The resolver's answer, as returned.</summary>
    public RecordOwnerResolution Resolution { get; }

    /// <summary>The parents the resolver was asked about, in order (the first is primary).</summary>
    internal IReadOnlyList<RecordOwnershipParent> Parents { get; }

    /// <summary>The D-134 assignee the answer may have been re-resolved for.</summary>
    internal Guid? Assignee { get; }

    /// <summary>True when this answer was given for exactly <paramref name="parents"/> and <paramref name="assignee"/>.</summary>
    internal bool Answers(IReadOnlyList<RecordOwnershipParent> parents, Guid? assignee) =>
        Assignee == assignee && Parents.SequenceEqual(parents);
}

/// <summary>
/// Task 142: a Decision Record's filing under <paramref name="Subject"/> as computed once: the columns the shared regarding builder and
/// the core-ancestor stamp wrote (subject lookup, ADR-024 pair, <c>sprk_regarding{core}</c> stamps, inherited Access Permission), and
/// the cores the stamp derived (owner parents).
/// </summary>
internal sealed record DecisionRecordFiling(
    DecisionRecordRef Subject, IReadOnlyDictionary<string, object> Columns, IReadOnlyList<CoreAncestorStamp> DerivedCores);

/// <summary>Stable EventIds for the Decision Record writer (the 5030x range is the Signal side's).</summary>
public static class DecisionRecordEvents
{
    /// <summary>Error: the writer refused (nothing written), or the create failed. Properties: reason only, never fact values.</summary>
    public static readonly EventId WriteRefused = new(50310, nameof(WriteRefused));

    /// <summary>Error: a Secure-team create read back with another owner. The row exists (append-only except the live stamp columns, task 144 C2); uac-r2's reconcile corrects the owner.</summary>
    public static readonly EventId SecureOwnerMismatch = new(50311, nameof(SecureOwnerMismatch));

    /// <summary>Error (task 042): the communications gate decided but its Decision Record was NOT written (a refusal or a fault). The
    /// gate's outcome and the consumer's behaviour are unchanged. Properties: ids and the refusal reason only, never fact values.</summary>
    public static readonly EventId GateDecisionNotRecorded = new(50312, nameof(GateDecisionNotRecorded));

    /// <summary>Warning (task 149, D-144; replaces task 042's Information event of the same id): a gate decision about a communication that
    /// stands alone is not recorded because its owner, or its owning business unit (needed to rule out the Secure Record unit), cannot be read
    /// (<see cref="DecisionRecordRefusalReason.GateSkippedNoUnitTeam"/>). Ids and the resolver's refusal code only.</summary>
    public static readonly EventId GateDecisionSkippedNoUnitTeam = new(50313, nameof(GateDecisionSkippedNoUnitTeam));

    /// <summary>Warning (task 149, D-144 as amended, uac-r2 K2): a gate decision about an unfiled communication is not recorded because the
    /// communication's owning user is disabled (<see cref="DecisionRecordRefusalReason.GateSkippedOwnerDisabled"/>). Ids only.</summary>
    public static readonly EventId GateDecisionSkippedOwnerDisabled = new(50314, nameof(GateDecisionSkippedOwnerDisabled));

    /// <summary>Warning (task 153, adversarial pass 1 F1): a Do-lane condition-cleared closure is not recorded because its record can never be
    /// written (the subject no longer exists, cannot be filed or is not a table a record can be filed under). Counted on
    /// <c>ontology.writer.skips</c>, not retried. Ids and the reason only.</summary>
    public static readonly EventId ConditionClearedSkipped = new(50315, nameof(ConditionClearedSkipped));

    /// <summary>Warning (task 042b, D-169): an unfiled communication's gate record participant share could not be read or made. Ids only.</summary>
    public static readonly EventId ParticipantShareFailed = new(50316, nameof(ParticipantShareFailed));

    /// <summary>Warning (task 042b, D-169): a filed communication's gate record could not be re-owned, read back, or have its shares
    /// revoked. Ids only; the filing stands.</summary>
    public static readonly EventId FilingReownFailed = new(50317, nameof(FilingReownFailed));
}

/// <summary>Thrown when the writer refuses a review. NOTHING was written. The commit route reports it and writes no record.</summary>
public sealed class DecisionRecordRefusedException : Exception
{
    /// <summary>One of <see cref="DecisionRecordRefusalReason"/>'s constants.</summary>
    public string Reason { get; }

    /// <summary>The ownership resolver's refusal code when <see cref="Reason"/> is owner_refused.</summary>
    public string? OwnerRefusalCode { get; }

    public DecisionRecordRefusedException(
        string reason, string message, string? ownerRefusalCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
        OwnerRefusalCode = ownerRefusalCode;
    }
}

/// <summary>
/// Writes the ONE <c>sprk_decisionrecord</c> a review produces (append-only except the live stamp columns, task 144 C2) (task 040; spec FR-18, FR-19, FR-20, FR-22,
/// FR-50, FR-51; D-17, D-33, D-34, D-36, D-39).
/// </summary>
/// <remarks>
/// <para><b>Every human resolution writes one record, typed.</b> BR-1 was reversed: there is no lane carve-out. The class is
/// derived here from task 036's catalog and never from the client: Dismissal when nothing was taken and no Next step was
/// created; Judgement when any taken action or any Next step is Judgement; Routine otherwise. An action code the catalog does
/// not know is refused, never defaulted (the option set is closed).</para>
/// <para><b>Append-only except the live stamp columns (task 144, C2).</b> The core-ancestor restamper re-writes the record's
/// FR-26 stamp (<c>sprk_regarding{matter,project,workassignment,servicerequest}</c>) when its subject is re-filed, and uac-r2's
/// reconcile re-owns it (both under the BFF's own identity); the decision context (D-34) is never written again. The writer only
/// ever calls <c>CreateAsync</c>. No human role and not this writer identity holds Write or Delete on the table, and this class
/// has no update path.</para>
/// <para><b>Ownership.</b> uac-r2's <see cref="IRecordOwnershipResolver"/> is asked with EVERY parent the record sits under: the
/// core record (if any), the record's own subject, every core its filing derives (task 142, contract C3) and the subject of every
/// Signal the record resolves (<see cref="DecisionRecordRequest.SignalSubjects"/>),
/// and its answer is used exactly as returned (D-39 / D-129: records are owned by teams, uac-r2 F1): the owning team is set in the
/// create (the Secure Record Owners team when ANY parent is secure, D-123, even when the core is an ordinary matter; otherwise the
/// team the resolver picks for the core's, or with no core the subject's, business unit). A refusal writes nothing and throws
/// <see cref="DecisionRecordRefusedException"/>; the writer never picks an owner, never keeps its own, and never sets
/// <c>owningbusinessunit</c>. The decider, an individual, is <c>sprk_assignedtointernal</c> (a contact), never <c>ownerid</c>.
/// D-134: a no-core review of a PERSONAL To Do whose parents are all ordinary is owned by its Assigned To user's business-unit
/// team instead (<see cref="DecisionRecordRequest.AssigneeSystemUserId"/>), still the resolver's answer; a secure parent wins.
/// The typed lineage lookup (<c>sprk_matter</c>, <c>sprk_project</c>,
/// <c>sprk_workassignment</c>) is read from uac-r2's <see cref="SecureChildLineage"/> entry for this table, not branched per
/// type; a service request has none and is recorded in the generic columns only.</para>
/// <para><b>Filing (task 142, child-record contract C1/C2/C3).</b> The record is filed under its subject
/// (<see cref="DecisionRecordRequest.ItemSubject"/>, else the core record): its one subject lookup and the regarding pair are written
/// by the shared regarding builder (<c>TodoRegardingBuilder.ApplySubjectLookupAndPairAsync</c>, D-133; an untyped pair is valid), and
/// THEN the core-ancestor stamp <c>sprk_regarding{matter,project,workassignment,servicerequest}</c> is derived from the same subject by
/// <see cref="CoreAncestorResolver"/> (K2: the builder's ADR-024 guard refuses a row that already carries a regarding lookup). Every
/// core the stamp derives is an owner parent too. A filing that cannot be derived refuses with nothing written.</para>
/// <para><b>Fact snapshot.</b> <c>sprk_factsnapshot</c> is mandatory: a review with no fact values is refused. The decider's role
/// and the "because" text sit in it beside the facts.</para>
/// <para><b>Privilege.</b> <see cref="DecisionRecordRequest.PrivilegeFlagged"/> is copied to <c>sprk_privilegeflagged</c> and
/// nothing reads it for a decision (ADR-015).</para>
/// <para><b>The Signal link is NOT made here.</b> The commit route (task 043, step 6) closes each resolved Signal and sets
/// <c>sprk_signal.sprk_decisionrecord</c> in the same update, in one place; this writer returns the record id and echoes the
/// resolved Signal ids (they are also stored in <c>sprk_steps</c>).</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="SignalWriter"/> writes a different table
/// with a different lifecycle (create-or-reconcile, business-unit ownership). Extension: no; this table is create-only, one
/// row per review, with a derived class, and shares only the injected client and resolver. Cost of doing nothing: no durable
/// per-matter answer to why a decision was made (task 040 justification).</para>
/// </remarks>
public sealed class DecisionRecordWriter
{
    private const string Table = "sprk_decisionrecord";
    private const int ProposedActionMaxLength = 400;
    private const int NameMaxLength = 200;
    private const int ActionCodeMaxLength = 100;
    private const int GateTierMaxLength = 100;
    private const int ReasonMaxLength = 2000;
    // Live MaxLengths (sprk_decisionrecord): the fact snapshot 100000, the two JSON memos 1048576 (task 007). An oversize memo is
    // refused BEFORE any I/O: by the time a create would fail, 043 has already sent its emails.
    private const int FactSnapshotMaxLength = 100_000;
    private const int JsonMemoMaxLength = 1_048_576;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// The typed lineage column on this table for each core entity, read from uac-r2's lineage entry for
    /// <c>sprk_decisionrecord</c>. One data table; no per-type branch. A core entity absent here (the service request) has no
    /// typed column (D-36).
    /// </summary>
    /// <remarks>
    /// <para><b>The <c>Key == Value</c> typed-core rule (uac-r2 task 182, #1780).</b> The lineage entry maps each lookup COLUMN
    /// (key) to the root TABLE it points at (value). A lookup is kept here only when its target is a secure root AND its column
    /// name IS that root's logical name (<c>sprk_matter</c> → <c>sprk_matter</c>, <c>sprk_project</c> → <c>sprk_project</c>,
    /// <c>sprk_workassignment</c> → <c>sprk_workassignment</c>): that is the typed core column. The <c>sprk_regarding{core}</c>
    /// lookups the same entry lists (key ≠ value) are the filing and stamp columns: the shared regarding builder and
    /// <see cref="CoreAncestorResolver"/> write them (task 142, contract C1/C2), never this map.</para>
    /// <para><b>Never empty (#1780 K3).</b> The typed columns stay populated for new rows. <b>Task 145 decided: they are decision
    /// CONTEXT, not filing</b> (D-34): the core the decision was about, frozen with <c>sprk_corerecordid</c>, and kept as a lineage
    /// row because that core is one of the record's owner parents (the writer asks with it) and a record about a secure core belongs
    /// to that family (D-123). The filing is the subject lookup plus the live <c>sprk_regarding{core}</c> stamp, which follows the
    /// SUBJECT, so after a re-file it can name another core (144 review K1): a stamp never stands in for this column. No reader
    /// treats it as filing (<c>notes/task-145-matter-readers.md</c>). Pinned by <c>DecisionRecordWriterTests</c> (a missing root
    /// fails there with <c>KeyNotFoundException</c>).</para>
    /// </remarks>
    internal static readonly FrozenDictionary<string, string> LineageColumnByCoreEntity =
        SecureChildLineage.Children[Table].Lookups
            .Where(l => SecureChildLineage.Roots.Contains(l.Value) && l.Key == l.Value)
            .ToFrozenDictionary(l => l.Value, l => l.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The Decision Record's subject lookups, by subject table: the shared regarding builder's own map for this host
    /// (task 141, D-133), never a copy. A subject whose table is not here cannot be filed (contract C1).</summary>
    private static readonly IReadOnlyDictionary<string, string> SubjectLookupByEntity =
        TodoRegardingBuilder.RegardingLookupsByHostTable[Table];

    private readonly OntologyWriterDataverseClient _writerClient;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly CoreAncestorResolver _coreAncestors;
    private readonly IGenericEntityService _entities;
    /// <summary>The shared regarding builder (D-133), ONE for the writer's life so its <c>sprk_recordtype_ref</c> cache (answers only; a faulting
    /// read is never cached) is paid once per type across every record (ontology task 042, K2).</summary>
    private readonly TodoRegardingBuilder _regarding;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DecisionRecordWriter> _logger;
    private readonly GateDecisionRecordSharing _participantShares;

    /// <param name="writerClient">The ontology writer identity: the record's ONLY create, and the secure-owner read-back.</param>
    /// <param name="ownership">uac-r2's owner resolver (contract C3).</param>
    /// <param name="coreAncestors">The core-ancestor stamp of the record's subject (contract C2), and the derived cores its owner is asked about.</param>
    /// <param name="entities">The shared sysadmin reader, used for ONE read only: whether a gate decision's record already exists (task 149,
    /// D-139). The writer identity cannot be assumed to see records owned by other teams, so the lookup is not made through it.</param>
    /// <param name="recordTypes">The <c>sprk_recordtype_ref</c> catalog the shared regarding builder types the pair from (task 141).</param>
    /// <param name="timeProvider">The decision time when the review gives none.</param>
    /// <param name="logger">Refusals and the secure-owner read-back.</param>
    /// <param name="participantShares">Task 042b (D-169): shares an unfiled communication's gate record, Read only, with the communication's
    /// internal sharees after the create (best-effort, never throws). Required (042b round 3, as uac-r2 required for its two consumers).</param>
    public DecisionRecordWriter(
        OntologyWriterDataverseClient writerClient,
        IRecordOwnershipResolver ownership,
        CoreAncestorResolver coreAncestors,
        IGenericEntityService entities,
        ICommunicationDataverseService recordTypes,
        TimeProvider timeProvider,
        ILogger<DecisionRecordWriter> logger,
        GateDecisionRecordSharing participantShares)
    {
        _participantShares = participantShares ?? throw new ArgumentNullException(nameof(participantShares));
        _writerClient = writerClient ?? throw new ArgumentNullException(nameof(writerClient));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _coreAncestors = coreAncestors ?? throw new ArgumentNullException(nameof(coreAncestors));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _regarding = new TodoRegardingBuilder(
            recordTypes ?? throw new ArgumentNullException(nameof(recordTypes)), _coreAncestors, logger ?? throw new ArgumentNullException(nameof(logger)));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Writes the review's one Decision Record.</summary>
    /// <exception cref="DecisionRecordRefusedException">The ONLY exception this writer throws (other than cancellation): the
    /// review is invalid or too large for its columns, has no fact snapshot, cannot be typed from the catalog, has no subject the
    /// record can be filed under, could not be filed, or its owner was refused (nothing was written); or the Dataverse create failed
    /// (<see cref="DecisionRecordRefusalReason.CreateFailed"/>, or <see cref="DecisionRecordRefusalReason.DataverseAccessDenied"/> for a
    /// privilege fault; the inner exception is kept, and after a timeout the row may exist).</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    public Task<DecisionRecordWriteResult> WriteAsync(DecisionRecordRequest request, CancellationToken ct = default) =>
        WriteAsync(request, resolvedOwner: null, ct);

    /// <summary>Writes the review's one Decision Record with an owner already resolved by <see cref="ResolveOwnerAsync"/>
    /// (null: resolve it here). The handed-in answer is used only when the record is filed under exactly the parents it was given
    /// for; otherwise the owner is asked again. Same exceptions as <see cref="WriteAsync(DecisionRecordRequest, CancellationToken)"/>.</summary>
    public async Task<DecisionRecordWriteResult> WriteAsync(DecisionRecordRequest request, DecisionRecordOwner? resolvedOwner, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return await CreateAsync(request, Validate(request), resolvedOwner, ct).ConfigureAwait(false);
        }
        catch (DecisionRecordRefusedException ex)
        {
            LogRefusal(request.ReviewId, ex);
            throw;
        }
    }

    /// <summary>
    /// Task 042 (FR-23; main-session decision "A+C"): writes the ONE Decision Record of an automated communications-gate decision, on
    /// the authorize path AND the deny path. It is an additive entry point: <see cref="WriteAsync(DecisionRecordRequest, CancellationToken)"/>
    /// and the review shape are unchanged, and everything below the request is the SAME code: <see cref="Content"/> (so the D-34
    /// context columns still have one writer), <see cref="FiledAndOwnedAsync"/> (the communication filed through the shared regarding
    /// builder, the core-ancestor stamp, uac-r2's owner answer over EVERY parent, secure-if-any) and the one <see cref="CreateAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Closed field semantics.</b> Class: always Judgement, set here (owner, D-17 amendment), not derived from the catalog.
    /// Outcome: Authorized or Denied from <see cref="GateDecisionRecordRequest.Authorize"/>. <c>sprk_action</c> is never set and
    /// <c>sprk_actioncode</c> is null on a deny (FR-01); on an authorize it is the gate's proposed action as free text (never added to
    /// <see cref="DecisionActionCatalog"/>). <c>sprk_confirmedby</c>, <c>sprk_policyversion</c>, <c>sprk_gatetier</c>, <c>sprk_gatesessionid</c>
    /// and <c>sprk_assignedtointernal</c> are null: no human decided (D-129: never the BFF identity or an owner), the gate's rules are
    /// <c>sprk_communicationrule</c> rows (the matched rule id and priority are in the fact snapshot), and no SessionGate exists in
    /// enrichment (the correlation id is in the fact snapshot). No Signal is resolved, and none is linked.</para>
    /// <para><b>The core record (D-34, D-37).</b> Derived from the communication by <see cref="CoreAncestorResolver"/>, never read off its
    /// row: one stamp is the core; several are decided by the record it is DIRECTLY filed under, then matter over project. A tie that
    /// neither decides is NOT a reason to lose the decision: the record is written without the core pair and typed lineage lookup, and
    /// the filing stamps (which name every matter and project) still make it queryable per matter and per project; every one is an owner
    /// parent. The communication is also an owner parent, so a secure matter, project or work assignment above it makes the record a
    /// secure child (D-33, D-123).</para>
    /// </remarks>
    /// <exception cref="DecisionRecordRefusedException">The same single exception type as <see cref="WriteAsync(DecisionRecordRequest, CancellationToken)"/>:
    /// the request is invalid, the communication cannot be filed, the owner was refused, or the create failed. Nothing was written (after
    /// a timeout the row may exist).</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    public async Task<DecisionRecordWriteResult> WriteGateDecisionAsync(GateDecisionRecordRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var plan = ValidateGate(request);

            // Task 149 (D-139): ONE record per (communication, outcome). The record's key is deterministic from those two values (never the
            // per-run EvaluationId) and is stored where every record already carries its review id (sprk_steps.reviewId), so a repeat -- an
            // enrichment retry, a redelivered job, a second job for the same decision -- finds the record and writes nothing.
            var decisionKey = GateDecisionKey.IdOf(request.CommunicationId, request.Authorize);
            if (await FindGateRecordAsync(request.CommunicationId, decisionKey, ct).ConfigureAwait(false) is { } existing)
            {
                _logger.LogInformation(
                    "Gate decision {EvaluationId} already recorded as Decision Record {RecordId} (outcome {Outcome}); nothing written.",
                    request.EvaluationId, existing, request.Authorize ? "Authorized" : "Denied");
                return new DecisionRecordWriteResult(existing, RecordedDecisionClass.Judgement, [])
                {
                    AlreadyRecorded = true,
                };
            }

            var (core, derived) = await ResolveGateCoreAsync(request, ct).ConfigureAwait(false);
            // The review shape the shared builders read: no confirmer, no policy version, no Signal (Guid.Empty and [] mean "none").
            var review = new DecisionRecordRequest(
                ReviewId: decisionKey,
                PolicyVersionId: Guid.Empty,
                Core: core,
                ItemSubject: new DecisionRecordRef(GateSubjectEntity, request.CommunicationId),
                ConfirmedByUserId: Guid.Empty,
                DecidedOn: request.DecidedOn,
                Steps: plan.Steps.Select(s => s.Step).ToList(),
                FollowOns: [],
                GateTier: null,
                ResolvedSignalIds: [],
                FactValues: request.Facts,
                DeciderRole: GateDeciderRole,
                Because: request.Reason,
                Reason: request.Reason,
                Denied: !request.Authorize,
                PrivilegeFlagged: request.PrivilegeFlagged);
            return await CreateAsync(review, plan with { AlreadyDerived = derived }, resolvedOwner: null, ct).ConfigureAwait(false);
        }
        catch (DecisionRecordRefusedException ex)
        {
            LogRefusal(request.EvaluationId, ex);
            throw;
        }
    }

    /// <summary>
    /// Task 149 (D-139): the Decision Record already written for this gate decision, or null. Narrowed by the communication's regarding
    /// lookup (the indexed column the writer files the record under, so Dataverse never scans every record's <c>sprk_steps</c> memo), then
    /// by the decision key stored in <c>sprk_steps</c> -- the lookup <c>DecisionCommitService</c> already makes for a review id -- and only among
    /// records with no <c>sprk_confirmedby</c> (a gate record has none; a human review's always does, so a client-chosen review id equal to the
    /// key cannot hide the gate's record). Read through
    /// the shared sysadmin reader, so a record owned by any team (a business-unit or secure team) is found.
    /// </summary>
    /// <exception cref="DecisionRecordRefusedException"><see cref="DecisionRecordRefusalReason.GateRecordLookupFailed"/>: the lookup faulted.
    /// Nothing is written then: guessing "none" would risk a second record, and a retry may succeed.</exception>
    private async Task<Guid?> FindGateRecordAsync(Guid communicationId, Guid decisionKey, CancellationToken ct)
    {
        try
        {
            var query = new QueryExpression(Table)
            {
                ColumnSet = new ColumnSet(false),
                TopCount = 1,
                NoLock = true,
            };
            query.Criteria.AddCondition(SubjectLookupByEntity[GateSubjectEntity], ConditionOperator.Equal, communicationId);
            query.Criteria.AddCondition("sprk_steps", ConditionOperator.Like, $"%\"reviewId\":\"{decisionKey:D}\"%");
            // A gate record never sets sprk_confirmedby; a human review's record always does. A review commit can carry a client-chosen
            // review id equal to this key, so without this condition it would count as the gate record and the gate's record would never be written.
            query.Criteria.AddCondition("sprk_confirmedby", ConditionOperator.Null);
            // Task 153 (adversarial pass 2 F1): and only a record the writer itself created. A Console user may create a Decision Record
            // (Deep create) carrying this key and no confirmer; without this condition it would stop the gate's record being written.
            query.Criteria.AddCondition(CreatedByColumn, ConditionOperator.Equal, await WriterUserIdAsync(ct).ConfigureAwait(false));
            var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            return rows?.Entities?.FirstOrDefault() is { } row && row.Id != Guid.Empty ? row.Id : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DecisionRecordRefusedException(
                DecisionRecordRefusalReason.GateRecordLookupFailed,
                "Whether the gate decision's Decision Record already exists could not be checked, so nothing was written.",
                innerException: ex);
        }
    }

    /// <summary>The table a gate decision's Decision Record is filed under.</summary>
    private const string GateSubjectEntity = "sprk_communication";

    /// <summary>Task 153 (adversarial pass 2 F1): the column Dataverse stamps with the identity that created a row.</summary>
    internal const string CreatedByColumn = "createdby";

    /// <summary>
    /// Task 153 (adversarial pass 2 F1): the ontology writer's own <c>systemuserid</c> (WhoAmI on its dedicated connection, cached by
    /// <see cref="OntologyWriterDataverseClient.GetWriterUserIdAsync"/>). The writer creates every Decision Record through that connection
    /// with no impersonation, so it is the <c>createdby</c> of every record it wrote; a row a person created is never the writer's. A fault
    /// propagates: the lookups that use it then refuse (fail closed) rather than match on anything weaker.
    /// </summary>
    internal Task<Guid> WriterUserIdAsync(CancellationToken ct) => _writerClient.GetWriterUserIdAsync(ct);

    /// <summary>The decider recorded in the fact snapshot for a gate decision: a system, not a person.</summary>
    private const string GateDeciderRole = "automated: communications policy gate";

    private const int AuthorizedOutcome = 100000000;
    private const int DeniedOutcome = 100000001;

    /// <summary>
    /// Task 153 (D-145b): writes the ONE Routine Decision Record of a Do-lane Signal the nightly pass closed as ConditionCleared: the item's
    /// work was done outside the decision route (a colleague completed the To Do on its form, recorded the response). An additive entry
    /// point beside <see cref="WriteGateDecisionAsync"/>: below the request it is the SAME code, <see cref="Content"/> (D-34 context),
    /// <see cref="FiledAndOwnedAsync"/> (filed under the Signal's subject through the shared regarding builder, the core-ancestor stamp, uac-r2's
    /// owner answer over EVERY parent, secure-if-any, D-134's assignee for a personal To Do) and the one <see cref="CreateAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Closed field semantics.</b> Class Routine, outcome Authorized (the work was done), one taken step <c>condition-cleared</c>
    /// (not a catalog action: <c>sprk_action</c> stays null), the Signal's policy version and core copied (D-34), the Signal named in
    /// <c>sprk_steps.resolvedSignalIds</c>. <b>Attribution, best effort (D-145b, D-129)</b>: <c>sprk_confirmedby</c> is the user in the
    /// subject's <c>modifiedby</c>, read here by the closing pass, ONLY when that user is a person (enabled, interactive, not an application
    /// user such as the Spaarke service identity, not flagged external: the one eligibility rule,
    /// <c>InternalShareEndpoints.ClassifyEligibility</c>) AND the change falls while the Signal was open (after it was raised, not after it
    /// was closed). Otherwise, or when the read fails, the record is still written, with <c>sprk_confirmedby</c> null and a reason saying the
    /// person is unknown and why. Never the assignee, never the owner: nobody is guessed.</para>
    /// <para><b>One record per closure (D-139).</b> Keyed by <see cref="ConditionClearedRecordKey"/> (Signal, episode), stored as the review
    /// id in <c>sprk_steps</c> and looked up first, so a re-run of the pass writes nothing new and a failed write is retried on the next pass.
    /// The gate's lookup cannot match this record: it is filed under a communication and keyed <c>gate-dr-</c>.</para>
    /// </remarks>
    /// <exception cref="DecisionRecordRefusedException">The only exception type (besides cancellation): the lookup faulted
    /// (<see cref="DecisionRecordRefusalReason.ConditionClearedLookupFailed"/>), the subject cannot be filed, the owner was refused, or the
    /// create failed. Logged and metered on <c>ontology.writer.failures</c> here; a permanent refusal (the subject no longer exists, cannot be
    /// filed or is unsupported: <see cref="ConditionClearedSkipTag"/>) is logged at Warning and metered on <c>ontology.writer.skips</c>
    /// instead.</exception>
    public async Task<DecisionRecordWriteResult> WriteConditionClearedAsync(ConditionClearedRecordRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = ConditionClearedRecordKey.IdOf(request.SignalId, request.Episode);

        try
        {
            var plan = ValidateConditionCleared(request);
            if (await FindConditionClearedRecordAsync(request.Subject, key, ct).ConfigureAwait(false) is { } existing)
            {
                _logger.LogInformation(
                    "Signal {SignalId} (episode {Episode}): its condition-cleared closure is already recorded as Decision Record {RecordId}; nothing written.",
                    request.SignalId, request.Episode, existing.Id);
                return new DecisionRecordWriteResult(existing.Id, RecordedDecisionClass.Routine, [request.SignalId])
                {
                    AlreadyRecorded = true,
                    ConfirmedByUserId = existing.ConfirmedBy,
                };
            }

            var (person, unknownWhy, changedOn) = await ReadChangedByAsync(request, ct).ConfigureAwait(false);
            var subjectLabel = request.Subject.Entity;
            var reason = person is not null
                ? $"condition_cleared - The {subjectLabel} was changed and the item's condition no longer holds; Confirmed By is the person who last changed it before the nightly pass closed the item."
                : $"condition_cleared_person_unknown - The item's condition no longer holds, but the person who changed the {subjectLabel} is unknown ({unknownWhy}).";
            var facts = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["signalId"] = request.SignalId.ToString("D", CultureInfo.InvariantCulture),
                ["episode"] = request.Episode,
                ["policyCode"] = request.PolicyCode,
                ["subjectEntity"] = request.Subject.Entity,
                ["subjectId"] = request.Subject.Id.ToString("D", CultureInfo.InvariantCulture),
                ["resolution"] = "ConditionCleared",
                ["closedOn"] = request.ClosedOn,
                ["changedOn"] = changedOn,
                ["attribution"] = person is not null ? "modifiedby" : "unknown",
                ["attributionUnknownReason"] = unknownWhy,
            };

            var review = new DecisionRecordRequest(
                ReviewId: key,
                PolicyVersionId: request.PolicyVersionId,
                Core: request.Core,
                ItemSubject: request.Subject,
                ConfirmedByUserId: person ?? Guid.Empty,
                DecidedOn: new DateTimeOffset(DateTime.SpecifyKind(request.ClosedOn, DateTimeKind.Utc)),
                Steps: plan.Steps.Select(s => s.Step).ToList(),
                FollowOns: [],
                GateTier: null,
                ResolvedSignalIds: [request.SignalId],
                FactValues: facts,
                DeciderRole: ConditionClearedDeciderRole,
                Because: null,
                Reason: reason,
                PrivilegeFlagged: request.PrivilegeFlagged,
                SignalSubjects: [request.Subject],
                AssigneeSystemUserId: request.AssigneeSystemUserId);
            var written = await CreateAsync(review, plan, resolvedOwner: null, ct).ConfigureAwait(false);
            return written with { ConfirmedByUserId = person };
        }
        catch (DecisionRecordRefusedException ex) when (ConditionClearedSkipTag(ex.Reason) is { } skip)
        {
            // Adversarial pass 1 F1: a record that can never be written (the subject is gone, cannot be filed, or is not a filing table) is
            // a counted SKIP, never ontology.writer.failures (which pages). Warning, so an operator can see the unrecorded closure.
            _logger.LogWarning(DecisionRecordEvents.ConditionClearedSkipped,
                "Signal {SignalId} (episode {Episode}): its condition-cleared closure cannot be recorded (reason={Reason}); nothing was written and it is not retried.",
                request.SignalId, request.Episode, skip);
            OntologyWriterTelemetry.RecordSkip(skip);
            throw;
        }
        catch (DecisionRecordRefusedException ex)
        {
            LogRefusal(key, ex);
            throw;
        }
    }

    /// <summary>
    /// Task 153 (adversarial pass 1 F1): the <c>ontology.writer.skips</c> tag of a <see cref="WriteConditionClearedAsync"/> refusal that is
    /// permanent (a retry is refused the same way), or null for a refusal a later pass can still succeed at. The writer meters the skip; the
    /// job counts it and does not retry it.
    /// </summary>
    internal static string? ConditionClearedSkipTag(string reason) => reason switch
    {
        DecisionRecordRefusalReason.ConditionClearedSubjectMissing => OntologyWriterFailureReason.ClosureRecordSkippedSubjectMissing,
        DecisionRecordRefusalReason.SubjectUnfileable => OntologyWriterFailureReason.ClosureRecordSkippedSubjectUnfileable,
        DecisionRecordRefusalReason.SubjectUnsupported => OntologyWriterFailureReason.ClosureRecordSkippedSubjectUnsupported,
        _ => null,
    };

    /// <summary>
    /// Task 153 (adversarial pass 1 F2): true when a Decision Record's <c>sprk_steps</c> is the condition-cleared record of this Signal and
    /// episode: the same two tests <see cref="FindConditionClearedRecordAsync"/> makes in Dataverse (the key as the review id, and the step
    /// code only this entry point writes). The job uses it on a closed Signal's link, so a closure already linked to its record is not retried.
    /// </summary>
    internal static bool IsConditionClearedRecordFor(string? steps, Guid signalId, int episode) =>
        steps is not null
        && steps.Contains($"\"reviewId\":\"{ConditionClearedRecordKey.IdOf(signalId, episode):D}\"", StringComparison.Ordinal)
        && steps.Contains($"\"code\":\"{ConditionClearedStepCode}\"", StringComparison.Ordinal);

    /// <summary>
    /// Task 153 (uac-r2 re-check K2): THE rule for whether a review's record leaves its Signals open: something was done (an action taken or a
    /// Next step created) and none of the taken actions resolves the Signal (the catalog's <see cref="DecisionActionDefinition.ResolvesSignal"/>;
    /// D-119's budget inquiry, D-128 Q18's Do-lane reminder, or a review that only created Next steps). <c>DecisionCommitService</c>'s
    /// <c>RecordedResolution</c> (InquirySent) and <see cref="IsNonResolvingReviewRecord"/> (the job's retry) both derive from it, so a record
    /// the commit leaves a Signal Acknowledged and linked to is the same record the closure retry may replace.
    /// </summary>
    internal static bool LeavesSignalOpen(IReadOnlyCollection<string> takenCodes, int followOnCount) =>
        (takenCodes.Count > 0 || followOnCount > 0)
        && !takenCodes.Any(code => DecisionActionCatalog.TryGet(code, out var definition) && definition.ResolvesSignal);

    /// <summary>
    /// Task 153 (adversarial pass 2; uac-r2 re-check K2): true when a Decision Record (its <c>sprk_steps</c> and <c>sprk_followons</c>) is one
    /// that <see cref="LeavesSignalOpen"/> says leaves the item open: the record a review leaves the Signal Acknowledged and linked to
    /// (<c>DecisionCommitService</c>'s InquirySent). Only such a record's link is the one a condition-cleared closure's record replaces on a
    /// retry. Narrower than the commit's rule in one respect: a taken code the catalog does not know makes it false (such a record is no D-128
    /// link; the job logs and meters the drop). Unreadable steps or follow-ons are false.
    /// </summary>
    internal static bool IsNonResolvingReviewRecord(string? steps, string? followOns)
    {
        if (string.IsNullOrWhiteSpace(steps)) return false;
        try
        {
            using var document = JsonDocument.Parse(steps);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("steps", out var list)
                || list.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var taken = new List<string>();
            foreach (var step in list.EnumerateArray())
            {
                if (step.ValueKind != JsonValueKind.Object
                    || !step.TryGetProperty("taken", out var isTaken) || isTaken.ValueKind != JsonValueKind.True)
                {
                    continue;
                }

                var code = step.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                if (!DecisionActionCatalog.TryGet(code, out _)) return false;
                taken.Add(code!);
            }

            var followOnCount = 0;
            if (!string.IsNullOrWhiteSpace(followOns))
            {
                using var next = JsonDocument.Parse(followOns);
                if (next.RootElement.ValueKind != JsonValueKind.Array) return false;
                followOnCount = next.RootElement.GetArrayLength();
            }

            return LeavesSignalOpen(taken, followOnCount);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Task 153: the one step of a condition-cleared record. Not a catalog code (a review can never carry it: <see cref="Validate"/>
    /// refuses any code the catalog does not know), so the lookup below can require it. No '_' or '%' (it is matched with LIKE).</summary>
    internal const string ConditionClearedStepCode = "condition-cleared";

    /// <summary>The decider recorded in the fact snapshot for a condition-cleared closure: the nightly pass, not a person.</summary>
    private const string ConditionClearedDeciderRole = "automated: nightly pass (condition cleared)";

    /// <summary>The alias of the modifying user's <c>systemuser</c> link in <see cref="ReadChangedByAsync"/>.</summary>
    private const string ChangedByAlias = "changedby";

    /// <summary>Task 153: the condition-cleared request's checks, with no I/O, and the plan its record is built from.</summary>
    private static Plan ValidateConditionCleared(ConditionClearedRecordRequest r)
    {
        static DecisionRecordRefusedException Invalid(string message) => new(DecisionRecordRefusalReason.ReviewInvalid, message);

        if (r.SignalId == Guid.Empty) throw Invalid("SignalId is required.");
        if (r.Episode < 1) throw Invalid("Episode must be 1 or more.");
        if (r.Subject is null || string.IsNullOrWhiteSpace(r.Subject.Entity) || r.Subject.Id == Guid.Empty) throw Invalid("Subject is required.");
        if (!SubjectLookupByEntity.ContainsKey(r.Subject.Entity))
        {
            throw new DecisionRecordRefusedException(
                DecisionRecordRefusalReason.SubjectUnsupported,
                "The closed Signal's subject is not a record a Decision Record can be filed under; nothing was written.");
        }

        var definition = new DecisionActionDefinition(
            ConditionClearedStepCode, "Done outside the decision route (condition cleared)", PlanLane: null, IsNextStep: false, WorkType: null,
            Parameters: [], EffectLines: [], Excludes: [], DecisionRecordClass.Routine);
        var step = new DecisionRecordStep(ConditionClearedStepCode, Taken: true, Values: null, Outcome: "ConditionCleared");
        var steps = new List<(DecisionRecordStep Step, DecisionActionDefinition Def)> { (step, definition) };

        return new Plan(
            RecordedDecisionClass.Routine,
            AuthorizedOutcome,
            steps,
            steps,
            Name: $"{RecordedDecisionClass.Routine}: condition cleared",
            NoRequester: true);
    }

    /// <summary>
    /// Task 153 (D-139): the condition-cleared record already written for this key, with its <c>sprk_confirmedby</c>, or null. Narrowed by the
    /// subject's regarding lookup (indexed), then by the key in <c>sprk_steps</c> and by the step code only this entry point writes (a review's
    /// record cannot carry it, so a client-chosen review id equal to the key cannot hide this record), and only among records the writer itself
    /// created (<c>createdby</c>, adversarial pass 2 F1: a user's row carrying the key and the code is not this record). Read through the shared
    /// sysadmin reader, so a record owned by any team is found (a re-owned record keeps its <c>createdby</c>).
    /// </summary>
    /// <exception cref="DecisionRecordRefusedException"><see cref="DecisionRecordRefusalReason.ConditionClearedLookupFailed"/>: the lookup
    /// faulted. Nothing is written: guessing "none" would risk a second record; the next pass retries.</exception>
    private async Task<(Guid Id, Guid? ConfirmedBy)?> FindConditionClearedRecordAsync(DecisionRecordRef subject, Guid key, CancellationToken ct)
    {
        try
        {
            var query = new QueryExpression(Table)
            {
                ColumnSet = new ColumnSet("sprk_confirmedby"),
                TopCount = 1,
                NoLock = true,
            };
            query.Criteria.AddCondition(SubjectLookupByEntity[subject.Entity], ConditionOperator.Equal, subject.Id);
            query.Criteria.AddCondition("sprk_steps", ConditionOperator.Like, $"%\"reviewId\":\"{key:D}\"%");
            query.Criteria.AddCondition("sprk_steps", ConditionOperator.Like, $"%\"code\":\"{ConditionClearedStepCode}\"%");
            // Adversarial pass 2 F1: and only a record the writer itself created. The key is computable from the Signal id and episode, and a
            // Console user may create a Decision Record (Deep create): a user's row with this key, this code and a confirmer of their choosing
            // would otherwise be taken as the closure's record, and its confirmer copied to the Signal's sprk_resolvedby.
            query.Criteria.AddCondition(CreatedByColumn, ConditionOperator.Equal, await WriterUserIdAsync(ct).ConfigureAwait(false));
            var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            return rows?.Entities?.FirstOrDefault() is { } row && row.Id != Guid.Empty
                ? (row.Id, row.GetAttributeValue<EntityReference>("sprk_confirmedby")?.Id)
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DecisionRecordRefusedException(
                DecisionRecordRefusalReason.ConditionClearedLookupFailed,
                "Whether the closure's Decision Record already exists could not be checked, so nothing was written.",
                innerException: ex);
        }
    }

    /// <summary>
    /// Task 153 (D-145b, D-129): the person who last changed the closed Signal's subject, from its <c>modifiedby</c> as read now (one read: the
    /// user's eligibility columns come with it through a left outer link), or null with the reason the person is unknown. A read that faults
    /// is "unknown", and the record is still written.
    /// </summary>
    /// <exception cref="DecisionRecordRefusedException"><see cref="DecisionRecordRefusalReason.ConditionClearedSubjectMissing"/>: the read
    /// answered and found no row (the subject was deleted).</exception>
    private async Task<(Guid? Person, string? UnknownWhy, DateTime? ChangedOn)> ReadChangedByAsync(
        ConditionClearedRecordRequest request, CancellationToken ct)
    {
        Entity? row;
        try
        {
            var query = new QueryExpression(request.Subject.Entity)
            {
                ColumnSet = new ColumnSet("modifiedby", "modifiedon"),
                TopCount = 1,
                NoLock = true,
            };
            query.Criteria.AddCondition($"{request.Subject.Entity}id", ConditionOperator.Equal, request.Subject.Id);
            var link = query.AddLink("systemuser", "modifiedby", "systemuserid", JoinOperator.LeftOuter);
            link.EntityAlias = ChangedByAlias;
            link.Columns = new ColumnSet("isdisabled", "accessmode", "applicationid", "sprk_isexternal");
            row = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities?.FirstOrDefault();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                "Signal {SignalId}: the {SubjectEntity} it was about could not be read for its last change ({ExceptionType}); the closure is recorded with the person unknown.",
                request.SignalId, request.Subject.Entity, ex.GetType().Name);
            return (null, "the change could not be read", null);
        }

        if (row is null)
        {
            // Adversarial pass 1 F1: a by-id read through the sysadmin reader that finds no row means the subject was deleted. The record
            // could never be filed under it (the stamp read would fail the same way on every pass), so this is a permanent refusal.
            throw new DecisionRecordRefusedException(
                DecisionRecordRefusalReason.ConditionClearedSubjectMissing,
                "The closed Signal's subject no longer exists, so its closure cannot be filed; nothing was written.");
        }

        var changedOn = row.GetAttributeValue<DateTime?>("modifiedon");
        if (row.GetAttributeValue<EntityReference>("modifiedby") is not { LogicalName: "systemuser", Id: var userId } || userId == Guid.Empty)
        {
            return (null, "the record names no user who changed it", changedOn);
        }

        // An unreadable (absent) value is passed as null: ClassifyEligibility reads a null as "not enabled" / "not a person" (fail closed).
        object? Aliased(string column) => row.GetAttributeValue<AliasedValue>($"{ChangedByAlias}.{column}")?.Value;
        var eligibility = Sprk.Bff.Api.Api.ExternalAccess.InternalShareEndpoints.ClassifyEligibility(
            Aliased("isdisabled") is bool disabled ? disabled : null,
            Aliased("accessmode") switch { OptionSetValue o => o.Value, int i => i, _ => null },
            Aliased("applicationid") is Guid app && app != Guid.Empty ? app : null,
            Aliased("sprk_isexternal") is bool external ? external : null,
            rootIsRestricted: null);
        if (eligibility != Sprk.Bff.Api.Api.ExternalAccess.InternalShareEndpoints.ShareEligibility.Eligible)
        {
            return (null, "it was last changed by an application, service, disabled or external account", changedOn);
        }

        if (changedOn is not { } on)
        {
            return (null, "the time of the last change is unknown", null);
        }

        var onUtc = DateTime.SpecifyKind(on, DateTimeKind.Utc);
        if (onUtc > DateTime.SpecifyKind(request.ClosedOn, DateTimeKind.Utc))
        {
            return (null, "it was changed again after the item was closed", changedOn);
        }

        if (request.SignalRaisedOn is not { } raised || onUtc <= DateTime.SpecifyKind(raised, DateTimeKind.Utc))
        {
            return (null, "it was not changed while the item was open", changedOn);
        }

        return (userId, null, changedOn);
    }

    /// <summary>
    /// The gate request's checks, with no I/O, and the plan its record is built from. The one step is the gate's proposed action
    /// (free text, an ad-hoc definition that is NOT in the catalog): taken with outcome Authorized on an authorize; not taken on a deny,
    /// where <c>sprk_steps</c> shows it with outcome Denied.
    /// </summary>
    private static Plan ValidateGate(GateDecisionRecordRequest g)
    {
        static DecisionRecordRefusedException Invalid(string message) => new(DecisionRecordRefusalReason.ReviewInvalid, message);

        if (g.Facts is null || g.Facts.Count == 0)
        {
            throw new DecisionRecordRefusedException(
                DecisionRecordRefusalReason.FactSnapshotMissing,
                "sprk_factsnapshot is mandatory: the gate decision carries no fact values, so no Decision Record was written.");
        }

        if (g.CommunicationId == Guid.Empty) throw Invalid("CommunicationId is required.");
        if (g.EvaluationId == Guid.Empty) throw Invalid("EvaluationId is required.");
        if (string.IsNullOrWhiteSpace(g.Reason)) throw Invalid("Reason is required (the gate's reason code).");
        if (g.Reason.Length > ReasonMaxLength) throw Invalid("Reason is too long for sprk_reason.");
        if (string.IsNullOrWhiteSpace(g.ProposedActionCode)) throw Invalid("ProposedActionCode is required.");
        if (g.ProposedActionCode.Length > ActionCodeMaxLength) throw Invalid("ProposedActionCode is too long for sprk_actioncode.");
        if (string.IsNullOrWhiteSpace(g.ProposedActionLabel)) throw Invalid("ProposedActionLabel is required.");

        var definition = new DecisionActionDefinition(
            g.ProposedActionCode, g.ProposedActionLabel, PlanLane: null, IsNextStep: false, WorkType: null,
            Parameters: [], EffectLines: [], Excludes: [], DecisionRecordClass.Judgement);
        var step = new DecisionRecordStep(
            g.ProposedActionCode, Taken: g.Authorize, Values: null, Outcome: g.Authorize ? "Authorized" : null);
        var steps = new List<(DecisionRecordStep Step, DecisionActionDefinition Def)> { (step, definition) };

        return new Plan(
            RecordedDecisionClass.Judgement,
            g.Authorize ? AuthorizedOutcome : DeniedOutcome,
            steps,
            g.Authorize ? steps : [],
            Name: g.Authorize ? null : $"{RecordedDecisionClass.Judgement}: denied ({g.Reason})",
            SkippedOutcome: "Denied",
            IsGate: true);
    }

    /// <summary>
    /// D-34 / D-37: the core record of the communication, from <see cref="CoreAncestorResolver"/> (never the row's own matter lookup),
    /// or null when it has none, cannot be decided, or the <c>sprk_recordtype_ref</c> catalog has no row for it. A derivation that
    /// FAILS returns null here: the filing step (<see cref="FileUnderSubjectAsync"/>) derives it again and gives the classified refusal.
    /// </summary>
    /// <exception cref="DecisionRecordRefusedException"><see cref="DecisionRecordRefusalReason.FilingFailed"/>: the catalog read faulted.</exception>
    private async Task<(DecisionRecordCore? Core, CoreAncestorResult? Derived)> ResolveGateCoreAsync(GateDecisionRecordRequest g, CancellationToken ct)
    {
        var derived = await _coreAncestors.ResolveStampsAsync(GateSubjectEntity, g.CommunicationId, ct).ConfigureAwait(false);
        if (!derived.Succeeded)
        {
            return (null, null); // the filing step derives again and classifies the failure
        }

        if (derived.Stamps.Count == 0)
        {
            return (null, derived);
        }

        var stamps = derived.Stamps;
        CoreAncestorStamp? pick = null;
        if (stamps.Count == 1)
        {
            pick = stamps[0];
        }
        else if (g.DirectFiledUnderId is { } direct && stamps.Where(s => s.RecordId == direct).ToList() is [var filedUnder])
        {
            pick = filedUnder; // D-37: the record it is directly filed under wins
        }
        else
        {
            var remaining = stamps.ToList();
            if (remaining.Any(s => s.EntityType == "sprk_matter"))
            {
                remaining.RemoveAll(s => s.EntityType == "sprk_project"); // D-37: matter over project
            }

            if (remaining.Count == 1)
            {
                pick = remaining[0];
            }
        }

        if (pick is not { } core)
        {
            _logger.LogWarning(
                "Gate decision {EvaluationId}: the communication names {Count} core records and neither its direct filed-under record nor " +
                "matter-over-project decides between them (D-37); the Decision Record is written without a core pair and stays queryable " +
                "by every stamp.", g.EvaluationId, stamps.Count);
            return (null, derived);
        }

        // The shared regarding builder's record-type cache (one builder for the writer's life): the catalog is read once per type,
        // not once per decision. A read that FAULTS is not cached (the builder's contract) and is a refusal here.
        (Guid Id, string DisplayName)? typeRow;
        try
        {
            typeRow = await _regarding.ResolveRecordTypeRefAsync(core.EntityType, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw FilingFailed(new DecisionRecordRef(GateSubjectEntity, g.CommunicationId),
                $"the record-type catalog could not be read ({ex.GetType().Name})", ex);
        }

        if (typeRow is not { } type || type.Id == Guid.Empty)
        {
            _logger.LogWarning(
                "Gate decision {EvaluationId}: the record-type catalog has no row for {CoreEntity}; the Decision Record is written " +
                "without a core pair.", g.EvaluationId, core.EntityType);
            return (null, derived);
        }

        return (new DecisionRecordCore(core.EntityType, core.RecordId, type.Id), derived);
    }

    /// <summary>One logging/metering point for every refusal: reason only, never fact values, values or reasons text.</summary>
    private void LogRefusal(Guid reviewId, DecisionRecordRefusedException ex)
    {
        if (ex.Reason == DecisionRecordRefusalReason.GateSkippedNoUnitTeam)
        {
            // D-144: the communication's owner or its owning business unit cannot be read. Not a failure (counted on the SKIP
            // counter, never ontology.writer.failures), but Warning, not Information: a decision went unrecorded and an operator can
            // act on it (uac-r2 K3: a visible, dimensioned skip). Ids and the resolver's refusal code only (D-123).
            _logger.LogWarning(DecisionRecordEvents.GateDecisionSkippedNoUnitTeam,
                "Gate decision {EvaluationId} not recorded: its communication's owner or owning business unit cannot be read "
                + "(ownerRefusal={OwnerRefusalCode}).", reviewId, ex.OwnerRefusalCode);
            OntologyWriterTelemetry.RecordSkip(ex.Reason);
            return;
        }

        if (ex.Reason == DecisionRecordRefusalReason.GateSkippedOwnerDisabled)
        {
            // D-144 as amended (uac-r2 K2): same shape as the skip above. A disabled owner cannot be assigned a record and does not recover on
            // a retry, so it is metered as a skip, not a failure, and logged at Warning so an operator can see it.
            _logger.LogWarning(DecisionRecordEvents.GateDecisionSkippedOwnerDisabled,
                "Gate decision {EvaluationId} not recorded: its communication's owning user is disabled.", reviewId);
            OntologyWriterTelemetry.RecordSkip(ex.Reason);
            return;
        }

        _logger.LogError(DecisionRecordEvents.WriteRefused, ex,
            "Decision Record writer refused review {ReviewId} (reason={Reason}, ownerRefusal={OwnerRefusalCode}).",
            reviewId, ex.Reason, ex.OwnerRefusalCode);
        OntologyWriterTelemetry.RecordFailure(ex.Reason);
    }

    /// <summary>
    /// THE record's one create, for every entry point (<see cref="WriteAsync(DecisionRecordRequest, DecisionRecordOwner?, CancellationToken)"/>,
    /// <see cref="WriteGateDecisionAsync"/> and, task 153, <see cref="WriteConditionClearedAsync"/>): the content (<see cref="Content"/>), the
    /// filing and owner (<see cref="FiledAndOwnedAsync"/>),
    /// the one <c>CreateAsync</c>, and the secure-owner read-back. A second create site is pinned out by
    /// <c>DecisionRecordContextImmutabilityTests</c>.
    /// </summary>
    private async Task<DecisionRecordWriteResult> CreateAsync(
        DecisionRecordRequest request, Plan plan, DecisionRecordOwner? resolvedOwner, CancellationToken ct)
    {
        var content = Content(request, plan);
        // Filed under its subject and owned (FiledAndOwnedAsync); the route's step-1f filing and owner are applied with no read.
        var (entity, recordOwner) = await FiledAndOwnedAsync(
            request, resolvedOwner, ct, plan.AlreadyDerived, plan.IsGate, plan.NoRequester).ConfigureAwait(false);
        var owner = recordOwner.Resolution;
        foreach (var (column, value) in content)
        {
            entity[column] = value;
        }

        Guid? secureTeamId = owner.IsSecureOwner ? owner.OwningTeamId : null;

        Guid recordId;
        try
        {
            recordId = await _writerClient.CreateAsync(entity, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Wrapped, not leaked: the commit route handles ONE exception type from this writer. The inner exception is kept.
            // A timeout can leave the row created; the route must not assume "failed" means "absent" without a check.
            var denied = IsDataverseAccessDenied(ex);
            throw new DecisionRecordRefusedException(
                denied ? DecisionRecordRefusalReason.DataverseAccessDenied : DecisionRecordRefusalReason.CreateFailed,
                denied
                    ? "Dataverse refused the Decision Record create for lack of a privilege; no record was written."
                    : "The Decision Record create failed; the record may not have been written.",
                innerException: ex);
        }

        if (secureTeamId is { } expectedTeam)
        {
            await VerifySecureOwnerAsync(recordId, expectedTeam, ct).ConfigureAwait(false);
        }

        // Task 042b (D-169): a gate record owned by an unfiled communication's own user (the only record with a Principal owner, D-144) is
        // also readable by the communication's internal sharees. After the create, never before; never throws (the record has landed). A
        // team-owned record never reaches this (D-123).
        if (recordOwner.Principal is { LogicalName: "systemuser", Id: var ownerUserId })
        {
            await _participantShares.ShareAtWriteAsync(recordId, SubjectOf(request).Id, ownerUserId).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Decision Record {RecordId} written for review {ReviewId}: class {RecordClass}, {SignalCount} Signal(s).",
            recordId, request.ReviewId, plan.Class, request.ResolvedSignalIds.Count);

        return new DecisionRecordWriteResult(recordId, plan.Class, request.ResolvedSignalIds)
        {
            SecureOwnerTeamId = secureTeamId,
        };
    }

    /// <summary>
    /// Task 043 / 142: resolves the Decision Record's owner over every parent it will sit under, with every owner refusal the write
    /// would give, and the record's whole FILING (every read the write would make: the record-type catalog, the stamp derivation and
    /// its column probe), so a parent the filing adds can never be left out of the answer and no filing read is left for after the
    /// sends. The commit route calls it before its first write and passes the answer to
    /// <see cref="WriteAsync(DecisionRecordRequest, DecisionRecordOwner?, CancellationToken)"/>, which applies it with no read. Writes
    /// nothing.
    /// </summary>
    /// <exception cref="DecisionRecordRefusedException">The owner was refused, the subject cannot be filed, or the resolver or the
    /// filing derivation failed (<see cref="DecisionRecordRefusalReason.OwnerResolutionFailed"/> /
    /// <see cref="DecisionRecordRefusalReason.FilingFailed"/>).</exception>
    public async Task<DecisionRecordOwner> ResolveOwnerAsync(DecisionRecordRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return (await FiledAndOwnedAsync(request, handed: null, ct).ConfigureAwait(false)).Owner;
        }
        catch (DecisionRecordRefusedException ex)
        {
            _logger.LogError(DecisionRecordEvents.WriteRefused, ex,
                "Decision Record writer refused the owner for review {ReviewId} (reason={Reason}, ownerRefusal={OwnerRefusalCode}).",
                request.ReviewId, ex.Reason, ex.OwnerRefusalCode);
            OntologyWriterTelemetry.RecordFailure(ex.Reason);
            throw;
        }
    }

    /// <summary>
    /// The review's owner, in D-134's order. FIRST every parent (<paramref name="context"/>, <see cref="OwnershipContextOf"/>): a refusal
    /// refuses and a secure answer stands (item 7, binding: a secure parent wins). Only when that answer is ordinary AND the review has
    /// no core (a core filing always takes precedence) AND a personal To Do's Assigned To named one enabled user, the owner is that
    /// user's business-unit team (<see cref="RecordOwnershipContext.ForUserTeam"/>), with the same refusals. Otherwise the parents' answer.
    /// </summary>
    private async Task<DecisionRecordOwner> ResolveRequestOwnerAsync(
        DecisionRecordRequest request, RecordOwnershipContext context, DecisionRecordFiling filing, CancellationToken ct)
    {
        var (owner, principal) = await ResolveOwnerCoreAsync(context, requireTeam: true, ct).ConfigureAwait(false);
        if (!owner.IsSecureOwner && principal is null && request.Core is null && request.AssigneeSystemUserId is { } assignee && assignee != Guid.Empty)
        {
            (owner, _) = await ResolveOwnerCoreAsync(
                RecordOwnershipContext.ForUserTeam(assignee) with { RequestedBy = context.RequestedBy }, requireTeam: true, ct).ConfigureAwait(false);
        }

        return new DecisionRecordOwner(owner, context.Parents, request.AssigneeSystemUserId, filing, principal);
    }

    /// <summary>
    /// The ownership question for a review (D-39 / D-129, uac-r2 F1, contract C3): EVERY parent the record sits under, as one list:
    /// the core record first (it decides an ordinary record's business unit), then the record's subject (<see cref="SubjectOf"/>; a
    /// no-core review's primary parent), every core its filing derived from that subject (<paramref name="derivedCores"/>, the stamp
    /// the record carries), and the subject of every Signal the record resolves, primary and also-resolve. Duplicates collapse
    /// (<see cref="RecordOwnershipContext.ForParents"/>). The resolver reads them all (secure-if-any) and the list is not bounded or
    /// truncated here; the route caps the Signals it resolves (25), so nothing is dropped. A user-owned subject (a personal To Do) is
    /// the resolver's call, never a writer-picked owner (D-134's assignee is applied after this answer, by
    /// <see cref="ResolveRequestOwnerAsync"/>, and only when it is ordinary). A parent whose table is not an ownership parent is
    /// ignored by the resolver itself, as for every other writer. The deciding user is the person who asked
    /// (<see cref="RecordOwnershipContext.RequestedBy"/>, #1783 / D-135): stamped as <c>sprk_createdbyperson</c>, deciding nothing
    /// about the owner. Task 153 (adversarial pass 1 F3): with <paramref name="noRequester"/> (a condition-cleared closure, written by the
    /// nightly job for nobody) no person is the requester, so <c>sprk_createdbyperson</c> stays empty as for every background-job writer
    /// (<see cref="RecordCreatorPerson"/>); the person who did the work is named only in <c>sprk_confirmedby</c>.
    /// </summary>
    internal static RecordOwnershipContext OwnershipContextOf(
        DecisionRecordRequest request, IReadOnlyList<CoreAncestorStamp> derivedCores, bool noRequester = false)
    {
        ArgumentNullException.ThrowIfNull(derivedCores);
        var subject = SubjectOf(request);

        var parents = new List<RecordOwnershipParent?>();
        if (request.Core is { } core)
        {
            parents.Add(new RecordOwnershipParent(core.EntityLogicalName, core.RecordId));
        }

        parents.Add(new RecordOwnershipParent(subject.Entity, subject.Id));
        foreach (var derived in derivedCores)
        {
            parents.Add(new RecordOwnershipParent(derived.EntityType, derived.RecordId));
        }

        foreach (var signalSubject in request.SignalSubjects ?? [])
        {
            parents.Add(new RecordOwnershipParent(signalSubject.Entity, signalSubject.Id));
        }

        // UAC task 194b fix round R (B3): the writer's own order IS sprk_decisionrecord's rule — the closed opt-out.
        return RecordOwnershipContext.ForParentsInWriterOrder(parents) with
        {
            RequestedBy = noRequester ? null : RecordRequester.Of(request.ConfirmedByUserId),
        };
    }

    /// <summary>
    /// Task 142 (contract C1): the record the Decision Record is filed under: <see cref="DecisionRecordRequest.ItemSubject"/>, else the
    /// core record. No I/O.
    /// </summary>
    /// <exception cref="DecisionRecordRefusedException">There is no subject (<see cref="DecisionRecordRefusalReason.OwnerMissing"/>), or
    /// its table has no subject lookup on this table (<see cref="DecisionRecordRefusalReason.SubjectUnsupported"/>).</exception>
    internal static DecisionRecordRef SubjectOf(DecisionRecordRequest request)
    {
        DecisionRecordRef? subject = request.ItemSubject is { } item && !string.IsNullOrWhiteSpace(item.Entity) && item.Id != Guid.Empty
            ? item
            : request.Core is { } core ? new DecisionRecordRef(core.EntityLogicalName, core.RecordId) : null;
        if (subject is null)
        {
            throw new DecisionRecordRefusedException(
                DecisionRecordRefusalReason.OwnerMissing,
                "A review of an item with no core record needs the item's subject record to resolve its Decision Record owner; nothing was written.");
        }

        if (!SubjectLookupByEntity.ContainsKey(subject.Entity))
        {
            throw new DecisionRecordRefusedException(
                DecisionRecordRefusalReason.SubjectUnsupported,
                "The decision's subject is not a record a Decision Record can be filed under; nothing was written.");
        }

        return subject;
    }

    /// <summary>
    /// Task 142: the ONE place a Decision Record entity is built: filed under its subject (<see cref="FileUnderSubjectAsync"/>) and owned
    /// by the resolver's answer over every parent (<see cref="OwnershipContextOf"/>), applied with <c>ApplyTo</c> (ownerid and, for the
    /// deciding user, <c>sprk_createdbyperson</c>). With <paramref name="handed"/> (the route's step-1f answer) for the same subject, its
    /// filing is applied as computed then, with NO Dataverse read: the route resolves at 1f so that a read fault refuses before any send
    /// (task 043). A handed filing that does not answer the request (another subject, or an owner answer for other parents or another
    /// assignee) is REFUSED (<see cref="DecisionRecordRefusalReason.FilingFailed"/>), never re-read: that would be a Dataverse read after
    /// the sends. With no handed filing (a non-route caller) the owner is used only when it answered for exactly these parents and
    /// assignee; otherwise it is asked again.
    /// </summary>
    private async Task<(Entity Record, DecisionRecordOwner Owner)> FiledAndOwnedAsync(
        DecisionRecordRequest request, DecisionRecordOwner? handed, CancellationToken ct, CoreAncestorResult? alreadyDerived = null, bool gate = false,
        bool noRequester = false)
    {
        var subject = SubjectOf(request);
        var entity = new Entity(Table);

        DecisionRecordFiling filing;
        if (handed?.Filing is { } computed)
        {
            // The route's contract (fix round 2, K1): a filing handed in at step 1f is for THIS subject, applied with no read. If it is
            // not, re-filing would read Dataverse after the sends began, so it refuses (fail closed) instead of silently re-reading.
            if (computed.Subject != subject)
            {
                throw HandedFilingDoesNotAnswer(request, "the handed filing is for a different subject");
            }

            foreach (var (column, value) in computed.Columns)
            {
                entity[column] = value;
            }

            filing = computed;
        }
        else
        {
            var derivedCores = await FileUnderSubjectAsync(entity, subject, alreadyDerived, ct).ConfigureAwait(false);
            filing = new DecisionRecordFiling(
                subject, entity.Attributes.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal), derivedCores);
        }

        // D-33/D-34/D-39 as amended by D-129 (records are owned by TEAMS), uac-r2 F1 and contract C3: the owner is uac-r2's resolver
        // asked with EVERY parent the record sits under, exactly as returned (secure-if-any). owningbusinessunit is never set here (it
        // derives from the owner); a refusal writes nothing; KeepCreator is not requested, so an Unchanged answer is refused.
        var context = OwnershipContextOf(request, filing.DerivedCores, noRequester);
        // Task 153: the gate is named by its plan (ValidateGate), not by an empty confirmer: a condition-cleared record whose person is
        // unknown also has no confirmer, and must be owned like a review, never through the gate's unfiled-communication rule.
        if (gate)
        {
            // Task 042 (security fails closed): the gate asks as the CONTENT of a communication (uac-r2's ContentOf shape). The resolver
            // applies KeepCreatorUnlessTargetIsTeamOwned to the FIRST parent it is asked about, which is the core when the communication has
            // one: then the check is made against the CORE, not against the communication. A core record is never content (owner decision
            // 2026-10-29): it stays among the parents, the record resolves record-first over every parent (secure-if-any), and a work
            // assignment with no owning team that is filed answers with its matter's (then project's) business unit, as every other path
            // does; any other core record, and an unfiled work assignment, answers with its own business unit. The D-144 owner below never
            // applies to a core. With no core the first parent is the communication itself: if it is not team-owned the resolver
            // reads ITS OWN filing (filed -> secure-if-any over what it is filed under; UNFILED -> E1, owner round 10 item 8: it keeps its
            // creator and its privacy rests on per-participant shares -> Unchanged). Task 149 / D-144 (owner decision): that Unchanged is
            // no longer a refusal. D-144 as amended (owner ruling on uac-r2 F1): a decision about an unfiled communication is owned by the
            // communication's own owning user, never by its business unit's default team (that would show its subject, privilege flag and
            // snapshot to every colleague in the unit). ResolveOwnerCoreAsync asks the resolver once more, as a plain parent, only for
            // secure-if-any (the Secure Record unit's named team still wins); a missing or unreadable owner is the by-design skip. The
            // review route never sets this.
            context = context with { KeepCreatorUnlessTargetIsTeamOwned = true };
        }

        DecisionRecordOwner owner;
        if (handed is not null && handed.Answers(context.Parents, request.AssigneeSystemUserId))
        {
            owner = handed;
        }
        else
        {
            if (handed?.Filing is not null)
            {
                // Same contract as above: a handed filing whose owner answer is for other parents (or another assignee) cannot be
                // re-asked after the sends; the route never takes this path, so reaching it is a bug and fails closed.
                throw HandedFilingDoesNotAnswer(request, "the handed owner answer is for other parents or another assignee");
            }

            if (handed is not null)
            {
                _logger.LogWarning(
                    "Decision Record owner for review {ReviewId} was resolved for other parents than the record is filed under; asking again.",
                    request.ReviewId);
            }

            owner = await ResolveRequestOwnerAsync(request, context, filing, ct).ConfigureAwait(false);
        }

        if (!owner.Resolution.IsOwned && owner.Principal is null)
        {
            throw new DecisionRecordRefusedException(
                DecisionRecordRefusalReason.OwnerRefused,
                "The resolved owner of the Decision Record is not a team; nothing was written.",
                owner.Resolution.RefusalCode ?? RecordOwnerRefusal.NoOwnerSource);
        }

        owner.Resolution.ApplyTo(entity);
        if (owner.Principal is { } principal)
        {
            entity["ownerid"] = principal; // D-144 as amended: the communication's own owning user; D-169: shared after the create (CreateAsync)
        }

        return (entity, owner);
    }

    /// <summary>
    /// Task 142 (contract C1/C2; 140-schema-delta section 9 K2): the subject lookup and the regarding pair through the SHARED regarding
    /// builder, THEN the core-ancestor stamp from the same subject. Never the other way round: the builder's ADR-024 guard refuses a row
    /// that already carries any regarding lookup, and the stamp columns are regarding lookups. Returns every core the stamp derived (the
    /// subject itself when it is a core record): they are parents of the owner question. A derived core this table has no column for is
    /// logged by the resolver and not a reason to refuse (its documented contract); every derived core is still an owner parent.
    /// </summary>
    /// <exception cref="DecisionRecordRefusedException"><see cref="DecisionRecordRefusalReason.FilingFailed"/>: the catalog read or the
    /// stamp derivation failed (fail closed, nothing written).</exception>
    private async Task<IReadOnlyList<CoreAncestorStamp>> FileUnderSubjectAsync(
        Entity entity, DecisionRecordRef subject, CoreAncestorResult? alreadyDerived, CancellationToken ct)
    {
        try
        {
            await _regarding
                .ApplySubjectLookupAndPairAsync(entity, subject.Entity, subject.Id, regardingDisplayName: null, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw FilingFailed(subject, $"the subject lookup and pair could not be built ({ex.GetType().Name})", ex);
        }

        var stamp = await _coreAncestors.StampAsync(entity, subject.Entity, subject.Id, ct, alreadyDerived).ConfigureAwait(false);
        if (!stamp.Succeeded)
        {
            // A data condition (two different roots of one table, or a subject that no longer exists) is a refusal a retry does not
            // change; anything else is a fault (503). Neither text names a record.
            throw stamp.IsPermanent
                ? new DecisionRecordRefusedException(
                    DecisionRecordRefusalReason.SubjectUnfileable,
                    "The decision's subject cannot be filed: the records it sits under conflict, or it no longer exists; nothing was written.")
                : FilingFailed(subject, stamp.Error);
        }

        return stamp.Stamps;
    }

    /// <summary>
    /// UAC task 194b fix round (F3): the parents this writer asks the owner question over for <paramref name="request"/> — through
    /// the write's own filing and owner step (<see cref="FiledAndOwnedAsync"/>: the shared regarding builder, the derived cores,
    /// <see cref="DecisionRecordRequest.SignalSubjects"/>, then the resolver) — with no write. The parity seam: tests compare it
    /// with the reconciler's question for the existing row.
    /// </summary>
    internal async Task<IReadOnlyList<RecordOwnershipParent>> OwnershipParentsForAsync(DecisionRecordRequest request, CancellationToken ct)
    {
        var (_, owner) = await FiledAndOwnedAsync(request, handed: null, ct, noRequester: true).ConfigureAwait(false);
        return owner.Parents;
    }

    /// <summary>Fix round 2 (K1): a handed filing that does not answer the request. Logged at Error (a caller bug, not a data
    /// condition); the refusal names no record and nothing has been read or written for it.</summary>
    private DecisionRecordRefusedException HandedFilingDoesNotAnswer(DecisionRecordRequest request, string why)
    {
        _logger.LogError(
            "Decision Record writer for review {ReviewId} was handed a filing that does not answer the request ({Why}); refusing without re-reading (fail closed).",
            request.ReviewId, why);
        return new DecisionRecordRefusedException(
            DecisionRecordRefusalReason.FilingFailed,
            "The Decision Record's handed-in filing does not match the request, and it is not re-read once the commit has begun; nothing was written.");
    }

    private static DecisionRecordRefusedException FilingFailed(DecisionRecordRef subject, string? detail, Exception? inner = null) =>
        new(DecisionRecordRefusalReason.FilingFailed,
            $"The Decision Record could not be filed under its {subject.Entity} subject ({detail ?? "no detail"}); nothing was written.",
            innerException: inner);

    /// <param name="context">The ownership question.</param>
    /// <param name="requireTeam">True for every Decision Record whose owner is the resolver's team: any answer that names no team (a
    /// refusal, or an Unchanged the context never asked for) refuses HERE, before the route's first write. The one exception is the gate
    /// decision about an unfiled communication, whose owner is the communication's own owning user (D-144 as amended).</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The resolver's answer and, only for an unfiled gate communication, the owning principal to apply instead of a team.</returns>
    private async Task<(RecordOwnerResolution Resolution, EntityReference? Principal)> ResolveOwnerCoreAsync(
        RecordOwnershipContext context, bool requireTeam, CancellationToken ct)
    {
        var owner = await AskOwnerAsync(context, ct).ConfigureAwait(false);

        // Task 149 / D-144 AS AMENDED (owner ruling on uac-r2 F1). Only the gate path asks as the content of a communication
        // (KeepCreatorUnlessTargetIsTeamOwned), and its Unchanged answer means the communication is not team-owned and filed under
        // nothing (E1: it keeps its creator). The decision is then owned by the communication's OWN owning principal, never by its
        // business unit's default team (a default-team owner would show the communication's subject, privilege flag and snapshot to every
        // colleague in the unit). Secure-if-any still comes first, and it is uac-r2's predicate, not this writer's: the same parents are
        // asked once more as a plain parent ONLY to learn whether the communication sits in the Secure Record business unit. A secure
        // answer is used as returned (the named Secure team); an ordinary team answer is DISCARDED, never applied.
        var gate = context.KeepCreatorUnlessTargetIsTeamOwned;
        var askedAsPlainParent = false;
        EntityReference? principal = null;
        if (gate && owner.Outcome == RecordOwnerOutcome.Unchanged)
        {
            askedAsPlainParent = true;
            var secureCheck = await AskOwnerAsync(context with { KeepCreatorUnlessTargetIsTeamOwned = false }, ct).ConfigureAwait(false);
            if (secureCheck.IsOwned && !secureCheck.IsSecureOwner)
            {
                principal = await ReadCommunicationOwnerAsync(context, ct).ConfigureAwait(false);
            }
            else if (secureCheck.IsRefused && secureCheck.RefusalCode == RecordOwnerRefusal.NoDefaultOwnerTeam)
            {
                // The resolver decides "secure" before it looks for a default team, so this answer is "ordinary, and the unit has no
                // default team": the team is not needed any more, the communication's owner is.
                principal = await ReadCommunicationOwnerAsync(context, ct).ConfigureAwait(false);
            }
            else
            {
                owner = secureCheck; // secure (the named team), or a refusal handled below
            }
        }

        if (owner.IsRefused)
        {
            // D-144: the communication's business unit (or, for a filing the first ask could not read, a parent) cannot be read: by design
            // nothing is written, and it is a SKIP, not a failure. Limited to what D-144 names: the answer for a communication that stands
            // alone (the secure check above, or a first ask whose only parent is the communication). A core record whose unit cannot be
            // read stays a real, fail-closed refusal.
            var skip = gate
                && owner.RefusalCode is RecordOwnerRefusal.ParentUnresolved or RecordOwnerRefusal.NoDefaultOwnerTeam
                && (askedAsPlainParent || context.Parents.All(p => p.EntityLogicalName == GateSubjectEntity));
            throw new DecisionRecordRefusedException(
                skip ? DecisionRecordRefusalReason.GateSkippedNoUnitTeam : DecisionRecordRefusalReason.OwnerRefused,
                skip
                    ? "The communication's owner, or its owning business unit (needed to rule out the Secure Record unit), cannot be read, so nobody can own a Decision Record about it; nothing was written."
                    : "The ownership resolver refused an owner for the Decision Record; nothing was written.",
                owner.RefusalCode ?? RecordOwnerRefusal.NoOwnerSource);
        }

        // A secure answer with no team is malformed: falling through would write an ordinary-owned child of a secure root.
        if (owner.IsSecureOwner && !owner.IsOwned)
        {
            throw new DecisionRecordRefusedException(
                DecisionRecordRefusalReason.OwnerRefused,
                "The ownership resolver marked the record secure but named no owner team; nothing was written.",
                RecordOwnerRefusal.NoOwnerSource);
        }

        if (requireTeam && !owner.IsOwned && principal is null)
        {
            throw new DecisionRecordRefusedException(
                DecisionRecordRefusalReason.OwnerRefused,
                "The ownership resolver named no owner team for the Decision Record; nothing was written.",
                owner.RefusalCode ?? RecordOwnerRefusal.NoOwnerSource);
        }

        return (owner, principal);
    }

    /// <summary>The alias of the owner's <c>systemuser</c> link in <see cref="ReadCommunicationOwnerAsync"/>.</summary>
    private const string OwnerAlias = "owner";

    /// <summary>
    /// Task 149 / D-144 as amended: the owning principal of the unfiled communication a gate decision is about, as READ from the row (the
    /// shared sysadmin reader, which sees any owner; the writer identity cannot be assumed to). Only a user owner is accepted: a
    /// communication that is not team-owned kept a user (or application) owner, which Dataverse reports as a <c>systemuser</c>. Anything
    /// else (no row, no owner, a team, an empty id) is "missing or unreadable": the by-design skip, never a fall-through to any team.
    /// </summary>
    /// <exception cref="DecisionRecordRefusedException"><see cref="DecisionRecordRefusalReason.GateSkippedNoUnitTeam"/> when there is no
    /// readable user owner; <see cref="DecisionRecordRefusalReason.GateSkippedOwnerDisabled"/> when that user is disabled;
    /// <see cref="DecisionRecordRefusalReason.OwnerResolutionFailed"/> when the read faulted.</exception>
    private async Task<EntityReference> ReadCommunicationOwnerAsync(RecordOwnershipContext context, CancellationToken ct)
    {
        var communicationId = context.Parents.First(p => p.EntityLogicalName == GateSubjectEntity).RecordId;
        EntityReference? owner;
        bool disabled;
        try
        {
            var query = new QueryExpression(GateSubjectEntity)
            {
                ColumnSet = new ColumnSet("ownerid"),
                TopCount = 1,
                NoLock = true,
            };
            query.Criteria.AddCondition($"{GateSubjectEntity}id", ConditionOperator.Equal, communicationId);
            // The owner's isdisabled comes back with the SAME read (a left outer link to systemuser: a team owner still returns the row, with
            // no alias value), so a disabled owner costs no second request.
            var ownerLink = query.AddLink("systemuser", "ownerid", "systemuserid", JoinOperator.LeftOuter);
            ownerLink.EntityAlias = OwnerAlias;
            ownerLink.Columns = new ColumnSet("isdisabled");
            var row = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities?.FirstOrDefault();
            owner = row?.GetAttributeValue<EntityReference>("ownerid");
            // Issue #1576 (fail closed): only an explicit isdisabled = false is an active owner. A user owner whose linked row carries
            // no value is not proof of an active user, so it is the disabled skip, never an assignment. (A team owner also has no value;
            // `disabled` is consulted only for a systemuser owner below.)
            disabled = row?.GetAttributeValue<AliasedValue>($"{OwnerAlias}.isdisabled")?.Value is not false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DecisionRecordRefusedException(
                DecisionRecordRefusalReason.OwnerResolutionFailed,
                "The communication's owner could not be read, so the Decision Record owner is unknown; nothing was written.",
                innerException: ex);
        }

        if (owner is { Id: var id, LogicalName: "systemuser" } && id != Guid.Empty)
        {
            if (disabled)
            {
                // Uac-r2 K2: Dataverse refuses to assign a record to a disabled user, and a retry meets the same condition. Skip by design;
                // never fall through to a team.
                throw new DecisionRecordRefusedException(
                    DecisionRecordRefusalReason.GateSkippedOwnerDisabled,
                    "The communication's owning user is disabled, so nobody can own a Decision Record about it; nothing was written.");
            }

            return owner;
        }

        throw new DecisionRecordRefusedException(
            DecisionRecordRefusalReason.GateSkippedNoUnitTeam,
            "The communication has no readable user owner, so nobody can own a Decision Record about it; nothing was written.",
            RecordOwnerRefusal.ParentUnresolved);
    }

    /// <summary>One question to the ownership resolver; a fault in it is not an answer, so it becomes the writer's one exception type.</summary>
    private async Task<RecordOwnerResolution> AskOwnerAsync(RecordOwnershipContext context, CancellationToken ct)
    {
        try
        {
            return await _ownership.ResolveOwnerAsync(context, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A Dataverse fault in the resolver is not an answer (it propagates there); here it becomes the writer's one
            // exception type. Nothing was written.
            throw new DecisionRecordRefusedException(
                DecisionRecordRefusalReason.OwnerResolutionFailed,
                "The ownership resolver failed, so the Decision Record owner is unknown; nothing was written.",
                innerException: ex);
        }
    }

    /// <summary>
    /// Task 043: every refusal <see cref="WriteAsync(DecisionRecordRequest, CancellationToken)"/> can give WITHOUT I/O (validation, record
    /// class, column sizes, the subject the record is filed under), for a review projected before its first write. The commit route calls
    /// it before any action runs, because by the time <see cref="WriteAsync(DecisionRecordRequest, CancellationToken)"/> refuses, the
    /// review's emails have gone. Writes and reads nothing; logs nothing.
    /// </summary>
    /// <exception cref="DecisionRecordRefusedException">The review as projected would be refused.</exception>
    public void Preflight(DecisionRecordRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var plan = Validate(request);
        _ = Content(request, plan);
        _ = SubjectOf(request);
    }

    /// <summary>
    /// After a Secure-team create, reads <c>owningteam</c> back. A mismatch cannot be corrected by the writer (the row is append-only except the live stamp columns) and a
    /// failure here must not make the route retry into a duplicate, so it is logged at Error and metered, and the record id is
    /// still returned: uac-r2's secure-child reconcile re-owns the row (the table is registered in its lineage).
    /// </summary>
    private async Task VerifySecureOwnerAsync(Guid recordId, Guid expectedTeam, CancellationToken ct)
    {
        try
        {
            var row = await _writerClient.RetrieveAsync(Table, recordId, new[] { "owningteam" }, ct).ConfigureAwait(false);
            var actual = row.GetAttributeValue<EntityReference>("owningteam")?.Id;
            if (actual == expectedTeam)
            {
                return;
            }

            _logger.LogError(DecisionRecordEvents.SecureOwnerMismatch,
                "Decision Record {RecordId} was created but owningteam is {ActualTeam}, not the Secure Record Owners team {ExpectedTeam}.",
                recordId, actual, expectedTeam);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(DecisionRecordEvents.SecureOwnerMismatch, ex,
                "Decision Record {RecordId} was created but its owner could not be read back to verify.", recordId);
        }

        OntologyWriterTelemetry.RecordFailure(DecisionRecordRefusalReason.SecureOwnerMismatch);
    }

    /// <summary>
    /// True for a Dataverse privilege-denied fault (0x80040220 / 0x80040299), by typed fault code first, message fallback second.
    /// Same classification as <c>SignalWriter</c>'s private helper; copied rather than made shared because lane 037 is editing
    /// that file (consolidate afterwards).
    /// </summary>
    private static bool IsDataverseAccessDenied(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is System.ServiceModel.FaultException<OrganizationServiceFault> fault
                && (fault.Detail?.ErrorCode == unchecked((int)0x80040220) || fault.Detail?.ErrorCode == unchecked((int)0x80040299)))
            {
                return true;
            }

            var m = e.Message;
            if (!string.IsNullOrEmpty(m) && (
                m.Contains("0x80040220", StringComparison.OrdinalIgnoreCase) ||
                m.Contains("0x80040299", StringComparison.OrdinalIgnoreCase) ||
                (m.Contains("does not have", StringComparison.OrdinalIgnoreCase) && m.Contains("right", StringComparison.OrdinalIgnoreCase)) ||
                m.Contains("Privilege Check", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    // ── Validation and typing ───────────────────────────────────────────────────────────────────────────────────

    /// <param name="Name">Task 042: the record's name when the gate path sets it; null derives it from the class and the taken labels.</param>
    /// <param name="SkippedOutcome">Task 042: the outcome <c>sprk_steps</c> shows for a step that was not taken ("Skipped" for a review;
    /// "Denied" for a gate deny).</param>
    /// <param name="AlreadyDerived">Task 042 (K2): the subject's stamps the gate path already derived to find the core, handed to the filing
    /// step so the derivation is not paid for twice; null derives there.</param>
    /// <param name="IsGate">Task 153: true only for a communications-gate decision (<see cref="ValidateGate"/>), the one record whose owner
    /// question is asked as the content of a communication (<see cref="FiledAndOwnedAsync"/>).</param>
    /// <param name="NoRequester">Task 153 (adversarial pass 1 F3): true only for a condition-cleared closure
    /// (<see cref="ValidateConditionCleared"/>): the nightly job writes it for nobody, so the owner question names no requester and
    /// <c>sprk_createdbyperson</c> stays empty (<see cref="OwnershipContextOf"/>).</param>
    private sealed record Plan(
        RecordedDecisionClass Class,
        int Outcome,
        IReadOnlyList<(DecisionRecordStep Step, DecisionActionDefinition Def)> Steps,
        IReadOnlyList<(DecisionRecordStep Step, DecisionActionDefinition Def)> Taken,
        string? Name = null,
        string SkippedOutcome = "Skipped",
        CoreAncestorResult? AlreadyDerived = null,
        bool IsGate = false,
        bool NoRequester = false);

    private static Plan Validate(DecisionRecordRequest r)
    {
        static DecisionRecordRefusedException Invalid(string message) =>
            new(DecisionRecordRefusalReason.ReviewInvalid, message);

        // sprk_factsnapshot is MANDATORY (D-2): a review with no facts is refused, not written empty.
        if (r.FactValues is null || r.FactValues.Count == 0)
        {
            throw new DecisionRecordRefusedException(
                DecisionRecordRefusalReason.FactSnapshotMissing,
                "sprk_factsnapshot is mandatory: the review carries no fact values, so no Decision Record was written.");
        }

        if (r.ReviewId == Guid.Empty) throw Invalid("ReviewId is required.");
        if (r.PolicyVersionId == Guid.Empty) throw Invalid("PolicyVersionId is required.");
        if (r.ConfirmedByUserId == Guid.Empty) throw Invalid("ConfirmedByUserId is required.");
        // sprk_proposedaction is required and is the offered labels: a review that offered nothing has nothing to record.
        if (r.Steps is not { Count: > 0 }) throw Invalid("Steps is required (every offered action, taken or skipped).");
        if (r.FollowOns is null) throw Invalid("FollowOns is required (empty when no Next step was created).");
        if (r.ResolvedSignalIds is not { Count: > 0 } || r.ResolvedSignalIds.Any(id => id == Guid.Empty)
            || r.ResolvedSignalIds.Distinct().Count() != r.ResolvedSignalIds.Count)
        {
            throw Invalid("ResolvedSignalIds must name at least one distinct Signal.");
        }

        if (r.GateTier is { Length: > GateTierMaxLength }) throw Invalid("GateTier is too long for sprk_gatetier.");
        if (r.Reason is { Length: > ReasonMaxLength }) throw Invalid("Reason is too long for sprk_reason.");

        if (r.Core is { } core)
        {
            if (core.RecordId == Guid.Empty || core.RecordTypeRefId == Guid.Empty
                || !CoreAncestorResolver.CoreRecordEntities.Contains(core.EntityLogicalName, StringComparer.Ordinal))
            {
                throw new DecisionRecordRefusedException(
                    DecisionRecordRefusalReason.CoreUnsupported,
                    "The core record is not one of the access-control core record types, or is missing its id or type reference.");
            }
        }

        var steps = new List<(DecisionRecordStep, DecisionActionDefinition)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in r.Steps)
        {
            if (step is null || !DecisionActionCatalog.TryGet(step.ActionCode, out var def) || def.PlanLane is null)
            {
                throw new DecisionRecordRefusedException(
                    DecisionRecordRefusalReason.RecordClassUnderivable,
                    "An offered action is not a plan action in the catalog, so the record class cannot be derived.");
            }

            if (!seen.Add(def.Code)) throw Invalid($"Action '{def.Code}' is listed twice.");
            if (step.Taken && string.IsNullOrWhiteSpace(step.Outcome)) throw Invalid($"Taken action '{def.Code}' has no outcome.");
            // Section 0.3: a skipped action did nothing, so it may not carry an outcome or written rows.
            if (!step.Taken && (!string.IsNullOrWhiteSpace(step.Outcome) || step.Written is { Count: > 0 }))
                throw Invalid($"Skipped action '{def.Code}' carries an outcome or written rows.");
            steps.Add((step, def));
        }

        var taken = steps.Where(s => s.Item1.Taken).ToList();
        if (DecisionActionCatalog.FirstConflict(taken.Select(t => t.Item2.Code)) is { } clash)
        {
            throw Invalid($"Actions '{clash.First}' and '{clash.Second}' exclude each other and cannot both be taken.");
        }

        foreach (var followOn in r.FollowOns)
        {
            if (followOn is null || followOn.Id == Guid.Empty || string.IsNullOrWhiteSpace(followOn.Entity)
                || !DecisionActionCatalog.TryGet(followOn.ActionCode, out var nextDef) || !nextDef.IsNextStep)
            {
                throw new DecisionRecordRefusedException(
                    DecisionRecordRefusalReason.RecordClassUnderivable,
                    "A follow-on is not a Next-step creator in the catalog, or has no created row, so the record class cannot be derived.");
            }
        }

        var unnamed = r.UnnamedNextSteps ?? [];
        foreach (var code in unnamed)
        {
            if (!DecisionActionCatalog.TryGet(code, out var unnamedDef) || !unnamedDef.IsNextStep)
            {
                throw new DecisionRecordRefusedException(
                    DecisionRecordRefusalReason.RecordClassUnderivable,
                    "An unnamed Next step is not a Next-step creator in the catalog, so the record class cannot be derived.");
            }
        }

        var nextSteps = r.FollowOns.Count + unnamed.Count;
        var nothingTaken = taken.Count == 0 && nextSteps == 0;
        if (r.Denied && !nothingTaken) throw Invalid("A denied review cannot also have taken an action or created a Next step.");
        if (nothingTaken && string.IsNullOrWhiteSpace(r.Reason))
        {
            throw Invalid("A review that took nothing needs a reason.");
        }

        // Section 0.3: the gate tier is "the strictest among taken actions", so nothing taken means there is none to record.
        if (nothingTaken && !string.IsNullOrWhiteSpace(r.GateTier))
        {
            throw Invalid("A review that took nothing cannot carry a gate tier.");
        }

        // The class comes from the catalog (D-17, FR-50): Dismissal when nothing was taken; Judgement when any taken action or any
        // Next step is Judgement (every Next-step creator is); Routine otherwise.
        var recordClass = nothingTaken
            ? RecordedDecisionClass.Dismissal
            : nextSteps > 0 || taken.Any(t => t.Item2.RecordClass == DecisionRecordClass.Judgement)
                ? RecordedDecisionClass.Judgement
                : RecordedDecisionClass.Routine;

        // sprk_decisionoutcome: what the human decided at the gate. NOT sprk_servicerequest.sprk_disposition.
        var outcome = r.Denied ? 100000001 : nothingTaken ? 100000002 : 100000000;

        return new Plan(recordClass, outcome, steps, taken);
    }

    private static string Memo(string column, int maxLength, object value)
    {
        var json = JsonSerializer.Serialize(value, Json);
        if (json.Length > maxLength)
        {
            throw new DecisionRecordRefusedException(
                DecisionRecordRefusalReason.ReviewInvalid,
                $"{column} would be {json.Length} characters; the column holds {maxLength}. Nothing was written.");
        }

        return json;
    }

    // ── Content ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The record's content columns (everything but its filing, stamp and owner), as one column → value map: the create is
    /// built from it in <see cref="WriteAsync(DecisionRecordRequest, DecisionRecordOwner?, CancellationToken)"/>, and <see cref="Preflight"/>
    /// builds it only for its refusals. D-34: the context columns (core pair, policy version, fact snapshot) are written here, at
    /// create, and by no later path (the writer has no update; pinned by <c>DecisionRecordContextImmutabilityTests</c>).</summary>
    private Dictionary<string, object> Content(DecisionRecordRequest r, Plan plan)
    {
        var e = new Dictionary<string, object>(StringComparer.Ordinal);

        var proposed = string.Join("; ", plan.Steps.Select(s => s.Def.Label));
        if (proposed.Length > ProposedActionMaxLength) proposed = proposed[..(ProposedActionMaxLength - 3)] + "...";
        e["sprk_proposedaction"] = proposed;

        var takenLabels = string.Join(", ", plan.Taken.Select(t => t.Def.Label));
        var name = plan.Name
            ?? (plan.Class == RecordedDecisionClass.Dismissal
                ? "Dismissal"
                : takenLabels.Length > 0 ? $"{plan.Class}: {takenLabels}" : $"{plan.Class}: next steps");
        e["sprk_name"] = name.Length > NameMaxLength ? name[..(NameMaxLength - 3)] + "..." : name;

        // A review always has both (Validate refuses an empty id); the gate path (task 042) has neither -- no policy version (its rules
        // are sprk_communicationrule rows) and no confirming human (D-129) -- and leaves the columns null (live: both nullable).
        if (r.PolicyVersionId != Guid.Empty)
        {
            e["sprk_policyversion"] = new EntityReference("sprk_policyversion", r.PolicyVersionId);
        }

        e["sprk_recordclass"] = new OptionSetValue((int)plan.Class);
        e["sprk_decisionoutcome"] = new OptionSetValue(plan.Outcome);
        if (r.ConfirmedByUserId != Guid.Empty)
        {
            e["sprk_confirmedby"] = new EntityReference("systemuser", r.ConfirmedByUserId);
        }
        e["sprk_decidedon"] = (r.DecidedOn ?? _timeProvider.GetUtcNow()).UtcDateTime;
        e["sprk_privilegeflagged"] = r.PrivilegeFlagged; // copied forward, never branched on (ADR-015)

        if (!string.IsNullOrWhiteSpace(r.Reason)) e["sprk_reason"] = r.Reason;

        // sprk_action (the nullable lookup) is never set: it stays null on deny and for every catalog action (#6).
        if (plan.Taken.Count > 0)
        {
            var first = plan.Taken[0].Def.Code;
            e["sprk_actioncode"] = first.Length > ActionCodeMaxLength ? first[..ActionCodeMaxLength] : first;
        }

        if (!string.IsNullOrWhiteSpace(r.GateTier)) e["sprk_gatetier"] = r.GateTier;

        e["sprk_factsnapshot"] = Memo("sprk_factsnapshot", FactSnapshotMaxLength, new
        {
            facts = r.FactValues,
            decidedBy = new { role = r.DeciderRole },
            because = r.Because,
        });

        var envelope = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["reviewId"] = r.ReviewId.ToString("D", CultureInfo.InvariantCulture),
            ["resolvedSignalIds"] = r.ResolvedSignalIds.Select(id => id.ToString("D", CultureInfo.InvariantCulture)),
            ["steps"] = plan.Steps.Select(s => new
            {
                code = s.Def.Code,
                label = s.Def.Label,
                taken = s.Step.Taken,
                values = s.Step.Taken ? s.Step.Values : null,
                outcome = s.Step.Taken ? s.Step.Outcome : plan.SkippedOutcome,
                written = (s.Step.Written ?? Array.Empty<DecisionRecordRef>()).Select(w => new { entity = w.Entity, id = w.Id }),
            }),
        };
        if (r.ReviewHash is { Length: > 0 } reviewHash)
        {
            // Task 043 (F1): kept right after the review id, so the stored-record lookup can prove the repeat is the same review.
            var ordered = new Dictionary<string, object?>(StringComparer.Ordinal) { ["reviewId"] = envelope["reviewId"], ["reviewHash"] = reviewHash };
            foreach (var (key, value) in envelope.Where(kv => kv.Key != "reviewId"))
                ordered[key] = value;
            envelope = ordered;
        }

        e["sprk_steps"] = Memo("sprk_steps", JsonMemoMaxLength, envelope);

        // Task 043: a Next step that ran but created no row the record can name (a notification whose communication row was
        // not saved after the send) is still a follow-on: listed with a null entity and id, never dropped.
        e["sprk_followons"] = Memo("sprk_followons", JsonMemoMaxLength,
            r.FollowOns.Select(f => new { actionCode = f.ActionCode, entity = (string?)f.Entity, id = (Guid?)f.Id })
                .Concat((r.UnnamedNextSteps ?? []).Select(code => new { actionCode = code, entity = (string?)null, id = (Guid?)null })));

        // D-129: the individual who decided comes from Assigned To Internal (a contact lookup), never from ownerid. Empty when the
        // caller has no linked internal contact; sprk_confirmedby above stays the caller's systemuser.
        if (r.DeciderContactId is { } deciderContact && deciderContact != Guid.Empty)
        {
            e["sprk_assignedtointernal"] = new EntityReference("contact", deciderContact);
        }

        if (r.Core is { } core)
        {
            // D-34/D-36: copy the Signal's core pair, and set the typed lineage lookup when the core type has one.
            e["sprk_corerecordtype"] = new EntityReference("sprk_recordtype_ref", core.RecordTypeRefId);
            e["sprk_corerecordid"] = core.RecordId.ToString("D", CultureInfo.InvariantCulture).ToLowerInvariant();
            if (LineageColumnByCoreEntity.TryGetValue(core.EntityLogicalName, out var column))
            {
                e[column] = new EntityReference(core.EntityLogicalName, core.RecordId);
            }
        }

        return e;
    }
}
