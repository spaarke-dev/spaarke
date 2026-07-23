// FR-25 (task 051, import round-trip) — wiring proof that ComposeService.LoadAsync runs the EXISTING
// DocxAnnotationReader on the load-time bytes (alongside the paraId pre-parse + the client mammoth
// convert) and PROJECTS each recovered native w:comment onto LoadComposeDocumentResult.ImportedComments
// with the E2 w14:paraId of its containing paragraph. This is the service-seam assertion for FR-25's
// Load-response projection; the through-the-wire imported-comments-survive-save slice is task 052's
// charter (mirrors ComposeServiceLoadImportedRevisionsTests, task 050, exactly).
//
// Mocking boundary (ADR-038 §4): ISpeFileOperations (the SPE/Graph facade, ADR-007) + a ChatSessionManager
// backed by an in-memory store — the SAME harness as ComposeServiceLoadImportedRevisionsTests. A REAL
// Word-commented .docx is built with the Open XML SDK via DocxAnnotationWriter (the writer produces the
// exact w:comment + CommentRangeStart/End markup a Word-for-Web session emits) and flows through
// DownloadFileAsUserAsync, so the reader runs against genuine WordprocessingML. NO Mock<HttpMessageHandler>,
// no DI-registration/ctor-null tests.

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

public sealed class ComposeServiceLoadImportedCommentsTests
{
    private const string Tenant = "tenant-aad-051";
    private const string DocumentSpeId = "spe-item-051";
    private const string DriveId = "drive-051";
    private static readonly DateTime When = new(2026, 7, 19, 10, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ISpeFileOperations> _spe = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);
    private readonly Mock<IPostUploadIndexingEnqueuer> _indexing = new(MockBehavior.Strict);
    private readonly Mock<ITenantCache> _cache = new(MockBehavior.Loose);
    private readonly Mock<ChatSessionManager> _sessions;
    private readonly Dictionary<string, ChatSession> _store = new(StringComparer.Ordinal);

