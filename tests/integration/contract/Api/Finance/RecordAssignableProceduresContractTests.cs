using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;
using static Sprk.Bff.Api.Tests.Api.Finance.RecordProcedureRig;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// ebilling-r1 task 084d-5a (design notes/084d-dry-run-design.md "084d-5 design" and its binding conditions; EB-D-227..230): the assignable
/// list <c>GET /api/finance/{core}/{recordId}/procedures/assignable</c>, through the REAL routes and filters, the REAL
/// <see cref="RecordProcedureReadService"/> over the in-memory world and a REAL <see cref="ProcedureAssignmentService"/> planning through it.
/// Offered = After (the record's list with each candidate's Follow-latest row overlaid) minus Now. Nothing is written
/// (<see cref="RecordProcedureRig.NoWrites"/>). The parity test runs the REAL write over <see cref="RecordProcedureWriteRig"/>.
/// </summary>
/// <remarks>Maintain-class (ADR-038, KEEP contract path as 084's deviation 7): the list's offer is the contract the Assign wizard rests on.</remarks>
public sealed class RecordAssignableProceduresContractTests
{
    private const string AssignmentSet = ProcedureAssignmentEntity.EntitySet;
    private const string ProcedureSet = ProcedureEntity.EntitySet;
    private const string Organizations = "sprk_organizations";
    private const int OtherArea = RuleDomains.PolicyDomainBilling + 1;

    private static readonly (string Segment, string Table, string Set, Guid Id)[] Cores =
    {
        ("matters", Matter, "sprk_matters", M), ("projects", Project, "sprk_projects", P), ("workassignments", WorkAssignment, "sprk_workassignments", W),
    };

