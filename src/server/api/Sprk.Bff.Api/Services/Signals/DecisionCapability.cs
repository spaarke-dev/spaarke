using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// The decision capability (spaarkeai-mcp-bff-api-r1 task 013, design §8 B-1, spec FR-10 as amended 2026-11-07, option C): the
/// worklist, decision-plan and suppression route bodies, moved here unchanged so the REST routes and the MCP tools run one copy.
/// Task 014 adds the commit member in <c>DecisionCapability.Commit.cs</c>, a direct delegation to <see cref="DecisionCommitService"/>.
/// </summary>
/// <remarks>
/// <para><b>Unchanged rules.</b> The worklist is <see cref="SignalReadService.ReadAsync"/> as it was; the plan and suppression reads
/// are the former <c>DecisionPlanEndpoints</c> bodies line for line, in the same order: <see cref="SignalCoreRecordAccess.AuthorizeAsync"/>
/// first, then the plan (or the count), and the plan-read stamp last, only on a returned plan. Only the HTTP shaping stayed in the
/// endpoints (through <see cref="DecisionResultMapper"/>). The one addition is the caller guard: no request context or no authenticated
/// identity is <c>UserContextRequired</c> before any Dataverse call (FR-10; ADR-002 WP-6, fail closed). It uses the same test as
/// <c>RequireAuthorization</c>'s default policy (any authenticated identity), so a request that reached a REST route always passes it.</para>
/// <para><b>Placement justification (CLAUDE.md section 10; .claude/constraints/bff-extensions.md).</b> The decision slice's data owner is
/// the BFF (FR-24: Signals are readable only through BFF routes), so the second host (MCP, in the same BFF, design §14) needs the same
/// bodies in a shared place rather than a copy. No package, no AI-internal type, no background work; scoped like the services it calls
/// and registered unconditionally (SignalsModule), like the routes that use it.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: the endpoint bodies in <c>DecisionPlanEndpoints</c> and
/// <c>SignalReadEndpoints</c>. Extension: this is those bodies, moved; the endpoints keep only HTTP shaping. Cost of doing nothing: the
/// MCP tools would call endpoint code or copy it, giving the plan-read stamp and the read rules a second owner (spec §11).</para>
/// </remarks>
public sealed partial class DecisionCapability : IDecisionCapability
{
    /// <summary>
    /// The worklist read's <c>UserContextRequired</c> answer. <see cref="SignalReadResult"/> is ontology's type and has no such outcome,
    /// so it is the single-403 outcome (what <see cref="SignalReadService"/> answers when the caller client reports
    /// <c>UserContextRequired</c>) tagged with that code as its reason; <see cref="SignalReadService"/> never sets a reason on
    /// <see cref="SignalReadOutcome.CallerUnresolved"/>, so <see cref="DecisionResultMapper"/> can tell the two apart.
    /// </summary>
    public static readonly SignalReadResult UserContextRequiredReadResult =
        new(SignalReadOutcome.CallerUnresolved, null, DataverseUserClientErrorCodes.UserContextRequired);

    private readonly SignalReadService _signalRead;
    private readonly SignalCoreRecordAccess _signalAccess;
    private readonly DecisionPlanService _planService;
    private readonly RuleBodyDescriber _describer;
    private readonly SignalSuppressionService _suppression;
    private readonly IDataverseUserClient _caller;
    private readonly ILogger<SignalSuppressionService> _suppressionLogger;
    private readonly DecisionCommitService _commits;
    private readonly DecisionPlanReadStamp? _planReadStamp;
    private readonly DecisionParameterSources? _parameterSources;

