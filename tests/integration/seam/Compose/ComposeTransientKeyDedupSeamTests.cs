// Task 022 (spaarkeai-compose-r5, FR-06 / gap G7) — the THROUGH-THE-WIRE proof that a TRANSIENT
// Compose draft dedups repeated create-on-save calls to ONE sprk_document record via the durable
// client-minted transient key (sprk_composetransientkey_uk alt-key), fixing the 8-duplicate UAT defect,
// and that "Save New Document" (forkNew) deliberately forks a fresh record.
//
// Root cause the fix addresses: a transient draft has NO SPE drive-item id until its first save mints
// one; the transient create-on-save branch minted a NEW SPE item on EVERY call, so a lost/raced
// round-trip (concurrent saves, a re-created mount, a new tab) produced another item → another record.
// The client now mints a stable transient key ONCE at mount and sends it on every create-on-save; the
// server resolves it against the alt-key BEFORE minting and REPLACES the existing item in place on a hit.
//
// Three slices, all across the REAL route (WebApplicationFactory), endpoint -> ComposeService ->
// SPE/Dataverse module boundary:
//
//   (A) Save-Version dedup — a create-on-save whose transient key matches an existing row REPLACES the
//       existing SPE item in place: NO new mint, NO new record.
//   (B) Save-New fork — forkNew=true SKIPS the transient-key dedup lookup entirely and mints a fresh
//       SPE item + creates a NEW record, even when a row with a transient key already exists.
//   (C) The 8-duplicate scenario — eight repeated create-on-save calls with the SAME transient key
//       produce exactly ONE record + ONE mint (+ seven in-place replaces), no duplicates.
//
// Reuses (root CLAUDE.md §11 — extend, don't introduce): ComposeFidelitySeamFixture (host + SPE/Dataverse/
// indexing module-boundary mocks + fake auth); the create-on-save arrange mirrors
// ComposeOriginRoutingSeamTests (task 020). No new fixture class.
//
// ADR-038 seam DoD: through-the-wire WebApplicationFactory slices only. NO Mock<HttpMessageHandler>, NO
// DI-registration test, NO ctor-null test. Mocks live only at the ISpeFileOperations /
// IGenericEntityService / IPostUploadIndexingEnqueuer boundaries. NFR-02/I-7: dedup resolves by KEY
// (the alt-key), never by content — asserted by the transient-key alt-key being the ONLY discriminator.

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
using Moq;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeTransientKeyDedupSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposeTransientKeyDedupSeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    private const string TransientKeyAttribute = "sprk_composetransientkey";
    private const string GraphItemIdAttribute = "sprk_graphitemid";
    private const string GraphDriveIdAttribute = "sprk_graphdriveid";

    private static readonly string[] Paragraphs =
    {
        "This engagement letter is drafted in the Compose editor.",
        "It persists on first Save via create-on-save.",
    };

    private static readonly string[] ParaIds = { "00000001", "00000002" };

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (A) Save-Version — a repeated create-on-save with the SAME transient key replaces in place.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SaveVersion_RepeatedCreateOnSaveSameTransientKey_ReplacesInPlace_OneRecord_ThroughTheWire()
    {
        var world = ArrangeTransientWorld();
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var transientKey = $"tk-{Guid.NewGuid():N}";
        var docBytes = BuildDocxWithParaIds(Paragraphs, ParaIds);

        using var client = _fixture.CreateAuthenticatedClient();

        // First save → mints the SPE item + creates the row (stamped with the transient key).
        var first = await client.PostAsJsonAsync("/api/compose/documents/create-on-save", new
        {
            containerId = world.ContainerId,
            tenantId = tenant,
            sessionId = string.Empty,
            displayName = "save-version.docx",
            content = docBytes,
            transientKey,
            forkNew = false,
        });
        (await ReadOkBody(first)).Should().NotBeNull();

        // Second save with the SAME transient key → dedup hit → replace in place (no new mint/record).
        var second = await client.PostAsJsonAsync("/api/compose/documents/create-on-save", new
        {
            containerId = world.ContainerId,
            tenantId = tenant,
            sessionId = string.Empty,
            displayName = "save-version.docx",
            content = docBytes,
            transientKey,
            forkNew = false,
        });
        (await ReadOkBody(second)).Should().NotBeNull();

        _fixture.DataverseMock.Verify(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Once, "the same transient key must resolve to ONE record — the second Save-Version replaces in place");
        _fixture.SpeMock.Verify(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once, "only the FIRST create-on-save mints a drive-item; the dedup hit takes the replace path");
        _fixture.SpeMock.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once, "the second save replaces the existing item's content in place");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (B) Save-New — forkNew SKIPS dedup and creates a NEW record even when a row already exists.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SaveNew_ForkNew_SkipsTransientKeyDedup_ForksNewRecord_ThroughTheWire()
    {
        _fixture.ResetBoundaries();

        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var containerId = $"b!container-022-fork-{Guid.NewGuid():N}";
        var forkSpeId = $"spe-item-022-fork-{Guid.NewGuid():N}";
        var forkDriveId = $"drive-022-fork-{Guid.NewGuid():N}";
        var docBytes = BuildDocxWithParaIds(Paragraphs, ParaIds);
        var forkKey = $"tk-{Guid.NewGuid():N}";

        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(forkDriveId);
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(forkSpeId, forkDriveId, size: 2048, eTag: "\"fork-etag\""));

        // A row ALREADY exists for this transient key — if the fork consulted the dedup lookup it would
        // replace in place. forkNew must skip it entirely (proven by Times.Never below).
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(),
                It.Is<KeyAttributeCollection>(k => k.ContainsKey(TransientKeyAttribute)),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildExistingRow(GraphItemIdAttribute, "spe-preexisting", GraphDriveIdAttribute, "drive-preexisting"));
        // The fork's freshly-minted SPE id has no row yet → graph-item-id promote lookup returns null → create fires.
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(),
                It.Is<KeyAttributeCollection>(k => k.ContainsKey(GraphItemIdAttribute)),
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

        // Save New Document: forkNew=true → must mint + create a NEW record, and MUST NOT consult the
        // transient-key alt-key at all (the fork deliberately skips dedup) even though a row exists.
        var fork = await client.PostAsJsonAsync("/api/compose/documents/create-on-save", new
        {
            containerId,
            tenantId = tenant,
            sessionId = string.Empty,
            displayName = "forked-copy.docx",
            content = docBytes,
            transientKey = forkKey,
            forkNew = true,
        });
        (await ReadOkBody(fork)).Should().NotBeNull();

        _fixture.DataverseMock.Verify(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(),
                It.Is<KeyAttributeCollection>(k => k.ContainsKey(TransientKeyAttribute)),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "forkNew (Save New Document) must SKIP the transient-key dedup lookup — a deliberate new document even when a matching row exists");
        _fixture.SpeMock.Verify(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once, "the fork mints a fresh SPE drive-item");
        _fixture.SpeMock.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never, "the fork never replaces an existing item — it forks a brand-new one");
        _fixture.DataverseMock.Verify(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Once, "the fork creates a brand-new sprk_document record");
    }

    private static Entity BuildExistingRow(string a1, string v1, string a2, string v2)
    {
        var row = new Entity("sprk_document", Guid.NewGuid());
        row[a1] = v1;
        row[a2] = v2;
        return row;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (C) The 8-duplicate scenario — eight repeated saves, ONE record.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EightRepeatedCreateOnSaveSameTransientKey_ProduceExactlyOneRecord_ThroughTheWire()
    {
        var world = ArrangeTransientWorld();
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var transientKey = $"tk-{Guid.NewGuid():N}";
        var docBytes = BuildDocxWithParaIds(Paragraphs, ParaIds);

        using var client = _fixture.CreateAuthenticatedClient();

        const int saves = 8; // the concrete "8-duplicate" UAT scenario
        for (var i = 0; i < saves; i++)
        {
            var response = await client.PostAsJsonAsync("/api/compose/documents/create-on-save", new
            {
                containerId = world.ContainerId,
                tenantId = tenant,
                sessionId = string.Empty,
                displayName = "eight-duplicate.docx",
                content = docBytes,
                transientKey,
                forkNew = false,
            });
            (await ReadOkBody(response)).Should().NotBeNull($"save #{i + 1} must succeed");
        }

        _fixture.DataverseMock.Verify(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Once, "eight repeated create-on-save calls with the SAME transient key must produce exactly ONE record — the 8-duplicate defect is fixed");
        _fixture.SpeMock.Verify(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once, "only the first save mints a drive-item; the remaining seven dedup to it");
        _fixture.SpeMock.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Exactly(saves - 1), "the seven follow-on saves each replace the ONE deduped item in place");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Shared through-the-wire arrange: a stateful transient world where the transient-key + graph-item-id
    // alt-key lookups return the row ONCE it has been created (models the durable alt-key uniqueness).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    private sealed record TransientWorld(string ContainerId, string MintedSpeId, string ResolvedDriveId);

    private TransientWorld ArrangeTransientWorld()
    {
        _fixture.ResetBoundaries();

        var world = new TransientWorld(
            ContainerId: $"b!container-022-{Guid.NewGuid():N}",
            MintedSpeId: $"spe-item-022-{Guid.NewGuid():N}",
            ResolvedDriveId: $"drive-022-{Guid.NewGuid():N}");

        // The single row this world creates — null until the first create-on-save creates it, then
        // returned by BOTH alt-key lookups (transient-key dedup + graph-item-id promote idempotency).
        Entity? createdRow = null;

        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(world.ResolvedDriveId);
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(world.MintedSpeId, world.ResolvedDriveId, size: 2048, eTag: "\"v1-etag\""));
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(world.MintedSpeId, world.ResolvedDriveId, size: 2048, eTag: "\"v2-etag\""));

        // Transient-key dedup lookup — null until the row exists, then the row carrying its SPE pointer.
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(),
                It.Is<KeyAttributeCollection>(k => k.ContainsKey(TransientKeyAttribute)),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => createdRow!);

        // Graph-item-id promote idempotency lookup — same row once created.
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(),
                It.Is<KeyAttributeCollection>(k => k.ContainsKey(GraphItemIdAttribute)),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => createdRow!);

        _fixture.DataverseMock
            .Setup(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) =>
            {
                var row = new Entity("sprk_document", Guid.NewGuid())
                {
                    [GraphItemIdAttribute] = e.Contains(GraphItemIdAttribute) ? e[GraphItemIdAttribute] : world.MintedSpeId,
                    [GraphDriveIdAttribute] = e.Contains(GraphDriveIdAttribute) ? e[GraphDriveIdAttribute] : world.ResolvedDriveId,
                };
                if (e.Contains(TransientKeyAttribute))
                {
                    row[TransientKeyAttribute] = e[TransientKeyAttribute];
                }
                createdRow = row;
            })
            .ReturnsAsync(() => (createdRow?.Id ?? Guid.NewGuid(), true)); // task 013 (FR-07d): upsert returns (id, created)

        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        return world;
    }

    private static async Task<string> ReadOkBody(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"the create-on-save must succeed — body: {body}");
        // Sanity: a well-formed save response is JSON carrying documentSpeId.
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("documentSpeId", out _).Should().BeTrue("the save response carries the SPE id");
        return body;
    }

    private static FileHandleDto BuildFileHandle(string speId, string driveId, int size, string eTag) =>
        new(Id: speId, Name: "transient-dedup.docx", ParentId: null, Size: size,
            CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
            ETag: eTag, IsFolder: false, WebUrl: null, DriveId: driveId);

    /// <summary>Builds a valid DOCX whose paragraphs carry the supplied physical w14:paraIds (mirrors the
    /// established per-file-local-helper convention in ComposeOriginRoutingSeamTests.BuildDocxWithParaIds).</summary>
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
