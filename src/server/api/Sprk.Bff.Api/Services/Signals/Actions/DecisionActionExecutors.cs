using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Communication.Engine;

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

    /// <summary>A write was attempted and did not complete cleanly; <see cref="DecisionActionOutcome.Written"/> lists any
    /// record that DID land (the budget revision when the amount was refused). The commit route reports it; it never retries
    /// with another identity.</summary>
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
    string? Detail = null)
{
    public static DecisionActionOutcome Done(string code, params DecisionRecordRef[] written) =>
        new(code, DecisionActionStatus.Done, written);

    public static DecisionActionOutcome Refused(string code, string reasonCode, string detail) =>
        new(code, DecisionActionStatus.Refused, [], reasonCode, detail);

    public static DecisionActionOutcome Failed(string code, string reasonCode, string detail, params DecisionRecordRef[] written) =>
        new(code, DecisionActionStatus.Failed, written, reasonCode, detail);
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

    /// <summary>The revision was written by the writer but the signed-in user could not write the budget amount (D-55).</summary>
    public const string BudgetAmountNotWritten = "decision.action.budget_amount_not_written";
}

/// <summary>One decision action's executor. Its <see cref="Code"/> is a catalog code.</summary>
public interface IDecisionActionExecutor
{
    string Code { get; }

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

    internal static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    internal static bool TryGuid(string? value, out Guid id) => Guid.TryParse(value, out id) && id != Guid.Empty;

    internal static string IsoDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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

    internal static JsonElement Json(IDictionary<string, object?> body) =>
        JsonSerializer.SerializeToElement(body);

    /// <summary>Maps a shipped core's answer to an outcome. Nothing was written unless the reply is a success or an
    /// unclassified failure.</summary>
    internal static DecisionActionOutcome FromReply(string code, RouteReply reply, params DecisionRecordRef[] written)
    {
        if (reply.IsSuccess)
        {
            return DecisionActionOutcome.Done(code, written);
        }

        var detail = reply.Detail ?? $"The record could not be updated (status {reply.Status}).";
        return reply.Status switch
        {
            400 or 422 => DecisionActionOutcome.Refused(code, reply.ReasonCode ?? DecisionActionReasons.InvalidState, detail),
            401 or 403 => DecisionActionOutcome.Refused(code, DecisionActionReasons.NotAuthorized, detail),
            404 => DecisionActionOutcome.Refused(code, DecisionActionReasons.NotFound, detail),
            409 => DecisionActionOutcome.Refused(code, DecisionActionReasons.Conflict, detail),
            _ => DecisionActionOutcome.Failed(code, DecisionActionReasons.WriteFailed, detail),
        };
    }

    internal static DecisionActionOutcome FromEventWrite(string code, EventDueAssigneeResult result, DecisionRecordRef subject) =>
        result.Outcome switch
        {
            EventDueAssigneeOutcome.Written => DecisionActionOutcome.Done(code, subject),
            EventDueAssigneeOutcome.NotFound => DecisionActionOutcome.Refused(code, DecisionActionReasons.NotFound, "The event was not found."),
            EventDueAssigneeOutcome.InvalidState => DecisionActionOutcome.Refused(code, DecisionActionReasons.InvalidState, result.Detail ?? "The event is not open work."),
            EventDueAssigneeOutcome.Denied => DecisionActionOutcome.Refused(code, DecisionActionReasons.NotAuthorized, "You do not have the rights this change needs on the event."),
            _ => DecisionActionOutcome.Failed(code, DecisionActionReasons.WriteFailed, "The event could not be updated."),
        };
}

// ---------------------------------------------------------------------------------------------------------------------
// Decide lane
// ---------------------------------------------------------------------------------------------------------------------

