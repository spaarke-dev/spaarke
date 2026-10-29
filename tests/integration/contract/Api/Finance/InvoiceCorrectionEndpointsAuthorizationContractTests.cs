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
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Tests.Services.Finance;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;
using W = Sprk.Bff.Api.Tests.Services.Finance.CorrectionWorld;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// The correction routes through the REAL <c>MapInvoiceCorrectionEndpoints</c>, <see cref="FinanceAuthorizationFilter"/>,
/// <see cref="AuthorizationService"/> and <see cref="EbillingEnabledFilter"/> (ebilling-r1 task 101 part B1, FR-31, FR-22, ADR-008, ADR-019, D-64, D-77):
/// 401 unauthenticated, 403 for a caller without the right on the invoice (the core-record access guard), the stable <c>ebilling.disabled</c> 503,
/// 409 when the invoice is not In Review, the line is decided or the row version is stale, the uniform 404, and the stable <c>errorCode</c> of each 400.
/// </summary>
/// <remarks>
/// The substitution is at the access-data boundary (a recording fake) and at <see cref="IGenericEntityService"/> under the REAL
/// <see cref="InvoiceCorrectionService"/> (the in-memory <see cref="CorrectionWorld"/> behind a STRICT mock): a denied request is shown to read nothing and
/// write nothing. The service's own behaviour is <c>InvoiceCorrectionServiceTests</c>.
/// <para><b>Seeding proof.</b> <c>Denied_WithoutTheRight_ReadsAndWritesNothing</c> fails when the route filter is removed from a route: the request
/// reaches the service, the strict Dataverse mock throws on the unmodelled call, and the status is not 403. Recorded in
/// <c>projects/ebilling-r1/notes/101-corrections-capture.md</c>.</para>
/// </remarks>
public sealed class InvoiceCorrectionEndpointsAuthorizationContractTests
{
    private const string InvoiceSet = "sprk_invoices"; // FinanceAuthorizationFilter.InvoiceEntitySet

    private static string LineRoute(Guid? line = null) => $"/api/finance/invoices/{W.InvoiceId}/lines/{line ?? W.FeeLineId}/corrections";

    private static string HeaderRoute => $"/api/finance/invoices/{W.InvoiceId}/header/corrections";

    private static string ListRoute => $"/api/finance/invoices/{W.InvoiceId}/corrections";

    private static object LineBody() => new { field = "Hours", value = "2.5" };

    private static object HeaderBody() => new { field = "TotalDue", value = "2400" };

    /// <summary>One request per route, with a body that is valid for it.</summary>
    public static IEnumerable<object[]> Routes() =>
    [
        ["POST", LineRoute(), LineBody()],
        ["POST", HeaderRoute, HeaderBody()],
        ["GET", ListRoute, null!],
    ];

