// Task 024 — FR-07/FR-01a/NFR-06 through-the-wire fidelity seam slice test (KEEP path: seam).
//
// THE E2E DEFINITION-OF-DONE this closes (spec NFR-06 + NFR-07, amended 2026-07-18): every prior
// fidelity assertion for the E1 delta-save cutover (task 022) and the born-in-editor renderer
// (task 026) ran at the SERVICE-UNIT level — calling `ComposeParagraphRedlineSynthesizer` /
// `ComposeDocumentRenderer` directly (see `Nfr09RealTemplateHardeningTests`,
// `ComposeDocumentRendererTests`). None of those drove the REAL `/api/compose/documents/*` routes
// end to end, so a broken wire between `ComposeEndpoints` → `SaveComposeDocumentRequest` →
// `ComposeService.SaveAsync` → the `SpeFileStore`/`ISpeFileOperations` facade would NOT have been
// caught — exactly the "unit-green ≠ done" gap ADR-038 §"vertical-slice-seam" exists to close.
//
// TWO seam paths, matching the 2026-07-18 re-architecture amendment on this task's POML:
//
//   (A) LOADED-DOCUMENT DIRTY SAVE — POSTs an edit ({content, editedParagraphs}) to a REAL,
//       Word-authored, richly-formatted fixture (letterhead headers/footers, a 9-level clause
//       numbering definition, custom styles, footnotes, 6 tables incl. 3 nested — the same
//       `commonpaper-cloud-service-agreement.docx` fixture task 003's NFR-09 hardening gate
//       proved this exact engine against) through the REAL `POST .../save` route, captures the
//       persisted bytes at the `ISpeFileOperations.ReplaceFileContentAsUserAsync` facade boundary,
//       and asserts BYTE-IDENTITY (not merely structural equivalence — amended FR-07) for every
//       UNTOUCHED paragraph + every non-body part (headers/footers/footnotes/styles/numbering,
//       which `ComposeParagraphRedlineSynthesizer` never opens), while the 3 EDITED paragraphs
//       carry native `w:ins`/`w:del` tracked changes.
//
//   (B) BORN-IN-EDITOR CREATE-ON-SAVE — POSTs a paraId-keyed `contentModel` (a 1/1.1/1.1.1 clause
//       tree) through the REAL `POST .../create-on-save` route, captures the SERVER-RENDERED bytes
//       at the `ISpeFileOperations.UploadSmallAsUserAsync` facade boundary, and asserts the
//       rendered `.docx` is schema-valid, round-trips through `ParaIdPreParser` (the Load-path
//       pre-parse) with a unique `w14:paraId` per paragraph, and renders the clause tree as a
//       STYLE-LINKED multi-level `abstractNum` (the NUMBERING GOLDEN-FILE — the single largest
//       net-new authoring surface, per `ComposeDocumentRendererTests`). A SECOND real `POST
//       .../save` (replace path) then applies a tracked-change edit to the rendered doc and
//       asserts the numbering part is BYTE-IDENTICAL afterward — proving a subsequent edit through
//       the real save route does not corrupt the just-authored numbering.
//
// KEEP-path classification (ADR-038 §"vertical-slice-seam" + tests/CLAUDE.md): `tests/integration/
// seam/**`. Mirrors the shape of `ComposeDocSessionDispatchSeamTests.cs` (WebApplicationFactory +
// real route + facade-boundary capture) and the mock-boundary discipline of
// `ComposeCreateOnSaveEndpointContractTests.cs` (KEEP the real ComposeService; mock ONLY the
// external SPE / Dataverse-entity / indexing module boundaries it depends on).
//
// Banned-pattern compliance (ADR-038 §4 + tests/CLAUDE.md): NO Mock<HttpMessageHandler>; NO
// DI-registration test; NO ctor-null test. Mocks live ONLY at the ISpeFileOperations /
// IGenericEntityService / IPostUploadIndexingEnqueuer module boundaries — the HTTP boundary, the
// real ComposeEndpoints route mapping, and the real ComposeService/ComposeParagraphRedlineSynthesizer/
// ComposeDocumentRenderer/ParaIdPreParser are all production types.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

