// R3 Part 1 Phase 2 — Task 085 (2026-06-22)
// Unit tests for MembershipReconciliationJob.
//
// Locks:
//   - Discovers identity-Lookup fields via IMembershipFieldDiscoveryService
//     and dispatches one Updated event per (parent, descriptor, populated GUID).
//   - Missing junction rows are self-healed by the handler's Updated path
//     (idempotency contract from task 084).
//   - Orphan junction rows (parent's source Lookup now empty/changed) trigger
//     Removed events.
//   - Verified-still-correct rows result in Updated events (idempotent — the
//     handler refreshes lastSyncedOn).
//   - Duplicate runs produce identical state (handler idempotency).
//   - Cancellation propagates promptly between pages + entity types (NFR-07).
//   - Per-parent handler failures are logged + counted but do NOT fail the run.
//   - JobRunResult.ResultJson contains per-entity-type breakdown.

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Membership;

[Trait("status", "new")]
public class MembershipReconciliationJobTests
{
    private const string MatterEntity = "sprk_matter";
    private const string JunctionEntity = "sprk_userentityassociation";

    private static readonly Guid UserA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid MatterA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MatterB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OrphanJunctionRowId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private const string CorrelationId = "corr-recon-test";

    // ─── Test scaffolding ─────────────────────────────────────────────────

    private static MembershipDescriptor OwnerDescriptor() =>
        new(Field: "ownerid", Role: "owner", IdentityType: "User",
            TargetTable: "systemuser", Source: "auto");

    private static MembershipDescriptor AttorneyDescriptor() =>
        new(Field: "sprk_assignedattorney1", Role: "assignedAttorney",
            IdentityType: "User", TargetTable: "systemuser", Source: "auto");

    private static Entity BuildMatter(Guid matterId, Guid? owner, Guid? attorney)
    {
        var e = new Entity(MatterEntity, matterId);
        if (owner is { } o && o != Guid.Empty)
        {
            e["ownerid"] = new EntityReference("systemuser", o);
        }
        if (attorney is { } a && a != Guid.Empty)
        {
            e["sprk_assignedattorney1"] = new EntityReference("systemuser", a);
        }
        return e;
    }

    private static Entity BuildJunctionRow(
        Guid rowId,
        Guid personId,
        Guid matterId,
        string sourceField,
        string role = "owner")
    {
        var e = new Entity(JunctionEntity, rowId)
        {
            ["sprk_personid"] = personId.ToString("D"),
            ["sprk_personidtype"] = new OptionSetValue((int)PersonIdentityType.User),
            ["sprk_entitylogicalname"] = MatterEntity,
            ["sprk_entityrecordid"] = matterId.ToString("D"),
            ["sprk_sourcefield"] = sourceField,
            ["sprk_role"] = role,
        };
        return e;
    }