    public static IEnumerable<object[]> WriteRoutes() => Routes().Take(2);

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
        host.World.Entities.VerifyNoOtherCalls();
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
            .Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, W.InvoiceId, HasToken: true));
        host.World.Entities.VerifyNoOtherCalls();
        host.Probe.SystemUserIdCalls.Should().Be(0);
    }

    public static IEnumerable<object[]> RoutesRefused() => StatedCaller.Cross(Routes(), StatedCaller.RefusedKinds);

    public static IEnumerable<object[]> RoutesInternal() => StatedCaller.Cross(Routes(), StatedCaller.InternalKinds);

    [Theory(DisplayName = "Task 099 must-fire: every correction route refuses a flagged-external, row-less or unreadable caller with 403, before any rights call, even one holding Read and Write")]
    [MemberData(nameof(RoutesRefused))]
    public async Task StepZero_RefusesBeforeAnyRightsCallOrRead(string method, string url, object? body, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, W.InvoiceId, AccessRights.Read | AccessRights.Write | AccessRights.Append);
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(Request(method, url, body));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: false);
        host.Access.Calls.Should().BeEmpty();
        host.World.Entities.VerifyNoOtherCalls();
        host.Probe.SystemUserIdCalls.Should().Be(0);
    }

    [Theory(DisplayName = "Task 099 must-not-fire: every correction route with a flag that reads false or blank reaches the record check as before")]
    [MemberData(nameof(RoutesInternal))]
    public async Task StepZero_AdmitsAnInternalCaller(string method, string url, object? body, StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(Request(method, url, body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Access.Calls.Should().ContainSingle()
            .Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, W.InvoiceId, HasToken: true));
        host.World.Entities.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task WriteRoutes_NeedWrite_ReadAloneIsNotEnough(string method, string url, object? body)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, W.InvoiceId, AccessRights.Read);

        var response = await host.SendAsync(Request(method, url, body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.World.Entities.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ListRoute_NeedsRead_WriteAloneIsNotEnough()
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, W.InvoiceId, AccessRights.Write | AccessRights.Append);

        var response = await host.SendAsync(Request("GET", ListRoute, null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.World.Entities.VerifyNoOtherCalls();
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
        host.Access.Calls.Should().ContainSingle().Which.Id.Should().Be(W.InvoiceId);
        host.World.Entities.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task FailedRightsCheck_IsDenied_NeverPassedThrough(string method, string url, object? body)
    {
        await using var host = await Host.StartAsync();
        host.Access.ThrowOnEveryCall = new InvalidOperationException("Dataverse unreachable");

        var response = await host.SendAsync(Request(method, url, body));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.ServiceUnavailable);
        host.World.Entities.VerifyNoOtherCalls();
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
        (await ErrorCode(response)).Should().Be("ebilling.disabled");
        host.World.Entities.VerifyNoOtherCalls();
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
    // Status and stable code of each refusal
    // =========================================================================================

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task Writes_NotInReview_Return409(string method, string url, object? body)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.World.Invoice["statuscode"] = new OptionSetValue(InvoiceStatusCodes.Approved);

        var response = await host.SendAsync(Request(method, url, body));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCode(response)).Should().Be("ebilling.invoice.not_in_review");
        host.World.ValueWrites.Should().BeEmpty();
        host.World.Corrections.Should().BeEmpty();
    }

    [Theory]
    [InlineData((int)InvoiceLineStatus.Accepted, "ebilling.correction.line_not_correctable")]
    [InlineData((int)InvoiceLineStatus.Disallowed, "ebilling.correction.line_not_correctable")]
    [InlineData((int)InvoiceLineStatus.Pending, "ebilling.line.pending")]
    public async Task ADecidedOrPendingLine_Returns409_AndWritesNothing(int status, string code)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.World.Line()["sprk_linestatus"] = new OptionSetValue(status);

        var response = await host.SendAsync(Request("POST", LineRoute(), LineBody()));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCode(response)).Should().Be(code);
        host.World.ValueWrites.Should().BeEmpty();
        host.World.Corrections.Should().BeEmpty();
    }

    [Fact]
    public async Task AStaleRowVersion_Returns409_ForTheLine_AndForTheHeader()
    {
        await using var line = await Host.StartAsync();
        line.GrantAll();
        line.World.BeforeValueWrite = () => line.World.Line()["versionnumber"] = 99L;
        await using var header = await Host.StartAsync();
        header.GrantAll();
        header.World.BeforeValueWrite = () => header.World.Invoice["versionnumber"] = 99L;

        var lineResponse = await line.SendAsync(Request("POST", LineRoute(), LineBody()));
        var headerResponse = await header.SendAsync(Request("POST", HeaderRoute, HeaderBody()));

        lineResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCode(lineResponse)).Should().Be("ebilling.line.changed");
        headerResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCode(headerResponse)).Should().Be("ebilling.invoice.changed");
        line.World.ValueWrites.Should().BeEmpty();
        line.World.Line().GetAttributeValue<decimal>("sprk_quantity").Should().Be(25m);
    }

    [Fact]
    public async Task ALineThatIsNotTheInvoices_Returns404Uniform_AndWritesNothing()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        var foreign = W.FeeLine();
        foreign["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid());
        host.World.Lines.Clear();
        host.World.Lines.Add(foreign);

        var response = await host.SendAsync(Request("POST", LineRoute(), LineBody()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(W.FeeLineId.ToString());
        JsonNode.Parse(body)!["reasonCode"]!.GetValue<string>().Should().Be("sdap.access.deny.record_unavailable");
        host.World.Corrections.Should().BeEmpty();
    }

    [Fact]
    public async Task ACodeOutsideTheLinesCodeSet_Returns400_WithItsCode_AndWritesNothing()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();

        var response = await host.SendAsync(Request("POST", LineRoute(), new { field = "TaskCode", value = "L999" }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).Should().Be("ebilling.code.not_in_set");
        host.World.Corrections.Should().BeEmpty();
        host.World.ValueWrites.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Date", "2026-09-13", "ebilling.correction.field_not_supported_yet")]
    [InlineData("TimekeeperAsBilled", "B. Chan", "ebilling.correction.field_not_supported_yet")]
    [InlineData("Bogus", "1", "ebilling.correction.field_unknown")]
    [InlineData("TotalDue", "1", "ebilling.correction.field_unknown")] // a header field on the line route
    [InlineData("Hours", null, "ebilling.correction.value_required")]
    [InlineData("Hours", "abc", "ebilling.correction.value_invalid")]
    [InlineData("Rate", "-5", "ebilling.correction.value_invalid")]
    public async Task LineRoute_InvalidRequests_Return400_WithStableCodes_BeforeAnyRead(string field, string? value, string code)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();

        var response = await host.SendAsync(Request("POST", LineRoute(), new { field, value }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).Should().Be(code);
        host.World.Entities.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("InvoiceDate", "2026-10-01", "ebilling.correction.field_not_supported_yet")]
    [InlineData("Hours", "1", "ebilling.correction.field_unknown")] // a line field on the header route
    [InlineData("BillingPeriodStart", "13/09/2026", "ebilling.correction.value_invalid")]
    [InlineData("Currency", "US", "ebilling.correction.value_invalid")]
    public async Task HeaderRoute_InvalidRequests_Return400_WithStableCodes_BeforeAnyRead(string field, string? value, string code)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();

        var response = await host.SendAsync(Request("POST", HeaderRoute, new { field, value }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).Should().Be(code);
        host.World.Entities.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task Writes_WithoutABody_Return400(string method, string url, object? _)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();

        var response = await host.SendAsync(Request(method, url, null));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnsupportedMediaType);
        host.World.Entities.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(WriteRoutes))]
    public async Task Writes_WithAnUnprovenIdentity_Return403_AndReadNothing(string method, string url, object? body)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.Probe.CallerSystemUserId = null;

        var response = await host.SendAsync(Request(method, url, body));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]!.GetValue<string>().Should().Be("ebilling.decider.unresolved");
        host.World.Entities.VerifyNoOtherCalls();
    }

    // =========================================================================================
    // The happy path, one per correctable field kind
    // =========================================================================================

    [Theory]
    [InlineData("Hours", "2.5", "2.5")] // a quantity
    [InlineData("Rate", "95.5", "95.50")] // money
    [InlineData("Amount", "2000", "2000.00")] // money
    [InlineData("Narrative", "Corrected narrative", "Corrected narrative")] // text
    [InlineData("TaskCode", "l100", "L100")] // a code, stored as the set spells it
    public async Task LineField_Succeeds_With200_OneCorrectedRow_AndTheCallersOwnReviewer(string field, string value, string stored)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();

        // The body also names an outcome, a role and a reviewer: none is read.
        var response = await host.SendAsync(Request("POST", LineRoute(), new
        {
            field, value, outcome = "Confirmed", role = "billing-admin", reviewer = Guid.NewGuid(), isLabel = false,
        }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["alreadyApplied"]!.GetValue<bool>().Should().BeFalse();
        json["correction"]!["field"]!.GetValue<string>().Should().Be(field);
        json["correction"]!["finalValue"]!.GetValue<string>().Should().Be(stored);
        json["correction"]!["outcome"]!.GetValue<string>().Should().Be("Corrected");
        json["correction"]!["isLabel"]!.GetValue<bool>().Should().BeTrue();
        json["correction"]!["reviewedBy"]!.GetValue<Guid>().Should().Be(W.Reviewer);
        host.Probe.SystemUserIdCalls.Should().Be(1);
        host.World.Corrections.Should().ContainSingle();
        host.World.ValueWrites.Should().ContainSingle();
    }

    [Theory]
    [InlineData("FirmInvoiceNumber", "INV-2002", "INV-2002")] // text
    [InlineData("BillingPeriodStart", "2026-08-15", "2026-08-15")] // a date
    [InlineData("Currency", "cad", "CAD")] // a currency
    [InlineData("TotalDue", "2400", "2400.00")] // money
    public async Task HeaderField_Succeeds_With200_OneCorrectedRowWithNoLine(string field, string value, string stored)
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();

        var response = await host.SendAsync(Request("POST", HeaderRoute, new { field, value }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["correction"]!["field"]!.GetValue<string>().Should().Be(field);
        json["correction"]!["finalValue"]!.GetValue<string>().Should().Be(stored);
        json["correction"]!["lineId"].Should().BeNull();
        host.World.Corrections.Should().ContainSingle();
        host.World.ValueWrites.Should().ContainSingle().Which.Entity.Should().Be("sprk_invoice");
    }

    [Fact]
    public async Task Confirm_Succeeds_With200_AConfirmedRow_AndNoValueWrite()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();

        var response = await host.SendAsync(Request("POST", LineRoute(), new { field = "Hours", confirm = true }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        json["correction"]!["outcome"]!.GetValue<string>().Should().Be("Confirmed");
        json["correction"]!["isLabel"]!.GetValue<bool>().Should().BeTrue();
        host.World.ValueWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task ARepeatedRequest_Returns200AlreadyApplied_AndWritesNoSecondRow()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();

        var first = await host.SendAsync(Request("POST", LineRoute(), LineBody()));
        var again = await host.SendAsync(Request("POST", LineRoute(), LineBody()));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.Parse(await again.Content.ReadAsStringAsync())!["alreadyApplied"]!.GetValue<bool>().Should().BeTrue();
        host.World.Corrections.Should().ContainSingle();
        host.World.ValueWrites.Should().ContainSingle();
    }

    // =========================================================================================
    // The corrections list
    // =========================================================================================

    [Fact]
    public async Task List_Returns200_ThisInvoicesRowsNewestFirst_FilterableBySourceCodeAndVersion()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        await host.SendAsync(Request("POST", LineRoute(), LineBody()));
        host.World.Time.Now = W.T0.AddMinutes(2);
        await host.SendAsync(Request("POST", LineRoute(), new { field = "TaskCode", value = "L100" }));
        host.World.AddRow(new Entity("sprk_invoicecorrection", Guid.NewGuid())
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", Guid.NewGuid()), // another invoice
            ["sprk_fieldname"] = "Hours", ["sprk_outcometype"] = new OptionSetValue(100000000), ["sprk_sourcecode"] = "sprk_invoice_v1",
            ["sprk_reviewedon"] = W.T0.AddMinutes(30),
        });

        var all = await host.SendAsync(Request("GET", ListRoute, null));
        var byAction = await host.SendAsync(Request("GET", ListRoute + "?sourceCode=invoice-line-code&sourceVersion=1", null));
        var wrongVersion = await host.SendAsync(Request("GET", ListRoute + "?sourceCode=invoice-line-code&sourceVersion=2", null));

        all.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonNode.Parse(await all.Content.ReadAsStringAsync())!["corrections"]!.AsArray().Select(c => c!["field"]!.GetValue<string>())
            .Should().Equal("TaskCode", "Hours");
        JsonNode.Parse(await byAction.Content.ReadAsStringAsync())!["corrections"]!.AsArray().Select(c => c!["field"]!.GetValue<string>())
            .Should().Equal("TaskCode");
        JsonNode.Parse(await wrongVersion.Content.ReadAsStringAsync())!["corrections"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task List_AReadFault_Returns503_NotAnEmptyList()
    {
        await using var host = await Host.StartAsync();
        host.GrantAll();
        host.World.ReadFault = new TimeoutException("simulated");

        var response = await host.SendAsync(Request("GET", ListRoute, null));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCode(response)).Should().Be("ebilling.correction.unavailable");
    }

    // =========================================================================================
    // Host
    // =========================================================================================

    private static async Task<string?> ErrorCode(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]?.GetValue<string>();

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

        public FinAuth.RecordingAccessDataSource Access { get; } = new();
        public FinAuth.RecordingPrivilegeProbe Probe { get; } = new() { CallerSystemUserId = W.Reviewer };
        public CorrectionWorld World { get; } = new();

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

            // The REAL service over the in-memory Dataverse boundary (a strict mock, so an unmodelled call fails the test).
            builder.Services.AddScoped(_ => host.World.Service());

            builder.WebHost.UseTestServer();
            host._app = builder.Build();
            host._app.UseRouting();
            host._app.UseAuthentication();
            host._app.UseAuthorization();
            host._app.MapInvoiceCorrectionEndpoints();
            await host._app.StartAsync();
            host._client = host._app.GetTestClient();
            return host;
        }

        public void GrantAll() => Access.Grant(InvoiceSet, W.InvoiceId, AccessRights.Read | AccessRights.Write);

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