/// <summary>
/// <c>revise-budget</c> (FR-52; D-18, D-55). The caller must be able to WRITE the chosen <c>sprk_budget</c>. Then the
/// WRITER creates the <c>sprk_budgetrevision</c> (budget, matter, prior and new amount, reason, revised-by, revised-on),
/// and the new amount is written onto <c>sprk_budget</c> AS THE SIGNED-IN USER (their rights, their audit). The writer never
/// writes <c>sprk_budget</c>.
/// </summary>
/// <remarks>
/// <para><b>Escalation (POML).</b> If the user cannot write the amount after the writer created the revision, the executor
/// STOPS: the outcome is <see cref="DecisionActionStatus.Failed"/> with <see cref="DecisionActionReasons.BudgetAmountNotWritten"/>
/// and the revision id in <see cref="DecisionActionOutcome.Written"/>. It does not retry and never writes the amount with
/// another identity.</para>
/// <para>The budget must belong to the Signal's matter: the user picks among that matter's budgets (D-18), and a revision
/// on a budget of another matter would close another matter's Path B Signal.</para>
/// </remarks>
public sealed class ReviseBudgetExecutor : IDecisionActionExecutor
{
    private const string BudgetEntity = "sprk_budget";
    private const string RevisionEntity = "sprk_budgetrevision";

    private readonly CallerRecordAccessProbe _probe;
    private readonly IDataverseUserClient _user;
    private readonly OntologyWriterDataverseClient _writer;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReviseBudgetExecutor> _logger;

    public ReviseBudgetExecutor(
        CallerRecordAccessProbe probe,
        IDataverseUserClient user,
        OntologyWriterDataverseClient writer,
        TimeProvider clock,
        ILogger<ReviseBudgetExecutor> logger)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Code => "revise-budget";

    public async Task<DecisionActionOutcome> ExecuteAsync(DecisionActionRequest request, CancellationToken ct)
    {
        if (!DecisionExec.TryGuid(request.Param("budget"), out var budgetId))
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.InvalidParameter, "Pick the budget to revise.");
        if (!decimal.TryParse(request.Param("amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount < 0)
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.InvalidParameter, "The new budget must be an amount of zero or more.");
        if (request.Param("reason") is not { } reason)
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.InvalidParameter, "Give a reason for the revision.");
        if (request.MatterId is not { } matterId || matterId == Guid.Empty)
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.BudgetNotOnMatter, "This item has no matter, so it has no budget to revise.");

        // 1. The caller's own right on the chosen budget, before anything is read for the write or written.
        if (!await DecisionExec.CallerHoldsAsync(_probe, request.Http, DecisionExec.BudgetSet, budgetId, "write", ct).ConfigureAwait(false))
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.NotAuthorized, "You do not have permission to change this budget.");

        // 2. The budget, as the caller: its matter and its current amount (the revision's prior amount).
        var budget = await _user.GetAsync(
            $"{DecisionExec.BudgetSet}({budgetId:D})?$select=sprk_totalbudget,_sprk_matter_value", ct).ConfigureAwait(false);
        if (!budget.IsSuccess || budget.Body is not { ValueKind: JsonValueKind.Object } row)
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.NotFound, "The budget was not found.");

        if (!row.TryGetProperty("_sprk_matter_value", out var matterValue) || matterValue.ValueKind != JsonValueKind.String
            || !Guid.TryParse(matterValue.GetString(), out var budgetMatter) || budgetMatter != matterId)
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.BudgetNotOnMatter, "That budget does not belong to this matter.");

        decimal? prior = row.TryGetProperty("sprk_totalbudget", out var total) && total.ValueKind == JsonValueKind.Number && total.TryGetDecimal(out var p)
            ? p
            : null;

        var revisedBy = await _probe.GetCallerSystemUserIdAsync(TokenHelper.ExtractBearerTokenOrNull(request.Http), ct).ConfigureAwait(false);
        if (revisedBy is not { } userId)
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.CallerUnresolved, "You could not be identified, so the revision was not recorded.");

        // 3. The WRITER creates the revision. Nothing else it does touches sprk_budget.
        var now = _clock.GetUtcNow().UtcDateTime;
        var revision = new Entity(RevisionEntity)
        {
            ["sprk_name"] = $"Budget revision {now:yyyy-MM-dd}",
            ["sprk_budget"] = new EntityReference(BudgetEntity, budgetId),
            ["sprk_matter"] = new EntityReference("sprk_matter", matterId),
            ["sprk_newamount"] = new Money(amount),
            ["sprk_reason"] = reason,
            ["sprk_revisedby"] = new EntityReference("systemuser", userId),
            ["sprk_revisedon"] = now,
        };
        if (prior is { } before)
        {
            revision["sprk_prioramount"] = new Money(before);
        }

        Guid revisionId;
        try
        {
            revisionId = await _writer.CreateAsync(revision, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[decision-action] revise-budget: the writer could not create the revision for budget {BudgetId}.", budgetId);
            return DecisionActionOutcome.Failed(Code, DecisionActionReasons.WriteFailed, "The budget revision could not be recorded; the budget was not changed.");
        }

        var revisionRef = new DecisionRecordRef(RevisionEntity, revisionId);

        // 4. The new AMOUNT is the signed-in user's write (D-55). If it is refused, STOP and report the revision id.
        var amountBody = JsonSerializer.Serialize(new Dictionary<string, object?> { ["sprk_totalbudget"] = amount });
        var patch = await _user.PatchAsync($"{DecisionExec.BudgetSet}({budgetId:D})", amountBody, ct).ConfigureAwait(false);
        if (!patch.IsSuccess)
        {
            _logger.LogError(
                "[decision-action] revise-budget: revision {RevisionId} was created for budget {BudgetId} but the signed-in user could not write the amount (status {Status}). Stopping; no other identity is used.",
                revisionId, budgetId, patch.StatusCode);
            return DecisionActionOutcome.Failed(Code, DecisionActionReasons.BudgetAmountNotWritten,
                $"The revision was recorded ({revisionId:D}) but you could not update the budget amount.", revisionRef);
        }

        return DecisionActionOutcome.Done(Code, revisionRef, new DecisionRecordRef(BudgetEntity, budgetId));
    }
}

