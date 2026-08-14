// FR-C3 content-dedup graduate-on-divergence (email-communication-intelligence-r2, R-2) — behavior tests
// for ComposeService.PromoteIfEphemeralAsync's create-on-save dedup hook.
//
// Why the direct-PromoteIfEphemeralAsync surface (not the full SaveAsync path): the create-branch LINK and the
// idempotent-branch GRADUATE are the whole behavior under test, and PromoteIfEphemeralAsync is a public seam on
// IComposeService — so these tests exercise exactly that logic without the SPE-upload / indexing / session
// scaffolding SaveAsync also drives.
//
// Model: an editable Compose document that saves byte-identical to an existing CANONICAL is recorded as a
// hash-linked COPY (sprk_canonicaldocument set + notified) — NOT suppressed (suppression is the immutable
// email-attachment path; suppressing an editable copy would cross-wire the session onto a foreign drive-item).
// On a later save whose content has diverged, the copy GRADUATES to its own canonical (link cleared).
//
// Mocking boundary (ADR-038 §4 — module boundaries only): IGenericEntityService (Dataverse) + the virtual
// ContentDedupDetector seam (mirrors OfficeDocumentPersistenceDedupTests). Each test names a production behavior
// that breaks if deleted.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Services.Documents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class ComposeContentDedupTests
{
    private const string Tenant = "tenant-c3";
    private const string DriveId = "drive-c3";
    private const string SpeItemId = "spe-item-c3";
    private const string CanonicalHashAttr = "sprk_canonicalhash";
    private const string CanonicalDocAttr = "sprk_canonicaldocument";

    private readonly Mock<ISpeFileOperations> _spe = new(MockBehavior.Loose);
    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Loose);
    private readonly Mock<IPostUploadIndexingEnqueuer> _indexing = new(MockBehavior.Loose);
    private readonly Mock<ChatSessionManager> _sessions;
    private readonly Mock<ContentDedupDetector> _dedup =
        new(MockBehavior.Loose, null!, null!, null!, NullLogger<ContentDedupDetector>.Instance);

    public ComposeContentDedupTests()
    {
        _sessions = new Mock<ChatSessionManager>(
            Mock.Of<ITenantCache>(),
            Mock.Of<IChatDataverseRepository>(),
            NullLogger<ChatSessionManager>.Instance,
            null!,
            null!);
    }

    // dedupDetector defaults null when omitted (the guarded no-op path — Test 5).
    private ComposeService CreateSut(ContentDedupDetector? detector) => new(
        _spe.Object,
        _sessions.Object,
        _dataverse.Object,
        _indexing.Object,
        NullLogger<ComposeService>.Instance,
        dedupDetector: detector);

    // SessionId "" skips the FR-07 rebind (no ChatSessionManager interaction) so the test targets the dedup hook.
    private static PromoteComposeDocumentRequest Request() => new()
    {
        DocumentSpeId = SpeItemId,
        SessionId = "",
        TenantId = Tenant,
        GraphDriveId = DriveId,
        FileName = "draft.docx",
    };

    private static DefaultHttpContext HttpCtx()
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("oid", Guid.NewGuid().ToString()) }, "test"));
        return ctx;
    }

    private void ArrangeNoExistingRow() =>
        _dataverse.Setup(d => d.RetrieveByAlternateKeyAsync("sprk_document", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);

    [Fact]
    public async Task CreateOnSave_NoContentMatch_StampsHash_DoesNotLink()
    {
        ArrangeNoExistingRow();
        Entity? created = null;
        _dataverse.Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => created = e)
            .ReturnsAsync(Guid.NewGuid());
        _dedup.Setup(d => d.ResolveContentIdentityAsync(DriveId, SpeItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("hashNew", (Guid?)null));

        var result = await CreateSut(_dedup.Object).PromoteIfEphemeralAsync(Request(), HttpCtx());

        result.WasCreated.Should().BeTrue();
        created.Should().NotBeNull();
        created!.GetAttributeValue<string>(CanonicalHashAttr).Should().Be("hashNew", "the first writer stamps the content identity");
        created.Contains(CanonicalDocAttr).Should().BeFalse("a first writer is a canonical, not a linked copy");
        _dedup.Verify(d => d.NotifyLinkedCopyAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateOnSave_ContentMatchesCanonical_LinksAndNotifies_DoesNotSuppress()
    {
        var canonical = Guid.NewGuid();
        var newId = Guid.NewGuid();
        ArrangeNoExistingRow();
        Entity? created = null;
        _dataverse.Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => created = e)
            .ReturnsAsync(newId);
        _dedup.Setup(d => d.ResolveContentIdentityAsync(DriveId, SpeItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("hashDup", canonical));

        var result = await CreateSut(_dedup.Object).PromoteIfEphemeralAsync(Request(), HttpCtx());

        // Editable copy is CREATED (not suppressed) — its own row + own drive-item, no session cross-wiring.
        result.WasCreated.Should().BeTrue("an editable Compose copy is never suppressed — it links, then graduates");
        result.DocumentRecordId.Should().Be(newId, "the result is the copy's OWN new record, not the canonical");
        created.Should().NotBeNull();
        created!.GetAttributeValue<EntityReference>(CanonicalDocAttr).Should().NotBeNull();
        created.GetAttributeValue<EntityReference>(CanonicalDocAttr)!.Id.Should().Be(canonical, "the copy links to the canonical it currently matches");
        created.GetAttributeValue<string>(CanonicalHashAttr).Should().Be("hashDup");
        _dedup.Verify(d => d.NotifyLinkedCopyAsync(It.IsAny<string>(), canonical, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once,
            "a linked copy must NOTIFY the uploader — never silent");
    }

    [Fact]
    public async Task SubsequentSave_StillIdentical_DoesNotGraduate()
    {
        var existingId = Guid.NewGuid();
        var canonical = Guid.NewGuid();
        // Idempotent branch: the row already exists (found by graphitemid alt-key), and the alt-key lookup
        // carries the FR-C3 dedup columns — it IS a linked copy.
        var row = new Entity("sprk_document", existingId);
        row[CanonicalDocAttr] = new EntityReference("sprk_document", canonical);
        row[CanonicalHashAttr] = "hashSame";
        _dataverse.Setup(d => d.RetrieveByAlternateKeyAsync("sprk_document", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        _dedup.Setup(d => d.ResolveContentIdentityAsync(DriveId, SpeItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("hashSame", (Guid?)null)); // live hash unchanged

        var result = await CreateSut(_dedup.Object).PromoteIfEphemeralAsync(Request(), HttpCtx());

        result.WasCreated.Should().BeFalse("idempotent branch — no new row");
        _dataverse.Verify(d => d.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never,
            "a still-identical linked copy has not diverged — it must NOT graduate");
    }

    [Fact]
    public async Task SubsequentSave_ContentDiverged_Graduates_ClearsLinkAndStampsNewHash()
    {
        var existingId = Guid.NewGuid();
        var canonical = Guid.NewGuid();
        var row = new Entity("sprk_document", existingId);
        row[CanonicalDocAttr] = new EntityReference("sprk_document", canonical);
        row[CanonicalHashAttr] = "hashOld";
        _dataverse.Setup(d => d.RetrieveByAlternateKeyAsync("sprk_document", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        _dedup.Setup(d => d.ResolveContentIdentityAsync(DriveId, SpeItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("hashNew", (Guid?)null)); // content DIVERGED

        Dictionary<string, object>? updated = null;
        _dataverse.Setup(d => d.UpdateAsync("sprk_document", existingId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, fields, _) => updated = fields)
            .Returns(Task.CompletedTask);

        await CreateSut(_dedup.Object).PromoteIfEphemeralAsync(Request(), HttpCtx());

        updated.Should().NotBeNull("a diverged linked copy graduates to its own canonical");
        updated!.Should().ContainKey(CanonicalDocAttr);
        updated![CanonicalDocAttr].Should().Be(DBNull.Value, "the link is CLEARED via the DBNull clear-sentinel");
        updated!.Should().ContainKey(CanonicalHashAttr);
        updated![CanonicalHashAttr].Should().Be("hashNew", "the graduated document stamps its own new content identity");
    }

    [Fact]
    public async Task CreateOnSave_NoDetector_IsNoOp_CreatesWithoutDedupStamps()
    {
        ArrangeNoExistingRow();
        Entity? created = null;
        _dataverse.Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => created = e)
            .ReturnsAsync(Guid.NewGuid());

        var result = await CreateSut(detector: null).PromoteIfEphemeralAsync(Request(), HttpCtx());

        result.WasCreated.Should().BeTrue();
        created.Should().NotBeNull();
        created!.Contains(CanonicalHashAttr).Should().BeFalse("no detector → no dedup stamp (pre-R2 behavior unchanged)");
        created.Contains(CanonicalDocAttr).Should().BeFalse();
    }

    [Fact]
    public async Task CreateOnSave_DedupThrows_IsNonFatal_StillCreates()
    {
        ArrangeNoExistingRow();
        _dataverse.Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _dedup.Setup(d => d.ResolveContentIdentityAsync(DriveId, SpeItemId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("graph blip"));

        var act = async () => await CreateSut(_dedup.Object).PromoteIfEphemeralAsync(Request(), HttpCtx());

        var result = await act.Should().NotThrowAsync("dedup is best-effort/non-fatal (NFR-04) — a failure must not fail the save");
        result.Subject!.WasCreated.Should().BeTrue();
        _dataverse.Verify(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
