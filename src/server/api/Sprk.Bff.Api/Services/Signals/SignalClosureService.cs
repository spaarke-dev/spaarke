using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>What a bulk system closure did (task 033).</summary>
/// <param name="Closed">Signals closed.</param>
/// <param name="ChangedSinceRead">Signals NOT closed because a person resolved them after they were read (row-version check).</param>
/// <param name="Failed">Signals whose close threw; the writer already logged and metered each one.</param>
/// <param name="PolicyFailures">Policies whose Signals could not be read or closed at all.</param>
/// <param name="AccessDenied">The part of <paramref name="Failed"/> that is a Dataverse privilege refusal
/// (<see cref="SignalWriter.IsDeterministicPrivilegeRefusal"/>): a retry is refused the same way, so a trigger does not redeliver on it.</param>
/// <remarks>
/// <b>Callers MUST check <see cref="IsComplete"/>.</b> A caller that reads only <see cref="Closed"/> reports "closed" while
/// Signals stay open (a failed close is counted here, not thrown, so one bad row cannot strand the rest).
/// </remarks>
public sealed record SignalClosureOutcome(int Closed, int ChangedSinceRead, int Failed, int PolicyFailures = 0, int AccessDenied = 0)
{
    public static SignalClosureOutcome None { get; } = new(0, 0, 0);

    /// <summary>True only when every targeted Signal was closed: nothing failed and no Signal was skipped because a person
    /// resolved it after it was read (benign, but the caller should re-run to confirm: the re-run finds it Resolved and is
    /// then complete).</summary>
    public bool IsComplete => Failed == 0 && ChangedSinceRead == 0 && PolicyFailures == 0;

    public SignalClosureOutcome Plus(SignalClosureOutcome other) =>
        new(Closed + other.Closed, ChangedSinceRead + other.ChangedSinceRead, Failed + other.Failed, PolicyFailures + other.PolicyFailures,
            AccessDenied + other.AccessDenied);
}

/// <summary>
/// The system closures that are not "the condition stopped holding" (task 033; spec FR-16, D-13, D-14, D-22, D-32): a Signal
/// citing a superseded policy version closes <c>Superseded</c>; a Signal of a policy that is Off (<c>sprk_enabled = No</c>) or
/// Retired closes <c>PolicyRetired</c>, and so does an open Signal of a policy that was deleted (task 102b fix round 3,
/// <see cref="CloseSignalsOfDeletedPoliciesAsync"/>). ConditionCleared (including a communication that simply aged out of the window) is
/// the job's own "not a hit" rule in <see cref="SignalEpisodePlanner"/>.
/// </summary>
/// <remarks>
/// <para><b>No Decision Record, no human.</b> Every close goes through <see cref="SignalWriter.CloseAsync"/>, which writes status,
/// resolution type, <c>sprk_resolvedon</c> and <c>sprk_lastevaluated</c> and nothing else: <c>sprk_decisionrecord</c> and
/// <c>sprk_resolvedby</c> stay null, and <c>sprk_suppresseduntil</c> is never set, so an auto-closure is never a dismissal and
/// never counts toward suppression (grain (policy, core record) or (policy, item), D-34/D-36/D-39). It also never touches
/// the Signal's policy, subject, core record, dedupe key or episode, so task 031 can find a <c>PolicyRetired</c> or
/// <c>Superseded</c> episode and re-raise from it (D-32). A Signal already Resolved is never read as open here, so no
/// closure path changes it. (Task 153, D-145b: only the job's Do-lane ConditionCleared closure is recorded, by the job after the
/// close; the closures here stay record-free.)</para>
/// <para><b>Callable.</b> <see cref="ClosePolicyRetiredNowAsync"/> is for task 100 (Retire closes immediately). Task 101
/// (publish) calls <see cref="SignalReevaluationJob.ReevaluatePolicyAsync"/> instead, which supersedes AND raises the new
/// episode in one pass (there is deliberately no close-only supersede: it would drop still-true items off the worklist until
/// the nightly run). The nightly job runs the same code, so the immediate and next-pass closures cannot disagree.</para>
/// <para><b>Superseded rule.</b> A Signal is superseded when the version it cites is not the policy's
/// <c>sprk_currentversion</c>. Publishing stamps the old version's <c>sprk_inforceto</c> and moves the pointer together (D-14), so a
/// cited version that is not current always has it set. A CURRENT version whose <c>sprk_inforceto</c> has passed has no
/// successor to re-raise under; the job refuses to evaluate such a policy (a failed run, nothing closed) rather than close
/// its Signals and reopen them on the same version every night.</para>
/// <para>§11: <i>Existing</i> — <see cref="SignalWriter.CloseAsync"/> closes one Signal; the job decides ConditionCleared only.
/// <i>Extension</i> — no existing component reads Signals by policy or state across the Off/Retire/publish triggers; the
/// writer and job are reused, not extended with admin-triggered callers. <i>Cost of doing nothing</i> — tasks 100 and 101
/// cannot close Signals immediately, and an Off or superseded policy's Signals stay Open in the worklist.</para>
/// </remarks>
public sealed class SignalClosureService
{
    private readonly SignalWriter _writer;
    private readonly IGenericEntityService _entities;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SignalClosureService> _logger;

