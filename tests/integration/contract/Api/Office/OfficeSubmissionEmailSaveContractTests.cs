using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// ebilling-r1 task 095 (model §9.1 step 1): the from-email submission's in-process save through the REAL <see cref="OfficeService"/>
/// pipeline, over <see cref="OfficeVersionSaveWorld"/>. These are what the submission service's doubles cannot show:
/// <list type="bullet">
/// <item>The <c>.eml</c> is created linked to the email's communication, with its own NON-provisional Restricted and the submission
/// marker, in the ONE create.</item>
/// <item>The parts the save reports are the parts of the bytes that reached storage.</item>
/// <item>An email with no communication is refused before the job row and the upload.</item>
/// </list>
/// </summary>
public class OfficeSubmissionEmailSaveContractTests
{
    private const string MessageId = "<inv-1001@firm.example>";
    private static readonly Guid CommunicationId = Guid.Parse("09509509-0950-4950-8950-0000000000c9");
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7 invoice 1001");
    private static readonly byte[] XlsxBytes = Encoding.ASCII.GetBytes("PK timesheet");

    private static AttachmentReference Attachment(string name, byte[] bytes, string type) => new()
    {
        AttachmentId = "att-" + name, FileName = name, Size = bytes.Length, ContentType = type, ContentBase64 = Convert.ToBase64String(bytes),
    };

    private static SaveRequest SubmissionSave(List<string>? selected = null, string? idempotencyKey = null) => new()
    {
        ContentType = SaveContentType.Email,
        TargetEntity = new SaveEntityReference { EntityType = "Matter", EntityId = Guid.Parse("09509509-0950-4950-8950-0000000000a9") },
        Email = new EmailMetadata
        {
            Subject = "Invoice 1001",
            SenderEmail = "billing@firm.example",
            InternetMessageId = MessageId,
            Body = "Please find our invoice attached.",
            Attachments = new List<AttachmentReference>
            {
                Attachment("Invoice 1001.pdf", PdfBytes, "application/pdf"),
                Attachment("Timesheet.xlsx", XlsxBytes, "application/vnd.ms-excel"),
            },
            SelectedAttachmentFileNames = selected,
        },
        IdempotencyKey = idempotencyKey,
    };

    /// <summary>The marked parts of <see cref="SubmissionSave"/>: the Invoice and the timesheet.</summary>
    private static readonly IReadOnlyList<string> Marked = new[] { EmlMimeParts.Sha256Hex(PdfBytes), EmlMimeParts.Sha256Hex(XlsxBytes) };

