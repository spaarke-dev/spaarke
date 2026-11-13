using System.Net;
using System.Net.Http.Json;
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
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;
using static Sprk.Bff.Api.Tests.Api.Finance.RecordProcedureRig;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// ebilling-r1 task 084d-2a (design notes/084d-dry-run-design.md §2-§4; uac-r2 and ontology-r1 build conditions): the dry-run routes
/// <c>POST /api/finance/{core}/{recordId}/procedures/dry-run</c>, through the REAL routes and filters, the REAL
/// <see cref="RecordProcedureReadService"/> over the in-memory world and a REAL <see cref="ProcedureAssignmentService"/> planning through it.
/// The world's entity service has no write setup, so a write fails loudly as well as being verified (<see cref="RecordProcedureRig.NoWrites"/>).
/// </summary>
public sealed class RecordProcedureDryRunContractTests
{
    private const string AssignmentSet = ProcedureAssignmentEntity.EntitySet;
    private const string Organizations = "sprk_organizations";

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
        /// <param name="privileges">The caller holds every privilege of every write (default true).</param>
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
            builder.Services.AddSingleton(Planner(host.Rig));
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

        /// <summary>The caller's Read on a subject the planned write names (the write routes' per-subject Read).</summary>
        public void CanRead(string set, Guid id) => Access.Grant(set, id, AccessRights.Read);

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    /// <summary>A REAL planner over the rig's world. Planning asks no owner, closes nothing and counts nothing (084d-1), so those are stubs that throw if reached.</summary>
    private static ProcedureAssignmentService Planner(RecordProcedureRig rig) => new(
        rig.Entities.Object, new Mock<IRecordOwnershipResolver>(MockBehavior.Strict).Object,
        (RuleSetService)RuntimeHelpers.GetUninitializedObject(typeof(RuleSetService)),
        (Sprk.Bff.Api.Services.Signals.SignalClosureService)RuntimeHelpers.GetUninitializedObject(typeof(Sprk.Bff.Api.Services.Signals.SignalClosureService)),
        (AffectedInvoiceCounter)RuntimeHelpers.GetUninitializedObject(typeof(AffectedInvoiceCounter)),
        NullLogger<ProcedureAssignmentService>.Instance);

    private static HttpRequestMessage Post(string segment, Guid record, object body, string query = "?date=2026-02-01") =>
        RateCardImportEndpointsTests.Req("POST", $"/api/finance/{segment}/{record}/procedures/dry-run{query}", true, JsonContent.Create(body));

