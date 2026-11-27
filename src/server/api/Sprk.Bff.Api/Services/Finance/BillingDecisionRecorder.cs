using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>Who decided a billing decision: a person (who confirmed it) or the system (an automatic decision, D-101).</summary>
public sealed record BillingDecider
{
    private BillingDecider(Guid? systemUserId, string role)
    {
        SystemUserId = systemUserId;
        Role = role;
    }

    /// <summary>The confirming user (<c>systemuserid</c>); null for a system decision.</summary>
    public Guid? SystemUserId { get; }

    /// <summary>The decider's role, recorded in the fact snapshot; <c>automated: &lt;what&gt;</c> for a system decision.</summary>
    public string Role { get; }

    /// <summary>A person confirmed the decision. Their linked internal contact (D-129) is not taken from the caller: the recorder looks it up
    /// from <paramref name="systemUserId"/> (ontology-r1 K3), as the commit route does, so a caller cannot name someone else as the decider.</summary>
    public static BillingDecider Human(Guid systemUserId, string role) => new(systemUserId, role);

    /// <summary>The system decided with no person (049's automatic rejection): no confirmer, decider <c>automated: {what}</c>.</summary>
    public static BillingDecider Automated(string what) => new(null, $"automated: {what}");
}

/// <summary>
/// ONE billing decision for <see cref="BillingDecisionRecorder"/> (ebilling-r1 task 074; FR-30, FR-19, FR-35 to FR-37; D-75, D-101).
/// </summary>
/// <param name="DecisionId">The caller's decision id. A retry with the same id writes nothing new; callers that need exactly-once across
/// callers derive it deterministically (103: from the approval portion).</param>
/// <param name="ActionCode">A billing code (<c>DecisionActionCatalog.BillingActions</c>).</param>
/// <param name="SubjectId">The line for a line code, the invoice for every other code (an override and an automatic rejection included,
/// D-101). The table follows the code, so a caller names only the id.</param>
/// <param name="Decider">Who decided.</param>
/// <param name="Facts">What the data said at decision time (billed and approved amounts, totals, findings). Mandatory.</param>
/// <param name="Outcome">What the caller did (<c>Reduced</c>, <c>Approved</c>, <c>Rejected</c>).</param>
/// <param name="Reason">Required for reduce, disallow, reject, return and override.</param>
/// <param name="Values">The action's other parameter values (<c>approvedAmount</c>, <c>note</c>).</param>
/// <param name="PolicyVersionId">The primary resolved finding's policy version; null when it resolves none.</param>
/// <param name="ResolvedSignalIds">The findings it resolves. The caller closes and links them with the returned record id.</param>
/// <param name="Written">Rows the decision wrote.</param>
/// <param name="PrivilegeFlagged">Copied forward (ADR-015).</param>
/// <param name="DecidedOn">When decided; now when null.</param>
public sealed record BillingDecision(
    Guid DecisionId,
    string ActionCode,
    Guid SubjectId,
    BillingDecider Decider,
    IReadOnlyDictionary<string, object?> Facts,
    string Outcome,
    string? Reason = null,
    IReadOnlyDictionary<string, object?>? Values = null,
    Guid? PolicyVersionId = null,
    IReadOnlyList<Guid>? ResolvedSignalIds = null,
    IReadOnlyList<DecisionRecordRef>? Written = null,
    bool PrivilegeFlagged = false,
    DateTimeOffset? DecidedOn = null);

