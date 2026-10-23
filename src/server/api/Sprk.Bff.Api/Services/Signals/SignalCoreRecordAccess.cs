using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>What <see cref="SignalCoreRecordAccess.AuthorizeAsync"/> decided.</summary>
public enum SignalAccessOutcome
{
    /// <summary>The caller may read the Signal and its core record (or, for a no-core Do item, its subject row), and a Do-lane
    /// Signal is the caller's own work (D-141).</summary>
    Allowed,

    /// <summary>
    /// Not found, not readable, or not decidable. One outcome for all of them (the 097/#1312 uniform 404): the route is
    /// not an existence oracle, and a fault is never a Read-shaped consolation prize.
    /// </summary>
    NotFound,

    /// <summary>The caller could not be resolved to a Dataverse identity (no token, OBO refused): the single 403 (D-29).</summary>
    CallerUnresolved,

    /// <summary>
    /// A no-core Signal's subject could not be read, or a Do-lane Signal's "mine" read could not be answered (the caller's identity
    /// or linked contact, the subject's entity set, the read itself), because of a FAULT (throttling, a 5xx, a failed lookup), which
    /// says nothing about whether the caller may see it: the route answers 503, never the uniform 404 that means "not yours"
    /// (uac-r2 F4-b; task 150: the worklist answers 503 for the same faults). Only those reads produce it; every other fault stays
    /// the uniform denial.
    /// </summary>
    Unavailable,
}

/// <summary>What <see cref="SignalCoreRecordAccess.CheckInquiryOutcomeTodoAsync"/> found (task 154, D-111).</summary>
internal enum InquiryOutcomeTodoCheck
{
    /// <summary>No open inquiry the caller reads derives the To Do's id: Mark complete stays available.</summary>
    Ordinary,
    /// <summary>The To Do records the outcome of an open inquiry: completed by the disposition, never by Mark complete.</summary>
    OpenInquiry,
    /// <summary>The caller cannot read the To Do (403/404): the uniform not-found answer.</summary>
    NotReadable,
    /// <summary>A fault: nothing is done (503).</summary>
    Unavailable,
}

/// <summary>The decision, plus what the caller was allowed to learn about the Signal.</summary>
/// <param name="Outcome">The decision.</param>
/// <param name="Lane">The Signal's lane; set only when <see cref="Outcome"/> is <see cref="SignalAccessOutcome.Allowed"/>.</param>
/// <param name="PolicyVersionId">The policy version that raised the Signal; set only when allowed.</param>
/// <param name="CoreTypeId">The core-record pair the caller's own read returned (task 043 K2), so a route that re-reads the
/// Signal app-only can refuse when the pair changed between the two reads. Both null for a no-core Signal.</param>
/// <param name="CoreId">The core record id of that pair; null for a no-core Signal.</param>
/// <param name="SubjectTable">Do lane only (task 150): the subject's table, from the subject lookup that carries the pair id (task
/// 141); null when it cannot be derived. Carried so <see cref="SignalCoreRecordAccess.ConfirmDoLaneMineAsync"/> can run later.</param>
/// <param name="SubjectId">Do lane only: the subject's id (<c>sprk_regardingrecordid</c>); null when absent.</param>
/// <param name="CallerContactId">Do lane only, once the "mine" read admitted the caller (<see cref="SignalCoreRecordAccess.ConfirmDoLaneMineAsync"/>):
/// the caller's linked contact (null when they have none), so the suppression count reports only the caller's own Do-lane count
/// (task 152, D-145a).</param>
/// <param name="TodoNotes">Do lane, To Do subject only, from the same "mine" read (task 154, D-111): the To Do's notes (the inquiry
/// hint). With <paramref name="TodoMatterId"/> it is what the plan route needs to identify an OPEN inquiry's "Record the outcome"
/// To Do exactly as the commit route does (<see cref="SignalCoreRecordAccess.CheckSubjectTodoInquiryAsync"/>, pass 2 F1).</param>
/// <param name="TodoMatterId">Do lane, To Do subject only, from the same "mine" read: the To Do's matter
/// (<c>_sprk_regardingmatter_value</c>), for the matter fallback when the notes carry no usable hint.</param>
/// <param name="CoreTable">The core record's table (<c>sprk_matter</c>, <c>sprk_project</c>, ...), resolved from the catalog row the
/// Signal's core-record type names; null for a no-core Signal. Carried so the plan and commit routes can ask the context questions
/// that depend on the core type (task 161: does the matter have a budget) without resolving it again.</param>
public sealed record SignalAccessDecision(
    SignalAccessOutcome Outcome, DecisionLane? Lane, Guid? PolicyVersionId, Guid? CoreTypeId = null, Guid? CoreId = null,
    string? SubjectTable = null, Guid? SubjectId = null, Guid? CallerContactId = null, string? TodoNotes = null, Guid? TodoMatterId = null,
    string? CoreTable = null)
{
    public static readonly SignalAccessDecision NotFound = new(SignalAccessOutcome.NotFound, null, null);
    public static readonly SignalAccessDecision CallerUnresolved = new(SignalAccessOutcome.CallerUnresolved, null, null);
    public static readonly SignalAccessDecision Unavailable = new(SignalAccessOutcome.Unavailable, null, null);
}

