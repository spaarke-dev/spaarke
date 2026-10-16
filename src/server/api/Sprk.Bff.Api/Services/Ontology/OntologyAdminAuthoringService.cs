using System.Collections.Frozen;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ontology;

/// <summary>
/// The rule-authoring half of the Ontology admin tab (ontology platform R1 task 101, D-22 slice b, FR-58): the authorable condition
/// catalog, the dry run, save a new rule (Off), and publish a new version (supersede and re-raise).
/// </summary>
/// <remarks>
/// <para><b>Two identities, never mixed (D-22, #39, C-11).</b> Every Dataverse read the admin acts on and every write runs AS THE
/// CALLER through <see cref="IDataverseUserClient"/>, so Dataverse enforces the Spaarke Ontology Administrator role on each write and
/// the audit names the human; the capability probe (task 104) is never consulted. The ONE exception is evaluation: the dry run builds
/// the policy IN MEMORY from the caller's own read plus the draft body and evaluates it through <see cref="SignalEvaluator"/> as the
/// evaluator principal, because as the admin a <c>notExists</c> clause is true for every matter. Before anything leaves, every row is
/// re-read as the caller (its subject and its core record) and dropped unless both come back, so the dry run never shows, names or
/// counts a row the caller cannot read. The publish then hands only a policy id the caller's read admitted to the job's single-policy
/// pass (task 033), which writes Signals as the writer identity (ADR-028 path A).</para>
/// <para><b>Immutability (D-14).</b> No route updates a version's body, type, template or plan: the only write to an existing
/// <c>sprk_policyversion</c> is <see cref="StampInForceToAsync"/>, whose JSON holds <c>sprk_inforceto</c> and nothing else.</para>
/// <para><b>Publish order (task 033 contract).</b> Create the new version, stamp the OLD version's <c>sprk_inforceto</c>, move
/// <c>sprk_currentversion</c>, then <see cref="SignalReevaluationJob.ReevaluatePolicyAsync"/> once and check its Success. The stamp and
/// the pointer move are conditional on the row versions read first, so a concurrent publish or edit is refused (409), and a failed
/// pointer move is compensated (the old stamp undone, the new version marked never in force) so no current version is left stamped.</para>
/// <para><b>Dry run before publish (v4).</b> A dry run records, per caller and per policy, a SHA-256 of the exact rule type, body and
/// template it ran; save and publish refuse a body without one (409). Any edit changes the hash.</para>
/// <para><b>Domain (ebilling-r1 task 070, #1911).</b> Every read of <c>sprk_policy</c> carries the Ontology domain condition
/// (<see cref="OntologyDomain"/>), checked before any write, so a Billing or Docketing policy is the uniform 404 with zero writes.</para>
/// <para>§11: <i>Existing</i> — validation (<see cref="PolicyVersionValidator.ValidateForSave"/>), plan resolution
/// (<see cref="DecisionPlanService.Resolve"/>), evaluation and closure exist; no BFF write path for <c>sprk_policyversion</c> does
/// (022-progress). <i>Extension</i> — all of those are reused as is; only the orchestration and the caller-side reads are new.
/// <i>Cost of doing nothing</i> — no rule can be authored, tested or published from the Console (D-22 slice b).</para>
/// </remarks>
public sealed partial class OntologyAdminAuthoringService
{
    internal const string ReasonCallerUnresolved = "sdap.access.deny.caller_unresolved";
    internal const string ReasonRefused = "ontology.admin.refused";
    internal const string ReasonNotFound = "ontology.admin.not_found";
    internal const string ReasonDryRunRequired = "ontology.admin.dry_run_required";
    internal const string ReasonPolicyCodeTaken = "ontology.admin.policy_code_taken";
    internal const string ReasonPolicyRetired = "ontology.admin.policy_retired";
    internal const string ReasonPolicyChanged = "ontology.admin.policy_changed";
    internal const string ReasonUnavailable = "ontology.admin.unavailable";

    internal const string PolicyEntitySet = "sprk_policies";
    internal const string VersionEntitySet = "sprk_policyversions";
    internal const string SignalEntitySet = "sprk_signals";
    internal const string TriageEntitySet = "sprk_triagecategories";
    private const string ApiRoot = "/api/data/v9.2/";

    /// <summary>The communication column a triage category is named in (the catalog offers the categories for it).</summary>
    internal const string TriageEntity = "sprk_communication";
    internal const string TriageField = "sprk_triagecategory";

    /// <summary>The <c>sprk_policy.sprk_subjecttype</c> option per rule subject (live spaarkedev1 option set, 2026-10-16). A subject with
    /// no option cannot be saved as a policy, so the catalog does not offer it (it stays a valid clause table through a join).</summary>
    internal static readonly FrozenDictionary<string, int> SubjectTypeBySubject = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["sprk_matter"] = 100000000,
        ["sprk_communication"] = 100000001,
        ["sprk_project"] = 100000002,
        ["sprk_workassignment"] = 100000004,
        ["sprk_todo"] = 100000005,
        ["sprk_event"] = 100000006,
        ["sprk_invoice"] = 100000007,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    internal const int DefaultPriority = 500;
    internal const int MaxPriority = 999999;
    internal const int MaxCodeLength = 50;
    internal const int MaxNameLength = 200;
    internal const int MaxShortNameLength = 100;
    internal const int MaxShortHeadlineLength = 200;
    internal const int MaxProposedActionLength = 400;
    internal const int MaxMessageTemplateLength = 2000;
    internal const int MaxDecisionPlanLength = 100000;

    /// <summary>Ids per batched caller read (as the worklist read).</summary>
    internal const int IdChunk = 100;

    /// <summary>Pages of Signals read for one policy before the dry run refuses (503) rather than count a partial set.</summary>
    internal const int MaxSignalPages = 20;

    internal const string StatusOpen = "open";
    internal const string StatusSuppressed = "suppressed";
    internal const string StatusNew = "new";

    internal const string ReevaluationCompleted = "completed";
    internal const string ReevaluationFailed = "failed";
    internal const string ReevaluationNotEvaluated = "notEvaluated";

    /// <summary>The tenant-cache resource holding the dry-run proofs, the payload version, and how long a proof lasts.</summary>
    internal const string DryRunProofResource = "ontology-admin-dryrun";
    internal const int DryRunProofVersion = 1;
    internal static readonly TimeSpan DryRunProofTtl = TimeSpan.FromHours(1);

    /// <summary>The policy code an in-memory dry run of a NEW rule carries (it is never written anywhere).</summary>
    internal const string DryRunPolicyCode = "DRY-RUN";

    private readonly IDataverseUserClient _user;
    private readonly PolicyVersionValidator _validator;
    private readonly IOntologyAdminAuthoringSignalEngine _engine;
    private readonly ITenantCache _cache;
    private readonly TimeProvider _clock;
    private readonly ILogger<OntologyAdminAuthoringService> _logger;

