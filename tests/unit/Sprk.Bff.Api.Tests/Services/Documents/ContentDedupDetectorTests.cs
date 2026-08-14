using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Documents;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Documents;

/// <summary>
/// Behavior tests for the FR-C3 Tier-1 content-dedup detector. Each protects a concrete contract: a
/// byte-identical upload is reported as a duplicate (so the caller creates NO second canonical document), a
/// first upload returns its hash to stamp, the read routes through the <see cref="SpeFileStore"/> facade
/// (ADR-007), a duplicate NOTIFIES (never silent), and every failure mode is non-fatal (NFR-04). The
/// <see cref="SpeFileStore"/> facade is mocked over real operation classes (the codebase idiom); the Dataverse
/// boundary is <see cref="IGenericEntityService"/> (ADR-038-permitted module boundary).
/// </summary>
public class ContentDedupDetectorTests
{
    private static Mock<SpeFileStore> BuildSpeMock(string? hashToReturn, bool throwOnRead = false)
    {
        var gcf = Mock.Of<IGraphClientFactory>();
        var speMock = new Mock<SpeFileStore>(MockBehavior.Loose,
            new ContainerOperations(gcf, Mock.Of<ILogger<ContainerOperations>>()),
            new DriveItemOperations(gcf, Mock.Of<ILogger<DriveItemOperations>>()),
            new UploadSessionManager(gcf, Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
            new UserOperations(gcf, Mock.Of<ILogger<UserOperations>>()),
            null!);

        var setup = speMock.Setup(s => s.GetQuickXorHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()));
        if (throwOnRead)
            setup.ThrowsAsync(new InvalidOperationException("graph down"));
        else
            setup.ReturnsAsync(hashToReturn);
        return speMock;
    }

    private static EntityCollection Docs(params Guid[] ids)
    {
        var c = new EntityCollection();
        foreach (var id in ids) c.Entities.Add(new Entity("sprk_document", id));
        return c;
    }

    private static ContentDedupDetector Detector(Mock<SpeFileStore> spe, Mock<IGenericEntityService> ds)
    {
        var notifications = new NotificationService(ds.Object, NullLogger<NotificationService>.Instance);
        return new ContentDedupDetector(spe.Object, ds.Object, notifications, NullLogger<ContentDedupDetector>.Instance);
    }

