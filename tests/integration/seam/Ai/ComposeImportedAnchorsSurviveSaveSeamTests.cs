// Task 052 — FR-26 imported anchors survive save (paraId + retained-original) + through-the-wire
// seam test (ADR-038 vertical-slice-seam KEEP category, NFR-06 E2E Definition-of-Done).
//
// THE SURVIVAL MECHANISM this test proves end-to-end: existing native Word revisions/comments
// (task 050/051's DocxAnnotationReader projection) live INSIDE the loaded `.docx` as native
// `w:ins`/`w:del`/`w:comment` OOXML. On an E1 dirty save (task 022), the persisted baseline is the
// RETAINED ORIGINAL bytes (the client's `content` — the same bytes Load returned, which still carry
// the imported marks) plus a paraId-keyed delta for ONLY the edited paragraph(s)
// (`ComposeParagraphRedlineSynthesizer`, Option C). Untouched paragraphs — including ones carrying
// imported marks — pass through the synthesizer BYTE-IDENTICAL (task 024 proved this for the
// fidelity core generally; this test proves it specifically for imported marks). Because task 022's
// synthesizer opens the SAME OPC package in place (`WordprocessingDocument.Open(buffer,
// isEditable: true)`) and only rewrites the matched paragraphs' XML, the separate
// `WordprocessingCommentsPart` (comments.xml) that a `w:comment` element's `CommentRangeStart`/
// `CommentRangeEnd`/`CommentReference` markers point into is never touched either — so an imported
// COMMENT survives even though its comment body lives in a different OPC part than the paragraph
// carrying its anchor markers.
//
// This test also surfaces and fixes a REAL production bug (documented in the fixture-setup comment
// below and in the PR/task report): `ComposeService.LoadAsync` has computed `ParaIdMap` (task 010) /
// `ImportedRevisions` (task 050) / `ImportedComments` (task 051) on `LoadComposeDocumentResult` since
// those tasks landed, but `ComposeEndpoints.Load`'s wire response (`LoadComposeDocumentResponse`)
// never projected them onto the HTTP JSON payload — every real Load response silently omitted all
// three fields. Task 052 adds the three fields to the wire response (additive, camelCase, mirrors the
// existing client `compose-contracts.ts` shapes verbatim) so this seam test — and the real client —
// can actually observe imported marks on Load. This is exactly the "unit-green != done" gap the
// vertical-slice-seam KEEP category (ADR-038) exists to catch: tasks 050/051 were fully unit-green
// (service-level + client-prop-level) while the wire was silently broken.
//
// Flow: Load (fixture carrying pre-existing w:ins/w:del/w:comment) -> assert imported marks present
// on Load with correct paraId anchors -> dirty-edit an UNRELATED paragraph -> Save through the REAL
// route (E1 delta) -> Load again -> assert the imported revisions AND comments are STILL present,
// STILL anchored to the SAME w14:paraId, NOT dropped or re-flattened; assert the dirty edit landed on
// exactly its paragraph and every other paragraph (incl. the ones carrying imported marks) is
// byte-identical.
//
// KEEP-path classification (ADR-038 "vertical-slice-seam" + tests/CLAUDE.md): tests/integration/
// seam/**. REUSES `ComposeFidelitySeamFixture` (task 024, same file) verbatim — same
// WebApplicationFactory host config, same SPE/Dataverse/Indexing module-boundary mocks, same fake
// auth handler. No new fixture class (CLAUDE.md SS11 Component Justification: the existing fixture IS
// the "load a real .docx through the real routes, mock only the SPE/Dataverse/Indexing boundary"
// harness this test needs; duplicating ~150 lines of WebApplicationFactory host wiring would drift
// from the canonical one task 024 already established).
//
// Banned-pattern compliance (ADR-038 SS4 + tests/CLAUDE.md): NO Mock<HttpMessageHandler>; NO
// DI-registration test; NO ctor-null test. Mocks live ONLY at the ISpeFileOperations /
// IGenericEntityService / IPostUploadIndexingEnqueuer module boundaries (the SAME fixture as task
// 024) — the HTTP boundary, the real ComposeEndpoints route mapping, and the real
// ComposeService/ComposeParagraphRedlineSynthesizer/DocxAnnotationReader/DocxAnnotationWriter/
// ParaIdPreParser are all production types.

using System.Net;
using System.Net.Http.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

