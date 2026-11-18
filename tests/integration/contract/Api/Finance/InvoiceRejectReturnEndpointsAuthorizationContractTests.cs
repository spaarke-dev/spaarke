using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Jobs;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Tests.Services.Finance;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// The reject and return routes through the REAL <c>MapInvoiceRejectReturnEndpoints</c>, <see cref="FinanceAuthorizationFilter"/>,
/// <see cref="AuthorizationService"/> and <see cref="EbillingEnabledFilter"/> (ebilling-r1 task 104, FR-36, ADR-008, ADR-018, ADR-019): 401
/// unauthenticated, 403 for a caller without Write on the invoice, step 0 (task 099), the stable <c>ebilling.disabled</c> 503, 403
/// <c>ebilling.decider.unresolved</c>, 403 <c>ebilling.approver.not_eligible</c> for a caller WITH Write who is not an eligible approver, 400
/// <c>ebilling.reason_required</c>, and 409 when the invoice is not In Review.
/// </summary>
/// <remarks>
/// The substitution is at the access-data boundary (a recording fake) and at the Dataverse seam under the REAL reject / return, resolver and
/// recorder services (<see cref="ApprovalWorld"/>), so a denied request is shown to read nothing and write nothing.
/// <para><b>Seeding proof (task 104 acceptance).</b> <c>WriteButNotEligible_Is403_AndWritesNothing</c> fails when the service's eligibility refusal
/// is removed. <c>Denied_WithoutTheRight_ReadsAndWritesNothing</c> and <c>RightOnAnotherInvoice_IsNotRightOnThisOne</c> fail when the route filter
/// is removed. <c>NoReason_Is400_AndWritesNothing</c> fails when the reason check is removed. Recorded in
/// <c>projects/ebilling-r1/notes/104-reject-return.md</c>.</para>
/// </remarks>
public sealed class InvoiceRejectReturnEndpointsAuthorizationContractTests
{
    private const string InvoiceSet = "sprk_invoices"; // FinanceAuthorizationFilter.InvoiceEntitySet

    public static IEnumerable<object[]> Routes() => [["reject"], ["return"]];

    public static IEnumerable<object[]> RoutesRefused() => StatedCaller.Cross(Routes(), StatedCaller.RefusedKinds);

    public static IEnumerable<object[]> RoutesInternal() => StatedCaller.Cross(Routes(), StatedCaller.InternalKinds);

    private static string ActionOf(string route) => route == "reject" ? "reject-invoice" : "return-to-firm";

    // =========================================================================================
    // Authorization (ADR-008)
    // =========================================================================================

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Unauthenticated_Returns401_AndReadsNothing(string route)
    {
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(route, authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Access.Calls.Should().BeEmpty();
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Denied_WithoutTheRight_ReadsAndWritesNothing(string route)
    {
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle()
            .Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, host.World.InvoiceId, HasToken: true));
        host.World.Entities.Invocations.Should().BeEmpty();
        host.Probe.SystemUserIdCalls.Should().Be(0);
    }

    [Theory(DisplayName = "Task 099 must-fire: both routes refuse a flagged-external, row-less or unreadable caller with 403 before any rights call, even one holding Write")]
    [MemberData(nameof(RoutesRefused))]
    public async Task StepZero_RefusesBeforeAnyRightsCallOrRead(string route, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(route);

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        host.Access.Calls.Should().BeEmpty();
        host.World.Entities.Invocations.Should().BeEmpty();
        host.Probe.SystemUserIdCalls.Should().Be(0);
    }

    [Theory(DisplayName = "Task 099 must-not-fire: both routes with a flag that reads false or blank reach the record check")]
    [MemberData(nameof(RoutesInternal))]
    public async Task StepZero_AdmitsAnInternalCaller(string route, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle().Which.Id.Should().Be(host.World.InvoiceId);
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task NeedsWrite_ReadAloneIsNotEnough(string route)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, host.World.InvoiceId, AccessRights.Read);

