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
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;
using FinAuth = Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests;
using World = Sprk.Bff.Api.Tests.Services.Finance.InvoiceAssignFirmServiceTests.World;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// ebilling-r1 task 095d-1b: <c>POST /api/finance/invoices/{invoiceId}/firm</c> through the REAL <c>MapInvoiceApprovalEndpoints</c>,
/// <see cref="FinanceAuthorizationFilter"/> (both filters), <see cref="AuthorizationService"/> and the kill switch, over the REAL
/// <see cref="InvoiceAssignFirmService"/> on its in-memory world (<see cref="World"/>; the allocator is mocked to capture the apply).
/// </summary>
/// <remarks>
/// The route asks, as the caller and fail closed: Write on the invoice (<c>ebilling.invoice.assign_firm</c>, the uniform 404), then Read
/// (<c>read</c>) and AppendTo (<c>finance.attach_invoice</c>) on the body's firm (403); step 0 runs first; the kill switch after them. A
/// denied request reads nothing through the service and applies nothing.
/// </remarks>
public sealed class InvoiceAssignFirmEndpointContractTests
{
    private const string InvoiceSet = "sprk_invoices";
    private const string FirmSet = "sprk_organizations";
    private static readonly Guid InvoiceId = Services.Finance.InvoiceAssignFirmServiceTests.InvoiceId;
    private static readonly Guid Firm = Services.Finance.InvoiceAssignFirmServiceTests.Firm;
    private static readonly Guid Decider = Services.Finance.InvoiceAssignFirmServiceTests.Decider;

    [Fact(DisplayName = "095d-1b: unauthenticated is 401 and reads nothing")]
    public async Task Unauthenticated_Is401()
    {
        await using var host = await Host.StartAsync();

        var response = await host.SendAsync(new { firmId = Firm }, authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.Access.Calls.Should().BeEmpty();
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Theory(DisplayName = "095d-1b: no Write on the invoice is the uniform 404, before the firm is asked or anything is read (must fire)")]
    [InlineData(AccessRights.None)]
    [InlineData(AccessRights.Read)]
    [InlineData(AccessRights.Read | AccessRights.AppendTo)]
    public async Task NoWriteOnTheInvoice_IsUniform404(AccessRights rights)
    {
        await using var host = await Host.StartAsync();
        host.Access.Grant(InvoiceSet, InvoiceId, rights);
        host.GrantFirm(AccessRights.Read | AccessRights.AppendTo);

        var response = await host.SendAsync(new { firmId = Firm });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReasonCode(response)).Should().Be("sdap.access.deny.record_unavailable");
        host.Access.Calls.Should().ContainSingle().Which.Should().Be(new FinAuth.AccessCall(FinAuth.AccessPath.Record, InvoiceSet, InvoiceId, HasToken: true));
        host.World.Entities.Invocations.Should().BeEmpty();
        host.World.ShouldHaveWrittenNothing();
    }

