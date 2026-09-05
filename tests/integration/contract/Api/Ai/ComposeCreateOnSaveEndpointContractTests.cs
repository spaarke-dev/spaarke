// Task 100 (E2E-R1) — Create-on-save vertical-slice contract test (KEEP path: endpoint-contract).
//
// WHY THIS FILE EXISTS (anti-recurrence, non-waivable per project CLAUDE.md §"E2E Definition-of-
// Done" + notes/e2e-gap-register.md Cluster 1):
//   The create-on-save SERVICE backbone (ComposeService.SaveAsync transient branch) was correct but
//   DEAD CODE over HTTP — no route reached it, the client never sent `containerId`, and the ONLY
//   test that touched Save mocked IComposeService entirely (ComposeEndpointsContractTests), so the
//   endpoint→DTO→service→SPE/Dataverse wire was never exercised. That is exactly how the false-green
//   shipped. This test drives the FULL slice through the REAL endpoints with the REAL ComposeService
//   (only the external SPE + Dataverse + indexing boundaries are mocked), so a broken wire between
//   the endpoint and the service fails the build — a service-only unit test would NOT catch it.
//
//   It POSTs an upload (proving the transient-mount bytes are served) then the create-on-save
//   (with `containerId`, NO `documentSpeId`) through the real POST routes, and asserts a new
//   sprk_document + SPE drive-item were created and the ChatSession rebound to the new record id.
//
// KEEP-path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `endpoint-contract`  ·  Path: `tests/integration/contract/Api/Ai/**` (csproj
//     auto-includes `tests/integration/contract/**`).
//   - "Every new endpoint => >=1 integration test": closes the contract for the new
//     `POST /api/compose/documents/create-on-save` route (task 100).
//
// Banned-pattern compliance (ADR-038 §4 + tests/CLAUDE.md): NO Mock<HttpMessageHandler>; mocks live
// ONLY at the SPE (ISpeFileOperations) / Dataverse (IGenericEntityService) / indexing module
// boundaries per ADR-038 §4 "mock at module boundaries". The class-under-test (ComposeService) and
// the HTTP boundary are REAL. Assertions are HTTP-observable + persisted side-effects.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
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
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Through-the-wire (anti-recurrence) contract tests for FR-05 create-on-save (task 100). Boots the
/// BFF in-process with the REAL <see cref="ComposeService"/> and only the external SPE / Dataverse /
/// indexing boundaries mocked, and drives upload → create-on-save through the real POST routes.
/// </summary>
public sealed class ComposeCreateOnSaveEndpointContractTests
    : IClassFixture<ComposeCreateOnSaveFixture>
{
    private readonly ComposeCreateOnSaveFixture _fixture;

    public ComposeCreateOnSaveEndpointContractTests(ComposeCreateOnSaveFixture fixture)
    {
        _fixture = fixture;
    }

    // Minimal valid DOCX ZIP-signature bytes — treated as an opaque payload by the transient branch
    // (the SPE boundary is mocked; no real OOXML parsing occurs on this path).
    private static readonly byte[] DraftBytes =
        { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00, 0x01, 0x02, 0x03, 0x04 };

    [Fact]
    public async Task UploadThenCreateOnSave_TransientDraft_PersistsNewDocumentAndSpeItemAndRebindsSession()
    {
        // ── Arrange ────────────────────────────────────────────────────────────────────────────
        // Issue #858: an OLD client still sends containerId — the server must IGNORE it (unknown JSON
        // properties are dropped) and derive the container itself. Posting a decoy and asserting the
        // SPE resolve saw the server-derived value instead is the strongest wire-level form of the
        // #858 property: the caller can no longer name the container it writes into.
        const string clientSuppliedDecoyContainerId = "b!client-supplied-must-be-ignored";
        const string mintedSpeItemId = "spe-item-created-001";
        const string resolvedDriveId = "drive-created-001";
        var newDocumentId = Guid.NewGuid();
        var uploadFileId = Guid.NewGuid().ToString("N");

        _fixture.ResetBoundaries();

        // SPE boundary: the transient branch resolves the drive from the client-supplied container,
        // then mints a new drive-item under OBO. Capture the container it resolves + the bytes.
        string? resolvedContainerArg = null;
        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((c, _) => resolvedContainerArg = c)
            .ReturnsAsync(resolvedDriveId);
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: mintedSpeItemId,
                Name: "draft.docx",
                ParentId: null,
                Size: DraftBytes.Length,
                CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"v1-etag\"",
                IsFolder: false,
                WebUrl: null,
                DriveId: resolvedDriveId));

        // Dataverse boundary: no existing row (alt-key lookup returns null) → create fires. Capture
        // the created entity so we can assert the SPE item id was recorded on the new sprk_document.
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        Entity? createdEntity = null;
        _fixture.DataverseMock
            .Setup(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => createdEntity = e)
            .ReturnsAsync((newDocumentId, true));

        // Indexing boundary: sync-OBO indexing succeeds (non-terminal for this assertion).
        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        // Pre-create a REAL ChatSession (DocumentId unset) and seed the retained upload bytes so the
        // upload leg + the rebind are both exercised end-to-end.
        string sessionId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
            var session = await sessions.CreateSessionAsync(ComposeCreateOnSaveFixture.TestTenantId, TestSessionOwner.Oid, documentId: null);
            sessionId = session.SessionId;

            var cache = scope.ServiceProvider.GetRequiredService<ITenantCache>();
            await cache.SetAsync(
                ComposeCreateOnSaveFixture.TestTenantId,
                "doc-upload-binary",
                $"{sessionId}:{uploadFileId}",
                1,
                DraftBytes);
        }

        using var client = _fixture.CreateAuthenticatedClient();

        // ── Act 1: transient-mount upload (real /api/compose/upload) ─────────────────────────────
        var uploadResponse = await client.PostAsJsonAsync(
            "/api/compose/upload", new { sessionId, documentId = uploadFileId });

        uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "the upload endpoint serves the transient draft's retained bytes for the editor mount");
        var uploadBody = await uploadResponse.Content.ReadFromJsonAsync<ComposeUploadResponse>();
        uploadBody!.Content.Should().Equal(DraftBytes, "the exact retained bytes are served for the transient mount");

        // ── Act 2: create-on-save with NO documentSpeId. The body still carries a containerId — the
        //    OLD client shape — which the server must ignore (#858 deploy-ordering: BFF ships first). ──
        var createOnSaveResponse = await client.PostAsJsonAsync(
            "/api/compose/documents/create-on-save",
            new
            {
                containerId = clientSuppliedDecoyContainerId,
                tenantId = ComposeCreateOnSaveFixture.TestTenantId,
                sessionId,
                content = uploadBody.Content,
                displayName = "draft.docx",
            });

        // ── Assert: HTTP contract ────────────────────────────────────────────────────────────────
        createOnSaveResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "the transient create-on-save reaches ComposeService.SaveAsync's create branch over the real wire");
        var result = await createOnSaveResponse.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        result.Should().NotBeNull();
        result!.DocumentRecordId.Should().Be(newDocumentId, "a NEW sprk_document was created");
        result.WasPromotedThisSave.Should().BeTrue("first Save of a transient draft creates the record (FR-06)");
        result.DocumentSpeId.Should().Be(mintedSpeItemId,
            "the server returns the minted SPE id so a second Save targets the real drive-item (gap 1.7)");

        // ── Assert: persisted side-effects (this is what a service-only unit test misses) ─────────
        // REWRITTEN for issue #858. Old assertion: the body containerId "flowed body →
        // request.ContainerId → SPE resolve" — that flow is the deleted defect. New truth: the SPE
        // resolve sees the SERVER-derived container (acting user → business unit → sprk_containerid),
        // and the body's value influences nothing.
        resolvedContainerArg.Should().Be(TestActingUserBusinessUnit.ContainerId,
            "#858: the container is derived server-side from the acting user's business unit");
        resolvedContainerArg.Should().NotBe(clientSuppliedDecoyContainerId,
            "#858's whole point: a client-supplied container id must never reach the SPE write path");
        _fixture.SpeMock.Verify(s => s.UploadSmallAsUserAsync(
            It.IsAny<HttpContext>(), resolvedDriveId, It.IsAny<string>(),
            It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once,
            "a new SPE drive-item was minted in the resolved BU drive");
        createdEntity.Should().NotBeNull("a new sprk_document row was created");
        createdEntity!.LogicalName.Should().Be("sprk_document");
        createdEntity.GetAttributeValue<string>("sprk_graphitemid").Should().Be(mintedSpeItemId,
            "the new document row points at the minted SPE drive-item");

        // ── Assert: session rebind (FR-07) ───────────────────────────────────────────────────────
        using (var scope = _fixture.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
            var reloaded = await sessions.GetSessionAsync(
                ComposeCreateOnSaveFixture.TestTenantId, sessionId, CancellationToken.None);
            reloaded.Should().NotBeNull();
            reloaded!.DocumentId.Should().Be(newDocumentId.ToString(),
                "the session rebinds from the transient draft to the new sprk_documentid (FR-07)");
        }
    }

    // G7 / FR-06 (task 022) — THE TRANSIENT-KEY DEDUP GUARANTEE, and why this test exists.
    //
    // `ComposeService` resolves the client-minted transient key against the durable
    // `sprk_composetransientkey_uk` alt-key BEFORE minting, so repeated create-on-save calls with the
    // same key replace ONE record in place instead of minting duplicates — the production defect the
    // field comment calls "the 8-duplicate defect".
    //
    // That guarantee had NO test. Found by the task-070 cluster-2b mutation pass: making
    // `TryFindDocumentByTransientKeyAsync` match nothing at all left the whole Compose suite
    // (1,813 tests) GREEN. A dedup path that can silently stop deduplicating, on a defect that has
    // already shipped once, is exactly the shape ADR-038's regression rule exists for.
    //
    // The Dataverse mock below is deliberately KEY-SENSITIVE — it answers only for the REAL transient
    // key. A mock that answered any alternate-key lookup would keep passing under that mutation and
    // reintroduce the hole in test form.
    [Fact]
    public async Task CreateOnSave_WhenTransientKeyMatchesAnExistingRow_ReplacesInPlaceAndMintsNoDuplicate()
    {
        // ── Arrange ────────────────────────────────────────────────────────────────────────────
        const string containerId = "b!container-bu-dedup";
        const string transientKey = "transient-key-dedup-001";
        const string existingSpeItemId = "spe-item-existing-dedup";
        const string existingDriveId = "drive-existing-dedup";
        var existingDocumentId = Guid.NewGuid();
        var wouldBeNewDocumentId = Guid.NewGuid();

        _fixture.ResetBoundaries();

        var existingRow = new Entity("sprk_document", existingDocumentId);
        existingRow["sprk_documentid"] = existingDocumentId;
        existingRow["sprk_graphitemid"] = existingSpeItemId;
        existingRow["sprk_graphdriveid"] = existingDriveId;

        // KEY-SENSITIVE: the row is findable by its REAL transient key, and (for the idempotent
        // promote step that follows the replace) by its graph-item id. Any other key value is a miss.
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, KeyAttributeCollection key, string[] _, CancellationToken _) =>
            {
                if (key.TryGetValue("sprk_composetransientkey", out var tk)
                    && string.Equals(tk as string, transientKey, StringComparison.Ordinal))
                {
                    return existingRow;
                }

                if (key.TryGetValue("sprk_graphitemid", out var gid)
                    && string.Equals(gid as string, existingSpeItemId, StringComparison.Ordinal))
                {
                    return existingRow;
                }

                return null!;
            });

        // If the dedup fails to resolve, the mint branch runs and creates a DIFFERENT record — which
        // is what the assertions below detect.
        _fixture.DataverseMock
            .Setup(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((wouldBeNewDocumentId, true));

        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("drive-should-not-be-used");
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: existingSpeItemId,
                Name: "draft.docx",
                ParentId: null,
                Size: DraftBytes.Length,
                CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"v2-etag\"",
                IsFolder: false,
                WebUrl: null,
                DriveId: existingDriveId));

        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        string sessionId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
            var session = await sessions.CreateSessionAsync(
                ComposeCreateOnSaveFixture.TestTenantId, TestSessionOwner.Oid, documentId: null);
            sessionId = session.SessionId;
        }

        using var client = _fixture.CreateAuthenticatedClient();

        // ── Act: create-on-save carrying the transient key of an ALREADY-CREATED record ──────────
        var response = await client.PostAsJsonAsync(
            "/api/compose/documents/create-on-save",
            new
            {
                containerId,
                tenantId = ComposeCreateOnSaveFixture.TestTenantId,
                sessionId,
                content = DraftBytes,
                displayName = "draft.docx",
                transientKey,
            });

        // ── Assert ───────────────────────────────────────────────────────────────────────────────
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        result.Should().NotBeNull();

        result!.DocumentRecordId.Should().Be(existingDocumentId,
            "the transient key resolved to the EXISTING sprk_document — a second row here is the 8-duplicate defect");
        result.DocumentRecordId.Should().NotBe(wouldBeNewDocumentId,
            "the mint branch must not have run");

        _fixture.SpeMock.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), existingDriveId, existingSpeItemId,
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once,
            "a dedup hit replaces the EXISTING drive-item's content in place");
        _fixture.SpeMock.Verify(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never,
            "no NEW drive-item is minted when the transient key already resolves to one");
    }

    // Task 041 B-MED-3 (operator resolution 2026-08-07, option C): a PDF-sourced create-on-save
    // carries the SOURCE sprk_document id and the new record INHERITS the source's record links
    // (ADR-024 document link set) — the new Word document files ALONGSIDE the source PDF.
    [Fact]
    public async Task CreateOnSave_WithSourceDocumentRecordId_InheritsSourceRecordLinks()
    {
        // ── Arrange ────────────────────────────────────────────────────────────────────────────
        const string containerId = "b!container-bu-pdf";
        const string mintedSpeItemId = "spe-item-pdf-docx-001";
        const string resolvedDriveId = "drive-pdf-001";
        var newDocumentId = Guid.NewGuid();
        var sourceDocumentId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        _fixture.ResetBoundaries();

        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resolvedDriveId);
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: mintedSpeItemId, Name: "Corteva NDA.docx", ParentId: null,
                Size: DraftBytes.Length, CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow, ETag: "\"v1\"",
                IsFolder: false, WebUrl: null, DriveId: resolvedDriveId));

        // The SOURCE PDF's record carries a matter + a project link (and empty/absent others).
        var sourceEntity = new Entity("sprk_document", sourceDocumentId);
        sourceEntity["sprk_matter"] = new EntityReference("sprk_matter", matterId);
        sourceEntity["sprk_project"] = new EntityReference("sprk_project", projectId);
        string[]? retrievedColumns = null;
        _fixture.DataverseMock
            .Setup(d => d.RetrieveAsync(
                "sprk_document", sourceDocumentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string[], CancellationToken>((_, _, cols, _) => retrievedColumns = cols)
            .ReturnsAsync(sourceEntity);
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        Entity? createdEntity = null;
        _fixture.DataverseMock
            .Setup(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => createdEntity = e)
            .ReturnsAsync((newDocumentId, true));
        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        using var client = _fixture.CreateAuthenticatedClient();

        // ── Act: the PDF-sourced create body the 041 client sends (sourceDocumentRecordId present) ──
        var response = await client.PostAsJsonAsync(
            "/api/compose/documents/create-on-save",
            new
            {
                containerId,
                tenantId = ComposeCreateOnSaveFixture.TestTenantId,
                sessionId = string.Empty,
                content = DraftBytes,
                displayName = "Corteva NDA.docx",
                sourceDocumentRecordId = sourceDocumentId,
            });

        // ── Assert ─────────────────────────────────────────────────────────────────────────────
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        createdEntity.Should().NotBeNull("a new sprk_document row was created");
        createdEntity!.GetAttributeValue<EntityReference>("sprk_matter").Should().NotBeNull(
            "the new Word document inherits the source PDF's matter link (filed alongside it)");
        createdEntity.GetAttributeValue<EntityReference>("sprk_matter")!.Id.Should().Be(matterId);
        createdEntity.GetAttributeValue<EntityReference>("sprk_project")!.Id.Should().Be(projectId);
        createdEntity.Contains("sprk_invoice").Should().BeFalse(
            "lookups the source does not carry are NOT invented on the new record");
        // 2026-09-04: this previously pinned SIX columns as "the ADR-024 document link vocabulary". That
        // was the defect written down as an assertion — the live table carries 17, so the inheritance
        // silently dropped ten link types and a PDF filed under an Agreement produced an unfiled Word doc.
        // It now asserts the endpoint reads THE MAP; the map itself is pinned against the live schema by
        // DocumentLinkFieldMapTests, so this stays honest without re-listing the columns in a second place.
        retrievedColumns.Should().BeEquivalentTo(
            Sprk.Bff.Api.Services.Documents.DocumentLinkFieldMap.AllAttributes,
            "the inheritance reads the whole sprk_document link vocabulary, not a subset of it");
        retrievedColumns.Should().Contain(
            "sprk_relatedagreement",
            "the Agreement link was one of the ten the old hard-coded list missed");
    }

    // Task 041 B-MED-3: link-inheritance is BEST-EFFORT — a failed source read must not fail the save.
    [Fact]
    public async Task CreateOnSave_WhenSourceRecordReadFails_StillCreatesTheDocumentUnassociated()
    {
        const string containerId = "b!container-bu-pdf-2";
        var newDocumentId = Guid.NewGuid();
        var sourceDocumentId = Guid.NewGuid();

        _fixture.ResetBoundaries();

        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("drive-pdf-002");
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: "spe-item-pdf-002", Name: "x.docx", ParentId: null, Size: DraftBytes.Length,
                CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"v1\"", IsFolder: false, WebUrl: null, DriveId: "drive-pdf-002"));
        _fixture.DataverseMock
            .Setup(d => d.RetrieveAsync(
                "sprk_document", sourceDocumentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("source record unreadable"));
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        Entity? createdEntity = null;
        _fixture.DataverseMock
            .Setup(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => createdEntity = e)
            .ReturnsAsync((newDocumentId, true));
        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/api/compose/documents/create-on-save",
            new
            {
                containerId,
                tenantId = ComposeCreateOnSaveFixture.TestTenantId,
                sessionId = string.Empty,
                content = DraftBytes,
                displayName = "x.docx",
                sourceDocumentRecordId = sourceDocumentId,
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "link inheritance is best-effort — a failed source read never fails the save");
        createdEntity.Should().NotBeNull();
        createdEntity!.Contains("sprk_matter").Should().BeFalse("no links could be inherited");
    }

    // ── REWRITTEN for issue #858 (was: CreateOnSave_WhenContainerIdMissing_Returns400) ────────────
    //
    // OLD PREMISE (deliberately dead): "no containerId in the body → 400, because the BFF does NOT
    // resolve BU→container (Fork A / multi-container INV-7) and so cannot know where to write."
    // #858 inverted that premise on purpose: requiring the caller to name a storage container WAS the
    // defect (the caller-named-container class this whole project removes), the INV-7 citation was
    // itself inverted (INV-7 PRESCRIBES server-side resolution), and both the body field and the 400
    // guard were deleted. The server now derives the container — for a matter-less draft, from the
    // acting user's business unit.
    //
    // PRESERVED INTENT: a create-on-save that cannot be PLACED must write nothing and fail honestly.
    // The post-#858 shape of "cannot be placed" is a business unit with no sprk_containerid stamped —
    // a legitimate, common configuration state (3 of 6 live BUs). That is a CONFIGURATION answer, not
    // a request-validation error: it arrives as the container-step failure the client already renders
    // (HTTP 200 carrying outcome=storage-failed on the step projection), never a success, and with no
    // SPE or Dataverse write attempted. Mirrors the unit-layer rewrite
    // (SaveAsync_TransientDraftWithNoConfiguredContainer_FailsContainerStep_NeverSuccess).
    [Fact]
    public async Task CreateOnSave_WhenActingUsersBusinessUnitHasNoContainer_FailsContainerStepHonestly_AndWritesNothing()
    {
        _fixture.ResetBoundaries();
        // Override the default arrangement: the derivation chain resolves (user → BU) but the BU has
        // NO container stamped. Last-setup-wins, so this shadows ResetBoundaries' default.
        TestActingUserBusinessUnit.ArrangeWithNoContainer(_fixture.DataverseMock);

        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            "/api/compose/documents/create-on-save",
            new
            {
                tenantId = ComposeCreateOnSaveFixture.TestTenantId,
                sessionId = Guid.NewGuid().ToString("N"),
                content = DraftBytes,
            });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"no configured container is a CONFIGURATION state carried on the step projection, not a " +
            $"request error and not an exception — body: {body}");

        using var payload = JsonDocument.Parse(body);
        payload.RootElement.GetProperty("outcome").GetString().Should().Be("storage-failed",
            "a save that stored nothing must SAY so on the wire (FR-S06)");
        payload.RootElement.GetProperty("versionId").GetString().Should().BeEmpty(
            "no SPE version exists — the outcome and the payload must agree");

        _fixture.SpeMock.Verify(
            s => s.UploadSmallAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never, "nothing may be written when no container could be derived");
        _fixture.DataverseMock.Verify(
            d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Never, "no sprk_document row may be minted for content that was never stored");
    }

    // ── FR-S06 (spaarkeai-compose-r8 task 013) — the 200-with-nothing-written defect ──────────────────
    //
    // THE defect the save-outcome contract exists to remove, asserted through the real route.
    // `ComposeService`'s container-failure path RETURNS a result rather than throwing (it carries the
    // per-step create-on-save completion projection, which the client needs), and the endpoint wraps
    // every returned result in `Results.Ok`. So a save that stored NOTHING arrived as HTTP 200 — and
    // before this contract the client had nothing in the body to distinguish it from success, so it
    // rendered "Saved ✓" over a write that never happened. Three releases shipped that way.
    //
    // The status deliberately stays 200 (the step-projection contract rides on this body). What changed
    // is that the body now SAYS what happened, and the client keys off that field rather than the status
    // — see the paired client assertion in ComposeWorkspace.saveErrorRouting.test.tsx.
    [Fact]
    public async Task CreateOnSave_WhenSpeCreateReturnsNull_Returns200CarryingStorageFailedOutcome()
    {
        _fixture.ResetBoundaries();

        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("drive-storage-failed-001");
        // The SPE mint fails softly — Graph returned nothing. Nothing is stored.
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileHandleDto?)null);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            "/api/compose/documents/create-on-save",
            new
            {
                containerId = "b!container-bu-storage-failed",
                tenantId = ComposeCreateOnSaveFixture.TestTenantId,
                sessionId = Guid.NewGuid().ToString("N"),
                content = DraftBytes,
            });

        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the container-failure path returns a result (it carries the step projection), so the status stays 200 — body: {body}");

        using var payload = JsonDocument.Parse(body);
        payload.RootElement.GetProperty("outcome").GetString().Should().Be("storage-failed",
            "FR-S06: a 200 that stored nothing MUST say so on the wire — this field is the only thing " +
            "distinguishing it from a successful save, and its absence is how a total write failure " +
            "rendered as 'Saved ✓' across three releases");

        payload.RootElement.GetProperty("versionId").GetString().Should().BeEmpty(
            "no SPE version was committed — the outcome and the payload must agree");
    }

    [Fact]
    public async Task CreateOnSave_WhenSpeCreateSucceeds_Returns200CarryingPersistedOutcome()
    {
        // NEGATIVE pairing for the above: the happy path must still report `persisted`, so the new
        // field cannot manufacture false failures.
        _fixture.ResetBoundaries();

        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("drive-persisted-001");
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: "spe-item-persisted-001",
                Name: "draft.docx",
                ParentId: null,
                Size: DraftBytes.Length,
                CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"v1-etag\"",
                IsFolder: false,
                WebUrl: null,
                DriveId: "drive-persisted-001"));

        // The SUCCESS path continues past the SPE mint into promote + indexing, so those boundaries
        // must be arranged too (the storage-failed case above returns before reaching them).
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        _fixture.DataverseMock
            .Setup(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid.NewGuid(), true));
        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            "/api/compose/documents/create-on-save",
            new
            {
                containerId = "b!container-bu-persisted",
                tenantId = ComposeCreateOnSaveFixture.TestTenantId,
                sessionId = Guid.NewGuid().ToString("N"),
                content = DraftBytes,
            });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"body: {body}");

        using var payload = JsonDocument.Parse(body);
        payload.RootElement.GetProperty("outcome").GetString().Should().Be("persisted",
            "a clean save reports `persisted` — the outcome field must not manufacture false failures");
    }

    [Fact]
    public async Task CreateOnSave_WhenUnauthenticated_Returns401()
    {
        _fixture.ResetBoundaries();
        using var client = _fixture.CreateUnauthenticatedClient();

        using var content = JsonContent.Create(new { containerId = "c", tenantId = "t", sessionId = "s", content = DraftBytes });
        var response = await client.PostAsync("/api/compose/documents/create-on-save", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "create-on-save inherits RequireAuthorization() from the /api/compose group (ADR-008 + ADR-028)");
    }

    // ── Task 110 (UAT-R1) — non-waivable E2E DoD ────────────────────────────────────────────────
    // The Browse/open-local-file first Save legitimately has NO chat session — the client sends
    // sessionId:"" (ComposeWorkspace.tsx `sessionId: state.sessionId`, unset on the Browse path).
    // BEFORE task 110 this 400'd at ComposeEndpoints.cs:1312 (and would have thrown at
    // ComposeService.SaveAsync/PromoteIfEphemeralAsync). This drives the EMPTY-sessionId body
    // through the REAL create-on-save route and asserts 200 + the persisted sprk_document
    // side-effect, and that NO ChatSession is rebound (the FR-07 rebind is skipped, not attempted).
    // The WITH-session rebind-fires case is proven by
    // UploadThenCreateOnSave_TransientDraft_PersistsNewDocumentAndSpeItemAndRebindsSession above.
    [Fact]
    public async Task CreateOnSave_WithEmptySessionId_Returns200AndPersistsDocumentWithoutRebind()
    {
        // ── Arrange ────────────────────────────────────────────────────────────────────────────
        // Issue #858: the body carries NO containerId — this is the NEW client shape. The container
        // is server-derived; with no session (and so no matter) that is the acting user's BU.
        const string mintedSpeItemId = "spe-item-sessionless-001";
        const string resolvedDriveId = "drive-sessionless-001";
        const string sentinelDocId = "sentinel-doc-untouched";
        var newDocumentId = Guid.NewGuid();

        _fixture.ResetBoundaries();

        string? resolvedContainerArg = null;
        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((c, _) => resolvedContainerArg = c)
            .ReturnsAsync(resolvedDriveId);
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: mintedSpeItemId,
                Name: "draft.docx",
                ParentId: null,
                Size: DraftBytes.Length,
                CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"v1-etag\"",
                IsFolder: false,
                WebUrl: null,
                DriveId: resolvedDriveId));

        // Dataverse: no existing row → create fires. Capture the created entity for the persisted assertion.
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        Entity? createdEntity = null;
        _fixture.DataverseMock
            .Setup(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => createdEntity = e)
            .ReturnsAsync((newDocumentId, true));

        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        // Pre-create an UNRELATED real session bound to a sentinel DocumentId. The sessionless Save
        // must NOT mutate it — proving no rebind was attempted on the empty-session path.
        string unrelatedSessionId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
            var session = await sessions.CreateSessionAsync(
                ComposeCreateOnSaveFixture.TestTenantId, TestSessionOwner.Oid, documentId: sentinelDocId);
            unrelatedSessionId = session.SessionId;
        }

        using var client = _fixture.CreateAuthenticatedClient();

        // ── Act: create-on-save with EMPTY sessionId (the body the Browse client actually sends —
        //    and, post-#858, with no containerId at all) ──
        var response = await client.PostAsJsonAsync(
            "/api/compose/documents/create-on-save",
            new
            {
                tenantId = ComposeCreateOnSaveFixture.TestTenantId,
                sessionId = string.Empty,
                content = DraftBytes,
                displayName = "draft.docx",
            });

        // ── Assert: HTTP contract — 200, NOT the pre-110 400 ─────────────────────────────────────
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a transient first Save with no chat session must succeed (task 110 — the sessionId guard is relaxed on the create-on-save path)");
        var result = await response.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        result.Should().NotBeNull();
        result!.DocumentRecordId.Should().Be(newDocumentId, "a NEW sprk_document was created even without a session");
        result.WasPromotedThisSave.Should().BeTrue("first Save of a transient draft creates the record (FR-06)");
        result.DocumentSpeId.Should().Be(mintedSpeItemId, "the server returns the minted SPE id");

        // ── Assert: persisted side-effect (the non-waivable E2E DoD — a service-only test misses this) ──
        // REWRITTEN for issue #858 — was "the client-resolved BU container still flows through
        // without a session", which is the deleted defect. A session-less save derives server-side
        // from the acting user's business unit.
        resolvedContainerArg.Should().Be(TestActingUserBusinessUnit.ContainerId,
            "#858: with no session (and so no matter) the server derives the acting user's BU container");
        _fixture.SpeMock.Verify(s => s.UploadSmallAsUserAsync(
            It.IsAny<HttpContext>(), resolvedDriveId, It.IsAny<string>(),
            It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once,
            "a new SPE drive-item was minted in the resolved BU drive");
        createdEntity.Should().NotBeNull("a new sprk_document row was persisted without a session (R5-E: file + record + index still required)");
        createdEntity!.LogicalName.Should().Be("sprk_document");
        createdEntity.GetAttributeValue<string>("sprk_graphitemid").Should().Be(mintedSpeItemId,
            "the persisted document row points at the minted SPE drive-item");

        // ── Assert: NO rebind attempted — the unrelated session's binding is untouched ────────────
        using (var scope = _fixture.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
            var reloaded = await sessions.GetSessionAsync(
                ComposeCreateOnSaveFixture.TestTenantId, unrelatedSessionId, CancellationToken.None);
            reloaded.Should().NotBeNull();
            reloaded!.DocumentId.Should().Be(sentinelDocId,
                "the sessionless Save skips the FR-07 rebind entirely — no ChatSession is mutated");
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    // Issue #858 — the NEW server-side container-derivation paths, driven through the wire.
    //
    // These four are the FIRST tests of these guarantees at ANY layer (the unit suite covers only the
    // acting-user resolvable/unresolvable pair) — the compose-r8 coverage warning named this exact
    // region: 76.8% branch coverage with untested documented guarantees. Each asserts one leg of the
    // ResolveCreateOnSaveContainerAsync contract: authorized matter → ITS container · missing
    // AppendTo → typed 403, never an opaque 500 · foreign session → binding ignored, never borrowed ·
    // unsupported host type → typed 409 refusal, never a guessed container.
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The plural entity set EntityAccessFilter.TryResolveEntitySet maps sprk_matter to —
    /// the probe verify pins it so the AUTHORIZED record and the CONTAINER-SOURCE record are one.</summary>
    private const string MatterEntitySet = "sprk_matters";

    private async Task<string> CreateSessionWithHostContextAsync(string ownerOid, ChatHostContext hostContext)
    {
        using var scope = _fixture.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
        var session = await sessions.CreateSessionAsync(
            ComposeCreateOnSaveFixture.TestTenantId, ownerOid, documentId: null,
            playbookId: null, hostContext: hostContext);
        return session.SessionId;
    }

    /// <summary>
    /// Seed the securable-entity registry's cache so the REAL SecurableEntityRegistry answers from
    /// cache instead of attempting the live metadata query (whose ServiceClient the test host cannot
    /// unwrap from the loose IDataverseService stub). The REAL registry + REAL RecordContainerResolver
    /// still run — only their externally-sourced answer is arranged, same as every other boundary.
    /// </summary>
    private async Task SeedSecurableEntitiesAsync(params string[] entityLogicalNames)
    {
        using var scope = _fixture.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        await cache.SetAsync(
            SecurableEntityRegistry.CacheKey,
            JsonSerializer.SerializeToUtf8Bytes(entityLogicalNames));
    }

    private Task<HttpResponseMessage> PostCreateOnSaveAsync(HttpClient client, string sessionId) =>
        client.PostAsJsonAsync(
            "/api/compose/documents/create-on-save",
            new
            {
                tenantId = ComposeCreateOnSaveFixture.TestTenantId,
                sessionId,
                content = DraftBytes,
                displayName = "draft.docx",
            });

    [Fact]
    public async Task CreateOnSave_WhenSessionBoundToAuthorizedSecureMatter_WritesIntoTheMattersOwnContainer()
    {
        // The strongest form of the #858 derivation: a SECURE matter's own sprk_containerid wins and
        // the shared acting-user BU container — which IS arranged and resolvable — must not be touched.
        const string secureMatterContainerId = "b!secure-matter-own-container-001";
        var matterId = Guid.NewGuid();

        _fixture.ResetBoundaries();
        await SeedSecurableEntitiesAsync("sprk_matter");

        // The caller holds AppendTo — the OperationAccessPolicy requirement for
        // entity.associate_document, the SAME key the Office save path uses for the same act.
        _fixture.ProbeMock
            .Setup(p => p.GetCallerRightsAsync(
                It.IsAny<string?>(), MatterEntitySet, matterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseAccessRightsMapper.FromAccessRightsString(
                "ReadAccess,WriteAccess,AppendToAccess"));

        _fixture.DataverseMock
            .Setup(d => d.RetrieveAsync(
                "sprk_matter", matterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_matter", matterId)
            {
                ["sprk_issecure"] = true,
                ["sprk_containerid"] = secureMatterContainerId,
            });

        string? resolvedContainerArg = null;
        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((c, _) => resolvedContainerArg = c)
            .ReturnsAsync("drive-secure-matter-001");
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: "spe-item-secure-001", Name: "draft.docx", ParentId: null,
                Size: DraftBytes.Length, CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow, ETag: "\"v1\"",
                IsFolder: false, WebUrl: null, DriveId: "drive-secure-matter-001"));
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        _fixture.DataverseMock
            .Setup(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid.NewGuid(), true));
        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        var sessionId = await CreateSessionWithHostContextAsync(
            TestSessionOwner.Oid,
            new ChatHostContext(EntityType: "matter", EntityId: matterId.ToString()));
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await PostCreateOnSaveAsync(client, sessionId);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"body: {body}");
        resolvedContainerArg.Should().Be(secureMatterContainerId,
            "the container comes from the AUTHORIZED matter bound to the session — the authorization " +
            "key and the write destination are one value by construction");
        resolvedContainerArg.Should().NotBe(TestActingUserBusinessUnit.ContainerId,
            "the shared acting-user BU container was resolvable and must still not be chosen over the " +
            "secure matter's own container — SPE permissions are additive-only, so this write cannot " +
            "be retracted if it lands in the shared container");
        _fixture.ProbeMock.Verify(
            p => p.GetCallerRightsAsync(
                It.IsAny<string?>(), MatterEntitySet, matterId, It.IsAny<CancellationToken>()),
            Times.Once,
            "the caller was authorized against exactly the record the container came from");
    }

    [Fact]
    public async Task CreateOnSave_WhenCallerLacksAppendToOnBoundMatter_Returns403WithStableCode_NotOpaque500()
    {
        // The denial leg, asserted THROUGH the wire. Read-but-not-AppendTo succeeded before #858
        // (nothing was authorized at all); now it must arrive as a typed 403 carrying the stable
        // compose_record_access_denied code — and NOT as the "Save failed: SdapProblemException: …"
        // opaque 500 the save route's catch-all produced before the SdapProblemException mapping arm
        // (verified on the wire 2026-09-01). Nothing may be written on the way.
        var matterId = Guid.NewGuid();

        _fixture.ResetBoundaries();

        _fixture.ProbeMock
            .Setup(p => p.GetCallerRightsAsync(
                It.IsAny<string?>(), MatterEntitySet, matterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseAccessRightsMapper.FromAccessRightsString("ReadAccess"));

        var sessionId = await CreateSessionWithHostContextAsync(
            TestSessionOwner.Oid,
            new ChatHostContext(EntityType: "matter", EntityId: matterId.ToString()));
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await PostCreateOnSaveAsync(client, sessionId);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"a caller who can read the matter but not AppendTo it may not file documents against it — body: {body}");
        body.Should().Contain("compose_record_access_denied",
            "the stable code crosses the wire so the client can route the refusal");
        body.Should().NotContain("SdapProblemException",
            "the exception type is an implementation detail; leaking it is the opaque-500 shape DEF-14 forbids");
        _fixture.SpeMock.Verify(
            s => s.UploadSmallAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never, "denial precedes every write — nothing is stored speculatively");
        _fixture.DataverseMock.Verify(
            d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Never, "no record row may be minted for a refused save");
    }

    [Fact]
    public async Task CreateOnSave_WhenSessionOwnedByAnotherUser_IgnoresItsMatterBinding_AndDerivesFromActingUser()
    {
        // The borrow attack: supply someone ELSE's SessionId and receive their matter's container.
        // Session ownership is checked before the host context is trusted (issue #863's test, applied
        // to #858 for the same reason), so a foreign session's binding is DISCARDED — the save still
        // succeeds, but into the CALLER's own BU container, and the foreign matter is never even
        // authorization-probed.
        var foreignMatterId = Guid.NewGuid();

        _fixture.ResetBoundaries();

        string? resolvedContainerArg = null;
        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((c, _) => resolvedContainerArg = c)
            .ReturnsAsync("drive-foreign-session-001");
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: "spe-item-foreign-001", Name: "draft.docx", ParentId: null,
                Size: DraftBytes.Length, CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow, ETag: "\"v1\"",
                IsFolder: false, WebUrl: null, DriveId: "drive-foreign-session-001"));
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        _fixture.DataverseMock
            .Setup(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid.NewGuid(), true));
        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        // The session belongs to ANOTHER user but is bound to a (their) matter.
        var sessionId = await CreateSessionWithHostContextAsync(
            TestSessionOwner.OtherOid,
            new ChatHostContext(EntityType: "matter", EntityId: foreignMatterId.ToString()));
        using var client = _fixture.CreateAuthenticatedClient(); // authenticates as TestSessionOwner.Oid

        var response = await PostCreateOnSaveAsync(client, sessionId);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"body: {body}");
        resolvedContainerArg.Should().Be(TestActingUserBusinessUnit.ContainerId,
            "a session the caller does not own is not a trustworthy source of a matter binding — the " +
            "binding is ignored and the caller's own BU container is derived instead");
        _fixture.ProbeMock.Verify(
            p => p.GetCallerRightsAsync(
                It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the foreign matter must not even be authorization-probed — the binding is discarded before it");
    }

    [Fact]
    public async Task CreateOnSave_WhenSessionBoundToUnsupportedHostType_Returns409Refusal_NotAGuessedContainer()
    {
        // BuildMatterHostContext is the ONLY host-context producer Compose has and it hard-codes
        // "matter" — so a project-bound session cannot occur today, and if a future change makes it
        // occur, this path must REFUSE (visibly, typed) rather than guess a storage location that was
        // never authorized. The 409 carries compose_host_entity_unsupported; before the save route's
        // SdapProblemException mapping arm it would have shipped as an opaque 500.
        var projectId = Guid.NewGuid();

        _fixture.ResetBoundaries();

        var sessionId = await CreateSessionWithHostContextAsync(
            TestSessionOwner.Oid,
            new ChatHostContext(EntityType: "project", EntityId: projectId.ToString()));
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await PostCreateOnSaveAsync(client, sessionId);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            $"an unsupported host entity type is refused, never resolved to a fallback container — body: {body}");
        body.Should().Contain("compose_host_entity_unsupported",
            "the stable code makes a future project-bound session VISIBLE instead of silently misfiled");
        _fixture.SpeMock.Verify(
            s => s.UploadSmallAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never, "refusal precedes every write");
    }
}