        var response = await host.SendAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.World.Entities.Invocations.Should().BeEmpty();
        host.World.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task RightOnAnotherInvoice_IsNotRightOnThisOne(string route)
    {
        await using var host = await Host.StartAsync();
        var decoy = Guid.NewGuid();
        host.Access.Grant(InvoiceSet, decoy, AccessRights.Read | AccessRights.Write);

        var response = await host.SendAsync(route, query: $"?invoiceId={decoy}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle().Which.Id.Should().Be(host.World.InvoiceId);
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task FailedRightsCheck_IsDenied_NeverPassedThrough(string route)
    {
        await using var host = await Host.StartAsync();
        host.Access.ThrowOnEveryCall = new InvalidOperationException("Dataverse unreachable");

        var response = await host.SendAsync(route);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable);
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    // =========================================================================================
    // The eBilling kill switch (ADR-018): after authorization, never instead of it
    // =========================================================================================

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task FlagOff_AuthorizedCaller_Gets503EbillingDisabled_AndReadsNothing(string route)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        host.GrantAll();

        var response = await host.SendAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCode(response)).Should().Be("ebilling.disabled");
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task FlagOff_UnauthorizedCaller_StillGets403(string route)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);

        var response = await host.SendAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // =========================================================================================
    // Who decides (FR-35 / F-7): the caller's own identity, live eligibility
    // =========================================================================================

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task WithAnUnprovenIdentity_Returns403_AndReadsNothing(string route)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.Probe.CallerSystemUserId = null;

