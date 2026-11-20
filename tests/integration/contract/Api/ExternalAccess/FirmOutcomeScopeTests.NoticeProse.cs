using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Finance;

/// <summary>
/// ebilling-r1 task 130g (EB-D-259), the route contract of <c>GET /api/v1/external/ebilling/invoices/{invoiceId}/outcome</c>: <c>noticeProse</c> is
/// serialized when the projection has it and the key is omitted (not <c>null</c>) when it does not. A partial of the route rig
/// (<see cref="FirmOutcomeScopeTests"/>), so the REAL route, filters and projection run exactly as for the 130b cases.
/// </summary>
public sealed partial class FirmOutcomeScopeTests
{
    private const string NoticeProse = "Your invoice was returned because several timekeeper rates need review. Please check them and send a corrected invoice.";
    private const string NoticeSubjectMarker = "SENT-SUBJECT-MARKER";
    private const string NoticeHtmlMarker = "SENT-HTML-MARKER";

    private static string StoredNotice(string kind, string prose = NoticeProse) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["v"] = 1,
        ["source"] = "Reviewer",
        ["kind"] = kind,
        ["subject"] = NoticeSubjectMarker + " (TRK-1)",
        ["prose"] = prose,
        ["html"] = $"<p>{NoticeHtmlMarker}</p>",
        ["htmlOmitted"] = false,
    });

    /// <summary>A decided invoice of the caller's firm, with one reduced line and, when <paramref name="stored"/> is set, its OWN submission holding that notice.</summary>
    private Guid SeedDecidedInvoice(int status, string? stored)
    {
        var submission = Guid.NewGuid();
        var invoice = SeedInvoice(FirmA, status: status, id: InvoiceSubmissionPersister.InvoiceIdFor(submission));
        SeedReducedLine(invoice, Reason);
        if (stored is not null)
        {
            _submissionRows.Add(new Entity("sprk_invoicesubmission", submission)
            {
                ["sprk_invoice"] = new EntityReference("sprk_invoice", invoice),
                ["sprk_firmnoticetext"] = stored,
            });
        }

        return invoice;
    }

    [Theory(DisplayName = "130g AC7: the outcome route serializes noticeProse (camelCase string) for a Rejected or Returned invoice, and nothing else of the stored notice")]
    [InlineData(InvoiceStatusCodes.Rejected, "Rejected", "InvoiceRejected")]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm, "ReturnedToFirm", "ReturnedToFirm")]
    public async Task Invoice_RejectedOrReturned_SerializesTheNoticeProse(int status, string kind, string storedKind)
    {
        var invoice = SeedDecidedInvoice(status, StoredNotice(storedKind));

        var response = await Invoice(invoice);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain(NoticeSubjectMarker).And.NotContain(NoticeHtmlMarker).And.NotContain("\"subject\"").And.NotContain("\"html\"").And.NotContain("\"source\"");
        using var body = JsonDocument.Parse(text);
        var root = body.RootElement;
        root.GetProperty("kind").GetString().Should().Be(kind);
        root.GetProperty("noticeProse").GetString().Should().Be(NoticeProse);
        root.GetProperty("items").EnumerateArray().Should().ContainSingle("the items still ride with the prose")
            .Which.GetProperty("reason").GetString().Should().Be(Reason);
    }

    [Theory(DisplayName = "130g AC7: the key is omitted (not null) when there is no prose to show: nothing stored, or another kind stored")]
    [InlineData(InvoiceStatusCodes.Rejected, null)]
    [InlineData(InvoiceStatusCodes.Rejected, "ReturnedToFirm")]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm, "InvoiceRejected")]
    public async Task Invoice_WithNoProseToShow_OmitsTheKey(int status, string? storedKind)
    {
        var invoice = SeedDecidedInvoice(status, storedKind is null ? null : StoredNotice(storedKind));

        var response = await Invoice(invoice);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.TryGetProperty("noticeProse", out _).Should().BeFalse();
        body.RootElement.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact(DisplayName = "130g AC7: a stored notice whose prose the screen refuses is omitted, and the body does not carry the refused text")]
    public async Task Invoice_ARefusedNotice_IsOmitted()
    {
        const string refused = "Your invoice was over by 450 on the rates.";
        var invoice = SeedDecidedInvoice(InvoiceStatusCodes.Rejected, StoredNotice("InvoiceRejected", refused));

        var response = await Invoice(invoice);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain(refused).And.NotContain("noticeProse");
    }

    [Theory(DisplayName = "130g AC7 (must-not-fire): an invoice that is not decided carries no noticeProse key even when a notice is stored on its submission")]
    [InlineData(InvoiceStatusCodes.InReview)]
    [InlineData(InvoiceStatusCodes.Approved)]
    public async Task Invoice_NotDecided_OmitsTheKey(int status)
    {
        var invoice = SeedDecidedInvoice(status, StoredNotice("InvoiceRejected"));

        var response = await Invoice(invoice);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("noticeProse").And.NotContain(NoticeProse);
    }

    [Fact(DisplayName = "130g AC5/AC7: a fault reading the stored notice still answers 200 with the items, and the key is omitted (never a 503 for a supplementary field)")]
    public async Task Invoice_ANoticeReadFault_Is200WithoutTheKey()
    {
        var invoice = SeedDecidedInvoice(InvoiceStatusCodes.Rejected, StoredNotice("InvoiceRejected"));
        _faultingProjectionEntities.Add("sprk_invoicesubmission");

        var response = await Invoice(invoice);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain("noticeProse").And.NotContain(NoticeProse);
        using var body = JsonDocument.Parse(text);
        body.RootElement.GetProperty("degraded").GetBoolean().Should().BeFalse();
        body.RootElement.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact(DisplayName = "130g NFR-10 / D-70: another firm's Rejected invoice is the same 403 as before and its notice prose is never read or shown")]
    public async Task Invoice_OfAnotherFirm_IsRefused_AndNoNoticeIsRead()
    {
        var submission = Guid.NewGuid();
        var invoice = SeedInvoice(FirmB, status: InvoiceStatusCodes.Rejected, id: InvoiceSubmissionPersister.InvoiceIdFor(submission));
        SeedReducedLine(invoice, Reason);
        _submissionRows.Add(new Entity("sprk_invoicesubmission", submission)
        {
            ["sprk_invoice"] = new EntityReference("sprk_invoice", invoice),
            ["sprk_firmnoticetext"] = StoredNotice("InvoiceRejected"),
        });

        var response = await Invoice(invoice);

        await AssertRefusedAsync(response, HttpStatusCode.Forbidden, Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(NoticeProse);
        _projectionReads.Should().BeEmpty("a refused request projects nothing, the notice included");
    }
}