/// <summary>
/// In-process BFF fixture that keeps the REAL <see cref="ComposeService"/> (NOT mocked) and replaces
/// only the external SPE / Dataverse / indexing boundaries with Moqs, so the endpoint→service wire
/// is genuinely exercised. Config-key set mirrors <c>ComposeContractFixture</c> (bff-extensions.md
/// §F.2 Fixture-Config-FIRST).
/// </summary>
public sealed class ComposeCreateOnSaveFixture : WebApplicationFactory<Program>
{
    public const string TestTenantId = "tenant-create-on-save-001";

    public Mock<ISpeFileOperations> SpeMock { get; } = new(MockBehavior.Loose);
    public Mock<IGenericEntityService> DataverseMock { get; } = new(MockBehavior.Loose);
    public Mock<IPostUploadIndexingEnqueuer> IndexingMock { get; } = new(MockBehavior.Loose);

    /// <summary>
    /// Issue #858: the authorization-answer boundary for a MATTER-bound create-on-save.
    /// <c>CallerRecordAccessProbe.GetCallerRightsAsync</c> is <c>public virtual</c> precisely so tests
    /// substitute the ANSWER without mocking its HttpClient transport (ADR-038 ban B1) — the same seam
    /// the unit-layer <c>ComposeServiceCollaborators.Probe</c> uses. The REAL
    /// <c>RecordContainerResolver</c> + <c>OperationAccessPolicy</c> stay in play.
    /// </summary>
    public Mock<Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe> ProbeMock { get; } =
        new(
            new HttpClient(),
            new ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe>.Instance,
            null!)
        { CallBase = false };

