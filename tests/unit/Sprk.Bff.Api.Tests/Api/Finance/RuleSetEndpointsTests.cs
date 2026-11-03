using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// The rule set routes through the REAL <c>MapRuleSetEndpoints</c>, <see cref="FinanceAuthorizationFilter"/>, <see cref="AuthorizationService"/>
/// and <see cref="EbillingEnabledFilter"/> (ebilling-r1 task 076, FR-28, D-76, ADR-008, ADR-019): 401 unauthenticated, 403 for a caller who
/// is not a billing admin (including an Ontology administrator who holds every policy privilege), the stable disabled code with the flag
/// off, and the HTTP shape of each outcome. The service is substituted at its own seam (its behaviour is <see cref="Services.Finance.RuleSetServiceTests"/>).
/// </summary>
public sealed class RuleSetEndpointsTests
{
    private const string Base = "/api/finance/rulesets";
    private static readonly Guid SetId = Guid.NewGuid();
    private static readonly Guid PolicyId = Guid.NewGuid();
    private static readonly Guid FirmId = Guid.NewGuid();

    public static TheoryData<string, string> AllRoutes => new()
    {
        { "POST", $"{Base}?name=OCG&sourceKind=Guideline" },
        { "GET", Base },
        { "GET", $"{Base}/{SetId}" },
        { "PATCH", $"{Base}/{SetId}/proposals/{PolicyId}" },
        { "POST", $"{Base}/{SetId}/proposals/{PolicyId}/accept" },
        { "POST", $"{Base}/{SetId}/proposals/{PolicyId}/reject" },
        { "POST", $"{Base}/{SetId}/publish" },
        { "PUT", $"{Base}/{SetId}/assignment" },
        { "PUT", $"{Base}/{SetId}/visibility" },
        { "POST", $"{Base}/{SetId}/draft" },
    };

    internal sealed class Host : IAsyncDisposable
    {
        /// <summary>The step-0 caller (task 099); internal unless a test changes <see cref="StatedCaller.Kind"/>.</summary>
        public StatedCaller StepZero { get; private set; } = null!;

        public readonly Mock<RuleSetService> Service;
        public readonly Mock<JobSubmissionService> Jobs;
        public readonly FinAuth.RecordingAccessDataSource Access = new();
        public readonly FinAuth.RecordingPrivilegeProbe Probe = new();
        private WebApplication _app = null!;
        public HttpClient Client = null!;

        public Host()
        {
            var gcf = Mock.Of<IGraphClientFactory>();
            var spe = new SpeFileStore(
                new ContainerOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<ContainerOperations>>()),
                new DriveItemOperations(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<ILogger<DriveItemOperations>>()),
                new UploadSessionManager(gcf, TestSpeOwnership.AllowAll(gcf), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
                new UserOperations(gcf, Mock.Of<ILogger<UserOperations>>()));
            var entities = Mock.Of<IGenericEntityService>();
            var schema = new RuleBodySchemaValidator();
            var validator = new PolicyVersionValidator(schema, new PredicateCompiler(schema, TimeProvider.System), NullLogger<PolicyVersionValidator>.Instance);
            Service = new Mock<RuleSetService>(
                MockBehavior.Strict,
                entities, Mock.Of<IDocumentDataverseService>(), spe, Mock.Of<IRecordOwnershipResolver>(), validator,
                new RuleBodyDescriber(validator, entities, NullLogger<RuleBodyDescriber>.Instance),
                Mock.Of<IOptionsMonitor<FinanceOptions>>(), Mock.Of<Spaarke.Scheduling.IScheduledJobLease>(), Mock.Of<ILogger<RuleSetService>>());
            // uac-r2 D: the proposal routes read the set's Procedure; a legacy set (none) unless a test says otherwise.
            Service.Setup(s => s.ProcedureOfRuleSetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);
            Jobs = new Mock<JobSubmissionService>(
                MockBehavior.Strict,
                Microsoft.Extensions.Options.Options.Create(new ServiceBusOptions
                {
                    QueueName = "test-jobs",
                    CommunicationQueueName = "test-comms",
                    ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
                }),
                Mock.Of<ILogger<JobSubmissionService>>(),
                new Mock<ServiceBusClient>().Object);
        }

        public static async Task<Host> StartAsync(bool ebillingEnabled = true, int maxMb = 25, bool admin = true)
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
            host.StepZero = StatedCaller.Register(builder.Services); // task 099: every /api/finance route runs step 0
            builder.Services.AddOptions<EbillingOptions>().Configure(o =>
            {
                o.Enabled = ebillingEnabled;
                o.MaxSubmissionFileSizeMb = maxMb;
            });
            builder.Services.AddSingleton<IAccessDataSource>(host.Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();
            builder.Services.AddSingleton<CallerRecordAccessProbe>(host.Probe);
            builder.Services.AddSingleton(host.Service.Object);
            builder.Services.AddSingleton(host.Jobs.Object);
            if (admin)
            {
                host.GrantAdmin();
            }

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapRuleSetEndpoints();
            await host._app.StartAsync();
            host.Client = host._app.GetTestClient();
            return host;
        }

        /// <summary>No service operation ran: the only call allowed is the proposal routes' authorization lookup of the set's Procedure
        /// (uac-r2 D), which is not a service operation.</summary>
        public void VerifyOnlyTheProcedureLookup()
        {
            Service.Verify(s => s.ProcedureOfRuleSetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce()); // one per request
            Service.VerifyNoOtherCalls();
        }

        public void GrantAdmin()
        {
            foreach (var p in new[]
            {
                BillingAdminAuthorization.CreateRuleSetPrivilege, BillingAdminAuthorization.WriteRuleSetPrivilege,
                BillingAdminAuthorization.CreatePolicyVersionPrivilege, BillingAdminAuthorization.WritePolicyVersionPrivilege,
                BillingAdminAuthorization.WritePolicyPrivilege, BillingAdminAuthorization.CreatePolicyPrivilege,
            })
            {
                Probe.Hold(p);
            }
        }

