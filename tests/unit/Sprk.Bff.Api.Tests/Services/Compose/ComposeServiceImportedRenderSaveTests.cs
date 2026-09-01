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
using Sprk.Bff.Api.Services.Compose.Operations;
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

        // No acting-user container setup here: this fixture exercises the REPLACE path (a document that
        // already has a drive-item), which never reaches issue #858's create-on-save container
        // derivation. Adding the setup would arrange a call that is never made.
    }

    private ComposeService CreateSut() => new(
        _spe.Object,
        _sessions.Object,
        _dataverse.Object,
        _indexing.Object,
        NullLogger<ComposeService>.Instance,
        ComposeServiceCollaborators.Resolver(_dataverse.Object),
        ComposeServiceCollaborators.Probe().Object);

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

        // FR-S09 item 7 (r8 task 016): a replace save now refreshes sprk_filesize/sprk_filepath on the
        // existing row. The mock is STRICT, so without this setup the call throws MockException, the
        // service's best-effort catch records a failed refresh, and every clean replace test acquires a
        // spurious `document-metadata-stale` warning — a fixture gap presenting as a production defect.
        _dataverse.Setup(d => d.UpdateAsync(
                "sprk_document", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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

        var result = await sut.SaveAsync(request, TestHttpContexts.Authenticated(), CancellationToken.None);

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

        var result = await sut.SaveAsync(request, TestHttpContexts.Authenticated(), CancellationToken.None);

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
        var result = await sut.SaveAsync(request, TestHttpContexts.Authenticated(), CancellationToken.None);

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
            ReplaceRequest(model, content: BuildCarrierBytes()), TestHttpContexts.Authenticated(), CancellationToken.None);

        result.DegradationWarnings.Should().NotBeNull("render drops surface as success-with-warnings, never silently");
        result.DegradationWarnings!.Should().ContainSingle(w => w.Code == "comment-anchor-dropped")
            .Which.Count.Should().Be(2);
    }

    [Fact]
    public async Task SaveAsync_ContentModelWithOpLog_IgnoresOpsLoudly_ModelIsAuthoritative()
    {
        // Step-9.5 F1: a mixed-contract request (model + op-log) renders from the MODEL; the ops are
        // never half-applied, and the drop is observable ON THE WIRE (op-log-ignored), not just logged.
        ArrangeReplaceExisting(out var capturedBytes);
        var sut = CreateSut();

        var request = ReplaceRequest(EditedModel(), content: BuildCarrierBytes());
        request = request with
        {
            OperationLog = new ComposeOperationLog
            {
                Operations = new List<ComposeOperation>
                {
                    new InsertTextOperation { ParaId = "7B00AA01", At = new ComposeRunPoint(0, 0), Text = "OP-INSERTED-TEXT" },
                },
            },
        };

        var result = await sut.SaveAsync(request, TestHttpContexts.Authenticated(), CancellationToken.None);

        result.DegradationWarnings.Should().NotBeNull();
        result.DegradationWarnings!.Should().ContainSingle(w => w.Code == "op-log-ignored")
            .Which.Count.Should().Be(1);

        using var doc = WordprocessingDocument.Open(new MemoryStream(capturedBytes(), writable: false), isEditable: false);
        var text = doc.MainDocumentPart!.Document!.Body!.InnerText;
        text.Should().Contain("Edited body text.", "the model is the authoritative document state");
        text.Should().NotContain("OP-INSERTED-TEXT", "the op-log must never half-apply on the render path");
    }

    [Fact]
    public async Task SaveAsync_CleanImportedRender_ReportsNoDegradations()
    {
        ArrangeReplaceExisting(out _);
        var sut = CreateSut();

        var result = await sut.SaveAsync(
            ReplaceRequest(EditedModel(), content: BuildCarrierBytes()), TestHttpContexts.Authenticated(), CancellationToken.None);

        result.DegradationWarnings.Should().BeNull("a clean render reports no warnings");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Task 012 — the client cutover: post-save model return, comments-through-the-model (carrier
    // append), the retired engine comment-bake (separate comments now ignored LOUDLY), and the
    // user-edit revision-author fallback.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SaveAsync_RenderPathSave_ReturnsPostSaveContentModel()
    {
        ArrangeReplaceExisting(out _);
        var sut = CreateSut();
        var request = ReplaceRequest(EditedModel(), content: BuildCarrierBytes());

        var result = await sut.SaveAsync(request, TestHttpContexts.Authenticated(), CancellationToken.None);

        result.ContentModel.Should().NotBeNull(
            "a render-path save returns the post-save canonical model — the client's new merge base");
        result.ContentModel!.Blocks.Should().NotBeEmpty();
        string.Join(" ", result.ContentModel.Blocks.SelectMany(b => b.Runs).Select(r => r.Text))
            .Should().Contain("Edited body text.", "the returned model reflects the persisted document state");
    }

    [Fact]
    public async Task SaveAsync_CleanReplaceSaveWithoutModel_ReturnsNoPostSaveModel()
    {
        ArrangeReplaceExisting(out _);
        var sut = CreateSut();
        var request = new SaveComposeDocumentRequest
        {
            DocumentSpeId = ExistingSpeItemId,
            DriveId = ExistingDriveId,
            Content = BuildCarrierBytes(),
            SessionId = Guid.NewGuid().ToString(),
            TenantId = Tenant,
        };

        var result = await sut.SaveAsync(request, TestHttpContexts.Authenticated(), CancellationToken.None);

        result.ContentModel.Should().BeNull("only render-path saves project a post-save model");
    }

    [Fact]
    public async Task SaveAsync_ContentModelWithSeparateComments_IgnoresThemLoudly_NoEngineBake()
    {
        ArrangeReplaceExisting(out var capturedBytes);
        var sut = CreateSut();
        var request = ReplaceRequest(EditedModel(), content: BuildCarrierBytes()) with
        {
            // The pre-cutover shape: separate (paraId, run-range)-anchored comments alongside the model.
            // The engine bake that consumed these was the LAST ComposeShadowPatchEngine caller reachable
            // with a ContentModel — retired by this task; comments now ride the model itself.
            Comments = new List<ComposeAnchoredComment>
            {
                new()
                {
                    ParaId = "7B00AA01",
                    Range = new ComposeRunRange(new ComposeRunPoint(0, 0), new ComposeRunPoint(0, 4)),
                    CommentText = "orphaned pre-cutover comment",
                    Author = "Alice Reviewer",
                    Date = DateTimeOffset.Parse("2026-08-06T00:00:00Z"),
                },
            },
        };

        var result = await sut.SaveAsync(request, TestHttpContexts.Authenticated(), CancellationToken.None);

        result.VersionId.Should().NotBeNullOrEmpty("ignoring the separate comments never fails the save");
        result.DegradationWarnings.Should().NotBeNull();
        result.DegradationWarnings!.Should().Contain(w => w.Code == "comments-ignored" && w.Count == 1,
            "the drop is wire-visible, never silent");

        using var doc = WordprocessingDocument.Open(new MemoryStream(capturedBytes(), writable: false), isEditable: false);
        doc.MainDocumentPart!.WordprocessingCommentsPart.Should().BeNull(
            "the engine bake is retired — a separate anchored comment no longer reaches the package");
    }

    /// <summary>A carrier whose comments part already holds ONE comment (id 1) — the append oracle.</summary>
    private static byte[] BuildCarrierBytesWithComment()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var commentsPart = main.AddNewPart<WordprocessingCommentsPart>();
            commentsPart.Comments = new Comments(
                new Comment(new Paragraph(new Run(new Text("Original carrier comment"))))
                {
                    Id = "1",
                    Author = "Carrier Author",
                });
            commentsPart.Comments.Save();

            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text("Original imported prose."))),
                new SectionProperties(new PageSize { Width = 12240, Height = 15840 })));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    [Fact]
    public async Task SaveAsync_ModelWithNewComment_AppendsToCarrierCommentsPart_PreservingExisting()
    {
        ArrangeReplaceExisting(out var capturedBytes);
        var sut = CreateSut();

        // The model the client posts: the LOADED comment (id 1, preserved verbatim) + a NEW session
        // comment (id 2) folded in by the client mapper, with Start/End anchor runs around body text.
        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                new ComposeBlock
                {
                    Kind = ComposeBlockKind.Paragraph,
                    Runs = new[]
                    {
                        new ComposeInlineRun { Text = string.Empty, CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.Start, Id = 2 } },
                        new ComposeInlineRun { Text = "Annotated edited text." },
                        new ComposeInlineRun { Text = string.Empty, CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.End, Id = 2 } },
                    },
                },
            },
            Comments = new[]
            {
                new ComposeComment { Id = 1, Author = "Carrier Author", Text = "Original carrier comment" },
                new ComposeComment { Id = 2, Author = "Session Reviewer", Date = "2026-08-06T09:30:00Z", Text = "New session comment" },
            },
        };

        var request = ReplaceRequest(model, content: BuildCarrierBytesWithComment());
        var result = await sut.SaveAsync(request, TestHttpContexts.Authenticated(), CancellationToken.None);

        result.VersionId.Should().NotBeNullOrEmpty();

        using var doc = WordprocessingDocument.Open(new MemoryStream(capturedBytes(), writable: false), isEditable: false);
        var comments = doc.MainDocumentPart!.WordprocessingCommentsPart!.Comments!.Elements<Comment>().ToList();
        comments.Should().HaveCount(2, "the new session comment is APPENDED; the carrier's own comment is preserved");
        comments.Select(c => c.Id!.Value).Should().BeEquivalentTo(new[] { "1", "2" });
        comments.Single(c => c.Id!.Value == "1").InnerText.Should().Contain("Original carrier comment",
            "existing carrier comment content is never edited");
        comments.Single(c => c.Id!.Value == "2").InnerText.Should().Contain("New session comment");

        var body = doc.MainDocumentPart.Document!.Body!;
        body.Descendants<CommentRangeStart>().Should().ContainSingle(a => a.Id!.Value == "2",
            "the new comment's anchor survives the anchor-validity filter (carrier ids ∪ appended ids)");
        body.Descendants<CommentReference>().Should().ContainSingle(r => r.Id!.Value == "2");

        // Task 013 (F6): the loaded comment (id 1, same text) is the normal round-trip - it must NOT
        // false-positive the collision warn.
        (result.DegradationWarnings ?? Array.Empty<ComposeProjectionWarning>())
            .Should().NotContain(w => w.Code == "comment-id-collision",
                "a same-text loaded round-trip is not a collision");
    }

    [Fact]
    public async Task SaveAsync_ModelCommentIdCollidingWithDifferentCarrierComment_WarnsLoudly()
    {
        ArrangeReplaceExisting(out var capturedBytes);
        var sut = CreateSut();

        // The F6 case: the model claims comment id 1 with text the carrier's comment 1 does NOT start
        // with - a client-allocated id landed on a carrier comment the loaded model never carried
        // (e.g. one the projection flattened). The anchor still binds to the carrier comment
        // (behavior unchanged), but the collision must be WIRE-VISIBLE, never silent.
        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                new ComposeBlock
                {
                    Kind = ComposeBlockKind.Paragraph,
                    Runs = new[]
                    {
                        new ComposeInlineRun { Text = string.Empty, CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.Start, Id = 1 } },
                        new ComposeInlineRun { Text = "Colliding anchor text." },
                        new ComposeInlineRun { Text = string.Empty, CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.End, Id = 1 } },
                    },
                },
            },
            Comments = new[]
            {
                new ComposeComment { Id = 1, Author = "Session Reviewer", Text = "Entirely different session comment" },
            },
        };

        var request = ReplaceRequest(model, content: BuildCarrierBytesWithComment());
        var result = await sut.SaveAsync(request, TestHttpContexts.Authenticated(), CancellationToken.None);

        result.VersionId.Should().NotBeNullOrEmpty("a collision warns - it never fails the save");
        result.DegradationWarnings.Should().NotBeNull();
        result.DegradationWarnings!.Should().Contain(w => w.Code == "comment-id-collision",
            "a model comment id pointing at a different-text carrier comment must be wire-visible");

        using var doc = WordprocessingDocument.Open(new MemoryStream(capturedBytes(), writable: false), isEditable: false);
        doc.MainDocumentPart!.WordprocessingCommentsPart!.Comments!.Elements<Comment>()
            .Should().ContainSingle(c => c.Id!.Value == "1",
                "the carrier's comment is authoritative - the colliding model comment is NOT appended");
    }

    [Fact]
    public async Task SaveAsync_ModelCommentTextClampedPrefix_DoesNotWarnCollision()
    {
        ArrangeReplaceExisting(out _);
        var sut = CreateSut();

        // The projection clamps long comment text, so the loaded model's text may be a PREFIX of the
        // carrier's - that is the normal round-trip, not a collision.
        var model = new ComposeContentModel
        {
            Blocks = new[] { new ComposeBlock { Kind = ComposeBlockKind.Paragraph, Runs = new[] { new ComposeInlineRun { Text = "Body." } } } },
            Comments = new[] { new ComposeComment { Id = 1, Author = "Carrier Author", Text = "Original carrier" } },
        };

        var request = ReplaceRequest(model, content: BuildCarrierBytesWithComment());
        var result = await sut.SaveAsync(request, TestHttpContexts.Authenticated(), CancellationToken.None);

        (result.DegradationWarnings ?? Array.Empty<ComposeProjectionWarning>())
            .Should().NotContain(w => w.Code == "comment-id-collision",
                "a clamped-prefix model text is the normal round-trip, not a collision");
    }

    [Fact]
    public async Task SaveAsync_RevisionFactWithoutAuthor_AttributedToSaveAuthor()
    {
        ArrangeReplaceExisting(out var capturedBytes);
        var sut = CreateSut();

        // The client mapper deliberately OMITS the author on user-edit revision facts (the server, not
        // the client, attributes the saving user); an imported revision fact CARRIES its true author.
        var model = EditedModel(
            new ComposeInlineRun { Text = "kept text " },
            new ComposeInlineRun { Text = "user insert", Revision = new ComposeRevision { Kind = ComposeRevisionKind.Inserted } },
            new ComposeInlineRun { Text = " imported insert", Revision = new ComposeRevision { Kind = ComposeRevisionKind.Inserted, Author = "Jane Q. Author" } });

        var request = ReplaceRequest(model, content: BuildCarrierBytes());
        await sut.SaveAsync(request, TestHttpContexts.Authenticated(), CancellationToken.None);

        using var doc = WordprocessingDocument.Open(new MemoryStream(capturedBytes(), writable: false), isEditable: false);
        var insertions = doc.MainDocumentPart!.Document!.Body!.Descendants<InsertedRun>().ToList();
        insertions.Should().HaveCount(2);
        insertions.Select(i => i.Author!.Value).Should().Contain("Spaarke Compose",
            "an author-less revision fact falls back to the save-time author (DefaultHttpContext has no name claim → the service's own fallback)");
        insertions.Select(i => i.Author!.Value).Should().Contain("Jane Q. Author",
            "a fact that carries an author keeps it — imported revisions round-trip their true authors");
    }

}
