using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 095: the pure rules of the from-email submission. They are the marks rules (contract section 6, in its order),
/// the key K and the ids derived from it (section 4, EB-D-160 generations), the <c>.eml</c> attachment parts (model §9.1 steps 2
/// and 3: one definition of ordinal, name and SHA-256 at save time and at the job's read), and how a saved mark finds its part.
/// </summary>
/// <remarks>Maintain-class (ADR-038): this is the wire contract word-add-in-r1 builds against, so a change here is a contract change.</remarks>
public class FromEmailSubmissionRulesTests
{
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7 invoice 1001");
    private static readonly byte[] XlsxBytes = Encoding.ASCII.GetBytes("PK timesheet");
    private static readonly byte[] PngBytes = Encoding.ASCII.GetBytes("PNG logo");

    private static AttachmentReference Att(string name, byte[]? bytes, string type, bool inline = false, string? contentId = null, long? size = null) => new()
    {
        AttachmentId = "att-" + name,
        FileName = name,
        Size = size ?? bytes?.Length ?? 10,
        ContentType = type,
        ContentBase64 = bytes is null ? null : Convert.ToBase64String(bytes),
        IsInline = inline,
        ContentId = contentId,
    };

    private static FromEmailMark Mark(AttachmentReference a, string mark) => new()
    {
        FileName = a.FileName, Size = a.Size, ContentType = a.ContentType, Mark = mark,
    };

