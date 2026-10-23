using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Tests.Services.Communication; // CapturingLogger<T>
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// An in-memory Dataverse for <see cref="GateDecisionRecordSharing"/> (ontology task 042b, D-169): the communication, its sharees' systemuser
/// rows and the gate Decision Records (read through an EVALUATING <see cref="IGenericEntityService"/> double: Equal, Null, NotNull and In are
/// applied, so a query that asks the wrong question finds the wrong rows), a POA table behind <see cref="IDataverseRecordShareService"/>, and the
/// writer's connection (WhoAmI and the Assign). Every share, owner read, Assign and revoke is appended to <see cref="Calls"/> in order.
/// </summary>
internal sealed class GateSharingWorld
{
    public static readonly Guid WriterUser = DecisionRecordWriterTests.WriterUser;
    public const string Communication = "sprk_communication";
    public const string Record = "sprk_decisionrecord";

    public Dictionary<(string Table, Guid Id), Entity> Rows { get; } = new();
    public Dictionary<(string Table, Guid Id), Dictionary<DataversePrincipalRef, int>> Poa { get; } = new();
    public List<string> Calls { get; } = [];
    public Mock<IGenericEntityService> Entities { get; } = new();
    public Mock<IOrganizationServiceAsync2> Org { get; } = new();
    public Mock<IRecordOwnershipResolver> Ownership { get; } = new();
    public Mock<IMembershipCacheInvalidator> Cache { get; } = new();
    public CapturingLogger<GateDecisionRecordSharing> Log { get; } = new();
    public FakeShares Shares { get; }
    public GateDecisionRecordSharing Sharing { get; }

    public Exception? CommunicationReadFault { get; set; }
    public Exception? UserReadFault { get; set; }
    public Exception? OwnerReadFault { get; set; }
    public Exception? AssignFault { get; set; }
    public bool AssignLands { get; set; } = true;

