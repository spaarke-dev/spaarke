using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Tests.Api.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Finance;

/// <summary>
/// <c>POST /api/finance/invoices/{invoiceId}/mark-paid</c> (ebilling-r1 task 151a; FR-42, EB-D-181, EB-D-178, ADR-008, ADR-019): Sent to AP to Paid and
/// nothing else, for a caller with the decide right and Read on the invoice. The REAL route, filter, authorization stack, kill switch and
/// <see cref="InvoiceLifecycleService"/> run over the in-memory world, so each refusal is shown to leave the stored invoice untouched.
/// </summary>
/// <remarks>
/// <b>Seeding proof (task 151a).</b> Removing the decide check or the Read check from <c>ApExportAuthorization.PaymentRouteTargets</c> fails
/// <c>NoDecideRight_Is404_AndNothingIsReadOrWritten</c> and <c>WriteWithoutRead_Is404_AndNothingIsReadOrWritten</c> respectively. Recorded in
/// <c>projects/ebilling-r1/notes/151a-mark-paid-download.md</c>.
/// </remarks>
public sealed class MarkPaidAuthorizationTests
{
    private const string InvoiceSet = "sprk_invoices";

    private static string Url(Guid invoiceId) => $"/api/finance/invoices/{invoiceId}/mark-paid";

    private static async Task<JsonNode> BodyAsync(HttpResponseMessage response) => JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    // =========================================================================================
    // The status rule (FR-42)
    // =========================================================================================

    [Fact(DisplayName = "151a FR-42: a Sent to AP invoice is marked Paid - 200 with the invoice and status, the stored invoice is Paid (inactive), one conditional write")]
    public async Task SentToAp_BecomesPaid()
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        var invoice = host.World.AddInvoice("INV-A", status: InvoiceStatusCodes.SentToAp);

