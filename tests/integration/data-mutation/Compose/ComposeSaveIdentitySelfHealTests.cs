// #781 item 2 — save-identity SELF-HEAL: a duplicated or key-broken sprk_graphitemid must not fail a
// user's save, and must never mint a third row.
//
// THE PRODUCTION EVENT THIS PROTECTS (dev UAT, 2026-08-17). `spaarkedev1` accumulated 105 duplicated
// `sprk_graphitemid` values across `sprk_document` (417 excess rows). A UNIQUE alternate key cannot
// build over non-unique data, so `sprk_graphitemid_uk` sat in `Failed`, and every alternate-key call
// threw one of two shapes:
//
//   "Found multiple records While trying to resolve alternate key"          (the value IS duplicated)
//   "... are not defined as keys ... sprk_graphitemid_uk (Not Active)"      (the index never built)
//
// Both are the same underlying condition seen from different angles. `TryFindDocumentByGraphItemIdAsync`
// swallowed BOTH as "not found", which sent an EXISTING document down the create branch, whose atomic
// upsert then failed on the very same broken key. Users saw an opaque 500 on a document that was
// sitting right there, and the preventive dedup could never run because the duplicates it was meant to
// prevent were what blocked its key.
//
// THE FIX AND ITS SHAPE. A plain COLUMN query does not use the alternate key, so it answers in both
// states. Resolving there lands the save on the IDEMPOTENT branch — which updates by record id and
// never touches the alternate key — so touching a duplicated document heals that save instead of
// failing it. The canonical row is chosen by a rule that must be DETERMINISTIC above all else
// (active-first, then oldest `createdon`, then lowest id); a rule that can return different answers to
// two concurrent callers is not a fix, it is the split-brain the unique key existed to prevent.
//
// WHAT IS DELIBERATELY *NOT* HEALED, pinned by the last two tests here: a genuinely NEW document still
// needs the key, because the create branch's atomic upsert is what closes the FR-07(d) TOCTOU race.
// Fixing the read is not a licence to weaken the write, and the fallback must not fire on the ordinary
// first-save path at all (the alt-key API throws for genuine not-found too, so an undiscriminating
// catch would put an extra Dataverse round-trip on every new document).
//
// KEEP path: tests/integration/data-mutation/** — "every new write path => >=1 integration test
// verifying rollback semantics" (tests/CLAUDE.md). The semantic here is: on a broken identity key,
// nothing NEW is written.

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Api.Ai;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Compose;

public sealed class ComposeSaveIdentitySelfHealTests : IClassFixture<ComposeCreateOnSaveFixture>
{
    private readonly ComposeCreateOnSaveFixture _fixture;

    public ComposeSaveIdentitySelfHealTests(ComposeCreateOnSaveFixture fixture) => _fixture = fixture;

    private const string DriveItemId = "spe-item-duplicated-001";
    private static readonly byte[] DraftBytes = { 0x50, 0x4B, 0x03, 0x04, 0x11, 0x22 };

    /// <summary>The exact Dataverse fault for a duplicated alternate-key value, wrapped the way
    /// <c>DataverseServiceClientImpl.RetrieveByAlternateKeyAsync</c> wraps every failure.</summary>
    private static InvalidOperationException FoundMultiple() => new(
        "Failed to retrieve sprk_document by alternate key: Found multiple records While trying to " +
        "resolve alternate key for the entity sprk_document.");

    /// <summary>The fault when the unique index never built over the duplicate data.</summary>
    private static InvalidOperationException KeyNotActive() => new(
        "Failed to retrieve sprk_document by alternate key: sprk_document With Ids = ... Or Keys = " +
        "sprk_graphitemid are not defined as keys for the entity: sprk_graphitemid_uk (Not Active).");

    /// <summary>Genuine not-found — what the API throws for a document that simply has no row yet.</summary>
    private static InvalidOperationException GenuineNotFound() => new(
        "Failed to retrieve sprk_document by alternate key: Entity sprk_document not found with " +
        "provided alternate key values");

