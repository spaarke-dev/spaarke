using System.Globalization;
using System.Text.Json;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;

namespace Sprk.Bff.Api.Services.Ontology;

/// <summary>
/// The Ontology admin Rules area's reads and writes (ontology platform R1 task 100; spec FR-57; D-22; reconciliation M-3..M-5, H-7;
/// #43, #44): the rule list, the read-only rule page, On/Off and Retire.
/// </summary>
/// <remarks>
/// <para><b>Everything runs as the caller (D-22).</b> Every read and every write goes through <see cref="IDataverseUserClient"/>, so
/// Dataverse applies the caller's own privileges, enforces the Spaarke Ontology Administrator role on each write, and the audit names
/// the human. The service never consults the capability answer the Console tab uses (display only), and it holds no application-identity
/// Dataverse client: <c>OntologyAdminCallerBoundaryGuardTests</c> bans those types in this file. The only service-identity work in a
/// Retire is task 033's closure of the rule's open Signals, which is the Signal writer's job by design (ADR-028 path A) and is called
/// only AFTER the caller's own write to the rule succeeded.</para>
/// <para><b>Only Ontology rules (cross-project, ebilling-r1).</b> <c>sprk_policy</c> also holds Billing and Docketing rules. A rule is
/// listed, shown, switched or retired here only when its <c>sprk_domain</c> is empty or Ontology (<see cref="OntologyOnly"/>); every
/// other rule is the same 404 as a missing one, with no write.</para>
/// <para><b>Write semantics.</b> Off sets <c>sprk_enabled</c> = No and nothing else; the open items close at the next pass (FR-16).
/// Retire sets <c>statecode</c> Inactive + <c>statuscode</c> Retired + <c>sprk_retiredreason</c> in ONE write, then calls
/// <see cref="PolicyRetiredClosure"/> (task 033) so the open items close now as rule retired. Neither writes a Decision Record. A
/// toggle always issues its write, even when the value is unchanged, so Dataverse is the authority on every call (a Console User is
/// refused the same way whatever the current value).</para>
/// <para><b>Closure result (task 033 hand-off).</b> The closure outcome is read through <c>IsComplete</c>, never a count. When it is
/// incomplete only because a person acted on a Signal in the meantime (<c>ChangedSinceRead</c> &gt; 0, nothing failed) it is re-run
/// once; a Failed or PolicyFailures count, or a re-run still incomplete, is reported to the admin as incomplete (the nightly job closes
/// the rest within a day). A closure that throws is reported as unavailable: the rule IS retired.</para>
/// <para><b>Numbers (#44).</b> Open items, last fired and the action rate per version in force (H-7) are read as the caller, so the
/// counts cover the Signals the caller may read and a secure family's items are never inferable from a total (D-123). The three are
/// read INDEPENDENTLY: one that cannot be read (a fault, or Dataverse's 50,000-row aggregate limit, <c>AggregateQueryRecordLimit</c>) is
/// shown as "could not be read" on its own and never blanks the other two; the rules are always listed. Last fired looks back
/// <see cref="LastFiredWindowDays"/> days (the action rate's window) so its aggregate scans a bounded set, not every Signal ever written.</para>
/// <para>§11. <i>Existing</i>: the model-driven app is read-only (D-22) and no route read or wrote <c>sprk_policy</c> for
/// administration. <i>Extension</i>: the closure, the describer, the plan resolver and the action rate are reused as they are; this
/// class only composes them. <i>Cost of doing nothing</i>: a rule cannot be switched or retired with a reason and an immediate
/// closure from the Console.</para>
/// </remarks>
public sealed class OntologyAdminRulesService
{
    /// <summary>The longest <c>sprk_retiredreason</c> (the column is a 2000-character memo).</summary>
    public const int MaxReasonLength = 2000;

    /// <summary><c>sprk_domain</c> = Ontology, from <see cref="RuleDomains"/> (ebilling-r1 task 070).</summary>
    internal const int OntologyDomain = RuleDomains.PolicyDomainOntology;

    /// <summary>One page of policies; a result this long is refused rather than shown as the whole list.</summary>
    internal const int MaxPolicies = 1000;

    /// <summary>How far back the last-fired number looks: a bounded aggregate, never every Signal ever written.</summary>
    public const int LastFiredWindowDays = PolicyActionRateService.WindowDays;

    internal const int StateActive = 0;
    internal const int StatusRetired = 100000000;

    internal const string ReasonRequired = "ontology.rule.reason_required";
    internal const string ReasonTooLong = "ontology.rule.reason_too_long";
    internal const string EnabledRequired = "ontology.rule.enabled_required";
    internal const string AlreadyRetired = "ontology.rule.already_retired";
    internal const string NotActive = "ontology.rule.not_active";
    internal const string ChangedSinceRead = "ontology.rule.changed_since_read";

    private const string PolicySet = "sprk_policies";
    private const string VersionSet = "sprk_policyversions";

