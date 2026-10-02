// unified-access-control-r2 task 152 (ADR-034 A3, event semantics) — the two junction writers agree.
//
// The create-time publishers and MembershipReconciliationJob both write sprk_userentityassociation. Before task 152
// they keyed one membership two ways: the publishers wrote the caller's AAD oid as a User; reconciliation wrote the
// lookup VALUE but typed it from the descriptor — so a team-owned row's team id was recorded as a User. These tests
// pin: (1) reconciliation types a polymorphic owner from its value; (2) the key a publisher builds for a row equals
// the key reconciliation builds for the same row; (3) an old oid-keyed row is an orphan and is removed; (4) an
// application-user owner is skipped by BOTH writers.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Tests.Services.Ai.Membership.Events;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Membership;

[Trait("status", "task-152-uac-r2")]
public class MembershipOwnerKeyParityTests
{
    private const string Matter = "sprk_matter";
    private static readonly Guid MatterId = Guid.Parse("11111111-aaaa-aaaa-aaaa-111111111111");

    /// <summary>The polymorphic Owner descriptor exactly as discovery produces it: always SystemUser (ADR-034 A1.1).</summary>
    private static MembershipDescriptor OwnerDescriptor() =>
        new(Field: "ownerid", Role: "ownerid", IdentityType: "SystemUser", TargetTable: "systemuser", Source: "auto");

    [Theory]
    [InlineData("team", PersonIdentityType.Team)]
    [InlineData("systemuser", PersonIdentityType.User)]
    public void ReadLookupAsIdentity_TypesThePolymorphicOwnerFromItsValue(string logicalName, PersonIdentityType expected)
    {
        var row = new Entity(Matter, MatterId) { ["ownerid"] = new EntityReference(logicalName, OwnerEventTestKit.TeamId) };

        var (id, type) = MembershipReconciliationJob.ReadLookupAsIdentity(row, OwnerDescriptor());

        id.Should().Be(OwnerEventTestKit.TeamId);
        type.Should().Be(expected, "the descriptor says SystemUser for every Owner column; the value says what it is");
    }

    [Theory]
    [InlineData("team")]
    [InlineData("systemuser")]
    public async Task PublisherKey_EqualsReconciliationKey_ForTheSameRow(string ownerLogicalName)
    {
        var ownerId = ownerLogicalName == "team" ? OwnerEventTestKit.TeamId : OwnerEventTestKit.HumanUserId;
        var owner = new EntityReference(ownerLogicalName, ownerId);

        // Publisher side.
        var publisher = new RecordingMembershipEventPublisher();
        await MembershipOwnerEvents.PublishOwnerAddedAsync(
            publisher, OwnerEventTestKit.Dataverse().Object, Matter, MatterId, owner, "c", NullLogger.Instance, CancellationToken.None);
        var published = publisher.Published.Should().ContainSingle().Subject;

        // Reconciliation side, over the same row.
        var dispatched = await RunReconciliationAsync(
            new Entity(Matter, MatterId) { ["ownerid"] = owner },
            junctionRows: Array.Empty<Entity>());
        var reconciled = dispatched.Should().ContainSingle(e => e.MutationType == MembershipMutationType.Updated).Subject;

        (reconciled.PersonId, reconciled.PersonIdType, reconciled.EntityLogicalName, reconciled.EntityRecordId, reconciled.SourceField)
            .Should().Be((published.PersonId, published.PersonIdType, published.EntityLogicalName, published.EntityRecordId, published.SourceField),
                "both writers must produce the SAME natural key, or the junction holds one membership twice");
    }

    [Fact]
    public async Task OldOidKeyedJunctionRow_IsAnOrphan_AndIsRemoved()
    {
        // A row the pre-task publishers wrote: the caller's AAD oid, typed User, for a team-owned matter.
        var oidRow = new Entity("sprk_userentityassociation", Guid.NewGuid())
        {
            ["sprk_personid"] = OwnerEventTestKit.CallerOid.ToString("D"),
            ["sprk_personidtype"] = new OptionSetValue((int)PersonIdentityType.User),
            ["sprk_entitylogicalname"] = Matter,
            ["sprk_entityrecordid"] = MatterId.ToString("D"),
            ["sprk_sourcefield"] = "ownerid",
            ["sprk_role"] = "owner",
        };

        var dispatched = await RunReconciliationAsync(
            new Entity(Matter, MatterId) { ["ownerid"] = new EntityReference("team", OwnerEventTestKit.TeamId) },
            junctionRows: new[] { oidRow });

        dispatched.Should().Contain(e =>
            e.MutationType == MembershipMutationType.Removed
            && e.PersonId == OwnerEventTestKit.CallerOid
            && e.PersonIdType == PersonIdentityType.User);
        dispatched.Should().Contain(e =>
            e.MutationType == MembershipMutationType.Updated
            && e.PersonId == OwnerEventTestKit.TeamId
            && e.PersonIdType == PersonIdentityType.Team);
    }

