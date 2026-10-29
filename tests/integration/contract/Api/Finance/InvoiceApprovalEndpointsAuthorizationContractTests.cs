using System.Net;
using System.Net.Http.Headers;
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
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Tests.Services.Finance;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// The approval routes through the REAL <c>MapInvoiceApprovalEndpoints</c>, <see cref="FinanceAuthorizationFilter"/>,
/// <see cref="AuthorizationService"/> and <see cref="EbillingEnabledFilter"/> (ebilling-r1 task 103, FR-35, ADR-008, ADR-018, ADR-019): 401
/// unauthenticated, 403 for a caller without the right on the invoice, step 0 (task 099), approve needs Write, the stable <c>ebilling.disabled</c>
/// 503, 403 <c>ebilling.decider.unresolved</c>, and 403 <c>ebilling.approver.not_eligible</c> for a caller WITH Write who is not an eligible approver.
/// </summary>
/// <remarks>
/// The substitution is at the access-data boundary (a recording fake) and at the Dataverse seam under the REAL approval, resolver, line-decision,
/// totals and recorder services (<see cref="ApprovalWorld"/>), so a denied request is shown to read nothing and write nothing.
/// <para><b>Seeding proof (task 103 acceptance).</b> <c>Approve_WriteButNotEligible_Is403_AndWritesNothing</c> fails when the service's eligibility
/// refusal is removed (Carol then approves: 200). <c>Denied_WithoutTheRight_ReadsAndWritesNothing</c> fails when the route filter is removed.
/// Recorded in <c>projects/ebilling-r1/notes/103-approval-worklist.md</c>.</para>
/// </remarks>
public sealed class InvoiceApprovalEndpointsAuthorizationContractTests
{
    private const string InvoiceSet = "sprk_invoices"; // FinanceAuthorizationFilter.InvoiceEntitySet

    public static IEnumerable<object[]> Routes() => [["GET", "approval"], ["POST", "approve"]];

    public static IEnumerable<object[]> RoutesRefused() => StatedCaller.Cross(Routes(), StatedCaller.RefusedKinds);

    public static IEnumerable<object[]> RoutesInternal() => StatedCaller.Cross(Routes(), StatedCaller.InternalKinds);

    // =========================================================================================
    // Authorization (ADR-008)
    // =========================================================================================

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Unauthenticated_Returns401_AndReadsNothing(string method, string route)
    {
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(method, route, authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Access.Calls.Should().BeEmpty();
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Denied_WithoutTheRight_ReadsAndWritesNothing(string method, string route)
    {
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(method, route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle()
            .Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, host.World.InvoiceId, HasToken: true));
        host.World.Entities.Invocations.Should().BeEmpty();
        host.Probe.SystemUserIdCalls.Should().Be(0);
    }

    [Theory(DisplayName = "Task 099 must-fire: both approval routes refuse a flagged-external, row-less or unreadable caller with 403 before any rights call, even one holding Read and Write")]
    [MemberData(nameof(RoutesRefused))]
    public async Task StepZero_RefusesBeforeAnyRightsCallOrRead(string method, string route, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(method, route);

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        host.Access.Calls.Should().BeEmpty();
        host.World.Entities.Invocations.Should().BeEmpty();
        host.Probe.SystemUserIdCalls.Should().Be(0);
    }

    [Theory(DisplayName = "Task 099 must-not-fire: both approval routes with a flag that reads false or blank reach the record check")]
    [MemberData(nameof(RoutesInternal))]
    public async Task StepZero_AdmitsAnInternalCaller(string method, string route, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(method, route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle().Which.Id.Should().Be(host.World.InvoiceId);
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Approve_NeedsWrite_ReadAloneIsNotEnough()
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, host.World.InvoiceId, AccessRights.Read);

        var response = await host.SendAsync("POST", "approve");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.World.Entities.Invocations.Should().BeEmpty();
        host.World.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
    }

    [Fact]
    public async Task Get_NeedsRead_WriteAloneIsNotEnough()
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, host.World.InvoiceId, AccessRights.Write | AccessRights.Append);