/// <summary>
/// ebilling-r1 task 084h-2 (EB-D-238, EB-D-239; design notes/084h section 4, 7.3): ONE per-record Procedure change that committed, for
/// <see cref="BillingDecisionRecorder.RecordProcedureAsync"/>. It is also the repair payload: the Procedure service hands the SAME value to
/// <see cref="IRecordProcedureDecisionEnqueuer"/> when the inline record fails, so the repair job rebuilds the identical writer request (and
/// hash) from it and never re-reads the override row (design section 7.3, review item 4).
/// </summary>
/// <param name="DecisionId">Minted by the service once per change, before the preflight; the record's idempotency key with the core record.</param>
/// <param name="ActionCode">A per-record Procedure code (<c>DecisionActionCatalog.RecordProcedureActions</c>).</param>
/// <param name="CoreTable">The core table: <c>sprk_matter</c>, <c>sprk_project</c> or <c>sprk_workassignment</c>.</param>
/// <param name="RecordId">The core record the change was made on.</param>
/// <param name="DeciderSystemUserId">The deciding user (<c>systemuserid</c>), resolved from the caller by the route; never request input.</param>
/// <param name="DeciderRole">The decider's role, recorded in the fact snapshot.</param>
/// <param name="Values">The decided content keyed by the catalog parameter codes (design section 4.1) plus the undeclared ids; <c>note</c>
/// only when a note was given.</param>
/// <param name="Outcome">What the change did (<c>Assigned</c>, <c>Pinned</c>, <c>Excluded</c>, <c>Changed</c>, <c>Unpinned</c>, <c>Removed</c>).</param>
/// <param name="Reason">The change's reason as stored on the override row (the POST codes); null for an unpin or a removal.</param>
/// <param name="DecidedOn">When the change was made (fixed by the service, so a repair records the same instant).</param>
/// <param name="Written">The override row the change wrote or removed.</param>
public sealed record RecordProcedureDecision(
    Guid DecisionId,
    string ActionCode,
    string CoreTable,
    Guid RecordId,
    Guid DeciderSystemUserId,
    string DeciderRole,
    IReadOnlyDictionary<string, object?> Values,
    string Outcome,
    string? Reason,
    DateTimeOffset DecidedOn,
    IReadOnlyList<DecisionRecordRef>? Written = null);

/// <summary>
/// ebilling-r1 task 084h-2 (design notes/084h section 7.3, EB-D-239 Q10): queues the repair of a per-record Procedure change whose inline
/// Decision Record failed after the change committed. Task 084h-3 implements it (an ADR-004 job: payload, enqueuer, handler, registration).
/// Until that registration exists the Procedure service has no enqueuer and answers <c>NotRecorded</c>, never <c>Queued</c>.
/// </summary>
public interface IRecordProcedureDecisionEnqueuer
{
    /// <summary>Queues <paramref name="decision"/> for the repair job, unchanged. Throws when it could not be queued.</summary>
    Task EnqueueAsync(RecordProcedureDecision decision, CancellationToken ct);
}

/// <summary>The recorded decision: the Decision Record the caller links its findings to.</summary>
/// <param name="RecordId">The <c>sprk_decisionrecord</c> id (the existing one when <paramref name="AlreadyRecorded"/>).</param>
/// <param name="AlreadyRecorded">True when this decision id was recorded before and nothing was written.</param>
/// <param name="RecordClass">The record's class, from the catalog.</param>
public sealed record BillingDecisionRecordResult(Guid RecordId, bool AlreadyRecorded, RecordedDecisionClass RecordClass);

/// <summary>
/// Stable <see cref="EventId"/>s of <see cref="BillingDecisionRecorder"/> (#3276), in the ontology / billing range that
/// <c>OntologyEventIdUniquenessGuardTests</c> keeps unique (50332 is free: 50330-50331 are the Signals core-pair read). Ids only in the message.
/// </summary>
public static class BillingDecisionRecorderEvents
{
    /// <summary>Warning: the decision is recorded, but the inline share of its secure Decision Record failed, was incomplete or threw; the reconcile completes it.</summary>
    public static readonly EventId ShareIncomplete = new(50332, nameof(ShareIncomplete));
}