    public OntologyAdminAuthoringService(
        IDataverseUserClient user,
        PolicyVersionValidator validator,
        IOntologyAdminAuthoringSignalEngine engine,
        ITenantCache cache,
        TimeProvider clock,
        ILogger<OntologyAdminAuthoringService> logger)
    {
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // =================================================================================================================
    // The domain allow-list: ONE helper.
    // =================================================================================================================

    /// <summary>
    /// The Ontology domain allow-list for <c>sprk_policy</c>: <c>sprk_domain</c> empty or Ontology (100000001). Every policy read in
    /// this service carries <see cref="Filter"/>, and <see cref="Admits"/> re-checks the row it returns; a new policy is created with
    /// <see cref="Value"/>.
    /// </summary>
    // Swap to RuleDomains (ebilling-r1 task 070, #1911) when it lands
    private static class OntologyDomain
    {
        /// <summary>The live <c>sprk_policy.sprk_domain</c> option "Ontology" (spaarkedev1, 2026-10-16: Billing 100000000,
        /// Ontology 100000001, Docketing 100000002).</summary>
        public const int Value = 100000001;

        public const string Column = "sprk_domain";

        public static string Filter => $"({Column} eq null or {Column} eq {Value.ToString(CultureInfo.InvariantCulture)})";

        public static bool Admits(JsonElement row) =>
            !row.TryGetProperty(Column, out var domain)
            || domain.ValueKind == JsonValueKind.Null
            || (domain.ValueKind == JsonValueKind.Number && domain.TryGetInt32(out var value) && value == Value);
    }

    // =================================================================================================================
    // Catalog (#46)
    // =================================================================================================================

    /// <summary>
    /// The authorable condition catalog: subjects, joins, fields, operators and the relative-date grammar read from
    /// <see cref="PredicateCompiler"/>'s own lists, and the triage categories by name, read as the caller. No hand-maintained word list.
    /// </summary>
    public async Task<OntologyAdminAuthoringResult<OntologyAuthoringCatalog>> GetCatalogAsync(CancellationToken ct)
    {
        try
        {
            var caller = await ResolveCallerAsync(ct).ConfigureAwait(false);
            if (caller.Failure is { } failure)
            {
                return Fail<OntologyAuthoringCatalog>(failure);
            }

            var categories = await ReadTriageCategoriesAsync(ct).ConfigureAwait(false);
            if (categories.Failure is { } readFailure)
            {
                return Fail<OntologyAuthoringCatalog>(readFailure);
            }

            return new(OntologyAdminAuthoringOutcome.Ok, BuildCatalog(categories.Value!));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Fault<OntologyAuthoringCatalog>("catalog", ex);
        }
    }

    /// <summary>The compiler-derived part of the catalog (pure, so a test can compile every entry it offers).</summary>
    internal static OntologyAuthoringCatalog BuildCatalog(IReadOnlyList<OntologyCatalogCategory> categories)
    {
        static IReadOnlyList<string> DateOnlyOf(string entity) =>
            PredicateCompiler.DateOnlyColumns.Where(c => c.Entity == entity).Select(c => c.Column).Order(StringComparer.Ordinal).ToList();

        var subjects = PredicateCompiler.EvaluatorGlobalReadableEntities
            .Where(SubjectTypeBySubject.ContainsKey)
            .Order(StringComparer.Ordinal)
            .Select(subject => new OntologyCatalogSubject(
                subject,
                DateOnlyOf(subject),
                PredicateCompiler.ThresholdColumnRanges
                    .Where(kv => kv.Key.Entity == subject)
                    .OrderBy(kv => kv.Key.Column, StringComparer.Ordinal)
                    .Select(kv => new OntologyCatalogThresholdField(kv.Key.Column, kv.Value.Min, kv.Value.Max))
                    .ToList(),
                PredicateCompiler.VerifiedJoins
                    .Where(kv => kv.Key.Subject == subject && PredicateCompiler.EvaluatorGlobalReadableEntities.Contains(kv.Key.Related))
                    .OrderBy(kv => kv.Key.Related, StringComparer.Ordinal)
                    .Select(kv => new OntologyCatalogJoin(kv.Key.Related, kv.Value, DateOnlyOf(kv.Key.Related)))
                    .ToList()))
            .ToList();

        return new OntologyAuthoringCatalog(
            RuleTypes: new[] { nameof(RuleType.Existence), nameof(RuleType.Threshold) },
            Subjects: subjects,
            Operators: new OntologyCatalogOperators(
                Comparison: PredicateCompiler.ComparisonOperators.Keys.Order(StringComparer.Ordinal).ToList(),
                Threshold: PredicateCompiler.ThresholdOperators.Order(StringComparer.Ordinal).ToList(),
                RangeLower: PredicateCompiler.LowerBoundOperators.Order(StringComparer.Ordinal).ToList(),
                RangeUpper: PredicateCompiler.UpperBoundOperators.Order(StringComparer.Ordinal).ToList(),
                NullTests: new[] { PredicateCompiler.NullTestMember, PredicateCompiler.NotNullTestMember },
                MaxInListLength: PredicateCompiler.MaxInListLength),
            RelativeDates: new OntologyCatalogRelativeDates(PredicateCompiler.RelativeDateForms, PredicateCompiler.MaxRelativeDateDays),
            Limits: new OntologyCatalogLimits(
                PredicateCompiler.MaxClauses, PredicateCompiler.MaxRuleBodyLength,
                PredicateCompiler.DefaultQuietWindowDays, PredicateCompiler.MaxQuietWindowDays),
            Triage: new OntologyCatalogTriage(TriageEntity, TriageField, categories));
    }

    private async Task<Read<IReadOnlyList<OntologyCatalogCategory>>> ReadTriageCategoriesAsync(CancellationToken ct)
    {
        var response = await _user.GetAsync(
            $"{TriageEntitySet}?$select=sprk_triagecategoryid,sprk_name,sprk_enabled&$orderby=sprk_name", ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            if (IsCallerUnresolved(response.ErrorCode)) return Read<IReadOnlyList<OntologyCatalogCategory>>.Failed(Failure.CallerUnresolved);
            if (response.ErrorCode == DataverseUserClientErrorCodes.AccessDenied)
            {
                // No Read on the taxonomy: the catalog offers no category, which a refused read must not dress up as an error.
                return Read<IReadOnlyList<OntologyCatalogCategory>>.Of(Array.Empty<OntologyCatalogCategory>());
            }

            _logger.LogWarning("Ontology authoring catalog: the triage categories could not be read ({ErrorCode}).", response.ErrorCode);
            return Read<IReadOnlyList<OntologyCatalogCategory>>.Failed(Failure.Unavailable);
        }

        var categories = new List<OntologyCatalogCategory>();
        foreach (var row in Rows(response.Body))
        {
            if (TryGuid(row, "sprk_triagecategoryid", out var id) && ReadString(row, "sprk_name") is { Length: > 0 } name)
            {
                categories.Add(new OntologyCatalogCategory(id, name,
                    row.TryGetProperty("sprk_enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True));
            }
        }

        return Read<IReadOnlyList<OntologyCatalogCategory>>.Of(categories);
    }

    // =================================================================================================================
    // Dry run (#39)
    // =================================================================================================================

    /// <summary>
    /// Evaluates the draft over current data AS THE EVALUATOR (writes nothing), keeps only the rows the caller can read, and classifies
    /// each against the policy's caller-readable Signals. Records a proof of the exact body for save and publish.
    /// </summary>
    public async Task<OntologyAdminAuthoringResult<OntologyDryRunResult>> DryRunAsync(
        ClaimsPrincipal principal, OntologyDryRunRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var caller = await ResolveCallerAsync(ct).ConfigureAwait(false);
            if (caller.Failure is { } failure)
            {
                return Fail<OntologyDryRunResult>(failure);
            }

            var proofScope = ProofScope(principal, caller.UserId);
            if (proofScope is null)
            {
                return Fail<OntologyDryRunResult>(Failure.CallerUnresolved);
            }

            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            PolicyRow? policy = null;
            DecisionLane lane;
            if (request.PolicyId is { } policyId)
            {
                if (policyId == Guid.Empty)
                {
                    return Fail<OntologyDryRunResult>(Failure.NotFound);
                }

                // The domain allow-list and the caller's own read, before anything is evaluated.
                var read = await ReadPolicyAsync(policyId, ct).ConfigureAwait(false);
                if (read.Failure is { } readFailure)
                {
                    return Fail<OntologyDryRunResult>(readFailure);
                }

                policy = read.Value!;
                lane = policy.Lane;
            }
            else if (!TryParseLane(request.Lane, out lane))
            {
                errors["lane"] = ["A dry run of a new rule names its lane: Decide or Do."];
            }

            var ruleTypeName = ValidateRule(request.RuleType, request.RuleBody, request.MessageTemplate, errors);
            if (errors.Count > 0)
            {
                return Invalid<OntologyDryRunResult>(errors);
            }

            // The policy, built IN MEMORY from the caller's read plus the draft; the evaluator is never handed the request itself.
            var spec = new SignalEvaluationPolicy(
                PolicyId: policy?.Id ?? Guid.Empty,
                PolicyCode: policy?.Code ?? DryRunPolicyCode,
                Lane: (int)lane,
                PolicyVersionId: Guid.Empty,
                RuleType: ruleTypeName,
                RuleBody: request.RuleBody,
                MessageTemplate: request.MessageTemplate);
            var evaluation = await _engine.EvaluateAsync(spec, ct).ConfigureAwait(false);
            if (!evaluation.PolicyValid)
            {
                // ValidateForSave passed, so this is the evaluation-time gate disagreeing: never show a partial answer.
                return Invalid<OntologyDryRunResult>(new Dictionary<string, string[]>
                {
                    ["ruleBody"] = ["The rule could not be prepared for evaluation."],
                });
            }

            var readable = await ReadableHitsAsync(evaluation.Hits, ct).ConfigureAwait(false);
            if (readable.Failure is { } hitFailure)
            {
                return Fail<OntologyDryRunResult>(hitFailure);
            }

            IReadOnlyList<CallerSignal> signals = Array.Empty<CallerSignal>();
            if (policy is not null)
            {
                var signalRead = await ReadPolicySignalsAsync(policy.Id, ct).ConfigureAwait(false);
                if (signalRead.Failure is { } signalFailure)
                {
                    return Fail<OntologyDryRunResult>(signalFailure);
                }

                signals = signalRead.Value!;
            }

            var result = Classify(readable.Value!, signals, lane, evaluation.QuietWindowDays, evaluation.WindowAnchorUtc, _clock.GetUtcNow().UtcDateTime);
            var recorded = await RecordProofAsync(proofScope.Value, policy?.Id, ruleTypeName!, request.RuleBody!, request.MessageTemplate, ct)
                .ConfigureAwait(false);

            return new(OntologyAdminAuthoringOutcome.Ok, result with { PolicyEnabled = policy is null ? null : policy.Enabled && policy.Active, Recorded = recorded });
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Fault<OntologyDryRunResult>("dry run", ex);
        }
    }

    /// <summary>The readable hits (subject and, when it has one, core record both returned by the caller's own read), plus how many of
    /// them the writer could not place.</summary>
    internal sealed record ReadableHits(IReadOnlyList<SignalHit> Placed, int NotPlaceable);

    private async Task<Read<ReadableHits>> ReadableHitsAsync(IReadOnlyList<SignalHit> hits, CancellationToken ct)
    {
        if (hits.Count == 0)
        {
            return Read<ReadableHits>.Of(new ReadableHits(Array.Empty<SignalHit>(), 0));
        }

        var sets = new Dictionary<string, string>(StringComparer.Ordinal);

        // (1) the subjects, as the caller
        var readableSubjects = new HashSet<(string, Guid)>();
        foreach (var byTable in hits.GroupBy(h => h.SubjectEntity, StringComparer.Ordinal))
        {
            var ids = await ReadableIdsAsync(byTable.Key, byTable.Select(h => h.SubjectId), sets, ct).ConfigureAwait(false);
            if (ids.Failure is { } failure) return Read<ReadableHits>.Failed(failure);
            foreach (var id in ids.Value!) readableSubjects.Add((byTable.Key, id));
        }

        // (2) the core records, as the caller; only uac-r2's core types (a forged or stale type is never a probe of another table)
        var readableCores = new HashSet<(string, Guid)>();
        foreach (var byCore in hits
                     .Where(h => h.Core is { IsNoCore: false } && readableSubjects.Contains((h.SubjectEntity, h.SubjectId)))
                     .GroupBy(h => h.Core!.Entity!, StringComparer.OrdinalIgnoreCase))
        {
            if (!CoreAncestorResolver.CoreRecordEntities.Contains(byCore.Key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var table = byCore.Key.ToLowerInvariant();
            var ids = await ReadableIdsAsync(table, byCore.Select(h => h.Core!.Id), sets, ct).ConfigureAwait(false);
            if (ids.Failure is { } failure) return Read<ReadableHits>.Failed(failure);
            foreach (var id in ids.Value!) readableCores.Add((table, id));
        }

        var placed = new List<SignalHit>();
        var notPlaceable = 0;
        foreach (var hit in hits)
        {
            if (!readableSubjects.Contains((hit.SubjectEntity, hit.SubjectId)))
            {
                continue; // the caller cannot read the subject: absent, and not counted anywhere
            }

            if (hit.Core is null)
            {
                notPlaceable++; // the writer cannot derive its core record: production would raise nothing (D-38)
                continue;
            }

            if (!hit.Core.IsNoCore && !readableCores.Contains((hit.Core.Entity!.ToLowerInvariant(), hit.Core.Id)))
            {
                continue; // the caller cannot read the core record (a matter they may not open): absent, not counted
            }

            placed.Add(hit);
        }

        return Read<ReadableHits>.Of(new ReadableHits(placed, notPlaceable));
    }

    /// <summary>The ids of <paramref name="table"/> the caller's own read returns, in batches. A table the caller has no Read on
    /// returns none (every row is absent); an unresolved caller or a fault fails the whole dry run, never a shorter list.</summary>
    private async Task<Read<HashSet<Guid>>> ReadableIdsAsync(
        string table, IEnumerable<Guid> candidates, Dictionary<string, string> sets, CancellationToken ct)
    {
        var readable = new HashSet<Guid>();
        if (!LogicalName().IsMatch(table))
        {
            return Read<HashSet<Guid>>.Of(readable);
        }

        if (!sets.TryGetValue(table, out var entitySet))
        {
            var meta = await _user.GetAsync($"EntityDefinitions(LogicalName='{table}')?$select=EntitySetName", ct).ConfigureAwait(false);
            if (!meta.IsSuccess || meta.Body is not { } body || ReadString(body, "EntitySetName") is not { Length: > 0 } name
                || !LogicalName().IsMatch(name))
            {
                if (!meta.IsSuccess && IsCallerUnresolved(meta.ErrorCode)) return Read<HashSet<Guid>>.Failed(Failure.CallerUnresolved);
                _logger.LogWarning("Ontology dry run: the entity set of {Table} could not be read ({ErrorCode}).", table, meta.ErrorCode ?? "unparseable");
                return Read<HashSet<Guid>>.Failed(Failure.Unavailable);
            }

            entitySet = name;
            sets[table] = entitySet;
        }

        var pk = table + "id";
        foreach (var chunk in candidates.Where(id => id != Guid.Empty).Distinct().Chunk(IdChunk))
        {
            var response = await _user.GetAsync(
                $"{entitySet}?$select={pk}&$filter={SignalReadService.InFilter(pk, chunk)}", ct).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                if (IsCallerUnresolved(response.ErrorCode)) return Read<HashSet<Guid>>.Failed(Failure.CallerUnresolved);
                if (response.ErrorCode == DataverseUserClientErrorCodes.AccessDenied) return Read<HashSet<Guid>>.Of(new HashSet<Guid>());
                _logger.LogWarning("Ontology dry run: the caller's read of {Table} failed ({ErrorCode}).", table, response.ErrorCode);
                return Read<HashSet<Guid>>.Failed(Failure.Unavailable);
            }

            foreach (var row in Rows(response.Body))
            {
                if (TryGuid(row, pk, out var id) && chunk.Contains(id))
                {
                    readable.Add(id);
                }
            }
        }

        return Read<HashSet<Guid>>.Of(readable);
    }

    /// <summary>One of the policy's Signals as the CALLER's read returned it.</summary>
    internal sealed record CallerSignal(
        string SubjectEntity, Guid SubjectId, int Episode, int? Status, int? Resolution, DateTime? ResolvedOn, DateTime? SuppressedUntil,
        Guid? CoreTypeId, Guid? CoreId)
    {
        public bool IsOpen => Status is SignalWriter.SignalStatusOpen or SignalWriter.SignalStatusAcknowledged;
    }

    private async Task<Read<IReadOnlyList<CallerSignal>>> ReadPolicySignalsAsync(Guid policyId, CancellationToken ct)
    {
        var fetch = new XElement("fetch",
            new XElement("entity", new XAttribute("name", "sprk_signal"),
                new[] { "sprk_signalid", "sprk_dedupekey", "sprk_signalstatus", "sprk_resolutiontype", "sprk_resolvedon", "sprk_suppresseduntil",
                        "sprk_corerecordid", "sprk_corerecordtype" }
                    .Select(c => new XElement("attribute", new XAttribute("name", c))),
                new XElement("order", new XAttribute("attribute", "sprk_signalid")),
                new XElement("filter",
                    new XElement("condition", new XAttribute("attribute", "sprk_policy"), new XAttribute("operator", "eq"),
                        new XAttribute("value", policyId.ToString("D"))))));

        var signals = new List<CallerSignal>();
        for (var page = 1; ; page++)
        {
            if (page > MaxSignalPages)
            {
                _logger.LogWarning("Ontology dry run: more than {MaxPages} pages of Signals for one policy; refusing rather than counting a part.", MaxSignalPages);
                return Read<IReadOnlyList<CallerSignal>>.Failed(Failure.Unavailable);
            }

            var response = await _user.GetAsync(
                $"{SignalEntitySet}?fetchXml={Uri.EscapeDataString(fetch.ToString(SaveOptions.DisableFormatting))}", ct).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                if (IsCallerUnresolved(response.ErrorCode)) return Read<IReadOnlyList<CallerSignal>>.Failed(Failure.CallerUnresolved);
                if (response.ErrorCode == DataverseUserClientErrorCodes.AccessDenied && page == 1)
                {
                    // No Read on sprk_signal: the caller sees no Signal, so every row is new and nothing would be superseded FOR THEM.
                    return Read<IReadOnlyList<CallerSignal>>.Of(Array.Empty<CallerSignal>());
                }

                _logger.LogWarning("Ontology dry run: the caller's Signal read failed ({ErrorCode}, page {Page}).", response.ErrorCode, page);
                return Read<IReadOnlyList<CallerSignal>>.Failed(Failure.Unavailable);
            }

            foreach (var row in Rows(response.Body))
            {
                if (!SignalWriter.TryParseDedupeKey(ReadString(row, "sprk_dedupekey"), out _, out var subjectEntity, out var subjectId, out var episode))
                {
                    continue; // a key with no readable subject cannot be matched to a row; the nightly run reports it
                }

                signals.Add(new CallerSignal(
                    subjectEntity, subjectId, episode,
                    ReadInt(row, "sprk_signalstatus"), ReadInt(row, "sprk_resolutiontype"),
                    ReadDate(row, "sprk_resolvedon"), ReadDate(row, "sprk_suppresseduntil"),
                    TryGuid(row, "_sprk_corerecordtype_value", out var type) ? type : null,
                    Guid.TryParse(ReadString(row, "sprk_corerecordid")?.Trim(), out var core) && core != Guid.Empty ? core : null));
            }

            if (response.Body is not { } body || !SignalReadService.HasMoreRecords(body))
            {
                return Read<IReadOnlyList<CallerSignal>>.Of(signals);
            }

            var cookie = SignalReadService.PagingCookieOf(body);
            if (cookie is null)
            {
                return Read<IReadOnlyList<CallerSignal>>.Failed(Failure.Unavailable);
            }

            fetch.SetAttributeValue("page", (page + 1).ToString(CultureInfo.InvariantCulture));
            fetch.SetAttributeValue("paging-cookie", cookie);
        }
    }

    /// <summary>
    /// Per readable hit: <c>open</c> when the caller can read an open Signal of the policy on its subject; <c>suppressed</c> when a
    /// readable Signal on its grain (its core record, or the item itself without one) carries a live <c>sprk_suppresseduntil</c>, or,
    /// in the Decide lane, its latest episode was dismissed inside the quiet window; otherwise <c>new</c>. A PREVIEW of the nightly
    /// planner over the caller's rows, not the planner itself (task 101 notes, known limit).
    /// </summary>
    internal static OntologyDryRunResult Classify(
        ReadableHits hits, IReadOnlyList<CallerSignal> signals, DecisionLane lane, int quietWindowDays, DateTimeOffset anchor, DateTime nowUtc)
    {
        var bySubject = signals.GroupBy(s => (s.SubjectEntity, s.SubjectId)).ToDictionary(g => g.Key, g => g.ToList());
        var mutedCores = signals.Where(s => s.SuppressedUntil > nowUtc && s.CoreTypeId is not null && s.CoreId is not null)
            .Select(s => (s.CoreTypeId!.Value, s.CoreId!.Value)).ToHashSet();
        var mutedItems = signals.Where(s => s.SuppressedUntil > nowUtc && s.CoreId is null)
            .Select(s => (s.SubjectEntity, s.SubjectId)).ToHashSet();

        var rows = new List<OntologyDryRunRow>(hits.Placed.Count);
        foreach (var hit in hits.Placed.OrderBy(h => h.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(h => h.SubjectId))
        {
            bySubject.TryGetValue((hit.SubjectEntity, hit.SubjectId), out var own);
            string status;
            if (own?.Any(s => s.IsOpen) == true)
            {
                status = StatusOpen;
            }
            else if ((hit.Core is { IsNoCore: false, TypeRefId: { } typeRef } core && mutedCores.Contains((typeRef, core.Id)))
                     || (hit.Core is { IsNoCore: true } && mutedItems.Contains((hit.SubjectEntity, hit.SubjectId)))
                     || (lane == DecisionLane.Decide && own?.MaxBy(s => s.Episode) is { Resolution: SignalWriter.ResolutionDismissed, ResolvedOn: { } dismissed }
                         && dismissed.AddDays(quietWindowDays) > nowUtc))
            {
                status = StatusSuppressed;
            }
            else
            {
                status = StatusNew;
            }

            rows.Add(new OntologyDryRunRow(
                hit.SubjectEntity, hit.SubjectId, hit.DisplayName, hit.RecordNumber,
                hit.Core is { IsNoCore: false } c ? c.Entity : null,
                hit.Core is { IsNoCore: false } k ? k.Id : null,
                status));
        }

        var openSignals = signals.Count(s => s.IsOpen);
        var reraise = rows.Count(r => r.Status == StatusOpen);
        return new OntologyDryRunResult(
            Rows: rows,
            Open: reraise,
            Suppressed: rows.Count(r => r.Status == StatusSuppressed),
            New: rows.Count(r => r.Status == StatusNew),
            WouldSupersede: openSignals,
            WouldReraise: reraise,
            NotPlaceable: hits.NotPlaceable,
            PolicyEnabled: null,
            WindowAnchorUtc: anchor,
            Recorded: false);
    }

    // =================================================================================================================
    // Save a new rule (FR-10: Off)
    // =================================================================================================================

    /// <summary>
    /// Saves a new Ontology policy, Off, with its first version as the current version, AS THE CALLER. The draft must pass
    /// ValidateForSave and the plan check and must have been dry-run by this caller; a refusal writes nothing.
    /// </summary>
    public async Task<OntologyAdminAuthoringResult<OntologyPolicySaved>> SavePolicyAsync(
        ClaimsPrincipal principal, OntologyNewPolicyRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var caller = await ResolveCallerAsync(ct).ConfigureAwait(false);
            if (caller.Failure is { } failure)
            {
                return Fail<OntologyPolicySaved>(failure);
            }

            var proofScope = ProofScope(principal, caller.UserId);
            if (proofScope is null)
            {
                return Fail<OntologyPolicySaved>(Failure.CallerUnresolved);
            }

            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var code = request.PolicyCode?.Trim();
            if (string.IsNullOrEmpty(code) || code.Length > MaxCodeLength || !PolicyCode().IsMatch(code))
            {
                errors["policyCode"] = [$"A policy code is 1 to {MaxCodeLength} letters, digits and hyphens, starting with a letter or digit."];
            }

            RequireText(request.Name, "name", MaxNameLength, errors);
            if (request.ShortName is { } shortName && shortName.Trim().Length > MaxShortNameLength)
            {
                errors["shortName"] = [$"The short name is at most {MaxShortNameLength} characters."];
            }

            if (!TryParseLane(request.Lane, out var lane))
            {
                errors["lane"] = ["The lane is Decide or Do."];
            }

            if (request.Severity is { } severity && severity is not (SignalWriter.SeverityInfo or SignalWriter.SeverityWarning or SignalWriter.SeverityCritical))
            {
                errors["severity"] = ["The severity is Info, Warning or Critical."];
            }

            if (request.WorkType is { } workType && !SignalReadService.WorkTypeKeys.ContainsKey(workType))
            {
                errors["workType"] = ["The work type is not one of the worklist's work types."];
            }

            var priority = request.Priority ?? DefaultPriority;
            if (priority is < 0 or > MaxPriority)
            {
                errors["priority"] = [$"The priority is a whole number from 0 to {MaxPriority} (lowest wins)."];
            }

            var draft = request.Draft;
            string? ruleTypeName = null;
            if (draft is null)
            {
                errors["draft"] = ["The rule is required."];
            }
            else
            {
                ruleTypeName = ValidateDraft(draft, errors.ContainsKey("lane") ? null : lane, errors);
            }

            var subjectType = 0;
            if (errors.Count == 0 && !TrySubjectType(draft!.RuleBody!, out subjectType))
            {
                errors["ruleBody"] = ["A rule about this table cannot be saved as a policy: it has no policy subject type."];
            }

            if (errors.Count > 0)
            {
                return Invalid<OntologyPolicySaved>(errors);
            }

            if (!await HasProofAsync(proofScope.Value, null, ruleTypeName!, draft!.RuleBody!, draft.MessageTemplate, ct).ConfigureAwait(false))
            {
                return Conflict<OntologyPolicySaved>(ReasonDryRunRequired);
            }

            // A code another Ontology policy already holds; a non-Ontology holder surfaces as the create's key conflict below.
            var existing = await _user.GetAsync(
                $"{PolicyEntitySet}?$select=sprk_policyid&$filter=sprk_policycode eq '{ODataString(code!)}' and {OntologyDomain.Filter}", ct)
                .ConfigureAwait(false);
            if (!existing.IsSuccess)
            {
                return Fail<OntologyPolicySaved>(ReadFailure(existing, "policy code check"));
            }

            if (Rows(existing.Body).Any())
            {
                return Conflict<OntologyPolicySaved>(ReasonPolicyCodeTaken);
            }

            var policyId = Guid.NewGuid();
            var versionId = Guid.NewGuid();
            var now = NowForDataverse();
            // Nested in the policy create, the version is linked to it by the deep insert itself, so it carries no policy bind.
            var version = VersionPayload(versionId, policyId: null, code!, 1, ruleTypeName!, draft, caller.UserId, now);

            var policyBody = new Dictionary<string, object?>
            {
                ["sprk_policyid"] = policyId,
                ["sprk_name"] = request.Name!.Trim(),
                ["sprk_policycode"] = code,
                ["sprk_shortname"] = string.IsNullOrWhiteSpace(request.ShortName) ? null : request.ShortName.Trim(),
                ["sprk_enabled"] = false, // FR-10: a new rule saves Off
                ["sprk_priority"] = priority,
                ["sprk_lane"] = (int)lane,
                ["sprk_subjecttype"] = subjectType,
                ["sprk_countstowardsuppression"] = request.CountsTowardSuppression ?? true,
                [OntologyDomain.Column] = OntologyDomain.Value,
                ["sprk_policyversion_policy"] = new[] { version }, // deep insert: the policy and its first version, or neither
            };
            if (request.Severity is { } s) policyBody["sprk_severity"] = s;
            if (request.WorkType is { } w) policyBody["sprk_worktype"] = w;

            var created = await _user.PostAsync(ApiRoot + PolicyEntitySet, JsonSerializer.Serialize(policyBody), ct).ConfigureAwait(false);
            if (!created.IsSuccess)
            {
                if (created.StatusCode is 409 or 412 || (created.StatusCode == 400 && IsDuplicateKey(created.ErrorMessage)))
                {
                    return Conflict<OntologyPolicySaved>(ReasonPolicyCodeTaken);
                }

                return Fail<OntologyPolicySaved>(WriteFailure(created, "policy create"));
            }

            // The pointer. The policy is Off, so nothing evaluates it while this is pending; a failure leaves an Off policy whose first
            // version exists but is not yet current, and is reported (503) rather than hidden.
            var pointer = await _user.PatchAsync(
                $"{PolicyEntitySet}({policyId:D})", CurrentVersionJson(versionId), ct).ConfigureAwait(false);
            if (!pointer.IsSuccess)
            {
                _logger.LogError("Ontology authoring: policy {PolicyId} was created Off but its current version could not be set ({ErrorCode}).",
                    policyId, pointer.ErrorCode);
                return Fail<OntologyPolicySaved>(WriteFailure(pointer, "current version"));
            }

            await ForgetProofAsync(proofScope.Value, null, ruleTypeName!, draft.RuleBody!, draft.MessageTemplate, ct).ConfigureAwait(false);
            _logger.LogInformation("Ontology authoring: policy {PolicyId} ({PolicyCode}) saved Off with version 1 {VersionId}.", policyId, code, versionId);
            return new(OntologyAdminAuthoringOutcome.Created, new OntologyPolicySaved(policyId, versionId, 1, Enabled: false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Fault<OntologyPolicySaved>("save", ex);
        }
    }

    // =================================================================================================================
    // Publish a new version (D-13, D-14)
    // =================================================================================================================

    /// <summary>
    /// Publishes a new version of an Ontology policy AS THE CALLER: create the version, stamp the old version's <c>sprk_inforceto</c>,
    /// move <c>sprk_currentversion</c>, then (for an enabled policy) run the single-policy pass that supersedes and re-raises.
    /// </summary>
    public async Task<OntologyAdminAuthoringResult<OntologyVersionPublished>> PublishVersionAsync(
        ClaimsPrincipal principal, Guid policyId, OntologyPublishRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var caller = await ResolveCallerAsync(ct).ConfigureAwait(false);
            if (caller.Failure is { } failure)
            {
                return Fail<OntologyVersionPublished>(failure);
            }

            var proofScope = ProofScope(principal, caller.UserId);
            if (proofScope is null)
            {
                return Fail<OntologyVersionPublished>(Failure.CallerUnresolved);
            }

            if (policyId == Guid.Empty)
            {
                return Fail<OntologyVersionPublished>(Failure.NotFound);
            }

            // (0) The domain allow-list and the caller's own read: before ANY write (new version, inforceto, currentversion).
            var read = await ReadPolicyAsync(policyId, ct).ConfigureAwait(false);
            if (read.Failure is { } readFailure)
            {
                return Fail<OntologyVersionPublished>(readFailure);
            }

            var policy = read.Value!;
            if (!policy.Active)
            {
                return Conflict<OntologyVersionPublished>(ReasonPolicyRetired);
            }

            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            string? ruleTypeName = null;
            if (request.Draft is not { } draft)
            {
                errors["draft"] = ["The rule is required."];
                return Invalid<OntologyVersionPublished>(errors);
            }

            ruleTypeName = ValidateDraft(draft, policy.Lane, errors);
            if (errors.Count > 0)
            {
                return Invalid<OntologyVersionPublished>(errors);
            }

            if (!await HasProofAsync(proofScope.Value, policy.Id, ruleTypeName!, draft.RuleBody!, draft.MessageTemplate, ct).ConfigureAwait(false))
            {
                return Conflict<OntologyVersionPublished>(ReasonDryRunRequired);
            }

            var versions = await ReadVersionsAsync(policy.Id, ct).ConfigureAwait(false);
            if (versions.Failure is { } versionFailure)
            {
                return Fail<OntologyVersionPublished>(versionFailure);
            }

            VersionRow? previous = null;
            if (policy.CurrentVersionId is { } currentId)
            {
                previous = versions.Value!.FirstOrDefault(v => v.Id == currentId);
                if (previous is null)
                {
                    // The pointer names a version the caller cannot read: nothing is written on the strength of a row we cannot see.
                    return Conflict<OntologyVersionPublished>(ReasonPolicyChanged);
                }
            }

            var number = versions.Value!.Count == 0 ? 1 : versions.Value!.Max(v => v.Number) + 1;
            var versionId = Guid.NewGuid();
            var now = NowForDataverse();

            // (1) the new version
            var created = await _user.PostAsync(
                ApiRoot + VersionEntitySet,
                JsonSerializer.Serialize(VersionPayload(versionId, policy.Id, policy.Code, number, ruleTypeName!, draft, caller.UserId, now)), ct)
                .ConfigureAwait(false);
            if (!created.IsSuccess)
            {
                return Fail<OntologyVersionPublished>(WriteFailure(created, "version create"));
            }

            // (2) the OLD version's sprk_inforceto, conditional on the row as read (a concurrent publish stamped it: refuse)
            if (previous is not null)
            {
                var stamped = await StampInForceToAsync(previous.Id, now, previous.RowVersion, ct).ConfigureAwait(false);
                if (!stamped.IsSuccess)
                {
                    await RetireUnpublishedAsync(versionId, now, ct).ConfigureAwait(false);
                    return stamped.StatusCode == 412
                        ? Conflict<OntologyVersionPublished>(ReasonPolicyChanged)
                        : Fail<OntologyVersionPublished>(WriteFailure(stamped, "previous version end"));
                }
            }

            // (3) the pointer, conditional on the policy as read (an On/Off, Retire or another publish meanwhile: refuse and undo)
            var moved = await _user.PatchAsync(
                $"{PolicyEntitySet}({policy.Id:D})", CurrentVersionJson(versionId), policy.RowVersion, ct).ConfigureAwait(false);
            if (!moved.IsSuccess)
            {
                if (previous is not null)
                {
                    await RestorePreviousAsync(previous.Id, now, ct).ConfigureAwait(false);
                }

                await RetireUnpublishedAsync(versionId, now, ct).ConfigureAwait(false);
                return moved.StatusCode == 412
                    ? Conflict<OntologyVersionPublished>(ReasonPolicyChanged)
                    : Fail<OntologyVersionPublished>(WriteFailure(moved, "current version"));
            }

            await ForgetProofAsync(proofScope.Value, policy.Id, ruleTypeName!, draft.RuleBody!, draft.MessageTemplate, ct).ConfigureAwait(false);

            // (4) supersede and re-raise, as the evaluator, once (task 033 contract). Only an enabled, active policy is evaluated; an Off
            // policy has no open Signals to supersede (the nightly run closes them PolicyRetired) and is evaluated when it is turned On.
            var reevaluation = ReevaluationNotEvaluated;
            int superseded = 0, raised = 0;
            if (policy.Enabled)
            {
                try
                {
                    var pass = await _engine.ReevaluateAsync(policy.Id, ct).ConfigureAwait(false);
                    superseded = pass.SupersededClosed;
                    raised = pass.Raised;
                    reevaluation = pass.Success ? ReevaluationCompleted : ReevaluationFailed;
                    if (!pass.Success)
                    {
                        _logger.LogError("Ontology authoring: version {VersionId} of policy {PolicyId} is published, but the supersede pass " +
                            "did not complete ({Summary}); the nightly run retries.", versionId, policy.Id, pass.FailureSummary);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    reevaluation = ReevaluationFailed;
                    _logger.LogError(ex, "Ontology authoring: version {VersionId} of policy {PolicyId} is published, but the supersede pass " +
                        "threw ({ExceptionType}); the nightly run retries.", versionId, policy.Id, ex.GetType().Name);
                }
            }

            _logger.LogInformation("Ontology authoring: policy {PolicyId} ({PolicyCode}) published version {Number} {VersionId} (reevaluation={Reevaluation}).",
                policy.Id, policy.Code, number, versionId, reevaluation);
            return new(OntologyAdminAuthoringOutcome.Ok,
                new OntologyVersionPublished(policy.Id, versionId, number, previous?.Id, reevaluation, superseded, raised));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Fault<OntologyVersionPublished>("publish", ex);
        }
    }

    /// <summary>
    /// THE only write this service makes to an existing <c>sprk_policyversion</c> (D-14): its <c>sprk_inforceto</c> and nothing else.
    /// Body, type, template and plan are immutable after create because no code path here can send them.
    /// </summary>
    private Task<DataverseUserResponse> StampInForceToAsync(Guid versionId, string? inForceTo, long? expectedRowVersion, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, string?> { ["sprk_inforceto"] = inForceTo });
        var path = $"{VersionEntitySet}({versionId:D})";
        return expectedRowVersion is { } version
            ? _user.PatchAsync(path, json, version, ct)
            : _user.PatchAsync(path, json, ct);
    }

    /// <summary>Compensation: a created version that never became current is marked never in force (in force to = in force from).</summary>
    private async Task RetireUnpublishedAsync(Guid versionId, string now, CancellationToken ct)
    {
        var undone = await StampInForceToAsync(versionId, now, null, ct).ConfigureAwait(false);
        if (!undone.IsSuccess)
        {
            _logger.LogError("Ontology authoring: version {VersionId} was created but not published, and could not be marked never in force ({ErrorCode}).",
                versionId, undone.ErrorCode);
        }
    }

    /// <summary>Compensation: the old version's stamp is undone, but only if it is still exactly the stamp this publish wrote.</summary>
    private async Task RestorePreviousAsync(Guid previousId, string stamp, CancellationToken ct)
    {
        var reread = await _user.GetAsync($"{VersionEntitySet}({previousId:D})?$select=sprk_inforceto,versionnumber", ct).ConfigureAwait(false);
        if (reread.IsSuccess && reread.Body is { } row
            && ReadDate(row, "sprk_inforceto") is { } inForceTo
            && FormatForDataverse(inForceTo) == stamp
            && ReadLong(row, "versionnumber") is { } rowVersion)
        {
            var restored = await StampInForceToAsync(previousId, null, rowVersion, ct).ConfigureAwait(false);
            if (restored.IsSuccess)
            {
                return;
            }
        }

        _logger.LogError("Ontology authoring: the previous version {VersionId} keeps the end stamp of a publish that did not complete; " +
            "the nightly run reports it (EventId 50309) until sprk_currentversion or sprk_inforceto is fixed.", previousId);
    }

    // =================================================================================================================
    // Validation (ValidateForSave + the plan check)
    // =================================================================================================================

    /// <summary>The draft's rule (type, body, template) through <see cref="PolicyVersionValidator.ValidateForSave"/>; returns the rule
    /// type's name on success and adds field-level errors otherwise.</summary>
    private string? ValidateRule(string? ruleType, string? ruleBody, string? messageTemplate, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(ruleBody))
        {
            errors["ruleBody"] = ["The rule body is required."];
            return null;
        }

        if (messageTemplate is { Length: > MaxMessageTemplateLength })
        {
            errors["messageTemplate"] = [$"The message template is at most {MaxMessageTemplateLength} characters."];
        }

        var result = _validator.ValidateForSave(ruleType, ruleBody, messageTemplate);
        if (!result.IsValid)
        {
            var field = result.Reason switch
            {
                OntologyWriterFailureReason.RuleTypeUnsupported => "ruleType",
                OntologyWriterFailureReason.TemplateMalformed or OntologyWriterFailureReason.TemplateTokenOutsideReadSet => "messageTemplate",
                _ => "ruleBody",
            };
            errors[field] = result.Errors.ToArray();
            return null;
        }

        // ValidateForSave accepted it, so the type is Existence or Threshold (Switch is refused, D-16).
        return RuleBodySchemaValidator.TryParseRuleType(ruleType, out var parsed) ? parsed.ToString() : null;
    }

    /// <summary>A whole draft for save or publish: the rule, the required columns, and the plan against the lane's catalog (task 036).</summary>
    private string? ValidateDraft(OntologyRuleDraft draft, DecisionLane? lane, Dictionary<string, string[]> errors)
    {
        var ruleTypeName = ValidateRule(draft.RuleType, draft.RuleBody, draft.MessageTemplate, errors);
        RequireText(draft.MessageTemplate, "messageTemplate", MaxMessageTemplateLength, errors);
        RequireText(draft.ShortHeadline, "shortHeadline", MaxShortHeadlineLength, errors);
        RequireText(draft.ProposedAction, "proposedAction", MaxProposedActionLength, errors);

        if (draft.DecisionPlan is { Length: > MaxDecisionPlanLength })
        {
            errors["decisionPlan"] = ["The decision plan is too long."];
        }
        else if (lane is { } l)
        {
            var plan = DecisionPlanService.Resolve(draft.DecisionPlan, l);
            if (plan.IsRefused)
            {
                errors["decisionPlan"] = [$"The decision plan is refused ({plan.RefusalReason}): it must list actions from the {l} lane's catalog."];
            }
        }

        return errors.Count == 0 ? ruleTypeName : null;
    }

    private static void RequireText(string? value, string field, int max, Dictionary<string, string[]> errors)
    {
        if (errors.ContainsKey(field))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = ["Required."];
        }
        else if (value.Trim().Length > max)
        {
            errors[field] = [$"At most {max} characters."];
        }
    }

    private static bool TryParseLane(string? raw, out DecisionLane lane)
    {
        lane = default;
        if (string.Equals(raw, nameof(DecisionLane.Decide), StringComparison.OrdinalIgnoreCase)) lane = DecisionLane.Decide;
        else if (string.Equals(raw, nameof(DecisionLane.Do), StringComparison.OrdinalIgnoreCase)) lane = DecisionLane.Do;
        else return false;
        return true;
    }

    private static bool TrySubjectType(string ruleBody, out int subjectType)
    {
        subjectType = 0;
        using var doc = PredicateCompiler.ParseBounded(ruleBody);
        return doc.RootElement.TryGetProperty("subject", out var subject)
            && subject.ValueKind == JsonValueKind.String
            && SubjectTypeBySubject.TryGetValue(subject.GetString()!, out subjectType);
    }

    // =================================================================================================================
    // The dry-run proof (v4: a test run before save; any edit invalidates it)
    // =================================================================================================================

    internal readonly record struct ProofOwner(string TenantId, Guid CallerId);

    private static ProofOwner? ProofScope(ClaimsPrincipal principal, Guid callerId) =>
        TenantResolution.ResolveTenantId(principal) is { Length: > 0 } tenant && callerId != Guid.Empty
            ? new ProofOwner(tenant, callerId)
            : null;

    /// <summary>The proof's key: the caller (the Dataverse user WhoAmI named), the policy (or "new"), and a SHA-256 of the exact rule
    /// type, body and template, length-prefixed so no two drafts share a text.</summary>
    internal static string ProofKey(Guid callerId, Guid? policyId, string ruleType, string ruleBody, string? messageTemplate)
    {
        var template = messageTemplate ?? string.Empty;
        var canonical = string.Create(CultureInfo.InvariantCulture,
            $"v1\n{ruleType}\n{ruleBody.Length}:{ruleBody}\n{template.Length}:{template}");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return $"{callerId:N}:{(policyId is { } p ? p.ToString("N") : "new")}:{hash}";
    }

    private async Task<bool> RecordProofAsync(ProofOwner owner, Guid? policyId, string ruleType, string ruleBody, string? template, CancellationToken ct)
    {
        try
        {
            await _cache.SetAsync(owner.TenantId, DryRunProofResource, ProofKey(owner.CallerId, policyId, ruleType, ruleBody, template),
                DryRunProofVersion, _clock.GetUtcNow(), DryRunProofTtl, ct: ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Ontology dry run: the proof could not be recorded ({ExceptionType}); save and publish will refuse this body.",
                ex.GetType().Name);
            return false;
        }
    }

    private async Task<bool> HasProofAsync(ProofOwner owner, Guid? policyId, string ruleType, string ruleBody, string? template, CancellationToken ct)
    {
        try
        {
            var at = await _cache.GetAsync<DateTimeOffset?>(owner.TenantId, DryRunProofResource,
                ProofKey(owner.CallerId, policyId, ruleType, ruleBody, template), DryRunProofVersion, ct: ct).ConfigureAwait(false);
            return at is not null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Fail closed: an unreadable proof is no proof.
            _logger.LogWarning("Ontology authoring: the dry-run proof could not be read ({ExceptionType}); refusing.", ex.GetType().Name);
            return false;
        }
    }

    private async Task ForgetProofAsync(ProofOwner owner, Guid? policyId, string ruleType, string ruleBody, string? template, CancellationToken ct)
    {
        try
        {
            await _cache.RemoveAsync(owner.TenantId, DryRunProofResource, ProofKey(owner.CallerId, policyId, ruleType, ruleBody, template),
                DryRunProofVersion, ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Ontology authoring: a used dry-run proof could not be removed ({ExceptionType}); it expires on its own.", ex.GetType().Name);
        }
    }

    // =================================================================================================================
    // Caller reads
    // =================================================================================================================

    internal enum Failure
    {
        CallerUnresolved,
        Refused,
        NotFound,
        Unavailable,
    }

    private readonly record struct Read<T>(T? Value, Failure? Failure)
        where T : class
    {
        public static Read<T> Of(T value) => new(value, null);

        public static Read<T> Failed(Failure failure) => new(null, failure);
    }

    private readonly record struct Caller(Guid UserId, Failure? Failure);

    /// <summary>WhoAmI AS THE CALLER. Unresolved (no user context, OBO not configured or refused, WhoAmI refused, or no id) is the single
    /// 403; any other fault 503.</summary>
    private async Task<Caller> ResolveCallerAsync(CancellationToken ct)
    {
        var who = await _user.GetAsync("WhoAmI()", ct).ConfigureAwait(false);
        if (who.IsSuccess && who.Body is { } body && TryGuid(body, "UserId", out var userId))
        {
            return new Caller(userId, null);
        }

        if (who.IsSuccess || IsCallerUnresolved(who.ErrorCode) || who.ErrorCode == DataverseUserClientErrorCodes.AccessDenied)
        {
            _logger.LogWarning("Ontology authoring: the caller could not be resolved ({ErrorCode}); single 403.", who.ErrorCode ?? "no-user-id");
            return new Caller(Guid.Empty, Failure.CallerUnresolved);
        }

        _logger.LogWarning("Ontology authoring: the caller's identity could not be checked ({ErrorCode}); unavailable.", who.ErrorCode);
        return new Caller(Guid.Empty, Failure.Unavailable);
    }

    internal sealed record PolicyRow(Guid Id, string Code, DecisionLane Lane, bool Enabled, bool Active, Guid? CurrentVersionId, long RowVersion);

    /// <summary>One policy AS THE CALLER, through the domain allow-list. Absent, unreadable or not an Ontology policy: the uniform 404.</summary>
    private async Task<Read<PolicyRow>> ReadPolicyAsync(Guid policyId, CancellationToken ct)
    {
        var response = await _user.GetAsync(
            $"{PolicyEntitySet}?$select=sprk_policyid,sprk_policycode,sprk_lane,sprk_enabled,statecode,_sprk_currentversion_value,versionnumber,{OntologyDomain.Column}"
            + $"&$filter=sprk_policyid eq {policyId:D} and {OntologyDomain.Filter}", ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return Read<PolicyRow>.Failed(response.ErrorCode == DataverseUserClientErrorCodes.AccessDenied
                ? Failure.NotFound
                : ReadFailure(response, "policy"));
        }

        var row = Rows(response.Body).FirstOrDefault(r => TryGuid(r, "sprk_policyid", out var id) && id == policyId);
        if (row.ValueKind != JsonValueKind.Object || !OntologyDomain.Admits(row))
        {
            return Read<PolicyRow>.Failed(Failure.NotFound);
        }

        if (ReadString(row, "sprk_policycode") is not { Length: > 0 } code
            || ReadInt(row, "sprk_lane") is not (int)DecisionLane.Decide and not (int)DecisionLane.Do
            || ReadLong(row, "versionnumber") is not { } rowVersion)
        {
            // A policy without a code, a lane or a row version cannot be published safely; nothing is written.
            _logger.LogWarning("Ontology authoring: policy {PolicyId} is missing its code, lane or row version.", policyId);
            return Read<PolicyRow>.Failed(Failure.Unavailable);
        }

        return Read<PolicyRow>.Of(new PolicyRow(
            policyId, code, (DecisionLane)ReadInt(row, "sprk_lane")!.Value,
            row.TryGetProperty("sprk_enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True,
            ReadInt(row, "statecode") is null or 0,
            TryGuid(row, "_sprk_currentversion_value", out var current) ? current : null,
            rowVersion));
    }

    internal sealed record VersionRow(Guid Id, int Number, long? RowVersion);

    private async Task<Read<IReadOnlyList<VersionRow>>> ReadVersionsAsync(Guid policyId, CancellationToken ct)
    {
        var response = await _user.GetAsync(
            $"{VersionEntitySet}?$select=sprk_policyversionid,sprk_versionnumber,versionnumber&$filter=_sprk_policy_value eq {policyId:D}", ct)
            .ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return Read<IReadOnlyList<VersionRow>>.Failed(ReadFailure(response, "versions"));
        }

        if (response.Body is { } body && body.TryGetProperty("@odata.nextLink", out _))
        {
            return Read<IReadOnlyList<VersionRow>>.Failed(Failure.Unavailable); // never number a version from a partial list
        }

        var versions = Rows(response.Body)
            .Where(r => TryGuid(r, "sprk_policyversionid", out _))
            .Select(r =>
            {
                TryGuid(r, "sprk_policyversionid", out var id);
                return new VersionRow(id, ReadInt(r, "sprk_versionnumber") ?? 0, ReadLong(r, "versionnumber"));
            })
            .ToList();
        return Read<IReadOnlyList<VersionRow>>.Of(versions);
    }

    // =================================================================================================================
    // Payloads
    // =================================================================================================================

    private static Dictionary<string, object?> VersionPayload(
        Guid versionId, Guid? policyId, string code, int number, string ruleTypeName, OntologyRuleDraft draft, Guid authoredBy, string now)
    {
        var payload = new Dictionary<string, object?>
        {
            ["sprk_policyversionid"] = versionId,
            ["sprk_name"] = $"{code} v{number.ToString(CultureInfo.InvariantCulture)}",
            ["sprk_versionnumber"] = number,
            ["sprk_ruletype"] = (int)Enum.Parse<RuleType>(ruleTypeName),
            ["sprk_rulebody"] = draft.RuleBody,
            ["sprk_messagetemplate"] = draft.MessageTemplate!.Trim(),
            ["sprk_shortheadline"] = draft.ShortHeadline!.Trim(),
            ["sprk_proposedaction"] = draft.ProposedAction!.Trim(),
            ["sprk_decisionplan"] = draft.DecisionPlan,
            ["sprk_inforcefrom"] = now,
            ["sprk_AuthoredBy@odata.bind"] = $"/systemusers({authoredBy:D})",
        };
        if (policyId is { } parent)
        {
            payload["sprk_Policy@odata.bind"] = $"/{PolicyEntitySet}({parent:D})";
        }

        return payload;
    }

    private static string CurrentVersionJson(Guid versionId) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { ["sprk_CurrentVersion@odata.bind"] = $"/{VersionEntitySet}({versionId:D})" });

    private string NowForDataverse() => FormatForDataverse(_clock.GetUtcNow().UtcDateTime);

    private static string FormatForDataverse(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    // =================================================================================================================
    // Result helpers
    // =================================================================================================================

    private Failure ReadFailure(DataverseUserResponse response, string what)
    {
        if (IsCallerUnresolved(response.ErrorCode)) return Failure.CallerUnresolved;
        if (response.ErrorCode == DataverseUserClientErrorCodes.AccessDenied) return Failure.Refused;
        _logger.LogWarning("Ontology authoring: the {What} read failed ({ErrorCode}).", what, response.ErrorCode);
        return Failure.Unavailable;
    }

    private Failure WriteFailure(DataverseUserResponse response, string what)
    {
        if (IsCallerUnresolved(response.ErrorCode)) return Failure.CallerUnresolved;
        if (response.ErrorCode == DataverseUserClientErrorCodes.AccessDenied)
        {
            // Dataverse refused the caller: the Spaarke Ontology Administrator role decides, never this service.
            _logger.LogInformation("Ontology authoring: Dataverse refused the caller's {What} write.", what);
            return Failure.Refused;
        }

        _logger.LogWarning("Ontology authoring: the {What} write failed ({ErrorCode}, {StatusCode}).", what, response.ErrorCode, response.StatusCode);
        return Failure.Unavailable;
    }

    private static OntologyAdminAuthoringResult<T> Fail<T>(Failure failure)
        where T : class =>
        failure switch
        {
            Failure.CallerUnresolved => new(OntologyAdminAuthoringOutcome.CallerUnresolved, ReasonCode: ReasonCallerUnresolved),
            Failure.Refused => new(OntologyAdminAuthoringOutcome.Refused, ReasonCode: ReasonRefused),
            Failure.NotFound => new(OntologyAdminAuthoringOutcome.NotFound, ReasonCode: ReasonNotFound),
            _ => new(OntologyAdminAuthoringOutcome.Unavailable, ReasonCode: ReasonUnavailable),
        };

    private static OntologyAdminAuthoringResult<T> Invalid<T>(Dictionary<string, string[]> errors)
        where T : class => new(OntologyAdminAuthoringOutcome.Invalid, Errors: errors);

    private static OntologyAdminAuthoringResult<T> Conflict<T>(string reason)
        where T : class => new(OntologyAdminAuthoringOutcome.Conflict, ReasonCode: reason);

    private OntologyAdminAuthoringResult<T> Fault<T>(string what, Exception ex)
        where T : class
    {
        _logger.LogError(ex, "Ontology authoring: the {What} failed ({ExceptionType}).", what, ex.GetType().Name);
        return new(OntologyAdminAuthoringOutcome.Unavailable, ReasonCode: ReasonUnavailable);
    }

    private static bool IsCallerUnresolved(string? code) =>
        code is DataverseUserClientErrorCodes.UserContextRequired
            or DataverseUserClientErrorCodes.OboExchangeFailed
            or DataverseUserClientErrorCodes.OboNotConfigured;

    private static bool IsDuplicateKey(string? message) =>
        message is not null && (message.Contains("0x80060892", StringComparison.OrdinalIgnoreCase)
                                || message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
                                || message.Contains("matching key values", StringComparison.OrdinalIgnoreCase));

    private static string ODataString(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    // =================================================================================================================
    // JSON helpers
    // =================================================================================================================

    private static IEnumerable<JsonElement> Rows(JsonElement? body) =>
        body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private static string? ReadString(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? ReadInt(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i
            : null;

    private static long? ReadLong(JsonElement row, string name)
    {
        if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        return v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : null;
    }

    private static DateTime? ReadDate(JsonElement row, string name) =>
        ReadString(row, name) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
            ? at.UtcDateTime
            : null;

    private static bool TryGuid(JsonElement row, string name, out Guid id)
    {
        id = Guid.Empty;
        return ReadString(row, name) is { } text && Guid.TryParse(text, out id) && id != Guid.Empty;
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex LogicalName();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex PolicyCode();
}