    [Theory(DisplayName = "095d-1b: Write on the invoice without Read AND AppendTo on the firm is 403 (must fire); with both it is assigned (must not fire)")]
    [InlineData(AccessRights.None, false)]
    [InlineData(AccessRights.Read, false)]
    [InlineData(AccessRights.AppendTo, false)]
    [InlineData(AccessRights.Read | AccessRights.AppendTo, true)]
    public async Task TheFirmNeedsReadAndAppendTo(AccessRights firmRights, bool assigned)
    {
        await using var host = await Host.StartAsync();
        host.GrantInvoiceWrite();
        host.GrantFirm(firmRights);

        var response = await host.SendAsync(new { firmId = Firm, note = "confirmed" });

        host.Access.Calls.Should().Contain(c => c.Set == FirmSet && c.Id == Firm, "the firm is asked as the caller");
        if (!assigned)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            host.World.Entities.Invocations.Should().BeEmpty();
            host.World.ShouldHaveWrittenNothing();
            return;
        }

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        json["outcome"]!.GetValue<string>().Should().Be("assigned");
        json["trackingNumber"]!.GetValue<string>().Should().Be("INV-7.1");
        json["previousTrackingNumber"]!.GetValue<string>().Should().Be("INV-7.3");
        json["rulesRerun"]!.GetValue<string>().Should().Be("pending");
        json["decisionId"]!.GetValue<Guid>().Should().Be(InvoiceAssignFirmService.DecisionIdFor(Services.Finance.InvoiceAssignFirmServiceTests.SubmissionId));
        host.World.Applied.Should().NotBeNull("the one apply went to the allocator");
        AssignFirmStoredDecision.Read(JsonNode.Parse((string)host.World.Applied!.SubmissionFields!["sprk_findingsjson"])!.AsObject())!
            .DeciderUserId.Should().Be(Decider, "the decider is the caller's systemuserid, never the request's");
    }

    [Theory(DisplayName = "095d-1b: a body that names no firm is 400 firm_required after the invoice check and before any firm check")]
    [InlineData("{}")]
    [InlineData("""{"firmId":"00000000-0000-0000-0000-000000000000"}""")]
    public async Task NoFirmInTheBody_Is400(string body)
    {
        await using var host = await Host.StartAsync();
        host.GrantInvoiceWrite();

        var response = await host.SendRawAsync(body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCode(response)).Should().Be(AssignFirmCodes.FirmRequired);
        host.Access.Calls.Should().NotContain(c => c.Set == FirmSet);
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "095d-1b: the kill switch off is 503 ebilling.disabled after the authorization filters, and nothing is read")]
    public async Task KillSwitchOff_Is503()
    {
        await using var host = await Host.StartAsync(ebillingEnabled: false);
        host.GrantInvoiceWrite();
        host.GrantFirm(AccessRights.Read | AccessRights.AppendTo);

        var response = await host.SendAsync(new { firmId = Firm });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ErrorCode(response)).Should().Be("ebilling.disabled");
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    public static IEnumerable<object[]> RefusedKinds() => StatedCaller.RefusedKinds.Select(k => new object[] { k });

    [Theory(DisplayName = "095d-1b step 0: a flagged-external, row-less or unreadable caller is refused as the uniform 404 before any rights call")]
    [MemberData(nameof(RefusedKinds))]
    public async Task StepZero_RefusesBeforeAnyRightsCall(StepZeroCallerKind kind)
    {
        await using var host = await Host.StartAsync();
        host.GrantInvoiceWrite();
        host.GrantFirm(AccessRights.Read | AccessRights.AppendTo);
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(new { firmId = Firm });

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: true);
        host.Access.Calls.Should().BeEmpty();
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "095d-1b: an unproven caller identity is 403 ebilling.decider.unresolved and nothing is read")]
    public async Task DeciderUnresolved_Is403()
    {
        await using var host = await Host.StartAsync();
        host.GrantInvoiceWrite();
        host.GrantFirm(AccessRights.Read | AccessRights.AppendTo);
        host.Probe.CallerSystemUserId = null;

        var response = await host.SendAsync(new { firmId = Firm });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReasonCode(response)).Should().Be(AssignFirmCodes.DeciderUnresolved, "a 403 carries its code as reasonCode (ProblemDetailsHelper.Forbidden, as approve)");
        host.World.Entities.Invocations.Should().BeEmpty();
    }

    [Fact(DisplayName = "095d-1b: the service's refusals reach the caller with their status and stable code (not linked 403, not held 409, gone 404 uniform)")]
    public async Task ServiceRefusals_KeepTheirStatusAndCode()
    {
        await using (var notLinked = await Host.StartAsync())
        {
            notLinked.GrantInvoiceWrite();
            notLinked.GrantFirm(AccessRights.Read | AccessRights.AppendTo);
            notLinked.World.Link = InvoiceFirmLink.NotLinked;
            var response = await notLinked.SendAsync(new { firmId = Firm });
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await ReasonCode(response)).Should().Be(InvoiceSubmissionErrors.FirmNotLinked);
        }

        await using (var notHeld = await Host.StartAsync())
        {
            notHeld.GrantInvoiceWrite();
            notHeld.GrantFirm(AccessRights.Read | AccessRights.AppendTo);
            notHeld.World.Invoice![InvoiceFirmHold.InvoiceHoldColumn] = null;
            notHeld.World.Submission[InvoiceSubmissionPersister.SubmissionReasonCode] = null;
            notHeld.World.Submission[InvoiceSubmissionPersister.SubmissionFindingsJson] = """{"trackingNumber":"INV-7.3"}""";
            var response = await notHeld.SendAsync(new { firmId = Firm });
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await ErrorCode(response)).Should().Be(AssignFirmCodes.NotHeld);
            notHeld.World.ShouldHaveWrittenNothing();
        }

        await using (var gone = await Host.StartAsync())
        {
            gone.GrantInvoiceWrite();
            gone.GrantFirm(AccessRights.Read | AccessRights.AppendTo);
            gone.World.Invoice = null;
            var response = await gone.SendAsync(new { firmId = Firm });
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await ReasonCode(response)).Should().Be("sdap.access.deny.record_unavailable");
        }
    }

    // ── Host ──

    private static async Task<string?> ErrorCode(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errorCode"]?.GetValue<string>();

    private static async Task<string?> ReasonCode(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!["reasonCode"]?.GetValue<string>();

    private sealed class Host : IAsyncDisposable
    {
        private WebApplication _app = null!;
        private HttpClient _client = null!;

        public StatedCaller StepZero { get; private set; } = null!;
        public World World { get; } = new();
        public FinAuth.RecordingAccessDataSource Access { get; } = new();
        public FinAuth.RecordingPrivilegeProbe Probe { get; } = new();

        public static async Task<Host> StartAsync(bool ebillingEnabled = true)
        {
            var host = new Host();
            host.Probe.CallerSystemUserId = Decider;
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
            builder.Services.AddOptions<EbillingOptions>().Configure(o => o.Enabled = ebillingEnabled);

            // The REAL authorization stack, substituted only at the access-data boundary.
            builder.Services.AddSingleton<IAccessDataSource>(host.Access);
            builder.Services.AddScoped<IAuthorizationRule, OperationAccessRule>();
            builder.Services.AddScoped<AuthorizationService>();
            builder.Services.AddSingleton<CallerRecordAccessProbe>(host.Probe);

            // The REAL Set-firm service over the in-memory world.
            builder.Services.AddScoped(_ => host.World.Service());
            // The file's other routes bind InvoiceApprovalService (inferred from the container); never called here.
            builder.Services.AddScoped(sp => new Services.Finance.ApprovalWorld().Service(InvoiceApprovalService.DecideCheck(
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

        public void GrantInvoiceWrite() => Access.Grant(InvoiceSet, InvoiceId, AccessRights.Read | AccessRights.Write);

        public void GrantFirm(AccessRights rights) => Access.Grant(FirmSet, Firm, rights);

        public Task<HttpResponseMessage> SendAsync(object body, bool authenticated = true) =>
            Send(JsonContent.Create(body), authenticated);

        public Task<HttpResponseMessage> SendRawAsync(string body) =>
            Send(new StringContent(body, System.Text.Encoding.UTF8, "application/json"), authenticated: true);

        private Task<HttpResponseMessage> Send(HttpContent content, bool authenticated)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/finance/invoices/{InvoiceId}/firm") { Content = content };
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
