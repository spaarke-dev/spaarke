using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Services.Signals;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// The Invoice Review read route and the three line-decision routes through the REAL <c>MapInvoiceLineReviewEndpoints</c>,
/// <see cref="FinanceAuthorizationFilter"/>, <see cref="AuthorizationService"/> and <see cref="EbillingEnabledFilter"/> (ebilling-r1 task 100,
/// FR-31, ADR-008, ADR-019, D-77): 401 unauthenticated, 403 for a caller without the right on the invoice (the core-record access guard), the stable
/// <c>ebilling.disabled</c> 503, 409 when the invoice is not In Review, the uniform 404 for a line that is not the invoice's, and the stable
/// <c>errorCode</c> of each 400.
/// </summary>
/// <remarks>
/// The substitution is at the access-data boundary (a recording fake answering "what may this caller do to record X of set S") and at
/// <see cref="IGenericEntityService"/> under the REAL services, with a STRICT mock: a denied request is shown to read nothing and write nothing.
/// The services' own behaviour is <c>InvoiceReviewReadServiceTests</c> and <c>InvoiceLineDecisionServiceTests</c>.
/// <para><b>Seeding proof (task 100 acceptance).</b> <c>Denied_WithoutTheRight_ReadsAndWritesNothing</c> fails when the route filter is removed from
/// a route: the request reaches the service, the strict Dataverse mock throws, and the status is not 403. Recorded in
/// <c>projects/ebilling-r1/notes/100-review-read-model.md</c>.</para>
/// </remarks>
public sealed class InvoiceLineReviewEndpointsAuthorizationContractTests
{
    private const string InvoiceSet = "sprk_invoices"; // FinanceAuthorizationFilter.InvoiceEntitySet

    private static readonly Guid InvoiceId = Guid.Parse("20000000-0000-4000-8000-0000000000a1");
    private static readonly Guid LineId = Guid.Parse("20000000-0000-4000-8000-0000000000b1");
    private static readonly Guid Decider = Guid.Parse("20000000-0000-4000-8000-0000000000c1");

    private static string Read => $"/api/finance/invoices/{InvoiceId}/review";

    private static string Single(Guid? line = null) => $"/api/finance/invoices/{InvoiceId}/lines/{line ?? LineId}/decision";

    private static string Multi => $"/api/finance/invoices/{InvoiceId}/lines/decisions";

    private static string Clean => $"/api/finance/invoices/{InvoiceId}/lines/accept-clean";

    /// <summary>One request per route, with a body that is valid for it.</summary>
    public static IEnumerable<object[]> Routes() =>
    [
        ["GET", Read, null!],
        ["POST", Single(), new { decisionId = Guid.NewGuid(), action = "accept" }],
        ["POST", Multi, new { decisionId = Guid.NewGuid(), action = "accept", lines = new[] { new { lineId = LineId } } }],
        ["POST", Clean, new { decisionId = Guid.NewGuid() }],
    ];

    public static IEnumerable<object[]> DecideRoutes() => Routes().Skip(1);

    // =========================================================================================
    // Authorization (ADR-008)
    // =========================================================================================

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Unauthenticated_Returns401_AndReadsNothing(string method, string url, object? body)
    {
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(Request(method, url, body, authenticated: false));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Access.Calls.Should().BeEmpty();
        host.Entities.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Denied_WithoutTheRight_ReadsAndWritesNothing(string method, string url, object? body)
    {
        // The core-record access guard: a caller with no right on the invoice (which inherits from its core record) is refused before any service runs.
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(Request(method, url, body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle()
            .Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, InvoiceId, HasToken: true));
        host.Entities.VerifyNoOtherCalls();
        host.Probe.SystemUserIdCalls.Should().Be(0);
    }

    public static IEnumerable<object[]> RoutesRefused() => StatedCaller.Cross(Routes(), StatedCaller.RefusedKinds);

    public static IEnumerable<object[]> RoutesInternal() => StatedCaller.Cross(Routes(), StatedCaller.InternalKinds);

    [Theory(DisplayName = "Task 099 must-fire: /review and every line-decision route refuse a flagged-external, row-less or unreadable caller with 403, before any rights call, even one holding Read and Write")]
    [MemberData(nameof(RoutesRefused))]
    public async Task StepZero_RefusesBeforeAnyRightsCallOrRead(string method, string url, object? body, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, InvoiceId, AccessRights.Read | AccessRights.Write | AccessRights.Append);
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(Request(method, url, body));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        host.Access.Calls.Should().BeEmpty();
        host.Entities.VerifyNoOtherCalls();
        host.Probe.SystemUserIdCalls.Should().Be(0);
    }