/// <summary>
/// THE one place a Signal-keyed route decides whether the caller may see a Signal (task 036; task 038 reuses it
/// rather than writing a second). Every question is asked AS THE CALLER through <see cref="IDataverseUserClient"/>,
/// so Dataverse's own security model decides, never the BFF identity.
/// </summary>
/// <remarks>
/// <para><b>The rule (D-33..D-36, D-38).</b> (1) The caller reads the Signal row itself: Dataverse first trims by the
/// Signal's own business unit / secure-child sharing. (2) The caller then reads the Signal's CORE record, whatever its
/// type: the type comes from the <c>sprk_recordtype_ref</c> catalog row the Signal's <c>sprk_corerecordtype</c> names,
/// the id from <c>sprk_corerecordid</c>. No code here names a core type (D-36 extensibility). (3) A Signal with NO core
/// record is visible only in the Do lane (D-35) and only to a caller who can read its SUBJECT row (the subject lookup that carries
/// the pair id <c>sprk_regardingrecordid</c>, an ownership-parent table; task 141) as themselves, in addition to the Signal row (D-129: records are
/// owned by teams, so the old "caller is the Signal's owner" test matched nobody for a team-owned item). A subject in a secure
/// record family is unreadable to a non-member, so the Signal is too (D-123).</para>
/// <para><b>Do items are private to their assignee (D-141, task 150).</b> After (1)-(3) allow a Do-lane Signal, core or no-core,
/// the caller must also be one the worklist's Do-lane "mine" rule admits for the Signal's subject: the SAME request the worklist
/// sends (<see cref="SignalReadService.MineSubjectQuery"/> over <see cref="SignalReadService.DoLaneMineRules"/>, with the caller
/// and linked contact from <see cref="SignalReadService.ResolveMineCallerAsync"/>), never a copy of the predicate. A subject whose
/// table has no "mine" rule, a subject that cannot be derived, or a read that does not return the subject is the uniform
/// <see cref="SignalAccessOutcome.NotFound"/>, exactly as an unreadable Signal; a fault is <see cref="SignalAccessOutcome.Unavailable"/>.
/// So a same-BU colleague who can read the Signal and its subject cannot open or act on someone else's Do item by id. Decide-lane
/// Signals are unchanged. The commit route asks for the readability half first and the "mine" half itself
/// (<see cref="ConfirmDoLaneMineAsync"/>), skipping it only to return or resume this caller's own recorded review of the same
/// content: the decision may have reassigned the item (adversarial review F1).</para>
/// <para><b>Fail closed, one denial.</b> A row the caller cannot read, a malformed core-record pair, a catalog row that
/// does not resolve, a Dataverse fault of any kind: all <see cref="SignalAccessOutcome.NotFound"/>, except a fault reading a
/// no-core Signal's SUBJECT or answering a Do-lane Signal's "mine" read, which is <see cref="SignalAccessOutcome.Unavailable"/>
/// (503, uac-r2 F4-b: a fault is not a denial). Only a caller who cannot be resolved at all is
/// <see cref="SignalAccessOutcome.CallerUnresolved"/>.</para>
/// <para>The catalog row and entity-set name are configuration and metadata, not matter data, so they are read with the
/// BFF's own <see cref="IGenericEntityService"/>; nothing read that way is returned to the caller.</para>
/// <para><b>Core set (K2).</b> The core record's table must be one of <see cref="CoreAncestorResolver.CoreRecordEntities"/>,
/// uac-r2's access-control core taxonomy, however the catalog row reads. Without it a user with Write on
/// <c>sprk_signal</c> could point a Signal's core record at any row they can read and use the route as a probe.
/// Still generic: the check is membership in uac-r2's list, not a branch per type (a fifth core type added there is
/// accepted here unchanged).</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <c>AuthorizationService.GetCallerRecordAccessAsync</c>
/// (uac-r2 task 070) already answers "may this caller read this record" for any entity set, as the caller, failing closed
/// without a token. Extension: that method needs the caller's user id and bearer token plumbed in and returns a rights
/// snapshot cached by <c>CachedAccessDataSource</c> (ADR-003 A1, 60 s). The decision here needs more than a rights bit: it needs
/// the Signal row's own columns (lane, policy version, core-record pair, subject pair) read under the caller's Dataverse
/// security, and a plain read answers "may they read it" and returns those columns in one call, uncached, so a revoked
/// share takes effect on the next request. Using it would add a second call per record and a stale window for no gain.
/// Cost of doing nothing: no check at all; the route would serve a plan for a Signal on a matter the caller cannot open.
/// If the reviewer prefers the shared mechanism for the CORE-record half, <c>GetCallerRecordAccessAsync</c> is a drop-in
/// for that one read.</para>
/// <para><b>uac-r2 mechanisms reused, none rebuilt:</b> the caller-identity client (<see cref="IDataverseUserClient"/>,
/// relocated by uac-r2 task 126) and the route-level uniform 404 / single 403 shapes. A caller-rights probe
/// (<c>CallerRecordAccessProbe</c>) answers a different question (rights beyond Read) and is not needed for a read.</para>
/// </remarks>
public sealed class SignalCoreRecordAccess
{
    private const string RecordTypeRefEntity = "sprk_recordtype_ref";
    private const string RecordLogicalNameColumn = "sprk_recordlogicalname";