        var response = await host.SendAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCode(response)).Should().Be("ebilling.decider.unresolved");
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Theory(DisplayName = "104 FR-35 SEEDED: a caller WITH Write on the invoice who is not an eligible approver gets 403 not_eligible and nothing is written")]
    [MemberData(nameof(Routes))]
    public async Task WriteButNotEligible_Is403_AndWritesNothing(string route)
    {
        await using var host = await Host.StartAsync(caller: ApprovalWorld.CarolUser);
        host.GrantAll();
        host.World.AddLine(InvoiceLineStatus.Clean);

        var response = await host.SendAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCode(response)).Should().Be("ebilling.approver.not_eligible");
        host.World.Writes.Should().Be(0);
        host.World.Transitions.Should().Be(0);
        host.World.RecordsOf(ActionOf(route)).Should().BeEmpty();
        host.World.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
    }

    [Theory(DisplayName = "104 FR-36: an eligible approver gets 200 decided; a second call is 200 alreadyDecided; ONE Decision Record")]
    [MemberData(nameof(Routes))]
    public async Task EligibleApprover_Gets200_ThenASecondCallIsANoOp(string route)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();

        var first = await host.SendAsync(route);
        var second = await host.SendAsync(route);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await first.Content.ReadAsStringAsync())!;
        json["outcome"]!.GetValue<string>().Should().Be("decided");
        json["decision"]!.GetValue<string>().Should().Be(route == "reject" ? "Rejected" : "Returned");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.Parse(await second.Content.ReadAsStringAsync())!["outcome"]!.GetValue<string>().Should().Be("alreadyDecided");
        host.World.RecordsOf(ActionOf(route)).Should().ContainSingle()
            .Which.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>("sprk_confirmedby").Id.Should().Be(ApprovalWorld.AliceUser);
        host.World.InvoiceStatus.Should().Be(route == "reject" ? InvoiceStatusCodes.Rejected : InvoiceStatusCodes.ReturnedToFirm);
    }

    // =========================================================================================
    // FR-36: the reason and the status
    // =========================================================================================

    [Theory(DisplayName = "104 FR-36 SEEDED: no body, an empty body, no reason or a blank reason is 400 ebilling.reason_required and writes nothing")]
    [InlineData("reject", null)]
    [InlineData("reject", "{}")]
    [InlineData("reject", "{\"reason\":\"   \"}")]
    [InlineData("return", null)]
    [InlineData("return", "{}")]
    [InlineData("return", "{\"reason\":\"\"}")]
    public async Task NoReason_Is400_AndWritesNothing(string route, string? body)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();

        var response = await host.SendAsync(route, body: body, jsonBody: body is not null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).Should().Be("ebilling.reason_required");
        host.World.Writes.Should().Be(0);
        host.World.Transitions.Should().Be(0);
        host.World.RecordsOf(ActionOf(route)).Should().BeEmpty();
        host.World.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
    }

    [Theory(DisplayName = "095f-2a: a body with a note is 200; the note is in the record's Values and in the internal sprk_decisionnote column")]
    [MemberData(nameof(Routes))]
    public async Task Note_IsRecorded(string route)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();

        var response = await host.SendAsync(route, body: "{\"reason\":\"Because\",\"note\":\"Spoke to the partner.\\nAgreed.\"}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var record = host.World.RecordsOf(ActionOf(route)).Should().ContainSingle().Subject;
        JsonNode.Parse(record.GetAttributeValue<string>("sprk_steps"))!["steps"]![0]!["values"]!["note"]!.GetValue<string>()
            .Should().Be("Spoke to the partner.\nAgreed.");
        host.World.Invoice.GetAttributeValue<string>(InvoiceCorrectionNote.DecisionColumn).Should().Be("Spoke to the partner.\nAgreed.");
    }

    [Theory(DisplayName = "095f-2a: a 2001-character note is 400 ebilling.note_too_long; a control character is 400 ebilling.note_invalid_characters; nothing is read or written")]
    [InlineData("reject", "too-long", "ebilling.note_too_long")]
    [InlineData("return", "too-long", "ebilling.note_too_long")]
    [InlineData("reject", "bell", "ebilling.note_invalid_characters")]
    [InlineData("return", "bell", "ebilling.note_invalid_characters")]
    public async Task RefusedNote_Is400_AndWritesNothing(string route, string shape, string code)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        var note = shape == "too-long" ? new string('x', InvoiceCorrectionNote.MaxLength + 1) : "see\\u0007this";

        var response = await host.SendAsync(route, body: $"{{\"reason\":\"Because\",\"note\":\"{note}\"}}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).Should().Be(code);
        host.World.Entities.Invocations.Should().BeEmpty("refused before any read");
        host.World.Transitions.Should().Be(0);
        host.World.RecordsOf(ActionOf(route)).Should().BeEmpty();
        host.World.InvoiceStatus.Should().Be(InvoiceStatusCodes.InReview);
    }

    [Theory(DisplayName = "104 FR-36: an invoice that is not In Review is 409 not_in_review")]
    [MemberData(nameof(Routes))]
    public async Task NotInReview_Is409(string route)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.World.Invoice["statuscode"] = new Microsoft.Xrm.Sdk.OptionSetValue(InvoiceStatusCodes.Approved);

        var response = await host.SendAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCode(response)).Should().Be("ebilling.invoice.not_in_review");
        host.World.Writes.Should().Be(0);
    }

    [Theory(DisplayName = "104 D-101: a held invoice is 409 ebilling.invoice.held")]
    [MemberData(nameof(Routes))]
    public async Task Held_Is409(string route)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.World.Hold();

        var response = await host.SendAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCode(response)).Should().Be("ebilling.invoice.held");
        host.World.Writes.Should().Be(0);
    }

    [Theory(DisplayName = "104: a decision that stopped after the status moved is 503 ebilling.invoice.decision_incomplete with the failed step; the repeat completes it")]
    [MemberData(nameof(Routes))]
    public async Task Incomplete_Is503_ThenTheRepeatCompletes(string route)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.World.FaultsOnce["link"] = new InvalidOperationException("dataverse down");

        var failed = await host.SendAsync(route);
        var repeat = await host.SendAsync(route);

        failed.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var json = JsonNode.Parse(await failed.Content.ReadAsStringAsync())!;
        json["errorCode"]!.GetValue<string>().Should().Be("ebilling.invoice.decision_incomplete");
        json["failedStep"]!.GetValue<string>().Should().Be("link");
        repeat.StatusCode.Should().Be(HttpStatusCode.OK);
        host.World.RecordsOf(ActionOf(route)).Should().ContainSingle();
    }

    // =========================================================================================
    // Task 132: the notification job (FR-39, EB-D-258)
    // =========================================================================================

    [Theory(DisplayName = "132 EB-D-258: a decision enqueues ONE notification job; it carries the reviewer-confirmed notice when the request had both parts, and none otherwise")]
    [InlineData("reject", EbillingNotificationEvent.InvoiceRejected)]
    [InlineData("return", EbillingNotificationEvent.ReturnedToFirm)]
    public async Task Decided_EnqueuesOneJob_WithTheConfirmedNoticeWhenGiven(string route, EbillingNotificationEvent expected)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();

        var response = await host.SendAsync(route, body: "{\"reason\":\"Because\",\"noticeSubject\":\"Please correct\",\"noticeProse\":\"Please correct the hours.\"}");
        var repeat = await host.SendAsync(route, body: "{\"reason\":\"Because\",\"noticeSubject\":\"Please correct\",\"noticeProse\":\"Please correct the hours.\"}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        repeat.StatusCode.Should().Be(HttpStatusCode.OK, "alreadyDecided");
        var job = host.Queued.Should().ContainSingle("a repeat of a decided invoice is a no-op and enqueues nothing").Subject;
        job.IdempotencyKey.Should().Be(EbillingNotificationEnqueuer.IdempotencyKeyFor(expected, host.World.InvoiceId));
        var payload = EbillingNotificationEnqueuer.ParsePayload(job.Payload)!;
        payload.Event.Should().Be(expected);
        payload.Subject.Should().Be(EbillingNotificationSubject.Invoice);
        payload.NoticeSubject.Should().Be("Please correct");
        payload.NoticeProse.Should().Be("Please correct the hours.");
    }

    [Theory(DisplayName = "132 EB-D-258: a decision WITHOUT the confirmed notice enqueues the job with none (the job then sends no firm email)")]
    [MemberData(nameof(Routes))]
    public async Task Decided_WithoutANotice_EnqueuesTheJobWithNone(string route)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();

        await host.SendAsync(route);

        var payload = EbillingNotificationEnqueuer.ParsePayload(host.Queued.Should().ContainSingle().Subject.Payload)!;
        payload.HasConfirmedNotice.Should().BeFalse();
    }

    [Theory(DisplayName = "132 review F1 (must-fire): a confirmed notice the firm screen refuses is 400 ebilling.notice_refused with the reason code; NOTHING is decided, recorded or queued")]
    [InlineData("reject", "We will pay 5000 by Friday.", "Please correct", "prose.digit")]
    [InlineData("return", "Please see https://evil.test/pay now.", "Please correct", "prose.link")]
    [InlineData("return", "Please correct the hours.", "Subject with a number 12", "digit")]
    public async Task ARefusedNotice_Is400_AndNothingIsDecided(string route, string prose, string subject, string reasonFragment)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        var before = host.World.InvoiceStatus;
        var body = JsonSerializer.Serialize(new { reason = "Because", noticeSubject = subject, noticeProse = prose });

        var response = await host.SendAsync(route, body: body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync();
        JsonNode.Parse(text)!["errorCode"]?.GetValue<string>().Should().Be(InvoiceRejectReturnCodes.NoticeRefused);
        text.Should().Contain(reasonFragment, "the reason code is named so the reviewer can fix the notice").And.NotContain(prose, "never the text back");
        host.World.InvoiceStatus.Should().Be(before, "nothing is decided");
        host.World.RecordsOf(ActionOf(route)).Should().BeEmpty("no Decision Record is written");
        host.Queued.Should().BeEmpty("no notification job is queued");

        // The control: the same invoice, decided again with a notice that passes, is decided as before (the refusal left nothing behind).
        var good = await host.SendAsync(route, body: "{\"reason\":\"Because\",\"noticeSubject\":\"Please correct\",\"noticeProse\":\"Please correct the hours.\"}");
        good.StatusCode.Should().Be(HttpStatusCode.OK);
        host.World.InvoiceStatus.Should().Be(route == "reject" ? InvoiceStatusCodes.Rejected : InvoiceStatusCodes.ReturnedToFirm);
        host.Queued.Should().ContainSingle();
    }

    [Theory(DisplayName = "132 review F1 (must-fire): a notice longer than the caps is 400 and nothing is decided; half a notice (only the prose) is screened too")]
    [MemberData(nameof(Routes))]
    public async Task ATooLongOrHalfNotice_Is400_AndNothingIsDecided(string route)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        var before = host.World.InvoiceStatus;
        var tooLong = JsonSerializer.Serialize(new { reason = "Because", noticeSubject = "Please correct", noticeProse = new string('a', 20_000) });
        var halfWithDigit = JsonSerializer.Serialize(new { reason = "Because", noticeProse = "Pay 5000 now." });

        var long1 = await host.SendAsync(route, body: tooLong);
        var half = await host.SendAsync(route, body: halfWithDigit);

        long1.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        half.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.World.InvoiceStatus.Should().Be(before);
        host.Queued.Should().BeEmpty();
    }

    [Theory(DisplayName = "132 review F1 (must-not-fire): the subject the draft route returned (with the code-written tracking suffix) passes the screen and is decided")]
    [MemberData(nameof(Routes))]
    public async Task TheDraftsOwnSubject_WithItsTrackingSuffix_IsDecided(string route)
    {
        await using var host = await Host.StartAsync();
        host.World.Invoice["sprk_trackingnumber"] = "TRK-1.2";
        host.GrantAll();

        var response = await host.SendAsync(route, body: "{\"reason\":\"Because\",\"noticeSubject\":\"Please correct your invoice (TRK-1.2)\",\"noticeProse\":\"Please correct the hours.\"}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the tracking number is code-written, not the reviewer's words, so its digit is not refused");
        host.Queued.Should().ContainSingle();
    }

    [Theory(DisplayName = "132: a decision that is REFUSED (no right, not eligible, no reason) enqueues no job")]
    [MemberData(nameof(Routes))]
    public async Task Refused_EnqueuesNothing(string route)
    {
        await using var host = await Host.StartAsync();
        var denied = await host.SendAsync(route);
        host.GrantAll();
        var noReason = await host.SendAsync(route, body: "{}");

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        noReason.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Queued.Should().BeEmpty();
    }

    [Theory(DisplayName = "132: a queue that is down never fails a decision that has been made")]
    [MemberData(nameof(Routes))]
    public async Task QueueDown_DoesNotFailTheDecision(string route)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.QueueFaults = true;

        var response = await host.SendAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.World.InvoiceStatus.Should().Be(route == "reject" ? InvoiceStatusCodes.Rejected : InvoiceStatusCodes.ReturnedToFirm);
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

        /// <summary>The notification jobs the routes queued (task 132).</summary>
        public List<JobContract> Queued { get; } = [];

        /// <summary>When set, the queue refuses every job (the decision must still stand).</summary>
        public bool QueueFaults { get; set; }

        public EbillingNotificationEnqueuer Enqueuer()
        {
            var queue = new Mock<JobSubmissionService>(
                MockBehavior.Loose,
                Microsoft.Extensions.Options.Options.Create(new ServiceBusOptions { QueueName = "sdap-jobs" }),
                Mock.Of<ILogger<JobSubmissionService>>(),
                new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object);
            queue.Setup(q => q.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
                .Returns((JobContract job, CancellationToken _) =>
                {
                    if (QueueFaults)
                    {
                        return Task.FromException(new InvalidOperationException("service bus down"));
                    }

                    Queued.Add(job);
                    return Task.CompletedTask;
                });
            return new EbillingNotificationEnqueuer(queue.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<EbillingNotificationEnqueuer>.Instance);
        }
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

            // The REAL reject / return service and everything under it, over the in-memory Dataverse.
            builder.Services.AddScoped(_ => host.World.RejectReturnService());

            // Task 132: the REAL enqueuer over a recording queue, so a decision's notification job is observed (what it carries, and that a refusal enqueues none).
            builder.Services.AddScoped(_ => host.Enqueuer());

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapInvoiceRejectReturnEndpoints();
            await host._app.StartAsync();
            host._client = host._app.GetTestClient();
            return host;
        }

        public void GrantAll() => Access.Grant(InvoiceSet, World.InvoiceId, AccessRights.Read | AccessRights.Write);

        /// <param name="body">The request body; the default is a JSON body with a reason.</param>
        /// <param name="jsonBody">False sends no body at all.</param>
        public Task<HttpResponseMessage> SendAsync(string route, bool authenticated = true, string query = "", string? body = "{\"reason\":\"Because\"}", bool jsonBody = true)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/finance/invoices/{World.InvoiceId}/{route}{query}");
            if (authenticated)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
                request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "present");
            }

            if (jsonBody && body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
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
