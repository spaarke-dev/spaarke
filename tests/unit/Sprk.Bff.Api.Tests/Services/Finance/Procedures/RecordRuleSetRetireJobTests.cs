// ebilling-r1 task 117 (procedure model 5.2 / 12, O-17): the no-plugin cleanup after a core record's delete. The PRODUCTION job runs over the
// PRODUCTION RuleSetService and ProcedureAssignmentService; Dataverse is RuleSetServiceTests.Harness's fake store, which evaluates the real
// queries (Equal, In, Null, NotEqual, the id cursor, the modifiedon age floor) and records every request.

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Procedures;

public sealed class RecordRuleSetRetireJobTests
{
    private const string RuleSet = "sprk_ruleset";
    private const string Policy = "sprk_policy";
    private const string PolicyVersion = "sprk_policyversion";
    private const string Assignment = "sprk_procedureassignment";

    /// <summary>The tables the sweep may send a request to. Everything else (a Procedure, its versions, a Stored File, a rate card, a document or
    /// communication, a policy version) is forbidden.</summary>
    private static readonly string[] AllowedTables = [RuleSet, Policy, Assignment];

    private static readonly string[] ForbiddenTables =
    [
        "sprk_procedure", "sprk_procedureversion", "sprk_storedfile", "sprk_ratecard", "sprk_ratecardrate", "sprk_document",
        "sprk_communication", PolicyVersion,
    ];

    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OwnerTeam = Guid.NewGuid();
    private static readonly Guid CreatorPerson = Guid.NewGuid();

    private readonly ProcedureAssignmentServiceTests.World _world = new();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly Dictionary<string, string?> _settings = new();
    private readonly List<(LogLevel Level, string Message)> _logs = [];

    /// <summary>The eBilling kill switch (<c>Ebilling:Enabled</c>) the job is built with: on, unless a test switches it off.</summary>
    private bool _ebillingEnabled = true;

    private RuleSetServiceTests.Harness H => _world.H;

    public static TheoryData<string> CoreTables()
    {
        var data = new TheoryData<string>();
        foreach (var core in InvoiceFamilyCoreRecordGuard.FilingCoreTables) data.Add(core);
        return data;
    }

    // ── Seeding ──────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A rule set (Billing, Published, owned by a team, with a creator person) of <paramref name="scope"/>; <paramref name="core"/> sets one
    /// core-record lookup, <paramref name="procedure"/> the Procedure lookup. Named as 089's record-set creator names it unless
    /// <paramref name="name"/> says otherwise.</summary>
    private Guid Set(
        int scope = RecordRuleSet.ScopeRecord, string? core = null, Guid? procedure = null, int status = RuleSetService.RuleSetPublished,
        int? domain = RuleDomains.PolicyDomainBilling, Guid? firm = null, string name = RecordRuleSet.Name)
    {
        var set = new Entity(RuleSet, Guid.NewGuid())
        {
            ["sprk_name"] = name,
            ["sprk_scope"] = new OptionSetValue(scope),
            ["statuscode"] = new OptionSetValue(status),
            ["statecode"] = new OptionSetValue(status == RuleSetService.RuleSetRetired ? 1 : 0),
            ["ownerid"] = new EntityReference("team", OwnerTeam),
            ["sprk_createdbyperson"] = new EntityReference("systemuser", CreatorPerson),
        };
        if (domain is { } d) set[RuleDomains.PolicyDomainColumn] = new OptionSetValue(d);
        if (core is not null) set[core] = new EntityReference(core, Guid.NewGuid());
        if (procedure is { } p) set["sprk_procedure"] = new EntityReference("sprk_procedure", p);
        if (firm is { } f) set["sprk_firm"] = new EntityReference("sprk_organization", f);
        H.Seed(set);
        return set.Id;
    }