    private static (
        MembershipReconciliationJob job,
        Mock<IMembershipJunctionUpdater> updater,
        Mock<IMembershipFieldDiscoveryService> discovery,
        Mock<IGenericEntityService> entityService) BuildSut(
            MembershipReconciliationOptions? options = null)
    {
        var updater = new Mock<IMembershipJunctionUpdater>(MockBehavior.Strict);
        updater
            .Setup(u => u.HandleAsync(It.IsAny<MembershipChangedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var discovery = new Mock<IMembershipFieldDiscoveryService>(MockBehavior.Loose);
        var entityService = new Mock<IGenericEntityService>(MockBehavior.Loose);

        var services = new ServiceCollection();
        services.AddSingleton(updater.Object);
        services.AddSingleton(discovery.Object);
        services.AddSingleton(entityService.Object);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var opts = Options.Create(options ?? new MembershipReconciliationOptions
        {
            EntityTypes = new List<string> { MatterEntity },
            CronSchedule = "0 2 * * *",
            Enabled = true,
            FetchPageSize = 50,
            OrphanFetchPageSize = 50,
        });
        var monitor = new StaticOptionsMonitor<MembershipReconciliationOptions>(opts.Value);

        var job = new MembershipReconciliationJob(
            scopeFactory,
            monitor,
            NullLogger<MembershipReconciliationJob>.Instance);

        return (job, updater, discovery, entityService);
    }

    private static JobRunContext BuildCtx() =>
        new(RunId: Guid.NewGuid(),
            CorrelationId: CorrelationId,
            Trigger: JobRunTrigger.Scheduled,
            Parameters: new Dictionary<string, object>());

    private static void SetupDiscovery(
        Mock<IMembershipFieldDiscoveryService> discovery,
        string entityType,
        params MembershipDescriptor[] descriptors)
    {
        var result = new DiscoveryResult(
            EntityType: entityType,
            DiscoveredAt: DateTimeOffset.UtcNow,
            DiscoveredFields: descriptors,
            ExcludedFields: Array.Empty<IgnoredField>(),
            IgnoredFields: Array.Empty<IgnoredField>());

        discovery
            .Setup(d => d.DiscoverAsync(entityType, It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }

    private static void SetupParentScanReturnsOnce(
        Mock<IGenericEntityService> entityService,
        string entityType,
        params Entity[] parents)
    {
        var collection = new EntityCollection(parents.ToList()) { MoreRecords = false };
        entityService
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == entityType),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
    }

    private static void SetupOrphanScanReturnsOnce(
        Mock<IGenericEntityService> entityService,
        params Entity[] rows)
    {
        var collection = new EntityCollection(rows.ToList()) { MoreRecords = false };
        entityService
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == JunctionEntity),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(collection);
    }

    // ─── Tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DiscoversAllSprkAssignedLookups_ForMatter()
    {
        var (job, updater, discovery, entityService) = BuildSut();
        SetupDiscovery(discovery, MatterEntity, OwnerDescriptor(), AttorneyDescriptor());
        // Single matter with both lookups populated → expect 2 Updated events.
        SetupParentScanReturnsOnce(entityService, MatterEntity,
            BuildMatter(MatterA, owner: UserA, attorney: UserB));
        SetupOrphanScanReturnsOnce(entityService); // No orphans.

        var result = await job.ExecuteAsync(BuildCtx(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ProcessedItems.Should().Be(2, "two Updated events dispatched (owner + attorney)");
        updater.Verify(u => u.HandleAsync(
            It.Is<MembershipChangedEvent>(e =>
                e.MutationType == MembershipMutationType.Updated
                && e.EntityLogicalName == MatterEntity
                && e.EntityRecordId == MatterA),
            It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Verify both fields were dispatched (owner + attorney).
        updater.Verify(u => u.HandleAsync(
            It.Is<MembershipChangedEvent>(e => e.SourceField == "ownerid" && e.PersonId == UserA),
            It.IsAny<CancellationToken>()), Times.Once);
        updater.Verify(u => u.HandleAsync(
            It.Is<MembershipChangedEvent>(e => e.SourceField == "sprk_assignedattorney1" && e.PersonId == UserB),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MissingJunctionRow_EmitsUpdatedEventForHandlerSelfHeal()
    {
        // The recon job dispatches Updated for every populated lookup. The
        // handler's "missing row → CREATE" path (verified by task 084's
        // HandleAsync_Updated_CreatesRowWhenMissing test) self-heals missing rows.
        // This test ensures the job CALLS the handler — the handler test verifies
        // the actual CREATE behavior.
        var (job, updater, discovery, entityService) = BuildSut();
        SetupDiscovery(discovery, MatterEntity, OwnerDescriptor());
        SetupParentScanReturnsOnce(entityService, MatterEntity,
            BuildMatter(MatterA, owner: UserA, attorney: null));
        SetupOrphanScanReturnsOnce(entityService);

        var result = await job.ExecuteAsync(BuildCtx(), CancellationToken.None);

        result.Success.Should().BeTrue();
        updater.Verify(u => u.HandleAsync(
            It.Is<MembershipChangedEvent>(e =>
                e.MutationType == MembershipMutationType.Updated
                && e.PersonId == UserA
                && e.SourceField == "ownerid"
                && e.Role == "owner"
                && e.CorrelationId == CorrelationId),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_OrphanedJunctionRow_EmitsRemovedEvent()
    {
        // Parent has owner=UserA. Junction has TWO rows: ownerid→UserA (still valid)
        // AND sprk_assignedattorney1→UserB (now orphaned — parent's attorney is null).
        var (job, updater, discovery, entityService) = BuildSut();
        SetupDiscovery(discovery, MatterEntity, OwnerDescriptor(), AttorneyDescriptor());
        SetupParentScanReturnsOnce(entityService, MatterEntity,
            BuildMatter(MatterA, owner: UserA, attorney: null));
        SetupOrphanScanReturnsOnce(entityService,
            BuildJunctionRow(Guid.NewGuid(), UserA, MatterA, "ownerid", role: "owner"),
            BuildJunctionRow(OrphanJunctionRowId, UserB, MatterA, "sprk_assignedattorney1", role: "assignedAttorney"));

        var result = await job.ExecuteAsync(BuildCtx(), CancellationToken.None);

        result.Success.Should().BeTrue();
        // Updated for owner (parent still has it).
        updater.Verify(u => u.HandleAsync(
            It.Is<MembershipChangedEvent>(e =>
                e.MutationType == MembershipMutationType.Updated && e.PersonId == UserA),
            It.IsAny<CancellationToken>()),
            Times.Once);
        // Removed for orphaned attorney.
        updater.Verify(u => u.HandleAsync(
            It.Is<MembershipChangedEvent>(e =>
                e.MutationType == MembershipMutationType.Removed
                && e.PersonId == UserB
                && e.SourceField == "sprk_assignedattorney1"
                && e.EntityRecordId == MatterA),
            It.IsAny<CancellationToken>()),
            Times.Once);
        // No other calls.
        updater.Verify(u => u.HandleAsync(It.IsAny<MembershipChangedEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ExecuteAsync_VerifiedRow_TriggersUpdatedToRefreshLastSeen()
    {
        // The recon job uses Updated mutationType for every present lookup —
        // the handler's idempotent path refreshes sprk_lastsyncedon. This
        // test locks the contract that every verified-still-correct row gets
        // an Updated event (not Removed, not skipped).
        var (job, updater, discovery, entityService) = BuildSut();
        SetupDiscovery(discovery, MatterEntity, OwnerDescriptor());
        SetupParentScanReturnsOnce(entityService, MatterEntity,
            BuildMatter(MatterA, owner: UserA, attorney: null),
            BuildMatter(MatterB, owner: UserA, attorney: null));
        // Junction already has both rows present → all verified-still-correct.
        SetupOrphanScanReturnsOnce(entityService,
            BuildJunctionRow(Guid.NewGuid(), UserA, MatterA, "ownerid"),
            BuildJunctionRow(Guid.NewGuid(), UserA, MatterB, "ownerid"));

        var result = await job.ExecuteAsync(BuildCtx(), CancellationToken.None);

        result.Success.Should().BeTrue();
        // Two Updated events (one per matter); no Removed events.
        updater.Verify(u => u.HandleAsync(
            It.Is<MembershipChangedEvent>(e => e.MutationType == MembershipMutationType.Updated),
            It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        updater.Verify(u => u.HandleAsync(
            It.Is<MembershipChangedEvent>(e => e.MutationType == MembershipMutationType.Removed),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateRun_IsIdempotent()
    {
        // Running the recon twice with identical Dataverse state produces the
        // same dispatch volume the second time — the handler's natural-key
        // idempotency makes the resulting junction state identical.
        var (job, updater, discovery, entityService) = BuildSut();
        SetupDiscovery(discovery, MatterEntity, OwnerDescriptor());
        SetupParentScanReturnsOnce(entityService, MatterEntity,
            BuildMatter(MatterA, owner: UserA, attorney: null));
        SetupOrphanScanReturnsOnce(entityService,
            BuildJunctionRow(Guid.NewGuid(), UserA, MatterA, "ownerid"));

        var result1 = await job.ExecuteAsync(BuildCtx(), CancellationToken.None);
        var result2 = await job.ExecuteAsync(BuildCtx(), CancellationToken.None);

        result1.Success.Should().BeTrue();
        result2.Success.Should().BeTrue();
        result1.ProcessedItems.Should().Be(result2.ProcessedItems);
        updater.Verify(u => u.HandleAsync(It.IsAny<MembershipChangedEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2), "two runs each dispatch one Updated event");
    }

    [Fact]
    public async Task ExecuteAsync_PreCancelledToken_ReturnsEarlyWithSuccess()
    {
        // Pre-cancelled tokens are observed at the loop header in
        // ExecuteAsync, which breaks before dispatching any entity-type
        // recon. The result is a clean Success=true with 0 processed items —
        // no half-completed work, no exception leak.
        var (job, updater, _, _) = BuildSut();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await job.ExecuteAsync(BuildCtx(), cts.Token);

        result.Success.Should().BeTrue("pre-cancellation observed at the entity-type loop header");
        result.ProcessedItems.Should().Be(0);
        updater.Verify(u => u.HandleAsync(It.IsAny<MembershipChangedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationMidScan_AbortsCleanly()
    {
        // Cancellation observed DURING a parent scan (e.g., host shutdown
        // while a Dataverse round-trip is in flight) must propagate the
        // OperationCanceledException up through ExecuteAsync's outer catch,
        // yielding Success=false + cancellation-flavored ErrorMessage. This
        // is the NFR-07 30s drain contract.
        var (job, _, discovery, entityService) = BuildSut();
        SetupDiscovery(discovery, MatterEntity, OwnerDescriptor());

        using var cts = new CancellationTokenSource();

        entityService
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns<QueryExpression, CancellationToken>((_, _) =>
            {
                // Simulate the host triggering shutdown while the SDK call is
                // in flight — cancel the source THEN throw OCE.
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });

        var result = await job.ExecuteAsync(BuildCtx(), cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Cancelled", "host-shutdown drain (NFR-07)");
    }

    [Fact]
    public async Task ExecuteAsync_BadParentRecord_LogsAndContinues()
    {
        // Handler throws for one parent → recon continues with the next.
        var (job, updater, discovery, entityService) = BuildSut();
        SetupDiscovery(discovery, MatterEntity, OwnerDescriptor());
        SetupParentScanReturnsOnce(entityService, MatterEntity,
            BuildMatter(MatterA, owner: UserA, attorney: null),
            BuildMatter(MatterB, owner: UserB, attorney: null));
        SetupOrphanScanReturnsOnce(entityService);

        // Reset to non-strict so we can mix throwing + non-throwing setups.
        updater.Reset();
        updater
            .Setup(u => u.HandleAsync(
                It.Is<MembershipChangedEvent>(e => e.EntityRecordId == MatterA),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated handler failure"));
        updater
            .Setup(u => u.HandleAsync(
                It.Is<MembershipChangedEvent>(e => e.EntityRecordId == MatterB),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await job.ExecuteAsync(BuildCtx(), CancellationToken.None);

        result.Success.Should().BeTrue("per-row errors must not fail the whole run");
        // ResultJson should reflect the error count.
        result.ResultJson.Should().NotBeNullOrEmpty();
        var payload = JsonDocument.Parse(result.ResultJson!);
        var entities = payload.RootElement.GetProperty("entities");
        entities.GetArrayLength().Should().Be(1);
        entities[0].GetProperty("errors").GetInt32().Should().BeGreaterThan(0);
        entities[0].GetProperty("verified").GetInt32().Should().Be(1, "MatterB succeeds even though MatterA fails");
    }

    [Fact]
    public async Task ExecuteAsync_Result_ContainsPerEntityBreakdown()
    {
        var (job, _, discovery, entityService) = BuildSut();
        SetupDiscovery(discovery, MatterEntity, OwnerDescriptor(), AttorneyDescriptor());
        SetupParentScanReturnsOnce(entityService, MatterEntity,
            BuildMatter(MatterA, owner: UserA, attorney: UserB));
        SetupOrphanScanReturnsOnce(entityService);

        var result = await job.ExecuteAsync(BuildCtx(), CancellationToken.None);

        result.ResultJson.Should().NotBeNullOrEmpty();
        var payload = JsonDocument.Parse(result.ResultJson!);
        var entities = payload.RootElement.GetProperty("entities");
        entities.GetArrayLength().Should().Be(1);

        var matterEntry = entities[0];
        matterEntry.GetProperty("entityType").GetString().Should().Be(MatterEntity);
        matterEntry.GetProperty("discoveredFields").GetInt32().Should().Be(2);
        matterEntry.GetProperty("parentRowsScanned").GetInt32().Should().Be(1);
        matterEntry.GetProperty("verified").GetInt32().Should().Be(2);
        matterEntry.GetProperty("removed").GetInt32().Should().Be(0);
        matterEntry.GetProperty("errors").GetInt32().Should().Be(0);
        matterEntry.GetProperty("durationMs").GetInt64().Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task ExecuteAsync_DiscoveryFailure_RecordsErrorAndContinues()
    {
        var (job, _, discovery, _) = BuildSut(new MembershipReconciliationOptions
        {
            EntityTypes = new List<string> { MatterEntity, "sprk_event" },
            FetchPageSize = 50,
            OrphanFetchPageSize = 50,
        });

        // Matter discovery throws; event discovery succeeds but has no fields.
        discovery
            .Setup(d => d.DiscoverAsync(MatterEntity, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("entity not found"));
        discovery
            .Setup(d => d.DiscoverAsync("sprk_event", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DiscoveryResult(
                EntityType: "sprk_event",
                DiscoveredAt: DateTimeOffset.UtcNow,
                DiscoveredFields: Array.Empty<MembershipDescriptor>(),
                ExcludedFields: Array.Empty<IgnoredField>(),
                IgnoredFields: Array.Empty<IgnoredField>()));

        var result = await job.ExecuteAsync(BuildCtx(), CancellationToken.None);

        result.Success.Should().BeTrue("discovery failure on one entity must not fail the whole run");
        var payload = JsonDocument.Parse(result.ResultJson!);
        var entities = payload.RootElement.GetProperty("entities");
        entities.GetArrayLength().Should().Be(2);

        var matterEntry = entities.EnumerateArray().Single(e => e.GetProperty("entityType").GetString() == MatterEntity);
        matterEntry.GetProperty("errors").GetInt32().Should().Be(1);
        matterEntry.GetProperty("errorMessage").GetString().Should().Contain("Discovery failed");

        var eventEntry = entities.EnumerateArray().Single(e => e.GetProperty("entityType").GetString() == "sprk_event");
        eventEntry.GetProperty("discoveredFields").GetInt32().Should().Be(0);
        eventEntry.GetProperty("errors").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_NoEntityTypesConfigured_ReturnsEmptySuccess()
    {
        var (job, updater, _, _) = BuildSut(new MembershipReconciliationOptions
        {
            EntityTypes = new List<string>(),
        });

        var result = await job.ExecuteAsync(BuildCtx(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ProcessedItems.Should().Be(0);
        updater.Verify(u => u.HandleAsync(It.IsAny<MembershipChangedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void Metadata_ExposedCorrectly()
    {
        var (job, _, _, _) = BuildSut();
        job.JobId.Should().Be("membership-reconciliation");
        job.DisplayName.Should().Be("Membership Junction Reconciliation");
        job.Description.Should().Contain("FR-2P2.7");
        MembershipReconciliationJob.JobIdConstant.Should().Be(job.JobId);
    }

    // ─── ReadLookupAsIdentity unit tests (internal helper) ─────────────────

    [Fact]
    public void ReadLookupAsIdentity_PopulatedLookup_ReturnsIdAndType()
    {
        var matter = BuildMatter(MatterA, owner: UserA, attorney: null);
        var (id, type) = MembershipReconciliationJob.ReadLookupAsIdentity(matter, OwnerDescriptor());
        id.Should().Be(UserA);
        type.Should().Be(PersonIdentityType.User);
    }

    [Fact]
    public void ReadLookupAsIdentity_EmptyLookup_ReturnsNullPair()
    {
        var matter = BuildMatter(MatterA, owner: null, attorney: null);
        var (id, type) = MembershipReconciliationJob.ReadLookupAsIdentity(matter, OwnerDescriptor());
        id.Should().BeNull();
        type.Should().BeNull();
    }

    [Fact]
    public void ReadLookupAsIdentity_UnknownIdentityType_ReturnsNullPair()
    {
        var matter = BuildMatter(MatterA, owner: UserA, attorney: null);
        var weird = OwnerDescriptor() with { IdentityType = "BusinessUnit" };
        var (id, type) = MembershipReconciliationJob.ReadLookupAsIdentity(matter, weird);
        id.Should().BeNull("BusinessUnit is derived, not a real lookup target per Q4");
        type.Should().BeNull();
    }

    [Theory]
    [InlineData("User", PersonIdentityType.User)]
    [InlineData("SystemUser", PersonIdentityType.User)]
    [InlineData("Contact", PersonIdentityType.Contact)]
    [InlineData("Team", PersonIdentityType.Team)]
    [InlineData("Organization", PersonIdentityType.Organization)]
    public void TryParseIdentityType_KnownLabel_ReturnsType(string label, PersonIdentityType expected)
    {
        var ok = MembershipReconciliationJob.TryParseIdentityType(label, out var actual);
        ok.Should().BeTrue();
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("BusinessUnit")]
    [InlineData("Account")]
    [InlineData("Unknown")]
    public void TryParseIdentityType_UnknownLabel_ReturnsFalse(string? label)
    {
        var ok = MembershipReconciliationJob.TryParseIdentityType(label!, out _);
        ok.Should().BeFalse();
    }

    // ─── BuildAtLeastOneNotNullFilter ──────────────────────────────────────

    [Fact]
    public void BuildAtLeastOneNotNullFilter_ProducesOrFilterWithNotNullConditions()
    {
        var filter = MembershipReconciliationJob.BuildAtLeastOneNotNullFilter(
            new[] { OwnerDescriptor(), AttorneyDescriptor() });

        filter.FilterOperator.Should().Be(LogicalOperator.Or);
        filter.Conditions.Should().HaveCount(2);
        filter.Conditions.Should().AllSatisfy(c =>
            c.Operator.Should().Be(ConditionOperator.NotNull));
        filter.Conditions.Select(c => c.AttributeName).Should().BeEquivalentTo(
            new[] { "ownerid", "sprk_assignedattorney1" });
    }

    // ─── Argument guards ───────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_NullContext_Throws()
    {
        var (job, _, _, _) = BuildSut();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => job.ExecuteAsync(null!, CancellationToken.None));
    }

    /// <summary>
    /// Minimal <see cref="IOptionsMonitor{TOptions}"/> impl returning a fixed
    /// value (no change-token support — not needed for these tests).
    /// </summary>
    private sealed class StaticOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
    {
        public StaticOptionsMonitor(TOptions value) => CurrentValue = value;
        public TOptions CurrentValue { get; }
        public TOptions Get(string? name) => CurrentValue;
        public IDisposable OnChange(Action<TOptions, string?> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