    public GateSharingWorld()
    {
        Shares = new FakeShares(this);
        Entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string[] _, CancellationToken _) =>
            {
                if (table == Record)
                {
                    Calls.Add($"read-owner:{id:D}");
                    if (OwnerReadFault is { } ownerFault)
                        return Task.FromException<Entity>(ownerFault);
                }

                if (table == Communication && CommunicationReadFault is { } fault)
                    return Task.FromException<Entity>(fault);
                return Task.FromResult(Rows.TryGetValue((table, id), out var row) ? Clone(row) : new Entity(table, id));
            });
        Entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
            {
                if (query.EntityName == "systemuser" && UserReadFault is { } fault)
                    return Task.FromException<EntityCollection>(fault);
                if (query.EntityName == Communication && CommunicationReadFault is { } communicationFault)
                    return Task.FromException<EntityCollection>(communicationFault);
                var rows = Rows.Values.Where(r => r.LogicalName == query.EntityName && Satisfies(query, r)).Select(Clone)
                    .Take(query.TopCount ?? int.MaxValue).ToList();
                return Task.FromResult(new EntityCollection(rows));
            });
        Org.Setup(o => o.ExecuteAsync(It.IsAny<WhoAmIRequest>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult<OrganizationResponse>(new WhoAmIResponse { Results = { ["UserId"] = WriterUser } }));
        Org.Setup(o => o.ExecuteAsync(It.IsAny<AssignRequest>(), It.IsAny<CancellationToken>()))
            .Returns((OrganizationRequest request, CancellationToken _) =>
            {
                var assign = (AssignRequest)request;
                Calls.Add($"assign:{assign.Target.Id:D}:{assign.Assignee.Id:D}");
                if (AssignLands)
                    OwnByTeam(assign.Target.Id, assign.Assignee.Id);
                return AssignFault is { } fault ? Task.FromException<OrganizationResponse>(fault) : Task.FromResult(new OrganizationResponse());
            });
        Sharing = new GateDecisionRecordSharing(
            Entities.Object, Shares, new OntologyWriterDataverseClient(() => Org.Object, NullLogger<OntologyWriterDataverseClient>.Instance),
            Ownership.Object, Cache.Object, Log);
    }

    // ── Seeding ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The communication: owned by <paramref name="owner"/> (a user) unless <paramref name="owningTeam"/> names a team; filed under a
    /// matter when <paramref name="filedUnder"/> is given.</summary>
    public GateSharingWorld WithCommunication(Guid id, Guid owner, Guid? owningTeam = null, Guid? filedUnder = null)
    {
        var row = new Entity(Communication, id)
        {
            ["ownerid"] = owningTeam is { } team ? new EntityReference("team", team) : new EntityReference("systemuser", owner),
        };
        if (owningTeam is { } t)
            row["owningteam"] = new EntityReference("team", t);
        if (filedUnder is { } matter)
            row["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter);
        Rows[(Communication, id)] = row;
        return this;
    }

    /// <summary>A systemuser row; <paramref name="external"/> null leaves the flag blank.</summary>
    public GateSharingWorld WithUser(Guid id, bool? external = false, string? domainName = null)
    {
        var row = new Entity("systemuser", id) { ["domainname"] = domainName ?? $"user{id:N}@contoso.com" };
        if (external is { } flag)
            row["sprk_isexternal"] = flag;
        Rows[("systemuser", id)] = row;
        return this;
    }

    /// <summary>A gate Decision Record filed under the communication, created by the writer, owned by <paramref name="ownerUser"/>.</summary>
    public GateSharingWorld WithGateRecord(
        Guid id, Guid communicationId, Guid ownerUser, Guid? createdBy = null, Guid? confirmedBy = null, Guid? stampMatter = null)
    {
        var row = new Entity(Record, id)
        {
            ["sprk_regardingcommunication"] = new EntityReference(Communication, communicationId),
            ["owninguser"] = new EntityReference("systemuser", ownerUser),
            ["ownerid"] = new EntityReference("systemuser", ownerUser),
            ["createdby"] = new EntityReference("systemuser", createdBy ?? WriterUser),
        };
        if (confirmedBy is { } confirmer)
            row["sprk_confirmedby"] = new EntityReference("systemuser", confirmer);
        if (stampMatter is { } matter)
            row["sprk_regardingmatter"] = new EntityReference("sprk_matter", matter);
        Rows[(Record, id)] = row;
        return this;
    }

    public void OwnByTeam(Guid recordId, Guid team)
    {
        var row = Rows[(Record, recordId)];
        row.Attributes.Remove("owninguser");
        row["owningteam"] = new EntityReference("team", team);
        row["ownerid"] = new EntityReference("team", team);
    }

    public GateSharingWorld Share(string table, Guid id, Guid user, int mask = 1)
    {
        SharesOf(table, id)[DataversePrincipalRef.User(user)] = mask;
        return this;
    }

    public Dictionary<DataversePrincipalRef, int> SharesOf(string table, Guid id)
    {
        if (!Poa.TryGetValue((table, id), out var shares))
            Poa[(table, id)] = shares = new Dictionary<DataversePrincipalRef, int>();
        return shares;
    }

    /// <summary>The systemusers holding a share on the record.</summary>
    public IReadOnlyList<Guid> RecordSharees(Guid recordId) =>
        SharesOf(Record, recordId).Keys.Where(p => p.Kind == DataversePrincipalKind.SystemUser).Select(p => p.Id).OrderBy(g => g).ToList();

    public void OwnerAnswers(RecordOwnerResolution answer) =>
        Ownership.Setup(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(answer);

    /// <summary>
    /// 042b round 2: a helper a fault escapes (its gate-record read fails and its own failure log throws), for the callers' fault isolation.
    /// </summary>
    public static GateDecisionRecordSharing Faulting()
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test: the gate records could not be read."));
        var logger = new Mock<ILogger<GateDecisionRecordSharing>>();
        logger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        logger.Setup(l => l.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Throws(new InvalidOperationException("Test: the gate helper faulted."));
        return new GateDecisionRecordSharing(
            entities.Object, Mock.Of<IDataverseRecordShareService>(),
            new OntologyWriterDataverseClient(() => Mock.Of<IOrganizationServiceAsync2>(), NullLogger<OntologyWriterDataverseClient>.Instance),
            Mock.Of<IRecordOwnershipResolver>(), Mock.Of<IMembershipCacheInvalidator>(), logger.Object);
    }

    // ── The evaluating reader ────────────────────────────────────────────────────────────────────────────────────

    private static Entity Clone(Entity source)
    {
        var copy = new Entity(source.LogicalName, source.Id);
        foreach (var attribute in source.Attributes)
            copy[attribute.Key] = attribute.Value;
        return copy;
    }

    private static bool Satisfies(QueryExpression query, Entity row) => query.Criteria.Conditions.All(condition =>
    {
        object? actual = condition.AttributeName == row.LogicalName + "id"
            ? row.Id
            : row.Contains(condition.AttributeName) ? row[condition.AttributeName] : null;
        var actualId = actual is EntityReference reference ? reference.Id : actual;
        return condition.Operator switch
        {
            ConditionOperator.Null => actual is null,
            ConditionOperator.NotNull => actual is not null,
            ConditionOperator.Equal => Equals(actualId, condition.Values.Single()),
            ConditionOperator.In => condition.Values.Contains(actualId),
            _ => throw new NotSupportedException($"Test world: {condition.Operator} is not evaluated."),
        };
    });

    /// <summary>The POA table behind the share seam (entity SET names are mapped to the table, as Dataverse addresses one row).</summary>
    internal sealed class FakeShares(GateSharingWorld world) : IDataverseRecordShareService
    {
        /// <summary>Runs before a grant lands (a concurrent writer acting in between).</summary>
        public Func<string, Guid, DataversePrincipalRef, Task>? BeforeGrant { get; set; }
        public HashSet<Guid> FailGrantFor { get; } = [];
        public HashSet<string> FailReadFor { get; } = [];
        public bool FailRevoke { get; set; }
        public HashSet<Guid> FailRevokeFor { get; } = [];

        private static string Table(string name) => name.EndsWith('s') ? name[..^1] : name;

        public async Task GrantAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef principal, string accessRightsCsv, CancellationToken ct = default)
        {
            if (BeforeGrant is { } before)
                await before(Table(entitySetName), recordId, principal);
            if (FailGrantFor.Contains(principal.Id))
                throw new InvalidOperationException("Test: the grant failed.");
            accessRightsCsv.Should().Be("ReadAccess", "D-169: Read only");
            world.Calls.Add($"grant:{Table(entitySetName)}:{recordId:D}:{principal.Id:D}");
            world.SharesOf(Table(entitySetName), recordId)[principal] = 1;
        }

        public Task ModifyAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef principal, string accessRightsCsv, CancellationToken ct = default) =>
            throw new NotSupportedException("D-169 never modifies a share.");

        public Task RevokeAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef principal, CancellationToken ct = default) =>
            Revoke(entitySetName, recordId, principal);

        public Task RevokeAccessAsync(string entitySetName, Guid recordId, DataversePrincipalRef principal, DataversePrincipalRef recordOwner, CancellationToken ct = default) =>
            Revoke(entitySetName, recordId, principal);

        private Task Revoke(string entitySetName, Guid recordId, DataversePrincipalRef principal)
        {
            world.Calls.Add($"revoke:{Table(entitySetName)}:{recordId:D}:{principal.Id:D}");
            if (FailRevoke || FailRevokeFor.Contains(principal.Id))
                return Task.FromException(new InvalidOperationException("Test: the revoke failed."));
            world.SharesOf(Table(entitySetName), recordId).Remove(principal);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessAsync(string entityLogicalName, Guid recordId, CancellationToken ct = default) =>
            Task.FromResult(Read(entityLogicalName, recordId));

        public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessOrThrowAsync(string entityLogicalName, Guid recordId, CancellationToken ct = default) =>
            FailReadFor.Contains(entityLogicalName)
                ? Task.FromException<IReadOnlyList<DataversePrincipalAccess>>(new InvalidOperationException("Test: the share read failed."))
                : Task.FromResult(Read(entityLogicalName, recordId));

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<DataversePrincipalAccess>>> GetPrincipalAccessForRecordsOrThrowAsync(
            string entityLogicalName, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default) =>
            throw new NotSupportedException();

        private IReadOnlyList<DataversePrincipalAccess> Read(string table, Guid id) =>
            world.SharesOf(table, id).Select(s => new DataversePrincipalAccess(s.Key, s.Value, DateTimeOffset.UtcNow)).ToList();
    }
}