    /// <summary>Resets the boundary mocks between tests (xUnit runs a class's tests sequentially).</summary>
    public void ResetBoundaries()
    {
        SpeMock.Reset();
        DataverseMock.Reset();
        IndexingMock.Reset();
        ProbeMock.Reset();

        // Issue #858: the container is SERVER-derived on every create-on-save — a matter-less draft
        // derives it from the acting user's business unit, so the real derivation reads must be
        // arranged for the caller every request authenticates as. Re-applied here because Reset()
        // erases it. Tests that need a different shape (e.g. a business unit with NO container)
        // override after this call — Moq resolves overlaps last-setup-wins.
        TestActingUserBusinessUnit.Arrange(DataverseMock);
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
                ["Graph:ManagedIdentity:Enabled"] = "false",
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
            // Test hosts must not authenticate for real — see TestTokenCredential.
            services.UseStubTokenCredential();

            services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options =>
            {
                options.ThrowOnBadRequest = false;
            });

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = CreateOnSaveFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = CreateOnSaveFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, CreateOnSaveFakeAuthHandler>(
                CreateOnSaveFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = CreateOnSaveFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = CreateOnSaveFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            services.RemoveAll<IHostedService>();

            // Mock IDataverseService (used by the session cold-storage repo / health probes).
            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);

