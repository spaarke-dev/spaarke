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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// The firm-notice draft route through the REAL <c>MapFirmNoticeEndpoints</c>, <see cref="FinanceAuthorizationFilter"/>, <see cref="AuthorizationService"/>
/// and <see cref="EbillingEnabledFilter"/> (ebilling-r1 task 131, FR-50, ADR-008, ADR-018, ADR-019): 401 unauthenticated, 403 for a caller who may not
/// Reject or Return the invoice (the same <c>ebilling.invoice.decide</c> Write check 104 uses), step 0 (task 099), the stable <c>ebilling.disabled</c>
/// 503, 400 for a kind that is not ReturnedToFirm or InvoiceRejected, 404 / 409 / 503 for the composer's refusals, and 200 with subject, prose, preview
/// HTML and the source.
/// </summary>
/// <remarks>
/// The substitution is at the access-data boundary (a recording fake) and at the composer's projection seam (a delegate), so a denied request is shown
/// to read nothing and call no model. <b>Seeding proof</b>: <c>Denied_WithoutTheRight_ReadsNothing</c>, <c>NeedsWrite_ReadAloneIsNotEnough</c> and
/// <c>RightOnAnotherInvoice_IsNotRightOnThisOne</c> fail when the route filter is removed; recorded in <c>projects/ebilling-r1/notes/131-firm-notice-action.md</c>.
/// </remarks>
public sealed class FirmNoticeEndpointsAuthorizationContractTests
{
    private const string InvoiceSet = "sprk_invoices"; // FinanceAuthorizationFilter.InvoiceEntitySet

    public static IEnumerable<object[]> Kinds() => [["ReturnedToFirm"], ["InvoiceRejected"]];

    public static IEnumerable<object[]> KindsRefused() => StatedCaller.Cross(Kinds(), StatedCaller.RefusedKinds);

    public static IEnumerable<object[]> KindsInternal() => StatedCaller.Cross(Kinds(), StatedCaller.InternalKinds);

    // ── Authorization (ADR-008) ──

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Unauthenticated_Returns401_AndReadsNothing(string kind)
    {
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(kind, authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Access.Calls.Should().BeEmpty();
        host.ProjectionReads.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Denied_WithoutTheRight_ReadsNothing_AndCallsNoModel(string kind)
    {
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(kind);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle()
            .Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, host.InvoiceId, HasToken: true));
        host.ProjectionReads.Should().Be(0);
        host.AiCalls.Should().Be(0);
    }

    [Theory(DisplayName = "Task 099 must-fire: the route refuses a flagged-external, row-less or unreadable caller with 403 before any rights call, even one holding Write")]
    [MemberData(nameof(KindsRefused))]
    public async Task StepZero_RefusesBeforeAnyRightsCallOrRead(string kind, StepZeroCallerKind caller)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.StepZero.Kind = caller;

        var response = await host.SendAsync(kind);