        var response = await host.SendAsync("GET", "approval");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task RightOnAnotherInvoice_IsNotRightOnThisOne(string method, string route)
    {
        await using var host = await Host.StartAsync();
        var decoy = Guid.NewGuid();
        host.Access.Grant(InvoiceSet, decoy, AccessRights.Read | AccessRights.Write);

        var response = await host.SendAsync(method, route, query: $"?invoiceId={decoy}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle().Which.Id.Should().Be(host.World.InvoiceId);
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task FailedRightsCheck_IsDenied_NeverPassedThrough(string method, string route)
    {
        await using var host = await Host.StartAsync();
        host.Access.ThrowOnEveryCall = new InvalidOperationException("Dataverse unreachable");

        var response = await host.SendAsync(method, route);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable);
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    // =========================================================================================
    // The eBilling kill switch (ADR-018): after authorization, never instead of it
    // =========================================================================================

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task FlagOff_AuthorizedCaller_Gets503EbillingDisabled_AndReadsNothing(string method, string route)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        host.GrantAll();

        var response = await host.SendAsync(method, route);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCode(response)).Should().Be("ebilling.disabled");
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task FlagOff_UnauthorizedCaller_StillGets403(string method, string route)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);

        var response = await host.SendAsync(method, route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // =========================================================================================
    // Who approves (FR-35): the caller's own identity, live eligibility
    // =========================================================================================

    [Fact]
    public async Task Approve_WithAnUnprovenIdentity_Returns403_AndReadsNothing()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.Probe.CallerSystemUserId = null;

        var response = await host.SendAsync("POST", "approve");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCode(response)).Should().Be("ebilling.decider.unresolved");
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "103 FR-35 SEEDED: a caller WITH Write on the invoice who is not an eligible approver gets 403 not_eligible and nothing is written")]
    public async Task Approve_WriteButNotEligible_Is403_AndWritesNothing()
    {
        await using var host = await Host.StartAsync(caller: ApprovalWorld.CarolUser);
        host.GrantAll();
        host.World.AddLine(InvoiceLineStatus.Clean);

        var response = await host.SendAsync("POST", "approve");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCode(response)).Should().Be("ebilling.approver.not_eligible");
        host.World.Writes.Should().Be(0);
        host.World.Transitions.Should().Be(0);
        host.World.ApproveRecords.Should().BeEmpty();
        host.World.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
    }

    [Fact]
    public async Task Approve_EligibleApprover_Gets200_ThenASecondApproveIsANoOp()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.World.AddLine(InvoiceLineStatus.Accepted);

        var first = await host.SendAsync("POST", "approve");
        var second = await host.SendAsync("POST", "approve");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await first.Content.ReadAsStringAsync())!;
        json["outcome"]!.GetValue<string>().Should().Be("approved");
        json["state"]!["status"]!.GetValue<string>().Should().Be("Approved");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.Parse(await second.Content.ReadAsStringAsync())!["outcome"]!.GetValue<string>().Should().Be("alreadyApproved");
        host.World.ApproveRecords.Should().ContainSingle()
            .Which.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>("sprk_confirmedby").Id.Should().Be(ApprovalWorld.AliceUser);
    }

    [Fact(DisplayName = "103 I-2: approve with open lines is 409 lines_open with the counts")]
    public async Task Approve_OpenLines_Is409_WithCounts()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.World.AddLine(InvoiceLineStatus.Flagged);
        host.World.AddLine(InvoiceLineStatus.Pending);
        host.World.AddLine(InvoiceLineStatus.Flagged);

        var response = await host.SendAsync("POST", "approve");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        json["errorCode"]!.GetValue<string>().Should().Be("ebilling.approve.lines_open");
        json["pendingLines"]!.GetValue<int>().Should().Be(1);
        json["flaggedLines"]!.GetValue<int>().Should().Be(2);
        host.World.Writes.Should().Be(0);
    }

    // =========================================================================================
    // The state route
    // =========================================================================================

    [Fact]
    public async Task Get_ReaderGets200_WithTheCallersEligibility()
    {
        await using var host = await Host.StartAsync(caller: ApprovalWorld.CarolUser);
        host.Access.Grant(InvoiceSet, host.World.InvoiceId, AccessRights.Read);
        host.World.AddLine(InvoiceLineStatus.Clean);

        var response = await host.SendAsync("GET", "approval");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        json["status"]!.GetValue<string>().Should().Be("Pending");
        json["callerEligible"]!.GetValue<bool>().Should().BeFalse();
        json["canApprove"]!.GetValue<bool>().Should().BeFalse();
        json["eligibleApprovers"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Equal("Alice Adams", "Bob Brown");
        json["undecidedCleanLines"]!.GetValue<int>().Should().Be(1);
    }

    [Fact(DisplayName = "103 F4: an eligible approver with only Read on the invoice gets canApprove false; with Write, true")]
    public async Task Get_CanApprove_NeedsWriteOnTheInvoice()
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, host.World.InvoiceId, AccessRights.Read);
        host.World.AddLine(InvoiceLineStatus.Accepted);

        var readOnly = JsonNode.Parse(await (await host.SendAsync("GET", "approval")).Content.ReadAsStringAsync())!;
        readOnly["callerEligible"]!.GetValue<bool>().Should().BeTrue();
        readOnly["canApprove"]!.GetValue<bool>().Should().BeFalse("approve would be refused by its Write filter");

        host.GrantAll();
        var writer = JsonNode.Parse(await (await host.SendAsync("GET", "approval")).Content.ReadAsStringAsync())!;
        writer["canApprove"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Get_InvoiceGoneAfterTheCheck_Returns404Uniform()
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, host.World.InvoiceId, AccessRights.Read);
        host.World.Rows.Remove(("sprk_invoice", host.World.InvoiceId));

        var response = await host.SendAsync("GET", "approval");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(host.World.InvoiceId.ToString());
        JsonNode.Parse(body)!["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.record_unavailable");
    }

    // =========================================================================================
    // Host
    // =========================================================================================

    private static async Task<string?> ErrorCode(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]?.GetValue<string>();

    private static async Task<string?> ReasonCode(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]?.GetValue<string>();

    private sealed class Host : IAsyncDisposable
    {
        private WebApplication _app = null!;
        private HttpClient _client = null!;

        public StatedCaller StepZero { get; private set; } = null!;
        public ApprovalWorld World { get; } = new();
        public FinAuth.RecordingAccessDataSource Access { get; } = new();
        public FinAuth.RecordingPrivilegeProbe Probe { get; } = new();

        public static async Task<Host> StartAsync(bool ebillingEnabled = true, Guid? caller = null)
        {
            var host = new Host();
            host.Probe.CallerSystemUserId = caller ?? ApprovalWorld.AliceUser;
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
            builder.Services.AddSingleton<CallerRecordAccessProbe>(host.Probe);

            // The REAL approval service and everything under it, over the in-memory Dataverse.
            // F4: canApprove asks the decide right through the REAL authorization stack, as the caller.
            builder.Services.AddScoped(sp => host.World.Service(InvoiceApprovalService.DecideCheck(
                sp.GetRequiredService<AuthorizationService>(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)));

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapInvoiceApprovalEndpoints();
            await host._app.StartAsync();
            host._client = host._app.GetTestClient();
            return host;
        }

        public void GrantAll() => Access.Grant(InvoiceSet, World.InvoiceId, AccessRights.Read | AccessRights.Write);

        public Task<HttpResponseMessage> SendAsync(string method, string route, bool authenticated = true, string query = "")
        {
            var request = new HttpRequestMessage(new HttpMethod(method), $"/api/finance/invoices/{World.InvoiceId}/{route}{query}");
            if (authenticated)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
                request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "present");
            }

            return _client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            _client?.Dispose();
            if (_app is not null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }
    }
}