            // ── The point of this fixture: KEEP the real ComposeService; mock ONLY the external
            //    SPE / Dataverse-entity / indexing boundaries it depends on. ──
            services.RemoveAll<ISpeFileOperations>();
            services.AddSingleton(SpeMock.Object);

            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(DataverseMock.Object);

            services.RemoveAll<IPostUploadIndexingEnqueuer>();
            services.AddSingleton(IndexingMock.Object);

            // Issue #858: substitute the authorization-ANSWER boundary (see ProbeMock remarks). The
            // production registration is a typed HttpClient (ExternalAccessModule) whose real probe
            // would call Dataverse; the mock answers instead. The REAL RecordContainerResolver stays.
            services.RemoveAll<Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe>();
            services.AddSingleton(ProbeMock.Object);
        });
    }

    public HttpClient CreateUnauthenticatedClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }
}

/// <summary>
/// Fake auth handler that authenticates any request carrying an <c>Authorization</c> header and
/// emits <c>oid</c> + <c>tid</c> claims (the upload endpoint reads tenant from the <c>tid</c> claim).
/// </summary>
internal sealed class CreateOnSaveFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "CreateOnSaveFakeAuth";

    public CreateOnSaveFakeAuthHandler(
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

        // Issue #863 (fixture repair, bff-extensions.md §F.2): a STABLE oid. This minted a
        // fresh one per request, which Entra never does — every call arrived as a different
        // user, so the suite silently exercised cross-user access on every request.
        var oid = TestSessionOwner.Oid;
        var claims = new List<Claim>
        {
            new("oid", oid),
            new("tid", ComposeCreateOnSaveFixture.TestTenantId),
            new(System.Security.Claims.ClaimTypes.NameIdentifier, oid),
            new(System.Security.Claims.ClaimTypes.Name, $"Create-On-Save Test User {oid}"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
