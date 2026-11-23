using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
/// ebilling-r1 task 084g (EB-D-221; uac-r2 U-1 to U-5): the record read routes return the caller's write permissions
/// {assignOrExclude, remove, change}, computed from the write routes' own record and privilege declarations
/// (<see cref="ProcedureAssignmentEndpoints.WritePermissionChecks"/>) through the Finance filter's evaluator for the caller. One host maps
/// BOTH the REAL read routes (<c>MapRecordProcedureEndpoints</c>, the real <see cref="RecordProcedureReadService"/> over the in-memory world)
/// and the REAL write routes (<c>MapProcedureAssignmentEndpoints</c>, the service substituted at its seam), so a flag and the route it stands
/// for are asked of the same caller, the same rights and the same probe.
/// </summary>
public sealed class RecordWritePermissionsContractTests
{
    private static readonly string[] Lists = { "procedures", "rules-in-force" };

    private static readonly (string Segment, string Set, Guid Id)[] Cores =
    {
        ("matters", "sprk_matters", M), ("projects", "sprk_projects", P), ("workassignments", "sprk_workassignments", W),
    };

    /// <summary>Every table privilege any 089 write route declares (BillingAdminAuthorization's live names).</summary>
    private static readonly string[] AllPrivileges =
    {
        BillingAdminAuthorization.WriteRuleSetPrivilege, BillingAdminAuthorization.CreateRuleSetPrivilege, BillingAdminAuthorization.CreatePolicyPrivilege,
        BillingAdminAuthorization.CreatePolicyVersionPrivilege, BillingAdminAuthorization.WritePolicyVersionPrivilege, BillingAdminAuthorization.WritePolicyPrivilege,
    };

    private static readonly Guid ProcedureId = Guid.Parse("084a0000-0000-4000-8000-0000000000a1");
    private static readonly Guid PolicyId = Guid.Parse("084a0000-0000-4000-8000-0000000000a2");
    private static readonly Guid OverrideId = Guid.Parse("084a0000-0000-4000-8000-0000000000a3");

    public static TheoryData<string, string, string> AllReadRoutes
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

    // ═══════════════════════════════════════════════════════════════════════════
    // Host
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>What the flags' evaluator logged: (level, message).</summary>
    private sealed class FilterLog : ILogger<FinanceAuthorizationFilter>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>
    /// The recording rights source, faulting a record's check from its <see cref="FaultFromCall"/>-th ask on (the read's own Read is the
    /// first): by throwing, or (<see cref="FaultedSnapshot"/>) as the production source does, with a no-rights snapshot marked faulted.
    /// </summary>
    private sealed class FaultingAccess(FinAuth.RecordingAccessDataSource inner) : IAccessDataSource
    {
        public int? FaultFromCall { get; set; }

        public bool FaultedSnapshot { get; set; }

        private int _recordCalls;

        public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default) =>
            inner.GetUserAccessAsync(userId, resourceId, userAccessToken, ct);