    private static readonly AttachmentReference Invoice = Att("Invoice 1001.pdf", PdfBytes, "application/pdf");
    private static readonly AttachmentReference Timesheet = Att("Timesheet.xlsx", XlsxBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    private static readonly AttachmentReference Logo = Att("logo.png", PngBytes, "image/png");

    // ── marks rules ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Marks_Valid_ReturnsTheInvoiceFirst_ThenSupportingInAttachmentOrder_WithEachFilesSha256()
    {
        var other = Att("Receipts.pdf", Encoding.ASCII.GetBytes("receipts"), "application/pdf");
        var attachments = new[] { Timesheet, Logo, Invoice, other };
        var marks = new[] { Mark(other, "Supporting"), Mark(Logo, "DontSave"), Mark(Invoice, "Invoice"), Mark(Timesheet, "Supporting") };

        var (refusal, saved) = FromEmailMarkRules.Check(attachments, marks);

        refusal.Should().BeNull();
        saved.Select(s => s.Entry.FileName).Should().Equal("Invoice 1001.pdf", "Timesheet.xlsx", "Receipts.pdf");
        saved[0].Sha256.Should().Be(EmlMimeParts.Sha256Hex(PdfBytes));
        saved[0].EntryIndex.Should().Be(2);
    }

    [Fact]
    public void Marks_MatchTheContentTypeIgnoringCase_AndAcceptADontSaveMarkWithNoEntry()
    {
        var marks = new[]
        {
            Mark(Invoice, "Invoice") with { ContentType = "APPLICATION/PDF" },
            new FromEmailMark { FileName = "gone.txt", Size = 3, ContentType = "text/plain", Mark = "DontSave" },
        };

        var (refusal, saved) = FromEmailMarkRules.Check(new[] { Invoice }, marks);

        refusal.Should().BeNull();
        saved.Should().ContainSingle();
    }

    public static TheoryData<string, AttachmentReference[], FromEmailMark[], string?> Refusals()
    {
        var inline = Att("image001.png", PngBytes, "image/png", inline: true, contentId: "cid1");
        var twinA = Att("scan.pdf", PdfBytes, "application/pdf");
        var twinB = twinA with { AttachmentId = "att-twin" };
        var text = Att("invoice.txt", PdfBytes, "text/plain");
        var data = new TheoryData<string, AttachmentReference[], FromEmailMark[], string?>
        {
            { FromEmailMarksReasons.UnknownMark, new[] { Invoice }, new[] { Mark(Invoice, "invoice") }, "Invoice 1001.pdf" },
            { FromEmailMarksReasons.UnknownMark, new[] { Invoice }, new[] { Mark(Invoice, "Invoice") with { Mark = null } }, "Invoice 1001.pdf" },
            { FromEmailMarksReasons.AmbiguousAttachment, new[] { twinA, twinB }, new[] { Mark(twinA, "Invoice") }, "scan.pdf" },
            { FromEmailMarksReasons.InlineAttachment, new[] { Invoice, inline }, new[] { Mark(Invoice, "Invoice"), Mark(inline, "Supporting") }, "image001.png" },
            { FromEmailMarksReasons.DuplicateMark, new[] { Invoice, Timesheet }, new[] { Mark(Invoice, "Invoice"), Mark(Timesheet, "Supporting"), Mark(Timesheet, "DontSave") }, "Timesheet.xlsx" },
            { FromEmailMarksReasons.UnmarkedAttachment, new[] { Invoice, Timesheet }, new[] { Mark(Invoice, "Invoice") }, "Timesheet.xlsx" },
            { FromEmailMarksReasons.InvoiceCount, new[] { Invoice, Timesheet }, new[] { Mark(Invoice, "Supporting"), Mark(Timesheet, "Supporting") }, null },
            { FromEmailMarksReasons.InvoiceCount, new[] { Invoice, Timesheet }, new[] { Mark(Invoice, "Invoice"), Mark(Timesheet, "Invoice") }, null },
            { FromEmailMarksReasons.InvoiceNotPdf, new[] { text }, new[] { Mark(text, "Invoice") }, "invoice.txt" },
            // The order is the contract's: an unknown mark is reported before the twins that would also refuse the request.
            { FromEmailMarksReasons.UnknownMark, new[] { twinA, twinB }, new[] { Mark(twinA, "Bogus") }, "scan.pdf" },
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Marks_EachRuleRefuses_400MarksInvalid_WithItsReasonAndFileName(
        string reason, AttachmentReference[] attachments, FromEmailMark[] marks, string? fileName)
    {
        var (refusal, saved) = FromEmailMarkRules.Check(attachments, marks);

        refusal.Should().NotBeNull();
        refusal!.Status.Should().Be(StatusCodes.Status400BadRequest);
        refusal.ErrorCode.Should().Be("ebilling.submission.marks_invalid");
        refusal.Reason.Should().Be(reason);
        refusal.FileName.Should().Be(fileName);
        saved.Should().BeEmpty();
    }

    [Theory]
    [InlineData("application/octet-stream", "Invoice 1001.pdf")] // the name ends in .pdf
    [InlineData("application/PDF", "Invoice 1001")]              // the type contains pdf
    [InlineData("application/x-pdf", "scan.bin")]
    public void Marks_InvoiceNotPdf_RefusesOnlyWhenNeitherTheTypeNorTheNameSaysPdf(string type, string name)
    {
        var invoice = Att(name, PdfBytes, type);

        FromEmailMarkRules.Check(new[] { invoice }, new[] { Mark(invoice, "Invoice") }).Refusal.Should().BeNull();
    }

    public static TheoryData<AttachmentReference[], FromEmailMark[], string, string> MissingBytes()
    {
        var noBytes = Att("Invoice 1001.pdf", null, "application/pdf", size: 21);
        var badBase64 = Invoice with { ContentBase64 = "not base64 !!" };
        return new TheoryData<AttachmentReference[], FromEmailMark[], string, string>
        {
            { new[] { noBytes }, new[] { Mark(noBytes, "Invoice") }, "Invoice 1001.pdf", "Invoice" },
            { new[] { badBase64 }, new[] { Mark(badBase64, "Invoice") }, "Invoice 1001.pdf", "Invoice" },
            {
                new[] { Invoice },
                new[] { Mark(Invoice, "Invoice"), new FromEmailMark { FileName = "Timesheet.xlsx", Size = 12, ContentType = "application/x", Mark = "Supporting" } },
                "Timesheet.xlsx", "Supporting"
            },
        };
    }

    [Theory]
    [MemberData(nameof(MissingBytes))]
    public void Marks_ASavedMarkWithoutItsBytes_Is422MarkedFileMissing_WithFileNameAndMark(
        AttachmentReference[] attachments, FromEmailMark[] marks, string fileName, string mark)
    {
        var (refusal, _) = FromEmailMarkRules.Check(attachments, marks);

        refusal.Should().NotBeNull();
        refusal!.Status.Should().Be(StatusCodes.Status422UnprocessableEntity);
        refusal.ErrorCode.Should().Be("ebilling.submission.marked_file_missing");
        refusal.FileName.Should().Be(fileName);
        refusal.Mark.Should().Be(mark);
    }

    [Fact]
    public void Marks_TheContractStrings_ArePinned()
    {
        new[]
        {
            FromEmailMarksReasons.InvoiceCount, FromEmailMarksReasons.UnknownMark, FromEmailMarksReasons.InlineAttachment,
            FromEmailMarksReasons.UnmarkedAttachment, FromEmailMarksReasons.DuplicateMark, FromEmailMarksReasons.AmbiguousAttachment,
            FromEmailMarksReasons.InvoiceNotPdf,
        }.Should().Equal("invoice_count", "unknown_mark", "inline_attachment", "unmarked_attachment", "duplicate_mark",
            "ambiguous_attachment", "invoice_not_pdf");
        new[]
        {
            FromEmailUnidentifiableReasons.NoMessageId, FromEmailUnidentifiableReasons.EmailNotFound,
            FromEmailUnidentifiableReasons.EarlierSaveUnlinked, FromEmailUnidentifiableReasons.NoCommunication,
            FromEmailUnidentifiableReasons.EmlAmbiguous,
        }.Should().Equal("no_message_id", "email_not_found", "earlier_save_unlinked", "no_communication", "eml_ambiguous");
        new[]
        {
            FromEmailCorrectionReasons.NotAvailable, FromEmailCorrectionReasons.InvoiceStatus,
            FromEmailCorrectionReasons.DifferentFirm, FromEmailCorrectionReasons.DifferentCoreRecord,
        }.Should().Equal("not_available", "invoice_status", "different_firm", "different_core_record");
        new[]
        {
            FromEmailSubmissionErrors.MarksInvalid, FromEmailSubmissionErrors.MarkedFileMissing, FromEmailSubmissionErrors.EmailUnidentifiable,
            FromEmailSubmissionErrors.EmailUnavailable, FromEmailSubmissionErrors.CorrectionNotAllowed, FromEmailSubmissionErrors.EmailSaveFailed,
        }.Should().Equal("ebilling.submission.marks_invalid", "ebilling.submission.marked_file_missing",
            "ebilling.submission.email_unidentifiable", "ebilling.submission.email_unavailable",
            "ebilling.submission.correction_not_allowed", "ebilling.submission.email_save_failed");
        new[] { FromEmailMarks.Invoice, FromEmailMarks.Supporting, FromEmailMarks.DontSave, FromEmailModes.New, FromEmailModes.Correction }
            .Should().Equal("Invoice", "Supporting", "DontSave", "New", "Correction");
    }

    // ── K and the ids ───────────────────────────────────────────────────────────────────────────────────────────

    private static readonly Guid MatterId = Guid.Parse("09509509-0950-4950-8950-0000000000a1");
    private static readonly Guid FirmId = Guid.Parse("09509509-0950-4950-8950-0000000000f1");

    internal static FromEmailSubmissionInput Input(IReadOnlyList<FromEmailMark>? marks = null, string number = "1001",
        string mode = "New", Guid? firm = null, string messageId = "<inv-1001@firm.example>") => new()
    {
        CoreRecord = new InvoiceCoreRecord(InvoiceCoreRecordKind.Matter, MatterId),
        SaveEntityType = "Matter",
        Email = new EmailMetadata { Subject = "Invoice 1001", SenderEmail = "billing@firm.example", InternetMessageId = messageId },
        Marks = marks ?? new[] { Mark(Invoice, "Invoice"), Mark(Timesheet, "Supporting") },
        FirmId = firm ?? FirmId,
        FirmInvoiceNumber = number,
        Mode = mode,
    };

    [Fact]
    public void Key_IsTheCanonicalV2Array_InTheContractsFieldOrder_WithNoFirmDetails()
    {
        var k = FromEmailSubmissionKey.Of(Input());

        using var doc = JsonDocument.Parse(k);
        var a = doc.RootElement.EnumerateArray().ToList();
        a.Select(e => e.ValueKind == JsonValueKind.Array ? "[marks]" : e.GetString())
            .Should().Equal("v2", "<inv-1001@firm.example>", "Matter", MatterId.ToString("D"), "New", string.Empty, "[marks]");
        a[6].EnumerateArray().Select(m => m[0].GetString()).Should().Equal("Invoice 1001.pdf", "Timesheet.xlsx");
        a[6][0][2].GetString().Should().Be("APPLICATION/PDF", "the content type is compared ignoring case, so K upper-cases it");
    }

    [Fact]
    public void Key_DoesNotDependOnMarkOrder_TypeCase_OrNumberWhitespace()
    {
        var a = FromEmailSubmissionKey.Of(Input());
        var b = FromEmailSubmissionKey.Of(Input(
            new[] { Mark(Timesheet, "Supporting"), Mark(Invoice, "Invoice") with { ContentType = "Application/Pdf" } }, number: "  1001 "));

        b.Should().Be(a);
    }

    [Fact]
    public void Key_ChangesWithTheMarking_TheMode_AndTheMessage()
    {
        var k = FromEmailSubmissionKey.Of(Input());

        new[]
        {
            FromEmailSubmissionKey.Of(Input(new[] { Mark(Invoice, "Invoice"), Mark(Timesheet, "DontSave") })),
            FromEmailSubmissionKey.Of(Input(mode: "Correction")),
            FromEmailSubmissionKey.Of(Input(messageId: "<other@firm.example>")),
        }.Should().NotContain(k).And.OnlyHaveUniqueItems();
    }

    /// <summary>095 review F2 (contract section 5): another firm or number is the same K (the same submission, other details); the
    /// response-cache binding still tells them apart, so a reused header never replays the other request's 202.</summary>
    [Fact]
    public void Key_DoesNotChangeWithTheFirmOrTheNumber_ButTheCacheBindingDoes()
    {
        var k = FromEmailSubmissionKey.Of(Input());

        FromEmailSubmissionKey.Of(Input(firm: Guid.NewGuid())).Should().Be(k);
        FromEmailSubmissionKey.Of(Input(number: "1002")).Should().Be(k);

        var binding = FromEmailSubmissionKey.CacheBinding(Input());
        new[]
        {
            FromEmailSubmissionKey.CacheBinding(Input(firm: Guid.NewGuid())),
            FromEmailSubmissionKey.CacheBinding(Input(number: "1002")),
            FromEmailSubmissionKey.CacheBinding(Input(mode: "Correction")),
        }.Should().NotContain(binding).And.OnlyHaveUniqueItems();
        FromEmailSubmissionKey.CacheBinding(Input(number: " 1001 ")).Should().Be(binding);
    }

    [Fact]
    public void Ids_GenerationOne_IsTheContractsDerivation_LaterGenerationsDiffer_AndZeroIsRefused()
    {
        var k = FromEmailSubmissionKey.Of(Input());

        FromEmailSubmissionKey.SubmissionIdFor(k, 1).Should().Be(InvoiceSubmissionPersister.IdOf("ebilling-from-email-submission-" + k));
        FromEmailSubmissionKey.JobIdFor(k, 1).Should().Be(InvoiceSubmissionPersister.IdOf("ebilling-from-email-job-" + k));

        var ids = Enumerable.Range(1, FromEmailSubmissionKey.MaxGeneration)
            .SelectMany(g => new[] { FromEmailSubmissionKey.SubmissionIdFor(k, g), FromEmailSubmissionKey.JobIdFor(k, g) })
            .ToList();
        ids.Should().OnlyHaveUniqueItems();
        FromEmailSubmissionKey.SubmissionIdFor(k, 2).Should().Be(FromEmailSubmissionKey.SubmissionIdFor(k, 2), "deterministic");

        var act = () => FromEmailSubmissionKey.SubmissionIdFor(k, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
        FromEmailSubmissionKey.CacheBinding(Input()).Should().StartWith(k);
    }

    // ── .eml parts ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EmlParts_OfTheBuiltEml_AreTheCarriedAttachments_InOrder_WithTheirSha256()
    {
        var email = new EmailMetadata
        {
            Subject = "Invoice 1001",
            SenderEmail = "billing@firm.example",
            InternetMessageId = "<inv-1001@firm.example>",
            Body = "<p>See attached <img src=\"cid:cid1\"></p>",
            IsBodyHtml = true,
            Attachments = new List<AttachmentReference>
            {
                Att("image001.png", PngBytes, "image/png", inline: true, contentId: "cid1"), // a linked resource, not a part
                Invoice,
                Att("missing.pdf", null, "application/pdf"),                               // no bytes: the builder skips it
                Timesheet,
            },
        };

        using var eml = OfficeEmailEnricher.BuildEmlFromMetadata(email);
        var parts = EmlMimeParts.Read(eml);

        parts.Select(p => (p.Ordinal, p.FileName, p.Sha256)).Should().Equal(
            (0, "Invoice 1001.pdf", EmlMimeParts.Sha256Hex(PdfBytes)),
            (1, "Timesheet.xlsx", EmlMimeParts.Sha256Hex(XlsxBytes)));
        EmlMimeParts.Sha256Hex(PdfBytes).Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void EmlParts_ANonMimeStream_IsAFormatException()
    {
        using var empty = new MemoryStream();

        var act = () => EmlMimeParts.Read(empty);

        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData(null, "application/pdf", false)]
    [InlineData("", "application/pdf", false)]
    [InlineData("%%%", "application/pdf", false)]
    [InlineData("JVBERg==", "application/pdf", true)]
    [InlineData("JVBERg==", null, true)]
    public void CarriedBytes_MirrorTheBuildersSkipRules(string? base64, string? type, bool carried)
    {
        EmlMimeParts.TryCarriedBytes(base64, type, out var bytes).Should().Be(carried);
        if (carried)
        {
            bytes.Should().Equal(Convert.FromBase64String(base64!));
        }
    }

    // ── a saved mark finds its part ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MatchParts_FindsEachMarkBySha_AtTheLowestUnusedOrdinal_SoTwoEqualFilesTakeTwoParts()
    {
        var copy = Att("Invoice copy.pdf", PdfBytes, "application/pdf");
        var (_, saved) = FromEmailMarkRules.Check(new[] { Invoice, copy }, new[] { Mark(Invoice, "Invoice"), Mark(copy, "Supporting") });
        var sha = EmlMimeParts.Sha256Hex(PdfBytes);
        var parts = new[] { new EmlPart(0, "Invoice 1001.pdf", sha), new EmlPart(1, "Invoice copy.pdf", sha) };

        var matched = FromEmailSubmissionService.MatchParts(saved, parts);

        matched.Should().NotBeNull();
        matched!.Select(m => (m.Ordinal, m.FileName, m.Mark)).Should().Equal((0, "Invoice 1001.pdf", "Invoice"), (1, "Invoice copy.pdf", "Supporting"));
    }

    [Fact]
    public void MatchParts_AMarkWithNoPart_IsNull()
    {
        var (_, saved) = FromEmailMarkRules.Check(new[] { Invoice, Timesheet }, new[] { Mark(Invoice, "Invoice"), Mark(Timesheet, "Supporting") });

        FromEmailSubmissionService.MatchParts(saved, new[] { new EmlPart(0, "Invoice 1001.pdf", EmlMimeParts.Sha256Hex(PdfBytes)) })
            .Should().BeNull();
    }
}
