// FR-01a (task 026, E1 born-in-editor) — unit test for ComposeService.SaveAsync's create-on-save
// orchestration when the request carries a ContentModel (a document BORN IN THE EDITOR — AI-drafted / blank /
// browse-local — with no retained original and no SPE drive-item yet). It verifies the ORCHESTRATION SaveAsync
// adds: it renders the .docx server-side via ComposeDocumentRenderer (NOT a client docx.js reconstruction) and
// persists THOSE bytes through the create-on-save path (captured at the SpeFileStore facade). The renderer's
// fidelity is covered by ComposeDocumentRendererTests; this pins that SaveAsync routes the born-in-editor case
// through the renderer and up to SPE.
//
// Mocking boundary (ADR-038 §4): ISpeFileOperations (SPE/Graph facade, ADR-007), IGenericEntityService
// (Dataverse), IPostUploadIndexingEnqueuer (RAG indexing seam), ChatSessionManager (Redis session store) — the
// SAME boundary set as the sibling ComposeServiceDeltaSaveTests. Real in-memory .docx (Open XML SDK); no
// transport mocks (B1), no DI/ctor tests (B3/B4). The through-the-wire seam proof rides task 024 (NFR-06).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class ComposeServiceBornInEditorSaveTests
{
    private const string Tenant = "tenant-aad-026";
    private const string ContainerId = "container-026";
    private const string ResolvedDriveId = "drive-026";
    private const string CreatedSpeItemId = "spe-created-026";

    // task 039 (born-in-editor 2nd save) — the doc is already promoted (DocumentSpeId + DriveId set).
    private const string ExistingSpeItemId = "spe-existing-039";
    private const string ExistingDriveId = "drive-039";

    private readonly Mock<ISpeFileOperations> _spe = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);
    private readonly Mock<IPostUploadIndexingEnqueuer> _indexing = new(MockBehavior.Strict);
    private readonly Mock<ChatSessionManager> _sessions;

    public ComposeServiceBornInEditorSaveTests()
    {
        _sessions = new Mock<ChatSessionManager>(
            Mock.Of<ITenantCache>(),
            Mock.Of<IChatDataverseRepository>(),
            NullLogger<ChatSessionManager>.Instance,
            null!,
            null!);
    }

    private ComposeService CreateSut() => new(
        _spe.Object,
        _sessions.Object,
        _dataverse.Object,        _indexing.Object,
        NullLogger<ComposeService>.Instance);

    private static FileHandleDto CreatedDriveItem() => new(
        Id: CreatedSpeItemId,
        Name: "draft.docx",
        ParentId: null,
        Size: 5555,
        CreatedDateTime: DateTimeOffset.UtcNow,
        LastModifiedDateTime: DateTimeOffset.UtcNow,
        ETag: "\"etag-created-026\"",
        IsFolder: false,
        WebUrl: "https://spe/web/draft",
        DriveId: ResolvedDriveId);

    private void ArrangeCreateOnSave(out Func<byte[]> capturedBytesAccessor)
    {
        byte[]? captured = null;

        _spe.Setup(s => s.ResolveDriveIdAsync(ContainerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResolvedDriveId);

        _spe.Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), ResolvedDriveId, It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, CancellationToken>((_, _, _, stream, _) =>
            {
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                captured = buffer.ToArray();
            })
            .ReturnsAsync(CreatedDriveItem());

        // FR-06 idempotent promotion: an existing row is found → no create needed (keeps the strict-mock
        // arrangement minimal; the promotion idempotency itself is covered elsewhere).
        _dataverse.Setup(d => d.RetrieveByAlternateKeyAsync(
                "sprk_document", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document") { Id = Guid.NewGuid() });

        _indexing.Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        capturedBytesAccessor = () => captured
            ?? throw new InvalidOperationException("UploadSmallAsUserAsync was never invoked.");
    }

    [Fact]
    public async Task SaveAsync_WithContentModelAndNoSpeItem_PersistsServerRenderedBytesViaCreateOnSave()
    {
        ArrangeCreateOnSave(out var capturedBytes);
        var sut = CreateSut();

        var request = new SaveComposeDocumentRequest
        {
            // Born-in-editor first Save: NO DocumentSpeId, NO Content bytes, NO BaselineVersionId.
            ContainerId = ContainerId,
            ContentModel = new ComposeContentModel
            {
                Blocks = new[]
                {
                    new ComposeBlock
                    {
                        Kind = ComposeBlockKind.Heading, Level = 1,
                        Runs = new[] { new ComposeInlineRun { Text = "Definitions" } },
                    },
                    new ComposeBlock
                    {
                        Kind = ComposeBlockKind.Heading, Level = 2,
                        Runs = new[] { new ComposeInlineRun { Text = "Confidential Information" } },
                    },
                    new ComposeBlock
                    {
                        Kind = ComposeBlockKind.Paragraph,
                        Runs = new[] { new ComposeInlineRun { Text = "The parties agree as follows." } },
                    },
                },
            },
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        var persisted = capturedBytes();

        // The persisted bytes are a real, server-authored .docx (opens cleanly) with the fidelity the
        // renderer produces — style-linked numbering + real Heading styles — NOT a flattened client rebuild.
        using var doc = WordprocessingDocument.Open(new MemoryStream(persisted, writable: false), isEditable: false);
        var body = doc.MainDocumentPart!.Document!.Body!;

        doc.MainDocumentPart.NumberingDefinitionsPart.Should().NotBeNull(
            "the born-in-editor render authors a NumberingDefinitionsPart (the client docx.js path never did)");
        doc.MainDocumentPart.StyleDefinitionsPart!.Styles!.Elements<Style>()
            .Should().Contain(s => s.StyleId == "Heading1" && s.StyleName!.Val!.Value == "heading 1");

        var headings = body.Descendants<Paragraph>()
            .Where(p => p.GetFirstChild<ParagraphProperties>()?.GetFirstChild<ParagraphStyleId>()?.Val?.Value?.StartsWith("Heading") == true)
            .ToList();
        headings.Should().HaveCount(2);
        headings.Should().OnlyContain(p => p.GetFirstChild<ParagraphProperties>()!.GetFirstChild<NumberingProperties>() == null,
            "clause headings are numbered through the style — no direct paragraph numId (FR-27)");

        body.Descendants<Paragraph>().Select(p => p.ParagraphId?.Value)
            .Should().OnlyContain(id => !string.IsNullOrEmpty(id), "every rendered w:p carries a w14:paraId (E2 substrate)");
    }

    private static FileHandleDto ReplacedDriveItem() => new(
        Id: ExistingSpeItemId,
        Name: "draft.docx",
        ParentId: null,
        Size: 6789,
        CreatedDateTime: DateTimeOffset.UtcNow,
        LastModifiedDateTime: DateTimeOffset.UtcNow,
        ETag: "\"etag-replaced-039\"",
        IsFolder: false,
        WebUrl: "https://spe/web/draft",
        DriveId: ExistingDriveId);

    private void ArrangeReplaceOnSave(out Func<byte[]> capturedBytesAccessor)
    {
        byte[]? captured = null;

        // The replace path reads the live metadata for the FR-08 pre-write staleness assert. Returning null
        // keeps preWriteETag null (no prior stamp to assert against); the ContentModel branch skips the
        // stamp block regardless — so this is the only metadata read on this path.
        _spe.Setup(s => s.GetFileMetadataAsUserAsync(
                It.IsAny<HttpContext>(), ExistingDriveId, ExistingSpeItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileHandleDto?)null);

        // The born-in-editor replace save overwrites the EXISTING drive-item in place (never a create).
        _spe.Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), ExistingDriveId, ExistingSpeItemId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, CancellationToken>((_, _, _, stream, _) =>
            {
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                captured = buffer.ToArray();
            })
            .ReturnsAsync(ReplacedDriveItem());

        // FR-06 idempotent promotion: the row already exists (created on the FIRST save) → no create.
        _dataverse.Setup(d => d.RetrieveByAlternateKeyAsync(
                "sprk_document", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document") { Id = Guid.NewGuid() });

        _indexing.Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        capturedBytesAccessor = () => captured
            ?? throw new InvalidOperationException("ReplaceFileContentAsUserAsync was never invoked.");
    }

    [Fact]
    public async Task SaveAsync_WithContentModelOnExistingItem_UpdatesSameItemAndDoesNotCreate()
    {
        // task 039 (UAT round 1+2, born-in-editor 2nd save): the doc was promoted on its FIRST save
        // (DocumentSpeId now set) but holds NO retained original bytes and no real SPE baseline version.
        // The 2nd..Nth in-session save re-authors via ContentModel on the REPLACE path — it must overwrite
        // the SAME drive-item (update, not a create-on-save) so no duplicate sprk_document is minted per
        // save, and it must NOT 400 on a missing baseline.
        ArrangeReplaceOnSave(out var capturedBytes);
        var sut = CreateSut();

        var request = new SaveComposeDocumentRequest
        {
            // Second save of a born-in-editor doc: DocumentSpeId + DriveId are now set (rebound from the
            // first create-on-save); ContentModel re-authors the whole doc; NO Content / BaselineVersionId /
            // OperationLog (there is nothing to diff onto).
            DocumentSpeId = ExistingSpeItemId,
            DriveId = ExistingDriveId,
            ContentModel = new ComposeContentModel
            {
                Blocks = new[]
                {
                    new ComposeBlock
                    {
                        Kind = ComposeBlockKind.Heading, Level = 1,
                        Runs = new[] { new ComposeInlineRun { Text = "Definitions" } },
                    },
                    new ComposeBlock
                    {
                        Kind = ComposeBlockKind.Paragraph,
                        Runs = new[] { new ComposeInlineRun { Text = "Second in-session save." } },
                    },
                },
            },
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        var result = await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        // (a) It UPDATED the SAME drive-item via replace — never a create-on-save upload (no duplicate record).
        _spe.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), ExistingDriveId, ExistingSpeItemId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _spe.Verify(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _spe.Verify(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);

        // (b) The result points at the SAME existing SPE item (same-record update).
        result.DocumentSpeId.Should().Be(ExistingSpeItemId);

        // (c) The persisted bytes are a real server-authored .docx rendered FROM the content model (the
        // renderer path, not an op-log patch onto a baseline), so the 2nd save succeeds with no baseline error.
        var persisted = capturedBytes();
        using var doc = WordprocessingDocument.Open(new MemoryStream(persisted, writable: false), isEditable: false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        body.InnerText.Should().Contain("Second in-session save.");
        body.Descendants<Paragraph>().Select(p => p.ParagraphId?.Value)
            .Should().OnlyContain(id => !string.IsNullOrEmpty(id), "every rendered w:p carries a w14:paraId");
    }
}
