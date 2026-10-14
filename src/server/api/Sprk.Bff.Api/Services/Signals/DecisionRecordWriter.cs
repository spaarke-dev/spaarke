using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xrm.Sdk;
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
}

/// <summary>Bounded-cardinality refusal reasons for the Decision Record writer (log and metric dimension).</summary>
public static class DecisionRecordRefusalReason
{
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

    /// <summary>Task 142: the record's subject is a table the Decision Record has no subject lookup for (contract C1). No I/O.</summary>
    public const string SubjectUnsupported = "decision_subject_unsupported";

    /// <summary>Task 142: the filing (subject lookup and pair, or the core-ancestor stamp, contract C1/C2) could not be derived, so the
    /// record's place and the parents its owner is asked about are unknown. Nothing was written; a retry may succeed.</summary>
    public const string FilingFailed = "decision_filing_failed";

    /// <summary>Task 142 (adversarial pass 1 F1): the subject cannot be filed for a reason a retry does not change (its lineage names
    /// two different records of one core table, or it no longer exists). A refusal, not an outage.</summary>
    public const string SubjectUnfileable = "decision_subject_unfileable";
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
        RecordOwnerResolution resolution, IReadOnlyList<RecordOwnershipParent> parents, Guid? assignee, DecisionRecordFiling? filing = null)
    {
        Resolution = resolution ?? throw new ArgumentNullException(nameof(resolution));
        Parents = parents ?? throw new ArgumentNullException(nameof(parents));
        Assignee = assignee;
        Filing = filing;
    }

    /// <summary>The filing the answer was given for (null when none was computed with it).</summary>
    internal DecisionRecordFiling? Filing { get; }

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
    /// <para><b>Never empty (#1780 K3).</b> The typed columns stay populated for new rows until task 145 decides their
    /// retirement; a change that moves filing off them replaces this map in the same change. Pinned by
    /// <c>DecisionRecordWriterTests</c> (a missing root fails there with <c>KeyNotFoundException</c>).</para>
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
    private readonly ICommunicationDataverseService _recordTypes;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DecisionRecordWriter> _logger;

    /// <param name="writerClient">The ontology writer identity: the record's ONLY create, and the secure-owner read-back.</param>
    /// <param name="ownership">uac-r2's owner resolver (contract C3).</param>
    /// <param name="coreAncestors">The core-ancestor stamp of the record's subject (contract C2), and the derived cores its owner is asked about.</param>
    /// <param name="recordTypes">The <c>sprk_recordtype_ref</c> catalog the shared regarding builder types the pair from (task 141).</param>
    /// <param name="timeProvider">The decision time when the review gives none.</param>
    /// <param name="logger">Refusals and the secure-owner read-back.</param>
    public DecisionRecordWriter(
        OntologyWriterDataverseClient writerClient,
        IRecordOwnershipResolver ownership,
        CoreAncestorResolver coreAncestors,
        ICommunicationDataverseService recordTypes,
        TimeProvider timeProvider,
        ILogger<DecisionRecordWriter> logger)
    {
        _writerClient = writerClient ?? throw new ArgumentNullException(nameof(writerClient));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _coreAncestors = coreAncestors ?? throw new ArgumentNullException(nameof(coreAncestors));
        _recordTypes = recordTypes ?? throw new ArgumentNullException(nameof(recordTypes));
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
            var plan = Validate(request);
            var content = Content(request, plan);
            // Filed under its subject and owned (FiledAndOwnedAsync); the route's step-1f filing and owner are applied with no read.
            var (entity, recordOwner) = await FiledAndOwnedAsync(request, resolvedOwner, ct).ConfigureAwait(false);
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

            _logger.LogInformation(
                "Decision Record {RecordId} written for review {ReviewId}: class {RecordClass}, {SignalCount} Signal(s).",
                recordId, request.ReviewId, plan.Class, request.ResolvedSignalIds.Count);

            return new DecisionRecordWriteResult(recordId, plan.Class, request.ResolvedSignalIds)
            {
                SecureOwnerTeamId = secureTeamId,
            };
        }
        catch (DecisionRecordRefusedException ex)
        {
            // One logging/metering point for every refusal: reason only, never fact values, values or reasons text.
            _logger.LogError(DecisionRecordEvents.WriteRefused, ex,
                "Decision Record writer refused review {ReviewId} (reason={Reason}, ownerRefusal={OwnerRefusalCode}).",
                request.ReviewId, ex.Reason, ex.OwnerRefusalCode);
            OntologyWriterTelemetry.RecordFailure(ex.Reason);
            throw;
        }
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
        var owner = await ResolveOwnerCoreAsync(context, requireTeam: true, ct).ConfigureAwait(false);
        if (!owner.IsSecureOwner && request.Core is null && request.AssigneeSystemUserId is { } assignee && assignee != Guid.Empty)
        {
            owner = await ResolveOwnerCoreAsync(
                RecordOwnershipContext.ForUserTeam(assignee) with { RequestedBy = context.RequestedBy }, requireTeam: true, ct).ConfigureAwait(false);
        }

        return new DecisionRecordOwner(owner, context.Parents, request.AssigneeSystemUserId, filing);
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
    /// about the owner.
    /// </summary>
    internal static RecordOwnershipContext OwnershipContextOf(DecisionRecordRequest request, IReadOnlyList<CoreAncestorStamp> derivedCores)
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

        return RecordOwnershipContext.ForParents(parents) with { RequestedBy = RecordRequester.Of(request.ConfirmedByUserId) };
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
        DecisionRecordRequest request, DecisionRecordOwner? handed, CancellationToken ct)
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
            var derivedCores = await FileUnderSubjectAsync(entity, subject, ct).ConfigureAwait(false);
            filing = new DecisionRecordFiling(
                subject, entity.Attributes.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal), derivedCores);
        }

        // D-33/D-34/D-39 as amended by D-129 (records are owned by TEAMS), uac-r2 F1 and contract C3: the owner is uac-r2's resolver
        // asked with EVERY parent the record sits under, exactly as returned (secure-if-any). owningbusinessunit is never set here (it
        // derives from the owner); a refusal writes nothing; KeepCreator is not requested, so an Unchanged answer is refused.
        var context = OwnershipContextOf(request, filing.DerivedCores);
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

        if (!owner.Resolution.IsOwned)
        {
            throw new DecisionRecordRefusedException(
                DecisionRecordRefusalReason.OwnerRefused,
                "The resolved owner of the Decision Record is not a team; nothing was written.",
                owner.Resolution.RefusalCode ?? RecordOwnerRefusal.NoOwnerSource);
        }

        owner.Resolution.ApplyTo(entity);
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
    private async Task<IReadOnlyList<CoreAncestorStamp>> FileUnderSubjectAsync(Entity entity, DecisionRecordRef subject, CancellationToken ct)
    {
        try
        {
            await new TodoRegardingBuilder(_recordTypes, _coreAncestors, _logger)
                .ApplySubjectLookupAndPairAsync(entity, subject.Entity, subject.Id, regardingDisplayName: null, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw FilingFailed(subject, $"the subject lookup and pair could not be built ({ex.GetType().Name})", ex);
        }

        var stamp = await _coreAncestors.StampAsync(entity, subject.Entity, subject.Id, ct).ConfigureAwait(false);
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
    /// <param name="requireTeam">True for every Decision Record, whose owner is always the resolver's team: any answer that names no
    /// team (a refusal, or an Unchanged the context never asked for) refuses HERE, before the route's first write.</param>
    /// <param name="ct">Cancellation.</param>
    private async Task<RecordOwnerResolution> ResolveOwnerCoreAsync(RecordOwnershipContext context, bool requireTeam, CancellationToken ct)
    {
        RecordOwnerResolution owner;
        try
        {
            owner = await _ownership.ResolveOwnerAsync(context, ct).ConfigureAwait(false);
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

        if (owner.IsRefused)
        {
            throw new DecisionRecordRefusedException(
                DecisionRecordRefusalReason.OwnerRefused,
                "The ownership resolver refused an owner for the Decision Record; nothing was written.",
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

        if (requireTeam && !owner.IsOwned)
        {
            throw new DecisionRecordRefusedException(
                DecisionRecordRefusalReason.OwnerRefused,
                "The ownership resolver named no owner team for the Decision Record; nothing was written.",
                owner.RefusalCode ?? RecordOwnerRefusal.NoOwnerSource);
        }

        return owner;
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

    private sealed record Plan(
        RecordedDecisionClass Class,
        int Outcome,
        IReadOnlyList<(DecisionRecordStep Step, DecisionActionDefinition Def)> Steps,
        IReadOnlyList<(DecisionRecordStep Step, DecisionActionDefinition Def)> Taken);

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
        var name = plan.Class == RecordedDecisionClass.Dismissal
            ? "Dismissal"
            : takenLabels.Length > 0 ? $"{plan.Class}: {takenLabels}" : $"{plan.Class}: next steps";
        e["sprk_name"] = name.Length > NameMaxLength ? name[..(NameMaxLength - 3)] + "..." : name;

        e["sprk_policyversion"] = new EntityReference("sprk_policyversion", r.PolicyVersionId);
        e["sprk_recordclass"] = new OptionSetValue((int)plan.Class);
        e["sprk_decisionoutcome"] = new OptionSetValue(plan.Outcome);
        e["sprk_confirmedby"] = new EntityReference("systemuser", r.ConfirmedByUserId);
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
                outcome = s.Step.Taken ? s.Step.Outcome : "Skipped",
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
