using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Signals.Billing;

/// <summary>
/// The extracted and resolved invoice that task 049 hands the evaluator (ebilling-r1 task 072, FR-25): the submission context
/// (044), the header as read (043) and one <see cref="BillingLineFacts"/> per line, which 049 builds from the resolution (046) and
/// coding (047) outputs. Everything the evaluator reads from Dataverse is loaded by <see cref="BillingFactLoader"/>, not passed here.
/// </summary>
public sealed record BillingEvaluationRequest
{
    /// <summary>The submission being judged (<c>sprk_invoicesubmission</c>). It becomes <see cref="BillingInvoiceFacts.SubmissionId"/>
    /// and is never one of its own priors.</summary>
    public required Guid SubmissionId { get; init; }

    /// <summary>The firm (<c>sprk_organization</c>) of the submission context: selects the firm rule sets and the priors.</summary>
    public required Guid FirmId { get; init; }

    /// <summary>The core record of the submission context (044); <c>null</c> = none.</summary>
    public CoreRecordReference? CoreRecord { get; init; }

    /// <summary>This submission's tracking number (045).</summary>
    public string? TrackingNumber { get; init; }

    public string? FirmInvoiceNumber { get; init; }

    public DateOnly? InvoiceDate { get; init; }

    /// <summary>
    /// The date the submission was received (<c>sprk_invoicesubmission</c>, task 049 supplies it as data, never a clock read). The
    /// judging date of a subject with no line date and no invoice date, so version selection stays deterministic (072 review A3).
    /// A UTC date (the received timestamp's UTC calendar day, as rule versions take effect at midnight UTC, task 076). Never
    /// <c>default</c> (0001-01-01): no version is in force on it, so every undated subject would silently be judged by nothing;
    /// <see cref="InvoiceComplianceEvaluator"/> throws <see cref="ArgumentException"/> instead (072 part W).
    /// </summary>
    public required DateOnly ReceivedOn { get; init; }

    public DateOnly? BillingPeriodStart { get; init; }

    public DateOnly? BillingPeriodEnd { get; init; }

    /// <summary>The currency as read. Never defaulted: a missing currency stays <c>null</c> (D-20).</summary>
    public string? Currency { get; init; }

    /// <summary>The combined confidence (046) of each header field, keyed by <see cref="BillingFieldNames"/>.</summary>
    public IReadOnlyDictionary<string, decimal> HeaderConfidence { get; init; } = EmptyConfidence.Instance;

    /// <summary>The lines in invoice order; <see cref="BillingLineFacts.Index"/> is the position 046 used.</summary>
    public required IReadOnlyList<BillingLineFacts> Lines { get; init; }
}

/// <summary>
/// One published billing rule version as read (task 019 store, task 076 publish): the policy, its version, its rule set's scope,
/// and the row values the gate (<see cref="PolicyVersionValidator.TryPrepareBillingRule"/>) and the Signal mapping need. Raw:
/// nothing here has passed the gate yet.
/// </summary>
public sealed record BillingRuleCandidate
{
    public required Guid PolicyId { get; init; }

    public required string PolicyCode { get; init; }

    public required Guid PolicyVersionId { get; init; }

    public required Guid RuleSetId { get; init; }

    /// <summary>Client default or Firm (<c>sprk_ruleset.sprk_scope</c>). A firm rule overrides a client-default rule of the same type (D-39).</summary>
    public required RuleSetScope Scope { get; init; }

    /// <summary><c>sprk_ruletype</c> as the gate reads it: the option value in invariant text.</summary>
    public string? RuleType { get; init; }

    public string? RuleBody { get; init; }

    public string? MessageTemplate { get; init; }

    public string? ShortHeadline { get; init; }

    public string? ClauseReference { get; init; }

    public string? ClauseLocation { get; init; }

    /// <summary><c>sprk_inforcefrom</c> as a UTC date (publish writes the effective date at midnight UTC, task 076).</summary>
    public DateOnly? InForceFrom { get; init; }

    /// <summary><c>sprk_inforceto</c> as a UTC date; set on a version that a later publish superseded.</summary>
    public DateOnly? InForceTo { get; init; }

    /// <summary>The policy's <c>sprk_currentversion</c> points at this version.</summary>
    public bool IsCurrent { get; init; }

    public int VersionNumber { get; init; }

    /// <summary><c>sprk_policy.sprk_lane</c>; <c>null</c> when the row has none.</summary>
    public int? Lane { get; init; }

    /// <summary><c>sprk_policy.sprk_severity</c>; optional.</summary>
    public int? Severity { get; init; }

    /// <summary><c>sprk_policyversion.sprk_consequence</c> (task 073 reads it).</summary>
    public int? Consequence { get; init; }

    /// <summary>
    /// A version that can ever be in force (task 076 K3): the policy's current version, or a version that carries an end date (a
    /// superseded one). Anything else is a draft, a rejected draft or an abandoned edit, and is never evaluated, whatever its
    /// <c>sprk_inforcefrom</c> says (an edit creates its version with <c>sprk_inforcefrom = UtcNow</c> and no end date).
    /// </summary>
    public bool CanBeInForce => IsCurrent || InForceTo is not null;