/// <summary>
/// Ontology task 042b (owner decision D-169, #1876; internal only, owner 2026-10-18): the gate Decision Record of an UNFILED communication is
/// readable by the communication's owner and its internal sharees (Read only), in either order of the record write and the communication share;
/// once an Office save files the communication the record is re-owned to the resolver's team, read back, and only then unshared.
/// </summary>
public sealed class GateDecisionRecordSharingTests
{
    private static readonly Guid CommunicationId = Guid.Parse("c0c0c0c0-0000-4000-8000-000000000001");
    private static readonly Guid RecordId = Guid.Parse("d0d0d0d0-0000-4000-8000-000000000001");
    private static readonly Guid Owner = Guid.Parse("a0000000-0000-4000-8000-0000000000a0");
    private static readonly Guid Reader = Guid.Parse("a0000000-0000-4000-8000-0000000000a1");
    private static readonly Guid Editor = Guid.Parse("a0000000-0000-4000-8000-0000000000a2");
    private static readonly Guid WriteOnly = Guid.Parse("a0000000-0000-4000-8000-0000000000a3");
    private static readonly Guid Bystander = Guid.Parse("a0000000-0000-4000-8000-0000000000a4");
    private static readonly Guid Late = Guid.Parse("a0000000-0000-4000-8000-0000000000a5");
    private static readonly Guid MatterTeam = Guid.Parse("e0000000-0000-4000-8000-0000000000e1");
    private static readonly Guid MatterId = Guid.Parse("f0000000-0000-4000-8000-0000000000f1");

    private const int Read = 1;
    private const int Write = 2;

    /// <summary>An unfiled user-owned communication shared with Reader (Read), Editor (Read + Write) and WriteOnly (no Read), and a team.</summary>
    private static GateSharingWorld UnfiledCommunication()
    {
        var world = new GateSharingWorld()
            .WithCommunication(CommunicationId, Owner)
            .WithUser(Owner).WithUser(Reader).WithUser(Editor).WithUser(WriteOnly).WithUser(Bystander).WithUser(Late)
            .Share(GateSharingWorld.Communication, CommunicationId, Reader, Read)
            .Share(GateSharingWorld.Communication, CommunicationId, Editor, Read | Write)
            .Share(GateSharingWorld.Communication, CommunicationId, WriteOnly, Write);
        world.SharesOf(GateSharingWorld.Communication, CommunicationId)[DataversePrincipalRef.Team(MatterTeam)] = Read;
        return world;
    }

    // ── AC 1: share at write, exactly the communication's Read sharees ──────────────────────────────────────────

    [Fact]
    public async Task AtWrite_TheRecordIsSharedReadWithEveryCommunicationShareeHoldingRead_AndNoOneElse_TheOwnerGetsNoShare()
    {
        var world = UnfiledCommunication().WithGateRecord(RecordId, CommunicationId, Owner);

        await world.Sharing.ShareAtWriteAsync(RecordId, CommunicationId, Owner);

        world.RecordSharees(RecordId).Should().Equal(new[] { Reader, Editor }.OrderBy(g => g),
            "the sharees holding Read (or more); WriteOnly has no Read, a team has no external flag to read, the Bystander is no sharee");
        world.SharesOf(GateSharingWorld.Record, RecordId).Values.Should().OnlyContain(mask => mask == Read, "Read only (D-169)");
        world.RecordSharees(RecordId).Should().NotContain(Owner, "the owner already owns it");
        world.SharesOf(GateSharingWorld.Record, RecordId).Keys.Should().NotContain(p => p.Kind == DataversePrincipalKind.Team);
    }

    // ── AC 2: both orders ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BothOrders_AGrantBeforeTheRecordIsPickedUpAtWrite_AGrantAfterItIsMirrored_SameResult()
    {
        // Share first, then the record is written: picked up at write.
        var shareFirst = UnfiledCommunication().WithGateRecord(RecordId, CommunicationId, Owner)
            .Share(GateSharingWorld.Communication, CommunicationId, Late);
        await shareFirst.Sharing.ShareAtWriteAsync(RecordId, CommunicationId, Owner);

        // The record first, then the communication is shared: the grant is mirrored.
        var recordFirst = UnfiledCommunication().WithGateRecord(RecordId, CommunicationId, Owner);
        await recordFirst.Sharing.ShareAtWriteAsync(RecordId, CommunicationId, Owner);
        recordFirst.RecordSharees(RecordId).Should().NotContain(Late);
        recordFirst.Share(GateSharingWorld.Communication, CommunicationId, Late);
        await recordFirst.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

        shareFirst.RecordSharees(RecordId).Should().Contain(Late);
        recordFirst.RecordSharees(RecordId).Should().Equal(shareFirst.RecordSharees(RecordId), "the order of the write and the share does not matter");
    }

    // ── AC 3: externals (must fire) ─────────────────────────────────────────────────────────────────────────────

    public enum Via { AtWrite, Mirror }

