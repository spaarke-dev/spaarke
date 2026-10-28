using System.Globalization;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Signals;

/// <summary>
/// Task 061: the four Do-lane policy rows APPLIED to spaarkedev1 (<c>config/do-lane.policies.json</c>), read back from Dataverse and
/// run through the REAL evaluator (<see cref="SignalEvaluator.EvaluateAsync"/>, read-only: it writes nothing) over real data, then
/// compared with what the Daily Briefing collector's own predicates select for the same day (<c>DailyBriefingCollector</c>:
/// open Task events due before today / within 3 days; there is no collector channel for overdue To Dos or a work assignment's
/// response, so those two are compared with an independent FetchXML written from the POML).
/// </summary>
/// <remarks>
/// <para><b>Read-only, apart from the <c>zz-061-</c> rows.</b> No Signal, no Decision Record and no policy row is written; the only writes are the
/// seven test rows of <c>ZzRows_...</c> (events and work assignments), deleted by name prefix in a <c>finally</c> and checked by a zero-count
/// query. Rows created server-side on their creation, if any, are not tracked. The knob proof
/// evaluates a body edited IN MEMORY (<c>now-1d</c> to <c>now-5d</c>), which is exactly what publishing a new version does, minus the
/// write. The policies stay Off (<c>sprk_enabled</c> = No); the nightly job is not run here.</para>
/// <para><b>What a difference is allowed to be.</b> The collector judges an item against the CALLER's local today; the evaluator judges
/// a Date Only bound against the ASSIGNEE's (D-25 as amended by D-129). A row near the boundary can therefore fall on either side
/// of a UTC-day reference, so a difference is accepted only when the row's date is within one day of the reference boundary; any
/// other difference fails the test.</para>
/// <para>Skip-via-return, opt-in, same convention and fixture as <see cref="SignalWriterSeamTests"/> (the writer principal
/// 3121bf1b-9fbf-f111-aaaf-0022482913fc via <see cref="SignalPredicateTests.CallerIdEnvVar"/>).</para>
/// </remarks>
[Trait("status", "new")]
[Trait("Category", "Live")]
public sealed class DoLanePolicySeamTests : IClassFixture<SignalWriterSeamTests.LiveDataverse>
{
    private static readonly Guid TaskEventType = Guid.Parse("124f5fc9-98ff-f011-8406-7c1e525abd8b");
    private const int EventOpen = 659490001;

    private static readonly Dictionary<string, Guid> VersionIds = new()
    {
        ["POL-TASK-OVERDUE"] = Guid.Parse("00000000-0000-4000-8000-000000006111"),
        ["POL-TASK-DUESOON"] = Guid.Parse("00000000-0000-4000-8000-000000006112"),
        ["POL-WA-RESPONSE"] = Guid.Parse("00000000-0000-4000-8000-000000006113"),
        ["POL-TODO-OVERDUE"] = Guid.Parse("00000000-0000-4000-8000-000000006114"),
    };

    private static readonly Dictionary<string, Guid> PolicyIds = new()
    {
        ["POL-TASK-OVERDUE"] = Guid.Parse("00000000-0000-4000-8000-000000006101"),
        ["POL-TASK-DUESOON"] = Guid.Parse("00000000-0000-4000-8000-000000006102"),
        ["POL-WA-RESPONSE"] = Guid.Parse("00000000-0000-4000-8000-000000006103"),
        ["POL-TODO-OVERDUE"] = Guid.Parse("00000000-0000-4000-8000-000000006104"),
    };

    private readonly SignalWriterSeamTests.LiveDataverse _dv;
    private readonly ITestOutputHelper _out;

    public DoLanePolicySeamTests(SignalWriterSeamTests.LiveDataverse dv, ITestOutputHelper output)
    {
        _dv = dv;
        _out = output;
    }

    private static DateTime Today => DateTime.UtcNow.Date;

