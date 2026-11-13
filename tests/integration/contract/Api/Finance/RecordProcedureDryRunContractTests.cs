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
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
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

        /// <summary>084d-2b: the open billing findings per policy that the closure's two SELECT methods return (the only members set up).</summary>
        public readonly Dictionary<Guid, List<OpenPolicySignal>> Findings = new();

        /// <summary>084d-2b: policies whose selection faults.</summary>
        public readonly HashSet<Guid> SelectionFaults = new();

        /// <summary>084d-2b: a STRICT closure mock: only the selections are set up, so any close throws.</summary>
        public Mock<SignalClosureService> Closure = null!;

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
            builder.Services.AddSingleton(Planner(host));
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

    /// <summary>
    /// A REAL planner over the rig's world, with the rig's REAL rule-set service (a change is validated by it: 084d-2a fix round 1, F-1). Planning
    /// asks no owner (a strict resolver). 084d-2b: the count is the REAL <see cref="AffectedInvoiceCounter"/> over the rig's world and probe; the
    /// closure is a STRICT mock with only its two selections set up (from <see cref="Host.Findings"/>), so a close would throw.
    /// </summary>
    private static ProcedureAssignmentService Planner(Host host)
    {
        var rig = host.Rig;
        var writer = (SignalWriter)RuntimeHelpers.GetUninitializedObject(typeof(SignalWriter));
        host.Closure = new Mock<SignalClosureService>(MockBehavior.Strict, writer, rig.Entities.Object, TimeProvider.System, NullLogger<SignalClosureService>.Instance);
        host.Closure.Setup(c => c.SelectOpenSignalsOfPolicyAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns((Guid policy, bool _, CancellationToken _) => host.SelectionFaults.Contains(policy)
                ? throw new InvalidOperationException("simulated selection fault")
                : Task.FromResult<IReadOnlyList<OpenPolicySignal>>(host.Findings.GetValueOrDefault(policy, []).ToList()));
        host.Closure.Setup(c => c.SelectOpenSignalsOfPolicyOnSubjectsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .Returns((Guid policy, IReadOnlyCollection<Guid> invoices, IReadOnlyCollection<Guid> _, CancellationToken _) => host.SelectionFaults.Contains(policy)
                ? throw new InvalidOperationException("simulated selection fault")
                : Task.FromResult<IReadOnlyList<OpenPolicySignal>>(
                    host.Findings.GetValueOrDefault(policy, []).Where(s => invoices.Contains(s.RegardingInvoiceId!.Value)).ToList()));
        return new ProcedureAssignmentService(
            rig.Entities.Object, new Mock<IRecordOwnershipResolver>(MockBehavior.Strict).Object,
            rig.RuleSets,
            host.Closure.Object,
            new AffectedInvoiceCounter(rig.Entities.Object, rig.Probe, NullLogger<AffectedInvoiceCounter>.Instance),
            NullLogger<ProcedureAssignmentService>.Instance);
    }

    /// <summary>084d-2b: an invoice in the world: filed on <paramref name="table"/>(<paramref name="record"/>), of <paramref name="firm"/>, In Review unless stated.</summary>
    private static Guid Invoice(Host host, string table, Guid record, Guid firm, DateOnly dated, bool inReview = true)
    {
        var id = Guid.NewGuid();
        var column = InvoiceFamilyCoreRecordGuard.CoreRecordLookups["sprk_invoice"].Single(l => l.CoreEntity == table).Column;
        host.Rig.World.Add("sprk_invoice", id,
            (column, Ref(table, record)), ("sprk_vendororg", Ref("sprk_organization", firm)),
            (InvoiceAdjustmentService.InvoiceDateColumn, dated.ToDateTime(TimeOnly.MinValue)),
            ("statecode", Option(InvoiceStatusCodes.StateActive)),
            ("statuscode", Option(inReview ? InvoiceStatusCodes.InReview : InvoiceStatusCodes.InReview + 1)));
        return id;
    }

    /// <summary>084d-2b: an open billing finding of <paramref name="policy"/> on <paramref name="invoice"/>, returned by the closure's selections.</summary>
    private static void Finding(Host host, Guid policy, Guid invoice)
    {
        var signal = new ExistingSignal(Guid.NewGuid(), null, "sprk_invoice", invoice, 1, null, null, null, null, null, null, null, null);
        if (!host.Findings.TryGetValue(policy, out var list)) host.Findings[policy] = list = new();
        list.Add(new OpenPolicySignal(signal, invoice, null));
    }

    private const string InvoiceSet = "sprk_invoices";

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

    /// <summary>A change of a rule's message, headline and consequence (the base version has no template, so the change names one).</summary>
    private static object ChangeBody(Guid policy, Guid? replaces = null) =>
        new
        {
            kind = "ChangeRule",
            replacesOverrideId = replaces,
            changeRule = new
            {
                policyId = policy,
                reason = "Agreed for this record",
                shortHeadline = "Rate agreed for this record",
                messageTemplate = "The billed rate differs from the rate agreed for this record.",
                consequence = "Flag",
            },
        };

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
            new[] { "record", "date", "firm", "firmSource", "proposal", "now", "after", "afterLabel", "disclosure", "closes", "affectedOpenInvoices" },
            "2b's shape: closes and affectedOpenInvoices added; reach is not built (left out, not stubbed)");
        body["closes"]!.AsObject().Select(kv => kv.Key).Should().BeEquivalentTo(new[] { "d170", "kinds", "warning", "message" });
        body["closes"]!["d170"]!.GetValue<bool>().Should().BeFalse("an assignment closes nothing");
        body["closes"]!["kinds"]!.AsArray().Should().BeEmpty();
        body["affectedOpenInvoices"]!.AsObject().Select(kv => kv.Key).Should().BeEquivalentTo(new[] { "count", "text", "basis" });
        body["affectedOpenInvoices"]!["count"]!.GetValue<int>().Should().Be(0, "no invoice is filed in the reach");
        body["affectedOpenInvoices"]!["basis"]!.GetValue<string>().Should().Be("ResolutionChanged");
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
    // 084d-2b: closes and affectedOpenInvoices
    // ═══════════════════════════════════════════════════════════════════════════

    private static JsonObject Closes(JsonObject body) => body["closes"]!.AsObject();

    private static JsonObject Affected(JsonObject body) => body["affectedOpenInvoices"]!.AsObject();

    private static int? CountOf(JsonObject body) => Affected(body)["count"]?.GetValue<int>();

    [Fact(DisplayName = "084d-2b: an assignment closes nothing; it counts the In Review invoices the caller can Read, on the record and directly under it, whose resolution changes; not a decided one (a child's own nearer row), not an unreadable one; the rows compared are asked in the ONE check; nothing selected or closed")]
    public async Task Assign_ClosesNothing_CountsTheVisibleInvoicesWhoseResolutionChanges()
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.Simple("ASSIGN-ME", ProcedureAppliesTo.AssignedRecordsOnly);
        host.CanRead(BillingAdminAuthorization.ProcedureEntitySet, s.Procedure);
        Invoice(host, Project, P, F, Feb1);                              // the record's own: changes
        Invoice(host, WorkAssignment, W, F, Feb1);                       // directly under it (W's chain reaches P): changes
        var unreadable = Invoice(host, WorkAssignment, W, G, Feb1);      // changes, but the caller cannot Read it
        host.Rig.Probe.Denied.Add((InvoiceSet, unreadable));

        // F-3: a child that decides the Procedure itself (its own nearer row), so the record's assignment changes nothing for it: not counted.
        var decided = Guid.Parse("08400000-0000-4000-8000-0000000000c2");
        host.Rig.World.Add(WorkAssignment, decided, ("sprk_name", "Work decided"), ("sprk_regardingproject", Ref(Project, P)));
        host.Rig.Assign(WorkAssignment, decided, s.Procedure);
        Invoice(host, WorkAssignment, decided, F, Feb1);

        var response = await host.Client.SendAsync(Post("projects", P, AssignBody(s.Procedure)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        Closes(body)["d170"]!.GetValue<bool>().Should().BeFalse();
        Closes(body)["kinds"]!.AsArray().Should().BeEmpty();
        Closes(body)["warning"].Should().BeNull();
        Closes(body)["message"].Should().BeNull();
        CountOf(body).Should().Be(2, "the record's and the child's In Review invoices the caller can Read; not the decided child's (its own row wins), not the unreadable one");
        Affected(body)["text"]!.GetValue<string>().Should().Be(ProcedureService.AffectedText(2));
        Affected(body)["basis"]!.GetValue<string>().Should().Be(RecordProcedureReadService.AffectedBasisResolutionChanged);

        var check = host.Rig.Probe.Batches.Last();
        check.Should().Contain(("sprk_workassignments", W), "the child's resolution was compared, so its chain record is asked in the ONE check");
        check.Should().NotContain(a => a.EntitySet == InvoiceSet, "the invoices are the counter's own question, asked before anything is judged");
        host.Rig.Probe.Batches.Count.Should().Be(2, "the counter's invoice question, then the ONE combined check");
        host.Closure.Verify(c => c.SelectOpenSignalsOfPolicyAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        host.Closure.VerifyNoOtherCalls();
        host.Rig.NoWrites();

        // Must-fire (EB-D-218: a row the count read is asked): denying the child record alone is 503.
        host.Rig.Probe.Denied.Add(("sprk_workassignments", W));
        await AssertUnavailable(await host.Client.SendAsync(Post("projects", P, AssignBody(s.Procedure))), host);
    }

    [Fact(DisplayName = "084d-2b (bounds): more resolutions to compare than the bound, or an invoice the counter cannot answer for, is a null count, never a guess; the 200 still stands")]
    public async Task ResolutionCount_PastTheBound_OrUnanswered_IsNull()
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.Simple("ASSIGN-ME", ProcedureAppliesTo.AssignedRecordsOnly);
        host.CanRead(BillingAdminAuthorization.ProcedureEntitySet, s.Procedure);
        for (var i = 0; i <= RecordProcedureReadService.MaxComparedResolutions; i++)
        {
            var firm = Guid.NewGuid();
            host.Rig.World.Add("sprk_organization", firm, ("sprk_organizationname", $"Firm {i}"));
            Invoice(host, Project, P, firm, Feb1);
        }

        var past = await Json(await host.Client.SendAsync(Post("projects", P, AssignBody(s.Procedure))));
        CountOf(past).Should().BeNull("one resolution per firm, past the bound");
        Affected(past)["text"].Should().BeNull();

        await using var host2 = await Host.StartAsync();
        var s2 = host2.Rig.Simple("ASSIGN-ME", ProcedureAppliesTo.AssignedRecordsOnly);
        host2.CanRead(BillingAdminAuthorization.ProcedureEntitySet, s2.Procedure);
        host2.Rig.Probe.Unknown.Add((InvoiceSet, Invoice(host2, Project, P, F, Feb1)));
        var unanswered = await host2.Client.SendAsync(Post("projects", P, AssignBody(s2.Procedure)));
        unanswered.StatusCode.Should().Be(HttpStatusCode.OK);
        CountOf(await Json(unanswered)).Should().BeNull("an unanswered rights question is no count");

        // F-3 must-not-fire: exactly AT the bound is a number (a > vs >= slip would null it).
        await using var host3 = await Host.StartAsync();
        var s3 = host3.Rig.Simple("ASSIGN-ME", ProcedureAppliesTo.AssignedRecordsOnly);
        host3.CanRead(BillingAdminAuthorization.ProcedureEntitySet, s3.Procedure);
        for (var i = 0; i < RecordProcedureReadService.MaxComparedResolutions; i++)
        {
            var firm = Guid.NewGuid();
            host3.Rig.World.Add("sprk_organization", firm, ("sprk_organizationname", $"Firm {i}"));
            Invoice(host3, Project, P, firm, Feb1);
        }

        var atBound = await host3.Client.SendAsync(Post("projects", P, AssignBody(s3.Procedure)));
        atBound.StatusCode.Should().Be(HttpStatusCode.OK, await atBound.Content.ReadAsStringAsync());
        CountOf(await Json(atBound)).Should().Be(RecordProcedureReadService.MaxComparedResolutions, "exactly the bound's worth of resolutions is compared");
    }

    private const int SizeOfTheInvoiceBound = AffectedInvoiceCounter.MaxDryRunInvoices;

    [Fact(DisplayName = "084d-2b fix round 1 (K-5): more In Review invoices in reach than the rights bound is a null count and the caller's rights on invoices are never asked; exactly at the bound is a number; the same bound on the findings-closed count")]
    public async Task InvoiceCount_PastTheRightsBound_IsNull_AndAtTheBoundANumber()
    {
        await using var past = await Host.StartAsync();
        var s = past.Rig.Simple("ASSIGN-ME", ProcedureAppliesTo.AssignedRecordsOnly);
        past.CanRead(BillingAdminAuthorization.ProcedureEntitySet, s.Procedure);
        for (var i = 0; i <= SizeOfTheInvoiceBound; i++) Invoice(past, Project, P, F, Feb1);

        var pastResponse = await past.Client.SendAsync(Post("projects", P, AssignBody(s.Procedure)));

        pastResponse.StatusCode.Should().Be(HttpStatusCode.OK, await pastResponse.Content.ReadAsStringAsync());
        CountOf(await Json(pastResponse)).Should().BeNull("past the bound the count is null, never a partial one");
        past.Rig.Probe.Asked.Should().NotContain(a => a.EntitySet == InvoiceSet, "the rights of the caller on the invoices are not asked past the bound");

        await using var at = await Host.StartAsync();
        var s2 = at.Rig.Simple("ASSIGN-ME", ProcedureAppliesTo.AssignedRecordsOnly);
        at.CanRead(BillingAdminAuthorization.ProcedureEntitySet, s2.Procedure);
        for (var i = 0; i < SizeOfTheInvoiceBound; i++) Invoice(at, Project, P, F, Feb1);

        var atResponse = await at.Client.SendAsync(Post("projects", P, AssignBody(s2.Procedure)));

        atResponse.StatusCode.Should().Be(HttpStatusCode.OK, await atResponse.Content.ReadAsStringAsync());
        CountOf(await Json(atResponse)).Should().Be(SizeOfTheInvoiceBound, "exactly at the bound is a number");
        at.Rig.Probe.Asked.Count(a => a.EntitySet == InvoiceSet).Should().Be(SizeOfTheInvoiceBound);

        // The findings-closed count passes the same bound.
        await using var change = await Host.StartAsync();
        var c = change.Rig.Changeable("CHANGE-ME");
        change.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, c.Rule);
        for (var i = 0; i <= SizeOfTheInvoiceBound; i++) Finding(change, c.Rule, Invoice(change, Project, P, F, new DateOnly(2026, 2, 12)));

        var changeResponse = await change.Client.SendAsync(Post("projects", P, ChangeBody(c.Rule)));

        changeResponse.StatusCode.Should().Be(HttpStatusCode.OK, await changeResponse.Content.ReadAsStringAsync());
        var changeBody = await Json(changeResponse);
        Affected(changeBody)["basis"]!.GetValue<string>().Should().Be(RecordProcedureReadService.AffectedBasisFindingsClosed);
        CountOf(changeBody).Should().BeNull("past the bound the findings-closed count is null too");
        change.Rig.Probe.Asked.Should().NotContain(a => a.EntitySet == InvoiceSet);
    }

    /// <summary>An In Review invoice filed on P that cannot be judged: <paramref name="how"/> "no date" (no invoice date, no created-on) or "two lookups" (FR-05).</summary>
    private static Guid Unjudgeable(Host host, string how, Guid firm)
    {
        var id = Guid.NewGuid();
        var lookups = InvoiceFamilyCoreRecordGuard.CoreRecordLookups["sprk_invoice"];
        var columns = new List<(string, object)>
        {
            (lookups.Single(l => l.CoreEntity == Project).Column, Ref(Project, P)),
            ("sprk_vendororg", Ref("sprk_organization", firm)),
            ("statecode", Option(InvoiceStatusCodes.StateActive)),
            ("statuscode", Option(InvoiceStatusCodes.InReview)),
        };
        if (how == "two lookups")
        {
            columns.Add((lookups.Single(l => l.CoreEntity == Matter).Column, Ref(Matter, M)));
            columns.Add((InvoiceAdjustmentService.InvoiceDateColumn, Feb1.ToDateTime(TimeOnly.MinValue)));
        }

        host.Rig.World.Add("sprk_invoice", id, columns.ToArray());
        return id;
    }

    [Theory(DisplayName = "084d-2b fix round 1 (F-2, K-c): an In Review invoice that cannot be judged (no date, or two core lookups) makes the count null only when the caller can Read it; one the caller cannot Read leaves the count a number")]
    [InlineData("no date", true, null)]
    [InlineData("no date", false, 1)]
    [InlineData("two lookups", true, null)]
    [InlineData("two lookups", false, 1)]
    public async Task UnjudgeableInvoice_NullsTheCount_OnlyWhenTheCallerCanReadIt(string how, bool readable, int? expected)
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.Simple("ASSIGN-ME", ProcedureAppliesTo.AssignedRecordsOnly);
        host.CanRead(BillingAdminAuthorization.ProcedureEntitySet, s.Procedure);
        Invoice(host, Project, P, F, Feb1);                              // the record's own, readable: changes
        var malformed = Unjudgeable(host, how, F);
        if (!readable)
        {
            host.Rig.Probe.Denied.Add((InvoiceSet, malformed));
        }

        var response = await host.Client.SendAsync(Post("projects", P, AssignBody(s.Procedure)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        CountOf(await Json(response)).Should().Be(expected, readable
            ? "an invoice the caller can Read and that cannot be judged is no count"
            : "an invoice the caller cannot Read takes no part in the answer, judgeable or not");
        host.Rig.NoWrites();
    }

    [Fact(DisplayName = "084d-2b fix round 1 (F-2): the lines of an invoice the caller cannot Read are never read, so they cannot change the answer (an endless lines table does not null a count whose only invoice is unreadable)")]
    public async Task UnreadableInvoice_ItsLinesAreNeverRead()
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.Simple("ASSIGN-ME", ProcedureAppliesTo.AssignedRecordsOnly);
        host.CanRead(BillingAdminAuthorization.ProcedureEntitySet, s.Procedure);
        host.Rig.Probe.Denied.Add((InvoiceSet, Invoice(host, Project, P, F, Feb1)));
        host.Rig.World.EndlessPagesOf("sprk_billingevent");

        var response = await host.Client.SendAsync(Post("projects", P, AssignBody(s.Procedure)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        CountOf(await Json(response)).Should().Be(0, "the only invoice is unreadable: nothing to judge, and its lines are not read");
    }

    private static void Children(Host host, int total)
    {
        // W is the first child; add the rest under P.
        for (var i = 1; i < total; i++)
        {
            host.Rig.World.Add(WorkAssignment, Guid.NewGuid(), ("sprk_name", $"Work {i}"), ("sprk_regardingproject", Ref(Project, P)));
        }
    }

    [Theory(DisplayName = "084d-2b fix round 1 (F-1): a change whose records under the record were not read (more than the 100-child bound, or a child that cannot be read) says so with the write's own children-open message and has NO count, never the record's own invoices as the whole; exactly 100 children are read and counted")]
    [InlineData("more than 100 children", false)]
    [InlineData("a child that cannot be read", false)]
    [InlineData("exactly 100 children", true)]
    public async Task Change_WithUnreadChildren_IsANullCount_NeverAPartialOne(string how, bool childrenRead)
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.Changeable("CHANGE-ME");
        host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, s.Rule);
        Finding(host, s.Rule, Invoice(host, Project, P, F, new DateOnly(2026, 2, 12)));    // the record's own: a partial count would be 1
        switch (how)
        {
            case "more than 100 children": Children(host, ProcedureAssignmentService.MaxChildRecords + 1); break;
            case "exactly 100 children": Children(host, ProcedureAssignmentService.MaxChildRecords); break;
            default: host.Rig.World.FailingRowReadsOf(WorkAssignment, W); break;
        }

        var response = await host.Client.SendAsync(Post("projects", P, ChangeBody(s.Rule)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        Closes(body)["kinds"]!.AsArray().Select(k => k!.GetValue<string>()).Should().Equal(PlannedCloses.BaseRuleSuperseded);
        Closes(body)["message"]!.GetValue<string>().Should().Be(
            childrenRead ? ProcedureAssignmentService.ChangeCloseCompleteMessage : ProcedureAssignmentService.ChangeCloseChildrenOpenMessage);
        CountOf(body).Should().Be(childrenRead ? 1 : null);
        Affected(body)["basis"]!.GetValue<string>().Should().Be(RecordProcedureReadService.AffectedBasisFindingsClosed);
        host.Rig.NoWrites();
    }

    [Fact(DisplayName = "084d-2b fix round 1 (review finding 4): a change whose selection faults has the write's own 'could not be closed now' message and NO count")]
    public async Task Change_SelectionFault_IsANullCount_WithTheIncompleteMessage()
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.Changeable("CHANGE-ME");
        host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, s.Rule);
        Finding(host, s.Rule, Invoice(host, Project, P, F, new DateOnly(2026, 2, 12)));
        host.SelectionFaults.Add(s.Rule);

        var response = await host.Client.SendAsync(Post("projects", P, ChangeBody(s.Rule)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        Closes(body)["kinds"]!.AsArray().Select(k => k!.GetValue<string>()).Should().Equal(PlannedCloses.BaseRuleSuperseded);
        Closes(body)["message"]!.GetValue<string>().Should().Be(ProcedureAssignmentService.ChangeCloseIncompleteMessage);
        CountOf(body).Should().BeNull();
        host.Rig.NoWrites();
    }

    [Fact(DisplayName = "084d-2b (D-170, R1): a plain rule Exclude closes nothing, even with open findings on the rule; its count is the resolution count")]
    public async Task PlainExclude_ClosesNothing_EvenWithOpenFindings()
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.Simple("GENERAL");
        host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, s.Rule);
        Finding(host, s.Rule, Invoice(host, Project, P, F, Feb1));

        var response = await host.Client.SendAsync(Post("projects", P, ExcludeBody(s.Rule)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        Closes(body)["d170"]!.GetValue<bool>().Should().BeFalse("D-170 applies only when a rule is switched Off");
        Closes(body)["kinds"]!.AsArray().Should().BeEmpty();
        Closes(body)["warning"].Should().BeNull();
        Affected(body)["basis"]!.GetValue<string>().Should().Be(RecordProcedureReadService.AffectedBasisResolutionChanged);
        CountOf(body).Should().Be(1, "the rule stops running on the record's invoice");
        host.Closure.VerifyNoOtherCalls();
        host.Rig.NoWrites();
    }

    [Fact(DisplayName = "084d-2b (D-170, section 15): removing a changed rule shows the removal warning and counts the invoices whose findings of the record rule close (the removal preview's selection); a plain removal closes nothing; a selection fault keeps the warning with no count")]
    public async Task RemoveChangedRule_ShowsTheRemovalWarning_AndCountsTheFindingsThatClose()
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.Changeable("GENERAL");
        var recordRule = ProcedureAssignmentService.RecordRulePolicyIdFor(ProcedureAssignmentService.RecordRuleSetIdFor(Project, P, null), s.Rule);
        var changed = host.Rig.ChangeRuleRows(Project, P, s.Rule, s.RuleVersion, Jan15, code: ProcedureAssignmentService.RecordRuleCode(recordRule), rowVersion: true);
        host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, s.Rule);
        Finding(host, changed.RecordRule, Invoice(host, Project, P, F, Feb1));
        Finding(host, changed.RecordRule, Invoice(host, WorkAssignment, W, F, Feb1));
        Finding(host, s.Rule, Invoice(host, Project, P, F, Feb1));          // the BASE rule's finding: not this close's
        var other = host.Rig.Simple("OTHER").Rule;
        var plain = host.Rig.ExcludeRule(Project, P, other, reason: "waived");
        host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, other);

        var response = await host.Client.SendAsync(Post("projects", P, new { kind = "Remove", overrideId = changed.Exclude }));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        Closes(body)["d170"]!.GetValue<bool>().Should().BeTrue();
        Closes(body)["kinds"]!.AsArray().Select(k => k!.GetValue<string>()).Should().Equal(PlannedCloses.ChangedRuleRemoved);
        Closes(body)["warning"]!.GetValue<string>().Should().Be(ProcedureAssignmentService.RemoveChangedWarning, "the existing removal warning, no new text");
        Closes(body)["message"].Should().BeNull();
        CountOf(body).Should().Be(2);
        Affected(body)["basis"]!.GetValue<string>().Should().Be(RecordProcedureReadService.AffectedBasisFindingsClosed);
        host.Closure.Verify(c => c.SelectOpenSignalsOfPolicyAsync(changed.RecordRule, true, It.IsAny<CancellationToken>()), Times.Once);
        host.Closure.VerifyNoOtherCalls();
        host.Rig.NoWrites();

        var plainBody = await Json(await host.Client.SendAsync(Post("projects", P, new { kind = "Remove", overrideId = plain })));
        Closes(plainBody)["d170"]!.GetValue<bool>().Should().BeFalse("a plain removal switches nothing Off");
        Closes(plainBody)["kinds"]!.AsArray().Should().BeEmpty();

        host.SelectionFaults.Add(changed.RecordRule);
        var faulted = await Json(await host.Client.SendAsync(Post("projects", P, new { kind = "Remove", overrideId = changed.Exclude })));
        Closes(faulted)["warning"]!.GetValue<string>().Should().Be(ProcedureAssignmentService.RemoveChangedWarning);
        CountOf(faulted).Should().BeNull("a selection that faults is no count");
    }

    [Fact(DisplayName = "084d-2b (089e, F2): a change says what would close with the write's own message and the write's own selection: the base rule's findings on the record's and its direct children's invoices dated on or after the change; not an earlier-dated one; nothing closed")]
    public async Task Change_SaysWhatWouldClose_WithTheWritesOwnMessageAndSelection()
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.Changeable("CHANGE-ME");
        host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, s.Rule);
        var later = new DateOnly(2026, 2, 12);                             // the rig clock is 2026-02-10: "if saved now"
        Finding(host, s.Rule, Invoice(host, Project, P, F, later));
        Finding(host, s.Rule, Invoice(host, WorkAssignment, W, F, later)); // the child's: P's change decides it once the Exclude row exists
        Finding(host, s.Rule, Invoice(host, Project, P, F, Feb1));         // dated before the change: held back

        var response = await host.Client.SendAsync(Post("projects", P, ChangeBody(s.Rule)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        Closes(body)["d170"]!.GetValue<bool>().Should().BeFalse("a change closes Superseded, not the D-170 close");
        Closes(body)["kinds"]!.AsArray().Select(k => k!.GetValue<string>()).Should().Equal(PlannedCloses.BaseRuleSuperseded);
        Closes(body)["warning"].Should().BeNull();
        Closes(body)["message"]!.GetValue<string>().Should().Be(ProcedureAssignmentService.ChangeCloseCompleteMessage, "F2: the write's own text");
        CountOf(body).Should().Be(2, "the record's later invoice and the child's; not the earlier-dated one");
        Affected(body)["basis"]!.GetValue<string>().Should().Be(RecordProcedureReadService.AffectedBasisFindingsClosed);
        host.Closure.Verify(c => c.SelectOpenSignalsOfPolicyOnSubjectsAsync(
            s.Rule, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        host.Closure.VerifyNoOtherCalls();
        host.Rig.NoWrites();
    }

    [Fact(DisplayName = "084d-2b: a re-change (a Change replacing this record's changed rule) closes both: the removal's switch-off of the old record rule (D-170, the removal warning) and the change's supersede (its own message); the findings are counted once each")]
    public async Task ReChange_ClosesTheRemovedRecordRulesFindings_AndSupersedesTheBase()
    {
        await using var host = await Host.StartAsync();
        var s = host.Rig.Changeable("RE-CHANGE");
        var recordRule = ProcedureAssignmentService.RecordRulePolicyIdFor(ProcedureAssignmentService.RecordRuleSetIdFor(Project, P, null), s.Rule);
        var changed = host.Rig.ChangeRuleRows(Project, P, s.Rule, s.RuleVersion, Jan15, code: ProcedureAssignmentService.RecordRuleCode(recordRule), rowVersion: true);
        host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, s.Rule);
        var invoice = Invoice(host, Project, P, F, Feb1);
        Finding(host, changed.RecordRule, invoice);

        var response = await host.Client.SendAsync(Post("projects", P, ChangeBody(s.Rule, replaces: changed.Exclude)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        Closes(body)["d170"]!.GetValue<bool>().Should().BeTrue("the replaced override's removal switches its record rule Off first");
        Closes(body)["kinds"]!.AsArray().Select(k => k!.GetValue<string>()).Should().Equal(PlannedCloses.ChangedRuleRemoved, PlannedCloses.BaseRuleSuperseded);
        Closes(body)["warning"]!.GetValue<string>().Should().Be(ProcedureAssignmentService.RemoveChangedWarning);
        Closes(body)["message"]!.GetValue<string>().Should().Be(ProcedureAssignmentService.ChangeCloseCompleteMessage);
        CountOf(body).Should().Be(1, "the old record rule's finding on the record's invoice");
        host.Closure.Verify(c => c.SelectOpenSignalsOfPolicyAsync(changed.RecordRule, true, It.IsAny<CancellationToken>()), Times.Once);
        host.Rig.NoWrites();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // EB-D-218 guards over the dry-run routes
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed record DryRunSeed(string Segment, Guid RecordId, object Body, Guid[] AlsoAsked);

    public static TheoryData<string> DryRunSeeds => new()
    {
        "assign", "exclude with firm", "switch", "remove", "change", "re-change",
        "assign with invoices", "exclude for a firm with an invoice", "remove changed with findings", "change with findings",
    };

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

            case "change":
                {
                    // 084d-2a fix round 1 (F-1): the Change kind through the route. Its base rule is real and shows; the record rule, its set, its
                    // first version and the Exclude row are the proposal's.
                    var s = rig.Changeable("CHANGE-ME");
                    host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, s.Rule);
                    return new DryRunSeed("projects", P, ChangeBody(s.Rule), []);
                }

            case "re-change":
                {
                    // A Change replacing this record's own Change of the same rule: the removal is planned first, and the change is planned over it,
                    // re-creating the record rule at its derived id (a real row) and the Exclude row at its derived id (the removed one).
                    var s = rig.Changeable("RE-CHANGE");
                    var recordRule = ProcedureAssignmentService.RecordRulePolicyIdFor(ProcedureAssignmentService.RecordRuleSetIdFor(Project, P, null), s.Rule);
                    var changed = rig.ChangeRuleRows(
                        Project, P, s.Rule, s.RuleVersion, Jan15, code: ProcedureAssignmentService.RecordRuleCode(recordRule), rowVersion: true);
                    host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, s.Rule);
                    return new DryRunSeed("projects", P, ChangeBody(s.Rule, replaces: changed.Exclude), [changed.Exclude]);
                }

            // 084d-2b: seeds whose 200 carries a non-zero closes / affectedOpenInvoices.
            case "assign with invoices":
                {
                    rig.Simple("GENERAL");
                    var s = rig.Simple("ASSIGN-ME", ProcedureAppliesTo.AssignedRecordsOnly);
                    host.CanRead(BillingAdminAuthorization.ProcedureEntitySet, s.Procedure);
                    Invoice(host, Project, P, F, Feb1);
                    Invoice(host, WorkAssignment, W, F, Feb1);
                    return new DryRunSeed("projects", P, AssignBody(s.Procedure), [W]);
                }

            case "exclude for a firm with an invoice":
                {
                    // The excluded rule is firm G's: the list (no firm) does not show it, so its version is read only by the count's (P, G)
                    // resolution. The count depends on that version, so it must be asked (the version-row guard's must-fire seed).
                    rig.Simple("GENERAL");
                    var g = rig.Simple("FIRM-G", firm: G);
                    host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, g.Rule);
                    Invoice(host, Project, P, G, Feb1);
                    return new DryRunSeed("projects", P, ExcludeBody(g.Rule, G), [g.RuleVersion]);
                }

            case "remove changed with findings":
                {
                    var s = rig.Changeable("GENERAL");
                    var recordRule = ProcedureAssignmentService.RecordRulePolicyIdFor(ProcedureAssignmentService.RecordRuleSetIdFor(Project, P, null), s.Rule);
                    var changed = rig.ChangeRuleRows(Project, P, s.Rule, s.RuleVersion, Jan15, code: ProcedureAssignmentService.RecordRuleCode(recordRule), rowVersion: true);
                    host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, s.Rule);
                    Finding(host, changed.RecordRule, Invoice(host, Project, P, F, Feb1));
                    return new DryRunSeed("projects", P, new { kind = "Remove", overrideId = changed.Exclude }, [changed.Exclude]);
                }

            case "change with findings":
                {
                    var s = rig.Changeable("CHANGE-ME");
                    host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, s.Rule);
                    Finding(host, s.Rule, Invoice(host, Project, P, F, new DateOnly(2026, 2, 12)));
                    return new DryRunSeed("projects", P, ChangeBody(s.Rule), []);
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

    /// <summary>The four override entity sets and the tables they hold (the only rows a dry run can exempt).</summary>
    private static readonly IReadOnlyDictionary<string, string> TableOfOverrideSet = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [AssignmentSet] = ProcedureAssignmentEntity.Entity,
        ["sprk_rulesets"] = RuleSet,
        ["sprk_policies"] = Policy,
        ["sprk_policyversions"] = PolicyVersion,
    };

    /// <summary>
    /// K-7: the exempt ids the guard allows, derived from the ANSWER and the WORLD and never from what the service reported: the body's
    /// proposed row ids minus those that exist in any of the four override tables of the rig's world (the dry run writes nothing, so the world
    /// after the request is the world it read).
    /// </summary>
    private static HashSet<Guid> ExpectedExemptIds(JsonObject body, RecordProcedureRig rig) =>
        body["proposal"]!["proposedRowIds"]!.AsArray().Select(v => v!.GetValue<Guid>())
            .Where(id => !TableOfOverrideSet.Values.Any(table => rig.World.Has(table, id))).ToHashSet();

    /// <summary>
    /// K-7: how the reported exemption departs from the derived one. Each exempt (entity set, id) must name one of the four override sets and
    /// must be absent from THAT set's table (a real row is never exempt), and the exempt ids must be exactly the derived ones.
    /// </summary>
    private static List<string> ExemptionViolations(IEnumerable<(string Set, Guid Id)> exempted, JsonObject body, RecordProcedureRig rig)
    {
        var reported = exempted.Distinct().ToList();
        var violations = new List<string>();
        foreach (var (set, id) in reported)
        {
            if (!TableOfOverrideSet.TryGetValue(set, out var table))
            {
                violations.Add($"{set} {id}: not an override entity set");
            }
            else if (rig.World.Has(table, id))
            {
                violations.Add($"{set} {id}: a real row is exempt");
            }
        }

        if (!reported.Select(r => r.Id).ToHashSet().SetEquals(ExpectedExemptIds(body, rig)))
        {
            violations.Add("the exempt ids are not exactly the proposed ids that exist in no override table");
        }

        return violations;
    }

    [Theory(DisplayName = "084d-2a guard (Every200Guid_WasAskedOfTheCaller, dry run): every GUID in a dry-run 200 was asked of the caller, except exactly the synthetic proposed rows (exempt, never asked); a real proposed id is asked")]
    [MemberData(nameof(DryRunSeeds))]
    public async Task Every200Guid_WasAskedOfTheCaller_DryRun(string seedName)
    {
        await using var host = await Host.StartAsync();
        var seed = SeedFor(seedName, host);

        var response = await host.Client.SendAsync(Post(seed.Segment, seed.RecordId, seed.Body));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        var guids = GuidsIn(body).Distinct().ToList();
        var askedPairs = host.Rig.Probe.Asked.ToHashSet();
        var asked = askedPairs.Select(a => a.RecordId).ToHashSet();
        var reported = host.Exempted.SelectMany(e => e).ToList();

        // K-7: the exempt ids the verdict allows are derived from the answer and the world, not from what the service reported exempt, so a
        // code change that exempts a real row fails here with no AlsoAsked listing needed.
        var exempt = ExpectedExemptIds(body, host.Rig);
        guids.Should().Contain(seed.RecordId);
        guids.Count.Should().BeGreaterThanOrEqualTo(3, "the seed is rich enough to be a guard");
        ExemptionViolations(reported, body, host.Rig).Should().BeEmpty($"{seedName}: only the synthetic rows are exempt, matched with their entity set");
        Unasked(guids, asked, exempt).Should().BeEmpty($"{seedName}: a GUID in the answer the caller's rights were never checked against");
        reported.Where(askedPairs.Contains).Should().BeEmpty("an exempt (entity set, id) is never asked");
        seed.AlsoAsked.Where(g => !asked.Contains(g)).Should().BeEmpty($"{seedName}: a row that decided what the answer shows, never checked");

        // Must-fire controls: the verdicts are not vacuous. A GUID planted in the walk that nobody asked is reported; a real row exempted (the
        // seed's base policy version, which every seed holds) is reported; a synthetic row left unexempted is reported.
        var planted = Guid.Parse("084d2a00-0000-4000-8000-00000000beef");
        Unasked(guids.Append(planted), asked, exempt).Should().Equal(planted);

        // 084d-2b: the walk covers the new fields: a GUID planted in closes or in affectedOpenInvoices is reported.
        foreach (var field in new[] { "closes", "affectedOpenInvoices" })
        {
            var mutated = body.DeepClone().AsObject();
            mutated[field]!.AsObject()["planted"] = planted.ToString();
            Unasked(GuidsIn(mutated).Distinct(), asked, exempt).Should().Equal(new[] { planted }, $"a GUID in {field} is walked");
        }
        var baseVersion = host.Rig.VersionRows.First(v => v.Set == "sprk_policyversions");
        ExemptionViolations(reported.Append((baseVersion.Set, baseVersion.Id)), body, host.Rig).Should().NotBeEmpty("a real row exempt must fail the guard");
        if (reported.Count > 0)
        {
            ExemptionViolations(reported.Skip(1), body, host.Rig).Should().NotBeEmpty("a synthetic row asked of Dataverse must fail the guard");
        }
    }

    [Theory(DisplayName = "084d-2a F-2 (EB-D-218): the override a removal removes, or a create replaces, is asked of the caller even when no list shows it (a Follow row of a Retired Procedure); denying that row alone is 503")]
    [InlineData("remove")]
    [InlineData("replace")]
    public async Task TheNamedOverride_IsAsked_EvenWhenNoListShowsIt(string how)
    {
        await using var host = await Host.StartAsync();
        var rig = host.Rig;
        var retired = rig.AddProcedure("RETIRED", ProcedureAppliesTo.AssignedRecordsOnly, status: ProcedureEntity.StatusRetired);
        var row = rig.Assign(Project, P, retired, by: Reviewer);
        var other = rig.Simple("GENERAL");
        host.CanRead(BillingAdminAuthorization.ProcedureEntitySet, retired);
        host.CanRead(ProcedureAssignmentEndpoints.PolicyEntitySet, other.Rule);
        object body = how == "remove"
            ? new { kind = "Remove", overrideId = row }
            : new { kind = "ExcludeRule", replacesOverrideId = row, excludeRule = new { policyId = other.Rule, reason = "Agreed for this record" } };

        var response = await host.Client.SendAsync(Post("projects", P, body));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await Json(response);
        GuidsIn(json["now"]).Should().NotContain(row, "the premise: no list shows a row of a Retired Procedure");
        GuidsIn(json["after"]).Should().NotContain(row);
        GuidsIn(json["proposal"]).Should().Contain(row, "the answer names it");
        host.Rig.Probe.Asked.Should().Contain((AssignmentSet, row), "the named override is asked, whatever the lists show");
        host.Exempted.SelectMany(e => e).Should().NotContain((AssignmentSet, row), "it is a real row");
        host.Rig.NoWrites();

        // Deny that row alone: 503.
        host.Rig.Probe.Denied.Add((AssignmentSet, row));
        await AssertUnavailable(await host.Client.SendAsync(Post("projects", P, body)), host);
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
