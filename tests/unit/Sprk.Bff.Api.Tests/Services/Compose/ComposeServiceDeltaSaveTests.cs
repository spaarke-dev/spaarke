// FR-01 / FR-06 (task 022, E1 keystone — Option C) — unit tests for ComposeService.SaveAsync's
// baseline-inversion orchestration: a dirty save persists a DELTA onto the retained load-time original,
// never a client reconstruction. These verify the ORCHESTRATION SaveAsync adds (baseline resolution +
// driving the synthesizer + persisting the delta), NOT the synthesis algorithm itself — that is covered
// by ComposeParagraphRedlineSynthesizerTests. Each test names a concrete production behavior:
//   - EditedParagraphs supplied → the persisted bytes carry w:ins/w:del for exactly the edited paragraph,
//     synthesized ONTO the resolved baseline (the Content fast-path here), untouched paragraphs preserved
//   - Content absent → SaveAsync re-fetches the load-time SPE version by BaselineVersionId (FR-06 primary,
//     the "save after a page refresh" case) and synthesizes onto THAT baseline
//   - no baseline resolvable (no Content, no BaselineVersionId) → a clear error, never a lossy rebuild (FR-01)
//
// Mocking boundary (ADR-038 §4 — module boundaries, NOT the class-under-test's own collaborators):
// ISpeFileOperations (SPE/Graph facade, ADR-007), IGenericEntityService (Dataverse), ChatSessionManager
// (Redis session store), IPostUploadIndexingEnqueuer (RAG indexing seam) — the SAME boundary set as the
// sibling ComposeServiceUploadFidelityTests. Real in-memory .docx fixtures (Open XML SDK); no transport
// mocks (B1), no DI/ctor tests (B3/B4). The through-the-wire seam proof rides task 024 (NFR-06).