/// <summary>
/// <c>approve-variance</c> (D-19): record-only. The Decision Record (outcome Authorized) IS the approval; nothing else is
/// written. The class takes NO collaborators on purpose: it has nothing it could write with.
/// </summary>
public sealed class ApproveVarianceExecutor : IDecisionActionExecutor
{
    public string Code => "approve-variance";

    public Task<DecisionActionOutcome> ExecuteAsync(DecisionActionRequest request, CancellationToken ct) =>
        Task.FromResult(DecisionActionOutcome.Done(Code));
}

// ---------------------------------------------------------------------------------------------------------------------
// Do lane
// ---------------------------------------------------------------------------------------------------------------------

/// <summary>
/// <c>mark-complete</c>: an event through the events complete core; a To Do through the child-records update core
/// (<c>statecode</c> 1, <c>statuscode</c> Completed, <c>sprk_completedon</c>, the same write the To Do detail makes).
/// </summary>
public sealed class MarkCompleteExecutor : IDecisionActionExecutor
{
    private readonly CallerRecordAccessProbe _probe;
    private readonly DecisionRouteCores _cores;
    private readonly TimeProvider _clock;

    public MarkCompleteExecutor(CallerRecordAccessProbe probe, DecisionRouteCores cores, TimeProvider clock)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _cores = cores ?? throw new ArgumentNullException(nameof(cores));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public string Code => "mark-complete";

    public async Task<DecisionActionOutcome> ExecuteAsync(DecisionActionRequest request, CancellationToken ct)
    {
        if (request.Subject is not { } subject)
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.SubjectUnsupported, "This item cannot be completed here.");

