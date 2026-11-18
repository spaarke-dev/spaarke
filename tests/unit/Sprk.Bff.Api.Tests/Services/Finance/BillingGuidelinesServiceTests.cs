using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 135 (FR-49, D-66): <see cref="BillingGuidelinesService"/> over the REAL <see cref="ProcedureResolver"/>, the REAL
/// <see cref="RuleBodyDescriber"/> and an in-memory Dataverse that evaluates each query (<see cref="SecureChildShareWorld"/>). Each negative
/// case is seeded with a decoy that a dropped filter would let through (a must-fire), beside the row that must show (a must-not-fire).
/// </summary>
public sealed class BillingGuidelinesServiceTests
{
    private const string Matter = SecureRootInheritance.Matter;
    private const string Project = SecureRootInheritance.Project;
    private const string RuleSet = "sprk_ruleset";
    private const string Policy = "sprk_policy";
    private const string PolicyVersion = "sprk_policyversion";
    private const string MatterName = "Acme Holdings v. Secret Counterparty";
    private const string InternalName = "INTERNAL-PROCEDURE-NAME-SECRET";
    private const string ReasonSecret = "OVERRIDE-REASON-SECRET";
    private const string RefusalWords = "closed rule-type set";

    private static readonly Guid F = Guid.Parse("13500000-0000-4000-8000-0000000000f1");
    private static readonly Guid G = Guid.Parse("13500000-0000-4000-8000-0000000000f2");
    private static readonly Guid M = Guid.Parse("13500000-0000-4000-8000-0000000000a1");
    private static readonly Guid P = Guid.Parse("13500000-0000-4000-8000-0000000000b1");
    private static readonly Guid MatterType = Guid.Parse("13500000-0000-4000-8000-0000000000e1");

    private static readonly DateOnly Today = new(2026, 6, 15);
    private static readonly DateOnly Jan1 = new(2026, 1, 1);
    private static readonly DateOnly Jul1 = new(2026, 7, 1);

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard();
    private readonly CapturingLogger _log = new();
    private readonly BillingGuidelinesService _service;
    private readonly Dictionary<Guid, Guid> _setOf = new();

    public BillingGuidelinesServiceTests()
    {
        var entities = SecureChildShareWorld.EntitiesOver(() => _world).Object;
        _world.Add(SecureRootInheritance.RecordTypeRefEntity, MatterType, ("sprk_recordlogicalname", "sprk_matter"));
        _world.Add(Matter, M, ("sprk_name", MatterName));
        // P is filed under M (its pair): M is P's direct parent.
        _world.Add(Project, P, (SecureRootInheritance.PairIdColumn, M.ToString()),
            (SecureRootInheritance.PairTypeColumn, new EntityReference(SecureRootInheritance.RecordTypeRefEntity, MatterType)));
        var schema = new RuleBodySchemaValidator();
        var validator = new PolicyVersionValidator(schema, new PredicateCompiler(schema, TimeProvider.System), NullLogger<PolicyVersionValidator>.Instance);
        _service = new BillingGuidelinesService(entities, new RuleBodyDescriber(validator, entities, NullLogger<RuleBodyDescriber>.Instance), _log)
        {
            Clock = new FixedClock(Today),
        };
    }

    // ── Seeding ──

    private static OptionSetValue Option(int value) => new(value);

    private static EntityReference Ref(string table, Guid id) => new(table, id);

    private Guid AddProcedure(string name, string? firmFacing, ProcedureAppliesTo appliesTo, Guid? firm = null, int status = ProcedureEntity.StatusActive,
        bool? visible = true, int ruleSetStatus = RuleSetService.RuleSetPublished)
    {
        var id = Guid.NewGuid();
        var columns = new List<(string, object)>
        {
            (ProcedureEntity.AreaColumn, Option(RuleDomains.PolicyDomainBilling)),
            (ProcedureEntity.StatusColumn, Option(status)),
            ("statecode", Option(0)),
            (ProcedureEntity.NameColumn, name),
            (ProcedureEntity.AppliesToColumn, Option((int)appliesTo)),
        };
        if (firmFacing is not null) columns.Add((ProcedureEntity.FirmFacingNameColumn, firmFacing));
        if (firm is { } f) columns.Add((ProcedureEntity.FirmColumn, Ref("sprk_organization", f)));
        _world.Add(ProcedureEntity.Entity, id, columns.ToArray());

        var set = Guid.NewGuid();
        var setColumns = new List<(string, object)>
        {
            ("sprk_procedure", Ref(ProcedureEntity.Entity, id)), (RuleDomains.PolicyDomainColumn, Option(RuleDomains.PolicyDomainBilling)),
            ("statecode", Option(0)), ("statuscode", Option(ruleSetStatus)),
        };
        if (visible is { } shown) setColumns.Add(("sprk_visibletofirms", shown));
        _world.Add(RuleSet, set, setColumns.ToArray());
        _setOf[id] = set;
        return id;
    }