    /// <summary>
    /// In force on <paramref name="date"/> (task 076 K3, spec MUST): the current version from its <c>sprk_inforcefrom</c> on (the pointer
    /// alone decides, so an end date left by a failed publish does not end it); a superseded version inside
    /// <c>[sprk_inforcefrom, sprk_inforceto)</c>; never a version that is neither.
    /// </summary>
    public bool IsInForceOn(DateOnly date)
    {
        if (InForceFrom is not { } from || date < from)
        {
            return false;
        }

        if (IsCurrent)
        {
            return true;
        }

        return InForceTo is { } to && date < to;
    }
}

/// <summary>What <see cref="BillingFactLoader.LoadAsync"/> read for one submission.</summary>
public sealed record BillingLoadedFacts
{
    /// <summary>Every version of every enabled billing policy in a Published rule set that applies to the firm, and that can be in force.</summary>
    public required IReadOnlyList<BillingRuleCandidate> Rules { get; init; }

    /// <summary>The core record's dates and state; <c>null</c> when the submission has none or it was not found.</summary>
    public BillingCoreRecordFacts? CoreRecord { get; init; }

    /// <summary>The firm's earlier submissions (this one excluded), from both sides of the secure wall (D-112). Empty when no
    /// Duplicate rule can be in force.</summary>
    public IReadOnlyList<BillingPriorSubmission> PriorSubmissions { get; init; } = [];

    /// <summary>The secure roots above the judged invoice (D-112); <c>null</c> = unresolved, or not read because no Duplicate rule can be in force.</summary>
    public required IReadOnlySet<Guid>? InvoiceSecureRoots { get; init; }

    /// <summary>The eBilling reliability threshold (FR-26, default 0.90).</summary>
    public required decimal ReliabilityThreshold { get; init; }
}

/// <summary>
/// Reads, once per submission and app-only, everything the in-memory billing evaluation needs from Dataverse (ebilling-r1 task 072,
/// FR-25, design.md 6 "Fact assembly"): the published billing rules for the client default and the submission's firm, the core
/// record's dates, the firm's prior submissions with their lines for Duplicate, the secure roots of the judged invoice and of each
/// prior (D-112), and the reliability threshold. Reads only; it never writes.
/// </summary>
/// <remarks>
/// <para><b>Bounded.</b> A fixed set of queries per submission, none per line of the judged invoice: rule sets, policies, versions,
/// the core record, prior submissions and prior invoices (only when a Duplicate rule can be in force), prior lines (only when such a
/// rule names a line match field, and only of Active priors: 072 review B1), and
/// the secure-root walk (one run of <see cref="SecureChildShareSynchronizer.SecureRootsAboveEachAsync"/> for the invoice and every
/// prior). Id lists are chunked at <see cref="MaxInValues"/>; each query pages.</para>
/// <para><b>Rules.</b> <c>sprk_ruleset</c> rows with domain Billing, status Published, and scope Client default or Firm with
/// <c>sprk_firm</c> = the firm; their enabled, active billing policies; and those policies' versions that can be in force (the
/// current version or one with an end date, task 076 K3). The domain is filtered in the query and re-checked on every row
/// (<see cref="RuleDomains.IsBillingRow"/>), as task 076 does. Rules are not narrowed by core record: the store (task 019) scopes a
/// rule set to the client default or a firm only.</para>
/// <para><b>Priors (D-112, FR-36).</b> Every submission of the firm except this one, from both sides of the secure wall. A Failed
/// submission or a Cancelled invoice never reached the firm as a payable invoice and is not loaded. Status: the invoice's status when
/// the submission has one (Rejected, Returned to Firm, otherwise Active), else the submission's (Rejected, otherwise Active).</para>
/// <para><b>Secure roots (D-112; uac-r2's calling conditions on <c>SecureChildShareSynchronizer.cs:648-721</c>).</b> (1) Each prior row
/// is read with every lineage lookup of its table plus the owner columns, so no lookup is silently absent (an absent lookup would
/// shrink the prior's set and pass the subset test). (2) The rows walked are child rows, never a root table (the walk refuses
/// one): a prior's <c>sprk_invoice</c> row AND its <c>sprk_invoicesubmission</c> row, whose sets are unioned (072 review A1: a
/// lookup cleared on one is still covered by the other), or the submission row alone when it has no invoice yet. A walked row
/// owned by the Secure team whose set comes back empty makes that prior unresolved: it is secure, but its wall is unknown. The
/// judged invoice does not exist yet (049 persists after the
/// evaluation), so it is walked as an unsaved <c>sprk_invoice</c> probe carrying only the submission's core lookup (fewer lookups
/// can only shrink the invoice's set, which hides more, never less). A core record is never passed itself. (3) Resolved means
/// Status Completed and no row of the group undetermined; anything else, and any fault, is unresolved (<c>null</c>), and the
/// Duplicate finding still fires without detail. (4) NotApplicable (no Secure Record owner team) means every set is empty.
/// The core record's own <c>sprk_issecure</c> is never read: a record secure through its parent is owned by the Secure team and
/// the walk places it under its root.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="PolicyScopeResolver"/> (Ontology policy scope,
/// tenant / matter, no firm or rule-set axis) and the Ontology readers (domain allowlist excludes Billing). Extension: no; billing rule
/// selection is by rule set scope and firm, which those readers do not model. Cost of doing nothing: 049 has no facts to evaluate a
/// submission with (FR-25).</para>
/// </remarks>
public sealed class BillingFactLoader
{
    /// <summary>The most ids in one <c>In</c> condition (the same bound as task 076's reads).</summary>
    internal const int MaxInValues = 500;