    private const string PolicyColumns =
        "sprk_policyid,sprk_name,sprk_policycode,sprk_enabled,statecode,statuscode,sprk_lane,sprk_subjecttype,sprk_severity,sprk_worktype,"
        + "sprk_priority,sprk_tenant,_sprk_matter_value,sprk_countstowardsuppression,_sprk_currentversion_value,sprk_retiredreason,"
        + "modifiedon,_modifiedby_value";

    private const string VersionSummaryColumns =
        "sprk_policyversionid,_sprk_policy_value,sprk_versionnumber,sprk_shortheadline,sprk_inforcefrom,sprk_inforceto,_sprk_authoredby_value,createdon";

    private const string VersionDetailColumns = VersionSummaryColumns
        + ",sprk_ruletype,sprk_rulebody,sprk_messagetemplate,sprk_proposedaction,sprk_decisionplan";

    // The four option-set tables below are pinned to the solution source by OntologyAdminRulesSchemaAgreementTests.
    internal static readonly IReadOnlyDictionary<int, string> LaneLabels = new Dictionary<int, string>
    {
        [100000000] = "Decide",
        [100000001] = "Do",
    };

    internal static readonly IReadOnlyDictionary<int, string> SeverityLabels = new Dictionary<int, string>
    {
        [100000000] = "Info",
        [100000001] = "Warning",
        [100000002] = "Critical",
    };

    internal static readonly IReadOnlyDictionary<int, string> SubjectTypeLabels = new Dictionary<int, string>
    {
        [100000000] = "Matter",
        [100000001] = "Communication",
        [100000002] = "Project",
        [100000003] = "Service Request",
        [100000004] = "Work Assignment",
        [100000005] = "To Do",
        [100000006] = "Event",
        [100000007] = "Invoice",
        [100000008] = "Document",
    };

    internal static readonly IReadOnlyDictionary<int, string> WorkTypeLabels = new Dictionary<int, string>
    {
        [100000000] = "Ask outside counsel",
        [100000001] = "Approve or rebudget",
        [100000002] = "Chase a reply",
        [100000003] = "Finish or reschedule",
        [100000004] = "Coming due",
        [100000005] = "Chase a response",
    };

    private readonly IDataverseUserClient _user;
    private readonly PolicyActionRateService _rates;
    private readonly RuleBodyDescriber _describer;
    private readonly PolicyRetiredClosure _closeRetired;
    private readonly ILogger<OntologyAdminRulesService> _logger;

