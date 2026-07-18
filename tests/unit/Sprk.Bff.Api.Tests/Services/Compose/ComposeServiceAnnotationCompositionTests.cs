// FR-04 (task 023, E1) — unit tests for ComposeService.SaveAsync's ANNOTATION COMPOSITION: AI redlines +
// comments (request.Annotations) apply via the UNCHANGED DocxAnnotationWriter onto the SERVER-AUTHORED
// baseline — composing correctly with BOTH the direct-typing delta (task 022 synthesizer) AND the
// born-in-editor render (task 026 ComposeDocumentRenderer). The writer itself is covered by
// DocxAnnotationWriterTests; these prove the ORCHESTRATION composition order (baseline/render →
// EditedParagraphs delta → Annotations) persists native w:ins/w:del/w:comment on a FAITHFUL baseline and
// does not corrupt either set of tracked changes.
//
// Mocking boundary (ADR-038 §4): ISpeFileOperations (SPE/Graph facade, ADR-007), IGenericEntityService
// (Dataverse), IPostUploadIndexingEnqueuer (RAG indexing seam), ChatSessionManager (Redis session store) —
// the SAME boundary set as the sibling ComposeServiceDeltaSaveTests / ComposeServiceBornInEditorSaveTests.
// Real in-memory .docx (Open XML SDK); no transport mocks (B1), no DI/ctor tests (B3/B4). Through-the-wire
// seam rides task 024 (NFR-06).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
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

public sealed class ComposeServiceAnnotationCompositionTests
{
    private const string Tenant = "tenant-aad-023";
    private const string ExistingDriveId = "drive-023";
    private const string ExistingSpeItemId = "spe-023";
    private const string ContainerId = "container-023";
    private const string ResolvedDriveId = "drive-023-created";
    private const string CreatedSpeItemId = "spe-023-created";

    private const string EditedParaId = "0000002B"; // the middle paragraph (direct-typing edit)
    private const string UntouchedParaId = "0000002A";
    private const string AnnotatedParaId = "0000002C";