public sealed class ComposeFidelitySeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposeFidelitySeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (A) Loaded-document dirty save — real formatted-doc fixture through the real /save route.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DirtySave_OnRealFormattedFixture_PreservesUntouchedOoxmlByteIdentical_AndCarriesEditsAsTrackedChanges()
    {
        _fixture.ResetBoundaries();

        var original = LoadCsaFixtureBytes();
        var originalParaIds = BodyParaIds(original);
        originalParaIds.Should().NotBeEmpty("the real Word-authored CSA fixture carries w14:paraId on every body paragraph");

        // Pick 3 real, non-trivial paragraphs to edit (mirrors the NFR-09 hardening gate's own
        // paragraph-selection convention on this same fixture).
        var targets = EditableParagraphs(original, minLen: 25).Take(3).ToList();
        targets.Should().HaveCountGreaterThanOrEqualTo(3);
        var edits = targets
            .Select(t => new { paraId = t.ParaId, newText = t.Text + " (as amended)" })
            .ToList();
        var editedIdSet = targets.Select(t => t.ParaId.ToUpperInvariant()).ToHashSet();

        const string speId = "spe-item-fidelity-loaded-001";
        const string driveId = "drive-fidelity-loaded-001";
        var existingDocumentId = Guid.NewGuid();

        string sessionId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
            var session = await sessions.CreateSessionAsync(ComposeFidelitySeamFixture.TestTenantId, documentId: speId);
            sessionId = session.SessionId;
        }

        // ── SPE facade boundary: capture the bytes ComposeService actually persists ──────────────
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
                Id: speId, Name: "commonpaper-cloud-service-agreement.docx", ParentId: null,
                Size: original.Length, CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow, ETag: "\"v2-etag\"", IsFolder: false,
                WebUrl: null, DriveId: driveId));

        // The document is already promoted (this is a REPLACE save, not a first-Save) — the
        // idempotent alt-key lookup finds the existing row, so no CreateAsync fires.
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", existingDocumentId));

        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        using var client = _fixture.CreateAuthenticatedClient();

        // ── Act: the REAL wire contract task 027's client sends for a loaded-doc dirty save —
        //    the same-session fast-path retained-original `content` + the paraId-keyed
        //    `editedParagraphs` delta (see SaveComposeDocumentBody / ComposeEndpoints.Save). ──────
        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = ComposeFidelitySeamFixture.TestTenantId,
            driveId,
            content = original,
            editedParagraphs = edits,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the real save route resolves the fast-path baseline + synthesizes the redline over the real wire");
        var result = await response.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        result.Should().NotBeNull();
        result!.DocumentSpeId.Should().Be(speId);

        persisted.Should().NotBeNull("the SPE facade boundary captured the bytes ComposeService actually persisted");

        using var originalDoc = WordprocessingDocument.Open(new MemoryStream(original, writable: false), isEditable: false);
        using var persistedDoc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false);

        // ── Assert 1: the FULL paraId set survives (Option C preserves every w14:paraId by
        //    construction — only run content of the edited paragraphs is rewritten). ────────────
        var persistedParaIds = BodyParaIds(persisted!);
        persistedParaIds.Should().BeEquivalentTo(originalParaIds,
            "a dirty save must never drop or regenerate a w14:paraId — the E2 anchor substrate depends on it");

        // ── Assert 2 (amended FR-07 — BYTE-IDENTICAL, not merely structurally equivalent): every
        //    non-body part the synthesizer never opens (headers/footers/footnotes/styles/numbering)
        //    is raw-byte identical before vs after the dirty save. ──────────────────────────────
        AssertNonBodyPartsByteIdentical(originalDoc, persistedDoc);

        // ── Assert 3 (amended FR-07 — paragraph-level byte identity): every UNTOUCHED paragraph's
        //    exact OOXML (OuterXml — attributes, pPr, every run) is unchanged; only the 3 edited
        //    paragraphs' XML differs. ─────────────────────────────────────────────────────────────
        var originalParasByid = ParagraphsById(originalDoc);
        var persistedParasById = ParagraphsById(persistedDoc);
        persistedParasById.Keys.Should().BeEquivalentTo(originalParasByid.Keys);

        var untouchedCount = 0;
        foreach (var (paraId, originalXml) in originalParasByid)
        {
            if (editedIdSet.Contains(paraId))
            {
                continue;
            }

            untouchedCount++;
            persistedParasById[paraId].Should().Be(originalXml,
                $"untouched paragraph {paraId} must be BYTE-IDENTICAL after a dirty save (amended FR-07, Option C)");
        }

        untouchedCount.Should().BeGreaterThan(300, "the CSA fixture has 345 body paragraphs; only 3 were edited");

        // ── Assert 4: the 3 EDITED paragraphs carry native w:ins/w:del tracked changes, and their
        //    XML DID change (a sanity contrast to Assert 3). ────────────────────────────────────
        foreach (var editedId in editedIdSet)
        {
            persistedParasById[editedId].Should().NotBe(originalParasByid[editedId],
                $"edited paragraph {editedId} must actually change");

            var editedParagraph = persistedDoc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
                .Single(p => string.Equals(p.ParagraphId?.Value, editedId, StringComparison.OrdinalIgnoreCase));
            (editedParagraph.Descendants<InsertedRun>().Any() || editedParagraph.Descendants<DeletedRun>().Any())
                .Should().BeTrue($"edited paragraph {editedId} must carry a tracked-change insertion or deletion");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (B) Born-in-editor create-on-save — content model → server-rendered .docx → numbering
    //     golden-file → round-trip → a subsequent tracked-change edit that doesn't corrupt it.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task BornInEditorCreateOnSave_RendersNumberingGoldenFile_RoundTrips_AndSurvivesSubsequentTrackedChangeEdit()
    {
        _fixture.ResetBoundaries();

        const string containerId = "b!container-fidelity-born-001";
        const string mintedSpeId = "spe-item-fidelity-born-001";
        const string resolvedDriveId = "drive-fidelity-born-001";
        var newDocumentId = Guid.NewGuid();

        string sessionId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
            var session = await sessions.CreateSessionAsync(ComposeFidelitySeamFixture.TestTenantId, documentId: null);
            sessionId = session.SessionId;
        }

        // ── SPE facade boundary: capture the SERVER-RENDERED bytes (ComposeDocumentRenderer). ────
        byte[]? rendered = null;
        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedDriveId);
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, CancellationToken>((_, _, _, stream, _) =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                rendered = ms.ToArray();
            })
            .ReturnsAsync(new FileHandleDto(
                Id: mintedSpeId, Name: "draft.docx", ParentId: null, Size: 0,
                CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"v1-etag\"", IsFolder: false, WebUrl: null, DriveId: resolvedDriveId));

        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        _fixture.DataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newDocumentId);

        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        using var client = _fixture.CreateAuthenticatedClient();

        // ── Act 1: create-on-save with a paraId-keyed 1/1.1/1.1.1 clause-tree contentModel — the
        //    real wire contract task 027's client sends for a born-in-editor first Save. ─────────
        var createResponse = await client.PostAsJsonAsync("/api/compose/documents/create-on-save", new
        {
            containerId,
            tenantId = ComposeFidelitySeamFixture.TestTenantId,
            sessionId,
            displayName = "New Agreement.docx",
            contentModel = new
            {
                blocks = new object[]
                {
                    new { kind = "heading", level = 1, runs = new[] { new { text = "Definitions" } } },
                    new { kind = "heading", level = 2, runs = new[] { new { text = "Confidential Information" } } },
                    new { kind = "heading", level = 3, runs = new[] { new { text = "Exclusions" } } },
                },
            },
        });

        createResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "the real create-on-save route reaches ComposeService.SaveAsync's ContentModel render branch");
        var createResult = await createResponse.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        createResult.Should().NotBeNull();
        createResult!.DocumentSpeId.Should().Be(mintedSpeId);
        createResult.DocumentRecordId.Should().Be(newDocumentId);
        createResult.WasPromotedThisSave.Should().BeTrue("the first Save of a born-in-editor draft creates the record");

        rendered.Should().NotBeNull("the SPE facade boundary captured the SERVER-RENDERED bytes (ComposeDocumentRenderer)");

        // ── Assert: well-formed / Word-openable (schema-valid OpenXML). ─────────────────────────
        using (var doc = WordprocessingDocument.Open(new MemoryStream(rendered!, writable: false), isEditable: false))
        {
            var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc).ToList();
            var detail = string.Join("\n", errors.Select(e => $"{e.Path?.XPath}: {e.Description}"));
            Assert.True(errors.Count == 0, "the server-rendered .docx must be schema-valid — offenders:\n" + detail);
        }

        // ── Assert: round-trips through ParaIdPreParser (the Load-path pre-parse), one entry per
        //    paragraph, every id already present (nothing minted at Load — the renderer already
        //    assigned them), and every id unique. ────────────────────────────────────────────────
        var preParse = new ParaIdPreParser().Parse(rendered!);
        preParse.Entries.Should().HaveCount(3);
        preParse.Entries.Should().OnlyContain(e => e.IsMinted == false,
            "the rendered doc's paraIds are read back verbatim by the Load-path pre-parse");
        preParse.Entries.Select(e => e.ParaId).Should().OnlyHaveUniqueItems();
        preParse.Entries.Should().OnlyContain(e => IsValidParaId(e.ParaId));

        // ── Assert: the NUMBERING GOLDEN-FILE — the clause tree is authored as a STYLE-LINKED
        //    multi-level abstractNum (the single largest net-new authoring surface; pins the same
        //    shape ComposeDocumentRendererTests establishes at the unit level, now proven through
        //    the real wire). ───────────────────────────────────────────────────────────────────
        using (var doc = WordprocessingDocument.Open(new MemoryStream(rendered!, writable: false), isEditable: false))
        {
            AssertHeadingNumberingGoldenShape(doc);
        }

        // ── Act 2: a SECOND real POST through the replace route — a tracked-change edit onto the
        //    just-rendered document, exercising the SAME real save route the client uses for every
        //    subsequent edit. ─────────────────────────────────────────────────────────────────────
        string editedHeadingParaId;
        byte[] numberingBeforeEdit;
        using (var doc = WordprocessingDocument.Open(new MemoryStream(rendered!, writable: false), isEditable: false))
        {
            editedHeadingParaId = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
                .ElementAt(1).ParagraphId!.Value!; // the middle heading ("Confidential Information", Heading2)
            numberingBeforeEdit = ReadPartBytes(doc.MainDocumentPart!.NumberingDefinitionsPart!);
        }

        // The document is now promoted (Act 1 created the row) — idempotent lookup finds it.
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", newDocumentId));

        byte[]? editedBytes = null;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, CancellationToken>((_, _, _, stream, _) =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                editedBytes = ms.ToArray();
            })
            .ReturnsAsync(new FileHandleDto(
                Id: mintedSpeId, Name: "draft.docx", ParentId: null, Size: rendered!.Length,
                CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"v2-etag\"", IsFolder: false, WebUrl: null, DriveId: resolvedDriveId));

        var editResponse = await client.PostAsJsonAsync($"/api/compose/documents/{mintedSpeId}/save", new
        {
            sessionId,
            tenantId = ComposeFidelitySeamFixture.TestTenantId,
            driveId = resolvedDriveId,
            content = rendered,
            editedParagraphs = new[]
            {
                new { paraId = editedHeadingParaId, newText = "Confidential Information and Trade Secrets" },
            },
        });

        editResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "a tracked-change edit onto the just-rendered born-in-editor doc goes through the SAME real save route");

        editedBytes.Should().NotBeNull();

        using var editedDoc = WordprocessingDocument.Open(new MemoryStream(editedBytes!, writable: false), isEditable: false);

        // ── Assert: numbering is BYTE-IDENTICAL before vs after the edit — the tracked-change
        //    synthesizer only opens MainDocumentPart.Document; NumberingDefinitionsPart is never
        //    touched, so the just-authored style-linked numbering survives a subsequent edit intact. ──
        ReadPartBytes(editedDoc.MainDocumentPart!.NumberingDefinitionsPart!).Should().Equal(numberingBeforeEdit,
            "a subsequent tracked-change edit through the real save route must not corrupt the just-rendered numbering");

        // ── Assert: the edit landed as a tracked change, and the heading is still style-linked
        //    (Heading2, no direct numPr — never double-numbered). ─────────────────────────────────
        var editedHeading = editedDoc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Single(p => string.Equals(p.ParagraphId?.Value, editedHeadingParaId, StringComparison.OrdinalIgnoreCase));
        editedHeading.Descendants<InsertedRun>().Any().Should().BeTrue("the edit synthesized a tracked-change insertion");
        var editedPPr = editedHeading.GetFirstChild<ParagraphProperties>();
        editedPPr!.GetFirstChild<ParagraphStyleId>()!.Val!.Value.Should().Be("Heading2");
        editedPPr.GetFirstChild<NumberingProperties>().Should().BeNull(
            "the edited heading stays numbered THROUGH its style — a direct numPr would double-number it");
    }

    // ── shared fixture bytes + OOXML helpers ─────────────────────────────────────────────────────

    private static byte[] LoadCsaFixtureBytes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Compose", "RealTemplates",
            "commonpaper-cloud-service-agreement.docx");
        File.Exists(path).Should().BeTrue(
            "the real-template fixture must be copied to the test output (see csproj Content Include)");
        return File.ReadAllBytes(path);
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

    private static Dictionary<string, string> ParagraphsById(WordprocessingDocument doc) =>
        doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Where(p => !string.IsNullOrEmpty(p.ParagraphId?.Value))
            .ToDictionary(
                p => p.ParagraphId!.Value!.ToUpperInvariant(),
                p => p.OuterXml,
                StringComparer.Ordinal);

    private static List<(string ParaId, string Text)> EditableParagraphs(byte[] docx, int minLen)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        var result = new List<(string, string)>();
        foreach (var p in doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>())
        {
            var pid = p.ParagraphId?.Value;
            var text = p.InnerText;
            if (!string.IsNullOrEmpty(pid) && text.Length >= minLen)
            {
                result.Add((pid!, text));
            }
        }

        return result;
    }

    private static byte[] ReadPartBytes(OpenXmlPart part)
    {
        using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Asserts every non-body part <c>ComposeParagraphRedlineSynthesizer</c> never opens (it loads +
    /// saves ONLY <c>MainDocumentPart.Document</c>) is raw-byte identical before vs after a dirty save —
    /// headers, footers, footnotes, styles, and numbering.
    /// </summary>
    private static void AssertNonBodyPartsByteIdentical(WordprocessingDocument original, WordprocessingDocument persisted)
    {
        var originalHeadersFooters = HeaderFooterPartBytesByUri(original);
        var persistedHeadersFooters = HeaderFooterPartBytesByUri(persisted);
        persistedHeadersFooters.Keys.Should().BeEquivalentTo(originalHeadersFooters.Keys,
            "the header/footer part set survives a dirty save unchanged");
        foreach (var (uri, bytes) in originalHeadersFooters)
        {
            persistedHeadersFooters[uri].Should().Equal(bytes,
                $"header/footer part '{uri}' is never opened by the redline synthesizer — byte-identical");
        }

        ReadPartBytes(persisted.MainDocumentPart!.FootnotesPart!).Should().Equal(
            ReadPartBytes(original.MainDocumentPart!.FootnotesPart!),
            "footnotes.xml is never opened by the redline synthesizer — byte-identical");
        ReadPartBytes(persisted.MainDocumentPart!.StyleDefinitionsPart!).Should().Equal(
            ReadPartBytes(original.MainDocumentPart!.StyleDefinitionsPart!),
            "styles.xml is never opened by the redline synthesizer — byte-identical");
        ReadPartBytes(persisted.MainDocumentPart!.NumberingDefinitionsPart!).Should().Equal(
            ReadPartBytes(original.MainDocumentPart!.NumberingDefinitionsPart!),
            "numbering.xml is never opened by the redline synthesizer — byte-identical");
    }

    private static Dictionary<string, byte[]> HeaderFooterPartBytesByUri(WordprocessingDocument doc)
    {
        var dict = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var header in doc.MainDocumentPart!.HeaderParts)
        {
            dict[header.Uri.OriginalString] = ReadPartBytes(header);
        }

        foreach (var footer in doc.MainDocumentPart!.FooterParts)
        {
            dict[footer.Uri.OriginalString] = ReadPartBytes(footer);
        }

        return dict;
    }

    /// <summary>
    /// Pins the heading numbering.xml SHAPE for a 1/1.1/1.1.1 clause tree: for each of the 9 levels,
    /// (ilvl, pStyle-or-none, lvlText, numFmt). Levels 0-2 style-link Heading1..3 (the clause tree this
    /// test renders reaches level 3); levels 3-8 are decimal-cascaded but unused/unlinked. Mirrors the
    /// golden-file shape <c>ComposeDocumentRendererTests.SynthesizeDocument_HeadingNumberingXml_
    /// MatchesGoldenShape</c> establishes at the unit level — proven here through the real wire.
    /// </summary>
    private static void AssertHeadingNumberingGoldenShape(WordprocessingDocument doc)
    {
        var numbering = doc.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!;
        var headingAbstract = numbering.Elements<AbstractNum>().Single(a => a.AbstractNumberId?.Value == 0);
        headingAbstract.GetFirstChild<MultiLevelType>()!.Val!.Value.Should().Be(MultiLevelValues.Multilevel,
            "a clause scheme is ONE multilevel abstractNum, not per-level singles");

        var shape = headingAbstract.Elements<Level>()
            .OrderBy(l => l.LevelIndex!.Value)
            .Select(l => (
                Ilvl: l.LevelIndex!.Value,
                Style: l.GetFirstChild<ParagraphStyleIdInLevel>()?.Val?.Value ?? "-",
                LvlText: l.GetFirstChild<LevelText>()!.Val!.Value!,
                Fmt: l.GetFirstChild<NumberingFormat>()!.Val!.Value))
            .ToList();

        shape.Should().Equal(new List<(int, string, string, NumberFormatValues)>
        {
            (0, "Heading1", "%1", NumberFormatValues.Decimal),
            (1, "Heading2", "%1.%2", NumberFormatValues.Decimal),
            (2, "Heading3", "%1.%2.%3", NumberFormatValues.Decimal),
            (3, "Heading4", "%1.%2.%3.%4", NumberFormatValues.Decimal),
            (4, "Heading5", "%1.%2.%3.%4.%5", NumberFormatValues.Decimal),
            (5, "Heading6", "%1.%2.%3.%4.%5.%6", NumberFormatValues.Decimal),
            (6, "-", "%1.%2.%3.%4.%5.%6.%7", NumberFormatValues.Decimal),
            (7, "-", "%1.%2.%3.%4.%5.%6.%7.%8", NumberFormatValues.Decimal),
            (8, "-", "%1.%2.%3.%4.%5.%6.%7.%8.%9", NumberFormatValues.Decimal),
        });

        // ONE num instance backs the heading abstract; headings carry NO direct numPr (style-linked only).
        var headingNum = numbering.Elements<NumberingInstance>().Single(n => n.NumberID?.Value == 1);
        headingNum.GetFirstChild<AbstractNumId>()!.Val!.Value.Should().Be(0);

        foreach (var heading in doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>())
        {
            heading.GetFirstChild<ParagraphProperties>()?.GetFirstChild<NumberingProperties>().Should().BeNull(
                "a DIRECT paragraph numId alongside the style numId is exactly the double-numbering FR-27 forbids");
        }
    }

    private static bool IsValidParaId(string id) =>
        id.Length == 8
        && uint.TryParse(id, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v)
        && v > 0 && v < 0x80000000u;
}