        switch (subject.Entity)
        {
            case "sprk_event":
                if (!await DecisionExec.CallerHoldsAsync(_probe, request.Http, DecisionExec.EventSet, subject.Id, "write", ct).ConfigureAwait(false))
                    return DecisionActionOutcome.Refused(Code, DecisionActionReasons.NotAuthorized, "You do not have permission to complete this event.");
                return DecisionExec.FromReply(Code, await _cores.CompleteEventAsync(request.Http, subject.Id, ct).ConfigureAwait(false), subject);

            case "sprk_todo":
                if (!await DecisionExec.CallerHoldsAsync(_probe, request.Http, DecisionExec.TodoSet, subject.Id, "write", ct).ConfigureAwait(false))
                    return DecisionActionOutcome.Refused(Code, DecisionActionReasons.NotAuthorized, "You do not have permission to complete this To Do.");
                var body = DecisionExec.Json(new Dictionary<string, object?>
                {
                    ["statecode"] = 1,
                    ["statuscode"] = 2,
                    ["sprk_completedon"] = _clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                });
                return DecisionExec.FromReply(Code, await _cores.UpdateChildAsync(request.Http, "sprk_todo", subject.Id, body, ct).ConfigureAwait(false), subject);

            default:
                return DecisionActionOutcome.Refused(Code, DecisionActionReasons.SubjectUnsupported, "Only an event or a To Do can be completed here.");
        }
    }
}

/// <summary>
/// <c>reschedule</c> (D-27): writes <c>sprk_duedate</c> — never <c>sprk_finalduedate</c>. An event through the narrow events
/// write; a To Do through the child-records update core.
/// </summary>
public sealed class RescheduleExecutor : IDecisionActionExecutor
{
    private readonly CallerRecordAccessProbe _probe;
    private readonly DecisionRouteCores _cores;

    public RescheduleExecutor(CallerRecordAccessProbe probe, DecisionRouteCores cores)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _cores = cores ?? throw new ArgumentNullException(nameof(cores));
    }

    public string Code => "reschedule";

    public async Task<DecisionActionOutcome> ExecuteAsync(DecisionActionRequest request, CancellationToken ct)
    {
        if (!DecisionExec.TryDate(request.Param("dueDate"), out var due))
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.InvalidParameter, "Give the new due date as yyyy-MM-dd.");
        if (request.Subject is not { } subject)
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.SubjectUnsupported, "This item cannot be rescheduled here.");

        switch (subject.Entity)
        {
            case "sprk_event":
                if (!await DecisionExec.CallerHoldsAsync(_probe, request.Http, DecisionExec.EventSet, subject.Id, "write", ct).ConfigureAwait(false))
                    return DecisionActionOutcome.Refused(Code, DecisionActionReasons.NotAuthorized, "You do not have permission to reschedule this event.");
                return DecisionExec.FromEventWrite(Code,
                    await _cores.WriteEventDueAssigneeAsync(request.Http, subject.Id, new UpdateEventDueAssigneeRequest(due, null), ct).ConfigureAwait(false),
                    subject);

            case "sprk_todo":
                if (!await DecisionExec.CallerHoldsAsync(_probe, request.Http, DecisionExec.TodoSet, subject.Id, "write", ct).ConfigureAwait(false))
                    return DecisionActionOutcome.Refused(Code, DecisionActionReasons.NotAuthorized, "You do not have permission to reschedule this To Do.");
                var body = DecisionExec.Json(new Dictionary<string, object?> { ["sprk_duedate"] = DecisionExec.IsoDate(due) });
                return DecisionExec.FromReply(Code, await _cores.UpdateChildAsync(request.Http, "sprk_todo", subject.Id, body, ct).ConfigureAwait(false), subject);

            default:
                return DecisionActionOutcome.Refused(Code, DecisionActionReasons.SubjectUnsupported, "Only an event or a To Do can be rescheduled here.");
        }
    }
}

/// <summary>
/// <c>reassign</c>: the assignee is a CONTACT (<c>sprk_assignedto</c>). An event also becomes <c>Reassigned</c> with
/// <c>sprk_reassignedby</c> (the narrow events write); a To Do has no such status, so only the lookup changes.
/// </summary>
public sealed class ReassignExecutor : IDecisionActionExecutor
{
    private readonly CallerRecordAccessProbe _probe;
    private readonly DecisionRouteCores _cores;