    private const int PageSize = 5000;

    internal const string RuleSetEntity = "sprk_ruleset";
    internal const string PolicyEntity = "sprk_policy";
    internal const string VersionEntity = "sprk_policyversion";
    internal const string SubmissionEntity = "sprk_invoicesubmission";
    internal const string InvoiceEntity = "sprk_invoice";
    internal const string LineEntity = "sprk_billingevent";

    /// <summary>ebilling-r1 task 081: <c>sprk_ruleset.sprk_procedure</c>, set on a Procedure's rule set (not read here until task 082).</summary>
    internal const string ProcedureColumn = "sprk_procedure";

    private const int RuleSetPublished = 659490000;
    private const int StateActive = 0;
    private const string OpenedDateColumn = "sprk_openeddate";
    private const string ClosedDateColumn = "sprk_closeddate";
    private const string BillingStatusColumn = "sprk_billingstatus";
    private const int CostTypeFee = 100000000;

    private static readonly string[] OwnerColumns = ["owningteam", "owninguser"];

    private readonly IGenericEntityService _entities;
    private readonly IEbillingSettingsReader _settings;
    private readonly SecureChildShareSynchronizer _secureRoots;
    private readonly IConfiguration _configuration;
    private readonly FinanceTelemetry _telemetry;
    private readonly ILogger<BillingFactLoader> _logger;

    public BillingFactLoader(
        IGenericEntityService entities,
        IEbillingSettingsReader settings,
        SecureChildShareSynchronizer secureRoots,
        IConfiguration configuration,
        FinanceTelemetry telemetry,
        ILogger<BillingFactLoader> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _secureRoots = secureRoots ?? throw new ArgumentNullException(nameof(secureRoots));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Loads the facts for <paramref name="request"/>. A Dataverse fault propagates (the job decides), except in the
    /// secure-root walk, where it means "unresolved" (fail closed, D-112).</summary>
    public async Task<BillingLoadedFacts> LoadAsync(BillingEvaluationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SubmissionId == Guid.Empty) throw new ArgumentException("SubmissionId must not be empty.", nameof(request));
        if (request.FirmId == Guid.Empty) throw new ArgumentException("FirmId must not be empty.", nameof(request));

        var settings = await _settings.GetAsync(ct).ConfigureAwait(false);
        var rules = await LoadRulesAsync(request.FirmId, ct).ConfigureAwait(false);
        var coreRecord = request.CoreRecord is { } core ? await LoadCoreRecordAsync(core, ct).ConfigureAwait(false) : null;

        IReadOnlyList<BillingPriorSubmission> priors = [];
        IReadOnlySet<Guid>? invoiceRoots = null;
        if (rules.Any(r => IsRuleType(r, Signals.RuleType.Duplicate)))
        {
            var read = await LoadPriorsAsync(request, ComparesLines(rules), ct).ConfigureAwait(false);
            var roots = await SecureRootsAsync(request, read, ct).ConfigureAwait(false);
            invoiceRoots = roots[0];
            priors = read.Select((p, i) => p.Prior with { SecureRoots = roots[i + 1] }).ToList();
        }

        return new BillingLoadedFacts
        {
            Rules = rules,
            CoreRecord = coreRecord,
            PriorSubmissions = priors,
            InvoiceSecureRoots = invoiceRoots,
            ReliabilityThreshold = settings.ReliabilityThreshold,
        };
    }

    private static bool IsRuleType(BillingRuleCandidate candidate, RuleType type) =>
        RuleBodySchemaValidator.TryParseRuleType(candidate.RuleType, out var parsed) && parsed == type;

    // ── Rules ────────────────────────────────────────────────────────────────────────────────────────────────

