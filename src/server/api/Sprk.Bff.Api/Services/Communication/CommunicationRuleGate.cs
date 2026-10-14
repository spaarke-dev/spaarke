using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Input to the comms policy gate (spec FR-12): the dimensions an assessed communication is matched + gated on.
/// </summary>
/// <param name="CommunicationId">The assessed <c>sprk_communication</c> id (for decision logging/correlation).</param>
/// <param name="Tenant">Tenant/org identifier; null/blank matches rules with a blank <c>sprk_tenant</c> ("all tenants").</param>
/// <param name="MatterId">The communication's regarding matter; null matches rules with an empty <c>sprk_matter</c> ("all matters").</param>
/// <param name="Confidence">The assessment confidence [0–1] compared against the matched rule's threshold.</param>
public sealed record CommunicationRuleDecisionRequest(
    Guid CommunicationId,
    string? Tenant,
    Guid? MatterId,
    double Confidence);

/// <summary>
/// The comms policy gate's decision (spec FR-12). Carries the authorize/deny outcome, the matched rule (if any),
/// the confidence + threshold compared, an explicit ADR-015 privilege flag (flagged, NEVER auto-decided), and a
/// human-readable reason. Produced on BOTH authorize and deny paths.
/// </summary>
public sealed record CommunicationRuleDecision(
    bool Authorize,
    Guid? MatchedRuleId,
    double Confidence,
    double Threshold,
    bool PrivilegeFlagged,
    string Reason,
    /// <summary>Days from now for the RI task's <c>sprk_duedate</c> — the matched rule's
    /// <c>sprk_taskduedays</c>, else <see cref="CommsPolicyOptions.DefaultTaskDueDays"/>.</summary>
    int TaskDueDays = 1,
    /// <summary>Days from now for the RI task's <c>sprk_finalduedate</c> — the matched rule's
    /// <c>sprk_taskfinalduedays</c>, else <see cref="CommsPolicyOptions.DefaultTaskFinalDueDays"/>.</summary>
    int TaskFinalDueDays = 3);

/// <summary>
/// The comms Responsive-Intelligence policy gate (spec FR-12). Decides WHETHER to act on an assessed
/// communication by (1) resolving the applicable rule for its tenant/matter from the dedicated
/// <c>sprk_communicationrule</c> table (owner decision 2026-07-22, <c>notes/041-rule-store-decision.md</c>),
/// and (2) evaluating the assessment confidence against the matched rule's threshold (falling back to
/// <see cref="CommsPolicyOptions.DefaultConfidenceThreshold"/>). Rule match + confidence pass → AUTHORIZE
/// (actions are handed to task 042's future call point, NOT executed here); no match, or match with confidence
/// below threshold → DENY, logged, no side effect.
/// </summary>
/// <remarks>
/// <para>
/// <b>Reuses gate PRIMITIVES, not the chat seam.</b> The confidence-threshold dial mirrors
/// <c>EventRulesOptions.ClassifyConfidenceThreshold</c> as a pattern (via <see cref="CommsPolicyOptions"/>);
/// this gate NEVER calls <c>IEventRulesService.FireAsync</c> and keys NO state on chat <c>SessionId</c>/<c>UserOid</c>
/// (spec constraint). It is a pure read-then-evaluate over its own table — no outbox write, no action execution.
/// </para>
/// <para>
/// <b>Privilege is flagged, never decided (ADR-015).</b> <see cref="CommunicationRuleDecision.PrivilegeFlagged"/>
/// carries the matched rule's <c>sprk_flagprivilege</c> value forward for downstream/human review; the gate does
/// NOT suppress or allow based on privilege.
/// </para>
/// <para>
/// <b>Placement / justification (root §10/§11).</b> Concrete class (ADR-010 — no second implementation needed).
/// Lives in <c>Services/Communication/</c> beside the comms-RI producer (task 040) and its own rule table.
/// Reads its dedicated <c>sprk_communicationrule</c> store (owner-chosen Path B) — it introduces no second AI
/// *routing* surface (Binding still owns dispatch routing); ADR-039 tension accepted per the rule-store note.
/// </para>
/// </remarks>
public sealed class CommunicationRuleGate
{
    private const string RuleEntity = "sprk_communicationrule";
    private const string IdColumn = "sprk_communicationruleid";
    private const string TenantColumn = "sprk_tenant";
    private const string MatterColumn = "sprk_matter";
    private const string ThresholdColumn = "sprk_confidencethreshold";
    private const string FlagPrivilegeColumn = "sprk_flagprivilege";
    private const string EnabledColumn = "sprk_enabled";
    private const string PriorityColumn = "sprk_priority";
    // Added 2026-09-29: the RI task's due dates are DECLARED on the rule, not compiled in -- same
    // rule-wins-over-options pattern as sprk_confidencethreshold. Purpose of the rule table.
    private const string TaskDueDaysColumn = "sprk_taskduedays";
    private const string TaskFinalDueDaysColumn = "sprk_taskfinalduedays";

