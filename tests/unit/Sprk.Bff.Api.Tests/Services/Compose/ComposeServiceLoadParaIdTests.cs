// FR-08 / NFR-06 (task 010, E2) — wiring proof that ComposeService.LoadAsync invokes the
// ParaIdPreParser and PROJECTS the complete ordered w14:paraId map onto LoadComposeDocumentResult
// for a real formatted document (incl. a table-cell paragraph). This is the service-seam assertion
// for FR-08's "the Load response carries one id per body paragraph"; the through-the-wire HTTP seam
// rides task 024 (the Compose endpoints seam suite) — LoadAsync's Graph hop is behind the
// ISpeFileOperations facade, which is the correct module boundary to mock here (ADR-038 §4), NOT a
// transport mock (B1).
//
// Mocking boundary: ISpeFileOperations (SPE/Graph facade, ADR-007) + ChatSessionManager backed by an
// in-memory store — same harness as CrossVersionSessionPersistenceTests. A REAL .docx (Open XML SDK)
// flows through DownloadFileAsUserAsync so the pre-parse runs against genuine WordprocessingML.

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

public sealed class ComposeServiceLoadParaIdTests
{
    private const string Tenant = "tenant-aad-010";
    private const string DocumentSpeId = "spe-item-010";
    private const string DriveId = "drive-010";