// ════════════════════════════════════════════════════════════════════════════════════════════════
// Fixture — keeps the REAL ComposeService (+ ComposeParagraphRedlineSynthesizer + ComposeDocumentRenderer
// + ParaIdPreParser); mocks ONLY the external SPE / Dataverse-entity / indexing module boundaries, per
// ADR-038 §"mock at module boundaries" and the ComposeCreateOnSaveFixture precedent. Config-key set
// mirrors the canonical Compose fixtures (bff-extensions.md §F.2 Fixture-Config-FIRST).
// ════════════════════════════════════════════════════════════════════════════════════════════════

public sealed class ComposeFidelitySeamFixture : WebApplicationFactory<Program>
{
    public const string TestTenantId = "tenant-compose-fidelity-seam-001";

    public Mock<ISpeFileOperations> SpeMock { get; } = new(MockBehavior.Loose);
    public Mock<IGenericEntityService> DataverseMock { get; } = new(MockBehavior.Loose);
    public Mock<IPostUploadIndexingEnqueuer> IndexingMock { get; } = new(MockBehavior.Loose);

    /// <summary>Resets the boundary mocks between tests (xUnit runs a class's [Fact]s sequentially
    /// against the same IClassFixture instance).</summary>
    public void ResetBoundaries()
    {
        SpeMock.Reset();
        DataverseMock.Reset();
        IndexingMock.Reset();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["Cors:AllowedOrigins:0"] = "https://localhost:5173",
                ["UAMI_CLIENT_ID"] = "test-client-id",
                ["TENANT_ID"] = "test-tenant-id",
                ["API_APP_ID"] = "test-app-id",
                ["API_CLIENT_SECRET"] = "test-secret",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-app-id",
                ["AzureAd:Audience"] = "api://test-app-id",
                ["Graph:TenantId"] = "test-tenant-id",
                ["Graph:ClientId"] = "test-client-id",
                ["Graph:ClientSecret"] = "test-client-secret",
                ["Graph:UseManagedIdentity"] = "false",
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",
                ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
                ["Dataverse:ClientSecret"] = "test-client-secret",
                ["Dataverse:TenantId"] = "test-tenant-id",
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["ServiceBus:QueueName"] = "sdap-jobs",
                ["DocumentIntelligence:Enabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "test-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
                ["Analysis:Enabled"] = "true",
                ["Analysis:UseStubResolver"] = "true",
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["OfficeRateLimit:Enabled"] = "false",
                ["Redis:Enabled"] = "false",
                ["Redis:AllowInMemoryFallback"] = "true",
                ["ModelSelector:DefaultModel"] = "gpt-4o",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",
                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",
                ["SpeAdmin:KeyVaultUri"] = "https://test.vault.azure.net/",
                ["ManagedIdentity:ClientId"] = "test-managed-identity-client-id",
                ["CosmosPersistence:Endpoint"] = "https://test.documents.azure.com:443/",
                ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",
                ["AgentService:Enabled"] = "false",
                ["AgentService:Endpoint"] = "https://test.services.ai.azure.com/api/projects/test-project",
                ["AgentService:AgentId"] = "test-agent-id",
                ["AgentService:MaxConcurrency"] = "4",
                ["AgentService:ThreadCacheExpiryMinutes"] = "60",
            };
            config.AddInMemoryCollection(dict);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options =>
            {
                options.ThrowOnBadRequest = false;
            });

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = ComposeFidelitySeamFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = ComposeFidelitySeamFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, ComposeFidelitySeamFakeAuthHandler>(
                ComposeFidelitySeamFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = ComposeFidelitySeamFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = ComposeFidelitySeamFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            services.RemoveAll<IHostedService>();

            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);

