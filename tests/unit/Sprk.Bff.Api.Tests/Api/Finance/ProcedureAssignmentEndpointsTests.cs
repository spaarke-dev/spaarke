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
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// The assignment and rule-exclude routes through the REAL <c>MapProcedureAssignmentEndpoints</c>, step 0 (task 099),
/// <see cref="FinanceAuthorizationFilter"/>, <see cref="AuthorizationService"/> and <see cref="EbillingEnabledFilter"/> (ebilling-r1 task
/// 089b, ADR-008, ADR-019): step 0 refuses a flagged-external, row-less or flag-fault caller on every route; every route needs the caller's
/// Write and AppendTo on THAT core record, answered as the uniform 404; then Read on the Procedure or rule it names and the billing-admin
/// marker (403). Every refusal writes nothing. The service is substituted at its own seam (its behaviour is
/// <see cref="Services.Finance.Procedures.ProcedureAssignmentServiceTests"/>).
/// </summary>
public sealed class ProcedureAssignmentEndpointsTests
{
    private static readonly Guid RecordId = Guid.NewGuid();
    private static readonly Guid ProcedureId = Guid.NewGuid();
    private static readonly Guid PolicyId = Guid.NewGuid();
    private const string ProcedureSet = "sprk_procedures";
    private const string PolicySet = "sprk_policies";

    /// <summary>(route segment, the record's entity set, its table) for the three core-record types.</summary>
    public static TheoryData<string, string, string> Cores => new()
    {
        { "matters", "sprk_matters", "sprk_matter" },
        { "projects", "sprk_projects", "sprk_project" },
        { "workassignments", "sprk_workassignments", "sprk_workassignment" },
    };

    private static readonly string[] Suffixes = { "/procedure-assignments", "/rule-overrides/exclude", "/rule-overrides/change" };