    public ComposeServiceLoadImportedCommentsTests()
    {
        _spe.Setup(s => s.GetCurrentVersionIdAsUserAsync(It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("v-load-1");

        _cache
            .Setup(c => c.SetSlidingAsync<ChatSession>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<ChatSession>(), It.IsAny<TimeSpan>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, int, ChatSession, TimeSpan, string, CancellationToken>(
                (_, _, _, _, session, _, _, _) => _store[session.SessionId] = session)
            .Returns(Task.CompletedTask);

        _sessions = new Mock<ChatSessionManager>(
            _cache.Object,
            Mock.Of<IChatDataverseRepository>(),
            NullLogger<ChatSessionManager>.Instance,
            null!,
            null!);

        _sessions
            .Setup(s => s.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string tenantId, string sessionId, CancellationToken _) =>
                _store.TryGetValue(sessionId, out var session) && session.TenantId == tenantId ? session : null);

        _sessions
            .Setup(s => s.UpdateSessionCacheAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>()))
            .Returns((ChatSession session, CancellationToken _) =>
            {
                _store[session.SessionId] = session;
                return Task.CompletedTask;
            });
    }

    private ComposeService CreateSut() => new(
        _spe.Object,
        _sessions.Object,
        _dataverse.Object,
        new DocxAnnotationWriter(),
        _indexing.Object,
        NullLogger<ComposeService>.Instance);

    private void SetupSpeReturns(byte[] docx)
    {
        _spe.Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: DocumentSpeId,
                Name: "contract.docx",
                ParentId: null,
                Size: docx.Length,
                CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"etag-v1\"",
                IsFolder: false,
                WebUrl: "https://spe/web",
                DriveId: DriveId));
        _spe.Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(docx));
    }

    private static byte[] CreateDocx(params string[] paragraphs)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
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

    /// <summary>A two-paragraph doc with TWO Word comments anchored to the SAME span in paragraph 0 (the
    /// legacy multi-comment-on-one-span shape a non-modern-comments `.docx` uses to represent a "reply" —
    /// see DocxAnnotationWriter/Reader) plus a THIRD comment anchored to paragraph 1.</summary>
    private static byte[] WordCommentedDocx()
    {
        var source = CreateDocx("The quick brown fox.", "The lazy dog sleeps.");
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Comment, TargetText = "quick brown fox", CommentText = "Define this term.", Author = "Jordan Ellis", Date = When },
            new DocxAnnotation { Kind = TrackChangeKind.Comment, TargetText = "quick brown fox", CommentText = "Agreed — see defined terms.", Author = "Sam Rivera", Date = When.AddMinutes(5) },
            new DocxAnnotation { Kind = TrackChangeKind.Comment, TargetText = "lazy dog", CommentText = "Cut this sentence.", Author = "Jordan Ellis", Date = When.AddMinutes(10) },
        };
        return new DocxAnnotationWriter().Annotate(source, annotations);
    }

    [Fact]
    public async Task LoadAsync_ForWordCommentedDocument_ProjectsRecoveredCommentsWithParaId()
    {
        SetupSpeReturns(WordCommentedDocx());
        var sut = CreateSut();

        var result = await sut.LoadAsync(
            new LoadComposeDocumentRequest { DriveId = DriveId, DocumentSpeId = DocumentSpeId, TenantId = Tenant },
            new DefaultHttpContext(),
            CancellationToken.None);

        result.ImportedComments.Should().HaveCount(3, "the doc carries three w:comment elements (FR-25)");

        var para0Comments = result.ImportedComments.Where(c => c.ParagraphHint == 0).OrderBy(c => c.Date).ToList();
        para0Comments.Should().HaveCount(2, "two comments anchor to paragraph 0's span");
        para0Comments[0].Author.Should().Be("Jordan Ellis");
        para0Comments[0].CommentText.Should().Be("Define this term.");
        para0Comments[1].Author.Should().Be("Sam Rivera");
        para0Comments[1].CommentText.Should().Be("Agreed — see defined terms.");

        var para1Comment = result.ImportedComments.Should().ContainSingle(c => c.ParagraphHint == 1).Subject;
        para1Comment.Author.Should().Be("Jordan Ellis");
        para1Comment.CommentText.Should().Be("Cut this sentence.");
        para1Comment.Date.UtcDateTime.Should().Be(When.AddMinutes(10));

        // Each projected comment carries the E2 paraId of its paragraph — the SAME id the paraId map
        // surfaces for that document-order index (the primary client anchor), mirroring ImportedRevision.
        foreach (var comment in para0Comments)
        {
            comment.ParaId.Should().NotBeNullOrEmpty()
                .And.Be(result.ParaIdMap.Single(e => e.Index == 0).ParaId, "paragraph-0 comments anchor to paragraph 0's w14:paraId");
        }
        para1Comment.ParaId.Should().NotBeNullOrEmpty()
            .And.Be(result.ParaIdMap.Single(e => e.Index == 1).ParaId, "the paragraph-1 comment anchors to paragraph 1's w14:paraId");
    }

    [Fact]
    public async Task LoadAsync_ForDocumentWithNoComments_ReturnsEmptyImportedCommentsNotNull()
    {
        SetupSpeReturns(CreateDocx("A plain paragraph with no comments."));
        var sut = CreateSut();

        var result = await sut.LoadAsync(
            new LoadComposeDocumentRequest { DriveId = DriveId, DocumentSpeId = DocumentSpeId, TenantId = Tenant },
            new DefaultHttpContext(),
            CancellationToken.None);

        result.ImportedComments.Should().NotBeNull().And.BeEmpty(
            "a document with no w:comment yields an EMPTY list, never null (FR-25 acceptance)");
    }

    [Fact]
    public async Task LoadAsync_WhenSourceIsNotAReadableDocx_ReturnsEmptyImportedCommentsAndStillSucceeds()
    {
        // Best-effort: a malformed source must NOT fail Load — imported comments degrade to empty, the
        // content bytes still return (matches the sibling paraId pre-parse + imported-revisions contract).
        var notADocx = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        SetupSpeReturns(notADocx);
        var sut = CreateSut();

        var result = await sut.LoadAsync(
            new LoadComposeDocumentRequest { DriveId = DriveId, DocumentSpeId = DocumentSpeId, TenantId = Tenant },
            new DefaultHttpContext(),
            CancellationToken.None);

        result.ImportedComments.Should().NotBeNull().And.BeEmpty();
        result.ImportedRevisions.Should().NotBeNull().And.BeEmpty();
        result.Content.Length.Should().Be(notADocx.Length, "Load still returns the content bytes");
    }

    [Fact]
    public async Task LoadAsync_ForDocumentWithBothCommentsAndRevisions_ProjectsBothFromOneReaderPass()
    {
        // FR-24 + FR-25 co-exist on the SAME single DocxAnnotationReader.Read() call (NFR-08) — this proves
        // the task-051 refactor did not regress the task-050 revisions projection.
        var source = CreateDocx("The quick brown fox.", "The lazy dog sleeps.");
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Insertion, TargetText = "fox", NewText = " (Vulpes vulpes)", Author = "Jordan Ellis", Date = When },
            new DocxAnnotation { Kind = TrackChangeKind.Comment, TargetText = "lazy dog", CommentText = "Cut this sentence.", Author = "Sam Rivera", Date = When },
        };
        SetupSpeReturns(new DocxAnnotationWriter().Annotate(source, annotations));
        var sut = CreateSut();

        var result = await sut.LoadAsync(
            new LoadComposeDocumentRequest { DriveId = DriveId, DocumentSpeId = DocumentSpeId, TenantId = Tenant },
            new DefaultHttpContext(),
            CancellationToken.None);

        result.ImportedRevisions.Should().ContainSingle(r => r.Kind == RecoveredAnnotationKind.Insertion);
        result.ImportedComments.Should().ContainSingle(c => c.CommentText == "Cut this sentence.");
    }
}
