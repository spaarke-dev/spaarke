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
/// The Procedure routes through the REAL <c>MapProcedureEndpoints</c>, step 0 (task 099), <see cref="FinanceAuthorizationFilter"/>,
/// <see cref="AuthorizationService"/> and <see cref="EbillingEnabledFilter"/> (ebilling-r1 task 081, EB-D-148, I-P5, ADR-008, ADR-019):
/// step 0 refuses a flagged-external, row-less or flag-fault caller on every route; every write route needs the caller's Write on THAT
/// Procedure and the billing-admin marker and fails closed on a probe fault; create needs Create on <c>sprk_procedure</c>. The service is
/// substituted at its own seam (its behaviour is <see cref="Services.Finance.Procedures.ProcedureServiceTests"/>).
/// </summary>
public sealed class ProcedureEndpointsTests
{
    private const string Base = "/api/finance/procedures";
    private static readonly Guid ProcedureId = Guid.NewGuid();
    private static readonly Guid VersionId = Guid.NewGuid();
    private const string ProcedureSet = "sprk_procedures";

    public static TheoryData<string, string> AllRoutes => new()
    {
        { "POST", Base },
        { "GET", $"{Base}/{ProcedureId}" },
        { "PATCH", $"{Base}/{ProcedureId}" },
        { "POST", $"{Base}/{ProcedureId}/versions" },
        { "PATCH", $"{Base}/{ProcedureId}/versions/{VersionId}" },
        { "POST", $"{Base}/{ProcedureId}/versions/{VersionId}/publish" },
        { "POST", $"{Base}/{ProcedureId}/versions/{VersionId}/retire" },
        { "GET", $"{Base}/{ProcedureId}/retire-preview" },
        { "POST", $"{Base}/{ProcedureId}/retire" },
    };

    /// <summary>Every route that changes a Procedure or reads its retire confirmation (the record Write + admin routes).</summary>
    public static TheoryData<string, string> WriteRoutes => new()
    {
        { "PATCH", $"{Base}/{ProcedureId}" },
        { "POST", $"{Base}/{ProcedureId}/versions" },
        { "PATCH", $"{Base}/{ProcedureId}/versions/{VersionId}" },
        { "POST", $"{Base}/{ProcedureId}/versions/{VersionId}/publish" },
        { "POST", $"{Base}/{ProcedureId}/versions/{VersionId}/retire" },
        { "GET", $"{Base}/{ProcedureId}/retire-preview" },
        { "POST", $"{Base}/{ProcedureId}/retire" },
    };

    private static readonly string[] AdminPrivileges =
    {
        BillingAdminAuthorization.CreateProcedurePrivilege, BillingAdminAuthorization.CreateRuleSetPrivilege,
        BillingAdminAuthorization.WriteRuleSetPrivilege, BillingAdminAuthorization.CreatePolicyVersionPrivilege,
        BillingAdminAuthorization.WritePolicyVersionPrivilege, BillingAdminAuthorization.WritePolicyPrivilege,
    };

    internal sealed class Host : IAsyncDisposable
    {
        public StatedCaller StepZero { get; private set; } = null!;
        public readonly Mock<ProcedureService> Service = new(
            MockBehavior.Strict,
            Mock.Of<IGenericEntityService>(),
            (RuleSetService)RuntimeHelpers.GetUninitializedObject(typeof(RuleSetService)),
            (SignalClosureService)RuntimeHelpers.GetUninitializedObject(typeof(SignalClosureService)),
            (AffectedInvoiceCounter)RuntimeHelpers.GetUninitializedObject(typeof(AffectedInvoiceCounter)),
            Mock.Of<IRecordOwnershipResolver>(),
            NullLogger<ProcedureService>.Instance);

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

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapProcedureEndpoints();
            await host._app.StartAsync();
            host.Client = host._app.GetTestClient();
            return host;
        }

