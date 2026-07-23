// Redline/comment save fidelity (spaarkeai-compose-r2 UAT-R7 #2/#3/#4) — unit tests for
// ComposeService.SaveAsync's annotation-apply branch (the fix that routes SAVE through the proven
// DocxAnnotationWriter so pending redlines persist as NATIVE Word tracked-changes + comments).
//
// Before this fix, Save uploaded whatever bytes the client sent — and the client's mark-blind
// tipTapToDocxBytes writer flattened insertion/deletion marks to plain body text (both the original
// AND the AI text landed as ordinary text; zero w:ins/w:del; no comments). The fix: the client now
// sends a clean BASELINE + a structured annotation list, and SaveAsync applies them onto the baseline
// via DocxAnnotationWriter.Annotate BEFORE the SPE write — exactly the pattern PushAnnotationsAsync
// already uses. These tests prove, through the REAL DocxAnnotationWriter and by re-opening the SAVED
// OOXML the SPE write layer received (no OOXML mock), that:
//   (a) a redlined+commented Save persists a .docx carrying real w:ins / w:del / w:comment; and
//   (b) a Save with no annotations persists the baseline BYTE-IDENTICAL (the plain no-redline Save
//       is unchanged — FR-06a byte fidelity preserved).
//
// Mocking boundary (ADR-038 §4 "mock at module boundaries"): every collaborator mocked is a genuine
// external boundary (ISpeFileOperations → SPE/Graph facade; IGenericEntityService → Dataverse;
// ChatSessionManager → Redis-backed store; IPostUploadIndexingEnqueuer → RAG indexing seam). The
// class-under-test (ComposeService) and its DocxAnnotationWriter collaborator are REAL. Same boundary
// set as ComposeServiceUploadFidelityTests.cs, which this file extends for the annotation branch.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class ComposeServiceSaveAnnotationsTests
{
    private const string Tenant = "tenant-aad-r7";
    private const string ExistingDriveId = "drive-existing-r7";
    private const string ExistingSpeItemId = "spe-existing-r7";
    private static readonly DateTimeOffset When = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<ISpeFileOperations> _spe = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);
    private readonly Mock<IPostUploadIndexingEnqueuer> _indexing = new(MockBehavior.Strict);
    private readonly Mock<ChatSessionManager> _sessions;

    public ComposeServiceSaveAnnotationsTests()
    {
        _sessions = new Mock<ChatSessionManager>(
            Mock.Of<ITenantCache>(),
            Mock.Of<IChatDataverseRepository>(),
            NullLogger<ChatSessionManager>.Instance,
            null!,
            null!);
        _sessions
            .Setup(s => s.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatSession?)null);
    }

    private ComposeService CreateSut() => new(
        _spe.Object,
        _sessions.Object,
        _dataverse.Object,
        new DocxAnnotationWriter(),
        _indexing.Object,
        NullLogger<ComposeService>.Instance);

    private static FileHandleDto ReplacedDriveItem() => new(
        Id: ExistingSpeItemId,
        Name: "contract.docx",
        ParentId: null,
        Size: 4242,
        CreatedDateTime: When,
        LastModifiedDateTime: When,
        ETag: "\"etag-r7-v2\"",
        IsFolder: false,
        WebUrl: "https://spe/web",
        DriveId: ExistingDriveId);

    /// <summary>A real, minimal WordprocessingML .docx baseline (one paragraph) — the clean
    /// reject-state document the client sends as <c>Content</c>.</summary>
    private static byte[] CreateDocx(params string[] paragraphs)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();
            foreach (var text in paragraphs)
                body.AppendChild(new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
            mainPart.Document = new Document(body);
            mainPart.Document.Save();
        }
        return ms.ToArray();
    }

    private void ArrangeReplaceExisting(out Func<byte[]> capturedBytesAccessor)
    {
        byte[]? captured = null;
        _spe.Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), ExistingDriveId, ExistingSpeItemId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, CancellationToken>((_, _, _, stream, _) =>
            {
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                captured = buffer.ToArray();
            })
            .ReturnsAsync(ReplacedDriveItem());
        capturedBytesAccessor = () => captured
            ?? throw new InvalidOperationException("ReplaceFileContentAsUserAsync was never invoked.");
    }

    private void ArrangeExistingRecordFound() =>
        _dataverse.Setup(d => d.RetrieveByAlternateKeyAsync(
                "sprk_document", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document") { Id = Guid.NewGuid() });

    private void ArrangeIndexingSubmitted() =>
        _indexing.Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

    // ── #2/#4: a redlined + commented Save persists native w:ins / w:del / w:comment ──────────────
    [Fact]
    public async Task SaveAsync_WithRedlineAndCommentAnnotations_PersistsDocxWithNativeTrackedChangesAndComment()
    {
        ArrangeReplaceExisting(out var capturedBytes);
        ArrangeExistingRecordFound();
        ArrangeIndexingSubmitted();

        // Baseline = the reject-state document: the ORIGINAL "indemnify" is present (its struck
        // half), the proposed "defend" is NOT (an insertion is dropped in the reject baseline). The
        // annotations re-apply the redline (replace "indemnify" → "defend") + one comment.
        var baseline = CreateDocx("The Supplier shall indemnify the Customer.");
        var annotations = new[]
        {
            // Insertion BEFORE Deletion for the replace pair (the client bridge orders it this way so
            // the writer's anchor lookup still finds the live original before it becomes w:delText).
            new DocxAnnotation { Kind = TrackChangeKind.Insertion, TargetText = "indemnify", NewText = "defend", Author = "Spaarke Assistant", Date = When },
            new DocxAnnotation { Kind = TrackChangeKind.Deletion, TargetText = "indemnify", Author = "Spaarke Assistant", Date = When },
            new DocxAnnotation { Kind = TrackChangeKind.Comment, TargetText = "Customer", CommentText = "Name the counterparty entity.", Author = "Spaarke Assistant", Date = When },
        };

        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = ExistingSpeItemId,
            DriveId = ExistingDriveId,
            Content = baseline,
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
            Annotations = annotations,
        };

        await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        // Re-open the exact bytes the SPE write layer received and assert the NATIVE markup landed.
        using var doc = WordprocessingDocument.Open(new MemoryStream(capturedBytes()), false);
        var body = doc.MainDocumentPart!.Document!.Body!;

        var ins = body.Descendants<InsertedRun>().Single();
        ins.Descendants<Text>().Single().Text.Should().Be("defend", "the proposed text saves as a native w:ins");

        var del = body.Descendants<DeletedRun>().Single();
        del.Descendants<DeletedText>().Single().Text.Should().Be("indemnify", "the struck original saves as a native w:del (w:delText)");

        var comment = doc.MainDocumentPart!.WordprocessingCommentsPart!.Comments!.Elements<Comment>().Single();
        comment.InnerText.Should().Be("Name the counterparty entity.", "the comment saves as a native w:comment");
        body.Descendants<CommentRangeStart>().Single().Id!.Value.Should().Be(comment.Id!.Value);

        // The saved bytes are the ANNOTATED render, not the flattened baseline the client sent.
        capturedBytes().Should().NotBeEquivalentTo(baseline, "SaveAsync applied the annotations before persisting");
    }

    // ── plain Save: no annotations → baseline persisted BYTE-IDENTICAL (FR-06a fidelity preserved) ─
    [Fact]
    public async Task SaveAsync_WithNoAnnotations_PersistsBaselineByteIdentical()
    {
        ArrangeReplaceExisting(out var capturedBytes);
        ArrangeExistingRecordFound();
        ArrangeIndexingSubmitted();

        var baseline = CreateDocx("A document with no pending redlines.");
        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = ExistingSpeItemId,
            DriveId = ExistingDriveId,
            Content = baseline,
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
            Annotations = null, // a plain no-redline Save
        };

        await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        capturedBytes().Should().BeEquivalentTo(
            baseline,
            options => options.WithStrictOrdering(),
            "with no annotations the baseline is persisted byte-identical — no DocxAnnotationWriter transform");
    }

    // ── empty (non-null) annotation list is also a no-op (defensive: client always sends the field) ─
    [Fact]
    public async Task SaveAsync_WithEmptyAnnotationList_PersistsBaselineByteIdentical()
    {
        ArrangeReplaceExisting(out var capturedBytes);
        ArrangeExistingRecordFound();
        ArrangeIndexingSubmitted();

        var baseline = CreateDocx("Still nothing to annotate here.");
        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = ExistingSpeItemId,
            DriveId = ExistingDriveId,
            Content = baseline,
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
            Annotations = Array.Empty<DocxAnnotation>(),
        };

        await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        capturedBytes().Should().BeEquivalentTo(
            baseline,
            options => options.WithStrictOrdering(),
            "an empty annotation list is a no-op — the baseline persists unchanged");
    }
}