    // Internal since ebilling-r1 task 102: the invoice-adjustment rule list (D-74, F-8) uses the evaluator's own selection, not a second copy.
    internal async Task<IReadOnlyList<BillingRuleCandidate>> LoadRulesAsync(Guid firmId, CancellationToken ct)
    {
        var setQuery = new QueryExpression(RuleSetEntity)
        {
            ColumnSet = new ColumnSet("sprk_scope", "sprk_firm", "statuscode", RuleDomains.PolicyDomainColumn, ProcedureColumn),
            NoLock = true,
        };
        RuleDomains.AddBillingFilter(setQuery);
        setQuery.Criteria.AddCondition("statuscode", ConditionOperator.Equal, RuleSetPublished);
        // ebilling-r1 task 081 (EB-D-148): a Procedure's rule set is in force per Procedure Version (its window, its Applies to, the rules the
        // version kept), which this set-level reader cannot honour: it would apply an "Assigned records only" Procedure to every invoice and
        // keep a dropped rule firing. Until the resolver (task 082) reads Procedure Versions, a Procedure's set is not evaluated here at all.
        setQuery.Criteria.AddCondition(ProcedureColumn, ConditionOperator.Null);
        var scope = new FilterExpression(LogicalOperator.Or);
        scope.AddCondition("sprk_scope", ConditionOperator.Equal, (int)RuleSetScope.ClientDefault);
        var firm = new FilterExpression(LogicalOperator.And);
        firm.AddCondition("sprk_scope", ConditionOperator.Equal, (int)RuleSetScope.Firm);
        firm.AddCondition("sprk_firm", ConditionOperator.Equal, firmId);
        scope.AddFilter(firm);
        setQuery.Criteria.AddFilter(scope);

        // Second layer, as task 076 does: a row the query should not have returned is dropped here.
        var sets = new Dictionary<Guid, RuleSetScope>();
        foreach (var row in await AllAsync(setQuery, ct).ConfigureAwait(false))
        {
            if (!RuleDomains.IsBillingRow(row) || row.GetAttributeValue<OptionSetValue>("statuscode")?.Value != RuleSetPublished
                || row.GetAttributeValue<EntityReference>(ProcedureColumn) is not null)
            {
                continue;
            }

            var setScope = row.GetAttributeValue<OptionSetValue>("sprk_scope")?.Value;
            if (setScope == (int)RuleSetScope.ClientDefault)
            {
                sets[row.Id] = RuleSetScope.ClientDefault;
            }
            else if (setScope == (int)RuleSetScope.Firm && row.GetAttributeValue<EntityReference>("sprk_firm")?.Id == firmId)
            {
                sets[row.Id] = RuleSetScope.Firm;
            }
        }

        if (sets.Count == 0)
        {
            return [];
        }

        var policies = new Dictionary<Guid, Entity>();
        foreach (var chunk in sets.Keys.Order().Chunk(MaxInValues))
        {
            var policyQuery = new QueryExpression(PolicyEntity)
            {
                ColumnSet = new ColumnSet(
                    "sprk_policycode", "sprk_currentversion", "sprk_ruleset", "sprk_lane", "sprk_severity", "sprk_enabled", "statecode",
                    RuleDomains.PolicyDomainColumn),
                NoLock = true,
            };
            RuleDomains.AddBillingFilter(policyQuery);
            policyQuery.Criteria.AddCondition("sprk_ruleset", ConditionOperator.In, chunk.Cast<object>().ToArray());
            policyQuery.Criteria.AddCondition("sprk_enabled", ConditionOperator.Equal, true);
            policyQuery.Criteria.AddCondition("statecode", ConditionOperator.Equal, StateActive);
            foreach (var row in await AllAsync(policyQuery, ct).ConfigureAwait(false))
            {
                if (RuleDomains.IsBillingRow(row)
                    && row.GetAttributeValue<bool?>("sprk_enabled") == true
                    && row.GetAttributeValue<OptionSetValue>("statecode")?.Value == StateActive
                    && row.GetAttributeValue<EntityReference>("sprk_ruleset") is { } set && sets.ContainsKey(set.Id)
                    && !string.IsNullOrWhiteSpace(row.GetAttributeValue<string>("sprk_policycode")))
                {
                    policies[row.Id] = row;
                }
            }
        }

        var candidates = new List<BillingRuleCandidate>();
        foreach (var chunk in policies.Keys.Order().Chunk(MaxInValues))
        {
            var versionQuery = new QueryExpression(VersionEntity)
            {
                ColumnSet = new ColumnSet(
                    "sprk_policy", "sprk_versionnumber", "sprk_ruletype", "sprk_rulebody", "sprk_messagetemplate", "sprk_shortheadline",
                    "sprk_consequence", "sprk_clausereference", "sprk_clauselocation", "sprk_inforcefrom", "sprk_inforceto"),
                NoLock = true,
            };
            versionQuery.Criteria.AddCondition("sprk_policy", ConditionOperator.In, chunk.Cast<object>().ToArray());
            foreach (var row in await AllAsync(versionQuery, ct).ConfigureAwait(false))
            {
                if (row.GetAttributeValue<EntityReference>("sprk_policy") is not { } policyRef
                    || !policies.TryGetValue(policyRef.Id, out var policy))
                {
                    continue;
                }

                var candidate = new BillingRuleCandidate
                {
                    PolicyId = policy.Id,
                    PolicyCode = policy.GetAttributeValue<string>("sprk_policycode").Trim(),
                    PolicyVersionId = row.Id,
                    RuleSetId = policy.GetAttributeValue<EntityReference>("sprk_ruleset").Id,
                    Scope = sets[policy.GetAttributeValue<EntityReference>("sprk_ruleset").Id],
                    RuleType = row.GetAttributeValue<OptionSetValue>("sprk_ruletype")?.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    RuleBody = row.GetAttributeValue<string>("sprk_rulebody"),
                    MessageTemplate = row.GetAttributeValue<string>("sprk_messagetemplate"),
                    ShortHeadline = row.GetAttributeValue<string>("sprk_shortheadline"),
                    ClauseReference = row.GetAttributeValue<string>("sprk_clausereference"),
                    ClauseLocation = row.GetAttributeValue<string>("sprk_clauselocation"),
                    InForceFrom = UtcDate(row.GetAttributeValue<DateTime?>("sprk_inforcefrom")),
                    InForceTo = UtcDate(row.GetAttributeValue<DateTime?>("sprk_inforceto")),
                    IsCurrent = policy.GetAttributeValue<EntityReference>("sprk_currentversion")?.Id == row.Id,
                    VersionNumber = row.GetAttributeValue<int?>("sprk_versionnumber") ?? 0,
                    Lane = policy.GetAttributeValue<OptionSetValue>("sprk_lane")?.Value,
                    Severity = policy.GetAttributeValue<OptionSetValue>("sprk_severity")?.Value,
                    Consequence = row.GetAttributeValue<OptionSetValue>("sprk_consequence")?.Value,
                };

                // K3 (task 076): a draft, a rejected draft or an abandoned edit is never in force; it is not even returned.
                if (candidate.CanBeInForce)
                {
                    candidates.Add(candidate);
                }
            }
        }

        return candidates
            .OrderBy(c => c.PolicyCode, StringComparer.Ordinal)
            .ThenBy(c => c.PolicyId)
            .ThenBy(c => c.VersionNumber)
            .ThenBy(c => c.PolicyVersionId)
            .ToList();
    }