    [Theory]
    [InlineData(Via.AtWrite)]
    [InlineData(Via.Mirror)]
    public async Task AnExternalSharee_AGuest_OrOneWhoseFlagCannotBeRead_GetsNoShare_ABlankFlagIsInternal(Via path)
    {
        var external = Guid.NewGuid();
        var guest = Guid.NewGuid();
        var unreadable = Guid.NewGuid(); // no systemuser row
        var blank = Guid.NewGuid();
        var world = new GateSharingWorld()
            .WithCommunication(CommunicationId, Owner)
            .WithUser(external, external: true)
            .WithUser(guest, external: false, domainName: "partner_fabrikam.com#EXT#@contoso.onmicrosoft.com")
            .WithUser(blank, external: null)
            .WithGateRecord(RecordId, CommunicationId, Owner)
            .Share(GateSharingWorld.Communication, CommunicationId, external)
            .Share(GateSharingWorld.Communication, CommunicationId, guest)
            .Share(GateSharingWorld.Communication, CommunicationId, unreadable)
            .Share(GateSharingWorld.Communication, CommunicationId, blank);

        if (path == Via.AtWrite)
            await world.Sharing.ShareAtWriteAsync(RecordId, CommunicationId, Owner);
        else
            await world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

        world.RecordSharees(RecordId).Should().Equal(new[] { blank },
            "internal only: sprk_isexternal = true, a #EXT# guest and an unreadable flag are skipped; a blank flag is internal (owner round 67)");
    }

    [Fact]
    public async Task WhenTheShareesFlagsCannotBeRead_NoOneIsShared()
    {
        var world = UnfiledCommunication().WithGateRecord(RecordId, CommunicationId, Owner);
        world.UserReadFault = new InvalidOperationException("Test: the systemuser read failed.");

        await world.Sharing.ShareAtWriteAsync(RecordId, CommunicationId, Owner);

        world.RecordSharees(RecordId).Should().BeEmpty("a flag that cannot be read is never read as internal");
    }

    // ── AC 4: the mirror / filing race, and a filed communication ───────────────────────────────────────────────

    [Fact]
    public async Task Race_FilingReownsAndRevokesBetweenTheMirrorsReadAndItsGrant_EndsWithNoParticipantShareOnTheTeamOwnedRecord()
    {
        var world = UnfiledCommunication().WithGateRecord(RecordId, CommunicationId, Owner);
        var filed = false;
        world.Shares.BeforeGrant = (table, id, _) =>
        {
            if (table == GateSharingWorld.Record && !filed)
            {
                // The filing lands here: the mirror has read the record as user-owned; the filing re-owns it and revokes every share.
                filed = true;
                world.OwnByTeam(id, MatterTeam);
                world.SharesOf(GateSharingWorld.Record, id).Clear();
            }

            return Task.CompletedTask;
        };

        await world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

        filed.Should().BeTrue("the interleaving happened");
        world.Rows[(GateSharingWorld.Record, RecordId)].GetAttributeValue<EntityReference>("owningteam").Id.Should().Be(MatterTeam);
        world.RecordSharees(RecordId).Should().BeEmpty("the grants that landed after the re-own are revoked once the owner reads as a team");
    }

    [Fact]
    public async Task AGrantOnAFiledCommunicationWhoseRecordStayedUserOwned_AddsNoShare()
    {
        var world = new GateSharingWorld()
            .WithCommunication(CommunicationId, Owner, filedUnder: MatterId)
            .WithUser(Reader)
            .WithGateRecord(RecordId, CommunicationId, Owner)
            .Share(GateSharingWorld.Communication, CommunicationId, Reader);

        await world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

        world.RecordSharees(RecordId).Should().BeEmpty("a filed communication's record takes the team path; a failed re-own is not widened");
        world.Calls.Should().NotContain(c => c.StartsWith("grant:", StringComparison.Ordinal));
    }

    // ── AC 5: team-owned records and Secure-team-owned communications ──────────────────────────────────────────