    private static Entity Row(Guid id, int stateCode, DateTime createdOn)
    {
        var e = new Entity("sprk_document", id);
        e["sprk_documentid"] = id;
        e["statecode"] = new OptionSetValue(stateCode);
        e["createdon"] = createdOn;
        return e;
    }

    private static ComposeRecordResolution NewResolution(Mock<IGenericEntityService> dataverse) =>
        // `sessions` is only touched by RebindSessionDocumentIdAsync, which this slice never calls;
        // `dedupDetector` is the documented bare-ctor null. One mocked boundary, real logic.
        new(sessions: null!, dataverse.Object, NullLogger.Instance, dedupDetector: null);

    private static Mock<IGenericEntityService> DataverseWhereAltKeyThrows(
        InvalidOperationException fault,
        params Entity[] rowsTheColumnQueryReturns)
    {
        var dataverse = new Mock<IGenericEntityService>();
        dataverse
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(fault);
        dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(rowsTheColumnQueryReturns.ToList()));
        return dataverse;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (1) THE CANONICAL RULE — deterministic, and every term of it load-bearing.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TryFindDocumentByGraphItemId_WhenTheValueIsDuplicated_ResolvesTheOldestActiveRow()
    {
        // Three live rows for one drive item. The rule takes the OLDEST — deliberately NOT the issue's
        // suggested "newest modifiedon", because modifiedon moves whenever a row is touched, so two
        // concurrent saves could pick DIFFERENT canonicals. createdon never changes, and the oldest row
        // is also the one downstream records (matter links, regarding, activities) already point at.
        var oldest = Row(Guid.Parse("11111111-1111-1111-1111-111111111111"), 0, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));
        var middle = Row(Guid.Parse("22222222-2222-2222-2222-222222222222"), 0, new DateTime(2026, 3, 9, 0, 0, 0, DateTimeKind.Utc));
        var newest = Row(Guid.Parse("33333333-3333-3333-3333-333333333333"), 0, new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc));

        // Presented newest-first, so a rule that simply took the first row Dataverse handed back would
        // pick the wrong one and this test would catch it.
        var dataverse = DataverseWhereAltKeyThrows(FoundMultiple(), newest, middle, oldest);

        var resolved = await NewResolution(dataverse).TryFindDocumentByGraphItemIdAsync(DriveItemId, CancellationToken.None);

        resolved.Should().NotBeNull("a duplicated key must still resolve — returning null here sends an " +
            "EXISTING document into the create branch and mints yet another row");
        resolved!.Id.Should().Be(oldest.Id);
    }

    [Fact]
    public async Task TryFindDocumentByGraphItemId_WhenTheOldestDuplicateIsInactive_PrefersTheActiveRow()
    {
        // Active-first RANKS ahead of oldest: a deactivated row must never win a save. Note it ranks
        // rather than filters — see the all-inactive case below for why that distinction matters.
        var oldestButInactive = Row(Guid.Parse("11111111-1111-1111-1111-111111111111"), 1, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));
        var activeButNewer = Row(Guid.Parse("22222222-2222-2222-2222-222222222222"), 0, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var dataverse = DataverseWhereAltKeyThrows(FoundMultiple(), oldestButInactive, activeButNewer);

        var resolved = await NewResolution(dataverse).TryFindDocumentByGraphItemIdAsync(DriveItemId, CancellationToken.None);

        resolved!.Id.Should().Be(activeButNewer.Id,
            "a save must not be written into a deactivated document row");
    }

    [Fact]
    public async Task TryFindDocumentByGraphItemId_WhenEveryDuplicateIsInactive_StillResolvesOneRatherThanMinting()
    {
        // The reason the rule RANKS on statecode instead of FILTERING on it. Filtering would return zero
        // rows here, the method would report not-found, and the create branch would mint a row — adding
        // to the very duplication being healed. Resolving onto an inactive row is a worse-but-recoverable
        // outcome; minting is not.
        var older = Row(Guid.Parse("11111111-1111-1111-1111-111111111111"), 1, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));
        var newer = Row(Guid.Parse("22222222-2222-2222-2222-222222222222"), 1, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var dataverse = DataverseWhereAltKeyThrows(FoundMultiple(), newer, older);

        var resolved = await NewResolution(dataverse).TryFindDocumentByGraphItemIdAsync(DriveItemId, CancellationToken.None);

        resolved!.Id.Should().Be(older.Id);
    }

    [Fact]
    public async Task TryFindDocumentByGraphItemId_WhenRowsShareACreationTimestamp_IsStableAcrossCalls()
    {
        // Determinism is the property that makes this a fix rather than a coin flip. Two rows created in
        // the same instant still have a total order, via the record id.
        var sameInstant = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc);
        var lowerId = Row(Guid.Parse("11111111-1111-1111-1111-111111111111"), 0, sameInstant);
        var higherId = Row(Guid.Parse("99999999-9999-9999-9999-999999999999"), 0, sameInstant);

        var presentedOneWay = DataverseWhereAltKeyThrows(FoundMultiple(), higherId, lowerId);
        var presentedTheOther = DataverseWhereAltKeyThrows(FoundMultiple(), lowerId, higherId);

        var first = await NewResolution(presentedOneWay).TryFindDocumentByGraphItemIdAsync(DriveItemId, CancellationToken.None);
        var second = await NewResolution(presentedTheOther).TryFindDocumentByGraphItemIdAsync(DriveItemId, CancellationToken.None);

        first!.Id.Should().Be(lowerId.Id);
        second!.Id.Should().Be(first.Id,
            "two callers seeing the same rows in different orders MUST choose the same canonical — " +
            "otherwise the heal reintroduces the split-brain the unique key existed to prevent");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (2) THE OTHER FAULT SHAPE — an index that never built, with no duplication left to find.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TryFindDocumentByGraphItemId_WhenTheUniqueIndexIsNotActive_StillResolvesTheSingleRow()
    {
        // The outage shape: the key is Failed, so EVERY alt-key call throws — including for documents
        // that are not duplicated at all. Before the heal, saving any existing Compose document 500'd
        // for as long as the key stayed broken.
        var theOnlyRow = Row(Guid.Parse("44444444-4444-4444-4444-444444444444"), 0, new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc));
        var dataverse = DataverseWhereAltKeyThrows(KeyNotActive(), theOnlyRow);

        var resolved = await NewResolution(dataverse).TryFindDocumentByGraphItemIdAsync(DriveItemId, CancellationToken.None);

        resolved!.Id.Should().Be(theOnlyRow.Id,
            "a column query does not use the alternate key, so it answers even while the index is Failed");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (3) THE BOUNDARIES — what the heal must NOT do.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TryFindDocumentByGraphItemId_WhenTheKeyIsBrokenButNoRowCarriesTheValue_ReportsNotFound()
    {
        // A genuine first save during a key outage. The heal must hand back null so the create branch
        // runs; swallowing this as "resolved" would silently drop the document's record.
        var dataverse = DataverseWhereAltKeyThrows(KeyNotActive() /* no rows */);

        var resolved = await NewResolution(dataverse).TryFindDocumentByGraphItemIdAsync(DriveItemId, CancellationToken.None);

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task TryFindDocumentByGraphItemId_OnAnOrdinaryFirstSave_DoesNotIssueAFallbackQuery()
    {
        // The alt-key API throws for GENUINE not-found as well, so an undiscriminating catch would run
        // the fallback query on every new document's first save — an extra Dataverse round-trip on the
        // hot path, permanently, to heal a fault that is not occurring. This test is what keeps the
        // catch filter narrow.
        var dataverse = new Mock<IGenericEntityService>();
        dataverse
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(GenuineNotFound());

        var resolved = await NewResolution(dataverse).TryFindDocumentByGraphItemIdAsync(DriveItemId, CancellationToken.None);

        resolved.Should().BeNull();
        dataverse.Verify(
            d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the fallback is for a BROKEN key, not for the ordinary absence of a row");
    }

    [Fact]
    public async Task TryFindDocumentByGraphItemId_WhenTheFallbackQueryAlsoFails_ReportsNotFoundRatherThanThrowing()
    {
        // Best-effort: if the heal itself cannot run, leave the save on its pre-#781 path (create branch
        // -> upsert -> the honest 409/503 the endpoint maps) rather than converting one fault into a
        // different, less recognisable one.
        var dataverse = new Mock<IGenericEntityService>();
        dataverse
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(FoundMultiple());
        dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse is having a bad day"));

        var act = async () => await NewResolution(dataverse)
            .TryFindDocumentByGraphItemIdAsync(DriveItemId, CancellationToken.None);

        await act.Should().NotThrowAsync();
        (await NewResolution(dataverse).TryFindDocumentByGraphItemIdAsync(DriveItemId, CancellationToken.None))
            .Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (4) END TO END — the guarantee that actually matters to a user: no third row.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreateOnSave_WhenTheGraphItemIdIsDuplicated_LandsOnTheCanonicalRowAndMintsNoNewOne()
    {
        // Drives the REAL create-on-save route with the REAL ComposeService. The transient key resolves
        // to an existing document (so the save takes the replace-content path, i.e. "every save after
        // the first"), and the graphitemid alternate key is BROKEN underneath it.
        //
        // Before #781 this returned an opaque 500 by way of the create branch's upsert. The assertion
        // that carries the guarantee is the LAST one: UpsertAsync is never called, so no third row can
        // exist regardless of what the mock would have returned.
        const string transientKey = "transient-key-selfheal-001";
        const string existingDriveId = "drive-existing-selfheal";
        var canonicalId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var duplicateId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var wouldBeThirdRowId = Guid.NewGuid();

        _fixture.ResetBoundaries();

        var transientRow = new Entity("sprk_document", canonicalId);
        transientRow["sprk_documentid"] = canonicalId;
        transientRow["sprk_graphitemid"] = DriveItemId;
        transientRow["sprk_graphdriveid"] = existingDriveId;

        // KEY-SENSITIVE, and asymmetric on purpose: sprk_composetransientkey_uk is a DIFFERENT key and
        // is unaffected by the graphitemid duplication, so it still answers. Only the graphitemid key
        // is broken — exactly the production shape.
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, KeyAttributeCollection key, string[] _, CancellationToken _) =>
            {
                if (key.TryGetValue("sprk_graphitemid", out _))
                {
                    throw FoundMultiple();
                }

                if (key.TryGetValue("sprk_composetransientkey", out var tk)
                    && string.Equals(tk as string, transientKey, StringComparison.Ordinal))
                {
                    return transientRow;
                }

                return null!;
            });

        _fixture.DataverseMock
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                Row(duplicateId, 0, new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc)),
                Row(canonicalId, 0, new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc)),
            }));

        // If the heal fails to resolve, THIS is what a user's save would have created.
        _fixture.DataverseMock
            .Setup(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((wouldBeThirdRowId, true));

        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("drive-should-not-be-used");
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: DriveItemId,
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

        var response = await client.PostAsJsonAsync(
            "/api/compose/documents/create-on-save",
            new
            {
                tenantId = ComposeCreateOnSaveFixture.TestTenantId,
                sessionId,
                content = DraftBytes,
                displayName = "draft.docx",
                transientKey,
            });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"a duplicated identity key must not surface to a user as a failed save — body: {body}");

        var result = await response.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        result!.DocumentRecordId.Should().Be(canonicalId,
            "the heal resolves to the oldest active row, deterministically");
        result.DocumentRecordId.Should().NotBe(wouldBeThirdRowId);

        _fixture.DataverseMock.Verify(
            d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "THE guarantee: healing a duplicated key must never add a row to the duplication");
    }
}
