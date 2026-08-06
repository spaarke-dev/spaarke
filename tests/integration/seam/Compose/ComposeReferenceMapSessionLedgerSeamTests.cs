// Task 041 (spaarkeai-compose-fidelity-r4.5, spec FR-17, design.md §4 WS-4) — the session-ledger
// persistence half of the WS-4 reference layer. Task 040 landed the per-paragraph reference set
// (computedNumber/numberingLevel/listPath/headingLevel) on ComposeDocxProjection.ParaIdMap[] — the
// PROJECTION-PAYLOAD half of the owner's "BOTH payload + session ledger" decision (project
// CLAUDE.md "WS-4 store"). This file proves the SESSION-LEDGER half: ComposeService.LoadAsync now
// persists the SAME paraId -> legal-number map onto ChatSession.ReferenceMap via the EXISTING R4
// three-tier session stack (ADR-040) — no new store.
//
// What this file proves THROUGH THE WIRE (ADR-038 "unit-green != done" — the vertical-slice-seam
// KEEP category exists precisely because a unit test of ComposeService.BuildReferenceMap alone
// would not catch a broken wire between the projection build and the session-ledger write):
//   1. A real Load persists the paraId -> {computedNumber, numberingLevel, listPath, headingLevel}
//      map onto the session — read back via ChatSessionManager.GetSessionAsync directly (NOT via a
//      second projection rebuild) — with values IDENTICAL to what the Load response's own
//      ParaIdMap carries. "Resolves without a recompute divergence" (spec FR-17 acceptance).
//   2. A second Load of an EDITED document (one new paragraph appended; all original paragraphs'
//      physical w14:paraId untouched, mirroring R4's stable-paraId-across-edits invariant) — under
//      the SAME resumed SessionId — keeps the SAME persisted number for every UNCHANGED paraId, and
//      adds a NEW entry for the freshly-minted paraId of the inserted paragraph. Nothing is silently
//      dropped or reconciled (R4 re-anchor is a separate concern this task does not touch).
//
// REUSES (root CLAUDE.md §11 — extend, don't duplicate): ComposeFidelitySeamFixture (real
// ComposeService/ComposeEndpoints/ChatSessionManager wiring; SPE/Dataverse/indexing module-boundary
// mocks only) — the SAME fixture ComposeUploadProjectionSeamTests/ComposeNoOpRoundTripByteDiffSeamTests
// use. ChatSessionManager is NOT mocked — the fixture's real (Redis:Enabled=false, in-memory-
// fallback) registration is resolved from DI, matching production's own read/write contract.
//
// Corpus: tests/fixtures/compose-corpus/heading-style-numbering.docx (task 040's hand-verified
// numbering exemplar — idx=6 "Confidentiality" -> "4", idx=9 nested "Confidentiality" -> "4.2",
// per task-040 notes and ComposeReadFidelityHarnessSeamTests.LegalNumberingExemplars).
//
// NEGATIVE (ADR-038 banned patterns): NO Mock<HttpMessageHandler>, NO DI-registration test, NO
// ctor-null test anywhere in this file.

