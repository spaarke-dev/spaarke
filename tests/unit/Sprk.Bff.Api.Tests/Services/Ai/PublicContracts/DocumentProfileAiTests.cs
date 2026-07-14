// spaarkeai-compose-r2 (UAT #7b) — unit tests for the OBO document-profile facade.
//
// The facade's ONE job is to fix the round-6 bug: a document saved from Compose is written to SPE
// under the user's OBO identity, so the app-only (Managed Identity) profiling path 403s on the
// download and the profile fields silently never populate. The facade downloads under OBO instead,
// then delegates the (unchanged) extract → classify → summarize → field-map → UpdateDocumentAsync
// pipeline to IAppOnlyAnalysisService. These are BEHAVIOR tests of that boundary:
//   • Does it download via the OBO path (DownloadFileAsUserAsync), NOT the app-only path
//     (DownloadFileAsync) that caused the bug?
//   • Does it delegate the downloaded stream to the reused profiling method (which owns the field
//     mapping + Dataverse write)?
//   • Is it best-effort (never throws; skips/fails degrade to a result)?
//
// Mocking boundary (ADR-038 §4 module-boundary mocks — not banned collaborator mocking): every mock
// here is a genuine external boundary — IDocumentDataverseService (Dataverse), ISpeFileOperations
// (SPE/Graph facade, ADR-007), IAppOnlyAnalysisService (the reused AI profiling seam).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PublicContracts;

public sealed class DocumentProfileAiTests
{
    private const string DriveId = "drive-obo-1";
    private const string ItemId = "item-obo-1";
    private const string FileName = "draft.docx";

    private readonly Mock<IDocumentDataverseService> _documents = new(MockBehavior.Strict);
    private readonly Mock<ISpeFileOperations> _spe = new(MockBehavior.Strict);
    private readonly Mock<IAppOnlyAnalysisService> _analysis = new(MockBehavior.Strict);

    private DocumentProfileAi CreateSut() => new(
        _documents.Object, _spe.Object, _analysis.Object, NullLogger<DocumentProfileAi>.Instance);

    private void ArrangeDocument(string? driveId = DriveId, string? itemId = ItemId)
    {
        _documents
            .Setup(d => d.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEntity
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Draft",
                FileName = FileName,
                GraphDriveId = driveId,
                GraphItemId = itemId,
            });
    }

    // ── The fix: downloads under OBO (not app-only) and delegates to the reused profiling pipeline ─
    [Fact]
    public async Task ProfileDocumentAsUserAsync_DownloadsUnderObo_DelegatesToProfiling_ReturnsSuccess()
    {
        var documentId = Guid.NewGuid();
        ArrangeDocument();

        var ctx = new DefaultHttpContext();
        using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        _spe
            .Setup(s => s.DownloadFileAsUserAsync(ctx, DriveId, ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stream);

        Stream? delegatedStream = null;
        _analysis
            .Setup(a => a.AnalyzeDocumentFromStreamAsync(
                documentId, FileName, It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, Stream, string?, CancellationToken>((_, _, s, _, _) => delegatedStream = s)
            .ReturnsAsync(AppOnlyDocumentAnalysisResult.Success(documentId, profileUpdate: null));

        var result = await CreateSut().ProfileDocumentAsUserAsync(documentId, ctx, CancellationToken.None);

        result.Success.Should().BeTrue();

        // The bug fix: the OBO download path is used with the record's pointers + the request context;
        // the app-only path (which 403s on user-written files) is NEVER used.
        _spe.Verify(s => s.DownloadFileAsUserAsync(ctx, DriveId, ItemId, It.IsAny<CancellationToken>()), Times.Once);
        _spe.Verify(s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // The OBO-downloaded stream is handed to the reused profiling method (which owns the field
        // mapping + UpdateDocumentAsync write) — no mapping is duplicated in the facade.
        _analysis.Verify(a => a.AnalyzeDocumentFromStreamAsync(
            documentId, FileName, It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        delegatedStream.Should().BeSameAs(stream);
    }

    // ── Skip cleanly (no download, no profiling) when the record has no SPE pointer ─────────────
    [Fact]
    public async Task ProfileDocumentAsUserAsync_WhenNoSpePointer_SkipsWithoutDownloadOrProfiling()
    {
        var documentId = Guid.NewGuid();
        ArrangeDocument(driveId: null, itemId: null);

        var result = await CreateSut().ProfileDocumentAsUserAsync(documentId, new DefaultHttpContext(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.SkipReason.Should().NotBeNullOrEmpty();
        _spe.Verify(s => s.DownloadFileAsUserAsync(
            It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _analysis.Verify(a => a.AnalyzeDocumentFromStreamAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── A null OBO download degrades to Failed (best-effort), never throws ──────────────────────
    [Fact]
    public async Task ProfileDocumentAsUserAsync_WhenOboDownloadReturnsNull_ReturnsFailed()
    {
        var documentId = Guid.NewGuid();
        ArrangeDocument();
        _spe
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), DriveId, ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream?)null);

        var result = await CreateSut().ProfileDocumentAsUserAsync(documentId, new DefaultHttpContext(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrEmpty();
        _analysis.Verify(a => a.AnalyzeDocumentFromStreamAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Best-effort: a throwing profiling step is swallowed into a Failed result (never propagates) ─
    [Fact]
    public async Task ProfileDocumentAsUserAsync_WhenProfilingThrows_ReturnsFailedAndDoesNotThrow()
    {
        var documentId = Guid.NewGuid();
        ArrangeDocument();

        using var stream = new MemoryStream(new byte[] { 9 });
        _spe
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), DriveId, ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stream);
        _analysis
            .Setup(a => a.AnalyzeDocumentFromStreamAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("playbook exploded"));

        var act = async () => await CreateSut().ProfileDocumentAsUserAsync(documentId, new DefaultHttpContext(), CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.Success.Should().BeFalse();
        result.Subject.FailureReason.Should().NotBeNullOrEmpty();
    }
}