    private Guid AddVersion(Guid procedure, int number, DateOnly? from, DateOnly? to, int status = ProcedureEntity.VersionStatusActive)
    {
        var id = Guid.NewGuid();
        var columns = new List<(string, object)>
        {
            (ProcedureEntity.VersionProcedureColumn, Ref(ProcedureEntity.Entity, procedure)),
            (ProcedureEntity.VersionNumberColumn, number),
            (ProcedureEntity.VersionStatusColumn, Option(status)),
            (ProcedureEntity.VersionPublishedOnColumn, new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc)),
        };
        if (from is { } f) columns.Add((ProcedureEntity.VersionEffectiveFromColumn, f.ToDateTime(TimeOnly.MinValue)));
        if (to is { } t) columns.Add((ProcedureEntity.VersionEffectiveToColumn, t.ToDateTime(TimeOnly.MinValue)));
        _world.Add(ProcedureEntity.VersionEntity, id, columns.ToArray());
        return id;
    }

    private Guid AddRule(Guid set, string code, string name, Guid? id = null)
    {
        id ??= Guid.NewGuid();
        _world.Add(Policy, id.Value,
            ("sprk_policycode", code), ("sprk_name", name), ("sprk_ruleset", Ref(RuleSet, set)), ("sprk_enabled", true), ("statecode", Option(0)),
            (RuleDomains.PolicyDomainColumn, Option(RuleDomains.PolicyDomainBilling)));
        return id.Value;
    }

    private Guid AddRuleVersion(Guid policy, Guid? procedureVersion, string? body, string? quote = "The billed rate may exceed the approved rate by 5 percent.",
        string? section = "Section 4.2", bool published = true, DateOnly? from = null, Guid? basedOn = null, DateTime? created = null)
    {
        var id = Guid.NewGuid();
        var columns = new List<(string, object)>
        {
            ("sprk_policy", Ref(Policy, policy)),
            ("sprk_versionnumber", 1),
            ("sprk_ruletype", Option((int)RuleType.ApprovedRate)),
            ("sprk_inforcefrom", (from ?? Jan1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
            (RuleSetService.PublishStateColumn, Option(published ? RuleSetService.PublishStatePublished : RuleSetService.PublishStateDraft)),
        };
        if (body is not null) columns.Add(("sprk_rulebody", body));
        if (quote is not null) columns.Add(("sprk_clausereference", quote));
        if (section is not null) columns.Add(("sprk_clauselocation", section));
        if (procedureVersion is { } pv) columns.Add((RuleSetService.ProcedureVersionColumn, Ref(ProcedureEntity.VersionEntity, pv)));
        if (basedOn is { } based) columns.Add((ProcedureAssignmentService.BasedOnVersionColumn, Ref(PolicyVersion, based)));
        if (created is { } at) columns.Add(("createdon", at));
        _world.Add(PolicyVersion, id, columns.ToArray());
        return id;
    }

    private const string ApprovedRateBody = """{"type":"ApprovedRate","tolerancePercent":5}""";

    /// <summary>A Procedure with one version [Jan 1, open), one rule and its published version.</summary>
    private (Guid Procedure, Guid Version, Guid Rule, Guid RuleVersion) Simple(
        string name, string? firmFacing, ProcedureAppliesTo appliesTo = ProcedureAppliesTo.AllRecords, Guid? firm = null, bool? visible = true,
        string? body = ApprovedRateBody, string ruleName = "Approved rates", string code = "BILL-RATE", string? quote = "The billed rate may exceed the approved rate by 5 percent.")
    {
        var procedure = AddProcedure(name, firmFacing, appliesTo, firm, visible: visible);
        var version = AddVersion(procedure, 3, Jan1, null);
        var rule = AddRule(_setOf[procedure], code, ruleName);
        var ruleVersion = AddRuleVersion(rule, version, body, quote);
        return (procedure, version, rule, ruleVersion);
    }

    private Guid Assign(string table, Guid record, Guid procedure, string? reason = null)
    {
        var columns = new List<(string, object)>
        {
            (ProcedureAssignmentEntity.ProcedureColumn, Ref(ProcedureEntity.Entity, procedure)),
            (ProcedureAssignmentEntity.ModeColumn, Option(ProcedureAssignmentEntity.ModeFollowLatest)),
            ("statecode", Option(0)),
            (table, Ref(table, record)),
        };
        if (reason is not null) columns.Add((ProcedureAssignmentEntity.ReasonColumn, reason));
        var id = Guid.NewGuid();
        _world.Add(ProcedureAssignmentEntity.Entity, id, columns.ToArray());
        return id;
    }

    private static CallerFirm Firm(Guid id, string name = "Alpha LLP") => new(id, name);

    private async Task<(BillingGuidelinesResponse Response, string Json)> General(params CallerFirm[] firms)
    {
        var response = await _service.GetAsync(firms, null, CancellationToken.None);
        return (response, JsonSerializer.Serialize(response));
    }

    // ── What shows ──

    [Fact(DisplayName = "135: the general view lists a client-default and the firm's own Procedure, grouped, with firm-facing labels, dates, description, parameters and clause")]
    public async Task General_ListsClientDefaultAndOwnFirmProcedures_Grouped()
    {
        Simple(InternalName, "Outside counsel guidelines");
        Simple("Alpha rate card internal", "Alpha rate card", ProcedureAppliesTo.Firm, F, ruleName: "Alpha rates", code: "BILL-RATE-F");

        var (response, _) = await General(Firm(F));

        response.Sets.Select(s => s.Name).Should().Equal("Alpha rate card", "Outside counsel guidelines");
        response.Sets.Select(s => s.Scope).Should().Equal(BillingGuidelinesService.ScopeFirm, BillingGuidelinesService.ScopeAllRecords);
        var set = response.Sets.Single(s => s.Name == "Outside counsel guidelines");
        set.FirmName.Should().Be("Alpha LLP");
        set.Version.Should().Be(3);
        set.EffectiveFrom.Should().Be(Jan1);
        set.EffectiveTo.Should().BeNull();
        var rule = set.Rules.Should().ContainSingle().Subject;
        rule.Name.Should().Be("Approved rates");
        rule.DescriptionAvailable.Should().BeTrue();
        rule.Description.Should().Contain("5 percent");
        rule.Parameters.Should().ContainSingle().Which.Should().Be(new BillingGuidelineParameter("Tolerance", "5", "percent"));
        rule.ClauseQuote.Should().Be("The billed rate may exceed the approved rate by 5 percent.");
        rule.SectionReference.Should().Be("Section 4.2");
        rule.InForceFrom.Should().Be(Jan1);
    }

    [Fact(DisplayName = "135: a Procedure with no firm-facing name is labelled with its Name")]
    public async Task ABlankFirmFacingName_FallsBackToTheName()
    {
        Simple("Plain name", null);

        (await General(Firm(F))).Response.Sets.Single().Name.Should().Be("Plain name");
    }

    // ── What never shows (each beside a decoy that must show) ──

    [Fact(DisplayName = "135 negative: another firm's Procedure never appears (and the caller's own and the client default do)")]
    public async Task AnotherFirmsProcedure_NeverAppears()
    {
        Simple("Default", "Default guidelines");
        Simple("Alpha", "Alpha card", ProcedureAppliesTo.Firm, F, code: "BILL-A");
        Simple("Beta", "Beta card BETA-ONLY", ProcedureAppliesTo.Firm, G, code: "BILL-B");
        Simple("Beta all", "Beta all-records BETA-ALL", ProcedureAppliesTo.AllRecords, G, code: "BILL-C");

        var (response, json) = await General(Firm(F));

        response.Sets.Select(s => s.Name).Should().BeEquivalentTo("Default guidelines", "Alpha card");
        json.Should().NotContain("BETA");
    }

    [Fact(DisplayName = "135 negative: a rule set with 'visible to firms' off (or never set) never appears, whatever it holds")]
    public async Task AFlagOffSet_NeverAppears()
    {
        Simple("On", "Visible set");
        Simple("Off", "Hidden set FLAG-OFF", visible: false, code: "BILL-OFF", quote: "Hidden clause HIDDEN-CLAUSE");
        Simple("Unset", "Unset set FLAG-UNSET", visible: null, code: "BILL-UNSET");

        var (response, json) = await General(Firm(F));

        response.Sets.Select(s => s.Name).Should().Equal("Visible set");
        json.Should().NotContain("FLAG-OFF").And.NotContain("FLAG-UNSET").And.NotContain("HIDDEN-CLAUSE");
    }

    [Fact(DisplayName = "135 negative: a Draft rule version, a Draft rule set and a Procedure version not in force today never appear")]
    public async Task NotPublishedOrNotInForce_NeverAppears()
    {
        Simple("Live", "Live set");

        var draftRule = AddProcedure("D1", "Draft rule DRAFT-RULE", ProcedureAppliesTo.AllRecords);
        AddRuleVersion(AddRule(_setOf[draftRule], "BILL-D1", "Draft"), AddVersion(draftRule, 1, Jan1, null), ApprovedRateBody, published: false);

        var draftSet = AddProcedure("D2", "Draft set DRAFT-SET", ProcedureAppliesTo.AllRecords, ruleSetStatus: RuleSetService.RuleSetPublished + 1);
        AddRuleVersion(AddRule(_setOf[draftSet], "BILL-D2", "Draft set rule"), AddVersion(draftSet, 1, Jan1, null), ApprovedRateBody);

        var future = AddProcedure("D3", "Future FUTURE-VERSION", ProcedureAppliesTo.AllRecords);
        AddRuleVersion(AddRule(_setOf[future], "BILL-D3", "Future"), AddVersion(future, 1, Jul1, null), ApprovedRateBody, from: Jul1);

        var ended = AddProcedure("D4", "Ended ENDED-VERSION", ProcedureAppliesTo.AllRecords);
        AddRuleVersion(AddRule(_setOf[ended], "BILL-D4", "Ended"), AddVersion(ended, 1, Jan1, new DateOnly(2026, 3, 1)), ApprovedRateBody);

        var retired = AddProcedure("D5", "Retired RETIRED-PROCEDURE", ProcedureAppliesTo.AllRecords, status: ProcedureEntity.StatusRetired);
        AddRuleVersion(AddRule(_setOf[retired], "BILL-D5", "Retired"), AddVersion(retired, 1, Jan1, null), ApprovedRateBody);

        var (response, json) = await General(Firm(F));

        response.Sets.Select(s => s.Name).Should().Equal("Live set");
        json.Should().NotContain("DRAFT-").And.NotContain("FUTURE-").And.NotContain("ENDED-").And.NotContain("RETIRED-");
    }

    [Fact(DisplayName = "135 negative: a Procedure assigned to a record, or on a parent, is not in the general view (no record, no assignment)")]
    public async Task AnAssignedOnlyProcedure_IsNotInTheGeneralView()
    {
        var assigned = Simple("Assigned", "Assigned set ASSIGNED-ONLY", ProcedureAppliesTo.AssignedRecordsOnly).Procedure;
        Assign(Matter, M, assigned, ReasonSecret);

        (await General(Firm(F))).Response.Sets.Should().BeEmpty();
    }

    // ── Per record ──

    [Fact(DisplayName = "135 per record: a Procedure assigned on the direct parent is labelled 'from the parent record' and the parent is never named")]
    public async Task ARuleFromTheParent_IsLabelled_AndNeverNamesTheParent()
    {
        var assigned = Simple("Assigned", "Matter guidelines", ProcedureAppliesTo.AssignedRecordsOnly).Procedure;
        Assign(Matter, M, assigned, ReasonSecret);

        var onProject = await _service.GetAsync([Firm(F)], new CoreRecordReference(CoreRecordKind.Project, P), CancellationToken.None);
        var json = JsonSerializer.Serialize(onProject);

        onProject.Sets.Should().ContainSingle().Which.Scope.Should().Be(BillingGuidelinesService.ScopeParentRecord);
        json.Should().NotContain(MatterName).And.NotContain(M.ToString()).And.NotContain(M.ToString("N")).And.NotContain(ReasonSecret);

        var onMatter = await _service.GetAsync([Firm(F)], new CoreRecordReference(CoreRecordKind.Matter, M), CancellationToken.None);
        onMatter.Sets.Should().ContainSingle().Which.Scope.Should().Be(BillingGuidelinesService.ScopeThisRecord);
    }

    [Fact(DisplayName = "135 per record: a record rule shows under the neutral record label, never its code or the record's name")]
    public async Task ARecordRule_IsLabelledNeutrally_WithoutItsCodeOrRecord()
    {
        var baseSet = Simple("Base", "Base guidelines");
        var recordSet = ProcedureAssignmentService.RecordRuleSetIdFor(Matter, M, null);
        _world.Add(RuleSet, recordSet,
            ("sprk_scope", Option(RecordRuleSet.ScopeRecord)), (RuleDomains.PolicyDomainColumn, Option(RuleDomains.PolicyDomainBilling)), ("statecode", Option(0)),
            (Matter, Ref(Matter, M)), ("sprk_visibletofirms", true), ("statuscode", Option(RuleSetService.RuleSetPublished)));
        var at = new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc);
        _world.Add(ProcedureAssignmentEntity.Entity, Guid.NewGuid(),
            (ProcedureAssignmentEntity.ExcludedPolicyColumn, Ref(Policy, baseSet.Rule)), (ProcedureAssignmentEntity.ModeColumn, Option(ProcedureAssignmentEntity.ModeExcluded)),
            ("statecode", Option(0)), (Matter, Ref(Matter, M)), (ProcedureAssignmentEntity.ReasonColumn, ReasonSecret), ("createdon", at));
        var recordRule = ProcedureAssignmentService.RecordRulePolicyIdFor(recordSet, baseSet.Rule);
        AddRule(recordSet, "BRR-1", "Tighter rate", recordRule);
        AddRuleVersion(recordRule, null, """{"type":"ApprovedRate","tolerancePercent":2}""", "Record clause text", "Section 9", basedOn: baseSet.RuleVersion, created: at);

        var response = await _service.GetAsync([Firm(F)], new CoreRecordReference(CoreRecordKind.Matter, M), CancellationToken.None);
        var json = JsonSerializer.Serialize(response);

        var set = response.Sets.Should().ContainSingle().Subject;
        set.Name.Should().Be(BillingGuidelinesService.RecordLabel);
        set.Rules.Should().ContainSingle().Which.Parameters.Single().Value.Should().Be("2");
        json.Should().NotContain("BRR").And.NotContain(recordRule.ToString()).And.NotContain(MatterName).And.NotContain(ReasonSecret);
    }

    // ── The describer's refusal ──

    [Fact(DisplayName = "135 FR-24: a rule the describer refuses is listed with its name and clause and a marker, with no description, no parameters and no refusal text")]
    public async Task ARefusedRule_IsListedWithAMarker_NeverDropped_NeverDescribedByFallback()
    {
        Simple("Mixed", "Mixed set", body: """{"type":"NotARuleType","tolerancePercent":5}""", ruleName: "Refused rule", code: "BILL-REFUSED", quote: "Refused clause text");

        var (response, json) = await General(Firm(F));

        var rule = response.Sets.Single().Rules.Should().ContainSingle().Subject;
        rule.Name.Should().Be("Refused rule");
        rule.DescriptionAvailable.Should().BeFalse();
        rule.Description.Should().BeNull();
        rule.Parameters.Should().BeEmpty();
        rule.ClauseQuote.Should().Be("Refused clause text");
        json.Should().NotContain(RefusalWords).And.NotContain("NotARuleType");
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("describer refused"));
    }

    // ── The allow-list DTO ──

    [Fact(DisplayName = "135: no member of any DTO is a document link, container id, drive item id, Procedure id, record id or override reason, and none of their values reach the body")]
    public async Task TheDtoIsAnAllowList_WithNoDocumentOrIdentityMember()
    {
        var assigned = Simple(InternalName, "Public name", ProcedureAppliesTo.AssignedRecordsOnly).Procedure;
        Assign(Matter, M, assigned, ReasonSecret);
        _world.Add("sprk_storedfile", Guid.NewGuid(), (ProcedureEntity.StoredFileProcedureVersionColumn, Ref(ProcedureEntity.VersionEntity, Guid.NewGuid())), ("sprk_name", "SOURCE-DOCUMENT.pdf"));

        var response = await _service.GetAsync([Firm(F)], new CoreRecordReference(CoreRecordKind.Matter, M), CancellationToken.None);
        var json = JsonSerializer.Serialize(response);

        var members = new[] { typeof(BillingGuidelinesResponse), typeof(BillingGuidelineSet), typeof(BillingGuidelineRule), typeof(BillingGuidelineParameter) }
            .SelectMany(t => t.GetProperties().Select(p => p.Name)).ToList();
        members.Should().BeEquivalentTo(
            "Sets", "FirmName", "Name", "Scope", "Version", "EffectiveFrom", "EffectiveTo", "Rules",
            "Name", "DescriptionAvailable", "Description", "Parameters", "ClauseQuote", "SectionReference", "InForceFrom",
            "Name", "Value", "Unit");
        json.Should().NotContain(InternalName).And.NotContain(ReasonSecret).And.NotContain(assigned.ToString()).And.NotContain(assigned.ToString("N"))
            .And.NotContain("BILL-RATE").And.NotContain("SOURCE-DOCUMENT").And.NotContain(MatterName);
        json.ToLowerInvariant().Should().NotContain("container").And.NotContain("driveitem").And.NotContain("link").And.NotContain("http");
    }

    // ── Several firms ──

    [Fact(DisplayName = "135 owner question: a caller of several firms sees each firm's sets, each labelled with its firm and no other firm's")]
    public async Task SeveralFirms_EachSetIsLabelledWithItsFirm()
    {
        Simple("Default", "Default guidelines");
        Simple("Alpha", "Alpha card", ProcedureAppliesTo.Firm, F, code: "BILL-A");
        Simple("Beta", "Beta card", ProcedureAppliesTo.Firm, G, code: "BILL-B");
        Simple("Gamma", "Gamma card GAMMA-ONLY", ProcedureAppliesTo.Firm, Guid.NewGuid(), code: "BILL-C");

        var (response, json) = await General(Firm(G, "Beta Partners"), Firm(F, "Alpha LLP"));

        response.Sets.Select(s => (s.FirmName, s.Name)).Should().Equal(
            ("Alpha LLP", "Alpha card"), ("Alpha LLP", "Default guidelines"),
            ("Beta Partners", "Beta card"), ("Beta Partners", "Default guidelines"));
        json.Should().NotContain("GAMMA-ONLY");
    }

    [Fact(DisplayName = "135: a caller with no firm to resolve gets an empty answer, and a clause with neither quote nor section lists the rule without a clause")]
    public async Task NoFirms_IsEmpty_AndAClauselessRuleHasNoClause()
    {
        Simple("Clauseless", "Clauseless set", quote: null);
        var (response, _) = await General();
        response.Sets.Should().BeEmpty();

        var withFirm = await General(Firm(F));
        withFirm.Response.Sets.Single().Rules.Single().ClauseQuote.Should().BeNull();
    }

    // ── The parameter table ──

    [Fact(DisplayName = "135: every member of every billing rule body schema has a label in the closed parameter table (or is 'type')")]
    public void EveryBillingSchemaMember_HasATableEntry()
    {
        var missing = new List<string>();
        foreach (var type in BillingRuleCatalogue.Types)
        {
            using var schema = JsonDocument.Parse(BillingRuleCatalogue.SchemaText(type));
            foreach (var property in schema.RootElement.GetProperty("properties").EnumerateObject())
            {
                if (!BillingGuidelinesService.NotShown.Contains(property.Name) && !BillingGuidelinesService.ParameterTable.ContainsKey(property.Name))
                {
                    missing.Add($"{type}.{property.Name}");
                }
            }
        }

        missing.Should().BeEmpty("a new rule-body member needs its label and unit in BillingGuidelinesService.ParameterTable");
    }

    [Theory(DisplayName = "135: parameters are the body's scalar and list members with their units; an object member is skipped")]
    [InlineData("""{"type":"DailyHoursCap","maxHoursPerDay":8}""", "Maximum hours per day|8|hours")]
    [InlineData("""{"type":"ExpenseRule","nonReimbursableCodes":["E101","E102"],"maxAmountPerItem":250.5}""", "Non-reimbursable codes|E101, E102|;Maximum per item|250.5|invoice currency")]
    [InlineData("""{"type":"AddedFee","feeKind":"lateFee","allowance":"cap","feeCodes":["E115"],"capPercent":2}""", "Fee kind|late fee|;Allowance|cap|;Fee codes|E115|;Cap|2|percent of the invoice's other lines")]
    [InlineData("""{"type":"X","inner":{"a":1},"nothing":null}""", "")]
    public void ParametersOf_ShapesNamesValuesAndUnits(string body, string expected)
    {
        var actual = BillingGuidelinesService.ParametersOf(body).Select(p => $"{p.Name}|{p.Value}|{p.Unit}");

        actual.Should().Equal(expected.Length == 0 ? [] : expected.Split(';'));
    }

    // ── Doubles ──

    private sealed class FixedClock(DateOnly date) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(date.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc));
    }

    private sealed class CapturingLogger : ILogger<BillingGuidelinesService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