    public SignalClosureService(
        SignalWriter writer, IGenericEntityService entities, TimeProvider timeProvider, ILogger<SignalClosureService> logger)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Task 100 (Retire): closes every open Signal of the policy as <c>PolicyRetired</c> now. The caller has already retired
    /// the policy (<c>statuscode</c> Retired + <c>sprk_retiredreason</c>); this method does not check, because the admin route
    /// that calls it owns that authorization. Calling it on a live policy only closes Signals the next pass re-raises.
    /// </summary>
    public async Task<SignalClosureOutcome> ClosePolicyRetiredNowAsync(Guid policyId, CancellationToken ct = default)
    {
        if (policyId == Guid.Empty) throw new ArgumentException("policyId must not be Guid.Empty.", nameof(policyId));
        var policy = await _entities.RetrieveAsync("sprk_policy", policyId, new[] { "sprk_policycode" }, ct).ConfigureAwait(false);
        var code = policy.GetAttributeValue<string>("sprk_policycode") ?? string.Empty;
        var open = await ReadOpenSignalsAsync(policyId, ct).ConfigureAwait(false);
        return await CloseAsync(code, open, SignalWriter.ResolutionPolicyRetired, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The job's in-pass supersede: closes the open Signals in <paramref name="existing"/> that cite a version other than
    /// <paramref name="currentVersionId"/> and returns the list with each closed Signal shown as Resolved/Superseded, so the same
    /// pass's planner re-raises the subjects the current version still holds.
    /// </summary>
    /// <param name="outsideScope">Task 102b (fix round 1, uac-r2 F2): for a policy limited to one matter, the Signals outside that scope.
    /// An open one closes <c>Superseded</c> as well, whatever version it cites (the current one, when the scope changed without a
    /// publish, or none): the policy's scope supersedes it. Null for an unscoped policy (only the version rule applies).</param>
    internal async Task<(IReadOnlyList<ExistingSignal> Existing, SignalClosureOutcome Outcome, IReadOnlySet<Guid> NotClosed)> SupersedeInPassAsync(
        string policyCode, Guid currentVersionId, IReadOnlyList<ExistingSignal> existing, CancellationToken ct,
        Func<ExistingSignal, bool>? outsideScope = null)
    {
        var superseded = existing
            .Where(s => IsSuperseded(s, currentVersionId) || (outsideScope is not null && s.IsOpen && outsideScope(s)))
            .ToList();
        if (superseded.Count == 0) return (existing, SignalClosureOutcome.None, new HashSet<Guid>());

        var closedIds = new HashSet<Guid>();
        var outcome = await CloseAsync(policyCode, superseded, SignalWriter.ResolutionSuperseded, ct, closedIds).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var updated = existing
            .Select(s => closedIds.Contains(s.Id)
                ? s with { Status = SignalWriter.SignalStatusResolved, Resolution = SignalWriter.ResolutionSuperseded, ResolvedOn = now }
                : s)
            .ToList();
        // Targeted but not closed (changed since read, or the close failed): the planner must leave them alone this pass, or it
        // would retry the same stale row and count the same Signal twice. The next pass re-reads them.
        return (updated, outcome, superseded.Where(s => !closedIds.Contains(s.Id)).Select(s => s.Id).ToHashSet());
    }

    /// <summary>
    /// The job's Off/Retired step: for every policy that is Off (<c>sprk_enabled = No</c>) or Inactive (Retired), closes its open
    /// Signals as <c>PolicyRetired</c>. Idempotent: a policy with no open Signal writes nothing (D-61). The Signals keep their
    /// policy, subject and episode, so switching the policy On again re-raises from them (D-32).
    /// </summary>
    internal async Task<SignalClosureOutcome> ClosePoliciesNotInForceAsync(CancellationToken ct)
    {
        var policies = await PoliciesNotInForceAsync(ct).ConfigureAwait(false);

        var total = SignalClosureOutcome.None;
        foreach (var (policyId, code) in policies)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var open = await ReadOpenSignalsAsync(policyId, ct).ConfigureAwait(false);
                total = total.Plus(await CloseAsync(code, open, SignalWriter.ResolutionPolicyRetired, ct).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                total = total with { PolicyFailures = total.PolicyFailures + 1 };
                _logger.LogError(OntologyWriterEvents.EvaluatorPolicyFailed, ex,
                    "Policy {PolicyCode} is not in force but its open Signals could not be read ({ExceptionType}); they were not closed this run.",
                    code, ex.GetType().Name);
            }
        }

        return total;
    }

    /// <summary>Every policy that is Off (<c>sprk_enabled = No</c>) or Inactive (Retired), by id, with its code. The one query of
    /// "not in force": the Off/Retired close above and the job's stored-evidence redaction of those policies' Signals (task 145).</summary>
    internal async Task<IReadOnlyDictionary<Guid, string>> PoliciesNotInForceAsync(CancellationToken ct)
    {
        var policies = new Dictionary<Guid, string>();
        foreach (var (attribute, value) in new (string, object)[] { ("sprk_enabled", false), ("statecode", 1) })
        {
            var query = new QueryExpression("sprk_policy") { ColumnSet = new ColumnSet("sprk_policycode") };
            query.Criteria.AddCondition(attribute, ConditionOperator.Equal, value);
            var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            foreach (var row in rows.Entities) policies[row.Id] = row.GetAttributeValue<string>("sprk_policycode") ?? string.Empty;
        }

        return policies;
    }

    /// <summary>
    /// Task 102b fix round 3 (owner 2026-10-17: a matter's delete deletes a rule limited to it; <c>sprk_policy_matter</c> is
    /// CascadeDelete=Cascade). A deleted policy's Signals keep their rows with <c>sprk_policy</c> nulled (<c>sprk_signal_policy</c> is
    /// RemoveLink), so neither the policy loop (by dedupe-key prefix of an EXISTING policy) nor the Off/Retired close (by the lookup)
    /// ever reaches them again, and an open one would stay in the worklist forever. This closes every Open or Acknowledged Signal whose
    /// <c>sprk_policy</c> is empty AND that <paramref name="existing"/> calls an orphan (<see cref="ExistingPolicyCodes.IsOrphan"/>: its
    /// dedupe key names a code no <c>sprk_policy</c> row carries, in any state, or (fix round 4, F1) the policy now holding that code was
    /// created after the Signal), as <c>PolicyRetired</c>: the same write as <see cref="ClosePolicyRetiredNowAsync"/> (status, resolution,
    /// resolvedon, lastevaluated; no Decision Record).
    /// </summary>
    /// <remarks>
    /// <para><b>Never fires on a live policy's Signal.</b> A Signal whose key's code matches an existing policy (compared ignoring case, so
    /// a doubt counts as "exists") is left alone, whatever that policy's state or scope, unless it was created BEFORE that policy (a reused
    /// code: the Signal was another, deleted policy's). A Signal created at or after it, or with either date unknown, is left alone even
    /// with an empty lookup (a hand edit). So is a Signal whose key cannot be parsed, and a row read back with a <c>sprk_policy</c> despite
    /// the condition.</para>
    /// <para><b>Fail closed.</b> <paramref name="existing"/> is read in full (<see cref="ExistingPolicyCodesAsync"/>) and the candidate
    /// Signals are read before anything is closed; either read faulting throws and nothing is closed. The Signals read is bounded to
    /// <see cref="SignalEvaluator.MaxPages"/> pages per run: the rest are reported (<see cref="DeletedPolicySweep.MoreRemain"/>) and closed
    /// by the next run, since a closed one drops out.</para>
    /// </remarks>
    internal async Task<DeletedPolicySweep> CloseSignalsOfDeletedPoliciesAsync(ExistingPolicyCodes existing, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var (candidates, moreRemain) = await ReadOpenSignalsWithoutPolicyAsync(ct).ConfigureAwait(false);

        var orphans = new Dictionary<string, List<ExistingSignal>>(StringComparer.Ordinal);
        var unparseableIds = new List<Guid>();
        var stillExists = 0;
        foreach (var (signal, key, createdOn) in candidates)
        {
            if (!SignalWriter.TryParseDedupeKey(key, out var code, out _, out _, out _))
            {
                unparseableIds.Add(signal.Id);
                continue;
            }

            if (!existing.IsOrphan(code, createdOn))
            {
                stillExists++;
                continue;
            }

            if (!orphans.TryGetValue(code, out var list)) orphans[code] = list = new List<ExistingSignal>();
            list.Add(signal);
        }

        var closedIds = new HashSet<Guid>();
        var total = SignalClosureOutcome.None;
        foreach (var (code, signals) in orphans)
        {
            ct.ThrowIfCancellationRequested();
            total = total.Plus(await CloseAsync(code, signals, SignalWriter.ResolutionPolicyRetired, ct, closedIds).ConfigureAwait(false));
        }

        var closedInOrder = candidates.Select(c => c.Signal.Id).Where(closedIds.Contains).ToList();
        var closedCodes = orphans.Where(o => o.Value.Any(s => closedIds.Contains(s.Id))).Select(o => o.Key)
            .Order(StringComparer.Ordinal).ToList();
        return new DeletedPolicySweep(total, closedInOrder, stillExists, unparseableIds.Count, moreRemain)
        {
            ClosedCodes = closedCodes,
            UnparseableIds = unparseableIds,
        };
    }

    /// <summary>
    /// Task 102b fix round 4 (item 1, D-123): the codes of deleted policies that still have Signals, in ANY status (Resolved included),
    /// for the stored-evidence redaction sweep. Reads the dedupe key and <c>createdon</c> of every Signal whose <c>sprk_policy</c> is empty,
    /// and keeps each parseable code that <paramref name="existing"/> calls an orphan for at least one of them (the same rule as
    /// <see cref="CloseSignalsOfDeletedPoliciesAsync"/>, so a reused code's older Signals are included). A read fault throws: the caller
    /// redacts nothing extra and fails the run. Bounded to <see cref="SignalEvaluator.MaxPages"/> pages: past it the codes read so far are
    /// returned with <see cref="DeletedPolicyCodes.MoreRemain"/> set (a partial list redacts less than the whole, never something else).
    /// </summary>
    internal async Task<DeletedPolicyCodes> DeletedPolicyCodesWithSignalsAsync(ExistingPolicyCodes existing, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var query = new QueryExpression("sprk_signal")
        {
            ColumnSet = new ColumnSet("sprk_dedupekey", "createdon"),
            PageInfo = new PagingInfo { Count = SignalEvaluator.PageSize, PageNumber = 1 },
        };
        query.Criteria.AddCondition("sprk_policy", ConditionOperator.Null);
        query.AddOrder("sprk_signalid", OrderType.Ascending);

        var codes = new SortedSet<string>(StringComparer.Ordinal);
        for (var page = 1; ; page++)
        {
            var result = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            foreach (var row in result.Entities)
            {
                if (row.GetAttributeValue<EntityReference>("sprk_policy") is not null) continue; // defence in depth, as above
                if (SignalWriter.TryParseDedupeKey(row.GetAttributeValue<string>("sprk_dedupekey"), out var code, out _, out _, out _)
                    && existing.IsOrphan(code, row.GetAttributeValue<DateTime?>("createdon")))
                {
                    codes.Add(code);
                }
            }

            if (!result.MoreRecords) return new DeletedPolicyCodes(codes.ToList(), false);
            if (page >= SignalEvaluator.MaxPages) return new DeletedPolicyCodes(codes.ToList(), true);
            query.PageInfo.PageNumber = page + 1;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }
    }

    /// <summary>Every <c>sprk_policy</c> row's code, whatever its state (Off and Retired included), with its <c>createdon</c>, paged; throws
    /// past <see cref="SignalEvaluator.MaxPages"/> pages rather than return a partial list (an incomplete list could make a live policy look
    /// deleted). One read serves both deleted-policy steps of a run, the close and the redaction (fix round 4).</summary>
    internal async Task<ExistingPolicyCodes> ExistingPolicyCodesAsync(CancellationToken ct)
    {
        // Keep this query UNFILTERED by domain: billing (ebilling-r1) depends on it seeing every policy code, and ebilling pins that in a test.
        var query = new QueryExpression("sprk_policy")
        {
            ColumnSet = new ColumnSet("sprk_policycode", "createdon"),
            PageInfo = new PagingInfo { Count = SignalEvaluator.PageSize, PageNumber = 1 },
        };
        query.AddOrder("sprk_policyid", OrderType.Ascending);

        var codes = new ExistingPolicyCodes();
        for (var page = 1; ; page++)
        {
            if (page > SignalEvaluator.MaxPages)
            {
                throw new InvalidOperationException($"There are more than {SignalEvaluator.MaxPages} pages of policies; the list is incomplete.");
            }

            var result = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            foreach (var row in result.Entities)
            {
                if (row.GetAttributeValue<string>("sprk_policycode") is { Length: > 0 } code) codes.Add(code, row.GetAttributeValue<DateTime?>("createdon"));
            }

            if (!result.MoreRecords) return codes;
            query.PageInfo.PageNumber = page + 1;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }
    }

    /// <summary>Open or Acknowledged Signals whose <c>sprk_policy</c> is empty, with the row version the conditional close needs, their
    /// dedupe key and <c>createdon</c>; at most <see cref="SignalEvaluator.MaxPages"/> pages (MoreRemain when Dataverse has more).</summary>
    private async Task<(IReadOnlyList<(ExistingSignal Signal, string? Key, DateTime? CreatedOn)> Signals, bool MoreRemain)> ReadOpenSignalsWithoutPolicyAsync(CancellationToken ct)
    {
        var query = new QueryExpression("sprk_signal")
        {
            ColumnSet = new ColumnSet("sprk_signalstatus", "sprk_policy", "sprk_policyversion", "sprk_dedupekey", "versionnumber", "createdon"),
            PageInfo = new PagingInfo { Count = SignalEvaluator.PageSize, PageNumber = 1 },
        };
        query.Criteria.AddCondition("sprk_policy", ConditionOperator.Null);
        query.Criteria.AddCondition("sprk_signalstatus", ConditionOperator.In,
            new object[] { SignalWriter.SignalStatusOpen, SignalWriter.SignalStatusAcknowledged });
        query.AddOrder("sprk_signalid", OrderType.Ascending);

        var signals = new List<(ExistingSignal, string?, DateTime?)>();
        for (var page = 1; ; page++)
        {
            var result = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            foreach (var row in result.Entities)
            {
                // Defence in depth: a row that carries a policy is never this sweep's, whatever the query returned.
                if (row.GetAttributeValue<EntityReference>("sprk_policy") is not null) continue;
                signals.Add((ToOpenSignal(row), row.GetAttributeValue<string>("sprk_dedupekey"), row.GetAttributeValue<DateTime?>("createdon")));
            }

            if (!result.MoreRecords) return (signals, false);
            if (page >= SignalEvaluator.MaxPages) return (signals, true);
            query.PageInfo.PageNumber = page + 1;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }
    }

    private static bool IsSuperseded(ExistingSignal signal, Guid currentVersionId) =>
        signal.IsOpen && signal.PolicyVersionId is { } cited && cited != currentVersionId;

    private async Task<SignalClosureOutcome> CloseAsync(
        string policyCode, IReadOnlyList<ExistingSignal> signals, int resolution, CancellationToken ct, HashSet<Guid>? closedIds = null)
    {
        int closed = 0, changed = 0, failed = 0, denied = 0;
        foreach (var signal in signals)
        {
            try
            {
                var outcome = await _writer.CloseAsync(signal.Id, signal.RowVersion, resolution, policyCode, ct).ConfigureAwait(false);
                if (outcome == SignalCloseOutcome.Closed)
                {
                    closed++;
                    closedIds?.Add(signal.Id);
                }
                else
                {
                    changed++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // The writer logged (50300-series WriteRefused) and metered it; the run counts it as refused.
                failed++;
                if (SignalWriter.IsDeterministicPrivilegeRefusal(ex))
                {
                    // K1: deterministic, so counted apart (a trigger does not redeliver on it) and logged as a Warning without a
                    // second Error: the writer's Error carries the exception.
                    denied++;
                    _logger.LogWarning(OntologyWriterEvents.EvaluatorPolicyFailed,
                        "Signal {SignalId} of policy {PolicyCode} was not closed (resolution {Resolution}, reason={Reason}); a retry cannot fix a missing privilege.",
                        signal.Id, policyCode, resolution, OntologyWriterFailureReason.DataverseAccessDenied);
                    continue;
                }

                _logger.LogError(OntologyWriterEvents.EvaluatorPolicyFailed, ex,
                    "Signal {SignalId} of policy {PolicyCode} was not closed (resolution {Resolution}, {ExceptionType}).",
                    signal.Id, policyCode, resolution, ex.GetType().Name);
            }
        }

        return new SignalClosureOutcome(closed, changed, failed, AccessDenied: denied);
    }

    /// <summary>Every Open or Acknowledged Signal of the policy (by the policy lookup, so no dedupe-key parsing and no Resolved
    /// row can be returned), with the row version the conditional close needs.</summary>
    private async Task<IReadOnlyList<ExistingSignal>> ReadOpenSignalsAsync(Guid policyId, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_signal")
        {
            ColumnSet = new ColumnSet("sprk_signalstatus", "sprk_policyversion", "sprk_dedupekey", "versionnumber"),
            PageInfo = new PagingInfo { Count = SignalEvaluator.PageSize, PageNumber = 1 },
        };
        query.Criteria.AddCondition("sprk_policy", ConditionOperator.Equal, policyId);
        query.Criteria.AddCondition("sprk_signalstatus", ConditionOperator.In,
            new object[] { SignalWriter.SignalStatusOpen, SignalWriter.SignalStatusAcknowledged });
        query.AddOrder("sprk_signalid", OrderType.Ascending);

        var signals = new List<ExistingSignal>();
        for (var page = 1; ; page++)
        {
            if (page > SignalEvaluator.MaxPages)
            {
                throw new InvalidOperationException($"Policy {policyId} has more than {SignalEvaluator.MaxPages} pages of open Signals.");
            }

            var result = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            foreach (var row in result.Entities)
            {
                signals.Add(ToOpenSignal(row));
            }

            if (!result.MoreRecords) return signals;
            query.PageInfo.PageNumber = page + 1;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }
    }

    /// <summary>The fields a system close needs (id, row version, status, cited version), from a Signal row.</summary>
    private static ExistingSignal ToOpenSignal(Entity row) => new(
        Id: row.Id,
        RowVersion: row.RowVersion ?? (row.GetAttributeValue<long?>("versionnumber") is { } v ? v.ToString(CultureInfo.InvariantCulture) : null),
        SubjectEntity: string.Empty,
        SubjectId: Guid.Empty,
        Episode: 0,
        Status: row.GetAttributeValue<OptionSetValue>("sprk_signalstatus")?.Value,
        Resolution: null,
        ResolvedOn: null,
        SuppressedUntil: null,
        DueDate: null,
        CoreTypeRefId: null,
        CoreRecordId: null,
        Owner: null,
        PolicyVersionId: row.GetAttributeValue<EntityReference>("sprk_policyversion")?.Id);
}

/// <summary>What <see cref="SignalClosureService.CloseSignalsOfDeletedPoliciesAsync"/> did (task 102b fix round 3).</summary>
/// <param name="Outcome">The closes, as for every system closure (callers add it to the run's counts).</param>
/// <param name="ClosedIds">The Signals closed <c>PolicyRetired</c>, in read order.</param>
/// <param name="PolicyStillExists">Signals with no policy lookup whose key names a policy that exists and is not younger than them: left alone.</param>
/// <param name="Unparseable">Signals with no policy lookup whose dedupe key cannot be parsed: left alone.</param>
/// <param name="MoreRemain">The per-run bound was reached; the next run continues.</param>
internal sealed record DeletedPolicySweep(
    SignalClosureOutcome Outcome, IReadOnlyList<Guid> ClosedIds, int PolicyStillExists, int Unparseable, bool MoreRemain)
{
    /// <summary>Fix round 4 (K a): the deleted policies' codes at least one Signal was closed for, in ordinal order.</summary>
    public IReadOnlyList<string> ClosedCodes { get; init; } = [];

    /// <summary>Fix round 4 (K b): the Signals counted in <see cref="Unparseable"/>, in read order.</summary>
    public IReadOnlyList<Guid> UnparseableIds { get; init; } = [];
}

/// <summary>What <see cref="SignalClosureService.DeletedPolicyCodesWithSignalsAsync"/> found (task 102b fix round 4).</summary>
/// <param name="Codes">The deleted policies' codes that still have Signals, in ordinal order.</param>
/// <param name="MoreRemain">The per-run bound was reached: more Signals with no policy lookup were not read this run.</param>
internal sealed record DeletedPolicyCodes(IReadOnlyList<string> Codes, bool MoreRemain);

/// <summary>
/// Every existing <c>sprk_policy</c> code (any state), compared ignoring case, with the <c>createdon</c> of the policy holding it (task 102b
/// fix rounds 3 and 4). When two rows hold the same code ignoring case, the EARLIEST date is kept, and an unknown date wins over any date:
/// both count more Signals as "still exists", the direction that never closes a live policy's Signal.
/// </summary>
internal sealed class ExistingPolicyCodes
{
    private readonly Dictionary<string, DateTime?> _createdOn = new(StringComparer.OrdinalIgnoreCase);

    internal void Add(string code, DateTime? createdOn)
    {
        if (!_createdOn.TryGetValue(code, out var known))
        {
            _createdOn[code] = createdOn;
        }
        else if (known is { } k)
        {
            _createdOn[code] = createdOn is { } c ? (c < k ? c : k) : null;
        }
    }

    /// <summary>True when a policy row (any state) holds the code.</summary>
    public bool Contains(string code) => _createdOn.ContainsKey(code);

    /// <summary>
    /// True when a Signal of <paramref name="code"/>, created at <paramref name="signalCreatedOn"/>, with an EMPTY <c>sprk_policy</c>
    /// belongs to a policy that no longer exists: no policy holds the code; or (fix round 4, F1) the policy holding it was created after
    /// the Signal, so the code was reused after the Signal's own policy was deleted. False when either date is unknown, and for a Signal
    /// created at or after the policy (a hand-edited lookup on a live policy's Signal is never an orphan).
    /// </summary>
    public bool IsOrphan(string code, DateTime? signalCreatedOn) =>
        !_createdOn.TryGetValue(code, out var policyCreatedOn)
        || (policyCreatedOn is { } policy && signalCreatedOn is { } signal && signal < policy);
}