    private readonly IDataverseUserClient _userClient;
    private readonly IGenericEntityService _entities;
    private readonly IIdentityNormalizationService _identity;
    private readonly ILogger<SignalCoreRecordAccess> _logger;

    public SignalCoreRecordAccess(
        IDataverseUserClient userClient,
        IGenericEntityService entities,
        IIdentityNormalizationService identity,
        ILogger<SignalCoreRecordAccess> logger)
    {
        _userClient = userClient ?? throw new ArgumentNullException(nameof(userClient));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The decision: readability and, for a Do-lane Signal, the "mine" rule (D-141). Read routes never pass
    /// <paramref name="enforceDoLaneMine"/>. False is for the commit route only, which then calls <see cref="ConfirmDoLaneMineAsync"/>
    /// itself unless it is returning or resuming the caller's own recorded review of the same content; an allowed Do-lane decision
    /// carries its subject for that later call. (One method, not an overload: the route ledger requires every body of
    /// <c>AuthorizeAsync</c> to reach the caller-identity client.)
    /// </summary>
    public async Task<SignalAccessDecision> AuthorizeAsync(Guid signalId, CancellationToken ct, bool enforceDoLaneMine = true)
    {
        if (signalId == Guid.Empty)
        {
            return SignalAccessDecision.NotFound;
        }

        try
        {
            var signal = await _userClient.GetAsync(
                $"sprk_signals({signalId})?$select=sprk_lane,_sprk_policyversion_value,_sprk_corerecordtype_value,sprk_corerecordid,sprk_regardingrecordid,{SubjectLookupSelect}",
                ct).ConfigureAwait(false);

            if (!signal.IsSuccess)
            {
                return await DeniedAsync(signal, "signal", ct).ConfigureAwait(false);
            }

            if (signal.Body is not { ValueKind: JsonValueKind.Object } row
                || !TryReadLane(row, out var lane)
                || !TryReadGuid(row, "_sprk_policyversion_value", out var policyVersionId))
            {
                _logger.LogWarning("Signal access: the Signal row is missing its lane or policy version; denying.");
                return SignalAccessDecision.NotFound;
            }

            var hasType = TryReadGuid(row, "_sprk_corerecordtype_value", out var coreTypeId);
            var coreIdText = ReadString(row, "sprk_corerecordid");

            if (!hasType && string.IsNullOrWhiteSpace(coreIdText))
            {
                return await AuthorizeNoCoreRecordAsync(row, lane, policyVersionId, enforceDoLaneMine, ct).ConfigureAwait(false);
            }

            // A pair with only one half is malformed, not "no core record": deny rather than treat it as owner-only.
            if (!hasType || !Guid.TryParse(coreIdText, out var coreId) || coreId == Guid.Empty)
            {
                _logger.LogWarning("Signal access: the Signal's core-record pair is incomplete or unparseable; denying.");
                return SignalAccessDecision.NotFound;
            }

            var logicalName = (await _entities
                .RetrieveAsync(RecordTypeRefEntity, coreTypeId, [RecordLogicalNameColumn], ct)
                .ConfigureAwait(false))
                .GetAttributeValue<string>(RecordLogicalNameColumn)?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(logicalName))
            {
                _logger.LogWarning("Signal access: the core record type does not resolve to a table; denying.");
                return SignalAccessDecision.NotFound;
            }

            if (!CoreAncestorResolver.CoreRecordEntities.Contains(logicalName, StringComparer.OrdinalIgnoreCase))
            {
                // K2: a Signal's core record can only be a core type; anything else is a forged or stale pointer.
                _logger.LogWarning("Signal access: the core record type is not an access-control core type; denying.");
                return SignalAccessDecision.NotFound;
            }

            var entitySet = await _entities.GetEntitySetNameAsync(logicalName, ct).ConfigureAwait(false);

            // createdon exists on every Dataverse table; the value is not used, only whether the caller may read the row.
            var core = await _userClient.GetAsync($"{entitySet}({coreId})?$select=createdon", ct).ConfigureAwait(false);
            if (!core.IsSuccess)
            {
                return await DeniedAsync(core, "core record", ct).ConfigureAwait(false);
            }

            var allowed = new SignalAccessDecision(SignalAccessOutcome.Allowed, lane, policyVersionId, coreTypeId, coreId, CoreTable: logicalName);
            if (lane != DecisionLane.Do)
            {
                return allowed;
            }

            var (subjectTable, subjectId) = SubjectOf(row);
            allowed = allowed with { SubjectTable = subjectTable, SubjectId = subjectId };
            return enforceDoLaneMine ? await ConfirmDoLaneMineAsync(allowed, ct).ConfigureAwait(false) : allowed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Fail closed: "could not answer" is "not authorized". Type only, never the message (it can echo row content).
            _logger.LogError("Signal access: the check faulted ({ExceptionType}); denying.", ex.GetType().Name);
            return SignalAccessDecision.NotFound;
        }
    }