        var response = await host.SendAsync(HttpMethod.Post, Url(invoice.Id));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await BodyAsync(response);
        body["invoiceId"]!.GetValue<Guid>().Should().Be(invoice.Id);
        body["status"]!.GetValue<string>().Should().Be("Paid");
        host.World.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.Paid);
        host.World.Invoice(invoice.Id).GetAttributeValue<OptionSetValue>("statecode").Value.Should().Be(InvoiceStatusCodes.StateInactive);
        host.StatusWrites.Should().Equal(invoice.Id);
    }

    public static IEnumerable<object[]> OtherStatuses() =>
    [
        [InvoiceStatusCodes.InReview, "InReview"],
        [InvoiceStatusCodes.Approved, "Approved"],
        [InvoiceStatusCodes.Paid, "Paid"],
        [InvoiceStatusCodes.Rejected, "Rejected"],
        [InvoiceStatusCodes.ReturnedToFirm, "ReturnedToFirm"],
        [InvoiceStatusCodes.Cancelled, "Cancelled"],
        [InvoiceStatusCodes.Superseded, "Superseded"],
    ];

    [Theory(DisplayName = "151a FR-42: any status but Sent to AP (Paid included) is 409 invoice.transition_not_allowed with the current status, and the invoice is not written")]
    [MemberData(nameof(OtherStatuses))]
    public async Task EveryOtherStatus_Is409_AndNothingIsWritten(int statuscode, string expectedStatusName)
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        var invoice = host.World.AddInvoice("INV-A", status: statuscode);

        var response = await host.SendAsync(HttpMethod.Post, Url(invoice.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await BodyAsync(response);
        body["errorCode"]!.GetValue<string>().Should().Be(InvoiceTransitionReasons.NotAllowed);
        body["currentStatus"]!.GetValue<string>().Should().Be(expectedStatusName);
        host.StatusWrites.Should().BeEmpty();
        host.World.StatusOfInvoice(invoice.Id).Should().Be(statuscode);
    }

    [Fact(DisplayName = "151a FR-42: a second Mark Paid on the same invoice is 409 and writes nothing (Paid is not moved twice)")]
    public async Task Repeat_Is409_WithOneWrite()
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        var invoice = host.World.AddInvoice("INV-A", status: InvoiceStatusCodes.SentToAp);

        var first = await host.SendAsync(HttpMethod.Post, Url(invoice.Id));
        var second = await host.SendAsync(HttpMethod.Post, Url(invoice.Id));

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(second))["errorCode"]!.GetValue<string>().Should().Be(InvoiceTransitionReasons.NotAllowed);
        host.StatusWrites.Should().HaveCount(1);
    }

    [Fact(DisplayName = "151a FR-42: a statuscode the lifecycle does not know is 409 invoice.status_unknown (fail closed) with no write and no current status")]
    public async Task UnknownStatus_Is409_StatusUnknown()
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        var invoice = host.World.AddInvoice("INV-A", status: InvoiceStatusCodes.SentToAp);
        host.World.Dataverse.Touch("sprk_invoice", invoice.Id, new Dictionary<string, object> { ["statuscode"] = new OptionSetValue(424242) });

        var response = await host.SendAsync(HttpMethod.Post, Url(invoice.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await BodyAsync(response);
        body["errorCode"]!.GetValue<string>().Should().Be(InvoiceTransitionReasons.StatusUnknown);
        body["currentStatus"].Should().BeNull();
        host.StatusWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "151a: an invoice that changes on every attempt is 409 invoice.concurrent_change with nothing written")]
    public async Task ChangingInvoice_Is409_ConcurrentChange()
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        var invoice = host.World.AddInvoice("INV-A", status: InvoiceStatusCodes.SentToAp);
        host.RivalWriterOnEveryAttempt = true;

        var response = await host.SendAsync(HttpMethod.Post, Url(invoice.Id));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await BodyAsync(response))["errorCode"]!.GetValue<string>().Should().Be(InvoiceTransitionReasons.ConcurrentChange);
        host.StatusWrites.Should().BeEmpty();
        host.World.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.SentToAp);
    }

    [Fact(DisplayName = "151a ADR-019: a fault in the lifecycle's Dataverse read is 503 ebilling.invoice.payment_unavailable, never a partial answer")]
    public async Task LifecycleFault_Is503()
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        var invoice = host.World.AddInvoice("INV-A", status: InvoiceStatusCodes.SentToAp);
        host.LifecycleReadFault = new InvalidOperationException("dataverse down");

        var response = await host.SendAsync(HttpMethod.Post, Url(invoice.Id));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await BodyAsync(response))["errorCode"]!.GetValue<string>().Should().Be(InvoicePaymentCodes.Unavailable);
        host.StatusWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "151a: rights that hold on an invoice that no longer exists give the same uniform 404 as no rights")]
    public async Task InvoiceGoneAfterTheRightsCheck_IsTheUniform404()
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        var gone = Guid.NewGuid();
        host.World.Access.Grant(InvoiceSet, gone, AccessRights.Read | AccessRights.Write);

        var response = await host.SendAsync(HttpMethod.Post, Url(gone));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BodyAsync(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.RecordUnavailableReasonCode);
        host.StatusWrites.Should().BeEmpty();
    }

    // =========================================================================================
    // Authorization (the export seam: decide + finance.read on the invoice)
    // =========================================================================================

    [Fact(DisplayName = "151a: unauthenticated is 401 and touches nothing")]
    public async Task Unauthenticated_Is401()
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        var invoice = host.World.AddInvoice("INV-A", status: InvoiceStatusCodes.SentToAp);

        var response = await host.SendAsync(HttpMethod.Post, Url(invoice.Id), authenticated: false);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        host.World.Access.Calls.Should().BeEmpty();
        host.StatusWrites.Should().BeEmpty();
    }

    [Fact(DisplayName = "151a EB-D-178 SEEDED: Read without the decide right is the uniform 404; the invoice is neither read nor written")]
    public async Task NoDecideRight_Is404_AndNothingIsReadOrWritten()
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        var invoice = host.World.AddInvoice("INV-A", status: InvoiceStatusCodes.SentToAp, grant: false);
        host.World.Access.Grant(InvoiceSet, invoice.Id, AccessRights.Read);

        var response = await host.SendAsync(HttpMethod.Post, Url(invoice.Id));

        await AssertUniform404Async(host, response);
        host.World.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.SentToAp);
    }

    [Fact(DisplayName = "151a D-85 SEEDED: Write without Read is the uniform 404; the invoice is neither read nor written")]
    public async Task WriteWithoutRead_Is404_AndNothingIsReadOrWritten()
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        var invoice = host.World.AddInvoice("INV-A", status: InvoiceStatusCodes.SentToAp, grant: false);
        host.World.Access.Grant(InvoiceSet, invoice.Id, AccessRights.Write);

        var response = await host.SendAsync(HttpMethod.Post, Url(invoice.Id));

        await AssertUniform404Async(host, response);
        host.World.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.SentToAp);
    }

    [Fact(DisplayName = "151a: an unknown invoice and a caller with no rights get the identical uniform 404")]
    public async Task UnknownInvoice_IsTheSame404_AsNoRights()
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        var known = host.World.AddInvoice("INV-A", status: InvoiceStatusCodes.SentToAp, grant: false);

        var noRights = await host.SendAsync(HttpMethod.Post, Url(known.Id));
        var unknown = await host.SendAsync(HttpMethod.Post, Url(Guid.NewGuid()));

        noRights.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var a = await BodyAsync(noRights);
        var b = await BodyAsync(unknown);
        a["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.RecordUnavailableReasonCode);
        b["reasonCode"]!.GetValue<string>().Should().Be(a["reasonCode"]!.GetValue<string>());
        a["detail"]?.GetValue<string>().Should().Be(b["detail"]?.GetValue<string>());
        a["title"]?.GetValue<string>().Should().Be(b["title"]?.GetValue<string>());
    }

    [Fact(DisplayName = "151a: a rights read that faults is the uniform 404 (fail closed), and nothing is written")]
    public async Task RightsFault_Is404()
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        var invoice = host.World.AddInvoice("INV-A", status: InvoiceStatusCodes.SentToAp);
        host.World.Access.ThrowOnEveryCall = new InvalidOperationException("down");

        var response = await host.SendAsync(HttpMethod.Post, Url(invoice.Id));

        await AssertUniform404Async(host, response);
        host.World.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.SentToAp);
    }

    public static IEnumerable<object[]> RefusedKinds() => StatedCaller.RefusedKinds.Select(k => new object[] { k });

    [Theory(DisplayName = "151a task 099 must-fire: a flagged-external, row-less or unreadable caller is the uniform 404 before any rights call, even holding every right")]
    [MemberData(nameof(RefusedKinds))]
    public async Task StepZero_RefusesBeforeAnyRightsCall(StepZeroCallerKind kind)
    {
        await using var host = await ApExportRoutesHost.StartAsync();
        var invoice = host.World.AddInvoice("INV-A", status: InvoiceStatusCodes.SentToAp);
        host.StepZero.Kind = kind;

        var response = await host.SendAsync(HttpMethod.Post, Url(invoice.Id));

        await host.StepZero.AssertRefusedAsync(response, kind, uniformNotFound: true);
        host.World.Access.Calls.Should().BeEmpty();
        host.StatusWrites.Should().BeEmpty();
        host.World.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.SentToAp);
    }

    [Fact(DisplayName = "151a ADR-018: the kill switch is 503 ebilling.disabled for an authorized caller, and writes nothing")]
    public async Task FlagOff_AuthorizedCaller_Gets503()
    {
        await using var host = await ApExportRoutesHost.StartAsync(ebillingEnabled: false);
        var invoice = host.World.AddInvoice("INV-A", status: InvoiceStatusCodes.SentToAp);

        var response = await host.SendAsync(HttpMethod.Post, Url(invoice.Id));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await BodyAsync(response))["errorCode"]!.GetValue<string>().Should().Be("ebilling.disabled");
        host.StatusWrites.Should().BeEmpty();
        host.World.StatusOfInvoice(invoice.Id).Should().Be(InvoiceStatusCodes.SentToAp);
    }

    [Fact(DisplayName = "151a ADR-018: the kill switch never replaces authorization - an unauthorized caller still gets the 404 when the module is off")]
    public async Task FlagOff_UnauthorizedCaller_StillGets404()
    {
        await using var host = await ApExportRoutesHost.StartAsync(ebillingEnabled: false);
        var invoice = host.World.AddInvoice("INV-A", status: InvoiceStatusCodes.SentToAp, grant: false);

        var response = await host.SendAsync(HttpMethod.Post, Url(invoice.Id));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task AssertUniform404Async(ApExportRoutesHost host, HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BodyAsync(response))["reasonCode"]!.GetValue<string>().Should().Be(FinanceAuthorizationFilter.RecordUnavailableReasonCode);
        host.StatusWrites.Should().BeEmpty();
        host.StatusReads.Should().Be(0, "a refused caller's invoice is neither read nor written");
    }
}
