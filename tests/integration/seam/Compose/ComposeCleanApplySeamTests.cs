// Task 021 (spaarkeai-compose-r5, FR-02 / gap G2 / REQ-1 — R5-D2 Candidate A) — the THROUGH-THE-WIRE proof
// that an AUTHORED-origin document's cross-session edits apply CLEAN (zero w:ins/w:del) while an
// IMPORTED-origin document's edits stay TRACKED, both driven by the DURABLE sprk_composeorigin marker the
// server reads (never inferred from SPE-id/content — NFR-02/I-7), over the REAL
// POST /api/compose/documents/{id}/save route: endpoint -> ComposeService.SaveAsync -> ReadPersistedOrigin
// -> ComposeShadowPatchEngine.Apply(trackChanges:false|true) -> SPE facade.
//
// Candidate A (engine clean-apply BRANCH over retained bytes) was selected by the task-003 Phase-0 spike and
// reaffirmed by the operator 2026-07-29 as the highest-fidelity path (notes/g2-clean-apply-decision.md):
// only document.xml is re-serialized; every other package part + untouched paragraph subtree stays
// byte-identical (I-4/NFR-01). It is NOT a re-author-from-content-model (which drops headers/footers/styles
// on rich docs). The two byte-authors are NOT merged — this is a mode on the existing delta-applier.
//
// Reuses (root CLAUDE.md §11): ComposeFidelitySeamFixture + ComposeOoxmlPackagePartComparer (task 034); the
// /save arrange mirrors ComposeFidelitySeamTests / ConcurrencySaveSeamTests. No new fixture class.
//
// ADR-038 seam DoD: through-the-wire WebApplicationFactory slices only. NO Mock<HttpMessageHandler>, NO
// DI-registration test, NO ctor-null test. Mocks live only at the ISpeFileOperations / IGenericEntityService
// / IPostUploadIndexingEnqueuer boundaries.