    [Fact]
    public async Task ATeamOwnedRecord_GetsNoParticipantShare()
    {
        var world = UnfiledCommunication().WithGateRecord(RecordId, CommunicationId, Owner);
        world.OwnByTeam(RecordId, MatterTeam); // e.g. the Secure business unit's record went to the Secure team

        await world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

        world.RecordSharees(RecordId).Should().BeEmpty("D-123: a team-owned record is never shared");
        world.Calls.Should().NotContain(c => c.StartsWith("grant:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Via.AtWrite)]
    [InlineData(Via.Mirror)]
    public async Task ARecordWhoseCommunicationIsSecureTeamOwned_GetsNoParticipantShare(Via path)
    {
        var secureTeam = Guid.NewGuid();
        var world = new GateSharingWorld()
            .WithCommunication(CommunicationId, Owner, owningTeam: secureTeam)
            .WithUser(Reader)
            .WithGateRecord(RecordId, CommunicationId, Owner)
            .Share(GateSharingWorld.Communication, CommunicationId, Reader);
        // At write a team-owned communication is asked of the resolver; the Secure answer is the reconciler's, so nothing is assigned.
        world.OwnerAnswers(RecordOwnerResolution.Owned(secureTeam) with { IsSecureOwner = true });

        if (path == Via.AtWrite)
            await world.Sharing.ShareAtWriteAsync(RecordId, CommunicationId, Owner);
        else
            await world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

        world.RecordSharees(RecordId).Should().BeEmpty("D-123: a Secure-team-owned communication's sharees are the synchronizer's");
        world.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning, "the Secure answer is a defined outcome, not a failed re-own");
        world.Calls.Should().NotContain(c => c.StartsWith("assign:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnlyTheWritersOwnUnconfirmedRecords_AreMirrored()
    {
        var personsOwn = Guid.NewGuid();
        var review = Guid.NewGuid();
        var world = UnfiledCommunication()
            .WithGateRecord(RecordId, CommunicationId, Owner)
            .WithGateRecord(personsOwn, CommunicationId, Owner, createdBy: Bystander)
            .WithGateRecord(review, CommunicationId, Owner, confirmedBy: Bystander);

        await world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

        world.RecordSharees(RecordId).Should().NotBeEmpty();
        world.RecordSharees(personsOwn).Should().BeEmpty("a row a person created is never touched (task 153 F1)");
        world.RecordSharees(review).Should().BeEmpty("a human review always has a confirmer");
    }

    // ── AC 6: failures are logged, counted and contained ───────────────────────────────────────────────────────

    private static readonly AsyncLocal<Guid> MetricScope = new();

    private static (MeterListener Listener, List<string?> Reasons) ListenFailures(Guid scope)
    {
        var reasons = new List<string?>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OntologyWriterTelemetry.MeterName && instrument.Name == "ontology.writer.failures")
                    l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (MetricScope.Value != scope) return;
            foreach (var tag in tags)
                if (tag.Key == "reason") lock (reasons) reasons.Add(tag.Value as string);
        });
        listener.Start();
        return (listener, reasons);
    }

    [Fact]
    public async Task AFailedShareRead_IsLoggedAndCounted_NothingIsShared_AndNothingEscapes()
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenFailures(scope);
        using var _ = listener;
        var world = UnfiledCommunication().WithGateRecord(RecordId, CommunicationId, Owner);
        world.Shares.FailReadFor.Add(GateSharingWorld.Communication);

        var act = () => world.Sharing.ShareAtWriteAsync(RecordId, CommunicationId, Owner);

        await act.Should().NotThrowAsync();
        world.RecordSharees(RecordId).Should().BeEmpty("a failed read is not 'no shares' and never widens");
        world.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.EventId == DecisionRecordEvents.ParticipantShareFailed);
        reasons.Should().Contain(DecisionRecordRefusalReason.ParticipantShareFailed);
    }

    [Fact]
    public async Task AFailedGrant_IsLoggedAndCounted_TheOtherShareesAreStillShared_AndNoOneBeyondThem()
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenFailures(scope);
        using var _ = listener;
        var world = UnfiledCommunication().WithGateRecord(RecordId, CommunicationId, Owner);
        world.Shares.FailGrantFor.Add(Reader);

        var act = () => world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

