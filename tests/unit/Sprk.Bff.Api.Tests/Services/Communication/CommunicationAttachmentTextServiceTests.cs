using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of <see cref="CommunicationAttachmentTextService"/> — the reconciliation browse reader's
/// attachment-text read model (email-communication-intelligence-r2 B2.1). These protect the load-bearing
/// properties that would regress silently:
/// <list type="number">
///   <item>a supported, downloadable, caller-visible attachment yields <c>Extractable=true</c> with its text;</item>
///   <item>EVERY non-text outcome (unsupported type, missing SPE reference, a document the caller cannot see,
///   image/vision-required, a download miss, an extraction failure, a thrown exception) degrades to the SAME
///   non-fatal <c>Extractable=false</c> fold — never an error, never a lost sibling attachment;</item>
///   <item>the two Dataverse reads are IMPERSONATED (NFR-06 no-leak) and an unresolvable caller is refused 403
///   (fail-closed).</item>
/// </list>
/// The impersonated query seam (<see cref="IImpersonatedCommunicationQuery"/>), the caller resolver
/// (<see cref="ICallerSystemUserResolver"/>), the SPE facade (<see cref="ISpeFileOperations"/>, ADR-007), and the
/// shared cache-aware extractor (<see cref="ITextExtractor"/>) are mocked at the module boundary — no live
/// Dataverse/SPE/Document-Intelligence is provisioned, and these side effects have no other observable surface
/// (same rationale + shape as <c>MessageAttachmentMaterializerTests</c> / <c>CommunicationThreadReadService</c> tests).
/// </summary>
public class CommunicationAttachmentTextServiceTests
{
    private static readonly Guid CommunicationId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CallerSystemUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string AttachmentSet = "sprk_communicationattachments";
    private const string DocumentSet = "sprk_documents";
    private const string DriveId = "b!drive-000";
    private const string ItemId = "item-000";

    private static Dictionary<string, JsonElement> Row(params (string Key, object? Val)[] fields)
    {
        var obj = new Dictionary<string, object?>();
        foreach (var (k, v) in fields)
            obj[k] = v;
        var json = JsonSerializer.Serialize(obj);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    }

    private static Dictionary<string, JsonElement> AttachmentRow(Guid attachmentId, Guid documentId, string name) =>
        Row(
            ("sprk_communicationattachmentid", attachmentId.ToString()),
            ("_sprk_document_value", documentId.ToString()),
            ("sprk_name", name));

    private static Dictionary<string, JsonElement> DocumentRow(
        Guid documentId, string fileName, string? driveId = DriveId, string? itemId = ItemId) =>
        Row(
            ("sprk_documentid", documentId.ToString()),
            ("sprk_graphdriveid", driveId),
            ("sprk_graphitemid", itemId),
            ("sprk_filename", fileName));

    private sealed class Harness
    {
        public Mock<IImpersonatedCommunicationQuery> Query { get; } = new(MockBehavior.Loose);
        public Mock<ICallerSystemUserResolver> Resolver { get; } = new(MockBehavior.Loose);
        public Mock<ISpeFileOperations> Spe { get; } = new(MockBehavior.Loose);
        public Mock<ITextExtractor> Extractor { get; } = new(MockBehavior.Loose);