    /// <summary>All nine create routes: (url, the record's entity set).</summary>
    public static TheoryData<string, string> AllRoutes
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var row in Cores)
            {
                foreach (var suffix in Suffixes)
                {
                    data.Add($"/api/finance/{row[0]}/{RecordId}{suffix}", (string)row[1]);
                }
            }

            return data;
        }
    }

    private sealed class Host : IAsyncDisposable
    {
        public StatedCaller StepZero { get; private set; } = null!;
        // Task 089c: the constructor takes the rule-set service, the shared close and the counter; the routes never reach them (the service is substituted).
        public readonly Mock<ProcedureAssignmentService> Service = new(
            MockBehavior.Strict, Mock.Of<IGenericEntityService>(), Mock.Of<IRecordOwnershipResolver>(),
            (RuleSetService)RuntimeHelpers.GetUninitializedObject(typeof(RuleSetService)),
            (Sprk.Bff.Api.Services.Signals.SignalClosureService)RuntimeHelpers.GetUninitializedObject(typeof(Sprk.Bff.Api.Services.Signals.SignalClosureService)),
            (AffectedInvoiceCounter)RuntimeHelpers.GetUninitializedObject(typeof(AffectedInvoiceCounter)),
            NullLogger<ProcedureAssignmentService>.Instance);

        public readonly FinAuth.RecordingAccessDataSource Access = new();
        public readonly FinAuth.RecordingPrivilegeProbe Probe = new();
        private WebApplication _app = null!;
        public HttpClient Client = null!;

        public static async Task<Host> StartAsync(bool ebillingEnabled = true, bool admin = true, string? callerOid = null)
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
            // Step 0 only resolves a GUID oid, so a host whose token oid is not one asks the real resolver under the normalised oid (as the document fixtures do).
            host.StepZero = StatedCaller.Register(builder.Services, normaliseOid: callerOid is not null);
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
            if (callerOid is not null)
            {
                // The authenticated principal carries this oid instead of the handler's GUID (a token whose oid claim is not a GUID).
                host._app.Use(async (http, next) =>
                {
                    if (http.User.Identity?.IsAuthenticated == true)
                    {
                        http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                            new[] { new System.Security.Claims.Claim("oid", callerOid) }, FinanceAuthzTestAuthHandler.SchemeName));
                    }

                    await next();
                });
            }

            host._app.UseAuthorization();
            host._app.MapProcedureAssignmentEndpoints();
            await host._app.StartAsync();
            host.Client = host._app.GetTestClient();
            return host;
        }

        /// <summary>
        /// A billing admin with Write and AppendTo on every core record, Read on the Procedure and the rule, and the privileges a change and a
        /// removal add (task 089c).
        /// </summary>
        public void GrantAdmin()
        {
            Probe.Hold(BillingAdminAuthorization.WriteRuleSetPrivilege);
            foreach (var privilege in ChangePrivileges.Append(BillingAdminAuthorization.WritePolicyPrivilege))
            {
                Probe.Hold(privilege);
            }
            foreach (var row in Cores)
            {
                Access.Grant((string)row[1], RecordId, AccessRights.Read | AccessRights.Write | AccessRights.AppendTo);
            }

            Access.Grant(ProcedureSet, ProcedureId, AccessRights.Read);
            Access.Grant(PolicySet, PolicyId, AccessRights.Read);
        }

        public void NoWrites()
        {
            Service.Verify(s => s.AssignProcedureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ProcedureAssignmentRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            Service.Verify(s => s.ExcludeRuleAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<RuleExcludeRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            Service.Verify(s => s.ChangeRuleAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<RuleChangeRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
            Service.Verify(s => s.RemoveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
            Service.Verify(s => s.PreviewRemovalAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static HttpRequestMessage Post(string url, bool authenticated = true, HttpContent? content = null) =>
        RateCardImportEndpointsTests.Req("POST", url, authenticated, content ?? BodyFor(url));

    /// <summary>A body naming the Procedure or the rule the route checks Read on.</summary>
    private static HttpContent BodyFor(string url) =>
        url.EndsWith("/procedure-assignments", StringComparison.Ordinal)
            ? JsonContent.Create(new { procedureId = ProcedureId })
            : url.EndsWith("/change", StringComparison.Ordinal)
                ? JsonContent.Create(new { policyId = PolicyId, reason = "x", shortHeadline = "Cap" })
                : JsonContent.Create(new { policyId = PolicyId, reason = "x" });

    /// <summary>The privileges a change needs besides the billing-admin marker (Create and Write on rule set, Create and Write on policy, Create and Write on version).</summary>
    private static readonly string[] ChangePrivileges =
    {
        BillingAdminAuthorization.CreateRuleSetPrivilege, BillingAdminAuthorization.CreatePolicyPrivilege,
        BillingAdminAuthorization.CreatePolicyVersionPrivilege, BillingAdminAuthorization.WritePolicyVersionPrivilege,
        BillingAdminAuthorization.WritePolicyPrivilege,
    };

    private static async Task<JsonObject> Json(HttpResponseMessage r) => JsonNode.Parse(await r.Content.ReadAsStringAsync())!.AsObject();

    /// <summary>The response body without its per-request ids, for a byte comparison of two refusals.</summary>
    private static async Task<string> ShapeOf(HttpResponseMessage r)
    {
        var body = await Json(r);
        body.Remove("traceId");
        body.Remove("correlationId");
        return $"{(int)r.StatusCode} {r.Content.Headers.ContentType} {body.ToJsonString()}";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // 401 / step 0 / the record (uniform 404) / the subject and role (403) / disabled
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_Unauthenticated_Returns401_AndNeverReachesTheService(string url, string _)
    {
        await using var host = await Host.StartAsync();

        var response = await host.Client.SendAsync(Post(url, authenticated: false));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Service.VerifyNoOtherCalls();
    }

    public static IEnumerable<object[]> AllRoutesRefused => StatedCaller.Cross(AllRoutes, StatedCaller.RefusedKinds);

    [Theory(DisplayName = "089b must-fire: every override route refuses a flagged-external, row-less or flag-fault caller at step 0, before any check or service call")]
    [MemberData(nameof(AllRoutesRefused))]
    public async Task EveryRoute_StepZero_RefusesBeforeAnyCheckOrServiceCall(string url, string _, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Post(url));

        // The record filter's denial is the uniform 404, so step 0 answers in that shape (no existence oracle either way).
        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: true);
        host.Probe.Calls.Should().BeEmpty();
        host.Access.Calls.Should().BeEmpty();
        host.Service.VerifyNoOtherCalls();
    }

    public static IEnumerable<object[]> AllRoutesInternal => StatedCaller.Cross(AllRoutes, StatedCaller.InternalKinds);

    [Theory(DisplayName = "089b must-not-fire: a flag that reads false or blank reaches the record check, asked as the caller about THAT record")]
    [MemberData(nameof(AllRoutesInternal))]
    public async Task EveryRoute_StepZero_AdmitsAnInternalCaller_ToTheRecordCheck(string url, string set, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync(admin: false);
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Post(url));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.Access.Calls.Should().ContainSingle().Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, set, RecordId, HasToken: true));
        host.Service.VerifyNoOtherCalls();
    }

    [Theory(DisplayName = "089b must-fire: without Write AND AppendTo on the record, every route is the uniform 404, byte-identical to an unknown record, and writes nothing")]
    [InlineData(AccessRights.Read)]
    [InlineData(AccessRights.Read | AccessRights.Write)]
    [InlineData(AccessRights.Read | AccessRights.AppendTo)]
    public async Task EveryRoute_WithoutWriteAndAppendToOnTheRecord_IsTheUniform404_AndWritesNothing(AccessRights held)
    {
        foreach (var route in AllRoutes)
        {
            var (url, set) = ((string)route[0], (string)route[1]);

            await using var host = await Host.StartAsync();
            host.Access.Grant(set, RecordId, held);
            var response = await host.Client.SendAsync(Post(url));

            await using var unknown = await Host.StartAsync();
            var unknownUrl = url.Replace(RecordId.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal);
            var unknownResponse = await unknown.Client.SendAsync(Post(unknownUrl));

            response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{url} with {held}");
            (await ShapeOf(response)).Should().Be(await ShapeOf(unknownResponse), "no existence oracle");
            host.Access.Calls.Should().Contain(c => c.Set == set && c.Id == RecordId && c.HasToken, "asked as the caller, about THAT record");
            host.Probe.Calls.Should().BeEmpty("the record is checked first");
            host.Service.VerifyNoOtherCalls();
        }
    }

    [Theory(DisplayName = "089b must-fire: without the billing-admin privilege every route is 403 and writes nothing")]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_WithoutTheBillingAdminMarker_Returns403_AndWritesNothing(string url, string _)
    {
        await using var host = await Host.StartAsync();
        host.Probe.Release(BillingAdminAuthorization.WriteRuleSetPrivilege);

        var response = await host.Client.SendAsync(Post(url));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Probe.Calls.Should().Contain(c => c.Privilege == BillingAdminAuthorization.WriteRuleSetPrivilege && c.HasToken);
        host.NoWrites();
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_WhenTheRecordProbeFaults_FailsClosed(string url, string _)
    {
        await using var host = await Host.StartAsync();
        host.Access.ThrowOnEveryCall = new HttpRequestException("simulated Dataverse fault");

        var response = await host.Client.SendAsync(Post(url));

        response.IsSuccessStatusCode.Should().BeFalse("fail closed");
        host.NoWrites();
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_WhenThePrivilegeProbeFaults_FailsClosed(string url, string _)
    {
        await using var host = await Host.StartAsync();
        host.Probe.ThrowOnEveryCall = new HttpRequestException("simulated Dataverse fault");

        var response = await host.Client.SendAsync(Post(url));

        response.IsSuccessStatusCode.Should().BeFalse("fail closed");
        host.NoWrites();
    }

    [Theory(DisplayName = "089b must-fire: a billing admin with Write on the record but no Read on the Procedure is 403 and writes nothing")]
    [InlineData("matters")]
    [InlineData("projects")]
    [InlineData("workassignments")]
    public async Task Assign_WithoutReadOnTheProcedure_Returns403_AndWritesNothing(string segment)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(ProcedureSet, ProcedureId, AccessRights.None);

        var response = await host.Client.SendAsync(Post($"/api/finance/{segment}/{RecordId}/procedure-assignments"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().Contain(c => c.Set == ProcedureSet && c.Id == ProcedureId && c.HasToken);
        host.NoWrites();
    }

    [Theory(DisplayName = "089b must-fire: a billing admin with Write on the record but no Read on the rule is 403 and writes nothing")]
    [InlineData("matters")]
    [InlineData("projects")]
    [InlineData("workassignments")]
    public async Task ExcludeRule_WithoutReadOnTheRule_Returns403_AndWritesNothing(string segment)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(PolicySet, PolicyId, AccessRights.None);

        var response = await host.Client.SendAsync(Post($"/api/finance/{segment}/{RecordId}/rule-overrides/exclude"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().Contain(c => c.Set == PolicySet && c.Id == PolicyId && c.HasToken);
        host.NoWrites();
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_WhenEbillingIsOff_Returns503WithTheDisabledCode_AndNeverReachesTheService(string url, string _)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);

        var response = await host.Client.SendAsync(Post(url));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.NoWrites();
    }

    [Theory]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_WhenEbillingIsOff_StillDeniesACallerWithoutRights(string url, string _)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false, admin: false);

        var response = await host.Client.SendAsync(Post(url));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "the flag never replaces authorization");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The core table comes from the route; response shapes (ADR-019)
    // ═══════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(Cores))]
    public async Task Assign_Returns201_AndHandsTheServiceTheRoutesTableAndTheCaller(string segment, string set, string table)
    {
        await using var host = await Host.StartAsync();
        var view = new OverrideView(Guid.NewGuid(), "Procedure", "Excluded", ProcedureId, null, null, null, null);
        string? seenTable = null;
        Guid? seenCaller = null;
        host.Service.Setup(s => s.AssignProcedureAsync(It.IsAny<string>(), RecordId, It.IsAny<ProcedureAssignmentRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((string t, Guid _, ProcedureAssignmentRequest _, Guid? c, CancellationToken _) => (seenTable, seenCaller) = (t, c))
            .ReturnsAsync(RuleSetOutcome<OverrideView>.Done(view));
        var url = $"/api/finance/{segment}/{RecordId}/procedure-assignments";

        var response = await host.Client.SendAsync(Post(url, content: JsonContent.Create(new { procedureId = ProcedureId, mode = "Excluded" })));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().EndWith($"{url}/{view.Id}");
        (await Json(response))["mode"]!.GetValue<string>().Should().Be("Excluded");
        seenTable.Should().Be(table);
        seenCaller.Should().Be(Guid.Parse(FinanceAuthzTestAuthHandler.CallerObjectId));
        host.Access.Calls.Should().Contain(c => c.Set == set && c.Id == RecordId);
    }

    [Theory]
    [MemberData(nameof(Cores))]
    public async Task ExcludeRule_Returns201_AndHandsTheServiceTheRoutesTableTheBodyAndTheCaller(string segment, string _, string table)
    {
        await using var host = await Host.StartAsync();
        var firm = Guid.NewGuid();
        RuleExcludeRequest? seen = null;
        string? seenTable = null;
        host.Service.Setup(s => s.ExcludeRuleAsync(It.IsAny<string>(), RecordId, It.IsAny<RuleExcludeRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((string t, Guid _, RuleExcludeRequest r, Guid? _, CancellationToken _) => (seenTable, seen) = (t, r))
            .ReturnsAsync(RuleSetOutcome<OverrideView>.Done(new OverrideView(Guid.NewGuid(), "RuleExcluded", "Excluded", null, null, PolicyId, firm, "agreed")));

        var response = await host.Client.SendAsync(Post($"/api/finance/{segment}/{RecordId}/rule-overrides/exclude",
            content: JsonContent.Create(new { policyId = PolicyId, reason = "agreed", firmId = firm })));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        seenTable.Should().Be(table);
        seen.Should().Be(new RuleExcludeRequest(PolicyId, "agreed", firm));
    }

    [Theory(DisplayName = "089b review 2 K3 must-fire: a caller whose oid is not a GUID is 401 and the service never runs with no requester")]
    [MemberData(nameof(AllRoutes))]
    public async Task EveryRoute_ACallerWhoseOidIsNotAGuid_Is401_AndNeverReachesTheService(string url, string _)
    {
        await using var host = await Host.StartAsync(callerOid: "not-a-guid");

        var response = await host.Client.SendAsync(Post(url));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.NoWrites();
    }

    [Theory(DisplayName = "089b: the duplicate rule's 409 reaches the caller with its stable code")]
    [InlineData("/procedure-assignments")]
    [InlineData("/rule-overrides/exclude")]
    public async Task ADuplicate_Is409WithItsCode(string suffix)
    {
        await using var host = await Host.StartAsync();
        var duplicate = RuleSetOutcome<OverrideView>.Refused(409, "assignment.duplicate", ProcedureAssignmentService.DuplicateDetail);
        host.Service.Setup(s => s.AssignProcedureAsync(It.IsAny<string>(), RecordId, It.IsAny<ProcedureAssignmentRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(duplicate);
        host.Service.Setup(s => s.ExcludeRuleAsync(It.IsAny<string>(), RecordId, It.IsAny<RuleExcludeRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(duplicate);

        var response = await host.Client.SendAsync(Post($"/api/finance/matters/{RecordId}{suffix}"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await Json(response);
        body["errorCode"]!.GetValue<string>().Should().Be("assignment.duplicate");
        body["detail"]!.GetValue<string>().Should().Be(ProcedureAssignmentService.DuplicateDetail);
    }

    [Fact]
    public async Task AnInvalidRequest_Is400WithTheErrorCodeAndTheFieldsNamed()
    {
        await using var host = await Host.StartAsync();
        host.Service.Setup(s => s.AssignProcedureAsync(It.IsAny<string>(), RecordId, It.IsAny<ProcedureAssignmentRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<OverrideView>.Invalid("assignment.invalid",
                new[] { new RuleSetError(null, null, "pinnedVersionId", "A pinned assignment names the Procedure Version it pins.", null) }));

        var response = await host.Client.SendAsync(Post($"/api/finance/matters/{RecordId}/procedure-assignments"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await Json(response);
        body["errorCode"]!.GetValue<string>().Should().Be("assignment.invalid");
        body["errors"]!.AsArray().Select(e => e!["field"]!.GetValue<string>()).Should().Equal("pinnedVersionId");
    }

    [Theory]
    [InlineData("/procedure-assignments", """{"mode":"Excluded"}""")]
    [InlineData("/procedure-assignments", """{"procedureId":"00000000-0000-0000-0000-000000000000"}""")]
    [InlineData("/rule-overrides/exclude", """{"reason":"x"}""")]
    [InlineData("/rule-overrides/change", """{"reason":"x","shortHeadline":"Cap"}""")]
    public async Task AMissingBodyId_Is400_BeforeAnySubjectCheckOrServiceCall(string suffix, string json)
    {
        await using var host = await Host.StartAsync();

        var response = await host.Client.SendAsync(Post($"/api/finance/matters/{RecordId}{suffix}",
            content: new StringContent(json, System.Text.Encoding.UTF8, "application/json")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Access.Calls.Should().NotContain(c => c.Set == ProcedureSet || c.Set == PolicySet);
        host.NoWrites();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 089c: change rule
    // ═══════════════════════════════════════════════════════════════════════════

    public static TheoryData<string> ChangePrivilegeNames => new()
    {
        BillingAdminAuthorization.CreateRuleSetPrivilege, BillingAdminAuthorization.CreatePolicyPrivilege,
        BillingAdminAuthorization.CreatePolicyVersionPrivilege, BillingAdminAuthorization.WritePolicyVersionPrivilege,
        BillingAdminAuthorization.WritePolicyPrivilege,
    };

    [Theory(DisplayName = "089c must-fire: a change without any one of the privileges of what it creates is 403 and writes nothing")]
    [MemberData(nameof(ChangePrivilegeNames))]
    public async Task Change_WithoutOneCreatePrivilege_Returns403(string privilege)
    {
        await using var host = await Host.StartAsync();
        host.Probe.Release(privilege);

        var response = await host.Client.SendAsync(Post($"/api/finance/matters/{RecordId}/rule-overrides/change"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Probe.Calls.Should().Contain(c => c.Privilege == privilege && c.HasToken);
        host.NoWrites();
    }

    [Theory(DisplayName = "089c must-fire: a billing admin with Write on the record but no Read on the rule cannot change it (403, nothing written)")]
    [InlineData("matters")]
    [InlineData("projects")]
    [InlineData("workassignments")]
    public async Task Change_WithoutReadOnTheRule_Returns403(string segment)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(PolicySet, PolicyId, AccessRights.None);

        var response = await host.Client.SendAsync(Post($"/api/finance/{segment}/{RecordId}/rule-overrides/change"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().Contain(c => c.Set == PolicySet && c.Id == PolicyId && c.HasToken, "the caller's own Read on the rule");
        host.NoWrites();
    }

    [Theory]
    [MemberData(nameof(Cores))]
    public async Task Change_Returns201_AndHandsTheServiceTheRoutesTableTheBodyAndTheCaller(string segment, string _, string table)
    {
        await using var host = await Host.StartAsync();
        RuleChangeRequest? seen = null;
        string? seenTable = null;
        var view = new RuleChangedView(
            new OverrideView(Guid.NewGuid(), "RuleChanged", "Excluded", null, null, PolicyId, null, "x"),
            new RecordRuleView(Guid.NewGuid(), Guid.NewGuid(), "BRR-x", Guid.NewGuid(), 1, Guid.NewGuid(), "Record", null));
        host.Service.Setup(s => s.ChangeRuleAsync(It.IsAny<string>(), RecordId, It.IsAny<RuleChangeRequest>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((string t, Guid _, RuleChangeRequest r, Guid? _, CancellationToken _) => (seenTable, seen) = (t, r))
            .ReturnsAsync(RuleSetOutcome<RuleChangedView>.Done(view));
        var url = $"/api/finance/{segment}/{RecordId}/rule-overrides/change";

        var response = await host.Client.SendAsync(Post(url));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().EndWith($"{url}/{view.Override.Id}");
        (await Json(response))["recordRule"]!["code"]!.GetValue<string>().Should().Be("BRR-x");
        seenTable.Should().Be(table);
        seen.Should().Be(new RuleChangeRequest(PolicyId, "x", null, null, null, "Cap", null));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Task 089c: removal and its preview
    // ═══════════════════════════════════════════════════════════════════════════

    private static readonly Guid OverrideId = Guid.NewGuid();

    /// <summary>The six removal routes: (method, url, the record's entity set).</summary>
    public static TheoryData<string, string, string> RemovalRoutes
    {
        get
        {
            var data = new TheoryData<string, string, string>();
            foreach (var row in Cores)
            {
                var url = $"/api/finance/{row[0]}/{RecordId}/overrides/{OverrideId}";
                data.Add("GET", url + "/removal-preview", (string)row[1]);
                data.Add("DELETE", url, (string)row[1]);
            }

            return data;
        }
    }

    /// <summary>The core table of a core entity set (<c>sprk_matters</c> is <c>sprk_matter</c>).</summary>
    private static string TableOf(string entitySet) => entitySet[..^1];

    private static HttpRequestMessage Send(string method, string url, bool authenticated = true) =>
        RateCardImportEndpointsTests.Req(method, url, authenticated);

    /// <summary>The stored row's subject, as the service reads it for the filter, and a removal or preview that succeeds.</summary>
    private static void StoredRowOverrides(Host host, OverrideSubject? subject)
    {
        host.Service.Setup(s => s.SubjectOfOverrideAsync(It.IsAny<string>(), RecordId, OverrideId, It.IsAny<CancellationToken>())).ReturnsAsync(subject);
        host.Service.Setup(s => s.PreviewRemovalAsync(It.IsAny<string>(), RecordId, OverrideId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<OverrideRemovalPreview>.Done(new OverrideRemovalPreview(OverrideId, "RuleExcluded", false, null, null, null)));
        host.Service.Setup(s => s.RemoveAsync(It.IsAny<string>(), RecordId, OverrideId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<OverrideRemoved>.Done(new OverrideRemoved(OverrideId, "RuleExcluded", false, true, null, null, null)));
    }

    [Theory]
    [MemberData(nameof(RemovalRoutes))]
    public async Task Removal_Unauthenticated_Returns401(string method, string url, string _)
    {
        await using var host = await Host.StartAsync();

        var response = await host.Client.SendAsync(Send(method, url, authenticated: false));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Service.VerifyNoOtherCalls();
    }

    public static IEnumerable<object[]> RemovalRoutesRefused => StatedCaller.Cross(RemovalRoutes, StatedCaller.RefusedKinds);

    [Theory(DisplayName = "089c must-fire: every removal route refuses a flagged-external, row-less or flag-fault caller at step 0, before any check or read")]
    [MemberData(nameof(RemovalRoutesRefused))]
    public async Task Removal_StepZero_RefusesBeforeAnyCheckOrRead(string method, string url, string _, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;

        var response = await host.Client.SendAsync(Send(method, url));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: true);
        host.Probe.Calls.Should().BeEmpty();
        host.Access.Calls.Should().BeEmpty();
        host.Service.VerifyNoOtherCalls();
    }

    [Theory(DisplayName = "089c must-fire: without Write AND AppendTo on the record, a removal or its preview is the uniform 404, byte-identical to an unknown record, and never reads the row")]
    [MemberData(nameof(RemovalRoutes))]
    public async Task Removal_WithoutWriteAndAppendToOnTheRecord_IsTheUniform404(string method, string url, string set)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(set, RecordId, AccessRights.Read | AccessRights.Write);
        var response = await host.Client.SendAsync(Send(method, url));

        await using var unknown = await Host.StartAsync();
        var unknownResponse = await unknown.Client.SendAsync(Send(method, url.Replace(RecordId.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ShapeOf(response)).Should().Be(await ShapeOf(unknownResponse), "no existence oracle");
        host.Access.Calls.Should().Contain(c => c.Set == set && c.Id == RecordId && c.HasToken);
        host.Service.VerifyNoOtherCalls();
    }

    [Theory(DisplayName = "089c must-fire: without the billing-admin privilege a removal or its preview is 403 and never reads the row")]
    [MemberData(nameof(RemovalRoutes))]
    public async Task Removal_WithoutTheBillingAdminMarker_Returns403(string method, string url, string _)
    {
        await using var host = await Host.StartAsync();
        host.Probe.Release(BillingAdminAuthorization.WriteRuleSetPrivilege);

        var response = await host.Client.SendAsync(Send(method, url));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Service.VerifyNoOtherCalls();
    }

    [Theory(DisplayName = "089c must-fire: a removal without Write on sprk_policy is 403 (a changed rule's removal switches its record rule Off); its preview does not need it")]
    [InlineData("matters")]
    [InlineData("projects")]
    [InlineData("workassignments")]
    public async Task Remove_WithoutWriteOnPolicy_Returns403_ThePreviewDoesNot(string segment)
    {
        await using var host = await Host.StartAsync();
        host.Probe.Release(BillingAdminAuthorization.WritePolicyPrivilege);
        StoredRowOverrides(host, new OverrideSubject(PolicySet, PolicyId));
        var url = $"/api/finance/{segment}/{RecordId}/overrides/{OverrideId}";

        var removed = await host.Client.SendAsync(Send("DELETE", url));
        var previewed = await host.Client.SendAsync(Send("GET", url + "/removal-preview"));

        removed.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        previewed.StatusCode.Should().Be(HttpStatusCode.OK, "must not fire: the preview writes nothing");
        host.Service.Verify(s => s.RemoveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public static TheoryData<string, string, string, string, string> RemovalRoutesBySubject
    {
        get
        {
            var data = new TheoryData<string, string, string, string, string>();
            foreach (var route in RemovalRoutes)
            {
                data.Add((string)route[0], (string)route[1], (string)route[2], ProcedureSet, ProcedureId.ToString());
                data.Add((string)route[0], (string)route[1], (string)route[2], PolicySet, PolicyId.ToString());
            }

            return data;
        }
    }

    [Theory(DisplayName = "089c must-fire: before a removal or its preview, the caller's own Read on what the stored row overrides; without it 403 insufficient_rights and nothing is removed")]
    [MemberData(nameof(RemovalRoutesBySubject))]
    public async Task Removal_WithoutReadOnTheStoredSubject_Returns403(string method, string url, string set, string subjectSet, string subjectId)
    {
        await using var host = await Host.StartAsync();
        var subject = new OverrideSubject(subjectSet, Guid.Parse(subjectId));
        StoredRowOverrides(host, subject);
        host.Access.Grant(subjectSet, subject.Id, AccessRights.None);

        var response = await host.Client.SendAsync(Send(method, url));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.InsufficientRightsReasonCode);
        host.Access.Calls.Should().Contain(c => c.Set == subjectSet && c.Id == subject.Id && c.HasToken, "asked as the caller, about the stored subject");
        host.Service.Verify(s => s.SubjectOfOverrideAsync(TableOf(set), RecordId, OverrideId, It.IsAny<CancellationToken>()), Times.Once, "the row is read for THIS route's record");
        host.NoWrites();
    }

    [Theory(DisplayName = "089c must-not-fire: with Read on the stored subject, the removal and its preview reach the service with the route's table")]
    [MemberData(nameof(RemovalRoutesBySubject))]
    public async Task Removal_WithReadOnTheStoredSubject_ReachesTheService(string method, string url, string set, string subjectSet, string subjectId)
    {
        await using var host = await Host.StartAsync();
        StoredRowOverrides(host, new OverrideSubject(subjectSet, Guid.Parse(subjectId)));

        var response = await host.Client.SendAsync(Send(method, url));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        if (method == "DELETE")
        {
            host.Service.Verify(s => s.RemoveAsync(TableOf(set), RecordId, OverrideId, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        else
        {
            host.Service.Verify(s => s.PreviewRemovalAsync(TableOf(set), RecordId, OverrideId, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Theory(DisplayName = "089c must-fire: when the stored row cannot be read, or Dataverse gives no answer on the subject, the removal fails closed (403 system_failure)")]
    [MemberData(nameof(RemovalRoutes))]
    public async Task Removal_WhenTheSubjectCannotBeChecked_FailsClosed(string method, string url, string _)
    {
        await using var faulted = await Host.StartAsync();
        faulted.Service.Setup(s => s.SubjectOfOverrideAsync(It.IsAny<string>(), RecordId, OverrideId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("simulated Dataverse fault"));
        await using var unanswered = await Host.StartAsync();
        StoredRowOverrides(unanswered, new OverrideSubject(PolicySet, PolicyId));
        unanswered.Access.Grant(PolicySet, PolicyId, AccessRights.None);
        unanswered.Access.Fault(PolicySet, PolicyId); // no right AND no answer: an unknown, never a denial

        foreach (var host in new[] { faulted, unanswered })
        {
            var response = await host.Client.SendAsync(Send(method, url));

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await Json(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.SystemFailureReasonCode);
            host.NoWrites();
        }
    }

    [Theory(DisplayName = "089c: a row that is not this record's passes the subject filter and the service answers its 404")]
    [MemberData(nameof(RemovalRoutes))]
    public async Task Removal_OfAnUnknownRow_IsTheServices404(string method, string url, string _)
    {
        await using var host = await Host.StartAsync();
        StoredRowOverrides(host, null);
        var notFound = RuleSetOutcome<OverrideRemoved>.Refused(404, "assignment.override_not_found", "The override was not found on this record.");
        host.Service.Setup(s => s.RemoveAsync(It.IsAny<string>(), RecordId, OverrideId, It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(notFound);
        host.Service.Setup(s => s.PreviewRemovalAsync(It.IsAny<string>(), RecordId, OverrideId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RuleSetOutcome<OverrideRemovalPreview>.Refused(404, "assignment.override_not_found", "The override was not found on this record."));

        var response = await host.Client.SendAsync(Send(method, url));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("assignment.override_not_found");
    }

    [Theory]
    [MemberData(nameof(RemovalRoutes))]
    public async Task Removal_WhenEbillingIsOff_Returns503_AndNeverReachesTheService(string method, string url, string _)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        StoredRowOverrides(host, new OverrideSubject(PolicySet, PolicyId));

        var response = await host.Client.SendAsync(Send(method, url));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response))["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.NoWrites();
    }

    [Theory(DisplayName = "089c must-fire: a caller whose oid is not a GUID is 401 on a removal or its preview, and the service never runs")]
    [MemberData(nameof(RemovalRoutes))]
    public async Task Removal_ACallerWhoseOidIsNotAGuid_Is401(string method, string url, string _)
    {
        await using var host = await Host.StartAsync(callerOid: "not-a-guid");
        StoredRowOverrides(host, new OverrideSubject(PolicySet, PolicyId));

        var response = await host.Client.SendAsync(Send(method, url));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.NoWrites();
    }
}