        /// <summary>An Ontology administrator: every policy privilege, nothing on rule sets.</summary>
        public void BecomeOntologyAdminOnly()
        {
            foreach (var p in new[]
            {
                BillingAdminAuthorization.CreateRuleSetPrivilege, BillingAdminAuthorization.WriteRuleSetPrivilege,
            })
            {
                Probe.Release(p);
            }
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

    private static MultipartFormDataContent Upload(byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", "ocg.pdf");
        return content;
    }

    private static HttpContent? BodyFor(string method) => method is "PATCH" or "PUT" or "POST" ? JsonContent.Create(new { }) : null;

    private static async Task<JsonObject> Json(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!.AsObject();

    private static ProposalView Proposal(string state = "Accepted") => new(
        PolicyId, "BILL-A", "Rule BILL-A", state, false, false, Guid.NewGuid(), 1, "DailyHoursCap", JsonNode.Parse("""{"type":"DailyHoursCap","maxHoursPerDay":10}"""),
        "tpl", "headline", "Flag", "at most 10 hours per day", null, null, "No timekeeper may bill more than 10 hours.", "Section 4.2", Array.Empty<string>());

    private static RuleSetSummary Summary(bool visible = true) =>
        new(SetId, "Acme OCG", "Draft", "ClientDefault", null, visible, null, null, 3);

    // ═══════════════════════════════════════════════════════════════════════════
    // 401 / 403 / disabled — every route
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_Unauthenticated_Returns401_AndNeverReachesTheService(string method, string url)
    {
        await using var host = await Host.StartAsync();

        var response = await host.Client.SendAsync(Req(method, url, authenticated: false));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_ANonAdmin_Returns403_AndNeverReachesTheService(string method, string url)
    {
        await using var host = await Host.StartAsync(admin: false);

        var response = await host.Client.SendAsync(Req(method, url, content: method == "POST" && url.Contains("sourceKind") ? Upload(new byte[] { 1 }) : BodyFor(method)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.InsufficientPrivilegeReasonCode);
        host.Probe.Calls.Should().NotBeEmpty("the route asked Dataverse, as the caller, what they hold");
        host.Service.VerifyNoOtherCalls();
    }

    public static IEnumerable<object[]> AllRoutesRefused => StatedCaller.Cross(AllRoutes, StatedCaller.RefusedKinds);

    public static IEnumerable<object[]> AllRoutesInternal => StatedCaller.Cross(AllRoutes, StatedCaller.InternalKinds);

    [Theory(DisplayName = "Task 099 must-fire: every /api/finance/rulesets route refuses a flagged-external, row-less or unreadable billing admin with 403, before any privilege question or service call")]
    [MemberData(nameof(AllRoutesRefused))]
    public async Task EveryRoute_StepZero_RefusesBeforeAnyCheckOrServiceCall(string method, string url, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Req(method, url, content: method == "POST" && url.Contains("sourceKind") ? Upload(new byte[] { 1 }) : BodyFor(method)));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        host.Probe.Calls.Should().BeEmpty();
        host.Service.VerifyNoOtherCalls();
    }

    [Theory(DisplayName = "Task 099 must-not-fire: on every /api/finance/rulesets route a flag that reads false or blank reaches the privilege check as before")]
    [MemberData(nameof(AllRoutesInternal))]
    public async Task EveryRoute_StepZero_AdmitsAnInternalCaller(string method, string url, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync(admin: false);
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Req(method, url, content: method == "POST" && url.Contains("sourceKind") ? Upload(new byte[] { 1 }) : BodyFor(method)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.InsufficientPrivilegeReasonCode);
        host.Probe.Calls.Should().NotBeEmpty();
        host.Service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_AnOntologyAdministrator_HoldingEveryPolicyPrivilegeButNoRuleSetWrite_Returns403(string method, string url)
    {
        await using var host = await Host.StartAsync();
        host.BecomeOntologyAdminOnly();

        var response = await host.Client.SendAsync(Req(method, url, content: method == "POST" && url.Contains("sourceKind") ? Upload(new byte[] { 1 }) : BodyFor(method)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "policy privileges alone must not make an Ontology administrator a billing admin");
        host.Service.VerifyNoOtherCalls();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // uac-r2 review item D (2026-10-22, I-P5): a Procedure's rule set also needs Write on that Procedure
    // ═══════════════════════════════════════════════════════════════════════════

    public static TheoryData<string, string> ProposalRoutes => new()
    {
        { "PATCH", $"{Base}/{SetId}/proposals/{PolicyId}" },
        { "POST", $"{Base}/{SetId}/proposals/{PolicyId}/accept" },
        { "POST", $"{Base}/{SetId}/proposals/{PolicyId}/reject" },
    };

    private static HttpContent ProposalBody(string url) =>
        url.EndsWith("/accept", StringComparison.Ordinal) ? JsonContent.Create(new { rowVersion = "4711" })
        : url.EndsWith("/reject", StringComparison.Ordinal) ? JsonContent.Create(new { rowVersion = "4711", reason = "Wrong limit." })
        : JsonContent.Create(new { headline = "x" });

    /// <summary>The service answers 404 to any edit or decision, so a test can tell "reached the service" from "refused before it".</summary>
    private static void ServiceAnswers404(Host host)
    {
        host.Service.Setup(s => s.EditAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ProposalEdit>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<ProposalEdited>.Refused(404, RuleSetService.CodeProposalNotFound, "no such rule"));
        host.Service.Setup(s => s.DecideAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<long>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(RuleSetOutcome<ProposalView>.Refused(404, RuleSetService.CodeProposalNotFound, "no such rule"));
    }

    [Theory(DisplayName = "081 uac D must-fire: on a Procedure's rule set, a billing admin without Write on that Procedure gets 403 and never reaches the service")]
    [MemberData(nameof(ProposalRoutes))]
    public async Task ProposalRoute_OnAProceduresSet_WithoutWriteOnTheProcedure_Is403(string method, string url)
    {
        await using var host = await Host.StartAsync();
        var procedureId = Guid.NewGuid();
        host.Service.Setup(s => s.ProcedureOfRuleSetAsync(SetId, It.IsAny<CancellationToken>())).ReturnsAsync(procedureId);
        host.Access.Grant("sprk_procedures", procedureId, AccessRights.Read); // Read only

        var response = await host.Client.SendAsync(Req(method, url, content: ProposalBody(url)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await Json(response);
        problem["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.InsufficientRightsReasonCode);
        problem["detail"]!.GetValue<string>().Should().Be("You need Write on this rule set's Procedure.", "round 3 F1: the 403 names the missing right");
        host.Access.Calls.Should().Contain(c => c.Path == FinAuth.AccessPath.Record && c.Set == "sprk_procedures" && c.Id == procedureId && c.HasToken,
            "the Procedure's rights are asked of Dataverse as the caller");
        host.Service.Verify(s => s.EditAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ProposalEdit>(), It.IsAny<CancellationToken>()), Times.Never);
        host.Service.Verify(s => s.DecideAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<long>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory(DisplayName = "081 uac D: on a Procedure's rule set, Write on that Procedure lets the edit or decision through")]
    [MemberData(nameof(ProposalRoutes))]
    public async Task ProposalRoute_OnAProceduresSet_WithWriteOnTheProcedure_ReachesTheService(string method, string url)
    {
        await using var host = await Host.StartAsync();
        var procedureId = Guid.NewGuid();
        host.Service.Setup(s => s.ProcedureOfRuleSetAsync(SetId, It.IsAny<CancellationToken>())).ReturnsAsync(procedureId);
        host.Access.Grant("sprk_procedures", procedureId, AccessRights.Read | AccessRights.Write);
        ServiceAnswers404(host);

        var response = await host.Client.SendAsync(Req(method, url, content: ProposalBody(url)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "the request reached the service, which answered");
    }

    [Theory(DisplayName = "081 uac D must-not-fire: a rule set without a Procedure is unchanged (no Procedure check is asked)")]
    [MemberData(nameof(ProposalRoutes))]
    public async Task ProposalRoute_OnALegacySet_AsksNoProcedureCheck(string method, string url)
    {
        await using var host = await Host.StartAsync();
        ServiceAnswers404(host);

        var response = await host.Client.SendAsync(Req(method, url, content: ProposalBody(url)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "the request reached the service, as before");
        host.Access.Calls.Should().NotContain(c => c.Set == "sprk_procedures");
    }

    [Theory(DisplayName = "081 uac D: a fault reading the rule set's Procedure denies (fail closed) and never reaches the service")]
    [MemberData(nameof(ProposalRoutes))]
    public async Task ProposalRoute_WhenTheSetsProcedureCannotBeRead_Is403(string method, string url)
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.ProcedureOfRuleSetAsync(SetId, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("simulated read fault"));

        var response = await host.Client.SendAsync(Req(method, url, content: ProposalBody(url)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        host.Service.Verify(s => s.EditAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ProposalEdit>(), It.IsAny<CancellationToken>()), Times.Never);
        host.Service.Verify(s => s.DecideAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<long>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // uac-r2 re-check round 2 (2026-10-22, I-P5): /draft needs Write on the set's Procedure; the reads need Read on it
    // ═══════════════════════════════════════════════════════════════════════════

    private static void SetBelongsToProcedure(Host host, Guid procedureId) =>
        host.Service.Setup(s => s.ProcedureOfRuleSetAsync(SetId, It.IsAny<CancellationToken>())).ReturnsAsync(procedureId);

    [Fact(DisplayName = "081 uac r2 I-P5 must-fire: /draft on a Procedure's rule set without Write on that Procedure is 403 and queues nothing")]
    public async Task Draft_OnAProceduresSet_WithoutWriteOnTheProcedure_Is403_AndNothingIsQueued()
    {
        await using var host = await Host.StartAsync();
        var procedureId = Guid.NewGuid();
        SetBelongsToProcedure(host, procedureId);
        host.Access.Grant("sprk_procedures", procedureId, AccessRights.Read); // Read only

        var response = await host.Client.SendAsync(Req("POST", $"{Base}/{SetId}/draft", content: JsonContent.Create(new { })));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.InsufficientRightsReasonCode);
        host.Service.Verify(s => s.GetDraftSourceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        host.Jobs.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "081 uac r2 I-P5 must-not-fire: /draft on a rule set with no Procedure is unchanged (no Procedure check is asked)")]
    public async Task Draft_OnALegacySet_AsksNoProcedureCheck_AndReachesTheService()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.GetDraftSourceAsync(SetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Refused(404, RuleSetService.CodeNotFound, "no such rule set"));

        var response = await host.Client.SendAsync(Req("POST", $"{Base}/{SetId}/draft", content: JsonContent.Create(new { })));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "the request reached the service, as before");
        host.Access.Calls.Should().NotContain(c => c.Set == "sprk_procedures");
    }

    [Fact(DisplayName = "081 uac r2 I-P5: /draft on a Procedure's rule set with Write on that Procedure reaches the service")]
    public async Task Draft_OnAProceduresSet_WithWriteOnTheProcedure_ReachesTheService()
    {
        await using var host = await Host.StartAsync();
        var procedureId = Guid.NewGuid();
        SetBelongsToProcedure(host, procedureId);
        host.Access.Grant("sprk_procedures", procedureId, AccessRights.Read | AccessRights.Write);
        host.Service.Setup(s => s.GetDraftSourceAsync(SetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Refused(422, RuleSetService.CodeSourceMissing, "no source"));

        var response = await host.Client.SendAsync(Req("POST", $"{Base}/{SetId}/draft", content: JsonContent.Create(new { })));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "the request reached the service, which answered");
    }

    [Theory(DisplayName = "081 uac r2 I-P5: an UNKNOWN rights answer on the Procedure is system_failure, not insufficient_rights, on the write-gated routes too")]
    [MemberData(nameof(ProposalRoutes))]
    public async Task ProposalRoute_WhenTheProceduresRightsAreUnknown_IsSystemFailure(string method, string url)
    {
        await using var host = await Host.StartAsync();
        var procedureId = Guid.NewGuid();
        SetBelongsToProcedure(host, procedureId);
        host.Access.Fault("sprk_procedures", procedureId);

        var response = await host.Client.SendAsync(Req(method, url, content: ProposalBody(url)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        host.Service.Verify(s => s.EditAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ProposalEdit>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static void ServiceReadsTheSet(Host host) =>
        host.Service.Setup(s => s.GetAsync(SetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<RuleSetDetail>.Done(new RuleSetDetail(Summary(), Guid.NewGuid(), null, new[] { Proposal() })));

    [Fact(DisplayName = "081 uac r2 I-P5 must-fire: GET of a Procedure's rule set without Read on that Procedure is 403 insufficient_rights and never reads the set")]
    public async Task Get_OnAProceduresSet_WithoutReadOnTheProcedure_Is403_AndNeverReadsTheSet()
    {
        await using var host = await Host.StartAsync();
        var procedureId = Guid.NewGuid();
        SetBelongsToProcedure(host, procedureId);
        host.Access.Grant("sprk_procedures", procedureId, AccessRights.None); // Dataverse answered: no Read
        ServiceReadsTheSet(host);

        var response = await host.Client.SendAsync(Req("GET", $"{Base}/{SetId}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await Json(response);
        problem["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.InsufficientRightsReasonCode);
        problem["detail"]!.GetValue<string>().Should().Be("You need Read on this rule set's Procedure.", "round 3 F1: the 403 names the missing right");
        host.Access.Calls.Should().Contain(c => c.Path == FinAuth.AccessPath.Record && c.Set == "sprk_procedures" && c.Id == procedureId && c.HasToken);
        host.Service.Verify(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "081 uac r2 I-P5 must-fire: GET of a Procedure's rule set whose Procedure rights are UNKNOWN is 403 system_failure")]
    public async Task Get_OnAProceduresSet_WhenTheProceduresRightsAreUnknown_IsSystemFailure()
    {
        await using var host = await Host.StartAsync();
        var procedureId = Guid.NewGuid();
        SetBelongsToProcedure(host, procedureId);
        host.Access.Fault("sprk_procedures", procedureId);
        ServiceReadsTheSet(host);

        var response = await host.Client.SendAsync(Req("GET", $"{Base}/{SetId}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        host.Service.Verify(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "081 uac r2 I-P5 must-fire: GET whose rule set cannot be read for its Procedure is 403 system_failure")]
    public async Task Get_WhenTheSetsProcedureCannotBeRead_IsSystemFailure()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.ProcedureOfRuleSetAsync(SetId, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("simulated read fault"));
        ServiceReadsTheSet(host);

        var response = await host.Client.SendAsync(Req("GET", $"{Base}/{SetId}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        host.Service.Verify(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "081 uac r2 I-P5: GET of a Procedure's rule set with Read on that Procedure returns it")]
    public async Task Get_OnAProceduresSet_WithReadOnTheProcedure_Returns200()
    {
        await using var host = await Host.StartAsync();
        var procedureId = Guid.NewGuid();
        SetBelongsToProcedure(host, procedureId);
        host.Access.Grant("sprk_procedures", procedureId, AccessRights.Read);
        ServiceReadsTheSet(host);

        var response = await host.Client.SendAsync(Req("GET", $"{Base}/{SetId}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "081 uac r2 I-P5 must-not-fire: GET of a rule set with no Procedure is unchanged (no Procedure check is asked)")]
    public async Task Get_OnALegacySet_AsksNoProcedureCheck()
    {
        await using var host = await Host.StartAsync();
        ServiceReadsTheSet(host);

        var response = await host.Client.SendAsync(Req("GET", $"{Base}/{SetId}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Access.Calls.Should().NotContain(c => c.Set == "sprk_procedures");
    }

    private static RuleSetSummary SummaryOf(Guid id, Guid? procedureId) =>
        new RuleSetSummary(id, "Set " + id.ToString()[..4], "Draft", "ClientDefault", null, true, null, null, 1) { ProcedureId = procedureId };

    [Fact(DisplayName = "081 uac r2 I-P5 must-not-fire (round 3 F2): a deny-only list is 200 without the denied set; legacy sets stay; one question per DISTINCT Procedure")]
    public async Task List_LeavesOutProcedureSetsTheCallerCannotRead_AndAsksOncePerDistinctProcedure()
    {
        await using var host = await Host.StartAsync();
        var legacy = SummaryOf(Guid.NewGuid(), null);
        var readableProcedure = Guid.NewGuid();
        var deniedProcedure = Guid.NewGuid();
        var readable = SummaryOf(Guid.NewGuid(), readableProcedure);
        var readableToo = SummaryOf(Guid.NewGuid(), readableProcedure); // the SAME Procedure as the set above
        var denied = SummaryOf(Guid.NewGuid(), deniedProcedure);
        host.Access.Grant("sprk_procedures", readableProcedure, AccessRights.Read);
        host.Access.Grant("sprk_procedures", deniedProcedure, AccessRights.None);
        host.Service.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Done(new[] { legacy, readable, readableToo, denied }));

        var response = await host.Client.SendAsync(Req("GET", Base));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ids = (await response.Content.ReadFromJsonAsync<JsonArray>())!.Select(n => n!["id"]!.GetValue<Guid>()).ToList();
        ids.Should().BeEquivalentTo(new[] { legacy.Id, readable.Id, readableToo.Id });
        var distinctProcedures = new[] { readable, readableToo, denied }.Select(r => r.ProcedureId).Distinct().Count();
        distinctProcedures.Should().Be(2);
        host.Access.Calls.Count(c => c.Set == "sprk_procedures").Should().Be(distinctProcedures, "one rights question per distinct Procedure");
    }

    [Fact(DisplayName = "081 uac r2 round 3 F2 must-fire: ONE unknown rights answer makes the list 403 system_failure, with no list")]
    public async Task List_WhenAnyProcedureRightsAreUnknown_IsSystemFailure_AndReturnsNoList()
    {
        await using var host = await Host.StartAsync();
        var readableProcedure = Guid.NewGuid();
        var unknownProcedure = Guid.NewGuid();
        host.Access.Grant("sprk_procedures", readableProcedure, AccessRights.Read);
        host.Access.Fault("sprk_procedures", unknownProcedure);
        host.Service.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Done(new[]
            {
                SummaryOf(Guid.NewGuid(), null), SummaryOf(Guid.NewGuid(), readableProcedure), SummaryOf(Guid.NewGuid(), unknownProcedure),
            }));

        var response = await host.Client.SendAsync(Req("GET", Base));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await Json(response);
        problem["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        problem.ContainsKey("id").Should().BeFalse();
    }

    private static RuleSetSummary[] SetsOnDistinctProcedures(Host host, int count)
    {
        var sets = new RuleSetSummary[count];
        for (var i = 0; i < count; i++)
        {
            var procedureId = Guid.NewGuid();
            host.Access.Grant("sprk_procedures", procedureId, AccessRights.Read);
            sets[i] = SummaryOf(Guid.NewGuid(), procedureId);
        }

        return sets;
    }

    [Fact(DisplayName = "081 uac r2 round 3 K2 must-fire: more distinct Procedures than the cap is 403 system_failure and asks no more than the cap")]
    public async Task List_WithMoreDistinctProceduresThanTheCap_IsSystemFailure_AndAsksNoMoreThanTheCap()
    {
        await using var host = await Host.StartAsync();
        var sets = SetsOnDistinctProcedures(host, RuleSetEndpoints.MaxProcedureRightsChecks + 1);
        host.Service.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Done(sets));

        var response = await host.Client.SendAsync(Req("GET", Base));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        host.Access.Calls.Count(c => c.Set == "sprk_procedures").Should().Be(0, "the cap is checked before any rights question");
    }

    [Fact(DisplayName = "081 uac r2 round 3 K2 must-not-fire: exactly the cap of distinct Procedures is answered in full")]
    public async Task List_WithExactlyTheCapOfDistinctProcedures_IsAnsweredInFull()
    {
        await using var host = await Host.StartAsync();
        var sets = SetsOnDistinctProcedures(host, RuleSetEndpoints.MaxProcedureRightsChecks);
        host.Service.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Done(sets));

        var response = await host.Client.SendAsync(Req("GET", Base));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonArray>())!.Should().HaveCount(RuleSetEndpoints.MaxProcedureRightsChecks);
        host.Access.Calls.Count(c => c.Set == "sprk_procedures").Should().Be(RuleSetEndpoints.MaxProcedureRightsChecks);
    }

    [Fact(DisplayName = "081 uac r2 I-P5 must-not-fire: a list of only legacy sets is returned whole and asks no Procedure question")]
    public async Task List_OfLegacySetsOnly_IsUnchanged_AndAsksNoProcedureCheck()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Done(new[] { SummaryOf(Guid.NewGuid(), null), SummaryOf(Guid.NewGuid(), null) }));

        var response = await host.Client.SendAsync(Req("GET", Base));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonArray>())!.Should().HaveCount(2);
        host.Access.Calls.Should().NotContain(c => c.Set == "sprk_procedures");
    }

    [Fact(DisplayName = "081 uac r2 I-P5: the Procedure id is internal; the list response never carries it")]
    public async Task List_DoesNotSerializeTheProcedureId()
    {
        await using var host = await Host.StartAsync();
        var procedureId = Guid.NewGuid();
        host.Access.Grant("sprk_procedures", procedureId, AccessRights.Read);
        host.Service.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Done(new[] { SummaryOf(Guid.NewGuid(), procedureId) }));

        var response = await host.Client.SendAsync(Req("GET", Base));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = (await response.Content.ReadFromJsonAsync<JsonArray>())!;
        list.Should().HaveCount(1);
        list[0]!.AsObject().Select(kv => kv.Key).Should().NotContain(k => k.Equals("procedureId", StringComparison.OrdinalIgnoreCase));
        (await response.Content.ReadAsStringAsync()).Should().NotContainEquivalentOf("procedureId");
    }

    [Theory]
    [InlineData("POST", $"{Base}?name=OCG&sourceKind=Guideline", BillingAdminAuthorization.CreateRuleSetPrivilege)]
    [InlineData("POST", $"{Base}?name=OCG&sourceKind=Guideline", BillingAdminAuthorization.WriteRuleSetPrivilege)]
    [InlineData("PATCH", "{set}/proposals/{p}", BillingAdminAuthorization.CreatePolicyVersionPrivilege)]
    [InlineData("PATCH", "{set}/proposals/{p}", BillingAdminAuthorization.WritePolicyVersionPrivilege)]
    [InlineData("PATCH", "{set}/proposals/{p}", BillingAdminAuthorization.WriteRuleSetPrivilege)]
    [InlineData("POST", "{set}/proposals/{p}/accept", BillingAdminAuthorization.WritePolicyVersionPrivilege)]
    [InlineData("POST", "{set}/proposals/{p}/reject", BillingAdminAuthorization.WritePolicyVersionPrivilege)]
    [InlineData("POST", "{set}/publish", BillingAdminAuthorization.WritePolicyVersionPrivilege)]
    [InlineData("POST", "{set}/publish", BillingAdminAuthorization.WritePolicyPrivilege)]
    [InlineData("POST", "{set}/publish", BillingAdminAuthorization.WriteRuleSetPrivilege)]
    [InlineData("POST", "{set}/draft", BillingAdminAuthorization.WriteRuleSetPrivilege)]
    [InlineData("POST", "{set}/draft", BillingAdminAuthorization.CreatePolicyPrivilege)]
    [InlineData("POST", "{set}/draft", BillingAdminAuthorization.CreatePolicyVersionPrivilege)]
    [InlineData("GET", "{set}", BillingAdminAuthorization.WriteRuleSetPrivilege)]
    [InlineData("GET", "", BillingAdminAuthorization.WriteRuleSetPrivilege)]
    [InlineData("PUT", "{set}/assignment", BillingAdminAuthorization.WriteRuleSetPrivilege)]
    [InlineData("PUT", "{set}/visibility", BillingAdminAuthorization.WriteRuleSetPrivilege)]
    public async Task EachRoute_NeedsEveryPrivilegeOfTheTablesItWrites(string method, string path, string missing)
    {
        await using var host = await Host.StartAsync();
        host.Probe.Release(missing);
        var url = path.StartsWith(Base, StringComparison.Ordinal)
            ? path
            : Base + (path.Length == 0 ? "" : "/" + path.Replace("{set}", SetId.ToString()).Replace("{p}", PolicyId.ToString()));

        var response = await host.Client.SendAsync(Req(method, url, content: method == "POST" && url.Contains("sourceKind") ? Upload(new byte[] { 1 }) : BodyFor(method)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_WhenEbillingIsOff_Returns503WithTheDisabledCode_AndNeverReachesTheService(string method, string url)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);

        var response = await host.Client.SendAsync(Req(method, url, content: method == "POST" && url.Contains("sourceKind") ? Upload(new byte[] { 1 }) : BodyFor(method)));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.Service.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_WhenEbillingIsOff_StillDeniesANonAdmin_TheFlagNeverReplacesAuthorization(string method, string url)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false, admin: false);

        var response = await host.Client.SendAsync(Req(method, url, content: method == "POST" && url.Contains("sourceKind") ? Upload(new byte[] { 1 }) : BodyFor(method)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Create
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Create_HandsTheDocumentAndParametersToTheService_AndReturns201()
    {
        await using var host = await Host.StartAsync();
        RuleSetCreateRequest? seen = null;
        byte[]? seenBytes = null;
        var created = new RuleSetCreated(SetId, Guid.NewGuid(), "Draft", "Firm", FirmId, "RateCard", true);
        host.Service.Setup(s => s.CreateAsync(It.IsAny<RuleSetCreateRequest>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Callback((RuleSetCreateRequest r, byte[] b, CancellationToken _) => { seen = r; seenBytes = b; })
            .ReturnsAsync(RuleSetOutcome<RuleSetCreated>.Done(created));

        var response = await host.Client.SendAsync(Req("POST", $"{Base}?name=%20Acme%20OCG%20&sourceKind=ratecard&scope=firm&firmId={FirmId}", content: Upload(new byte[] { 9, 8, 7 })));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().EndWith($"{Base}/{SetId}");
        (await Json(response))["ruleSetId"]!.GetValue<Guid>().Should().Be(SetId);
        seen!.Name.Should().Be("Acme OCG");
        seen.SourceKind.Should().Be(RuleSetSourceKind.RateCard);
        seen.Scope.Should().Be(RuleSetScope.Firm);
        seen.FirmId.Should().Be(FirmId);
        seen.FileName.Should().Be("ocg.pdf");
        seen.CallerObjectId.Should().Be(Guid.Parse(FinanceAuthzTestAuthHandler.CallerObjectId));
        seenBytes.Should().Equal(9, 8, 7);
    }

    [Theory]
    [InlineData("sourceKind=Guideline", "name")]
    [InlineData("name=X", "sourceKind")]
    [InlineData("name=X&sourceKind=1", "sourceKind")]
    [InlineData("name=X&sourceKind=Guideline&scope=everyone", "scope")]
    [InlineData("name=X&sourceKind=Guideline&firmId=nope", "firmId")]
    public async Task Create_BadQuery_Returns400WithTheFieldNamed_AndNeverReachesTheService(string query, string field)
    {
        await using var host = await Host.StartAsync();

        var response = await host.Client.SendAsync(Req("POST", $"{Base}?{query}", content: Upload(new byte[] { 1 })));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await Json(response);
        body["errorCode"]!.GetValue<string>().Should().Be("ruleset.invalid");
        body["errors"]!.AsArray().Select(e => e!["field"]!.GetValue<string>()).Should().Contain(field);
        host.Service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_WithNoFile_Returns400_AndAFileOverTheLimitReturns413()
    {
        await using var host = await Host.StartAsync(maxMb: 1);

        var empty = await host.Client.SendAsync(Req("POST", $"{Base}?name=X&sourceKind=Guideline", content: Upload(Array.Empty<byte>())));
        var large = await host.Client.SendAsync(Req("POST", $"{Base}?name=X&sourceKind=Guideline", content: Upload(new byte[(1024 * 1024) + 10])));

        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        large.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await Json(large))["errorCode"]!.GetValue<string>().Should().Be("ruleset.file_too_large");
        host.Service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_OwnerRefused_Returns409WithTheOwnerReasonCode_AndContainerNotConfiguredReturns503()
    {
        await using var host = await Host.StartAsync();
        host.Service.SetupSequence(s => s.CreateAsync(It.IsAny<RuleSetCreateRequest>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<RuleSetCreated>.OwnerRefused(RecordOwnerResolution.Refused(RecordOwnerRefusal.NoOwnerSource, "no team")))
            .ReturnsAsync(RuleSetOutcome<RuleSetCreated>.Refused(503, RuleSetService.CodeContainerNotConfigured, "not configured"));

        var owner = await host.Client.SendAsync(Req("POST", $"{Base}?name=X&sourceKind=Guideline", content: Upload(new byte[] { 1 })));
        var container = await host.Client.SendAsync(Req("POST", $"{Base}?name=X&sourceKind=Guideline", content: Upload(new byte[] { 1 })));

        owner.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(owner))["reasonCode"]!.GetValue<string>().Should().Be(RecordOwnerRefusal.NoOwnerSource);
        container.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(container))["errorCode"]!.GetValue<string>().Should().Be("ruleset.container_not_configured");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Reads
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task List_And_Get_Return200WithTheService_Result_AndAnUnknownSetIs404WithItsCode()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Done(new[] { Summary() }));
        host.Service.Setup(s => s.GetAsync(SetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<RuleSetDetail>.Done(new RuleSetDetail(Summary(), Guid.NewGuid(), null, new[] { Proposal() })));
        var unknown = Guid.NewGuid();
        host.Service.Setup(s => s.GetAsync(unknown, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<RuleSetDetail>.Refused(404, RuleSetService.CodeNotFound, "no such rule set"));

        var list = await host.Client.SendAsync(Req("GET", Base));
        var get = await host.Client.SendAsync(Req("GET", $"{Base}/{SetId}"));
        var missing = await host.Client.SendAsync(Req("GET", $"{Base}/{unknown}"));

        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadFromJsonAsync<JsonArray>())!.Should().HaveCount(1);
        var detail = await Json(get);
        var rule = detail["proposals"]![0]!;
        rule["clauseQuote"]!.GetValue<string>().Should().Contain("10 hours");
        rule["clauseLocation"]!.GetValue<string>().Should().Be("Section 4.2");
        rule["descriptionText"]!.GetValue<string>().Should().Contain("10 hours");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(missing))["errorCode"]!.GetValue<string>().Should().Be("ruleset.not_found");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Edit / accept / reject
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Edit_PassesAnObjectBodyAsItsRawJson_AndAStringBodyAsTheString()
    {
        await using var host = await Host.StartAsync();
        var seen = new List<ProposalEdit>();
        host.Service.Setup(s => s.EditAsync(SetId, PolicyId, It.IsAny<ProposalEdit>(), It.IsAny<CancellationToken>()))
            .Callback((Guid _, Guid _, ProposalEdit e, CancellationToken _) => seen.Add(e))
            .ReturnsAsync(RuleSetOutcome<ProposalEdited>.Done(new ProposalEdited(Proposal("Proposed"), true)));

        var asObject = await host.Client.SendAsync(Req("PATCH", $"{Base}/{SetId}/proposals/{PolicyId}", content: JsonContent.Create(new
        {
            ruleType = "DailyHoursCap", ruleBody = new { type = "DailyHoursCap", maxHoursPerDay = 8 }, consequence = "Reject", clauseLocation = "S9",
        })));
        var asString = await host.Client.SendAsync(Req("PATCH", $"{Base}/{SetId}/proposals/{PolicyId}", content: JsonContent.Create(new
        {
            ruleBody = """{"type":"DailyHoursCap","maxHoursPerDay":9}""",
        })));

        asObject.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(asObject))["createdNewVersion"]!.GetValue<bool>().Should().BeTrue();
        asString.StatusCode.Should().Be(HttpStatusCode.OK);
        seen[0].RuleType.Should().Be("DailyHoursCap");
        JsonNode.Parse(seen[0].RuleBody!)!["maxHoursPerDay"]!.GetValue<int>().Should().Be(8);
        seen[0].Consequence.Should().Be("Reject");
        seen[0].ClauseLocation.Should().Be("S9");
        seen[0].CallerObjectId.Should().Be(Guid.Parse(FinanceAuthzTestAuthHandler.CallerObjectId));
        seen[1].RuleBody.Should().Be("""{"type":"DailyHoursCap","maxHoursPerDay":9}""");
    }

    [Fact]
    public async Task Edit_ARuleBodyThatIsNeitherAnObjectNorAString_Returns400_AndNeverReachesTheService()
    {
        await using var host = await Host.StartAsync();

        var response = await host.Client.SendAsync(Req("PATCH", $"{Base}/{SetId}/proposals/{PolicyId}", content: JsonContent.Create(new { ruleBody = 5 })));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(response))["errors"]![0]!["field"]!.GetValue<string>().Should().Be("ruleBody");
        host.VerifyOnlyTheProcedureLookup();
    }

    [Fact]
    public async Task Edit_InvalidResult_Returns400WithPerFieldErrors_AndInForceReturns409()
    {
        await using var host = await Host.StartAsync();
        host.Service.SetupSequence(s => s.EditAsync(SetId, PolicyId, It.IsAny<ProposalEdit>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<ProposalEdited>.Invalid(RuleSetService.CodeInvalid, new[]
            {
                new RuleSetError(PolicyId, "Rule BILL-A", "ruleType", "wrong domain", "rule_type_wrong_domain"),
            }))
            .ReturnsAsync(RuleSetOutcome<ProposalEdited>.Refused(409, RuleSetService.CodeRetired, "retired"));

        var invalid = await host.Client.SendAsync(Req("PATCH", $"{Base}/{SetId}/proposals/{PolicyId}", content: JsonContent.Create(new { ruleType = "Existence" })));
        var retired = await host.Client.SendAsync(Req("PATCH", $"{Base}/{SetId}/proposals/{PolicyId}", content: JsonContent.Create(new { headline = "x" })));

        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = (await Json(invalid))["errors"]![0]!;
        error["policyId"]!.GetValue<Guid>().Should().Be(PolicyId);
        error["rule"]!.GetValue<string>().Should().Be("Rule BILL-A");
        error["field"]!.GetValue<string>().Should().Be("ruleType");
        error["reason"]!.GetValue<string>().Should().Be("rule_type_wrong_domain");
        retired.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(retired))["errorCode"]!.GetValue<string>().Should().Be("ruleset.retired");
    }

    [Fact]
    public async Task Accept_And_Reject_CallDecideWithTheRightFlag_AndTheVersionTheAdminSaw_AndAnOntologyPolicyIs404()
    {
        await using var host = await Host.StartAsync();
        var ontologyPolicy = Guid.NewGuid();
        host.Service.Setup(s => s.DecideAsync(SetId, PolicyId, true, 4711L, It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(RuleSetOutcome<ProposalView>.Done(Proposal("Accepted")));
        host.Service.Setup(s => s.DecideAsync(SetId, PolicyId, false, 4711L, It.IsAny<CancellationToken>(), "Wrong limit."))
            .ReturnsAsync(RuleSetOutcome<ProposalView>.Done(Proposal("Rejected")));
        host.Service.Setup(s => s.DecideAsync(SetId, ontologyPolicy, It.IsAny<bool>(), It.IsAny<long>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(RuleSetOutcome<ProposalView>.Refused(404, RuleSetService.CodeProposalNotFound, "no such rule"));

        var accept = await host.Client.SendAsync(Req("POST", $"{Base}/{SetId}/proposals/{PolicyId}/accept", content: JsonContent.Create(new { rowVersion = "4711" })));
        var reject = await host.Client.SendAsync(Req("POST", $"{Base}/{SetId}/proposals/{PolicyId}/reject", content: JsonContent.Create(new { rowVersion = "4711", reason = "  Wrong limit.  " })));
        var foreign = await host.Client.SendAsync(Req("POST", $"{Base}/{SetId}/proposals/{ontologyPolicy}/accept", content: JsonContent.Create(new { rowVersion = "4711" })));

        (await Json(accept))["state"]!.GetValue<string>().Should().Be("Accepted");
        (await Json(reject))["state"]!.GetValue<string>().Should().Be("Rejected");
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(foreign))["errorCode"]!.GetValue<string>().Should().Be("ruleset.proposal_not_found");
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("reject")]
    public async Task Decide_WithoutTheVersionTheAdminSaw_Is400_AndNeverReachesTheService(string decision)
    {
        await using var host = await Host.StartAsync();
        var url = $"{Base}/{SetId}/proposals/{PolicyId}/{decision}";

        var noBody = await host.Client.SendAsync(Req("POST", url));
        var emptyObject = await host.Client.SendAsync(Req("POST", url, content: JsonContent.Create(new { })));
        var blank = await host.Client.SendAsync(Req("POST", url, content: JsonContent.Create(new { rowVersion = " " })));
        var notANumber = await host.Client.SendAsync(Req("POST", url, content: JsonContent.Create(new { rowVersion = "12ab" })));
        var negative = await host.Client.SendAsync(Req("POST", url, content: JsonContent.Create(new { rowVersion = "-5" })));

        foreach (var response in new[] { noBody, emptyObject, blank, notANumber, negative })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await Json(response);
            body["errorCode"]!.GetValue<string>().Should().Be("ruleset.invalid");
            body["errors"]![0]!["field"]!.GetValue<string>().Should().Be("rowVersion");
        }

        host.VerifyOnlyTheProcedureLookup();
    }

    [Fact]
    public async Task Reject_WithoutAReason_Is400OnTheReasonField_AndNeverReachesTheService()
    {
        await using var host = await Host.StartAsync();
        var url = $"{Base}/{SetId}/proposals/{PolicyId}/reject";

        var missing = await host.Client.SendAsync(Req("POST", url, content: JsonContent.Create(new { rowVersion = "4711" })));
        var empty = await host.Client.SendAsync(Req("POST", url, content: JsonContent.Create(new { rowVersion = "4711", reason = "" })));
        var blank = await host.Client.SendAsync(Req("POST", url, content: JsonContent.Create(new { rowVersion = "4711", reason = " \t\n " })));
        var tooLong = await host.Client.SendAsync(Req("POST", url, content: JsonContent.Create(new { rowVersion = "4711", reason = new string('r', 2001) })));

        foreach (var response in new[] { missing, empty, blank, tooLong })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await Json(response);
            body["errorCode"]!.GetValue<string>().Should().Be("ruleset.invalid");
            body["errors"]![0]!["field"]!.GetValue<string>().Should().Be("reason");
        }

        host.VerifyOnlyTheProcedureLookup();
    }

    [Fact]
    public async Task Reject_PassesTheTrimmedReasonToTheService_AndAcceptNeverPassesOne()
    {
        await using var host = await Host.StartAsync();
        var exact = new string('r', 2000);
        host.Service.Setup(s => s.DecideAsync(SetId, PolicyId, false, 4711L, It.IsAny<CancellationToken>(), exact))
            .ReturnsAsync(RuleSetOutcome<ProposalView>.Done(Proposal("Rejected")));
        host.Service.Setup(s => s.DecideAsync(SetId, PolicyId, true, 4711L, It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(RuleSetOutcome<ProposalView>.Done(Proposal("Accepted")));

        var reject = await host.Client.SendAsync(Req("POST", $"{Base}/{SetId}/proposals/{PolicyId}/reject", content: JsonContent.Create(new { rowVersion = "4711", reason = " " + exact + " " })));
        var accept = await host.Client.SendAsync(Req("POST", $"{Base}/{SetId}/proposals/{PolicyId}/accept", content: JsonContent.Create(new { rowVersion = "4711", reason = "ignored" })));

        reject.StatusCode.Should().Be(HttpStatusCode.OK, "2,000 characters after trimming is the limit");
        accept.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Decide_OfARuleThatChangedSinceItWasShown_IsThe409ConcurrentEdit()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.DecideAsync(SetId, PolicyId, true, 4711L, It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(RuleSetOutcome<ProposalView>.Refused(409, RuleSetService.CodeConcurrentEdit, "changed"));

        var response = await host.Client.SendAsync(Req("POST", $"{Base}/{SetId}/proposals/{PolicyId}/accept", content: JsonContent.Create(new { rowVersion = "4711" })));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ruleset.concurrent_edit");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Publish / assign / visibility
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Publish_ReturnsTheClauseVisibilityNotice_AndPassesTheEffectiveDate()
    {
        await using var host = await Host.StartAsync();
        var effective = new DateOnly(2026, 11, 1);
        host.Service.Setup(s => s.PublishAsync(SetId, effective, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<RuleSetPublished>.Done(new RuleSetPublished(
                SetId, "Published", effective, new[] { new PublishedRuleVersion(PolicyId, Guid.NewGuid(), 1, null) }, 1, 0, false, RuleSetService.HiddenNotice)));

        var response = await host.Client.SendAsync(Req("POST", $"{Base}/{SetId}/publish", content: JsonContent.Create(new { effectiveDate = "2026-11-01" })));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Json(response);
        body["clauseQuotesVisibleToFirms"]!.GetValue<bool>().Should().BeFalse();
        body["clauseVisibilityNotice"]!.GetValue<string>().Should().Be("Clause quotes will not be visible to firms.");
        body["notPublishedProposed"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task Publish_WithoutAnEffectiveDate_Returns400_AndNeverReachesTheService()
    {
        await using var host = await Host.StartAsync();

        var response = await host.Client.SendAsync(Req("POST", $"{Base}/{SetId}/publish", content: JsonContent.Create(new { })));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(response))["errors"]![0]!["field"]!.GetValue<string>().Should().Be("effectiveDate");
        host.Service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Publish_OneInvalidRule_Returns400WithThePublishInvalidCodeAndPerRuleErrors()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.PublishAsync(SetId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<RuleSetPublished>.Invalid(RuleSetService.CodePublishInvalid, new[]
            {
                new RuleSetError(PolicyId, "Rule BILL-BAD", "ruleBody", "maxHoursPerDay must be > 0", "schema_invalid"),
                new RuleSetError(Guid.NewGuid(), "Rule BILL-BAD2", "consequence", "required", null),
            }));

        var response = await host.Client.SendAsync(Req("POST", $"{Base}/{SetId}/publish", content: JsonContent.Create(new { effectiveDate = "2026-11-01" })));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await Json(response);
        body["errorCode"]!.GetValue<string>().Should().Be("ruleset.publish_invalid");
        body["errors"]!.AsArray().Should().HaveCount(2);
    }

    [Fact]
    public async Task Assign_ParsesTheScope_AndRejectsAnUnknownOne()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.AssignAsync(SetId, RuleSetScope.Firm, FirmId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<RuleSetSummary>.Done(Summary() with { Scope = "Firm", FirmId = FirmId }));

        var ok = await host.Client.SendAsync(Req("PUT", $"{Base}/{SetId}/assignment", content: JsonContent.Create(new { scope = "firm", firmId = FirmId })));
        var bad = await host.Client.SendAsync(Req("PUT", $"{Base}/{SetId}/assignment", content: JsonContent.Create(new { scope = "everyone" })));

        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(ok))["firmId"]!.GetValue<Guid>().Should().Be(FirmId);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(bad))["errors"]![0]!["field"]!.GetValue<string>().Should().Be("scope");
    }

    [Fact]
    public async Task Visibility_RequiresTheFlag_AndPassesItToTheService()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.SetVisibilityAsync(SetId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<RuleSetSummary>.Done(Summary(visible: false)));

        var ok = await host.Client.SendAsync(Req("PUT", $"{Base}/{SetId}/visibility", content: JsonContent.Create(new { visibleToFirms = false })));
        var missing = await host.Client.SendAsync(Req("PUT", $"{Base}/{SetId}/visibility", content: JsonContent.Create(new { })));

        (await Json(ok))["visibleToFirms"]!.GetValue<bool>().Should().BeFalse();
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Service.Verify(s => s.SetVisibilityAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Publish_Or_Assign_OnARetiredSet_Returns409WithItsCode()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.PublishAsync(SetId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<RuleSetPublished>.Refused(409, RuleSetService.CodeRetired, "retired"));

        var response = await host.Client.SendAsync(Req("POST", $"{Base}/{SetId}/publish", content: JsonContent.Create(new { effectiveDate = "2026-11-01" })));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ruleset.retired");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Draft (task 075, FR-27): queue the rule drafting job
    // ═══════════════════════════════════════════════════════════════════════════

    private static DraftSource Source(bool drafted = false) =>
        new(SetId, "Acme OCG", Guid.NewGuid(), "b!lib", "item-1", "ocg.pdf", drafted);

    [Fact]
    public async Task Draft_QueuesTheJob_AndReturns202WithTheRuleSetReadAsItsLocation()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.GetDraftSourceAsync(SetId, It.IsAny<CancellationToken>())).ReturnsAsync(RuleSetOutcome<DraftSource>.Done(Source()));
        JobContract? queued = null;
        host.Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Callback((JobContract job, CancellationToken _) => queued = job)
            .Returns(Task.CompletedTask);

        var response = await host.Client.SendAsync(Req("POST", $"{Base}/{SetId}/draft"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location!.ToString().Should().Be($"{Base}/{SetId}");
        var body = await Json(response);
        body["ruleSetId"]!.GetValue<Guid>().Should().Be(SetId);
        body["statusUrl"]!.GetValue<string>().Should().Be($"{Base}/{SetId}");
        body["jobId"]!.GetValue<Guid>().Should().Be(queued!.JobId);

        queued.JobType.Should().Be(BillingRuleDraftJobHandler.JobTypeName);
        queued.SubjectId.Should().Be(SetId.ToString());
        queued.IdempotencyKey.Should().Be(SetId.ToString());
        var payload = queued.Payload!.RootElement;
        payload.GetProperty("ruleSetId").GetGuid().Should().Be(SetId);
        payload.GetProperty("callerObjectId").GetGuid().Should().Be(Guid.Parse(FinanceAuthzTestAuthHandler.CallerObjectId),
            "the caller's id is server-derived from the token, never from a body");
        host.Jobs.Verify(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(404, "ruleset.not_found")]
    [InlineData(409, "ruleset.retired")]
    [InlineData(422, "ruleset.source_missing")]
    public async Task Draft_ARefusedSource_IsReportedNow_AndNoJobIsQueued(int status, string code)
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.GetDraftSourceAsync(SetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Refused(status, code, "refused"));

        var response = await host.Client.SendAsync(Req("POST", $"{Base}/{SetId}/draft"));

        ((int)response.StatusCode).Should().Be(status);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(code);
        host.Jobs.Verify(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Draft_AFinishedRun_Is409AlreadyDrafted_AndNoJobIsQueued()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.GetDraftSourceAsync(SetId, It.IsAny<CancellationToken>())).ReturnsAsync(RuleSetOutcome<DraftSource>.Done(Source(drafted: true)));

        var response = await host.Client.SendAsync(Req("POST", $"{Base}/{SetId}/draft"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ruleset.already_drafted");
        host.Jobs.Verify(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Draft_WhenTheQueueIsDown_Is503WithItsCode_NotAnUnhandled500()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.GetDraftSourceAsync(SetId, It.IsAny<CancellationToken>())).ReturnsAsync(RuleSetOutcome<DraftSource>.Done(Source()));
        host.Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("bus down"));

        var response = await host.Client.SendAsync(Req("POST", $"{Base}/{SetId}/draft"));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ruleset.draft_not_queued");
    }
}
