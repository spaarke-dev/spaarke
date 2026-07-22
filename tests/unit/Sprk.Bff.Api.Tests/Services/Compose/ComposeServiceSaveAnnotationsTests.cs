// Clean-save byte fidelity (FR-06a) — unit test for ComposeService.SaveAsync's clean-save path.
//
// R4 task 032 retired the save-path DocxAnnotationWriter composition branch (the old
// SaveComposeDocumentRequest.Annotations payload): all save/annotation writing now routes through the
// single ComposeShadowPatchEngine, driven by the request's OperationLog + (paraId,range)-anchored
// Comments. The save-path native-redline/comment composition once asserted here is now covered by
// ComposeShadowPatchEngineTests (engine behavior) + the through-the-wire seam slices
// (ComposeFidelitySeamTests / ComposeImportedAnchorsSurviveSaveSeamTests) — so the retired
// save-path-Annotations tests were removed with the contract. What remains is the load-bearing
// clean-save invariant: a Save carrying neither an OperationLog nor Comments persists the baseline
// BYTE-IDENTICAL (FR-06a — the plain no-redline Save never re-serializes document.xml).
//
// Mocking boundary (ADR-038 §4 "mock at module boundaries"): every collaborator mocked is a genuine
// external boundary (ISpeFileOperations → SPE/Graph facade; IGenericEntityService → Dataverse;
// ChatSessionManager → Redis-backed store; IPostUploadIndexingEnqueuer → RAG indexing seam). The
// class-under-test (ComposeService) is REAL. Same boundary set as ComposeServiceUploadFidelityTests.cs.

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

    // ── plain Save: no operation log / no comments → baseline persisted BYTE-IDENTICAL (FR-06a) ─────
    [Fact]
    public async Task SaveAsync_WithNoOperationLogOrComments_PersistsBaselineByteIdentical()
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
            // No OperationLog and no Comments → a clean Save: the engine is a byte-identical passthrough.
        };

        await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        capturedBytes().Should().BeEquivalentTo(
            baseline,
            options => options.WithStrictOrdering(),
            "with no operation log or comments the baseline is persisted byte-identical — the patch engine never re-serializes document.xml on a clean Save");
    }
}