        /// <summary>A billing admin with Read and Write on the Procedure.</summary>
        public void GrantAdmin()
        {
            foreach (var p in AdminPrivileges)
            {
                Probe.Hold(p);
            }

            Access.Grant(ProcedureSet, ProcedureId, AccessRights.Read | AccessRights.Write);
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

    private static HttpContent? BodyFor(string method) => method is "PATCH" or "PUT" or "POST" ? JsonContent.Create(new { }) : null;

    private static async Task<JsonObject> Json(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!.AsObject();

    private static ProcedureView View() => new(
        ProcedureId, "Acme OCG", null, "Guideline", "AllRecords", null, null, null, null, "Active", null, Guid.NewGuid(),
        Array.Empty<ProcedureVersionView>());

    // ═══════════════════════════════════════════════════════════════════════════
    // 401 / step 0 / 403 / disabled — every route
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_Unauthenticated_Returns401_AndNeverReachesTheService(string method, string url)
    {
        await using var host = await Host.StartAsync();

        var response = await host.Client.SendAsync(Req(method, url, authenticated: false, content: BodyFor(method)));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Service.VerifyNoOtherCalls();
    }

    public static IEnumerable<object[]> AllRoutesRefused => StatedCaller.Cross(AllRoutes, StatedCaller.RefusedKinds);

    public static IEnumerable<object[]> AllRoutesInternal => StatedCaller.Cross(AllRoutes, StatedCaller.InternalKinds);

    [Theory(DisplayName = "081 must-fire: every Procedure route refuses a flagged-external, row-less or flag-fault caller at step 0, before any check or service call")]
    [MemberData(nameof(AllRoutesRefused))]
    public async Task EveryRoute_StepZero_RefusesBeforeAnyCheckOrServiceCall(string method, string url, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method)));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        host.Probe.Calls.Should().BeEmpty();
        host.Access.Calls.Should().BeEmpty();
        host.Service.VerifyNoOtherCalls();
    }

    [Theory(DisplayName = "081 must-not-fire: a flag that reads false or blank reaches the route's own check")]
    [MemberData(nameof(AllRoutesInternal))]
    public async Task EveryRoute_StepZero_AdmitsAnInternalCaller_ToTheRoutesOwnCheck(string method, string url, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync(admin: false);
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (host.Probe.Calls.Count + host.Access.Calls.Count).Should().BePositive("the route asked Dataverse, as the caller");
        host.Service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task EveryWriteRoute_WithoutTheBillingAdminMarker_Returns403(string method, string url)
    {
        await using var host = await Host.StartAsync();
        host.Probe.Release(BillingAdminAuthorization.WriteRuleSetPrivilege);

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task EveryWriteRoute_WithoutWriteOnThatProcedure_Returns403_EvenForABillingAdmin(string method, string url)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(ProcedureSet, ProcedureId, AccessRights.Read);

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().Contain(c => c.Set == ProcedureSet && c.Id == ProcedureId && c.HasToken, "asked as the caller, about THAT Procedure");
        host.Service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task EveryWriteRoute_WhenTheRecordProbeFaults_Returns403(string method, string url)
    {
        await using var host = await Host.StartAsync();
        host.Access.ThrowOnEveryCall = new HttpRequestException("simulated Dataverse fault");

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "fail closed");
        host.Service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task EveryWriteRoute_WhenThePrivilegeProbeFaults_Returns403(string method, string url)
    {
        await using var host = await Host.StartAsync();
        host.Probe.ThrowOnEveryCall = new HttpRequestException("simulated Dataverse fault");

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "fail closed");
        host.Service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("POST", "", BillingAdminAuthorization.CreateProcedurePrivilege)]
    [InlineData("POST", "", BillingAdminAuthorization.CreateRuleSetPrivilege)]
    [InlineData("POST", "", BillingAdminAuthorization.WriteRuleSetPrivilege)]
    [InlineData("POST", "/{p}/versions", BillingAdminAuthorization.CreatePolicyVersionPrivilege)]
    [InlineData("POST", "/{p}/versions/{v}/publish", BillingAdminAuthorization.WritePolicyVersionPrivilege)]
    [InlineData("POST", "/{p}/versions/{v}/publish", BillingAdminAuthorization.WritePolicyPrivilege)]
    [InlineData("POST", "/{p}/retire", BillingAdminAuthorization.WritePolicyPrivilege)]
    public async Task EachRoute_NeedsThePrivilegeOfEveryTableItWrites(string method, string path, string missing)
    {
        await using var host = await Host.StartAsync();
        host.Probe.Release(missing);

        var response = await host.Client.SendAsync(Req(method,
            Base + path.Replace("{p}", ProcedureId.ToString()).Replace("{v}", VersionId.ToString()), content: BodyFor(method)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Read_NeedsTheCallersReadOnTheProcedure_NotTheAdminMarker()
    {
        await using var host = await Host.StartAsync(admin: false);
        host.Service.Setup(s => s.GetAsync(ProcedureId, It.IsAny<CancellationToken>())).ReturnsAsync(RuleSetOutcome<ProcedureView>.Done(View()));

        var denied = await host.Client.SendAsync(Req("GET", $"{Base}/{ProcedureId}"));
        host.Access.Grant(ProcedureSet, ProcedureId, AccessRights.Read);
        var allowed = await host.Client.SendAsync(Req("GET", $"{Base}/{ProcedureId}"));

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void ThePrivilegeName_IsTheDataverseOne()
    {
        BillingAdminAuthorization.CreateProcedurePrivilege.Should().Be("prvCreatesprk_procedure");
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_WhenEbillingIsOff_Returns503WithTheDisabledCode_AndNeverReachesTheService(string method, string url)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method)));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.Service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_WhenEbillingIsOff_StillDeniesANonAdmin(string method, string url)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false, admin: false);

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "the flag never replaces authorization");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Response shapes (ADR-019)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Create_Returns201WithTheLocation_AndHandsTheCallerToTheService()
    {
        await using var host = await Host.StartAsync();
        Guid? caller = null;
        host.Service.Setup(s => s.CreateAsync(It.IsAny<ProcedureCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((ProcedureCreateRequest _, Guid? c, CancellationToken _) => caller = c)
            .ReturnsAsync(RuleSetOutcome<ProcedureView>.Done(View()));

        var response = await host.Client.SendAsync(Req("POST", Base, content: JsonContent.Create(new { name = "Acme OCG", type = "Guideline" })));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().EndWith($"{Base}/{ProcedureId}");
        caller.Should().Be(Guid.Parse(FinanceAuthzTestAuthHandler.CallerObjectId));
    }

    [Fact]
    public async Task AnInvalidRequest_Is400WithTheErrorCodeAndTheFieldsNamed()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.CreateAsync(It.IsAny<ProcedureCreateRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<ProcedureView>.Invalid(ProcedureService.CodeFirmRequired,
                new[] { new RuleSetError(null, null, "firmId", "A Rate card names the firm it applies to.", null) }));

        var response = await host.Client.SendAsync(Req("POST", Base, content: JsonContent.Create(new { name = "X", type = "RateCard" })));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await Json(response);
        body["errorCode"]!.GetValue<string>().Should().Be("procedure.firm_required");
        body["errors"]!.AsArray().Select(e => e!["field"]!.GetValue<string>()).Should().Equal("firmId");
    }

    [Fact]
    public async Task AScopeLock_Is409WithItsCode()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.UpdateAsync(ProcedureId, It.IsAny<ProcedureUpdateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<ProcedureView>.Refused(409, ProcedureService.CodeScopeLocked, ProcedureService.ScopeLockedDetail));

        var response = await host.Client.SendAsync(Req("PATCH", $"{Base}/{ProcedureId}", content: JsonContent.Create(new { appliesTo = "Firm" })));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await Json(response);
        body["errorCode"]!.GetValue<string>().Should().Be("procedure.scope_locked");
        body["detail"]!.GetValue<string>().Should().Be("Create a new Procedure to change who it governs.");
    }

    [Fact]
    public async Task ThePreview_CarriesTheCallersToken_AndReturnsOnlyTheWarningTheTextAndTheCount()
    {
        await using var host = await Host.StartAsync();
        string? token = null;
        host.Service.Setup(s => s.PreviewRetireAsync(ProcedureId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, string? t, CancellationToken _) => token = t)
            .ReturnsAsync(RuleSetOutcome<ProcedureRetirePreview>.Done(new ProcedureRetirePreview("warning", "2 open invoices you can see are affected.", 2)));

        var response = await host.Client.SendAsync(Req("GET", $"{Base}/{ProcedureId}/retire-preview"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        token.Should().NotBeNullOrEmpty("the count is asked as the caller");
        (await Json(response)).Select(p => p.Key).Should().BeEquivalentTo("warning", "affectedText", "visibleOpenInvoices");
    }
}
