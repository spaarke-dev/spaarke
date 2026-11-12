using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// ebilling-r1 task 084 Part A: the world, the rights probe and the REAL <see cref="RecordProcedureReadService"/> shared by the read-model
/// tests and the HTTP tests. Rows live in the in-memory Dataverse of <see cref="SecureChildShareWorld"/> (the strict
/// <see cref="IGenericEntityService"/> over it has no Create setup, so a write from a route fails loudly as well as being verified).
/// Chain: project P is filed under matter M; work assignment W is filed under P (and M, when a test says so).
/// </summary>
internal sealed class RecordProcedureRig
{
    public const string Matter = SecureRootInheritance.Matter;
    public const string Project = SecureRootInheritance.Project;
    public const string WorkAssignment = SecureRootInheritance.WorkAssignment;
    public const string RuleSet = "sprk_ruleset";
    public const string Policy = "sprk_policy";
    public const string PolicyVersion = "sprk_policyversion";
    public const string RecordType = SecureRootInheritance.RecordTypeRefEntity;

    public static readonly Guid F = Guid.Parse("08400000-0000-4000-8000-0000000000f1");
    public static readonly Guid G = Guid.Parse("08400000-0000-4000-8000-0000000000f2");
    public static readonly Guid M = Guid.Parse("08400000-0000-4000-8000-0000000000a1");
    public static readonly Guid P = Guid.Parse("08400000-0000-4000-8000-0000000000b1");
    public static readonly Guid W = Guid.Parse("08400000-0000-4000-8000-0000000000c1");
    public static readonly Guid MatterType = Guid.Parse("08400000-0000-4000-8000-0000000000e1");
    public static readonly Guid Reviewer = Guid.Parse("08400000-0000-4000-8000-0000000000d1");

    public static readonly DateOnly Jan1 = new(2026, 1, 1);
    public static readonly DateOnly Jan15 = new(2026, 1, 15);
    public static readonly DateOnly Feb1 = new(2026, 2, 1);
    public static readonly DateOnly Feb10 = new(2026, 2, 10);
    public static readonly DateOnly Mar1 = new(2026, 3, 1);
    public static readonly DateOnly Mar15 = new(2026, 3, 15);

    public const string ValidRuleBody = "{\"type\":\"ApprovedRate\",\"tolerancePercent\":0}";

    public SecureChildShareWorld World { get; } = SecureChildShareWorld.Standard();

    public Mock<IGenericEntityService> Entities { get; }

    public RightsProbe Probe { get; } = new();

    public RecordProcedureReadService Service { get; }

    /// <summary>What the service logged at Warning or above: (level, exception type).</summary>
    public CapturingLogger Log { get; } = new();

    /// <summary>084e fix round 1: everything the billing gate (the validator the service holds) logged, at every level.</summary>
    public Sprk.Bff.Api.Tests.Services.Communication.CapturingLogger<PolicyVersionValidator> GateLog { get; } = new();

    private readonly Dictionary<Guid, Guid> _setOf = new();
    private DateTime _created = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    private int _nextId;
    private readonly Dictionary<Guid, string> _procedureNames = new();
    private readonly Dictionary<Guid, string> _ruleCodes = new();

    /// <summary>
    /// The next row id. Ids are a counter, not random, so two rigs seeded the same way hold the same ids and a 200 body can be compared
    /// byte for byte across them (the fix round 3 guard removes one row from a fresh rig and compares).
    /// </summary>
    private Guid NextId() => new($"08400000-0000-4000-9000-{++_nextId:x12}");

    /// <summary>
    /// Every Procedure Version and policy version row seeded, with a label that names it by what it is (not by id), in seeding order:
    /// (entity set, id, label). The fix round 3 guard walks these.
    /// </summary>
    public List<(string Set, Guid Id, string Label)> VersionRows { get; } = new();

