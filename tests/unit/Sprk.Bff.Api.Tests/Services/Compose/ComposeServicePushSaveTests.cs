// FR-28 (task 055) — unit tests for ComposeService.PushAnnotationsAsync's push/save orchestration:
// the pipeline happy path (push+save -> new version, per-step JobAwareCompletionState, Tier-2c
// preview), the etag-conflict abort (no partial write, per-step state reflects the abort point),
// and the annotate-failure abort (save is NEVER attempted). Also covers the pre-confirm
// PreviewPushAnnotationsAsync path.
//
// Mocking boundary (ADR-038 §4 "mock at module boundaries"): ISpeFileOperations (SPE/Graph
// facade, ADR-007) is a Strict mock — a Strict mock that is never Setup for a call fails the
// test if that call happens, which is how the "save is never attempted" assertions are proven
// without a separate Verify(Times.Never()) (Strict already enforces it; Times.Never() is added
// for readability). ChatSessionManager backs a real in-memory dictionary (round-trip, not
// interaction-only) — same pattern as AnchoredAnnotationPersistenceTests/ComposeServiceCreateOnSaveTests.
// The cross-request Redis persistence surface uses a REAL MemoryDistributedCache (same pattern as
// AnnotationReanchorServiceTests) so the "no partial write, state IS recorded" claim is verified
// against the actual ComposePushSaveStatusStore, not a mock of it.
//
// Banned-pattern compliance (tests/CLAUDE.md B1-B17): no Mock<HttpMessageHandler> (B1), no
// DI-registration test (B3), no ctor null-check test (B4), no mirror/getter tests (B6/B16). Each
// test names a concrete production behavior that breaks if the test is deleted.

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
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class ComposeServicePushSaveTests
{
    private const string Tenant = "tenant-aad-055";
    private const string DocumentSpeId = "spe-item-055";
    private const string DriveId = "drive-055";
    private const string LoadTimeETag = "\"etag-load-1\"";
    private const string NewVersionId = "spe-item-055-v2";

    private static readonly DateTimeOffset When = new(2026, 7, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<ISpeFileOperations> _spe = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);
    private readonly Mock<IPostUploadIndexingEnqueuer> _indexing = new(MockBehavior.Strict);
    private readonly Mock<ChatSessionManager> _sessions;
    private readonly Dictionary<string, ChatSession> _sessionStore = new(StringComparer.Ordinal);
    private readonly IDistributedCache _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    public ComposeServicePushSaveTests()
    {
        _sessions = new Mock<ChatSessionManager>(
            Mock.Of<ITenantCache>(),
            Mock.Of<IChatDataverseRepository>(),
            NullLogger<ChatSessionManager>.Instance,
            null!,
            null!);

        _sessions
            .Setup(s => s.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tenantId, string sessionId, CancellationToken _) =>
                _sessionStore.TryGetValue(sessionId, out var session) && session.TenantId == tenantId ? session : null);
    }

    private ComposeService CreateSut() => new(
        _spe.Object,
        _sessions.Object,
        _dataverse.Object,
        new DocxAnnotationWriter(),
        _indexing.Object,
        NullLogger<ComposeService>.Instance,
        _cache);

    private void SeedSession(string sessionId, IReadOnlyList<DefinedTerm>? definedTerms = null)
    {
        _sessionStore[sessionId] = new ChatSession(
            SessionId: sessionId,
            TenantId: Tenant,
            DocumentId: DocumentSpeId,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>())
        {
            DefinedTermsTracking = definedTerms,
        };
    }

    private static byte[] CreateDocx(params string[] paragraphs)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();
            foreach (var text in paragraphs)
            {
                body.AppendChild(new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
            }

            mainPart.Document = new Document(body);
            mainPart.Document.Save();
        }

        return ms.ToArray();
    }

    private static IReadOnlyList<DocxAnnotation> ThreeMixedAnnotations() => new[]
    {
        new DocxAnnotation { Kind = TrackChangeKind.Comment, TargetText = "quick", CommentText = "Consider a stronger adjective.", Author = "Spaarke AI", Date = When },
        new DocxAnnotation { Kind = TrackChangeKind.Insertion, TargetText = "fox", NewText = " (Vulpes vulpes)", Author = "Spaarke AI", Date = When },
        new DocxAnnotation { Kind = TrackChangeKind.Deletion, TargetText = "lazy ", Author = "Spaarke AI", Date = When },
    };

    private void ArrangeDownload(byte[] sourceBytes) =>
        _spe.Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(sourceBytes));

    private static JobAwareState StateOf(JobAwareCompletionState completion, string stepName) =>
        completion.Steps.Single(s => s.StepName == stepName).State;

    // ── Acceptance #1: pipeline happy path (push+save -> new version) ───────────────────────────
    [Fact]
    public async Task PushAnnotationsAsync_HappyPath_PushesSavesAndReturnsNewVersion_WithCompletedSteps()
    {
        var source = CreateDocx("The quick brown fox jumps over the lazy dog.");
        ArrangeDownload(source);

        var saved = new FileHandleDto(
            Id: NewVersionId, Name: "draft.docx", ParentId: null, Size: 4321,
            CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
            ETag: "\"etag-v2\"", IsFolder: false, WebUrl: "https://spe/web", DriveId: DriveId);

        _spe.Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<Stream>(), LoadTimeETag, It.IsAny<CancellationToken>()))
            .ReturnsAsync(saved);

        var sut = CreateSut();
        var request = new PushAnnotationsRequest
        {
            DriveId = DriveId,
            DocumentSpeId = DocumentSpeId,
            TenantId = Tenant,
            IfMatch = LoadTimeETag,
            Annotations = ThreeMixedAnnotations(),
        };

        var result = await sut.PushAnnotationsAsync(request, new DefaultHttpContext());

        result.VersionId.Should().Be(NewVersionId);
        result.ETag.Should().Be("\"etag-v2\"");
        result.AnnotationCount.Should().Be(3);

        result.CompletionState.Should().NotBeNull();
        StateOf(result.CompletionState!, ComposeService.StepPush).Should().Be(JobAwareState.Completed);
        StateOf(result.CompletionState!, ComposeService.StepSave).Should().Be(JobAwareState.Completed);
        StateOf(result.CompletionState!, ComposeService.StepVersion).Should().Be(JobAwareState.Completed);
        result.CompletionState!.Aggregate.Should().Be(JobAwareState.Completed);
    }

    [Fact]
    public async Task PushAnnotationsAsync_HappyPath_ComputesCorrectPreviewCounts()
    {
        var source = CreateDocx("The quick brown fox jumps over the lazy dog.");
        ArrangeDownload(source);

        var saved = new FileHandleDto(
            Id: NewVersionId, Name: "draft.docx", ParentId: null, Size: 4321,
            CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
            ETag: "\"etag-v2\"", IsFolder: false, WebUrl: "https://spe/web", DriveId: DriveId);

        _spe.Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<Stream>(), LoadTimeETag, It.IsAny<CancellationToken>()))
            .ReturnsAsync(saved);

        var sut = CreateSut();
        var request = new PushAnnotationsRequest
        {
            DriveId = DriveId,
            DocumentSpeId = DocumentSpeId,
            TenantId = Tenant,
            IfMatch = LoadTimeETag,
            Annotations = ThreeMixedAnnotations(),
        };

        var result = await sut.PushAnnotationsAsync(request, new DefaultHttpContext());

        result.Preview.Should().NotBeNull();
        result.Preview!.CommentCount.Should().Be(1);
        result.Preview!.InsertionCount.Should().Be(1);
        result.Preview!.DeletionCount.Should().Be(1);
        result.Preview!.TrackChangeCount.Should().Be(2);
        result.Preview!.WordBoundCount.Should().Be(3);
        result.Preview!.ComposeOnlyCount.Should().Be(0, "no SessionId was supplied on this request");
    }

    // ── SessionId supplied: Compose-only count reflects DefinedTermsTracking ────────────────────
    [Fact]
    public async Task PushAnnotationsAsync_WithSessionId_ComposeOnlyCountReflectsDefinedTermsTracking()
    {
        var source = CreateDocx("The quick brown fox jumps over the lazy dog.");
        ArrangeDownload(source);

        var saved = new FileHandleDto(
            Id: NewVersionId, Name: "draft.docx", ParentId: null, Size: 4321,
            CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
            ETag: "\"etag-v2\"", IsFolder: false, WebUrl: "https://spe/web", DriveId: DriveId);

        _spe.Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<Stream>(), LoadTimeETag, It.IsAny<CancellationToken>()))
            .ReturnsAsync(saved);

        var sessionId = Guid.NewGuid().ToString();
        SeedSession(sessionId, definedTerms: new[]
        {
            new DefinedTerm { Term = "Confidential Information", Definition = "d1", Source = "ai" },
            new DefinedTerm { Term = "Effective Date", Definition = "d2", Source = "ai" },
        });

        var sut = CreateSut();
        var request = new PushAnnotationsRequest
        {
            DriveId = DriveId,
            DocumentSpeId = DocumentSpeId,
            TenantId = Tenant,
            IfMatch = LoadTimeETag,
            Annotations = ThreeMixedAnnotations(),
            SessionId = sessionId,
        };

        var result = await sut.PushAnnotationsAsync(request, new DefaultHttpContext());

        result.Preview!.ComposeOnlyCount.Should().Be(2, "the session has 2 DefinedTermsTracking entries with no Word-native representation");
    }

    // ── Acceptance #4 (negative): etag conflict aborts with NO partial write ───────────────────
    [Fact]
    public async Task PushAnnotationsAsync_WhenEtagConflict_ThrowsAndPersistsFailedSaveStep_NoPartialWrite()
    {
        var source = CreateDocx("The quick brown fox jumps over the lazy dog.");
        ArrangeDownload(source);

        _spe.Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<Stream>(), LoadTimeETag, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EtagPreconditionFailedException(DocumentSpeId, LoadTimeETag));

        var sut = CreateSut();
        var request = new PushAnnotationsRequest
        {
            DriveId = DriveId,
            DocumentSpeId = DocumentSpeId,
            TenantId = Tenant,
            IfMatch = LoadTimeETag,
            Annotations = ThreeMixedAnnotations(),
        };

        await Assert.ThrowsAsync<EtagPreconditionFailedException>(
            () => sut.PushAnnotationsAsync(request, new DefaultHttpContext()));

        // Verify the write was attempted exactly once (no retry, no silent double-write) and that
        // the per-step state persisted to Redis reflects "push landed, save failed, version never
        // started" — proving the abort point without inferring it from a caught exception alone.
        _spe.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<Stream>(), LoadTimeETag, It.IsAny<CancellationToken>()),
            Times.Once);

        var persisted = await new ComposePushSaveStatusStore(_cache).GetAsync(DocumentSpeId, CancellationToken.None);
        persisted.Should().NotBeNull();
        StateOf(persisted!, ComposeService.StepPush).Should().Be(JobAwareState.Completed);
        StateOf(persisted!, ComposeService.StepSave).Should().Be(JobAwareState.Failed);
        StateOf(persisted!, ComposeService.StepVersion).Should().Be(JobAwareState.Queued, "version was never attempted once save failed");
        persisted!.Aggregate.Should().Be(JobAwareState.Failed);
    }

    [Fact]
    public async Task PushAnnotationsAsync_WhenDocumentLockedByWord_ThrowsAndPersistsFailedSaveStep()
    {
        var source = CreateDocx("The quick brown fox jumps over the lazy dog.");
        ArrangeDownload(source);

        _spe.Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<Stream>(), LoadTimeETag, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DocumentLockedByWordException(DocumentSpeId));

        var sut = CreateSut();
        var request = new PushAnnotationsRequest
        {
            DriveId = DriveId,
            DocumentSpeId = DocumentSpeId,
            TenantId = Tenant,
            IfMatch = LoadTimeETag,
            Annotations = ThreeMixedAnnotations(),
        };

        await Assert.ThrowsAsync<DocumentLockedByWordException>(
            () => sut.PushAnnotationsAsync(request, new DefaultHttpContext()));

        var persisted = await new ComposePushSaveStatusStore(_cache).GetAsync(DocumentSpeId, CancellationToken.None);
        persisted.Should().NotBeNull();
        StateOf(persisted!, ComposeService.StepSave).Should().Be(JobAwareState.Failed);
        persisted!.Aggregate.Should().Be(JobAwareState.Failed);
    }

    // ── Push (annotate) failure: save is NEVER attempted (pipeline abort before any I/O) ────────
    [Fact]
    public async Task PushAnnotationsAsync_WhenAnnotationTargetNotFound_NeverAttemptsSave()
    {
        var source = CreateDocx("The quick brown fox jumps over the lazy dog.");
        ArrangeDownload(source);
        // _spe has NO setup for ReplaceFileContentAsUserAsync — Strict mock fails the test if it's called.

        var sut = CreateSut();
        var request = new PushAnnotationsRequest
        {
            DriveId = DriveId,
            DocumentSpeId = DocumentSpeId,
            TenantId = Tenant,
            IfMatch = LoadTimeETag,
            Annotations = new[]
            {
                new DocxAnnotation { Kind = TrackChangeKind.Deletion, TargetText = "this text does not exist in the document", Author = "AI", Date = When },
            },
        };

        await Assert.ThrowsAsync<DocxAnnotationException>(
            () => sut.PushAnnotationsAsync(request, new DefaultHttpContext()));

        _spe.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var persisted = await new ComposePushSaveStatusStore(_cache).GetAsync(DocumentSpeId, CancellationToken.None);
        persisted.Should().NotBeNull();
        StateOf(persisted!, ComposeService.StepPush).Should().Be(JobAwareState.Failed);
        StateOf(persisted!, ComposeService.StepSave).Should().Be(JobAwareState.Queued);
        persisted!.Aggregate.Should().Be(JobAwareState.Failed);
    }

    // ── PreviewPushAnnotationsAsync: pre-confirm, non-mutating ──────────────────────────────────
    [Fact]
    public async Task PreviewPushAnnotationsAsync_NeverTouchesSpe_AndReturnsMatchingCounts()
    {
        // No SPE setups at all — a Strict mock proves the preview path makes zero SPE calls.
        var sut = CreateSut();
        var request = new PreviewPushAnnotationsRequest
        {
            TenantId = Tenant,
            Annotations = ThreeMixedAnnotations(),
        };

        var preview = await sut.PreviewPushAnnotationsAsync(request);

        preview.CommentCount.Should().Be(1);
        preview.InsertionCount.Should().Be(1);
        preview.DeletionCount.Should().Be(1);
        preview.WordBoundCount.Should().Be(3);
        preview.ComposeOnlyCount.Should().Be(0);
    }
}