    private readonly Mock<ISpeFileOperations> _spe = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);
    private readonly Mock<IPostUploadIndexingEnqueuer> _indexing = new(MockBehavior.Strict);
    private readonly Mock<ITenantCache> _cache = new(MockBehavior.Loose);
    private readonly Mock<ChatSessionManager> _sessions;
    private readonly Dictionary<string, ChatSession> _store = new(StringComparer.Ordinal);

    public ComposeServiceLoadParaIdTests()
    {
        // FR-06 (task 027): LoadAsync resolves the load-time version id best-effort. A class-wide default
        // keeps the strict SPE mock happy for every test; the formatted-doc test asserts it is surfaced.
        _spe.Setup(s => s.GetCurrentVersionIdAsUserAsync(It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("v-load-1");

        // CreateSessionAsync is not virtual — the real method runs; its cache write lands in _store so
        // GetSessionAsync stays consistent (mirrors CrossVersionSessionPersistenceTests).
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
            .Setup(s => s.UpdateSessionCacheAsync(It.IsAny<ChatSession>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Returns((ChatSession session, CancellationToken _, bool __) =>
            {
                _store[session.SessionId] = session;
                return Task.CompletedTask;
            });
    }

    private ComposeService CreateSut() => new(
        _spe.Object,
        _sessions.Object,
        _dataverse.Object,        _indexing.Object,
        NullLogger<ComposeService>.Instance);

    // 3 body paragraphs: one with an existing id, one without, one inside a table cell.
    private static byte[] FormattedDocx()
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var withId = new Paragraph(new Run(new Text("has id"))) { ParagraphId = new HexBinaryValue("AAAA0001") };
            var body = new Body(
                withId,
                new Paragraph(new Run(new Text("no id"))),
                new Table(new TableRow(new TableCell(new Paragraph(new Run(new Text("table cell")))))));
            main.Document = new Document(body);
            main.Document.Save();
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task LoadAsync_ForFormattedDocument_ProjectsCompleteParaIdMapOntoResponse()
    {
        var docx = FormattedDocx();
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

        var sut = CreateSut();

        var result = await sut.LoadAsync(
            new LoadComposeDocumentRequest { DriveId = DriveId, DocumentSpeId = DocumentSpeId, TenantId = Tenant },
            new DefaultHttpContext(),
            CancellationToken.None);

        result.ParaIdMap.Should().HaveCount(3, "one id per body paragraph incl. the table-cell paragraph (FR-08)");
        result.ParaIdMap.Select(e => e.ParaId).Should().OnlyHaveUniqueItems();
        result.ParaIdMap[0].ParaId.Should().Be("AAAA0001", "an existing paraId is collected verbatim");
        result.ParaIdMap[0].IsMinted.Should().BeFalse();
        // FR-01 (task 010, ingest): ComposeBaselineParaIdStamper.MintAndPersist now mints + PHYSICALLY
        // WRITES ids into `content` BEFORE the projection builder runs (see
        // LoadAsync_ForFormattedDocument_PersistsMintedParaIdsIntoTheReturnedContentBytes for the
        // durability proof). By the time the single-authority projection builder walks the body, those
        // paragraphs already carry an id — so its own IsMinted flag reads false for them too (it is
        // reporting "did *I* just mint this", not "was this paragraph originally id-less"). The
        // load-bearing invariant this task adds is that every paragraph resolves to a non-empty, unique
        // id that is PERSISTED in the retained bytes — which the id-uniqueness assertion above already
        // covers alongside the byte-level proof in the companion test.
        result.ParaIdMap.Skip(1).Should().OnlyContain(e => !e.IsMinted,
            "ingest-time persistence (task 010) means the projection builder finds these ids already physically present");
        result.VersionId.Should().Be("v-load-1",
            "the load-time SPE version id is surfaced so a later dirty save can re-fetch this baseline (FR-06)");
    }

    [Fact]
    public async Task LoadAsync_ForFormattedDocument_PersistsMintedParaIdsIntoTheReturnedContentBytes()
    {
        // FR-01 (task 010, ingest): the whole point of persisting on ingest is that the id lives in the
        // RETAINED PACKAGE BYTES returned as Content — not only in the ParaIdMap projection — because
        // Content is what SaveAsync's same-session fast path echoes back as the baseline
        // (ResolveSaveBaselineAsync). If minting were only in the map, this would fail.
        var docx = FormattedDocx();
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

        var sut = CreateSut();

        var result = await sut.LoadAsync(
            new LoadComposeDocumentRequest { DriveId = DriveId, DocumentSpeId = DocumentSpeId, TenantId = Tenant },
            new DefaultHttpContext(),
            CancellationToken.None);

        using var ms = new MemoryStream(result.Content.ToArray());
        using var openedDoc = WordprocessingDocument.Open(ms, isEditable: false);
        var idsInReturnedBytes = openedDoc.MainDocumentPart!.Document!.Body!
            .Descendants<Paragraph>()
            .Select(p => p.ParagraphId?.Value)
            .ToList();

        idsInReturnedBytes.Should().HaveCount(3);
        idsInReturnedBytes.Should().OnlyContain(id => !string.IsNullOrEmpty(id),
            "every editable paragraph's paraId must be PHYSICALLY present in the Content bytes Load returns, not only in ParaIdMap");
        idsInReturnedBytes.Should().OnlyHaveUniqueItems();
        idsInReturnedBytes.Should().BeEquivalentTo(
            result.ParaIdMap.Select(e => e.ParaId),
            "the projection's paraId map stays consistent with what is now physically persisted in the retained bytes");
    }

    [Fact]
    public async Task LoadAsync_WhenSourceIsNotAReadableDocx_ReturnsEmptyMapAndStillSucceeds()
    {
        // Best-effort contract: a malformed source must NOT fail Load (it historically did no
        // server-side parse). The client degrades to fuzzy-only anchoring (task 012).
        var notADocx = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        _spe.Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: DocumentSpeId, Name: "broken.docx", ParentId: null, Size: notADocx.Length,
                CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"etag-v1\"", IsFolder: false, WebUrl: "https://spe/web", DriveId: DriveId));
        _spe.Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), DriveId, DocumentSpeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(notADocx));

        var sut = CreateSut();

        var result = await sut.LoadAsync(
            new LoadComposeDocumentRequest { DriveId = DriveId, DocumentSpeId = DocumentSpeId, TenantId = Tenant },
            new DefaultHttpContext(),
            CancellationToken.None);

        result.ParaIdMap.Should().BeEmpty();
        result.Content.Length.Should().Be(notADocx.Length, "Load still returns the content bytes");
    }
}
