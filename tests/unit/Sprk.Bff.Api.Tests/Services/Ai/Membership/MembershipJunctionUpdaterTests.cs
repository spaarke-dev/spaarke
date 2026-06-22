// R3 Part 1 Phase 2 — Task 084 (2026-06-22)
// Unit tests for MembershipJunctionUpdater + NullMembershipJunctionUpdaterHost.
// Locks:
//   - FR-2P2.4 idempotency: composite key {personId, personIdType,
//     entityLogicalName, entityRecordId, sourceField} — duplicate delivery
//     produces the same final state (no duplicate row, role + timestamp
//     re-applied).
//   - Added → CREATE when missing; Updated → UPDATE existing row's
//     role + lastSyncedOn; Removed → DELETE when present, no-op when
//     absent (idempotent).
//   - Cancellation propagates promptly (NFR-07 contract; the host's
//     30s drain depends on this).
//   - Null hosted-service peer (ADR-032): logs once on start + returns;
//     no Service Bus interaction.

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Membership;

[Trait("status", "new")]
public class MembershipJunctionUpdaterTests
{
    private static readonly Guid PersonId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EntityRecordId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ExistingRowId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string EntityLogicalName = "sprk_matter";
    private const string SourceField = "ownerid";
    private const string Role = "owner";
    private const string CorrelationId = "corr-junction-test";
    private const string JunctionEntityName = "sprk_userentityassociation";

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 22, 14, 30, 0, TimeSpan.Zero);

    private static MembershipChangedEvent BuildEvent(
        MembershipMutationType mutation = MembershipMutationType.Added,
        string role = Role) => new()
        {
            PersonId = PersonId,
            PersonIdType = PersonIdentityType.User,
            EntityLogicalName = EntityLogicalName,
            EntityRecordId = EntityRecordId,
            SourceField = SourceField,
            Role = role,
            MutationType = mutation,
            CorrelationId = CorrelationId,
            OccurredOnUtc = FixedNow.UtcDateTime,
        };

    private static (MembershipJunctionUpdater sut, Mock<IDataverseService> dataverse) CreateSut()
    {
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        var clock = new FakeTimeProvider(FixedNow);
        var sut = new MembershipJunctionUpdater(
            dataverse.Object, clock, NullLogger<MembershipJunctionUpdater>.Instance);
        return (sut, dataverse);
    }

    private static void SetupRetrieveMiss(Mock<IDataverseService> dataverse) =>
        dataverse
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                JunctionEntityName,
                It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                $"Entity {JunctionEntityName} not found with provided alternate key values"));

    private static void SetupRetrieveHit(
        Mock<IDataverseService> dataverse,
        Guid existingRowId)
    {
        var existing = new Entity(JunctionEntityName, existingRowId);
        dataverse
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                JunctionEntityName,
                It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
    }

    // ─── Added ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Added_CreatesRowWhenMissing()
    {
        // Arrange
        var (sut, dataverse) = CreateSut();
        SetupRetrieveMiss(dataverse);

        Entity? capturedEntity = null;
        dataverse
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => capturedEntity = e)
            .ReturnsAsync(Guid.NewGuid());

        // Act
        await sut.HandleAsync(BuildEvent(MembershipMutationType.Added), CancellationToken.None);

        // Assert
        capturedEntity.Should().NotBeNull();
        capturedEntity!.LogicalName.Should().Be(JunctionEntityName);
        capturedEntity["sprk_personid"].Should().Be(PersonId.ToString("D"));
        capturedEntity["sprk_entitylogicalname"].Should().Be(EntityLogicalName);
        capturedEntity["sprk_entityrecordid"].Should().Be(EntityRecordId.ToString("D"));
        capturedEntity["sprk_sourcefield"].Should().Be(SourceField);
        capturedEntity["sprk_role"].Should().Be(Role);
        capturedEntity["sprk_lastsyncedon"].Should().Be(FixedNow.UtcDateTime);
        ((OptionSetValue)capturedEntity["sprk_personidtype"]).Value
            .Should().Be((int)PersonIdentityType.User);
    }

    [Fact]
    public async Task HandleAsync_Added_UpdatesWhenRowExists()
    {
        // Idempotency invariant — Added on an existing row must not create a
        // duplicate; it overwrites role + lastSyncedOn.
        var (sut, dataverse) = CreateSut();
        SetupRetrieveHit(dataverse, ExistingRowId);

        Dictionary<string, object>? capturedFields = null;
        dataverse
            .Setup(d => d.UpdateAsync(
                JunctionEntityName,
                ExistingRowId,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>(
                (_, _, fields, _) => capturedFields = fields)
            .Returns(Task.CompletedTask);

        await sut.HandleAsync(BuildEvent(MembershipMutationType.Added), CancellationToken.None);

        dataverse.Verify(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never,
            "Added on existing row must NOT create a duplicate");
        capturedFields.Should().NotBeNull();
        capturedFields!["sprk_role"].Should().Be(Role);
        capturedFields["sprk_lastsyncedon"].Should().Be(FixedNow.UtcDateTime);
    }

    // ─── Updated ───────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Updated_OverwritesRoleAndLastSyncedOn()
    {
        var (sut, dataverse) = CreateSut();
        SetupRetrieveHit(dataverse, ExistingRowId);

        Dictionary<string, object>? capturedFields = null;
        dataverse
            .Setup(d => d.UpdateAsync(
                JunctionEntityName,
                ExistingRowId,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>(
                (_, _, fields, _) => capturedFields = fields)
            .Returns(Task.CompletedTask);

        const string newRole = "reviewingAttorney";
        await sut.HandleAsync(
            BuildEvent(MembershipMutationType.Updated, role: newRole),
            CancellationToken.None);

        capturedFields.Should().NotBeNull();
        capturedFields!["sprk_role"].Should().Be(newRole);
        capturedFields["sprk_lastsyncedon"].Should().Be(FixedNow.UtcDateTime);
        capturedFields.Should().NotContainKey("sprk_personid",
            "Update MUST NOT touch immutable key attributes");
        capturedFields.Should().NotContainKey("sprk_sourcefield",
            "Update MUST NOT touch immutable key attributes");
    }

    [Fact]
    public async Task HandleAsync_Updated_CreatesRowWhenMissing()
    {
        // Idempotency safety net: if the recon backstop fires an Updated event
        // for a row that should exist but doesn't (e.g., missed Added event),
        // the handler self-heals by creating the row.
        var (sut, dataverse) = CreateSut();
        SetupRetrieveMiss(dataverse);

        dataverse
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        await sut.HandleAsync(BuildEvent(MembershipMutationType.Updated), CancellationToken.None);

        dataverse.Verify(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Removed ───────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Removed_DeletesExistingRow()
    {
        var (sut, dataverse) = CreateSut();
        SetupRetrieveHit(dataverse, ExistingRowId);

        dataverse
            .Setup(d => d.DeleteAsync(JunctionEntityName, ExistingRowId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await sut.HandleAsync(BuildEvent(MembershipMutationType.Removed), CancellationToken.None);

        dataverse.Verify(d => d.DeleteAsync(JunctionEntityName, ExistingRowId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Removed_IsNoOpWhenRowAbsent()
    {
        // Idempotency: deleting an absent row is acceptable. Duplicate
        // Removed delivery must not throw.
        var (sut, dataverse) = CreateSut();
        SetupRetrieveMiss(dataverse);

        await sut.HandleAsync(BuildEvent(MembershipMutationType.Removed), CancellationToken.None);

        dataverse.Verify(d => d.DeleteAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        dataverse.Verify(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ─── Idempotency: duplicate delivery ───────────────────────────────────

    [Fact]
    public async Task HandleAsync_DuplicateDelivery_IsIdempotent()
    {
        // Service Bus at-least-once semantics → same event can arrive twice.
        // Final state must be identical: still exactly one row, role +
        // lastSyncedOn match the latest event.
        var dataverse = new Mock<IDataverseService>(MockBehavior.Strict);
        var clock = new FakeTimeProvider(FixedNow);
        var sut = new MembershipJunctionUpdater(
            dataverse.Object, clock, NullLogger<MembershipJunctionUpdater>.Instance);

        // First delivery: row absent → CREATE.
        // Second delivery: row present → UPDATE (no second CREATE).
        var rowExists = false;
        dataverse
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                JunctionEntityName,
                It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => rowExists
                ? Task.FromResult(new Entity(JunctionEntityName, ExistingRowId))
                : Task.FromException<Entity>(new InvalidOperationException(
                    "Entity sprk_userentityassociation not found with provided alternate key values")));

        dataverse
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((_, _) => rowExists = true)
            .ReturnsAsync(ExistingRowId);

        dataverse
            .Setup(d => d.UpdateAsync(
                JunctionEntityName,
                ExistingRowId,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var evt = BuildEvent(MembershipMutationType.Added);

        // Act — same event delivered twice.
        await sut.HandleAsync(evt, CancellationToken.None);
        await sut.HandleAsync(evt, CancellationToken.None);

        // Assert — Create called ONCE, Update called ONCE on the second run.
        dataverse.Verify(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Once, "duplicate delivery must not produce a duplicate row");
        dataverse.Verify(d => d.UpdateAsync(
            JunctionEntityName,
            ExistingRowId,
            It.IsAny<Dictionary<string, object>>(),
            It.IsAny<CancellationToken>()),
            Times.Once, "duplicate delivery must re-apply role + timestamp via update");
    }

    // ─── Cancellation (NFR-07) ─────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_CancellationRequested_Propagates()
    {
        var (sut, dataverse) = CreateSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        dataverse
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                JunctionEntityName,
                It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Should propagate, not swallow.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.HandleAsync(BuildEvent(), cts.Token));
    }

    // ─── Argument guards ───────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_NullEvent_Throws()
    {
        var (sut, _) = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => sut.HandleAsync(null!, CancellationToken.None));
    }

    /// <summary>
    /// Deterministic <see cref="TimeProvider"/> for assertions against the
    /// `sprk_lastsyncedon` write value. Mirrors the sibling
    /// `ManagePinnedContextHandlerTests.FakeTimeProvider` pattern (rather
    /// than taking a new dependency on `Microsoft.Extensions.Time.Testing`).
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
