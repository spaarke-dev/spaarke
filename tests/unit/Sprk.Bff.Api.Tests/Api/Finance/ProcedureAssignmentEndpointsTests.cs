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
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// The assignment and override routes through the REAL <c>MapProcedureAssignmentEndpoints</c>, step 0 (task 099),
/// <see cref="FinanceAuthorizationFilter"/>, <see cref="AuthorizationService"/> and <see cref="EbillingEnabledFilter"/> (ebilling-r1 task 089,
/// ADR-008, ADR-019): step 0 refuses a flagged-external, row-less or flag-fault caller on every route; every route needs the caller's Write and
/// AppendTo on THAT core record, answered as the uniform 404; then Read on the Procedure or rule it names and the billing-admin marker (403),
/// a change the Create privileges of the tables it creates, a removal Write on the policy; for an existing override the Read is on what the
/// STORED row overrides. Every refusal writes nothing. The service is substituted at its own seam (its behaviour is
/// <see cref="Services.Finance.Procedures.ProcedureAssignmentServiceTests"/>).
/// </summary>
public sealed class ProcedureAssignmentEndpointsTests
{
    private static readonly Guid RecordId = Guid.NewGuid();
    private static readonly Guid ProcedureId = Guid.NewGuid();
    private static readonly Guid PolicyId = Guid.NewGuid();
    private static readonly Guid AssignmentId = Guid.NewGuid();
    private const string Matters = "/api/finance/matters";
    private const string ProcedureSet = "sprk_procedures";
    private const string PolicySet = "sprk_policies";
    private const string MatterSet = "sprk_matters";

    private static string M(string path) => $"{Matters}/{RecordId}{path}";

    public static TheoryData<string, string> AllRoutes => new()
    {
        { "POST", M("/procedure-assignments") },
        { "POST", M("/rule-overrides/exclude") },
        { "POST", M("/rule-overrides/change") },
        { "GET", M($"/overrides/{AssignmentId}/removal-preview") },
        { "DELETE", M($"/overrides/{AssignmentId}") },
    };

    private static readonly string[] AdminPrivileges =
    {
        BillingAdminAuthorization.WriteRuleSetPrivilege, BillingAdminAuthorization.CreateRuleSetPrivilege,
        BillingAdminAuthorization.CreatePolicyPrivilege, BillingAdminAuthorization.CreatePolicyVersionPrivilege,
        BillingAdminAuthorization.WritePolicyPrivilege,
    };

    private sealed class Host : IAsyncDisposable
    {
        public StatedCaller StepZero { get; private set; } = null!;
        public readonly Mock<ProcedureAssignmentService> Service = new(
            MockBehavior.Strict,
            Mock.Of<IGenericEntityService>(),
            (RuleSetService)RuntimeHelpers.GetUninitializedObject(typeof(RuleSetService)),
            (SignalClosureService)RuntimeHelpers.GetUninitializedObject(typeof(SignalClosureService)),
            (AffectedInvoiceCounter)RuntimeHelpers.GetUninitializedObject(typeof(AffectedInvoiceCounter)),
            Mock.Of<IRecordOwnershipResolver>(),
            NullLogger<ProcedureAssignmentService>.Instance);

        public readonly FinAuth.RecordingAccessDataSource Access = new();
        public readonly FinAuth.RecordingPrivilegeProbe Probe = new();
        private WebApplication _app = null!;
        public HttpClient Client = null!;

        public static async Task<Host> StartAsync(bool ebillingEnabled = true, bool admin = true)
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
            builder.Services.AddSingleton<CallerRecordAccessProbe>(host.Probe);
            builder.Services.AddSingleton(host.Service.Object);
            if (admin)
            {
                host.GrantAdmin();
            }

            // The stored override names the Procedure (a Procedure-level row) unless a test says otherwise.
            host.Service.Setup(s => s.TargetOfAsync(It.IsAny<string>(), RecordId, AssignmentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OverrideTarget(ProcedureSet, ProcedureId));

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapProcedureAssignmentEndpoints();
            await host._app.StartAsync();
            host.Client = host._app.GetTestClient();
            return host;
        }

