using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Tests.Services.Finance;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// The blocked-submission routes (ebilling-r1 task 105; FR-19, FR-37, D-73, D-76) through the REAL <c>MapSubmissionOverrideEndpoints</c>,
/// <see cref="FinanceAuthorizationFilter"/>, <see cref="AuthorizationService"/>, <see cref="EbillingEnabledFilter"/> and the REAL
/// <see cref="SubmissionOverrideService"/> over <see cref="SubmissionOverrideServiceTests.World"/>: 401 unauthenticated, 403 for a caller
/// without rights on the submission, 403 <c>ebilling.override.not_permitted</c> for an override without the privilege (and for a privilege
/// check that faults), Return with no privilege (D-73), the 503 kill switch after authorization, and a request body that cannot name the decider.
/// </summary>
/// <remarks>
/// Security-sensitive, so every refusal asserts that nothing was recorded, written or announced. The seeding proof of task 105 neutralised
/// the privilege check (<c>EbillingOverridePrivilege.HoldsAsync</c> answering true) and watched
/// <see cref="Override_WithRightsButWithoutThePrivilege_Is403NotPermitted_AndNothingIsWritten"/> fail (task notes).
/// </remarks>
public sealed class SubmissionOverrideEndpointsAuthorizationContractTests
{
    private const string Set = FinanceAuthorizationFilter.InvoiceSubmissionEntitySet;
    private static readonly Guid SubmissionId = SubmissionOverrideServiceTests.World.SubmissionId;

    private static string Url(string verb, Guid? id = null) => $"/api/finance/submissions/{id ?? SubmissionId}/{verb}";

    // ── Authentication ──

    [Theory]
    [InlineData("GET", "blocked")]
    [InlineData("POST", "override")]
    [InlineData("POST", "return")]
    public async Task Unauthenticated_Returns401_AndTouchesNothing(string method, string verb)
    {
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(Request(method, Url(verb), authenticated: false));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Access.Calls.Should().BeEmpty();
        host.World.Probe.Calls.Should().BeEmpty();
        host.World.Entities.Invocations.Should().BeEmpty();
        host.World.AssertNothingChanged();
    }

    // ── Authorization on the submission (ADR-008) ──

    [Theory]
    [InlineData("override")]
    [InlineData("return")]
    public async Task Post_ByACallerWithNoRightsOnTheSubmission_Is403_AndNothingIsAskedOrWritten(string verb)
    {
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(Request("POST", Url(verb), ReasonBody()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle().Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, Set, SubmissionId, HasToken: true));
        host.World.Probe.Calls.Should().BeEmpty("the privilege is asked only after the route's rights pass");
        host.World.Entities.Invocations.Should().BeEmpty();
        host.World.AssertNothingChanged();
    }

    // ── Step 0 (ebilling-r1 task 099; section 5.3) ──

    private static readonly object[][] AllRoutes = [["GET", "blocked", true], ["POST", "override", false], ["POST", "return", false]];

    public static IEnumerable<object[]> RoutesRefused => StatedCaller.Cross(AllRoutes, StatedCaller.RefusedKinds);

    public static IEnumerable<object[]> RoutesInternal => StatedCaller.Cross(AllRoutes, StatedCaller.InternalKinds);