        await host.StepZero.AssertRefusedAsync(response, caller, uniformNotFound: false);
        host.Access.Calls.Should().BeEmpty();
        host.ProjectionReads.Should().Be(0);
    }

    [Theory(DisplayName = "Task 099 must-not-fire: a flag that reads false or blank reaches the record check")]
    [MemberData(nameof(KindsInternal))]
    public async Task StepZero_AdmitsAnInternalCaller(string kind, StepZeroCallerKind caller)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = caller;

        var response = await host.SendAsync(kind);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle().Which.Id.Should().Be(host.InvoiceId);
        host.ProjectionReads.Should().Be(0);
    }

    [Fact]
    public async Task NeedsWrite_ReadAloneIsNotEnough()
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, host.InvoiceId, AccessRights.Read);

        var response = await host.SendAsync("ReturnedToFirm");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.ProjectionReads.Should().Be(0);
    }

    [Fact]
    public async Task RightOnAnotherInvoice_IsNotRightOnThisOne()
    {
        await using var host = await Host.StartAsync();
        var decoy = Guid.NewGuid();
        host.Access.Grant(InvoiceSet, decoy, AccessRights.Read | AccessRights.Write);

        var response = await host.SendAsync("ReturnedToFirm", query: $"&invoiceId={decoy}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle().Which.Id.Should().Be(host.InvoiceId);
        host.ProjectionReads.Should().Be(0);
    }

    [Fact]
    public async Task FailedRightsCheck_IsDenied_NeverPassedThrough()
    {
        await using var host = await Host.StartAsync();
        host.Access.ThrowOnEveryCall = new InvalidOperationException("Dataverse unreachable");

        var response = await host.SendAsync("InvoiceRejected");

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable);
        host.ProjectionReads.Should().Be(0);
    }

    // ── The eBilling kill switch (ADR-018): after authorization, never instead of it ──

    [Fact]
    public async Task FlagOff_AuthorizedCaller_Gets503EbillingDisabled_AndReadsNothing()
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        host.GrantAll();

        var response = await host.SendAsync("ReturnedToFirm");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCode(response)).Should().Be("ebilling.disabled");
        host.ProjectionReads.Should().Be(0);
    }

    [Fact]
    public async Task FlagOff_UnauthorizedCaller_StillGets403()
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);

        (await host.SendAsync("ReturnedToFirm")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── The request ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SubmissionRejected")]
    [InlineData("ApprovedWithAdjustments")]
    [InlineData("returnedtofirm")]
    [InlineData("1")]
    [InlineData("Banana")]
    public async Task AKindThatIsNotReturnedToFirmOrInvoiceRejected_Is400_AndReadsNothing(string? kind)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();

        var response = await host.SendAsync(kind);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).Should().Be("ebilling.firm-notice.invalid_kind");
        host.ProjectionReads.Should().Be(0);
    }

    // ── What the composer answers ──

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Authorized_Returns200_WithSubjectProseHtmlAndSource_AndNoStore(string kind)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();

        var response = await host.SendAsync(kind);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        json["kind"]!.GetValue<string>().Should().Be(kind);
        json["subject"]!.GetValue<string>().Should().Be("Invoice returned for correction (TRK-77.1)");
        json["prose"]!.GetValue<string>().Should().Be("Your invoice has been returned to you.");
        json["html"]!.GetValue<string>().Should().Contain("Your invoice has been returned to you.").And.Contain("Billed total: 100.00");
        json["source"]!.GetValue<string>().Should().Be("Action");
        json["actionVersion"]!.GetValue<string>().Should().Be("INVOICE-FIRM-NOTICE@v1");
        json["fallbackReason"].Should().BeNull();
        host.ProjectionReads.Should().Be(1);
        host.AiCalls.Should().Be(1);
    }

    [Fact]
    public async Task WhenTheActionIsUnavailable_Returns200_WithTheTemplate_AndTheReason()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.AiFailure = new InvoiceAiException(InvoiceAiException.BindingMissing, ConsumerTypes.InvoiceFirmNotice, "no binding");

        var response = await host.SendAsync("InvoiceRejected");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an unavailable Action never fails the reviewer's draft");
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        json["source"]!.GetValue<string>().Should().Be("Template");
        json["fallbackReason"]!.GetValue<string>().Should().Be(InvoiceAiException.BindingMissing);
        json["actionVersion"].Should().BeNull();
        json["prose"]!.GetValue<string>().Should().Be(FirmNoticeFallbackText.Prose[FirmNoticeKind.InvoiceRejected]);
    }

    [Fact]
    public async Task AnInvoiceThatDoesNotExist_Is404Uniform()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.Outcome = null;

        var response = await host.SendAsync("ReturnedToFirm");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.AiCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("ReturnedToFirm", FirmOutcomeKind.Paid)]
    [InlineData("ReturnedToFirm", FirmOutcomeKind.Rejected)]
    [InlineData("InvoiceRejected", FirmOutcomeKind.ReturnedToFirm)]
    [InlineData("InvoiceRejected", FirmOutcomeKind.Closed)]
    public async Task AnInvoiceInAStateTheNoticeDoesNotFit_Is409NotApplicable(string kind, FirmOutcomeKind state)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.Outcome = new FirmOutcome(state, "TRK-1", state.ToString(), 1m, null, [], false);

        var response = await host.SendAsync(kind);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCode(response)).Should().Be("ebilling.firm-notice.not_applicable");
        host.AiCalls.Should().Be(0);
    }

    [Fact]
    public async Task AFaultedProjection_Is503Unavailable_AndNothingIsDrafted()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.Outcome = new FirmOutcome(FirmOutcomeKind.Unavailable, null, "x", null, null, [], true);

        var response = await host.SendAsync("ReturnedToFirm");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCode(response)).Should().Be("ebilling.firm-notice.unavailable");
        host.AiCalls.Should().Be(0);
    }

    // ── Host ──

    private static async Task<string?> ErrorCode(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]?.GetValue<string>();

    private sealed class Host : IAsyncDisposable
    {
        private WebApplication _app = null!;
        private HttpClient _client = null!;

        public StatedCaller StepZero { get; private set; } = null!;
        public Guid InvoiceId { get; } = Guid.NewGuid();
        public FinAuth.RecordingAccessDataSource Access { get; } = new();
        public FinAuth.RecordingPrivilegeProbe Probe { get; } = new();

        public int ProjectionReads;
        public int AiCalls;
        public Exception? AiFailure;

        public FirmOutcome? Outcome = new(
            FirmOutcomeKind.InvoiceInReview, "TRK-77.1", "In review", 100m, null,
            [new FirmOutcomeItem(FirmOutcomeDecision.Disallowed, Guid.NewGuid(), new FirmOutcomeLine(null, null, "A line", 100m, 0m), null, null, [], [])],
            false);

        public static async Task<Host> StartAsync(bool ebillingEnabled = true)
        {
            var host = new Host();
            host.Probe.CallerSystemUserId = Guid.NewGuid();
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

            // The REAL composer, over a recorded projection read and a recorded model call.
            var ai = new Mock<IInvoiceAi>(MockBehavior.Strict);
            ai.Setup(a => a.DraftFirmNoticeAsync(It.IsAny<InvoiceFirmNoticeRequest>(), It.IsAny<CancellationToken>()))
                .Returns((InvoiceFirmNoticeRequest _, CancellationToken _) =>
                {
                    host.AiCalls++;
                    return host.AiFailure is { } failure
                        ? Task.FromException<InvoiceFirmNoticeDraft>(failure)
                        : Task.FromResult(new InvoiceFirmNoticeDraft { Subject = "Invoice returned for correction", Body = "Your invoice has been returned to you." });
                });
            var settings = new Mock<IEbillingSettingsReader>();
            settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EbillingSettings());
            builder.Services.AddScoped(_ => new FirmNoticeComposer(
                (_, _) =>
                {
                    host.ProjectionReads++;
                    return Task.FromResult(host.Outcome);
                },
                (_, _, _) => Task.FromResult(host.Outcome),
                settings.Object, ai.Object, NullLogger<FirmNoticeComposer>.Instance));

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapFirmNoticeEndpoints();
            await host._app.StartAsync();
            host._client = host._app.GetTestClient();
            return host;
        }

        public void GrantAll() => Access.Grant(InvoiceSet, InvoiceId, AccessRights.Read | AccessRights.Write);

        /// <param name="kind">The <c>kind</c> query value; null sends none.</param>
        /// <param name="query">Extra query text, starting with <c>&amp;</c>.</param>
        public Task<HttpResponseMessage> SendAsync(string? kind, bool authenticated = true, string query = "")
        {
            var url = $"/api/finance/invoices/{InvoiceId}/firm-notice/draft" + (kind is null ? "?x=1" : $"?kind={Uri.EscapeDataString(kind)}") + query;
            var request = new HttpRequestMessage(HttpMethod.Post, url);
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
