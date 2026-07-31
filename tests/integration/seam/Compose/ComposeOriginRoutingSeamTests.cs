// Task 020 (spaarkeai-compose-r5, FR-01 / gap G1 / REQ-1) — the THROUGH-THE-WIRE proof that Compose
// routes a document by its DURABLE, PERSISTED authored-vs-imported origin marker (`sprk_composeorigin`),
// never by inferring origin from SPE-id presence or document content (NFR-02 / I-7 — the fragile
// discriminator G1 exists to replace).
//
// Two halves, both across the REAL routes (WebApplicationFactory), endpoint -> ComposeService ->
// SPE/Dataverse module boundary:
//
//   (A) LOAD reads + returns the persisted marker (Path A — an existing sprk_document record):
//       GET /api/compose/documents/{id}?documentRecordId={guid} -> LoadComposeDocumentResponse.origin.
//       Authored (100000000) -> "authored"; Imported (100000001) -> "imported"; a legacy row with no
//       stored value -> null (the BINDING null-handling contract: the client treats null as imported,
//       never strict-equal to authored). A Path B continuation (no documentRecordId) NEVER queries
//       Dataverse for origin — the NFR-02 no-inference guarantee, asserted by Verify(..., Times.Never).
//
//   (B) SAVE resolves origin from the SAME `request.ContentModel is not null` discriminant SaveAsync
//       already uses to select the born-in-editor render branch, and persists it ONLY at create-on-save:
//         - a reopened IMPORTED doc's dirty save (operation log, no content model) reports origin
//           "imported" and stays on the tracked path (the persisted bytes carry a native w:ins) — REQ-2
//           not regressed;
//         - a born-in-editor create-on-save (content model present) reports origin "authored" AND writes
//           sprk_composeorigin = OptionSetValue(100000000) onto the NEW sprk_document row (captured at the
//           IGenericEntityService.CreateAsync boundary) — REQ-1.
//
// Reuses (root CLAUDE.md §11 — extend, don't introduce): ComposeFidelitySeamFixture (host + SPE/Dataverse/
// indexing module-boundary mocks + fake auth) established by task 024/034; the create-on-save arrange
// mirrors ComposeCreateOnSaveEndpointContractTests (task 100/110); the loaded-doc dirty save mirrors
// ComposeFidelitySeamTests (task 034). No new fixture class.
//
// ADR-038 seam DoD: through-the-wire WebApplicationFactory slices only. NO Mock<HttpMessageHandler>, NO
// DI-registration test, NO ctor-null test. Mocks live only at the ISpeFileOperations /
// IGenericEntityService / IPostUploadIndexingEnqueuer boundaries.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