    [Theory(DisplayName = "Task 099 must-fire: /blocked, /override and /return refuse a flagged-external, row-less or unreadable caller with their own denial, before any rights call, privilege question or read")]
    [MemberData(nameof(RoutesRefused))]
    public async Task StepZero_RefusesBeforeAnyRightsCallOrRead(string method, string verb, bool uniformNotFound, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.GrantRights();
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(Request(method, Url(verb), method == "POST" ? ReasonBody() : null));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound);
        host.Access.Calls.Should().BeEmpty();
        host.World.Probe.Calls.Should().BeEmpty();
        host.World.Entities.Invocations.Should().BeEmpty();
        host.World.AssertNothingChanged();
    }

    [Theory(DisplayName = "Task 099 must-not-fire: /blocked, /override and /return with a flag that reads false or blank reach the record check as before")]
    [MemberData(nameof(RoutesInternal))]
    public async Task StepZero_AdmitsAnInternalCaller(string method, string verb, bool uniformNotFound, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(Request(method, Url(verb), method == "POST" ? ReasonBody() : null));

        response.StatusCode.Should().Be(uniformNotFound ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle().Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, Set, SubmissionId, HasToken: true));
        host.World.AssertNothingChanged();
    }

    [Theory(DisplayName = "Task 099: a step-0 refusal of /blocked is indistinguishable from an internal caller's 404 for an absent submission id: same status, Content-Type and body but the correlation id")]
    [InlineData(StepZeroCallerKind.FlagTrue)]
    [InlineData(StepZeroCallerKind.NoSystemUserRow)]
    [InlineData(StepZeroCallerKind.FlagReadFault)]
    [InlineData(StepZeroCallerKind.LookupFault)]
    public async Task StepZero_Blocked_IsIndistinguishableFromAnInternalAbsentId(StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.GrantRights();
        host.StepZero.Kind = kind;

        var refused = await host.SendAsync(Request("GET", Url("blocked")));
        await host.StepZero.AssertRefusedAsync(refused, kind, uniformNotFound: true);
        host.Access.Calls.Should().BeEmpty();

        host.StepZero.Kind = StepZeroCallerKind.FlagFalse; // an INTERNAL caller asking for an id it holds no rights on (absent)
        var absent = await host.SendAsync(Request("GET", Url("blocked", Guid.NewGuid())));
        host.Access.Calls.Should().ContainSingle("the internal caller reached the route's own record check");

        await Sprk.Bff.Api.Tests.Api.Filters.FinanceStepZeroTests.AssertIndistinguishableAsync(refused, absent);
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Theory]
    [InlineData("override")]
    [InlineData("return")]
    public async Task Post_WithReadButNotWriteOnTheSubmission_Is403(string verb)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(Set, SubmissionId, AccessRights.Read);

        var response = await host.SendAsync(Request("POST", Url(verb), ReasonBody()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.World.AssertNothingChanged();
    }

    [Fact]
    public async Task Get_ByACallerWithNoRead_IsTheUniformNotFound_AndReadsNothing()
    {
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(Request("GET", Url("blocked")));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.World.Entities.Invocations.Should().BeEmpty();
        host.World.Probe.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task RightsOnAnotherSubmission_AreNotRightsOnThisOne_AndAQueryIdCannotSubstitute()
    {
        await using var host = await Host.StartAsync();
        var decoy = Guid.NewGuid();
        host.Access.Grant(Set, decoy, AccessRights.Read | AccessRights.Write);

        var response = await host.SendAsync(Request("POST", Url("override") + $"?submissionId={decoy}", ReasonBody()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle().Which.Id.Should().Be(SubmissionId);
        host.World.AssertNothingChanged();
    }

    [Fact]
    public async Task AFailedRightsCheck_IsDenied_NeverPassedThrough()
    {
        await using var host = await Host.StartAsync();
        host.Access.ThrowOnEveryCall = new InvalidOperationException("Dataverse unreachable");

        var response = await host.SendAsync(Request("POST", Url("override"), ReasonBody()));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable);
        host.World.AssertNothingChanged();
    }

    // ── The override privilege (D-76) ──

    [Fact]
    public async Task Override_WithRightsButWithoutThePrivilege_Is403NotPermitted_AndNothingIsWritten()
    {
        await using var host = await Host.StartAsync();
        host.GrantRights();
        host.World.Probe.Release(EbillingOverridePrivilege.PrivilegeName);

        var response = await host.SendAsync(Request("POST", Url("override"), ReasonBody()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["errorCode"]!.GetValue<string>().Should().Be("ebilling.override.not_permitted");
        json["reasonCode"]!.GetValue<string>().Should().Be("ebilling.override.not_permitted");
        host.World.AssertNothingChanged();
        host.World.Status.Should().Be(InvoiceSubmissionStatus.Rejected);
    }

    [Fact]
    public async Task Override_WhenThePrivilegeCheckFaults_Is403NotPermitted()
    {
        await using var host = await Host.StartAsync();
        host.GrantRights();
        host.World.Probe.PrivilegeFault = new HttpRequestException("dataverse down");

        var response = await host.SendAsync(Request("POST", Url("override"), ReasonBody()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(response)).Should().Be("ebilling.override.not_permitted");
        host.World.AssertNothingChanged();
    }

    [Fact]
    public async Task Override_WithoutABearerToken_Is403_EvenWhenTheProbeWouldSayYes()
    {
        await using var host = await Host.StartAsync();
        host.GrantRights();

        var response = await host.SendAsync(Request("POST", Url("override"), ReasonBody(), bearer: false));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.World.AssertNothingChanged();
    }

    [Fact]
    public async Task Override_WithRightsAndThePrivilege_Is200_RecordsOnce_AndAcceptsTheSubmission()
    {
        await using var host = await Host.StartAsync();
        host.GrantRights();

        var response = await host.SendAsync(Request("POST", Url("override"), ReasonBody()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["outcome"]!.GetValue<string>().Should().Be("Overridden");
        json["alreadyDecided"]!.GetValue<bool>().Should().BeFalse();
        json["submissionId"]!.GetValue<Guid>().Should().Be(SubmissionId);
        json["recordId"]!.GetValue<Guid>().Should().NotBeEmpty();
        host.World.RecordCreates.Should().Be(1);
        host.World.Status.Should().Be(InvoiceSubmissionStatus.Accepted);
        host.World.Probe.Calls.Should().OnlyContain(c => c.Privilege == EbillingOverridePrivilege.PrivilegeName && c.HasToken);
    }

    [Fact]
    public async Task Override_Repeated_Is200AlreadyDecided_AndWritesNothingNew()
    {
        await using var host = await Host.StartAsync();
        host.GrantRights();
        await host.SendAsync(Request("POST", Url("override"), ReasonBody()));

        var response = await host.SendAsync(Request("POST", Url("override"), ReasonBody()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["alreadyDecided"]!.GetValue<bool>().Should().BeTrue();
        host.World.RecordCreates.Should().Be(1);
    }

    // ── Return needs no privilege (D-73) ──

    [Fact]
    public async Task Return_WithRightsAndNoPrivilege_Is200_AndTheInvoiceIsReturned()
    {
        await using var host = await Host.StartAsync();
        host.GrantRights();
        host.World.Probe.Release(EbillingOverridePrivilege.PrivilegeName);

        var response = await host.SendAsync(Request("POST", Url("return"), ReasonBody()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["outcome"]!.GetValue<string>().Should().Be("Returned");
        host.World.Probe.Calls.Should().BeEmpty("Return never asks the override privilege");
        host.World.InvoiceStatus.Should().Be(InvoiceLifecycleStatus.ReturnedToFirm);
        host.World.RecordCreates.Should().Be(1);
    }

    // ── Reason and body ──

    [Theory]
    [InlineData("override")]
    [InlineData("return")]
    public async Task Post_WithoutAReason_Is400ReasonRequired_AndNothingChanges(string verb)
    {
        await using var host = await Host.StartAsync();
        host.GrantRights();

        var blank = await host.SendAsync(Request("POST", Url(verb), Json("""{"reason":"   "}""")));
        var missing = await host.SendAsync(Request("POST", Url(verb), Json("{}")));
        var noBody = await host.SendAsync(Request("POST", Url(verb)));

        foreach (var response in new[] { blank, missing, noBody })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await ErrorCodeAsync(response)).Should().Be("ebilling.reason_required");
        }

        host.World.AssertNothingChanged();
    }

    [Fact]
    public async Task Override_ABodyCannotNameTheDeciderTheInvoiceOrTheDecisionId()
    {
        await using var host = await Host.StartAsync();
        host.GrantRights();
        var attacker = Guid.NewGuid();
        var body = Json($$"""
            {"reason":"{{"Negotiated rate is not yet in the rate card."}}","decider":"{{attacker}}","deciderId":"{{attacker}}","systemUserId":"{{attacker}}",
             "invoiceId":"{{attacker}}","subjectId":"{{attacker}}","decisionId":"{{attacker}}","policyVersionId":"{{attacker}}","outcome":"Rejected"}
            """);

        var response = await host.SendAsync(Request("POST", Url("override"), body));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var decision = host.World.Recorded.Should().ContainSingle().Which;
        decision.Decider.SystemUserId.Should().Be(SubmissionOverrideServiceTests.World.Caller, "the decider is the authenticated caller");
        decision.SubjectId.Should().Be(SubmissionOverrideServiceTests.World.InvoiceId, "the invoice is the submission's own");
        decision.DecisionId.Should().Be(SubmissionOverrideService.OverrideDecisionIdFor(SubmissionId));
        decision.Outcome.Should().Be("Overridden");
        decision.PolicyVersionId.Should().NotBe(attacker);
        host.World.Updates.Should().ContainSingle().Which.Keys.Should().BeEquivalentTo(
            [InvoiceSubmissionPersister.SubmissionStatus, InvoiceSubmissionPersister.SubmissionReasonCode, InvoiceSubmissionPersister.SubmissionFindingsJson],
            "the body chose none of the written columns");
    }

    [Fact]
    public async Task ARefusedDecision_IsProblemDetailsWithAStableCode_AndACorrelationId()
    {
        await using var host = await Host.StartAsync();
        host.GrantRights();
        host.World.SetSubmission(InvoiceSubmissionStatus.Accepted, null);

        var response = await host.SendAsync(Request("POST", Url("override"), ReasonBody()));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["errorCode"]!.GetValue<string>().Should().Be("ebilling.blocked.already_accepted");
        json["correlationId"]!.GetValue<string>().Should().NotBeNullOrEmpty();
        host.World.AssertNothingChanged();
    }

    // ── The kill switch (ADR-018): after authorization, never instead of it ──

    [Theory]
    [InlineData("GET", "blocked")]
    [InlineData("POST", "override")]
    [InlineData("POST", "return")]
    public async Task FlagOff_AnAuthorizedCaller_Gets503EbillingDisabled_AndNothingChanges(string method, string verb)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        host.GrantRights();

        var response = await host.SendAsync(Request(method, Url(verb), method == "POST" ? ReasonBody() : null));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCodeAsync(response)).Should().Be("ebilling.disabled");
        host.World.Entities.Invocations.Should().BeEmpty();
        host.World.AssertNothingChanged();
    }

    [Fact]
    public async Task FlagOff_AnUnauthorizedCaller_StillGets403_TheFlagNeverReplacesAuthorization()
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);

        var response = await host.SendAsync(Request("POST", Url("override"), ReasonBody()));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── The read ──

    [Fact]
    public async Task Get_WithRead_ShowsTheBlockedSubmission_AndCanOverrideFollowsThePrivilege()
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(Set, SubmissionId, AccessRights.Read);

        var privileged = await host.SendAsync(Request("GET", Url("blocked")));
        host.World.Probe.Release(EbillingOverridePrivilege.PrivilegeName);
        var plain = await host.SendAsync(Request("GET", Url("blocked")));

        privileged.StatusCode.Should().Be(HttpStatusCode.OK);
        var first = JsonNode.Parse(await privileged.Content.ReadAsStringAsync())!.AsObject();
        first["state"]!.GetValue<string>().Should().Be("Blocked");
        first["canOverride"]!.GetValue<bool>().Should().BeTrue();
        first["findings"]!.AsArray().Should().NotBeEmpty();
        var second = JsonNode.Parse(await plain.Content.ReadAsStringAsync())!.AsObject();
        second["canOverride"]!.GetValue<bool>().Should().BeFalse();
        second["canReturn"]!.GetValue<bool>().Should().BeTrue();
        host.World.AssertNothingChanged();
    }

    [Fact]
    public async Task Get_OfAMissingSubmission_Is404()
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(Set, SubmissionId, AccessRights.Read);
        host.World.SubmissionAbsent = true;

        var response = await host.SendAsync(Request("GET", Url("blocked")));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_WhenTheReadFaults_Is503_NeverA404()
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(Set, SubmissionId, AccessRights.Read);
        host.World.ReadFault = new TimeoutException("dataverse");

        var response = await host.SendAsync(Request("GET", Url("blocked")));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // ── Helpers ──

    private static StringContent ReasonBody() => Json("""{"reason":"Negotiated rate is not yet in the rate card."}""");

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static HttpRequestMessage Request(string method, string url, HttpContent? content = null, bool authenticated = true, bool bearer = true)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url) { Content = content };
        if (authenticated)
        {
            request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "1");
            if (bearer)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SubmissionOverrideServiceTests.World.Token);
            }
        }

        return request;
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        return json["errorCode"]?.GetValue<string>();
    }

    private sealed class Host : IAsyncDisposable
    {
        /// <summary>The step-0 caller (task 099); internal unless a test changes <see cref="StatedCaller.Kind"/>.</summary>
        public StatedCaller StepZero { get; private set; } = null!;

        private WebApplication _app = null!;
        private HttpClient _client = null!;

        public FinAuth.RecordingAccessDataSource Access { get; } = new();
        public SubmissionOverrideServiceTests.World World { get; private set; } = null!;

        public static async Task<Host> StartAsync(bool ebillingEnabled = true)
        {
            var host = new Host { World = await SubmissionOverrideServiceTests.World.CreateAsync() };
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
            builder.Services.AddOptions<EbillingOptions>().Configure(o => o.Enabled = ebillingEnabled);

            // The REAL authorization stack, substituted only at the access-data boundary.
            builder.Services.AddSingleton<IAccessDataSource>(host.Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();

            // The REAL service over the unit tests' world (Dataverse, the lock, the recorder's writer and the probe are the substitutes).
            builder.Services.AddSingleton(host.World.Service);

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapSubmissionOverrideEndpoints();
            await host._app.StartAsync();
            host._client = host._app.GetTestClient();
            return host;
        }

        public void GrantRights() => Access.Grant(Set, SubmissionId, AccessRights.Read | AccessRights.Write);

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => _client.SendAsync(request);

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
