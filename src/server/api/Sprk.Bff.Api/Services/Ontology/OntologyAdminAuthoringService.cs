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
/// <para><b>The dry run's own gate (F1-1).</b> A dry run writes nothing, so Dataverse never checks the role on it; ungated, any
/// signed-in user could make the evaluator run any body (a one-bit-per-request oracle over rows they cannot read). So the dry run
/// first asks Dataverse, AS THE CALLER and fresh on every request, whether the caller holds <see cref="AuthoringPrivilegeName"/>
/// (<see cref="CheckAuthoringPrivilegeAsync"/>): not held is 403 <see cref="ReasonForbidden"/> with nothing evaluated, an unresolved
/// caller the single 403, a fault 503. This is its own check, never the capability probe's answer (task 104, D-22: display only).
/// The catalog is not gated: it is the compiler's static lists plus the triage categories the caller's own read returns.</para>
/// <para><b>Immutability (D-14).</b> No route updates a version's body, type, template or plan: the only write to an existing
/// <c>sprk_policyversion</c> is <see cref="StampInForceToAsync"/>, whose JSON holds <c>sprk_inforceto</c> and nothing else.</para>
/// <para><b>Publish order (task 033 contract).</b> Create the new version, stamp the OLD version's <c>sprk_inforceto</c>, move
/// <c>sprk_currentversion</c>, then <see cref="SignalReevaluationJob.ReevaluatePolicyAsync"/> once and check its Success. The stamp and
/// the pointer move are conditional on the row versions read first, so a concurrent publish or edit is refused (409), and a failed
/// pointer move is compensated (the old stamp undone, the new version marked never in force) so no current version is left stamped.</para>
/// <para><b>Dry run before publish (v4).</b> A dry run records, per caller and per policy, a SHA-256 of the exact lane, rule type, body
/// and template it ran; save and publish refuse a body without one (409). Any edit changes the hash. The proof binds the BODY, not
/// the data: it says this caller ran this exact rule, not that the data is unchanged since (K1).</para>
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
    internal const string ReasonForbidden = "ontology.admin.forbidden";

    /// <summary>The stable code every authoring route (validate, dry run, save, publish) returns when the body names a column outside
    /// <see cref="PolicyVersionValidator.AuthorableColumns"/> (task 102a, owner option 101(b)).</summary>
    internal const string ReasonColumnNotAuthorable = "ontology.admin.column_not_authorable";

    /// <summary>The stable code a plan refused for <see cref="DecisionPlanRefusalReason.DuplicateAction"/> carries: a code listed twice
    /// in one list, or in both <c>actions</c> and <c>nextSteps</c> (PR #2054 review). The catalog offers <c>assign-work</c> in both the
    /// Decide lane's actions and the next steps, so the wizard must pick one place for it.</summary>
    internal const string ReasonDuplicateAction = "ontology.admin.duplicate_action";

    /// <summary>Task 102b (D-165): the stable code every authoring route (validate, dry run, save, publish) returns for a matter scope on a
    /// rule whose subject is not <see cref="MatterSubject"/>. One shared check (<see cref="CheckScopeAsync"/>), like 102a's allow-list.</summary>
    internal const string ReasonScopeSubjectMismatch = "ontology.admin.scope_subject_mismatch";

    /// <summary>Task 102b fix round 1 (F4): the stable code for a dry run or publish of a policy that is limited to a matter when the
    /// request says neither which matter (<c>matterId</c>) nor <c>allMatters: true</c>, or says both. A request without a scope intent
    /// never clears a stored scope (a stale client would otherwise widen the rule silently).</summary>
    internal const string ReasonScopeIntentRequired = "ontology.admin.scope_intent_required";

    /// <summary>Task 102b (D-165): the one subject a matter scope is allowed on, and the table the matter is read from as the caller.</summary>
    internal const string MatterSubject = "sprk_matter";
    internal const string MatterEntitySet = "sprk_matters";

    /// <summary>Task 102b: the single answer for a matter that does not exist AND for one the caller cannot read (no oracle).</summary>
    internal const string MatterNotFoundMessage = "The matter was not found.";
    internal const string ScopeSubjectMismatchMessage = "A matter limit is allowed only on a rule about matters.";
    internal const string TenantRefusedMessage = "A rule has no tenant scope: every environment is single-tenant.";
    internal const string ScopeIntentRequiredMessage =
        "This rule is limited to a matter. Send that matter to keep the limit, or all matters to remove it.";
    internal const string ScopeIntentConflictMessage = "Send a matter or all matters, not both.";

    /// <summary>Task 102b fix round 4 (uac-r2 E, D-123): the stable code every authoring route (validate, dry run, save, publish) returns
    /// for a matter scope on a Secure or Restricted matter, or on one whose access level the caller's read did not return. Only reached
    /// after the caller's own read returned the matter, so it tells the caller nothing their read did not (no existence oracle).</summary>
    internal const string ReasonScopeMatterSecured = "ontology.admin.scope_matter_secured";
    internal const string ScopeMatterSecuredMessage =
        "A rule cannot be limited to a Secure or Restricted matter, or to one whose access level could not be read.";

    /// <summary>
    /// unified-access-control-r2 #2084 (owner round 102 item 4; option A3, agreed with spaarke-ontology-platform-r1): the stable code
    /// a publish returns, as a 409 with NOTHING written, when <see cref="IRecordOwnershipResolver"/> answers that a new version of the
    /// rule belongs to the Secure Record Owners team: the rule is held by a Secure matter (its own matter or the one the request
    /// names). The version would have to be created owned by that team, which the caller cannot do (no Assign on the table, and user
    /// roles are not widened). The message names no matter.
    /// </summary>
    internal const string ReasonRuleHeldSecure = "ontology.admin.rule_held_secure";
    internal const string RuleHeldSecureMessage = "This rule is held by a Secure matter's team; publish a new rule instead.";

    /// <summary>
    /// #2084 round 2: the stable code of the 503 a publish returns when the new version's owner could not be decided (a resolver
    /// refusal, an answer with no team, or a fault). Asked before any write, so NOTHING was written: the wizard says so, unlike a
    /// 503 after a write began (<see cref="ReasonUnavailable"/>), whose outcome is unknown.
    /// </summary>
    internal const string ReasonOwnerUnavailable = "ontology.admin.owner_unavailable";

    /// <summary>#2084: the rule table, as the ownership resolver names a parent (a version is filed under its rule).</summary>
    private const string PolicyTable = "sprk_policy";

    /// <summary>The privilege the dry run requires, asked FRESH on every request as the caller (F1-1): creating a policy version is
    /// what authoring a rule means (D-22).</summary>
    internal const string AuthoringPrivilegeName = "prvCreatesprk_PolicyVersion";

    /// <summary>The 400 a rule about a table with no policy subject type gets, from the dry run and from the save alike.</summary>
    private const string NoSubjectTypeMessage = "A rule about this table cannot be saved as a policy: it has no policy subject type.";

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
    internal const int DryRunProofVersion = 2;
    internal static readonly TimeSpan DryRunProofTtl = TimeSpan.FromHours(1);

    /// <summary>
    /// How long an end stamp on the current version is taken to mean "another publish is in flight" (round 3, F2). A concurrent publish
    /// holds its stamp for seconds, so a stamp this old on the version the pointer still names is a leftover of a failed publish (K2: the
    /// pointer move failed and so did the restore). Such a publish proceeds, restamps and moves the pointer, which heals the policy.
    /// </summary>
    internal static readonly TimeSpan ConcurrentPublishWindow = TimeSpan.FromMinutes(2);

    /// <summary>The policy code an in-memory dry run of a NEW rule carries (it is never written anywhere).</summary>
    internal const string DryRunPolicyCode = "DRY-RUN";

    private readonly IDataverseUserClient _user;
    private readonly PolicyVersionValidator _validator;
    private readonly IOntologyAdminAuthoringSignalEngine _engine;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly ITenantCache _cache;
    private readonly TimeProvider _clock;
    private readonly ILogger<OntologyAdminAuthoringService> _logger;

    public OntologyAdminAuthoringService(
        IDataverseUserClient user,
        PolicyVersionValidator validator,
        IOntologyAdminAuthoringSignalEngine engine,
        IRecordOwnershipResolver ownership,
        ITenantCache cache,
        TimeProvider clock,
        ILogger<OntologyAdminAuthoringService> logger)
    {
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
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

        // Task 102a: the allow-listed columns a filter may name. Two kinds are left out. A join path is named by "path", never as a
        // filter field. A Threshold column is offered as a ThresholdField: a 'when' on its stored copy is refused (task 025b).
        static IReadOnlyList<string> AuthorableOf(string entity) =>
            PolicyVersionValidator.AuthorableColumns
                .Where(c => c.Entity == entity
                            && !PredicateCompiler.ThresholdColumns.Contains(c)
                            && !PredicateCompiler.VerifiedJoins.Any(j => j.Key.Related == entity && j.Value == c.Column))
                .Select(c => c.Column)
                .Order(StringComparer.Ordinal)
                .ToList();

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
                    .Select(kv => new OntologyCatalogJoin(kv.Key.Related, kv.Value, DateOnlyOf(kv.Key.Related), AuthorableOf(kv.Key.Related)))
                    .ToList(),
                AuthorableOf(subject)))
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
            Triage: new OntologyCatalogTriage(TriageEntity, TriageField, categories),
            Lanes: [LaneCatalog(DecisionLane.Decide), LaneCatalog(DecisionLane.Do)]);
    }

    /// <summary>
    /// One lane's authoring lists (task 102a, owner option A), each read from its server source:
    /// <list type="bullet">
    /// <item>actions: <see cref="DecisionActionCatalog"/> codes whose plan lane is this one, the same test as
    /// <see cref="DecisionPlanService.Resolve"/> (which also refuses a substitution-only action, task 161, so none is listed, nor named
    /// as an exclusion);</item>
    /// <item>next steps: its <c>IsNextStep</c> codes;</item>
    /// <item>dismissal reasons: <see cref="DecisionActionCatalog.DismissalReasons"/>;</item>
    /// <item>severities and work types: the <c>sprk_policy</c> option labels of <see cref="OntologyAdminRulesService"/>, which
    /// OntologyAdminRulesSchemaAgreementTests pins to the solution source.</item>
    /// </list>
    /// The server has no lane mapping for severities or work types, so each lane carries the full list.
    /// </summary>
    internal static OntologyCatalogLane LaneCatalog(DecisionLane lane)
    {
        static OntologyCatalogAction ActionOf(DecisionActionDefinition a) => new(a.Code, a.Label,
            a.Excludes.Where(code => !(DecisionActionCatalog.TryGet(code, out var excluded) && excluded.SubstitutionOnly)).ToList());
        static IReadOnlyList<OntologyCatalogOption> OptionsOf(IReadOnlyDictionary<int, string> labels) =>
            labels.OrderBy(kv => kv.Key).Select(kv => new OntologyCatalogOption(kv.Key, kv.Value)).ToList();

        return new OntologyCatalogLane(
            lane.ToString(),
            DecisionActionCatalog.All.Where(a => a.PlanLane == lane && !a.SubstitutionOnly).Select(ActionOf).ToList(),
            DecisionActionCatalog.All.Where(a => a.IsNextStep).Select(ActionOf).ToList(),
            DecisionActionCatalog.DismissalReasons(lane).Select(r => new OntologyCatalogChoice(r.Code, r.Label)).ToList(),
            OptionsOf(OntologyAdminRulesService.SeverityLabels),
            OptionsOf(OntologyAdminRulesService.WorkTypeLabels));
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

            // F1-1: the privilege, asked as the caller on THIS request, before anything is read or evaluated.
            if (await CheckAuthoringPrivilegeAsync(caller.UserId, ct).ConfigureAwait(false) is { } privilegeFailure)
            {
                return Fail<OntologyDryRunResult>(privilegeFailure);
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

            var check = ValidateRule(request.RuleType, request.RuleBody, request.MessageTemplate, errors);
            var ruleTypeName = check.RuleTypeName;
            if (errors.Count == 0 && policy is null && !TrySubjectType(request.RuleBody!, out _))
            {
                // K5: a new rule the save would refuse for its subject is refused here, before it is evaluated.
                errors["ruleBody"] = [NoSubjectTypeMessage];
            }

            // Task 102b (D-165): the scope, through the one check save and publish use; the dry run evaluates within it.
            // Fix round 1 (F4): for an existing policy limited to a matter, the request must say which scope it means.
            var scope = await CheckScopeAsync(request.MatterId, request.AllMatters, policy?.MatterId, request.Tenant, request.RuleBody, errors, ct)
                .ConfigureAwait(false);
            if (scope.Failure is { } scopeFailure)
            {
                return Fail<OntologyDryRunResult>(scopeFailure);
            }

            if (errors.Count > 0)
            {
                return Invalid<OntologyDryRunResult>(errors, check.ReasonCode ?? scope.ReasonCode);
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
            // DI-112 (task 102b): within the matter scope, the same subject-scoped evaluation the publish re-evaluation runs, so the
            // counts below are the ones publishing produces.
            var evaluation = await _engine.EvaluateAsync(spec, request.MatterId, ct).ConfigureAwait(false);
            if (!evaluation.PolicyValid)
            {
                // ValidateForSave passed, so this is the evaluation-time gate disagreeing: never show a partial answer.
                return Invalid<OntologyDryRunResult>(new Dictionary<string, string[]>
                {
                    ["ruleBody"] = ["The rule could not be prepared for evaluation."],
                });
            }

            if (AnyHitOutside(evaluation.Hits, request.MatterId))
            {
                _logger.LogError("Ontology dry run: the evaluation returned a subject outside the rule's matter scope; refusing rather than showing it.");
                return Fail<OntologyDryRunResult>(Failure.Unavailable);
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
            // Fix round 1 (F4): the proof records the policy's STORED scope as read now, so a publish after another admin changed it is
            // refused (the counts confirmed here were worked out against the scope it replaces).
            var recorded = await RecordProofAsync(
                    proofScope.Value, policy?.Id, lane, ruleTypeName!, request.RuleBody!, request.MessageTemplate, request.MatterId, policy?.MatterId, ct)
                .ConfigureAwait(false);

            // Fix round 1 (F4): the stored scope, so the wizard can say when the publish changes it; the name only when the caller reads it.
            var storedName = policy?.MatterId is { } stored ? await ReadMatterNameAsync(stored, ct).ConfigureAwait(false) : null;
            return new(OntologyAdminAuthoringOutcome.Ok, result with
            {
                PolicyEnabled = policy is null ? null : policy.Enabled && policy.Active,
                Recorded = recorded,
                StoredMatterId = policy?.MatterId,
                StoredMatterName = storedName,
            });
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
    // Validate (task 102a): compile only
    // =================================================================================================================

    /// <summary>
    /// Checks a draft the way save (a new rule) or publish (with a policy id) would, and returns the field errors and the fields a template
    /// may name. It compiles only: nothing is written, nothing is evaluated, and no dry-run proof is recorded or needed.
    /// </summary>
    /// <remarks>Authorization is the dry run's: WhoAmI as the caller, then the caller's <see cref="AuthoringPrivilegeName"/> asked fresh
    /// as the caller, before anything in the request is read. A policy id is read as the caller through the Ontology domain allow-list
    /// (the uniform 404). The rule is checked by <see cref="ValidateDraft"/>, the path save and publish use.</remarks>
    public async Task<OntologyAdminAuthoringResult<OntologyRuleValidation>> ValidateAsync(OntologyValidateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var caller = await ResolveCallerAsync(ct).ConfigureAwait(false);
            if (caller.Failure is { } failure)
            {
                return Fail<OntologyRuleValidation>(failure);
            }

            if (await CheckAuthoringPrivilegeAsync(caller.UserId, ct).ConfigureAwait(false) is { } privilegeFailure)
            {
                return Fail<OntologyRuleValidation>(privilegeFailure);
            }

            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            DecisionLane? lane = null;
            var newRule = true;
            Guid? storedMatter = null;
            if (request.PolicyId is { } policyId)
            {
                if (policyId == Guid.Empty)
                {
                    return Fail<OntologyRuleValidation>(Failure.NotFound);
                }

                var read = await ReadPolicyAsync(policyId, ct).ConfigureAwait(false);
                if (read.Failure is { } readFailure)
                {
                    return Fail<OntologyRuleValidation>(readFailure);
                }

                lane = read.Value!.Lane; // as publish: the policy's own lane, never the request's
                storedMatter = read.Value!.MatterId; // fix round 2 (K2): the stored scope, as the dry run and publish read it
                newRule = false;
            }
            else if (TryParseLane(request.Lane, out var parsedLane))
            {
                lane = parsedLane;
            }
            else
            {
                errors["lane"] = ["The lane is Decide or Do."];
            }

            var check = RuleCheck.None;
            if (request.Draft is not { } draft)
            {
                errors["draft"] = ["The rule is required."];
            }
            else
            {
                check = ValidateDraft(draft, lane, errors);
                if (errors.Count == 0 && newRule && !TrySubjectType(draft.RuleBody!, out _))
                {
                    errors["ruleBody"] = [NoSubjectTypeMessage]; // as save: a new rule needs a policy subject type
                }
            }

            // Task 102b: the scope, through the one check the dry run, save and publish use. Fix round 2 (K2): with the stored scope and the
            // request's intent, so validate refuses the same no-intent and matter-plus-allMatters cases, with the same codes and fields.
            var scope = await CheckScopeAsync(request.MatterId, request.AllMatters, storedMatter, request.Tenant, request.Draft?.RuleBody, errors, ct)
                .ConfigureAwait(false);
            if (scope.Failure is { } scopeFailure)
            {
                return Fail<OntologyRuleValidation>(scopeFailure);
            }

            var list = errors
                .OrderBy(e => e.Key, StringComparer.Ordinal)
                .SelectMany(e => e.Value.Select(message => new OntologyValidationError(e.Key, message)))
                .ToList();
            return new(OntologyAdminAuthoringOutcome.Ok,
                new OntologyRuleValidation(errors.Count == 0, list, check.ReasonCode ?? scope.ReasonCode, check.TemplateEligibleFields));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Fault<OntologyRuleValidation>("validate", ex);
        }
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
            string? reasonCode = null;
            if (draft is null)
            {
                errors["draft"] = ["The rule is required."];
            }
            else
            {
                var check = ValidateDraft(draft, errors.ContainsKey("lane") ? null : lane, errors);
                ruleTypeName = check.RuleTypeName;
                reasonCode = check.ReasonCode;
            }

            var subjectType = 0;
            if (errors.Count == 0 && !TrySubjectType(draft!.RuleBody!, out subjectType))
            {
                errors["ruleBody"] = [NoSubjectTypeMessage];
            }

            // Task 102b (D-165): the scope; the matter is read as the caller before anything is written.
            var scope = await CheckScopeAsync(request.MatterId, null, null, request.Tenant, draft?.RuleBody, errors, ct).ConfigureAwait(false);
            if (scope.Failure is { } scopeFailure)
            {
                return Fail<OntologyPolicySaved>(scopeFailure);
            }

            if (errors.Count > 0)
            {
                return Invalid<OntologyPolicySaved>(errors, reasonCode ?? scope.ReasonCode);
            }

            if (await ReadProofAsync(proofScope.Value, null, lane, ruleTypeName!, draft!.RuleBody!, draft.MessageTemplate, request.MatterId, ct)
                    .ConfigureAwait(false) is null)
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
            if (request.MatterId is { } matter) policyBody[MatterBind] = MatterReference(matter); // sprk_tenant is never written (D-165)

            var created = await _user.PostAsync(ApiRoot + PolicyEntitySet, JsonSerializer.Serialize(policyBody), ct).ConfigureAwait(false);
            if (!created.IsSuccess)
            {
                if (created.StatusCode is 409 or 412 || (created.StatusCode == 400 && IsDuplicateKey(created.ErrorMessage)))
                {
                    return Conflict<OntologyPolicySaved>(ReasonPolicyCodeTaken);
                }

                return Fail<OntologyPolicySaved>(WriteFailure(created, "policy create"));
            }

            // The pointer. The policy is Off, so nothing evaluates it while this is pending. The policy now exists, so this runs to the
            // end even if the request is abandoned (CancellationToken.None), and a failure (an answer or a throw) leaves an Off policy
            // whose first version exists but is not yet current: reported with that policy's id (K6), so the client can recover by
            // publishing a version of it, rather than hidden.
            DataverseUserResponse pointer;
            try
            {
                pointer = await _user.PatchAsync(
                    $"{PolicyEntitySet}({policyId:D})", CurrentVersionJson(versionId), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ontology authoring: policy {PolicyId} was created Off but setting its current version threw ({ExceptionType}).",
                    policyId, ex.GetType().Name);
                return Fail<OntologyPolicySaved>(Failure.Unavailable) with { PolicyId = policyId };
            }

            if (!pointer.IsSuccess)
            {
                // EVERY failure here is the "saved Off, publish a version to finish" 503 with the policy's id (review pass 2, F1-b): a
                // refusal or an unresolved caller must not read as "nothing was written", because the policy and its version exist.
                _logger.LogError("Ontology authoring: policy {PolicyId} was created Off but its current version could not be set ({ErrorCode}, {StatusCode}).",
                    policyId, pointer.ErrorCode, pointer.StatusCode);
                return Fail<OntologyPolicySaved>(Failure.Unavailable) with { PolicyId = policyId };
            }

            await ForgetProofAsync(proofScope.Value, null, lane, ruleTypeName!, draft.RuleBody!, draft.MessageTemplate, request.MatterId, CancellationToken.None)
                .ConfigureAwait(false);
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

            var check = ValidateDraft(draft, policy.Lane, errors);
            ruleTypeName = check.RuleTypeName;

            // Task 102b (D-165): the scope this version is published with (null: all matters), checked by the shared scope check.
            // Fix round 1 (F4): a policy limited to a matter is published only with an explicit scope intent; none never clears it.
            var scope = await CheckScopeAsync(request.MatterId, request.AllMatters, policy.MatterId, request.Tenant, draft.RuleBody, errors, ct)
                .ConfigureAwait(false);
            if (scope.Failure is { } scopeFailure)
            {
                return Fail<OntologyVersionPublished>(scopeFailure);
            }

            if (errors.Count > 0)
            {
                return Invalid<OntologyVersionPublished>(errors, check.ReasonCode ?? scope.ReasonCode);
            }

            if (await ReadProofAsync(proofScope.Value, policy.Id, policy.Lane, ruleTypeName!, draft.RuleBody!, draft.MessageTemplate, request.MatterId, ct)
                    .ConfigureAwait(false) is not { } proof)
            {
                return Conflict<OntologyVersionPublished>(ReasonDryRunRequired);
            }

            if (proof.StoredMatter != policy.MatterId)
            {
                // Fix round 1 (F4): another admin changed the policy's stored scope after this dry run, so the change the admin confirmed
                // ("from X to Y") is not the one this publish would make. Refused before any write.
                _logger.LogInformation("Ontology authoring: the matter scope of policy {PolicyId} changed since the dry run; refusing.", policy.Id);
                return Conflict<OntologyVersionPublished>(ReasonPolicyChanged);
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

                if (previous.Ended && IsFreshStamp(previous.EndedAt))
                {
                    // The current version carries a FRESH end stamp: a concurrent publish is between its stamp and its pointer move.
                    // Refused before any write (review pass 2, F1-a): stamping it again and then undoing "our" stamp would wipe the end of
                    // a version that has really ended. A stamp older than ConcurrentPublishWindow is a leftover of a failed publish
                    // (K2 double failure) and falls through: this publish restamps it and moves the pointer, which heals it (round 3).
                    _logger.LogInformation("Ontology authoring: the current version {VersionId} of policy {PolicyId} carries a fresh end stamp; refusing.",
                        previous.Id, policy.Id);
                    return Conflict<OntologyVersionPublished>(ReasonPolicyChanged);
                }
            }

            // #2084: the new version's owner, asked BEFORE any write (the version, the end stamp, the pointer and scope). A rule held by
            // a Secure matter is refused with nothing written; an ordinary answer binds no owner, so the version stays the caller's.
            if (await CheckVersionOwnerAsync(policy, request.MatterId, ct).ConfigureAwait(false) is { } held)
            {
                return held;
            }

            var number = versions.Value!.Count == 0 ? 1 : versions.Value!.Max(v => v.Number) + 1;
            var versionId = Guid.NewGuid();
            var now = NowForDataverse();

            // (1) the new version. Nothing is written before this, so the request's token still applies.
            var created = await _user.PostAsync(
                ApiRoot + VersionEntitySet,
                JsonSerializer.Serialize(VersionPayload(versionId, policy.Id, policy.Code, number, ruleTypeName!, draft, caller.UserId, now)), ct)
                .ConfigureAwait(false);
            if (!created.IsSuccess)
            {
                return Fail<OntologyVersionPublished>(WriteFailure(created, "version create"));
            }

            // From here a write has landed: steps 2 to 4 and every compensation run to the end on CancellationToken.None, so a timeout or
            // a client disconnect can never stop the publish half way (F2-2). (2) and (3) are in StampAndMoveAsync.
            if (await StampAndMoveAsync(policy, previous, versionId, now, request.MatterId).ConfigureAwait(false) is { } refused)
            {
                return refused;
            }

            await ForgetProofAsync(
                    proofScope.Value, policy.Id, policy.Lane, ruleTypeName!, draft.RuleBody!, draft.MessageTemplate, request.MatterId, CancellationToken.None)
                .ConfigureAwait(false);

            // (4) supersede and re-raise, as the evaluator, once (task 033 contract). Only an enabled, active policy is evaluated; an Off
            // policy has no open Signals to supersede (the nightly run closes them PolicyRetired) and is evaluated when it is turned On.
            // Task 102b: a matter-scoped policy is evaluated within its matter; its open Signals outside the scope cite the old version, so
            // the same pass closes them Superseded and never re-raises them (SignalReevaluationJob, "closure on scope change").
            var reevaluation = ReevaluationNotEvaluated;
            int superseded = 0, raised = 0;
            if (policy.Enabled)
            {
                try
                {
                    var pass = await _engine.ReevaluateAsync(policy.Id, CancellationToken.None).ConfigureAwait(false);
                    superseded = pass.SupersededClosed;
                    raised = pass.Raised;
                    reevaluation = pass.Success ? ReevaluationCompleted : ReevaluationFailed;
                    if (!pass.Success)
                    {
                        _logger.LogError("Ontology authoring: version {VersionId} of policy {PolicyId} is published, but the supersede pass " +
                            "did not complete ({Summary}); the nightly run retries.", versionId, policy.Id, pass.FailureSummary);
                    }
                }
                catch (Exception ex)
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
    /// unified-access-control-r2 #2084 (owner round 102 item 4, option A3): who would own the version this publish creates, decided by
    /// <see cref="IRecordOwnershipResolver"/> before anything is written. The version is filed under its rule; the rule's stored matter
    /// and the matter the request names are passed too, so a rule whose matter turned Secure is caught while the reconciler has not yet
    /// re-owned it, and so is a requested matter that turned Secure after <see cref="CheckScopeAsync"/> read it (secure-if-any).
    /// <list type="bullet">
    /// <item>The Secure Record Owners team, refused with nothing written: the caller cannot create a row owned by that team (no Assign
    /// on the table, and user roles are not widened), and a version owned by the caller would expose the rule's text. Round 2: the
    /// answer names what made it Secure. The rule itself is held (its <c>ownerid</c>, read with the policy, is that team): 409
    /// <see cref="ReasonRuleHeldSecure"/>. Otherwise, when the request names a matter, that matter turned Secure after
    /// <see cref="CheckScopeAsync"/> read it: the 400 <see cref="ReasonScopeMatterSecured"/> the scope check gives. With no matter
    /// named, only the rule's own stored matter can be Secure (the reconciler has not yet re-owned the rule): 409
    /// <see cref="ReasonRuleHeldSecure"/>.</item>
    /// <item>An ordinary team, or <see cref="RecordOwnerOutcome.Unchanged"/>: null. No <c>ownerid</c> is bound; the version stays the
    /// caller's, as before #2084.</item>
    /// <item>A refusal (the owner could not be determined), an answer with no team, or a fault: 503 <see cref="ReasonOwnerUnavailable"/>,
    /// fail closed, nothing written.</item>
    /// </list>
    /// </summary>
    private async Task<OntologyAdminAuthoringResult<OntologyVersionPublished>?> CheckVersionOwnerAsync(
        PolicyRow policy, Guid? matterScope, CancellationToken ct)
    {
        var parents = new List<RecordOwnershipParent> { new(PolicyTable, policy.Id) };
        foreach (var matter in new[] { policy.MatterId, matterScope })
        {
            if (matter is { } id && id != Guid.Empty)
            {
                parents.Add(new RecordOwnershipParent(MatterSubject, id));
            }
        }

        // D-197 / UAC round 137: author wins, including the root; a child table here would make 194b refuse root-homed authors.
        RecordOwnerResolution owner;
        try
        {
            owner = await _ownership.ResolveOwnerAsync(
                new RecordOwnershipContext { Parents = parents.Distinct().ToArray(), WhenUnfiled = UnfiledOwnership.KeepCreator }, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Ontology authoring: the owner of a new version of policy {PolicyId} could not be resolved ({ExceptionType}); " +
                "nothing was written.", policy.Id, ex.GetType().Name);
            return Fail<OntologyVersionPublished>(Failure.OwnerUnavailable);
        }

        if (owner.IsOwned && owner.IsSecureOwner)
        {
            if (policy.OwnerId != owner.OwningTeamId && matterScope is { } requested && requested != Guid.Empty
                && (policy.MatterId is not { } stored || stored == Guid.Empty || stored == requested))
            {
                // Round 2: the rule is not held, and the requested matter is the only matter that can have made the answer Secure
                // (the rule stores no other), so it turned Secure after the scope check read it. When the rule still stores a
                // DIFFERENT matter, that matter may be the cause (Secure, not yet re-owned); blaming the requested one would name a
                // matter the caller cannot change, so that case falls through to "held" (round 3, ontology F3).
                _logger.LogInformation("Ontology authoring: the matter a publish of policy {PolicyId} names is now Secure; the publish is " +
                    "refused and nothing was written.", policy.Id);
                return Invalid<OntologyVersionPublished>(
                    new Dictionary<string, string[]>(StringComparer.Ordinal) { ["matterId"] = [ScopeMatterSecuredMessage] }, ReasonScopeMatterSecured);
            }

            _logger.LogInformation("Ontology authoring: policy {PolicyId} is held by a Secure matter's team; the publish is refused and nothing was written.",
                policy.Id);
            return Conflict<OntologyVersionPublished>(ReasonRuleHeldSecure);
        }

        if (owner.IsOwned || owner.Outcome == RecordOwnerOutcome.Unchanged)
        {
            return null;
        }

        _logger.LogWarning("Ontology authoring: the owner of a new version of policy {PolicyId} could not be determined ({RefusalCode}); " +
            "nothing was written.", policy.Id, owner.RefusalCode ?? "no team");
        return Fail<OntologyVersionPublished>(Failure.OwnerUnavailable);
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

    /// <summary>
    /// Steps (2) and (3) of a publish, after the new version exists, all on <see cref="CancellationToken.None"/> (F2-2): stamp the old
    /// version's <c>sprk_inforceto</c>, then move <c>sprk_currentversion</c>, each conditional on the row as read. Returns null when the
    /// pointer names the new version (step 4 follows), otherwise the answer to return, after compensating.
    /// </summary>
    /// <remarks>An answer other than 412, or a throw (a transport timeout surfaces as <see cref="TaskCanceledException"/>, which the
    /// caller's client does not catch), says nothing about whether the write landed, so the row is re-read as the caller first (F2-3):
    /// a write that landed is a success, never compensated, and a compensation never stamps the version that is current.</remarks>
    private async Task<OntologyAdminAuthoringResult<OntologyVersionPublished>?> StampAndMoveAsync(
        PolicyRow policy, VersionRow? previous, Guid versionId, string now, Guid? matterScope)
    {
        try
        {
            // (2) the OLD version's sprk_inforceto, conditional on the row as read (a concurrent publish stamped it: refuse)
            if (previous is not null)
            {
                var stamped = await StampInForceToAsync(previous.Id, now, previous.RowVersion, CancellationToken.None).ConfigureAwait(false);
                if (!stamped.IsSuccess)
                {
                    if (stamped.StatusCode == 412)
                    {
                        await RetireUnpublishedAsync(policy.Id, versionId, now).ConfigureAwait(false);
                        return Conflict<OntologyVersionPublished>(ReasonPolicyChanged);
                    }

                    if (!await CarriesStampAsync(previous.Id, now).ConfigureAwait(false))
                    {
                        await CompensateAsync(policy.Id, previous, versionId, now).ConfigureAwait(false);
                        return Fail<OntologyVersionPublished>(WriteFailure(stamped, "previous version end"));
                    }

                    _logger.LogWarning("Ontology authoring: the end stamp of version {VersionId} was answered {ErrorCode} but landed; continuing.",
                        previous.Id, stamped.ErrorCode);
                }
            }

            // (3) the pointer, conditional on the policy as read (an On/Off, Retire or another publish meanwhile: refuse and undo). Task
            // 102b: a scope change rides in the same conditional write, so the pointer and the scope move together or not at all.
            var moved = await _user.PatchAsync(
                $"{PolicyEntitySet}({policy.Id:D})", PointerJson(versionId, policy.MatterId, matterScope), policy.RowVersion, CancellationToken.None)
                .ConfigureAwait(false);
            if (moved.IsSuccess)
            {
                return null;
            }

            if (moved.StatusCode == 412)
            {
                await CompensateAsync(policy.Id, previous, versionId, now).ConfigureAwait(false);
                return Conflict<OntologyVersionPublished>(ReasonPolicyChanged);
            }

            return await SettleUnknownPointerAsync(policy.Id, previous, versionId, now, WriteFailure(moved, "current version")).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ontology authoring: publishing version {VersionId} of policy {PolicyId} threw after the version was created " +
                "({ExceptionType}); re-reading the pointer before compensating.", versionId, policy.Id, ex.GetType().Name);
            return await SettleUnknownPointerAsync(policy.Id, previous, versionId, now, Failure.Unavailable).ConfigureAwait(false);
        }
    }

    /// <summary>After a step whose outcome is unknown: the pointer names the new version (it landed: null, step 4 follows), names
    /// another version (compensate, then <paramref name="failure"/>), or cannot be read (nothing is undone on a guess: 503, logged).</summary>
    private async Task<OntologyAdminAuthoringResult<OntologyVersionPublished>?> SettleUnknownPointerAsync(
        Guid policyId, VersionRow? previous, Guid versionId, string now, Failure failure)
    {
        switch (await PointerOfAsync(policyId, versionId).ConfigureAwait(false))
        {
            case Pointer.New:
                _logger.LogWarning("Ontology authoring: the pointer move of policy {PolicyId} failed to answer but landed on version {VersionId}; continuing.",
                    policyId, versionId);
                return null;
            case Pointer.Other:
                await CompensateAsync(policyId, previous, versionId, now).ConfigureAwait(false);
                return Fail<OntologyVersionPublished>(failure);
            default:
                _logger.LogError("Ontology authoring: whether policy {PolicyId} now names version {VersionId} is unknown (its pointer could not be " +
                    "re-read); nothing was undone. The nightly run reports a current version with an end stamp (EventId 50309).", policyId, versionId);
                return Fail<OntologyVersionPublished>(Failure.Unavailable);
        }
    }

    /// <summary>Where a policy's <c>sprk_currentversion</c> points, re-read as the caller: at the version asked about (<see cref="Pointer.New"/>;
    /// the new version for a publish, the previous one for <see cref="RestorePreviousAsync"/>), at another, or unknown.</summary>
    private enum Pointer
    {
        New,
        Other,
        Unknown,
    }

    private async Task<Pointer> PointerOfAsync(Guid policyId, Guid versionId)
    {
        try
        {
            var response = await _user.GetAsync(
                $"{PolicyEntitySet}?$select=sprk_policyid,_sprk_currentversion_value&$filter=sprk_policyid eq {policyId:D}", CancellationToken.None)
                .ConfigureAwait(false);
            if (response.IsSuccess
                && Rows(response.Body).FirstOrDefault(r => TryGuid(r, "sprk_policyid", out var id) && id == policyId) is { ValueKind: JsonValueKind.Object } row)
            {
                return TryGuid(row, "_sprk_currentversion_value", out var current) && current == versionId ? Pointer.New : Pointer.Other;
            }

            _logger.LogWarning("Ontology authoring: the pointer of policy {PolicyId} could not be re-read ({ErrorCode}).", policyId, response.ErrorCode ?? "no-row");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Ontology authoring: re-reading the pointer of policy {PolicyId} threw ({ExceptionType}).", policyId, ex.GetType().Name);
        }

        return Pointer.Unknown;
    }

    /// <summary>Whether the old version carries exactly the end stamp this publish wrote (re-read as the caller; unreadable is "no").</summary>
    private async Task<bool> CarriesStampAsync(Guid previousId, string stamp)
    {
        try
        {
            var reread = await _user.GetAsync($"{VersionEntitySet}({previousId:D})?$select=sprk_inforceto,versionnumber", CancellationToken.None)
                .ConfigureAwait(false);
            return reread.IsSuccess && reread.Body is { } row && ReadDate(row, "sprk_inforceto") is { } inForceTo && FormatForDataverse(inForceTo) == stamp;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Ontology authoring: re-reading version {VersionId} threw ({ExceptionType}).", previousId, ex.GetType().Name);
            return false;
        }
    }

    /// <summary>Compensation for a publish whose pointer did not move: the old version's stamp is undone (if it is ours) and the new
    /// version is marked never in force (if it is not current).</summary>
    private async Task CompensateAsync(Guid policyId, VersionRow? previous, Guid versionId, string now)
    {
        if (previous is not null)
        {
            await RestorePreviousAsync(policyId, previous.Id, now).ConfigureAwait(false);
        }

        await RetireUnpublishedAsync(policyId, versionId, now).ConfigureAwait(false);
    }

    /// <summary>Compensation: a created version that never became current is marked never in force (in force to = in force from). The
    /// pointer is re-read first, and the version that is current is NEVER stamped (F2-3), nor one whose pointer cannot be read.</summary>
    private async Task RetireUnpublishedAsync(Guid policyId, Guid versionId, string now)
    {
        var pointer = await PointerOfAsync(policyId, versionId).ConfigureAwait(false);
        if (pointer != Pointer.Other)
        {
            _logger.LogError("Ontology authoring: version {VersionId} was not marked never in force: the pointer of policy {PolicyId} {State}.",
                versionId, policyId, pointer == Pointer.New ? "names it" : "could not be read");
            return;
        }

        try
        {
            var undone = await StampInForceToAsync(versionId, now, null, CancellationToken.None).ConfigureAwait(false);
            if (undone.IsSuccess)
            {
                return;
            }

            _logger.LogError("Ontology authoring: version {VersionId} was created but not published, and could not be marked never in force ({ErrorCode}).",
                versionId, undone.ErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ontology authoring: version {VersionId} was created but not published, and marking it never in force threw ({ExceptionType}).",
                versionId, ex.GetType().Name);
        }
    }

    /// <summary>Compensation: the old version's stamp is undone, but only while the pointer (re-read as the caller) still names it and
    /// only if it is still exactly the stamp this publish wrote. A version the pointer has moved away from has really ended: its stamp
    /// stands (review pass 2, F1-a). An unreadable pointer undoes nothing on a guess.</summary>
    private async Task RestorePreviousAsync(Guid policyId, Guid previousId, string stamp)
    {
        // Pointer.New here means "names previousId" (PointerOfAsync compares against the id it is given).
        switch (await PointerOfAsync(policyId, previousId).ConfigureAwait(false))
        {
            case Pointer.Other:
                _logger.LogInformation("Ontology authoring: policy {PolicyId} no longer names version {VersionId}; its end stamp stands.",
                    policyId, previousId);
                return;
            case Pointer.Unknown:
                _logger.LogError("Ontology authoring: the end stamp of version {VersionId} was not undone: the pointer of policy {PolicyId} could not " +
                    "be re-read. The nightly run reports a current version with an end stamp (EventId 50309) until it is fixed.", previousId, policyId);
                return;
        }

        try
        {
            var reread = await _user.GetAsync($"{VersionEntitySet}({previousId:D})?$select=sprk_inforceto,versionnumber", CancellationToken.None)
                .ConfigureAwait(false);
            if (reread.IsSuccess && reread.Body is { } row)
            {
                if (ReadDate(row, "sprk_inforceto") is not { } inForceTo || FormatForDataverse(inForceTo) != stamp)
                {
                    return; // not this publish's stamp (it never landed, or another writer's): nothing of ours to undo
                }

                if (ReadLong(row, "versionnumber") is { } rowVersion
                    && (await StampInForceToAsync(previousId, null, rowVersion, CancellationToken.None).ConfigureAwait(false)).IsSuccess)
                {
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Ontology authoring: undoing the end stamp of version {VersionId} threw ({ExceptionType}).", previousId, ex.GetType().Name);
        }

        _logger.LogError("Ontology authoring: the previous version {VersionId} keeps the end stamp of a publish that did not complete; " +
            "the nightly run reports it (EventId 50309) until sprk_currentversion or sprk_inforceto is fixed.", previousId);
    }

    // =================================================================================================================
    // Validation (ValidateForSave + the plan check)
    // =================================================================================================================

    /// <summary>What <see cref="ValidateRule"/> decided: the rule type's name on success (null otherwise), the fields a template may name
    /// (filled whenever the body compiled), and a stable reason code for a refusal that has one.</summary>
    internal readonly record struct RuleCheck(string? RuleTypeName, IReadOnlyList<string> TemplateEligibleFields, string? ReasonCode)
    {
        public static RuleCheck None => new(null, Array.Empty<string>(), null);
    }

    /// <summary>
    /// THE validation of a draft's rule (type, body, template), shared by validate, dry run, save and publish so they cannot disagree
    /// (task 102a). It calls <see cref="PolicyVersionValidator.ValidateForAuthoring"/>, which is <c>ValidateForSave</c> plus the
    /// authorable-column allow-list. It adds field-level errors and, for a column outside the allow-list, returns
    /// <see cref="ReasonColumnNotAuthorable"/>.
    /// </summary>
    private RuleCheck ValidateRule(string? ruleType, string? ruleBody, string? messageTemplate, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(ruleBody))
        {
            errors["ruleBody"] = ["The rule body is required."];
            return RuleCheck.None;
        }

        if (messageTemplate is { Length: > MaxMessageTemplateLength })
        {
            errors["messageTemplate"] = [$"The message template is at most {MaxMessageTemplateLength} characters."];
        }

        var authoring = _validator.ValidateForAuthoring(ruleType, ruleBody, messageTemplate);
        var result = authoring.Validation;
        if (!result.IsValid)
        {
            var field = result.Reason switch
            {
                OntologyWriterFailureReason.RuleTypeUnsupported => "ruleType",
                OntologyWriterFailureReason.TemplateMalformed or OntologyWriterFailureReason.TemplateTokenOutsideReadSet => "messageTemplate",
                _ => "ruleBody",
            };
            errors[field] = result.Errors.ToArray();
            return new RuleCheck(null, authoring.TemplateEligibleFields,
                result.Reason == OntologyWriterFailureReason.ColumnNotAuthorable ? ReasonColumnNotAuthorable : null);
        }

        // ValidateForAuthoring accepted it, so the type is Existence or Threshold (Switch is refused, D-16).
        return new RuleCheck(RuleBodySchemaValidator.TryParseRuleType(ruleType, out var parsed) ? parsed.ToString() : null,
            authoring.TemplateEligibleFields, null);
    }

    /// <summary>A whole draft for save, publish or validate: the rule, the required columns, and the plan against the lane's catalog
    /// (task 036). The rule type's name is null unless the whole draft is valid.</summary>
    private RuleCheck ValidateDraft(OntologyRuleDraft draft, DecisionLane? lane, Dictionary<string, string[]> errors)
    {
        var check = ValidateRule(draft.RuleType, draft.RuleBody, draft.MessageTemplate, errors);
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
            if (plan.IsRefused && plan.RefusalReason == DecisionPlanRefusalReason.DuplicateAction)
            {
                errors["decisionPlan"] = [$"The decision plan is refused ({plan.RefusalReason}): each code may appear once, in actions or in " +
                    "nextSteps, never twice in one list and never in both."];
                check = check with { ReasonCode = check.ReasonCode ?? ReasonDuplicateAction };
            }
            else if (plan.IsRefused)
            {
                errors["decisionPlan"] = [$"The decision plan is refused ({plan.RefusalReason}): it must list actions from the {l} lane's catalog."];
            }
        }

        return errors.Count == 0 ? check : check with { RuleTypeName = null };
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
    // Scope (task 102b, D-165): ONE check, shared by validate, dry run, save and publish
    // =================================================================================================================

    /// <summary>What <see cref="CheckScopeAsync"/> decided: a stable reason code for a refusal that has one, or the failure of the caller's
    /// read of the matter (the single 403 or a 503).</summary>
    private readonly record struct ScopeCheck(string? ReasonCode, Failure? Failure);

    /// <summary>
    /// THE scope check (task 102b, D-165), shared by validate, dry run, save and publish so they cannot disagree. It runs after the rule's
    /// own validation and adds field errors to <paramref name="errors"/>:
    /// <list type="bullet">
    /// <item>a non-blank <paramref name="tenant"/> is refused: the tenant scope stays blank (every environment is single-tenant,
    /// <c>CommunicationRuleGate</c>), and the tenant is never read from a request;</item>
    /// <item>a <paramref name="matterId"/> on a rule whose subject (the draft body's, which is what the evaluator evaluates) is not
    /// <see cref="MatterSubject"/> is refused with <see cref="ReasonScopeSubjectMismatch"/>;</item>
    /// <item>otherwise the matter is read AS THE CALLER (the dry run's readable-ids read): a matter that does not exist and one the caller
    /// cannot read get the same <see cref="MatterNotFoundMessage"/>, so the answer is no oracle. The read is skipped when the request is
    /// refused anyway.</item>
    /// <item>(fix round 4, uac-r2 E) a matter the caller's read returns as Secure or Restricted, or with an access level it did not return,
    /// is refused with <see cref="ReasonScopeMatterSecured"/> (<see cref="ReadMatterAccessAsync"/>).</item>
    /// </list>
    /// No matter id means all matters: the rule behaves exactly as before task 102b, except (fix round 1, F4) on a dry run or publish of a
    /// policy whose <paramref name="storedMatter"/> is set: there, no matter id needs <paramref name="allMatters"/> true, or the request is
    /// refused with <see cref="ReasonScopeIntentRequired"/>; a matter id with <paramref name="allMatters"/> true is refused the same way.
    /// Save passes null for both (a save only ever creates a new rule, which has no stored scope). Validate (fix round 2, K2) passes the
    /// stored scope of the policy it is asked about, and null for a new rule.
    /// </summary>
    private async Task<ScopeCheck> CheckScopeAsync(
        Guid? matterId, bool? allMatters, Guid? storedMatter, string? tenant, string? ruleBody, Dictionary<string, string[]> errors,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(tenant))
        {
            errors["tenant"] = [TenantRefusedMessage];
        }

        // Fix round 1 (F4): the scope intent. A matter AND all matters is a contradiction, whatever is stored.
        if (matterId is not null && allMatters == true)
        {
            errors["matterId"] = [ScopeIntentConflictMessage];
            return new ScopeCheck(ReasonScopeIntentRequired, null);
        }

        if (matterId is not { } matter)
        {
            if (storedMatter is not null && allMatters != true)
            {
                // A policy limited to a matter, and a request that names no scope (a stale client, or one that never knew of scopes):
                // refused, so the limit is never cleared by omission. A stored null with no intent stays unscoped (as before 102b).
                errors["matterId"] = [ScopeIntentRequiredMessage];
                return new ScopeCheck(ReasonScopeIntentRequired, null);
            }

            return default;
        }

        if (RuleSubjectOf(ruleBody) is { } subject && !string.Equals(subject, MatterSubject, StringComparison.Ordinal))
        {
            errors["matterId"] = [ScopeSubjectMismatchMessage];
            return new ScopeCheck(ReasonScopeSubjectMismatch, null);
        }

        if (errors.Count > 0)
        {
            return default; // refused for another reason: nothing is read on its behalf
        }

        if (matter == Guid.Empty)
        {
            errors["matterId"] = [MatterNotFoundMessage];
            return default;
        }

        var access = await ReadMatterAccessAsync(matter, ct).ConfigureAwait(false);
        if (access.Failure is { } failure)
        {
            return new ScopeCheck(null, failure);
        }

        if (access.Flags is not { } flags)
        {
            errors["matterId"] = [MatterNotFoundMessage]; // absent or unreadable: one answer
            return default;
        }

        // Fix round 4 (uac-r2 E, D-123): the rule row is not a secure-family record and a reader of sprk_policy (Console User held Read at Deep depth
        // until D-185, task 163; Ontology Administrator still does) sees every Ontology rule, so a
        // limit on a Secure or Restricted matter would show a non-member the matter link and what is watched on it. Refused; an access
        // level the caller's read did not return (a masked sprk_issecure) is refused the same way, never read as ordinary.
        if (flags.IsUnreadable || flags.IsSecure || flags.IsRestricted)
        {
            errors["matterId"] = [ScopeMatterSecuredMessage];
            return new ScopeCheck(ReasonScopeMatterSecured, null);
        }

        return default;
    }

    /// <summary>Fix round 4: what the caller's read of a matter scope returned.</summary>
    /// <param name="Flags">The matter's access flags, mapped by the one rule the access services use
    /// (<see cref="Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalParticipationService.FlagsFrom"/>); null when the read returned no row
    /// (the matter does not exist, or the caller cannot read it).</param>
    /// <param name="Failure">The read failed: the scope is neither allowed nor refused on a guess.</param>
    private readonly record struct MatterAccess(Sprk.Bff.Api.Infrastructure.ExternalAccess.RootRecordFlags? Flags, Failure? Failure);

    /// <summary>
    /// Fix round 4 (uac-r2 E): reads the scope matter AS THE CALLER with its access columns
    /// (<see cref="Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalParticipationService.RootFlagColumns"/>: <c>sprk_issecure</c>,
    /// <c>sprk_accesspermission</c>, <c>statecode</c>) and maps them with <c>ExternalParticipationService.FlagsFrom</c>, the canonical
    /// Secure / Restricted rule (an EMPTY <c>sprk_issecure</c> is <see cref="Sprk.Bff.Api.Infrastructure.ExternalAccess.RootRecordFlags.IsUnreadable"/>).
    /// The caller's own read is the existence check as before (no row: not found), so a matter the caller cannot read is never classified.
    /// Fails closed: an unresolved caller, a non-success other than a refusal, or a fault is a failure, never "allowed".
    /// </summary>
    private async Task<MatterAccess> ReadMatterAccessAsync(Guid matterId, CancellationToken ct)
    {
        DataverseUserResponse response;
        try
        {
            response = await _user.GetAsync(
                $"{MatterEntitySet}?$select=sprk_matterid,{Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalParticipationService.RootFlagColumns}"
                + $"&$filter=sprk_matterid eq {matterId:D}&$top=1", ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Ontology authoring: the caller's read of the scope matter failed ({ExceptionType}).", ex.GetType().Name);
            return new MatterAccess(null, Failure.Unavailable);
        }

        if (!response.IsSuccess)
        {
            if (IsCallerUnresolved(response.ErrorCode)) return new MatterAccess(null, Failure.CallerUnresolved);
            if (response.ErrorCode == DataverseUserClientErrorCodes.AccessDenied) return new MatterAccess(null, null);
            _logger.LogWarning("Ontology authoring: the caller's read of the scope matter failed ({ErrorCode}).", response.ErrorCode);
            return new MatterAccess(null, Failure.Unavailable);
        }

        foreach (var row in Rows(response.Body))
        {
            if (!TryGuid(row, "sprk_matterid", out var id) || id != matterId) continue;
            bool? isSecure = row.TryGetProperty("sprk_issecure", out var secure) && secure.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? secure.GetBoolean()
                : null;
            return new MatterAccess(
                Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalParticipationService.FlagsFrom(
                    isSecure, ReadInt(row, "sprk_accesspermission"), ReadInt(row, "statecode")),
                null);
        }

        return new MatterAccess(null, null);
    }

    /// <summary>The rule body's <c>subject</c>, or null when the body cannot be read (its own validation reports that).</summary>
    private static string? RuleSubjectOf(string? ruleBody)
    {
        if (string.IsNullOrWhiteSpace(ruleBody))
        {
            return null;
        }

        try
        {
            using var doc = PredicateCompiler.ParseBounded(ruleBody);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("subject", out var subject)
                   && subject.ValueKind == JsonValueKind.String
                ? subject.GetString()
                : null;
        }
        catch (Exception ex) when (ex is JsonException or PredicateCompilationException)
        {
            return null;
        }
    }

    /// <summary>A hit the evaluator returned outside the matter scope. The scoped query makes it impossible; if it ever happens the
    /// answer is refused (fail closed), never shown or counted.</summary>
    private static bool AnyHitOutside(IReadOnlyList<SignalHit> hits, Guid? matterScope) =>
        matterScope is { } matter
        && hits.Any(h => !string.Equals(h.SubjectEntity, MatterSubject, StringComparison.Ordinal) || h.SubjectId != matter);

    /// <summary>Fix round 1 (F4): the stored matter's name as the CALLER reads it, for the dry run's answer (the query shape
    /// <c>OntologyAdminRulesService</c> uses for the rule detail). Null when the caller cannot read it or the read fails: the id is still
    /// returned, and the name is display only, so it never fails the dry run.</summary>
    private async Task<string?> ReadMatterNameAsync(Guid matterId, CancellationToken ct)
    {
        try
        {
            var response = await _user.GetAsync(
                $"{MatterEntitySet}?$select=sprk_mattername&$filter=sprk_matterid eq {matterId:D}&$top=1", ct).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                if (response.ErrorCode != DataverseUserClientErrorCodes.AccessDenied)
                {
                    _logger.LogWarning("Ontology dry run: the stored matter's name could not be read ({ErrorCode}).", response.ErrorCode);
                }

                return null;
            }

            return Rows(response.Body).Select(row => ReadString(row, "sprk_mattername")).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Fix round 2 (uac-r2 F1): the name is display only, so a fault (an HttpClient timeout is a TaskCanceledException while the
            // request is still live) never fails the dry run. The matter name is not logged.
            _logger.LogWarning("Ontology dry run: the stored matter's name could not be read ({ExceptionType}).", ex.GetType().Name);
            return null;
        }
    }

    // =================================================================================================================
    // The dry-run proof (v4: a test run before save; any edit invalidates it)
    // =================================================================================================================

    internal readonly record struct ProofOwner(string TenantId, Guid CallerId);

    private static ProofOwner? ProofScope(ClaimsPrincipal principal, Guid callerId) =>
        TenantResolution.ResolveTenantId(principal) is { Length: > 0 } tenant && callerId != Guid.Empty
            ? new ProofOwner(tenant, callerId)
            : null;

    /// <summary>The proof's key: the caller (the Dataverse user WhoAmI named), the policy (or "new"), and a SHA-256 of the exact lane
    /// (K2: a new rule dry-run in one lane is not a proof for saving it in the other), rule type, body and template, length-prefixed so
    /// no two drafts share a text. It binds the body, not the data (K1). Task 102b: and the matter scope, so a test run within one matter
    /// is not a proof for another or for all matters (the counts the admin confirmed depend on it). Without a scope the text is exactly
    /// the pre-102b one.</summary>
    internal static string ProofKey(
        Guid callerId, Guid? policyId, DecisionLane lane, string ruleType, string ruleBody, string? messageTemplate, Guid? matterId = null)
    {
        var template = messageTemplate ?? string.Empty;
        var scope = matterId is { } matter ? "\nmatter:" + matter.ToString("N") : string.Empty;
        var canonical = string.Create(CultureInfo.InvariantCulture,
            $"v2\n{(int)lane}\n{ruleType}\n{ruleBody.Length}:{ruleBody}\n{template.Length}:{template}{scope}");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return $"{callerId:N}:{(policyId is { } p ? p.ToString("N") : "new")}:{hash}";
    }

    /// <summary>A recorded dry run: when, and (fix round 1, F4) the policy's STORED matter scope at that time (null for a new rule or an
    /// unscoped policy). Publish refuses with <see cref="ReasonPolicyChanged"/> when the stored scope is no longer this one.</summary>
    internal sealed record DryRunProof(DateTimeOffset At, Guid? StoredMatter);

    private async Task<bool> RecordProofAsync(
        ProofOwner owner, Guid? policyId, DecisionLane lane, string ruleType, string ruleBody, string? template, Guid? matterId, Guid? storedMatter,
        CancellationToken ct)
    {
        try
        {
            await _cache.SetAsync(owner.TenantId, DryRunProofResource, ProofKey(owner.CallerId, policyId, lane, ruleType, ruleBody, template, matterId),
                DryRunProofVersion, new DryRunProof(_clock.GetUtcNow(), storedMatter), DryRunProofTtl, ct: ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Ontology dry run: the proof could not be recorded ({ExceptionType}); save and publish will refuse this body.",
                ex.GetType().Name);
            return false;
        }
    }

    private async Task<DryRunProof?> ReadProofAsync(
        ProofOwner owner, Guid? policyId, DecisionLane lane, string ruleType, string ruleBody, string? template, Guid? matterId, CancellationToken ct)
    {
        try
        {
            return await _cache.GetAsync<DryRunProof>(owner.TenantId, DryRunProofResource,
                ProofKey(owner.CallerId, policyId, lane, ruleType, ruleBody, template, matterId), DryRunProofVersion, ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Fail closed: an unreadable proof is no proof.
            _logger.LogWarning("Ontology authoring: the dry-run proof could not be read ({ExceptionType}); refusing.", ex.GetType().Name);
            return null;
        }
    }

    private async Task ForgetProofAsync(
        ProofOwner owner, Guid? policyId, DecisionLane lane, string ruleType, string ruleBody, string? template, Guid? matterId, CancellationToken ct)
    {
        try
        {
            await _cache.RemoveAsync(owner.TenantId, DryRunProofResource, ProofKey(owner.CallerId, policyId, lane, ruleType, ruleBody, template, matterId),
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
        Forbidden,
        NotFound,
        Unavailable,

        /// <summary>#2084 round 2: the new version's owner could not be decided, before any write (<see cref="ReasonOwnerUnavailable"/>).</summary>
        OwnerUnavailable,
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

    /// <summary>
    /// F1-1: whether the caller holds <see cref="AuthoringPrivilegeName"/>, asked FRESH on this request AS THE CALLER (never cached,
    /// never the capability probe's answer: D-22 keeps that display only). The function is bound to the id <c>WhoAmI</c> returned,
    /// never a request value, and counts privileges held through a team's roles. Null when held; otherwise
    /// <see cref="Failure.Forbidden"/> (not held, or Dataverse refusing to answer for the caller: fail closed), the single 403 for an
    /// unresolved caller, or <see cref="Failure.Unavailable"/> (a fault or an unreadable answer: 503, fail closed).
    /// </summary>
    private async Task<Failure?> CheckAuthoringPrivilegeAsync(Guid callerId, CancellationToken ct)
    {
        var response = await _user.GetAsync(
            $"systemusers({callerId:D})/Microsoft.Dynamics.CRM.RetrieveUserPrivilegeByPrivilegeName(PrivilegeName='{AuthoringPrivilegeName}')", ct)
            .ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            if (IsCallerUnresolved(response.ErrorCode)) return Failure.CallerUnresolved;
            if (response.ErrorCode == DataverseUserClientErrorCodes.AccessDenied)
            {
                _logger.LogInformation("Ontology authoring: Dataverse refused the caller's privilege read; forbidden.");
                return Failure.Forbidden;
            }

            _logger.LogWarning("Ontology authoring: {Privilege} could not be read for the caller ({ErrorCode}); unavailable.",
                AuthoringPrivilegeName, response.ErrorCode);
            return Failure.Unavailable;
        }

        if (response.Body is not { ValueKind: JsonValueKind.Object } body
            || !body.TryGetProperty("RolePrivileges", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            // A body without the array is unparseable, never "not held".
            _logger.LogWarning("Ontology authoring: the {Privilege} answer could not be read; unavailable.", AuthoringPrivilegeName);
            return Failure.Unavailable;
        }

        var held = rows.EnumerateArray().Any(row => string.Equals(ReadString(row, "PrivilegeName"), AuthoringPrivilegeName, StringComparison.OrdinalIgnoreCase));
        if (!held)
        {
            _logger.LogInformation("Ontology authoring: the caller does not hold {Privilege}; the dry run is forbidden.", AuthoringPrivilegeName);
        }

        return held ? null : Failure.Forbidden;
    }

    /// <param name="MatterId">Task 102b: the policy's matter scope (<c>sprk_matter</c>), null for all matters.</param>
    /// <param name="OwnerId">#2084 round 2: the policy's current owner (<c>ownerid</c>, a user or a team), as the same read returns it.</param>
    internal sealed record PolicyRow(
        Guid Id, string Code, DecisionLane Lane, bool Enabled, bool Active, Guid? CurrentVersionId, long RowVersion, Guid? MatterId = null,
        Guid? OwnerId = null);

    /// <summary>One policy AS THE CALLER, through the domain allow-list. Absent, unreadable or not an Ontology policy: the uniform 404.</summary>
    private async Task<Read<PolicyRow>> ReadPolicyAsync(Guid policyId, CancellationToken ct)
    {
        var response = await _user.GetAsync(
            $"{PolicyEntitySet}?$select=sprk_policyid,sprk_policycode,sprk_lane,sprk_enabled,statecode,_sprk_currentversion_value,versionnumber,_sprk_matter_value,_ownerid_value,{OntologyDomain.Column}"
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
            rowVersion,
            TryGuid(row, "_sprk_matter_value", out var matter) ? matter : null,
            TryGuid(row, "_ownerid_value", out var owner) ? owner : null));
    }

    /// <summary>A version of the policy as the caller reads it. <paramref name="Ended"/>: it carries an <c>sprk_inforceto</c>.
    /// <paramref name="EndedAt"/>: that stamp as a UTC instant; null when it is absent or cannot be parsed.</summary>
    internal sealed record VersionRow(Guid Id, int Number, long? RowVersion, bool Ended, DateTime? EndedAt);

    private async Task<Read<IReadOnlyList<VersionRow>>> ReadVersionsAsync(Guid policyId, CancellationToken ct)
    {
        var response = await _user.GetAsync(
            $"{VersionEntitySet}?$select=sprk_policyversionid,sprk_versionnumber,sprk_inforceto,versionnumber&$filter=_sprk_policy_value eq {policyId:D}", ct)
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
                return new VersionRow(id, ReadInt(r, "sprk_versionnumber") ?? 0, ReadLong(r, "versionnumber"),
                    Ended: ReadString(r, "sprk_inforceto") is { Length: > 0 }, EndedAt: ReadDate(r, "sprk_inforceto"));
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

    /// <summary>Task 102b: the publish's pointer move, plus the policy's matter scope when this publish changes it (a new matter, or
    /// <c>null</c> to clear it: all matters). An unchanged scope sends exactly the pre-102b body.</summary>
    private static string PointerJson(Guid versionId, Guid? currentScope, Guid? requestedScope)
    {
        if (currentScope == requestedScope)
        {
            return CurrentVersionJson(versionId);
        }

        return JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["sprk_CurrentVersion@odata.bind"] = $"/{VersionEntitySet}({versionId:D})",
            [MatterBind] = requestedScope is { } matter ? MatterReference(matter) : null,
        });
    }

    /// <summary>The <c>sprk_policy.sprk_matter</c> lookup's bind (navigation property <c>sprk_Matter</c>, live spaarkedev1 2026-10-17).</summary>
    private const string MatterBind = "sprk_Matter@odata.bind";

    private static string MatterReference(Guid matterId) => $"/{MatterEntitySet}({matterId:D})";

    /// <summary>
    /// Whether an end stamp on the CURRENT version is recent enough to belong to a publish still in flight. A concurrent publish sits
    /// between its stamp and its pointer move for seconds. Older than <see cref="ConcurrentPublishWindow"/> is a leftover of a failed
    /// publish. An unparseable stamp (<paramref name="endedAt"/> null) or one in the future counts as fresh: the request is refused (fails closed).
    /// Uses the same clock the stamp is written with (<see cref="NowForDataverse"/>).
    /// </summary>
    private bool IsFreshStamp(DateTime? endedAt) =>
        endedAt is not { } at || _clock.GetUtcNow().UtcDateTime - at < ConcurrentPublishWindow;

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
            Failure.Forbidden => new(OntologyAdminAuthoringOutcome.Forbidden, ReasonCode: ReasonForbidden),
            Failure.NotFound => new(OntologyAdminAuthoringOutcome.NotFound, ReasonCode: ReasonNotFound),
            Failure.OwnerUnavailable => new(OntologyAdminAuthoringOutcome.Unavailable, ReasonCode: ReasonOwnerUnavailable),
            _ => new(OntologyAdminAuthoringOutcome.Unavailable, ReasonCode: ReasonUnavailable),
        };

    private static OntologyAdminAuthoringResult<T> Invalid<T>(Dictionary<string, string[]> errors, string? reasonCode = null)
        where T : class => new(OntologyAdminAuthoringOutcome.Invalid, Errors: errors, ReasonCode: reasonCode);

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