    [Theory(DisplayName = "Task 099 must-not-fire: /review and every line-decision route with a flag that reads false or blank reach the record check as before")]
    [MemberData(nameof(RoutesInternal))]
    public async Task StepZero_AdmitsAnInternalCaller(string method, string url, object? body, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(Request(method, url, body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle()
            .Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, InvoiceId, HasToken: true));
        host.Entities.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(DecideRoutes))]
    public async Task DecideRoutes_NeedWrite_ReadAloneIsNotEnough(string method, string url, object? body)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, InvoiceId, AccessRights.Read);

        var response = await host.SendAsync(Request(method, url, body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadRoute_NeedsRead_WriteAloneIsNotEnough()
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, InvoiceId, AccessRights.Write | AccessRights.Append);

        var response = await host.SendAsync(Request("GET", Read, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Entities.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task RightOnAnotherInvoice_IsNotRightOnThisOne_AndAQueryIdCannotSubstitute(string method, string url, object? body)
    {
        await using var host = await Host.StartAsync();
        var decoy = Guid.NewGuid();
        host.Access.Grant(InvoiceSet, decoy, AccessRights.Read | AccessRights.Write);

        var response = await host.SendAsync(Request(method, url + $"?invoiceId={decoy}", body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle().Which.Id.Should().Be(InvoiceId);
        host.Entities.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task FailedRightsCheck_IsDenied_NeverPassedThrough(string method, string url, object? body)
    {
        await using var host = await Host.StartAsync();
        host.Access.ThrowOnEveryCall = new InvalidOperationException("Dataverse unreachable");

        var response = await host.SendAsync(Request(method, url, body));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable);
        host.Entities.VerifyNoOtherCalls();
    }

    // =========================================================================================
    // The eBilling kill switch (ADR-018): after authorization, never instead of it
    // =========================================================================================

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task FlagOff_AuthorizedCaller_Gets503EbillingDisabled_AndReadsNothing(string method, string url, object? body)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        host.GrantAll();

        var response = await host.SendAsync(Request(method, url, body));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.Entities.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task FlagOff_UnauthorizedCaller_StillGets403(string method, string url, object? body)
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);

        var response = await host.SendAsync(Request(method, url, body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // =========================================================================================
    // Decisions: status and stable code of each refusal
    // =========================================================================================

    [Fact]
    public async Task Decide_NotInReview_Returns409_ForEveryDecideRoute()
    {
        foreach (var route in DecideRoutes())
        {
            await using var host = await Host.StartAsync();
            host.GrantAll();
            host.Invoice = InvoiceRow(InvoiceStatusCodes.Approved);

            var response = await host.SendAsync(Request((string)route[0], (string)route[1], route[2]));

            response.StatusCode.Should().Be(HttpStatusCode.Conflict, (string)route[1]);
            (await ErrorCode(response)).Should().Be("ebilling.invoice.not_in_review");
            host.Writes.Should().Be(0);
        }
    }

    [Fact]
    public async Task Decide_ALineThatIsNotTheInvoices_Returns404Uniform_AndWritesNothing()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.Invoice = InvoiceRow(InvoiceStatusCodes.InReview);
        host.Lines.Add(LineRow(LineId, InvoiceLineStatus.Flagged));
        var foreign = Guid.NewGuid();

        var response = await host.SendAsync(Request("POST", Single(foreign), new { decisionId = Guid.NewGuid(), action = "accept" }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(foreign.ToString());
        JsonNode.Parse(body)!["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.record_unavailable");
        host.Writes.Should().Be(0);

        var multi = await host.SendAsync(Request(
            "POST", Multi, new { decisionId = Guid.NewGuid(), action = "accept", lines = new[] { new { lineId = LineId }, new { lineId = foreign } } }));
        multi.StatusCode.Should().Be(HttpStatusCode.NotFound, "one foreign line refuses the whole request");
        host.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Decide_APendingLine_Returns409_WithItsCode()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.Invoice = InvoiceRow(InvoiceStatusCodes.InReview);
        host.Lines.Add(LineRow(LineId, InvoiceLineStatus.Pending));

        var response = await host.SendAsync(Request("POST", Single(), new { decisionId = Guid.NewGuid(), action = "accept" }));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCode(response)).Should().Be("ebilling.line.pending");
        host.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData("reduce", null, 50, "ebilling.line.reason_required")]
    [InlineData("disallow", null, null, "ebilling.line.reason_required")]
    [InlineData("reduce", "Too high", null, "ebilling.line.amount_required")]
    [InlineData("accept", null, 50, "ebilling.line.amount_not_allowed")]
    [InlineData("settle", null, null, "ebilling.request.invalid")]
    [InlineData(null, null, null, "ebilling.request.invalid")]
    public async Task Decide_InvalidRequests_Return400_WithStableCodes_BeforeAnyRead(string? action, string? reason, int? amount, string code)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();

        var response = await host.SendAsync(Request("POST", Single(), new { decisionId = Guid.NewGuid(), action, reason, approvedAmount = amount }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).Should().Be(code);
        host.Entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Decide_AmountOutsideTheRange_Returns400()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.Invoice = InvoiceRow(InvoiceStatusCodes.InReview);
        host.Lines.Add(LineRow(LineId, InvoiceLineStatus.Flagged, amount: 100m));

        var response = await host.SendAsync(Request("POST", Single(), new { decisionId = Guid.NewGuid(), action = "reduce", reason = "r", approvedAmount = 100 }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).Should().Be("ebilling.line.amount_out_of_range");
        host.Writes.Should().Be(0);
    }

    [Fact]
    public async Task Decide_WithAnUnprovenIdentity_Returns403_AndReadsNothing()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.Probe.CallerSystemUserId = null;

        var response = await host.SendAsync(Request("POST", Single(), new { decisionId = Guid.NewGuid(), action = "accept" }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]!.GetValue<string>().Should().Be("ebilling.decider.unresolved");
        host.Entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Decide_TheDeciderIsTheCallersOwnSystemUser_NeverTheBody()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.Invoice = InvoiceRow(InvoiceStatusCodes.InReview);

        // Accept all Clean over an invoice with no Clean line: 200, nothing written; the identity was asked for exactly once, from the caller's token.
        var response = await host.SendAsync(Request("POST", Clean, new { decisionId = Guid.NewGuid(), decidedBy = Guid.NewGuid() }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["lines"]!.AsArray().Should().BeEmpty();
        host.Probe.SystemUserIdCalls.Should().Be(1);
        host.Writes.Should().Be(0);
    }

    // =========================================================================================
    // The read route
    // =========================================================================================

    [Fact]
    public async Task Read_AuthorizedCaller_Gets200_WithTheModel()
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, InvoiceId, AccessRights.Read);
        host.Invoice = InvoiceRow(InvoiceStatusCodes.InReview);
        host.Lines.Add(LineRow(LineId, InvoiceLineStatus.Pending));

        var response = await host.SendAsync(Request("GET", Read, null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["header"]!["status"]!.GetValue<string>().Should().Be("InReview");
        json["lines"]!.AsArray().Should().ContainSingle().Which!["statusLabel"]!.GetValue<string>().Should().Be("Processing");
        json["flags"]!["canRead"]!.GetValue<bool>().Should().BeTrue();
        json["flags"]!["canDecide"]!.GetValue<bool>().Should().BeFalse("Read alone does not decide");
    }

    [Fact]
    public async Task Read_InvoiceDeletedAfterTheCheck_Returns404Uniform()
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, InvoiceId, AccessRights.Read);
        host.Invoice = null;

        var response = await host.SendAsync(Request("GET", Read, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(InvoiceId.ToString());
        JsonNode.Parse(body)!["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.record_unavailable");
    }

    // =========================================================================================
    // Host
    // =========================================================================================

    private static async Task<string?> ErrorCode(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]?.GetValue<string>();

    private static Entity InvoiceRow(int status) => new("sprk_invoice", InvoiceId)
    {
        ["statuscode"] = new OptionSetValue(status),
        ["sprk_totalamount"] = new Money(100m),
        ["sprk_project"] = new EntityReference("sprk_project", Guid.NewGuid()),
    };

    private static Entity LineRow(Guid id, InvoiceLineStatus status, decimal amount = 100m) => new("sprk_billingevent", id)
    {
        ["sprk_invoice"] = new EntityReference("sprk_invoice", InvoiceId),
        ["sprk_linesequence"] = 1,
        ["sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Fee),
        ["sprk_amount"] = new Money(amount),
        ["sprk_linestatus"] = new OptionSetValue((int)status),
        ["versionnumber"] = 7L,
    };

    private static HttpRequestMessage Request(string method, string url, object? body, bool authenticated = true)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (authenticated)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
            request.Headers.Add(FinanceAuthzTestAuthHandler.CallerHeader, "present");
        }

        return request;
    }

    private sealed class Host : IAsyncDisposable
    {
        /// <summary>The step-0 caller (task 099); internal unless a test changes <see cref="StatedCaller.Kind"/>.</summary>
        public StatedCaller StepZero { get; private set; } = null!;

        private WebApplication _app = null!;
        private HttpClient _client = null!;
        private int _writes;

        public FinAuth.RecordingAccessDataSource Access { get; } = new();
        public FinAuth.RecordingPrivilegeProbe Probe { get; } = new() { CallerSystemUserId = Decider };
        public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Strict);
        public Entity? Invoice { get; set; }
        public List<Entity> Lines { get; } = [];

        /// <summary>How many writes (line updates, transactions) reached Dataverse.</summary>
        public int Writes => _writes;

        public static async Task<Host> StartAsync(bool ebillingEnabled = true)
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
            builder.Services.AddOptions<EbillingOptions>().Configure(o => o.Enabled = ebillingEnabled);

            // The REAL authorization stack, substituted only at the access-data boundary.
            builder.Services.AddSingleton<IAccessDataSource>(host.Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();
            builder.Services.AddSingleton<CallerRecordAccessProbe>(host.Probe);

            // The REAL services over a strict Dataverse seam that answers reads from the fixture and counts writes.
            host.Entities
                .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
                .Returns((QueryExpression q, CancellationToken _) => Task.FromResult(q.EntityName switch
                {
                    "sprk_invoice" => new EntityCollection(host.Invoice is null ? [] : [host.Invoice]),
                    "sprk_billingevent" => new EntityCollection(host.Lines.ToList()),
                    _ => new EntityCollection(),
                }));
            host.Entities
                .Setup(e => e.BulkUpdateIfRowVersionsMatchAsync(
                    It.IsAny<string>(), It.IsAny<List<(Guid id, Dictionary<string, object> fields, long rowVersion)>>(), It.IsAny<CancellationToken>()))
                .Returns(() => { Interlocked.Increment(ref host._writes); return Task.CompletedTask; });
            host.Entities
                .Setup(e => e.ExecuteTransactionAsync(It.IsAny<IReadOnlyList<OrganizationRequest>>(), It.IsAny<CancellationToken>()))
                .Returns(() => { Interlocked.Increment(ref host._writes); return Task.CompletedTask; });

            var settings = new Mock<IEbillingSettingsReader>();
            settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EbillingSettings());
            // No submission row here, so no Duplicate to judge: the caller's client is strict and never asked (task 119's check is unit-tested).
            builder.Services.AddScoped(sp => new InvoiceReviewReadService(
                host.Entities.Object, sp.GetRequiredService<AuthorizationService>(), settings.Object,
                new Mock<Sprk.Bff.Api.Infrastructure.Dataverse.IDataverseUserClient>(MockBehavior.Strict).Object,
                new EarlierInvoiceReadability(NullLogger<EarlierInvoiceReadability>.Instance), NullLogger<InvoiceReviewReadService>.Instance));

            var locks = new Mock<IIdempotencyService>();
            locks.Setup(l => l.TryAcquireProcessingLockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            var identity = new Mock<IIdentityNormalizationService>();
            identity.Setup(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid u, CancellationToken _) => new PersonIdentity(u));
            var recorder = new BillingDecisionRecorder(
                DecisionRecordWriterTests.WithBillingLookups(new DecisionRecordWriterTests.Harness()).Writer, identity.Object, NullLogger<BillingDecisionRecorder>.Instance);
            builder.Services.AddScoped(_ => new InvoiceLineDecisionService(
                host.Entities.Object, Mock.Of<IInvoiceTotalsService>(), recorder, locks.Object, Mock.Of<IRecordOwnershipResolver>(),
                host.Probe,
                // The fixture's lines carry no Signal, so nothing is closed; the route's authorization is what this suite proves.
                (_, _, _, _, _, _) => Task.FromResult(SignalAutomatedCloseOutcome.Closed),
                NullLogger<InvoiceLineDecisionService>.Instance));

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapInvoiceLineReviewEndpoints();
            await host._app.StartAsync();
            host._client = host._app.GetTestClient();
            return host;
        }

        public void GrantAll() => Access.Grant(InvoiceSet, InvoiceId, AccessRights.Read | AccessRights.Write);

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => _client.SendAsync(request);

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
