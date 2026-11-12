using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
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
using Sprk.Bff.Api.Services.Finance.Procedures;
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

        /// <summary>Every log line the host wrote (089e: the record-scoped answer is tracked under its code).</summary>
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Logs = new();
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
            // 089e: every by-id route asks the caller's Read on the set, then reads its record home; a readable set that is not
            // record-scoped unless a test says otherwise.
            Service.Setup(s => s.RecordHomeOfRuleSetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((RecordRuleSetHome?)null);
            Access.Grant("sprk_rulesets", SetId, AccessRights.Read);
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

        public static async Task<Host> StartAsync(
            bool ebillingEnabled = true, int maxMb = 25, bool admin = true, Action<IEndpointRouteBuilder>? alsoMap = null)
        {
            var host = new Host();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new LineLogger(host.Logs));
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
            alsoMap?.Invoke(host._app); // 089e F3: the route guard's must-fire control maps a route without the set filter
            await host._app.StartAsync();
            host.Client = host._app.GetTestClient();
            return host;
        }

        /// <summary>No service operation ran: the only call allowed is the proposal routes' authorization lookup of the set's Procedure
        /// (uac-r2 D), which is not a service operation.</summary>
        public void VerifyOnlyTheProcedureLookup()
        {
            Service.Verify(s => s.ProcedureOfRuleSetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce()); // one per request
            VerifyNoServiceOperation();
        }

        /// <summary>
        /// No service operation ran. The by-id routes' authorization lookups (089e's record home, uac-r2 D's Procedure) are not service
        /// operations, so they are allowed, any number of times.
        /// </summary>
        public void VerifyNoServiceOperation()
        {
            Service.Verify(s => s.ProcedureOfRuleSetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.AtMost(int.MaxValue));
            Service.Verify(s => s.RecordHomeOfRuleSetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.AtMost(int.MaxValue));
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

        /// <summary>089e F3: every route this host mapped, as the routing system holds it (group prefixes and filters applied).</summary>
        public IReadOnlyList<RouteEndpoint> Routes =>
            ((IEndpointRouteBuilder)_app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>().ToList();

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

    /// <summary>089e fix round 2: the list asks the caller's Read on every set it keeps; these sets are readable.</summary>
    private static RuleSetSummary[] Readable(Host host, params RuleSetSummary[] sets)
    {
        foreach (var set in sets)
        {
            host.Access.Grant("sprk_rulesets", set.Id, AccessRights.Read);
        }

        return sets;
    }

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
            .ReturnsAsync(RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Done(Readable(host, legacy, readable, readableToo, denied)));

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
            sets[i] = Readable(host, SummaryOf(Guid.NewGuid(), procedureId))[0];
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
            .ReturnsAsync(RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Done(Readable(host, SummaryOf(Guid.NewGuid(), null), SummaryOf(Guid.NewGuid(), null))));

        var response = await host.Client.SendAsync(Req("GET", Base));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonArray>())!.Should().HaveCount(2);
        host.Access.Calls.Should().NotContain(c => c.Set == "sprk_procedures");
    }

    [Fact(DisplayName = "089e F1 ii must-fire: a set the caller cannot Read is absent from the list; one question per set the Procedure check kept")]
    public async Task List_LeavesOutSetsTheCallerCannotRead_AndAsksOncePerSet()
    {
        await using var host = await Host.StartAsync();
        var procedureId = Guid.NewGuid();
        host.Access.Grant("sprk_procedures", procedureId, AccessRights.Read);
        var legacyReadable = Readable(host, SummaryOf(Guid.NewGuid(), null))[0];
        var legacyUnreadable = SummaryOf(Guid.NewGuid(), null);
        var procedureReadable = Readable(host, SummaryOf(Guid.NewGuid(), procedureId))[0];
        var procedureUnreadable = SummaryOf(Guid.NewGuid(), procedureId);
        host.Access.Grant("sprk_rulesets", legacyUnreadable.Id, AccessRights.None); // Dataverse answered: no Read
        host.Access.Grant("sprk_rulesets", procedureUnreadable.Id, AccessRights.Write); // rights, but not Read
        host.Service.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Done(new[] { legacyReadable, legacyUnreadable, procedureReadable, procedureUnreadable }));

        var response = await host.Client.SendAsync(Req("GET", Base));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var ids = (await response.Content.ReadFromJsonAsync<JsonArray>())!.Select(n => n!["id"]!.GetValue<Guid>()).ToList();
        ids.Should().Equal(legacyReadable.Id, procedureReadable.Id);
        host.Access.Calls.Where(c => c.Set == "sprk_rulesets").Select(c => c.Id).Should().BeEquivalentTo(
            new[] { legacyReadable.Id, legacyUnreadable.Id, procedureReadable.Id, procedureUnreadable.Id }, "one Read question per listed set");
        host.Access.Calls.Where(c => c.Set == "sprk_rulesets").Should().OnlyContain(c => c.HasToken, "asked as the caller");
    }

    [Fact(DisplayName = "089e F1 ii: the list and the by-id read agree - a set the list leaves out is the by-id route's uniform 404")]
    public async Task List_And_TheByIdRead_Agree_OnASetTheCallerCannotRead()
    {
        var missing = await MissingSetAnswer("GET", $"{Base}/{SetId}");
        await using var host = await Host.StartAsync();
        host.Access.Grant("sprk_rulesets", SetId, AccessRights.None);
        host.Service.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Done(new[] { Summary() }));

        var list = await host.Client.SendAsync(Req("GET", Base));
        var get = await host.Client.SendAsync(Req("GET", $"{Base}/{SetId}"));

        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadFromJsonAsync<JsonArray>())!.Should().BeEmpty();
        get.StatusCode.Should().Be(missing.Status);
        (await get.Content.ReadAsStringAsync()).Should().Be(missing.Body);
    }

    [Fact(DisplayName = "089e F1 ii must-fire: ONE unknown Read answer on a set makes the list 403 system_failure, with no list")]
    public async Task List_WhenAnySetRightsAreUnknown_IsSystemFailure_AndReturnsNoList()
    {
        await using var host = await Host.StartAsync();
        var known = Readable(host, SummaryOf(Guid.NewGuid(), null))[0];
        var unknown = SummaryOf(Guid.NewGuid(), null);
        host.Access.Fault("sprk_rulesets", unknown.Id);
        host.Service.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Done(new[] { known, unknown }));

        var response = await host.Client.SendAsync(Req("GET", Base));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await Json(response);
        problem["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(known.Id.ToString());
    }

    [Fact(DisplayName = "089e F1 ii / K2 must-fire: more sets than the cap is 422 result_truncated (not a 403) and asks no Read question on a set")]
    public async Task List_WithMoreSetsThanTheCap_IsResultTruncated_AndAsksNoSetQuestion()
    {
        await using var host = await Host.StartAsync();
        var sets = Enumerable.Range(0, RuleSetEndpoints.MaxRuleSetReadChecks + 1).Select(_ => Readable(host, SummaryOf(Guid.NewGuid(), null))[0]).ToArray();
        host.Service.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Done(sets));

        var response = await host.Client.SendAsync(Req("GET", Base));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(RuleSetService.CodeResultTruncated).And.Contain(RuleSetService.TooManyRuleSetsMessage);
        body.Should().NotContain(FinanceAuthorizationFilter.SystemFailureReasonCode);
        host.Access.Calls.Count(c => c.Set == "sprk_rulesets").Should().Be(0, "the cap is checked before any rights question");
    }

    [Fact(DisplayName = "089e F1 ii must-not-fire: exactly the cap of readable sets is answered in full")]
    public async Task List_WithExactlyTheCapOfSets_IsAnsweredInFull()
    {
        await using var host = await Host.StartAsync();
        var sets = Enumerable.Range(0, RuleSetEndpoints.MaxRuleSetReadChecks).Select(_ => Readable(host, SummaryOf(Guid.NewGuid(), null))[0]).ToArray();
        host.Service.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Done(sets));

        var response = await host.Client.SendAsync(Req("GET", Base));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonArray>())!.Should().HaveCount(RuleSetEndpoints.MaxRuleSetReadChecks);
    }

    [Fact(DisplayName = "081 uac r2 I-P5: the Procedure id is internal; the list response never carries it")]
    public async Task List_DoesNotSerializeTheProcedureId()
    {
        await using var host = await Host.StartAsync();
        var procedureId = Guid.NewGuid();
        host.Access.Grant("sprk_procedures", procedureId, AccessRights.Read);
        host.Service.Setup(s => s.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<IReadOnlyList<RuleSetSummary>>.Done(Readable(host, SummaryOf(Guid.NewGuid(), procedureId))));

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
            ruleType = "DailyHoursCap",
            ruleBody = new { type = "DailyHoursCap", maxHoursPerDay = 8 },
            consequence = "Reject",
            clauseLocation = "S9",
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
        host.VerifyNoServiceOperation();
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

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 089e (owner 2026-11-04, "Approve (A)+(B) on all 9 routes"): the rule-set by-id routes authorise the set and its record
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Every route here that takes a rule set id, with a body its handler accepts (so a refusal is the filter's, not a 400).</summary>
    public static TheoryData<string, string> ByIdRoutes => new()
    {
        { "POST", $"{Base}/{SetId}/draft" },
        { "GET", $"{Base}/{SetId}" },
        { "PATCH", $"{Base}/{SetId}/proposals/{PolicyId}" },
        { "POST", $"{Base}/{SetId}/proposals/{PolicyId}/accept" },
        { "POST", $"{Base}/{SetId}/proposals/{PolicyId}/reject" },
        { "POST", $"{Base}/{SetId}/publish" },
        { "PUT", $"{Base}/{SetId}/assignment" },
        { "PUT", $"{Base}/{SetId}/visibility" },
    };

    private static HttpContent? ByIdBody(string method, string url) =>
        url.EndsWith("/publish", StringComparison.Ordinal) ? JsonContent.Create(new { effectiveDate = "2026-12-01" })
        : url.EndsWith("/assignment", StringComparison.Ordinal) ? JsonContent.Create(new { scope = "ClientDefault" })
        : url.EndsWith("/visibility", StringComparison.Ordinal) ? JsonContent.Create(new { visibleToFirms = true })
        : url.Contains("/proposals/", StringComparison.Ordinal) ? ProposalBody(url)
        : BodyFor(method);

    private static readonly Guid MatterId = Guid.NewGuid();

    /// <summary>The set at <see cref="SetId"/> is record-scoped, filed under <paramref name="coreTable"/>(<paramref name="coreId"/>).</summary>
    private static void SetIsRecordScoped(Host host, string coreTable, Guid coreId) =>
        host.Service.Setup(s => s.RecordHomeOfRuleSetAsync(SetId, It.IsAny<CancellationToken>())).ReturnsAsync(new RecordRuleSetHome(coreTable, coreId));

    /// <summary>The service answers its own "not found" on every by-id operation: a set that is gone by the time the service reads it.</summary>
    private static void ServiceFindsNoSet(Host host)
    {
        host.Service.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(RuleSetService.RuleSetNotFound<RuleSetDetail>());
        host.Service.Setup(s => s.GetDraftSourceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(RuleSetService.RuleSetNotFound<DraftSource>());
        host.Service.Setup(s => s.EditAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ProposalEdit>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetService.RuleSetNotFound<ProposalEdited>());
        host.Service.Setup(s => s.DecideAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<long>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(RuleSetService.RuleSetNotFound<ProposalView>());
        host.Service.Setup(s => s.PublishAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(RuleSetService.RuleSetNotFound<RuleSetPublished>());
        host.Service.Setup(s => s.AssignAsync(It.IsAny<Guid>(), It.IsAny<RuleSetScope>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetService.RuleSetNotFound<RuleSetSummary>());
        host.Service.Setup(s => s.SetVisibilityAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(RuleSetService.RuleSetNotFound<RuleSetSummary>());
    }

    /// <summary>The bytes a set id that does not exist gets on this route: the caller's Read on the set refused, as Dataverse answers for no row.</summary>
    private static async Task<(HttpStatusCode Status, string Body)> MissingSetAnswer(string method, string url)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant("sprk_rulesets", SetId, AccessRights.None);
        var response = await host.Client.SendAsync(Req(method, url, content: ByIdBody(method, url)));
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// 089e F3: the (method, url) of every route the host mapped whose pattern carries a rule set id, read from the routing table rather than
    /// written by hand. The set id becomes <see cref="SetId"/>; any other parameter becomes <see cref="PolicyId"/>.
    /// </summary>
    private static IReadOnlyList<(string Method, string Url)> MappedByIdRoutes(Host host) =>
        host.Routes
            .Where(e => e.RoutePattern.RawText?.Contains("{ruleSetId", StringComparison.Ordinal) == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? new[] { "GET" })
                .Select(method => (method, "/" + System.Text.RegularExpressions.Regex.Replace(
                    e.RoutePattern.RawText!.TrimStart('/'), @"\{(\w+)[^}]*\}",
                    m => (m.Groups[1].Value == "ruleSetId" ? SetId : PolicyId).ToString()))))
            .ToList();

    /// <summary>089e F3: each mapped by-id route that does NOT answer a set the caller cannot Read with the uniform 404 bytes.</summary>
    private static async Task<IReadOnlyList<string>> ByIdRoutesWithoutTheUniform404(Host host, (HttpStatusCode Status, string Body) missing)
    {
        host.Access.Grant("sprk_rulesets", SetId, AccessRights.None);
        var breaks = new List<string>();
        foreach (var (method, url) in MappedByIdRoutes(host))
        {
            var response = await host.Client.SendAsync(Req(method, url, content: ByIdBody(method, url)));
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != missing.Status || body != missing.Body)
            {
                breaks.Add($"{method} {url}: {(int)response.StatusCode} {body}");
            }
        }

        return breaks;
    }

    [Fact(DisplayName = "089e F3 guard: EVERY mapped route with a rule set id answers a set the caller cannot Read with the uniform 404, and the theory list matches the routing table")]
    public async Task EveryMappedByIdRoute_ASetTheCallerCannotRead_IsTheUniform404()
    {
        var missing = await MissingSetAnswer("GET", $"{Base}/{SetId}");
        missing.Status.Should().Be(HttpStatusCode.NotFound);
        JsonNode.Parse(missing.Body)!["errorCode"]!.GetValue<string>().Should().Be(RuleSetService.CodeNotFound);
        await using var host = await Host.StartAsync();

        var mapped = MappedByIdRoutes(host);

        (await ByIdRoutesWithoutTheUniform404(host, missing)).Should().BeEmpty();
        mapped.Should().HaveCount(8, "non-vacuity: the eight by-id routes MapRuleSetEndpoints maps today");
        mapped.Should().BeEquivalentTo(
            ByIdRoutes.Select(row => ((string)row[0], (string)row[1])),
            "the hand-written ByIdRoutes theory list is the routing table's, so every theory above covers every by-id route");
    }

    [Fact(DisplayName = "089e F3 guard must-fire: a by-id route mapped without the set filter is reported")]
    public async Task TheRouteGuard_ReportsAByIdRouteWithoutTheSetFilter()
    {
        var missing = await MissingSetAnswer("GET", $"{Base}/{SetId}");
        await using var host = await Host.StartAsync(alsoMap: routes =>
            routes.MapGet(Base + "/{ruleSetId:guid}/unguarded", (Guid ruleSetId) => Microsoft.AspNetCore.Http.Results.Ok(new { ruleSetId })));

        var breaks = await ByIdRoutesWithoutTheUniform404(host, missing);

        breaks.Should().ContainSingle().Which.Should().StartWith($"GET {Base}/{SetId}/unguarded: 200");
    }

    [Theory(DisplayName = "089e (A) must-fire: a set the caller cannot Read is the uniform 404, even when its record is readable, and nothing about the set is read")]
    [MemberData(nameof(ByIdRoutes))]
    public async Task ByIdRoute_ASetTheCallerCannotRead_IsTheUniform404_BeforeTheSetIsRead(string method, string url)
    {
        var missing = await MissingSetAnswer(method, url);
        await using var host = await Host.StartAsync();
        host.Access.Grant("sprk_rulesets", SetId, AccessRights.None); // no Read on the set
        SetIsRecordScoped(host, ProcedureAssignmentEndpoints.MatterTable, MatterId);
        host.Access.Grant(FinanceAuthorizationFilter.MatterEntitySet, MatterId, AccessRights.Read | AccessRights.Write); // the record IS readable

        var response = await host.Client.SendAsync(Req(method, url, content: ByIdBody(method, url)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(RuleSetService.CodeNotFound);
        body.Should().Be(missing.Body, "an unreadable set answers exactly what a set id that does not exist answers");
        host.Access.Calls.Should().Contain(c => c.Path == FinAuth.AccessPath.Record && c.Set == "sprk_rulesets" && c.Id == SetId && c.HasToken,
            "the set's Read is asked of Dataverse as the caller");
        host.Service.Verify(s => s.RecordHomeOfRuleSetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never(),
            "the set is authorised before anything that differs between \"exists\" and \"does not exist\" is read");
        host.Access.Calls.Should().NotContain(c => c.Set == FinanceAuthorizationFilter.MatterEntitySet);
        host.VerifyNoServiceOperation();
    }

    [Theory(DisplayName = "089e (B) must-fire: a record-scoped set whose record the caller cannot Read is the uniform 404, byte-identical to a set id that does not exist")]
    [MemberData(nameof(ByIdRoutes))]
    public async Task ByIdRoute_ARecordScopedSet_WhoseRecordTheCallerCannotRead_IsTheUniform404(string method, string url)
    {
        var missing = await MissingSetAnswer(method, url);
        await using var host = await Host.StartAsync();
        SetIsRecordScoped(host, ProcedureAssignmentEndpoints.MatterTable, MatterId); // the set is readable (Host grants Read on it)

        var response = await host.Client.SendAsync(Req(method, url, content: ByIdBody(method, url)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Be(missing.Body);
        host.Access.Calls.Should().Contain(c => c.Path == FinAuth.AccessPath.Record && c.Set == FinanceAuthorizationFilter.MatterEntitySet && c.Id == MatterId && c.HasToken,
            "the record is asked of Dataverse as the caller, through the record routes' own check");
        host.VerifyNoServiceOperation();
    }

    [Theory(DisplayName = "089e (B) must-fire: a Restricted record, while the caller holds (Deep) Read on rule sets, is the uniform 404, not the 409")]
    [MemberData(nameof(ByIdRoutes))]
    public async Task ByIdRoute_ARestrictedRecord_WithReadOnTheSet_IsTheUniform404(string method, string url)
    {
        var missing = await MissingSetAnswer(method, url);
        await using var host = await Host.StartAsync();
        var restricted = Guid.NewGuid();
        // A Billing Administrator holds Read at Deep on sprk_ruleset, so (A) passes; the Restricted record answers no Read to this caller.
        host.Access.Grant("sprk_rulesets", SetId, AccessRights.Read);
        SetIsRecordScoped(host, ProcedureAssignmentEndpoints.ProjectTable, restricted);
        host.Access.Grant(FinanceAuthorizationFilter.ProjectEntitySet, restricted, AccessRights.None);

        var response = await host.Client.SendAsync(Req(method, url, content: ByIdBody(method, url)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "Read on the set alone never shows that a record the caller cannot see has a changed rule");
        (await response.Content.ReadAsStringAsync()).Should().Be(missing.Body);
        host.Logs.Should().NotContain(l => l.Contains(RuleSetEndpoints.CodeRecordScoped, StringComparison.Ordinal));
        host.VerifyNoServiceOperation();
    }

    [Theory(DisplayName = "089e (B) fail closed: no answer on the record, or a set with no single core record, is the uniform 404")]
    [InlineData("fault")]
    [InlineData("no-core")]
    public async Task Get_ARecordScopedSet_WhenTheRecordCannotBeDecided_IsTheUniform404(string shape)
    {
        var url = $"{Base}/{SetId}";
        var missing = await MissingSetAnswer("GET", url);
        await using var host = await Host.StartAsync();
        if (shape == "no-core")
        {
            host.Service.Setup(s => s.RecordHomeOfRuleSetAsync(SetId, It.IsAny<CancellationToken>())).ReturnsAsync(new RecordRuleSetHome(null, Guid.Empty));
        }
        else
        {
            SetIsRecordScoped(host, ProcedureAssignmentEndpoints.MatterTable, MatterId);
            host.Access.Fault(FinanceAuthorizationFilter.MatterEntitySet, MatterId); // Dataverse gave no answer
            host.Access.Grant(FinanceAuthorizationFilter.MatterEntitySet, MatterId, AccessRights.None);
        }

        var response = await host.Client.SendAsync(Req("GET", url));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Be(missing.Body);

        host.VerifyNoServiceOperation();
    }

    [Theory(DisplayName = "089e must-fire: a record-scoped set the caller can Read, with its record, is 409 ruleset.record_scoped naming the override routes, and tracked")]
    [MemberData(nameof(ByIdRoutes))]
    public async Task ByIdRoute_AReadableRecordScopedSet_Is409RecordScoped_AndLogged(string method, string url)
    {
        await using var host = await Host.StartAsync();
        SetIsRecordScoped(host, ProcedureAssignmentEndpoints.MatterTable, MatterId);
        host.Access.Grant(FinanceAuthorizationFilter.MatterEntitySet, MatterId, AccessRights.Read);

        var response = await host.Client.SendAsync(Req(method, url, content: ByIdBody(method, url)));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await Json(response);
        body["errorCode"]!.GetValue<string>().Should().Be(RuleSetEndpoints.CodeRecordScoped);
        body["recordKind"]!.GetValue<string>().Should().Be("matter");
        body["overridesRoute"]!.GetValue<string>().Should().Be($"/api/finance/matters/{MatterId}");
        var detail = body["detail"]!.GetValue<string>();
        detail.Should().Contain($"/api/finance/matters/{MatterId}/rule-overrides/change").And.Contain($"/api/finance/matters/{MatterId}/overrides/");
        host.Logs.Should().Contain(l => l.Contains(RuleSetEndpoints.CodeRecordScoped, StringComparison.Ordinal) && l.Contains(SetId.ToString(), StringComparison.Ordinal),
            "the answer is tracked under its code");
        host.Service.Verify(s => s.ProcedureOfRuleSetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never(),
            "the record-scoped answer comes before the Procedure filter");
        host.VerifyNoServiceOperation();
    }

    [Theory(DisplayName = "089e: the 409 names the record kind and its own override routes for each core table")]
    [InlineData(ProcedureAssignmentEndpoints.MatterTable, FinanceAuthorizationFilter.MatterEntitySet, "matters", "matter")]
    [InlineData(ProcedureAssignmentEndpoints.ProjectTable, FinanceAuthorizationFilter.ProjectEntitySet, "projects", "project")]
    [InlineData(ProcedureAssignmentEndpoints.WorkAssignmentTable, FinanceAuthorizationFilter.WorkAssignmentEntitySet, "workassignments", "work assignment")]
    public async Task Get_AReadableRecordScopedSet_NamesItsRecordKind(string coreTable, string entitySet, string segment, string kind)
    {
        await using var host = await Host.StartAsync();
        var recordId = Guid.NewGuid();
        SetIsRecordScoped(host, coreTable, recordId);
        host.Access.Grant(entitySet, recordId, AccessRights.Read);

        var response = await host.Client.SendAsync(Req("GET", $"{Base}/{SetId}"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await Json(response);
        body["recordKind"]!.GetValue<string>().Should().Be(kind);
        body["overridesRoute"]!.GetValue<string>().Should().Be($"/api/finance/{segment}/{recordId}");
    }

    [Theory(DisplayName = "089e must-not-fire: a set that is not record-scoped (legacy or a Procedure's) with Read on the set reaches the route as before")]
    [MemberData(nameof(ByIdRoutes))]
    public async Task ByIdRoute_ASetThatIsNotRecordScoped_IsUnchanged(string method, string url)
    {
        await using var host = await Host.StartAsync();
        ServiceFindsNoSet(host);

        var response = await host.Client.SendAsync(Req(method, url, content: ByIdBody(method, url)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "the service answered: the filter let the request through");
        (await response.Content.ReadAsStringAsync()).Should().Be((await MissingSetAnswer(method, url)).Body,
            "the service's own 404 and the filter's are the same bytes");
        host.Access.Calls.Should().NotContain(c => c.Set == FinanceAuthorizationFilter.MatterEntitySet || c.Set == FinanceAuthorizationFilter.ProjectEntitySet
            || c.Set == FinanceAuthorizationFilter.WorkAssignmentEntitySet, "no record is asked about for a set that has none");
    }

    [Theory(DisplayName = "089e (A) on a Procedure's set: without Read on the set it is the uniform 404 and the Procedure check is never asked")]
    [MemberData(nameof(ProposalRoutes))]
    public async Task ProposalRoute_OnAProceduresSet_WithoutReadOnTheSet_IsTheUniform404(string method, string url)
    {
        var missing = await MissingSetAnswer(method, url);
        await using var host = await Host.StartAsync();
        var procedureId = Guid.NewGuid();
        host.Service.Setup(s => s.ProcedureOfRuleSetAsync(SetId, It.IsAny<CancellationToken>())).ReturnsAsync(procedureId);
        host.Access.Grant("sprk_procedures", procedureId, AccessRights.Read | AccessRights.Write);
        host.Access.Grant("sprk_rulesets", SetId, AccessRights.None);

        var response = await host.Client.SendAsync(Req(method, url, content: ProposalBody(url)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Be(missing.Body);
        host.Access.Calls.Should().NotContain(c => c.Set == "sprk_procedures");
        host.Service.Verify(s => s.ProcedureOfRuleSetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact(DisplayName = "089e: a fault reading the set's record home, after (A) passed, denies 403 system_failure and never reaches the service")]
    public async Task Get_WhenTheSetsHomeCannotBeRead_Is403SystemFailure()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.RecordHomeOfRuleSetAsync(SetId, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("simulated read fault"));

        var response = await host.Client.SendAsync(Req("GET", $"{Base}/{SetId}"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        host.VerifyNoServiceOperation();
    }

    private sealed class LineLogger(System.Collections.Concurrent.ConcurrentQueue<string> lines) : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue(formatter(state, exception));
    }
}