    private static async Task<JsonObject> Json(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!.AsObject();

    private static async Task<string> ShapeOf(HttpResponseMessage r)
    {
        var body = await Json(r);
        body.Remove("traceId");
        body.Remove("correlationId");
        return $"{(int)r.StatusCode} {r.Content.Headers.ContentType} {body.ToJsonString()}";
    }

    private static async Task AssertUnavailable(HttpResponseMessage response, Host host)
    {
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        body["errorCode"]!.GetValue<string>().Should().Be(RecordProcedureReadService.UnavailableErrorCode);
        body.ContainsKey("now").Should().BeFalse("never a partial answer");
        body.ContainsKey("after").Should().BeFalse("never a partial answer");
        host.Rig.NoWrites();
    }

    private static Guid DerivedAssignmentId(string table, Guid record, Guid procedure) =>
        ProcedureAssignmentService.AssignmentIdFor(table, record, ProcedureAssignmentEntity.ProcedureColumn, procedure);

    private static Guid DerivedExcludeId(string table, Guid record, Guid policy) =>
        ProcedureAssignmentService.AssignmentIdFor(table, record, ProcedureAssignmentEntity.ExcludedPolicyColumn, policy);

    /// <summary>A FollowLatest row on a record AT the write's own derived id (so a switch re-creates the same id), created by <see cref="RecordProcedureRig.Reviewer"/>.</summary>
    private static Guid FollowLatestAtDerivedId(RecordProcedureRig rig, string table, Guid record, Guid procedure)
    {
        var id = DerivedAssignmentId(table, record, procedure);
        rig.World.Add(ProcedureAssignmentEntity.Entity, id,
            (ProcedureAssignmentEntity.ProcedureColumn, Ref(ProcedureEntity.Entity, procedure)),
            (ProcedureAssignmentEntity.ModeColumn, Option(ProcedureAssignmentEntity.ModeFollowLatest)),
            ("sprk_createdbyperson", Ref("systemuser", Reviewer)),
            ("statecode", Option(0)), (table, Ref(table, record)), ("createdon", rig.Tick()));
        return id;
    }

    private static JsonObject? ProcedureIn(JsonObject side, Guid procedure) =>
        side["procedures"]!["procedures"]!.AsArray().Select(p => p!.AsObject()).FirstOrDefault(p => p["id"]!.GetValue<Guid>() == procedure);

    private static JsonObject? RuleIn(JsonObject side, Guid policy) =>
        side["rules"]!["rules"]!.AsArray().Select(p => p!.AsObject()).FirstOrDefault(p => p["policyId"]!.GetValue<Guid>() == policy);

    private static object AssignBody(Guid procedure, string mode = "FollowLatest", Guid? pinned = null, Guid? replaces = null) =>
        new { kind = "Assign", replacesOverrideId = replaces, assign = new { procedureId = procedure, mode, pinnedVersionId = pinned } };

    private static object ExcludeBody(Guid policy, Guid? firm = null) =>
        new { kind = "ExcludeRule", excludeRule = new { policyId = policy, reason = "Agreed for this record", firmId = firm } };

    // ═══════════════════════════════════════════════════════════════════════════
    // Now -> After, the response shape, one combined check, nothing written
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "084d-2a: on every core record, assigning a Procedure is absent Now and InForce (ManualOwn) After, labelled 'If saved now', with the disclosures; ONE batched check; nothing written")]
    [InlineData("matters")]
    [InlineData("projects")]
    [InlineData("workassignments")]
    public async Task Assign_NowWithout_AfterWith_OneCheck_NothingWritten(string segment)
    {
        await using var host = await Host.StartAsync();
        var core = Cores.Single(c => c.Segment == segment);
        var s = host.Rig.Simple("ASSIGN-ME", ProcedureAppliesTo.AssignedRecordsOnly);
        host.CanRead(BillingAdminAuthorization.ProcedureEntitySet, s.Procedure);

        var response = await host.Client.SendAsync(Post(segment, core.Id, AssignBody(s.Procedure)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        var proposed = DerivedAssignmentId(core.Table, core.Id, s.Procedure);
        ProcedureIn(body["now"]!.AsObject(), s.Procedure).Should().BeNull("not assigned yet");
        var after = ProcedureIn(body["after"]!.AsObject(), s.Procedure)!;
        after["status"]!.GetValue<string>().Should().Be("InForce");
        after["level"]!.GetValue<string>().Should().Be("ManualOwn");
        RuleIn(body["after"]!.AsObject(), s.Rule).Should().NotBeNull("its rule runs After");
        RuleIn(body["now"]!.AsObject(), s.Rule).Should().BeNull();

        var proposal = body["proposal"]!.AsObject();
        proposal["kind"]!.GetValue<string>().Should().Be("Assign");
        proposal["proposedRowIds"]!.AsArray().Select(x => x!.GetValue<Guid>()).Should().Equal(proposed);
        body["afterLabel"]!.GetValue<string>().Should().Be("If saved now");
        var disclosure = body["disclosure"]!.AsObject();
        disclosure["predictsOwnerRefusal"]!.GetValue<bool>().Should().BeFalse("R5");
        disclosure["readAtOneInstant"]!.GetValue<bool>().Should().BeFalse("R10");
        disclosure["requestFirmAsked"]!.GetValue<bool>().Should().BeFalse("no firm in the request");
        disclosure["notes"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal(
            RecordProcedureReadService.NotAPromiseNote, RecordProcedureReadService.OwnerNote, RecordProcedureReadService.SeparateReadsNote);
        body.Select(kv => kv.Key).Should().BeEquivalentTo(
            new[] { "record", "date", "firm", "firmSource", "proposal", "now", "after", "afterLabel", "disclosure" },
            "2a's shape: closes, affectedOpenInvoices and reach are 084d-2b's, left out (not stubbed)");
        var withoutDisclosure = body.DeepClone().AsObject();
        withoutDisclosure.Remove("disclosure");
        withoutDisclosure.ToJsonString().Should().NotContainAny(new[] { "succeed", "Succeed" }, "nothing but the disclaimer speaks of success; After is never presented as a promise");

        host.Rig.Probe.Batches.Should().ContainSingle("ONE batched check over Now and After");
        host.Rig.Probe.Asked.Should().NotContain((AssignmentSet, proposed), "the synthetic row cannot be asked; it is exempt");
        host.Rig.Probe.Asked.Should().Contain((core.Set, core.Id));
        host.Exempted.Should().ContainSingle().Which.Should().BeEquivalentTo(new[] { (AssignmentSet, proposed) }, "exactly the synthetic row");
        host.Rig.NoWrites();
    }

    [Fact(DisplayName = "084d-2a (EB-D-218): a rule Exclude exempts exactly its one synthetic row; every other row After shows (the rule, its version, set and Procedure) is asked")]
    public async Task ExcludeRule_ExemptsExactlyTheSyntheticRow()
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.Simple("GENERAL");
        host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, s.Rule);

        var response = await host.Client.SendAsync(Post("projects", P, ExcludeBody(s.Rule)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        var proposed = DerivedExcludeId(Project, P, s.Rule);
        RuleIn(body["now"]!.AsObject(), s.Rule)!["inForce"]!.GetValue<bool>().Should().BeTrue();
        var after = RuleIn(body["after"]!.AsObject(), s.Rule)!;
        after["inForce"]!.GetValue<bool>().Should().BeFalse();
        var mark = after["marks"]!.AsArray().Single()!.AsObject();
        mark["kind"]!.GetValue<string>().Should().Be("Excluded");
        mark["overrideRowId"]!.GetValue<Guid>().Should().Be(proposed);
        mark["stamp"]!["byId"].Should().BeNull("a proposed row has no creator (F3)");
        mark["stamp"]!["on"]!.GetValue<DateTime>().Should().Be(new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc), "the request's one stamp");

        host.Exempted.Should().ContainSingle().Which.Should().BeEquivalentTo(new[] { (AssignmentSet, proposed) });
        var asked = host.Rig.Probe.Asked.ToList();
        asked.Should().NotContain((AssignmentSet, proposed));
        asked.Should().Contain(new[] { ("sprk_policies", s.Rule), ("sprk_policyversions", s.RuleVersion), (ProcedureEntity.EntitySet, s.Procedure) });
        host.Rig.NoWrites();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // uac-r2 condition 1: the synthetic by-id load faults -> 503, nothing exempted
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "084d-2a uac-r2 condition 1 (must-fire): the app-only by-id load that decides 'synthetic' faults -> 503 procedures.unavailable, Exempt is never called and nothing is asked")]
    public async Task SyntheticLoadFault_Is503_AndNothingIsExempted()
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.Simple("ASSIGN-ME", ProcedureAppliesTo.AssignedRecordsOnly);
        host.CanRead(BillingAdminAuthorization.ProcedureEntitySet, s.Procedure);
        var proposed = DerivedAssignmentId(Project, P, s.Procedure);

        // ONLY the load that decides "synthetic" (an In on the primary key asking for the key column alone) faults; every later read of the
        // same id (the creators' stamp read) still answers. So a dry run that swallowed this fault and exempted the row would reach 200.
        host.Rig.Entities
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == ProcedureAssignmentEntity.Entity
                    && q.ColumnSet.Columns.Count == 1 && q.ColumnSet.Columns[0] == ProcedureAssignmentEntity.Entity + "id"
                    && q.Criteria.Conditions.Any(c => c.AttributeName == ProcedureAssignmentEntity.Entity + "id" && c.Values.Contains(proposed))),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("by-id load throttled"));

        var response = await host.Client.SendAsync(Post("projects", P, AssignBody(s.Procedure)));

        await AssertUnavailable(response, host);
        host.Exempted.Should().BeEmpty("a fault never reads as 'found nothing'");
        host.Rig.Probe.Batches.Should().BeEmpty("no answer is built, so nothing is asked");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // uac-r2 condition 2 and ontology-r1 F3: a switch re-creates a REAL id
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "084d-2a uac-r2 condition 2 + ontology-r1 F3: a switch (Follow -> Pin) re-creates the SAME derived id, which is real: it is asked, never exempt, After shows no creator on it (Now shows the old one)")]
    public async Task Switch_TheRecreatedIdIsReal_Asked_AndHasNoCreatorAfter()
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.Simple("SWITCH", ProcedureAppliesTo.AssignedRecordsOnly);
        var x = FollowLatestAtDerivedId(host.Rig, Project, P, s.Procedure);
        host.CanRead(BillingAdminAuthorization.ProcedureEntitySet, s.Procedure);

        var response = await host.Client.SendAsync(Post("projects", P, AssignBody(s.Procedure, "Pinned", s.Version, replaces: x)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        body["proposal"]!["proposedRowIds"]!.AsArray().Select(v => v!.GetValue<Guid>()).Should().Equal(x);
        body["proposal"]!["replacesOverrideId"]!.GetValue<Guid>().Should().Be(x);
        var nowRow = ProcedureIn(body["now"]!.AsObject(), s.Procedure)!["overrides"]!.AsArray().Single()!.AsObject();
        var afterRow = ProcedureIn(body["after"]!.AsObject(), s.Procedure)!["overrides"]!.AsArray().Single()!.AsObject();
        nowRow["mode"]!.GetValue<string>().Should().Be("FollowLatest");
        nowRow["stamp"]!["byId"]!.GetValue<Guid>().Should().Be(Reviewer);
        afterRow["rowId"]!.GetValue<Guid>().Should().Be(x);
        afterRow["mode"]!.GetValue<string>().Should().Be("Pinned");
        afterRow["stamp"]!["byId"].Should().BeNull("F3: the replaced row's old creator is not the proposed row's");
        afterRow["stamp"]!["byName"].Should().BeNull();

        host.Exempted.Should().BeEmpty("the created id exists already, so it is real and nothing is exempt");
        host.Rig.Probe.Asked.Should().Contain((AssignmentSet, x));
        host.Rig.NoWrites();

        // Deny that row alone: 503.
        host.Rig.Probe.Denied.Add((AssignmentSet, x));
        await AssertUnavailable(await host.Client.SendAsync(Post("projects", P, AssignBody(s.Procedure, "Pinned", s.Version, replaces: x))), host);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // R6: a firm the request names is asked as a query firm
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "084d-2a R6: a firm named in the request is asked as a query firm: readable is 200 and asked; unreadable is 503 (never a name for a firm the caller cannot read); not a firm is the write's own 400")]
    [InlineData("readable")]
    [InlineData("denied")]
    [InlineData("missing")]
    public async Task RequestFirm_IsAskedAsAQueryFirm(string which)
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.Simple("GENERAL");
        host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, s.Rule);
        var firm = which == "missing" ? Guid.Parse("084d2a00-0000-4000-8000-0000000000f9") : G;
        if (which == "denied")
        {
            host.Rig.Probe.Denied.Add((Organizations, G));
        }
        else if (which == "missing")
        {
            host.Rig.Probe.Unknown.Add((Organizations, firm));
        }

        var response = await host.Client.SendAsync(Post("projects", P, ExcludeBody(s.Rule, firm)));

        if (which == "readable")
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            (await Json(response))["disclosure"]!["requestFirmAsked"]!.GetValue<bool>().Should().BeTrue();
            host.Rig.Probe.Asked.Should().Contain((Organizations, G));
        }
        else if (which == "missing")
        {
            // Not a firm at all: planning refuses exactly as the write does (the write route's own 400 for the same body), before any check.
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
            (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("assignment.invalid");
            host.Rig.Probe.Batches.Should().BeEmpty();
            host.Rig.NoWrites();
        }
        else
        {
            await AssertUnavailable(response, host);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Planning refusals are the write's own
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "084d-2a: a planning refusal is the write's own answer: excluding a rule the record already excludes is 409 assignment.duplicate; nothing is asked or written")]
    public async Task PlanningRefusal_IsTheWritesOwn()
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.Simple("GENERAL");
        host.Rig.ExcludeRule(Project, P, s.Rule, reason: "already");
        host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, s.Rule);

        var response = await host.Client.SendAsync(Post("projects", P, ExcludeBody(s.Rule)));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(ProcedureAssignmentService.CodeDuplicate);
        host.Rig.Probe.Batches.Should().BeEmpty();
        host.Rig.NoWrites();
    }

    [Fact(DisplayName = "084d-2a: removing an override is listed Now and gone After (the removed row is asked: Now shows it)")]
    public async Task Remove_NowWith_AfterWithout()
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.Simple("GENERAL");
        var row = host.Rig.ExcludeRule(Project, P, s.Rule, reason: "waived", by: Reviewer);
        host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, s.Rule);

        var response = await host.Client.SendAsync(Post("projects", P, new { kind = "Remove", overrideId = row }));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        RuleIn(body["now"]!.AsObject(), s.Rule)!["inForce"]!.GetValue<bool>().Should().BeFalse();
        RuleIn(body["after"]!.AsObject(), s.Rule)!["inForce"]!.GetValue<bool>().Should().BeTrue();
        body["proposal"]!["overrideId"]!.GetValue<Guid>().Should().Be(row);
        body["proposal"]!["proposedRowIds"]!.AsArray().Should().BeEmpty();
        host.Exempted.Should().BeEmpty();
        host.Rig.Probe.Asked.Should().Contain((AssignmentSet, row));
        host.Rig.NoWrites();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Filter order (uac-r2 C5): step 0, the uniform 404 BEFORE any 403, the kill switch, the removal's subject Read, 401
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "084d-2a must-fire (C5): a record the caller may Read but not write, or not Read at all, is the uniform 404, byte-identical to an unknown record, BEFORE any privilege is asked (even with no privilege held)")]
    [InlineData("read-only", true)]
    [InlineData("read-only", false)]
    [InlineData("none", false)]
    public async Task RecordFilter_IsTheUniform404_BeforeAny403(string rights, bool privileges)
    {
        var s = new RecordProcedureRig().Simple("ASSIGN-ME", ProcedureAppliesTo.AssignedRecordsOnly);
        await using var host = await Host.StartAsync(rights == "none" ? AccessRights.None : AccessRights.Read, privileges);
        host.Rig.Simple("ASSIGN-ME", ProcedureAppliesTo.AssignedRecordsOnly);
        var response = await host.Client.SendAsync(Post("projects", P, AssignBody(s.Procedure)));

        await using var unknown = await Host.StartAsync();
        var unknownResponse = await unknown.Client.SendAsync(Post("projects", Guid.Parse("084d2a00-0000-4000-8000-00000000dead"), AssignBody(s.Procedure)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ShapeOf(response)).Should().Be(await ShapeOf(unknownResponse), "no existence oracle");
        host.Rig.Probe.PrivilegeCalls.Should().BeEmpty("the record filter refuses before the 403 filter asks anything");
        host.Rig.World.QueriedTables.Should().BeEmpty();
    }

    [Theory(DisplayName = "084d-2a must-fire (C5): with the record allowed, a missing privilege or an unreadable subject is 403; nothing is read")]
    [InlineData("privilege")]
    [InlineData("subject")]
    public async Task SubjectAndPrivilegeFilter_Is403(string missing)
    {
        await using var host = await Host.StartAsync(privileges: missing != "privilege");
        var s = host.Rig.Simple("ASSIGN-ME", ProcedureAppliesTo.AssignedRecordsOnly);
        if (missing != "subject")
        {
            host.CanRead(BillingAdminAuthorization.ProcedureEntitySet, s.Procedure);
        }

        var response = await host.Client.SendAsync(Post("projects", P, AssignBody(s.Procedure)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        host.Rig.World.QueriedTables.Should().BeEmpty();
    }

    [Theory(DisplayName = "084d-2a: a body without a kind, or without the id its kind needs, is the write's 400, before any Dataverse call")]
    [InlineData("""{"kind":null}""")]
    [InlineData("""{"kind":"Assign"}""")]
    [InlineData("""{"kind":"Remove"}""")]
    [InlineData("""{"kind":"Remove","overrideId":"084d2a00-0000-4000-8000-000000000001","replacesOverrideId":"084d2a00-0000-4000-8000-000000000002"}""")]
    public async Task MissingIds_Are400(string json)
    {
        await using var host = await Host.StartAsync();
        var request = RateCardImportEndpointsTests.Req("POST", $"/api/finance/projects/{P}/procedures/dry-run", true,
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        var response = await host.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        host.Rig.World.QueriedTables.Should().BeEmpty();
    }

    [Fact(DisplayName = "084d-2a: eBilling off is 503 ebilling.disabled after the record and privilege filters, before the service reads anything")]
    public async Task FlagOff_Is503Disabled()
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        var procedure = Guid.Parse("084d2a00-0000-4000-8000-0000000000a1");
        host.CanRead(BillingAdminAuthorization.ProcedureEntitySet, procedure);

        var response = await host.Client.SendAsync(Post("projects", P, AssignBody(procedure)));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.Rig.Probe.PrivilegeCalls.Should().NotBeEmpty("the privilege filter ran before the kill switch");
        host.Rig.World.QueriedTables.Should().BeEmpty();
    }

    [Theory(DisplayName = "084d-2a must-fire: removing, or replacing, an override whose subject the caller cannot Read is the write routes' 404 assignment.override_not_found (the shared helper)")]
    [InlineData("remove")]
    [InlineData("replace")]
    public async Task UnreadableOverrideSubject_IsTheWritesNotFound(string how)
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.Simple("SWITCH", ProcedureAppliesTo.AssignedRecordsOnly);
        var x = FollowLatestAtDerivedId(host.Rig, Project, P, s.Procedure);
        object body = how == "remove" ? new { kind = "Remove", overrideId = x } : AssignBody(s.Procedure, "Pinned", s.Version, replaces: x);
        if (how == "replace")
        {
            host.CanRead(BillingAdminAuthorization.ProcedureEntitySet, s.Procedure);
            host.Access.Grant(BillingAdminAuthorization.ProcedureEntitySet, s.Procedure, AccessRights.Read);
        }

        // The subject Read of the stored row: deny it (the 403 filter's own subject Read for "replace" is the same Procedure, so deny it only after
        // that filter: here by making the stored row's subject a different, unreadable Procedure).
        var other = host.Rig.Simple("OTHER", ProcedureAppliesTo.AssignedRecordsOnly);
        host.Rig.World.Remove(ProcedureAssignmentEntity.Entity, x);
        host.Rig.World.Add(ProcedureAssignmentEntity.Entity, x,
            (ProcedureAssignmentEntity.ProcedureColumn, Ref(ProcedureEntity.Entity, other.Procedure)),
            (ProcedureAssignmentEntity.ModeColumn, Option(ProcedureAssignmentEntity.ModeFollowLatest)),
            ("statecode", Option(0)), (Project, Ref(Project, P)));

        var response = await host.Client.SendAsync(Post("projects", P, body));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(ProcedureAssignmentService.CodeOverrideNotFound);
        host.Rig.Probe.Batches.Should().BeEmpty();
        host.Rig.NoWrites();
    }

    [Theory(DisplayName = "084d-2a must-fire: a flagged-external, row-less or flag-fault caller is refused at step 0 on every dry-run route, before any check or read")]
    [InlineData("matters")]
    [InlineData("projects")]
    [InlineData("workassignments")]
    public async Task StepZero_RefusesFirst(string segment)
    {
        foreach (var kind in StatedCaller.RefusedKinds)
        {
            await using var host = await Host.StartAsync();
            host.StepZero.Kind = kind;

            var response = await host.Client.SendAsync(Post(segment, Cores.Single(c => c.Segment == segment).Id, AssignBody(Guid.NewGuid())));

            await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: true);
            host.Access.Calls.Should().BeEmpty($"{segment} {kind}");
            host.Rig.World.QueriedTables.Should().BeEmpty();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // EB-D-218 guards over the dry-run routes
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed record DryRunSeed(string Segment, Guid RecordId, object Body, Guid[] AlsoAsked);

    public static TheoryData<string> DryRunSeeds => new() { "assign", "exclude with firm", "switch", "remove" };

    private static DryRunSeed SeedFor(string name, Host host)
    {
        var rig = host.Rig;
        switch (name)
        {
            case "assign":
                {
                    rig.Simple("GENERAL");
                    var s = rig.Simple("ASSIGN-ME", ProcedureAppliesTo.AssignedRecordsOnly);
                    host.CanRead(BillingAdminAuthorization.ProcedureEntitySet, s.Procedure);
                    return new DryRunSeed("projects", P, AssignBody(s.Procedure), []);
                }

            case "exclude with firm":
                {
                    var s = rig.Simple("GENERAL");
                    host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, s.Rule);
                    return new DryRunSeed("projects", P, ExcludeBody(s.Rule, G), []);
                }

            case "switch":
                {
                    var s = rig.Simple("SWITCH", ProcedureAppliesTo.AssignedRecordsOnly);
                    var x = FollowLatestAtDerivedId(rig, Project, P, s.Procedure);
                    host.CanRead(BillingAdminAuthorization.ProcedureEntitySet, s.Procedure);
                    return new DryRunSeed("projects", P, AssignBody(s.Procedure, "Pinned", s.Version, replaces: x), [x]);
                }

            case "remove":
                {
                    var s = rig.Simple("GENERAL");
                    var row = rig.ExcludeRule(Matter, M, s.Rule, reason: "waived on the parent", by: Reviewer);
                    host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, s.Rule);
                    return new DryRunSeed("matters", M, new { kind = "Remove", overrideId = row }, [row]);
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

    /// <summary>The guard's verdict: GUIDs in the body that were neither asked nor exempt (the exempt ones must be exactly the synthetic rows).</summary>
    private static List<Guid> Unasked(IEnumerable<Guid> guids, ISet<Guid> asked, ISet<Guid> exempt) =>
        guids.Where(g => !asked.Contains(g) && !exempt.Contains(g)).ToList();

    [Theory(DisplayName = "084d-2a guard (Every200Guid_WasAskedOfTheCaller, dry run): every GUID in a dry-run 200 was asked of the caller, except exactly the synthetic proposed rows (exempt, never asked); a real proposed id is asked")]
    [MemberData(nameof(DryRunSeeds))]
    public async Task Every200Guid_WasAskedOfTheCaller_DryRun(string seedName)
    {
        await using var host = await Host.StartAsync();
        var seed = SeedFor(seedName, host);

        var response = await host.Client.SendAsync(Post(seed.Segment, seed.RecordId, seed.Body));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var guids = GuidsIn(await Json(response)).Distinct().ToList();
        var asked = host.Rig.Probe.Asked.Select(a => a.RecordId).ToHashSet();
        var exempt = host.Exempted.SelectMany(e => e).Select(e => e.Id).ToHashSet();
        guids.Should().Contain(seed.RecordId);
        guids.Count.Should().BeGreaterThanOrEqualTo(3, "the seed is rich enough to be a guard");
        Unasked(guids, asked, exempt).Should().BeEmpty($"{seedName}: a GUID in the answer the caller's rights were never checked against");
        exempt.Where(asked.Contains).Should().BeEmpty("an exempt row is never asked");
        seed.AlsoAsked.Where(g => !asked.Contains(g)).Should().BeEmpty($"{seedName}: a row that decided what the answer shows, never checked");

        // Must-fire control: the verdict is not vacuous; a GUID planted in the walk that nobody asked is reported.
        var planted = Guid.Parse("084d2a00-0000-4000-8000-00000000beef");
        Unasked(guids.Append(planted), asked, exempt).Should().Equal(planted);
    }

    [Theory(DisplayName = "084d-2a guard (version rows, dry run): every Procedure Version and policy version row of a seed is asked on the dry-run route, or the 200 body is byte-identical without it")]
    [MemberData(nameof(DryRunSeeds))]
    public async Task EveryVersionRow_IsAsked_OrTheBodyDoesNotDependOnIt_DryRun(string seedName)
    {
        await using var baseline = await Host.StartAsync();
        var seed = SeedFor(seedName, baseline);
        var expectedResponse = await baseline.Client.SendAsync(Post(seed.Segment, seed.RecordId, seed.Body));
        expectedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var expected = await expectedResponse.Content.ReadAsStringAsync();
        var asked = baseline.Rig.Probe.Asked.Select(a => a.RecordId).ToHashSet();
        var rows = baseline.Rig.VersionRows.ToList();
        rows.Should().NotBeEmpty();

        var dependsOnAnUnaskedRow = new List<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            var (set, id, label) = rows[i];
            baseline.Rig.Probe.Denied.Clear();
            baseline.Rig.Probe.Denied.Add((set, id));
            var denied = await baseline.Client.SendAsync(Post(seed.Segment, seed.RecordId, seed.Body));
            if (asked.Contains(id))
            {
                denied.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, $"{label} is asked, so denying it refuses");
                continue;
            }

            await using var perturbed = await Host.StartAsync();
            var again = SeedFor(seedName, perturbed);
            perturbed.Rig.VersionRows[i].Id.Should().Be(id, "the seed is deterministic");
            perturbed.Rig.World.Remove(set == ProcedureEntity.VersionEntitySet ? ProcedureEntity.VersionEntity : PolicyVersion, id);
            var without = await perturbed.Client.SendAsync(Post(again.Segment, again.RecordId, again.Body));
            if (without.StatusCode != HttpStatusCode.OK || await without.Content.ReadAsStringAsync() != expected)
            {
                dependsOnAnUnaskedRow.Add($"{label} => {(int)without.StatusCode}");
            }
        }

        dependsOnAnUnaskedRow.Should().BeEmpty("a row the dry run's body depends on but the caller was never asked about is an F1");
    }
}