    // ── Core record ──────────────────────────────────────────────────────────────────────────────────────────

    private async Task<BillingCoreRecordFacts?> LoadCoreRecordAsync(CoreRecordReference core, CancellationToken ct)
    {
        // D-116: the three tables carry the same columns (sprk_workassignment gained its dates with the billing status; verified live
        // 2026-10-18).
        var entity = core.Kind switch
        {
            CoreRecordKind.Matter => "sprk_matter",
            CoreRecordKind.Project => "sprk_project",
            CoreRecordKind.WorkAssignment => "sprk_workassignment",
            _ => throw new ArgumentOutOfRangeException(nameof(core), core.Kind, "Unknown core record kind."),
        };

        var query = new QueryExpression(entity)
        {
            ColumnSet = new ColumnSet("statecode", OpenedDateColumn, ClosedDateColumn, BillingStatusColumn),
            TopCount = 1,
            NoLock = true,
        };
        query.Criteria.AddCondition(entity + "id", ConditionOperator.Equal, core.Id);
        var row = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        if (row is null)
        {
            _logger.LogWarning(
                "Billing evaluation: core record {CoreEntity} {CoreId} was not found; CoreRecordWindow judges nothing for this submission.",
                entity, core.Id);
            return null;
        }

        var billingStatus = row.GetAttributeValue<OptionSetValue>(BillingStatusColumn)?.Value;
        if (billingStatus is { } status && !CoreRecordBillingStatus.All.Contains(status))
        {
            _logger.LogWarning(
                "Billing evaluation: core record {CoreEntity} {CoreId} has sprk_billingstatus {BillingStatus}, which is not Open, Final or Closed; CoreRecordWindow judges it as having no billing status (D-111).",
                entity, core.Id, status);
        }

        return new BillingCoreRecordFacts
        {
            OpenDate = DateOf(row.GetAttributeValue<DateTime?>(OpenedDateColumn)),
            CloseDate = DateOf(row.GetAttributeValue<DateTime?>(ClosedDateColumn)),
            // Closed = the record is inactive (statecode 1), whether or not it has a close date (D-111; read only with no billing status).
            IsClosed = row.GetAttributeValue<OptionSetValue>("statecode")?.Value is { } state && state != StateActive,
            BillingStatus = billingStatus,
        };
    }

    // ── Priors (Duplicate, FR-36, D-112) ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A prior as read, with the child rows whose lineage gives its secure roots (072 review A1): its <c>sprk_invoice</c> row and its
    /// <c>sprk_invoicesubmission</c> row when it has an invoice, else the submission row alone. Their roots are unioned, so a lookup
    /// cleared on one row (a grid edit) is still covered by the other.
    /// </summary>
    private sealed record PriorRead(BillingPriorSubmission Prior, IReadOnlyList<(Entity Row, ColumnSet ReadWith)> Rows);

    /// <summary>
    /// 072 review B1: true when a Duplicate version that can be in force names a line match field (<see cref="DuplicateHandler"/>'s
    /// line vocabulary), so prior lines are worth reading. A superset of the versions the evaluator selects (it does not narrow by
    /// date or precedence): reading lines a selected rule ignores costs a query, missing lines one needs would miss duplicates. A body
    /// that does not parse counts as naming one (the gate refuses it anyway; this only decides whether to read).
    /// </summary>
    internal static bool ComparesLines(IEnumerable<BillingRuleCandidate> rules) =>
        rules.Any(r => IsRuleType(r, Signals.RuleType.Duplicate) && NamesALineMatchField(r.RuleBody));

