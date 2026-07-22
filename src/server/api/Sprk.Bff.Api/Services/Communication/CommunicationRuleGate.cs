using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;

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
    string Reason);

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

    private static readonly string[] RuleColumns =
    {
        IdColumn, TenantColumn, MatterColumn, ThresholdColumn, FlagPrivilegeColumn, PriorityColumn,
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
            Reason: authorize ? "rule-matched-confidence-met" : "rule-matched-confidence-below-threshold");

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

    private static CommunicationRuleDecision Deny(
        CommunicationRuleDecisionRequest request, double threshold, bool privilege, string reason)
        => new(Authorize: false, MatchedRuleId: null, Confidence: request.Confidence,
               Threshold: threshold, PrivilegeFlagged: privilege, Reason: reason);

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
/// unchanged (that is the seam's whole point). On AUTHORIZE it does NOT execute any RI action (that is task 042's
/// job, downstream of this gate) — it logs the authorization and hands off nothing yet; the outbox write +
/// action execution land in 042. On DENY it is a logged no-op. Never calls <c>IEventRulesService.FireAsync</c>.
/// </summary>
public sealed class RuleGatedAssessedConsumer : ICommunicationAssessedProducer
{
    private const string CommunicationEntity = "sprk_communication";
    private const string RegardingMatterColumn = "sprk_regardingmatter";

    private readonly IGenericEntityService _entityService;
    private readonly CommunicationRuleGate _gate;
    private readonly ILogger<RuleGatedAssessedConsumer> _logger;

    public RuleGatedAssessedConsumer(
        IGenericEntityService entityService,
        CommunicationRuleGate gate,
        ILogger<RuleGatedAssessedConsumer> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishAsync(CommunicationAssessedSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        // Resolve the matter (regarding) for rule matching. Tenant is null today (single-tenant per environment);
        // a blank-tenant rule matches all tenants. A read failure resolves matter to null (matches all-matter rules).
        Guid? matterId = null;
        try
        {
            var comm = await _entityService
                .RetrieveAsync(CommunicationEntity, signal.CommunicationId, new[] { RegardingMatterColumn }, ct)
                .ConfigureAwait(false);
            var matter = comm?.GetAttributeValue<EntityReference>(RegardingMatterColumn);
            if (matter is not null && matter.Id != Guid.Empty)
            {
                matterId = matter.Id;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[comms-policy] could not resolve regarding matter for {CommunicationId}; matching against all-matter rules.",
                signal.CommunicationId);
        }

        var request = new CommunicationRuleDecisionRequest(
            signal.CommunicationId, Tenant: null, MatterId: matterId, Confidence: signal.Confidence);

        var decision = await _gate.EvaluateAsync(request, ct).ConfigureAwait(false);

        // FR-12: this gate DECIDES; task 042 EXECUTES on authorize. Nothing is executed here.
        if (decision.Authorize)
        {
            _logger.LogInformation(
                "[comms-policy] AUTHORIZED (RI action execution deferred to task 042) | CommunicationId: {CommunicationId}, RuleId: {RuleId}, PrivilegeFlagged: {PrivilegeFlagged}.",
                signal.CommunicationId, decision.MatchedRuleId, decision.PrivilegeFlagged);
        }
    }
}