    private static string Day(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- the rows are what the definition says

    [Fact]
    public async Task TheFourPolicyRows_AreOnDev_DoLane_AndPointAtTheirVersion()
    {
        if (!_dv.IsLive) return;

        foreach (var (code, policyId) in PolicyIds)
        {
            var policy = await _dv.OperatorClient.RetrieveAsync(
                "sprk_policy", policyId,
                new ColumnSet("sprk_policycode", "sprk_lane", "sprk_enabled", "sprk_currentversion", "sprk_worktype", "statecode"));

            policy.GetAttributeValue<string>("sprk_policycode").Should().Be(code);
            policy.GetAttributeValue<OptionSetValue>("sprk_lane").Value.Should().Be(SignalWriter.LaneDo);
            policy.GetAttributeValue<EntityReference>("sprk_currentversion").Id.Should().Be(VersionIds[code]);
            policy.GetAttributeValue<OptionSetValue>("sprk_worktype").Should().NotBeNull($"{code}: W-8 work type");
            // Created Off; the owner turns them on after review. A later enable is the owner's act, so only state the contract
            // that holds before it: the flag is a plain bool the nightly job reads.
            policy.Contains("sprk_enabled").Should().BeTrue();
            _out.WriteLine($"{code}: enabled={policy.GetAttributeValue<bool>("sprk_enabled")} worktype={policy.GetAttributeValue<OptionSetValue>("sprk_worktype").Value}");
        }
    }

    // ---------------------------------------------------------------- membership versus the collector's predicates

    [Fact]
    public async Task OverdueTask_Membership_MatchesTheCollectorsOverduePredicate_ForTheSameDay()
    {
        if (!_dv.IsLive) return;

        var hits = await EvaluateAsync("POL-TASK-OVERDUE");
        var cutoff = Today.AddDays(-1);
        // DailyBriefingCollector.QueryOverdueTasksAsync: type Task, statuscode Open, sprk_duedate OnOrBefore(today - 1), minus its
        // people targeting (the collector answers "for this caller"; the policy answers for every task).
        var reference = await ReferenceIdsAsync("sprk_event", "sprk_eventid", $@"
            <condition attribute='sprk_eventtype_ref' operator='eq' value='{TaskEventType}' />
            <condition attribute='statuscode' operator='eq' value='{EventOpen}' />
            <condition attribute='sprk_duedate' operator='on-or-before' value='{Day(cutoff)}' />");

        await AssertSameMembershipAsync("POL-TASK-OVERDUE", "sprk_event", hits, reference, cutoff);
    }

    [Fact]
    public async Task DueSoonTask_Membership_MatchesTheCollectorsUpcomingWindow_ForTheSameDay()
    {
        if (!_dv.IsLive) return;

        var hits = await EvaluateAsync("POL-TASK-DUESOON");
        // The collector's Upcoming channel reads NextXDays(sprk_duedate, N) with N the caller's Due-soon setting (default 5); the policy's window is the
        // POML's three days, stated explicitly: today through today + 3 (the knob is the 'now+3d' in the body).
        var reference = await ReferenceIdsAsync("sprk_event", "sprk_eventid", $@"
            <condition attribute='sprk_eventtype_ref' operator='eq' value='{TaskEventType}' />
            <condition attribute='statuscode' operator='eq' value='{EventOpen}' />
            <condition attribute='sprk_duedate' operator='on-or-after' value='{Day(Today)}' />
            <condition attribute='sprk_duedate' operator='on-or-before' value='{Day(Today.AddDays(3))}' />");

        await AssertSameMembershipAsync("POL-TASK-DUESOON", "sprk_event", hits, reference, Today, Today.AddDays(3));
    }

    [Fact]
    public async Task DueSoonKnob_WidenedWindow_StillMatchesTheReference_AndSelectsMoreThanTheThreeDayWindow()
    {
        if (!_dv.IsLive) return;

        // The three-day window is empty on dev today, which proves little. The window is the "now+3d" in the body, so widening it to 60
        // days is exactly a new policy version (no deployment) and gives the comparison something to compare.
        var threeDays = await EvaluateAsync("POL-TASK-DUESOON");
        var sixtyDays = await EvaluateAsync("POL-TASK-DUESOON", body => body.Replace("now+3d", "now+60d", StringComparison.Ordinal));
        var reference = await ReferenceIdsAsync("sprk_event", "sprk_eventid", $@"
            <condition attribute='sprk_eventtype_ref' operator='eq' value='{TaskEventType}' />
            <condition attribute='statuscode' operator='eq' value='{EventOpen}' />
            <condition attribute='sprk_duedate' operator='on-or-after' value='{Day(Today)}' />
            <condition attribute='sprk_duedate' operator='on-or-before' value='{Day(Today.AddDays(60))}' />");

        _out.WriteLine($"POL-TASK-DUESOON knob: now+3d={threeDays.Count} now+60d={sixtyDays.Count}");
        threeDays.Select(h => h.SubjectId).Should().BeSubsetOf(sixtyDays.Select(h => h.SubjectId));
        await AssertSameMembershipAsync("POL-TASK-DUESOON(60d)", "sprk_event", sixtyDays, reference, Today, Today.AddDays(60));
    }

    [Fact]
    public async Task OverdueToDo_Membership_CoversEveryOpenAndInProgressToDoPastItsDueDate()
    {
        if (!_dv.IsLive) return;

        var hits = await EvaluateAsync("POL-TODO-OVERDUE");
        var cutoff = Today.AddDays(-1);
        // D-31: ALL To Dos, no regarding-column filter. Status Open (1) and In Progress (659490001).
        var reference = await ReferenceIdsAsync("sprk_todo", "sprk_todoid", $@"
            <condition attribute='statuscode' operator='in'><value>1</value><value>{EventOpen}</value></condition>
            <condition attribute='sprk_duedate' operator='on-or-before' value='{Day(cutoff)}' />");

        await AssertSameMembershipAsync("POL-TODO-OVERDUE", "sprk_todo", hits, reference, cutoff);
    }

    [Fact]
    public async Task WorkAssignmentResponse_Membership_IsActivePastItsResponseDate_AndExcludesEveryRecordedResponse()
    {
        if (!_dv.IsLive) return;

        var hits = await EvaluateAsync("POL-WA-RESPONSE");
        var cutoff = Today.AddDays(-1);

        // The null test in the rule body, checked two independent ways. (1) A reference FetchXML written from the POML with the
        // same null operator: same ids. (2) The OPPOSITE query (a response IS recorded): not one of those ids is a hit. A broken
        // null/not-null (swapped, or dropped) fails (2) or (1).
        var reference = await ReferenceIdsAsync("sprk_workassignment", "sprk_workassignmentid", $@"
            <condition attribute='statecode' operator='eq' value='0' />
            <condition attribute='sprk_responseduedate' operator='on-or-before' value='{Day(cutoff)}' />
            <condition attribute='sprk_respondedon' operator='null' />");
        var answered = await ReferenceIdsAsync("sprk_workassignment", "sprk_workassignmentid", $@"
            <condition attribute='statecode' operator='eq' value='0' />
            <condition attribute='sprk_responseduedate' operator='on-or-before' value='{Day(cutoff)}' />
            <condition attribute='sprk_respondedon' operator='not-null' />");

        var hitIds = hits.Select(h => h.SubjectId).ToHashSet();
        _out.WriteLine($"POL-WA-RESPONSE: hits={hitIds.Count} reference(no response)={reference.Count} answered-but-past-due={answered.Count}");
        hitIds.Intersect(answered).Should().BeEmpty("D-54: a work assignment with a recorded response is not a missing one");

        await AssertSameMembershipAsync("POL-WA-RESPONSE", "sprk_workassignment", hits, reference, cutoff);

        // Each hit groups under the work assignment itself (D-36) and carries its response due date (D-27/D-33).
        foreach (var hit in hits)
        {
            hit.Core.Should().NotBeNull($"{hit.SubjectId}: {hit.CoreRefusalReason}");
            hit.Core!.Entity.Should().Be("sprk_workassignment");
            hit.Core.Id.Should().Be(hit.SubjectId);
            hit.DueDate.Should().NotBeNull();
        }
    }

    // ---------------------------------------------------------------- the knob needs no deployment

    [Fact]
    public async Task TheOverdueKnob_IsAVersionRowEdit_ChangingMembershipWithNoDeployment()
    {
        if (!_dv.IsLive) return;

        var one = await EvaluateAsync("POL-TASK-OVERDUE");
        var five = await EvaluateAsync("POL-TASK-OVERDUE", body => body.Replace("now-1d", "now-5d", StringComparison.Ordinal));
        var tenYears = await EvaluateAsync("POL-TASK-OVERDUE", body => body.Replace("now-1d", "now-3650d", StringComparison.Ordinal));

        var oneIds = one.Select(h => h.SubjectId).ToHashSet();
        var fiveIds = five.Select(h => h.SubjectId).ToHashSet();
        var tenIds = tenYears.Select(h => h.SubjectId).ToHashSet();
        _out.WriteLine($"POL-TASK-OVERDUE knob: now-1d={oneIds.Count} now-5d={fiveIds.Count} now-3650d={tenIds.Count}");

        // A deeper threshold selects a subset (the same query, one number different) -- membership moved without a single code change.
        fiveIds.Should().BeSubsetOf(oneIds);
        tenIds.Should().BeSubsetOf(fiveIds);

        // The query moved: a threshold ten years deep selects fewer than one day deep (when there is anything to select).
        if (oneIds.Count > 0) tenIds.Count.Should().BeLessThan(oneIds.Count);

        // The dropped rows are exactly the tasks due 1 to 4 days ago (within the one-day zone tolerance at each end).
        var dropped = oneIds.Except(fiveIds).ToList();
        _out.WriteLine($"  dropped by now-5d: {dropped.Count}");
        foreach (var id in dropped)
        {
            var due = one.Single(h => h.SubjectId == id).DueDate!.Value.Date;
            due.Should().BeOnOrAfter(Today.AddDays(-6));
        }
    }

    // ---------------------------------------------------------------- core record and due date of every hit

    [Theory]
    [InlineData("POL-TASK-OVERDUE", "sprk_event")]
    [InlineData("POL-TASK-DUESOON", "sprk_event")]
    [InlineData("POL-TODO-OVERDUE", "sprk_todo")]
    public async Task EveryHit_CarriesItsDueDate_AndGroupsUnderACoreRecord_OrIsAnOwnerOnlyNoCoreItem(string code, string subject)
    {
        if (!_dv.IsLive) return;

        var hits = await EvaluateAsync(code);
        var byCore = hits.GroupBy(h => h.Core?.Entity ?? (h.CoreRefusalReason is null ? "(no core, owner-only)" : "(refused)"))
            .ToDictionary(g => g.Key, g => g.Count());
        _out.WriteLine($"{code}: hits={hits.Count} " + string.Join(", ", byCore.Select(kv => $"{kv.Key}={kv.Value}")));

        foreach (var hit in hits)
        {
            hit.SubjectEntity.Should().Be(subject);
            hit.DueDate.Should().NotBeNull($"{code}/{hit.SubjectId}: D-27/D-33 the Signal carries the subject's own due date");
            if (hit.Core is { IsNoCore: false } core)
            {
                core.Entity.Should().BeOneOf("sprk_matter", "sprk_project", subject);
                core.Id.Should().NotBe(Guid.Empty);
            }
            else if (hit.Core is { IsNoCore: true } none)
            {
                none.NoCoreOwner.Should().NotBeNull("D-35: a no-core item is owned by a team, never left ownerless");
            }
            else
            {
                // A refusal is a defect of the data, reported with its reason (D-38), not hidden as a pass.
                hit.CoreRefusalReason.Should().BeNull($"{code}/{hit.SubjectId}: the writer refused to group it");
            }
        }
    }

    // ---------------------------------------------------------------- rows built for the cases dev data does not contain

    private const string TestPrefix = "zz-061-";

    /// <summary>
    /// Dev holds no work assignment with a recorded response and no open task due in the next 60 days, so the comparisons above
    /// cannot tell a working null test or due-soon window from a broken one. This builds those rows (prefix <c>zz-061-</c>, approved
    /// by D-138) and checks the REAL evaluator's answer on each: the null test excludes the answered assignment, the due-soon window
    /// holds the task due in two days and not the one due in ten, and the overdue knob moves the task due three days ago in and out
    /// by editing a number in the body. Every row is deleted afterwards (also on failure) and a zero-count query proves it.
    /// </summary>
    [Fact]
    public async Task ZzRows_ProveTheNullTest_TheDueSoonWindow_AndTheOverdueKnob_OnRealRows()
    {
        if (!_dv.IsLive) return;

        var created = new List<(string Entity, Guid Id)>();
        try
        {
            await DeleteTestRowsAsync(); // a leftover from an interrupted run

            Guid Event(string name, int dueInDays)
            {
                var row = new Entity("sprk_event")
                {
                    ["sprk_eventname"] = TestPrefix + name,
                    ["sprk_eventtype_ref"] = new EntityReference("sprk_eventtype_ref", TaskEventType),
                    ["statuscode"] = new OptionSetValue(EventOpen),
                    ["sprk_duedate"] = Today.AddDays(dueInDays),
                };
                var id = _dv.OperatorClient.Create(row);
                created.Add(("sprk_event", id));
                return id;
            }

            Guid Assignment(string name, int responseDueInDays, bool responded)
            {
                var row = new Entity("sprk_workassignment")
                {
                    ["sprk_name"] = TestPrefix + name,
                    ["sprk_responseduedate"] = Today.AddDays(responseDueInDays),
                };
                if (responded) row["sprk_respondedon"] = Today.AddDays(-1);
                var id = _dv.OperatorClient.Create(row);
                created.Add(("sprk_workassignment", id));
                return id;
            }

            var dueInTwo = Event("due-in-2-days", 2);
            var dueInTen = Event("due-in-10-days", 10);
            var overdueThree = Event("overdue-3-days", -3);
            var overdueThirty = Event("overdue-30-days", -30);
            var unanswered = Assignment("wa-unanswered-past-due", -5, responded: false);
            var answered = Assignment("wa-answered-past-due", -5, responded: true);
            var notDue = Assignment("wa-unanswered-not-due", 5, responded: false);

            var dueSoon = (await EvaluateAsync("POL-TASK-DUESOON")).Select(h => h.SubjectId).ToHashSet();
            dueSoon.Should().Contain(dueInTwo, "due in two days is inside today..today+3");
            dueSoon.Should().NotContain(new[] { dueInTen, overdueThree, overdueThirty }, "outside the window, or already overdue");

            var overdue1 = (await EvaluateAsync("POL-TASK-OVERDUE")).Select(h => h.SubjectId).ToHashSet();
            overdue1.Should().Contain(new[] { overdueThree, overdueThirty });
            overdue1.Should().NotContain(new[] { dueInTwo, dueInTen });

            var overdue5 = (await EvaluateAsync("POL-TASK-OVERDUE", b => b.Replace("now-1d", "now-5d", StringComparison.Ordinal)))
                .Select(h => h.SubjectId).ToHashSet();
            overdue5.Should().Contain(overdueThirty);
            overdue5.Should().NotContain(overdueThree, "the knob: now-5d no longer reaches a task due three days ago, with no deployment");

            var response = (await EvaluateAsync("POL-WA-RESPONSE")).Select(h => h.SubjectId).ToHashSet();
            response.Should().Contain(unanswered, "active, past its response date, no response");
            response.Should().NotContain(answered, "D-54: a recorded response is not a missing one (the null test)");
            response.Should().NotContain(notDue, "its response date has not passed");

            _out.WriteLine($"zz-061 rows: due-soon={dueSoon.Count} overdue(1d)={overdue1.Count} overdue(5d)={overdue5.Count} wa={response.Count}; " +
                           "all asserted by id");
        }
        finally
        {
            await DeleteTestRowsAsync();
        }

        // The zero-count query, recorded.
        var left = 0;
        foreach (var (entity, nameColumn) in new[] { ("sprk_event", "sprk_eventname"), ("sprk_workassignment", "sprk_name") })
        {
            left += (await TestRowsAsync(entity, nameColumn)).Count;
        }

        _out.WriteLine($"zz-061- rows remaining after cleanup: {left}");
        left.Should().Be(0);
    }

    private async Task<List<Guid>> TestRowsAsync(string entity, string nameColumn)
    {
        var query = new QueryExpression(entity) { ColumnSet = new ColumnSet(false) };
        query.Criteria.AddCondition(nameColumn, ConditionOperator.BeginsWith, TestPrefix);
        return (await _dv.OperatorClient.RetrieveMultipleAsync(query)).Entities.Select(e => e.Id).ToList();
    }

    private async Task DeleteTestRowsAsync()
    {
        foreach (var (entity, nameColumn) in new[] { ("sprk_event", "sprk_eventname"), ("sprk_workassignment", "sprk_name") })
        {
            foreach (var id in await TestRowsAsync(entity, nameColumn))
            {
                await _dv.OperatorClient.DeleteAsync(entity, id);
            }
        }
    }

    // ---------------------------------------------------------------- a defect this proof found on dev

    private const string WorkAssignmentCatalogWrongDisplayField = "sprk_workassignmentname";

    /// <summary>
    /// FINDING (task 061, filed as an ontology-deferred issue): spaarkedev1's <c>sprk_recordtype_ref</c> row for
    /// <c>sprk_workassignment</c> names <c>sprk_workassignmentname</c> as its display-name column. That column does not exist (the
    /// entity's name column is <c>sprk_name</c>; see also <c>ExternalDataService</c>'s remark), so the evaluator's subject query
    /// (it adds the catalog's display field to the columns it reads) is refused by Dataverse and POL-WA-RESPONSE would fail every
    /// night once enabled. This is DATA on the shared catalog, outside this task's approved writes, so it is reported, not edited.
    /// This fact goes green when the row is corrected; until then <see cref="CatalogRepairingAdapter"/> lets the rest of the proof run.
    /// </summary>
    [Fact]
    public async Task TheLiveCatalogRow_ForWorkAssignment_NamesColumnsThatExistOnTheEntity()
    {
        if (!_dv.IsLive) return;

        var query = new QueryExpression("sprk_recordtype_ref")
        {
            ColumnSet = new ColumnSet("sprk_recorddisplaynamefield", "sprk_regardingrecordnumberfield"),
        };
        query.Criteria.AddCondition("sprk_recordlogicalname", ConditionOperator.Equal, "sprk_workassignment");
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        var row = (await _dv.OperatorClient.RetrieveMultipleAsync(query)).Entities.Single();

        var metadata = (Microsoft.Xrm.Sdk.Messages.RetrieveEntityResponse)await _dv.OperatorClient.ExecuteAsync(
            new Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest
            {
                LogicalName = "sprk_workassignment",
                EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Attributes,
                RetrieveAsIfPublished = false,
            });
        var columns = metadata.EntityMetadata.Attributes.Select(a => a.LogicalName).ToHashSet(StringComparer.Ordinal);

        columns.Should().Contain(row.GetAttributeValue<string>("sprk_recorddisplaynamefield"),
            "the evaluator selects this column from the subject table; a name that is not there fails the whole policy");
        columns.Should().Contain(row.GetAttributeValue<string>("sprk_regardingrecordnumberfield"));
    }

    /// <summary>
    /// The shared sysadmin seam, with ONE in-memory repair: the work assignment catalog row's wrong display column is read as
    /// <c>sprk_name</c> (see <see cref="TheLiveCatalogRow_ForWorkAssignment_NamesColumnsThatExistOnTheEntity"/>). Nothing is written
    /// to Dataverse; every other read and every other row passes straight through.
    /// </summary>
    private sealed class CatalogRepairingAdapter : SignalWriterSeamTests.SysadminAdapter
    {
        public CatalogRepairingAdapter(Microsoft.PowerPlatform.Dataverse.Client.ServiceClient operatorClient) : base(operatorClient) { }

        public override async Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query, CancellationToken ct = default)
        {
            var result = await base.RetrieveMultipleAsync(query, ct);
            if (query.EntityName == "sprk_recordtype_ref")
            {
                foreach (var row in result.Entities)
                {
                    if (row.GetAttributeValue<string>("sprk_recorddisplaynamefield") == WorkAssignmentCatalogWrongDisplayField)
                    {
                        row["sprk_recorddisplaynamefield"] = "sprk_name";
                    }
                }
            }

            return result;
        }
    }

    // ---------------------------------------------------------------- plumbing

    private async Task<IReadOnlyList<SignalHit>> EvaluateAsync(string code, Func<string, string>? editBody = null)
    {
        var version = await _dv.OperatorClient.RetrieveAsync(
            "sprk_policyversion", VersionIds[code],
            new ColumnSet("sprk_ruletype", "sprk_rulebody", "sprk_messagetemplate"));

        var body = version.GetAttributeValue<string>("sprk_rulebody");
        if (editBody is not null) body = editBody(body);

        var spec = new SignalEvaluationPolicy(
            PolicyIds[code], code, SignalWriter.LaneDo, VersionIds[code],
            version.GetAttributeValue<OptionSetValue>("sprk_ruletype").Value.ToString(CultureInfo.InvariantCulture),
            body,
            version.GetAttributeValue<string>("sprk_messagetemplate"));

        var evaluation = await BuildEvaluator().EvaluateAsync(spec, subjectId: null);
        evaluation.PolicyValid.Should().BeTrue($"{code}: the live version row passes the evaluation-time gate");
        return evaluation.Hits;
    }

    private SignalEvaluator BuildEvaluator()
    {
        var writerClient = new OntologyWriterDataverseClient(() => _dv.WriterClient, NullLogger<OntologyWriterDataverseClient>.Instance);
        var sysadmin = new CatalogRepairingAdapter(_dv.OperatorClient);
        var ownership = new RecordOwnershipResolver(
            sysadmin, new ConfigurationBuilder().Build(), NullLogger<RecordOwnershipResolver>.Instance);
        var coreAncestors = new CoreAncestorResolver(
            sysadmin,
            (_, _) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(
                CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute), StringComparer.OrdinalIgnoreCase)),
            NullLogger<CoreAncestorResolver>.Instance);
        var recordTypes = new Sprk.Bff.Api.Tests.TestInfrastructure.RecordTypeCatalogStub(async (name, ct) =>
        {
            var query = new QueryExpression("sprk_recordtype_ref")
            {
                ColumnSet = new ColumnSet("sprk_recordtype_refid", "sprk_recorddisplayname"),
                TopCount = 1,
            };
            query.Criteria.AddCondition("sprk_recordlogicalname", ConditionOperator.Equal, name);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            return (await _dv.OperatorClient.RetrieveMultipleAsync(query, ct)).Entities.FirstOrDefault();
        });
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<Sprk.Bff.Api.Services.ExternalAccess.ContactUserRepresentation>(
            services, _ => Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.Representation(
                new Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.FakeAssignedAccessStore()));
        var scopes = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<
            Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(
            Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services));
        var writer = new SignalWriter(
            writerClient, sysadmin, coreAncestors, recordTypes, ownership, scopes, TimeProvider.System, NullLogger<SignalWriter>.Instance);

        var schema = new RuleBodySchemaValidator();
        var validator = new PolicyVersionValidator(schema, new PredicateCompiler(schema, TimeProvider.System), NullLogger<PolicyVersionValidator>.Instance);
        return new SignalEvaluator(validator, sysadmin, writer, NullLogger<SignalEvaluator>.Instance);
    }

    /// <summary>The ids a FetchXML filter selects, paged. <paramref name="conditions"/> is the body of an AND filter.</summary>
    private async Task<HashSet<Guid>> ReferenceIdsAsync(string entity, string idAttribute, string conditions)
    {
        var ids = new HashSet<Guid>();
        var page = 1;
        string? cookie = null;
        while (true)
        {
            var fetch = new XDocument(new XElement("fetch",
                new XAttribute("page", page), new XAttribute("count", 500),
                cookie is null ? null : new XAttribute("paging-cookie", cookie),
                new XElement("entity", new XAttribute("name", entity),
                    new XElement("attribute", new XAttribute("name", idAttribute)),
                    new XElement("filter", new XAttribute("type", "and"), XElement.Parse($"<c>{conditions}</c>").Elements()))));
            var result = await _dv.OperatorClient.RetrieveMultipleAsync(new FetchExpression(fetch.ToString(SaveOptions.DisableFormatting)));
            foreach (var row in result.Entities) ids.Add(row.GetAttributeValue<Guid?>(idAttribute) ?? row.Id);
            if (!result.MoreRecords) break;
            cookie = result.PagingCookie;
            page++;
        }

        return ids;
    }

    private async Task AssertSameMembershipAsync(
        string code, string entity, IReadOnlyList<SignalHit> hits, HashSet<Guid> reference, DateTime boundary, DateTime? upperBoundary = null)
    {
        var hitIds = hits.Select(h => h.SubjectId).ToHashSet();
        var onlyPolicy = hitIds.Except(reference).ToList();
        var onlyReference = reference.Except(hitIds).ToList();
        _out.WriteLine($"{code}: policy hits={hitIds.Count} reference={reference.Count} onlyPolicy={onlyPolicy.Count} onlyReference={onlyReference.Count}");

        // A difference is allowed only for a row whose own date sits within one day of a reference boundary: there the assignee's
        // today and the UTC-day reference can disagree (D-25 as amended by D-129). Anything else is a defect.
        var dateColumn = entity == "sprk_workassignment" ? "sprk_responseduedate" : "sprk_duedate";
        foreach (var id in onlyPolicy.Concat(onlyReference))
        {
            var row = await _dv.OperatorClient.RetrieveAsync(entity, id, new ColumnSet(dateColumn));
            var due = row.GetAttributeValue<DateTime?>(dateColumn);
            due.Should().NotBeNull($"{code}/{id}: a differing row must have a date to be explained by");
            var d = due!.Value.Date;
            var nearLower = Math.Abs((d - boundary.Date).TotalDays) <= 1;
            var nearUpper = upperBoundary is { } up && Math.Abs((d - up.Date).TotalDays) <= 1;
            (nearLower || nearUpper).Should().BeTrue(
                $"{code}/{id}: due {Day(d)} differs from the reference but is not within a day of its boundary ({Day(boundary)}"
                + (upperBoundary is { } u ? $", {Day(u)}" : string.Empty) + ") -- the policy and the collector's predicate disagree");
            _out.WriteLine($"  explained difference {id}: due {Day(d)} (zone boundary)");
        }
    }
}