/// <summary>
/// ebilling-r1 task 074 (spec FR-30; D-75, D-100, D-101): the ONE Finance-side owner of a billing decision's Decision Record. The line
/// decisions (100), approve (103), reject and return (104), override (105) and the automatic rejection (049) call it, so each builds the same
/// request and none can pair a code with the wrong subject table or a system decision with a confirmer.
/// </summary>
/// <remarks>
/// <para><b>What it does.</b> It maps a <see cref="BillingDecision"/> to the writer's additive billing entry
/// (<see cref="DecisionRecordWriter.WriteBillingDecisionAsync"/>, D-100): the subject table from the code (line codes file under the line,
/// every other code under the invoice), the confirmer and decider role from <see cref="BillingDecider"/>, and the deciding person's linked
/// internal contact (D-129, <c>sprk_assignedtointernal</c>) looked up here from the confirmer, never passed by a caller. Every rule (billing codes only, a
/// required reason, a null confirmer only for an <c>automated:</c> decider, exactly once per decision id, the 026 filing checks) is the
/// writer's; the recorder copies none of them. It does not close or link findings: each caller does that with the returned record id.</para>
/// <para><b>Refuse before your own writes.</b> A caller that changes the invoice before recording calls <see cref="Preflight"/> first: it
/// gives every refusal the writer can give without I/O (a missing reason among them), so nothing changes when the decision would be refused.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="DecisionRecordWriter"/> writes the record; nothing in
/// Finance records a decision (no <c>DecisionRecordWriter</c> use under <c>Services/Finance</c> before this task). Extension: the writer is
/// ontology-owned and must stay subject-agnostic about billing callers; the code-to-table mapping and the decider shape are the eBilling
/// callers' contract, so they live here. Cost of doing nothing: five callers would each build the request, and one that passed a confirmer
/// for a system decision, or a line code with the invoice id, would be refused at run time after the invoice had already changed.</para>
/// <para><b>Inline share (#3276, DI-178).</b> A Decision Record the writer places under the Secure Record Owners team (a secure child), and a
/// record found already written, is shared with the secure family's sharees before <see cref="RecordAsync"/> or <see cref="RecordProcedureAsync"/>
/// returns, so they do not wait for the two-minute reconcile. It is the rule of <c>DecisionCommitService.ShareSecureRecordAsync</c> (PR #3270),
/// applied here after the writer returns: app-only, never throws, never fails or undoes the decision. <b>Component justification (CLAUDE.md
/// section 11):</b> no new service, endpoint, registration or package; the constructor takes an already-registered framework
/// <see cref="IServiceScopeFactory"/> (the recorder is a singleton and the synchronizer is scoped, so it is resolved in a fresh scope per share).</para>
/// </remarks>
public sealed class BillingDecisionRecorder
{
    private readonly DecisionRecordWriter _writer;
    private readonly IIdentityNormalizationService _identity;
    private readonly ILogger<BillingDecisionRecorder> _logger;
    private readonly IServiceScopeFactory? _scopes;