        public Harness()
        {
            // Default: a resolvable caller. Individual tests override for the fail-closed case.
            Resolver
                .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString()));
        }

        public Harness CallerUnresolved()
        {
            Resolver
                .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallerSystemUserResolution { IsResolved = false, UnresolvedReason = "no_oid" });
            return this;
        }

        public Harness Attachments(params Dictionary<string, JsonElement>[] rows)
        {
            Query
                .Setup(q => q.QueryAsync(AttachmentSet, It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(rows.ToList());
            return this;
        }

        public Harness AttachmentsThrow(Exception ex)
        {
            Query
                .Setup(q => q.QueryAsync(AttachmentSet, It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(ex);
            return this;
        }

        public Harness Documents(params Dictionary<string, JsonElement>[] rows)
        {
            Query
                .Setup(q => q.QueryAsync(DocumentSet, It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(rows.ToList());
            return this;
        }

        public Harness Supported(bool supported = true)
        {
            Extractor.Setup(x => x.IsSupported(It.IsAny<string>())).Returns(supported);
            return this;
        }

        public Harness DownloadReturns(Func<Stream?> stream)
        {
            Spe
                .Setup(s => s.DownloadFileAsUserAsync(
                    It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(stream);
            Spe
                .Setup(s => s.GetFileMetadataAsUserAsync(
                    It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FileHandleDto(
                    Id: ItemId, Name: "f", ParentId: null, Size: 1,
                    CreatedDateTime: DateTimeOffset.UnixEpoch, LastModifiedDateTime: DateTimeOffset.UnixEpoch,
                    ETag: "etag-1", IsFolder: false, WebUrl: null));
            return this;
        }

        public Harness Extracts(TextExtractionResult result)
        {
            Extractor
                .Setup(x => x.ExtractAsync(
                    It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(),
                    It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);
            return this;
        }

        public CommunicationAttachmentTextService Build() =>
            new(Query.Object, Resolver.Object, Spe.Object, Extractor.Object,
                Mock.Of<ILogger<CommunicationAttachmentTextService>>());
    }

    private static Stream Bytes(string s = "file bytes") => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(s));

    private static Task<CommunicationAttachmentTextResponse> Run(Harness h) =>
        h.Build().GetAttachmentTextAsync(CommunicationId, new ClaimsPrincipal(), new DefaultHttpContext(), default);

    [Fact]
    public async Task GetAttachmentText_WhenSupportedAndDownloadable_ReturnsExtractedTextExtractable()
    {
        var att = Guid.NewGuid();
        var doc = Guid.NewGuid();
        var result = await Run(new Harness()
            .Attachments(AttachmentRow(att, doc, "brief.pdf"))
            .Documents(DocumentRow(doc, "brief.pdf"))
            .Supported()
            .DownloadReturns(() => Bytes())
            .Extracts(TextExtractionResult.Succeeded("SECTION 1. The registrant hereby files.", TextExtractionMethod.DocumentIntelligence)));

        var item = result.Attachments.Should().ContainSingle().Subject;
        item.AttachmentId.Should().Be(att);
        item.DocumentId.Should().Be(doc);
        item.FileName.Should().Be("brief.pdf");
        item.Extractable.Should().BeTrue();
        item.Text.Should().Be("SECTION 1. The registrant hereby files.");
        item.Method.Should().Be("DocumentIntelligence");
    }

    [Fact]
    public async Task GetAttachmentText_WhenFileTypeUnsupported_ReturnsNotExtractableAndSkipsDownload()
    {
        var doc = Guid.NewGuid();
        var harness = new Harness()
            .Attachments(AttachmentRow(Guid.NewGuid(), doc, "photo.heic"))
            .Documents(DocumentRow(doc, "photo.heic"))
            .Supported(false);

        var result = await Run(harness);

        result.Attachments.Should().ContainSingle().Which.Extractable.Should().BeFalse();
        // Unsupported types must never trigger an SPE download.
        harness.Spe.Verify(
            s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetAttachmentText_WhenExtractionRequiresVision_ReturnsNotExtractable()
    {
        var doc = Guid.NewGuid();
        var result = await Run(new Harness()
            .Attachments(AttachmentRow(Guid.NewGuid(), doc, "scan.png"))
            .Documents(DocumentRow(doc, "scan.png"))
            .Supported()
            .DownloadReturns(() => Bytes())
            .Extracts(TextExtractionResult.RequiresVision())); // Success=true but Text=null

        var item = result.Attachments.Should().ContainSingle().Subject;
        item.Extractable.Should().BeFalse();
        item.Text.Should().BeNull();
    }

    [Fact]
    public async Task GetAttachmentText_WhenExtractionFails_ReturnsNotExtractable()
    {
        var doc = Guid.NewGuid();
        var result = await Run(new Harness()
            .Attachments(AttachmentRow(Guid.NewGuid(), doc, "corrupt.pdf"))
            .Documents(DocumentRow(doc, "corrupt.pdf"))
            .Supported()
            .DownloadReturns(() => Bytes())
            .Extracts(TextExtractionResult.Failed("boom", TextExtractionMethod.DocumentIntelligence)));

        result.Attachments.Should().ContainSingle().Which.Extractable.Should().BeFalse();
    }

    [Fact]
    public async Task GetAttachmentText_WhenDownloadReturnsNull_ReturnsNotExtractable()
    {
        var doc = Guid.NewGuid();
        var result = await Run(new Harness()
            .Attachments(AttachmentRow(Guid.NewGuid(), doc, "brief.pdf"))
            .Documents(DocumentRow(doc, "brief.pdf"))
            .Supported()
            .DownloadReturns(() => null));

        result.Attachments.Should().ContainSingle().Which.Extractable.Should().BeFalse();
    }

    [Fact]
    public async Task GetAttachmentText_WhenDocumentNotVisibleToCaller_ReturnsNotExtractable()
    {
        // The attachment is visible, but the impersonated DOCUMENT read returns nothing (caller cannot see the
        // sprk_document) → no SPE pointer → not extractable, no download attempted.
        var harness = new Harness()
            .Attachments(AttachmentRow(Guid.NewGuid(), Guid.NewGuid(), "brief.pdf"))
            .Documents() // empty: document not visible to the caller
            .Supported();

        var result = await Run(harness);

        result.Attachments.Should().ContainSingle().Which.Extractable.Should().BeFalse();
        harness.Spe.Verify(
            s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetAttachmentText_WhenOneAttachmentThrows_StillReturnsBothNonFatally()
    {
        var ok = Guid.NewGuid();
        var bad = Guid.NewGuid();
        var okDoc = Guid.NewGuid();
        var badDoc = Guid.NewGuid();
        var harness = new Harness()
            .Attachments(AttachmentRow(ok, okDoc, "good.pdf"), AttachmentRow(bad, badDoc, "bad.pdf"))
            .Documents(
                DocumentRow(okDoc, "good.pdf", driveId: "b!ok", itemId: "ok"),
                DocumentRow(badDoc, "bad.pdf", driveId: "b!bad", itemId: "bad"))
            .Supported();
        harness.Spe
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileHandleDto?)null);
        harness.Spe
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), "b!ok", "ok", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Bytes());
        harness.Spe
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), "b!bad", "bad", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient SPE failure"));
        harness.Extracts(TextExtractionResult.Succeeded("good text", TextExtractionMethod.Native));

        var result = await Run(harness);

        result.Attachments.Should().HaveCount(2);
        result.Attachments.Single(a => a.AttachmentId == ok).Extractable.Should().BeTrue();
        result.Attachments.Single(a => a.AttachmentId == bad).Extractable.Should().BeFalse();
    }

    [Fact]
    public async Task GetAttachmentText_WhenAttachmentQueryThrows_ReturnsEmptyNonFatally()
    {
        var result = await Run(new Harness().AttachmentsThrow(new InvalidOperationException("dataverse down")));

        result.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAttachmentText_WhenNoAttachments_ReturnsEmptyList()
    {
        var result = await Run(new Harness().Attachments());

        result.Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAttachmentText_WhenCallerUnresolvable_Throws403FailClosed()
    {
        var harness = new Harness().CallerUnresolved();

        var act = async () => await Run(harness);

        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.StatusCode.Should().Be(403);
        // Fail-closed: never touches Dataverse or SPE when the caller cannot be resolved.
        harness.Query.Verify(
            q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