using System.Net;
using System.Net.Http.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeCleanApplySeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private const int AuthoredOptionValue = 100000000;
    private const int ImportedOptionValue = 100000001;
    private const string ComposeOriginAttribute = "sprk_composeorigin";

    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposeCleanApplySeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    private static readonly string[] Paragraphs =
    {
        "This Master Services Agreement governs the engagement between the parties.",
        "Confidential Information shall not be disclosed to any third party.",
        "This clause shall survive termination of the Agreement.",
    };

    private static readonly string[] ParaIds = { "0B000001", "0B000002", "0B000003" };

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1. AUTHORED origin → an insert + a delete apply CLEAN: zero w:ins/w:del, the new text is a PLAIN
    //    run, the deleted run is physically gone, untouched paragraphs stay byte-identical.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_AuthoredOrigin_InsertAndDelete_AppliesCleanNoTrackedMarkup_ThroughTheWire()
    {
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);
        var otherParasBefore = ReadOtherParagraphOuterXml(original, excluded: new[] { ParaIds[0], ParaIds[1] });

        var (response, persisted) = await PostAuthoredSaveAsync(
            original,
            storedOriginOptionValue: AuthoredOptionValue,
            operations: new object[]
            {
                new { type = "insertText", paraId = ParaIds[0], at = new { runIndex = 0, offset = 0 }, text = "[CLEAN-INSERT] " },
                new { type = "deleteRange", paraId = ParaIds[1], range = new { start = new { runIndex = 0, offset = 0 }, end = new { runIndex = 0, offset = Paragraphs[1].Length } } },
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an authored-origin op-log save must succeed via the engine clean branch");
        persisted.Should().NotBeNull("the SPE facade must have captured the clean-patched bytes");

        using var doc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false);
        var body = doc.MainDocumentPart!.Document!.Body!;

        // ── The whole document carries ZERO tracked-change markup (criterion 1). ──────────────────────
        body.Descendants<InsertedRun>().Should().BeEmpty("clean mode inserts a plain w:r — no w:ins");
        body.Descendants<DeletedRun>().Should().BeEmpty("clean mode physically removes the run — no w:del");
        body.Descendants<Inserted>().Should().BeEmpty("no tracked paragraph-mark insertions in clean mode");
        body.Descendants<Deleted>().Should().BeEmpty("no tracked paragraph-mark deletions in clean mode");
        body.Descendants<ParagraphPropertiesChange>().Should().BeEmpty("no w:pPrChange in clean mode");

        // ── The insert landed as PLAIN text at the target paraId. ─────────────────────────────────────
        var editedPara = ParagraphById(body, ParaIds[0])!;
        editedPara.Descendants<Text>().Select(t => t.Text).Should().Contain(t => t.Contains("[CLEAN-INSERT]"),
            "the authored insert must appear as plain run text");

        // ── The deleted range physically vanished (not struck). ───────────────────────────────────────
        var deletedPara = ParagraphById(body, ParaIds[1])!;
        string.Concat(deletedPara.Descendants<Text>().Select(t => t.Text))
            .Should().NotContain("Confidential Information", "the deleted text must be physically removed, not struck");

        // ── Untouched paragraph (para 3) stays byte-identical (I-4). ───────────────────────────────────
        var otherParasAfter = ReadOtherParagraphOuterXml(persisted!, excluded: new[] { ParaIds[0], ParaIds[1] });
        otherParasAfter.Should().Equal(otherParasBefore, "every untouched paragraph subtree must stay byte-identical");

        // ── NFR-01: every package part other than document.xml stays byte-identical. ──────────────────
        var comparison = ComposeOoxmlPackagePartComparer.Compare(original, persisted!, strictDocumentXmlByteIdentity: false);
        comparison.AllUntouchedPartsByteIdentical.Should().BeTrue(
            $"a clean apply must leave every non-document.xml package part byte-identical — {comparison.DescribeMismatches()}");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2. IMPORTED origin → the SAME insert stays TRACKED (native w:ins) — proving the durable marker,
    //    not the payload shape, drives clean-vs-tracked (REQ-2 not regressed).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_ImportedOrigin_Insert_StaysTracked_ThroughTheWire()
    {
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        var (response, persisted) = await PostAuthoredSaveAsync(
            original,
            storedOriginOptionValue: ImportedOptionValue,
            operations: new object[]
            {
                new { type = "insertText", paraId = ParaIds[0], at = new { runIndex = 0, offset = 0 }, text = "[TRACKED-INSERT] " },
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an imported-origin op-log save must succeed via the tracked path");
        persisted.Should().NotBeNull();

        using var doc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false);
        var editedPara = ParagraphById(doc.MainDocumentPart!.Document!.Body!, ParaIds[0])!;
        editedPara.Descendants<InsertedRun>().SelectMany(i => i.Descendants<Text>()).Select(t => t.Text)
            .Should().Contain(t => t.Contains("[TRACKED-INSERT]"),
                "an imported doc's edit must remain a native tracked insertion (w:ins) — the marker keeps it tracked");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. Legacy row (marker absent) → treated as Imported → stays tracked (the BINDING null-handling
    //    contract, proven server-side end to end).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_LegacyRowNoMarker_TreatedAsImported_StaysTracked_ThroughTheWire()
    {
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        var (response, persisted) = await PostAuthoredSaveAsync(
            original,
            storedOriginOptionValue: null, // legacy row: no sprk_composeorigin value
            operations: new object[]
            {
                new { type = "insertText", paraId = ParaIds[0], at = new { runIndex = 0, offset = 0 }, text = "[LEGACY-NULL] " },
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        persisted.Should().NotBeNull();

        using var doc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false);
        doc.MainDocumentPart!.Document!.Body!.Descendants<InsertedRun>().Should().NotBeEmpty(
            "a legacy row with no marker degrades to Imported (tracked) — null is never treated as Authored");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Shared through-the-wire helper — a reopened-doc /save carrying documentRecordId + an operation log;
    // the persisted sprk_composeorigin marker is served by the Dataverse RetrieveAsync boundary.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    private async Task<(HttpResponseMessage Response, byte[]? Persisted)> PostAuthoredSaveAsync(
        byte[] original, int? storedOriginOptionValue, object[] operations)
    {
        _fixture.ResetBoundaries();

        var speId = $"spe-item-021-{Guid.NewGuid():N}";
        var driveId = $"drive-021-{Guid.NewGuid():N}";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var recordId = Guid.NewGuid();

        // Existing item (REPLACE save): the idempotent alt-key lookup finds the row (no create fires).
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", recordId));

        // The durable origin marker the server reads to select clean-vs-tracked apply.
        var documentEntity = new Entity("sprk_document", recordId);
        if (storedOriginOptionValue is { } value)
        {
            documentEntity[ComposeOriginAttribute] = new OptionSetValue(value);
        }
        _fixture.DataverseMock
            .Setup(d => d.RetrieveAsync("sprk_document", recordId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentEntity);

        // Existing-item save fetches the live SPE eTag (no prior stamp ⇒ not stale ⇒ normal apply).
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v1-etag\""));

        byte[]? persisted = null;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, string?, CancellationToken>((_, _, _, stream, _, _) =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                persisted = ms.ToArray();
            })
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v2-etag\""));

        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            documentRecordId = recordId,
            content = original,
            operationLog = new { schemaVersion = "compose-ops-v2", operations },
            comments = (object?)null,
        });

        return (response, persisted);
    }

    private async Task<string> CreateSessionAsync(string tenant, string speId)
    {
        using var scope = _fixture.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
        var session = await sessions.CreateSessionAsync(tenant, TestSessionOwner.Oid, documentId: speId);
        return session.SessionId;
    }

    private static FileHandleDto BuildFileHandle(string speId, string driveId, int size, string eTag) =>
        new(Id: speId, Name: "clean-apply-seam.docx", ParentId: null, Size: size,
            CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
            ETag: eTag, IsFolder: false, WebUrl: null, DriveId: driveId);

    private static List<string> ReadOtherParagraphOuterXml(byte[] bytes, IReadOnlyCollection<string> excluded)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Where(p => p.ParagraphId?.Value is { } id && !excluded.Contains(id))
            .Select(p => p.OuterXml)
            .ToList();
    }

    private static Paragraph? ParagraphById(Body body, string paraId) =>
        body.Descendants<Paragraph>().FirstOrDefault(p => string.Equals(p.ParagraphId?.Value, paraId, StringComparison.OrdinalIgnoreCase));

    private static byte[] BuildDocxWithParaIds(IReadOnlyList<string> paragraphs, IReadOnlyList<string> paraIds)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());
            for (var i = 0; i < paragraphs.Count; i++)
            {
                var p = new Paragraph(new Run(new Text(paragraphs[i]) { Space = SpaceProcessingModeValues.Preserve }))
                {
                    ParagraphId = new HexBinaryValue(paraIds[i]),
                };
                body.AppendChild(p);
            }
            body.AppendChild(new SectionProperties());
            mainPart.Document.Save();
        }
        return stream.ToArray();
    }
}
