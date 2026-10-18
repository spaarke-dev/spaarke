using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;

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
/// the core record, prior submissions, prior invoices and prior lines (the last three only when a Duplicate rule can be in force), and
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
/// shrink the prior's set and pass the subset test). (2) The rows walked are child rows: a prior's <c>sprk_invoice</c> row, or its
/// <c>sprk_invoicesubmission</c> row when it has no invoice yet; the judged invoice does not exist yet (049 persists after the
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

    private const int RuleSetPublished = 659490000;
    private const int StateActive = 0;
    private const int CostTypeFee = 100000000;

    private static readonly string[] OwnerColumns = ["owningteam", "owninguser"];

    private readonly IGenericEntityService _entities;
    private readonly IEbillingSettingsReader _settings;
    private readonly SecureChildShareSynchronizer _secureRoots;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BillingFactLoader> _logger;

    public BillingFactLoader(
        IGenericEntityService entities,
        IEbillingSettingsReader settings,
        SecureChildShareSynchronizer secureRoots,
        IConfiguration configuration,
        ILogger<BillingFactLoader> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _secureRoots = secureRoots ?? throw new ArgumentNullException(nameof(secureRoots));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
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
            var read = await LoadPriorsAsync(request, ct).ConfigureAwait(false);
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

    private async Task<IReadOnlyList<BillingRuleCandidate>> LoadRulesAsync(Guid firmId, CancellationToken ct)
    {
        var setQuery = new QueryExpression(RuleSetEntity)
        {
            ColumnSet = new ColumnSet("sprk_scope", "sprk_firm", "statuscode", RuleDomains.PolicyDomainColumn),
            NoLock = true,
        };
        RuleDomains.AddBillingFilter(setQuery);
        setQuery.Criteria.AddCondition("statuscode", ConditionOperator.Equal, RuleSetPublished);
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
            if (!RuleDomains.IsBillingRow(row) || row.GetAttributeValue<OptionSetValue>("statuscode")?.Value != RuleSetPublished)
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
        var (entity, opened, closed) = core.Kind switch
        {
            CoreRecordKind.Matter => ("sprk_matter", "sprk_openeddate", "sprk_closeddate"),
            CoreRecordKind.Project => ("sprk_project", "sprk_openeddate", "sprk_closeddate"),
            // A work assignment has no open or close date column (InvoiceContextService): only its state is judged.
            CoreRecordKind.WorkAssignment => ("sprk_workassignment", (string?)null, (string?)null),
            _ => throw new ArgumentOutOfRangeException(nameof(core), core.Kind, "Unknown core record kind."),
        };

        var columns = new List<string> { "statecode" };
        if (opened is not null) columns.Add(opened);
        if (closed is not null) columns.Add(closed);
        var query = new QueryExpression(entity) { ColumnSet = new ColumnSet(columns.ToArray()), TopCount = 1, NoLock = true };
        query.Criteria.AddCondition(entity + "id", ConditionOperator.Equal, core.Id);
        var row = (await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        if (row is null)
        {
            _logger.LogWarning(
                "Billing evaluation: core record {CoreEntity} {CoreId} was not found; CoreRecordWindow judges nothing for this submission.",
                entity, core.Id);
            return null;
        }

        return new BillingCoreRecordFacts
        {
            OpenDate = opened is null ? null : DateOf(row.GetAttributeValue<DateTime?>(opened)),
            CloseDate = closed is null ? null : DateOf(row.GetAttributeValue<DateTime?>(closed)),
            // Closed = the record is inactive (statecode 1), whether or not it has a close date (D-111).
            IsClosed = row.GetAttributeValue<OptionSetValue>("statecode")?.Value is { } state && state != StateActive,
        };
    }

    // ── Priors (Duplicate, FR-36, D-112) ─────────────────────────────────────────────────────────────────────

    /// <summary>A prior as read, with the row whose lineage gives its secure roots.</summary>
    private sealed record PriorRead(BillingPriorSubmission Prior, Entity LineageRow, ColumnSet ReadWith);

    private async Task<List<PriorRead>> LoadPriorsAsync(BillingEvaluationRequest request, CancellationToken ct)
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

        var lines = await LoadPriorLinesAsync(invoices.Keys.Order().ToList(), ct).ConfigureAwait(false);

        var priors = new List<PriorRead>();
        foreach (var submission in submissions)
        {
            var invoice = submission.GetAttributeValue<EntityReference>("sprk_invoice") is { } link && invoices.TryGetValue(link.Id, out var found)
                ? found
                : null;
            if (StatusOf(submission, invoice) is not { } status)
            {
                continue; // a Cancelled invoice never reached the firm as a payable one
            }

            var prior = new BillingPriorSubmission
            {
                SubmissionId = submission.Id,
                TrackingNumber = submission.GetAttributeValue<string>("sprk_trackingnumber") ?? string.Empty,
                Sequence = submission.GetAttributeValue<int?>("sprk_submissionsequence") ?? 0,
                FirmInvoiceNumber = submission.GetAttributeValue<string>("sprk_firminvoicenumber"),
                Status = status,
                SecureRoots = null, // set from the walk below; unresolved until then
                Lines = invoice is not null && lines.TryGetValue(invoice.Id, out var priorLines) ? priorLines : [],
            };

            // uac-r2 condition (2): the child row in the lineage. The invoice when there is one, else the submission row.
            priors.Add(invoice is not null
                ? new PriorRead(prior, invoice, new ColumnSet(invoiceColumns))
                : new PriorRead(prior, submission, new ColumnSet(submissionColumns)));
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
                    "sprk_amount", "sprk_costtype", "sprk_linesequence"),
                NoLock = true,
            };
            query.Criteria.AddCondition("sprk_invoice", ConditionOperator.In, chunk.Cast<object>().ToArray());
            rows.AddRange(await AllAsync(query, ct).ConfigureAwait(false));
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

            var groups = new List<IReadOnlyCollection<Entity>> { new[] { InvoiceProbe(request) } };
            groups.AddRange(priors.Select(p => (IReadOnlyCollection<Entity>)new[] { p.LineageRow }));
            var readRows = priors.Select(p => (p.LineageRow, p.ReadWith)).ToList();
            var walked = await _secureRoots.SecureRootsAboveEachAsync(groups, secureTeam, ct, readRows).ConfigureAwait(false);
            for (var i = 0; i < walked.Count && i < answers.Length; i++)
            {
                answers[i] = Resolved(walked[i], groups[i]);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // uac-r2 condition (3): a thrown Dataverse fault is unresolved; the Duplicate finding still fires.
            _logger.LogWarning(
                ex,
                "Billing evaluation of submission {SubmissionId}: the secure roots could not be read; every set is unresolved and Duplicate shows no prior's detail.",
                request.SubmissionId);
            Array.Clear(answers);
        }

        return answers;
    }

    /// <summary>uac-r2 condition (3): resolved only when the walk Completed and no row of the group is undetermined.</summary>
    private static IReadOnlySet<Guid>? Resolved(SecureRootsAbove above, IReadOnlyCollection<Entity> group)
    {
        if (above.Status != SecureChildShareSyncStatus.Completed || above.Undetermined.Count > 0)
        {
            return null;
        }

        if (group.Any(row => above.UndeterminedRowRefs.Any(u =>
                string.Equals(u.Table, row.LogicalName, StringComparison.OrdinalIgnoreCase) && u.Id == row.Id)))
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