    [Fact]
    public async Task ApplicationUserOwner_IsSkippedByBothWriters()
    {
        var owner = new EntityReference("systemuser", OwnerEventTestKit.ApplicationUserId);

        var publisher = new RecordingMembershipEventPublisher();
        await MembershipOwnerEvents.PublishOwnerAddedAsync(
            publisher, OwnerEventTestKit.Dataverse().Object, Matter, MatterId, owner, "c", NullLogger.Instance, CancellationToken.None);

        var dispatched = await RunReconciliationAsync(new Entity(Matter, MatterId) { ["ownerid"] = owner }, Array.Empty<Entity>());

        publisher.Published.Should().BeEmpty();
        dispatched.Should().NotContain(e => e.PersonId == OwnerEventTestKit.ApplicationUserId && e.MutationType == MembershipMutationType.Updated);
    }

    /// <summary>
    /// The publisher's "unknown" case: a user owner whose <c>applicationid</c> cannot be read (the read throws, or
    /// returns no row) publishes NOTHING — an unknown systemuser is never treated as a person; the nightly
    /// reconciliation re-decides it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApplicationUserCheckUnreadable_PublisherPublishesNothing(bool readThrows)
    {
        var dataverse = new Mock<IGenericEntityService>(MockBehavior.Strict);
        var read = dataverse.Setup(d => d.RetrieveAsync("systemuser", OwnerEventTestKit.HumanUserId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()));
        if (readThrows)
        {
            read.ThrowsAsync(new InvalidOperationException("read fault"));
        }
        else
        {
            read.ReturnsAsync((Entity)null!);
        }

        var publisher = new RecordingMembershipEventPublisher();
        var evt = await MembershipOwnerEvents.PublishOwnerAddedAsync(
            publisher, dataverse.Object, Matter, MatterId, new EntityReference("systemuser", OwnerEventTestKit.HumanUserId),
            "c", NullLogger.Instance, CancellationToken.None);

        evt.Should().BeNull();
        publisher.Published.Should().BeEmpty("an unreadable systemuser is never published as a person");
        dataverse.Verify(d => d.RetrieveAsync("systemuser", OwnerEventTestKit.HumanUserId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplicationUserCheckUnreadable_ReconciliationKeepsTheExistingRow()
    {
        // Never delete a person's membership on a read fault.
        var existing = new Entity("sprk_userentityassociation", Guid.NewGuid())
        {
            ["sprk_personid"] = OwnerEventTestKit.HumanUserId.ToString("D"),
            ["sprk_personidtype"] = new OptionSetValue((int)PersonIdentityType.User),
            ["sprk_entitylogicalname"] = Matter,
            ["sprk_entityrecordid"] = MatterId.ToString("D"),
            ["sprk_sourcefield"] = "ownerid",
            ["sprk_role"] = "owner",
        };

        var dispatched = await RunReconciliationAsync(
            new Entity(Matter, MatterId) { ["ownerid"] = new EntityReference("systemuser", OwnerEventTestKit.HumanUserId) },
            new[] { existing },
            systemUserReadFails: true);

        dispatched.Should().NotContain(e => e.MutationType == MembershipMutationType.Removed);
    }

    private static async Task<List<MembershipChangedEvent>> RunReconciliationAsync(
        Entity parent, IReadOnlyList<Entity> junctionRows, bool systemUserReadFails = false)
    {
        var dispatched = new List<MembershipChangedEvent>();
        var updater = new Mock<IMembershipJunctionUpdater>();
        updater.Setup(u => u.HandleAsync(It.IsAny<MembershipChangedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<MembershipChangedEvent, CancellationToken>((e, _) => dispatched.Add(e))
            .Returns(Task.CompletedTask);

        var discovery = new Mock<IMembershipFieldDiscoveryService>();
        discovery.Setup(d => d.DiscoverAsync(Matter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DiscoveryResult(Matter, DateTimeOffset.UtcNow, new[] { OwnerDescriptor() },
                Array.Empty<IgnoredField>(), Array.Empty<IgnoredField>()));

        var entityService = new Mock<IGenericEntityService>();
        entityService.Setup(s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == Matter), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { parent }));
        entityService.Setup(s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_userentityassociation"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(junctionRows.ToList()));
        if (systemUserReadFails)
        {
            entityService.Setup(s => s.RetrieveAsync("systemuser", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("read fault"));
        }
        else
        {
            var kit = OwnerEventTestKit.Dataverse();
            entityService.Setup(s => s.RetrieveAsync("systemuser", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .Returns((string e, Guid id, string[] c, CancellationToken ct) => kit.Object.RetrieveAsync(e, id, c, ct));
        }

        var services = new ServiceCollection();
        services.AddSingleton(updater.Object);
        services.AddSingleton(discovery.Object);
        services.AddSingleton(entityService.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var job = new MembershipReconciliationJob(
            scopeFactory,
            new StaticOptionsMonitor<MembershipReconciliationOptions>(new MembershipReconciliationOptions
            {
                EntityTypes = new List<string> { Matter }, FetchPageSize = 50, OrphanFetchPageSize = 50,
            }),
            NullLogger<MembershipReconciliationJob>.Instance);

        await job.ExecuteAsync(
            new JobRunContext(Guid.NewGuid(), "corr", JobRunTrigger.Scheduled, new Dictionary<string, object>()),
            CancellationToken.None);
        return dispatched;
    }

    private sealed class StaticOptionsMonitor<TOptions> : Microsoft.Extensions.Options.IOptionsMonitor<TOptions>
    {
        public StaticOptionsMonitor(TOptions value) => CurrentValue = value;
        public TOptions CurrentValue { get; }
        public TOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
