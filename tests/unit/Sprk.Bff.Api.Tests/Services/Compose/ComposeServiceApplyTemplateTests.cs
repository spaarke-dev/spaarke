// Task 032 (spaarkeai-compose-r6, FR-05) — unit tests for ComposeService.ApplyTemplateAsync, the
// apply-template orchestration wiring the 030 part-merge engine end-to-end:
//   1. merged output persisted as a NEW SPE version carries the TEMPLATE's chrome (header/footer/
//      landscape sectPr/styles) AND the DOCUMENT's body text — via the REAL engine, never a re-impl
//   2. the result mirrors the Save path's version conventions (VersionId = replace response id, ETag,
//      Size) and returns the post-merge canonical ContentModel for the client re-mount
//   3. merge degradations surface LOUDLY on MergeWarnings (template-merge-* codes), never silently
//   4. document-not-found (null download) → InvalidOperationException("not found") — the endpoint's 404
//   5. replace failure (null response) → InvalidOperationException — never a silent success
//
// Mocking boundary (ADR-038 §4, same set as the sibling ComposeServiceImportedRenderSaveTests):
// ISpeFileOperations (SPE facade, ADR-007), IGenericEntityService, IPostUploadIndexingEnqueuer,
// ChatSessionManager. Real in-memory .docx/.dotx bytes (Open XML SDK) + the REAL
// ComposeTemplatePartMergeEngine / ComposeDocxProjectionBuilder / ComposeBaselineParaIdStamper.
// No Mock<HttpMessageHandler> (B1), no DI-registration (B3), no ctor-null (B4).

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

public sealed class ComposeServiceApplyTemplateTests
{
    private const string DriveId = "drive-032-apply-template";
    private const string SpeItemId = "spe-item-032-apply-template";
    private const string FirmHeaderText = "FIRM HEADER — 032 Apply Template LLP";
    private const string TemplateBoilerplate = "TEMPLATE BOILERPLATE — must be dropped";
    private const string DocumentBodyText = "The parties agree to the confidentiality obligations herein.";
    private const string TemplateName = "Firm Standard";