        /// <summary>A billing admin with Write and AppendTo on the matter and Read on the Procedure and the rule.</summary>
        public void GrantAdmin()
        {
            foreach (var p in AdminPrivileges)
            {
                Probe.Hold(p);
            }

            Access.Grant(MatterSet, RecordId, AccessRights.Read | AccessRights.Write | AccessRights.AppendTo);
            Access.Grant(ProcedureSet, ProcedureId, AccessRights.Read);
            Access.Grant(PolicySet, PolicyId, AccessRights.Read);
        }

        public void NoWrites()
        {
            Service.Verify(s => s.AssignProcedureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ProcedureAssignmentRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            Service.Verify(s => s.ExcludeRuleAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<RuleExcludeRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            Service.Verify(s => s.ChangeRuleAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<RuleChangeRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            Service.Verify(s => s.PreviewRemoveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
            Service.Verify(s => s.RemoveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static HttpRequestMessage Req(string method, string url, bool authenticated = true, HttpContent? content = null) =>
        RateCardImportEndpointsTests.Req(method, url, authenticated, content);

    /// <summary>A body naming the Procedure or the rule the route checks Read on.</summary>
    private static HttpContent? BodyFor(string method, string url) =>
        method != "POST" ? null
        : url.EndsWith("/procedure-assignments", StringComparison.Ordinal) ? JsonContent.Create(new { procedureId = ProcedureId })
        : url.EndsWith("/change", StringComparison.Ordinal) ? JsonContent.Create(new { policyId = PolicyId, parameters = new { type = "DailyHoursCap", maxHoursPerDay = 12 }, reason = "x" })
        : JsonContent.Create(new { policyId = PolicyId, reason = "x" });

    private static async Task<JsonObject> Json(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!.AsObject();

    private static OverrideView View() => new(Guid.NewGuid(), "Procedure", "FollowLatest", ProcedureId, null, null, null, null);

    // ═══════════════════════════════════════════════════════════════════════════
    // 401 / step 0 / the record (uniform 404) / the subject and role (403) / disabled
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_Unauthenticated_Returns401_AndNeverReachesTheService(string method, string url)
    {
        await using var host = await Host.StartAsync();

        var response = await host.Client.SendAsync(Req(method, url, authenticated: false, content: BodyFor(method, url)));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Service.VerifyNoOtherCalls();
    }

    public static IEnumerable<object[]> AllRoutesRefused => StatedCaller.Cross(AllRoutes, StatedCaller.RefusedKinds);

    [Theory(DisplayName = "089 must-fire: every override route refuses a flagged-external, row-less or flag-fault caller at step 0, before any check or service call")]
    [MemberData(nameof(AllRoutesRefused))]
    public async Task EveryRoute_StepZero_RefusesBeforeAnyCheckOrServiceCall(string method, string url, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method, url)));

        // The record filter's denial is the uniform 404, so step 0 answers in that shape (no existence oracle either way).
        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: true);
        host.Probe.Calls.Should().BeEmpty();
        host.Access.Calls.Should().BeEmpty();
        host.Service.VerifyNoOtherCalls();
    }

    public static IEnumerable<object[]> AllRoutesInternal => StatedCaller.Cross(AllRoutes, StatedCaller.InternalKinds);

    [Theory(DisplayName = "089 must-not-fire: a flag that reads false or blank reaches the record check, asked as the caller")]
    [MemberData(nameof(AllRoutesInternal))]
    public async Task EveryRoute_StepZero_AdmitsAnInternalCaller_ToTheRecordCheck(string method, string url, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync(admin: false);
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method, url)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.Access.Calls.Should().ContainSingle().Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, MatterSet, RecordId, HasToken: true));
        host.Service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(AccessRights.Read)]
    [InlineData(AccessRights.Read | AccessRights.Write)]
    [InlineData(AccessRights.Read | AccessRights.AppendTo)]
    public async Task EveryRoute_WithoutWriteAndAppendToOnTheRecord_IsTheUniform404_AndWritesNothing(AccessRights held)
    {
        foreach (var (method, url) in AllRoutes.Select(r => ((string)r[0], (string)r[1])))
        {
            await using var host = await Host.StartAsync();
            host.Access.Grant(MatterSet, RecordId, held);

            var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method, url)));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{method} {url} with {held}");
            host.Access.Calls.Should().Contain(c => c.Set == MatterSet && c.Id == RecordId && c.HasToken, "asked as the caller, about THAT record");
            host.Probe.Calls.Should().BeEmpty("the record is checked first");
            host.Service.VerifyNoOtherCalls();
        }
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_WithoutTheBillingAdminMarker_Returns403_AndWritesNothing(string method, string url)
    {
        await using var host = await Host.StartAsync();
        host.Probe.Release(BillingAdminAuthorization.WriteRuleSetPrivilege);

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method, url)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.NoWrites();
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_WhenTheRecordProbeFaults_FailsClosed(string method, string url)
    {
        await using var host = await Host.StartAsync();
        host.Access.ThrowOnEveryCall = new HttpRequestException("simulated Dataverse fault");

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method, url)));

        response.IsSuccessStatusCode.Should().BeFalse("fail closed");
        host.NoWrites();
    }

    [Fact]
    public async Task Assign_WithoutReadOnTheProcedure_Returns403_AndWritesNothing()
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(ProcedureSet, ProcedureId, AccessRights.None);

        var response = await host.Client.SendAsync(Req("POST", M("/procedure-assignments"), content: BodyFor("POST", M("/procedure-assignments"))));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().Contain(c => c.Set == ProcedureSet && c.Id == ProcedureId && c.HasToken);
        host.NoWrites();
    }

    [Theory]
    [InlineData("/rule-overrides/exclude")]
    [InlineData("/rule-overrides/change")]
    public async Task ARuleOverride_WithoutReadOnTheRule_Returns403_AndWritesNothing(string path)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(PolicySet, PolicyId, AccessRights.None);

        var response = await host.Client.SendAsync(Req("POST", M(path), content: BodyFor("POST", M(path))));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().Contain(c => c.Set == PolicySet && c.Id == PolicyId && c.HasToken);
        host.NoWrites();
    }

    [Theory]
    [InlineData("POST", "/rule-overrides/change", BillingAdminAuthorization.CreateRuleSetPrivilege)]
    [InlineData("POST", "/rule-overrides/change", BillingAdminAuthorization.CreatePolicyPrivilege)]
    [InlineData("POST", "/rule-overrides/change", BillingAdminAuthorization.CreatePolicyVersionPrivilege)]
    [InlineData("DELETE", "/overrides/{a}", BillingAdminAuthorization.WritePolicyPrivilege)]
    public async Task EachRoute_NeedsThePrivilegeOfEveryTableItWrites(string method, string path, string missing)
    {
        await using var host = await Host.StartAsync();
        host.Probe.Release(missing);
        var url = M(path.Replace("{a}", AssignmentId.ToString()));

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method, url)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.NoWrites();
    }

    [Theory]
    [InlineData("GET", "/removal-preview")]
    [InlineData("DELETE", "")]
    public async Task AnExistingOverride_NeedsReadOnWhatTheStoredRowOverrides_AndARefusalOrFaultWritesNothing(string method, string suffix)
    {
        var url = M($"/overrides/{AssignmentId}{suffix}");

        await using (var noRead = await Host.StartAsync())
        {
            noRead.Service.Setup(s => s.TargetOfAsync(It.IsAny<string>(), RecordId, AssignmentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OverrideTarget(PolicySet, PolicyId));
            noRead.Access.Grant(PolicySet, PolicyId, AccessRights.None);

            var response = await noRead.Client.SendAsync(Req(method, url));

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await Json(response)).ToJsonString().Should().Contain(FinanceAuthorizationFilter.InsufficientRightsReasonCode);
            noRead.Access.Calls.Should().Contain(c => c.Set == PolicySet && c.Id == PolicyId && c.HasToken, "the STORED row's rule, asked as the caller");
            noRead.NoWrites();
        }

        await using (var fault = await Host.StartAsync())
        {
            fault.Service.Setup(s => s.TargetOfAsync(It.IsAny<string>(), RecordId, AssignmentId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("simulated Dataverse fault"));

            var response = await fault.Client.SendAsync(Req(method, url));

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "fail closed");
            (await Json(response)).ToJsonString().Should().Contain(FinanceAuthorizationFilter.SystemFailureReasonCode);
            fault.NoWrites();
        }
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_WhenEbillingIsOff_Returns503WithTheDisabledCode_AndNeverReachesTheService(string method, string url)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method, url)));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.NoWrites();
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_WhenEbillingIsOff_StillDeniesACallerWithoutRights(string method, string url)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false, admin: false);

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method, url)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "the flag never replaces authorization");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The core table comes from the route; response shapes (ADR-019)
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("matters", "sprk_matters", "sprk_matter")]
    [InlineData("projects", "sprk_projects", "sprk_project")]
    [InlineData("workassignments", "sprk_workassignments", "sprk_workassignment")]
    public async Task Assign_Returns201_ChecksThatRecordsTable_AndHandsTheServiceTheRoutesTableAndTheCaller(string segment, string set, string table)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(set, RecordId, AccessRights.Read | AccessRights.Write | AccessRights.AppendTo);
        var view = View();
        string? seenTable = null;
        Guid? seenCaller = null;
        host.Service.Setup(s => s.AssignProcedureAsync(It.IsAny<string>(), RecordId, It.IsAny<ProcedureAssignmentRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((string t, Guid _, ProcedureAssignmentRequest _, Guid? c, CancellationToken _) => (seenTable, seenCaller) = (t, c))
            .ReturnsAsync(RuleSetOutcome<OverrideView>.Done(view));
        var url = $"/api/finance/{segment}/{RecordId}/procedure-assignments";

        var response = await host.Client.SendAsync(Req("POST", url, content: JsonContent.Create(new { procedureId = ProcedureId, mode = "Excluded" })));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().EndWith($"{url}/{view.Id}");
        seenTable.Should().Be(table);
        seenCaller.Should().Be(Guid.Parse(FinanceAuthzTestAuthHandler.CallerObjectId));
        host.Access.Calls.Should().Contain(c => c.Set == set && c.Id == RecordId);
    }

    [Fact]
    public async Task ADuplicate_Is409WithItsCode()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.ExcludeRuleAsync(It.IsAny<string>(), RecordId, It.IsAny<RuleExcludeRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<OverrideView>.Refused(409, "assignment.duplicate", "This record already has an override for this rule."));

        var response = await host.Client.SendAsync(Req("POST", M("/rule-overrides/exclude"), content: BodyFor("POST", M("/rule-overrides/exclude"))));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("assignment.duplicate");
    }

    [Fact]
    public async Task AnInvalidChange_Is400WithTheErrorCodeAndTheFieldsNamed()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.ChangeRuleAsync(It.IsAny<string>(), RecordId, It.IsAny<RuleChangeRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<OverrideView>.Invalid("assignment.invalid",
                new[] { new RuleSetError(null, null, "parameters.maxHoursPerDay", "Must be a number.", null) }));

        var response = await host.Client.SendAsync(Req("POST", M("/rule-overrides/change"), content: BodyFor("POST", M("/rule-overrides/change"))));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await Json(response);
        body["errorCode"]!.GetValue<string>().Should().Be("assignment.invalid");
        body["errors"]!.AsArray().Select(e => e!["field"]!.GetValue<string>()).Should().Equal("parameters.maxHoursPerDay");
    }

    [Fact]
    public async Task ThePreview_CarriesTheCallersToken_AndReturnsTheWarningTheTextAndTheCount()
    {
        await using var host = await Host.StartAsync();
        string? token = null;
        host.Service.Setup(s => s.PreviewRemoveAsync("sprk_matter", RecordId, AssignmentId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, Guid _, Guid _, string? t, CancellationToken _) => token = t)
            .ReturnsAsync(RuleSetOutcome<OverrideRemovalPreview>.Done(
                new OverrideRemovalPreview(AssignmentId, "RuleChanged", true, "warning", "2 open invoices you can see are affected.", 2)));

        var response = await host.Client.SendAsync(Req("GET", M($"/overrides/{AssignmentId}/removal-preview")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        token.Should().NotBeNullOrEmpty("the count is asked as the caller");
        (await Json(response))["affectedText"]!.GetValue<string>().Should().Be("2 open invoices you can see are affected.");
    }

    [Fact]
    public async Task AMissingBodyId_Is400_BeforeAnyServiceCall()
    {
        await using var host = await Host.StartAsync();

        var response = await host.Client.SendAsync(Req("POST", M("/procedure-assignments"), content: JsonContent.Create(new { mode = "Excluded" })));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.NoWrites();
    }
}