    /// <param name="writer">The Decision Record writer's billing entry.</param>
    /// <param name="identity">The deciding user's linked internal contact (the user's <c>sprk_primarycontact</c>, else the contact bound to
    /// their Entra oid): the identity service the commit route reads it from (D-129).</param>
    /// <param name="logger">A decider with no linked contact, or a contact that could not be read.</param>
    /// <param name="scopes">The host's scope factory (a framework service, already registered), so each inline share resolves the scoped
    /// <see cref="SecureChildShareSynchronizer"/> in a fresh scope. Null (a unit-test composition): no inline share is attempted, which
    /// is the behaviour before #3276; the production host always supplies it (pinned by <c>BillingDecisionRecorderRegistrationTests</c>).</param>
    public BillingDecisionRecorder(
        DecisionRecordWriter writer, IIdentityNormalizationService identity, ILogger<BillingDecisionRecorder> logger, IServiceScopeFactory? scopes = null)
    {
        _scopes = scopes;
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Writes the decision's one Decision Record, or finds the one already written for its decision id. The deciding user's contact is
    /// read only when a record is going to be written, so a true retry is answered even while that read faults.</summary>
    /// <exception cref="DecisionRecordRefusedException">The writer refused (nothing written; after a create timeout the row may exist), or
    /// the deciding user's linked contact could not be read (<see cref="DecisionRecordRefusalReason.BillingRecordLookupFailed"/>; nothing
    /// written: the append-only record must not lose its decider to a fault).</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    public async Task<BillingDecisionRecordResult> RecordAsync(BillingDecision decision, CancellationToken ct = default)
    {
        var request = ToRequest(decision);
        _writer.PreflightBillingDecision(request); // an invalid decision is refused before the identity read
        // The contact is supplied LAZILY: the writer asks for it only once it knows a record will be written, after its "already recorded"
        // check. A true retry is then answered even while the identity read faults; a new decision with a faulting read is still refused
        // before anything is written.
        Func<CancellationToken, Task<Guid?>>? contact = decision.Decider.SystemUserId is { } user && user != Guid.Empty
            ? token => DeciderContactAsync(user, DecisionRecordRefusalReason.BillingRecordLookupFailed, "Billing decision", token)
            : null;
        var result = await _writer.WriteBillingDecisionAsync(request, contact, ct).ConfigureAwait(false);
        await ShareSecureRecordAsync(result, decision.DecisionId, "Billing decision").ConfigureAwait(false);
        return new BillingDecisionRecordResult(result.RecordId, result.AlreadyRecorded, result.RecordClass);
    }

    /// <summary>
    /// ebilling-r1 task 084h-2 (EB-D-238; design notes/084h sections 3.2, 6): writes a per-record Procedure change's one Decision Record through
    /// the writer's Procedure entry (<see cref="DecisionRecordWriter.WriteRecordProcedureDecisionAsync(RecordProcedureDecisionRecordRequest, CancellationToken)"/>),
    /// or finds the one already written for its decision id. The deciding user's linked contact is read lazily, as <see cref="RecordAsync"/>
    /// reads it, and an unreadable contact is refused as <see cref="DecisionRecordRefusalReason.RecordProcedureLookupFailed"/> (084h-1 hand-off
    /// H-1), never with the billing reason.
    /// </summary>
    /// <exception cref="DecisionRecordRefusedException">The writer refused, or the deciding user's contact could not be read. Nothing written.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    public async Task<BillingDecisionRecordResult> RecordProcedureAsync(RecordProcedureDecision decision, CancellationToken ct = default)
    {
        var request = ToRequest(decision);
        _writer.PreflightRecordProcedureDecision(request);
        Func<CancellationToken, Task<Guid?>>? contact = decision.DeciderSystemUserId != Guid.Empty
            ? token => DeciderContactAsync(decision.DeciderSystemUserId, DecisionRecordRefusalReason.RecordProcedureLookupFailed, "Procedure change", token)
            : null;
        var result = await _writer.WriteRecordProcedureDecisionAsync(request, contact, ct).ConfigureAwait(false);
        await ShareSecureRecordAsync(result, decision.DecisionId, "Procedure change").ConfigureAwait(false);
        return new BillingDecisionRecordResult(result.RecordId, result.AlreadyRecorded, result.RecordClass);
    }

    /// <summary>
    /// #3276 (DI-178; the rule of <c>DecisionCommitService.ShareSecureRecordAsync</c>, ontology task 225, D-229): the inline share of ONE
    /// Decision Record this recorder wrote or found, through uac-r2's per-child mirror
    /// (<see cref="SecureChildShareSynchronizer.SyncChildAsync"/>, app-only, <see cref="CancellationToken.None"/>: the decision is already
    /// committed, so a caller that went away must not leave the record unshared). Called only after the writer has returned, so the record
    /// and its owner are committed and the synchronizer decides from the stored row. The id is always the writer's
    /// <see cref="DecisionRecordWriteResult.RecordId"/>, never a caller's.
    /// <para>It shares when the record was written now AND is owned by the Secure Record Owners team, or when the record already existed (a
    /// retry may follow an attempt that stopped before its share; <c>SyncChildAsync</c> answers NotApplicable for a row the Secure team does
    /// not own). An ordinary record makes no share call. Never throws and never fails or undoes the decision: a <c>Failed</c> or
    /// <c>Incomplete</c> answer and any fault are logged (ids and status only) and left to the two-minute reconcile, the backstop.</para>
    /// <para>The synchronizer is scoped and this recorder is a singleton, so it is resolved from a fresh scope per share, disposed after it.</para>
    /// <para><b>Cost (known limit, review 2026-11-26).</b> A first-time ordinary record makes no call. A secure record costs one per-child
    /// mirror, the work the reconcile would otherwise do for it. A retry of an already-recorded record costs the synchronizer's ownership
    /// reads (about three) even when the row is ordinary, because only the stored row can say. The line-decision loop records at most
    /// <see cref="InvoiceLineDecisionService.MaxLinesPerRequest"/> lines per request, so this is bounded and proportional to the writer's own
    /// per-line reads.</para>
    /// <para><b>Why this wrapper is not shared with the commit route's.</b> The rule itself is uac-r2's (<c>SyncChildAsync</c>); what is
    /// repeated here is only the log-and-swallow around it, and its gate reads a different answer (the writer's
    /// <see cref="DecisionRecordWriteResult.SecureOwnerTeamId"/>, not the commit route's owner resolution). Sharing it would mean editing
    /// the ontology-owned <c>DecisionCommitService</c>, which another open change is editing.</para>
    /// </summary>
    private async Task ShareSecureRecordAsync(DecisionRecordWriteResult result, Guid decisionId, string what)
    {
        if (_scopes is null || (result.SecureOwnerTeamId is null && !result.AlreadyRecorded))
        {
            return;
        }

        try
        {
            using var scope = _scopes.CreateScope();
            var shares = scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>();
            var shared = await shares.SyncChildAsync(DecisionRecordTable, result.RecordId, CancellationToken.None).ConfigureAwait(false);
            if (shared.Status is SecureChildShareSyncStatus.Failed or SecureChildShareSyncStatus.Incomplete)
            {
                _logger.LogWarning(BillingDecisionRecorderEvents.ShareIncomplete,
                    "{What}: decision {DecisionId} recorded as {RecordId}; the secure record's sharees were not mirrored ({Status}: {Detail}); the reconcile completes it.",
                    what, decisionId, result.RecordId, shared.Status, shared.Detail);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(BillingDecisionRecorderEvents.ShareIncomplete, ex,
                "{What}: decision {DecisionId} recorded as {RecordId}; the secure record's share stopped; the reconcile completes it.",
                what, decisionId, result.RecordId);
        }
    }

    private const string DecisionRecordTable = "sprk_decisionrecord";

    /// <summary>Every refusal <see cref="RecordProcedureAsync"/> can give without I/O (task 084h-2: the Procedure service runs it before its
    /// transaction). Reads and writes nothing.</summary>
    /// <exception cref="DecisionRecordRefusedException">The change as given would be refused.</exception>
    public void PreflightProcedure(RecordProcedureDecision decision) => _writer.PreflightRecordProcedureDecision(ToRequest(decision));

    /// <summary>
    /// The writer request of a per-record Procedure change: the subject is the core record, the facts are the core table and the record id
    /// (design section 4.2: no counts, no close state), and the deciding contact is never the caller's (it is looked up, K3). Pure, so the
    /// 084h-3 repair job rebuilds the identical request (and hash) from the queued <see cref="RecordProcedureDecision"/>.
    /// </summary>
    internal static RecordProcedureDecisionRecordRequest ToRequest(RecordProcedureDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return new RecordProcedureDecisionRecordRequest(
            decision.DecisionId,
            decision.ActionCode,
            new DecisionRecordRef(decision.CoreTable, decision.RecordId),
            decision.DeciderSystemUserId,
            decision.DeciderRole,
            decision.Values,
            decision.Outcome,
            new Dictionary<string, object?> { ["coreTable"] = decision.CoreTable, ["recordId"] = decision.RecordId },
            decision.Reason,
            decision.Written,
            DeciderContactId: null, // looked up from the decider (K3); never the caller's
            decision.DecidedOn);
    }

    /// <summary>
    /// ontology-r1 K3 (D-129): the deciding user's linked internal contact, by the commit route's rule (its decider lookup in
    /// <see cref="DecisionCommitService"/>): the identity service's contact; null, with a warning, when the user genuinely has none (the
    /// record's Assigned To Internal stays empty and the decision is still recorded); refused when the read FAULTED, because the answer is
    /// unknown.
    /// </summary>
    /// <exception cref="DecisionRecordRefusedException"><paramref name="refusalReason"/> (the billing path's
    /// <see cref="DecisionRecordRefusalReason.BillingRecordLookupFailed"/>, the Procedure path's
    /// <see cref="DecisionRecordRefusalReason.RecordProcedureLookupFailed"/>): the contact could not be read. Nothing is written; a retry may
    /// succeed.</exception>
    private async Task<Guid?> DeciderContactAsync(Guid systemUserId, string refusalReason, string what, CancellationToken ct)
    {
        PersonIdentity identity;
        try
        {
            identity = await _identity.ResolveAsync(systemUserId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("{What}: the deciding user's linked contact could not be read ({ExceptionType}); refusing before any write.",
                what, ex.GetType().Name);
            throw DeciderUnreadable(refusalReason, ex);
        }

        if (identity.ContactId is { } contactId && contactId != Guid.Empty)
        {
            return contactId;
        }

        if (identity.ContactUnreadable)
        {
            _logger.LogWarning("{What}: the deciding user's linked contact could not be read (identity read faults: {Faults}); refusing before any write.",
                what, identity.Faults);
            throw DeciderUnreadable(refusalReason, innerException: null);
        }

        _logger.LogWarning("{What}: the deciding user has no linked internal contact; the Decision Record's Assigned To Internal is left empty.", what);
        return null;
    }

    private static DecisionRecordRefusedException DeciderUnreadable(string refusalReason, Exception? innerException) => new(
        refusalReason,
        "The deciding user's linked contact could not be read, so the decision was not recorded; nothing was written.",
        innerException: innerException);

    /// <summary>
    /// Task 165b: the <c>systemuserid</c> of the identity that creates every Decision Record (<see cref="DecisionRecordWriter.WriterUserIdAsync"/>),
    /// so a Finance reader can trust only a record that identity wrote (<c>createdby</c>). A fault propagates: the caller refuses.
    /// </summary>
    internal Task<Guid> WriterUserIdAsync(CancellationToken ct) => _writer.WriterUserIdAsync(ct);

    /// <summary>
    /// The D-122 set a decision already recorded under <paramref name="decisionId"/> stored with its record (ebilling-r1 task 100: a retry
    /// closes exactly that set); null when no record exists yet (a first attempt). The subject table follows the code, as for
    /// <see cref="RecordAsync"/>. Read only (<see cref="DecisionRecordWriter.FindBillingDecisionResolvedSetAsync"/>).
    /// </summary>
    /// <exception cref="DecisionRecordRefusedException">More than one record matches, the stored set cannot be read, or the lookup faulted.</exception>
    internal Task<DecisionRecordWriter.BillingStoredResolvedSet?> FindStoredResolvedSetAsync(
        string actionCode, Guid subjectId, Guid decisionId, CancellationToken ct = default) =>
        _writer.FindBillingDecisionResolvedSetAsync(
            new DecisionRecordRef(DecisionRecordWriter.BillingSubjectEntityOf(actionCode) ?? string.Empty, subjectId), decisionId, ct);

    /// <summary>
    /// <see cref="FindStoredResolvedSetAsync"/> for many subjects of one code under one decision id, in ONE query (task 100's line decisions,
    /// K2): every id is answered, with its stored set or null. Read only
    /// (<see cref="DecisionRecordWriter.FindBillingDecisionResolvedSetsAsync"/>).
    /// </summary>
    /// <exception cref="DecisionRecordRefusedException">A subject has more than one record, a stored set cannot be read, or the lookup faulted.</exception>
    internal Task<IReadOnlyDictionary<Guid, DecisionRecordWriter.BillingStoredResolvedSet?>> FindStoredResolvedSetsAsync(
        string actionCode, IReadOnlyCollection<Guid> subjectIds, Guid decisionId, CancellationToken ct = default) =>
        _writer.FindBillingDecisionResolvedSetsAsync(DecisionRecordWriter.BillingSubjectEntityOf(actionCode) ?? string.Empty, subjectIds, decisionId, ct);

    /// <summary>Every refusal <see cref="RecordAsync"/> can give without I/O. Reads and writes nothing.</summary>
    /// <exception cref="DecisionRecordRefusedException">The decision as given would be refused.</exception>
    public void Preflight(BillingDecision decision) => _writer.PreflightBillingDecision(ToRequest(decision));

    /// <summary>
    /// ebilling-r1 iss029 (ISS-029): when <paramref name="stored"/> is the record already written under the decision's subject and decision id
    /// (read by <see cref="FindStoredResolvedSetsAsync"/>), the writer's own "already recorded" comparison of the decision against it
    /// (<see cref="DecisionRecordWriter.PreflightBillingRepeat"/>), before the caller's first write: the same rules and refusals <see cref="RecordAsync"/>
    /// would give, so a caller can refuse a decision id that is recorded with another decision before it changes anything. Null
    /// <paramref name="stored"/> (a first attempt) passes. Reads and writes nothing.
    /// </summary>
    /// <exception cref="DecisionRecordRefusedException"><see cref="DecisionRecordRefusalReason.ReviewInvalid"/>: another billing code, other content
    /// (<see cref="DecisionRecordRefusedException.IsOtherContent"/>), or a recorded content that cannot be read.</exception>
    internal static void PreflightRepeat(BillingDecision decision, DecisionRecordWriter.BillingStoredResolvedSet? stored)
    {
        if (stored is not null)
        {
            DecisionRecordWriter.PreflightBillingRepeat(ToRequest(decision), stored);
        }
    }

    private static BillingDecisionRecordRequest ToRequest(BillingDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(decision.Decider);

        // An unknown code has no table; the writer refuses the code before it looks at the subject.
        var table = DecisionRecordWriter.BillingSubjectEntityOf(decision.ActionCode) ?? string.Empty;
        return new BillingDecisionRecordRequest(
            decision.DecisionId,
            decision.ActionCode,
            new DecisionRecordRef(table, decision.SubjectId),
            decision.Decider.SystemUserId,
            decision.Decider.Role,
            decision.Values,
            decision.Outcome,
            decision.Facts,
            decision.Reason,
            decision.PolicyVersionId,
            decision.ResolvedSignalIds,
            decision.Written,
            DeciderContactId: null, // RecordAsync looks it up from the confirmer (K3); never the caller's
            decision.PrivilegeFlagged,
            decision.DecidedOn);
    }
}