    public OntologyAdminRulesService(
        IDataverseUserClient user,
        PolicyActionRateService rates,
        RuleBodyDescriber describer,
        PolicyRetiredClosure closeRetired,
        ILogger<OntologyAdminRulesService> logger)
    {
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _rates = rates ?? throw new ArgumentNullException(nameof(rates));
        _describer = describer ?? throw new ArgumentNullException(nameof(describer));
        _closeRetired = closeRetired ?? throw new ArgumentNullException(nameof(closeRetired));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ================================================================================================================
    // The Ontology-only condition. ONE place, used by every sprk_policy query and by-id read in this class.
    // ================================================================================================================

    private static string OntologyOnly() =>
        $"({RuleDomains.PolicyDomainColumn} eq null or {RuleDomains.PolicyDomainColumn} eq {OntologyDomain.ToString(CultureInfo.InvariantCulture)})";

    // ================================================================================================================
    // Reads
    // ================================================================================================================

    /// <summary>Every Ontology rule, with the numbers #44 allows. The All / Decide / Do / Needs a look filters are applied by the client.</summary>
    public async Task<OntologyAdminRulesResult<RuleListResponse>> ListAsync(CancellationToken ct)
    {
        var policies = await GetRowsAsync(
            $"{PolicySet}?$select={PolicyColumns}&$filter={OntologyOnly()}&$orderby=sprk_policycode asc&$top={MaxPolicies}", ct).ConfigureAwait(false);
        if (policies.Failure is { } failure)
        {
            return OntologyAdminRulesResult<RuleListResponse>.Fail(failure);
        }

        if (policies.Rows.Count >= MaxPolicies)
        {
            _logger.LogWarning("Ontology admin rules: {Count} policies came back, the page limit; refusing a list that may be short.", policies.Rows.Count);
            return OntologyAdminRulesResult<RuleListResponse>.Fail(OntologyAdminRulesOutcome.Unavailable);
        }

        var pointerIds = policies.Rows.Select(p => GuidOf(p, "_sprk_currentversion_value")).OfType<Guid>().Distinct().ToList();
        var versions = new Dictionary<Guid, JsonElement>();
        if (pointerIds.Count > 0)
        {
            var read = await GetRowsAsync(
                $"{VersionSet}?$select={VersionSummaryColumns}&$filter={SignalReadService.InFilter("sprk_policyversionid", pointerIds)}", ct)
                .ConfigureAwait(false);
            if (read.Failure is { } versionFailure)
            {
                return OntologyAdminRulesResult<RuleListResponse>.Fail(versionFailure);
            }

            foreach (var v in read.Rows)
            {
                if (GuidOf(v, "sprk_policyversionid") is { } id)
                {
                    versions[id] = v;
                }
            }
        }

        var metrics = await ReadMetricsAsync(ct).ConfigureAwait(false);
        var rules = new List<RuleSummary>(policies.Rows.Count);
        foreach (var policy in policies.Rows)
        {
            var version = GuidOf(policy, "_sprk_currentversion_value") is { } pointer && versions.TryGetValue(pointer, out var row) ? row : (JsonElement?)null;
            rules.Add(Summarize(policy, version, metrics));
        }

        return OntologyAdminRulesResult<RuleListResponse>.Ok(
            new RuleListResponse(
                rules, metrics.Availability.All, metrics.Availability, PolicyActionRateService.WindowDays, LastFiredWindowDays,
                PolicyActionRateService.MinimumJudged));
    }

    /// <summary>One rule's page: condition in words and formal form, wording, plan, scope, noise, behaviour, versions, change history.</summary>
    public async Task<OntologyAdminRulesResult<RuleDetailResponse>> GetAsync(Guid policyId, CancellationToken ct)
    {
        var found = await ReadPolicyAsync(policyId, PolicyColumns, ct).ConfigureAwait(false);
        if (found.Failure is { } failure)
        {
            return OntologyAdminRulesResult<RuleDetailResponse>.Fail(failure);
        }

        var policy = found.Row;
        var versionRead = await GetRowsAsync(
            $"{VersionSet}?$select={VersionDetailColumns}&$filter=_sprk_policy_value eq {policyId:D}&$orderby=sprk_versionnumber desc&$top=200", ct)
            .ConfigureAwait(false);
        if (versionRead.Failure is { } versionFailure)
        {
            return OntologyAdminRulesResult<RuleDetailResponse>.Fail(versionFailure);
        }

        var versions = versionRead.Rows;
        var inForce = PickInForce(policy, versions);
        var inForceId = inForce is { } v ? GuidOf(v, "sprk_policyversionid") : null;

        var lane = Int(policy, "sprk_lane");
        var names = await ReadUserNamesAsync(
            versions.Select(r => GuidOf(r, "_sprk_authoredby_value")).Append(GuidOf(policy, "_modifiedby_value")).OfType<Guid>().Distinct().ToList(), ct)
            .ConfigureAwait(false);
        var matterId = GuidOf(policy, "_sprk_matter_value");
        var matterName = matterId is { } m ? await ReadMatterNameAsync(m, ct).ConfigureAwait(false) : null;

        var metrics = await ReadMetricsAsync(ct).ConfigureAwait(false);
        var summary = Summarize(policy, inForce, metrics);

        var ruleBody = inForce is { } f ? Str(f, "sprk_rulebody") : null;
        var condition = await DescribeAsync(inForce, ruleBody, ct).ConfigureAwait(false);
        var wording = inForce is { } w
            ? new RuleWordingView(Str(w, "sprk_shortheadline"), Str(w, "sprk_messagetemplate"), Str(w, "sprk_proposedaction"))
            : new RuleWordingView(null, null, null);
        var plan = ResolvePlan(inForce is { } p ? Str(p, "sprk_decisionplan") : null, lane);

        var scope = new RuleScopeView(Str(policy, "sprk_tenant"), matterId, matterName, Int(policy, "sprk_priority"));
        var noise = new RuleNoiseView(
            Bool(policy, "sprk_countstowardsuppression") ?? true,
            SignalSuppressionService.ThresholdDismissals,
            SignalSuppressionService.WindowDays,
            QuietWindowDays(ruleBody));
        var behaviour = new RuleBehaviourView(
            summary.Lane, summary.Severity, Label(WorkTypeLabels, Int(policy, "sprk_worktype"), allowNull: true), summary.SubjectType,
            summary.State, Str(policy, "sprk_retiredreason"));

        var versionViews = versions
            .Select(r => new RuleVersionView(
                GuidOf(r, "sprk_policyversionid") ?? System.Guid.Empty,
                Int(r, "sprk_versionnumber") ?? 0,
                Str(r, "sprk_shortheadline"),
                GuidOf(r, "_sprk_authoredby_value") is { } by && names.TryGetValue(by, out var authoredBy) ? authoredBy : null,
                Date(r, "createdon"),
                Date(r, "sprk_inforcefrom"),
                Date(r, "sprk_inforceto"),
                inForceId is { } current && GuidOf(r, "sprk_policyversionid") == current))
            .OrderByDescending(r => r.Number)
            .ToList();

        return OntologyAdminRulesResult<RuleDetailResponse>.Ok(new RuleDetailResponse(
            summary, condition, wording, plan, scope, noise, behaviour, versionViews,
            BuildHistory(policy, versionViews, names),
            metrics.Availability.All, metrics.Availability, PolicyActionRateService.WindowDays, LastFiredWindowDays,
            PolicyActionRateService.MinimumJudged));
    }

    // ================================================================================================================
    // Writes (as the caller)
    // ================================================================================================================

    /// <summary>On/Off: sets <c>sprk_enabled</c> and nothing else. An inactive rule (Retired or otherwise) cannot be switched.</summary>
    public async Task<OntologyAdminRulesResult<RuleEnabledResult>> SetEnabledAsync(Guid policyId, bool? enabled, CancellationToken ct)
    {
        if (enabled is null)
        {
            return OntologyAdminRulesResult<RuleEnabledResult>.Fail(
                OntologyAdminRulesOutcome.Invalid, EnabledRequired, "The request must say whether the rule is on (enabled: true or false).");
        }

        var found = await ReadPolicyAsync(policyId, "sprk_policyid,sprk_policycode,statecode,statuscode", ct).ConfigureAwait(false);
        if (found.Failure is { } failure)
        {
            return OntologyAdminRulesResult<RuleEnabledResult>.Fail(failure);
        }

        if (Int(found.Row, "statecode") is { } state && state != StateActive)
        {
            return OntologyAdminRulesResult<RuleEnabledResult>.Fail(
                OntologyAdminRulesOutcome.Conflict, NotActive, "An inactive rule cannot be switched.");
        }

        var body = JsonSerializer.Serialize(new Dictionary<string, object?> { ["sprk_enabled"] = enabled.Value });
        var write = await WriteAsync(policyId, body, expectedVersion: null, ct).ConfigureAwait(false);
        if (write is not null)
        {
            return OntologyAdminRulesResult<RuleEnabledResult>.Fail(write.Value);
        }

        _logger.LogInformation("Ontology admin rules: policy {PolicyCode} switched {Enabled}.", Str(found.Row, "sprk_policycode"), enabled.Value ? "On" : "Off");
        return OntologyAdminRulesResult<RuleEnabledResult>.Ok(new RuleEnabledResult(policyId, enabled.Value));
    }

    /// <summary>
    /// Retire: a reason is required (nothing is read or written without one); the rule is set Retired with the reason in one write, as
    /// the caller; then its open Signals are closed now as rule retired. A rule already Retired is refused (409), so a Console User and an
    /// administrator get the same answer whatever the rule's state.
    /// </summary>
    public async Task<OntologyAdminRulesResult<RuleRetiredResult>> RetireAsync(Guid policyId, string? reason, CancellationToken ct)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return OntologyAdminRulesResult<RuleRetiredResult>.Fail(
                OntologyAdminRulesOutcome.Invalid, ReasonRequired, "A reason is required to retire a rule.");
        }