public sealed class ComposeOriginRoutingSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposeOriginRoutingSeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    // The AS-BUILT integer values of the owner-created sprk_composeorigin choice field
    // (notes/g1-origin-field-asbuilt.md). Referenced by literal here so this test is the independent
    // check on the ComposeOrigin enum's numbering — if someone renumbers the enum, this fails.
    private const int AuthoredOptionValue = 100000000;
    private const int ImportedOptionValue = 100000001;
    private const string ComposeOriginAttribute = "sprk_composeorigin";

    private static readonly string[] Paragraphs =
    {
        "This Master Services Agreement governs the engagement between the parties.",
        "Confidential Information shall not be disclosed to any third party.",
        "This clause shall survive termination of the Agreement.",
    };

    private static readonly string[] ParaIds = { "00000001", "00000002", "00000003" };

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (A) LOAD — reads + returns the persisted origin marker (Path A: an existing sprk_document record).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Load_ExistingRecordWithAuthoredMarker_ReturnsAuthoredOrigin_ThroughTheWire()
    {
        var origin = await LoadOriginThroughWireAsync(storedOriginOptionValue: AuthoredOptionValue);

        origin.ValueKind.Should().Be(JsonValueKind.String);
        origin.GetString().Should().Be("authored",
            "a doc born in the editor carries sprk_composeorigin=Authored — LoadAsync must surface it verbatim so the client routes it clean on reopen (REQ-1)");
    }

    [Fact]
    public async Task Load_ExistingRecordWithImportedMarker_ReturnsImportedOrigin_ThroughTheWire()
    {
        var origin = await LoadOriginThroughWireAsync(storedOriginOptionValue: ImportedOptionValue);

        origin.ValueKind.Should().Be(JsonValueKind.String);
        origin.GetString().Should().Be("imported",
            "an imported doc carries sprk_composeorigin=Imported — LoadAsync must surface it so the client keeps it on the tracked path (REQ-2)");
    }

    [Fact]
    public async Task Load_LegacyRecordWithNoMarker_ReturnsNullOrigin_ClientTreatsAsImported_ThroughTheWire()
    {
        // A pre-existing row that predates the field: RetrieveAsync returns an entity with NO
        // sprk_composeorigin attribute (no backfill). Origin must degrade to null — the BINDING
        // null-handling contract makes the client treat null exactly as 'imported'.
        var origin = await LoadOriginThroughWireAsync(storedOriginOptionValue: null);

        origin.ValueKind.Should().Be(JsonValueKind.Null,
            "a legacy row with no stored marker returns null origin (no backfill) — the client treats null the same as 'imported', never strict-equal to 'authored'");
    }

    [Fact]
    public async Task Load_PathBContinuation_NoRecordId_NeverQueriesDataverseForOrigin_ReturnsNull_ThroughTheWire()
    {
        // NFR-02 / I-7 negative: a Path B continuation (no documentRecordId — the doc is not yet
        // promoted) has no row to read. Origin MUST stay null and the service MUST NOT fall back to
        // inferring origin from SPE-id presence or content — proven by the Dataverse RetrieveAsync
        // boundary never being touched.
        _fixture.ResetBoundaries();

        const string speId = "spe-item-020-pathb-noorigin";
        const string driveId = "drive-020-pathb-noorigin";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var docBytes = BuildDocxWithParaIds(Paragraphs, ParaIds);

        ArrangeSpeForLoad(speId, driveId, docBytes);

        using var client = _fixture.CreateAuthenticatedClient();
        // NO documentRecordId query parameter — a not-yet-promoted mount.
        var response = await client.GetAsync(
            $"/api/compose/documents/{speId}?driveId={driveId}&tenantId={tenant}");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"a Path B load must succeed — body: {body}");

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("origin", out var origin).Should().BeTrue("the origin field is always present on the wire");
        origin.ValueKind.Should().Be(JsonValueKind.Null,
            "a not-yet-promoted mount has no persisted marker — origin is null, never inferred");

        _fixture.DataverseMock.Verify(
            d => d.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "NFR-02: with no record id there is nothing to read — the service must NOT query Dataverse (and must never infer origin from SPE-id/content instead)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (B) SAVE — resolves origin from the ContentModel discriminant; persists it only at create-on-save.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_ReopenedImported_OperationLogPath_ResolvesImportedOrigin_StaysTracked_ThroughTheWire()
    {
        _fixture.ResetBoundaries();

        const string speId = "spe-item-020-imported-tracked";
        const string driveId = "drive-020-imported-tracked";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        // Existing row (a REPLACE save, not a first-Save) — the idempotent alt-key lookup finds it,
        // so no CreateAsync fires and the persisted marker is NOT re-touched.
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", Guid.NewGuid()));

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
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v2-etag\""));

        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        // A loaded/imported dirty save: retained-original content + an operation log (NO content model)
        // — the tracked path. The origin discriminant resolves this to Imported.
        var operationLog = new
        {
            schemaVersion = "compose-ops-v2",
            operations = new object[]
            {
                new { type = "insertText", paraId = ParaIds[0], at = new { runIndex = 0, offset = 0 }, text = "[G1-IMPORTED-TRACKED]" },
            },
        };

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog,
            comments = (object?)null,
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"a well-formed imported dirty save must succeed — body: {body}");

        using (var doc = JsonDocument.Parse(body))
        {
            doc.RootElement.GetProperty("origin").GetString().Should().Be("imported",
                "a save carrying an operation log (no content model) resolves to Imported — resolved from the ContentModel discriminant, never from SPE-id/content");
        }

        // Stays TRACKED: the edit landed as a native tracked insertion (w:ins), not a clean run — REQ-2.
        persisted.Should().NotBeNull("the SPE facade must have captured the patched bytes");
        using var patchedDoc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false);
        var editedPara = patchedDoc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Single(p => string.Equals(p.ParagraphId?.Value, ParaIds[0], StringComparison.OrdinalIgnoreCase));
        editedPara.Descendants<InsertedRun>().SelectMany(i => i.Descendants<Text>()).Select(t => t.Text)
            .Should().Contain("[G1-IMPORTED-TRACKED]",
                "an imported doc's edit must remain a tracked change (w:ins) — the origin routing must NOT flip it onto the clean path");
    }

    [Fact]
    public async Task Save_ImportedTransient_CreateOnSave_OperationLogPath_StaysTracked_PersistsImportedMarker_ThroughTheWire()
    {
        // UAT #1A regression (task 050): an IMPORTED doc mounted transiently (Browse/upload) whose user makes
        // a tracked edit and Saves for the FIRST time (create-on-save). The client now sends the retained
        // ORIGINAL bytes (content) + the tracked operation log (NO content model) — the SAME imported shape the
        // replace path uses. The server MUST apply the op via ComposeShadowPatchEngine as a native tracked
        // change (w:ins, so Word shows the redline) and stamp sprk_composeorigin=Imported onto the NEW row —
        // NOT route to the renderer (plain runs, no redline) and NOT stamp Authored. The pre-fix defect saved
        // this through the ContentModel/renderer path (plain runs) and durably mis-stamped it Authored.
        _fixture.ResetBoundaries();

        const string containerId = "b!container-050-imported";
        const string mintedSpeItemId = "spe-item-050-imported-001";
        const string resolvedDriveId = "drive-050-imported-001";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var newDocumentId = Guid.NewGuid();
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedDriveId);

        byte[]? persisted = null;
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, CancellationToken>((_, _, _, stream, _) =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                persisted = ms.ToArray();
            })
            .ReturnsAsync(BuildFileHandle(mintedSpeItemId, resolvedDriveId, original.Length, "\"v1-etag\""));

        // No existing row → create fires. Capture the created entity to assert the persisted origin marker.
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        Entity? createdEntity = null;
        _fixture.DataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => createdEntity = e)
            .ReturnsAsync(newDocumentId);

        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        using var client = _fixture.CreateAuthenticatedClient();

        // Imported transient create-on-save: retained original bytes + tracked op-log, NO content model.
        var operationLog = new
        {
            schemaVersion = "compose-ops-v2",
            operations = new object[]
            {
                new { type = "insertText", paraId = ParaIds[0], at = new { runIndex = 0, offset = 0 }, text = "[UAT-050-IMPORTED-TRACKED]" },
            },
        };

        var response = await client.PostAsJsonAsync("/api/compose/documents/create-on-save", new
        {
            containerId,
            tenantId = tenant,
            sessionId = string.Empty,
            displayName = "imported-upload.docx",
            content = original,
            operationLog,
            comments = (object?)null,
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"an imported transient create-on-save with an op-log must succeed — body: {body}");

        using (var doc = JsonDocument.Parse(body))
        {
            doc.RootElement.GetProperty("origin").GetString().Should().Be("imported",
                "a create-on-save carrying retained original bytes + an op-log (no content model) is IMPORTED — never mis-stamped Authored (UAT #1A)");
        }

        // Stays TRACKED: the edit landed as a native w:ins on the retained baseline, NOT a plain rendered run.
        persisted.Should().NotBeNull("the SPE facade must have captured the uploaded bytes on create-on-save");
        using var patchedDoc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false);
        var editedPara = patchedDoc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Single(p => string.Equals(p.ParagraphId?.Value, ParaIds[0], StringComparison.OrdinalIgnoreCase));
        editedPara.Descendants<InsertedRun>().SelectMany(i => i.Descendants<Text>()).Select(t => t.Text)
            .Should().Contain("[UAT-050-IMPORTED-TRACKED]",
                "an imported transient edit must persist as a tracked change (w:ins) — the create-on-save must apply the op-log via the engine, NOT render plain runs (UAT #1A regression)");

        // The durable marker is PERSISTED as Imported onto the NEW row (never Authored for an imported doc).
        createdEntity.Should().NotBeNull("a new sprk_document row must be created on first Save");
        createdEntity!.Contains(ComposeOriginAttribute).Should().BeTrue("create-on-save must stamp sprk_composeorigin");
        ((OptionSetValue)createdEntity[ComposeOriginAttribute]).Value.Should().Be(ImportedOptionValue,
            "an imported doc persists sprk_composeorigin=Imported (100000001) — so a later reopen routes tracked, never clean (UAT #1A)");
    }

    [Fact]
    public async Task Save_BornInEditor_CreateOnSave_ContentModel_ResolvesAuthored_PersistsAuthoredMarker_ThroughTheWire()
    {
        _fixture.ResetBoundaries();

        const string containerId = "b!container-020-authored";
        const string mintedSpeItemId = "spe-item-020-authored-001";
        const string resolvedDriveId = "drive-020-authored-001";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var newDocumentId = Guid.NewGuid();

        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedDriveId);
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(mintedSpeItemId, resolvedDriveId, size: 2048, eTag: "\"v1-etag\""));

        // No existing row → create fires. Capture the created entity to assert the persisted marker.
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        Entity? createdEntity = null;
        _fixture.DataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => createdEntity = e)
            .ReturnsAsync(newDocumentId);

        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        using var client = _fixture.CreateAuthenticatedClient();

        // A born-in-editor create-on-save: the server RENDERS the whole document from the content model
        // (no retained baseline). Content model present → origin resolves to Authored.
        var response = await client.PostAsJsonAsync("/api/compose/documents/create-on-save", new
        {
            containerId,
            tenantId = tenant,
            sessionId = string.Empty,
            displayName = "authored-draft.docx",
            contentModel = new
            {
                blocks = new object[]
                {
                    new { kind = "paragraph", runs = new object[] { new { text = "An AI-drafted engagement letter, authored in the editor." } } },
                },
            },
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"a born-in-editor create-on-save must succeed — body: {body}");

        using (var doc = JsonDocument.Parse(body))
        {
            doc.RootElement.GetProperty("origin").GetString().Should().Be("authored",
                "a save carrying a content model resolves to Authored — the SAME discriminant SaveAsync uses to pick the born-in-editor render branch");
        }

        // The durable marker is PERSISTED onto the NEW row (set once, at create-on-save).
        createdEntity.Should().NotBeNull("a new sprk_document row must be created on first Save (FR-06)");
        createdEntity!.Contains(ComposeOriginAttribute).Should().BeTrue(
            "the create-on-save must stamp sprk_composeorigin so a later reopen routes clean without inference");
        createdEntity[ComposeOriginAttribute].Should().BeOfType<OptionSetValue>();
        ((OptionSetValue)createdEntity[ComposeOriginAttribute]).Value.Should().Be(AuthoredOptionValue,
            "a born-in-editor doc persists sprk_composeorigin=Authored (100000000) — the durable marker G1 reads back on reopen");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Shared through-the-wire helpers.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Drives a real GET load for an EXISTING record (Path A) and returns the response's
    /// <c>origin</c> JSON element. <paramref name="storedOriginOptionValue"/> null models a legacy row
    /// with no stored marker (the RetrieveAsync entity omits the attribute entirely).</summary>
    private async Task<JsonElement> LoadOriginThroughWireAsync(int? storedOriginOptionValue)
    {
        _fixture.ResetBoundaries();

        var speId = $"spe-item-020-load-{Guid.NewGuid():N}";
        var driveId = $"drive-020-load-{Guid.NewGuid():N}";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var recordId = Guid.NewGuid();
        var docBytes = BuildDocxWithParaIds(Paragraphs, ParaIds);

        ArrangeSpeForLoad(speId, driveId, docBytes);

        var documentEntity = new Entity("sprk_document", recordId);
        if (storedOriginOptionValue is { } value)
        {
            documentEntity[ComposeOriginAttribute] = new OptionSetValue(value);
        }

        _fixture.DataverseMock
            .Setup(d => d.RetrieveAsync("sprk_document", recordId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentEntity);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync(
            $"/api/compose/documents/{speId}?driveId={driveId}&tenantId={tenant}&documentRecordId={recordId}");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"a Path A load must succeed — body: {body}");

        // Clone the element so it outlives the JsonDocument (which the using disposes).
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("origin", out var origin).Should().BeTrue("the origin field is always present on the load wire");
        return origin.Clone();
    }

    private void ArrangeSpeForLoad(string speId, string driveId, byte[] docBytes)
    {
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, docBytes.Length, "\"v1-etag\""));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(docBytes.ToArray()));
    }

    private async Task<string> CreateSessionAsync(string tenant, string speId)
    {
        using var scope = _fixture.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
        var session = await sessions.CreateSessionAsync(tenant, documentId: speId);
        return session.SessionId;
    }

    private static FileHandleDto BuildFileHandle(string speId, string driveId, int size, string eTag) =>
        new(Id: speId, Name: "origin-seam.docx", ParentId: null, Size: size,
            CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
            ETag: eTag, IsFolder: false, WebUrl: null, DriveId: driveId);

    /// <summary>Builds a valid DOCX whose paragraphs carry the supplied physical w14:paraIds (mirrors the
    /// established per-file-local-helper convention in ConcurrencySaveSeamTests.BuildDocxWithParaIds).</summary>
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