            // ── The point of this fixture: KEEP the real ComposeService (+ redline synthesizer +
            //    document renderer + paraId pre-parser); mock ONLY the external SPE / Dataverse-entity
            //    / indexing boundaries it depends on (ADR-038 §"mock at module boundaries"). ──────────
            services.RemoveAll<ISpeFileOperations>();
            services.AddSingleton(SpeMock.Object);

            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(DataverseMock.Object);

            services.RemoveAll<IPostUploadIndexingEnqueuer>();
            services.AddSingleton(IndexingMock.Object);
        });
    }

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }
}

/// <summary>Fake auth handler emitting oid + tid (+ name) claims — the Save/create-on-save endpoints
/// resolve the revision author from the `name` claim (ComposeService.ResolveRevisionAuthor).</summary>
internal sealed class ComposeFidelitySeamFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ComposeFidelitySeamFakeAuth";

    public ComposeFidelitySeamFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));
        }

        var oid = Guid.NewGuid().ToString();
        var claims = new List<Claim>
        {
            new("oid", oid),
            new("tid", ComposeFidelitySeamFixture.TestTenantId),
            new(System.Security.Claims.ClaimTypes.NameIdentifier, oid),
            new(System.Security.Claims.ClaimTypes.Name, "Spaarke Fidelity Seam Test User"),
            new("name", "Spaarke Fidelity Seam Test User"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