    private static readonly string[] AllPrivileges =
    {
        BillingAdminAuthorization.WriteRuleSetPrivilege, BillingAdminAuthorization.CreateRuleSetPrivilege, BillingAdminAuthorization.CreatePolicyPrivilege,
        BillingAdminAuthorization.CreatePolicyVersionPrivilege, BillingAdminAuthorization.WritePolicyVersionPrivilege, BillingAdminAuthorization.WritePolicyPrivilege,
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // Host
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed class Host : IAsyncDisposable
    {
        public readonly RecordProcedureRig Rig = new();
        public readonly FinAuth.RecordingAccessDataSource Access = new();
        public readonly List<IReadOnlyCollection<(string Set, Guid Id)>> Exempted = new();
        public StatedCaller StepZero { get; private set; } = null!;
        public HttpClient Client = null!;
        private WebApplication _app = null!;

        /// <param name="recordRights">The caller's rights on each core record (default: Read, Write and AppendTo).</param>
        /// <param name="privileges">The caller holds every billing-admin privilege (default true).</param>
        public static async Task<Host> StartAsync(
            AccessRights recordRights = AccessRights.Read | AccessRights.Write | AccessRights.AppendTo, bool privileges = true, bool ebillingEnabled = true)
        {
            var host = new Host();
            host.Rig.Service.OnExempt = rows => host.Exempted.Add(rows);
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
            builder.Services.AddSingleton(PlannerOver(host.Rig));
            foreach (var core in Cores)
            {
                host.Access.Grant(core.Set, core.Id, recordRights);
            }

            if (privileges)
            {
                host.Rig.Probe.Held.UnionWith(AllPrivileges);
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

    /// <summary>A REAL planner over the rig's world. Planning asks no owner and closes nothing (both STRICT mocks with no setup).</summary>
    private static ProcedureAssignmentService PlannerOver(RecordProcedureRig rig)
    {
        var writer = (SignalWriter)RuntimeHelpers.GetUninitializedObject(typeof(SignalWriter));
        var closure = new Mock<SignalClosureService>(MockBehavior.Strict, writer, rig.Entities.Object, TimeProvider.System, NullLogger<SignalClosureService>.Instance);
        return new ProcedureAssignmentService(
            rig.Entities.Object, new Mock<IRecordOwnershipResolver>(MockBehavior.Strict).Object, rig.RuleSets, closure.Object,
            new AffectedInvoiceCounter(rig.Entities.Object, rig.Probe, NullLogger<AffectedInvoiceCounter>.Instance),
            NullLogger<ProcedureAssignmentService>.Instance);
    }

    private static HttpRequestMessage Get(string segment, Guid record, string query = "?date=2026-02-01") =>
        RateCardImportEndpointsTests.Req("GET", $"/api/finance/{segment}/{record}/procedures/assignable{query}");

    private static async Task<JsonObject> Json(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!.AsObject();

    private static async Task<string> ShapeOf(HttpResponseMessage r)
    {
        var body = await Json(r);
        body.Remove("traceId");
        body.Remove("correlationId");
        return $"{(int)r.StatusCode} {r.Content.Headers.ContentType} {body.ToJsonString()}";
    }

    private static async Task<JsonObject> Ok(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await Json(response);
    }

    private static async Task AssertUnavailable(HttpResponseMessage response, Host host)
    {
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        body["errorCode"]!.GetValue<string>().Should().Be(RecordProcedureReadService.UnavailableErrorCode);
        body.ContainsKey("procedures").Should().BeFalse("never a partial list");
        host.Rig.NoWrites();
    }

    private static async Task AssertInvalidQuery(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(RecordProcedureEndpoints.InvalidQueryErrorCode);
    }

    private static List<JsonObject> Offered(JsonObject body) => body["procedures"]!.AsArray().Select(p => p!.AsObject()).ToList();

    private static List<Guid> OfferedIds(JsonObject body) => Offered(body).Select(p => p["id"]!.GetValue<Guid>()).ToList();

    private static List<string> OfferedNames(JsonObject body) => Offered(body).Select(p => p["name"]!.GetValue<string>()).ToList();

    private static Guid DerivedAssignmentId(string table, Guid record, Guid procedure) =>
        ProcedureAssignmentService.AssignmentIdFor(table, record, ProcedureAssignmentEntity.ProcedureColumn, procedure);

    /// <summary>An assignable Procedure: Billing, Active, applying to assigned records only, one Active version [<paramref name="from"/>, open) and one rule under it.</summary>
    private static Guid Candidate(RecordProcedureRig rig, string name, Guid? firm = null, DateOnly? from = null)
    {
        var procedure = rig.AddProcedure(name, ProcedureAppliesTo.AssignedRecordsOnly, firm);
        var version = rig.AddVersion(procedure, 1, from ?? Jan1, null);
        var rule = rig.AddProcedureRule(procedure, "R-" + name);
        rig.AddRuleVersion(rule, version, from ?? Jan1);
        return procedure;
    }

    /// <summary>
    /// An assignable Procedure with a hole: version 1 [Jan 1, Jan 15) and version 2 [Mar 1, open), each with its rule. On Feb 1 it is a Gap
    /// (<c>HoleBetweenVersions</c>); on Mar 15 version 2 is in force.
    /// </summary>
    private static Guid WithHole(RecordProcedureRig rig, string name)
    {
        var procedure = rig.AddProcedure(name, ProcedureAppliesTo.AssignedRecordsOnly);
        var v1 = rig.AddVersion(procedure, 1, Jan1, Jan15, ProcedureEntity.VersionStatusSuperseded);
        var v2 = rig.AddVersion(procedure, 2, Mar1, null);
        var rule = rig.AddProcedureRule(procedure, "R-" + name);
        rig.AddRuleVersion(rule, v1, Jan1, to: Jan15);
        rig.AddRuleVersion(rule, v2, Mar1, number: 2);
        return procedure;
    }

    /// <summary>A Procedure with only a Draft version: unpublished, so the resolver never applies it.</summary>
    private static Guid DraftOnly(RecordProcedureRig rig, string name)
    {
        var procedure = rig.AddProcedure(name, ProcedureAppliesTo.AssignedRecordsOnly);
        rig.AddVersion(procedure, 1, Jan1, null, ProcedureEntity.VersionStatusDraft);
        return procedure;
    }

    /// <summary>An override row on a record in a stated state (0 active, 1 inactive), Follow latest.</summary>
    private static Guid OwnRow(RecordProcedureRig rig, string table, Guid record, Guid procedure, int state, int mode = ProcedureAssignmentEntity.ModeFollowLatest)
    {
        var id = Guid.NewGuid();
        rig.World.Add(ProcedureAssignmentEntity.Entity, id,
            (ProcedureAssignmentEntity.ProcedureColumn, Ref(ProcedureEntity.Entity, procedure)),
            (ProcedureAssignmentEntity.ModeColumn, Option(mode)),
            ("sprk_createdbyperson", Ref("systemuser", Reviewer)),
            ("statecode", Option(state)), (table, Ref(table, record)), ("createdon", rig.Tick()));
        return id;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The set: After minus Now, as it would show if assigned now
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "084d-5a: on every core record, an assignable Procedure is offered as it would show if assigned now; one already on the record is not; the shape, label and bound; ONE batched check; the planned rows are exempt; nothing written")]
    [InlineData("matters")]
    [InlineData("projects")]
    [InlineData("workassignments")]
    public async Task Offered_IsAfterMinusNow_OneCheck_NothingWritten(string segment)
    {
        await using var host = await Host.StartAsync();
        var core = Cores.Single(c => c.Segment == segment);
        var general = host.Rig.Simple("GENERAL"); // applies to all records: on the record Now
        var assignable = Candidate(host.Rig, "Assignable");

        var body = await Ok(await host.Client.SendAsync(Get(segment, core.Id)));

        body.Select(kv => kv.Key).Should().Equal(
            "record", "date", "firm", "firmSource", "q", "bound", "tooMany", "procedures", "notOffered", "label");
        body["label"]!.GetValue<string>().Should().Be("If assigned now");
        body["bound"]!.GetValue<int>().Should().Be(50);
        body["tooMany"]!.GetValue<bool>().Should().BeFalse();
        body["q"].Should().BeNull();
        body["date"]!.GetValue<string>().Should().Be("2026-02-01");
        body["record"]!["id"]!.GetValue<Guid>().Should().Be(core.Id);
        body["notOffered"]!.AsArray().Should().BeEmpty();
        OfferedIds(body).Should().Equal(new[] { assignable }, "GENERAL is already on the record Now");
        var item = Offered(body).Single();
        item.Select(kv => kv.Key).Should().Equal("id", "name", "type", "appliesTo", "firm", "statusIfAssigned", "versionInForce", "gap");
        item["name"]!.GetValue<string>().Should().Be("Assignable");
        item["appliesTo"]!.GetValue<string>().Should().Be("AssignedRecordsOnly");
        item["statusIfAssigned"]!.GetValue<string>().Should().Be("InForce");
        item["versionInForce"]!["number"]!.GetValue<int>().Should().Be(1);
        item["gap"].Should().BeNull();
        body.ToJsonString().Should().NotContain("pinned", "no pin versions in the list (084d-5 design)");

        host.Rig.Probe.Batches.Should().HaveCount(2, "the candidates' Read check (app-only fetch), then ONE batched check over Now and After");
        host.Rig.Probe.Batches[0].Should().Equal(new[] { (ProcedureSet, assignable) }, "the first batch is the candidate Procedures, none on the record");
        host.Rig.Probe.Asked.Should().Contain(new[] { (core.Set, core.Id), (ProcedureSet, assignable), (ProcedureSet, general.Procedure) });
        var proposed = DerivedAssignmentId(core.Table, core.Id, assignable);
        host.Rig.Probe.Asked.Should().NotContain((AssignmentSet, proposed), "a planned row is synthetic, exempt");
        host.Exempted.Should().ContainSingle().Which.Should().BeEquivalentTo(new[] { (AssignmentSet, proposed) });
        host.Rig.NoWrites();
    }

    [Fact(DisplayName = "084d-5a EB-D-230: a Procedure that would be a Gap on the date is offered, with the gap and its warning; one not yet in force is offered NotYetInForce with no gap; on a date a version applies both are InForce")]
    public async Task Gap_IsOffered_WithItsWarning()
    {
        await using var host = await Host.StartAsync();
        var hole = WithHole(host.Rig, "Has a hole");
        var later = Candidate(host.Rig, "Starts in March", from: Mar1);

        var feb = await Ok(await host.Client.SendAsync(Get("projects", P, "?date=2026-02-01")));
        OfferedIds(feb).Should().Equal(hole, later);
        var gapItem = Offered(feb)[0];
        gapItem["statusIfAssigned"]!.GetValue<string>().Should().Be("Gap");
        var gap = gapItem["gap"]!.AsObject();
        gap.Select(kv => kv.Key).Should().Equal("kind", "text", "warning");
        gap["kind"]!.GetValue<string>().Should().Be(nameof(ProcedureGapKind.HoleBetweenVersions));
        gap["warning"]!.GetValue<string>().Should().Be(RecordProcedureReadService.GapWarning);
        gap["text"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        Offered(feb)[1]["statusIfAssigned"]!.GetValue<string>().Should().Be("NotYetInForce");
        Offered(feb)[1]["gap"].Should().BeNull("not a gap: the date is before its first version");

        var mar = await Ok(await host.Client.SendAsync(Get("projects", P, "?date=2026-03-15")));
        Offered(mar).Select(p => p["statusIfAssigned"]!.GetValue<string>()).Should().Equal("InForce", "InForce");
        Offered(mar).Select(p => p["gap"]).Should().AllSatisfy(g => g.Should().BeNull());
    }

    [Fact(DisplayName = "084d-5a ontology-r1 condition 3: an unpublished (Draft-only) Procedure is a candidate and planned, but the resolver drops it, so it is not offered")]
    public async Task Unpublished_IsPlanned_ButNotOffered()
    {
        await using var host = await Host.StartAsync();
        var draft = DraftOnly(host.Rig, "Draft only");
        var offered = Candidate(host.Rig, "Published");

        var body = await Ok(await host.Client.SendAsync(Get("projects", P)));

        OfferedIds(body).Should().Equal(offered);
        body["notOffered"]!.AsArray().Should().BeEmpty();
        host.Exempted.SelectMany(e => e).Should().Contain((AssignmentSet, DerivedAssignmentId(Project, P, draft)),
            "the premise: the query did not drop it, the resolver did (no query condition on publication)");
        host.Rig.NoWrites();
    }

    [Fact(DisplayName = "084d-5a: a Retired, a non-Billing and an inactive Procedure are not candidates, so never planned or offered")]
    public async Task RetiredNonBillingInactive_AreNotCandidates()
    {
        await using var host = await Host.StartAsync();
        var retired = host.Rig.AddProcedure("Retired", ProcedureAppliesTo.AssignedRecordsOnly, status: ProcedureEntity.StatusRetired);
        host.Rig.AddVersion(retired, 1, Jan1, null);
        var other = Candidate(host.Rig, "Other area");
        host.Rig.World.Set(ProcedureEntity.Entity, other, ProcedureEntity.AreaColumn, Option(OtherArea));
        var inactive = Candidate(host.Rig, "Inactive");
        host.Rig.World.Set(ProcedureEntity.Entity, inactive, "statecode", Option(1));
        var offered = Candidate(host.Rig, "Offered");

        var body = await Ok(await host.Client.SendAsync(Get("projects", P)));

        OfferedIds(body).Should().Equal(offered);
        var planned = host.Exempted.SelectMany(e => e).Select(e => e.Id).ToList();
        planned.Should().NotContain(new[] { retired, other, inactive }.Select(id => DerivedAssignmentId(Project, P, id)));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // OwnRow (uac-r2 note 2)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "084d-5a uac-r2 note 2: a candidate the record already has a row for (an INACTIVE row too) is notOffered OwnRow; the row and its Procedure are asked; denying either is 503")]
    public async Task OwnRow_IncludingInactive_IsNotOffered_AndAsked()
    {
        await using var host = await Host.StartAsync();
        var lapsed = Candidate(host.Rig, "Lapsed");
        var inactiveRow = OwnRow(host.Rig, Project, P, lapsed, state: 1);
        var draft = DraftOnly(host.Rig, "Draft assigned");
        var activeRow = OwnRow(host.Rig, Project, P, draft, state: 0);
        var offered = Candidate(host.Rig, "Offered");

        var body = await Ok(await host.Client.SendAsync(Get("projects", P)));

        OfferedIds(body).Should().Equal(offered);
        var notOffered = body["notOffered"]!.AsArray().Select(n => n!.AsObject()).ToList();
        notOffered.Select(n => (n["id"]!.GetValue<Guid>(), n["name"]!.GetValue<string>(), n["reason"]!.GetValue<string>())).Should().Equal(
            (draft, "Draft assigned", "OwnRow"), (lapsed, "Lapsed", "OwnRow"));
        host.Rig.Probe.Asked.Should().Contain(new[] { (ProcedureSet, lapsed), (AssignmentSet, inactiveRow), (ProcedureSet, draft), (AssignmentSet, activeRow) });
        host.Exempted.SelectMany(e => e).Select(e => e.Id).Should().NotContain(new[] { DerivedAssignmentId(Project, P, lapsed), DerivedAssignmentId(Project, P, draft) },
            "an own-row candidate is never planned");

        // The own ROW stays all-or-nothing (uac-r2 note 2): denying it is 503.
        host.Rig.Probe.Denied.Add((AssignmentSet, inactiveRow));
        await AssertUnavailable(await host.Client.SendAsync(Get("projects", P)), host);

        // The own-row Procedure is a candidate, so an unreadable one is dropped before judging (uac-r2 2026-11-13): no 503, and no notOffered entry.
        host.Rig.Probe.Denied.Clear();
        host.Rig.Probe.Denied.Add((ProcedureSet, lapsed));
        host.Rig.Probe.Batches.Clear();
        var dropped = await Ok(await host.Client.SendAsync(Get("projects", P)));
        OfferedIds(dropped).Should().Equal(offered);
        dropped["notOffered"]!.AsArray().Select(n => n!["id"]!.GetValue<Guid>()).Should().Equal(draft);
        host.Rig.Probe.Asked.Should().NotContain((AssignmentSet, inactiveRow), "its Procedure was dropped before the own-row step, so its row is never asked");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The firm (EB-D-229; ontology-r1 condition 1)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "084d-5a EB-D-229 must-fire: the list follows the panel's firm: with firm F a firm-F Procedure is offered and a firm-G one is never planned; with no firm neither; with the record's billing firm G (no firmId) the firm-G one")]
    public async Task Firm_NarrowsTheCandidates()
    {
        await using var host = await Host.StartAsync();
        var ofF = Candidate(host.Rig, "Of firm F", RecordProcedureRig.F);
        var ofG = Candidate(host.Rig, "Of firm G", G);
        var open = Candidate(host.Rig, "Of no firm");
        IEnumerable<Guid> Planned() => host.Exempted.SelectMany(e => e).Select(e => e.Id);

        var withF = await Ok(await host.Client.SendAsync(Get("projects", P, $"?date=2026-02-01&firmId={RecordProcedureRig.F}")));
        OfferedIds(withF).Should().BeEquivalentTo(new[] { ofF, open });
        Planned().Should().NotContain(DerivedAssignmentId(Project, P, ofG), "a Procedure of another firm is not a candidate");
        withF["firm"]!["id"]!.GetValue<Guid>().Should().Be(RecordProcedureRig.F);

        host.Exempted.Clear();
        var none = await Ok(await host.Client.SendAsync(Get("projects", P)));
        OfferedIds(none).Should().Equal(open);
        Planned().Should().NotContain(new[] { DerivedAssignmentId(Project, P, ofF), DerivedAssignmentId(Project, P, ofG) });

        host.Exempted.Clear();
        host.Rig.BillingFirm(Project, P, G);
        var billing = await Ok(await host.Client.SendAsync(Get("projects", P)));
        OfferedIds(billing).Should().BeEquivalentTo(new[] { ofG, open });
        Planned().Should().NotContain(DerivedAssignmentId(Project, P, ofF));
    }

    private static readonly MethodInfo FirmConditionHolds = typeof(ProcedureResolution).GetMethod("FirmConditionHolds", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ProcedureResolution.FirmConditionHolds moved: re-pin the assignable list's firm pre-filter");

    [Fact(DisplayName = "084d-5a ontology-r1 condition 1 (pin): the candidate query's firm filter holds exactly when the resolver's FirmConditionHolds does, for every Procedure firm (none, F, G) and list firm (F, none)")]
    public async Task FirmPreFilter_IsTheResolversFirmCondition()
    {
        var rig = new RecordProcedureRig();
        var procedures = new (Guid Id, Guid? Firm)[]
        {
            (Candidate(rig, "None"), null), (Candidate(rig, "F", RecordProcedureRig.F), RecordProcedureRig.F), (Candidate(rig, "G", G), G),
        };
        var planner = PlannerOver(rig);
        foreach (var listFirm in new Guid?[] { RecordProcedureRig.F, null })
        {
            var catalog = await planner.PlanAssignCatalogAsync(
                new WriteRefusingEntityService(rig.Entities.Object), Project, P, listFirm, Array.Empty<Guid>(), null, 50,
                (ids, _) => Task.FromResult<IReadOnlySet<Guid>?>(ids.ToHashSet()), CancellationToken.None);
            catalog.Kind.Should().Be(RuleSetOutcomeKind.Done);
            var planned = catalog.Value!.Plan!.Created.Select(c => c.Id).ToHashSet();
            foreach (var (id, firm) in procedures)
            {
                var resolverHolds = (bool)FirmConditionHolds.Invoke(null, [new ProcedureRow(id, "x", ProcedureAppliesTo.AssignedRecordsOnly, firm), listFirm ?? Guid.Empty])!;
                planned.Contains(DerivedAssignmentId(Project, P, id)).Should().Be(resolverHolds, $"Procedure firm {firm?.ToString() ?? "none"}, list firm {listFirm?.ToString() ?? "none"}");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The bound (EB-D-227; uac-r2 note 1) and the order (ontology-r1 condition 2)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "084d-5a EB-D-227: 50 candidates are all offered; a 51st makes the list null with tooMany (never a partial list); a Procedure already on the record does not count")]
    public async Task Bound_At50_And51()
    {
        await using var host = await Host.StartAsync();
        host.Rig.Simple("GENERAL");
        var fifty = Enumerable.Range(1, 50).Select(n => Candidate(host.Rig, $"Candidate {n:D3}")).ToList();

        var at = await Ok(await host.Client.SendAsync(Get("projects", P)));
        at["tooMany"]!.GetValue<bool>().Should().BeFalse();
        OfferedIds(at).Should().Equal(fifty, "all 50, in name order");

        Candidate(host.Rig, "Candidate 051");
        var over = await Ok(await host.Client.SendAsync(Get("projects", P)));
        over["tooMany"]!.GetValue<bool>().Should().BeTrue();
        over["procedures"].Should().BeNull("never a partial list");
        over["notOffered"]!.AsArray().Should().BeEmpty();
        over["bound"]!.GetValue<int>().Should().Be(50);
        host.Rig.NoWrites();
    }

    [Fact(DisplayName = "084d-5a EB-D-227 (review round 1, must-fire): a Procedure ON the record that sorts first does not eat the bound: 51 candidates behind it are still tooMany with a null list, never a silently partial list of 50")]
    public async Task Bound_OnRecordRowSortingFirst_NeverMakesAPartialList()
    {
        await using var host = await Host.StartAsync();
        // On the record Now (applies to all records) and first by name, so it takes a slot in the candidate query's top rows.
        var onRecord = host.Rig.AddProcedure("A on the record", ProcedureAppliesTo.AllRecords);
        var version = host.Rig.AddVersion(onRecord, 1, Jan1, null);
        var rule = host.Rig.AddProcedureRule(onRecord, "R-A");
        host.Rig.AddRuleVersion(rule, version, Jan1);
        for (var n = 1; n <= 51; n++)
        {
            Candidate(host.Rig, $"Candidate {n:D3}");
        }

        var body = await Ok(await host.Client.SendAsync(Get("projects", P)));

        body["tooMany"]!.GetValue<bool>().Should().BeTrue("51 candidates are past the bound; the on-record Procedure must not hide one of them");
        body["procedures"].Should().BeNull("never a partial list");
        host.Rig.NoWrites();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // uac-r2 T1 (2026-11-13): the stop counts only rows that can still be OFFERED; the caller is always resolved
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>The Procedure rows asked of the caller (with no Now rows, these are the candidates walked and the own-row Procedures).</summary>
    private static List<Guid> ProcedureRowsAsked(Host host) => host.Rig.Probe.Asked.Where(a => a.EntitySet == ProcedureSet).Select(a => a.RecordId).ToList();

    [Fact(DisplayName = "084d-5a uac-r2 T1 (a): 51 readable candidates, one of them an own-row Procedure, give a list of 50 and NOT tooMany (the own-row Procedure is set aside before the scan and never counts)")]
    public async Task T1_OwnRowProcedure_DoesNotCountTowardTheBound()
    {
        await using var host = await Host.StartAsync();
        var candidates = Enumerable.Range(1, 51).Select(n => Candidate(host.Rig, $"Candidate {n:D3}")).ToList();
        var own = candidates[10];
        OwnRow(host.Rig, Project, P, own, state: 1);

        var body = await Ok(await host.Client.SendAsync(Get("projects", P)));

        body["tooMany"]!.GetValue<bool>().Should().BeFalse("50 rows can still be offered; the own-row Procedure never counts");
        OfferedIds(body).Should().Equal(candidates.Where(c => c != own).ToList());
        body["notOffered"]!.AsArray().Select(n => n!["id"]!.GetValue<Guid>()).Should().Equal(own);
        host.Rig.NoWrites();
    }

    [Fact(DisplayName = "084d-5a uac-r2 T1 (b, must-fire): 52 readable candidates, none an own-row Procedure, are tooMany with a null list")]
    public async Task T1_52ReadableCandidates_AreTooMany()
    {
        await using var host = await Host.StartAsync();
        Enumerable.Range(1, 52).ToList().ForEach(n => Candidate(host.Rig, $"Candidate {n:D3}"));

        var body = await Ok(await host.Client.SendAsync(Get("projects", P)));

        body["tooMany"]!.GetValue<bool>().Should().BeTrue();
        body["procedures"].Should().BeNull("never a partial list");
        host.Rig.NoWrites();
    }

    [Fact(DisplayName = "084d-5a uac-r2 T1 (c): the candidate check stops early: with 600 readable candidates only the first rights page (250, in list order) is asked, not all 600; tooMany stays true")]
    public async Task T1_StopsAskingOnceBoundPlusOneAreFound()
    {
        await using var host = await Host.StartAsync();
        var all = Enumerable.Range(1, 600).Select(n => Candidate(host.Rig, $"Candidate {n:D3}")).ToList();

        var body = await Ok(await host.Client.SendAsync(Get("projects", P)));

        body["tooMany"]!.GetValue<bool>().Should().BeTrue();
        body["procedures"].Should().BeNull();
        var asked = ProcedureRowsAsked(host);
        asked.Should().HaveCount(RecordProcedureReadService.RightsPageSize, "one page reaches 51 offerable readable rows; no later page is asked");
        asked.Should().BeEquivalentTo(all.Take(RecordProcedureReadService.RightsPageSize), "the first page in the list's order (name, then id)");
        host.Rig.NoWrites();
    }

    [Fact(DisplayName = "084d-5a uac-r2 T1 (d): with no candidate left after the own-row exclusion the caller is still resolved (no zero-candidate shortcut); the answer is a 200 with an empty list")]
    public async Task T1_NoCandidates_StillResolvesTheCaller()
    {
        await using var none = await Host.StartAsync();
        var emptyResponse = await Ok(await none.Client.SendAsync(Get("projects", P)));
        OfferedIds(emptyResponse).Should().BeEmpty();
        emptyResponse["tooMany"]!.GetValue<bool>().Should().BeFalse();
        ProcedureRowsAsked(none).Should().BeEmpty("only the per-row request is skipped when there are no ids");

        // The same route with ONE candidate that is not an own-row Procedure: the walk resolves the caller once more, for its page. So the empty
        // world's count is that minus one: the own-row step (always) and Now's check (always) both resolved the caller.
        await using var one = await Host.StartAsync();
        Candidate(one.Rig, "Candidate");
        await Ok(await one.Client.SendAsync(Get("projects", P)));
        none.Rig.Probe.CallerLookups.Should().Be(one.Rig.Probe.CallerLookups - 1, "the walk resolves the caller once per page");

        // Zero candidates left once the own-row Procedure is set aside: the same resolutions, the same 200, the own-row Procedure notOffered.
        await using var onlyOwn = await Host.StartAsync();
        var own = Candidate(onlyOwn.Rig, "Own");
        OwnRow(onlyOwn.Rig, Project, P, own, state: 1);
        var answer = await Ok(await onlyOwn.Client.SendAsync(Get("projects", P)));
        OfferedIds(answer).Should().BeEmpty();
        answer["notOffered"]!.AsArray().Select(n => n!["id"]!.GetValue<Guid>()).Should().Equal(own);
        onlyOwn.Rig.Probe.CallerLookups.Should().Be(none.Rig.Probe.CallerLookups, "no walk page: the own-row step and Now's check only");
    }

    [Fact(DisplayName = "084d-5a uac-r2 T1 (f): a denied own-row Procedure among more than 51 readable candidates is not listed (200), and tooMany is computed on the non-own candidates only")]
    public async Task T1_DeniedOwnRowProcedure_IsNotListed_AndNeverCounts()
    {
        // 52 readable non-own candidates plus a denied own-row Procedure: tooMany (52 > 50), no trace of the denied one.
        await using var over = await Host.StartAsync();
        Enumerable.Range(1, 52).ToList().ForEach(n => Candidate(over.Rig, $"Candidate {n:D3}"));
        var hiddenOver = Candidate(over.Rig, "Secret own row");
        OwnRow(over.Rig, Project, P, hiddenOver, state: 1);
        over.Rig.Probe.Denied.Add((ProcedureSet, hiddenOver));
        var tooMany = await Ok(await over.Client.SendAsync(Get("projects", P)));
        tooMany["tooMany"]!.GetValue<bool>().Should().BeTrue();
        tooMany["procedures"].Should().BeNull();
        tooMany.ToJsonString().Should().NotContain(hiddenOver.ToString()).And.NotContain("Secret");

        // 50 readable non-own candidates plus the denied own-row Procedure: not tooMany, 50 offered, nothing about the denied row.
        await using var under = await Host.StartAsync();
        var fifty = Enumerable.Range(1, 50).Select(n => Candidate(under.Rig, $"Candidate {n:D3}")).ToList();
        var hidden = Candidate(under.Rig, "Secret own row");
        var hiddenRow = OwnRow(under.Rig, Project, P, hidden, state: 1);
        under.Rig.Probe.Denied.Add((ProcedureSet, hidden));
        var body = await Ok(await under.Client.SendAsync(Get("projects", P)));
        body["tooMany"]!.GetValue<bool>().Should().BeFalse();
        OfferedIds(body).Should().Equal(fifty);
        body["notOffered"]!.AsArray().Should().BeEmpty("a denied own-row Procedure is not listed");
        body.ToJsonString().Should().NotContain(hidden.ToString()).And.NotContain("Secret");
        under.Rig.Probe.Asked.Should().NotContain((AssignmentSet, hiddenRow), "its Procedure is denied, so its own row is never asked");

        // A READABLE own-row Procedure keeps its notOffered entry, and its own row is still asked: denying that row is 503.
        under.Rig.Probe.Denied.Clear();
        (await Ok(await under.Client.SendAsync(Get("projects", P))))["notOffered"]!.AsArray().Select(n => n!["id"]!.GetValue<Guid>()).Should().Equal(hidden);
        under.Rig.Probe.Denied.Add((AssignmentSet, hiddenRow));
        await AssertUnavailable(await under.Client.SendAsync(Get("projects", P)), under);
    }

    [Fact(DisplayName = "084d-5a re-check F1: the record's own override rows are read as one page; a read that reports more rows than the page is 503 procedures.unavailable with no list (never a list that misses own rows); a one-page read is 200")]
    public async Task T1_OwnRowsPastOnePage_Is503_NeverAListMissingThem()
    {
        await using var host = await Host.StartAsync();
        var own = Candidate(host.Rig, "Own");
        OwnRow(host.Rig, Project, P, own, state: 1);
        var offered = Candidate(host.Rig, "Offered");
        var pastOnePage = false;
        var ownRowReads = 0;
        host.Rig.Entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
            {
                var answer = host.Rig.World.Answer(query);
                // The planner's own-row read: the assignment table, the procedure column alone.
                if (query.EntityName == ProcedureAssignmentEntity.Entity && query.ColumnSet.Columns.SequenceEqual(new[] { ProcedureAssignmentEntity.ProcedureColumn }))
                {
                    ownRowReads++;
                    if (pastOnePage)
                    {
                        answer.MoreRecords = true;
                        answer.PagingCookie = "cookie";
                    }
                }

                return Task.FromResult(answer);
            });

        var onePage = await Ok(await host.Client.SendAsync(Get("projects", P)));
        OfferedIds(onePage).Should().Equal(offered);
        ownRowReads.Should().Be(1, "must-not-fire control: the own-row read was reached and one page is answered");

        pastOnePage = true;
        await AssertUnavailable(await host.Client.SendAsync(Get("projects", P)), host);
        ownRowReads.Should().Be(2);
    }

    [Fact(DisplayName = "084d-5a re-check F3 (a): a second rights page counts toward the bound: 301 candidates, the first 250 holding 200 unreadable and 50 readable, then 1 readable on page 2, are tooMany with a null list")]
    public async Task T1_SecondRightsPage_ReadableRowsThere_CountTowardTheBound()
    {
        await using var host = await Host.StartAsync();
        var all = Enumerable.Range(1, 301).Select(n => Candidate(host.Rig, $"Candidate {n:D3}")).ToList();
        foreach (var id in all.Take(200).Concat(all.Skip(251)))
        {
            host.Rig.Probe.Denied.Add((ProcedureSet, id));
        }

        var body = await Ok(await host.Client.SendAsync(Get("projects", P)));

        body["tooMany"]!.GetValue<bool>().Should().BeTrue("50 readable on page 1 and the 51st on page 2");
        body["procedures"].Should().BeNull("never a partial list");
        body["notOffered"]!.AsArray().Should().BeEmpty();
        ProcedureRowsAsked(host).Should().Contain(all[250], "page 2 was asked");
        host.Rig.NoWrites();
    }

    [Theory(DisplayName = "084d-5a re-check F3 (b): a fault, an unknown row or a short answer on the second rights page (page 1 fine, fewer than 51 readable on it) is 503 procedures.unavailable, never a 200")]
    [InlineData("fault")]
    [InlineData("unknown")]
    [InlineData("short")]
    public async Task T1_SecondRightsPage_FaultUnknownOrShortAnswer_Is503(string failure)
    {
        await using var host = await Host.StartAsync();
        var all = Enumerable.Range(1, 260).Select(n => Candidate(host.Rig, $"Candidate {n:D3}")).ToList();
        foreach (var id in all.Take(200))
        {
            host.Rig.Probe.Denied.Add((ProcedureSet, id));
        }

        var pageTwoRow = (ProcedureSet, all[250]);
        switch (failure)
        {
            case "fault":
                host.Rig.Probe.Answer = targets => targets.Contains(pageTwoRow) ? throw new InvalidOperationException("Test: page 2 faulted") : null;
                break;
            case "unknown":
                host.Rig.Probe.Unknown.Add(pageTwoRow);
                break;
            default:
                host.Rig.Probe.Answer = targets => targets.Contains(pageTwoRow) ? targets.Skip(1).Select(_ => (AccessRights?)AccessRights.Read).ToList() : null;
                break;
        }

        await AssertUnavailable(await host.Client.SendAsync(Get("projects", P)), host);
        ProcedureRowsAsked(host).Should().Contain(all[250], "page 2 was asked, and failed");
    }

    [Fact(DisplayName = "084d-5a EB-D-227 (review round 1; design §6 mutation seed 'the query includes Retired'): what the query rules out (Retired, another area, another firm, inactive) never counts toward the bound: 50 candidates plus those are 50 offered, not tooMany")]
    public async Task Bound_CountsOnlyCandidates()
    {
        await using var host = await Host.StartAsync();
        var fifty = Enumerable.Range(1, 50).Select(n => Candidate(host.Rig, $"Candidate {n:D3}")).ToList();
        var retired = Candidate(host.Rig, "Candidate 051 retired");
        host.Rig.World.Set(ProcedureEntity.Entity, retired, ProcedureEntity.StatusColumn, Option(ProcedureEntity.StatusRetired));
        var otherArea = Candidate(host.Rig, "Candidate 052 other area");
        host.Rig.World.Set(ProcedureEntity.Entity, otherArea, ProcedureEntity.AreaColumn, Option(OtherArea));
        Candidate(host.Rig, "Candidate 053 firm G", G);
        var inactive = Candidate(host.Rig, "Candidate 054 inactive");
        host.Rig.World.Set(ProcedureEntity.Entity, inactive, "statecode", Option(1));

        var body = await Ok(await host.Client.SendAsync(Get("projects", P)));

        body["tooMany"]!.GetValue<bool>().Should().BeFalse("only the 50 real candidates count");
        OfferedIds(body).Should().Equal(fifty);
        host.Rig.NoWrites();
    }

    [Fact(DisplayName = "084d-5a uac-r2 note 1 (TooMany_StillAsksNowRows, must-fire): past the bound the ONE batched check still runs over Now's rows, the record and the query firm; denying a Now row or the firm is 503")]
    public async Task TooMany_StillAsksNowRows()
    {
        await using var host = await Host.StartAsync();
        var general = host.Rig.Simple("GENERAL");
        var fillers = Enumerable.Range(1, 51).Select(n => Candidate(host.Rig, $"Candidate {n:D3}")).ToList();
        var query = $"?date=2026-02-01&firmId={RecordProcedureRig.F}";

        var body = await Ok(await host.Client.SendAsync(Get("projects", P, query)));

        body["tooMany"]!.GetValue<bool>().Should().BeTrue();
        host.Rig.Probe.Batches.Should().HaveCount(2, "the candidates' Read check, then the one batched check over Now's rows");
        host.Rig.Probe.Batches[0].Select(a => a.RecordId).Should().BeEquivalentTo(fillers, "the candidates are asked before they are counted (drop before judge)");
        host.Rig.Probe.Batches[1].Should().Contain(new[]
        {
            ("sprk_projects", P), (ProcedureSet, general.Procedure), (ProcedureEntity.VersionEntitySet, general.Version), (Organizations, RecordProcedureRig.F),
        });
        host.Rig.Probe.Batches[1].Select(a => a.RecordId).Should().NotIntersectWith(fillers, "nothing past the bound is shown, so none of it is in the answer's check");
        host.Exempted.Should().BeEmpty("nothing is planned past the bound");

        foreach (var denied in new[] { (ProcedureSet, general.Procedure), (Organizations, RecordProcedureRig.F) })
        {
            host.Rig.Probe.Denied.Clear();
            host.Rig.Probe.Denied.Add(denied);
            await AssertUnavailable(await host.Client.SendAsync(Get("projects", P, query)), host);
        }
    }

    [Fact(DisplayName = "084d-5a ontology-r1 condition 2: the order is stable: by name (ordinal), then by id")]
    public async Task Order_IsByNameThenId()
    {
        await using var host = await Host.StartAsync();
        var gamma = Candidate(host.Rig, "Gamma");
        var alphaFirst = Candidate(host.Rig, "Alpha");
        var beta = Candidate(host.Rig, "Beta");
        var alphaSecond = Candidate(host.Rig, "Alpha");

        var body = await Ok(await host.Client.SendAsync(Get("projects", P)));

        var alphas = new[] { alphaFirst, alphaSecond }.OrderBy(id => id).ToArray();
        OfferedIds(body).Should().Equal(alphas[0], alphas[1], beta, gamma);
        (await Ok(await host.Client.SendAsync(Get("projects", P)))).ToJsonString().Should().Be(body.ToJsonString(), "the same answer twice");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // q (uac-r2 note 4)
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "084d-5a uac-r2 note 4: q matches the name as typed: the Like wildcards %, _ and [ are literals; q is trimmed and echoed")]
    [InlineData("100%25", "Rate 100%", "100%")]
    [InlineData("_", "Rate_x", "_")]
    [InlineData("%5Bdraft%5D", "[draft] rate", "[draft]")]
    [InlineData("%20Rate_x%20", "Rate_x", "Rate_x")]
    [InlineData("rate%201", "Rate 100%|Rate 1000", "rate 1")]
    public async Task Q_MatchesTheNameAsTyped(string q, string expected, string echoed)
    {
        await using var host = await Host.StartAsync();
        foreach (var name in new[] { "Rate 100%", "Rate 1000", "Rate_x", "RateAx", "[draft] rate", "draft rate" })
        {
            Candidate(host.Rig, name);
        }

        var body = await Ok(await host.Client.SendAsync(Get("projects", P, $"?date=2026-02-01&q={q}")));

        OfferedNames(body).Should().Equal(expected.Split('|'));
        body["q"]!.GetValue<string>().Should().Be(echoed);
    }

    [Theory(DisplayName = "084d-5a uac-r2 note 4: a q that is empty, blank, over 100 characters or holds a control character is 400 procedures.invalid_query (the only bad-input answer), before any read")]
    [InlineData("")]
    [InlineData("%20%20")]
    [InlineData("a%01b")]
    [InlineData("a%09b")]
    [InlineData("a%0Ab")]
    [InlineData("a%7Fb")]
    [InlineData("TOO-LONG")]
    public async Task BadQ_Is400(string q)
    {
        await using var host = await Host.StartAsync();
        var value = q == "TOO-LONG" ? new string('a', ProcedureAssignmentService.MaxNameSearchChars + 1) : q;

        await AssertInvalidQuery(await host.Client.SendAsync(Get("projects", P, $"?date=2026-02-01&q={value}")));
        host.Rig.World.QueriedTables.Should().BeEmpty();
    }

    [Fact(DisplayName = "084d-5a: a q of exactly 100 characters is accepted; a bad firmId or date is the same 400")]
    public async Task Q_AtTheLimit_IsAccepted_AndOtherBadInputIs400()
    {
        await using var host = await Host.StartAsync();
        var body = await Ok(await host.Client.SendAsync(Get("projects", P, $"?q={new string('a', ProcedureAssignmentService.MaxNameSearchChars)}")));
        Offered(body).Should().BeEmpty();

        await AssertInvalidQuery(await host.Client.SendAsync(Get("projects", P, "?firmId=not-a-guid")));
        await AssertInvalidQuery(await host.Client.SendAsync(Get("projects", P, "?date=2026-13-01")));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Refusals: the uniform 404, 403, the kill switch, 503 (uac-r2 Q2, Q3, note 3)
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "084d-5a uac-r2 note 3 must-fire: a record the caller cannot Read, or Reads but cannot write, is the uniform 404, byte-identical to an unknown record, before any privilege is asked and before any read")]
    [InlineData("read-only", true)]
    [InlineData("read-only", false)]
    [InlineData("none", true)]
    [InlineData("none", false)]
    public async Task RecordFilter_IsTheUniform404_BeforeAny403(string rights, bool privileges)
    {
        await using var host = await Host.StartAsync(rights == "none" ? AccessRights.None : AccessRights.Read, privileges);
        Candidate(host.Rig, "Assignable");
        await using var unknown = await Host.StartAsync();
        foreach (var core in Cores)
        {
            var response = await host.Client.SendAsync(Get(core.Segment, core.Id));
            var unknownResponse = await unknown.Client.SendAsync(Get(core.Segment, Guid.Parse("084d5a00-0000-4000-8000-00000000dead")));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound, core.Segment);
            (await ShapeOf(response)).Should().Be(await ShapeOf(unknownResponse), $"{core.Segment}: no existence oracle");
        }

        host.Rig.Probe.PrivilegeCalls.Should().BeEmpty("the record filter refuses before the 403 filter asks anything");
        host.Rig.World.QueriedTables.Should().BeEmpty("no read before the 404");
    }

    [Theory(DisplayName = "084d-5a uac-r2 note 3 must-fire: a flagged-external, row-less or flag-fault caller is refused at step 0 on every route with the same 404 as an unknown record, before any check or read")]
    [InlineData("matters")]
    [InlineData("projects")]
    [InlineData("workassignments")]
    public async Task StepZero_RefusesFirst_WithTheUniform404(string segment)
    {
        await using var unknown = await Host.StartAsync();
        var unknownShape = await ShapeOf(await unknown.Client.SendAsync(Get(segment, Guid.Parse("084d5a00-0000-4000-8000-00000000dead"))));
        foreach (var kind in StatedCaller.RefusedKinds)
        {
            await using var host = await Host.StartAsync();
            host.StepZero.Kind = kind;

            var response = await host.Client.SendAsync(Get(segment, Cores.Single(c => c.Segment == segment).Id));

            await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: true);
            (await ShapeOf(response)).Should().Be(unknownShape, $"{segment} {kind}: byte-identical to an unknown record");
            host.Access.Calls.Should().BeEmpty($"{segment} {kind}");
            host.Rig.World.QueriedTables.Should().BeEmpty();
        }
    }

    [Theory(DisplayName = "084d-5a uac-r2 Q3 must-fire: on every route, with the record allowed, a caller without the assign write's privileges (Admin()) is 403, before any read")]
    [InlineData("matters")]
    [InlineData("projects")]
    [InlineData("workassignments")]
    public async Task NoAdminPrivilege_Is403(string segment)
    {
        await using var host = await Host.StartAsync(privileges: false);
        Candidate(host.Rig, "Assignable");

        var response = await host.Client.SendAsync(Get(segment, Cores.Single(c => c.Segment == segment).Id));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        host.Rig.Probe.PrivilegeCalls.Select(c => c.Privilege).Should().BeEquivalentTo(
            ProcedureAssignmentEndpoints.AssignOrExcludePrivileges().Select(c => c.Operation));
        host.Rig.World.QueriedTables.Should().BeEmpty();
    }

    [Fact(DisplayName = "084d-5a: the route's filters are the dry run's record checks (finance.read, then the write's record check) and then the assign write's privileges, from the same declarations")]
    public void Filters_AreTheWritesDeclarations()
    {
        ProcedureAssignmentEndpoints.DryRunRecordChecks("sprk_projects", P).Select(c => c.Operation)
            .Should().Equal("finance.read", ProcedureAssignmentEndpoints.RecordWriteCheck("sprk_projects", P).Operation);
        var context = new Mock<Microsoft.AspNetCore.Http.EndpointFilterInvocationContext>().Object;
        ProcedureAssignmentEndpoints.AssignableTargets(context).Checks.Should().Equal(ProcedureAssignmentEndpoints.AssignOrExcludePrivileges());
        ProcedureAssignmentEndpoints.AssignOrExcludePrivileges().Should().Equal(BillingAdminAuthorization.Admin().Checks);
    }

    [Fact(DisplayName = "084d-5a: eBilling off is 503 ebilling.disabled after the record and privilege filters, before the service reads anything")]
    public async Task FlagOff_Is503Disabled()
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);

        var response = await host.Client.SendAsync(Get("projects", P));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.Rig.Probe.PrivilegeCalls.Should().NotBeEmpty("the privilege filter ran before the kill switch");
        host.Rig.World.QueriedTables.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Candidates the caller cannot Read: dropped before judging (uac-r2 ruling 2026-11-13, replacing Q2's 503)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>A published, Billing, unfirmed, in-reach Procedure whose name matches "secret": the row the two worlds differ by.</summary>
    private static Guid Unreadable(Host host, string name = "Secret rate card")
    {
        var id = Candidate(host.Rig, name);
        host.Rig.Probe.Denied.Add((ProcedureSet, id));
        return id;
    }

    [Fact(DisplayName = "084d-5a uac-r2 2026-11-13 must-fire: a candidate the caller cannot Read is dropped, never a 503 (the same request offers it when it is readable); no trace of it in the answer")]
    public async Task DeniedCandidate_IsDropped_NotA503()
    {
        await using var host = await Host.StartAsync();
        var readable = Candidate(host.Rig, "Assignable");
        var hidden = Candidate(host.Rig, "Secret rate card");
        OfferedIds(await Ok(await host.Client.SendAsync(Get("projects", P)))).Should().Equal(readable, hidden);

        host.Rig.Probe.Denied.Add((ProcedureSet, hidden));
        host.Rig.Probe.Batches.Clear();
        var body = await Ok(await host.Client.SendAsync(Get("projects", P)));

        OfferedIds(body).Should().Equal(readable);
        body["notOffered"]!.AsArray().Should().BeEmpty();
        body["tooMany"]!.GetValue<bool>().Should().BeFalse();
        body.ToJsonString().Should().NotContain(hidden.ToString()).And.NotContain("Secret");
        host.Rig.Probe.Batches[0].Should().Contain((ProcedureSet, hidden), "it was asked, then dropped before anything judged it");
        host.Rig.Probe.Asked.Skip(host.Rig.Probe.Batches[0].Count).Should().NotContain((ProcedureSet, hidden), "no later check mentions it");
        host.Rig.NoWrites();
    }

    [Fact(DisplayName = "084d-5a uac-r2 2026-11-13 must-fire (a): two worlds that differ ONLY by one unreadable, published, Billing, unfirmed, in-reach Procedure matching q give BYTE-IDENTICAL responses for a q that matches only that row; the readable control is offered")]
    public async Task UnreadableCandidate_OnlyMatchOfQ_IsByteIdenticalToItsAbsence()
    {
        const string query = "?date=2026-02-01&q=secret";
        await using var without = await Host.StartAsync();
        Candidate(without.Rig, "Assignable");
        await using var with = await Host.StartAsync();
        Candidate(with.Rig, "Assignable");
        Unreadable(with);

        var absent = await without.Client.SendAsync(Get("projects", P, query));
        var hidden = await with.Client.SendAsync(Get("projects", P, query));

        absent.StatusCode.Should().Be(HttpStatusCode.OK);
        hidden.StatusCode.Should().Be(absent.StatusCode);
        (await ShapeOf(hidden)).Should().Be(await ShapeOf(absent), "an unreadable Procedure must leave no trace");
        (await Json(absent))["procedures"]!.AsArray().Should().BeEmpty();

        // Must-not-fire control: the same world with the row READABLE offers it, so the byte-identity above is the drop's doing.
        await using var readable = await Host.StartAsync();
        Candidate(readable.Rig, "Assignable");
        var seen = Candidate(readable.Rig, "Secret rate card");
        OfferedIds(await Ok(await readable.Client.SendAsync(Get("projects", P, query)))).Should().Equal(seen);
    }

    [Fact(DisplayName = "084d-5a uac-r2 2026-11-13 must-fire (b): 50 readable candidates plus 1 unreadable give the SAME response as the 50 alone: not tooMany, a list of 50 (the unreadable one does not tip the bound)")]
    public async Task UnreadableCandidate_DoesNotTipTheBound_ByteIdenticalToItsAbsence()
    {
        await using var without = await Host.StartAsync();
        Enumerable.Range(1, 50).ToList().ForEach(n => Candidate(without.Rig, $"Candidate {n:D3}"));
        await using var with = await Host.StartAsync();
        Enumerable.Range(1, 50).ToList().ForEach(n => Candidate(with.Rig, $"Candidate {n:D3}"));
        Unreadable(with, "Candidate 025 secret");

        var absent = await without.Client.SendAsync(Get("projects", P));
        var hidden = await with.Client.SendAsync(Get("projects", P));

        var body = await Ok(hidden);
        body["tooMany"]!.GetValue<bool>().Should().BeFalse();
        Offered(body).Should().HaveCount(50);
        (await ShapeOf(hidden)).Should().Be(await ShapeOf(absent), "an unreadable Procedure must leave no trace");

        // Must-not-fire control: with the 51st READABLE, the list is tooMany.
        await using var readable = await Host.StartAsync();
        Enumerable.Range(1, 50).ToList().ForEach(n => Candidate(readable.Rig, $"Candidate {n:D3}"));
        Candidate(readable.Rig, "Candidate 025 secret");
        (await Ok(await readable.Client.SendAsync(Get("projects", P))))["tooMany"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact(DisplayName = "084d-5a uac-r2 2026-11-13: unreadable candidates never crowd out readable ones (no pre-filter cap): 951 unreadable that sort first and 49 readable behind them offer the 49; 1001 matching rows are tooMany (the ceiling)")]
    public async Task NoPreFilterCap_AndTheCeilingIsTooMany()
    {
        await using var crowded = await Host.StartAsync();
        for (var n = 1; n <= 951; n++)
        {
            var hidden = crowded.Rig.AddProcedure($"A hidden {n:D4}", ProcedureAppliesTo.AssignedRecordsOnly);
            crowded.Rig.Probe.Denied.Add((ProcedureSet, hidden));
        }

        var readable = Enumerable.Range(1, 49).Select(n => Candidate(crowded.Rig, $"Candidate {n:D3}")).ToList();

        var body = await Ok(await crowded.Client.SendAsync(Get("projects", P)));

        body["tooMany"]!.GetValue<bool>().Should().BeFalse();
        OfferedIds(body).Should().Equal(readable);
        ProcedureAssignmentService.MaxCandidateRows.Should().Be(1000);
        crowded.Rig.Probe.Batches.Take(4).SelectMany(b => b).Should().HaveCount(1000, "the whole matching set up to the ceiling (4 pages of 250) is asked, uncapped before the Read filter");

        await using var over = await Host.StartAsync();
        for (var n = 1; n <= 1001; n++)
        {
            var id = over.Rig.AddProcedure($"Candidate {n:D4}", ProcedureAppliesTo.AssignedRecordsOnly);
            if (n > 1)
            {
                // All but one are unreadable: only the ceiling (not the bound, which they would not count toward) makes this tooMany.
                over.Rig.Probe.Denied.Add((ProcedureSet, id));
            }
        }

        var tooMany = await Ok(await over.Client.SendAsync(Get("projects", P)));
        tooMany["tooMany"]!.GetValue<bool>().Should().BeTrue("past the ceiling the set is not judged");
        tooMany["procedures"].Should().BeNull();
        over.Rig.Probe.Batches.Should().ContainSingle("only Now's check: nothing past the ceiling was asked");
        over.Rig.NoWrites();
    }

    [Fact(DisplayName = "084d-5a uac-r2 2026-11-13: the candidates' Read check faulting, or refused as a whole (an unanswered row, the caller not established), is 503, never 'drop all' and never an empty 200")]
    public async Task CandidateReadCheck_FaultOrRefusal_Is503()
    {
        await using var host = await Host.StartAsync();
        Candidate(host.Rig, "Assignable");
        OfferedIds(await Ok(await host.Client.SendAsync(Get("projects", P)))).Should().HaveCount(1);

        host.Rig.Probe.Batches.Clear();
        host.Rig.Probe.Throws = true;
        await AssertUnavailable(await host.Client.SendAsync(Get("projects", P)), host);
        host.Rig.Probe.Batches.Should().ContainSingle("the fault is in the candidates' own check, the first one asked");
        host.Rig.Probe.Batches[0].Should().OnlyContain(a => a.EntitySet == ProcedureSet);

        host.Rig.Probe.Throws = false;
        host.Rig.Probe.Unknown.Add((ProcedureSet, Candidate(host.Rig, "Unanswered")));
        host.Rig.Probe.Batches.Clear();
        await AssertUnavailable(await host.Client.SendAsync(Get("projects", P)), host);
        host.Rig.Probe.Batches.Should().ContainSingle("an unanswered candidate refuses the whole check");

        host.Rig.Probe.Unknown.Clear();
        host.Rig.Probe.Caller = null;
        await AssertUnavailable(await host.Client.SendAsync(Get("projects", P)), host);
    }

    [Fact(DisplayName = "084d-5a uac-r2 2026-11-13: Now's rows, an own row and the firm stay all-or-nothing: one unreadable is 503 although unreadable candidates are dropped")]
    public async Task NowOwnRowAndFirm_Unreadable_StillAre503()
    {
        await using var host = await Host.StartAsync();
        var general = host.Rig.Simple("GENERAL");
        var lapsed = Candidate(host.Rig, "Lapsed");
        var ownRow = OwnRow(host.Rig, Project, P, lapsed, state: 1);
        Candidate(host.Rig, "Assignable");
        var query = $"?date=2026-02-01&firmId={RecordProcedureRig.F}";
        (await host.Client.SendAsync(Get("projects", P, query))).StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var denied in new[] { (ProcedureSet, general.Procedure), (AssignmentSet, ownRow), (Organizations, RecordProcedureRig.F) })
        {
            host.Rig.Probe.Denied.Clear();
            host.Rig.Probe.Denied.Add(denied);
            await AssertUnavailable(await host.Client.SendAsync(Get("projects", P, query)), host);
        }
    }

    [Fact(DisplayName = "084d-5a EB-D-218: a Now row the caller cannot Read is 503; a rights check that faults is 503")]
    public async Task DeniedNowRow_OrFault_Is503()
    {
        await using var host = await Host.StartAsync();
        var general = host.Rig.Simple("GENERAL");
        Candidate(host.Rig, "Assignable");

        host.Rig.Probe.Denied.Add((ProcedureSet, general.Procedure));
        await AssertUnavailable(await host.Client.SendAsync(Get("projects", P)), host);

        host.Rig.Probe.Denied.Clear();
        host.Rig.Probe.Throws = true;
        await AssertUnavailable(await host.Client.SendAsync(Get("projects", P)), host);
    }

    [Fact(DisplayName = "084d-5a: a faulted candidate read is 503 procedures.unavailable, never a partial list")]
    public async Task CandidateReadFault_Is503()
    {
        await using var host = await Host.StartAsync();
        Candidate(host.Rig, "Assignable");
        host.Rig.Entities
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<Microsoft.Xrm.Sdk.Query.QueryExpression>(q => q.EntityName == ProcedureEntity.Entity && q.TopCount != null),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("throttled"));

        await AssertUnavailable(await host.Client.SendAsync(Get("projects", P)), host);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Guards: every GUID asked; every version row asked or not depended on
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>A rich seed: a Procedure on the record, an offered one, a Gap, a firm-F one, a Draft-only one and an inactive own row.</summary>
    private static void RichSeed(RecordProcedureRig rig)
    {
        rig.Simple("GENERAL");
        Candidate(rig, "Offered");
        Candidate(rig, "Starts in March", from: Mar1);
        WithHole(rig, "Has a hole");
        Candidate(rig, "Of firm F", RecordProcedureRig.F);
        DraftOnly(rig, "Draft only");
        var lapsed = Candidate(rig, "Lapsed");
        rig.World.Add(ProcedureAssignmentEntity.Entity, Guid.Parse("084d5a00-0000-4000-8000-0000000000aa"),
            (ProcedureAssignmentEntity.ProcedureColumn, Ref(ProcedureEntity.Entity, lapsed)),
            (ProcedureAssignmentEntity.ModeColumn, Option(ProcedureAssignmentEntity.ModeFollowLatest)),
            ("statecode", Option(1)), (Project, Ref(Project, P)), ("createdon", new DateTime(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc)));
    }

    private const string RichQuery = "?date=2026-02-01&firmId=08400000-0000-4000-8000-0000000000f1";

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

    [Fact(DisplayName = "084d-5a guard (Every200Guid_WasAskedOfTheCaller, assignable): every GUID in the 200 was asked of the caller; the body holds no planned (exempt) row id")]
    public async Task Every200Guid_WasAskedOfTheCaller_Assignable()
    {
        await using var host = await Host.StartAsync();
        RichSeed(host.Rig);

        var body = await Ok(await host.Client.SendAsync(Get("projects", P, RichQuery)));

        var guids = GuidsIn(body).Distinct().ToList();
        var asked = host.Rig.Probe.Asked.Select(a => a.RecordId).ToHashSet();
        var exempt = host.Exempted.SelectMany(e => e).Select(e => e.Id).ToHashSet();
        guids.Count.Should().BeGreaterThanOrEqualTo(6, "the seed is rich enough to be a guard");
        guids.Where(g => !asked.Contains(g)).Should().BeEmpty("a GUID in the answer the caller's rights were never checked against");
        guids.Should().NotIntersectWith(exempt, "a synthetic row is never shown");

        // Must-fire control: a GUID planted in the walk that nobody asked is reported.
        var planted = Guid.Parse("084d5a00-0000-4000-8000-00000000beef");
        guids.Append(planted).Where(g => !asked.Contains(g)).Should().Equal(planted);
        var mutated = body.DeepClone().AsObject();
        mutated["notOffered"]!.AsArray().Add(new JsonObject { ["id"] = planted.ToString() });
        GuidsIn(mutated).Where(g => !asked.Contains(g)).Should().Equal(new[] { planted }, "notOffered is walked");
    }

    [Fact(DisplayName = "084d-5a guard (version rows, assignable): every Procedure Version and policy version row of the seed is asked, or the 200 body is byte-identical without it")]
    public async Task EveryVersionRow_IsAsked_OrTheBodyDoesNotDependOnIt_Assignable()
    {
        await using var baseline = await Host.StartAsync();
        RichSeed(baseline.Rig);
        var expectedResponse = await baseline.Client.SendAsync(Get("projects", P, RichQuery));
        expectedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var expected = await expectedResponse.Content.ReadAsStringAsync();
        var asked = baseline.Rig.Probe.Asked.Select(a => a.RecordId).ToHashSet();
        var rows = baseline.Rig.VersionRows.ToList();
        rows.Should().HaveCountGreaterThan(5);

        var dependsOnAnUnaskedRow = new List<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            var (set, id, label) = rows[i];
            if (asked.Contains(id))
            {
                baseline.Rig.Probe.Denied.Clear();
                baseline.Rig.Probe.Denied.Add((set, id));
                var denied = await baseline.Client.SendAsync(Get("projects", P, RichQuery));
                denied.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, $"{label} is asked, so denying it refuses");
                continue;
            }

            await using var perturbed = await Host.StartAsync();
            RichSeed(perturbed.Rig);
            perturbed.Rig.VersionRows[i].Id.Should().Be(id, "the seed is deterministic");
            perturbed.Rig.World.Remove(set == ProcedureEntity.VersionEntitySet ? ProcedureEntity.VersionEntity : PolicyVersion, id);
            var without = await perturbed.Client.SendAsync(Get("projects", P, RichQuery));
            if (without.StatusCode != HttpStatusCode.OK || await without.Content.ReadAsStringAsync() != expected)
            {
                dependsOnAnUnaskedRow.Add($"{label} => {(int)without.StatusCode}");
            }
        }

        dependsOnAnUnaskedRow.Should().BeEmpty("a row the body depends on but the caller was never asked about is an F1");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Parity (ontology-r1 condition 2): offered iff the REAL assign succeeds AND the record's list gains it
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The parity seed, deterministic (the rig's ids are a counter): every way a Procedure can be offered or not. With <paramref name="fillers"/>
    /// the candidate set is past the bound.
    /// </summary>
    private static Dictionary<string, Guid> ParitySeed(RecordProcedureRig rig, int fillers)
    {
        var seeded = new Dictionary<string, Guid>
        {
            ["P-all"] = rig.Simple("ALL").Procedure,
            ["P-assigned"] = Candidate(rig, "P-assigned"),
            ["P-firmF"] = Candidate(rig, "P-firmF", RecordProcedureRig.F),
            ["P-firmG"] = Candidate(rig, "P-firmG", G),
            ["P-later"] = Candidate(rig, "P-later", from: Mar1),
            ["P-gap"] = WithHole(rig, "P-gap"),
            ["P-draft"] = DraftOnly(rig, "P-draft"),
        };
        var appliesToFirm = rig.AddProcedure("P-appliesToFirmF", ProcedureAppliesTo.Firm, RecordProcedureRig.F);
        rig.AddVersion(appliesToFirm, 1, Jan1, null);
        seeded["P-appliesToFirmF"] = appliesToFirm;
        var retired = rig.AddProcedure("P-retired", ProcedureAppliesTo.AssignedRecordsOnly, status: ProcedureEntity.StatusRetired);
        rig.AddVersion(retired, 1, Jan1, null);
        seeded["P-retired"] = retired;
        seeded["P-otherArea"] = Candidate(rig, "P-otherArea");
        rig.World.Set(ProcedureEntity.Entity, seeded["P-otherArea"], ProcedureEntity.AreaColumn, Option(OtherArea));
        seeded["P-lapsedRow"] = Candidate(rig, "P-lapsedRow");
        seeded["P-excludedRow"] = Candidate(rig, "P-excludedRow");
        for (var n = 1; n <= fillers; n++)
        {
            seeded[$"Filler {n:D3}"] = Candidate(rig, $"Filler {n:D3}");
        }

        return seeded;
    }

    /// <summary>The parity seed's own rows on the record (fixed ids, so two rigs seeded alike hold the same rows).</summary>
    private static void ParityRows(RecordProcedureRig rig, string table, Guid record, Dictionary<string, Guid> seeded)
    {
        rig.World.Add(ProcedureAssignmentEntity.Entity, Guid.Parse("084d5a00-0000-4000-8000-0000000000b1"),
            (ProcedureAssignmentEntity.ProcedureColumn, Ref(ProcedureEntity.Entity, seeded["P-lapsedRow"])),
            (ProcedureAssignmentEntity.ModeColumn, Option(ProcedureAssignmentEntity.ModeFollowLatest)),
            ("statecode", Option(1)), (table, Ref(table, record)), ("createdon", new DateTime(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc)));
        rig.World.Add(ProcedureAssignmentEntity.Entity, Guid.Parse("084d5a00-0000-4000-8000-0000000000b2"),
            (ProcedureAssignmentEntity.ProcedureColumn, Ref(ProcedureEntity.Entity, seeded["P-excludedRow"])),
            (ProcedureAssignmentEntity.ModeColumn, Option(ProcedureAssignmentEntity.ModeExcluded)),
            ("statecode", Option(0)), (table, Ref(table, record)), ("createdon", new DateTime(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc)));
    }

    public static TheoryData<string, string, string, int> ParityMatrix()
    {
        var data = new TheoryData<string, string, string, int>();
        foreach (var table in new[] { Matter, Project, WorkAssignment })
            foreach (var firm in new[] { "F", "none" })
                foreach (var date in new[] { "2026-02-01", "2026-03-15" })
                    data.Add(table, firm, date, 0);
        data.Add(Project, "F", "2026-02-01", 51); // past the bound
        data.Add(Matter, "none", "2026-03-15", 51);
        return data;
    }

    private const string Token = "caller-token";

    [Theory(DisplayName = "084d-5a parity (ontology-r1 condition 2): a Procedure is offered iff 089's REAL Follow-latest assign succeeds AND the record's list then gains it; past the bound the list is null and each Procedure, narrowed by its own name, still agrees")]
    [MemberData(nameof(ParityMatrix))]
    public async Task Parity_OfferedIffTheRealAssignAddsIt(string table, string firmName, string day, int fillers)
    {
        var record = table == Matter ? M : table == Project ? P : W;
        Guid? firm = firmName == "F" ? RecordProcedureRig.F : null;
        var date = DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture);

        var listRig = new RecordProcedureWriteRig();
        var seeded = ParitySeed(listRig.Rig, fillers);
        ParityRows(listRig.Rig, table, record, seeded);
        listRig.Strict();
        var whole = await listRig.Rig.Service.ListAssignableAsync(listRig.Planner, table, record, firm, date, null, Token, CancellationToken.None);
        whole.Status.Should().Be(RecordProcedureReadStatus.Done);
        listRig.WriteInvocations().Should().BeEmpty("the list writes nothing");

        var probes = seeded.Keys.ToList();
        Func<string, Task<AssignableProcedureItem?>> offered;
        if (fillers > RecordProcedureReadService.AssignableBound)
        {
            whole.Value!.TooMany.Should().BeTrue();
            whole.Value.Procedures.Should().BeNull();
            probes = probes.Where(k => !k.StartsWith("Filler", StringComparison.Ordinal) || k is "Filler 001" || k == $"Filler {fillers:D3}").ToList();
            offered = async name =>
            {
                var narrowed = await listRig.Rig.Service.ListAssignableAsync(listRig.Planner, table, record, firm, date, name, Token, CancellationToken.None);
                narrowed.Status.Should().Be(RecordProcedureReadStatus.Done);
                narrowed.Value!.TooMany.Should().BeFalse($"'{name}' narrows the list within the bound");
                return narrowed.Value.Procedures!.SingleOrDefault(p => p.Id == seeded[name]);
            };
        }
        else
        {
            whole.Value!.TooMany.Should().BeFalse();
            var items = whole.Value.Procedures!.ToDictionary(p => p.Id);
            offered = name => Task.FromResult(items.GetValueOrDefault(seeded[name]));
        }

        var disagreements = new List<string>();
        var comparedFields = 0;
        var verdicts = new List<(string Name, bool Offered, bool Written, string? Code)>();
        foreach (var name in probes)
        {
            var w = new RecordProcedureWriteRig();
            var again = ParitySeed(w.Rig, fillers);
            ParityRows(w.Rig, table, record, again);
            again[name].Should().Be(seeded[name], "the seed is deterministic");

            var before = await w.Rig.Service.ListProceduresAsync(table, record, firm, date, Token, CancellationToken.None);
            before.Status.Should().Be(RecordProcedureReadStatus.Done);
            var outcome = await w.Writer.AssignProcedureAsync(table, record, new(seeded[name], "FollowLatest", null, null), RecordProcedureWriteRig.CallerObjectId, CancellationToken.None);
            var gains = false;
            ProcedureListItem? written = null;
            if (outcome.Kind == RuleSetOutcomeKind.Done)
            {
                var after = await w.Rig.Service.ListProceduresAsync(table, record, firm, date, Token, CancellationToken.None);
                after.Status.Should().Be(RecordProcedureReadStatus.Done);
                written = after.Value!.Procedures.SingleOrDefault(p => p.Id == seeded[name]);
                gains = written is not null && before.Value!.Procedures.All(p => p.Id != seeded[name]);
            }

            var item = await offered(name);
            var isOffered = item is not null;
            if (gains && isOffered)
            {
                // F4: what the list says it would be is what the list then says it is.
                if (item!.StatusIfAssigned != written!.Status || item.VersionInForce != written.VersionInForce)
                {
                    disagreements.Add($"{name}: offered as {item.StatusIfAssigned} / {item.VersionInForce}, after the write {written.Status} / {written.VersionInForce}");
                }

                comparedFields++;
            }

            verdicts.Add((name, isOffered, outcome.Kind == RuleSetOutcomeKind.Done, outcome.ErrorCode));
            if (isOffered != gains)
            {
                disagreements.Add($"{name}: offered={isOffered}, write={outcome.Kind} {outcome.ErrorCode}, list gains={gains}");
            }
        }

        disagreements.Should().BeEmpty();
        comparedFields.Should().BeGreaterThan(0, "the statusIfAssigned / versionInForce comparison ran (F4)");

        // The parity is not vacuous: in every case something is offered, the write refuses something (Retired, another area, an own row: the
        // EB-D-228 refusal among them), and the write accepts something the list does not gain (Draft only, or already on the record).
        verdicts.Should().Contain(v => v.Offered, "P-assigned is offered");
        verdicts.Where(v => !v.Written).Select(v => v.Code).Should().Contain(new[]
        {
            ProcedureAssignmentService.CodeProcedureRetired, ProcedureAssignmentService.CodeProcedureNotBilling,
        });
        verdicts.Should().Contain(v => v.Name == "P-draft" && v.Written && !v.Offered, "EB-D-228: Draft-only is accepted, and never gained");
    }
}