    private readonly Mock<ISpeFileOperations> _spe = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Strict);
    private readonly Mock<IPostUploadIndexingEnqueuer> _indexing = new(MockBehavior.Strict);
    private readonly Mock<ChatSessionManager> _sessions;

    public ComposeServiceAnnotationCompositionTests()
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
        Para(AnnotatedParaId, "Confidential Information shall not be shared."));

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

    // ── strict-mock arrangements ──────────────────────────────────────────────────────────────────

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
            .ReturnsAsync(new FileHandleDto(
                Id: ExistingSpeItemId, Name: "doc.docx", ParentId: null, Size: 4321,
                CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"etag-023\"", IsFolder: false, WebUrl: "https://spe/web", DriveId: ExistingDriveId));

        capturedBytesAccessor = () => captured
            ?? throw new InvalidOperationException("ReplaceFileContentAsUserAsync was never invoked.");
    }

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
            .ReturnsAsync(new FileHandleDto(
                Id: CreatedSpeItemId, Name: "draft.docx", ParentId: null, Size: 5555,
                CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"etag-023-created\"", IsFolder: false, WebUrl: "https://spe/web/draft", DriveId: ResolvedDriveId));

        capturedBytesAccessor = () => captured
            ?? throw new InvalidOperationException("UploadSmallAsUserAsync was never invoked.");
    }

    private void ArrangePromotionAndIndexing()
    {
        _dataverse.Setup(d => d.RetrieveByAlternateKeyAsync(
                "sprk_document", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document") { Id = Guid.NewGuid() });
        _indexing.Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));
    }

    // ── OOXML query helpers ───────────────────────────────────────────────────────────────────────

    private static WordprocessingDocument Open(byte[] docx) =>
        WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);

    private static Paragraph ParaById(WordprocessingDocument doc, string paraId) =>
        doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .First(p => string.Equals(p.ParagraphId?.Value, paraId, StringComparison.OrdinalIgnoreCase));

    private static void AssertSchemaValid(WordprocessingDocument doc)
    {
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc).ToList();
        var detail = string.Join("\n", errors.Select(e => $"{e.Path?.XPath}: {e.Description}"));
        Assert.True(errors.Count == 0, "the composed .docx must be schema-valid — offenders:\n" + detail);
    }

    // ── criterion 1: AI redline + comment persist as native OOXML on the retained-original baseline ──

    [Fact]
    public async Task SaveAsync_WithAnnotationsOnly_PersistsNativeRevisionsAndCommentOnRetainedOriginal()
    {
        ArrangeReplaceExisting(out var capturedBytes);
        ArrangePromotionAndIndexing();

        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = ExistingSpeItemId,
            DriveId = ExistingDriveId,
            Content = ThreeParagraphDoc(),      // retained ORIGINAL bytes (same-session fast-path)
            Annotations = new[]
            {
                new DocxAnnotation { Kind = TrackChangeKind.Comment, TargetText = "Confidential", CommentText = "Define this term.", Author = "Spaarke AI", Date = DateTimeOffset.UtcNow },
                new DocxAnnotation { Kind = TrackChangeKind.Deletion, TargetText = "not ", Author = "Spaarke AI", Date = DateTimeOffset.UtcNow },
            },
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        using var doc = Open(capturedBytes());
        var body = doc.MainDocumentPart!.Document!.Body!;

        body.Descendants<DeletedRun>().Should().ContainSingle("the accepted AI deletion persists as native w:del");
        body.Descendants<DeletedText>().Single().Text.Should().Be("not ");
        doc.MainDocumentPart.WordprocessingCommentsPart!.Comments!.Elements<Comment>().Single()
            .InnerText.Should().Be("Define this term.", "the AI comment persists as a native w:comment");
        // The baseline's paraIds survive (E2 substrate preserved through the annotation pass).
        body.Descendants<Paragraph>().Select(p => p.ParagraphId?.Value)
            .Should().Contain(new[] { UntouchedParaId, EditedParaId, AnnotatedParaId });
        AssertSchemaValid(doc);
    }

    // ── criterion 2: AI redline composes with the direct-typing delta without corrupting either ──────

    [Fact]
    public async Task SaveAsync_WithEditedParagraphsAndAnnotations_ComposesBothWithoutCorruptingEither()
    {
        ArrangeReplaceExisting(out var capturedBytes);
        ArrangePromotionAndIndexing();

        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = ExistingSpeItemId,
            DriveId = ExistingDriveId,
            Content = ThreeParagraphDoc(),
            // Direct-typing edit on paragraph B (synthesizer delta).
            EditedParagraphs = new[] { new ComposeEditedParagraph(EditedParaId, "Termination requires sixty days written notice.") },
            // AI redline/comment targeting paragraph C (annotation pass) — a DIFFERENT paragraph.
            Annotations = new[]
            {
                new DocxAnnotation { Kind = TrackChangeKind.Comment, TargetText = "Confidential", CommentText = "Define this term.", Author = "Spaarke AI", Date = DateTimeOffset.UtcNow },
                new DocxAnnotation { Kind = TrackChangeKind.Deletion, TargetText = "not ", Author = "Spaarke AI", Date = DateTimeOffset.UtcNow },
            },
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        using var doc = Open(capturedBytes());

        // Paragraph B carries the DIRECT-TYPING delta (synthesizer w:ins/w:del) — its edit landed.
        var edited = ParaById(doc, EditedParaId);
        (edited.Descendants<InsertedRun>().Any() || edited.Descendants<DeletedRun>().Any())
            .Should().BeTrue("the direct-typing edit persists as tracked changes on its paragraph");
        edited.InnerText.Should().Contain("sixty", "the user's new text landed");

        // Paragraph C carries the AI ANNOTATION delta (writer w:del + w:comment) — unaffected by the edit.
        var annotated = ParaById(doc, AnnotatedParaId);
        annotated.Descendants<DeletedRun>().Should().ContainSingle("the AI deletion landed on its own paragraph");
        annotated.Descendants<DeletedText>().Single().Text.Should().Be("not ");
        doc.MainDocumentPart!.WordprocessingCommentsPart!.Comments!.Elements<Comment>()
            .Should().ContainSingle("the AI comment survived composition with the direct-typing delta");

        // Paragraph A (touched by neither) is byte-clean.
        var untouched = ParaById(doc, UntouchedParaId);
        untouched.InnerText.Should().Be("The parties enter into this Agreement.");
        untouched.Descendants<InsertedRun>().Should().BeEmpty();
        untouched.Descendants<DeletedRun>().Should().BeEmpty();

        AssertSchemaValid(doc);
    }

    // ── criterion (amendment): AI redline composes onto the born-in-editor RENDER (task 026) ─────────

    [Fact]
    public async Task SaveAsync_WithContentModelAndAnnotations_AnnotatesTheServerRenderedBornInEditorDoc()
    {
        ArrangeCreateOnSave(out var capturedBytes);
        ArrangePromotionAndIndexing();

        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            // Born-in-editor first Save: the renderer authors the .docx; annotations decorate THAT render.
            ContainerId = ContainerId,
            ContentModel = new ComposeContentModel
            {
                Blocks = new[]
                {
                    new ComposeBlock { Kind = ComposeBlockKind.Heading, Level = 1, Runs = new[] { new ComposeInlineRun { Text = "Confidentiality" } } },
                    new ComposeBlock { Kind = ComposeBlockKind.Paragraph, Runs = new[] { new ComposeInlineRun { Text = "Confidential Information shall not be shared." } } },
                },
            },
            Annotations = new[]
            {
                new DocxAnnotation { Kind = TrackChangeKind.Comment, TargetText = "Confidential Information", CommentText = "Define this term.", Author = "Spaarke AI", Date = DateTimeOffset.UtcNow },
            },
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        await sut.SaveAsync(request, new DefaultHttpContext(), CancellationToken.None);

        using var doc = Open(capturedBytes());

        // The render happened (numbering part authored) AND the annotation decorates it.
        doc.MainDocumentPart!.NumberingDefinitionsPart.Should().NotBeNull("the born-in-editor render authored numbering");
        doc.MainDocumentPart.WordprocessingCommentsPart!.Comments!.Elements<Comment>().Single()
            .InnerText.Should().Be("Define this term.", "the AI comment persists on the server-rendered doc");
        AssertSchemaValid(doc);
    }
}