    /// <param name="suppressionLogger">The suppression route's logger, kept so its log category is unchanged by the move.</param>
    /// <param name="commits">The single owner of completion (task 014); <see cref="CommitAsync"/> delegates to it unchanged.</param>
    /// <param name="planReadStamp">Optional exactly as it was on the plan route: without it no stamp is written.</param>
    /// <param name="parameterSources">Optional exactly as it was on the plan route: without it no option list is resolved.</param>
    public DecisionCapability(
        SignalReadService signalRead,
        SignalCoreRecordAccess signalAccess,
        DecisionPlanService planService,
        RuleBodyDescriber describer,
        SignalSuppressionService suppression,
        IDataverseUserClient caller,
        ILogger<SignalSuppressionService> suppressionLogger,
        DecisionCommitService commits,
        DecisionPlanReadStamp? planReadStamp = null,
        DecisionParameterSources? parameterSources = null)
    {
        _signalRead = signalRead ?? throw new ArgumentNullException(nameof(signalRead));
        _signalAccess = signalAccess ?? throw new ArgumentNullException(nameof(signalAccess));
        _planService = planService ?? throw new ArgumentNullException(nameof(planService));
        _describer = describer ?? throw new ArgumentNullException(nameof(describer));
        _suppression = suppression ?? throw new ArgumentNullException(nameof(suppression));
        _caller = caller ?? throw new ArgumentNullException(nameof(caller));
        _suppressionLogger = suppressionLogger ?? throw new ArgumentNullException(nameof(suppressionLogger));
        _commits = commits ?? throw new ArgumentNullException(nameof(commits));
        _planReadStamp = planReadStamp;
        _parameterSources = parameterSources;
    }

    /// <summary>True when the principal carries an authenticated identity: the test <c>RequireAuthorization</c>'s default policy
    /// (<c>DenyAnonymousAuthorizationRequirement</c>) applies, so every request that reached a REST route passes it.</summary>
    internal static bool HasSignedInCaller([NotNullWhen(true)] ClaimsPrincipal? user) =>
        user is not null && user.Identities.Any(i => i.IsAuthenticated);

    /// <inheritdoc />
    public async Task<SignalReadResult> ListDecideAsync(ClaimsPrincipal? user, CancellationToken ct, WorklistSet set = WorklistSet.Lanes)
    {
        if (!HasSignedInCaller(user))
        {
            return UserContextRequiredReadResult;
        }

        return await _signalRead.ReadAsync(set, user, ct);
    }

    /// <inheritdoc />
    public async Task<DecidePageResult> ListDecidePageAsync(ClaimsPrincipal? user, string? cursor, CancellationToken ct)
    {
        if (!HasSignedInCaller(user))
        {
            return DecidePageResult.UserContextRequired;
        }

        // A cursor that does not decode is refused before anything is read; a well-formed one that no longer matches the list is not an error.
        DecideCursor? after = null;
        if (!string.IsNullOrEmpty(cursor) && !DecideCursor.TryDecode(cursor, out after))
        {
            return DecidePageResult.CursorInvalid;
        }

        // The lane is read exactly as the Console's list is (SignalReadService, as the caller); the page only slices its result.
        var extras = new Dictionary<Guid, WorklistItemExtras>();
        var read = await _signalRead.ReadAsync(WorklistSet.Lanes, user, ct, extras);
        if (read.Outcome != SignalReadOutcome.Ok || read.Response is not { } response)
        {
            return DecidePageResult.ReadFailed(read);
        }

        var decide = response.Lanes.FirstOrDefault(l => l.Lane == "Decide");
        var (items, next) = DecideLanePaging.Page(decide?.Groups ?? [], extras, after);
        return DecidePageResult.Ok(items, next);
    }

