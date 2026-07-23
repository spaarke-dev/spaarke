// FR-24 (task 050, import round-trip) — wiring proof that ComposeService.LoadAsync runs the EXISTING
// DocxAnnotationReader on the load-time bytes (alongside the paraId pre-parse + the client mammoth
// convert) and PROJECTS each recovered native w:ins/w:del onto LoadComposeDocumentResult.ImportedRevisions
// with the E2 w14:paraId of its containing paragraph. This is the service-seam assertion for FR-24's
// Load-response projection; the through-the-wire imported-marks-survive-save slice is task 052's charter.
//
// Mocking boundary (ADR-038 §4): ISpeFileOperations (the SPE/Graph facade, ADR-007) + a ChatSessionManager
// backed by an in-memory store — the SAME harness as ComposeServiceLoadParaIdTests. A REAL Word-redlined
// .docx (built with the Open XML SDK via DocxAnnotationWriter — the writer produces the exact w:ins/w:del
// markup a Word-for-Web session emits) flows through DownloadFileAsUserAsync, so the reader runs against
// genuine WordprocessingML. NO Mock<HttpMessageHandler>, no DI-registration/ctor-null tests.

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

public sealed class ComposeServiceLoadImportedRevisionsTests
{
    private const string Tenant = "tenant-aad-050";
    private const string DocumentSpeId = "spe-item-050";
    private const string DriveId = "drive-050";
    private static readonly DateTime When = new(2026, 7, 19, 9, 30, 0, DateTimeKind.Utc);

    private readonly Mock<ISpeFileOperations> _spe = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);
    private readonly Mock<IPostUploadIndexingEnqueuer> _indexing = new(MockBehavior.Strict);
    private readonly Mock<ITenantCache> _cache = new(MockBehavior.Loose);
    private readonly Mock<ChatSessionManager> _sessions;
    private readonly Dictionary<string, ChatSession> _store = new(StringComparer.Ordinal);

    public ComposeServiceLoadImportedRevisionsTests()
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

    /// <summary>A two-paragraph doc redlined in Word shape (via the writer, which emits the exact w:ins/w:del
    /// markup): an insertion in paragraph 0 + a deletion in paragraph 1, authored by a real human.</summary>
    private static byte[] WordRedlinedDocx()
    {
        var source = CreateDocx("The quick brown fox.", "The lazy dog sleeps.");
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Insertion, TargetText = "fox", NewText = " (Vulpes vulpes)", Author = "Jordan Ellis", Date = When },
            new DocxAnnotation { Kind = TrackChangeKind.Deletion, TargetText = "lazy ", Author = "Jordan Ellis", Date = When },
        };
        return new DocxAnnotationWriter().Annotate(source, annotations);
    }

    [Fact]
    public async Task LoadAsync_ForWordRedlinedDocument_ProjectsRecoveredRevisionsWithParaId()
    {
        SetupSpeReturns(WordRedlinedDocx());
        var sut = CreateSut();

        var result = await sut.LoadAsync(
            new LoadComposeDocumentRequest { DriveId = DriveId, DocumentSpeId = DocumentSpeId, TenantId = Tenant },
            new DefaultHttpContext(),
            CancellationToken.None);

        result.ImportedRevisions.Should().HaveCount(2, "the doc carries one w:ins and one w:del (FR-24)");

        var insertion = result.ImportedRevisions.Should()
            .ContainSingle(r => r.Kind == RecoveredAnnotationKind.Insertion).Subject;
        insertion.Author.Should().Be("Jordan Ellis", "author fidelity is preserved by the reader");
        insertion.Date.UtcDateTime.Should().Be(When);
        insertion.Text.Should().Be(" (Vulpes vulpes)");
        insertion.ParagraphHint.Should().Be(0);

        var deletion = result.ImportedRevisions.Should()
            .ContainSingle(r => r.Kind == RecoveredAnnotationKind.Deletion).Subject;
        deletion.Author.Should().Be("Jordan Ellis");
        deletion.Date.UtcDateTime.Should().Be(When);
        deletion.Text.Should().Be("lazy ");
        deletion.ParagraphHint.Should().Be(1);

        // Each projected revision carries the E2 paraId of its paragraph — the SAME id the paraId map
        // surfaces for that document-order index (the primary client anchor).
        insertion.ParaId.Should().NotBeNullOrEmpty()
            .And.Be(result.ParaIdMap.Single(e => e.Index == 0).ParaId, "the insertion anchors to paragraph 0's w14:paraId");
        deletion.ParaId.Should().NotBeNullOrEmpty()
            .And.Be(result.ParaIdMap.Single(e => e.Index == 1).ParaId, "the deletion anchors to paragraph 1's w14:paraId");
    }

    [Fact]
    public async Task LoadAsync_ForDocumentWithNoRevisions_ReturnsEmptyImportedRevisionsNotNull()
    {
        SetupSpeReturns(CreateDocx("A plain paragraph with no tracked changes."));
        var sut = CreateSut();

        var result = await sut.LoadAsync(
            new LoadComposeDocumentRequest { DriveId = DriveId, DocumentSpeId = DocumentSpeId, TenantId = Tenant },
            new DefaultHttpContext(),
            CancellationToken.None);

        result.ImportedRevisions.Should().NotBeNull().And.BeEmpty(
            "a document with no w:ins/w:del yields an EMPTY list, never null (FR-24 acceptance)");
    }

    [Fact]
    public async Task LoadAsync_WhenSourceIsNotAReadableDocx_ReturnsEmptyImportedRevisionsAndStillSucceeds()
    {
        // Best-effort: a malformed source must NOT fail Load — imported revisions degrade to empty, the
        // content bytes still return (matches the sibling paraId pre-parse contract).
        var notADocx = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        SetupSpeReturns(notADocx);
        var sut = CreateSut();

        var result = await sut.LoadAsync(
            new LoadComposeDocumentRequest { DriveId = DriveId, DocumentSpeId = DocumentSpeId, TenantId = Tenant },
            new DefaultHttpContext(),
            CancellationToken.None);

        result.ImportedRevisions.Should().NotBeNull().And.BeEmpty();
        result.Content.Length.Should().Be(notADocx.Length, "Load still returns the content bytes");
    }
}
