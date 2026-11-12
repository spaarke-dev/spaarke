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

        // The organization is asked only when the caller NAMED the firm (?firmId=): the record's billing firm and a firm on a Procedure, override or
        // rule set are lookup values on rows already checked (fix round 3, F-D). Same firm, so the same seed; only its origin differs.
        var firmQuery = what == "organization" ? $"&firmId={F}" : "";
        var response = await host.Client.SendAsync(Get($"/api/finance/projects/{P}/{list}?date=2026-02-01{firmQuery}"));

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
        "retired: exclude row", "retired: record rule version", "changed: record rule version",
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
            // fix round 3, F-A: basedOn in the BaseRetired and Changed marks is read from the record rule's latest version row
            "retired: record rule version" => ("sprk_policyversions", s.Retired.RuleVersion),
            "changed: record rule version" => ("sprk_policyversions", s.Changed.RuleVersion),
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

    // ═══════════════════════════════════════════════════════════════════════════
    // 503: what the answer derives from a Procedure's WHOLE version set (fix round 3, F-B)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A scenario whose answer is decided by Procedure Versions it does not show: (the text that proves the 200 is the scenario, the versions
    /// that are NOT the one chosen). Queried on project P, date 2026-02-01. Seeded under matter M so the parent pin / assignment is on the chain.
    /// </summary>
    private static (string Marker, Guid[] Unchosen) SeedVersionSet(RecordProcedureRig rig, string scenario)
    {
        switch (scenario)
        {
            case "NotYetInForce":
                {
                    // Opens on May 1: neither applied nor a gap on Feb 1. Both versions decide that, and neither is shown.
                    var procedure = rig.AddProcedure("LATER", ProcedureAppliesTo.AllRecords);
                    var v1 = rig.AddVersion(procedure, 1, new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 1), ProcedureEntity.VersionStatusSuperseded);
                    var v2 = rig.AddVersion(procedure, 2, new DateOnly(2026, 6, 1), null);
                    return ("NotYetInForce", [v1, v2]);
                }

            case "HoleBetweenVersions":
                {
                    var procedure = rig.AddProcedure("HOLE", ProcedureAppliesTo.AllRecords);
                    var v1 = rig.AddVersion(procedure, 1, Jan1, new DateOnly(2026, 1, 20), ProcedureEntity.VersionStatusSuperseded);
                    var v2 = rig.AddVersion(procedure, 2, Mar1, null);
                    return ("HoleBetweenVersions", [v1, v2]);
                }

            case "AfterLastVersion":
                {
                    var procedure = rig.AddProcedure("ENDED", ProcedureAppliesTo.AllRecords);
                    var v1 = rig.AddVersion(procedure, 1, Jan1, new DateOnly(2026, 1, 20), ProcedureEntity.VersionStatusSuperseded);
                    return ("AfterLastVersion", [v1]);
                }

            case "AbsentNewerVersion":
                {
                    // Pinned to the only version in force; a Draft version 2 is not "newer in force", and that absence is decided by reading it.
                    var procedure = rig.AddProcedure("PINNED", ProcedureAppliesTo.AssignedRecordsOnly);
                    var v1 = rig.AddVersion(procedure, 1, Jan1, null);
                    var draft = rig.AddVersion(procedure, 2, null, null, ProcedureEntity.VersionStatusDraft);
                    rig.AddRuleVersion(rig.AddProcedureRule(procedure, "PIN-1"), v1, Jan1);
                    rig.Assign(Matter, M, procedure, ProcedureAssignmentEntity.ModePinned, v1);
                    return ("\"pinned\":true", [draft]);
                }

            default: throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null);
        }
    }

    public static TheoryData<string, string, int> VersionSetCases => new()
    {
        { "NotYetInForce", "procedures", 0 }, { "NotYetInForce", "procedures", 1 },
        { "HoleBetweenVersions", "procedures", 0 }, { "HoleBetweenVersions", "procedures", 1 },
        { "HoleBetweenVersions", "rules-in-force", 0 }, { "HoleBetweenVersions", "rules-in-force", 1 },
        { "AfterLastVersion", "procedures", 0 }, { "AfterLastVersion", "rules-in-force", 0 },
        { "AbsentNewerVersion", "procedures", 0 }, { "AbsentNewerVersion", "rules-in-force", 0 },
    };

    [Theory(DisplayName = "084 must-fire (F-B): NotYetInForce, a hole between versions, after the last version and an absent newer version derive from versions the answer does not show; any one of them unreadable is 503")]
    [MemberData(nameof(VersionSetCases))]
    public async Task DerivedFromTheVersionSet_AnUnchosenVersionUnreadable_Is503(string scenario, string list, int which)
    {
        await using var host = await Host.StartAsync();
        var (marker, unchosen) = SeedVersionSet(host.Rig, scenario);
        var url = $"/api/finance/projects/{P}/{list}?date=2026-02-01";

        var open = await host.Client.SendAsync(Get(url));
        open.StatusCode.Should().Be(HttpStatusCode.OK, "the same seed with nothing denied");
        (await open.Content.ReadAsStringAsync()).Should().Contain(marker, $"{scenario}/{list}: the 200 is the scenario");
        host.Rig.Probe.Denied.Add((ProcedureEntity.VersionEntitySet, unchosen[which]));

        await AssertUnavailable(await host.Client.SendAsync(Get(url)), host);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The firm's origin decides whether the organization is asked (fix round 3, F-D)
    // ═══════════════════════════════════════════════════════════════════════════

    private const string OrganizationSet = FinanceAuthorizationFilter.VendorOrganizationEntitySet;

    [Theory(DisplayName = "084 must-not-fire (F-D): a firm that is a LOOKUP value on a checked row (the billing firm, a Procedure's, an Exclude's, a changed rule's set) is named without asking about the organization: 200 though the organization is unreadable")]
    [InlineData("procedures")]
    [InlineData("rules-in-force")]
    public async Task LookupOriginFirm_OrganizationUnreadable_Is200_AndNamed(string list)
    {
        await using var host = await Host.StartAsync();
        var rig = host.Rig;
        rig.BillingFirm(Project, P, F);
        rig.Simple("FIRM-F", ProcedureAppliesTo.Firm, F);
        var general = rig.Simple("GENERAL");
        rig.ExcludeRule(Matter, M, general.Rule, F, reason: "for F", by: Reviewer);
        var other = rig.Simple("OTHER");
        rig.ChangeRule(Matter, M, other.Rule, other.RuleVersion, Jan15, F, "BRR-OTHER");
        rig.Probe.Denied.Add((OrganizationSet, F));

        var response = await host.Client.SendAsync(Get($"/api/finance/projects/{P}/{list}?date=2026-02-01"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        body["firmSource"]!.GetValue<string>().Should().Be("Record");
        body["firm"]!["name"]!.GetValue<string>().Should().Be("Firm F", "the name comes with the row's own read");
        var text = body.ToJsonString();
        text.Split("Firm F").Length.Should().BeGreaterThan(2, "the firm is named on the record, and again on the Procedure / Exclude / changed rule");
        rig.Probe.Batches.Should().ContainSingle();
        rig.Probe.Asked.Should().NotContain((OrganizationSet, F), "a lookup-origin firm is not asked");
    }

    [Theory(DisplayName = "084 must-fire (F-D): a firm named by ?firmId= has nothing vouching for it, so the organization IS asked: an unreadable or nonexistent one is 503 (no firm-existence or firm-name oracle)")]
    [InlineData("procedures", false)]
    [InlineData("procedures", true)]
    [InlineData("rules-in-force", false)]
    [InlineData("rules-in-force", true)]
    public async Task QueryOriginFirm_OrganizationUnreadableOrMissing_Is503(string list, bool exists)
    {
        await using var host = await Host.StartAsync();
        var rig = host.Rig;
        rig.BillingFirm(Project, P, F);
        rig.Simple("FIRM-G", ProcedureAppliesTo.Firm, G);
        var named = exists ? G : Guid.NewGuid();
        var url = $"/api/finance/projects/{P}/{list}?date=2026-02-01&firmId={named}";
        if (exists)
        {
            var control = await host.Client.SendAsync(Get(url));
            control.StatusCode.Should().Be(HttpStatusCode.OK, "the same query with the organization readable");
            (await Json(control))["firmSource"]!.GetValue<string>().Should().Be("Query");
            rig.Probe.Asked.Should().Contain((OrganizationSet, named), "a query-origin firm is asked");
            rig.Probe.Denied.Add((OrganizationSet, named));
        }
        else
        {
            rig.Probe.Unknown.Add((OrganizationSet, named)); // a row that is not there is a rights question that gets no answer
        }

        await AssertUnavailable(await host.Client.SendAsync(Get(url)), host);

        rig.Probe.Asked.Should().Contain((OrganizationSet, named), "the organization is in the asked set");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Mechanical guard (fix round 3): every asked row is load-bearing for the refusal; no version row the answer depends on goes unasked
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "084 guard (round 3): on every seed and both routes, denying ANY ONE row the probe was asked about is 503 with no list")]
    [MemberData(nameof(GuardCases))]
    public async Task EveryAskedRow_DeniedAlone_Is503_WithNoList(string seedName, string list)
    {
        await using var host = await Host.StartAsync();
        var seed = GuardSeedFor(seedName, host.Rig);
        var url = $"/api/finance/{seed.Segment}/{seed.RecordId}/{list}?date={seed.Date}";

        (await host.Client.SendAsync(Get(url))).StatusCode.Should().Be(HttpStatusCode.OK, "nothing denied");
        var asked = host.Rig.Probe.Asked.Distinct().ToList();
        asked.Count.Should().BeGreaterThanOrEqualTo(4, "the seed is rich enough to be a guard");

        foreach (var row in asked)
        {
            host.Rig.Probe.Denied.Clear();
            host.Rig.Probe.Denied.Add(row);
            var response = await host.Client.SendAsync(Get(url));
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, $"{seedName}/{list}: denying only {row}");
            var body = await Json(response);
            body["errorCode"]!.GetValue<string>().Should().Be("procedures.unavailable");
            body.ContainsKey("procedures").Should().BeFalse($"denying only {row}: never a partial list");
            body.ContainsKey("rules").Should().BeFalse($"denying only {row}: never a partial list");
        }
    }

    /// <summary>
    /// The version rows that exist in a seed and are NOT asked of the caller, and whose removal changes nothing in the 200 body: (seed/list/label)
    /// to why. Anything else unasked must change the body when removed (that would be an F1: a value derived from a row nobody checked), so an
    /// entry here is a claim the test re-proves on every run, and a stale entry fails too.
    /// </summary>
    private static readonly Dictionary<string, string> UnaskedVersionRows = new()
    {
        // The Procedures list shows no rule at all, so no policy version can appear in its body.
        ["changed and base retired/procedures/policy version 1 of rule 'BRR-DROPPED' (in force from 2026-01-15)"] = ProceduresListShowsNoRule,
        ["changed and base retired/procedures/policy version 1 of rule 'BRR-GENERAL' (in force from 2026-01-15)"] = ProceduresListShowsNoRule,
        ["changed and base retired/procedures/policy version 1 of rule 'DROPPED' (in force from 2026-01-01)"] = ProceduresListShowsNoRule,
        ["changed and base retired/procedures/policy version 2 of rule 'GENERAL' (in force from 2026-03-01)"] = ProceduresListShowsNoRule,
        ["conflicting parents/procedures/policy version 1 of rule 'PIN-1' (in force from 2026-01-01)"] = ProceduresListShowsNoRule,
        ["conflicting parents/procedures/policy version 2 of rule 'PIN-1' (in force from 2026-03-01)"] = ProceduresListShowsNoRule,
        ["excluded procedure/procedures/policy version 1 of rule 'GENERAL' (in force from 2026-01-01)"] = ProceduresListShowsNoRule,
        ["excluded procedure/procedures/policy version 1 of rule 'REMOVED' (in force from 2026-01-01)"] = ProceduresListShowsNoRule,
        ["parent pin/procedures/policy version 1 of rule 'PIN-1' (in force from 2026-01-01)"] = ProceduresListShowsNoRule,
        ["refusals seed/procedures/policy version 1 of rule 'RULE-A' (in force from 2026-01-01)"] = ProceduresListShowsNoRule,
        ["refusals seed/procedures/policy version 1 of rule 'RULE-B' (in force from 2026-01-01)"] = ProceduresListShowsNoRule,

        // The Procedure is a Gap (the two parents pin different versions): none of its rules runs, so none is listed.
        ["conflicting parents/rules-in-force/policy version 1 of rule 'PIN-1' (in force from 2026-01-01)"] =
            "the Procedure is a Gap (conflicting pins), so none of its rules is listed; the pins (asked) decide, not the rule versions",
        ["conflicting parents/rules-in-force/policy version 2 of rule 'PIN-1' (in force from 2026-03-01)"] =
            "the Procedure is a Gap (conflicting pins), so none of its rules is listed; the pins (asked) decide, not the rule versions",

        // A Procedure-level Exclude removes the Procedure and all its rules from the list; the Exclude row (asked) is what decides.
        ["excluded procedure/rules-in-force/Procedure version 1 of 'Procedure REMOVED'"] =
            "the Procedure is removed by a Procedure-level Exclude, so nothing about it is listed; the Exclude row (asked) decides, not its versions",
        ["excluded procedure/rules-in-force/policy version 1 of rule 'REMOVED' (in force from 2026-01-01)"] =
            "the Procedure is removed by a Procedure-level Exclude, so none of its rules is listed; the Exclude row (asked) decides",

        // 084e: the outranked seed. The Procedures list shows no rule (legacy rules included: that route does not read them).
        ["outranked/procedures/policy version 1 of rule 'LEGACY-RATE' (in force from 2026-01-01)"] = ProceduresListShowsNoRule,
        ["outranked/procedures/policy version 1 of rule 'LEGACY-CD-RATE' (in force from 2026-01-01)"] = ProceduresListShowsNoRule,
        ["outranked/procedures/policy version 1 of rule 'GENERAL' (in force from 2026-01-01)"] = ProceduresListShowsNoRule,
        ["outranked/procedures/policy version 1 of rule 'FIRM-EXP' (in force from 2026-01-01)"] = ProceduresListShowsNoRule,
        ["outranked/procedures/policy version 1 of rule 'GENERAL-EXP' (in force from 2026-01-01)"] = ProceduresListShowsNoRule,
        // A legacy rule ranked BELOW the top rank of its key: precedence never consults it (O-1), and it is not a row of the list (the list is the
        // resolver's), so nothing in the body is derived from it.
        ["outranked/rules-in-force/policy version 1 of rule 'LEGACY-CD-RATE' (in force from 2026-01-01)"] =
            "a legacy rule below the top rank of its key: precedence never consults it (084e O-1) and the list shows only the resolver's rules, so the body does not depend on it",

        // 084e fix round 1 (D): a gate-refused legacy Firm rule ranked ABOVE the top of its key (GENERAL-TI, All records). The gate refuses it, so
        // it is in no group (R1) and outranks nothing; it is not a row of the list. Its rows are never asked and the body does not depend on them.
        ["outranked/procedures/policy version 1 of rule 'GENERAL-TI' (in force from 2026-01-01)"] = ProceduresListShowsNoRule,
        ["outranked/procedures/policy version 1 of rule 'LEGACY-BAD-TI' (in force from 2026-01-01)"] = ProceduresListShowsNoRule,
        ["outranked/rules-in-force/policy version 1 of rule 'LEGACY-BAD-TI' (in force from 2026-01-01)"] = GateRefusedLegacyAboveTop,

        // 084e fix round 1 (D): the legacy policy and rule-set rows. The Procedures route reads no legacy rule set; the rules route asks every legacy
        // row that decided a mark (LEGACY-RATE and its Firm set), and the rest are below the top (LEGACY-CD-RATE, its Client default set) or refused.
        ["outranked/procedures/legacy Firm rule set first holding 'LEGACY-RATE'"] = ProceduresRouteReadsNoLegacyRow,
        ["outranked/procedures/legacy rule 'LEGACY-RATE'"] = ProceduresRouteReadsNoLegacyRow,
        ["outranked/procedures/legacy ClientDefault rule set first holding 'LEGACY-CD-RATE'"] = ProceduresRouteReadsNoLegacyRow,
        ["outranked/procedures/legacy rule 'LEGACY-CD-RATE'"] = ProceduresRouteReadsNoLegacyRow,
        ["outranked/procedures/legacy rule 'LEGACY-BAD-TI'"] = ProceduresRouteReadsNoLegacyRow,
        ["outranked/rules-in-force/legacy ClientDefault rule set first holding 'LEGACY-CD-RATE'"] =
            "the Client default set holds only LEGACY-CD-RATE, below the top rank of its key (O-1); without it nothing in the body changes",
        ["outranked/rules-in-force/legacy rule 'LEGACY-CD-RATE'"] =
            "a legacy rule below the top rank of its key: precedence never consults it (084e O-1), so the body does not depend on it",
        ["outranked/rules-in-force/legacy rule 'LEGACY-BAD-TI'"] = GateRefusedLegacyAboveTop,
    };

    private const string ProceduresListShowsNoRule =
        "the Procedures list shows no rule, so no policy version appears in its body; the resolver's integrity checks still pass without this row";

    private const string ProceduresRouteReadsNoLegacyRow =
        "the Procedures route does not read legacy rule sets (084e: the mark is the rules route's only), so no legacy row can change its body";

    private const string GateRefusedLegacyAboveTop =
        "a gate-refused legacy rule above the top of its key: the gate refuses it, so it is in no group (084e R1) and the list does not show it; the body does not depend on it";

    /// <summary>
    /// Version rows that are not asked and whose removal makes the resolver fault (503, the same refusal as any other), on a route that does not show
    /// the row. Not a value in the body: the 503 reveals that the data is inconsistent, nothing the row says.
    /// </summary>
    private static readonly Dictionary<string, string> IntegrityFaultRows = new()
    {
        ["changed and base retired/procedures/policy version 1 of rule 'GENERAL' (in force from 2026-01-01)"] =
            "the record rule's version names this version as the one it is based on; without it the resolver faults (RequireBasedOn) even though the Procedures list shows no rule. The rules route asks about it",
    };

    /// <summary>The rows the removal guard walks: every Procedure Version and policy version row, then (084e fix round 1) every legacy policy and
    /// legacy rule-set row, in seeding order.</summary>
    private static List<(string Set, Guid Id, string Label)> GuardRows(RecordProcedureRig rig) => rig.VersionRows.Concat(rig.LegacyRows).ToList();

    /// <summary>The table a guard row is removed from, by its entity set.</summary>
    private static string TableOf(string set) => set switch
    {
        ProcedureEntity.VersionEntitySet => ProcedureEntity.VersionEntity,
        "sprk_policyversions" => PolicyVersion,
        "sprk_policies" => Policy,
        "sprk_rulesets" => RuleSet,
        _ => throw new ArgumentOutOfRangeException(nameof(set), set, null),
    };

    [Theory(DisplayName = "084 guard (round 3; 084e fix 1 adds the legacy policy and rule-set rows): every Procedure Version, policy version and legacy row that exists in a seed is either asked of the caller, or the 200 body is byte-identical without it (listed, with the reason)")]
    [MemberData(nameof(GuardCases))]
    public async Task EveryVersionRow_IsAsked_OrExemptBecauseTheBodyDoesNotDependOnIt(string seedName, string list)
    {
        string UrlOf(GuardSeed s) => $"/api/finance/{s.Segment}/{s.RecordId}/{list}?date={s.Date}";

        await using var baseline = await Host.StartAsync();
        var seed = GuardSeedFor(seedName, baseline.Rig);
        var baselineResponse = await baseline.Client.SendAsync(Get(UrlOf(seed)));
        baselineResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var expected = await baselineResponse.Content.ReadAsStringAsync();
        var asked = baseline.Rig.Probe.Asked.Select(a => a.RecordId).ToHashSet();
        var rows = GuardRows(baseline.Rig);
        rows.Should().NotBeEmpty();

        var exempt = new List<string>();
        var integrityFault = new List<string>();
        var dependsOnAnUnaskedRow = new List<string>();
        var labels = new HashSet<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            var (set, id, label) = rows[i];
            var key = $"{seedName}/{list}/{label}";
            labels.Add(key).Should().BeTrue($"the label names one row: {key}");

            // Denial: asked => 503. Unasked => denial cannot matter (it is never consulted), which is exactly why the removal below exists.
            baseline.Rig.Probe.Denied.Clear();
            baseline.Rig.Probe.Denied.Add((set, id));
            var denied = await baseline.Client.SendAsync(Get(UrlOf(seed)));
            if (asked.Contains(id))
            {
                denied.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, $"{key} is asked, so denying it refuses");
                continue;
            }

            denied.StatusCode.Should().Be(HttpStatusCode.OK, $"{key} is never asked");
            (await denied.Content.ReadAsStringAsync()).Should().Be(expected, $"{key}: denial of an unasked row changes nothing");

            // Removal: the same seed in a fresh rig (ids are counters, so the bodies compare), that one row gone.
            await using var perturbed = await Host.StartAsync();
            var again = GuardSeedFor(seedName, perturbed.Rig);
            var (_, sameId, _) = GuardRows(perturbed.Rig)[i];
            sameId.Should().Be(id, "the seed is deterministic");
            perturbed.Rig.World.Remove(TableOf(set), id);
            var without = await perturbed.Client.SendAsync(Get(UrlOf(again)));
            var withoutText = await without.Content.ReadAsStringAsync();
            if (without.StatusCode == HttpStatusCode.OK && withoutText == expected)
            {
                exempt.Add(key);
            }
            else if (without.StatusCode == HttpStatusCode.ServiceUnavailable && withoutText.Contains("procedures.unavailable", StringComparison.Ordinal))
            {
                integrityFault.Add(key);
            }
            else
            {
                dependsOnAnUnaskedRow.Add($"{key} => {(int)without.StatusCode}");
            }
        }

        var prefix = $"{seedName}/{list}/";
        dependsOnAnUnaskedRow.Should().BeEmpty("a row the 200 body depends on but the caller was never asked about is an F1");
        exempt.Where(k => !UnaskedVersionRows.ContainsKey(k)).Should().BeEmpty("an unasked version row needs an entry in UnaskedVersionRows with its reason");
        integrityFault.Where(k => !IntegrityFaultRows.ContainsKey(k)).Should().BeEmpty("a version row whose removal faults the resolver needs an entry in IntegrityFaultRows with its reason");
        UnaskedVersionRows.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Except(exempt).Should().BeEmpty("a stale exemption");
        IntegrityFaultRows.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).Except(integrityFault).Should().BeEmpty("a stale integrity entry");
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

    private sealed record GuardSeed(string Segment, Guid RecordId, string Date, Guid[] AlsoAsked, Guid[]? AlsoAskedOnRules = null, Guid[]? LookupFirms = null);

    /// <summary>
    /// The seeds the guard walks. <c>AlsoAsked</c>: rows that DECIDED something the answer shows only through a derived value or an absence (a pin
    /// behind <c>pinned</c>/<c>level</c>, an Exclude behind a missing Procedure), so their ids are not in the JSON; the caller must be asked about them too.
    /// </summary>
    public static TheoryData<string, string> GuardCases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var seed in new[] { "refusals seed", "changed and base retired", "conflicting parents", "parent pin", "excluded procedure", "outranked" })
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
                    // F is the record's billing firm and the firm of ASSIGNED-F / the Exclude: a lookup value on rows that are asked (F-D), so its id is
                    // in the body without the organization being asked. This is the guard's only exemption, and it is checked from both sides below.
                    return new GuardSeed("projects", P, "2026-02-01", [s.Assignment], [s.Exclude], [F]);
                }

            case "changed and base retired":
                {
                    var s = rig.SeedChangedAndBaseRetired();
                    return new GuardSeed("projects", P, "2026-03-15", [], [s.Changed.Exclude, s.Retired.Exclude, s.Retired.RuleVersion]);
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

            case "outranked":
                {
                    // 084e: a legacy Firm rule outranks a Procedure rule (Replace), a Firm Procedure's capped ExpenseRule cap-outranks an
                    // All-records one (Split), and a legacy Client default rule sits below the top rank of the first key (never consulted, O-1).
                    rig.BillingFirm(Project, P, F);
                    var legacy = rig.AddLegacyRule("LEGACY-RATE", Sprk.Bff.Api.Services.Finance.RuleSetScope.Firm, F);
                    rig.AddLegacyRule("LEGACY-CD-RATE", Sprk.Bff.Api.Services.Finance.RuleSetScope.ClientDefault);
                    rig.Simple("GENERAL");
                    rig.Simple("FIRM-EXP", ProcedureAppliesTo.Firm, F, type: Sprk.Bff.Api.Services.Signals.RuleType.ExpenseRule,
                        body: """{"type":"ExpenseRule","maxAmountPerItem":500}""");
                    rig.Simple("GENERAL-EXP", type: Sprk.Bff.Api.Services.Signals.RuleType.ExpenseRule,
                        body: """{"type":"ExpenseRule","nonReimbursableCodes":["E109"],"maxAmountPerItem":500}""");
                    // 084e fix round 1 (D): a legacy Firm rule the gate refuses (no incrementHours), ranked ABOVE the top of its key (GENERAL-TI,
                    // All records). It outranks nothing and is never asked; without the gate it would outrank GENERAL-TI.
                    rig.Simple("GENERAL-TI", type: Sprk.Bff.Api.Services.Signals.RuleType.TimeIncrement, body: """{"type":"TimeIncrement","incrementHours":0.1}""");
                    rig.AddLegacyRule("LEGACY-BAD-TI", Sprk.Bff.Api.Services.Finance.RuleSetScope.Firm, F,
                        type: Sprk.Bff.Api.Services.Signals.RuleType.TimeIncrement, body: """{"type":"TimeIncrement"}""");
                    // The legacy rule's set decides its Level under "by", but its id is not in the body.
                    return new GuardSeed("projects", P, "2026-02-01", [], [legacy.RuleSet], [F]);
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

    [Theory(DisplayName = "084 must-fire (F2 guard): every GUID-shaped value in a 200 body, and every row that decided something it shows, was asked of the caller (one exemption: a firm id that is a lookup value on a checked row, F-D, which must NOT be asked)")]
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
        var lookupFirms = seed.LookupFirms ?? [];
        guids.Where(g => !asked.Contains(g) && !lookupFirms.Contains(g)).Should().BeEmpty($"{seedName}/{list}: a GUID in the answer that the caller's rights were never checked against");
        if (lookupFirms.Length > 0)
        {
            lookupFirms.Should().OnlyContain(f => guids.Contains(f) && !asked.Contains(f), "the exemption is real: the firm is in the body and was (rightly) not asked");
        }
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