    /// <summary>An enabled record rule (policy in force, with its version) of <paramref name="setId"/>, owned like its set.</summary>
    private (Guid Policy, Guid Version) Rule(Guid setId, string code, bool enabled = true)
    {
        var (policy, version) = H.Rule(setId, code, ProposalState.Accepted, current: true);
        var row = H.Row(Policy, policy);
        row["sprk_enabled"] = enabled;
        row["ownerid"] = new EntityReference("team", OwnerTeam);
        row["sprk_createdbyperson"] = new EntityReference("systemuser", CreatorPerson);
        return (policy, version);
    }

    /// <summary>A Procedure assignment row; <paramref name="core"/> sets its one core lookup (null = an orphan).</summary>
    private Guid AssignmentRow(string? core = null)
    {
        var row = new Entity(Assignment, Guid.NewGuid())
        {
            [ProcedureAssignmentEntity.NameColumn] = "Acme guidelines",
            [ProcedureAssignmentEntity.ModeColumn] = new OptionSetValue(ProcedureAssignmentEntity.ModeFollowLatest),
            [ProcedureAssignmentEntity.ProcedureColumn] = new EntityReference("sprk_procedure", Guid.NewGuid()),
            ["ownerid"] = new EntityReference("team", OwnerTeam),
        };
        if (core is not null) row[core] = new EntityReference(core, Guid.NewGuid());
        H.Seed(row);
        return row.Id;
    }

    private int Status(Guid setId) => H.Row(RuleSet, setId).GetAttributeValue<OptionSetValue>("statuscode").Value;

    private int State(Guid setId) => H.Row(RuleSet, setId).GetAttributeValue<OptionSetValue>("statecode").Value;

    private bool Enabled(Guid policyId) => H.Row(Policy, policyId).GetAttributeValue<bool>("sprk_enabled");

    private bool Exists(string table, Guid id) => H.Rows.GetValueOrDefault(table)?.Any(r => r.Id == id) == true;

    // ── Running ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private RecordRuleSetRetireJob NewJob(bool? apply)
    {
        if (apply is { } a) _settings[RecordRuleSetRetireJob.ApplyKey] = a ? "true" : "false";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(_settings).Build();
        var services = new ServiceCollection();
        services.AddScoped(_ => H.Service());
        services.AddScoped(_ => _world.Service());
        var provider = services.BuildServiceProvider();
        return new RecordRuleSetRetireJob(
            provider.GetRequiredService<IServiceScopeFactory>(), Options.Create(new EbillingOptions { Enabled = _ebillingEnabled }), configuration, _clock,
            new ListLogger(_logs));
    }

    private async Task<JsonElement> RunAsync(bool? apply = true)
    {
        var result = await RunJobAsync(apply);
        using var doc = JsonDocument.Parse(result.ResultJson!);
        return doc.RootElement.Clone();
    }

