using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Signals.Actions;

// =====================================================================================================================
// The decision action executors (ontology platform R1 task 044; spec FR-52, FR-61; D-18, D-19, D-27, D-29, D-54, D-55).
//
// One executor per catalog action that the commit route (task 043) calls, except send-budget-inquiry (task 070) and
// assign-work (task 046). They are INTERNAL SERVICES, not routes. Each one:
//   * refuses BEFORE it writes when the signed-in caller lacks the right (the outcome is Refused and nothing was written);
//   * performs exactly its write, through the shipped core for that table, as the caller;
//   * returns the written record ids so the commit route can list them in the Decision Record's steps and follow-ons.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11), for the whole file.
//   Existing: the events complete handler, the child-records create/update cores, CommunicationService.SendAsync and the
//     events due-date/assignee write (DecisionRouteCores), plus OntologyWriterDataverseClient for the one write that has no
//     route (the budget revision).
//   Extension: yes, every executor but ReviseBudget is a thin adapter over one of those. Revise budget has no existing path
//     (#26): nothing creates sprk_budgetrevision, and the writer identity is the only one granted Create on it (task 008).
//   Cost of doing nothing: Path B's own remedy cannot be recorded, the Do lane cannot run inside a decision, and Next steps
//     stay browser-created after a record that can no longer list them (#23).
// =====================================================================================================================

/// <summary>How an executor ended.</summary>
public enum DecisionActionStatus
{
    /// <summary>The write happened. <see cref="DecisionActionOutcome.Written"/> lists what was written.</summary>
    Done,

    /// <summary>NOTHING was written: bad input, a missing right, an unsupported subject, or the core refused first.</summary>
    Refused,

    /// <summary>A write was attempted and did not complete cleanly. <see cref="DecisionActionOutcome.Written"/> lists the
    /// records CONFIRMED to have landed (the budget revision when the amount was refused);
    /// <see cref="DecisionActionOutcome.PossiblyWritten"/> lists those whose write got no clear answer (a 5xx, a timeout or a
    /// transport failure after the request was sent). The commit route reports both; it never retries with another identity.</summary>
    Failed,
}

/// <summary>A record an executor wrote.</summary>
public sealed record DecisionRecordRef(string Entity, Guid Id);

/// <summary>The core record a Signal groups under, which Next steps are filed under.</summary>
/// <param name="Entity">Its logical name; one of <c>CoreAncestorResolver.CoreRecordEntities</c>.</param>
/// <param name="Id">Its id.</param>
/// <param name="RecordTypeRefId">The <c>sprk_recordtype_ref</c> row the Signal names (<c>sprk_corerecordtype</c>), for the
/// ADR-024 type field.</param>
/// <param name="Name">Its display name, for the ADR-024 name field.</param>
public sealed record DecisionCoreRecord(string Entity, Guid Id, Guid? RecordTypeRefId = null, string? Name = null);