        await act.Should().NotThrowAsync();
        world.RecordSharees(RecordId).Should().Equal(Editor);
        world.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.EventId == DecisionRecordEvents.ParticipantShareFailed);
        reasons.Should().Contain(DecisionRecordRefusalReason.ParticipantShareFailed);
    }

    // ── AC 7: filing into an ordinary record ────────────────────────────────────────────────────────────────────

    /// <summary>A filed communication whose gate record is still owned by its user and shared with Reader and Editor.</summary>
    private static GateSharingWorld FiledWithASharedRecord()
    {
        var world = new GateSharingWorld()
            .WithCommunication(CommunicationId, Owner, owningTeam: MatterTeam, filedUnder: MatterId)
            .WithGateRecord(RecordId, CommunicationId, Owner, stampMatter: MatterId)
            .Share(GateSharingWorld.Record, RecordId, Reader)
            .Share(GateSharingWorld.Record, RecordId, Editor);
        world.OwnerAnswers(RecordOwnerResolution.Owned(MatterTeam));
        return world;
    }

    [Fact]
    public async Task Filing_ReownsToTheResolversTeam_ReadsItBack_ThenRevokesEveryShare_InThatOrder()
    {
        var world = FiledWithASharedRecord();
        RecordOwnershipContext? asked = null;
        world.Ownership.Setup(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .Callback<RecordOwnershipContext, CancellationToken>((c, _) => asked = c)
            .ReturnsAsync(RecordOwnerResolution.Owned(MatterTeam));

        await world.Sharing.ReownAfterFilingAsync(CommunicationId);

        world.Rows[(GateSharingWorld.Record, RecordId)].GetAttributeValue<EntityReference>("owningteam").Id.Should().Be(MatterTeam);
        world.RecordSharees(RecordId).Should().BeEmpty("every share on the record is revoked once the new owner is confirmed");
        var assign = world.Calls.FindIndex(c => c.StartsWith("assign:", StringComparison.Ordinal));
        var readBack = world.Calls.FindIndex(assign, c => c.StartsWith("read-owner:", StringComparison.Ordinal));
        var firstRevoke = world.Calls.FindIndex(c => c.StartsWith("revoke:", StringComparison.Ordinal));
        assign.Should().BeGreaterThanOrEqualTo(0);
        readBack.Should().BeGreaterThan(assign);
        firstRevoke.Should().BeGreaterThan(readBack, "revoke only after the read-back confirms the new owner");
        world.Calls.Should().Contain($"assign:{RecordId:D}:{MatterTeam:D}");
        world.Cache.Verify(c => c.InvalidateRecordOwnerChangeAsync("sprk_decisionrecord", "sprk_decisionrecords", RecordId, It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once, "every owner-changing writer evicts (task 132)");

        // The gate writer's own question: the communication first, as the content of it, plus the record's live stamp.
        asked!.KeepCreatorUnlessTargetIsTeamOwned.Should().BeTrue();
        asked.Parents.Should().Equal(
            new RecordOwnershipParent(GateSharingWorld.Communication, CommunicationId), new RecordOwnershipParent("sprk_matter", MatterId));
    }

    [Fact]
    public async Task Filing_AFailedReown_KeepsTheSharesAndTheUserOwner_LogsAndCounts_AndNothingEscapes()
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenFailures(scope);
        using var _ = listener;
        var world = FiledWithASharedRecord();
        world.AssignLands = false;
        world.AssignFault = new InvalidOperationException("Test: Dataverse refused the Assign.");

        var act = () => world.Sharing.ReownAfterFilingAsync(CommunicationId);

        await act.Should().NotThrowAsync();
        world.Rows[(GateSharingWorld.Record, RecordId)].GetAttributeValue<EntityReference>("owninguser").Id.Should().Be(Owner, "never orphaned");
        world.RecordSharees(RecordId).Should().Equal(new[] { Reader, Editor }.OrderBy(g => g), "the over-share stays bounded to the participants");
        world.Calls.Should().NotContain(c => c.StartsWith("revoke:", StringComparison.Ordinal));
        world.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.EventId == DecisionRecordEvents.FilingReownFailed);
        reasons.Should().Contain(DecisionRecordRefusalReason.FilingReownFailed);
    }

    [Fact]
    public async Task Filing_AnAssignThatLandedButCannotBeReadBack_KeepsTheShares()
    {
        var world = FiledWithASharedRecord();
        world.OwnerReadFault = new InvalidOperationException("Test: the owner read failed.");

        await world.Sharing.ReownAfterFilingAsync(CommunicationId);

        world.Calls.Should().Contain(c => c.StartsWith("assign:", StringComparison.Ordinal));
        world.RecordSharees(RecordId).Should().Equal(new[] { Reader, Editor }.OrderBy(g => g),
            "no revoke without a read-back that names the new owner");
        world.Calls.Should().NotContain(c => c.StartsWith("revoke:", StringComparison.Ordinal));
    }

    // ── AC 8: filing into a Secure record is SecureChildReconciler's ───────────────────────────────────────────

    [Fact]
    public async Task Filing_IntoASecureRecord_AssignsNothing_AndRevokesNothing()
    {
        var world = FiledWithASharedRecord();
        world.OwnerAnswers(RecordOwnerResolution.Owned(Guid.NewGuid()) with { IsSecureOwner = true });

        await world.Sharing.ReownAfterFilingAsync(CommunicationId);

        world.Calls.Should().NotContain(c => c.StartsWith("assign:", StringComparison.Ordinal) || c.StartsWith("revoke:", StringComparison.Ordinal),
            "the reconciler re-owns a record that moved under a secure record, and its mirror replaces the shares");
        world.Org.Verify(o => o.ExecuteAsync(It.IsAny<AssignRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        world.RecordSharees(RecordId).Should().Equal(new[] { Reader, Editor }.OrderBy(g => g));
    }
    // ── 042b round 2 (uac-r2): the mirror needs the COMMUNICATION unfiled; the re-own is idempotent ─────────────

    [Fact]
    public async Task AGrantOnACommunicationWhoseFilingCannotBeRead_AddsNoShare_FailClosed()
    {
        var world = new GateSharingWorld()
            .WithCommunication(CommunicationId, Owner)
            .WithUser(Reader)
            .WithGateRecord(RecordId, CommunicationId, Owner)
            .Share(GateSharingWorld.Communication, CommunicationId, Reader);
        world.CommunicationReadFault = new InvalidOperationException("Test: the communication could not be read.");

        var act = () => world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

        await act.Should().NotThrowAsync();
        world.RecordSharees(RecordId).Should().BeEmpty("a communication whose filing state cannot be read is not known to be unfiled");
        world.Calls.Should().NotContain(c => c.StartsWith("grant:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Filing_ASecondCall_OnARecordAlreadyTeamOwned_DoesNothing()
    {
        var world = FiledWithASharedRecord();
        await world.Sharing.ReownAfterFilingAsync(CommunicationId);
        var afterFirst = world.Calls.Count;

        await world.Sharing.ReownAfterFilingAsync(CommunicationId);

        world.Calls.Skip(afterFirst).Should().BeEmpty("a team-owned record is not found again: no Assign, read-back or revoke");
        world.Org.Verify(o => o.ExecuteAsync(It.IsAny<AssignRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        world.Ownership.Verify(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()), Times.Once);
        world.Rows[(GateSharingWorld.Record, RecordId)].GetAttributeValue<EntityReference>("owningteam").Id.Should().Be(MatterTeam);
    }

    // ── 042b round 3 F1-a: "filed" is the ownership resolver's test (ownership-parent lookups only) ──────────────

    [Theory]
    [InlineData("sprk_regardingperson", "contact", Via.AtWrite)]
    [InlineData("sprk_regardingperson", "contact", Via.Mirror)]
    [InlineData("sprk_regardingaccount", "account", Via.AtWrite)]
    [InlineData("sprk_regardingaccount", "account", Via.Mirror)]
    [InlineData("sprk_regardingorganization", "sprk_organization", Via.AtWrite)]
    [InlineData("sprk_regardingorganization", "sprk_organization", Via.Mirror)]
    public async Task ACommunicationRegardingOnlyAContactAnAccountOrAnOrganization_IsUnfiled_ItsRecordGetsTheParticipantShares(
        string column, string table, Via path)
    {
        var world = UnfiledCommunication().WithGateRecord(RecordId, CommunicationId, Owner);
        var communication = world.Rows[(GateSharingWorld.Communication, CommunicationId)];
        communication[column] = new EntityReference(table, Guid.NewGuid());
        communication["sprk_regardingrecordid"] = Guid.NewGuid().ToString("D"); // the ADR-024 pair is set too
        communication["sprk_regardingrecordtype"] = new EntityReference("sprk_recordtype_ref", Guid.NewGuid());

        if (path == Via.AtWrite)
            await world.Sharing.ShareAtWriteAsync(RecordId, CommunicationId, Owner);
        else
            await world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

        world.RecordSharees(RecordId).Should().Equal(new[] { Reader, Editor }.OrderBy(g => g),
            "the resolver answers Unchanged for a communication filed under no ownership parent, so D-169's shares are the only readers");
        world.Calls.Should().NotContain(c => c.StartsWith("assign:", StringComparison.Ordinal));
    }

    // ── 042b round 3 F1-b: a filing that lands between the writer's owner decision and its create ───────────────

    [Fact]
    public async Task AtWrite_ACommunicationAlreadyFiled_ReownsTheRecordToTheResolversTeam_InsteadOfLeavingItUserOwned()
    {
        var world = new GateSharingWorld()
            .WithCommunication(CommunicationId, Owner, filedUnder: MatterId)
            .WithUser(Reader)
            .WithGateRecord(RecordId, CommunicationId, Owner, stampMatter: MatterId)
            .Share(GateSharingWorld.Communication, CommunicationId, Reader);
        world.OwnerAnswers(RecordOwnerResolution.Owned(MatterTeam));

        await world.Sharing.ShareAtWriteAsync(RecordId, CommunicationId, Owner);

        world.Rows[(GateSharingWorld.Record, RecordId)].GetAttributeValue<EntityReference>("owningteam").Id.Should().Be(MatterTeam);
        world.Calls.Should().NotContain(c => c.StartsWith("grant:", StringComparison.Ordinal));
        world.RecordSharees(RecordId).Should().BeEmpty();
    }

    // ── 042c: a communication filed by a thread join is team-owned but carries no ownership-parent lookup ──────────

    /// <summary>The communication a thread join leaves: a team owner and <c>sprk_communicationthread</c>, none of its own parent lookups.</summary>
    private static GateSharingWorld ThreadFiledCommunication()
    {
        var world = new GateSharingWorld()
            .WithCommunication(CommunicationId, Owner, owningTeam: MatterTeam)
            .WithUser(Reader)
            .WithGateRecord(RecordId, CommunicationId, Owner)
            .Share(GateSharingWorld.Communication, CommunicationId, Reader);
        world.Rows[(GateSharingWorld.Communication, CommunicationId)]["sprk_communicationthread"] =
            new EntityReference("sprk_communicationthread", Guid.NewGuid());
        return world;
    }

    [Fact]
    public async Task AtWrite_ACommunicationFiledByAThreadJoin_ReownsTheRecordToTheResolversTeam_WithNoParticipantShare()
    {
        var world = ThreadFiledCommunication();
        world.OwnerAnswers(RecordOwnerResolution.Owned(MatterTeam));

        await world.Sharing.ShareAtWriteAsync(RecordId, CommunicationId, Owner);

        world.Rows[(GateSharingWorld.Record, RecordId)].GetAttributeValue<EntityReference>("owningteam")!.Id.Should().Be(MatterTeam,
            "a join owns the communication to the matter's team without stamping its parents; its record must follow, not stay user-owned");
        world.Rows[(GateSharingWorld.Record, RecordId)].Contains("owningteam").Should().BeTrue("the record must have been re-owned to a team");
        world.Calls.Should().NotContain(c => c.StartsWith("grant:", StringComparison.Ordinal));
        world.RecordSharees(RecordId).Should().BeEmpty();
    }

    [Fact]
    public async Task AtWrite_ACommunicationFiledByAThreadJoin_UnderASecureTeam_LeavesTheRecordToTheReconciler()
    {
        var world = ThreadFiledCommunication();
        world.OwnerAnswers(RecordOwnerResolution.Owned(MatterTeam) with { IsSecureOwner = true });

        await world.Sharing.ShareAtWriteAsync(RecordId, CommunicationId, Owner);

        // The Secure answer is left to SecureChildReconciler, whose recent-changes pass picks up sprk_decisionrecord (SecureChildLineage);
        // nothing is re-owned here, so the record stays user-owned until that pass runs.
        world.Ownership.Verify(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()), Times.Once);
        world.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning, "a Secure answer is a defined outcome, not a failed re-own");
        world.Calls.Should().NotContain(c => c.StartsWith("assign:", StringComparison.Ordinal), "a Secure answer is the SecureChildReconciler's");
        world.Rows[(GateSharingWorld.Record, RecordId)].GetAttributeValue<EntityReference>("owninguser")!.Id.Should().Be(Owner);
        world.Calls.Should().NotContain(c => c.StartsWith("grant:", StringComparison.Ordinal));
        world.RecordSharees(RecordId).Should().BeEmpty();
    }

    [Fact]
    public async Task AtWrite_ACommunicationFiledByAThreadJoin_WhenTheResolverFaults_FailsClosedWithOneFilingReownWarning()
    {
        var world = ThreadFiledCommunication();
        var fault = new InvalidOperationException("Test: the ownership resolver faulted.");
        world.Ownership.Setup(r => r.ResolveOwnerAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>())).ThrowsAsync(fault);

        await world.Sharing.ShareAtWriteAsync(RecordId, CommunicationId, Owner);

        // Fail-closed: a team-owned communication returns before any participant grant, so a resolver fault leaves the record user-owned
        // with no share (never shared to the communication's sharees instead), and the fault is logged once (uac-r2 #2113 review F3).
        world.Calls.Should().NotContain(c => c.StartsWith("assign:", StringComparison.Ordinal));
        world.Calls.Should().NotContain(c => c.StartsWith("grant:", StringComparison.Ordinal));
        world.Rows[(GateSharingWorld.Record, RecordId)].GetAttributeValue<EntityReference>("owninguser")!.Id.Should().Be(Owner);
        world.RecordSharees(RecordId).Should().BeEmpty();
        world.Log.Entries.Where(e => e.Level >= LogLevel.Warning).Should().ContainSingle("the resolver's fault is logged once")
            .Which.Should().Match<LogEntry>(e => e.EventId == DecisionRecordEvents.FilingReownFailed && ReferenceEquals(e.Exception, fault));
    }

    // ── 042b round 3 K2: a share a failed revoke left on the team-owned record goes on the next filing ──────────

    [Fact]
    public async Task Filing_ARevokeThatFailed_IsRetriedOnTheNextFiling_AndTheLeftoverSharesGo()
    {
        var world = FiledWithASharedRecord();
        world.Shares.FailRevoke = true;
        await world.Sharing.ReownAfterFilingAsync(CommunicationId);
        world.Rows[(GateSharingWorld.Record, RecordId)].GetAttributeValue<EntityReference>("owningteam").Id.Should().Be(MatterTeam);
        world.RecordSharees(RecordId).Should().Equal(new[] { Reader, Editor }.OrderBy(g => g), "the first refile's revokes failed");

        world.Shares.FailRevoke = false;
        await world.Sharing.ReownAfterFilingAsync(CommunicationId);

        world.RecordSharees(RecordId).Should().BeEmpty("the next refile removes the leftover participant shares");
        world.Calls.Count(c => c.StartsWith("assign:", StringComparison.Ordinal)).Should().Be(1, "the record is not re-owned twice");
    }

    [Fact]
    public async Task Filing_ASecureTeamOwnedRecordWithUserShares_IsNeverTouched_NorOneOwnedByAnotherTeam()
    {
        var secureTeam = Guid.NewGuid();
        var secure = FiledWithASharedRecord();
        secure.OwnByTeam(RecordId, secureTeam);
        secure.OwnerAnswers(RecordOwnerResolution.Owned(secureTeam) with { IsSecureOwner = true });
        var other = FiledWithASharedRecord();
        other.OwnByTeam(RecordId, Guid.NewGuid()); // the resolver still answers MatterTeam

        await secure.Sharing.ReownAfterFilingAsync(CommunicationId);
        await other.Sharing.ReownAfterFilingAsync(CommunicationId);

        foreach (var world in new[] { secure, other })
        {
            world.Calls.Should().NotContain(c => c.StartsWith("revoke:", StringComparison.Ordinal) || c.StartsWith("assign:", StringComparison.Ordinal),
                "a Secure-team row's shares are the Secure mirror's; another team's row is not this filing's");
            world.RecordSharees(RecordId).Should().Equal(new[] { Reader, Editor }.OrderBy(g => g));
        }
    }

    // ── 042b round 3 K4: telemetry ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mirror_TheCallersOwnCancellation_IsNotCountedAsAFailure_AnyOtherFaultIs(bool callerCancelled)
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenFailures(scope);
        using var _ = listener;
        using var cts = new CancellationTokenSource();
        if (callerCancelled)
            cts.Cancel();
        var world = UnfiledCommunication().WithGateRecord(RecordId, CommunicationId, Owner);
        world.Entities.Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == GateSharingWorld.Record), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("Test: the read was cancelled."));

        var act = () => world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, cts.Token);

        await act.Should().NotThrowAsync();
        if (callerCancelled)
            reasons.Should().NotContain(DecisionRecordRefusalReason.ParticipantShareFailed);
        else
            reasons.Should().Contain(DecisionRecordRefusalReason.ParticipantShareFailed, "a cancellation the caller did not ask for is a fault");
    }

    [Fact]
    public async Task AnOwnerThatCannotBeReadAfterTheGrants_IsCounted_AndTheGrantsAreRevoked()
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var (listener, reasons) = ListenFailures(scope);
        using var _ = listener;
        var world = UnfiledCommunication().WithGateRecord(RecordId, CommunicationId, Owner);
        world.OwnerReadFault = new InvalidOperationException("Test: the owner read failed.");

        await world.Sharing.ShareAtWriteAsync(RecordId, CommunicationId, Owner);

        reasons.Should().Contain(DecisionRecordRefusalReason.ParticipantShareFailed);
        world.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.EventId == DecisionRecordEvents.ParticipantShareFailed);
        world.RecordSharees(RecordId).Should().BeEmpty("fail closed: the grants are revoked");
    }

    // ── 042b round 4: an unreadable owner is ONE warning carrying its fault; the leftover log counts what was revoked ─────

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnOwnerThatCannotBeRead_IsLoggedOnce_AsTheFailureWarning_CarryingTheReadFault_042bRound4(bool atWrite)
    {
        var fault = new InvalidOperationException("Test: the owner read failed.");
        GateSharingWorld world;
        if (atWrite)
        {
            world = UnfiledCommunication().WithGateRecord(RecordId, CommunicationId, Owner);
            world.OwnerReadFault = fault;
            await world.Sharing.ShareAtWriteAsync(RecordId, CommunicationId, Owner);
        }
        else
        {
            // The filing's re-own: the Assign lands and the read-back throws.
            world = FiledWithASharedRecord();
            world.OwnerReadFault = fault;
            await world.Sharing.ReownAfterFilingAsync(CommunicationId);
        }

        var expected = atWrite ? DecisionRecordEvents.ParticipantShareFailed : DecisionRecordEvents.FilingReownFailed;
        world.Log.Entries.Where(e => e.Level >= LogLevel.Warning).Should().ContainSingle("the read's fault is logged once, by the failure warning")
            .Which.Should().Match<LogEntry>(e => e.EventId == expected && ReferenceEquals(e.Exception, fault));
    }

    [Fact]
    public async Task Filing_ALeftoverRevokeThatFailsAgain_IsNotCountedAsRevoked_042bRound4()
    {
        var world = FiledWithASharedRecord();
        world.Shares.FailRevoke = true;
        await world.Sharing.ReownAfterFilingAsync(CommunicationId);
        world.Shares.FailRevoke = false;
        world.Shares.FailRevokeFor.Add(Reader);
        world.Log.Entries.Clear();

        await world.Sharing.ReownAfterFilingAsync(CommunicationId);

        world.RecordSharees(RecordId).Should().Equal(Reader);
        world.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Information && e.Message.Contains("leftover", StringComparison.Ordinal))
            .Which.Fields["Count"].Should().Be(1, "one of the two leftover shares was revoked; the other's revoke failed");
    }
}