    private readonly Mock<ISpeFileOperations> _spe = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);
    private readonly Mock<IPostUploadIndexingEnqueuer> _indexing = new(MockBehavior.Strict);
    private readonly Mock<ChatSessionManager> _sessions;

    public ComposeServiceApplyTemplateTests()
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
        _dataverse.Object,
        _indexing.Object,
        NullLogger<ComposeService>.Instance);

    // ── builders (real OOXML — the engine under the service is the REAL 030 engine) ──────────────

    /// <summary>A firm/matter .dotx: header + footer wired through a LANDSCAPE sectPr, a distinctive
    /// styles part, and boilerplate body content the merge must DROP (chrome provenance oracle).</summary>
    private static byte[] BuildTemplateDotx(bool includeSectPr = true)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Template, autoSave: true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document();
            var body = main.Document.AppendChild(new Body());
            body.AppendChild(new Paragraph(new Run(new Text(TemplateBoilerplate))));

            var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles = new Styles(
                new Style(new StyleName { Val = "Normal" })
                { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true },
                new Style(
                    new StyleName { Val = "Firm Body" },
                    new StyleRunProperties(new RunFonts { Ascii = "Garamond" }))
                { Type = StyleValues.Paragraph, StyleId = "FirmBody" });

            if (includeSectPr)
            {
                var headerPart = main.AddNewPart<HeaderPart>();
                headerPart.Header = new Header(new Paragraph(new Run(new Text(FirmHeaderText))));

                body.AppendChild(new SectionProperties(
                    new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(headerPart) },
                    new PageSize { Width = 15840, Height = 12240, Orient = PageOrientationValues.Landscape },
                    new PageMargin { Top = 720, Right = 720, Bottom = 720, Left = 720, Header = 360, Footer = 360, Gutter = 0 }));
            }

            main.Document.Save();
        }
        return stream.ToArray();
    }

    /// <summary>The persisted Compose document — body text is the provenance oracle for the merge.</summary>
    private static byte[] BuildDocumentBytes()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text(DocumentBodyText))),
                new SectionProperties(new PageSize { Width = 12240, Height = 15840 })));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static FileHandleDto ReplacedDriveItem() => new(
        Id: "5.0",
        Name: "merged.docx",
        ParentId: null,
        Size: 9999,
        CreatedDateTime: DateTimeOffset.UtcNow,
        LastModifiedDateTime: DateTimeOffset.UtcNow,
        ETag: "\"etag-merged-032\"",
        IsFolder: false,
        WebUrl: "https://spe/web/merged",
        DriveId: DriveId);

    private void ArrangeDownloadAndReplace(byte[] currentBytes, out Func<byte[]> capturedBytesAccessor)
    {
        _spe.Setup(s => s.DownloadFileAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, SpeItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(currentBytes));

        byte[]? captured = null;
        _spe.Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, SpeItemId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
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

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1+2. Happy path: template chrome + document body persisted as a new version; result mirrors the
    //      Save conventions and carries the post-merge canonical model.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ApplyTemplateAsync_PersistsMergedBytes_TemplateChromePlusDocumentBody()
    {
        ArrangeDownloadAndReplace(BuildDocumentBytes(), out var capturedBytes);
        var sut = CreateSut();

        var result = await sut.ApplyTemplateAsync(
            new DefaultHttpContext(), DriveId, SpeItemId, BuildTemplateDotx(), TemplateName, CancellationToken.None);

        // Result mirrors the Save path's version conventions.
        result.DocumentSpeId.Should().Be(SpeItemId);
        result.DriveId.Should().Be(DriveId);
        result.VersionId.Should().Be("5.0", "VersionId mirrors the replace response id (Save-path convention)");
        result.ETag.Should().Be("\"etag-merged-032\"");
        result.TemplateName.Should().Be(TemplateName);

        // Persisted bytes: TEMPLATE chrome + DOCUMENT body (the 030 engine's contract, via the service).
        var persisted = capturedBytes();
        using var doc = WordprocessingDocument.Open(new MemoryStream(persisted, writable: false), isEditable: false);
        var main = doc.MainDocumentPart!;
        var body = main.Document!.Body!;

        body.InnerText.Should().Contain(DocumentBodyText, "the document's body is the content source");
        body.InnerText.Should().NotContain(TemplateBoilerplate, "the template's boilerplate body must be dropped");

        // Chrome provenance: the template's landscape sectPr + header part survive with valid rels.
        var sectPr = body.Elements<SectionProperties>().Last();
        sectPr.GetFirstChild<PageSize>()!.Orient!.Value.Should().Be(PageOrientationValues.Landscape,
            "the merged sectPr is the TEMPLATE's (house chrome)");
        var headerRef = sectPr.GetFirstChild<HeaderReference>();
        headerRef.Should().NotBeNull("the template's header reference must survive the merge");
        ((HeaderPart)main.GetPartById(headerRef!.Id!.Value!)).Header!.InnerText.Should().Contain(FirmHeaderText,
            "the header reference must resolve to the template's own header part");
        main.StyleDefinitionsPart!.Styles!.Elements<Style>()
            .Should().Contain(s => s.StyleId!.Value == "FirmBody", "the template's style catalog is the merged catalog");

        // The persisted output is a DOCUMENT (the template was re-typed), not a .dotx.
        doc.DocumentType.Should().Be(WordprocessingDocumentType.Document);
    }

    [Fact]
    public async Task ApplyTemplateAsync_ReturnsPostMergeContentModel_ForClientRemount()
    {
        ArrangeDownloadAndReplace(BuildDocumentBytes(), out _);
        var sut = CreateSut();

        var result = await sut.ApplyTemplateAsync(
            new DefaultHttpContext(), DriveId, SpeItemId, BuildTemplateDotx(), TemplateName, CancellationToken.None);

        result.ContentModel.Should().NotBeNull("the persisted merged bytes re-project into the canonical model (post-save mirror)");
        result.ContentModel!.Blocks.Should().Contain(
            b => b.Runs.Any(r => (r.Text ?? string.Empty).Contains("confidentiality obligations")),
            "the model reflects the DOCUMENT body that was merged in");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. Degradations surface LOUDLY (template-merge-* codes on MergeWarnings).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ApplyTemplateAsync_MergeDegradation_SurfacesOnMergeWarnings_NeverSilently()
    {
        ArrangeDownloadAndReplace(BuildDocumentBytes(), out _);
        var sut = CreateSut();

        // A sectPr-less template cannot supply page chrome — the engine warns template-merge-missing-sectpr.
        var result = await sut.ApplyTemplateAsync(
            new DefaultHttpContext(), DriveId, SpeItemId, BuildTemplateDotx(includeSectPr: false), TemplateName,
            CancellationToken.None);

        result.MergeWarnings.Should().NotBeNull("degradations must surface loudly, never silently");
        result.MergeWarnings!.Should().Contain(w => w.Code == "template-merge-missing-sectpr");
    }

    [Fact]
    public async Task ApplyTemplateAsync_CleanMerge_MergeWarningsNull()
    {
        ArrangeDownloadAndReplace(BuildDocumentBytes(), out _);
        var sut = CreateSut();

        var result = await sut.ApplyTemplateAsync(
            new DefaultHttpContext(), DriveId, SpeItemId, BuildTemplateDotx(), TemplateName, CancellationToken.None);

        result.MergeWarnings.Should().BeNull("a clean merge carries no warnings (null, not empty)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 4+5. Failure honesty: not-found download / failed replace are exceptions, never fake success.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ApplyTemplateAsync_DocumentNotFound_ThrowsNotFound_AndNeverWrites()
    {
        _spe.Setup(s => s.DownloadFileAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, SpeItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream?)null);
        var sut = CreateSut();

        var act = () => sut.ApplyTemplateAsync(
            new DefaultHttpContext(), DriveId, SpeItemId, BuildTemplateDotx(), TemplateName, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*not found*", "the endpoint maps this to 404");
        _spe.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never, "a missing document must never reach the write");
    }

    [Fact]
    public async Task ApplyTemplateAsync_ReplaceFails_Throws_NeverSilentSuccess()
    {
        _spe.Setup(s => s.DownloadFileAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, SpeItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(BuildDocumentBytes()));
        _spe.Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, SpeItemId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileHandleDto?)null);
        var sut = CreateSut();

        var act = () => sut.ApplyTemplateAsync(
            new DefaultHttpContext(), DriveId, SpeItemId, BuildTemplateDotx(), TemplateName, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ApplyTemplateAsync_EmptyTemplateBytes_ThrowsArgumentException()
    {
        var sut = CreateSut();

        var act = () => sut.ApplyTemplateAsync(
            new DefaultHttpContext(), DriveId, SpeItemId, Array.Empty<byte>(), TemplateName, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>("empty template bytes are a caller bug — the endpoint 400s");
    }
}
