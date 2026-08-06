// Task 010 (spaarkeai-compose-r6, FR-01/FR-02) — THE CUTOVER: ComposeService.SaveAsync routes an
// IMPORTED save (ContentModel + a resolvable retained baseline) through render-from-model
// (ComposeDocumentRenderer.RenderIntoCarrier) instead of the surgical patch path. The anchor-
// reconciliation 422 class dies by construction on this path: the ComposeBaselineParaIdStamper and the
// ComposeShadowPatchEngine are never invoked (both retained in the codebase for the transitional
// op-log clean-apply path the ADR-049 Path-B amendment permits).
//
// Deviation from the POML (directional steps, documented): the POML — authored before task 011 — says
// "via SynthesizeDocument"; the imported path routes through RenderIntoCarrier (011's deliverable),
// which preserves the carrier's styles/numbering/headers/footers/comments parts that a blank-package
// synthesize would drop (the UAT #1A SEV-1 class). SynthesizeDocument remains the born-in-editor path.
//
// Mocking boundary (ADR-038 §4, same set as the sibling ComposeServiceBornInEditorSaveTests):
// ISpeFileOperations (SPE facade, ADR-007), IGenericEntityService, IPostUploadIndexingEnqueuer,
// ChatSessionManager. Real in-memory .docx bytes (Open XML SDK) + the REAL renderer/projector.
// No Mock<HttpMessageHandler> (B1), no DI-registration (B3), no ctor-null (B4).

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
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Seam.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class ComposeServiceImportedRenderSaveTests
{
    private const string Tenant = "tenant-aad-010";
    private const string ExistingDriveId = "drive-existing-010";
    private const string ExistingSpeItemId = "spe-existing-010";
    private const string LoadTimeVersionId = "3.0";
    private const string CarrierMarkerStyleId = "CarrierMarker010";

    private readonly Mock<ISpeFileOperations> _spe = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);
    private readonly Mock<IPostUploadIndexingEnqueuer> _indexing = new(MockBehavior.Strict);
    private readonly Mock<ChatSessionManager> _sessions;

    public ComposeServiceImportedRenderSaveTests()
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
        _indexing.Object,
        NullLogger<ComposeService>.Instance);

    /// <summary>A retained-original carrier whose STYLES PART carries a distinctive custom style —
    /// the oracle that the save rendered INTO the carrier (parts preserved) rather than synthesizing a
    /// blank package (parts dropped) or persisting the baseline unchanged.</summary>
    private static byte[] BuildCarrierBytes()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var styles = main.AddNewPart<StyleDefinitionsPart>();
            styles.Styles = new Styles(
                new Style(new StyleName { Val = "Carrier Marker 010" })
                { Type = StyleValues.Paragraph, StyleId = CarrierMarkerStyleId });
            styles.Styles.Save();

            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text("Original imported prose."))),
                new SectionProperties(new PageSize { Width = 12240, Height = 15840 })));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static ComposeContentModel EditedModel(params ComposeInlineRun[] runs) => new()
    {
        Blocks = new[]
        {
            new ComposeBlock
            {
                Kind = ComposeBlockKind.Paragraph,
                Runs = runs.Length > 0 ? runs : new[] { new ComposeInlineRun { Text = "Edited body text." } },
            },
        },
    };

    private static FileHandleDto ReplacedDriveItem() => new(
        Id: ExistingSpeItemId,
        Name: "imported.docx",
        ParentId: null,
        Size: 7777,
        CreatedDateTime: DateTimeOffset.UtcNow,
        LastModifiedDateTime: DateTimeOffset.UtcNow,
        ETag: "\"etag-replaced-010\"",
        IsFolder: false,
        WebUrl: "https://spe/web/imported",
        DriveId: ExistingDriveId);

    private void ArrangeReplaceExisting(out Func<byte[]> capturedBytesAccessor)
    {
        _spe.Setup(s => s.GetFileMetadataAsUserAsync(
                It.IsAny<HttpContext>(), ExistingDriveId, ExistingSpeItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileHandleDto?)null);

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

        _dataverse.Setup(d => d.RetrieveByAlternateKeyAsync(
                "sprk_document", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document") { Id = Guid.NewGuid() });

        _indexing.Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        capturedBytesAccessor = () => captured
            ?? throw new InvalidOperationException("ReplaceFileContentAsUserAsync was never invoked.");
    }

    private static SaveComposeDocumentRequest ReplaceRequest(
        ComposeContentModel model,
        ReadOnlyMemory<byte> content = default,
        string? baselineVersionId = null) => new()
    {
        DocumentSpeId = ExistingSpeItemId,
        DriveId = ExistingDriveId,
        Content = content,
        BaselineVersionId = baselineVersionId,
        ContentModel = model,
        SessionId = Guid.NewGuid().ToString(),
        TenantId = Tenant,
    };

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1. The cutover: ContentModel + retained bytes → RenderIntoCarrier. The persisted bytes carry the
    //    model's edit AND the carrier's preserved styles part; the old body is re-rendered (not
    //    patched); origin resolves Imported.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SaveAsync_ContentModelWithRetainedBytes_RendersIntoCarrier_OriginImported()
    {
        ArrangeReplaceExisting(out var capturedBytes);
        var sut = CreateSut();
        var carrier = BuildCarrierBytes();

        // A ParaIdMap rides along (the transitional-path shape) — the render path must IGNORE it:
        // the stamper (whose count-gate chain was the 422 root) is not part of this path.
        var request = ReplaceRequest(EditedModel(), content: carrier);
        request = request with
        {
            ParaIdMap = new List<ComposeBaselineParaId> { new(Index: 0, ParaId: "7B00AA01", Text: null) },
        };

        var result = await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        result.Origin.Should().Be(ComposeOrigin.Imported, "a save with a baseline source is Imported even with a ContentModel");
        result.VersionId.Should().NotBeNullOrEmpty();

        var persisted = capturedBytes();
        using var doc = WordprocessingDocument.Open(new MemoryStream(persisted, writable: false), isEditable: false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        body.InnerText.Should().Contain("Edited body text.", "the model is the authoring source");
        body.InnerText.Should().NotContain("Original imported prose.", "the body is re-rendered from the model, not patched");
        doc.MainDocumentPart.StyleDefinitionsPart!.Styles!.Elements<Style>()
            .Should().Contain(s => s.StyleId!.Value == CarrierMarkerStyleId,
                "the carrier's styles part is preserved — the render went INTO the carrier, not a blank package");
    }

    [Fact]
    public async Task SaveAsync_ContentModelWithBaselineVersionId_FetchesCarrierAndRenders()
    {
        ArrangeReplaceExisting(out var capturedBytes);
        var carrier = BuildCarrierBytes();
        _spe.Setup(s => s.DownloadFileVersionAsUserAsync(
                It.IsAny<HttpContext>(), ExistingDriveId, ExistingSpeItemId, LoadTimeVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(carrier));

        var sut = CreateSut();
        var request = ReplaceRequest(EditedModel(), baselineVersionId: LoadTimeVersionId);

        var result = await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        result.Origin.Should().Be(ComposeOrigin.Imported,
            "version-fetch coordinates are a baseline source — never mis-stamped Authored");

        var persisted = capturedBytes();
        using var doc = WordprocessingDocument.Open(new MemoryStream(persisted, writable: false), isEditable: false);
        doc.MainDocumentPart!.Document!.Body!.InnerText.Should().Contain("Edited body text.");
        doc.MainDocumentPart.StyleDefinitionsPart!.Styles!.Elements<Style>()
            .Should().Contain(s => s.StyleId!.Value == CarrierMarkerStyleId,
                "the load-time version's bytes are the render carrier after a page refresh");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2. THE 422 KILL — the NDA (duplicate paraIds + text-box breakers) saves through the real
    //    projector → real renderer via SaveAsync without any refusal. This is the exact document and
    //    the exact path that produced the production 422.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SaveAsync_NdaThroughRenderOnSave_Succeeds_No422()
    {
        ArrangeReplaceExisting(out var capturedBytes);
        var ndaPath = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .Single(p => Path.GetFileName(p).StartsWith("AppligentNDA", StringComparison.OrdinalIgnoreCase));
        var ndaBytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(ndaPath);

        // The load-time projection (the canonical hub) is the model the client would edit + re-post.
        var projection = new ComposeDocxProjectionBuilder().BuildContentModel(ndaBytes);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed);

        var sut = CreateSut();
        var request = ReplaceRequest(projection.Model, content: ndaBytes);

        // The old path 422'd here (count-gate mismatch → zero anchorable ops → hard refusal).
        var result = await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        result.VersionId.Should().NotBeNullOrEmpty("the NDA save must succeed — the 422 class is unreachable on the render path");

        var persisted = capturedBytes();
        using var doc = WordprocessingDocument.Open(new MemoryStream(persisted, writable: false), isEditable: false);
        var ids = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Select(p => p.ParagraphId?.Value)
            .Where(v => v is not null)
            .ToList();
        ids.Should().OnlyHaveUniqueItems("the renderer dedups the NDA's duplicate-paraId class");
        doc.MainDocumentPart.Document.Body!.InnerText.Should().Contain("For: Appligent, Inc.",
            "the signature-box text survives the save as degraded prose (026 accept-flatten)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. Success-with-warnings (the 026 obligation this task wires): render degradations on the
    //    IMPORTED carrier path surface in the save result.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SaveAsync_ImportedRenderDegradations_SurfaceInResult()
    {
        ArrangeReplaceExisting(out _);
        var sut = CreateSut();

        // A dangling comment anchor (no comment id 99 anywhere) — dropped by the render, counted.
        var model = EditedModel(
            new ComposeInlineRun { Text = "kept text " },
            new ComposeInlineRun { CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.Start, Id = 99 } },
            new ComposeInlineRun { CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.End, Id = 99 } });

        var result = await sut.SaveAsync(
            ReplaceRequest(model, content: BuildCarrierBytes()), new DefaultHttpContext(), CancellationToken.None);

        result.DegradationWarnings.Should().NotBeNull("render drops surface as success-with-warnings, never silently");
        result.DegradationWarnings!.Should().ContainSingle(w => w.Code == "comment-anchor-dropped")
            .Which.Count.Should().Be(2);
    }

    [Fact]
    public async Task SaveAsync_CleanImportedRender_ReportsNoDegradations()
    {
        ArrangeReplaceExisting(out _);
        var sut = CreateSut();

        var result = await sut.SaveAsync(
            ReplaceRequest(EditedModel(), content: BuildCarrierBytes()), new DefaultHttpContext(), CancellationToken.None);

        result.DegradationWarnings.Should().BeNull("a clean render reports no warnings");
    }
}
