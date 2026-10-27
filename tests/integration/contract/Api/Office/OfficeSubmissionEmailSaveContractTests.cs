using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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

    private static async Task<SubmissionEmailSaveOutcome> SaveAsync(OfficeVersionSaveTestWebAppFactory factory, SaveRequest request)
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
        return await save.SaveEmailForSubmissionAsync(request, "test-user-oid", http, CancellationToken.None);
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