    private async Task<JobRunResult> RunJobAsync(bool? apply = true)
    {
        // Only the run's own requests are recorded (the seeding above sent none).
        H.Entities.Invocations.Clear();
        H.Queries.Clear();
        return await NewJob(apply).ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), "corr-117", JobRunTrigger.ManualAdmin, new Dictionary<string, object>()), CancellationToken.None);
    }

    /// <summary>
    /// The request contract of every run: only reads (of the rule set, policy and assignment tables), single-row updates and deletes; a policy
    /// update writes <c>sprk_enabled</c> = No only, a rule-set update the Retired state and status only, and a delete only removes an assignment row
    /// in <paramref name="orphans"/>. So: no Assign, no owner or creator-person write, no lookup write, no policy-version write, no request against
    /// a forbidden table.
    /// </summary>
    private void AssertOnlyTheSweepsRequests(params Guid[] orphans)
    {
        var methods = H.Entities.Invocations.Select(i => i.Method.Name).Distinct().ToList();
        methods.Should().BeSubsetOf(
            [nameof(IGenericEntityService.RetrieveMultipleAsync), nameof(IGenericEntityService.UpdateAsync), nameof(IGenericEntityService.DeleteAsync)],
            "the sweep reads, updates one row at a time and deletes orphans; it sends no create, transaction, Assign or association");

        foreach (var query in H.Queries)
        {
            query.EntityName.Should().BeOneOf(AllowedTables);
            query.LinkEntities.Should().BeEmpty("a join would reach another table");
        }

        foreach (var invocation in H.Entities.Invocations.Where(i => i.Method.Name != nameof(IGenericEntityService.RetrieveMultipleAsync)))
        {
            var table = (string)invocation.Arguments[0];
            ForbiddenTables.Should().NotContain(table);
            switch (invocation.Method.Name)
            {
                case nameof(IGenericEntityService.UpdateAsync) when table == Policy:
                    ((Dictionary<string, object>)invocation.Arguments[2]).Should().BeEquivalentTo(new Dictionary<string, object> { ["sprk_enabled"] = false });
                    break;
                case nameof(IGenericEntityService.UpdateAsync) when table == RuleSet:
                    var fields = (Dictionary<string, object>)invocation.Arguments[2];
                    fields.Keys.Should().BeEquivalentTo(["statecode", "statuscode"]);
                    ((OptionSetValue)fields["statecode"]).Value.Should().Be(1);
                    ((OptionSetValue)fields["statuscode"]).Value.Should().Be(RuleSetService.RuleSetRetired);
                    break;
                case nameof(IGenericEntityService.DeleteAsync):
                    table.Should().Be(Assignment, "the only delete is of an orphan assignment row");
                    orphans.Should().Contain((Guid)invocation.Arguments[1]);
                    break;
                default:
                    throw new Xunit.Sdk.XunitException($"Unexpected write: {invocation.Method.Name} on {table}");
            }
        }
    }

    private int WriteCount => H.Entities.Invocations.Count(i =>
        i.Method.Name is not nameof(IGenericEntityService.RetrieveMultipleAsync));

    // ── Must fire ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "117: with Apply on, a Record set and a Firm record set with every lookup null are Retired and each of their policies is Off")]
    public async Task Apply_RetiresUnlinkedRecordAndFirmRecordSets_AndSwitchesTheirPoliciesOff()
    {
        var record = Set(RecordRuleSet.ScopeRecord);
        var firmRecord = Set(RecordRuleSet.ScopeFirmRecord, firm: Guid.NewGuid());
        var r1 = Rule(record, "BRR-1");
        var r2 = Rule(record, "BRR-2");
        var r3 = Rule(firmRecord, "BRR-3");
        var off = Rule(firmRecord, "BRR-4", enabled: false);
        var versionsBefore = H.Rows[PolicyVersion].ToDictionary(v => v.Id, v => v.Attributes.ToDictionary(a => a.Key, a => a.Value));

        var result = await RunAsync();

        foreach (var set in new[] { record, firmRecord })
        {
            Status(set).Should().Be(RuleSetService.RuleSetRetired);
            State(set).Should().Be(1);
            H.Row(RuleSet, set).GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(OwnerTeam, "a retire is no re-own");
            H.Row(RuleSet, set).GetAttributeValue<EntityReference>("sprk_createdbyperson").Id.Should().Be(CreatorPerson);
        }

        foreach (var policy in new[] { r1.Policy, r2.Policy, r3.Policy, off.Policy })
        {
            Enabled(policy).Should().BeFalse();
            H.Row(Policy, policy).GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(OwnerTeam);
            H.Row(Policy, policy).GetAttributeValue<EntityReference>("sprk_createdbyperson").Id.Should().Be(CreatorPerson);
        }

        H.Rows[PolicyVersion].ToDictionary(v => v.Id, v => v.Attributes.ToDictionary(a => a.Key, a => a.Value))
            .Should().BeEquivalentTo(versionsBefore, "policy versions are never written");
        H.Entities.Invocations.Count(i => i.Method.Name == nameof(IGenericEntityService.UpdateAsync) && (string)i.Arguments[0] == Policy)
            .Should().Be(3, "a policy already Off is not written again");
        AssertOnlyTheSweepsRequests();

        result.GetProperty("status").GetString().Should().Be("ok");
        result.GetProperty("ruleSets").GetProperty("selected").GetInt32().Should().Be(2);
        result.GetProperty("ruleSets").GetProperty("retired").GetInt32().Should().Be(2);
        result.GetProperty("ruleSets").GetProperty("policiesSwitchedOff").GetInt32().Should().Be(3);
        _logs.Should().ContainSingle(l => l.Message.Contains("heartbeat"))
            .Which.Message.Should().Contain("setsSelected=2").And.Contain("setsRetired=2").And.Contain("setsFaulted=0").And.Contain("apply=True");
    }

    [Fact(DisplayName = "117: with Apply on, an assignment row with every core lookup null is deleted; nothing else is")]
    public async Task Apply_DeletesAnOrphanAssignmentRow()
    {
        var orphan = AssignmentRow();

        var result = await RunAsync();

        Exists(Assignment, orphan).Should().BeFalse();
        AssertOnlyTheSweepsRequests(orphan);
        result.GetProperty("assignments").GetProperty("selected").GetInt32().Should().Be(1);
        result.GetProperty("assignments").GetProperty("deleted").GetInt32().Should().Be(1);
        _logs.Single(l => l.Message.Contains("heartbeat")).Message.Should().Contain("assignmentsDeleted=1");
    }

    [Fact(DisplayName = "117: a second run over the same state writes nothing")]
    public async Task ASecondRun_WritesNothing()
    {
        var set = Set();
        Rule(set, "BRR-1");
        AssignmentRow();
        await RunAsync();
        WriteCount.Should().BeGreaterThan(0);

        var second = await RunAsync();

        WriteCount.Should().Be(0);
        second.GetProperty("ruleSets").GetProperty("selected").GetInt32().Should().Be(0);
        second.GetProperty("assignments").GetProperty("selected").GetInt32().Should().Be(0);
    }

    // ── Must not fire ────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory(DisplayName = "117: a record-scoped set with one core lookup set is untouched (one case per core table)")]
    [MemberData(nameof(CoreTables))]
    public async Task ASetWithALookup_IsUntouched(string core)
    {
        var linked = Set(RecordRuleSet.ScopeRecord, core);
        var linkedFirm = Set(RecordRuleSet.ScopeFirmRecord, core, firm: Guid.NewGuid());
        var rule = Rule(linked, "BRR-1");
        var firmRule = Rule(linkedFirm, "BRR-2");

        var result = await RunAsync();

        Status(linked).Should().Be(RuleSetService.RuleSetPublished);
        Status(linkedFirm).Should().Be(RuleSetService.RuleSetPublished);
        Enabled(rule.Policy).Should().BeTrue();
        Enabled(firmRule.Policy).Should().BeTrue();
        WriteCount.Should().Be(0);
        result.GetProperty("ruleSets").GetProperty("selected").GetInt32().Should().Be(0);
    }

    [Theory(DisplayName = "117: an assignment row with one live core lookup is kept (one case per core table)")]
    [MemberData(nameof(CoreTables))]
    public async Task AnAssignmentRowWithALiveLookup_IsKept(string core)
    {
        var kept = AssignmentRow(core);

        var result = await RunAsync();

        Exists(Assignment, kept).Should().BeTrue();
        WriteCount.Should().Be(0);
        result.GetProperty("assignments").GetProperty("selected").GetInt32().Should().Be(0);
    }

    [Fact(DisplayName = "117: a Procedure set, a ClientDefault and a Firm set, a Retired set and a non-Billing record set are untouched")]
    public async Task OtherSets_AreUntouched()
    {
        var procedure = Guid.NewGuid();
        var sets = new[]
        {
            Set((int)RuleSetScope.ClientDefault, procedure: procedure),           // a Procedure's set
            Set(RecordRuleSet.ScopeRecord, procedure: Guid.NewGuid()),             // a record scope that carries a Procedure (never a record's own set)
            Set((int)RuleSetScope.ClientDefault),                                  // a pre-Procedure ClientDefault set
            Set((int)RuleSetScope.Firm, firm: Guid.NewGuid()),                     // a Firm set
            Set(RecordRuleSet.ScopeRecord, domain: RuleDomains.PolicyDomainOntology), // an Ontology record set
            Set(RecordRuleSet.ScopeRecord, domain: null),                          // an empty domain (reads as Ontology)
        };
        var rules = sets.Select((s, i) => Rule(s, $"BRR-{i}").Policy).ToList();
        var retired = Set(status: RuleSetService.RuleSetRetired);
        var retiredRule = Rule(retired, "BRR-R");

        var result = await RunAsync();

        sets.Should().OnlyContain(s => Status(s) == RuleSetService.RuleSetPublished && State(s) == 0);
        rules.Should().OnlyContain(p => Enabled(p));
        Enabled(retiredRule.Policy).Should().BeTrue("a Retired set is not selected again, so its rules are not written");
        WriteCount.Should().Be(0);
        result.GetProperty("ruleSets").GetProperty("selected").GetInt32().Should().Be(0);
    }

    [Theory(DisplayName = "117: a record-scoped set with no lookup that is not of 089's shape (another name) is untouched, Record and Firm record")]
    [InlineData("Acme guidelines")]                         // a legacy ClientDefault set hand-edited to a record scope in the app
    [InlineData("billing instructions for this record")]    // not 089's name exactly (ordinal)
    [InlineData("")]
    public async Task ASetNotOf089sShape_IsUntouched(string name)
    {
        var record = Set(RecordRuleSet.ScopeRecord, name: name);
        var firmRecord = Set(RecordRuleSet.ScopeFirmRecord, firm: Guid.NewGuid(), name: name);
        var rule = Rule(record, "BRR-1");
        var firmRule = Rule(firmRecord, "BRR-2");

        var result = await RunAsync();

        Status(record).Should().Be(RuleSetService.RuleSetPublished);
        Status(firmRecord).Should().Be(RuleSetService.RuleSetPublished);
        Enabled(rule.Policy).Should().BeTrue();
        Enabled(firmRule.Policy).Should().BeTrue();
        WriteCount.Should().Be(0);
        result.GetProperty("ruleSets").GetProperty("selected").GetInt32().Should().Be(0);
    }

    [Fact(DisplayName = "117: with Apply off nothing is written, for every case, and the counts are logged")]
    public async Task ApplyOff_WritesNothing_AndReportsTheCounts()
    {
        var record = Set(RecordRuleSet.ScopeRecord);
        var firmRecord = Set(RecordRuleSet.ScopeFirmRecord, firm: Guid.NewGuid());
        Rule(record, "BRR-1");
        Rule(firmRecord, "BRR-2");
        var orphan = AssignmentRow();
        foreach (var core in InvoiceFamilyCoreRecordGuard.FilingCoreTables)
        {
            Rule(Set(RecordRuleSet.ScopeRecord, core), "BRR-" + core);
            AssignmentRow(core);
        }

        var result = await RunAsync(apply: null); // absent = report only

        WriteCount.Should().Be(0);
        Status(record).Should().Be(RuleSetService.RuleSetPublished);
        Status(firmRecord).Should().Be(RuleSetService.RuleSetPublished);
        Exists(Assignment, orphan).Should().BeTrue();
        H.Rows[Policy].Should().OnlyContain(p => p.GetAttributeValue<bool>("sprk_enabled"));
        result.GetProperty("apply").GetBoolean().Should().BeFalse();
        result.GetProperty("mode").GetString().Should().Be("report-only");
        result.GetProperty("ruleSets").GetProperty("selected").GetInt32().Should().Be(2);
        result.GetProperty("ruleSets").GetProperty("retired").GetInt32().Should().Be(0);
        result.GetProperty("assignments").GetProperty("selected").GetInt32().Should().Be(1);
        result.GetProperty("assignments").GetProperty("deleted").GetInt32().Should().Be(0);
        var heartbeat = _logs.Single(l => l.Message.Contains("heartbeat"));
        heartbeat.Level.Should().Be(LogLevel.Warning, "a report-only run that found work says so");
        heartbeat.Message.Should().Contain("apply=False").And.Contain("setsSelected=2").And.Contain("assignmentsSelected=1");
        AssertOnlyTheSweepsRequests();
    }

    [Theory(DisplayName = "117: anything but 'true' is report only")]
    [InlineData("false")]
    [InlineData("yes")]
    [InlineData("")]
    public async Task AnythingButTrue_IsReportOnly(string setting)
    {
        Rule(Set(), "BRR-1");
        AssignmentRow();
        _settings[RecordRuleSetRetireJob.ApplyKey] = setting;

        await RunAsync(apply: null);

        WriteCount.Should().Be(0);
    }

    [Fact(DisplayName = "117: with the eBilling kill switch off and Apply on, the run sends no request at all and heartbeats ebilling-disabled")]
    public async Task EbillingDisabled_WithApplyOn_SendsNoRequest_AndHeartbeatsTheSkip()
    {
        var set = Set();
        var rule = Rule(set, "BRR-1");
        var orphan = AssignmentRow();
        _ebillingEnabled = false;

        var result = await RunJobAsync(apply: true);

        H.Entities.Invocations.Should().BeEmpty("ADR-018: with eBilling off the job reads nothing and writes nothing");
        H.Queries.Should().BeEmpty();
        Status(set).Should().Be(RuleSetService.RuleSetPublished);
        Enabled(rule.Policy).Should().BeTrue();
        Exists(Assignment, orphan).Should().BeTrue();
        result.Success.Should().BeTrue("a switched-off run is not a failure");
        result.Skipped.Should().BeFalse("only the host sets Skipped (scheduled-jobs pattern)");
        using var doc = JsonDocument.Parse(result.ResultJson!);
        doc.RootElement.GetProperty("skippedReason").GetString().Should().Be("ebilling-disabled");
        doc.RootElement.GetProperty("apply").GetBoolean().Should().BeFalse();
        _logs.Should().ContainSingle(l => l.Message.Contains("heartbeat"))
            .Which.Message.Should().Contain("skippedReason=ebilling-disabled").And.Contain("setsSelected=0");
    }

    // ── Age floor ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "117: a matching set or orphan row modified inside the age floor is untouched; just outside it, it is handled")]
    public async Task TheAgeFloor_LeavesRecentRows_AndTakesOlderOnes()
    {
        var recentSet = Set();
        var recentRule = Rule(recentSet, "BRR-1");
        var recentRow = AssignmentRow();
        var olderSet = Set();
        var olderRule = Rule(olderSet, "BRR-2");
        var olderRow = AssignmentRow();
        H.SetModifiedOn(recentSet, Now.UtcDateTime.AddMinutes(-14));
        H.SetModifiedOn(recentRow, Now.UtcDateTime.AddMinutes(-14));
        H.SetModifiedOn(olderSet, Now.UtcDateTime.AddMinutes(-16));
        H.SetModifiedOn(olderRow, Now.UtcDateTime.AddMinutes(-16));

        await RunAsync(); // default floor: 15 minutes

        Status(recentSet).Should().Be(RuleSetService.RuleSetPublished);
        Enabled(recentRule.Policy).Should().BeTrue();
        Exists(Assignment, recentRow).Should().BeTrue();
        Status(olderSet).Should().Be(RuleSetService.RuleSetRetired);
        Enabled(olderRule.Policy).Should().BeFalse();
        Exists(Assignment, olderRow).Should().BeFalse();
    }

    [Fact(DisplayName = "117: the age floor is configurable")]
    public async Task TheAgeFloor_IsConfigurable()
    {
        var set = Set();
        H.SetModifiedOn(set, Now.UtcDateTime.AddMinutes(-16));
        _settings[RecordRuleSetRetireJob.MinAgeMinutesKey] = "30";

        await RunAsync();

        Status(set).Should().Be(RuleSetService.RuleSetPublished);
    }

    [Fact(DisplayName = "117: a huge age floor is clamped to one year; the run completes, never throws")]
    public async Task AHugeAgeFloor_IsClampedToOneYear_AndTheRunCompletes()
    {
        var withinTheYear = Set();
        var olderThanAYear = Set();
        H.SetModifiedOn(withinTheYear, Now.UtcDateTime.AddDays(-300));
        H.SetModifiedOn(olderThanAYear, Now.UtcDateTime.AddDays(-400));
        _settings[RecordRuleSetRetireJob.MinAgeMinutesKey] = int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var result = await RunJobAsync();

        result.Success.Should().BeTrue();
        using var doc = JsonDocument.Parse(result.ResultJson!);
        doc.RootElement.GetProperty("modifiedBefore").GetDateTime().Should().Be(Now.UtcDateTime.AddMinutes(-RecordRuleSetRetireJob.MaxMinAgeMinutes));
        Status(withinTheYear).Should().Be(RuleSetService.RuleSetPublished);
        Status(olderThanAYear).Should().Be(RuleSetService.RuleSetRetired);
    }

    // ── Faults ───────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "117: a policy update fault leaves that set Active and its run partial, the next set is retired, the next run retires it")]
    public async Task APolicyFault_LeavesTheSetActive_TheRunContinues_AndTheNextRunRetiresIt()
    {
        var faulty = Set();
        var faultyRule = Rule(faulty, "BRR-1");
        var healthy = Set();
        var healthyRule = Rule(healthy, "BRR-2");
        H.FailUpdateOf = (table, id) => table == Policy && id == faultyRule.Policy;

        var first = await RunAsync();

        Status(faulty).Should().Be(RuleSetService.RuleSetPublished, "a set whose rule could not be switched Off is not retired");
        State(faulty).Should().Be(0);
        Enabled(faultyRule.Policy).Should().BeTrue();
        Status(healthy).Should().Be(RuleSetService.RuleSetRetired, "one set's fault does not stop the run");
        Enabled(healthyRule.Policy).Should().BeFalse();
        first.GetProperty("status").GetString().Should().Be("partial");
        first.GetProperty("ruleSets").GetProperty("faulted").GetInt32().Should().Be(1);
        first.GetProperty("ruleSets").GetProperty("retired").GetInt32().Should().Be(1);
        _logs.Should().Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("heartbeat") && l.Message.Contains("setsFaulted=1"));

        H.FailUpdateOf = null;
        var second = await RunAsync();

        Status(faulty).Should().Be(RuleSetService.RuleSetRetired);
        Enabled(faultyRule.Policy).Should().BeFalse();
        second.GetProperty("ruleSets").GetProperty("retired").GetInt32().Should().Be(1);
        AssertOnlyTheSweepsRequests();

        var third = await RunAsync();
        WriteCount.Should().Be(0);
        third.GetProperty("status").GetString().Should().Be("ok");
    }

    [Fact(DisplayName = "117: a delete fault on one orphan row is counted and the run deletes the next")]
    public async Task ADeleteFault_OnOneRow_ContinuesTheRun()
    {
        var faulty = AssignmentRow();
        var healthy = AssignmentRow();
        H.OnDelete = (table, id) => id == faulty ? new InvalidOperationException("simulated delete fault") : null;

        var result = await RunAsync();

        Exists(Assignment, faulty).Should().BeTrue();
        Exists(Assignment, healthy).Should().BeFalse();
        result.GetProperty("status").GetString().Should().Be("partial");
        result.GetProperty("assignments").GetProperty("faulted").GetInt32().Should().Be(1);
        result.GetProperty("assignments").GetProperty("deleted").GetInt32().Should().Be(1);

        H.OnDelete = null;
        await RunAsync();
        Exists(Assignment, faulty).Should().BeFalse("the next run retries it");
    }

    [Fact(DisplayName = "117: a page that cannot be read is a FAILED run, never 'nothing to do'")]
    public async Task AReadFault_IsAFailedRun()
    {
        Rule(Set(), "BRR-1");
        H.Entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == RuleSet), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated read fault"));
        var orphan = AssignmentRow();

        var result = await RunAsync();

        result.GetProperty("status").GetString().Should().Be("error");
        result.GetProperty("ruleSets").GetProperty("readFailed").GetBoolean().Should().BeTrue();
        Exists(Assignment, orphan).Should().BeFalse("the assignment part still runs");
    }

    // ── Paging ───────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "117: a run takes at most PageSize x MaxPages sets; the next run takes the rest")]
    public async Task ThePageCap_IsHonoured_AndTheNextRunTakesTheRest()
    {
        var sets = Enumerable.Range(0, 3).Select(_ => Set()).ToList();
        _settings[RecordRuleSetRetireJob.PageSizeKey] = "1";
        _settings[RecordRuleSetRetireJob.MaxPagesKey] = "2";

        var first = await RunAsync();

        first.GetProperty("ruleSets").GetProperty("retired").GetInt32().Should().Be(2);
        first.GetProperty("ruleSets").GetProperty("moreRemain").GetBoolean().Should().BeTrue();
        sets.Count(s => Status(s) == RuleSetService.RuleSetRetired).Should().Be(2);
        H.Queries.Where(q => q.EntityName == RuleSet && q.TopCount == 1).Should().HaveCount(2, "two pages of one row");

        var second = await RunAsync();

        second.GetProperty("ruleSets").GetProperty("retired").GetInt32().Should().Be(1);
        second.GetProperty("ruleSets").GetProperty("moreRemain").GetBoolean().Should().BeFalse();
        sets.Should().OnlyContain(s => Status(s) == RuleSetService.RuleSetRetired);
    }

    // ── Registration ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "117: the job is registered through AddScheduledJob in AddFinanceModule, disabled, on the configured or default cron")]
    public void TheJob_IsRegisteredThroughAddScheduledJob_Disabled()
    {
        var services = new ServiceCollection();
        services.AddFinanceModule(new ConfigurationBuilder().Build());

        services.Where(d => d.ServiceType == typeof(RecordRuleSetRetireJob)).Should().ContainSingle()
            .Which.Lifetime.Should().Be(ServiceLifetime.Singleton);
        services.Should().NotContain(d => d.ImplementationType == typeof(RecordRuleSetRetireJob) && d.ServiceType != typeof(RecordRuleSetRetireJob),
            "it is an IScheduledJob only through its ScheduledJobRegistration, never a hosted service");

        var registration = RegistrationOf(services, new ConfigurationBuilder().Build());
        registration.Enabled.Should().BeFalse("it runs only when an admin triggers or enables it");
        registration.CronSchedule.Should().Be(RecordRuleSetRetireJob.DefaultCronSchedule);
        registration.Job.Should().BeOfType<RecordRuleSetRetireJob>();

        var configured = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [RecordRuleSetRetireJob.CronKey] = "15 2 * * *",
        }).Build();
        var withCron = new ServiceCollection();
        withCron.AddFinanceModule(configured);
        RegistrationOf(withCron, configured).CronSchedule.Should().Be("15 2 * * *");
    }

    /// <summary>The job's ScheduledJobRegistration: the registrations' factories are called one by one (the module's other jobs need host-wide
    /// services this test does not provide), and the one that builds this job is returned.</summary>
    private static ScheduledJobRegistration RegistrationOf(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        using var provider = services.BuildServiceProvider();
        var found = new List<ScheduledJobRegistration>();
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(ScheduledJobRegistration)))
        {
            try
            {
                if (descriptor.ImplementationFactory?.Invoke(provider) is ScheduledJobRegistration { Job: RecordRuleSetRetireJob } r) found.Add(r);
            }
            catch (Exception)
            {
                // Another job of the module, whose host-wide dependencies are not registered here.
            }
        }

        return found.Should().ContainSingle("the job is registered exactly once").Subject;
    }

    private sealed class ListLogger(List<(LogLevel Level, string Message)> sink) : ILogger<RecordRuleSetRetireJob>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Add((logLevel, formatter(state, exception)));
    }
}
