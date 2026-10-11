using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>What a bulk system closure did (task 033).</summary>
/// <param name="Closed">Signals closed.</param>
/// <param name="ChangedSinceRead">Signals NOT closed because a person resolved them after they were read (row-version check).</param>
/// <param name="Failed">Signals whose close threw; the writer already logged and metered each one.</param>
/// <param name="PolicyFailures">Policies whose Signals could not be read or closed at all.</param>
public sealed record SignalClosureOutcome(int Closed, int ChangedSinceRead, int Failed, int PolicyFailures = 0)
{
    public static SignalClosureOutcome None { get; } = new(0, 0, 0);

    public SignalClosureOutcome Plus(SignalClosureOutcome other) =>
        new(Closed + other.Closed, ChangedSinceRead + other.ChangedSinceRead, Failed + other.Failed, PolicyFailures + other.PolicyFailures);
}

/// <summary>
/// The system closures that are not "the condition stopped holding" (task 033; spec FR-16, D-13, D-14, D-22, D-32): a Signal
/// citing a superseded policy version closes <c>Superseded</c>; a Signal of a policy that is Off (<c>sprk_enabled = No</c>) or
/// Retired closes <c>PolicyRetired</c>. ConditionCleared (including a communication that simply aged out of the window) is
/// the job's own "not a hit" rule in <see cref="SignalEpisodePlanner"/>.
/// </summary>
/// <remarks>
/// <para><b>No Decision Record, no human.</b> Every close goes through <see cref="SignalWriter.CloseAsync"/>, which writes status,
/// resolution type, <c>sprk_resolvedon</c> and <c>sprk_lastevaluated</c> and nothing else: <c>sprk_decisionrecord</c> and
/// <c>sprk_resolvedby</c> stay null, and <c>sprk_suppresseduntil</c> is never set, so an auto-closure is never a dismissal and
/// never counts toward suppression (grain (policy, core record) or (policy, item), D-34/D-36/D-39). It also never touches
/// the Signal's policy, subject, core record, dedupe key or episode, so task 031 can find a <c>PolicyRetired</c> or
/// <c>Superseded</c> episode and re-raise from it (D-32). A Signal already Resolved is never read as open here, so no
/// closure path changes it.</para>
/// <para><b>Callable.</b> <see cref="ClosePolicyRetiredNowAsync"/> is for task 100 (Retire closes immediately) and
/// <see cref="CloseSupersededAsync"/> for task 101 (publish). The nightly job runs the same code, so the immediate and the
/// next-pass closure cannot disagree. The new version's re-raise is the job's (episode n+1 citing the new version); the callable
/// closes only.</para>
/// <para><b>Superseded rule.</b> A Signal is superseded when the version it cites is not the policy's
/// <c>sprk_currentversion</c>. <c>sprk_inforceto</c> is not consulted: publishing stamps it and moves the pointer together (D-14),
/// so a cited version that is not current always has it set, while a CURRENT version with it set has no successor to
/// re-raise under (closing it would reopen the same version every night).</para>
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
    /// Task 101 (publish): closes every open Signal of the policy that cites a version other than its current one as
    /// <c>Superseded</c>. Run it after the pointer has moved to the new version; the next pass raises the new episode.
    /// </summary>
    public async Task<SignalClosureOutcome> CloseSupersededAsync(Guid policyId, CancellationToken ct = default)
    {
        if (policyId == Guid.Empty) throw new ArgumentException("policyId must not be Guid.Empty.", nameof(policyId));
        var policy = await _entities.RetrieveAsync("sprk_policy", policyId, new[] { "sprk_policycode", "sprk_currentversion" }, ct)
            .ConfigureAwait(false);
        var code = policy.GetAttributeValue<string>("sprk_policycode") ?? string.Empty;
        if (policy.GetAttributeValue<EntityReference>("sprk_currentversion") is not { } current || current.Id == Guid.Empty)
        {
            throw new InvalidOperationException($"Policy {code} has no current version (sprk_currentversion is empty); nothing can be superseded.");
        }

        var open = await ReadOpenSignalsAsync(policyId, ct).ConfigureAwait(false);
        return await CloseAsync(code, open.Where(s => IsSuperseded(s, current.Id)).ToList(), SignalWriter.ResolutionSuperseded, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The job's in-pass supersede: closes the open Signals in <paramref name="existing"/> that cite a version other than
    /// <paramref name="currentVersionId"/> and returns the list with each closed Signal shown as Resolved/Superseded, so the same
    /// pass's planner re-raises the subjects the current version still holds.
    /// </summary>
    internal async Task<(IReadOnlyList<ExistingSignal> Existing, SignalClosureOutcome Outcome)> SupersedeInPassAsync(
        string policyCode, Guid currentVersionId, IReadOnlyList<ExistingSignal> existing, CancellationToken ct)
    {
        var superseded = existing.Where(s => IsSuperseded(s, currentVersionId)).ToList();
        if (superseded.Count == 0) return (existing, SignalClosureOutcome.None);

        var closedIds = new HashSet<Guid>();
        var outcome = await CloseAsync(policyCode, superseded, SignalWriter.ResolutionSuperseded, ct, closedIds).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var updated = existing
            .Select(s => closedIds.Contains(s.Id)
                ? s with { Status = SignalWriter.SignalStatusResolved, Resolution = SignalWriter.ResolutionSuperseded, ResolvedOn = now }
                : s)
            .ToList();
        return (updated, outcome);
    }

    /// <summary>
    /// The job's Off/Retired step: for every policy that is Off (<c>sprk_enabled = No</c>) or Inactive (Retired), closes its open
    /// Signals as <c>PolicyRetired</c>. Idempotent: a policy with no open Signal writes nothing (D-61). The Signals keep their
    /// policy, subject and episode, so switching the policy On again re-raises from them (D-32).
    /// </summary>
    internal async Task<SignalClosureOutcome> ClosePoliciesNotInForceAsync(CancellationToken ct)
    {
        var policies = new Dictionary<Guid, string>();
        foreach (var (attribute, value) in new (string, object)[] { ("sprk_enabled", false), ("statecode", 1) })
        {
            var query = new QueryExpression("sprk_policy") { ColumnSet = new ColumnSet("sprk_policycode") };
            query.Criteria.AddCondition(attribute, ConditionOperator.Equal, value);
            var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            foreach (var row in rows.Entities) policies[row.Id] = row.GetAttributeValue<string>("sprk_policycode") ?? string.Empty;
        }

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

    private static bool IsSuperseded(ExistingSignal signal, Guid currentVersionId) =>
        signal.IsOpen && signal.PolicyVersionId is { } cited && cited != currentVersionId;

    private async Task<SignalClosureOutcome> CloseAsync(
        string policyCode, IReadOnlyList<ExistingSignal> signals, int resolution, CancellationToken ct, HashSet<Guid>? closedIds = null)
    {
        int closed = 0, changed = 0, failed = 0;
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
                _logger.LogError(OntologyWriterEvents.EvaluatorPolicyFailed, ex,
                    "Signal {SignalId} of policy {PolicyCode} was not closed (resolution {Resolution}, {ExceptionType}).",
                    signal.Id, policyCode, resolution, ex.GetType().Name);
            }
        }

        return new SignalClosureOutcome(closed, changed, failed);
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
                signals.Add(new ExistingSignal(
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
                    PolicyVersionId: row.GetAttributeValue<EntityReference>("sprk_policyversion")?.Id));
            }

            if (!result.MoreRecords) return signals;
            query.PageInfo.PageNumber = page + 1;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }
    }
}