using System.Net;
using System.Net.Http.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeReferenceMapSessionLedgerSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposeReferenceMapSessionLedgerSeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    private const string CorpusFileName = "heading-style-numbering.docx";

    [Fact]
    public async Task Load_PersistsReferenceMapOntoSessionLedger_ResolvableWithoutRecomputeDivergence()
    {
        _fixture.ResetBoundaries();

        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(LoadCorpusDoc(CorpusFileName));
        var speId = $"spe-item-refmap-{Guid.NewGuid():N}";
        var driveId = $"drive-refmap-{Guid.NewGuid():N}";

        SetupLoadBoundary(driveId, speId, original);

        using var client = _fixture.CreateAuthenticatedClient();

        var loadResponse = await client.GetAsync(
            $"/api/compose/documents/{speId}?driveId={Uri.EscapeDataString(driveId)}&tenantId={Uri.EscapeDataString(ComposeFidelitySeamFixture.TestTenantId)}");
        loadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var load = await loadResponse.Content.ReadFromJsonAsync<LoadComposeDocumentResponse>();
        load.Should().NotBeNull();

        // Sanity precondition: the exemplar doc must actually carry computed numbers (task 040's
        // FR-12 example) — otherwise the divergence check below would trivially pass on all-null data.
        load!.ParaIdMap.Should().Contain(e => e.ComputedNumber != null,
            "the corpus exemplar must project at least one numbered paragraph for this test to be meaningful");

        // ── Act: read the SESSION LEDGER directly — a fresh GetSessionAsync, not a second Load/
        //    projection rebuild — proving the map is genuinely PERSISTED, not merely returned
        //    on the wire from the SAME projection call that produced load.ParaIdMap. ──────────────
        ChatSession? session;
        using (var scope = _fixture.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
            session = await sessions.GetSessionAsync(ComposeFidelitySeamFixture.TestTenantId, load.SessionId, CancellationToken.None);
        }

        session.Should().NotBeNull("Load must persist the resumed/created session with the reference map attached");
        session!.ReferenceMap.Should().NotBeNullOrEmpty(
            "FR-17: the paraId -> legal-number map must persist onto the session ledger (BOTH stores — owner clarification)");

        // Every entry the projection payload carries must resolve, byte-for-byte, from the
        // independently-read session ledger — no recompute divergence (FR-17 acceptance criterion).
        foreach (var expected in load.ParaIdMap)
        {
            var ledgerEntry = session.ReferenceMap!.SingleOrDefault(e => e.ParaId == expected.ParaId);
            ledgerEntry.Should().NotBeNull($"paraId '{expected.ParaId}' present on the projection payload must also be persisted on the session ledger");
            ledgerEntry!.ComputedNumber.Should().Be(expected.ComputedNumber, $"paraId '{expected.ParaId}' computedNumber must match between payload and ledger");
            ledgerEntry.NumberingLevel.Should().Be(expected.NumberingLevel, $"paraId '{expected.ParaId}' numberingLevel must match between payload and ledger");
            ledgerEntry.ListPath.Should().BeEquivalentTo(expected.ListPath, $"paraId '{expected.ParaId}' listPath must match between payload and ledger");
            ledgerEntry.HeadingLevel.Should().Be(expected.HeadingLevel, $"paraId '{expected.ParaId}' headingLevel must match between payload and ledger");
        }
    }

    [Fact]
    public async Task Load_OnEditedReloadOfSameSession_KeepsStableNumbersForUnchangedParagraphsAndPersistsNewEntryForInserted()
    {
        _fixture.ResetBoundaries();

        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(LoadCorpusDoc(CorpusFileName));
        var speId = $"spe-item-refmap-edit-{Guid.NewGuid():N}";
        var driveId = $"drive-refmap-edit-{Guid.NewGuid():N}";

        SetupLoadBoundary(driveId, speId, original);

        using var client = _fixture.CreateAuthenticatedClient();

        // ── First load: mints/persists the baseline session + reference map. ─────────────────────
        var load1Response = await client.GetAsync(
            $"/api/compose/documents/{speId}?driveId={Uri.EscapeDataString(driveId)}&tenantId={Uri.EscapeDataString(ComposeFidelitySeamFixture.TestTenantId)}");
        load1Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var load1 = await load1Response.Content.ReadFromJsonAsync<LoadComposeDocumentResponse>();
        load1.Should().NotBeNull();

        // ── Simulate an edit: append ONE new plain paragraph after the retained bytes Load1
        //    returned (which carry every original paragraph's PHYSICAL w14:paraId, stamped/verified
        //    at Load time). Every original paragraph is untouched — only appended-to — mirroring R4's
        //    "an edit keeps a paragraph's paraId stable" invariant (AnnotationReanchorService remarks).
        var edited = AppendPlainParagraph(load1!.Content, "A newly inserted paragraph from the edit.");
        SetupLoadBoundary(driveId, speId, edited);

        // ── Second load: resumes the SAME session (sessionId supplied), over the EDITED bytes. ────
        var load2Response = await client.GetAsync(
            $"/api/compose/documents/{speId}?driveId={Uri.EscapeDataString(driveId)}&tenantId={Uri.EscapeDataString(ComposeFidelitySeamFixture.TestTenantId)}&sessionId={Uri.EscapeDataString(load1.SessionId)}");
        load2Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var load2 = await load2Response.Content.ReadFromJsonAsync<LoadComposeDocumentResponse>();
        load2.Should().NotBeNull();

        load2!.SessionId.Should().Be(load1.SessionId, "a sessionId bound to the SAME document must be RESUMED, not replaced");

        var originalParaIds = load1.ParaIdMap.Select(e => e.ParaId).ToHashSet(StringComparer.Ordinal);
        var editedParaIds = load2.ParaIdMap.Select(e => e.ParaId).ToHashSet(StringComparer.Ordinal);

        var unchangedParaIds = originalParaIds.Intersect(editedParaIds).ToList();
        unchangedParaIds.Should().NotBeEmpty("the edit only appended a paragraph — every original paragraph's physical w14:paraId is untouched");

        var newParaIds = editedParaIds.Except(originalParaIds).ToList();
        newParaIds.Should().ContainSingle("the inserted paragraph has no physical w14:paraId and must be freshly minted at the second Load");

        // ── Read the PERSISTED session ledger directly (not the wire response) after the edit. ────
        ChatSession? session;
        using (var scope = _fixture.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
            session = await sessions.GetSessionAsync(ComposeFidelitySeamFixture.TestTenantId, load2.SessionId, CancellationToken.None);
        }

        session.Should().NotBeNull();
        session!.ReferenceMap.Should().NotBeNullOrEmpty();

        // Unchanged paragraphs keep a STABLE persisted number across the edit + reload round trip.
        foreach (var paraId in unchangedParaIds)
        {
            var before = load1.ParaIdMap.Single(e => e.ParaId == paraId);
            var afterLedger = session.ReferenceMap!.SingleOrDefault(e => e.ParaId == paraId);

            afterLedger.Should().NotBeNull($"unchanged paraId '{paraId}' must still be present on the persisted session ledger after the edit + reload");
            afterLedger!.ComputedNumber.Should().Be(before.ComputedNumber, $"unchanged paraId '{paraId}' must keep its STABLE computed number across the edit (spec FR-17)");
            afterLedger.NumberingLevel.Should().Be(before.NumberingLevel);
            afterLedger.ListPath.Should().BeEquivalentTo(before.ListPath);
            afterLedger.HeadingLevel.Should().Be(before.HeadingLevel);
        }

        // The newly-inserted paragraph's freshly-minted paraId is persisted too — not silently dropped.
        var newParaId = newParaIds.Single();
        session.ReferenceMap!.Should().Contain(e => e.ParaId == newParaId,
            "the new/split paragraph's paraId must appear as a NEW entry on the persisted session ledger (R4 re-anchor — never a silent number loss)");
    }

    // ── Shared arrange + OOXML helpers ────────────────────────────────────────────────────────────

    private void SetupLoadBoundary(string driveId, string speId, byte[] bytes)
    {
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: speId, Name: CorpusFileName, ParentId: null,
                Size: bytes.Length, CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow, ETag: "\"v1-etag\"", IsFolder: false,
                WebUrl: null, DriveId: driveId));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult<Stream?>(new MemoryStream(bytes)));
    }

    private static string LoadCorpusDoc(string fileName)
    {
        var corpusDir = Path.GetDirectoryName(ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First())!;
        return Path.Combine(corpusDir, fileName);
    }

    /// <summary>Appends ONE new plain (no physical w14:paraId, no numbering) paragraph immediately
    /// before the body's closing sectPr — simulating an edit that inserts a paragraph without
    /// disturbing any existing paragraph's physical identity (mirrors R4's stable-paraId-across-
    /// edits invariant this test relies on).</summary>
    private static byte[] AppendPlainParagraph(byte[] source, string text)
    {
        using var stream = new MemoryStream();
        stream.Write(source, 0, source.Length);
        stream.Position = 0;

        using (var doc = WordprocessingDocument.Open(stream, isEditable: true))
        {
            var body = doc.MainDocumentPart!.Document!.Body!;
            var newParagraph = new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

            var sectPr = body.Elements<SectionProperties>().FirstOrDefault();
            if (sectPr is not null)
            {
                body.InsertBefore(newParagraph, sectPr);
            }
            else
            {
                body.AppendChild(newParagraph);
            }

            doc.MainDocumentPart.Document.Save();
        }

        return stream.ToArray();
    }
}