        public async Task<AccessSnapshot> GetRecordAccessAsync(string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default)
        {
            var call = Interlocked.Increment(ref _recordCalls);
            var answer = await inner.GetRecordAccessAsync(userId, entitySetName, recordId, userAccessToken, ct);
            if (FaultFromCall is not { } from || call < from)
            {
                return answer;
            }

            return FaultedSnapshot
                ? new AccessSnapshot { UserId = userId, ResourceId = recordId.ToString(), AccessRights = AccessRights.None, Faulted = true }
                : throw new HttpRequestException("RetrievePrincipalAccess faulted");
        }
    }

    private sealed class Host : IAsyncDisposable
    {
        public readonly RecordProcedureRig Rig = new();
        public readonly FinAuth.RecordingAccessDataSource Access = new();
        public readonly FilterLog Log = new();
        public FaultingAccess Faulting = null!;

        public readonly Mock<ProcedureAssignmentService> Assignments = new(
            MockBehavior.Strict, Mock.Of<IGenericEntityService>(), Mock.Of<IRecordOwnershipResolver>(),
            (RuleSetService)RuntimeHelpers.GetUninitializedObject(typeof(RuleSetService)),
            (Sprk.Bff.Api.Services.Signals.SignalClosureService)RuntimeHelpers.GetUninitializedObject(typeof(Sprk.Bff.Api.Services.Signals.SignalClosureService)),
            (AffectedInvoiceCounter)RuntimeHelpers.GetUninitializedObject(typeof(AffectedInvoiceCounter)),
            NullLogger<ProcedureAssignmentService>.Instance);

        public StatedCaller StepZero { get; private set; } = null!;
        public HttpClient Client = null!;
        private WebApplication _app = null!;

        /// <param name="everything">The caller holds every check of every write: Read, Write and AppendTo on each core record and every privilege.</param>
        /// <param name="registerProbe">False: no <see cref="CallerRecordAccessProbe"/> in DI (the read service keeps its own), so a privilege check has no probe.</param>
        public static async Task<Host> StartAsync(bool everything = true, bool ebillingEnabled = true, bool registerProbe = true)
        {
            var host = new Host();
            host.Faulting = new FaultingAccess(host.Access);
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
            builder.Services.AddSingleton<IAccessDataSource>(host.Faulting);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();
            if (registerProbe)
            {
                builder.Services.AddSingleton<CallerRecordAccessProbe>(host.Rig.Probe);
            }

            builder.Services.AddSingleton(host.Rig.Service);
            builder.Services.AddSingleton(host.Assignments.Object);
            builder.Services.AddSingleton<ILogger<FinanceAuthorizationFilter>>(host.Log);
            foreach (var core in Cores)
            {
                host.Access.Grant(core.Set, core.Id, everything ? AccessRights.Read | AccessRights.Write | AccessRights.AppendTo : AccessRights.Read);
            }

            if (everything)
            {
                host.Rig.Probe.Held.UnionWith(AllPrivileges);
                host.Access.Grant(BillingAdminAuthorization.ProcedureEntitySet, ProcedureId, AccessRights.Read);
                host.Access.Grant(ProcedureAssignmentEndpoints.PolicyEntitySet, PolicyId, AccessRights.Read);
            }

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapRecordProcedureEndpoints();
            host._app.MapProcedureAssignmentEndpoints();
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

    private static HttpRequestMessage Send(string method, string url, HttpContent? content = null) => RateCardImportEndpointsTests.Req(method, url, true, content);

    private static async Task<JsonObject> Json(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!.AsObject();

    private static async Task<(bool AssignOrExclude, bool Remove, bool Change)> FlagsOf(HttpResponseMessage response, string list)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await Json(response);
        body.ContainsKey(list == "procedures" ? "procedures" : "rules").Should().BeTrue("the list is returned with the flags");
        var p = body["permissions"]!.AsObject();
        p.Select(kv => kv.Key).Should().BeEquivalentTo(new[] { "assignOrExclude", "remove", "change" }, "exactly the three flags, camelCase");
        return (p["assignOrExclude"]!.GetValue<bool>(), p["remove"]!.GetValue<bool>(), p["change"]!.GetValue<bool>());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC 1: the flags on every route; each flag false when exactly one of its checks is denied
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory(DisplayName = "084g: both read routes on every core record return permissions; a caller holding every check gets all three true, asked as the caller")]
    [MemberData(nameof(AllReadRoutes))]
    public async Task EveryReadRoute_CallerHoldingEveryCheck_GetsAllThreeTrue(string url, string set, string list)
    {
        await using var host = await Host.StartAsync();
        host.Rig.Simple("GENERAL");

        var flags = await FlagsOf(await host.Client.SendAsync(Send("GET", url)), list);

        flags.Should().Be((true, true, true), $"{set} {list}");
        host.Rig.Probe.PrivilegeCalls.Should().OnlyContain(c => c.HasToken, "asked with the caller's own token");
        host.Rig.Probe.PrivilegeCalls.Select(c => c.Privilege).Should().BeEquivalentTo(AllPrivileges, "each privilege once, whatever number of flags use it");
        host.Access.Calls.Where(c => c.Set == set).Should().HaveCount(2, "the read's own Read check, then the flags' record check").And.OnlyContain(c => c.HasToken);
        host.Rig.NoWrites();
    }

    /// <summary>
    /// One check denied, and which flags it turns off (assignOrExclude, remove, change). The table is stated here from the 089 routes'
    /// contract (ProcedureAssignmentEndpoints remarks), not derived from the declarations under test.
    /// </summary>
    public static TheoryData<string, string, bool, bool, bool> OneCheckDenied
    {
        get
        {
            var data = new TheoryData<string, string, bool, bool, bool>();
            foreach (var list in Lists)
            {
                data.Add("record Write", list, false, false, false);
                data.Add("record AppendTo", list, false, false, false);
                data.Add(BillingAdminAuthorization.WriteRuleSetPrivilege, list, false, false, false);
                data.Add(BillingAdminAuthorization.WritePolicyPrivilege, list, true, false, false);
                data.Add(BillingAdminAuthorization.CreateRuleSetPrivilege, list, true, true, false);
                data.Add(BillingAdminAuthorization.CreatePolicyPrivilege, list, true, true, false);
                data.Add(BillingAdminAuthorization.CreatePolicyVersionPrivilege, list, true, true, false);
                data.Add(BillingAdminAuthorization.WritePolicyVersionPrivilege, list, true, true, false);
            }

            return data;
        }
    }

    [Theory(DisplayName = "084g must-fire: denying exactly one check turns off exactly the flags whose write route declares it; the list is still 200")]
    [MemberData(nameof(OneCheckDenied))]
    public async Task OneCheckDenied_TurnsOffExactlyTheFlagsThatDeclareIt(string denied, string list, bool assignOrExclude, bool remove, bool change)
    {
        await using var host = await Host.StartAsync();
        host.Rig.Simple("GENERAL");
        switch (denied)
        {
            case "record Write": host.Access.Grant("sprk_matters", M, AccessRights.Read | AccessRights.AppendTo); break;
            case "record AppendTo": host.Access.Grant("sprk_matters", M, AccessRights.Read | AccessRights.Write); break;
            default: host.Rig.Probe.Held.Remove(denied).Should().BeTrue(); break;
        }

        var flags = await FlagsOf(await host.Client.SendAsync(Send("GET", $"/api/finance/matters/{M}/{list}?date=2026-02-01")), list);

        flags.Should().Be((assignOrExclude, remove, change), denied);
        host.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning, "a denial is an answer, not a fault");

        // Fix round 1 (B3): the flags were evaluated, not merely defaulted. The flags' record check ran after the read's own Read, and past a
        // held record check the privilege questions were asked (all six, at once); a refused record check asks none.
        host.Access.Calls.Where(c => c.Set == "sprk_matters").Should().HaveCount(2, "the read's own Read check, then the flags' record check");
        host.Rig.Probe.PrivilegeCalls.Select(c => c.Privilege).Should().BeEquivalentTo(
            denied.StartsWith("record ", StringComparison.Ordinal) ? Array.Empty<string>() : AllPrivileges, denied);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC 2: the guard. Each write route enforces its flag's checks plus its own per-subject Read
    // ═══════════════════════════════════════════════════════════════════════════

    private enum Write { Assign, Exclude, Change, PreviewRemoval, Remove }

    private static EndpointFilterInvocationContext ContextFor(Guid recordId, object? body)
    {
        var http = new DefaultHttpContext();
        http.Request.RouteValues["recordId"] = recordId.ToString();
        return body is null ? new DefaultEndpointFilterInvocationContext(http) : new DefaultEndpointFilterInvocationContext(http, body);
    }

    /// <summary>What the route's two AddFinanceAuthorizationFilter declarations ask, in order (the record filter, then the subject and the privileges).</summary>
    private static List<FinanceAuthorizationCheck> RouteDeclares(Write write, string set, Guid recordId)
    {
        var (targets, body) = write switch
        {
            Write.Assign => ((Func<EndpointFilterInvocationContext, FinanceAuthorizationTargets>)ProcedureAssignmentEndpoints.AssignTargets,
                (object?)new ProcedureAssignmentRequest(ProcedureId, null, null, null)),
            Write.Exclude => (ProcedureAssignmentEndpoints.ExcludeTargets, new RuleExcludeRequest(PolicyId, "x", null)),
            Write.Change => (ProcedureAssignmentEndpoints.ChangeTargets, new RuleChangeRequest(PolicyId, "x", null, null, null, "Cap", null)),
            Write.PreviewRemoval => (ProcedureAssignmentEndpoints.PreviewRemovalTargets, null),
            _ => (ProcedureAssignmentEndpoints.RemoveTargets, null),
        };
        var context = ContextFor(recordId, body);
        var second = targets(context);
        second.Rejection.Should().BeNull("a complete body");
        return ProcedureAssignmentEndpoints.RecordTargets(context, set).Checks.Concat(second.Checks).ToList();
    }

    /// <summary>The route's per-subject Read (the read already asks the caller's Read on every subject it exposes); none for the removals, whose subject Read is a later filter.</summary>
    private static FinanceAuthorizationCheck? PerSubjectRead(Write write) => write switch
    {
        Write.Assign => new FinanceAuthorizationCheck { Path = FinanceCheckPath.Record, EntitySetName = BillingAdminAuthorization.ProcedureEntitySet, RecordId = ProcedureId, Operation = "read", Source = "body.procedureId" },
        Write.Exclude or Write.Change => new FinanceAuthorizationCheck { Path = FinanceCheckPath.Record, EntitySetName = ProcedureAssignmentEndpoints.PolicyEntitySet, RecordId = PolicyId, Operation = "read", Source = "body.policyId" },
        _ => null,
    };

    private static ProcedureAssignmentEndpoints.WriteFlag FlagOf(Write write) => write switch
    {
        Write.Assign or Write.Exclude => ProcedureAssignmentEndpoints.WriteFlag.AssignOrExclude,
        Write.Change => ProcedureAssignmentEndpoints.WriteFlag.Change,
        _ => ProcedureAssignmentEndpoints.WriteFlag.Remove,
    };

    /// <summary>The guard's comparison: the route's checks equal the flag's plus the route's per-subject Read (same checks, same order, nothing else).</summary>
    private static bool RouteEqualsFlagPlusSubject(IReadOnlyList<FinanceAuthorizationCheck> route, IReadOnlyList<FinanceAuthorizationCheck> flag, FinanceAuthorizationCheck? subject) =>
        route.Count(c => c == subject) == (subject is null ? 0 : 1)
        && route.Where(c => c != subject).SequenceEqual(flag);

    public static TheoryData<string, string> GuardCases
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var core in Cores)
            {
                foreach (var write in new[] { Write.Assign, Write.Exclude, Write.Change, Write.Remove })
                {
                    data.Add(write.ToString(), core.Set);
                }
            }

            return data;
        }
    }

    [Theory(DisplayName = "084g guard: each write route's declarations equal its flag's checks plus that route's per-subject Read (must-fire: a check added to either side alone fails)")]
    [MemberData(nameof(GuardCases))]
    public void EachWriteRoute_DeclaresItsFlagsChecks_PlusItsPerSubjectRead(string writeName, string set)
    {
        var write = Enum.Parse<Write>(writeName);
        var id = Cores.First(c => c.Set == set).Id;
        var route = RouteDeclares(write, set, id);
        var flag = ProcedureAssignmentEndpoints.WritePermissionChecks(FlagOf(write), set, id);
        var subject = PerSubjectRead(write);

        RouteEqualsFlagPlusSubject(route, flag, subject).Should().BeTrue($"{write} on {set}: route [{string.Join(", ", route.Select(c => c.Operation))}] vs flag [{string.Join(", ", flag.Select(c => c.Operation))}]");
        flag[0].Should().Be(ProcedureAssignmentEndpoints.RecordWriteCheck(set, id), "the record check comes first, as the route's first filter");
        flag[0].Operation.Should().Be("ebilling.record.override");

        // Must-fire controls: the comparison is not vacuous.
        var extra = FinanceAuthorizationCheck.CallerPrivilege("prvCreatesprk_somethingelse", "sprk_somethingelses", "control");
        RouteEqualsFlagPlusSubject(route.Append(extra).ToList(), flag, subject).Should().BeFalse("a check added to the route only must fail the guard");
        RouteEqualsFlagPlusSubject(route, flag.Append(extra).ToList(), subject).Should().BeFalse("a check added to the flag only must fail the guard");
        RouteEqualsFlagPlusSubject(route.Where(c => c != flag[^1]).ToList(), flag, subject).Should().BeFalse("a check dropped from the route must fail the guard");
    }

    [Theory(DisplayName = "084g guard: the removal preview's declarations are the remove flag's minus Write on sprk_policy, so a true remove flag never hides a preview refusal of the same checks")]
    [InlineData("sprk_matters")]
    [InlineData("sprk_projects")]
    [InlineData("sprk_workassignments")]
    public void RemovalPreview_IsTheRemoveFlagMinusWriteOnPolicy(string set)
    {
        var id = Cores.First(c => c.Set == set).Id;
        var preview = RouteDeclares(Write.PreviewRemoval, set, id);
        var remove = ProcedureAssignmentEndpoints.WritePermissionChecks(ProcedureAssignmentEndpoints.WriteFlag.Remove, set, id);

        remove.Except(preview).Select(c => c.Operation).Should().Equal(BillingAdminAuthorization.WritePolicyPrivilege);
        preview.Except(remove).Should().BeEmpty();
    }

    /// <summary>All fifteen write routes: (method, url, the record's entity set, the write).</summary>
    public static TheoryData<string, string, string, string> AllWriteRoutes
    {
        get
        {
            var data = new TheoryData<string, string, string, string>();
            foreach (var core in Cores)
            {
                var root = $"/api/finance/{core.Segment}/{core.Id}";
                data.Add("POST", root + "/procedure-assignments", core.Set, nameof(Write.Assign));
                data.Add("POST", root + "/rule-overrides/exclude", core.Set, nameof(Write.Exclude));
                data.Add("POST", root + "/rule-overrides/change", core.Set, nameof(Write.Change));
                data.Add("GET", $"{root}/overrides/{OverrideId}/removal-preview", core.Set, nameof(Write.PreviewRemoval));
                data.Add("DELETE", $"{root}/overrides/{OverrideId}", core.Set, nameof(Write.Remove));
            }

            return data;
        }
    }

    private static HttpContent? BodyOf(Write write) => write switch
    {
        Write.Assign => JsonContent.Create(new { procedureId = ProcedureId }),
        Write.Exclude => JsonContent.Create(new { policyId = PolicyId, reason = "x" }),
        Write.Change => JsonContent.Create(new { policyId = PolicyId, reason = "x", shortHeadline = "Cap" }),
        _ => null,
    };

    [Theory(DisplayName = "084g guard (behaviour): every write route, run through its real filter chain, asks the caller exactly its flag's record check and privileges (plus its subject Read), in order")]
    [MemberData(nameof(AllWriteRoutes))]
    public async Task EachWriteRoute_AsksTheCaller_ExactlyItsFlagsChecks(string method, string url, string set, string writeName)
    {
        // eBilling off: the record and privilege filters run, then the kill switch answers 503 before the subject's stored row or the service.
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        var write = Enum.Parse<Write>(writeName);
        var id = Cores.First(c => c.Set == set).Id;

        var response = await host.Client.SendAsync(Send(method, url, BodyOf(write)));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        var declared = write == Write.PreviewRemoval
            ? RouteDeclares(Write.PreviewRemoval, set, id)
            : ProcedureAssignmentEndpoints.WritePermissionChecks(FlagOf(write), set, id).ToList();
        host.Rig.Probe.PrivilegeCalls.Select(c => c.Privilege).Should().Equal(
            declared.Where(c => c.Path == FinanceCheckPath.Privilege).Select(c => c.Operation), "the route asks the privileges its flag evaluates");
        host.Access.Calls.Where(c => c.Set == set).Should().ContainSingle("the record check, once").Which.Id.Should().Be(id);
        host.Access.Calls.Where(c => c.Set != set).Select(c => (c.Set, c.Id)).Should().Equal(
            PerSubjectRead(write) is { } subject ? new[] { (subject.EntitySetName, subject.RecordId) } : Array.Empty<(string, Guid)>(),
            "beyond the flag's checks, only the route's per-subject Read");
        host.Assignments.VerifyNoOtherCalls();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 084d-2a: the dry run asks what the writes it plans ask (one declaration each, DryRunTargets over the *Checks)
    // ═══════════════════════════════════════════════════════════════════════════

    private static readonly Guid ReplacedId = Guid.Parse("084d2a00-0000-4000-8000-0000000000b1");

    /// <summary>A dry-run body: the proposal's kind, plus a replaced override (a switch or re-change) when <paramref name="replaces"/>.</summary>
    private static ProcedureDryRunRequest DryRunBody(Write write, bool replaces) => write switch
    {
        Write.Assign => new ProcedureDryRunRequest(ProcedureDryRunRequest.KindAssign, replaces ? ReplacedId : null,
            Assign: new ProcedureAssignmentRequest(ProcedureId, null, null, null)),
        Write.Exclude => new ProcedureDryRunRequest(ProcedureDryRunRequest.KindExcludeRule, replaces ? ReplacedId : null,
            ExcludeRule: new RuleExcludeRequest(PolicyId, "x", null)),
        Write.Change => new ProcedureDryRunRequest(ProcedureDryRunRequest.KindChangeRule, replaces ? ReplacedId : null,
            ChangeRule: new RuleChangeRequest(PolicyId, "x", null, null, null, "Cap", null)),
        _ => new ProcedureDryRunRequest(ProcedureDryRunRequest.KindRemove, OverrideId: OverrideId),
    };

    /// <summary>What the dry-run route's two AddFinanceAuthorizationFilter declarations ask, in order.</summary>
    private static List<FinanceAuthorizationCheck> DryRunDeclares(ProcedureDryRunRequest body, string set, Guid recordId)
    {
        var context = ContextFor(recordId, body);
        var second = ProcedureAssignmentEndpoints.DryRunTargets(context);
        second.Rejection.Should().BeNull("a complete body");
        return ProcedureAssignmentEndpoints.DryRunRecordTargets(context, set).Checks.Concat(second.Checks).ToList();
    }

    /// <summary>The guard's expectation: finance.read on the record, then the write routes' own declarations of every write the dry run plans.</summary>
    private static List<FinanceAuthorizationCheck> PlannedWritesDeclare(Write write, bool replaces, string set, Guid recordId)
    {
        var read = ProcedureAssignmentEndpoints.DryRunRecordChecks(set, recordId)[0];
        var writes = RouteDeclares(write, set, recordId).AsEnumerable();
        if (replaces)
        {
            writes = writes.Concat(RouteDeclares(Write.Remove, set, recordId));
        }

        return new[] { read }.Concat(writes).Distinct().ToList();
    }

    /// <summary>Same checks, no duplicates, and finance.read then the record's write check first (the uniform-404 filter).</summary>
    private static bool SameChecks(IReadOnlyList<FinanceAuthorizationCheck> dryRun, IReadOnlyList<FinanceAuthorizationCheck> expected) =>
        dryRun.Count == dryRun.Distinct().Count()
        && dryRun.ToHashSet().SetEquals(expected)
        && dryRun.Take(2).SequenceEqual(expected.Take(2));

    public static TheoryData<string, string, bool> DryRunGuardCases
    {
        get
        {
            var data = new TheoryData<string, string, bool>();
            foreach (var core in Cores)
            {
                foreach (var write in new[] { Write.Assign, Write.Exclude, Write.Change })
                {
                    data.Add(write.ToString(), core.Set, false);
                    data.Add(write.ToString(), core.Set, true);
                }

                data.Add(nameof(Write.Remove), core.Set, false);
            }

            return data;
        }
    }

    [Theory(DisplayName = "084d-2a guard (declaration equality): each dry run declares exactly finance.read plus the write routes' own declarations of every write it plans (a replaced override adds the removal's); must-fire: a check added to or dropped from either side fails")]
    [MemberData(nameof(DryRunGuardCases))]
    public void EachDryRun_DeclaresExactlyThePlannedWritesChecks(string writeName, string set, bool replaces)
    {
        var write = Enum.Parse<Write>(writeName);
        var id = Cores.First(c => c.Set == set).Id;
        var dryRun = DryRunDeclares(DryRunBody(write, replaces), set, id);
        var expected = PlannedWritesDeclare(write, replaces, set, id);

        SameChecks(dryRun, expected).Should().BeTrue(
            $"{write}{(replaces ? " replacing" : "")} on {set}: dry run [{string.Join(", ", dryRun.Select(c => c.Operation))}] vs writes [{string.Join(", ", expected.Select(c => c.Operation))}]");
        dryRun[0].Operation.Should().Be("finance.read");
        dryRun[1].Should().Be(ProcedureAssignmentEndpoints.RecordWriteCheck(set, id), "the write check is the uniform-404 filter's, before any 403");

        // Must-fire controls: the comparison is not vacuous.
        var extra = FinanceAuthorizationCheck.CallerPrivilege("prvCreatesprk_somethingelse", "sprk_somethingelses", "control");
        SameChecks(dryRun.Append(extra).ToList(), expected).Should().BeFalse("a check added to the dry run only must fail the guard");
        SameChecks(dryRun, expected.Append(extra).ToList()).Should().BeFalse("a check added to a write only must fail the guard");
        SameChecks(dryRun.Where(c => c != expected[^1]).ToList(), expected).Should().BeFalse("a check dropped from the dry run must fail the guard");
        SameChecks(dryRun.Skip(1).ToList(), expected).Should().BeFalse("dropping finance.read must fail the guard");
    }

    [Fact(DisplayName = "084d-2a guard must-fire: a switch (Assign replacing an override) that dropped the removal's checks would ask less than the writes it plans")]
    public void DryRunGuard_FiresWhenAReplacementDropsTheRemovalsChecks()
    {
        var (set, id) = (Cores[0].Set, Cores[0].Id);
        var withoutRemoval = DryRunDeclares(DryRunBody(Write.Assign, replaces: false), set, id);

        SameChecks(withoutRemoval, PlannedWritesDeclare(Write.Assign, replaces: true, set, id)).Should().BeFalse(
            "the remove flag's privileges are not all among assign's, so a replacement without them must fail the guard");
    }

    public static TheoryData<string, string, string, bool> AllDryRunRoutes
    {
        get
        {
            var data = new TheoryData<string, string, string, bool>();
            foreach (var row in DryRunGuardCases)
            {
                var set = (string)row[1];
                var core = Cores.First(c => c.Set == set);
                data.Add($"/api/finance/{core.Segment}/{core.Id}/procedures/dry-run", set, (string)row[0], (bool)row[2]);
            }

            return data;
        }
    }

    [Theory(DisplayName = "084d-2a guard (behaviour): every dry-run route, through its real filter chain, asks the caller exactly its declared record checks, subject Read and privileges, in order")]
    [MemberData(nameof(AllDryRunRoutes))]
    public async Task EachDryRunRoute_AsksTheCaller_ExactlyItsDeclaredChecks(string url, string set, string writeName, bool replaces)
    {
        // eBilling off: the record and privilege filters run, then the kill switch answers 503 before the override's subject Read or the service.
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        var write = Enum.Parse<Write>(writeName);
        var id = Cores.First(c => c.Set == set).Id;
        var body = DryRunBody(write, replaces);

        var response = await host.Client.SendAsync(Send("POST", url, JsonContent.Create(body)));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        var declared = DryRunDeclares(body, set, id);
        host.Rig.Probe.PrivilegeCalls.Select(c => c.Privilege).Should().Equal(
            declared.Where(c => c.Path == FinanceCheckPath.Privilege).Select(c => c.Operation), "the route asks the privileges the planned writes declare");
        host.Access.Calls.Where(c => c.Set == set).Should().NotBeEmpty().And.OnlyContain(c => c.Id == id, "the record checks, on this record only");
        host.Access.Calls.Where(c => c.Set != set).Select(c => (c.Set, c.Id)).Should().Equal(
            PerSubjectRead(write) is { } subject ? new[] { (subject.EntitySetName, subject.RecordId) } : Array.Empty<(string, Guid)>(),
            "beyond the record checks, only the planned write's per-subject Read");
        host.Assignments.VerifyNoOtherCalls();
    }

    /// <summary>Fix round 1 (A1): every write route × a record right short of Write and AppendTo (Read and Write without AppendTo; Read alone).</summary>
    public static TheoryData<string, string, string, string, string> AllWriteRoutesShortOfTheRecordRights
    {
        get
        {
            var data = new TheoryData<string, string, string, string, string>();
            foreach (var row in AllWriteRoutes)
            {
                foreach (var held in new[] { "Read|Write", "Read" })
                {
                    data.Add((string)row[0], (string)row[1], (string)row[2], (string)row[3], held);
                }
            }

            return data;
        }
    }

    [Theory(DisplayName = "084g fix round 1 (A1) guard (behaviour): each write route, with every privilege and subject Read held but the record short of Write+AppendTo, answers the uniform 404, and the matching flag on the same record is false")]
    [MemberData(nameof(AllWriteRoutesShortOfTheRecordRights))]
    public async Task EachWriteRoute_RefusesARecordShortOfWriteAndAppendTo_AndItsFlagIsFalse(string method, string url, string set, string writeName, string held)
    {
        await using var host = await Host.StartAsync();
        host.Rig.Simple("GENERAL");
        var write = Enum.Parse<Write>(writeName);
        var core = Cores.First(c => c.Set == set);
        host.Access.Grant(set, core.Id, held == "Read" ? AccessRights.Read : AccessRights.Read | AccessRights.Write);
        // A stored override row whose subject the caller may Read, so a removal route is not refused for its subject.
        host.Assignments.Setup(s => s.SubjectOfOverrideAsync(It.IsAny<string>(), core.Id, OverrideId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OverrideSubject(BillingAdminAuthorization.ProcedureEntitySet, ProcedureId));

        var response = await host.Client.SendAsync(Send(method, url, BodyOf(write)));
        var flags = await FlagsOf(await host.Client.SendAsync(Send("GET", $"/api/finance/{core.Segment}/{core.Id}/procedures?date=2026-02-01")), "procedures");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{method} {url} with the record {held}: the record filter's uniform 404");
        host.Rig.Probe.PrivilegeCalls.Should().BeEmpty("the record check refused first, on the write as on the flags");
        host.Assignments.VerifyNoOtherCalls();
        var flag = FlagOf(write) switch
        {
            ProcedureAssignmentEndpoints.WriteFlag.AssignOrExclude => flags.AssignOrExclude,
            ProcedureAssignmentEndpoints.WriteFlag.Remove => flags.Remove,
            _ => flags.Change,
        };
        flag.Should().BeFalse($"the {FlagOf(write)} flag stands for {method} {url}");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AC 3 and U-5: a faulted or unanswered check is false, the list 200, logged at Warning
    // ═══════════════════════════════════════════════════════════════════════════

    public static TheoryData<string, string> Faults
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var list in Lists)
            {
                data.Add("the record check faults", list);
                data.Add("the record check answers a faulted snapshot", list);
                data.Add("the Admin() privilege gets no answer", list);
                data.Add("every privilege question throws", list);
                data.Add("no probe is registered", list);
                data.Add("Write on sprk_policy gets no answer", list);
                data.Add("Create on rule set gets no answer", list);
            }

            return data;
        }
    }

    [Theory(DisplayName = "084g must-fire (U-5): a faulted or unanswered check makes its flags false, never true and never a 503; the list is still 200 and the fault is logged at Warning")]
    [MemberData(nameof(Faults))]
    public async Task AFaultedCheck_IsFalse_TheListIs200_AndTheFaultIsLoggedAtWarning(string fault, string list)
    {
        await using var host = await Host.StartAsync(registerProbe: fault != "no probe is registered");
        host.Rig.Simple("GENERAL");
        var expected = (false, false, false);
        switch (fault)
        {
            case "the record check faults": host.Faulting.FaultFromCall = 2; break; // the read's own Read is call 1
            case "the record check answers a faulted snapshot": // fix round 1 (A2): what DataverseAccessDataSource returns on a fault
                host.Faulting.FaultFromCall = 2;
                host.Faulting.FaultedSnapshot = true;
                break;
            case "the Admin() privilege gets no answer": host.Rig.Probe.PrivilegeFaults.Add(BillingAdminAuthorization.WriteRuleSetPrivilege); break;
            case "every privilege question throws": host.Rig.Probe.PrivilegeThrows = new HttpRequestException("privilege read failed"); break;
            case "no probe is registered": break;
            case "Create on rule set gets no answer": // fix round 1 (B4): asked at once with the others, a fault still turns off only its flag
                host.Rig.Probe.PrivilegeFaults.Add(BillingAdminAuthorization.CreateRuleSetPrivilege);
                expected = (true, true, false);
                break;
            default:
                host.Rig.Probe.PrivilegeFaults.Add(BillingAdminAuthorization.WritePolicyPrivilege);
                expected = (true, false, false);
                break;
        }

        var response = await host.Client.SendAsync(Send("GET", $"/api/finance/matters/{M}/{list}?date=2026-02-01"));

        (await FlagsOf(response, list)).Should().Be(expected, fault);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Procedure GENERAL".Replace("Procedure ", list == "procedures" ? "Procedure " : ""));
        host.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning, "the fault is logged at Warning").And.NotContain(e => e.Level >= LogLevel.Error);
        host.Rig.Log.Entries.Should().BeEmpty("the list itself saw no fault");
        host.Rig.NoWrites();
    }

    [Fact(DisplayName = "084g: no permission check runs when the list is not returned (a 503 procedures.unavailable carries no flags and asks nothing more)")]
    public async Task UnavailableList_AsksNoPermissionCheck_AndCarriesNoFlags()
    {
        await using var host = await Host.StartAsync();
        host.Rig.Simple("GENERAL");
        host.Rig.Probe.Throws = true;

        var response = await host.Client.SendAsync(Send("GET", $"/api/finance/matters/{M}/procedures?date=2026-02-01"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response)).ContainsKey("permissions").Should().BeFalse();
        host.Rig.Probe.PrivilegeCalls.Should().BeEmpty();
        host.Access.Calls.Should().ContainSingle("only the read's own record check");
    }

    [Theory(DisplayName = "084g: the extra checks per read: a held record check, then the 6 distinct privileges once each (Admin() held or not); no Write on the record 1 + 0")]
    [InlineData("every check", 6)]
    [InlineData("no Admin()", 6)]
    [InlineData("no record Write", 0)]
    public async Task ExtraChecksPerRead(string caller, int privilegeQuestions)
    {
        await using var host = await Host.StartAsync();
        host.Rig.Simple("GENERAL");
        if (caller == "no Admin()") host.Rig.Probe.Held.Remove(BillingAdminAuthorization.WriteRuleSetPrivilege);
        if (caller == "no record Write") host.Access.Grant("sprk_matters", M, AccessRights.Read);

        (await host.Client.SendAsync(Send("GET", $"/api/finance/matters/{M}/rules-in-force?date=2026-02-01"))).StatusCode.Should().Be(HttpStatusCode.OK);

        host.Access.Calls.Should().HaveCount(2, "the read's own Read check, plus ONE record check shared by the three flags");
        host.Rig.Probe.PrivilegeCalls.Should().HaveCount(privilegeQuestions, "each distinct privilege at most once, none when the record check refuses");
        host.Rig.Probe.Batches.Should().ContainSingle("the read's one batched rights check is unchanged");
    }

    [Theory(DisplayName = "084g fix round 1 (B4/A4): the privilege questions are asked at once, not one after another: two rounds per read (the record check, then all six privileges together)")]
    [InlineData("procedures")]
    [InlineData("rules-in-force")]
    public async Task ThePrivilegeQuestions_AreAskedConcurrently(string list)
    {
        await using var host = await Host.StartAsync();
        host.Rig.Simple("GENERAL");
        host.Rig.Probe.PrivilegeDelay = TimeSpan.FromMilliseconds(100);

        (await FlagsOf(await host.Client.SendAsync(Send("GET", $"/api/finance/matters/{M}/{list}?date=2026-02-01")), list)).Should().Be((true, true, true));

        host.Rig.Probe.PrivilegeCalls.Should().HaveCount(AllPrivileges.Length);
        host.Rig.Probe.MaxPrivilegeQuestionsInFlight.Should().Be(AllPrivileges.Length, "every distinct privilege question is in flight at the same moment");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // U-2: a flag is a hint, never authority
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "084g must-fire (U-2): with every flag true, a write whose per-subject Read fails is still refused by the write route, and nothing is written")]
    public async Task ATrueFlag_PlusAFailingSubjectRead_IsStillRefusedByTheWriteRoute()
    {
        await using var host = await Host.StartAsync();
        host.Rig.Simple("GENERAL");
        (await FlagsOf(await host.Client.SendAsync(Send("GET", $"/api/finance/matters/{M}/procedures?date=2026-02-01")), "procedures"))
            .Should().Be((true, true, true));
        host.Access.Grant(BillingAdminAuthorization.ProcedureEntitySet, ProcedureId, AccessRights.None);
        host.Access.Grant(ProcedureAssignmentEndpoints.PolicyEntitySet, PolicyId, AccessRights.None);
        var unreadableSubject = Guid.NewGuid();
        host.Assignments.Setup(s => s.SubjectOfOverrideAsync(It.IsAny<string>(), M, OverrideId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OverrideSubject(BillingAdminAuthorization.ProcedureEntitySet, unreadableSubject));

        var assign = await host.Client.SendAsync(Send("POST", $"/api/finance/matters/{M}/procedure-assignments", BodyOf(Write.Assign)));
        var exclude = await host.Client.SendAsync(Send("POST", $"/api/finance/matters/{M}/rule-overrides/exclude", BodyOf(Write.Exclude)));
        var change = await host.Client.SendAsync(Send("POST", $"/api/finance/matters/{M}/rule-overrides/change", BodyOf(Write.Change)));
        var remove = await host.Client.SendAsync(Send("DELETE", $"/api/finance/matters/{M}/overrides/{OverrideId}"));

        assign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        exclude.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        change.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        remove.StatusCode.Should().Be(HttpStatusCode.NotFound, "an unreadable stored subject is the 404 a missing row gets");
        (await Json(remove))["errorCode"]!.GetValue<string>().Should().Be("assignment.override_not_found");
        host.Assignments.Verify(s => s.SubjectOfOverrideAsync(It.IsAny<string>(), M, OverrideId, It.IsAny<CancellationToken>()), Times.Once);
        host.Assignments.VerifyNoOtherCalls();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // U-3: no flag cache. Each read computes the caller's flags afresh. (In production the record half of a flag rides the per-caller
    // 60-second record-rights cache the write route also uses; this host's rights source is uncached. The privilege half is uncached.)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "084g (U-3): the flags are not cached; each read asks the caller's checks again, so a right removed between two reads is false on the second (in production the record check rides the write route's 60 s per-caller rights cache; privileges are uncached)")]
    public async Task EveryRead_ComputesTheFlagsAfresh()
    {
        await using var host = await Host.StartAsync();
        host.Rig.Simple("GENERAL");
        var url = $"/api/finance/matters/{M}/procedures?date=2026-02-01";

        (await FlagsOf(await host.Client.SendAsync(Send("GET", url)), "procedures")).Should().Be((true, true, true));
        host.Rig.Probe.Held.Remove(BillingAdminAuthorization.WritePolicyPrivilege);
        (await FlagsOf(await host.Client.SendAsync(Send("GET", url)), "procedures")).Should().Be((true, false, false));
        host.Access.Grant("sprk_matters", M, AccessRights.Read);
        (await FlagsOf(await host.Client.SendAsync(Send("GET", url)), "procedures")).Should().Be((false, false, false));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // U-4: vetoes come from the write route's own evaluator
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "084g must-fire (U-4): a record the caller may Read but whose rights Dataverse restricts for it (no Write, no AppendTo) gives every flag false, and the write route refuses the same caller")]
    public async Task RestrictedRecord_AllFlagsFalse_AndTheWriteRouteRefusesTheSameCaller()
    {
        await using var host = await Host.StartAsync();
        host.Rig.Simple("GENERAL");
        // The restriction reaches the BFF only as the caller's own rights on the record, asked of Dataverse as the caller: here Read alone.
        host.Access.Grant("sprk_matters", M, AccessRights.Read);

        var flags = await FlagsOf(await host.Client.SendAsync(Send("GET", $"/api/finance/matters/{M}/procedures?date=2026-02-01")), "procedures");
        // Fix round 1 (B3): the flags' own record check ran (after the read's own Read), so "false" is its answer, not a default.
        host.Access.Calls.Where(c => c.Set == "sprk_matters").Should().HaveCount(2, "the read's own Read check, then the flags' record check");
        var assign = await host.Client.SendAsync(Send("POST", $"/api/finance/matters/{M}/procedure-assignments", BodyOf(Write.Assign)));
        var remove = await host.Client.SendAsync(Send("DELETE", $"/api/finance/matters/{M}/overrides/{OverrideId}"));

        flags.Should().Be((false, false, false));
        assign.StatusCode.Should().Be(HttpStatusCode.NotFound, "the write route's uniform 404");
        remove.StatusCode.Should().Be(HttpStatusCode.NotFound, "the write route's uniform 404");
        host.Rig.Probe.PrivilegeCalls.Should().BeEmpty("the record check refused first, on the read as on the write");
        host.Assignments.VerifyNoOtherCalls();
    }

    public static IEnumerable<object[]> ReadRoutesRefused => StatedCaller.Cross(AllReadRoutes, StatedCaller.RefusedKinds);

    [Theory(DisplayName = "084g (U-4): every caller kind step 0 refuses (StatedCaller.RefusedKinds) is refused by both read routes: no body, so no flags, and no check is asked")]
    [MemberData(nameof(ReadRoutesRefused))]
    public async Task StepZeroRefusal_HasNoBody_AndNoFlags(string url, string set, string list, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Send("GET", url));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: true);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("permissions", $"{set} {list} {kind}");
        host.Access.Calls.Should().BeEmpty();
        host.Rig.Probe.PrivilegeCalls.Should().BeEmpty();
    }
}
