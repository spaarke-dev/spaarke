using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;
using static Sprk.Bff.Api.Tests.Api.Finance.RecordProcedureRig;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// ebilling-r1 task 084 Part A, the HTTP contract of the two read routes per core record through the REAL
/// <c>MapRecordProcedureEndpoints</c>, step 0 (task 099), <see cref="FinanceAuthorizationFilter"/> and the REAL
/// <see cref="RecordProcedureReadService"/> over an in-memory Dataverse: step 0 refuses a flagged-external, row-less or flag-fault caller;
/// unknown and unreadable record are one 404; one unreadable row, an unanswered rights question or a faulted read is 503
/// <c>procedures.unavailable</c> with no list (EB-D-218); nothing is written.
/// </summary>
public sealed class RecordProcedureEndpointsContractTests
{
    private static readonly string[] Lists = { "procedures", "rules-in-force" };

    /// <summary>(route segment, the record's entity set, the record id) for the three core-record types.</summary>
    private static readonly (string Segment, string Set, Guid Id)[] Cores =
    {
        ("matters", "sprk_matters", M), ("projects", "sprk_projects", P), ("workassignments", "sprk_workassignments", W),
    };

    /// <summary>All six routes: (url, the record's entity set, the list).</summary>
    public static TheoryData<string, string, string> AllRoutes
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var core in Cores)
            {
                foreach (var list in Lists)
                {
                    data.Add($"/api/finance/{core.Segment}/{core.Id}/{list}?date=2026-02-01", core.Set, list);
                }
            }

            return data;
        }
    }

    private sealed class Host : IAsyncDisposable
    {
        public readonly RecordProcedureRig Rig = new();
        public readonly FinAuth.RecordingAccessDataSource Access = new();
        public StatedCaller StepZero { get; private set; } = null!;
        public HttpClient Client = null!;
        private WebApplication _app = null!;

        public static async Task<Host> StartAsync(bool grantRecords = true, bool ebillingEnabled = true)
        {
            var host = new Host();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.Services
                .AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = FinanceAuthzTestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = FinanceAuthzTestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, FinanceAuthzTestAuthHandler>(FinanceAuthzTestAuthHandler.SchemeName, _ => { });
            builder.Services.AddAuthorization();
            host.StepZero = StatedCaller.Register(builder.Services);
            builder.Services.AddOptions<EbillingOptions>().Configure(o => o.Enabled = ebillingEnabled);
            builder.Services.AddSingleton<IAccessDataSource>(host.Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();
            builder.Services.AddSingleton<CallerRecordAccessProbe>(host.Rig.Probe);
            builder.Services.AddSingleton(host.Rig.Service);
            if (grantRecords)
            {
                foreach (var core in Cores)
                {
                    host.Access.Grant(core.Set, core.Id, AccessRights.Read);
                }
            }

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapRecordProcedureEndpoints();
            await host._app.StartAsync();
            host.Client = host._app.GetTestClient();
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static HttpRequestMessage Get(string url, bool authenticated = true) => RateCardImportEndpointsTests.Req("GET", url, authenticated);

    private static async Task<JsonObject> Json(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!.AsObject();

    /// <summary>The response without its per-request ids, for a byte comparison of two refusals.</summary>
    private static async Task<string> ShapeOf(HttpResponseMessage r)
    {
        var body = await Json(r);
        body.Remove("traceId");
        body.Remove("correlationId");
        return $"{(int)r.StatusCode} {r.Content.Headers.ContentType} {body.ToJsonString()}";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 401 / step 0 / the record (uniform 404)
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "084 every route: an unauthenticated request is 401 and nothing is read")]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_Unauthenticated_Returns401_AndReadsNothing(string url, string set, string list)
    {
        await using var host = await Host.StartAsync();

        var response = await host.Client.SendAsync(Get(url, authenticated: false));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{set} {list}");
        host.Rig.World.QueriedTables.Should().BeEmpty();
    }

    public static IEnumerable<object[]> AllRoutesRefused => StatedCaller.Cross(AllRoutes, StatedCaller.RefusedKinds);

    [Theory(DisplayName = "084 must-fire: both lists on every core record refuse a flagged-external, row-less or flag-fault caller at step 0, before any check or read")]
    [MemberData(nameof(AllRoutesRefused))]
    public async Task EveryRoute_StepZero_RefusesBeforeAnyCheckOrRead(string url, string set, string list, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Get(url));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: true);
        host.Access.Calls.Should().BeEmpty($"{set} {list} {kind}");
        host.Rig.Probe.Batches.Should().BeEmpty();
        host.Rig.World.QueriedTables.Should().BeEmpty();
    }

    public static IEnumerable<object[]> AllRoutesInternal => StatedCaller.Cross(AllRoutes, StatedCaller.InternalKinds);

    [Theory(DisplayName = "084 must-not-fire: a flag that reads false or blank reaches the record check, asked as the caller about THAT record")]
    [MemberData(nameof(AllRoutesInternal))]
    public async Task EveryRoute_StepZero_AdmitsAnInternalCaller_ToTheRecordCheck(string url, string set, string _, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync(grantRecords: false);
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Get(url));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.Access.Calls.Should().ContainSingle().Which.Set.Should().Be(set);
        host.Rig.World.QueriedTables.Should().BeEmpty("the record check refused before any row was read");
    }

    [Theory(DisplayName = "084 must-fire: a record the caller cannot Read is the uniform 404, byte-identical to a record that does not exist")]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_UnreadableRecord_IsTheSame404AsAnUnknownOne(string url, string set, string _)
    {
        await using var unreadable = await Host.StartAsync(grantRecords: false);
        unreadable.Access.Grant(set, Cores.First(c => c.Set == set).Id, AccessRights.Write | AccessRights.AppendTo);
        var response = await unreadable.Client.SendAsync(Get(url));

        await using var unknown = await Host.StartAsync(grantRecords: false);
        var unknownUrl = url.Replace(Cores.First(c => c.Set == set).Id.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal);
        var unknownResponse = await unknown.Client.SendAsync(Get(unknownUrl));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ShapeOf(response)).Should().Be(await ShapeOf(unknownResponse), "no existence oracle");
        unreadable.Access.Calls.Should().Contain(c => c.Set == set && c.HasToken, "asked as the caller");
        unreadable.Rig.World.QueriedTables.Should().BeEmpty();
    }

    [Theory(DisplayName = "084: a record the caller may Read that is gone from the data is the same uniform 404 (the row vanished after the check)")]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_ReadableRecordWithNoRow_IsTheUniform404(string url, string set, string _)
    {
        await using var host = await Host.StartAsync();
        var id = Cores.First(c => c.Set == set).Id;
        var table = set.TrimEnd('s');
        host.Rig.World.Remove(table, id);
        await using var unknown = await Host.StartAsync(grantRecords: false);

        var response = await host.Client.SendAsync(Get(url));
        var unknownResponse = await unknown.Client.SendAsync(Get(url.Replace(id.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ShapeOf(response)).Should().Be(await ShapeOf(unknownResponse));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 200: the lists; the query
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "084: each route answers 200 with its list for a record the caller can Read, and the caller's rights are asked in ONE batched call")]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_ReadableRecord_Returns200_WithOneBatchedRightsCheck(string url, string set, string list)
    {
        await using var host = await Host.StartAsync();
        host.Rig.Simple("GENERAL");

        var response = await host.Client.SendAsync(Get(url));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        body[list == "procedures" ? "procedures" : "rules"]!.AsArray().Should().HaveCount(1);
        body["date"]!.GetValue<string>().Should().Be("2026-02-01");
        host.Rig.Probe.Batches.Should().ContainSingle("one batched rights check covers every exposed row");
        host.Rig.Probe.CallerLookups.Should().Be(1);
        host.Rig.Probe.Asked.Should().Contain((set, Cores.First(c => c.Set == set).Id), "the route's own record is part of what is checked");
        host.Rig.NoWrites();
    }

    [Theory(DisplayName = "084: a firmId that is not a GUID, or a date that is not yyyy-MM-dd, is 400 and reads no row")]
    [InlineData("firmId=not-a-guid")]
    [InlineData("firmId=00000000-0000-0000-0000-000000000000")]
    [InlineData("date=2026-13-01")]
    [InlineData("date=02/01/2026")]
    public async Task BadQuery_Is400_AndReadsNothing(string query)
    {
        await using var host = await Host.StartAsync();

        foreach (var list in Lists)
        {
            var response = await host.Client.SendAsync(Get($"/api/finance/matters/{M}/{list}?{query}"));
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await Json(response);
            body["errorCode"]!.GetValue<string>().Should().Be("procedures.invalid_query");
            body["correlationId"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace("the 400 carries a correlationId as the 503 does");
        }

        host.Rig.World.QueriedTables.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 503: never a partial list (EB-D-218)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>The rows a list is built from: Procedure P1 (all records, rules A and B), a manual assignment of it on M and a plain Exclude of rule B on M.</summary>
    private sealed record Seeded(
        Guid Procedure, Guid Version, Guid RuleSet, Guid RuleA, Guid RuleAVersion, Guid RuleB, Guid RuleBVersion, Guid Assignment, Guid Exclude);

    private static Seeded Seed(RecordProcedureRig rig)
    {
        var a = rig.Simple("RULE-A");
        var b = rig.AddProcedureRule(a.Procedure, "RULE-B");
        var bVersion = rig.AddRuleVersion(b, a.Version, Jan1);
        var assignment = rig.Assign(Matter, M, a.Procedure, reason: "by hand", by: Reviewer);
        var exclude = rig.ExcludeRule(Matter, M, b, reason: "not for this matter", by: Reviewer);
        return new Seeded(a.Procedure, a.Version, rig.RuleSetOf(a.Procedure), a.Rule, a.RuleVersion, b, bVersion, assignment, exclude);
    }

    /// <summary>One unreadable (or unanswered, or faulted) thing, and which lists it removes. The seed is queried on project P (its parent is matter M).</summary>
    public static TheoryData<string, string> Refusals
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var list in Lists)
            {
                data.Add("parent record", list);
                data.Add("the route's own record", list);
                data.Add("procedure", list);
                data.Add("procedure version", list);
                data.Add("override row", list);
                data.Add("override creator", list);
                data.Add("organization", list);
                data.Add("rights check faults", list);
                data.Add("rights unanswered for one row", list);
                data.Add("caller cannot be established", list);
                data.Add("procedure read faults", list);
                data.Add("override read faults", list);
                data.Add("policy read faults", list);
                data.Add("resolver fault: two versions in force", list);
            }

            data.Add("rule", "rules-in-force");
            data.Add("rule set", "rules-in-force");
            data.Add("policy version", "rules-in-force");
            return data;
        }
    }

    [Theory(DisplayName = "084 must-fire: one row the caller cannot Read, an unanswered or faulted rights check, an unestablished caller or a faulted read is 503 procedures.unavailable and NO list")]
    [MemberData(nameof(Refusals))]
    public async Task OneUnavailableThing_Is503_WithNoList(string what, string list)
    {
        await using var host = await Host.StartAsync();
        var rig = host.Rig;
        var s = Seed(rig);
        rig.BillingFirm(Project, P, F);
        rig.Assign(Matter, M, rig.AddProcedure("ASSIGNED-F", ProcedureAppliesTo.AssignedRecordsOnly, F), reason: "firm", by: Reviewer);
        rig.ExcludeRule(Matter, M, s.RuleA, F, reason: "for F", by: Reviewer);
        Deny(rig, what, s);

        var response = await host.Client.SendAsync(Get($"/api/finance/projects/{P}/{list}?date=2026-02-01"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, what);
        var body = await Json(response);
        body["errorCode"]!.GetValue<string>().Should().Be("procedures.unavailable");
        body.ContainsKey("procedures").Should().BeFalse("never a partial list");
        body.ContainsKey("rules").Should().BeFalse("never a partial list");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("RULE-A").And.NotContain("Procedure RULE-A");
        rig.NoWrites();
    }

    [Theory(DisplayName = "084 must-not-fire: with the same seed and nothing refused, both lists on the project are 200 and name what the refusals removed")]
    [InlineData("procedures")]
    [InlineData("rules-in-force")]
    public async Task SameSeed_NothingRefused_Is200(string list)
    {
        await using var host = await Host.StartAsync();
        var s = Seed(host.Rig);
        host.Rig.BillingFirm(Project, P, F);
        host.Rig.Assign(Matter, M, host.Rig.AddProcedure("ASSIGNED-F", ProcedureAppliesTo.AssignedRecordsOnly, F), reason: "firm", by: Reviewer);
        host.Rig.ExcludeRule(Matter, M, s.RuleA, F, reason: "for F", by: Reviewer);

        var response = await host.Client.SendAsync(Get($"/api/finance/projects/{P}/{list}?date=2026-02-01"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain(list == "procedures" ? "Procedure RULE-A" : "RULE-B");
    }

    private static void Deny(RecordProcedureRig rig, string what, Seeded s)
    {
        switch (what)
        {
            case "parent record": rig.Probe.Denied.Add(("sprk_matters", M)); break;
            case "the route's own record": rig.Probe.Denied.Add(("sprk_projects", P)); break;
            case "procedure": rig.Probe.Denied.Add((ProcedureEntity.EntitySet, s.Procedure)); break;
            case "procedure version": rig.Probe.Denied.Add((ProcedureEntity.VersionEntitySet, s.Version)); break;
            case "override row":
                rig.Probe.Denied.Add((ProcedureAssignmentEntity.EntitySet, s.Assignment));
                rig.Probe.Denied.Add((ProcedureAssignmentEntity.EntitySet, s.Exclude));
                break;
            case "override creator": rig.Probe.Denied.Add(("systemusers", Reviewer)); break;
            case "organization": rig.Probe.Denied.Add((FinanceAuthorizationFilter.VendorOrganizationEntitySet, F)); break;
            case "rule": rig.Probe.Denied.Add(("sprk_policies", s.RuleB)); break;
            case "rule set": rig.Probe.Denied.Add(("sprk_rulesets", s.RuleSet)); break;
            case "policy version": rig.Probe.Denied.Add(("sprk_policyversions", s.RuleBVersion)); break;
            case "rights check faults": rig.Probe.Throws = true; break;
            case "rights unanswered for one row": rig.Probe.Unknown.Add((ProcedureEntity.EntitySet, s.Procedure)); break;
            case "caller cannot be established": rig.Probe.Caller = null; break;
            case "procedure read faults": rig.World.FailingQueriesOf(ProcedureEntity.Entity); break;
            case "override read faults": rig.World.FailingQueriesOf(ProcedureAssignmentEntity.Entity); break;
            case "policy read faults": rig.World.FailingQueriesOf(RecordProcedureRig.Policy); break;
            case "resolver fault: two versions in force": rig.AddVersion(s.Procedure, 2, Jan1, null); break;
            default: throw new ArgumentOutOfRangeException(nameof(what), what, null);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 503: what the rules list derives from the Procedure's deciding rows (fix round 1, F1)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Matter M pins Procedure X to its version 1 (version 2 is newer); queried on project P (filed under M), date 2026-02-01.</summary>
    private static (Guid Procedure, Guid Pin) SeedParentPin(RecordProcedureRig rig)
    {
        var procedure = rig.AddProcedure("Pinned", ProcedureAppliesTo.AssignedRecordsOnly);
        var v1 = rig.AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusSuperseded);
        rig.AddVersion(procedure, 2, Mar1, null);
        rig.AddRuleVersion(rig.AddProcedureRule(procedure, "PIN-1"), v1, Jan1);
        return (procedure, rig.Assign(Matter, M, procedure, ProcedureAssignmentEntity.ModePinned, v1));
    }

    [Fact(DisplayName = "084 must-not-fire: a pin on the parent, nothing refused, is 200 and the rules route says pinned, ManualParent, distance 1")]
    public async Task ParentPin_NothingRefused_RulesRouteSaysPinnedManualParent()
    {
        await using var host = await Host.StartAsync();
        SeedParentPin(host.Rig);

        var response = await host.Client.SendAsync(Get($"/api/finance/projects/{P}/rules-in-force?date=2026-02-01"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rule = (await Json(response))["rules"]!.AsArray().Should().ContainSingle().Subject!;
        rule["procedure"]!["pinned"]!.GetValue<bool>().Should().BeTrue();
        rule["level"]!.GetValue<string>().Should().Be("ManualParent");
        rule["distance"]!.GetValue<int>().Should().Be(1);
    }

    [Theory(DisplayName = "084 must-fire (F1): the parent's pin row, or the parent record it sits on, unreadable is 503 on BOTH routes (pinned, level and distance derive from them)")]
    [InlineData("procedures", "parent pin row")]
    [InlineData("rules-in-force", "parent pin row")]
    [InlineData("procedures", "parent chain record")]
    [InlineData("rules-in-force", "parent chain record")]
    public async Task ParentPin_Unreadable_Is503_OnBothRoutes(string list, string what)
    {
        await using var host = await Host.StartAsync();
        var pin = SeedParentPin(host.Rig);
        host.Rig.Probe.Denied.Add(what == "parent pin row" ? (ProcedureAssignmentEntity.EntitySet, pin.Pin) : ("sprk_matters", M));

        var response = await host.Client.SendAsync(Get($"/api/finance/projects/{P}/{list}?date=2026-02-01"));

        await AssertUnavailable(response, host);
    }

    [Theory(DisplayName = "084 must-fire (F1): the Exclude row that removed a whole Procedure's rules from the list unreadable is 503 on the rules route")]
    [InlineData("exclude row")]
    [InlineData("chain record")]
    public async Task ExcludedProcedure_ItsExcludeRowUnreadable_Is503_OnTheRulesRoute(string what)
    {
        await using var host = await Host.StartAsync();
        host.Rig.Simple("GENERAL");
        var removed = host.Rig.Simple("REMOVED");
        var exclude = host.Rig.Assign(Matter, M, removed.Procedure, ProcedureAssignmentEntity.ModeExcluded);
        var url = $"/api/finance/projects/{P}/rules-in-force?date=2026-02-01";

        var open = await host.Client.SendAsync(Get(url));
        open.StatusCode.Should().Be(HttpStatusCode.OK);
        (await open.Content.ReadAsStringAsync()).Should().Contain("GENERAL").And.NotContain("REMOVED");
        host.Rig.Probe.Denied.Add(what == "exclude row" ? (ProcedureAssignmentEntity.EntitySet, exclude) : ("sprk_matters", M));

        await AssertUnavailable(await host.Client.SendAsync(Get(url)), host);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 503: the rows of a changed rule and a base-retired record rule (fix round 1, F2)
    // ═══════════════════════════════════════════════════════════════════════════

    public static TheoryData<string> ChangeAndRetiredRows => new()
    {
        "changed: base rule", "changed: based-on version", "changed: base version in force", "changed: record rule", "changed: record rule set",
        "changed: exclude row", "retired: base rule", "retired: record rule", "retired: record rule set", "retired: based-on version",
        "retired: exclude row",
    };

    [Theory(DisplayName = "084 must-fire (F2): any one row of a changed rule or a base-retired record rule that the caller cannot Read is 503 on the rules route")]
    [MemberData(nameof(ChangeAndRetiredRows))]
    public async Task ChangedAndBaseRetiredRows_OneUnreadable_Is503(string what)
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.SeedChangedAndBaseRetired();
        host.Rig.Probe.Denied.Add(what switch
        {
            "changed: base rule" => ("sprk_policies", s.ChangedBase),
            "changed: based-on version" => ("sprk_policyversions", s.ChangedBaseV1),
            "changed: base version in force" => ("sprk_policyversions", s.ChangedBaseV2),
            "changed: record rule" => ("sprk_policies", s.Changed.RecordRule),
            "changed: record rule set" => ("sprk_rulesets", s.Changed.RuleSet),
            "changed: exclude row" => (ProcedureAssignmentEntity.EntitySet, s.Changed.Exclude),
            "retired: base rule" => ("sprk_policies", s.RetiredBase),
            "retired: record rule" => ("sprk_policies", s.Retired.RecordRule),
            "retired: record rule set" => ("sprk_rulesets", s.Retired.RuleSet),
            "retired: based-on version" => ("sprk_policyversions", s.RetiredBaseVersion),
            "retired: exclude row" => (ProcedureAssignmentEntity.EntitySet, s.Retired.Exclude),
            _ => throw new ArgumentOutOfRangeException(nameof(what), what, null),
        });

        await AssertUnavailable(await host.Client.SendAsync(Get($"/api/finance/projects/{P}/rules-in-force?date=2026-03-15")), host);
    }

    [Fact(DisplayName = "084 must-not-fire (F2): the same seed with nothing refused is 200 with the changed rule and the base-retired record rule")]
    public async Task ChangedAndBaseRetired_NothingRefused_Is200()
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.SeedChangedAndBaseRetired();

        var response = await host.Client.SendAsync(Get($"/api/finance/projects/{P}/rules-in-force?date=2026-03-15"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rules = (await Json(response))["rules"]!.AsArray();
        rules.Select(r => r!["policyId"]!.GetValue<Guid>()).Should().BeEquivalentTo(new[] { s.Changed.RecordRule, s.Retired.RecordRule });
    }

    private static async Task AssertUnavailable(HttpResponseMessage response, Host host)
    {
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await Json(response);
        body["errorCode"]!.GetValue<string>().Should().Be("procedures.unavailable");
        body.ContainsKey("procedures").Should().BeFalse("never a partial list");
        body.ContainsKey("rules").Should().BeFalse("never a partial list");
        host.Rig.NoWrites();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The kill switch's place in the order (fix round 2, F2): step 0, then the record's Read, THEN the flag
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "084 (F2): flag off + a record the caller can Read is 503 ebilling.disabled on every route, before the service reads or checks anything")]
    [MemberData(nameof(AllRoutes))]
    public async Task FlagOff_ReadableRecord_Is503Disabled(string url, string set, string list)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);

        var response = await host.Client.SendAsync(Get(url));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, $"{set} {list}");
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.Access.Calls.Should().Contain(c => c.Set == set && c.HasToken, "the record's Read ran first, as the caller");
        host.Rig.Probe.Batches.Should().BeEmpty();
        host.Rig.World.QueriedTables.Should().BeEmpty();
    }

    [Theory(DisplayName = "084 must-fire (F2): flag off + a record the caller cannot Read is the uniform 404, byte-identical to the flag-on unknown record (the record's Read runs BEFORE the flag)")]
    [MemberData(nameof(AllRoutes))]
    public async Task FlagOff_UnreadableRecord_IsTheUniform404_NotTheDisabled503(string url, string set, string _)
    {
        await using var off = await Host.StartAsync(grantRecords: false, ebillingEnabled: false);
        var response = await off.Client.SendAsync(Get(url));

        await using var unknown = await Host.StartAsync(grantRecords: false);
        var unknownResponse = await unknown.Client.SendAsync(Get(url.Replace(Cores.First(c => c.Set == set).Id.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "the flag must not answer for a record the caller may not know exists");
        (await ShapeOf(response)).Should().Be(await ShapeOf(unknownResponse), "no existence oracle through the kill switch");
        off.Rig.World.QueriedTables.Should().BeEmpty();
    }

    [Theory(DisplayName = "084 must-fire (F2): flag off + a flagged-external, row-less or flag-fault caller is refused at step 0, before the record check and the flag")]
    [MemberData(nameof(AllRoutesRefused))]
    public async Task FlagOff_StepZero_RefusesFirst(string url, string set, string list, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Get(url));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: true);
        host.Access.Calls.Should().BeEmpty($"{set} {list} {kind}");
        host.Rig.Probe.Batches.Should().BeEmpty();
        host.Rig.World.QueriedTables.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Generic exposure guard (fix round 2, F2): every GUID in a 200 was asked of the caller
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed record GuardSeed(string Segment, Guid RecordId, string Date, Guid[] AlsoAsked, Guid[]? AlsoAskedOnRules = null);

    /// <summary>
    /// The seeds the guard walks. <c>AlsoAsked</c>: rows that DECIDED something the answer shows only through a derived value or an absence (a pin
    /// behind <c>pinned</c>/<c>level</c>, an Exclude behind a missing Procedure), so their ids are not in the JSON; the caller must be asked about them too.
    /// </summary>
    public static TheoryData<string, string> GuardCases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var seed in new[] { "refusals seed", "changed and base retired", "conflicting parents", "parent pin", "excluded procedure" })
            {
                foreach (var list in Lists)
                {
                    data.Add(seed, list);
                }
            }

            return data;
        }
    }

    private static (Guid MatterPin, Guid ProjectPin) SeedConflictingParents(RecordProcedureRig rig)
    {
        rig.World.Add(WorkAssignment, W, ("sprk_name", "Work W"), ("sprk_regardingmatter", Ref(Matter, M)), ("sprk_regardingproject", Ref(Project, P)));
        var procedure = rig.AddProcedure("Pinned twice", ProcedureAppliesTo.AssignedRecordsOnly);
        var v1 = rig.AddVersion(procedure, 1, Jan1, Mar1, ProcedureEntity.VersionStatusSuperseded);
        var v2 = rig.AddVersion(procedure, 2, Mar1, null);
        var rule = rig.AddProcedureRule(procedure, "PIN-1");
        rig.AddRuleVersion(rule, v1, Jan1);
        rig.AddRuleVersion(rule, v2, Mar1, number: 2);
        return (rig.Assign(Matter, M, procedure, ProcedureAssignmentEntity.ModePinned, v1, by: Reviewer), rig.Assign(Project, P, procedure, ProcedureAssignmentEntity.ModePinned, v2, by: Reviewer));
    }

    private static GuardSeed GuardSeedFor(string name, RecordProcedureRig rig)
    {
        switch (name)
        {
            case "refusals seed":
                {
                    var s = Seed(rig);
                    rig.BillingFirm(Project, P, F);
                    rig.Assign(Matter, M, rig.AddProcedure("ASSIGNED-F", ProcedureAppliesTo.AssignedRecordsOnly, F), reason: "firm", by: Reviewer);
                    rig.ExcludeRule(Matter, M, s.RuleA, F, reason: "for F", by: Reviewer);
                    return new GuardSeed("projects", P, "2026-02-01", [s.Assignment], [s.Exclude]);
                }

            case "changed and base retired":
                {
                    var s = rig.SeedChangedAndBaseRetired();
                    return new GuardSeed("projects", P, "2026-03-15", [], [s.Changed.Exclude, s.Retired.Exclude]);
                }

            case "conflicting parents":
                {
                    var (matterPin, projectPin) = SeedConflictingParents(rig);
                    return new GuardSeed("workassignments", W, "2026-03-15", [matterPin, projectPin]);
                }

            case "parent pin":
                {
                    var pin = SeedParentPin(rig);
                    return new GuardSeed("projects", P, "2026-02-01", [pin.Pin, M]);
                }

            case "excluded procedure":
                {
                    rig.Simple("GENERAL");
                    var removed = rig.Simple("REMOVED");
                    return new GuardSeed("projects", P, "2026-02-01", [rig.Assign(Matter, M, removed.Procedure, ProcedureAssignmentEntity.ModeExcluded), M]);
                }

            default: throw new ArgumentOutOfRangeException(nameof(name), name, null);
        }
    }

    private static IEnumerable<Guid> GuidsIn(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var value in o.Select(p => p.Value))
                {
                    foreach (var g in GuidsIn(value)) yield return g;
                }

                break;
            case JsonArray a:
                foreach (var value in a)
                {
                    foreach (var g in GuidsIn(value)) yield return g;
                }

                break;
            case JsonValue v when v.TryGetValue<string>(out var text) && text.Length == 36 && Guid.TryParse(text, out var guid):
                yield return guid;
                break;
        }
    }

    [Theory(DisplayName = "084 must-fire (F2 guard): every GUID-shaped value in a 200 body, and every row that decided something it shows, was asked of the caller (no exemptions: the record id and the firm id are asserted like the rest)")]
    [MemberData(nameof(GuardCases))]
    public async Task Every200Guid_WasAskedOfTheCaller(string seedName, string list)
    {
        await using var host = await Host.StartAsync();
        var seed = GuardSeedFor(seedName, host.Rig);

        var response = await host.Client.SendAsync(Get($"/api/finance/{seed.Segment}/{seed.RecordId}/{list}?date={seed.Date}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var guids = GuidsIn(await Json(response)).Distinct().ToList();
        var asked = host.Rig.Probe.Asked.Select(a => a.RecordId).ToHashSet();
        guids.Should().Contain(seed.RecordId, "the guard walks a body that names the record");
        guids.Count.Should().BeGreaterThanOrEqualTo(3, "the seed is rich enough to be a guard");
        guids.Where(g => !asked.Contains(g)).Should().BeEmpty($"{seedName}/{list}: a GUID in the answer that the caller's rights were never checked against");
        seed.AlsoAsked.Concat(list == "rules-in-force" ? seed.AlsoAskedOnRules ?? [] : []).Where(g => !asked.Contains(g)).Should().BeEmpty($"{seedName}/{list}: a row that decided what the answer shows, never checked");
    }

    [Theory(DisplayName = "084 must-fire (F2): two parents pin different versions and either pin row is unreadable is 503 (the gap's overrides are exposed), on both routes")]
    [InlineData("procedures", true)]
    [InlineData("procedures", false)]
    [InlineData("rules-in-force", true)]
    [InlineData("rules-in-force", false)]
    public async Task ConflictingParents_OnePinDenied_Is503(string list, bool denyMatterPin)
    {
        await using var host = await Host.StartAsync();
        var (matterPin, projectPin) = SeedConflictingParents(host.Rig);
        var url = $"/api/finance/workassignments/{W}/{list}?date=2026-03-15";

        (await host.Client.SendAsync(Get(url))).StatusCode.Should().Be(HttpStatusCode.OK, "the same seed with nothing denied is a 200");
        host.Rig.Probe.Denied.Add((ProcedureAssignmentEntity.EntitySet, denyMatterPin ? matterPin : projectPin));

        await AssertUnavailable(await host.Client.SendAsync(Get(url)), host);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Fault logging (fix round 2, K4): the 503 is the same, the log level says whether it was expected
    // ═══════════════════════════════════════════════════════════════════════════

    public static TheoryData<string, string> FaultLevels
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var list in Lists)
            {
                data.Add("unexpected", list);
                data.Add("http", list);
                data.Add("resolver", list);
            }

            return data;
        }
    }

    [Theory(DisplayName = "084 (K4): an unexpected fault is logged at Error, an expected one (resolver, HTTP) at Warning; the answer is the same 503 either way")]
    [MemberData(nameof(FaultLevels))]
    public async Task Fault_IsLoggedAtItsLevel_AndAnswers503(string fault, string list)
    {
        await using var host = await Host.StartAsync();
        var s = Seed(host.Rig);
        switch (fault)
        {
            case "unexpected": host.Rig.Probe.Fault = new NullReferenceException("a bug"); break;
            case "http": host.Rig.Probe.Fault = new HttpRequestException("rights call failed"); break;
            default: host.Rig.AddVersion(s.Procedure, 2, Jan1, null); break; // two versions in force: the resolver's own fault
        }

        var response = await host.Client.SendAsync(Get($"/api/finance/matters/{M}/{list}?date=2026-02-01"));

        await AssertUnavailable(response, host);
        var log = host.Rig.Log.Entries;
        if (fault == "unexpected")
        {
            log.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Exception == typeof(NullReferenceException));
        }
        else
        {
            log.Should().NotContain(e => e.Level >= LogLevel.Error);
            log.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Exception != null);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Nothing is written
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "084: neither list writes anything, whether it answers 200 or 503")]
    [InlineData("procedures", false)]
    [InlineData("rules-in-force", false)]
    [InlineData("procedures", true)]
    [InlineData("rules-in-force", true)]
    public async Task Lists_WriteNothing(string list, bool refused)
    {
        await using var host = await Host.StartAsync();
        var s = Seed(host.Rig);
        host.Rig.ChangeRule(Matter, M, s.RuleA, s.RuleAVersion, Jan15, code: "BRR-A");
        host.Rig.Probe.Throws = refused;

        var response = await host.Client.SendAsync(Get($"/api/finance/matters/{M}/{list}?date=2026-02-01"));

        response.StatusCode.Should().Be(refused ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        host.Rig.NoWrites();
    }
}