/// <summary>What the commit route hands an executor.</summary>
/// <param name="Http">The request the decision arrived on: every write is authorized and attributed to its caller.</param>
/// <param name="SignalId">The Signal being resolved.</param>
/// <param name="Subject">The Signal's subject, the item a Do action acts on (an event, a To Do, a work assignment).</param>
/// <param name="Core">The core record the Signal groups under; null for a no-core Do item (D-35).</param>
/// <param name="MatterId">The Signal's grouping matter; null when it has none.</param>
/// <param name="Parameters">The wizard's answers, keyed by the catalog's parameter codes.</param>
public sealed record DecisionActionRequest(
    HttpContext Http,
    Guid SignalId,
    DecisionRecordRef? Subject,
    DecisionCoreRecord? Core,
    Guid? MatterId,
    IReadOnlyDictionary<string, string?> Parameters)
{
    public string? Param(string code) =>
        Parameters.TryGetValue(code, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}

/// <summary>An executor's answer.</summary>
public sealed record DecisionActionOutcome(
    string Code,
    DecisionActionStatus Status,
    IReadOnlyList<DecisionRecordRef> Written,
    string? ReasonCode = null,
    string? Detail = null,
    IReadOnlyList<DecisionRecordRef>? PossiblyWrittenRecords = null)
{
    /// <summary>Records whose write got no clear answer (5xx, timeout, transport failure): they MAY have changed. Empty unless
    /// <see cref="DecisionActionStatus.Failed"/>.</summary>
    public IReadOnlyList<DecisionRecordRef> PossiblyWritten => PossiblyWrittenRecords ?? [];

    public static DecisionActionOutcome Done(string code, params DecisionRecordRef[] written) =>
        new(code, DecisionActionStatus.Done, written);

    public static DecisionActionOutcome Refused(string code, string reasonCode, string detail) =>
        new(code, DecisionActionStatus.Refused, [], reasonCode, detail);

    /// <param name="written">Confirmed to have landed.</param>
    /// <param name="possiblyWritten">Sent, but without a clear answer.</param>
    public static DecisionActionOutcome Failed(
        string code, string reasonCode, string detail,
        IReadOnlyList<DecisionRecordRef>? written = null, IReadOnlyList<DecisionRecordRef>? possiblyWritten = null) =>
        new(code, DecisionActionStatus.Failed, written ?? [], reasonCode, detail, possiblyWritten);
}

/// <summary>Stable reason codes an executor outcome carries.</summary>
public static class DecisionActionReasons
{
    public const string InvalidParameter = "decision.action.invalid_parameter";
    public const string SubjectUnsupported = "decision.action.subject_unsupported";
    public const string NotAuthorized = "decision.action.not_authorized";
    public const string NotFound = "decision.action.not_found";
    public const string InvalidState = "decision.action.invalid_state";
    public const string Conflict = "decision.action.conflict";
    public const string WriteFailed = "decision.action.write_failed";
    public const string RecipientUnresolved = "decision.action.recipient_unresolved";
    public const string BudgetNotOnMatter = "decision.action.budget_not_on_matter";
    public const string CallerUnresolved = "decision.action.caller_unresolved";
    public const string OwnerUnresolved = "decision.action.owner_unresolved";

    /// <summary>The revision was written by the writer but the signed-in user could not write the budget amount (D-55).</summary>
    public const string BudgetAmountNotWritten = "decision.action.budget_amount_not_written";
}

/// <summary>One decision action's executor. Its <see cref="Code"/> is a catalog code.</summary>
public interface IDecisionActionExecutor
{
    string Code { get; }

    /// <summary>
    /// Every refusal <see cref="ExecuteAsync"/> could give BEFORE it writes, with NO write: null means "would proceed"; otherwise
    /// the same <see cref="DecisionActionStatus.Refused"/> outcome <see cref="ExecuteAsync"/> would return. The commit route
    /// runs it for every action of a decision before it executes the first, so a refusal never lands after another action's
    /// write. A write can still fail afterwards (a race, a fault); that is <see cref="DecisionActionStatus.Failed"/>.
    /// </summary>
    Task<DecisionActionOutcome?> PreflightAsync(DecisionActionRequest request, CancellationToken ct);

    /// <summary>Runs <see cref="PreflightAsync"/> first, returning its refusal; otherwise performs the write.</summary>
    Task<DecisionActionOutcome> ExecuteAsync(DecisionActionRequest request, CancellationToken ct);
}

/// <summary>
/// The executors by catalog code. Built from DI; refuses to build when an executor names a code that is not in the closed
/// catalog or two executors claim one code, so a typo cannot silently drop an action.
/// </summary>
public sealed class DecisionActionExecutors
{
    /// <summary>The catalog actions whose executor is another task's: the inquiry (070) and Assign Work (046, D-21).</summary>
    public static readonly IReadOnlySet<string> OwnedElsewhere =
        new HashSet<string>(StringComparer.Ordinal) { "send-budget-inquiry", "assign-work" };

    private readonly Dictionary<string, IDecisionActionExecutor> _byCode = new(StringComparer.Ordinal);

    public DecisionActionExecutors(IEnumerable<IDecisionActionExecutor> executors)
    {
        foreach (var executor in executors)
        {
            if (!DecisionActionCatalog.TryGet(executor.Code, out _))
            {
                throw new InvalidOperationException($"Decision executor '{executor.GetType().Name}' names '{executor.Code}', which is not in the action catalog.");
            }

            if (!_byCode.TryAdd(executor.Code, executor))
            {
                throw new InvalidOperationException($"Two decision executors claim the code '{executor.Code}'.");
            }
        }
    }

    public bool TryGet(string? code, out IDecisionActionExecutor executor)
    {
        if (code is not null && _byCode.TryGetValue(code, out var found))
        {
            executor = found;
            return true;
        }

        executor = null!;
        return false;
    }

    /// <summary>Catalog codes this task owns that have no executor registered: empty when the set is complete.</summary>
    public IReadOnlyList<string> MissingCodes() =>
        DecisionActionCatalog.All.Select(a => a.Code).Where(c => !OwnedElsewhere.Contains(c) && !_byCode.ContainsKey(c)).ToList();
}

/// <summary>Shared parsing and rights helpers; no executor re-implements either.</summary>
internal static class DecisionExec
{
    // Entity SET names, read from live metadata (describe "Collection Name", spaarkedev1, 2026-10-09). Never a pluralized
    // logical name: a wrong set fails closed as a denial indistinguishable from "no access".
    internal const string BudgetSet = "sprk_budgets";
    internal const string EventSet = "sprk_events";
    internal const string TodoSet = "sprk_todos";
    internal const string WorkAssignmentSet = "sprk_workassignments";
    internal const string ContactSet = "contacts";

    /// <summary>Create on <c>sprk_todo</c> (live privilege name; the event one is <c>CommunicationRecordAuthorizationFilter.CreateEventPrivilege</c>).</summary>
    internal const string CreateTodoPrivilege = "prvCreatesprk_Todo";

    /// <summary>The <see cref="OperationAccessPolicy"/> key whose right is AppendTo (reused, as the upload route does).</summary>
    internal const string AppendToOperation = "entity.associate_document";

    internal static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    internal static bool TryGuid(string? value, out Guid id) => Guid.TryParse(value, out id) && id != Guid.Empty;

    internal static string IsoDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static Guid? CallerObjectId(HttpContext http) =>
        Guid.TryParse(CallerResolution.ResolveObjectId(http.User), out var oid) && oid != Guid.Empty ? oid : null;

    /// <summary>Whether the signed-in caller holds <paramref name="operation"/> (an <see cref="OperationAccessPolicy"/> key)
    /// on the record. Every "could not answer" is a no.</summary>
    internal static async Task<bool> CallerHoldsAsync(
        CallerRecordAccessProbe probe, HttpContext http, string entitySet, Guid id, string operation, CancellationToken ct)
    {
        try
        {
            var rights = await probe.GetCallerRightsAsync(TokenHelper.ExtractBearerTokenOrNull(http), entitySet, id, ct).ConfigureAwait(false);
            return OperationAccessPolicy.HasRequiredRights(rights, operation);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Whether the caller holds a Dataverse TABLE privilege (by its live name). A fault is a no.</summary>
    internal static async Task<bool> CallerHoldsPrivilegeAsync(
        CallerRecordAccessProbe probe, HttpContext http, string privilege, CancellationToken ct)
    {
        try
        {
            return await probe.CallerHoldsPrivilegeAsync(TokenHelper.ExtractBearerTokenOrNull(http), privilege, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>AppendTo on a record named by logical name, through the shared logical-name to entity-set table (a miss denies).</summary>
    internal static async Task<bool> CallerCanAppendToAsync(
        CallerRecordAccessProbe probe, HttpContext http, string entityLogicalName, Guid id, CancellationToken ct) =>
        EntityAccessFilter.TryResolveEntitySet(entityLogicalName, out var set)
        && await CallerHoldsAsync(probe, http, set, id, AppendToOperation, ct).ConfigureAwait(false);

    internal static JsonElement Json(IDictionary<string, object?> body) =>
        JsonSerializer.SerializeToElement(body);

    /// <summary>Maps a shipped core's answer to an outcome. A 4xx refusal wrote nothing; any other failure (5xx, no answer) MAY
    /// have been applied, so <paramref name="possiblyWritten"/> is listed for the commit route to name.</summary>
    internal static DecisionActionOutcome FromReply(string code, RouteReply reply, params DecisionRecordRef[] possiblyWritten)
    {
        if (reply.IsSuccess)
        {
            return DecisionActionOutcome.Done(code, possiblyWritten);
        }

        var detail = reply.Detail ?? $"The record could not be updated (status {reply.Status}).";
        return reply.Status switch
        {
            400 or 422 => DecisionActionOutcome.Refused(code, reply.ReasonCode ?? DecisionActionReasons.InvalidState, detail),
            401 or 403 => DecisionActionOutcome.Refused(code, DecisionActionReasons.NotAuthorized, detail),
            404 => DecisionActionOutcome.Refused(code, DecisionActionReasons.NotFound, detail),
            409 => DecisionActionOutcome.Refused(code, DecisionActionReasons.Conflict, detail),
            _ => DecisionActionOutcome.Failed(code, DecisionActionReasons.WriteFailed, detail + " It may have been applied.", possiblyWritten: possiblyWritten),
        };
    }

    internal static DecisionActionOutcome FromEventWrite(string code, EventDueAssigneeResult result, DecisionRecordRef subject) =>
        result.Outcome switch
        {
            EventDueAssigneeOutcome.Written => DecisionActionOutcome.Done(code, subject),
            EventDueAssigneeOutcome.NotFound => DecisionActionOutcome.Refused(code, DecisionActionReasons.NotFound, "The event was not found."),
            EventDueAssigneeOutcome.InvalidState => DecisionActionOutcome.Refused(code, DecisionActionReasons.InvalidState, result.Detail ?? "The event is not open work."),
            EventDueAssigneeOutcome.Denied => DecisionActionOutcome.Refused(code, DecisionActionReasons.NotAuthorized, "You do not have the rights this change needs on the event."),
            _ => DecisionActionOutcome.Failed(code, DecisionActionReasons.WriteFailed, "The event could not be updated; it may have been applied.", possiblyWritten: [subject]),
        };

    /// <summary>The outcome of a caller PATCH on a work assignment: Done, a refusal, or Failed with the item possibly written.</summary>
    internal static DecisionActionOutcome FromPatch(string code, DataverseUserResponse patch, DecisionRecordRef subject, string what) =>
        patch.IsSuccess
            ? DecisionActionOutcome.Done(code, subject)
            : patch.StatusCode is 401 or 403
                ? DecisionActionOutcome.Refused(code, DecisionActionReasons.NotAuthorized, "You do not have permission to change this work assignment.")
                : patch.StatusCode == 404
                    ? DecisionActionOutcome.Refused(code, DecisionActionReasons.NotFound, "The work assignment was not found.")
                    : DecisionActionOutcome.Failed(code, DecisionActionReasons.WriteFailed, $"The {what} could not be confirmed; it may have been applied.", possiblyWritten: [subject]);

    /// <summary>The refusal for an event that cannot be read or is not open work, or null. No write.</summary>
    internal static async Task<DecisionActionOutcome?> OpenEventRefusalAsync(
        string code, IDataverseUserClient user, Guid eventId, CancellationToken ct)
    {
        var (refusal, _) = await EventDueAssigneeWrite.ReadStatusAsync(user, eventId, ct).ConfigureAwait(false);
        return refusal is null ? null : refusal.Outcome switch
        {
            EventDueAssigneeOutcome.NotFound => DecisionActionOutcome.Refused(code, DecisionActionReasons.NotFound, "The event was not found."),
            EventDueAssigneeOutcome.InvalidState => DecisionActionOutcome.Refused(code, DecisionActionReasons.InvalidState, refusal.Detail ?? "The event is not open work."),
            _ => DecisionActionOutcome.Refused(code, DecisionActionReasons.NotFound, "The event could not be read."),
        };
    }
}

/// <summary>
/// The shape every executor but <c>approve-variance</c>, <c>revise-budget</c> and the messages shares: <see cref="PreflightAsync"/>
/// holds ALL the refusals (no write), <see cref="WriteAsync"/> only writes, and <see cref="ExecuteAsync"/> is the two in order.
/// An exception during the write (a timeout, a fault) is reported as <see cref="DecisionActionStatus.Failed"/> naming
/// <see cref="PossiblyWritten"/>, never thrown: the commit route needs to say what might have landed.
/// </summary>
public abstract class DecisionExecutorBase : IDecisionActionExecutor
{
    public abstract string Code { get; }

    public abstract Task<DecisionActionOutcome?> PreflightAsync(DecisionActionRequest request, CancellationToken ct);

    protected abstract Task<DecisionActionOutcome> WriteAsync(DecisionActionRequest request, CancellationToken ct);

    /// <summary>What a write that did not finish cleanly may have changed: the item acted on. A create names nothing (no id yet).</summary>
    protected virtual IReadOnlyList<DecisionRecordRef> PossiblyWritten(DecisionActionRequest request) =>
        request.Subject is { } subject ? [subject] : [];

    public async Task<DecisionActionOutcome> ExecuteAsync(DecisionActionRequest request, CancellationToken ct)
    {
        if (await PreflightAsync(request, ct).ConfigureAwait(false) is { } refusal)
        {
            return refusal;
        }

        try
        {
            return await WriteAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return DecisionActionOutcome.Failed(Code, DecisionActionReasons.WriteFailed,
                "The write did not finish cleanly; it may have been applied.", possiblyWritten: PossiblyWritten(request));
        }
    }

    protected DecisionActionOutcome Refuse(string reason, string detail) => DecisionActionOutcome.Refused(Code, reason, detail);
}

// ---------------------------------------------------------------------------------------------------------------------
// Decide lane
// ---------------------------------------------------------------------------------------------------------------------

/// <summary>
/// <c>revise-budget</c> (FR-52; D-18, D-55). The caller must be able to WRITE the chosen <c>sprk_budget</c>. Then the
/// WRITER creates the <c>sprk_budgetrevision</c> (budget, matter, prior and new amount, reason, revised-by, revised-on) OWNED
/// by the team the ownership rule names for a child of that budget and matter (the named Secure team under a Secure matter,
/// else the matter's business-unit team), and the new amount is written onto <c>sprk_budget</c> AS THE SIGNED-IN USER (their
/// rights, their audit). The writer never writes <c>sprk_budget</c>.
/// </summary>
/// <remarks>
/// <para><b>Escalation (POML).</b> If the user cannot write the amount after the writer created the revision, the executor
/// STOPS: the outcome is <see cref="DecisionActionStatus.Failed"/> with <see cref="DecisionActionReasons.BudgetAmountNotWritten"/>
/// and the revision id in <see cref="DecisionActionOutcome.Written"/>. It does not retry and never writes the amount with
/// another identity.</para>
/// <para>The budget must belong to the Signal's matter: the user picks among that matter's budgets (D-18), and a revision
/// on a budget of another matter would close another matter's Path B Signal.</para>
/// <para><b>Owner (D-33/D-38).</b> Left to default, the writer would own the revision in the root business unit, outside a
/// Secure matter's wall. The owner is resolved through <see cref="IRecordOwnershipResolver"/> (secure-if-any over the budget and
/// the matter) in the preflight, so an unresolvable owner refuses before anything is written. The writer needs Assign on the
/// table to set it (<c>prvAssignsprk_BudgetRevision</c>).</para>
/// </remarks>
public sealed class ReviseBudgetExecutor : IDecisionActionExecutor
{
    private const string BudgetEntity = "sprk_budget";
    private const string RevisionEntity = "sprk_budgetrevision";

    private readonly CallerRecordAccessProbe _probe;
    private readonly IDataverseUserClient _dataverse;
    private readonly OntologyWriterDataverseClient _writer;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReviseBudgetExecutor> _logger;

    public ReviseBudgetExecutor(
        CallerRecordAccessProbe probe,
        IDataverseUserClient user,
        OntologyWriterDataverseClient writer,
        IRecordOwnershipResolver ownership,
        TimeProvider clock,
        ILogger<ReviseBudgetExecutor> logger)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _dataverse = user ?? throw new ArgumentNullException(nameof(user));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Code => "revise-budget";

    private sealed record Plan(Guid BudgetId, Guid MatterId, decimal Amount, string Reason, decimal? Prior, Guid RevisedBy, RecordOwnerResolution Owner);

    public async Task<DecisionActionOutcome?> PreflightAsync(DecisionActionRequest request, CancellationToken ct) =>
        (await PlanAsync(request, ct).ConfigureAwait(false)).Refusal;

    public async Task<DecisionActionOutcome> ExecuteAsync(DecisionActionRequest request, CancellationToken ct)
    {
        var (refusal, plan) = await PlanAsync(request, ct).ConfigureAwait(false);
        if (refusal is not null || plan is null)
            return refusal ?? DecisionActionOutcome.Refused(Code, DecisionActionReasons.InvalidParameter, "The revision could not be planned.");

        // The WRITER creates the revision. Nothing else it does touches sprk_budget.
        var now = _clock.GetUtcNow().UtcDateTime;
        var revision = new Entity(RevisionEntity)
        {
            ["sprk_name"] = $"Budget revision {now:yyyy-MM-dd}",
            ["sprk_budget"] = new EntityReference(BudgetEntity, plan.BudgetId),
            ["sprk_matter"] = new EntityReference("sprk_matter", plan.MatterId),
            ["sprk_newamount"] = new Money(plan.Amount),
            ["sprk_reason"] = plan.Reason,
            ["sprk_revisedby"] = new EntityReference("systemuser", plan.RevisedBy),
            ["sprk_revisedon"] = now,
        };
        if (plan.Prior is { } before)
        {
            revision["sprk_prioramount"] = new Money(before);
        }

        plan.Owner.ApplyTo(revision); // the resolver's own writer of ownerid: the owned team, never the writer's business unit

        // The id is chosen HERE, before the create is sent, so a create that times out after Dataverse committed it can still be
        // named (the SDK honours a supplied primary id).
        var revisionId = Guid.NewGuid();
        revision.Id = revisionId;
        var revisionRef = new DecisionRecordRef(RevisionEntity, revisionId);
        var budgetRef = new DecisionRecordRef(BudgetEntity, plan.BudgetId);
        try
        {
            await _writer.CreateAsync(revision, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[decision-action] revise-budget: the writer's create of revision {RevisionId} for budget {BudgetId} did not complete cleanly.", revisionId, plan.BudgetId);
            return IsClearRefusal(ex)
                ? DecisionActionOutcome.Failed(Code, DecisionActionReasons.WriteFailed, "The budget revision could not be recorded; the budget was not changed.")
                : DecisionActionOutcome.Failed(Code, DecisionActionReasons.WriteFailed,
                    $"The budget revision ({revisionId:D}) did not confirm; it may have been recorded. The budget was not changed.",
                    possiblyWritten: [revisionRef]);
        }

        // The new AMOUNT is the signed-in user's write (D-55). If it is refused, STOP and report the revision id.
        DataverseUserResponse patch;
        try
        {
            patch = await _dataverse.PatchAsync(
                $"{DecisionExec.BudgetSet}({plan.BudgetId:D})",
                JsonSerializer.Serialize(new Dictionary<string, object?> { ["sprk_totalbudget"] = plan.Amount }), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // An HttpClient timeout is a TaskCanceledException whose token was NOT cancelled: the revision exists, so its id must survive.
            _logger.LogError(ex, "[decision-action] revise-budget: revision {RevisionId} exists but the amount write for budget {BudgetId} did not answer.", revisionId, plan.BudgetId);
            return DecisionActionOutcome.Failed(Code, DecisionActionReasons.BudgetAmountNotWritten,
                $"The revision was recorded ({revisionId:D}) but the budget amount write did not answer; it may have been applied.",
                written: [revisionRef], possiblyWritten: [budgetRef]);
        }

        if (!patch.IsSuccess)
        {
            _logger.LogError(
                "[decision-action] revise-budget: revision {RevisionId} was created for budget {BudgetId} but the signed-in user could not write the amount (status {Status}). Stopping; no other identity is used.",
                revisionId, plan.BudgetId, patch.StatusCode);
            // 5xx, or 0 (no answer), after the request was sent: the amount may have landed. A 4xx refusal wrote nothing.
            var unanswered = patch.StatusCode is 0 or >= 500;
            return DecisionActionOutcome.Failed(Code, DecisionActionReasons.BudgetAmountNotWritten,
                unanswered
                    ? $"The revision was recorded ({revisionId:D}) but the budget amount write did not answer; it may have been applied."
                    : $"The revision was recorded ({revisionId:D}) but you could not update the budget amount.",
                written: [revisionRef], possiblyWritten: unanswered ? [budgetRef] : null);
        }

        return DecisionActionOutcome.Done(Code, revisionRef, budgetRef);
    }

    /// <summary>True when Dataverse ANSWERED with a refusal (a service fault, or a 4xx): nothing was written. A timeout, a transport
    /// failure or a 5xx is not an answer, so the row may exist.</summary>
    private static bool IsClearRefusal(Exception ex) =>
        ex is System.ServiceModel.FaultException<OrganizationServiceFault>
        || (ex is Microsoft.PowerPlatform.Dataverse.Client.Utils.DataverseOperationException { InnerException: Microsoft.Rest.HttpOperationException { Response.StatusCode: >= System.Net.HttpStatusCode.BadRequest and < System.Net.HttpStatusCode.InternalServerError } });

    /// <summary>Every refusal, no write: parameters, matter, Write on the budget, the budget as the caller on this matter, the
    /// caller's systemuserid, and the owner the revision will have.</summary>
    private async Task<(DecisionActionOutcome? Refusal, Plan? Plan)> PlanAsync(DecisionActionRequest request, CancellationToken ct)
    {
        DecisionActionOutcome? Refuse(string reason, string detail) => DecisionActionOutcome.Refused(Code, reason, detail);

        if (!DecisionExec.TryGuid(request.Param("budget"), out var budgetId))
            return (Refuse(DecisionActionReasons.InvalidParameter, "Pick the budget to revise."), null);
        if (!decimal.TryParse(request.Param("amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0)
            return (Refuse(DecisionActionReasons.InvalidParameter, "The new budget must be an amount of zero or more."), null);
        if (request.Param("reason") is not { } reason)
            return (Refuse(DecisionActionReasons.InvalidParameter, "Give a reason for the revision."), null);
        if (request.MatterId is not { } matterId || matterId == Guid.Empty)
            return (Refuse(DecisionActionReasons.BudgetNotOnMatter, "This item has no matter, so it has no budget to revise."), null);

        // The caller's own right on the chosen budget, before anything is read for the write.
        if (!await DecisionExec.CallerHoldsAsync(_probe, request.Http, DecisionExec.BudgetSet, budgetId, "write", ct).ConfigureAwait(false))
            return (Refuse(DecisionActionReasons.NotAuthorized, "You do not have permission to change this budget."), null);

        // The budget, as the caller: its matter and its current amount (the revision's prior amount).
        var budget = await _dataverse.GetAsync(
            $"{DecisionExec.BudgetSet}({budgetId:D})?$select=sprk_totalbudget,_sprk_matter_value", ct).ConfigureAwait(false);
        if (!budget.IsSuccess || budget.Body is not { ValueKind: JsonValueKind.Object } row)
            return (Refuse(DecisionActionReasons.NotFound, "The budget was not found."), null);

        if (!row.TryGetProperty("_sprk_matter_value", out var matterValue) || matterValue.ValueKind != JsonValueKind.String
            || !Guid.TryParse(matterValue.GetString(), out var budgetMatter) || budgetMatter != matterId)
            return (Refuse(DecisionActionReasons.BudgetNotOnMatter, "That budget does not belong to this matter."), null);

        decimal? prior = row.TryGetProperty("sprk_totalbudget", out var total) && total.ValueKind == JsonValueKind.Number && total.TryGetDecimal(out var p)
            ? p
            : null;

        var revisedBy = await _probe.GetCallerSystemUserIdAsync(TokenHelper.ExtractBearerTokenOrNull(request.Http), ct).ConfigureAwait(false);
        if (revisedBy is not { } userId)
            return (Refuse(DecisionActionReasons.CallerUnresolved, "You could not be identified, so the revision was not recorded."), null);

        // The revision's owner (D-33/D-38): secure-if-any over the budget and the matter, never the writer's own business unit.
        RecordOwnerResolution owner;
        try
        {
            owner = await _ownership.ResolveOwnerAsync(new RecordOwnershipContext
            {
                TargetEntityLogicalName = "sprk_matter",
                TargetRecordId = matterId,
                Parents = [new RecordOwnershipParent(BudgetEntity, budgetId)],
                CallerObjectId = DecisionExec.CallerObjectId(request.Http),
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "[decision-action] revise-budget: the revision's owner could not be resolved for budget {BudgetId}.", budgetId);
            return (Refuse(DecisionActionReasons.OwnerUnresolved, "The revision's owner could not be determined, so nothing was recorded."), null);
        }

        if (!owner.IsOwned)
            return (Refuse(DecisionActionReasons.OwnerUnresolved, owner.Reason ?? "The revision's owner could not be determined, so nothing was recorded."), null);

        return (null, new Plan(budgetId, matterId, amount, reason, prior, userId, owner));
    }
}

/// <summary>
/// <c>approve-variance</c> (D-19): record-only. The Decision Record (outcome Authorized) IS the approval; nothing else is
/// written. The class takes NO collaborators on purpose: it has nothing it could write with, and nothing it could refuse.
/// </summary>
public sealed class ApproveVarianceExecutor : IDecisionActionExecutor
{
    public string Code => "approve-variance";

    public Task<DecisionActionOutcome?> PreflightAsync(DecisionActionRequest request, CancellationToken ct) =>
        Task.FromResult<DecisionActionOutcome?>(null);

    public Task<DecisionActionOutcome> ExecuteAsync(DecisionActionRequest request, CancellationToken ct) =>
        Task.FromResult(DecisionActionOutcome.Done(Code));
}

// ---------------------------------------------------------------------------------------------------------------------
// Do lane
// ---------------------------------------------------------------------------------------------------------------------

/// <summary>The Do-lane executors that act on the Signal's subject: the same collaborators and the same subject checks.</summary>
public abstract class DoLaneExecutor : DecisionExecutorBase
{
    protected DoLaneExecutor(CallerRecordAccessProbe probe, IDataverseUserClient user, DecisionRouteCores cores)
    {
        Probe = probe ?? throw new ArgumentNullException(nameof(probe));
        Dataverse = user ?? throw new ArgumentNullException(nameof(user));
        Cores = cores ?? throw new ArgumentNullException(nameof(cores));
    }

    /// <summary>For an executor that writes through the caller-identity client alone and calls no shipped route core.</summary>
    protected DoLaneExecutor(CallerRecordAccessProbe probe, IDataverseUserClient user)
    {
        Probe = probe ?? throw new ArgumentNullException(nameof(probe));
        Dataverse = user ?? throw new ArgumentNullException(nameof(user));
        Cores = null!; // never read by such an executor
    }

    protected CallerRecordAccessProbe Probe { get; }

    protected IDataverseUserClient Dataverse { get; }

    protected DecisionRouteCores Cores { get; }

    /// <summary>The subject must be one of the allowed tables and the caller must hold Write on it; for an event the open-work
    /// gate is applied too. Null when all hold.</summary>
    protected async Task<DecisionActionOutcome?> CheckSubjectAsync(
        DecisionActionRequest request, string verb, bool gateOpenEvents, CancellationToken ct)
    {
        if (request.Subject is not { } subject || subject.Entity is not ("sprk_event" or "sprk_todo"))
            return Refuse(DecisionActionReasons.SubjectUnsupported, $"Only an event or a To Do can be {verb} here.");

        var set = subject.Entity == "sprk_event" ? DecisionExec.EventSet : DecisionExec.TodoSet;
        if (!await DecisionExec.CallerHoldsAsync(Probe, request.Http, set, subject.Id, "write", ct).ConfigureAwait(false))
            return Refuse(DecisionActionReasons.NotAuthorized, $"You do not have permission to change this {(subject.Entity == "sprk_event" ? "event" : "To Do")}.");

        return gateOpenEvents && subject.Entity == "sprk_event"
            ? await DecisionExec.OpenEventRefusalAsync(Code, Dataverse, subject.Id, ct).ConfigureAwait(false)
            : null;
    }

    /// <summary>The subject must be a work assignment the caller can Write. Null when both hold.</summary>
    protected async Task<DecisionActionOutcome?> CheckWorkAssignmentAsync(DecisionActionRequest request, string reason, CancellationToken ct)
    {
        if (request.Subject is not { Entity: "sprk_workassignment" } subject)
            return Refuse(DecisionActionReasons.SubjectUnsupported, reason);
        return await DecisionExec.CallerHoldsAsync(Probe, request.Http, DecisionExec.WorkAssignmentSet, subject.Id, "write", ct).ConfigureAwait(false)
            ? null
            : Refuse(DecisionActionReasons.NotAuthorized, "You do not have permission to change this work assignment.");
    }
}

/// <summary>
/// <c>mark-complete</c>: an event through the events complete core; a To Do through the child-records update core
/// (<c>statecode</c> 1, <c>statuscode</c> Completed, <c>sprk_completedon</c>, the same write the To Do detail makes).
/// </summary>
public sealed class MarkCompleteExecutor : DoLaneExecutor
{
    private readonly TimeProvider _clock;

    public MarkCompleteExecutor(CallerRecordAccessProbe probe, IDataverseUserClient user, DecisionRouteCores cores, TimeProvider clock)
        : base(probe, user, cores) =>
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public override string Code => "mark-complete";

    public override Task<DecisionActionOutcome?> PreflightAsync(DecisionActionRequest request, CancellationToken ct) =>
        CheckSubjectAsync(request, "completed", gateOpenEvents: true, ct);

    protected override async Task<DecisionActionOutcome> WriteAsync(DecisionActionRequest request, CancellationToken ct)
    {
        var subject = request.Subject!;
        if (subject.Entity == "sprk_event")
            return DecisionExec.FromReply(Code, await Cores.CompleteEventAsync(request.Http, subject.Id, ct).ConfigureAwait(false), subject);

        var body = DecisionExec.Json(new Dictionary<string, object?>
        {
            ["statecode"] = 1,
            ["statuscode"] = 2,
            ["sprk_completedon"] = _clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        });
        return DecisionExec.FromReply(Code, await Cores.UpdateChildAsync(request.Http, "sprk_todo", subject.Id, body, ct).ConfigureAwait(false), subject);
    }
}

/// <summary>
/// <c>reschedule</c> (D-27): writes <c>sprk_duedate</c> — never <c>sprk_finalduedate</c>. An event through the narrow events
/// write; a To Do through the child-records update core.
/// </summary>
public sealed class RescheduleExecutor : DoLaneExecutor
{
    public RescheduleExecutor(CallerRecordAccessProbe probe, IDataverseUserClient user, DecisionRouteCores cores)
        : base(probe, user, cores)
    {
    }

    public override string Code => "reschedule";

    public override async Task<DecisionActionOutcome?> PreflightAsync(DecisionActionRequest request, CancellationToken ct)
    {
        if (!DecisionExec.TryDate(request.Param("dueDate"), out _))
            return Refuse(DecisionActionReasons.InvalidParameter, "Give the new due date as yyyy-MM-dd.");
        return await CheckSubjectAsync(request, "rescheduled", gateOpenEvents: true, ct).ConfigureAwait(false);
    }

    protected override async Task<DecisionActionOutcome> WriteAsync(DecisionActionRequest request, CancellationToken ct)
    {
        DecisionExec.TryDate(request.Param("dueDate"), out var due);
        var subject = request.Subject!;
        if (subject.Entity == "sprk_event")
            return DecisionExec.FromEventWrite(Code,
                await Cores.WriteEventDueAssigneeAsync(request.Http, subject.Id, new UpdateEventDueAssigneeRequest(due, null), ct).ConfigureAwait(false),
                subject);

        var body = DecisionExec.Json(new Dictionary<string, object?> { ["sprk_duedate"] = DecisionExec.IsoDate(due) });
        return DecisionExec.FromReply(Code, await Cores.UpdateChildAsync(request.Http, "sprk_todo", subject.Id, body, ct).ConfigureAwait(false), subject);
    }
}

/// <summary>
/// <c>reassign</c>: the assignee is a CONTACT (<c>sprk_assignedto</c>), and the caller needs AppendTo on it. An event also
/// becomes <c>Reassigned</c> with <c>sprk_reassignedby</c> (the narrow events write); a To Do has no such status, so only the
/// lookup changes.
/// </summary>
public sealed class ReassignExecutor : DoLaneExecutor
{
    public ReassignExecutor(CallerRecordAccessProbe probe, IDataverseUserClient user, DecisionRouteCores cores)
        : base(probe, user, cores)
    {
    }

    public override string Code => "reassign";

    public override async Task<DecisionActionOutcome?> PreflightAsync(DecisionActionRequest request, CancellationToken ct)
    {
        if (!DecisionExec.TryGuid(request.Param("assignee"), out var contactId))
            return Refuse(DecisionActionReasons.InvalidParameter, "Pick the person to reassign to.");
        if (await CheckSubjectAsync(request, "reassigned", gateOpenEvents: true, ct).ConfigureAwait(false) is { } refusal)
            return refusal;
        return await DecisionExec.CallerHoldsAsync(Probe, request.Http, DecisionExec.ContactSet, contactId, DecisionExec.AppendToOperation, ct).ConfigureAwait(false)
            ? null
            : Refuse(DecisionActionReasons.NotAuthorized, "You do not have permission to assign work to that person.");
    }

    protected override async Task<DecisionActionOutcome> WriteAsync(DecisionActionRequest request, CancellationToken ct)
    {
        DecisionExec.TryGuid(request.Param("assignee"), out var contactId);
        var subject = request.Subject!;
        if (subject.Entity == "sprk_event")
            return DecisionExec.FromEventWrite(Code,
                await Cores.WriteEventDueAssigneeAsync(request.Http, subject.Id, new UpdateEventDueAssigneeRequest(null, contactId), ct).ConfigureAwait(false),
                subject);

        var body = DecisionExec.Json(new Dictionary<string, object?> { ["sprk_assignedto@odata.bind"] = $"/contacts({contactId:D})" });
        return DecisionExec.FromReply(Code, await Cores.UpdateChildAsync(request.Http, "sprk_todo", subject.Id, body, ct).ConfigureAwait(false), subject);
    }
}

/// <summary>
/// <c>extend-response-date</c> (D-54): writes <c>sprk_workassignment.sprk_responseduedate</c> as the caller. Work assignments
/// are not in the child-records update list, so this is the caller's own PATCH after their Write check.
/// </summary>
public sealed class ExtendResponseDateExecutor : DoLaneExecutor
{
    public ExtendResponseDateExecutor(CallerRecordAccessProbe probe, IDataverseUserClient user)
        : base(probe, user)
    {
    }

    public override string Code => "extend-response-date";

    public override async Task<DecisionActionOutcome?> PreflightAsync(DecisionActionRequest request, CancellationToken ct)
    {
        if (!DecisionExec.TryDate(request.Param("responseDate"), out _))
            return Refuse(DecisionActionReasons.InvalidParameter, "Give the new response date as yyyy-MM-dd.");
        return await CheckWorkAssignmentAsync(request, "Only a work assignment has a response date.", ct).ConfigureAwait(false);
    }

    protected override async Task<DecisionActionOutcome> WriteAsync(DecisionActionRequest request, CancellationToken ct)
    {
        DecisionExec.TryDate(request.Param("responseDate"), out var date);
        var subject = request.Subject!;
        var patch = await Dataverse.PatchAsync(
            $"{DecisionExec.WorkAssignmentSet}({subject.Id:D})",
            JsonSerializer.Serialize(new Dictionary<string, object?> { ["sprk_responseduedate"] = DecisionExec.IsoDate(date) }),
            ct).ConfigureAwait(false);
        return DecisionExec.FromPatch(Code, patch, subject, "response date");
    }
}

/// <summary>
/// <c>record-the-response</c> (D-54, D-58): writes <c>sprk_respondedon</c> (today in the caller's time zone) and
/// <c>sprk_responseoutcome</c> on the work assignment as the caller. There is no note column; notes go in the Decision Record.
/// </summary>
public sealed class RecordTheResponseExecutor : DoLaneExecutor
{
    // D-58: the live values of sprk_workassignment.sprk_responseoutcome (task 047), keyed by the catalog's option codes.
    internal static readonly IReadOnlyDictionary<string, int> OutcomeValues = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["received-outside-spaarke"] = 100000000,
        ["delivered-on-the-matter"] = 100000001,
        ["no-longer-needed"] = 100000002,
    };

    public RecordTheResponseExecutor(CallerRecordAccessProbe probe, IDataverseUserClient user, DecisionRouteCores cores)
        : base(probe, user, cores)
    {
    }

    public override string Code => "record-the-response";

    public override async Task<DecisionActionOutcome?> PreflightAsync(DecisionActionRequest request, CancellationToken ct)
    {
        if (request.Param("response") is not { } response || !OutcomeValues.ContainsKey(response))
            return Refuse(DecisionActionReasons.InvalidParameter, "Pick how the response was received.");
        return await CheckWorkAssignmentAsync(request, "Only a work assignment records a response.", ct).ConfigureAwait(false);
    }

    protected override async Task<DecisionActionOutcome> WriteAsync(DecisionActionRequest request, CancellationToken ct)
    {
        var outcome = OutcomeValues[request.Param("response")!];
        var subject = request.Subject!;
        var today = await Cores.TodayForCallerAsync(request.Http, ct).ConfigureAwait(false);
        var patch = await Dataverse.PatchAsync(
            $"{DecisionExec.WorkAssignmentSet}({subject.Id:D})",
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["sprk_respondedon"] = DecisionExec.IsoDate(today),
                ["sprk_responseoutcome"] = outcome,
            }),
            ct).ConfigureAwait(false);
        return DecisionExec.FromPatch(Code, patch, subject, "response");
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// Messages: send-reminder (Do lane) and send-email (Next step) share one send path
// ---------------------------------------------------------------------------------------------------------------------

/// <summary>
/// The recipient and send logic both message actions share (#30). Recipients are email addresses, or ids of a contact or of
/// a law firm (<c>sprk_organization</c>). A law firm is resolved to its primary contact with an email address; a recipient that
/// cannot be resolved to an address REFUSES the whole send (nothing is sent to the others). Every read runs as the caller.
/// The preflight is the whole preparation (parameters, every recipient, AppendTo on the core record); only the send is left.
/// </summary>
public abstract class MessageExecutorBase : IDecisionActionExecutor
{
    private static readonly Regex Address = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly CallerRecordAccessProbe _probe;
    private readonly IDataverseUserClient _dataverse;
    private readonly DecisionRouteCores _cores;

    protected MessageExecutorBase(CallerRecordAccessProbe probe, IDataverseUserClient user, DecisionRouteCores cores)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _dataverse = user ?? throw new ArgumentNullException(nameof(user));
        _cores = cores ?? throw new ArgumentNullException(nameof(cores));
    }

    public abstract string Code { get; }

    /// <summary>True for the reminder, which may fall back to the subject's law-firm assignee when no recipient is typed.</summary>
    protected virtual bool FallsBackToLawFirm => false;

    public async Task<DecisionActionOutcome?> PreflightAsync(DecisionActionRequest request, CancellationToken ct) =>
        (await PrepareAsync(request, ct).ConfigureAwait(false)).Refusal;

    public async Task<DecisionActionOutcome> ExecuteAsync(DecisionActionRequest request, CancellationToken ct)
    {
        var (refusal, send) = await PrepareAsync(request, ct).ConfigureAwait(false);
        if (refusal is not null || send is null)
            return refusal ?? DecisionActionOutcome.Refused(Code, DecisionActionReasons.InvalidParameter, "The message could not be prepared.");

        try
        {
            var (reply, communicationId) = await _cores.SendCommunicationAsync(request.Http, send, ct).ConfigureAwait(false);
            if (!reply.IsSuccess)
                return DecisionExec.FromReply(Code, reply);

            return communicationId is { } sent
                ? DecisionActionOutcome.Done(Code, new DecisionRecordRef("sprk_communication", sent))
                : DecisionActionOutcome.Done(Code);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A send that did not answer may have gone out; there is no id to name.
            return DecisionActionOutcome.Failed(Code, DecisionActionReasons.WriteFailed, "The message did not confirm; it may have been sent.");
        }
    }

    private async Task<(DecisionActionOutcome? Refusal, SendCommunicationRequest? Send)> PrepareAsync(DecisionActionRequest request, CancellationToken ct)
    {
        (DecisionActionOutcome?, SendCommunicationRequest?) Refuse(string reason, string detail) =>
            (DecisionActionOutcome.Refused(Code, reason, detail), null);

        if (request.Param("subject") is not { } subjectLine)
            return Refuse(DecisionActionReasons.InvalidParameter, "Give the message a subject.");
        if (request.Param("body") is not { } body)
            return Refuse(DecisionActionReasons.InvalidParameter, "Write the message.");

        var tokens = (request.Param("to") ?? string.Empty)
            .Split([';', ',', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tokens.Count == 0 && FallsBackToLawFirm && request.Subject is { } item)
        {
            var firm = await ReadLawFirmAsync(item, ct).ConfigureAwait(false);
            if (firm is { } firmId)
                tokens.Add(firmId.ToString("D"));
        }

        if (tokens.Count == 0)
            return Refuse(DecisionActionReasons.RecipientUnresolved, "There is no recipient to send to.");

        var addresses = new List<string>();
        foreach (var token in tokens)
        {
            if (Address.IsMatch(token))
            {
                addresses.Add(token);
                continue;
            }

            var resolved = DecisionExec.TryGuid(token, out var id) ? await ResolveAddressAsync(id, ct).ConfigureAwait(false) : null;
            if (resolved is null)
                return Refuse(DecisionActionReasons.RecipientUnresolved,
                    "A recipient could not be resolved to a contact with an email address, so nothing was sent.");
            addresses.Add(resolved);
        }

        // The message is filed against the core record when there is one: the caller needs AppendTo on it (the same right the
        // send route asks of every association). A caller without it gets no send at all.
        CommunicationAssociation[]? associations = null;
        if (request.Core is { } core)
        {
            if (!await DecisionExec.CallerCanAppendToAsync(_probe, request.Http, core.Entity, core.Id, ct).ConfigureAwait(false))
                return Refuse(DecisionActionReasons.NotAuthorized, "You do not have permission to file a message against this record.");
            associations = [new CommunicationAssociation { EntityType = core.Entity, EntityId = core.Id, EntityName = core.Name }];
        }

        return (null, new SendCommunicationRequest
        {
            To = addresses.ToArray(),
            Subject = subjectLine,
            Body = body,
            BodyFormat = BodyFormat.PlainText,
            Associations = associations,
        });
    }

    /// <summary>The subject's <c>sprk_assignedlawfirm1</c> (events and work assignments carry it), read as the caller.</summary>
    private async Task<Guid?> ReadLawFirmAsync(DecisionRecordRef item, CancellationToken ct)
    {
        var set = item.Entity switch
        {
            "sprk_workassignment" => DecisionExec.WorkAssignmentSet,
            "sprk_event" => DecisionExec.EventSet,
            _ => null,
        };
        if (set is null) return null;

        var row = await _dataverse.GetAsync($"{set}({item.Id:D})?$select=_sprk_assignedlawfirm1_value", ct).ConfigureAwait(false);
        return row.IsSuccess && ReadGuid(row.Body, "_sprk_assignedlawfirm1_value") is { } firm ? firm : null;
    }

    /// <summary>An email address for a contact id, or for a law firm id (its primary contacts, in order); null when neither has one.</summary>
    private async Task<string?> ResolveAddressAsync(Guid id, CancellationToken ct)
    {
        if (await ContactAddressAsync(id, ct).ConfigureAwait(false) is { } direct)
            return direct;

        var firm = await _dataverse.GetAsync(
            $"sprk_organizations({id:D})?$select=_sprk_primarycontact1_value,_sprk_primarycontact2_value", ct).ConfigureAwait(false);
        if (!firm.IsSuccess)
            return null;

        foreach (var column in new[] { "_sprk_primarycontact1_value", "_sprk_primarycontact2_value" })
        {
            if (ReadGuid(firm.Body, column) is { } contact && await ContactAddressAsync(contact, ct).ConfigureAwait(false) is { } address)
                return address;
        }

        return null;
    }

    private async Task<string?> ContactAddressAsync(Guid contactId, CancellationToken ct)
    {
        var contact = await _dataverse.GetAsync($"contacts({contactId:D})?$select=emailaddress1", ct).ConfigureAwait(false);
        if (contact.IsSuccess && contact.Body is { ValueKind: JsonValueKind.Object } row
            && row.TryGetProperty("emailaddress1", out var email) && email.ValueKind == JsonValueKind.String
            && email.GetString() is { } text && Address.IsMatch(text.Trim()))
            return text.Trim();
        return null;
    }

    private static Guid? ReadGuid(JsonElement? body, string property) =>
        body is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String
        && Guid.TryParse(v.GetString(), out var id) && id != Guid.Empty
            ? id
            : null;
}

/// <summary><c>send-reminder</c> (Do lane; FR-52): the communications send core, to the typed recipient or, when none is
/// typed, to the subject's law-firm assignee resolved to a contact with an email address (#30).</summary>
public sealed class SendReminderExecutor : MessageExecutorBase
{
    public SendReminderExecutor(CallerRecordAccessProbe probe, IDataverseUserClient user, DecisionRouteCores cores)
        : base(probe, user, cores)
    {
    }

    public override string Code => "send-reminder";

    protected override bool FallsBackToLawFirm => true;
}

/// <summary><c>send-email</c> (Next step; #23): the same send core, run inside the commit; returns the communication id for
/// <c>sprk_followons</c>.</summary>
public sealed class SendEmailExecutor : MessageExecutorBase
{
    public SendEmailExecutor(CallerRecordAccessProbe probe, IDataverseUserClient user, DecisionRouteCores cores)
        : base(probe, user, cores)
    {
    }

    public override string Code => "send-email";
}

// ---------------------------------------------------------------------------------------------------------------------
// Next-step creators
// ---------------------------------------------------------------------------------------------------------------------

/// <summary>
/// The child-records create the two record-creating Next steps share. The preflight asks, AS THE CALLER, what the core would
/// ask first: Create on the table, AppendTo on the core record the row is filed under and on the assignee contact. The core then
/// repeats its own check before the application creates the row. The executor builds the payload the browser would have sent
/// (the typed regarding lookup for the core record, and the ADR-024 id/name/type fields) and returns the created id.
/// </summary>
public abstract class ChildCreateExecutorBase : DecisionExecutorBase
{
    private readonly CallerRecordAccessProbe _probe;
    private readonly DecisionRouteCores _cores;
    private readonly IGenericEntityService _entities;

    protected ChildCreateExecutorBase(CallerRecordAccessProbe probe, DecisionRouteCores cores, IGenericEntityService entities)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _cores = cores ?? throw new ArgumentNullException(nameof(cores));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
    }

    protected abstract string Table { get; }

    /// <summary>Create on the table, by its live privilege name.</summary>
    protected abstract string CreatePrivilege { get; }

    /// <summary>The contact the row is for, when the action names one (the caller needs AppendTo on it).</summary>
    protected virtual Guid? AssigneeContact(DecisionActionRequest request) => null;

    /// <summary>The table-specific columns, or a refusal when a parameter is missing or malformed.</summary>
    protected abstract DecisionActionOutcome? BuildColumns(DecisionActionRequest request, IDictionary<string, object?> payload);

    protected override IReadOnlyList<DecisionRecordRef> PossiblyWritten(DecisionActionRequest request) => [];

    public override async Task<DecisionActionOutcome?> PreflightAsync(DecisionActionRequest request, CancellationToken ct)
    {
        if (BuildColumns(request, new Dictionary<string, object?>()) is { } invalid)
            return invalid;

        if (!await DecisionExec.CallerHoldsPrivilegeAsync(_probe, request.Http, CreatePrivilege, ct).ConfigureAwait(false))
            return Refuse(DecisionActionReasons.NotAuthorized, "You do not have permission to create this.");

        if (request.Core is { } core
            && !await DecisionExec.CallerCanAppendToAsync(_probe, request.Http, core.Entity, core.Id, ct).ConfigureAwait(false))
            return Refuse(DecisionActionReasons.NotAuthorized, "You do not have permission to file this under that record.");

        if (AssigneeContact(request) is { } contact
            && !await DecisionExec.CallerHoldsAsync(_probe, request.Http, DecisionExec.ContactSet, contact, DecisionExec.AppendToOperation, ct).ConfigureAwait(false))
            return Refuse(DecisionActionReasons.NotAuthorized, "You do not have permission to assign this to that person.");

        return null;
    }

    protected override async Task<DecisionActionOutcome> WriteAsync(DecisionActionRequest request, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (BuildColumns(request, payload) is { } refusal)
            return refusal;

        if (request.Core is { } core)
        {
            // The typed lookup is named sprk_regarding{type}, for every core type, on To Dos and events alike.
            var suffix = core.Entity.StartsWith("sprk_", StringComparison.Ordinal) ? core.Entity["sprk_".Length..] : core.Entity;
            string set;
            try
            {
                set = await _entities.GetEntitySetNameAsync(core.Entity, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return DecisionActionOutcome.Failed(Code, DecisionActionReasons.WriteFailed, "The record this is filed under could not be resolved; nothing was created.");
            }

            payload[$"sprk_regarding{suffix}@odata.bind"] = $"/{set}({core.Id:D})";
            payload["sprk_regardingrecordid"] = core.Id.ToString("D");
            if (core.Name is { } name) payload["sprk_regardingrecordname"] = name;
            if (core.RecordTypeRefId is { } typeRef) payload["sprk_regardingrecordtype@odata.bind"] = $"/sprk_recordtype_refs({typeRef:D})";
        }

        var reply = await _cores.CreateChildAsync(request.Http, Table, DecisionExec.Json(payload), ct).ConfigureAwait(false);
        if (!reply.IsSuccess)
            return DecisionExec.FromReply(Code, reply);

        return reply.CreatedId is { } created
            ? DecisionActionOutcome.Done(Code, new DecisionRecordRef(Table, created))
            : DecisionActionOutcome.Failed(Code, DecisionActionReasons.WriteFailed, "The record was created but its id was not returned.");
    }
}

/// <summary><c>add-todo</c> (Next step): a <c>sprk_todo</c> through the child-records create core.</summary>
public sealed class AddTodoExecutor : ChildCreateExecutorBase
{
    public AddTodoExecutor(CallerRecordAccessProbe probe, DecisionRouteCores cores, IGenericEntityService entities)
        : base(probe, cores, entities)
    {
    }

    public override string Code => "add-todo";

    protected override string Table => "sprk_todo";

    protected override string CreatePrivilege => DecisionExec.CreateTodoPrivilege;

    protected override Guid? AssigneeContact(DecisionActionRequest request) =>
        DecisionExec.TryGuid(request.Param("assignee"), out var id) ? id : null;

    protected override DecisionActionOutcome? BuildColumns(DecisionActionRequest request, IDictionary<string, object?> payload)
    {
        if (request.Param("title") is not { } title)
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.InvalidParameter, "Name the To Do.");
        if (!DecisionExec.TryGuid(request.Param("assignee"), out var assignee))
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.InvalidParameter, "Pick who the To Do is for.");
        if (!DecisionExec.TryDate(request.Param("dueDate"), out var due))
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.InvalidParameter, "Give the due date as yyyy-MM-dd.");

        payload["sprk_name"] = title;
        payload["sprk_duedate"] = DecisionExec.IsoDate(due);
        payload["sprk_assignedto@odata.bind"] = $"/contacts({assignee:D})";
        return null;
    }
}

/// <summary><c>create-event</c> (Next step): a <c>sprk_event</c> through the child-records create core. The date is the due
/// date (<c>sprk_duedate</c>, D-27); attendees, which the table has no column for, go in the description.</summary>
public sealed class CreateEventExecutor : ChildCreateExecutorBase
{
    public CreateEventExecutor(CallerRecordAccessProbe probe, DecisionRouteCores cores, IGenericEntityService entities)
        : base(probe, cores, entities)
    {
    }

    public override string Code => "create-event";

    protected override string Table => "sprk_event";

    protected override string CreatePrivilege => CommunicationRecordAuthorizationFilter.CreateEventPrivilege;

    protected override DecisionActionOutcome? BuildColumns(DecisionActionRequest request, IDictionary<string, object?> payload)
    {
        if (request.Param("title") is not { } title)
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.InvalidParameter, "Name the event.");
        if (!DecisionExec.TryDate(request.Param("date"), out var date))
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.InvalidParameter, "Give the date as yyyy-MM-dd.");

        payload["sprk_eventname"] = title;
        payload["sprk_duedate"] = DecisionExec.IsoDate(date);
        payload["statuscode"] = EventStatusCode.Open;
        if (request.Param("attendees") is { } attendees)
            payload["sprk_description"] = $"Attendees: {attendees}";
        return null;
    }
}