    private static bool NamesALineMatchField(string? body)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body ?? string.Empty);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object
                || !document.RootElement.TryGetProperty("matchFields", out var fields)
                || fields.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return true;
            }

            return fields.EnumerateArray().Any(f => f.ValueKind == System.Text.Json.JsonValueKind.String
                && Handlers.DuplicateHandler.LineFieldOrder.Contains(f.GetString(), StringComparer.Ordinal));
        }
        catch (System.Text.Json.JsonException)
        {
            return true;
        }
    }

    private async Task<List<PriorRead>> LoadPriorsAsync(BillingEvaluationRequest request, bool readLines, CancellationToken ct)
    {
        var submissionColumns = WalkColumns(SubmissionEntity)
            .Concat(["sprk_trackingnumber", "sprk_submissionsequence", "sprk_firminvoicenumber", "sprk_processingstatus"])
            .Distinct(StringComparer.Ordinal).ToArray();
        var submissionQuery = new QueryExpression(SubmissionEntity) { ColumnSet = new ColumnSet(submissionColumns), NoLock = true };
        submissionQuery.Criteria.AddCondition("sprk_vendororg", ConditionOperator.Equal, request.FirmId);

        // D-112: every partition. The only exclusions are this submission and attempts that never became a payable invoice.
        var submissions = (await AllAsync(submissionQuery, ct).ConfigureAwait(false))
            .Where(s => s.Id != request.SubmissionId
                && s.GetAttributeValue<OptionSetValue>("sprk_processingstatus")?.Value != (int)InvoiceSubmissionStatus.Failed)
            .OrderBy(s => s.Id)
            .ToList();
        if (submissions.Count == 0)
        {
            return [];
        }

        var invoiceColumns = WalkColumns(InvoiceEntity).Append("statuscode").Distinct(StringComparer.Ordinal).ToArray();
        var invoiceIds = submissions
            .Select(s => s.GetAttributeValue<EntityReference>("sprk_invoice")?.Id)
            .OfType<Guid>().Where(id => id != Guid.Empty).Distinct().Order().ToList();
        var invoices = new Dictionary<Guid, Entity>();
        foreach (var chunk in invoiceIds.Chunk(MaxInValues))
        {
            var query = new QueryExpression(InvoiceEntity) { ColumnSet = new ColumnSet(invoiceColumns), NoLock = true };
            query.Criteria.AddCondition("sprk_invoiceid", ConditionOperator.In, chunk.Cast<object>().ToArray());
            foreach (var row in await AllAsync(query, ct).ConfigureAwait(false))
            {
                invoices[row.Id] = row;
            }
        }

        var kept = new List<(Entity Submission, Entity? Invoice, BillingPriorStatus Status)>();
        foreach (var submission in submissions)
        {
            var invoice = submission.GetAttributeValue<EntityReference>("sprk_invoice") is { } link && invoices.TryGetValue(link.Id, out var found)
                ? found
                : null;
            if (StatusOf(submission, invoice) is { } status)
            {
                kept.Add((submission, invoice, status)); // a Cancelled invoice never reached the firm as a payable one
            }
        }

        // 072 review B1: lines only when a Duplicate rule compares lines, and only of Active priors (the only ones Duplicate compares).
        var lines = readLines
            ? await LoadPriorLinesAsync(
                kept.Where(k => k.Status == BillingPriorStatus.Active && k.Invoice is not null)
                    .Select(k => k.Invoice!.Id).Distinct().Order().ToList(), ct).ConfigureAwait(false)
            : new Dictionary<Guid, IReadOnlyList<BillingPriorLine>>();

        var priors = new List<PriorRead>();
        foreach (var (submission, invoice, status) in kept)
        {
            var prior = new BillingPriorSubmission
            {
                SubmissionId = submission.Id,
                // None allocated: empty, and Duplicate cites such a prior with the generic withheld text, never "duplicates ." (B4).
                TrackingNumber = submission.GetAttributeValue<string>("sprk_trackingnumber") ?? string.Empty,
                Sequence = submission.GetAttributeValue<int?>("sprk_submissionsequence") ?? 0,
                FirmInvoiceNumber = submission.GetAttributeValue<string>("sprk_firminvoicenumber"),
                InvoiceId = submission.GetAttributeValue<EntityReference>("sprk_invoice")?.Id is { } priorInvoiceId && priorInvoiceId != Guid.Empty
                    ? priorInvoiceId
                    : null,
                Status = status,
                SecureRoots = null, // set from the walk below; unresolved until then
                Lines = invoice is not null && lines.TryGetValue(invoice.Id, out var priorLines) ? priorLines : [],
            };

            // uac-r2 condition (2): child rows only. A1: the invoice AND the submission, so their roots are unioned.
            var submissionRead = (submission, new ColumnSet(submissionColumns));
            priors.Add(invoice is not null
                ? new PriorRead(prior, [(invoice, new ColumnSet(invoiceColumns)), submissionRead])
                : new PriorRead(prior, [submissionRead]));
        }

        return priors;
    }

    private static BillingPriorStatus? StatusOf(Entity submission, Entity? invoice)
    {
        if (invoice?.GetAttributeValue<OptionSetValue>("statuscode")?.Value is { } invoiceStatus)
        {
            return invoiceStatus switch
            {
                InvoiceStatusCodes.Cancelled => null,
                // Task 097 (ontology-r1 088 gate F1): a corrected invoice keeps the firm's number (D-37), so the invoice it replaced must not
                // count as an earlier use of that number. Like Cancelled, it is no prior at all.
                InvoiceStatusCodes.Superseded => null,
                InvoiceStatusCodes.Rejected => BillingPriorStatus.Rejected,
                InvoiceStatusCodes.ReturnedToFirm => BillingPriorStatus.ReturnedToFirm,
                _ => BillingPriorStatus.Active,
            };
        }

        return submission.GetAttributeValue<OptionSetValue>("sprk_processingstatus")?.Value == (int)InvoiceSubmissionStatus.Rejected
            ? BillingPriorStatus.Rejected
            : BillingPriorStatus.Active;
    }

    private async Task<Dictionary<Guid, IReadOnlyList<BillingPriorLine>>> LoadPriorLinesAsync(IReadOnlyList<Guid> invoiceIds, CancellationToken ct)
    {
        var rows = new List<Entity>();
        foreach (var chunk in invoiceIds.Chunk(MaxInValues))
        {
            var query = new QueryExpression(LineEntity)
            {
                ColumnSet = new ColumnSet(
                    "sprk_invoice", "sprk_eventdate", "sprk_timekeeper", "sprk_resolvedtimekeeper", "sprk_quantity", "sprk_description",
                    "sprk_amount", "sprk_costtype", "sprk_linesequence", "statecode"),
                NoLock = true,
            };
            query.Criteria.AddCondition("sprk_invoice", ConditionOperator.In, chunk.Cast<object>().ToArray());
            // ebilling-r1 task 102 (NFR-09): a removed invoice-level adjustment is deactivated, not deleted, so it is not a prior line.
            rows.AddRange((await AllAsync(query, ct).ConfigureAwait(false)).Where(Finance.InvoiceTotalsService.IsCounted));
        }

        return rows
            .Where(r => r.GetAttributeValue<EntityReference>("sprk_invoice") is not null)
            .GroupBy(r => r.GetAttributeValue<EntityReference>("sprk_invoice").Id)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<BillingPriorLine>)g
                    .OrderBy(r => r.GetAttributeValue<int?>("sprk_linesequence") ?? int.MaxValue)
                    .ThenBy(r => r.Id)
                    .Select(ToPriorLine)
                    .ToList());
    }

    private static BillingPriorLine ToPriorLine(Entity row)
    {
        // Hours are a fee line's quantity; an expense line's quantity is a count of items, not hours.
        var isFee = row.GetAttributeValue<OptionSetValue>("sprk_costtype")?.Value == CostTypeFee;
        return new BillingPriorLine
        {
            Date = DateOf(row.GetAttributeValue<DateTime?>("sprk_eventdate")),
            TimekeeperName = row.GetAttributeValue<string>("sprk_timekeeper"),
            TimekeeperId = row.GetAttributeValue<EntityReference>("sprk_resolvedtimekeeper")?.Id,
            Hours = isFee ? DecimalOf(row, "sprk_quantity") : null,
            Narrative = row.GetAttributeValue<string>("sprk_description"),
            Amount = DecimalOf(row, "sprk_amount"),
        };
    }

    // ── Secure roots (D-112) ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The secure-root set of the judged invoice (index 0) and of each prior (index i + 1): resolved, or <c>null</c>. One walk for all
    /// of them. Every failure fails closed to <c>null</c> for every set, so no detail is shown.
    /// </summary>
    private async Task<IReadOnlySet<Guid>?[]> SecureRootsAsync(BillingEvaluationRequest request, List<PriorRead> priors, CancellationToken ct)
    {
        var answers = new IReadOnlySet<Guid>?[priors.Count + 1];
        try
        {
            var (teamId, refusal) = await SecureChildShareSynchronizer
                .ResolveSecureOwnerTeamAsync(_entities, _configuration, ct).ConfigureAwait(false);
            if (refusal is not null)
            {
                // uac-r2 condition (3): an ambiguous Secure team (Status Failed) is unresolved.
                _logger.LogWarning(
                    "Billing evaluation of submission {SubmissionId}: the Secure Record owner team is ambiguous ({Refusal}); every secure-root set is unresolved and Duplicate shows no prior's detail.",
                    request.SubmissionId, refusal);
                return answers;
            }

            if (teamId is not { } secureTeam)
            {
                // uac-r2 condition (4): NotApplicable, no Secure Record owner team, so no record is secure.
                var empty = new HashSet<Guid>();
                Array.Fill(answers, empty);
                return answers;
            }

            // One walk, one group per row, so each row's own answer is known (A1 (b)); a prior's set is the union of its rows'
            // sets, which is what walking its rows as one group returns.
            var probe = InvoiceProbe(request);
            var groups = new List<IReadOnlyCollection<Entity>> { new[] { probe } };
            groups.AddRange(priors.SelectMany(p => p.Rows).Select(r => (IReadOnlyCollection<Entity>)new[] { r.Row }));
            var readRows = priors.SelectMany(p => p.Rows).ToList();
            var walked = await _secureRoots.SecureRootsAboveEachAsync(groups, secureTeam, ct, readRows).ConfigureAwait(false);
            if (walked.Count != groups.Count)
            {
                throw new InvalidOperationException($"The secure-root walk answered {walked.Count} groups for {groups.Count}.");
            }

            answers[0] = Resolved(walked[0], probe, secureTeam);
            var next = 1;
            for (var i = 0; i < priors.Count; i++)
            {
                var sets = priors[i].Rows.Select(r => Resolved(walked[next++], r.Row, secureTeam)).ToList();
                answers[i + 1] = sets.Any(s => s is null) ? null : sets.SelectMany(s => s!).ToHashSet();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // uac-r2 condition (3): a thrown Dataverse fault is unresolved; the Duplicate finding still fires. 072 review B3: an
            // Error and a metric, not a Warning: it is a fault (Dataverse or walker), and while it lasts no prior's detail is shown.
            _telemetry.RecordBillingSecureRootWalkFault();
            _logger.LogError(
                ex,
                "Billing evaluation of submission {SubmissionId}: the secure roots could not be read; every set is unresolved and Duplicate shows no prior's detail.",
                request.SubmissionId);
            Array.Clear(answers);
        }

        return answers;
    }

    /// <summary>
    /// One walked row's set. uac-r2 condition (3): resolved only when the walk Completed and the row is not undetermined. 072 review
    /// A1 (b): a row owned by the Secure team is secure by decision of the ownership resolver, so if its lookups reach no secure
    /// root (a lookup cleared after filing, e.g. by a grid edit) its true wall is unknown: unresolved, never the ordinary empty set.
    /// </summary>
    private static IReadOnlySet<Guid>? Resolved(SecureRootsAbove above, Entity row, Guid secureTeam)
    {
        if (above.Status != SecureChildShareSyncStatus.Completed || above.Undetermined.Count > 0)
        {
            return null;
        }

        if (above.UndeterminedRowRefs.Any(u =>
                string.Equals(u.Table, row.LogicalName, StringComparison.OrdinalIgnoreCase) && u.Id == row.Id))
        {
            return null;
        }

        if (above.Roots.Count == 0 && row.GetAttributeValue<EntityReference>("owningteam")?.Id == secureTeam)
        {
            return null;
        }

        return above.Roots.Select(r => r.Id).ToHashSet();
    }

    /// <summary>
    /// The judged invoice as the unsaved <c>sprk_invoice</c> row 049 will create: only the submission's core lookup, which is how
    /// that row will be filed (the lookup names are <c>SecureChildLineage</c>'s for <c>sprk_invoice</c>). Its id is the submission's,
    /// so it never collides with a prior's row. No core record: no lookup, so no secure root.
    /// </summary>
    private static Entity InvoiceProbe(BillingEvaluationRequest request)
    {
        // probe, never created: the walk reads its lookups only (uac-r2: the rows' own owners do not matter)
        var probe = new Entity(InvoiceEntity, request.SubmissionId);
        if (request.CoreRecord is { } core)
        {
            var (column, table) = core.Kind switch
            {
                CoreRecordKind.Matter => ("sprk_matter", "sprk_matter"),
                CoreRecordKind.Project => ("sprk_project", "sprk_project"),
                CoreRecordKind.WorkAssignment => ("sprk_regardingworkassignment", "sprk_workassignment"),
                _ => throw new ArgumentOutOfRangeException(nameof(request), core.Kind, "Unknown core record kind."),
            };
            probe[column] = new EntityReference(table, core.Id);
        }

        return probe;
    }

    /// <summary>uac-r2 condition (1): every lineage lookup of the table plus the owner columns, so none counts as absent by omission.</summary>
    internal static string[] WalkColumns(string table) =>
        SecureChildLineage.Children[table].Lookups.Keys.Order(StringComparer.Ordinal).Concat(OwnerColumns).ToArray();

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<List<Entity>> AllAsync(QueryExpression query, CancellationToken ct)
    {
        query.PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 };
        var rows = new List<Entity>();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            rows.AddRange(page.Entities);
            if (!page.MoreRecords)
            {
                return rows;
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
    }

    private static decimal? DecimalOf(Entity row, string column) => row.Attributes.TryGetValue(column, out var value)
        ? value switch
        {
            Money money => money.Value,
            decimal number => number,
            _ => null,
        }
        : null;

    /// <summary>A date-only column as stored (the SDK returns it at midnight, kind Unspecified).</summary>
    private static DateOnly? DateOf(DateTime? value) => value is { } v ? DateOnly.FromDateTime(v) : null;

    /// <summary>An instant column as its UTC date (task 076 writes the effective date at midnight UTC).</summary>
    private static DateOnly? UtcDate(DateTime? value) => value is { } v
        ? DateOnly.FromDateTime(v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v)
        : null;
}