    private async Task<SignalAccessDecision> AuthorizeNoCoreRecordAsync(
        JsonElement signalRow, DecisionLane lane, Guid policyVersionId, bool enforceDoLaneMine, CancellationToken ct)
    {
        // D-35 as amended by D-129 (records are owned by teams, never by a person): a no-core item is visible to whoever can read
        // its SUBJECT row as themselves, and only in the Do lane; and, D-141, only when the worklist's "mine" rule admits them. The Signal row itself was read as the caller before this point
        // (both reads must succeed), so the subject's own team and sharing decide, not an owner-equality test; a subject in a
        // secure record family is unreadable to a non-member and so is the uniform denial (D-123).
        if (lane != DecisionLane.Do)
        {
            return SignalAccessDecision.NotFound;
        }

        // Task 141: the subject table comes from the subject lookup that carries the pair id (SignalWriter.SubjectEntityOf),
        // never from sprk_regardingrecordtype, which is a lookup to sprk_recordtype_ref since task 140 (and absent on an
        // untyped pair).
        var (subjectType, subject) = SubjectOf(signalRow);
        var subjectId = subject ?? Guid.Empty;
        if (subjectId == Guid.Empty || !RecordOwnershipResolver.IsOwnershipParent(subjectType))
        {
            // No subject pair, or a table that is not one of uac-r2's ownership parents: a forged or stale pointer (the K2 rule,
            // applied to the subject). Defence in depth for the second half: every table sprk_signal has a subject lookup for is
            // an ownership parent today (pinned by DecisionActionCatalogTests), so only an added non-parent lookup reaches it.
            _logger.LogWarning("Signal access: the no-core Signal's subject pair is missing or not an ownership-parent table; denying.");
            return SignalAccessDecision.NotFound;
        }

        string entitySet;
        try
        {
            entitySet = await _entities.GetEntitySetNameAsync(subjectType!, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError("Signal access: the subject's entity set could not be looked up ({ExceptionType}); unavailable.", ex.GetType().Name);
            return SignalAccessDecision.Unavailable;
        }

        // createdon exists on every Dataverse table; the value is not used, only whether the caller may read the row.
        DataverseUserResponse subjectRead;
        try
        {
            subjectRead = await _userClient.GetAsync($"{entitySet}({subjectId})?$select=createdon", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError("Signal access: the subject read threw ({ExceptionType}); unavailable.", ex.GetType().Name);
            return SignalAccessDecision.Unavailable;
        }

        if (!subjectRead.IsSuccess)
        {
            return await DeniedAsync(subjectRead, "subject", ct, faultIsUnavailable: true).ConfigureAwait(false);
        }

        var allowed = new SignalAccessDecision(
            SignalAccessOutcome.Allowed, lane, policyVersionId, SubjectTable: subjectType, SubjectId: subjectId);
        return enforceDoLaneMine ? await ConfirmDoLaneMineAsync(allowed, ct).ConfigureAwait(false) : allowed;
    }

    /// <summary>The subject the rest of this class uses (task 141): the subject lookup that carries the pair id, and that id.</summary>
    private static (string? Table, Guid? Id) SubjectOf(JsonElement signalRow)
    {
        Guid? subjectId = Guid.TryParse(ReadString(signalRow, "sprk_regardingrecordid"), out var pairId) && pairId != Guid.Empty ? pairId : null;
        var subjectType = SignalWriter.SubjectEntityOf(
            subjectId ?? Guid.Empty, lookup => TryReadGuid(signalRow, $"_{lookup}_value", out var id) ? id : null);
        return (subjectType, subjectId);
    }

    /// <summary>
    /// D-141 (task 150): a Do-lane Signal the readability reads allowed stays allowed only when the worklist's "mine" read, asked AS
    /// THE CALLER for this Signal's subject, returns that subject. Otherwise the uniform <see cref="SignalAccessOutcome.NotFound"/>; a
    /// fault is <see cref="SignalAccessOutcome.Unavailable"/> (the worklist answers 503 for the same faults). Any decision that is not
    /// an allowed Do-lane one is returned unchanged.
    /// </summary>
    internal async Task<SignalAccessDecision> ConfirmDoLaneMineAsync(SignalAccessDecision allowed, CancellationToken ct)
    {
        if (allowed.Outcome != SignalAccessOutcome.Allowed || allowed.Lane != DecisionLane.Do)
        {
            return allowed;
        }

        if (allowed.SubjectTable is not { } subjectType || allowed.SubjectId is not { } subjectId
            || !SignalReadService.DoLaneMineRules.TryGetValue(subjectType, out var rule))
        {
            // No derivable subject, or a subject table with no "mine" rule: the worklist shows such a Do row to nobody.
            _logger.LogDebug("Signal access: the Do-lane Signal's subject cannot be derived or has no mine rule; denying.");
            return SignalAccessDecision.NotFound;
        }

        try
        {
            var caller = await SignalReadService.ResolveMineCallerAsync(_userClient, _identity, _logger, ct).ConfigureAwait(false);
            if (caller.Failure is { } failure)
            {
                return failure.Outcome == SignalReadOutcome.CallerUnresolved
                    ? SignalAccessDecision.CallerUnresolved
                    : SignalAccessDecision.Unavailable;
            }

            var entitySet = await _entities.GetEntitySetNameAsync(rule.Table, ct).ConfigureAwait(false);
            // Task 154 (D-111, pass 1 K3, pass 2 F1): this single-id read also selects a To Do's notes (the inquiry hint) and its
            // matter (the fallback); the worklist's chunked read of the same rule does not.
            var mine = await _userClient.GetAsync(
                SignalReadService.MineSubjectQuery(rule, entitySet, [subjectId], caller.CallerId, caller.ContactId, includeTodoNotes: true), ct)
                .ConfigureAwait(false);
            if (!mine.IsSuccess)
            {
                if (mine.ErrorCode is DataverseUserClientErrorCodes.UserContextRequired
                    or DataverseUserClientErrorCodes.OboExchangeFailed
                    or DataverseUserClientErrorCodes.OboNotConfigured)
                {
                    return SignalAccessDecision.CallerUnresolved;
                }

                if (mine.ErrorCode == DataverseUserClientErrorCodes.AccessDenied)
                {
                    // No Read on the subject's table at all: nothing there is the caller's (the worklist drops these rows too).
                    _logger.LogDebug("Signal access: the caller has no read on the Do-lane subject's table; denying.");
                    return SignalAccessDecision.NotFound;
                }

                _logger.LogWarning("Signal access: the Do-lane mine read failed ({ErrorCode}); unavailable.", mine.ErrorCode);
                return SignalAccessDecision.Unavailable;
            }

            if (mine.Body is not { ValueKind: JsonValueKind.Object } body
                || !body.TryGetProperty("value", out var rows) || rows.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("Signal access: the Do-lane mine read was unparseable; unavailable.");
                return SignalAccessDecision.Unavailable;
            }

            var pk = $"{rule.Table}id";
            foreach (var subject in rows.EnumerateArray())
            {
                if (TryReadGuid(subject, pk, out var id) && id == subjectId)
                {
                    // Task 154 (D-111): the same row carries the To Do's notes and matter (no request of its own); whether it records
                    // an open inquiry is read only by the route that needs it (CheckSubjectTodoInquiryAsync).
                    if (!string.Equals(rule.Table, "sprk_todo", StringComparison.OrdinalIgnoreCase))
                    {
                        return allowed with { CallerContactId = caller.ContactId };
                    }

                    return allowed with
                    {
                        CallerContactId = caller.ContactId,
                        TodoNotes = ReadNotes(subject),
                        TodoMatterId = TryReadGuid(subject, SignalReadService.TodoMatterColumn, out var todoMatter) ? todoMatter : null,
                    };
                }
            }

            _logger.LogDebug("Signal access: the Do-lane Signal is not the caller's own work (D-141); denying.");
            return SignalAccessDecision.NotFound;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError("Signal access: the Do-lane mine check faulted ({ExceptionType}); unavailable.", ex.GetType().Name);
            return SignalAccessDecision.Unavailable;
        }
    }

    private static string? ReadNotes(JsonElement row) =>
        row.TryGetProperty(SignalReadService.TodoNotesColumn, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>
    /// Task 154 (D-111, pass 2 F1), for the plan route: whether the allowed Do-lane Signal's subject To Do is the "Record the outcome"
    /// To Do of an OPEN inquiry, read AS THE CALLER with the notes and matter the gate's own "mine" read returned
    /// (<see cref="SignalAccessDecision.TodoNotes"/>, <see cref="SignalAccessDecision.TodoMatterId"/>): the commit route's own
    /// identification (<see cref="InquiryDispositionService.FindOpenInquiryOfTodoAsync"/>), so the plan never offers a Mark complete
    /// the commit refuses. One GET of the hinted inquiry, or one list of the matter's open inquiries when there is no usable hint, or
    /// nothing for a To Do with neither. Any other subject is <see cref="InquiryOutcomeTodoCheck.Ordinary"/> with no request; a fault
    /// is <see cref="InquiryOutcomeTodoCheck.Unavailable"/>, never a guess.
    /// </summary>
    internal async Task<InquiryOutcomeTodoCheck> CheckSubjectTodoInquiryAsync(SignalAccessDecision decision, CancellationToken ct)
    {
        if (decision.Outcome != SignalAccessOutcome.Allowed || decision.Lane != DecisionLane.Do
            || !string.Equals(decision.SubjectTable, "sprk_todo", StringComparison.OrdinalIgnoreCase) || decision.SubjectId is not { } todoId)
        {
            return InquiryOutcomeTodoCheck.Ordinary;
        }

        try
        {
            return ToCheck(await InquiryDispositionService.FindOpenInquiryOfTodoAsync(
                _userClient, _logger, todoId, decision.TodoNotes, decision.TodoMatterId, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError("Signal access: the subject To Do's inquiry check faulted ({ExceptionType}); unavailable.", ex.GetType().Name);
            return InquiryOutcomeTodoCheck.Unavailable;
        }
    }

    /// <summary>
    /// Task 161 (D-180): the ONE substitution both routes apply. A plan that offers Revise budget on a MATTER the caller reads as having
    /// no ACTIVE budget (<c>sprk_budget</c> with <c>sprk_matter</c> = the core record and <c>statecode</c> 0) offers Create budget in its
    /// place (<see cref="ResolvedDecisionPlan.WithCreateBudgetInsteadOfRevise"/>). The count is read AS THE CALLER, one request, and
    /// only when the plan offers Revise budget and the core record is a matter. A count that cannot be read (a fault, a refusal, an
    /// answer that is not a list) substitutes nothing and never fails the route: the existing Revise budget path stays.
    /// </summary>
    internal async Task<ResolvedDecisionPlan> WithCreateBudgetWhenNoneAsync(
        ResolvedDecisionPlan plan, SignalAccessDecision decision, CancellationToken ct)
    {
        if (!plan.Actions.Any(a => a.Code == ResolvedDecisionPlan.ReviseBudgetCode)
            || !string.Equals(decision.CoreTable, "sprk_matter", StringComparison.OrdinalIgnoreCase)
            || decision.CoreId is not { } matterId || matterId == Guid.Empty)
        {
            return plan;
        }

        try
        {
            var response = await _userClient.GetAsync(
                $"{DecisionExec.BudgetSet}?$select=sprk_budgetid&$filter=_sprk_matter_value eq {matterId:D} and statecode eq 0&$top=1", ct).ConfigureAwait(false);
            if (!response.IsSuccess || response.Body is not { ValueKind: JsonValueKind.Object } body
                || !body.TryGetProperty("value", out var rows) || rows.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("Signal access: the matter's active budgets could not be counted ({ErrorCode}); Revise budget is offered as it is.", response.ErrorCode);
                return plan;
            }

            return rows.GetArrayLength() == 0 ? plan.WithCreateBudgetInsteadOfRevise() : plan;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Signal access: counting the matter's active budgets faulted ({ExceptionType}); Revise budget is offered as it is.", ex.GetType().Name);
            return plan;
        }
    }

    /// <summary>The one reading of <see cref="InquiryDispositionService.FindOpenInquiryOfTodoAsync"/> for both routes: anything but
    /// an open inquiry or none (a fault) is unavailable.</summary>
    private static InquiryOutcomeTodoCheck ToCheck(InquiryTodoMatchResult match) => match.Match switch
    {
        InquiryTodoMatch.OpenInquiry => InquiryOutcomeTodoCheck.OpenInquiry,
        InquiryTodoMatch.None => InquiryOutcomeTodoCheck.Ordinary,
        _ => InquiryOutcomeTodoCheck.Unavailable,
    };

    /// <summary>
    /// Task 154 (D-111, adversarial pass 1), for the commit route: whether the To Do <paramref name="todoId"/> is the "Record the
    /// outcome" To Do of an OPEN inquiry, read AS THE CALLER. One GET of the To Do (its notes and matter), then
    /// <see cref="InquiryDispositionService.FindOpenInquiryOfTodoAsync"/>, the for-todo lookup's own identification: one GET of the
    /// hinted inquiry, or, when the hint fails, one list of the matter's open outbound inquiries. A To Do the caller cannot read (403/404)
    /// is <see cref="InquiryOutcomeTodoCheck.NotReadable"/>, the same answer as any unreadable item; any other failure or a throw is
    /// <see cref="InquiryOutcomeTodoCheck.Unavailable"/>, never a guess.
    /// </summary>
    internal async Task<InquiryOutcomeTodoCheck> CheckInquiryOutcomeTodoAsync(Guid todoId, CancellationToken ct)
    {
        try
        {
            var response = await _userClient.GetAsync(
                $"sprk_todos({todoId:D})?$select={SignalReadService.TodoNotesColumn},{SignalReadService.TodoMatterColumn}", ct).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                if (response.StatusCode is 403 or 404)
                {
                    _logger.LogDebug("Signal access: the caller cannot read the To Do ({ErrorCode}); not found.", response.ErrorCode);
                    return InquiryOutcomeTodoCheck.NotReadable;
                }

                _logger.LogWarning("Signal access: the To Do's inquiry hint could not be read ({ErrorCode}); unavailable.", response.ErrorCode);
                return InquiryOutcomeTodoCheck.Unavailable;
            }

            if (response.Body is not { ValueKind: JsonValueKind.Object } body)
            {
                _logger.LogWarning("Signal access: the To Do's inquiry hint was unparseable; unavailable.");
                return InquiryOutcomeTodoCheck.Unavailable;
            }

            Guid? matterId = TryReadGuid(body, SignalReadService.TodoMatterColumn, out var matter) ? matter : null;
            return ToCheck(await InquiryDispositionService.FindOpenInquiryOfTodoAsync(_userClient, _logger, todoId, ReadNotes(body), matterId, ct)
                .ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError("Signal access: the To Do's inquiry check faulted ({ExceptionType}); unavailable.", ex.GetType().Name);
            return InquiryOutcomeTodoCheck.Unavailable;
        }
    }

    private async Task<SignalAccessDecision> DeniedAsync(
        DataverseUserResponse response, string what, CancellationToken ct, bool faultIsUnavailable = false)
    {
        if (response.ErrorCode is DataverseUserClientErrorCodes.UserContextRequired
            or DataverseUserClientErrorCodes.OboExchangeFailed
            or DataverseUserClientErrorCodes.OboNotConfigured)
        {
            _logger.LogWarning("Signal access: the caller could not be resolved reading the {What} ({ErrorCode}).",
                what, response.ErrorCode);
            return SignalAccessDecision.CallerUnresolved;
        }

        // A 403 from Dataverse is "no right on this row" for a user it knows, and also the answer for a valid token whose
        // owner has no Dataverse user at all. WhoAmI needs no privilege, so asking it now tells the two apart: a caller who
        // cannot be identified is the single 403 (D-29), as in the events list; one who can is an ordinary no-read 404.
        if (response.ErrorCode == DataverseUserClientErrorCodes.AccessDenied && what != "caller identity")
        {
            var who = await _userClient.GetAsync("WhoAmI", ct).ConfigureAwait(false);
            if (!who.IsSuccess)
            {
                // Only "the caller has no Dataverse identity" is unresolved: Dataverse refusing them, or the same user-context
                // / OBO codes the first read maps. Throttling, a 5xx or a transport fault says nothing about WHO they are, so a
                // known caller under load must get the same 404 whether or not the row exists.
                if (who.ErrorCode is DataverseUserClientErrorCodes.AccessDenied
                    or DataverseUserClientErrorCodes.UserContextRequired
                    or DataverseUserClientErrorCodes.OboExchangeFailed
                    or DataverseUserClientErrorCodes.OboNotConfigured)
                {
                    _logger.LogWarning("Signal access: reading the {What} was denied and the caller has no Dataverse identity ({ErrorCode}).",
                        what, who.ErrorCode);
                    return SignalAccessDecision.CallerUnresolved;
                }

                _logger.LogWarning("Signal access: reading the {What} was denied and the caller's identity could not be checked ({ErrorCode}); denying as not found.",
                    what, who.ErrorCode);
            }
        }

        // 404/403 are the expected "cannot read"; anything else is a fault. Both are the same denial to the caller;
        // the log keeps them apart for the operator.
        if (response.ErrorCode is DataverseUserClientErrorCodes.NotFound or DataverseUserClientErrorCodes.AccessDenied)
        {
            _logger.LogDebug("Signal access: the caller cannot read the {What} ({ErrorCode}).", what, response.ErrorCode);
        }
        else
        {
            _logger.LogWarning("Signal access: reading the {What} failed ({ErrorCode}); denying.", what, response.ErrorCode);
            if (faultIsUnavailable)
            {
                // A throttle, a 5xx or a transport fault says nothing about whether the caller may read the row: not a denial (503).
                return SignalAccessDecision.Unavailable;
            }
        }

        return SignalAccessDecision.NotFound;
    }

    private static bool TryReadLane(JsonElement row, out DecisionLane lane)
    {
        lane = default;
        if (row.TryGetProperty("sprk_lane", out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var raw) && Enum.IsDefined(typeof(DecisionLane), raw))
        {
            lane = (DecisionLane)raw;
            return true;
        }

        return false;
    }

    /// <summary>The Signal's subject lookups as Web API <c>$select</c> names (<c>_sprk_regardingmatter_value</c>, ...), task 141.</summary>
    private static readonly string SubjectLookupSelect =
        string.Join(",", SignalWriter.RegardingLookupByEntity.Values.Select(lookup => $"_{lookup}_value"));

    private static bool TryReadGuid(JsonElement row, string name, out Guid id)
    {
        id = Guid.Empty;
        return row.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && Guid.TryParse(value.GetString(), out id)
            && id != Guid.Empty;
    }

    private static string? ReadString(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