    [Fact]
    public async Task ReconcileAsync_HashMatchesExistingDocument_ReturnsDuplicateWithCanonicalId()
    {
        var canonical = Guid.NewGuid();
        var spe = BuildSpeMock("hashA");
        var ds = new Mock<IGenericEntityService>();
        ds.Setup(g => g.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_document"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Docs(canonical));

        // ownerOid intentionally non-resolvable → notify short-circuits cleanly; the DECISION is what matters here.
        var decision = await Detector(spe, ds).ReconcileAsync("drive1", "item2", ownerOid: "not-a-guid", fileName: "brief.pdf");

        decision.IsDuplicate.Should().BeTrue();
        decision.CanonicalDocumentId.Should().Be(canonical);
        decision.CanonicalHash.Should().Be("hashA");
    }

    [Fact]
    public async Task ReconcileAsync_NoExistingDocument_ReturnsNotDuplicateWithHashToStamp()
    {
        var spe = BuildSpeMock("hashB");
        var ds = new Mock<IGenericEntityService>();
        ds.Setup(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        var decision = await Detector(spe, ds).ReconcileAsync("drive1", "item2", "not-a-guid", "brief.pdf");

        decision.IsDuplicate.Should().BeFalse();
        decision.CanonicalDocumentId.Should().BeNull();
        decision.CanonicalHash.Should().Be("hashB", "the first writer stamps the hash so later uploads dedup against it");
    }

    [Fact]
    public async Task ReconcileAsync_HashUnavailable_ReturnsNoDedupAndSkipsLookup()
    {
        var spe = BuildSpeMock(null); // hash facet absent / not yet populated
        var ds = new Mock<IGenericEntityService>(MockBehavior.Strict); // strict → fails if any Dataverse call is made

        var decision = await Detector(spe, ds).ReconcileAsync("drive1", "item2", "not-a-guid", "brief.pdf");

        decision.Should().Be(DedupDecision.NoDedup);
        ds.Verify(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReconcileAsync_HashReadThrows_IsNonFatalNoDedup()
    {
        var spe = BuildSpeMock(null, throwOnRead: true);
        var ds = new Mock<IGenericEntityService>(MockBehavior.Strict);

        var act = async () => await Detector(spe, ds).ReconcileAsync("drive1", "item2", "not-a-guid", "brief.pdf");

        var decision = await act.Should().NotThrowAsync();
        decision.Subject.Should().Be(DedupDecision.NoDedup);
    }

    [Fact]
    public async Task ReconcileAsync_LookupThrows_IsNonFatalAndStillReturnsHash()
    {
        var spe = BuildSpeMock("hashC");
        var ds = new Mock<IGenericEntityService>();
        ds.Setup(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse blip"));

        var decision = await Detector(spe, ds).ReconcileAsync("drive1", "item2", "not-a-guid", "brief.pdf");

        decision.IsDuplicate.Should().BeFalse("a failed lookup must never erroneously block a document");
        decision.CanonicalHash.Should().Be("hashC", "the hash is still returned so the caller stamps it (best-effort dedup)");
    }

    [Fact]
    public async Task ReconcileAsync_ReadsHashViaSpeFileStoreFacade_NotDirectGraph()
    {
        var spe = BuildSpeMock("hashD");
        var ds = new Mock<IGenericEntityService>();
        ds.Setup(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        await Detector(spe, ds).ReconcileAsync("driveX", "itemY", "not-a-guid", "brief.pdf");

        spe.Verify(s => s.GetQuickXorHashAsync("driveX", "itemY", It.IsAny<CancellationToken>()), Times.Once,
            "ADR-007: the hash is read through the SpeFileStore facade, never direct Graph in the detector");
    }

    [Fact]
    public async Task ReconcileAsync_DuplicateWithResolvableOwner_EmitsNotification()
    {
        var canonical = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        var spe = BuildSpeMock("hashE");
        var ds = new Mock<IGenericEntityService>();

        // detector's document lookup → a match
        ds.Setup(g => g.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_document"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Docs(canonical));
        // NotificationService.ResolveSystemUserIdAsync → the systemuser query
        var user = new EntityCollection();
        user.Entities.Add(new Entity("systemuser", systemUserId));
        ds.Setup(g => g.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        Entity? notification = null;
        ds.Setup(g => g.CreateAsync(It.Is<Entity>(e => e.LogicalName == "appnotification"), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => notification = e)
            .ReturnsAsync(Guid.NewGuid());

        var decision = await Detector(spe, ds).ReconcileAsync("drive1", "item2", ownerOid: oid.ToString(), fileName: "brief.pdf");

        decision.IsDuplicate.Should().BeTrue();
        notification.Should().NotBeNull("a detected duplicate must NOTIFY the uploader — never silent");
    }

    // ── FR-C3 graduate-on-divergence (email-communication-intelligence-r2) ──────────────────────────

    [Fact]
    public async Task ResolveContentIdentityAsync_HashMatchesCanonical_ReturnsHashAndCanonicalId_NoSideEffects()
    {
        var canonical = Guid.NewGuid();
        var spe = BuildSpeMock("hashLink");
        var ds = new Mock<IGenericEntityService>();
        ds.Setup(g => g.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_document"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Docs(canonical));
        // Strict on notification would blow up if the pure resolver notified — it MUST NOT.
        ds.Setup(g => g.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ResolveContentIdentityAsync must not create/notify"));

        var (hash, canonicalId) = await Detector(spe, ds).ResolveContentIdentityAsync("drive1", "item2");

        hash.Should().Be("hashLink");
        canonicalId.Should().Be(canonical, "the pure resolver reports the canonical without suppressing or notifying");
    }

    [Fact]
    public async Task ResolveContentIdentityAsync_HashUnavailable_ReturnsNullsAndSkipsLookup()
    {
        var spe = BuildSpeMock(null);
        var ds = new Mock<IGenericEntityService>(MockBehavior.Strict); // any Dataverse call = failure

        var (hash, canonicalId) = await Detector(spe, ds).ResolveContentIdentityAsync("drive1", "item2");

        hash.Should().BeNull();
        canonicalId.Should().BeNull();
        ds.Verify(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FindCanonicalByHash_ExcludesLinkedCopies_QueryFiltersCanonicalDocumentNull()
    {
        // A hash-linked COPY (sprk_canonicaldocument set) must NEVER be returned as canonical — otherwise a
        // third identical upload would dedup/link against a copy that is about to graduate. Guard: the lookup
        // query MUST filter sprk_canonicaldocument IS NULL.
        var spe = BuildSpeMock("hashX");
        var ds = new Mock<IGenericEntityService>();
        QueryExpression? captured = null;
        ds.Setup(g => g.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(new EntityCollection());

        await Detector(spe, ds).ResolveContentIdentityAsync("drive1", "item2");

        captured.Should().NotBeNull();
        captured!.Criteria.Conditions.Should().ContainSingle(c =>
            c.AttributeName == "sprk_canonicaldocument" && c.Operator == ConditionOperator.Null,
            "linked copies must be excluded from the canonical lookup (graduate-on-divergence)");
    }

    [Fact]
    public async Task NotifyLinkedCopyAsync_ResolvableOwner_EmitsLinkedNotification()
    {
        var canonical = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var systemUserId = Guid.NewGuid();
        var spe = BuildSpeMock("hashE"); // unused by NotifyLinkedCopyAsync but Detector needs a SpeFileStore
        var ds = new Mock<IGenericEntityService>();
        var user = new EntityCollection();
        user.Entities.Add(new Entity("systemuser", systemUserId));
        ds.Setup(g => g.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        Entity? notification = null;
        ds.Setup(g => g.CreateAsync(It.Is<Entity>(e => e.LogicalName == "appnotification"), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => notification = e)
            .ReturnsAsync(Guid.NewGuid());

        await Detector(spe, ds).NotifyLinkedCopyAsync(oid.ToString(), canonical, "draft.docx");

        notification.Should().NotBeNull("a hash-linked editable copy must NOTIFY the uploader — never silent");
    }

    [Fact]
    public async Task NotifyLinkedCopyAsync_UnresolvableOwner_IsNonFatalNoNotification()
    {
        var spe = BuildSpeMock("hashE");
        var ds = new Mock<IGenericEntityService>(MockBehavior.Strict); // no Dataverse call permitted

        var act = async () => await Detector(spe, ds).NotifyLinkedCopyAsync("not-a-guid", Guid.NewGuid(), "draft.docx");

        await act.Should().NotThrowAsync("a non-resolvable uploader degrades to a log, never throws");
    }
}