    public RecordProcedureRig()
    {
        Entities = SecureChildShareWorld.EntitiesOver(() => World);
        var schema = new RuleBodySchemaValidator();
        var validator = new PolicyVersionValidator(
            schema, new PredicateCompiler(schema, new FakeTimeProvider(new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero))),
            GateLog);
        var describer = new RuleBodyDescriber(validator, Entities.Object, NullLogger<RuleBodyDescriber>.Instance);
        // 084e: the evaluator's own legacy-rule read, over the same world (only LoadLegacyRulesAsync is used; it reads through Entities alone).
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var legacyRules = new BillingFactLoader(
            Entities.Object, new Mock<IEbillingSettingsReader>(MockBehavior.Strict).Object,
            new SecureChildShareSynchronizer(Entities.Object, null!, null!, configuration, NullLogger<SecureChildShareSynchronizer>.Instance),
            configuration, new Sprk.Bff.Api.Telemetry.FinanceTelemetry(), NullLogger<BillingFactLoader>.Instance);
        Service = new RecordProcedureReadService(Entities.Object, Probe, describer, legacyRules, validator, Log)
        {
            Clock = new FakeTimeProvider(new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.Zero)),
        };

        World.Add(RecordType, MatterType, ("sprk_recordlogicalname", "sprk_matter"));
        World.Add(Matter, M, ("sprk_mattername", "Matter M"));
        World.Add(Project, P, ("sprk_projectname", "Project P"),
            (SecureRootInheritance.PairIdColumn, M.ToString()), (SecureRootInheritance.PairTypeColumn, Ref(RecordType, MatterType)));
        World.Add(WorkAssignment, W, ("sprk_name", "Work W"), ("sprk_regardingproject", Ref(Project, P)));
        World.Add("sprk_organization", F, ("sprk_organizationname", "Firm F"));
        World.Add("sprk_organization", G, ("sprk_organizationname", "Firm G"));
        World.Add("systemuser", Reviewer, ("fullname", "Rita Reviewer"));
    }

    // ── Seeding (the shapes ProcedureResolverTests seeds) ───────────────────────────────────────────────────

    public static EntityReference Ref(string table, Guid id) => new(table, id);

    public static OptionSetValue Option(int value) => new(value);

    private static DateTime Utc(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static DateTime Stored(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    public DateTime Tick() => _created = _created.AddMinutes(1);

    public Guid AddProcedure(string name, ProcedureAppliesTo appliesTo, Guid? firm = null, int status = ProcedureEntity.StatusActive, ProcedureType? type = null)
    {
        var id = NextId();
        var columns = new List<(string, object)>
        {
            (ProcedureEntity.AreaColumn, Option(RuleDomains.PolicyDomainBilling)),
            (ProcedureEntity.StatusColumn, Option(status)),
            (ProcedureEntity.NameColumn, name),
            (ProcedureEntity.AppliesToColumn, Option((int)appliesTo)),
        };
        if (firm is { } f) columns.Add((ProcedureEntity.FirmColumn, Ref("sprk_organization", f)));
        if (type is { } t) columns.Add((ProcedureEntity.TypeColumn, Option((int)t)));
        World.Add(ProcedureEntity.Entity, id, columns.ToArray());
        _procedureNames[id] = name;

        var set = NextId();
        World.Add(RuleSet, set,
            ("sprk_procedure", Ref(ProcedureEntity.Entity, id)), (RuleDomains.PolicyDomainColumn, Option(RuleDomains.PolicyDomainBilling)),
            ("statecode", Option(0)));
        _setOf[id] = set;
        return id;
    }

    public Guid AddVersion(Guid procedure, int number, DateOnly? from, DateOnly? to, int status = ProcedureEntity.VersionStatusActive)
    {
        var id = NextId();
        var columns = new List<(string, object)>
        {
            (ProcedureEntity.VersionProcedureColumn, Ref(ProcedureEntity.Entity, procedure)),
            (ProcedureEntity.VersionNumberColumn, number),
            (ProcedureEntity.VersionStatusColumn, Option(status)),
        };
        if (status is ProcedureEntity.VersionStatusActive or ProcedureEntity.VersionStatusSuperseded)
            columns.Add((ProcedureEntity.VersionPublishedOnColumn, new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc)));
        if (from is { } f) columns.Add((ProcedureEntity.VersionEffectiveFromColumn, Stored(f)));
        if (to is { } t) columns.Add((ProcedureEntity.VersionEffectiveToColumn, Stored(t)));
        World.Add(ProcedureEntity.VersionEntity, id, columns.ToArray());
        VersionRows.Add((ProcedureEntity.VersionEntitySet, id, $"Procedure version {number} of '{_procedureNames.GetValueOrDefault(procedure, "?")}'"));
        return id;
    }

    public Guid AddRule(Guid set, string code, Guid? id = null)
    {
        id ??= NextId();
        World.Add(Policy, id.Value,
            ("sprk_policycode", code), ("sprk_ruleset", Ref(RuleSet, set)), ("sprk_enabled", true), ("statecode", Option(0)),
            (RuleDomains.PolicyDomainColumn, Option(RuleDomains.PolicyDomainBilling)));
        _ruleCodes[id.Value] = code;
        return id.Value;
    }

    public Guid AddProcedureRule(Guid procedure, string code) => AddRule(_setOf[procedure], code);

    /// <summary>The Procedure's rule set (created with the Procedure).</summary>
    public Guid RuleSetOf(Guid procedure) => _setOf[procedure];

    public Guid AddRuleVersion(Guid policy, Guid? procedureVersion, DateOnly from, int number = 1, Guid? basedOn = null, DateTime? created = null, string? headline = null,
        RuleType type = RuleType.ApprovedRate, string? body = null, int? consequence = null, DateOnly? to = null)
    {
        var id = NextId();
        var columns = new List<(string, object)>
        {
            ("sprk_policy", Ref(Policy, policy)),
            ("sprk_versionnumber", number),
            ("sprk_ruletype", Option((int)type)),
            ("sprk_rulebody", body ?? ValidRuleBody),
            ("sprk_shortheadline", headline ?? "Approved rate"),
            ("sprk_inforcefrom", Utc(from)),
            (RuleSetService.PublishStateColumn, Option(RuleSetService.PublishStatePublished)),
        };
        if (procedureVersion is { } pv) columns.Add((RuleSetService.ProcedureVersionColumn, Ref(ProcedureEntity.VersionEntity, pv)));
        if (basedOn is { } b) columns.Add((ProcedureAssignmentService.BasedOnVersionColumn, Ref(PolicyVersion, b)));
        if (created is { } c) columns.Add(("createdon", c));
        if (consequence is { } stated) columns.Add(("sprk_consequence", Option(stated)));
        if (to is { } end) columns.Add(("sprk_inforceto", Utc(end)));
        World.Add(PolicyVersion, id, columns.ToArray());
        VersionRows.Add(("sprk_policyversions", id, $"policy version {number} of rule '{_ruleCodes.GetValueOrDefault(policy, "?")}' (in force from {from:yyyy-MM-dd})"));
        return id;
    }

    /// <summary>The ids of a legacy rule (<see cref="AddLegacyRule"/>): its rule set, policy and current version.</summary>
    public sealed record LegacyRule(Guid RuleSet, Guid Policy, Guid Version);

    private readonly Dictionary<(RuleSetScope, Guid?), Guid> _legacySets = new();

    /// <summary>
    /// 084e: a LEGACY rule, as <see cref="BillingFactLoader.LoadLegacyRulesAsync"/> reads it: a Published billing rule set with no Procedure, scope
    /// Client default (no firm) or Firm (<paramref name="firm"/>; one set per scope and firm), an enabled active policy, and its current version in
    /// force from <paramref name="from"/>.
    /// </summary>
    public LegacyRule AddLegacyRule(
        string code, RuleSetScope scope, Guid? firm = null, RuleType type = RuleType.ApprovedRate, string? body = null, int? consequence = null,
        DateOnly? from = null, DateOnly? to = null)
    {
        if (!_legacySets.TryGetValue((scope, firm), out var set))
        {
            set = NextId();
            var setColumns = new List<(string, object)>
            {
                ("sprk_scope", Option((int)scope)), ("statuscode", Option(659490000)), ("statecode", Option(0)),
                (RuleDomains.PolicyDomainColumn, Option(RuleDomains.PolicyDomainBilling)),
            };
            if (firm is { } f) setColumns.Add(("sprk_firm", Ref("sprk_organization", f)));
            World.Add(RuleSet, set, setColumns.ToArray());
            _legacySets[(scope, firm)] = set;
            LegacyRows.Add(("sprk_rulesets", set, $"legacy {scope} rule set first holding '{code}'"));
        }

        var policy = AddRule(set, code);
        var version = AddRuleVersion(policy, null, from ?? Jan1, type: type, body: body, consequence: consequence, to: to);
        SetCurrentVersion(policy, code, set, version);
        LegacyRows.Add(("sprk_policies", policy, $"legacy rule '{code}'"));
        return new LegacyRule(set, policy, version);
    }

    /// <summary>
    /// 084e fix round 1: another version of a legacy rule, in force from <paramref name="from"/>. With <paramref name="current"/> it becomes the
    /// policy's current version (<c>sprk_currentversion</c>); the earlier one then needs an end date to stay in force on a date before it.
    /// </summary>
    public Guid AddLegacyVersion(LegacyRule rule, string code, int number, DateOnly from, bool current)
    {
        var version = AddRuleVersion(rule.Policy, null, from, number: number);
        if (current)
        {
            SetCurrentVersion(rule.Policy, code, rule.RuleSet, version);
        }

        return version;
    }

    private void SetCurrentVersion(Guid policy, string code, Guid set, Guid version) =>
        World.Add(Policy, policy,
            ("sprk_policycode", code), ("sprk_ruleset", Ref(RuleSet, set)), ("sprk_enabled", true), ("statecode", Option(0)),
            (RuleDomains.PolicyDomainColumn, Option(RuleDomains.PolicyDomainBilling)), ("sprk_currentversion", Ref(PolicyVersion, version)));

    /// <summary>
    /// 084e fix round 1: every LEGACY rule set and legacy policy row seeded (entity set, id, label), in seeding order. The removal guard walks these
    /// after <see cref="VersionRows"/>, so a legacy row the body depends on cannot go unasked either.
    /// </summary>
    public List<(string Set, Guid Id, string Label)> LegacyRows { get; } = new();

    private Guid AddRow(string table, Guid record, DateTime? created, params (string Column, object Value)[] columns)
    {
        var id = NextId();
        var all = new List<(string, object)>(columns) { ("statecode", Option(0)), (table, Ref(table, record)) };
        if (created is { } c) all.Add(("createdon", c));
        World.Add(ProcedureAssignmentEntity.Entity, id, all.ToArray());
        return id;
    }

    /// <summary>A Procedure-level row on a record: FollowLatest, Pinned (with <paramref name="pinned"/>) or Excluded.</summary>
    public Guid Assign(string table, Guid record, Guid procedure, int mode = ProcedureAssignmentEntity.ModeFollowLatest, Guid? pinned = null,
        string? reason = null, Guid? by = null)
    {
        var columns = new List<(string, object)>
        {
            (ProcedureAssignmentEntity.ProcedureColumn, Ref(ProcedureEntity.Entity, procedure)),
            (ProcedureAssignmentEntity.ModeColumn, Option(mode)),
        };
        if (pinned is { } v) columns.Add((ProcedureAssignmentEntity.PinnedVersionColumn, Ref(ProcedureEntity.VersionEntity, v)));
        if (reason is not null) columns.Add((ProcedureAssignmentEntity.ReasonColumn, reason));
        if (by is { } person) columns.Add(("sprk_createdbyperson", Ref("systemuser", person)));
        return AddRow(table, record, Tick(), columns.ToArray());
    }

    public Guid ExcludeRule(string table, Guid record, Guid policy, Guid? firm = null, DateTime? created = null, string? reason = null, Guid? by = null)
    {
        var columns = new List<(string, object)>
        {
            (ProcedureAssignmentEntity.ExcludedPolicyColumn, Ref(Policy, policy)),
            (ProcedureAssignmentEntity.ModeColumn, Option(ProcedureAssignmentEntity.ModeExcluded)),
        };
        if (firm is { } f) columns.Add((ProcedureAssignmentEntity.FirmColumn, Ref("sprk_organization", f)));
        if (reason is not null) columns.Add((ProcedureAssignmentEntity.ReasonColumn, reason));
        if (by is { } person) columns.Add(("sprk_createdbyperson", Ref("systemuser", person)));
        return AddRow(table, record, created ?? Tick(), columns.ToArray());
    }

    /// <summary>A changed rule (089c): the base rule's Exclude row plus the record rule's first version based on <paramref name="baseVersion"/>.</summary>
    public Guid ChangeRule(string table, Guid record, Guid basePolicy, Guid baseVersion, DateOnly from, Guid? firm = null, string code = "BRR-1", string? reason = null) =>
        ChangeRuleRows(table, record, basePolicy, baseVersion, from, firm, code, reason).RecordRule;

    /// <summary>The rows <see cref="ChangeRuleRows"/> seeded: the record rule, the record rule set, the base rule's Exclude row and the record rule's version.</summary>
    public sealed record ChangedRows(Guid RecordRule, Guid RuleSet, Guid Exclude, Guid RuleVersion);

    /// <summary>As <see cref="ChangeRule"/>, naming every row it seeded.</summary>
    public ChangedRows ChangeRuleRows(string table, Guid record, Guid basePolicy, Guid baseVersion, DateOnly from, Guid? firm = null, string code = "BRR-1", string? reason = null)
    {
        var at = Tick();
        var exclude = ExcludeRule(table, record, basePolicy, firm, at, reason);
        var set = ProcedureAssignmentService.RecordRuleSetIdFor(table, record, firm);
        var setColumns = new List<(string, object)>
        {
            ("sprk_scope", Option(firm is null ? RecordRuleSet.ScopeRecord : RecordRuleSet.ScopeFirmRecord)),
            (RuleDomains.PolicyDomainColumn, Option(RuleDomains.PolicyDomainBilling)), ("statecode", Option(0)), (table, Ref(table, record)),
        };
        if (firm is { } f) setColumns.Add(("sprk_firm", Ref("sprk_organization", f)));
        World.Add(RuleSet, set, setColumns.ToArray());
        var recordRule = AddRule(set, code, ProcedureAssignmentService.RecordRulePolicyIdFor(set, basePolicy));
        var ruleVersion = AddRuleVersion(recordRule, null, from, basedOn: baseVersion, created: at, headline: "Changed rate");
        return new ChangedRows(recordRule, set, exclude, ruleVersion);
    }

    /// <summary>The ids of <see cref="SeedChangedAndBaseRetired"/>: a changed rule that is still in force on Mar15 and a record rule whose base is gone by then.</summary>
    public sealed record ChangedAndRetired(
        Guid Procedure, Guid Version, Guid ChangedBase, Guid ChangedBaseV1, Guid ChangedBaseV2, ChangedRows Changed,
        Guid RetiredBase, Guid RetiredBaseVersion, ChangedRows Retired, Guid FirstVersion);

    /// <summary>
    /// One Procedure (all records; version 1 to Mar 1, version 2 after). GENERAL is published under both versions and changed on the record
    /// (so on Mar15 it is a Changed rule, Stale because version 2 moved on); DROPPED is published under version 1 only and changed on the
    /// record (so on Mar15 it is a BaseRetired record rule). GENERAL is changed on matter M and DROPPED on project P, so the two record rule sets
    /// differ. Ask for project P, Mar15.
    /// </summary>
    public ChangedAndRetired SeedChangedAndBaseRetired()
    {
        var procedure = AddProcedure("Guideline", ProcedureAppliesTo.AllRecords);
        var v1 = AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusSuperseded);
        var v2 = AddVersion(procedure, 2, Mar1, null);
        var general = AddProcedureRule(procedure, "GENERAL");
        var generalV1 = AddRuleVersion(general, v1, Jan1);
        var generalV2 = AddRuleVersion(general, v2, Mar1, number: 2);
        var dropped = AddProcedureRule(procedure, "DROPPED");
        var droppedVersion = AddRuleVersion(dropped, v1, Jan1);
        var changed = ChangeRuleRows(Matter, M, general, generalV1, Jan15, code: "BRR-GENERAL");
        var retired = ChangeRuleRows(Project, P, dropped, droppedVersion, Jan15, code: "BRR-DROPPED");
        return new ChangedAndRetired(procedure, v2, general, generalV1, generalV2, changed, dropped, droppedVersion, retired, v1);
    }

    /// <summary>A Procedure with one version [from, open) and one rule <paramref name="code"/> published under it.</summary>
    public (Guid Procedure, Guid Version, Guid Rule, Guid RuleVersion) Simple(
        string code, ProcedureAppliesTo appliesTo = ProcedureAppliesTo.AllRecords, Guid? firm = null, DateOnly? from = null,
        RuleType type = RuleType.ApprovedRate, string? body = null, int? consequence = null)
    {
        var procedure = AddProcedure("Procedure " + code, appliesTo, firm);
        var version = AddVersion(procedure, 1, from ?? Jan1, null);
        var rule = AddProcedureRule(procedure, code);
        var ruleVersion = AddRuleVersion(rule, version, from ?? Jan1, type: type, body: body, consequence: consequence);
        return (procedure, version, rule, ruleVersion);
    }

    public void BillingFirm(string table, Guid record, Guid firm)
    {
        var firmRef = ("sprk_billingfirm", (object)Ref("sprk_organization", firm));
        switch (table)
        {
            case Matter:
                World.Add(Matter, record, ("sprk_mattername", "Matter M"), firmRef);
                break;
            case Project:
                World.Add(Project, record, ("sprk_projectname", "Project P"),
                    (SecureRootInheritance.PairIdColumn, M.ToString()), (SecureRootInheritance.PairTypeColumn, Ref(RecordType, MatterType)), firmRef);
                break;
            default:
                World.Add(WorkAssignment, record, ("sprk_name", "Work W"), ("sprk_regardingproject", Ref(Project, P)), firmRef);
                break;
        }
    }

    /// <summary>The resolver's own answer for the same record, date and firm: what the lists must equal.</summary>
    public async Task<ProcedureRulesOnDate> ResolverSays(CoreRecordKind kind, Guid record, Guid firm, DateOnly date) =>
        (await ProcedureResolver.ReadAsync(Entities.Object, NullLogger.Instance, new CoreRecordReference(kind, record), firm, CancellationToken.None))
        .RulesOn(date);

    public void NoWrites()
    {
        Entities.Verify(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
        Entities.Verify(e => e.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
        Entities.Verify(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
        Entities.Verify(e => e.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        Entities.Verify(
            e => e.UpdateIfRowVersionMatchesAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    public static string JsonOf(object value) => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>A logger that keeps (level, exception type) of every entry at Warning or above.</summary>
    internal sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<RecordProcedureReadService>
    {
        public List<(Microsoft.Extensions.Logging.LogLevel Level, Type? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= Microsoft.Extensions.Logging.LogLevel.Warning)
            {
                Entries.Add((logLevel, exception?.GetType()));
            }
        }
    }

    /// <summary>
    /// The caller's batched rights check, stated per test: by default the caller is established and holds Read on everything. A row can be
    /// denied (no Read) or unknown (no answer), the whole check can fault, or the caller can be unestablished.
    /// </summary>
    internal sealed class RightsProbe : CallerRecordAccessProbe
    {
        public RightsProbe()
            : base(new HttpClient(), new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        public Guid? Caller { get; set; } = Guid.Parse("5e0c1a52-0000-4000-8000-000000000099");

        public HashSet<(string Set, Guid Id)> Denied { get; } = new();

        public HashSet<(string Set, Guid Id)> Unknown { get; } = new();

        public bool Throws { get; set; }

        /// <summary>When set, the rights check throws this (instead of the default <see cref="InvalidOperationException"/> of <see cref="Throws"/>).</summary>
        public Exception? Fault { get; set; }

        /// <summary>Every batch the service asked, in order.</summary>
        public List<IReadOnlyList<(string EntitySet, Guid RecordId)>> Batches { get; } = new();

        public int CallerLookups { get; private set; }

        public IEnumerable<(string EntitySet, Guid RecordId)> Asked => Batches.SelectMany(b => b);

        public override Task<Guid?> GetCallerSystemUserIdAsync(string? callerBearerToken, CancellationToken ct = default)
        {
            CallerLookups++;
            return Task.FromResult(Caller);
        }

        public override Task<IReadOnlyList<AccessRights?>> GetCallerRightsForRecordsOrUnknownAsync(
            string? callerBearerToken, IReadOnlyList<(string EntitySet, Guid RecordId)> targets, CancellationToken ct = default)
        {
            Batches.Add(targets);
            if (Fault is not null)
            {
                throw Fault;
            }

            if (Throws)
            {
                throw new InvalidOperationException("RetrievePrincipalAccess faulted");
            }

            IReadOnlyList<AccessRights?> answer = targets
                .Select(t => Unknown.Contains((t.EntitySet, t.RecordId)) ? (AccessRights?)null
                    : Denied.Contains((t.EntitySet, t.RecordId)) ? AccessRights.None
                    : AccessRights.Read)
                .ToList();
            return Task.FromResult(answer);
        }
    }
}