public sealed class ComposeImportedAnchorsSurviveSaveSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private static readonly DateTime When = new(2026, 7, 19, 11, 0, 0, DateTimeKind.Utc);

    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposeImportedAnchorsSurviveSaveSeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task LoadEditSaveReload_PreservesImportedRevisionsAndComments_AnchoredByOriginalParaId()
    {
        _fixture.ResetBoundaries();

        // ── Arrange: a small, deterministic 3-paragraph fixture. Paragraph 0 carries an imported
        //    INSERTION; paragraph 1 carries an imported DELETION + an imported COMMENT (the same
        //    mixed-annotation shape ComposeServiceLoadImportedCommentsTests' "both" test uses);
        //    paragraph 2 is UNTOUCHED by any import and is the one we dirty-edit. ────────────────────
        const string para0Text = "The quick brown fox jumps over things.";
        const string para1Text = "The lazy dog sleeps soundly today.";
        const string para2Text = "This clause shall survive independent review by counsel.";

        var original = BuildRedlinedAndCommentedFixture(para0Text, para1Text, para2Text);

        const string speId = "spe-item-052-imported-survive";
        const string driveId = "drive-052-imported-survive";
        const string tenant = ComposeFidelitySeamFixture.TestTenantId;

        // ── Phase 1: LOAD (through the real GET route) ───────────────────────────────────────────
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: speId, Name: "redlined-commented.docx", ParentId: null, Size: original.Length,
                CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"v1-etag\"", IsFolder: false, WebUrl: null, DriveId: driveId));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(original));
        _fixture.SpeMock
            .Setup(s => s.GetCurrentVersionIdAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("v-load-1");

        using var client = _fixture.CreateAuthenticatedClient();

        var firstLoadResponse = await client.GetAsync(
            $"/api/compose/documents/{speId}?driveId={driveId}&tenantId={tenant}");
        firstLoadResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "the real Load route resolves the fixture bytes through the SPE facade boundary");

        var firstLoad = await firstLoadResponse.Content.ReadFromJsonAsync<LoadComposeDocumentResponse>();
        firstLoad.Should().NotBeNull();

        // ── Assert (Load side, FR-24/FR-25): imported marks are present on Load, over the real wire,
        //    with the E2 paraId of their containing paragraph (task 052's own fix — the Load response
        //    never carried these three fields until this task added them). ─────────────────────────
        firstLoad!.ParaIdMap.Should().HaveCount(3, "the fixture has 3 body paragraphs");
        firstLoad.ParaIdMap.Should().OnlyContain(e => !e.IsMinted,
            "the fixture carries REAL physical w14:paraId values (like a genuinely Word-authored document), never server-minted ones");
        var para0Id = firstLoad.ParaIdMap.Single(e => e.Index == 0).ParaId;
        var para1Id = firstLoad.ParaIdMap.Single(e => e.Index == 1).ParaId;
        var para2Id = firstLoad.ParaIdMap.Single(e => e.Index == 2).ParaId;
        para0Id.Should().Be(FixtureParaIds[0]);
        para1Id.Should().Be(FixtureParaIds[1]);
        para2Id.Should().Be(FixtureParaIds[2]);

        firstLoad.ImportedRevisions.Should().HaveCount(2,
            "the fixture carries one w:ins (paragraph 0) and one w:del (paragraph 1)");
        var insertion = firstLoad.ImportedRevisions.Should()
            .ContainSingle(r => r.Kind == RecoveredAnnotationKind.Insertion).Subject;
        var deletion = firstLoad.ImportedRevisions.Should()
            .ContainSingle(r => r.Kind == RecoveredAnnotationKind.Deletion).Subject;
        insertion.ParaId.Should().Be(para0Id, "the imported insertion anchors to paragraph 0's w14:paraId (E2, primary anchor)");
        deletion.ParaId.Should().Be(para1Id, "the imported deletion anchors to paragraph 1's w14:paraId (E2, primary anchor)");

        firstLoad.ImportedComments.Should().ContainSingle("the fixture carries one w:comment (paragraph 1)");
        var comment = firstLoad.ImportedComments[0];
        comment.ParaId.Should().Be(para1Id, "the imported comment anchors to paragraph 1's w14:paraId (E2, primary anchor)");
        comment.CommentText.Should().Be("Cut this sentence.");

        var sessionId = firstLoad.SessionId;
        sessionId.Should().NotBeNullOrWhiteSpace();

        // ── Phase 2: dirty-edit paragraph 2 (UNRELATED to the imported marks) and SAVE through the
        //    real route — the E1 retained-original delta (task 022). ─────────────────────────────────
        _fixture.ResetBoundaries();

        var existingDocumentId = Guid.NewGuid();
        byte[]? persisted = null;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, CancellationToken>((_, _, _, stream, _) =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                persisted = ms.ToArray();
            })
            .ReturnsAsync(new FileHandleDto(
                Id: speId, Name: "redlined-commented.docx", ParentId: null, Size: original.Length,
                CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"v2-etag\"", IsFolder: false, WebUrl: null, DriveId: driveId));

        // Idempotent promotion: this document is already backed by SPE (a replace save, not a
        // first-Save) — the alt-key lookup finds the existing row, so no CreateAsync fires.
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", existingDocumentId));

        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        const string amendedPara2Text = "This clause shall survive independent review by counsel and outside experts.";

        var saveResponse = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            // The real wire contract task 027's client sends for a loaded-doc dirty save: the
            // same-session fast-path retained-original `content` (STILL carrying the imported marks —
            // this is the client's pristine mount payload, never a reconstruction) + the paraId-keyed
            // `editedParagraphs` delta for the ONE paragraph the user actually changed.
            content = original,
            editedParagraphs = new[]
            {
                new { paraId = para2Id, newText = amendedPara2Text },
            },
        });

        var saveBody = await saveResponse.Content.ReadAsStringAsync();
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK, saveBody);
        var saveResult = await saveResponse.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        saveResult.Should().NotBeNull();
        saveResult!.DocumentSpeId.Should().Be(speId);

        persisted.Should().NotBeNull("the SPE facade boundary captured the bytes ComposeService actually persisted");

        // ── Assert (Save side, FR-01/E1 + FR-26 survival, byte-level): the imported marks' paragraphs
        //    (0 and 1) are BYTE-IDENTICAL after the dirty save; only paragraph 2 changed; the full
        //    paraId set survives. ──────────────────────────────────────────────────────────────────
        var originalParaIds = BodyParaIds(original);
        var persistedParaIds = BodyParaIds(persisted!);
        persistedParaIds.Should().BeEquivalentTo(originalParaIds,
            "a dirty save must never drop or regenerate a w14:paraId — the E2 anchor substrate the imported marks resolve by");

        var originalParas = ParagraphsById(original);
        var persistedParas = ParagraphsById(persisted!);
        persistedParas[para0Id].Should().Be(originalParas[para0Id],
            "paragraph 0 (carrying the imported insertion) is UNTOUCHED by the dirty edit and must be byte-identical");
        persistedParas[para1Id].Should().Be(originalParas[para1Id],
            "paragraph 1 (carrying the imported deletion + comment anchor) is UNTOUCHED by the dirty edit and must be byte-identical");
        persistedParas[para2Id].Should().NotBe(originalParas[para2Id],
            "paragraph 2 is the ONE paragraph the dirty edit targeted — it must actually change");

        var editedParagraph = OpenBody(persisted!).Descendants<Paragraph>()
            .Single(p => string.Equals(p.ParagraphId?.Value, para2Id, StringComparison.OrdinalIgnoreCase));
        editedParagraph.Descendants<InsertedRun>().Any().Should().BeTrue(
            "the dirty edit on paragraph 2 must be synthesized as a native w:ins tracked-change insertion (FR-01/E1)");

        // Comments live in a SEPARATE OPC part (comments.xml) referenced by CommentRangeStart/End +
        // CommentReference markers inside paragraph 1's (untouched) XML — assert that part itself
        // also survives byte-identical, not just the anchoring paragraph.
        using (var originalDoc = WordprocessingDocument.Open(new MemoryStream(original, writable: false), isEditable: false))
        using (var persistedDoc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false))
        {
            var originalComments = originalDoc.MainDocumentPart!.WordprocessingCommentsPart;
            var persistedComments = persistedDoc.MainDocumentPart!.WordprocessingCommentsPart;
            originalComments.Should().NotBeNull();
            persistedComments.Should().NotBeNull("the comments.xml part must survive a dirty save that never touches the commented paragraph");
            ReadPartBytes(persistedComments!).Should().Equal(ReadPartBytes(originalComments!),
                "comments.xml is never opened by the redline synthesizer (it only opens MainDocumentPart.Document) — byte-identical");
        }

        // ── Phase 3: RELOAD (through the real GET route again) — the through-the-wire round trip
        //    task 052 (FR-26) exists to prove: the imported revisions AND comment recovered on the
        //    FIRST Load are STILL recovered from the PERSISTED bytes, anchored to the SAME paraId. ──
        _fixture.ResetBoundaries();

        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: speId, Name: "redlined-commented.docx", ParentId: null, Size: persisted!.Length,
                CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"v2-etag\"", IsFolder: false, WebUrl: null, DriveId: driveId));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(persisted!));
        _fixture.SpeMock
            .Setup(s => s.GetCurrentVersionIdAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("v-load-2");

        var secondLoadResponse = await client.GetAsync(
            $"/api/compose/documents/{speId}?driveId={driveId}&tenantId={tenant}&sessionId={sessionId}");
        secondLoadResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "the real Load route re-reads the JUST-PERSISTED bytes (the round trip under test)");

        var secondLoad = await secondLoadResponse.Content.ReadFromJsonAsync<LoadComposeDocumentResponse>();
        secondLoad.Should().NotBeNull();

        // ── PRIMARY FR-26 assertions: the ORIGINAL imported revisions survive the open->save->reopen
        //    round trip, NOT dropped, NOT re-flattened, and anchored by w14:paraId to the SAME
        //    paragraphs as before the save (primary anchor — not the fuzzy fallback). The reader (by
        //    design, DocxAnnotationReader has no "imported vs freshly-synthesized" distinction — both
        //    are native w:ins/w:del) ALSO now reports the dirty edit's own tracked change on paragraph
        //    2 (identified below by author "Spaarke Fidelity Seam Test User" vs the imports' "Jordan
        //    Ellis"/"Sam Rivera") — that is FR-01/E1 working correctly, not a survival failure, and is
        //    asserted separately below. The PRIMARY assertion is that the two ORIGINAL, human-authored
        //    revisions are individually still present, identified by their author + text + paraId. ──
        var reloadedInsertion = secondLoad!.ImportedRevisions.Should().ContainSingle(r =>
            r.Kind == RecoveredAnnotationKind.Insertion && r.Author == "Jordan Ellis").Subject;
        var reloadedDeletion = secondLoad.ImportedRevisions.Should().ContainSingle(r =>
            r.Kind == RecoveredAnnotationKind.Deletion && r.Author == "Jordan Ellis").Subject;
        reloadedInsertion.ParaId.Should().Be(para0Id,
            "FR-26/E2: the imported insertion re-anchors to the SAME w14:paraId after the round trip (primary anchor, not fuzzy fallback)");
        reloadedInsertion.Text.Should().Be(" (Vulpes vulpes)", "the imported insertion's own text is untouched");
        reloadedDeletion.ParaId.Should().Be(para1Id,
            "FR-26/E2: the imported deletion re-anchors to the SAME w14:paraId after the round trip (primary anchor, not fuzzy fallback)");
        reloadedDeletion.Text.Should().Be("lazy ", "the imported deletion's own text is untouched");

        // ── Corroborating assertion: the dirty edit's OWN tracked change (paragraph 2) is also
        //    recovered on reopen — proving FR-01/E1 landed as a real, readable native w:ins/w:del, not
        //    silently lost or malformed. ──────────────────────────────────────────────────────────────
        secondLoad.ImportedRevisions.Should().Contain(r =>
            r.ParaId == para2Id && r.Kind == RecoveredAnnotationKind.Insertion,
            "the dirty edit itself lands as a native tracked-change insertion on paragraph 2, also readable on reopen");

        secondLoad.ImportedComments.Should().ContainSingle(
            "FR-26: the imported comment must still be recovered after the round trip");
        var reloadedComment = secondLoad.ImportedComments[0];
        reloadedComment.ParaId.Should().Be(para1Id,
            "FR-26/E2: the imported comment re-anchors to the SAME w14:paraId after the round trip (primary anchor, not fuzzy fallback)");
        reloadedComment.CommentText.Should().Be("Cut this sentence.", "the comment's own body text is untouched");
        reloadedComment.Author.Should().Be("Sam Rivera");

        // ── Corroborating assertion: the FULL paraId set (incl. the just-edited paragraph 2) is
        //    unchanged across the round trip — the same E2 substrate the imported marks resolve by. ──
        secondLoad.ParaIdMap.Select(e => e.ParaId).Should().BeEquivalentTo(
            firstLoad.ParaIdMap.Select(e => e.ParaId),
            "the paraId set must be identical before vs after the round trip");

        // ── Corroborating assertion (FR-01/E1): the dirty edit itself is visible on reopen too — the
        //    edited paragraph's imported-mark-free content reflects the amendment. ────────────────────
        var reopenedEditedParagraph = OpenBody(secondLoad.Content!).Descendants<Paragraph>()
            .Single(p => string.Equals(p.ParagraphId?.Value, para2Id, StringComparison.OrdinalIgnoreCase));
        reopenedEditedParagraph.InnerText.Should().Contain("outside experts",
            "the dirty edit to paragraph 2 is present in the reopened document");
    }

    // ── fixture construction + OOXML helpers ────────────────────────────────────────────────────

    /// <summary>The fixture's REAL, physically-authored <c>w14:paraId</c> values (deterministic — NOT
    /// server-minted). A minted id (produced by <see cref="ParaIdPreParser"/> for a source paragraph
    /// that has none) lives ONLY in the reported <c>ParaIdMap</c>, never physically in the bytes
    /// (design note on <see cref="ParaIdPreParser"/>: "the map, not the bytes, carries minted ids") —
    /// so a save's <c>editedParagraphs</c> key MUST resolve against a paraId the retained-original
    /// bytes ACTUALLY carry, exactly like a genuinely Word-authored document (the 024 fidelity seam's
    /// real CSA fixture). Explicit ids here mirror that Word-authored shape deterministically.</summary>
    private static readonly string[] FixtureParaIds = { "00000001", "00000002", "00000003" };

    /// <summary>Builds a small, deterministic 3-paragraph <c>.docx</c> — EVERY paragraph carrying a
    /// real physical <c>w14:paraId</c> (<see cref="FixtureParaIds"/>), exactly as a genuinely
    /// Word-authored document does — and redlines/comments it via <see cref="DocxAnnotationWriter"/>
    /// (the SAME writer that produces the exact Word-for-Web <c>w:ins</c>/<c>w:del</c>/<c>w:comment</c>
    /// markup the read side + tasks 050/051's own fixtures use): an insertion on paragraph 0, a
    /// deletion + comment on paragraph 1, paragraph 2 left untouched as the eventual dirty-edit
    /// target.</summary>
    private static byte[] BuildRedlinedAndCommentedFixture(string para0, string para1, string para2)
    {
        var source = CreateDocx(para0, para1, para2);
        var annotations = new[]
        {
            new DocxAnnotation { Kind = TrackChangeKind.Insertion, TargetText = "fox", NewText = " (Vulpes vulpes)", Author = "Jordan Ellis", Date = When },
            new DocxAnnotation { Kind = TrackChangeKind.Deletion, TargetText = "lazy ", Author = "Jordan Ellis", Date = When },
            new DocxAnnotation { Kind = TrackChangeKind.Comment, TargetText = "lazy dog", CommentText = "Cut this sentence.", Author = "Sam Rivera", Date = When },
        };
        return new DocxAnnotationWriter().Annotate(source, annotations);
    }

    private static byte[] CreateDocx(params string[] paragraphs)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();
            for (var i = 0; i < paragraphs.Length; i++)
            {
                body.AppendChild(new Paragraph(new Run(new Text(paragraphs[i]) { Space = SpaceProcessingModeValues.Preserve }))
                {
                    ParagraphId = new HexBinaryValue(FixtureParaIds[i]),
                });
            }
            mainPart.Document = new Document(body);
            mainPart.Document.Save();
        }
        return ms.ToArray();
    }

    private static Body OpenBody(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        // Clone so the returned tree survives past the `using` disposal of the source package.
        return (Body)doc.MainDocumentPart!.Document!.Body!.CloneNode(deep: true);
    }

    private static HashSet<string> BodyParaIds(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Select(p => p.ParagraphId?.Value)
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);
    }

    private static Dictionary<string, string> ParagraphsById(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Where(p => !string.IsNullOrEmpty(p.ParagraphId?.Value))
            .ToDictionary(
                p => p.ParagraphId!.Value!.ToUpperInvariant(),
                p => p.OuterXml,
                StringComparer.Ordinal);
    }

    private static byte[] ReadPartBytes(OpenXmlPart part)
    {
        using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