using System;
using System.Collections.Generic;
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
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class ComposeServiceDeltaSaveTests
{
    private const string Tenant = "tenant-aad-022";
    private const string ExistingDriveId = "drive-delta";
    private const string ExistingSpeItemId = "spe-delta";
    private const string LoadTimeVersionId = "v-load-time-1";

    // A paragraph the user edited, keyed by its w14:paraId — the middle paragraph of the 3-para fixture.
    private const string EditedParaId = "0000000B";
    private const string UntouchedParaId = "0000000A";

    private readonly Mock<ISpeFileOperations> _spe = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);
    private readonly Mock<IPostUploadIndexingEnqueuer> _indexing = new(MockBehavior.Strict);
    private readonly Mock<ChatSessionManager> _sessions;

    public ComposeServiceDeltaSaveTests()
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

    // ── real .docx fixture (3 paragraphs, each with a w14:paraId) ────────────────────────────────

    private static Paragraph Para(string paraId, string text)
    {
        var p = new Paragraph { ParagraphId = new HexBinaryValue(paraId) };
        p.AppendChild(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return p;
    }

    private static byte[] ThreeParagraphDoc() => BuildDocx(
        Para(UntouchedParaId, "The parties enter into this Agreement."),
        Para(EditedParaId, "Termination requires thirty days written notice."),
        Para("0000000C", "Confidential Information shall not be shared."));

    private static byte[] BuildDocx(params OpenXmlElement[] bodyChildren)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            foreach (var child in bodyChildren)
            {
                body.Append(child);
            }
            body.Append(new SectionProperties());
            main.Document = new Document(body);
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static (int Ins, int Del) RevisionCounts(byte[] docx)
    {
        using var buffer = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(buffer, false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        return (body.Descendants<InsertedRun>().Count(), body.Descendants<DeletedRun>().Count());
    }

    private static IReadOnlyList<string> BodyParaIds(byte[] docx)
    {
        using var buffer = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(buffer, false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Select(p => p.ParagraphId?.Value)
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!.ToUpperInvariant())
            .ToList();
    }

    private static string ParaText(byte[] docx, string paraId)
    {
        using var buffer = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(buffer, false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .First(p => string.Equals(p.ParagraphId?.Value, paraId, StringComparison.OrdinalIgnoreCase)).InnerText;
    }

    // ── strict-mock arrangements (replace path — existing SPE drive-item) ─────────────────────────

    private static FileHandleDto ReplacedDriveItem() => new(
        Id: ExistingSpeItemId,
        Name: "delta.docx",
        ParentId: null,
        Size: 4321,
        CreatedDateTime: DateTimeOffset.UtcNow,
        LastModifiedDateTime: DateTimeOffset.UtcNow,
        ETag: "\"etag-delta-v2\"",
        IsFolder: false,
        WebUrl: "https://spe/web",
        DriveId: ExistingDriveId);

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

    // ── FR-01: EditedParagraphs synthesize a delta ONTO the Content fast-path baseline ───────────
    [Fact]
    public async Task SaveAsync_WithEditedParagraphs_PersistsTrackedChangeDeltaOntoRetainedOriginal()
    {
        ArrangeReplaceExisting(out var capturedBytes);
        ArrangeExistingRecordFound();
        ArrangeIndexingSubmitted();

        var sut = CreateSut();
        var baseline = ThreeParagraphDoc();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = ExistingSpeItemId,
            DriveId = ExistingDriveId,
            Content = baseline,                 // same-session fast-path: the RETAINED ORIGINAL bytes
            EditedParagraphs = new[] { new ComposeEditedParagraph(EditedParaId, "Termination requires sixty days written notice.") },
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        var persisted = capturedBytes();
        var (ins, del) = RevisionCounts(persisted);
        ins.Should().BeGreaterThan(0, "the edited paragraph's new words are emitted as w:ins revision runs");
        del.Should().BeGreaterThan(0, "the edited paragraph's replaced words are emitted as w:del revision runs");
        persisted.Should().NotBeEquivalentTo(baseline.ToArray(),
            "a dirty save must persist a DELTA, not the untouched baseline");
        BodyParaIds(persisted).Should().BeEquivalentTo(BodyParaIds(baseline),
            "every w14:paraId survives — the delta rewrites run content in place, not the paragraph identity (E2)");
        ParaText(persisted, UntouchedParaId).Should().Be("The parties enter into this Agreement.",
            "a paragraph the user did not edit is left byte-untouched (the fidelity guarantee, NFR-07)");
    }

    // ── FR-06: Content absent → re-fetch the load-time version by versionId, synthesize onto it ──
    [Fact]
    public async Task SaveAsync_WithNoContentButBaselineVersionId_FetchesLoadTimeVersionAndDeltasOntoIt()
    {
        ArrangeReplaceExisting(out var capturedBytes);
        ArrangeExistingRecordFound();
        ArrangeIndexingSubmitted();

        var baseline = ThreeParagraphDoc();
        _spe.Setup(s => s.DownloadFileVersionAsUserAsync(
                It.IsAny<HttpContext>(), ExistingDriveId, ExistingSpeItemId, LoadTimeVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(baseline, writable: false));

        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = ExistingSpeItemId,
            DriveId = ExistingDriveId,
            // Content OMITTED — the "save after a page refresh" case: the client no longer holds the bytes.
            BaselineVersionId = LoadTimeVersionId,
            EditedParagraphs = new[] { new ComposeEditedParagraph(EditedParaId, "Termination requires sixty days written notice.") },
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        _spe.Verify(s => s.DownloadFileVersionAsUserAsync(
                It.IsAny<HttpContext>(), ExistingDriveId, ExistingSpeItemId, LoadTimeVersionId, It.IsAny<CancellationToken>()),
            Times.Once,
            "with no client bytes, the baseline MUST be re-fetched from the load-time SPE version (FR-06 primary)");

        var (ins, del) = RevisionCounts(capturedBytes());
        (ins + del).Should().BeGreaterThan(0,
            "the delta is synthesized onto the re-fetched load-time baseline, not skipped");
    }

    // ── FR-01 guard: no resolvable baseline → clear error, never a reconstruction fallback ───────
    [Fact]
    public async Task SaveAsync_WithNoContentAndNoBaselineVersionId_ThrowsRatherThanReconstruct()
    {
        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = ExistingSpeItemId,
            DriveId = ExistingDriveId,
            // Neither Content nor BaselineVersionId — the baseline cannot be resolved.
            EditedParagraphs = new[] { new ComposeEditedParagraph(EditedParaId, "changed") },
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        // Strict SPE mock has NO write arranged — a reconstruction fallback would surface as an
        // unexpected ReplaceFileContentAsUserAsync call; instead the baseline resolution throws first.
        await FluentActions.Awaiting(() => sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>("a dirty save with no resolvable baseline must fail honestly (FR-01), not reconstruct");
    }
}