    public ReassignExecutor(CallerRecordAccessProbe probe, DecisionRouteCores cores)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _cores = cores ?? throw new ArgumentNullException(nameof(cores));
    }

    public string Code => "reassign";

    public async Task<DecisionActionOutcome> ExecuteAsync(DecisionActionRequest request, CancellationToken ct)
    {
        if (!DecisionExec.TryGuid(request.Param("assignee"), out var contactId))
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.InvalidParameter, "Pick the person to reassign to.");
        if (request.Subject is not { } subject)
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.SubjectUnsupported, "This item cannot be reassigned here.");

        switch (subject.Entity)
        {
            case "sprk_event":
                if (!await DecisionExec.CallerHoldsAsync(_probe, request.Http, DecisionExec.EventSet, subject.Id, "write", ct).ConfigureAwait(false))
                    return DecisionActionOutcome.Refused(Code, DecisionActionReasons.NotAuthorized, "You do not have permission to reassign this event.");
                return DecisionExec.FromEventWrite(Code,
                    await _cores.WriteEventDueAssigneeAsync(request.Http, subject.Id, new UpdateEventDueAssigneeRequest(null, contactId), ct).ConfigureAwait(false),
                    subject);

            case "sprk_todo":
                if (!await DecisionExec.CallerHoldsAsync(_probe, request.Http, DecisionExec.TodoSet, subject.Id, "write", ct).ConfigureAwait(false))
                    return DecisionActionOutcome.Refused(Code, DecisionActionReasons.NotAuthorized, "You do not have permission to reassign this To Do.");
                var body = DecisionExec.Json(new Dictionary<string, object?> { ["sprk_assignedto@odata.bind"] = $"/contacts({contactId:D})" });
                return DecisionExec.FromReply(Code, await _cores.UpdateChildAsync(request.Http, "sprk_todo", subject.Id, body, ct).ConfigureAwait(false), subject);

            default:
                return DecisionActionOutcome.Refused(Code, DecisionActionReasons.SubjectUnsupported, "Only an event or a To Do can be reassigned here.");
        }
    }
}

/// <summary>
/// <c>extend-response-date</c> (D-54): writes <c>sprk_workassignment.sprk_responseduedate</c> as the caller. Work assignments
/// are not in the child-records update list, so this is the caller's own PATCH after their Write check.
/// </summary>
public sealed class ExtendResponseDateExecutor : IDecisionActionExecutor
{
    private readonly CallerRecordAccessProbe _probe;
    private readonly IDataverseUserClient _user;

    public ExtendResponseDateExecutor(CallerRecordAccessProbe probe, IDataverseUserClient user)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _user = user ?? throw new ArgumentNullException(nameof(user));
    }

    public string Code => "extend-response-date";

    public async Task<DecisionActionOutcome> ExecuteAsync(DecisionActionRequest request, CancellationToken ct)
    {
        if (!DecisionExec.TryDate(request.Param("responseDate"), out var date))
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.InvalidParameter, "Give the new response date as yyyy-MM-dd.");
        if (request.Subject is not { Entity: "sprk_workassignment" } subject)
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.SubjectUnsupported, "Only a work assignment has a response date.");

        if (!await DecisionExec.CallerHoldsAsync(_probe, request.Http, DecisionExec.WorkAssignmentSet, subject.Id, "write", ct).ConfigureAwait(false))
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.NotAuthorized, "You do not have permission to change this work assignment.");

        var patch = await _user.PatchAsync(
            $"{DecisionExec.WorkAssignmentSet}({subject.Id:D})",
            JsonSerializer.Serialize(new Dictionary<string, object?> { ["sprk_responseduedate"] = DecisionExec.IsoDate(date) }),
            ct).ConfigureAwait(false);
        return patch.IsSuccess
            ? DecisionActionOutcome.Done(Code, subject)
            : patch.StatusCode is 401 or 403
                ? DecisionActionOutcome.Refused(Code, DecisionActionReasons.NotAuthorized, "You do not have permission to change this work assignment.")
                : patch.StatusCode == 404
                    ? DecisionActionOutcome.Refused(Code, DecisionActionReasons.NotFound, "The work assignment was not found.")
                    : DecisionActionOutcome.Failed(Code, DecisionActionReasons.WriteFailed, "The response date could not be updated.");
    }
}