        if (trimmed.Length > MaxReasonLength)
        {
            return OntologyAdminRulesResult<RuleRetiredResult>.Fail(
                OntologyAdminRulesOutcome.Invalid, ReasonTooLong, $"The reason must be {MaxReasonLength} characters or fewer.");
        }

        var found = await ReadPolicyAsync(policyId, "sprk_policyid,sprk_policycode,statecode,statuscode,versionnumber", ct).ConfigureAwait(false);
        if (found.Failure is { } failure)
        {
            return OntologyAdminRulesResult<RuleRetiredResult>.Fail(failure);
        }

        if (Int(found.Row, "statuscode") == StatusRetired)
        {
            return OntologyAdminRulesResult<RuleRetiredResult>.Fail(
                OntologyAdminRulesOutcome.Conflict, AlreadyRetired, "The rule is already retired.");
        }

        // The Retire is conditional on the row version just read, so two admins retiring at once cannot both win (the loser would
        // overwrite the winner's reason and close the Signals a second time). No version read means no safe write: nothing is written.
        if (Long(found.Row, "versionnumber") is not { } version)
        {
            _logger.LogWarning("Ontology admin rules: policy {PolicyCode} was read without a versionnumber; nothing was written.", Str(found.Row, "sprk_policycode"));
            return OntologyAdminRulesResult<RuleRetiredResult>.Fail(OntologyAdminRulesOutcome.Unavailable);
        }