    private static readonly string[] RuleColumns =
    {
        IdColumn, TenantColumn, MatterColumn, ThresholdColumn, FlagPrivilegeColumn, PriorityColumn,
        TaskDueDaysColumn, TaskFinalDueDaysColumn,
    };

    private readonly IGenericEntityService _entityService;
    private readonly CommsPolicyOptions _options;
    private readonly ILogger<CommunicationRuleGate> _logger;

    public CommunicationRuleGate(
        IGenericEntityService entityService,
        IOptions<CommsPolicyOptions> options,
        ILogger<CommunicationRuleGate> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Evaluates the policy gate for <paramref name="request"/>. Never throws for a business outcome (deny is a
    /// value, not an exception); a store-read failure fail-closes to DENY (no ungoverned action).
    /// </summary>
    public async Task<CommunicationRuleDecision> EvaluateAsync(
        CommunicationRuleDecisionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<Entity> rules;
        try
        {
            rules = await LoadEnabledRulesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Fail-closed: a rule-store read failure denies (no silent action on unreadable policy).
            _logger.LogWarning(ex,
                "[comms-policy] rule store read failed — DENY (fail-closed) | CommunicationId: {CommunicationId}",
                request.CommunicationId);
            return Deny(request, _options.DefaultConfidenceThreshold, privilege: false, "rule-store-read-failed");
        }

        // Match: tenant blank-or-equal AND matter empty-or-equal; lowest sprk_priority wins (then id for determinism).
        var matched = rules
            .Where(r => TenantMatches(r, request.Tenant) && MatterMatches(r, request.MatterId))
            .OrderBy(r => r.GetAttributeValue<int?>(PriorityColumn) ?? 500)
            .ThenBy(r => r.Id)
            .FirstOrDefault();

        if (matched is null)
        {
            var decision = Deny(request, _options.DefaultConfidenceThreshold, privilege: false, "no-matching-rule");
            LogDecision(decision, request);
            return decision;
        }

        var threshold = ReadThreshold(matched) ?? _options.DefaultConfidenceThreshold;
        var privilege = matched.GetAttributeValue<bool>(FlagPrivilegeColumn);
        var authorize = request.Confidence >= threshold;

        var result = new CommunicationRuleDecision(
            Authorize: authorize,
            MatchedRuleId: matched.Id,
            Confidence: request.Confidence,
            Threshold: threshold,
            PrivilegeFlagged: privilege,
            Reason: authorize ? "rule-matched-confidence-met" : "rule-matched-confidence-below-threshold",
            TaskDueDays: matched.GetAttributeValue<int?>(TaskDueDaysColumn) ?? _options.DefaultTaskDueDays,
            TaskFinalDueDays: matched.GetAttributeValue<int?>(TaskFinalDueDaysColumn)
                              ?? _options.DefaultTaskFinalDueDays);

        LogDecision(result, request);
        return result;
    }

    private async Task<IReadOnlyList<Entity>> LoadEnabledRulesAsync(CancellationToken ct)
    {
        var query = new QueryExpression(RuleEntity)
        {
            ColumnSet = new ColumnSet(RuleColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition(EnabledColumn, ConditionOperator.Equal, true);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0); // Active

        var result = await _entityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return result?.Entities?.ToList() ?? (IReadOnlyList<Entity>)Array.Empty<Entity>();
    }

    private static bool TenantMatches(Entity rule, string? requestTenant)
    {
        var ruleTenant = rule.GetAttributeValue<string>(TenantColumn);
        // Blank rule tenant = "all tenants".
        return string.IsNullOrWhiteSpace(ruleTenant)
               || string.Equals(ruleTenant, requestTenant, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatterMatches(Entity rule, Guid? requestMatterId)
    {
        var ruleMatter = rule.GetAttributeValue<EntityReference>(MatterColumn);
        // Empty rule matter = "all matters".
        return ruleMatter is null || ruleMatter.Id == Guid.Empty || ruleMatter.Id == requestMatterId;
    }

    private static double? ReadThreshold(Entity rule)
    {
        var value = rule.GetAttributeValue<decimal?>(ThresholdColumn);
        return value.HasValue ? (double)value.Value : null;
    }

    private CommunicationRuleDecision Deny(
        CommunicationRuleDecisionRequest request, double threshold, bool privilege, string reason)
        => new(Authorize: false, MatchedRuleId: null, Confidence: request.Confidence,
               Threshold: threshold, PrivilegeFlagged: privilege, Reason: reason,
               TaskDueDays: _options.DefaultTaskDueDays,
               TaskFinalDueDays: _options.DefaultTaskFinalDueDays);

    private void LogDecision(CommunicationRuleDecision d, CommunicationRuleDecisionRequest request)
        => _logger.LogInformation(
            "[comms-policy] decision={Decision} | CommunicationId: {CommunicationId}, RuleId: {RuleId}, " +
            "Confidence: {Confidence}, Threshold: {Threshold}, PrivilegeFlagged: {PrivilegeFlagged}, Reason: {Reason}",
            d.Authorize ? "AUTHORIZE" : "DENY", request.CommunicationId, d.MatchedRuleId,
            d.Confidence, d.Threshold, d.PrivilegeFlagged, d.Reason);
}

/// <summary>
/// The REAL consumer behind task 040's <see cref="ICommunicationAssessedProducer"/> seam (spec FR-12): maps an
/// incoming <see cref="CommunicationAssessedSignal"/> to a <see cref="CommunicationRuleDecisionRequest"/> and
/// runs it through <see cref="CommunicationRuleGate"/>. Replaces the interim log-only default
/// (<c>LoggingCommunicationAssessedProducer</c>) registered by task 040 — the emit point in enrichment step 5 is
/// unchanged (that is the seam's whole point). On AUTHORIZE it delegates to
/// <see cref="CommunicationRiActionService"/> (task 042), which executes the RI action end-to-end (Layer-A seam
/// record → Layer-B outbox → Layer-C ping → appnotification mirror). On DENY it is a logged no-op — NO seam
/// call, NO outbox row, NO ping, NO appnotification (the short-circuit is structural: the RI service is only
/// invoked inside the <c>if (decision.Authorize)</c> branch). Never calls <c>IEventRulesService.FireAsync</c>.
/// </summary>
/// <remarks>
/// <b>The Decision Record (ontology task 042, FR-23).</b> The gate's decision is persisted ONCE, right after the gate returns and BEFORE
/// the authorize branch, so the authorize and the deny path share one call site and the gate is untouched. The write is
/// <see cref="DecisionRecordWriter.WriteGateDecisionAsync"/>; it can never change what this consumer does: a refusal or a fault is
/// logged (<see cref="DecisionRecordEvents.GateDecisionNotRecorded"/>) and metered, and the RI action still runs on an authorize. The
/// fact snapshot holds ids and numbers only (never the subject, sender or body).
/// </remarks>
public sealed class RuleGatedAssessedConsumer : ICommunicationAssessedProducer
{
    private const string CommunicationEntity = "sprk_communication";
    private const string RegardingMatterColumn = "sprk_regardingmatter";
    private const string TriageCategoryColumn = "sprk_triagecategory";

    /// <summary>The gate's proposed action as the Decision Record names it: free text, not a <c>DecisionActionCatalog</c> code (task 042).</summary>
    internal const string ProposedActionCode = "communication-ri-action";

    internal const string ProposedActionLabel = "Create a review task and notify the communication's owner";

    private readonly IGenericEntityService _entityService;
    private readonly CommunicationRuleGate _gate;
    private readonly CommunicationRiActionService _riAction;
    private readonly ILogger<RuleGatedAssessedConsumer> _logger;
    private readonly SignalEvaluationTrigger? _signalTrigger;
    private readonly DecisionRecordWriter? _decisionRecords;

    /// <param name="signalTrigger">Ontology task 032 (D-12): queues the matter's Signal re-evaluation once a communication has
    /// been classified. Registered in production; null only where a test builds the consumer without it.</param>
    /// <param name="decisionRecords">Ontology task 042 (FR-23): writes the gate decision's Decision Record on both paths. Registered in
    /// production (<c>SignalsModule</c>); null only where a test builds the consumer without it, in which case nothing is recorded.</param>
    public RuleGatedAssessedConsumer(
        IGenericEntityService entityService,
        CommunicationRuleGate gate,
        CommunicationRiActionService riAction,
        ILogger<RuleGatedAssessedConsumer> logger,
        SignalEvaluationTrigger? signalTrigger = null,
        DecisionRecordWriter? decisionRecords = null)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _riAction = riAction ?? throw new ArgumentNullException(nameof(riAction));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _signalTrigger = signalTrigger;
        _decisionRecords = decisionRecords;
    }

    public async Task PublishAsync(CommunicationAssessedSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        // Resolve the matter (regarding) for rule matching. Tenant is null today (single-tenant per environment);
        // a blank-tenant rule matches all tenants. A read failure resolves matter to null (matches all-matter rules).
        Guid? matterId = null;
        Guid? directFiledUnderId = null;
        var classified = false;
        try
        {
            var comm = await _entityService
                .RetrieveAsync(
                    CommunicationEntity, signal.CommunicationId,
                    new[] { RegardingMatterColumn, TriageCategoryColumn, CoreAncestorResolver.RegardingRecordIdColumn }, ct)
                .ConfigureAwait(false);
            // ADR-024 pair id: the record the communication is DIRECTLY filed under -- D-37's first tie-break for the Decision Record's
            // core. Not the matter lookup (that one only feeds rule matching below, unchanged).
            directFiledUnderId = Guid.TryParse(
                comm?.GetAttributeValue<string>(CoreAncestorResolver.RegardingRecordIdColumn)?.Trim(), out var filedUnder) && filedUnder != Guid.Empty
                ? filedUnder
                : null;
            var matter = comm?.GetAttributeValue<EntityReference>(RegardingMatterColumn);
            if (matter is not null && matter.Id != Guid.Empty)
            {
                matterId = matter.Id;
            }

            classified = comm?.GetAttributeValue<EntityReference>(TriageCategoryColumn) is { Id: var category } && category != Guid.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[comms-policy] could not resolve regarding matter for {CommunicationId}; matching against all-matter rules.",
                signal.CommunicationId);
        }

        // Ontology task 032 (D-12): the classification is persisted before this assessed event (enrichment step order), so a
        // classified communication on a matter may now raise that matter's Work Item. QUEUED, never evaluated here, so enrichment
        // latency is not coupled to evaluation; independent of the RI gate below (that decides notifications, this decides
        // Signals). A communication filed later reaches the evaluator through the association-confirmed trigger (D-48).
        if (_signalTrigger is not null && classified && matterId is { } classifiedMatter)
        {
            await _signalTrigger.EnqueueAsync(
                SignalTriggerKind.Classification, "sprk_matter", classifiedMatter, signal.CommunicationId, ct: ct).ConfigureAwait(false);
        }

        var request = new CommunicationRuleDecisionRequest(
            signal.CommunicationId, Tenant: null, MatterId: matterId, Confidence: signal.Confidence);

        var decision = await _gate.EvaluateAsync(request, ct).ConfigureAwait(false);

        // Ontology task 042 (FR-23): persist the decision on BOTH paths, from this one call site, before the branch. Never throws.
        await RecordDecisionAsync(signal, decision, directFiledUnderId, ct).ConfigureAwait(false);

        // FR-12 decides; FR-13 (task 042) executes. The RI action path is invoked ONLY on authorize — a DENY
        // returns here with NO seam call, NO outbox row, NO ping, NO appnotification (the short-circuit is
        // structural: nothing below runs on the deny path).
        if (decision.Authorize)
        {
            _logger.LogInformation(
                "[comms-policy] AUTHORIZED → executing RI action (FR-13) | CommunicationId: {CommunicationId}, RuleId: {RuleId}, PrivilegeFlagged: {PrivilegeFlagged}.",
                signal.CommunicationId, decision.MatchedRuleId, decision.PrivilegeFlagged);

            await _riAction.ExecuteAuthorizedActionsAsync(signal, decision, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Task 042: the gate decision's Decision Record, on the authorize AND the deny path. Never changes the gate's outcome or this
    /// consumer's behaviour: a refusal (already logged and metered by the writer under <see cref="DecisionRecordEvents.WriteRefused"/>) or
    /// any other fault is logged at Error with <see cref="DecisionRecordEvents.GateDecisionNotRecorded"/> (the ids only) and the
    /// unclassified ones are metered, so a lost record is visible in App Insights and never silent. Cancellation is not swallowed.
    /// </summary>
    private async Task RecordDecisionAsync(
        CommunicationAssessedSignal signal, CommunicationRuleDecision decision, Guid? directFiledUnderId, CancellationToken ct)
    {
        if (_decisionRecords is null)
        {
            return;
        }

        var evaluationId = Guid.NewGuid();
        try
        {
            var facts = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["correlationId"] = evaluationId,
                ["communicationId"] = signal.CommunicationId,
                ["gateOutcome"] = decision.Authorize ? "Authorized" : "Denied",
                ["gateReason"] = decision.Reason,
                ["confidence"] = decision.Confidence,
                ["threshold"] = decision.Threshold,
                ["matchedRuleId"] = decision.MatchedRuleId,
                ["matchedRulePriority"] = await ReadRulePriorityAsync(decision.MatchedRuleId, ct).ConfigureAwait(false),
                ["privilegeFlagged"] = decision.PrivilegeFlagged,
            };

            await _decisionRecords.WriteGateDecisionAsync(
                new GateDecisionRecordRequest(
                    CommunicationId: signal.CommunicationId,
                    EvaluationId: evaluationId,
                    Authorize: decision.Authorize,
                    Reason: decision.Reason,
                    ProposedActionCode: ProposedActionCode,
                    ProposedActionLabel: ProposedActionLabel,
                    Facts: facts,
                    PrivilegeFlagged: decision.PrivilegeFlagged,
                    DirectFiledUnderId: directFiledUnderId),
                ct).ConfigureAwait(false);
        }
        catch (DecisionRecordRefusedException ex)
        {
            // The writer has already logged (WriteRefused) and metered this refusal under its own reason.
            _logger.LogError(DecisionRecordEvents.GateDecisionNotRecorded,
                "[comms-policy] gate decision NOT recorded (writer refused: {Reason}) | CommunicationId: {CommunicationId}, EvaluationId: {EvaluationId}, Authorized: {Authorize}.",
                ex.Reason, signal.CommunicationId, evaluationId, decision.Authorize);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(DecisionRecordEvents.GateDecisionNotRecorded, ex,
                "[comms-policy] gate decision NOT recorded (unexpected failure) | CommunicationId: {CommunicationId}, EvaluationId: {EvaluationId}, Authorized: {Authorize}.",
                signal.CommunicationId, evaluationId, decision.Authorize);
            OntologyWriterTelemetry.RecordFailure(DecisionRecordRefusalReason.GateRecordFailed);
        }
    }

    /// <summary>The matched rule's <c>sprk_priority</c> for the fact snapshot (the gate's decision carries the rule id, not its priority).
    /// Best effort: a rule that cannot be read leaves the priority out of the snapshot and never costs the record.</summary>
    private async Task<int?> ReadRulePriorityAsync(Guid? ruleId, CancellationToken ct)
    {
        if (ruleId is not { } id || id == Guid.Empty)
        {
            return null;
        }

        try
        {
            var rule = await _entityService.RetrieveAsync("sprk_communicationrule", id, new[] { "sprk_priority" }, ct).ConfigureAwait(false);
            return rule?.GetAttributeValue<int?>("sprk_priority");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[comms-policy] could not read the priority of rule {RuleId} for the Decision Record; recording without it.", id);
            return null;
        }
    }
}