/// <summary>
/// <c>record-the-response</c> (D-54, D-58): writes <c>sprk_respondedon</c> (today in the caller's time zone) and
/// <c>sprk_responseoutcome</c> on the work assignment as the caller. There is no note column; notes go in the Decision Record.
/// </summary>
public sealed class RecordTheResponseExecutor : IDecisionActionExecutor
{
    // D-58: the live values of sprk_workassignment.sprk_responseoutcome (task 047), keyed by the catalog's option codes.
    internal static readonly IReadOnlyDictionary<string, int> OutcomeValues = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["received-outside-spaarke"] = 100000000,
        ["delivered-on-the-matter"] = 100000001,
        ["no-longer-needed"] = 100000002,
    };

    private readonly CallerRecordAccessProbe _probe;
    private readonly IDataverseUserClient _user;
    private readonly DecisionRouteCores _cores;

    public RecordTheResponseExecutor(CallerRecordAccessProbe probe, IDataverseUserClient user, DecisionRouteCores cores)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _cores = cores ?? throw new ArgumentNullException(nameof(cores));
    }

    public string Code => "record-the-response";

    public async Task<DecisionActionOutcome> ExecuteAsync(DecisionActionRequest request, CancellationToken ct)
    {
        if (request.Param("response") is not { } response || !OutcomeValues.TryGetValue(response, out var outcome))
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.InvalidParameter, "Pick how the response was received.");
        if (request.Subject is not { Entity: "sprk_workassignment" } subject)
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.SubjectUnsupported, "Only a work assignment records a response.");

        if (!await DecisionExec.CallerHoldsAsync(_probe, request.Http, DecisionExec.WorkAssignmentSet, subject.Id, "write", ct).ConfigureAwait(false))
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.NotAuthorized, "You do not have permission to change this work assignment.");

        var today = await _cores.TodayForCallerAsync(request.Http, ct).ConfigureAwait(false);
        var patch = await _user.PatchAsync(
            $"{DecisionExec.WorkAssignmentSet}({subject.Id:D})",
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["sprk_respondedon"] = DecisionExec.IsoDate(today),
                ["sprk_responseoutcome"] = outcome,
            }),
            ct).ConfigureAwait(false);
        return patch.IsSuccess
            ? DecisionActionOutcome.Done(Code, subject)
            : patch.StatusCode is 401 or 403
                ? DecisionActionOutcome.Refused(Code, DecisionActionReasons.NotAuthorized, "You do not have permission to change this work assignment.")
                : patch.StatusCode == 404
                    ? DecisionActionOutcome.Refused(Code, DecisionActionReasons.NotFound, "The work assignment was not found.")
                    : DecisionActionOutcome.Failed(Code, DecisionActionReasons.WriteFailed, "The response could not be recorded.");
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// Messages: send-reminder (Do lane) and send-email (Next step) share one send path
// ---------------------------------------------------------------------------------------------------------------------

/// <summary>
/// The recipient and send logic both message actions share (#30). Recipients are email addresses, or ids of a contact or of
/// a law firm (<c>sprk_organization</c>). A law firm is resolved to its primary contact with an email address; a recipient that
/// cannot be resolved to an address REFUSES the whole send (nothing is sent to the others). Every read runs as the caller.
/// </summary>
public abstract class MessageExecutorBase : IDecisionActionExecutor
{
    private static readonly Regex Address = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly CallerRecordAccessProbe _probe;
    private readonly IDataverseUserClient _user;
    private readonly DecisionRouteCores _cores;

