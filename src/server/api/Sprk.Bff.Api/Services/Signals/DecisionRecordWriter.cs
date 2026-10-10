using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;
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
/// <param name="ItemOwnerUserId">The no-core item's owner (<c>systemuserid</c>); required when <paramref name="Core"/> is null.</param>
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
public sealed record DecisionRecordRequest(
    Guid ReviewId,
    Guid PolicyVersionId,
    DecisionRecordCore? Core,
    Guid? ItemOwnerUserId,
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
    bool PrivilegeFlagged = false);

/// <summary>The result of <see cref="DecisionRecordWriter.WriteAsync"/>.</summary>
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
}

/// <summary>Stable EventIds for the Decision Record writer (the 5030x range is the Signal side's).</summary>
public static class DecisionRecordEvents
{
    /// <summary>Error: the writer refused (nothing written), or the create failed. Properties: reason only, never fact values.</summary>
    public static readonly EventId WriteRefused = new(50310, nameof(WriteRefused));

    /// <summary>Error: a Secure-team create read back with another owner. The row exists (append-only); uac-r2's reconcile corrects the owner.</summary>
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
/// Writes the ONE append-only <c>sprk_decisionrecord</c> a review produces (task 040; spec FR-18, FR-19, FR-20, FR-22,
/// FR-50, FR-51; D-17, D-33, D-34, D-36, D-39).
/// </summary>
/// <remarks>
/// <para><b>Every human resolution writes one record, typed.</b> BR-1 was reversed: there is no lane carve-out. The class is
/// derived here from task 036's catalog and never from the client: Dismissal when nothing was taken and no Next step was
/// created; Judgement when any taken action or any Next step is Judgement; Routine otherwise. An action code the catalog does
/// not know is refused, never defaulted (the option set is closed).</para>
/// <para><b>Append-only.</b> The writer only ever calls <c>CreateAsync</c>. No human role and not this identity holds Write or
/// Delete on the table, and this class has no update path.</para>
/// <para><b>Ownership.</b> With a core record, uac-r2's <see cref="IRecordOwnershipResolver"/> is asked with the core as the
/// parent (the same call <see cref="SignalWriter"/> makes): a Secure answer sets <c>ownerid</c> to the Secure Record Owners
/// team in the create (this identity holds Assign, never content rights); a not-secure answer keeps the writer's current
/// ownership; a refusal writes nothing and throws <see cref="DecisionRecordRefusedException"/>. With NO core record the record
/// is owned by the item's owner, set in the create (D-39). The typed lineage lookup (<c>sprk_matter</c>, <c>sprk_project</c>,
/// <c>sprk_workassignment</c>) is read from uac-r2's <see cref="SecureChildLineage"/> entry for this table, not branched per
/// type; a service request has none and is recorded in the generic columns only.</para>
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
    /// <c>sprk_decisionrecord</c> (its lookups whose target is a secure root). One data table; no per-type branch.
    /// A core entity absent here (the service request) has no typed column (D-36).
    /// </summary>
    internal static readonly FrozenDictionary<string, string> LineageColumnByCoreEntity =
        SecureChildLineage.Children[Table].Lookups
            .Where(l => SecureChildLineage.Roots.Contains(l.Value))
            .ToFrozenDictionary(l => l.Value, l => l.Key, StringComparer.OrdinalIgnoreCase);

    private readonly OntologyWriterDataverseClient _writerClient;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DecisionRecordWriter> _logger;