    /// <inheritdoc />
    public async Task<DecisionPlanReadResult> GetPlanAsync(HttpContext? http, Guid signalId, CancellationToken ct)
    {
        if (http is null || !HasSignedInCaller(http.User))
        {
            return DecisionPlanReadResult.UserContextRequired;
        }

        var decision = await _signalAccess.AuthorizeAsync(signalId, ct);
        if (decision.Outcome != SignalAccessOutcome.Allowed || decision.Lane is not { } lane || decision.PolicyVersionId is not { } versionId)
        {
            return DecisionPlanReadResult.NotAllowed(decision.Outcome);
        }

        var resolution = await _planService.GetAsync(versionId, lane, ct);
        if (resolution.Plan is not { } plan)
        {
            return DecisionPlanReadResult.Refused(resolution.RefusalReason);
        }

        // Task 154 (D-111): an OPEN inquiry's "Record the outcome" To Do is completed by recording the disposition, never by Mark
        // complete. Pass 2 F1: identified exactly as the commit route identifies it (the hint, else the To Do's matter's open
        // inquiries), from the notes and matter the gate's own "mine" read returned, as the caller; only for a To Do whose plan offers
        // Mark complete. Once the inquiry is closed the To Do is ordinary (adversarial pass 1). A fault offers nothing (503).
        if (plan.Actions.Any(a => a.Code == ResolvedDecisionPlan.MarkCompleteCode))
        {
            var inquiry = await _signalAccess.CheckSubjectTodoInquiryAsync(decision, ct);
            if (inquiry == InquiryOutcomeTodoCheck.Unavailable)
            {
                return DecisionPlanReadResult.AccessUnavailable;
            }

            if (inquiry == InquiryOutcomeTodoCheck.OpenInquiry)
            {
                plan = plan.WithoutMarkComplete();
            }
        }

        // Task 161 (D-180): a matter with no active budget has nothing to revise, so Create budget takes Revise budget's place in the
        // plan. Read as the caller; the commit route applies the same substitution (SignalCoreRecordAccess), so the two always agree.
        // A count that cannot be read leaves Revise budget offered and never fails this read.
        plan = await _signalAccess.WithCreateBudgetWhenNoneAsync(plan, decision, ct);

        // Task 160 (D-179): the budget inquiry's To pre-fill, read as the caller; none when it cannot be read. Never fails the read.
        var inquiryRecipient = await _signalAccess.ReadInquiryRecipientAsync(plan, decision, ct);

        // Task 167 (D-193): the live option lists and starting values the plan's parameters name (Create budget's Category, Period and
        // Currency), read once per source through the one resolver. Never fails the read: a list that cannot be read is empty.
        var parameterFacts = _parameterSources is null
            ? DecisionParameterFacts.None
            : await _parameterSources.ForPlanAsync(
                http, plan,
                string.Equals(decision.CoreTable, "sprk_matter", StringComparison.OrdinalIgnoreCase) ? decision.CoreId : null, ct);

        // "How this was determined" (task 026). A body that cannot be described is shown without the description, never
        // as a guess; it does not stop the decision from being taken.
        var described = await _describer.DescribeAsync(resolution.RuleBodyJson, ct);

        // Task 043 (K4): the server's own record of when this caller read this plan, so the commit route never trusts a client
        // time to decide whether a system closure happened during the review. Best effort; it never fails the read.
        if (_planReadStamp is not null)
        {
            await _planReadStamp.StampAsync(http, signalId, ct);
        }

        return DecisionPlanReadResult.Ok(
            DecisionPlanResponse.From(signalId, lane, versionId, plan, described.Description, inquiryRecipient, parameterFacts));
    }

    /// <inheritdoc />
    public async Task<SuppressionReadResult> GetSuppressionAsync(HttpContext? http, Guid signalId, CancellationToken ct)
    {
        if (http is null || !HasSignedInCaller(http.User))
        {
            return SuppressionReadResult.UserContextRequired;
        }

        var decision = await _signalAccess.AuthorizeAsync(signalId, ct);
        if (decision.Outcome != SignalAccessOutcome.Allowed)
        {
            return SuppressionReadResult.NotAllowed(decision.Outcome);
        }

        // The caller may read this Signal and its core record, so the count is theirs to see. The count is trimmed to the Signals the
        // caller's own read returns; the Decision-Record facts for those rows (not readable to Console users) are read with the BFF's own
        // reader, and only the numbers and the date are returned.
        SuppressionStatus? status;
        try
        {
            // D-145a (task 152): a Do item's count is its person's own, and only that person is told it.
            status = await _suppression.GetStatusForSignalAsync(signalId, _caller, ct, decision.CallerContactId);
        }
        catch (SuppressionPartitionUndeterminedException)
        {
            // #1661 round 2 K5: a data condition, not a fault. A retry does not change it, so the caller is not told to retry; the
            // Signal's secure family cannot be known, so its count is never answered (never one from across the wall).
            _suppressionLogger.LogWarning("Suppression count withheld: the secure-family partition of Signal {SignalId} is undetermined; answering 503.", signalId);
            return SuppressionReadResult.PartitionUndetermined;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // AFTER Allowed: the caller is authorized, so this is a fault of ours, not a denial. A timeout (a cancellation the caller did
            // not ask for) is the same. 503 with the trace id, never a guessed count and never the uniform 404 (that answer is for "not yours").
            _suppressionLogger.LogError(ex, "Suppression count failed for an authorized caller (Signal {SignalId}); answering 503.", signalId);
            return SuppressionReadResult.Unavailable;
        }

        return status is null
            ? SuppressionReadResult.NotFound
            : SuppressionReadResult.Ok(SuppressionResponse.From(signalId, status));
    }
}