    protected MessageExecutorBase(CallerRecordAccessProbe probe, IDataverseUserClient user, DecisionRouteCores cores)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _cores = cores ?? throw new ArgumentNullException(nameof(cores));
    }

    public abstract string Code { get; }

    /// <summary>True for the reminder, which may fall back to the subject's law-firm assignee when no recipient is typed.</summary>
    protected virtual bool FallsBackToLawFirm => false;

    public async Task<DecisionActionOutcome> ExecuteAsync(DecisionActionRequest request, CancellationToken ct)
    {
        if (request.Param("subject") is not { } subjectLine)
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.InvalidParameter, "Give the message a subject.");
        if (request.Param("body") is not { } body)
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.InvalidParameter, "Write the message.");

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
            return DecisionActionOutcome.Refused(Code, DecisionActionReasons.RecipientUnresolved, "There is no recipient to send to.");

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
                return DecisionActionOutcome.Refused(Code, DecisionActionReasons.RecipientUnresolved,
                    "A recipient could not be resolved to a contact with an email address, so nothing was sent.");
            addresses.Add(resolved);
        }

        // The message is filed against the core record when there is one: the caller needs AppendTo on it (the same right the
        // send route asks of every association). A caller without it gets no send at all.
        CommunicationAssociation[]? associations = null;
        if (request.Core is { } core)
        {
            if (!EntityAccessFilter.TryResolveEntitySet(core.Entity, out var set)
                || !await DecisionExec.CallerHoldsAsync(_probe, request.Http, set, core.Id, "entity.associate_document", ct).ConfigureAwait(false))
                return DecisionActionOutcome.Refused(Code, DecisionActionReasons.NotAuthorized, "You do not have permission to file a message against this record.");
            associations = [new CommunicationAssociation { EntityType = core.Entity, EntityId = core.Id, EntityName = core.Name }];
        }

        var send = new SendCommunicationRequest
        {
            To = addresses.ToArray(),
            Subject = subjectLine,
            Body = body,
            BodyFormat = BodyFormat.PlainText,
            Associations = associations,
        };

        var (reply, communicationId) = await _cores.SendCommunicationAsync(request.Http, send, ct).ConfigureAwait(false);
        if (!reply.IsSuccess)
            return DecisionExec.FromReply(Code, reply);

        return communicationId is { } sent
            ? DecisionActionOutcome.Done(Code, new DecisionRecordRef("sprk_communication", sent))
            : DecisionActionOutcome.Done(Code);
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

        var row = await _user.GetAsync($"{set}({item.Id:D})?$select=_sprk_assignedlawfirm1_value", ct).ConfigureAwait(false);
        return row.IsSuccess && ReadGuid(row.Body, "_sprk_assignedlawfirm1_value") is { } firm ? firm : null;
    }

    /// <summary>An email address for a contact id, or for a law firm id (its primary contacts, in order); null when neither has one.</summary>
    private async Task<string?> ResolveAddressAsync(Guid id, CancellationToken ct)
    {
        if (await ContactAddressAsync(id, ct).ConfigureAwait(false) is { } direct)
            return direct;

        var firm = await _user.GetAsync(
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
        var contact = await _user.GetAsync($"contacts({contactId:D})?$select=emailaddress1", ct).ConfigureAwait(false);
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
/// The child-records create the two record-creating Next steps share. The core checks, AS THE CALLER, Create on the table and
/// AppendTo on every record the payload binds, before the application creates the row; the executor only builds the payload
/// the browser would have sent (the typed regarding lookup for the core record, and the ADR-024 id/name/type fields) and
/// returns the created id.
/// </summary>
public abstract class ChildCreateExecutorBase : IDecisionActionExecutor
{
    private readonly DecisionRouteCores _cores;
    private readonly IGenericEntityService _entities;

    protected ChildCreateExecutorBase(DecisionRouteCores cores, IGenericEntityService entities)
    {
        _cores = cores ?? throw new ArgumentNullException(nameof(cores));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
    }

    public abstract string Code { get; }

    protected abstract string Table { get; }

    /// <summary>The table-specific columns, or a refusal when a parameter is missing or malformed.</summary>
    protected abstract DecisionActionOutcome? BuildColumns(DecisionActionRequest request, IDictionary<string, object?> payload);

    public async Task<DecisionActionOutcome> ExecuteAsync(DecisionActionRequest request, CancellationToken ct)
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
    public AddTodoExecutor(DecisionRouteCores cores, IGenericEntityService entities) : base(cores, entities)
    {
    }

    public override string Code => "add-todo";

    protected override string Table => "sprk_todo";

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
    public CreateEventExecutor(DecisionRouteCores cores, IGenericEntityService entities) : base(cores, entities)
    {
    }

    public override string Code => "create-event";

    protected override string Table => "sprk_event";

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