        var body = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["statecode"] = 1,
            ["statuscode"] = StatusRetired,
            ["sprk_retiredreason"] = trimmed,
        });
        var write = await WriteAsync(policyId, body, version, ct).ConfigureAwait(false);
        if (write is not null)
        {
            // The rule was not retired: the closure is NOT called (it does not check the state itself).
            return write.Value == OntologyAdminRulesOutcome.Conflict
                ? OntologyAdminRulesResult<RuleRetiredResult>.Fail(
                    OntologyAdminRulesOutcome.Conflict, ChangedSinceRead, "The rule has changed since you opened it. Reload the rule and check it before retiring.")
                : OntologyAdminRulesResult<RuleRetiredResult>.Fail(write.Value);
        }

        _logger.LogInformation("Ontology admin rules: policy {PolicyCode} retired.", Str(found.Row, "sprk_policycode"));
        var closure = await CloseOpenSignalsAsync(policyId, ct).ConfigureAwait(false);
        return OntologyAdminRulesResult<RuleRetiredResult>.Ok(new RuleRetiredResult(policyId, "Retired", trimmed, closure));
    }

    private async Task<RuleClosureView> CloseOpenSignalsAsync(Guid policyId, CancellationToken ct)
    {
        var closed = 0;
        try
        {
            var outcome = await _closeRetired(policyId, ct).ConfigureAwait(false);
            closed = outcome.Closed;

            // Incomplete ONLY because a person acted on a Signal after it was read: the re-run finds it Resolved. Anything that
            // failed is not re-run here (a retry cannot fix it); the nightly job closes the rest.
            if (!outcome.IsComplete && outcome.Failed == 0 && outcome.PolicyFailures == 0 && outcome.ChangedSinceRead > 0)
            {
                outcome = await _closeRetired(policyId, ct).ConfigureAwait(false);
                closed += outcome.Closed;
            }

            if (!outcome.IsComplete)
            {
                _logger.LogWarning(
                    "Ontology admin rules: policy {PolicyId} retired but its open Signals were not all closed (changedSinceRead={Changed}, failed={Failed}, policyFailures={PolicyFailures}); the nightly pass closes the rest.",
                    policyId, outcome.ChangedSinceRead, outcome.Failed, outcome.PolicyFailures);
            }

            return new RuleClosureView(closed, outcome.ChangedSinceRead, outcome.Failed, outcome.PolicyFailures, outcome.IsComplete, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Ontology admin rules: policy {PolicyId} retired but closing its open Signals threw ({ExceptionType}); the nightly pass closes them.",
                policyId, ex.GetType().Name);
            return new RuleClosureView(closed, 0, 0, 0, false, true);
        }
    }

    /// <summary>
    /// The caller's PATCH of one policy. Null when it succeeded, else the outcome to return. With <paramref name="expectedVersion"/> the
    /// PATCH is conditional on the row still being at that version: Dataverse answers 412 when it moved, returned here as
    /// <see cref="OntologyAdminRulesOutcome.Conflict"/> with nothing written.
    /// </summary>
    private async Task<OntologyAdminRulesOutcome?> WriteAsync(Guid policyId, string jsonBody, long? expectedVersion, CancellationToken ct)
    {
        DataverseUserResponse response;
        try
        {
            var path = $"{PolicySet}({policyId:D})";
            response = expectedVersion is { } version
                ? await _user.PatchAsync(path, jsonBody, version, ct).ConfigureAwait(false)
                : await _user.PatchAsync(path, jsonBody, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Ontology admin rules: the write to policy {PolicyId} threw ({ExceptionType}).", policyId, ex.GetType().Name);
            return OntologyAdminRulesOutcome.Unavailable;
        }

        if (response.IsSuccess)
        {
            return null;
        }

        _logger.LogWarning("Ontology admin rules: the write to policy {PolicyId} was refused ({ErrorCode}, status {Status}).",
            policyId, response.ErrorCode, response.StatusCode);
        return response.StatusCode == 412 ? OntologyAdminRulesOutcome.Conflict : Classify(response.ErrorCode);
    }

    // ================================================================================================================
    // Dataverse reads, as the caller
    // ================================================================================================================

    private readonly record struct RowsRead(IReadOnlyList<JsonElement> Rows, OntologyAdminRulesOutcome? Failure);

    private async Task<RowsRead> GetRowsAsync(string path, CancellationToken ct)
    {
        DataverseUserResponse response;
        try
        {
            response = await _user.GetAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Ontology admin rules: a read threw ({ExceptionType}).", ex.GetType().Name);
            return new RowsRead([], OntologyAdminRulesOutcome.Unavailable);
        }

        if (!response.IsSuccess)
        {
            _logger.LogWarning("Ontology admin rules: a read was refused ({ErrorCode}, status {Status}).", response.ErrorCode, response.StatusCode);
            return new RowsRead([], Classify(response.ErrorCode));
        }

        if (response.Body is not { } body || body.ValueKind != JsonValueKind.Object
            || !body.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("Ontology admin rules: a read answered a body with no value array.");
            return new RowsRead([], OntologyAdminRulesOutcome.Unavailable);
        }

        return new RowsRead(value.EnumerateArray().ToList(), null);
    }

    private readonly record struct PolicyRead(JsonElement Row, OntologyAdminRulesOutcome? Failure);

    /// <summary>One Ontology rule by id, as the caller. Missing, invisible and not-an-Ontology-rule are all NotFound.</summary>
    private async Task<PolicyRead> ReadPolicyAsync(Guid policyId, string columns, CancellationToken ct)
    {
        if (policyId == System.Guid.Empty)
        {
            return new PolicyRead(default, OntologyAdminRulesOutcome.NotFound);
        }

        var read = await GetRowsAsync(
            $"{PolicySet}?$select={columns}&$filter=sprk_policyid eq {policyId:D} and {OntologyOnly()}&$top=1", ct).ConfigureAwait(false);
        if (read.Failure is { } failure)
        {
            return new PolicyRead(default, failure);
        }

        return read.Rows.Count == 0
            ? new PolicyRead(default, OntologyAdminRulesOutcome.NotFound)
            : new PolicyRead(read.Rows[0], null);
    }

    private static OntologyAdminRulesOutcome Classify(string? errorCode) => errorCode switch
    {
        DataverseUserClientErrorCodes.UserContextRequired
            or DataverseUserClientErrorCodes.OboNotConfigured
            or DataverseUserClientErrorCodes.OboExchangeFailed => OntologyAdminRulesOutcome.CallerUnresolved,
        DataverseUserClientErrorCodes.AccessDenied => OntologyAdminRulesOutcome.Forbidden,
        DataverseUserClientErrorCodes.NotFound => OntologyAdminRulesOutcome.NotFound,
        _ => OntologyAdminRulesOutcome.Unavailable,
    };

    /// <summary>Best effort: a name that cannot be read is shown as unknown, never a failed page.</summary>
    private async Task<Dictionary<Guid, string>> ReadUserNamesAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string>();
        if (ids.Count == 0)
        {
            return names;
        }

        var read = await GetRowsAsync(
            $"systemusers?$select=systemuserid,fullname&$filter={SignalReadService.InFilter("systemuserid", ids)}", ct).ConfigureAwait(false);
        foreach (var row in read.Rows)
        {
            if (GuidOf(row, "systemuserid") is { } id && Str(row, "fullname") is { Length: > 0 } name)
            {
                names[id] = name;
            }
        }

        return names;
    }

    private async Task<string?> ReadMatterNameAsync(Guid matterId, CancellationToken ct)
    {
        var read = await GetRowsAsync(
            $"sprk_matters?$select=sprk_mattername&$filter=sprk_matterid eq {matterId:D}&$top=1", ct).ConfigureAwait(false);
        return read.Rows.Count > 0 ? Str(read.Rows[0], "sprk_mattername") : null;
    }

    // ================================================================================================================
    // Numbers (#44, H-7)
    // ================================================================================================================

    /// <summary>
    /// The three Signal numbers, each read on its own (a null member is a number that could not be read). Kept apart so one fault
    /// (a Dataverse error, or the 50,000-row aggregate limit, <c>AggregateQueryRecordLimit</c>) shows as "could not be read" for that
    /// number only.
    /// </summary>
    private sealed record Metrics(
        IReadOnlyList<PolicyActionRate>? Rates, IReadOnlyDictionary<Guid, int>? Open, IReadOnlyDictionary<Guid, DateTime>? LastFired)
    {
        public RuleMetricsAvailability Availability => new(Open is not null, LastFired is not null, Rates is not null);
    }

    private const string OpenItemsFetch =
        "<fetch aggregate=\"true\"><entity name=\"sprk_signal\">"
        + "<attribute name=\"sprk_signalid\" alias=\"n\" aggregate=\"count\"/>"
        + "<attribute name=\"sprk_policy\" alias=\"policy\" groupby=\"true\"/>"
        + "<filter><condition attribute=\"sprk_signalstatus\" operator=\"in\"><value>100000000</value><value>100000001</value></condition></filter>"
        + "</entity></fetch>";

    // Bounded to the last LastFiredWindowDays days. An aggregate over EVERY Signal ever written fails tenant-wide with
    // AggregateQueryRecordLimit once there are more than 50,000 rows; a window keeps the scan to recent Signals.
    private static readonly string LastFiredFetch =
        "<fetch aggregate=\"true\"><entity name=\"sprk_signal\">"
        + "<attribute name=\"createdon\" alias=\"last\" aggregate=\"max\"/>"
        + "<attribute name=\"sprk_policy\" alias=\"policy\" groupby=\"true\"/>"
        + $"<filter><condition attribute=\"createdon\" operator=\"last-x-days\" value=\"{LastFiredWindowDays.ToString(CultureInfo.InvariantCulture)}\"/></filter>"
        + "</entity></fetch>";

    /// <summary>The three Signal reads, all as the caller, each independent: a fault in one is logged and leaves that number unread; the rules are still shown.</summary>
    private async Task<Metrics> ReadMetricsAsync(CancellationToken ct)
    {
        var rates = await TryReadAsync("action rate", () => _rates.GetAsync(ct), ct).ConfigureAwait(false);

        var open = await TryReadAsync("open items", async () =>
        {
            var counts = new Dictionary<Guid, int>();
            foreach (var row in await AggregateAsync(OpenItemsFetch, ct).ConfigureAwait(false))
            {
                if (GuidOf(row, "policy") is { } policy)
                {
                    counts[policy] = InquiryDispositionService.ReadInt(row, "n");
                }
            }

            return (IReadOnlyDictionary<Guid, int>)counts;
        }, ct).ConfigureAwait(false);

        var last = await TryReadAsync("last fired", async () =>
        {
            var newest = new Dictionary<Guid, DateTime>();
            foreach (var row in await AggregateAsync(LastFiredFetch, ct).ConfigureAwait(false))
            {
                if (GuidOf(row, "policy") is { } policy && Date(row, "last") is { } at)
                {
                    newest[policy] = at;
                }
            }

            return (IReadOnlyDictionary<Guid, DateTime>)newest;
        }, ct).ConfigureAwait(false);

        return new Metrics(rates, open, last);
    }

    /// <summary>Runs one Signal read; null (and a warning that names the number) when it faults.</summary>
    private async Task<T?> TryReadAsync<T>(string number, Func<Task<T>> read, CancellationToken ct) where T : class
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Ontology admin rules: the {Number} number could not be read ({ExceptionType}); the rules are shown without it.",
                number, ex.GetType().Name);
            return null;
        }
    }

    private async Task<List<JsonElement>> AggregateAsync(string fetchXml, CancellationToken ct)
    {
        var response = await _user.GetAsync($"sprk_signals?fetchXml={Uri.EscapeDataString(fetchXml)}", ct).ConfigureAwait(false);
        return InquiryDispositionService.ReadRows(response);
    }

    private static RuleMetrics MetricsFor(JsonElement policy, Guid? versionId, Metrics metrics)
    {
        var id = GuidOf(policy, "sprk_policyid") ?? System.Guid.Empty;
        var code = Str(policy, "sprk_policycode") ?? string.Empty;

        RuleActionRate? actionRate = null;
        if (metrics.Rates is { } rates)
        {
            var inVersion = rates.Where(r => r.PolicyCode == code && versionId is { } v && r.PolicyVersionId == v).ToList();
            var rate = new PolicyActionRate(code, versionId, SignalLane.Decide, inVersion.Sum(r => r.Acted), inVersion.Sum(r => r.Dismissed));
            actionRate = new RuleActionRate(rate.Acted, rate.Dismissed, rate.Rate, rate.TooFewToJudge);
        }

        return new RuleMetrics(
            metrics.Open is { } open ? open.GetValueOrDefault(id) : null,
            metrics.LastFired is { } last && last.TryGetValue(id, out var at) ? at : null,
            actionRate,
            NeedsALook: actionRate?.Rate is < 0.5);
    }

    // ================================================================================================================
    // Shaping
    // ================================================================================================================

    private static RuleSummary Summarize(JsonElement policy, JsonElement? version, Metrics metrics)
    {
        var versionId = version is { } v ? GuidOf(v, "sprk_policyversionid") : null;
        var enabled = Bool(policy, "sprk_enabled") ?? false;
        return new RuleSummary(
            GuidOf(policy, "sprk_policyid") ?? System.Guid.Empty,
            Str(policy, "sprk_policycode") ?? string.Empty,
            Str(policy, "sprk_name") ?? string.Empty,
            Label(LaneLabels, Int(policy, "sprk_lane")),
            StateOf(policy, enabled),
            enabled,
            version is { } n ? Int(n, "sprk_versionnumber") : null,
            version is { } h ? Str(h, "sprk_shortheadline") : null,
            Label(SeverityLabels, Int(policy, "sprk_severity")),
            Label(SubjectTypeLabels, Int(policy, "sprk_subjecttype")),
            MetricsFor(policy, versionId, metrics));
    }

    /// <summary>Retired wins over the On/Off column; any other inactive rule reads Inactive.</summary>
    private static string StateOf(JsonElement policy, bool enabled)
    {
        if (Int(policy, "statuscode") == StatusRetired)
        {
            return "Retired";
        }

        if (Int(policy, "statecode") is { } state && state != StateActive)
        {
            return "Inactive";
        }

        return enabled ? "On" : "Off";
    }

    /// <summary>The version the rule runs on: the policy's pointer; when it is empty or names no readable version, the newest version
    /// with no in-force-to date.</summary>
    private static JsonElement? PickInForce(JsonElement policy, IReadOnlyList<JsonElement> versions)
    {
        if (GuidOf(policy, "_sprk_currentversion_value") is { } pointer)
        {
            foreach (var v in versions)
            {
                if (GuidOf(v, "sprk_policyversionid") == pointer)
                {
                    return v;
                }
            }
        }

        JsonElement? best = null;
        foreach (var v in versions)
        {
            if (Date(v, "sprk_inforceto") is null && (best is null || (Int(v, "sprk_versionnumber") ?? 0) > (Int(best.Value, "sprk_versionnumber") ?? 0)))
            {
                best = v;
            }
        }

        return best;
    }

    private async Task<RuleConditionView> DescribeAsync(JsonElement? version, string? ruleBody, CancellationToken ct)
    {
        if (version is not { } v)
        {
            return new RuleConditionView("None", [], "The rule has no version in force.");
        }

        var typeValue = Int(v, "sprk_ruletype");
        var type = typeValue is { } t && Enum.IsDefined(typeof(RuleType), t) ? ((RuleType)t).ToString() : "Unknown";
        try
        {
            var result = await _describer.DescribeAsync(ruleBody, ct).ConfigureAwait(false);
            return result.Description is { } description
                ? new RuleConditionView(
                    type, description.Clauses.Select(c => new RuleClauseView(c.ClauseId, c.Kind, c.Text, c.Formal)).ToList(), null)
                : new RuleConditionView(type, [], result.Refusal ?? "The condition could not be described.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Ontology admin rules: the condition could not be described ({ExceptionType}).", ex.GetType().Name);
            return new RuleConditionView(type, [], "The condition could not be described.");
        }
    }

    private static RulePlanView ResolvePlan(string? planJson, int? lane)
    {
        if (lane is not { } l || !Enum.IsDefined(typeof(DecisionLane), l))
        {
            return new RulePlanView([], [], [], "The rule's lane is not known, so its plan cannot be resolved.");
        }

        var resolution = DecisionPlanService.Resolve(planJson, (DecisionLane)l);
        if (resolution.Plan is not { } plan)
        {
            return new RulePlanView([], [], [], $"The stored plan does not resolve against the action catalog ({resolution.RefusalReason}).");
        }

        return new RulePlanView(
            plan.Actions.Select(ToView).ToList(),
            plan.NextSteps.Select(ToView).ToList(),
            plan.DismissalReasons.Select(r => r.Label).ToList(),
            null);

        static RulePlanActionView ToView(DecisionActionDefinition a) => new(a.Code, a.Label, a.EffectLines);
    }

    /// <summary>The rule body's <c>quietWindowDays</c> knob (D-13), else the default; a body that cannot be read shows the default.</summary>
    private static int QuietWindowDays(string? ruleBody)
    {
        if (string.IsNullOrWhiteSpace(ruleBody))
        {
            return PredicateCompiler.DefaultQuietWindowDays;
        }

        try
        {
            using var doc = JsonDocument.Parse(ruleBody);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("quietWindowDays", out var knob)
                && knob.ValueKind == JsonValueKind.Number && knob.TryGetInt32(out var days) && days >= 0)
            {
                return days;
            }
        }
        catch (JsonException)
        {
            // fall through to the default
        }

        return PredicateCompiler.DefaultQuietWindowDays;
    }

    /// <summary>
    /// #43: the change history from what is immutable and readable: each version's authoring (<c>sprk_authoredby</c>, <c>createdon</c>) and
    /// supersession (<c>sprk_inforceto</c>), plus the policy row's own last change (<c>modifiedon</c>, <c>modifiedby</c>). The audit log is
    /// not used: no Spaarke role can read it. The policy line says only that the row changed last then, not what changed.
    /// </summary>
    private static List<RuleHistoryEntry> BuildHistory(JsonElement policy, IReadOnlyList<RuleVersionView> versions, IReadOnlyDictionary<Guid, string> names)
    {
        var history = new List<RuleHistoryEntry>();
        foreach (var v in versions)
        {
            history.Add(new RuleHistoryEntry(v.CreatedOn, v.AuthoredBy, $"Version {v.Number} authored"));
            if (v.InForceTo is { } to)
            {
                history.Add(new RuleHistoryEntry(to, null, $"Version {v.Number} superseded"));
            }
        }

        if (Date(policy, "modifiedon") is { } modified)
        {
            history.Add(new RuleHistoryEntry(
                modified,
                GuidOf(policy, "_modifiedby_value") is { } by && names.TryGetValue(by, out var name) ? name : null,
                "Rule record last changed (On/Off, Retire or an edit)"));
        }

        return history.OrderByDescending(h => h.When).ToList();
    }

    private static string Label(IReadOnlyDictionary<int, string> table, int? value) => Label(table, value, allowNull: false) ?? "Unknown";

    private static string? Label(IReadOnlyDictionary<int, string> table, int? value, bool allowNull) =>
        value is { } v ? (table.TryGetValue(v, out var label) ? label : "Unknown") : (allowNull ? null : "Unknown");

    // ---- JSON cell readers ---------------------------------------------------------------------------------------------

    private static string? Str(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i
            : null;

    private static long? Long(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l)
            ? l
            : null;

    private static bool? Bool(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;

    private static Guid? GuidOf(JsonElement row, string name) =>
        Str(row, name) is { } s && System.Guid.TryParse(s, out var g) ? g : null;

    private static DateTime? Date(JsonElement row, string name) =>
        Str(row, name) is { } s
        && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? d
            : null;
}