    private static async Task<SubmissionEmailSaveOutcome> SaveAsync(
        OfficeVersionSaveTestWebAppFactory factory, SaveRequest request, IReadOnlyList<string>? marked = null)
    {
        using var scope = factory.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider, TraceIdentifier = "trace-095" };
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("oid", "test-user-oid"), new Claim(ClaimTypes.NameIdentifier, "test-user-id"), new Claim("tid", "test-tenant-id") },
            "Test"));
        http.Request.Headers.Authorization = "Bearer test-caller-token";
        if (scope.ServiceProvider.GetService<IHttpContextAccessor>() is { } accessor)
        {
            accessor.HttpContext = http;
        }

        var save = scope.ServiceProvider.GetRequiredService<IOfficeSubmissionEmailSave>();
        return await save.SaveEmailForSubmissionAsync(request, marked ?? Marked, "test-user-oid", http, CancellationToken.None);
    }

    [Fact]
    public async Task SubmissionSave_CreatesTheEmlLinked_OwnRestrictedNonProvisional_AndMarked_InTheOneCreate()
    {
        var world = new OfficeVersionSaveWorld();
        world.CommunicationsByMessageId[MessageId] = CommunicationId;
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var outcome = await SaveAsync(factory, SubmissionSave(selected: new List<string>()));

        outcome.Refusal.Should().BeNull();
        outcome.Response!.Success.Should().BeTrue();
        outcome.CommunicationId.Should().Be(CommunicationId);
        outcome.ContentDuplicate.Should().BeFalse();

        var create = world.CreatedDocumentRequests.Should().ContainSingle("only the .eml: no attachment Document is made").Subject;
        create.RelatedCommunicationId.Should().Be(CommunicationId);
        create.RestrictedBySubmission.Should().BeTrue();
        create.AccessPermission.Should().Be(100000002, "Restricted");
        var (record, unreadable) = AccessInheritance.Parse(create.AccessInheritanceRecord);
        unreadable.Should().BeFalse();
        record!.OwnProvisional.Should().BeFalse("linking the communication must never release the submission's narrowing");

        var created = world.Documents.Values.Should().ContainSingle().Subject;
        outcome.EmlDocumentId.Should().Be(created.Id);

        // The parts the save reports are the parts of the bytes that reached storage (model 9.1 step 2).
        var stored = world.SpeItems[created.ItemId!].Versions.Should().ContainSingle().Subject;
        using var storedEml = new MemoryStream(stored);
        outcome.EmlParts.Should().BeEquivalentTo(EmlMimeParts.Read(storedEml), o => o.WithStrictOrdering());
        outcome.EmlParts!.Select(p => (p.Ordinal, p.FileName, p.Sha256)).Should().Equal(
            (0, "Invoice 1001.pdf", EmlMimeParts.Sha256Hex(PdfBytes)),
            (1, "Timesheet.xlsx", EmlMimeParts.Sha256Hex(XlsxBytes)));
    }

    [Fact]
    public async Task SubmissionSave_WithNoCommunicationForTheEmail_IsRefusedBeforeTheJobRowAndTheUpload()
    {
        var world = new OfficeVersionSaveWorld();
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var outcome = await SaveAsync(factory, SubmissionSave(selected: new List<string>()));

        outcome.Refusal.Should().Be(SubmissionEmailSaveRefusal.NoCommunication);
        outcome.Response!.Success.Should().BeFalse();
        world.Jobs.Should().BeEmpty();
        world.DocumentCreates.Should().Be(0);
        world.SpeItems.Should().BeEmpty();
    }

    [Fact]
    public async Task OrdinarySave_OfTheSameEmail_IsNeverMarked()
    {
        var world = new OfficeVersionSaveWorld();
        world.CommunicationsByMessageId[MessageId] = CommunicationId;
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        using var scope = factory.Services.CreateScope();

        var response = await scope.ServiceProvider.GetRequiredService<IOfficeService>().SaveAsync(
            SubmissionSave(), "test-user-oid", new DefaultHttpContext { RequestServices = scope.ServiceProvider }, CancellationToken.None);

        response.Success.Should().BeTrue();
        world.CreatedDocumentRequests.Should().ContainSingle().Which.RestrictedBySubmission.Should().BeFalse();
    }

    // ── 095 review F1: the marked parts reach the .eml byte-for-byte ────────────────────────────────────────────

    public static TheoryData<string, string, byte[]> TextAttachments() => new()
    {
        { "crlf.csv", "text/csv", Encoding.ASCII.GetBytes("a,b\r\n1,2\r\n") },
        { "lf.csv", "text/csv", Encoding.ASCII.GetBytes("a,b\n1,2\n") },
        { "long.txt", "text/plain", Encoding.ASCII.GetBytes(new string('x', 1200) + "\r\nend\n") },
    };

    /// <summary>
    /// A text attachment keeps its exact bytes in the built <c>.eml</c> whichever newline format the message is written with (the BFF runs
    /// on Linux: MimeKit's default there is LF). A 7-bit or quoted-printable text part has its CRLF/LF rewritten on write; base64 does not.
    /// </summary>
    [Theory]
    [MemberData(nameof(TextAttachments))]
    public void BuiltEml_ATextAttachment_KeepsItsBytes_UnderBothNewlineFormats(string name, string type, byte[] bytes)
    {
        var email = SubmissionSave(selected: new List<string>()).Email! with
        {
            Attachments = new List<AttachmentReference> { Attachment(name, bytes, type) },
        };

        using var built = OfficeEmailEnricher.BuildEmlFromMetadata(email);
        var message = MimeMessage.Load(built);
        foreach (var format in new[] { NewLineFormat.Dos, NewLineFormat.Unix })
        {
            var options = FormatOptions.Default.Clone();
            options.NewLineFormat = format;
            using var written = new MemoryStream();
            message.WriteTo(options, written);
            written.Position = 0;

            EmlMimeParts.Read(written).Should().ContainSingle()
                .Which.Sha256.Should().Be(EmlMimeParts.Sha256Hex(bytes), $"{name} written with {format} newlines");
        }
    }

    /// <summary>
    /// The save refuses, after the build and before the upload and the document create, when a marked part is not in the built
    /// <c>.eml</c>: here an attached email (message/rfc822), which MIME nests as a message, not a file part. Step 0 refuses such a mark on
    /// the route; this is the save's own guard.
    /// </summary>
    [Fact]
    public async Task SubmissionSave_AMarkedPartTheBuiltEmlDoesNotCarry_IsRefused_BeforeTheUploadAndTheCreate()
    {
        var world = new OfficeVersionSaveWorld();
        world.CommunicationsByMessageId[MessageId] = CommunicationId;
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var forwarded = Encoding.ASCII.GetBytes("From: a@b.c\r\nSubject: original\r\n\r\nbody\r\n");
        var request = SubmissionSave(selected: new List<string>());
        request = request with
        {
            Email = request.Email! with
            {
                Attachments = new List<AttachmentReference>(request.Email.Attachments!) { Attachment("original.eml", forwarded, "message/rfc822") },
            },
        };

        var outcome = await SaveAsync(factory, request, Marked.Append(EmlMimeParts.Sha256Hex(forwarded)).ToList());

        outcome.Refusal.Should().Be(SubmissionEmailSaveRefusal.AttachmentNotPreserved);
        outcome.Response!.Success.Should().BeFalse();
        outcome.EmlDocumentId.Should().BeNull();
        world.SpeItems.Should().BeEmpty("refused before the upload");
        world.DocumentCreates.Should().Be(0);
        world.FinalizationPayloads.Should().BeEmpty();
    }

    /// <summary>
    /// Through the real save pipeline: a marked LF text file reaches storage byte-for-byte, and the parts the save reports are those of
    /// the stored bytes. Without base64 the part is written 7-bit with the writer's newlines, and the save refuses it.
    /// </summary>
    [Fact]
    public async Task SubmissionSave_AMarkedLfTextFile_ReachesStorageByteForByte()
    {
        var world = new OfficeVersionSaveWorld();
        world.CommunicationsByMessageId[MessageId] = CommunicationId;
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);
        var csv = Encoding.ASCII.GetBytes("date,hours\n2026-10-01,7.5\n2026-10-02,8\n");
        var request = SubmissionSave(selected: new List<string>());
        request = request with
        {
            Email = request.Email! with
            {
                Attachments = new List<AttachmentReference> { Attachment("Invoice 1001.pdf", PdfBytes, "application/pdf"), Attachment("hours.csv", csv, "text/csv") },
            },
        };

        var outcome = await SaveAsync(factory, request, new[] { EmlMimeParts.Sha256Hex(PdfBytes), EmlMimeParts.Sha256Hex(csv) });

        outcome.Refusal.Should().BeNull();
        outcome.Response!.Success.Should().BeTrue();
        var created = world.Documents.Values.Should().ContainSingle().Subject;
        using var storedEml = new MemoryStream(world.SpeItems[created.ItemId!].Versions.Single());
        EmlMimeParts.Read(storedEml).Select(p => p.Sha256).Should().Equal(EmlMimeParts.Sha256Hex(PdfBytes), EmlMimeParts.Sha256Hex(csv));
    }

    [Fact]
    public async Task SubmissionSave_TwoIdenticalMarkedFiles_NeedTwoParts()
    {
        var world = new OfficeVersionSaveWorld();
        world.CommunicationsByMessageId[MessageId] = CommunicationId;
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        var outcome = await SaveAsync(factory, SubmissionSave(selected: new List<string>()),
            new[] { EmlMimeParts.Sha256Hex(PdfBytes), EmlMimeParts.Sha256Hex(PdfBytes) });

        outcome.Refusal.Should().Be(SubmissionEmailSaveRefusal.AttachmentNotPreserved, "the .eml carries the PDF once");
        world.DocumentCreates.Should().Be(0);
    }

    public static TheoryData<SaveRequest> NotASubmissionSave() => new()
    {
        SubmissionSave(selected: null),
        SubmissionSave(selected: new List<string> { "Invoice 1001.pdf" }),
        SubmissionSave(selected: new List<string>(), idempotencyKey: "client-key"),
        SubmissionSave(selected: new List<string>()) with { ContentType = SaveContentType.Document },
    };

    [Theory]
    [MemberData(nameof(NotASubmissionSave))]
    public async Task SubmissionSave_OfAnythingButAnEmailWithNoSelectionAndNoBodyKey_IsADefect_RefusedBeforeAnyWrite(SaveRequest request)
    {
        var world = new OfficeVersionSaveWorld();
        world.CommunicationsByMessageId[MessageId] = CommunicationId;
        using var factory = new OfficeVersionSaveTestWebAppFactory(world);

        await FluentActions.Awaiting(() => SaveAsync(factory, request)).Should().ThrowAsync<ArgumentException>();
        world.DocumentCreates.Should().Be(0);
        world.Jobs.Should().BeEmpty();
    }
}
