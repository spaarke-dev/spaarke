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
using Sprk.Bff.Api.Services.Finance.Files;
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
        { "POST", $"{Base}/{ProcedureId}/versions/{VersionId}/draft" },
        { "PUT", $"{Base}/{ProcedureId}/versions/{VersionId}/file" },
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
        { "POST", $"{Base}/{ProcedureId}/versions/{VersionId}/draft" },
        { "PUT", $"{Base}/{ProcedureId}/versions/{VersionId}/file" },
        { "POST", $"{Base}/{ProcedureId}/retire" },
    };

    private static readonly string[] AdminPrivileges =
    {
        BillingAdminAuthorization.CreateProcedurePrivilege, BillingAdminAuthorization.CreateRuleSetPrivilege,
        BillingAdminAuthorization.WriteRuleSetPrivilege, BillingAdminAuthorization.CreatePolicyVersionPrivilege,
        BillingAdminAuthorization.WritePolicyVersionPrivilege, BillingAdminAuthorization.WritePolicyPrivilege,
        BillingAdminAuthorization.CreatePolicyPrivilege, // task 116: the /draft route (DraftRules)
    };

    /// <summary>Fix round 4 (EB-D-216): the table Delete privileges /draft asked in round 2, and asks no more.</summary>
    private const string DeletePolicyPrivilege = "prvDeletesprk_Policy";
    private const string DeletePolicyVersionPrivilege = "prvDeletesprk_PolicyVersion";

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

        /// <summary>The Dataverse seam the rate card privilege filter reads the Procedure's type through (085 fix round 1); by default it finds no Procedure.</summary>
        public readonly Mock<IGenericEntityService> Entities = new();
        public readonly FinAuth.RecordingAccessDataSource Access = new();
        public readonly FinAuth.RecordingPrivilegeProbe Probe = new();

        /// <summary>Task 116: the drafting route's rule set store and queue, and the file route's REAL file store over its fakes.</summary>
        public readonly Mock<RuleSetService> RuleSets = NewRuleSets();
        public readonly Mock<Sprk.Bff.Api.Services.Jobs.JobSubmissionService> Jobs = NewJobs();
        public Sprk.Bff.Api.Tests.Finance.Files.StoredFileRig Files { get; private set; } = new();

        /// <summary>Fix round 5 (F2): the drafting job's per-version lock, the real store over an in-memory cache, so a test can hold it as a run does.</summary>
        public readonly Sprk.Bff.Api.Services.Jobs.IdempotencyService Locks = NewLocks();

        /// <summary>The REAL caller-rights evaluator over a recording data source (fix round 5: the /draft handler's reject-rights calls).</summary>
        internal static AuthorizationService NewAuthorization(IAccessDataSource access) => new(
            access, new IAuthorizationRule[] { new OperationAccessRule(NullLogger<OperationAccessRule>.Instance) }, NullLogger<AuthorizationService>.Instance);

        internal static Sprk.Bff.Api.Services.Jobs.IdempotencyService NewLocks() => new(
            new Microsoft.Extensions.Caching.Distributed.MemoryDistributedCache(
                Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryDistributedCacheOptions())),
            NullLogger<Sprk.Bff.Api.Services.Jobs.IdempotencyService>.Instance);

        private WebApplication _app = null!;
        public HttpClient Client = null!;
        public IServiceProvider Services => _app.Services;

        public static async Task<Host> StartAsync(
            bool ebillingEnabled = true, bool admin = true, int maxFileMb = 25, bool storeConfigured = true,
            Func<CancellationToken, Task<Guid>>? organizationId = null)
        {
            var host = new Host();
            if (!storeConfigured)
            {
                host.Files = new Sprk.Bff.Api.Tests.Finance.Files.StoredFileRig(configured: false); // fix round 2: a store with no configuration
            }
            else if (organizationId is not null)
            {
                // Fix round 3: a configured store whose environment check answers as given (another environment: the stage refuses).
                host.Files = new Sprk.Bff.Api.Tests.Finance.Files.StoredFileRig(organizationId: organizationId);
            }

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
            builder.Services.AddOptions<EbillingOptions>().Configure(o =>
            {
                o.Enabled = ebillingEnabled;
                o.MaxSubmissionFileSizeMb = maxFileMb;
            });
            // Fix round 4 (EB-D-216): by default a /draft supersedes nothing (a first draft: no deactivation).
            host.RuleSets.Setup(r => r.PlanRedraftAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(RuleSetOutcome<RedraftPlan>.Done(new RedraftPlan(Array.Empty<SupersededDraft>())));
            // Fix round 5 (EB-D-216a): /draft asks the reject rights on the Procedure's rule set (SetId, of ProcedureId, not record-scoped).
            host.RuleSets.Setup(r => r.ProcedureRuleSetIdAsync(ProcedureId, It.IsAny<CancellationToken>())).ReturnsAsync(SetId);
            host.RuleSets.Setup(r => r.ProcedureOfRuleSetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(ProcedureId);
            host.RuleSets.Setup(r => r.RecordHomeOfRuleSetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((RecordRuleSetHome?)null);
            builder.Services.AddSingleton<Sprk.Bff.Api.Services.Jobs.IIdempotencyService>(host.Locks);
            builder.Services.AddSingleton(host.RuleSets.Object);
            builder.Services.AddSingleton(host.Jobs.Object);
            builder.Services.AddSingleton(host.Files.Service);
            builder.Services.AddSingleton<IAccessDataSource>(host.Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();
            builder.Services.AddSingleton<CallerRecordAccessProbe>(host.Probe);
            builder.Services.AddSingleton(host.Service.Object);
            host.Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntityCollection());
            builder.Services.AddSingleton(host.Entities.Object);
            if (admin)
            {
                host.GrantAdmin();
            }

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.Use(async (context, next) =>
            {
                if (host.BodyOverTheServerCap)
                {
                    context.Request.Body = new OverTheCapBody();
                }

                await next(context);
            });
            host._app.MapProcedureEndpoints();
            await host._app.StartAsync();
            host.Client = host._app.GetTestClient();
            return host;
        }

        /// <summary>The stored Procedure <see cref="ProcedureId"/> is of <paramref name="type"/>.</summary>
        public void ProcedureIs(ProcedureType type) =>
            Entities.Setup(e => e.RetrieveMultipleAsync(
                    It.Is<QueryExpression>(q => q.EntityName == "sprk_procedure" && q.Criteria.Conditions.Any(c => c.Values.Contains(ProcedureId))),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EntityCollection(new List<Entity>
                {
                    new("sprk_procedure", ProcedureId) { ["sprk_proceduretype"] = new OptionSetValue((int)type) },
                }));

        /// <summary>A billing admin with Read and Write on the Procedure.</summary>
        public void GrantAdmin()
        {
            foreach (var p in AdminPrivileges)
            {
                Probe.Hold(p);
            }

            Access.Grant(ProcedureSet, ProcedureId, AccessRights.Read | AccessRights.Write);

            // Fix round 5 (EB-D-216a): /draft asks Read on the Procedure's rule set, as the reject route does.
            Access.Grant("sprk_rulesets", SetId, AccessRights.Read);

            // Fix round 1: the version routes ask the caller's own rights on the version first (the file store's check).
            Files.Probe.Grant(StoredFileSchema.ProcedureVersionEntitySet, VersionId, AccessRights.Read | AccessRights.Write);
        }

        /// <summary>Fix round 1 (F4): the request body throws the server's 413 when read, as Kestrel does past its body cap.</summary>
        public bool BodyOverTheServerCap { get; set; }

        private static Mock<RuleSetService> NewRuleSets()
        {
            var entities = Mock.Of<IGenericEntityService>();
            var schema = new RuleBodySchemaValidator();
            var validator = new PolicyVersionValidator(schema, new PredicateCompiler(schema, TimeProvider.System), NullLogger<PolicyVersionValidator>.Instance);
            return new Mock<RuleSetService>(
                MockBehavior.Strict,
                entities, Mock.Of<IRecordOwnershipResolver>(), validator,
                new RuleBodyDescriber(validator, entities, NullLogger<RuleBodyDescriber>.Instance),
                Mock.Of<Spaarke.Scheduling.IScheduledJobLease>(), Mock.Of<ILogger<RuleSetService>>());
        }

        internal static Mock<Sprk.Bff.Api.Services.Jobs.JobSubmissionService> NewJobs() => new(
            MockBehavior.Strict,
            Microsoft.Extensions.Options.Options.Create(new ServiceBusOptions
            {
                QueueName = "test-jobs",
                CommunicationQueueName = "test-comms",
                ConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v",
            }),
            Mock.Of<ILogger<Sprk.Bff.Api.Services.Jobs.JobSubmissionService>>(),
            new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object);

        /// <summary>Task 116: the routes' own services were never reached (the procedure store, the rule set store, the queue, the file store).</summary>
        public void VerifyNothingReached()
        {
            Service.VerifyNoOtherCalls();
            RuleSets.VerifyNoOtherCalls();
            Jobs.VerifyNoOtherCalls();
            Files.Dataverse.Calls.Should().BeEmpty();
            Files.Gateway.StorageCalls.Should().Be(0);
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

    /// <summary>A JSON body for a write; the file route accepts only multipart/form-data (routing answers 415 to anything else).</summary>
    private static HttpContent? BodyFor(string method, string url = "") =>
        url.EndsWith("/file", StringComparison.Ordinal) ? FileBody(1)
        : method is "PATCH" or "PUT" or "POST" ? JsonContent.Create(new { }) : null;

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

        var response = await host.Client.SendAsync(Req(method, url, authenticated: false, content: BodyFor(method, url)));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.VerifyNothingReached();
    }

    public static IEnumerable<object[]> AllRoutesRefused => StatedCaller.Cross(AllRoutes, StatedCaller.RefusedKinds);

    public static IEnumerable<object[]> AllRoutesInternal => StatedCaller.Cross(AllRoutes, StatedCaller.InternalKinds);

    [Theory(DisplayName = "081 must-fire: every Procedure route refuses a flagged-external, row-less or flag-fault caller at step 0, before any check or service call")]
    [MemberData(nameof(AllRoutesRefused))]
    public async Task EveryRoute_StepZero_RefusesBeforeAnyCheckOrServiceCall(string method, string url, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method, url)));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        host.Probe.Calls.Should().BeEmpty();
        host.Access.Calls.Should().BeEmpty();
        host.VerifyNothingReached();
    }

    [Theory(DisplayName = "081 must-not-fire: a flag that reads false or blank reaches the route's own check")]
    [MemberData(nameof(AllRoutesInternal))]
    public async Task EveryRoute_StepZero_AdmitsAnInternalCaller_ToTheRoutesOwnCheck(string method, string url, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync(admin: false);
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method, url)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (host.Probe.Calls.Count + host.Access.Calls.Count).Should().BePositive("the route asked Dataverse, as the caller");
        host.VerifyNothingReached();
    }

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task EveryWriteRoute_WithoutTheBillingAdminMarker_Returns403(string method, string url)
    {
        await using var host = await Host.StartAsync();
        host.Probe.Release(BillingAdminAuthorization.WriteRuleSetPrivilege);

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method, url)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.VerifyNothingReached();
    }

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task EveryWriteRoute_WithoutWriteOnThatProcedure_Returns403_EvenForABillingAdmin(string method, string url)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(ProcedureSet, ProcedureId, AccessRights.Read);

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method, url)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().Contain(c => c.Set == ProcedureSet && c.Id == ProcedureId && c.HasToken, "asked as the caller, about THAT Procedure");
        host.VerifyNothingReached();
    }

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task EveryWriteRoute_WhenTheRecordProbeFaults_Returns403(string method, string url)
    {
        await using var host = await Host.StartAsync();
        host.Access.ThrowOnEveryCall = new HttpRequestException("simulated Dataverse fault");

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method, url)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "fail closed");
        host.VerifyNothingReached();
    }

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task EveryWriteRoute_WhenThePrivilegeProbeFaults_Returns403(string method, string url)
    {
        await using var host = await Host.StartAsync();
        host.Probe.ThrowOnEveryCall = new HttpRequestException("simulated Dataverse fault");

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method, url)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "fail closed");
        host.VerifyNothingReached();
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
            Base + path.Replace("{p}", ProcedureId.ToString()).Replace("{v}", VersionId.ToString()), content: BodyFor(method, path)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.VerifyNothingReached();
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

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method, url)));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.VerifyNothingReached();
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_WhenEbillingIsOff_StillDeniesANonAdmin(string method, string url)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false, admin: false);

        var response = await host.Client.SendAsync(Req(method, url, content: BodyFor(method, url)));

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

    // ── 085 fix round 1 (F1-c): a Rate card Procedure's new version also creates cards, so it also needs Create on the card tables ──

    private static ProcedureVersionCreated Created() =>
        new(new ProcedureVersionView(VersionId, 2, "Draft", null, null, null), null, 0);

    private static Task<HttpResponseMessage> CreateVersion(Host host) =>
        host.Client.SendAsync(Req("POST", $"{Base}/{ProcedureId}/versions", content: JsonContent.Create(new { })));

    [Theory(DisplayName = "085 must-fire (F1-c): a new version of a RATE CARD Procedure without Create on the card or the rates is 403 insufficient_privilege and nothing is written")]
    [InlineData(RateCardImportEndpoints.CreateRateCardPrivilege)]
    [InlineData(RateCardImportEndpoints.CreateRatePrivilege)]
    public async Task CreateVersion_OfARateCardProcedure_WithoutCreateOnTheCardTables_Returns403_AndWritesNothing(string missing)
    {
        await using var host = await Host.StartAsync();
        host.ProcedureIs(ProcedureType.RateCard);
        host.Probe.Hold(RateCardImportEndpoints.CreateRateCardPrivilege);
        host.Probe.Hold(RateCardImportEndpoints.CreateRatePrivilege);
        host.Probe.Release(missing);

        var response = await CreateVersion(host);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.InsufficientPrivilegeReasonCode);
        host.Probe.Calls.Should().Contain(c => c.Privilege == missing, "the card privilege was asked of Dataverse as the caller");
        host.VerifyNothingReached();
    }

    [Fact(DisplayName = "085 must-not-fire (F1-c): the same caller creating a version of a GUIDELINE Procedure is allowed: no card is copied")]
    public async Task CreateVersion_OfAGuideline_WithoutCreateOnTheCardTables_IsAllowed()
    {
        await using var host = await Host.StartAsync();
        host.ProcedureIs(ProcedureType.Guideline); // GrantAdmin holds no card privilege
        host.Service.Setup(s => s.CreateVersionAsync(ProcedureId, null, null, It.IsAny<Guid?>(), ProcedureType.Guideline, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<ProcedureVersionCreated>.Done(Created()));

        var response = await CreateVersion(host);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Probe.Calls.Should().NotContain(c => c.Privilege == RateCardImportEndpoints.CreateRateCardPrivilege || c.Privilege == RateCardImportEndpoints.CreateRatePrivilege);
        host.Service.Verify(s => s.CreateVersionAsync(ProcedureId, null, null, It.IsAny<Guid?>(), ProcedureType.Guideline, It.IsAny<CancellationToken>()),
            Times.Once, "the type the check covered is the one handed to the service");
    }

    [Fact(DisplayName = "085 must-not-fire (F1-c): a caller with Create on both card tables creates a version of a Rate card Procedure")]
    public async Task CreateVersion_OfARateCardProcedure_WithTheCardPrivileges_IsAllowed()
    {
        await using var host = await Host.StartAsync();
        host.ProcedureIs(ProcedureType.RateCard);
        host.Probe.Hold(RateCardImportEndpoints.CreateRateCardPrivilege);
        host.Probe.Hold(RateCardImportEndpoints.CreateRatePrivilege);
        host.Service.Setup(s => s.CreateVersionAsync(ProcedureId, null, null, It.IsAny<Guid?>(), ProcedureType.RateCard, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<ProcedureVersionCreated>.Done(Created()));

        var response = await CreateVersion(host);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        host.Service.Verify(s => s.CreateVersionAsync(ProcedureId, null, null, It.IsAny<Guid?>(), ProcedureType.RateCard, It.IsAny<CancellationToken>()),
            Times.Once, "the type the check covered is the one handed to the service");
    }

    [Fact(DisplayName = "085 must-fire (F1-c): a version-create handler with no record of the type check (no checked type in HttpContext.Items) answers 403 system_failure and calls no service")]
    public async Task CreateVersion_WithNoRecordOfTheTypeCheck_IsSystemFailure_AndCallsNoService()
    {
        await using var host = await Host.StartAsync();
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext { TraceIdentifier = "trace-1" }; // the filter never ran

        var result = await ProcedureEndpoints.CreateVersion(ProcedureId, null, host.Service.Object, http, default);

        var problem = result.Should().BeAssignableTo<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(403);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        host.VerifyNothingReached();
    }

    [Fact(DisplayName = "085 (F1-c, fail closed): a fault reading the Procedure's type is 403 system_failure and nothing is written")]
    public async Task CreateVersion_WhenTheProceduresTypeCannotBeRead_Returns403SystemFailure()
    {
        await using var host = await Host.StartAsync();
        host.Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("simulated read fault"));

        var response = await CreateVersion(host);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        host.VerifyNothingReached();
    }

    [Fact(DisplayName = "085 (F1-c, fail closed): an unanswered privilege question on a Rate card Procedure is 403 system_failure, not insufficient_privilege")]
    public async Task CreateVersion_OfARateCardProcedure_WhenThePrivilegeQuestionIsUnanswered_Returns403SystemFailure()
    {
        await using var host = await Host.StartAsync();
        host.ProcedureIs(ProcedureType.RateCard);
        host.Probe.Hold(RateCardImportEndpoints.CreateRateCardPrivilege);
        host.Probe.Hold(RateCardImportEndpoints.CreateRatePrivilege);
        host.Probe.FaultFor.Add(RateCardImportEndpoints.CreateRatePrivilege);

        var response = await CreateVersion(host);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
        host.VerifyNothingReached();
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

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 116 (model 5.3, 7.2, 3.2, 4.2): POST …/versions/{v}/draft and PUT …/versions/{v}/file
    // ═══════════════════════════════════════════════════════════════════════════

    private static readonly Guid SetId = Guid.NewGuid();
    private static readonly Guid SourceFileId = Guid.NewGuid();
    private static string DraftUrl => $"{Base}/{ProcedureId}/versions/{VersionId}/draft";
    private static string FileUrl => $"{Base}/{ProcedureId}/versions/{VersionId}/file";
    private static readonly StoredFileParent VersionParent = new(StoredFileParentKind.ProcedureVersion, VersionId);

    private static DraftSource Source(bool drafted = false) => new(VersionId, ProcedureId, SetId, SourceFileId, "ocg.pdf", drafted);

    private static MultipartFormDataContent FileBody(int bytes, string name = "ocg.pdf")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Enumerable.Repeat((byte)'%', bytes).ToArray());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(file, "file", name);
        return content;
    }

    private static void TheVersionIsADraft(Host host)
    {
        host.Service.Setup(s => s.GetDraftVersionForFileAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<ProcedureVersionView>.Done(new ProcedureVersionView(VersionId, 1, "Draft", null, null, null)));
        host.Service.Setup(s => s.ResetDraftedStateIfDraftAsync(VersionId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        host.Service.Setup(s => s.RecheckDraftAfterNewFileAsync(VersionId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    /// <summary>A request body that throws the server's 413 on its first read (what Kestrel does past the route's body cap).</summary>
    internal sealed class OverTheCapBody : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw TooLarge();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(TooLarge());
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromException<int>(TooLarge());
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        private static Microsoft.AspNetCore.Http.BadHttpRequestException TooLarge() => new("Request body too large.", Microsoft.AspNetCore.Http.StatusCodes.Status413PayloadTooLarge);
    }

    private static StoredFileStatus Status(Host host, Guid id) =>
        (StoredFileStatus)host.Files.Dataverse.Rows[id].GetAttributeValue<OptionSetValue>(StoredFileSchema.Status).Value;

    [Theory(DisplayName = "116 must-fire: /draft needs every DraftRules privilege (the rule set marker and Create on both policy tables)")]
    [InlineData(BillingAdminAuthorization.WriteRuleSetPrivilege)]
    [InlineData(BillingAdminAuthorization.CreatePolicyPrivilege)]
    [InlineData(BillingAdminAuthorization.CreatePolicyVersionPrivilege)]
    public async Task Draft_WithoutADraftRulesPrivilege_Is403_AndReachesNothing(string missing)
    {
        await using var host = await Host.StartAsync();
        host.Probe.Release(missing);

        var response = await host.Client.SendAsync(Req("POST", DraftUrl, content: BodyFor("POST")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.VerifyNothingReached(); // nothing read, nothing removed, nothing queued
    }

    [Fact(DisplayName = "116 fix 4 (EB-D-216) must-fire: /draft asks no Delete privilege; without either table Delete it still queues (202)")]
    public async Task Draft_AsksNoDeletePrivilege()
    {
        await using var host = await Host.StartAsync();
        host.Probe.Release(DeletePolicyPrivilege);
        host.Probe.Release(DeletePolicyVersionPrivilege);
        host.RuleSets.Setup(r => r.GetDraftSourceAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Done(Source()));
        host.Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<Sprk.Bff.Api.Services.Jobs.JobContract>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var response = await host.Client.SendAsync(Req("POST", DraftUrl, content: BodyFor("POST")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "a redraft deactivates, so no table Delete is needed");
        host.Probe.Calls.Select(c => c.Privilege).Should().NotContain(new[] { DeletePolicyPrivilege, DeletePolicyVersionPrivilege });
    }

    [Fact(DisplayName = "116 must-not-fire: /draft with Write on the Procedure and the DraftRules privileges queues the version's job (202, Location = the rule set read)")]
    public async Task Draft_QueuesTheVersionsJob_AndReturns202()
    {
        await using var host = await Host.StartAsync();
        host.RuleSets.Setup(r => r.GetDraftSourceAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Done(Source()));
        Sprk.Bff.Api.Services.Jobs.JobContract? queued = null;
        host.Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<Sprk.Bff.Api.Services.Jobs.JobContract>(), It.IsAny<CancellationToken>()))
            .Callback((Sprk.Bff.Api.Services.Jobs.JobContract job, CancellationToken _) => queued = job)
            .Returns(Task.CompletedTask);

        var response = await host.Client.SendAsync(Req("POST", DraftUrl, content: BodyFor("POST")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location!.ToString().Should().Be($"/api/finance/rulesets/{SetId}");
        var body = await Json(response);
        body["procedureVersionId"]!.GetValue<Guid>().Should().Be(VersionId);
        body["ruleSetId"]!.GetValue<Guid>().Should().Be(SetId);
        queued!.SubjectId.Should().Be(VersionId.ToString());
        queued.IdempotencyKey.Should().Be(VersionId.ToString());
        queued.Payload!.RootElement.GetProperty("procedureVersionId").GetGuid().Should().Be(VersionId);
        host.Access.Calls.Should().Contain(c => c.Set == ProcedureSet && c.Id == ProcedureId && c.HasToken, "Write on THAT Procedure, asked as the caller");
    }

    [Theory(DisplayName = "116: /draft answers a refused source now (another Procedure's version is the unknown-id 404) and queues nothing")]
    [InlineData(404, "procedure.version_not_found")]
    [InlineData(400, "procedure.not_billing")]
    [InlineData(409, "procedure_version.not_draft")]
    [InlineData(409, "procedure.retired")]
    [InlineData(409, "procedure.ruleset_missing")]
    [InlineData(422, "ruleset.source_missing")]
    public async Task Draft_ARefusedSource_IsAnsweredNow_AndNoJobIsQueued(int status, string code)
    {
        await using var host = await Host.StartAsync();
        host.RuleSets.Setup(r => r.GetDraftSourceAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Refused(status, code, "refused"));

        var response = await host.Client.SendAsync(Req("POST", DraftUrl, content: BodyFor("POST")));

        ((int)response.StatusCode).Should().Be(status);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(code);
        host.Jobs.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "116: /draft on a drafted version is 409 ruleset.already_drafted; a queue fault is 503 ruleset.draft_not_queued")]
    public async Task Draft_AlreadyDrafted_Is409_AndAQueueFault_Is503()
    {
        await using var host = await Host.StartAsync();
        host.RuleSets.SetupSequence(r => r.GetDraftSourceAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Done(Source(drafted: true)))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Done(Source()));
        host.Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<Sprk.Bff.Api.Services.Jobs.JobContract>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated Service Bus outage"));

        var drafted = await host.Client.SendAsync(Req("POST", DraftUrl, content: BodyFor("POST")));
        var down = await host.Client.SendAsync(Req("POST", DraftUrl, content: BodyFor("POST")));

        drafted.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(drafted))["errorCode"]!.GetValue<string>().Should().Be(RuleSetService.CodeAlreadyDrafted);
        down.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(down))["errorCode"]!.GetValue<string>().Should().Be(ProcedureEndpoints.CodeDraftNotQueued);
        host.Jobs.Verify(j => j.SubmitJobAsync(It.IsAny<Sprk.Bff.Api.Services.Jobs.JobContract>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Fix round 5 (EB-D-216a, F2): the reject rights, the scope assertion and the job's lock ─────────────────────────────────────

    private static SupersededDraft Superseded(Guid? ruleSet = null, Guid? procedureVersion = null) =>
        new(Guid.NewGuid(), new[] { Guid.NewGuid() }, ruleSet ?? SetId, new[] { procedureVersion ?? VersionId });

    [Fact(DisplayName = "116 fix 5 (EB-D-216a) must-fire: /draft without DecideRule()'s Write on sprk_policyversion is 403; nothing read, deactivated or queued")]
    public async Task Draft_WithoutTheDecideRulePrivilege_Is403_AndReachesNothing()
    {
        await using var host = await Host.StartAsync();
        host.Probe.Release(BillingAdminAuthorization.WritePolicyVersionPrivilege);

        var response = await host.Client.SendAsync(Req("POST", DraftUrl, content: BodyFor("POST")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Probe.Calls.Select(c => c.Privilege).Should().Contain(BillingAdminAuthorization.WritePolicyVersionPrivilege, "a reject needs it, so a redraft does");
        host.VerifyNothingReached(); // no draft state read, nothing deactivated, nothing queued
    }

    [Fact(DisplayName = "116 fix 5 (EB-D-216a) must-not-fire: /draft asks every DecideRule() privilege on its declaration, as the reject route does")]
    public void Draft_DeclaresEveryDecideRulePrivilege()
    {
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Request.RouteValues["procedureId"] = ProcedureId.ToString();
        var context = new Microsoft.AspNetCore.Http.DefaultEndpointFilterInvocationContext(http);

        var declared = BillingAdminAuthorization.DraftProcedureVersion(context).Checks;

        declared.Should().Contain(BillingAdminAuthorization.DecideRule().Checks);
        declared.Should().Contain(BillingAdminAuthorization.DraftRules().Checks);
        declared.Should().OnlyHaveUniqueItems();
    }

    [Theory(DisplayName = "116 fix 5 (EB-D-216a item 3) must-fire: a superseded draft outside the checked rule set or this version refuses the whole call 409, nothing deactivated or queued")]
    [InlineData("ruleset")]
    [InlineData("version")]
    public async Task Draft_ASupersededDraftOutsideTheCheckedScope_Is409_AndWritesNothing(string outside)
    {
        await using var host = await Host.StartAsync();
        host.RuleSets.Setup(r => r.GetDraftSourceAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Done(Source()));
        var stray = outside == "ruleset" ? Superseded(ruleSet: Guid.NewGuid()) : Superseded(procedureVersion: Guid.NewGuid());
        host.RuleSets.Setup(r => r.PlanRedraftAsync(VersionId, SourceFileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<RedraftPlan>.Done(new RedraftPlan(new[] { Superseded(), stray })));

        var response = await host.Client.SendAsync(Req("POST", DraftUrl, content: BodyFor("POST")));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(ProcedureEndpoints.CodeRedraftScopeMismatch);
        host.RuleSets.Verify(r => r.DeactivateSupersededDraftsAsync(It.IsAny<Guid>(), It.IsAny<RedraftPlan>(), It.IsAny<CancellationToken>()), Times.Never);
        host.Jobs.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "116 fix 5 (EB-D-216a item 3) must-fire: a version whose rule set is not the one the rights were asked on is 409, before any plan")]
    public async Task Draft_TheVersionsRuleSetIsNotTheCheckedOne_Is409()
    {
        await using var host = await Host.StartAsync();
        host.RuleSets.Setup(r => r.GetDraftSourceAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Done(Source() with { RuleSetId = Guid.NewGuid() }));

        var response = await host.Client.SendAsync(Req("POST", DraftUrl, content: BodyFor("POST")));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(ProcedureEndpoints.CodeRedraftScopeMismatch);
        host.RuleSets.Verify(r => r.PlanRedraftAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        host.Jobs.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "116 fix 5 (F2, fix b) must-fire: while a drafting run holds the version's lock, /draft is 409 drafting_in_progress and plans, deactivates and queues nothing")]
    public async Task Draft_WhileARunHoldsTheLock_Is409_AndTouchesNothing()
    {
        await using var host = await Host.StartAsync();
        host.RuleSets.Setup(r => r.GetDraftSourceAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Done(Source()));
        (await host.Locks.TryAcquireProcessingLockAsync(
            BillingRuleDraftJobHandlerLockKey(), Sprk.Bff.Api.Services.Jobs.Handlers.BillingRuleDraftJobHandler.LockDuration)).Should().BeTrue("the run takes its lock");

        var response = await host.Client.SendAsync(Req("POST", DraftUrl, content: BodyFor("POST")));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(ProcedureEndpoints.CodeDraftingInProgress);
        host.RuleSets.Verify(r => r.PlanRedraftAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        host.RuleSets.Verify(r => r.DeactivateSupersededDraftsAsync(It.IsAny<Guid>(), It.IsAny<RedraftPlan>(), It.IsAny<CancellationToken>()), Times.Never);
        host.Jobs.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "116 fix 5 (F2, fix b) must-not-fire: /draft releases the version's lock once the run is queued, so the run can take it")]
    public async Task Draft_ReleasesTheLock_SoTheRunCanTakeIt()
    {
        await using var host = await Host.StartAsync();
        host.RuleSets.Setup(r => r.GetDraftSourceAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Done(Source()));
        var heldWhileQueueing = false;
        host.Jobs.Setup(j => j.SubmitJobAsync(It.IsAny<Sprk.Bff.Api.Services.Jobs.JobContract>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                heldWhileQueueing = !await host.Locks.TryAcquireProcessingLockAsync(BillingRuleDraftJobHandlerLockKey(), TimeSpan.FromSeconds(1));
            });

        var response = await host.Client.SendAsync(Req("POST", DraftUrl, content: BodyFor("POST")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        heldWhileQueueing.Should().BeTrue("the plan, the deactivation and the queueing run under the lock");
        (await host.Locks.TryAcquireProcessingLockAsync(BillingRuleDraftJobHandlerLockKey(), TimeSpan.FromSeconds(1))).Should().BeTrue("released after");
    }

    private static string BillingRuleDraftJobHandlerLockKey() => Sprk.Bff.Api.Services.Jobs.Handlers.BillingRuleDraftJobHandler.LockKeyFor(VersionId);

    [Fact(DisplayName = "116 must-fire: PUT /file over the size limit by Content-Length is 413 before the body is read; nothing stored")]
    public async Task File_OverTheLimitByContentLength_Is413_BeforeAnything()
    {
        await using var host = await Host.StartAsync(maxFileMb: 1);
        TheVersionIsADraft(host);

        var response = await host.Client.SendAsync(Req("PUT", FileUrl, content: FileBody((2 * 1024 * 1024) + 10)));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(ProcedureEndpoints.CodeFileTooLarge);
        host.Service.Verify(s => s.ResetDraftedStateIfDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        host.Files.Dataverse.Calls.Should().BeEmpty();
        host.Files.Gateway.StorageCalls.Should().Be(0);
    }

    [Fact(DisplayName = "116 must-fire: PUT /file whose FILE is over the limit (Content-Length within the slack) is 413 and stores nothing")]
    public async Task File_OverTheLimitByFileLength_Is413_AndStoresNothing()
    {
        await using var host = await Host.StartAsync(maxFileMb: 1);
        TheVersionIsADraft(host);

        var response = await host.Client.SendAsync(Req("PUT", FileUrl, content: FileBody((1024 * 1024) + 512)));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(ProcedureEndpoints.CodeFileTooLarge);
        host.Files.Dataverse.Calls.Should().BeEmpty();
        host.Files.Gateway.StorageCalls.Should().Be(0);
    }

    [Theory(DisplayName = "116: PUT /file on a version that is not a Draft (a missing status included), retired, or of another Procedure stores nothing")]
    [InlineData(409, "procedure_version.not_draft")]
    [InlineData(409, "procedure.retired")]
    [InlineData(404, "procedure.version_not_found")]
    public async Task File_ARefusedVersion_IsAnswered_AndStoresNothing(int status, string code)
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.GetDraftVersionForFileAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<ProcedureVersionView>.Refused(status, code, "refused"));

        var response = await host.Client.SendAsync(Req("PUT", FileUrl, content: FileBody(10)));

        ((int)response.StatusCode).Should().Be(status);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(code);
        host.Files.Dataverse.Calls.Should().BeEmpty();
        host.Files.Gateway.StorageCalls.Should().Be(0);
    }

    [Fact(DisplayName = "116: PUT /file stores the version's file and replaces the earlier one: the new row is Servable, the earlier Removed")]
    public async Task File_StoresTheVersionsFile_AndReplacesTheEarlierOne()
    {
        await using var host = await Host.StartAsync();
        TheVersionIsADraft(host);
        host.Files.Probe.Grant(StoredFileSchema.ProcedureVersionEntitySet, VersionId, AccessRights.Read | AccessRights.Write);
        var earlier = Guid.NewGuid();
        host.Files.SeedFile(earlier, VersionParent, new byte[] { 1 }, purpose: StoredFilePurpose.ProcedureSource);

        var response = await host.Client.SendAsync(Req("PUT", FileUrl, content: FileBody(10)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Json(response);
        body["procedureVersionId"]!.GetValue<Guid>().Should().Be(VersionId);
        var created = body["fileId"]!.GetValue<Guid>();
        Status(host, created).Should().Be(StoredFileStatus.Servable);
        Status(host, earlier).Should().Be(StoredFileStatus.Removed);
        host.Files.Dataverse.Rows[created].GetAttributeValue<EntityReference>(StoredFileSchema.ProcedureVersionTable).Id.Should().Be(VersionId);
    }

    [Fact(DisplayName = "116 must-fire: PUT /file when the earlier file cannot be withdrawn is 500 file_write_failed; the new file is withdrawn, the earlier stays Servable")]
    public async Task File_AFailedWithdraw_Is500_AndTheEarlierFileStaysServable()
    {
        await using var host = await Host.StartAsync();
        TheVersionIsADraft(host);
        host.Files.Probe.Grant(StoredFileSchema.ProcedureVersionEntitySet, VersionId, AccessRights.Read | AccessRights.Write);
        var earlier = Guid.NewGuid();
        host.Files.SeedFile(earlier, VersionParent, new byte[] { 1 }, purpose: StoredFilePurpose.ProcedureSource);
        host.Files.Dataverse.Mock.Setup(e => e.UpdateIfRowVersionMatchesAsync(
                StoredFileSchema.Entity, earlier, It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated Dataverse 429"));

        var response = await host.Client.SendAsync(Req("PUT", FileUrl, content: FileBody(10)));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(ProcedureEndpoints.CodeFileWriteFailed);
        Status(host, earlier).Should().Be(StoredFileStatus.Servable);
        var created = host.Files.Dataverse.Rows.Keys.Where(id => id != earlier).Should().ContainSingle().Subject;
        Status(host, created).Should().Be(StoredFileStatus.Removed);
    }

    [Fact(DisplayName = "116 must-fire: PUT /file without the caller's own Write on the VERSION is 403 insufficient_rights from the file store; nothing stored")]
    public async Task File_WithoutWriteOnTheVersion_Is403_AndStoresNothing()
    {
        await using var host = await Host.StartAsync();
        TheVersionIsADraft(host);
        host.Files.Probe.Grant(StoredFileSchema.ProcedureVersionEntitySet, VersionId, AccessRights.Read);

        var response = await host.Client.SendAsync(Req("PUT", FileUrl, content: FileBody(10)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.InsufficientRightsReasonCode);
        host.Files.Dataverse.Writes.Should().Be(0);
        host.Files.Gateway.StorageCalls.Should().Be(0);
    }

    [Fact(DisplayName = "116: PUT /file with no file part is 400 procedure_version.file_missing")]
    public async Task File_WithNoFile_Is400()
    {
        await using var host = await Host.StartAsync();
        TheVersionIsADraft(host);

        var response = await host.Client.SendAsync(Req("PUT", FileUrl, content: new MultipartFormDataContent { { new StringContent("x"), "note" } }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(ProcedureEndpoints.CodeFileMissing);
        host.Files.Dataverse.Calls.Should().BeEmpty();
    }

    [Fact(DisplayName = "116: PUT /file stores the media type derived from the file name, never the client's claimed Content-Type")]
    public async Task File_StoresTheServerDerivedMediaType_NotTheClientsClaim()
    {
        await using var host = await Host.StartAsync();
        TheVersionIsADraft(host);
        host.Files.Probe.Grant(StoredFileSchema.ProcedureVersionEntitySet, VersionId, AccessRights.Read | AccessRights.Write);
        var body = new MultipartFormDataContent();
        var part = new ByteArrayContent(new byte[] { 1, 2, 3 });
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/html");
        body.Add(part, "file", "ocg.pdf");

        var response = await host.Client.SendAsync(Req("PUT", FileUrl, content: body));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = (await Json(response))["fileId"]!.GetValue<Guid>();
        host.Files.Dataverse.Rows[created].GetAttributeValue<string>(StoredFileSchema.ContentType).Should().Be("application/pdf");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 116 fix round 1
    // ═══════════════════════════════════════════════════════════════════════════

    private static void TheVersionIsUnknown(Host host) =>
        host.Service.Setup(s => s.GetDraftVersionForFileAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<ProcedureVersionView>.Refused(404, ProcedureService.CodeVersionNotFound, "The version was not found in this Procedure."));

    private static void NoRightsOnTheVersion(Host host, AccessRights rights = AccessRights.None) =>
        host.Files.Probe.Grant(StoredFileSchema.ProcedureVersionEntitySet, VersionId, rights);

    [Theory(DisplayName = "116 fix 1 must-fire: PUT /file on a version the caller cannot read answers exactly as an unknown version, for a non-Draft version and an oversize body")]
    [InlineData("not-draft")]
    [InlineData("oversize")]
    public async Task File_AVersionTheCallerCannotRead_AnswersAsAnUnknownVersion(string shape)
    {
        await using var host = await Host.StartAsync(maxFileMb: 1);
        HttpContent Body() => shape == "oversize" ? FileBody((2 * 1024 * 1024) + 10) : FileBody(10);

        TheVersionIsUnknown(host);
        var unknown = await host.Client.SendAsync(Req("PUT", FileUrl, content: Body()));

        if (shape == "oversize")
        {
            TheVersionIsADraft(host);
        }
        else
        {
            host.Service.Setup(s => s.GetDraftVersionForFileAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(RuleSetOutcome<ProcedureVersionView>.Refused(409, RuleSetService.CodeVersionNotDraft, "A file can be stored only on a Draft version."));
        }

        NoRightsOnTheVersion(host);
        var hidden = await host.Client.SendAsync(Req("PUT", FileUrl, content: Body()));

        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        hidden.StatusCode.Should().Be(unknown.StatusCode);
        (await hidden.Content.ReadAsStringAsync()).Should().Be(await unknown.Content.ReadAsStringAsync());
        host.Files.Dataverse.Calls.Should().BeEmpty();
        host.Files.Gateway.StorageCalls.Should().Be(0);
    }

    [Fact(DisplayName = "116 fix 1 must-fire: PUT /file with Read but no Write on a non-Draft version is 403, decided before the 409")]
    public async Task File_ReadWithoutWrite_OnANonDraftVersion_Is403()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.GetDraftVersionForFileAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<ProcedureVersionView>.Refused(409, RuleSetService.CodeVersionNotDraft, "A file can be stored only on a Draft version."));
        NoRightsOnTheVersion(host, AccessRights.Read);

        var response = await host.Client.SendAsync(Req("PUT", FileUrl, content: FileBody(10)));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.InsufficientRightsReasonCode);
        host.Files.Dataverse.Calls.Should().BeEmpty();
    }

    [Fact(DisplayName = "116 fix 1 must-fire: /draft without Write on the version is 403 and queues nothing")]
    public async Task Draft_WithoutWriteOnTheVersion_Is403()
    {
        await using var host = await Host.StartAsync();
        host.RuleSets.Setup(r => r.GetDraftSourceAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Done(Source()));
        NoRightsOnTheVersion(host, AccessRights.Read);

        var response = await host.Client.SendAsync(Req("POST", DraftUrl, content: BodyFor("POST")));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.InsufficientRightsReasonCode);
        host.Jobs.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "116 fix 1 must-fire: /draft on a version the caller cannot read answers exactly as an unknown version (also before a 409)")]
    public async Task Draft_AVersionTheCallerCannotRead_AnswersAsAnUnknownVersion()
    {
        await using var host = await Host.StartAsync();
        host.RuleSets.Setup(r => r.GetDraftSourceAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Refused(404, ProcedureService.CodeVersionNotFound, "The Procedure Version was not found."));
        var unknown = await host.Client.SendAsync(Req("POST", DraftUrl, content: BodyFor("POST")));

        host.RuleSets.Setup(r => r.GetDraftSourceAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Refused(409, RuleSetService.CodeVersionNotDraft, "Rules are drafted only for a Draft version."));
        NoRightsOnTheVersion(host);
        var hidden = await host.Client.SendAsync(Req("POST", DraftUrl, content: BodyFor("POST")));

        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await hidden.Content.ReadAsStringAsync()).Should().Be(await unknown.Content.ReadAsStringAsync());
        host.Jobs.VerifyNoOtherCalls();
    }

    [Fact(DisplayName = "116 fix 1 (A2): PUT /file resets the drafted state BEFORE anything is staged, then re-checks once the new file is Servable")]
    public async Task File_ResetsTheDraftedState_BeforeStaging_AndAgainOnceServable()
    {
        await using var host = await Host.StartAsync();
        TheVersionIsADraft(host);
        var order = new List<string>();
        host.Service.Setup(s => s.ResetDraftedStateIfDraftAsync(VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                order.Add(host.Files.Gateway.StorageCalls == 0 ? "reset-before-staging" : "reset-after-staging");
                return true;
            });
        host.Service.Setup(s => s.RecheckDraftAfterNewFileAsync(VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                order.Add(host.Files.Gateway.StorageCalls == 0 ? "recheck-before-staging" : "recheck-after-staging");
                return true;
            });

        var response = await host.Client.SendAsync(Req("PUT", FileUrl, content: FileBody(10)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        order.Should().Equal("reset-before-staging", "recheck-after-staging");
    }

    [Fact(DisplayName = "116 fix 1 (A2) must-fire: PUT /file whose drafted-state reset fails is 500 and stages and stores nothing")]
    public async Task File_AFailedReset_Is500_AndStoresNothing()
    {
        await using var host = await Host.StartAsync();
        TheVersionIsADraft(host);
        var earlier = Guid.NewGuid();
        host.Files.SeedFile(earlier, VersionParent, new byte[] { 1 }, purpose: StoredFilePurpose.ProcedureSource);
        var seededCalls = host.Files.Gateway.StorageCalls;
        host.Service.Setup(s => s.ResetDraftedStateIfDraftAsync(VersionId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated Dataverse 503"));

        var response = await host.Client.SendAsync(Req("PUT", FileUrl, content: FileBody(10)));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(ProcedureEndpoints.CodeFileWriteFailed);
        host.Files.Dataverse.Writes.Should().Be(0);
        host.Files.Gateway.StorageCalls.Should().Be(seededCalls);
        Status(host, earlier).Should().Be(StoredFileStatus.Servable);
    }

    [Fact(DisplayName = "116 fix 1 (A1) must-fire: a version that stops being a Draft during the upload is 409 not_draft; the new file is withdrawn, the earlier stays Servable")]
    public async Task File_AVersionPublishedDuringTheUpload_Is409_AndTheEarlierFileStays()
    {
        await using var host = await Host.StartAsync();
        TheVersionIsADraft(host);
        host.Service.Setup(s => s.ResetDraftedStateIfDraftAsync(VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);   // before staging: still a Draft
        host.Service.Setup(s => s.RecheckDraftAfterNewFileAsync(VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);  // once the new file is Servable: published meanwhile
        var earlier = Guid.NewGuid();
        host.Files.SeedFile(earlier, VersionParent, new byte[] { 1 }, purpose: StoredFilePurpose.ProcedureSource);

        var response = await host.Client.SendAsync(Req("PUT", FileUrl, content: FileBody(10)));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(RuleSetService.CodeVersionNotDraft);
        Status(host, earlier).Should().Be(StoredFileStatus.Servable);
        var created = host.Files.Dataverse.Rows.Keys.Where(id => id != earlier).Should().ContainSingle().Subject;
        Status(host, created).Should().Be(StoredFileStatus.Removed);
    }

    [Fact(DisplayName = "116 fix 1 (F4) must-fire: PUT /file whose body the server stops at its cap (a chunked body) is 413 file_too_large, not 400")]
    public async Task File_ABodyOverTheServerCap_Is413()
    {
        await using var host = await Host.StartAsync();
        TheVersionIsADraft(host);
        host.BodyOverTheServerCap = true;

        var response = await host.Client.SendAsync(Req("PUT", FileUrl, content: FileBody(10)));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(ProcedureEndpoints.CodeFileTooLarge);
        host.Files.Dataverse.Calls.Should().BeEmpty();
    }

    [Fact(DisplayName = "116 fix 1 (F4): PUT /file carries the submission routes' body cap, above the server's 30 MB default")]
    public async Task File_CarriesTheRequestSizeLimit()
    {
        await using var host = await Host.StartAsync();
        var endpoint = host.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText!.EndsWith("/file", StringComparison.Ordinal));

        endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata>()!.MaxRequestBodySize
            .Should().Be(InvoiceSubmissionHttp.MaxRequestBodyBytes);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 116 fix round 2
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "116 fix 2 (item 6) must-fire: both version routes ask the caller's rights on the version for an unknown id too, then answer 404")]
    public async Task BothRoutes_AskTheVersionRights_ForAnUnknownVersionToo()
    {
        await using var host = await Host.StartAsync();
        TheVersionIsUnknown(host);
        host.RuleSets.Setup(r => r.GetDraftSourceAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Refused(404, ProcedureService.CodeVersionNotFound, "The Procedure Version was not found."));

        var file = await host.Client.SendAsync(Req("PUT", FileUrl, content: FileBody(10)));
        var asksAfterFile = host.Files.Probe.Calls.Count(c => c.Set == StoredFileSchema.ProcedureVersionEntitySet && c.Id == VersionId);
        var draft = await host.Client.SendAsync(Req("POST", DraftUrl, content: BodyFor("POST")));
        var asksAfterDraft = host.Files.Probe.Calls.Count(c => c.Set == StoredFileSchema.ProcedureVersionEntitySet && c.Id == VersionId);

        file.StatusCode.Should().Be(HttpStatusCode.NotFound);
        draft.StatusCode.Should().Be(HttpStatusCode.NotFound);
        asksAfterFile.Should().Be(1, "PUT /file asks the version rights before acting on the 404");
        asksAfterDraft.Should().Be(2, "/draft asks them too");
        host.Jobs.VerifyNoOtherCalls();
        host.Files.Dataverse.Calls.Should().BeEmpty();
    }

    [Fact(DisplayName = "116 fix 2 (item 3) must-fire: PUT /file with a name the store refuses is 400 before the drafted state is reset; nothing is written")]
    public async Task File_ARefusedName_Is400_BeforeTheReset()
    {
        await using var host = await Host.StartAsync();
        TheVersionIsADraft(host);

        var response = await host.Client.SendAsync(Req("PUT", FileUrl, content: FileBody(10, name: "folder/")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(ProcedureEndpoints.CodeFileMissing);
        host.Service.Verify(s => s.ResetDraftedStateIfDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        host.Files.Dataverse.Calls.Should().BeEmpty();
        host.Files.Gateway.StorageCalls.Should().Be(0);
    }

    [Fact(DisplayName = "116 fix 2 (item 3) must-fire: PUT /file with no configured file store is 503 before the drafted state is reset; nothing is written")]
    public async Task File_AnUnconfiguredStore_Is503_BeforeTheReset()
    {
        await using var host = await Host.StartAsync(storeConfigured: false);
        TheVersionIsADraft(host);

        var response = await host.Client.SendAsync(Req("PUT", FileUrl, content: FileBody(10)));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(StoredFileRefusal.NotConfigured);
        host.Service.Verify(s => s.ResetDraftedStateIfDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        host.Files.Dataverse.Calls.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 116 fix round 3
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "116 fix 3 (item 2, scenario B) must-fire: PUT /file to a store whose stage check fails (another environment) is 503 before the reset; nothing is written")]
    public async Task File_AStoreThatCannotStage_Is503_BeforeTheReset()
    {
        await using var host = await Host.StartAsync(organizationId: _ => Task.FromResult(Guid.NewGuid()));
        TheVersionIsADraft(host);

        var response = await host.Client.SendAsync(Req("PUT", FileUrl, content: FileBody(10)));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(StoredFileRefusal.NotConfigured);
        host.Service.Verify(s => s.ResetDraftedStateIfDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        host.Files.Dataverse.Writes.Should().Be(0);
        host.Files.Gateway.StorageCalls.Should().Be(0);
    }

    [Fact(DisplayName = "116 fix 3 (item 2): PUT /file whose store check faults is 500 before the reset; nothing is written")]
    public async Task File_AFaultedStoreCheck_Is500_BeforeTheReset()
    {
        await using var host = await Host.StartAsync(organizationId: _ => Task.FromException<Guid>(new InvalidOperationException("simulated Dataverse 503")));
        TheVersionIsADraft(host);

        var response = await host.Client.SendAsync(Req("PUT", FileUrl, content: FileBody(10)));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be(ProcedureEndpoints.CodeFileWriteFailed);
        host.Service.Verify(s => s.ResetDraftedStateIfDraftAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        host.Files.Dataverse.Writes.Should().Be(0);
        host.Files.Gateway.StorageCalls.Should().Be(0);
    }

    [Fact(DisplayName = "116 fix 3 (item 2) must-not-fire: PUT /file to a store whose environment check confirms it stores the file")]
    public async Task File_AStoreThatCanStage_StoresTheFile()
    {
        await using var host = await Host.StartAsync(organizationId: _ => Task.FromResult(Sprk.Bff.Api.Tests.Finance.Files.StoredFileRig.Tenant));
        TheVersionIsADraft(host);

        var response = await host.Client.SendAsync(Req("PUT", FileUrl, content: FileBody(10)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Service.Verify(s => s.ResetDraftedStateIfDraftAsync(VersionId, It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact(DisplayName = "116 fix 3 (item 3) must-fire: /draft on an unknown id and on a version the caller cannot read makes the same reads, in number and order, and gives the same answer")]
    public async Task Draft_AnUnknownAndAnUnreadableVersion_MakeTheSameReads()
    {
        var unknown = await DraftAgainstTheStoreDouble(versionExists: false);
        var unreadable = await DraftAgainstTheStoreDouble(versionExists: true);

        unknown.Status.Should().Be(404);
        unreadable.Status.Should().Be(unknown.Status);
        unreadable.Answer.Should().Be(unknown.Answer);
        unreadable.EntityReads.Should().Equal(unknown.EntityReads, "the same Dataverse reads, in number and order");
        unreadable.RightsAsked.Should().Equal(unknown.RightsAsked, "the same rights questions, in number and order");
        unreadable.FileStoreCalls.Should().Equal(unknown.FileStoreCalls);
    }

    [Fact(DisplayName = "116 fix 3 (item 3) must-not-fire: /draft with Read but no Write on a version of another Procedure still reads the version first and answers 404")]
    public async Task Draft_ReadWithoutWrite_OnAnotherProceduresVersion_Is404()
    {
        await using var host = await Host.StartAsync();
        host.RuleSets.Setup(r => r.GetDraftSourceAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<DraftSource>.Refused(404, ProcedureService.CodeVersionNotFound, "The Procedure Version was not found."));
        NoRightsOnTheVersion(host, AccessRights.Read);

        var response = await host.Client.SendAsync(Req("POST", DraftUrl, content: BodyFor("POST")));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.RuleSets.Verify(r => r.GetDraftSourceAsync(ProcedureId, VersionId, It.IsAny<CancellationToken>()), Times.Once);
        host.Jobs.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Fix round 3 (item 3): the /draft handler over the REAL rule set service and file store (their Dataverse doubles record every read),
    /// for a caller with no rights on the version: an id that names nothing, or a Draft version of a Billing Procedure with one source file.
    /// </summary>
    private static async Task<(int Status, string Answer, string[] EntityReads, string[] RightsAsked, string[] FileStoreCalls)> DraftAgainstTheStoreDouble(bool versionExists)
    {
        var h = new Sprk.Bff.Api.Tests.Services.Finance.RuleSetServiceTests.Harness();
        var d = h.DraftVersion();
        var versionId = versionExists ? d.Version : Guid.NewGuid();
        var rig = new Sprk.Bff.Api.Tests.Finance.Files.StoredFileRig(); // grants nothing on either id
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer caller-token";
        var jobs = Host.NewJobs();

        var result = await ProcedureEndpoints.DraftVersionRules(
            d.Procedure, versionId, h.Service(), jobs.Object, rig.Service, Host.NewAuthorization(new FinAuth.RecordingAccessDataSource()), Host.NewLocks(),
            http, NullLogger<RuleSetService>.Instance, CancellationToken.None);

        jobs.VerifyNoOtherCalls();
        var problem = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject.ProblemDetails;
        return (
            problem.Status ?? 0,
            $"{problem.Detail}|{problem.Extensions["errorCode"]}",
            h.Entities.Invocations.Select(i => i.Method.Name + ":" + (i.Arguments.FirstOrDefault() is QueryExpression q ? q.EntityName : i.Arguments.FirstOrDefault())).ToArray(),
            rig.Probe.Calls.Select(c => c.Set).ToArray(),
            rig.Dataverse.Calls.ToArray());
    }
}