    public DecisionRecordWriter(
        OntologyWriterDataverseClient writerClient,
        IRecordOwnershipResolver ownership,
        TimeProvider timeProvider,
        ILogger<DecisionRecordWriter> logger)
    {
        _writerClient = writerClient ?? throw new ArgumentNullException(nameof(writerClient));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Writes the review's one Decision Record.</summary>
    /// <exception cref="DecisionRecordRefusedException">The ONLY exception this writer throws (other than cancellation): the
    /// review is invalid or too large for its columns, has no fact snapshot, cannot be typed from the catalog, or its owner was
    /// refused (nothing was written, no I/O); or the Dataverse create failed (<see cref="DecisionRecordRefusalReason.CreateFailed"/>,
    /// or <see cref="DecisionRecordRefusalReason.DataverseAccessDenied"/> for a privilege fault; the inner exception is kept, and
    /// after a timeout the row may exist).</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    public async Task<DecisionRecordWriteResult> WriteAsync(DecisionRecordRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var plan = Validate(request);

            Guid? secureTeamId = null;
            Entity entity = Build(request, plan);

            if (request.Core is { } core)
            {
                // Task 040 (D-33/D-34): the core record is the resolver's parent. Secure answer -> the team owns the row, set in
                // the create by uac-r2's own ApplyTo; not secure -> the writer's current ownership stays (SignalWriter's rule).
                RecordOwnerResolution owner;
                try
                {
                    owner = await _ownership.ResolveOwnerAsync(
                        new RecordOwnershipContext
                        {
                            TargetEntityLogicalName = core.EntityLogicalName,
                            TargetRecordId = core.RecordId,
                            Parents = Array.Empty<RecordOwnershipParent>(),
                        },
                        ct).ConfigureAwait(false);
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

                if (owner.IsSecureOwner)
                {
                    // A secure answer with no team is malformed: falling through would write an ordinary-owned child of a secure root.
                    if (!owner.IsOwned)
                    {
                        throw new DecisionRecordRefusedException(
                            DecisionRecordRefusalReason.OwnerRefused,
                            "The ownership resolver marked the record secure but named no owner team; nothing was written.",
                            RecordOwnerRefusal.NoOwnerSource);
                    }

                    owner.ApplyTo(entity);
                    secureTeamId = owner.OwningTeamId;
                }
            }
            else
            {
                // D-39: a no-core item's record is owned by the item's owner, set in the create (Assign, task 008).
                if (request.ItemOwnerUserId is not { } itemOwner || itemOwner == Guid.Empty)
                {
                    throw new DecisionRecordRefusedException(
                        DecisionRecordRefusalReason.OwnerMissing,
                        "A review of an item with no core record needs the item's owner to own its Decision Record; nothing was written.");
                }

                entity["ownerid"] = new EntityReference("systemuser", itemOwner);
            }

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
    /// After a Secure-team create, reads <c>owningteam</c> back. A mismatch cannot be corrected on an append-only row and a
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

        var nothingTaken = taken.Count == 0 && r.FollowOns.Count == 0;
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
            : r.FollowOns.Count > 0 || taken.Any(t => t.Item2.RecordClass == DecisionRecordClass.Judgement)
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

    // ── Entity ──────────────────────────────────────────────────────────────────────────────────────────────────

    private Entity Build(DecisionRecordRequest r, Plan plan)
    {
        var e = new Entity(Table);

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

        e["sprk_steps"] = Memo("sprk_steps", JsonMemoMaxLength, new
        {
            reviewId = r.ReviewId.ToString("D", CultureInfo.InvariantCulture),
            resolvedSignalIds = r.ResolvedSignalIds.Select(id => id.ToString("D", CultureInfo.InvariantCulture)),
            steps = plan.Steps.Select(s => new
            {
                code = s.Def.Code,
                label = s.Def.Label,
                taken = s.Step.Taken,
                values = s.Step.Taken ? s.Step.Values : null,
                outcome = s.Step.Taken ? s.Step.Outcome : "Skipped",
                written = (s.Step.Written ?? Array.Empty<DecisionRecordRef>()).Select(w => new { entity = w.Entity, id = w.Id }),
            }),
        });

        e["sprk_followons"] = Memo("sprk_followons", JsonMemoMaxLength,
            r.FollowOns.Select(f => new { actionCode = f.ActionCode, entity = f.Entity, id = f.Id }));

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
