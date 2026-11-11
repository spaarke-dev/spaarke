using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Signals;

/// <summary>
/// Ontology ruling D-215 (uac-r2 task 198 Part E; amends D-169 for Direct 1:1 messages only): the gate Decision Records of a message the
/// Direct-message owner team owns follow the message's participants. Identified by the ONE Direct check (ids only; unknown writes nothing);
/// the audience is the stamped pair that reads the message NOW, internal only; while unfiled the record is owned by the Direct team; filing
/// re-owns it but keeps the recomputed participants' Read (a Secure target is unchanged); an unfile back to Direct re-owns, evicts, reads
/// back, and only then grants and revokes. Every other owner (a business unit's default team, the Secure team, a user) keeps D-169 exactly.
/// </summary>
public sealed class GateDecisionRecordSharingDirectTests
{
    private static readonly Guid CommunicationId = Guid.Parse("c0c0c0c0-0000-4000-8000-0000000000d1");
    private static readonly Guid RecordId = Guid.Parse("d0d0d0d0-0000-4000-8000-0000000000d1");
    private static readonly Guid Sender = Guid.Parse("b0000000-0000-4000-8000-0000000000b1");
    private static readonly Guid Recipient = Guid.Parse("b0000000-0000-4000-8000-0000000000b2");
    private static readonly Guid Third = Guid.Parse("b0000000-0000-4000-8000-0000000000b3");
    private static readonly Guid MatterTeam = Guid.Parse("e0000000-0000-4000-8000-0000000000e2");
    private static readonly Guid MatterId = Guid.Parse("f0000000-0000-4000-8000-0000000000f2");
    private static readonly Guid DirectTeam = GateSharingWorld.DirectTeam;

    private const int Read = 1;
    private const int Write = 2;

    public enum Via { AtWrite, Mirror }

    /// <summary>
    /// An unfiled Direct message (owned by the Direct team, the pair Sender → Recipient stamped) that Sender, Recipient and Third (a
    /// non-participant, e.g. a reviewer) read, and its gate record owned by the Direct team with no share yet. The matter's team is a
    /// business unit's default team.
    /// </summary>
    private static GateSharingWorld UnfiledDirectMessage(bool directLookup = true)
    {
        var world = new GateSharingWorld(directLookup)
            .WithTeam(DirectTeam, DirectMessageOwnerTeamDouble.OwnerTeamName, isDefault: false)
            .WithTeam(MatterTeam, "Matters", isDefault: true)
            .WithCommunication(CommunicationId, Sender, owningTeam: DirectTeam)
            .WithPair(CommunicationId, Sender, Recipient)
            .WithUser(Sender).WithUser(Recipient).WithUser(Third)
            .Share(GateSharingWorld.Communication, CommunicationId, Sender)
            .Share(GateSharingWorld.Communication, CommunicationId, Recipient)
            .Share(GateSharingWorld.Communication, CommunicationId, Third)
            .WithGateRecord(RecordId, CommunicationId, Sender);
        world.OwnByTeam(RecordId, DirectTeam);
        return world;
    }

    /// <summary>The same message, now filed under a matter (owned by the matter's default team), its record still the Direct team's and
    /// shared with both participants and Third. The resolver answers the matter's team.</summary>
    private static GateSharingWorld FiledDirectMessage()
    {
        var world = UnfiledDirectMessage()
            .WithCommunication(CommunicationId, Sender, owningTeam: MatterTeam, filedUnder: MatterId)
            .WithPair(CommunicationId, Sender, Recipient)
            .Share(GateSharingWorld.Record, RecordId, Sender)
            .Share(GateSharingWorld.Record, RecordId, Recipient)
            .Share(GateSharingWorld.Record, RecordId, Third);
        world.OwnerAnswers(RecordOwnerResolution.Owned(MatterTeam));
        return world;
    }

    /// <summary>An unfile back to Direct: the re-file left the message Direct-team-owned and unfiled; its record is still owned by the
    /// matter's team and shared with Sender and Third (Recipient's share was revoked when it was filed).</summary>
    private static GateSharingWorld UnfiledBackToDirect()
    {
        var world = UnfiledDirectMessage()
            .Share(GateSharingWorld.Record, RecordId, Sender)
            .Share(GateSharingWorld.Record, RecordId, Third);
        world.OwnByTeam(RecordId, MatterTeam);
        return world;
    }

    private static Task Run(GateSharingWorld world, Via path) => path == Via.AtWrite
        ? world.Sharing.ShareTeamOwnedAtWriteAsync(RecordId, CommunicationId, DirectTeam)
        : world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

    private static Guid OwnerTeamOf(GateSharingWorld world) =>
        world.Rows[(GateSharingWorld.Record, RecordId)].GetAttributeValue<EntityReference>("owningteam")!.Id;

    private static IEnumerable<string> Writes(GateSharingWorld world) => world.Calls.Where(c =>
        c.StartsWith("assign:", StringComparison.Ordinal) || c.StartsWith("grant:", StringComparison.Ordinal)
        || c.StartsWith("revoke:", StringComparison.Ordinal));

    private static string Grant(Guid user) => $"grant:{GateSharingWorld.Record}:{RecordId:D}:{user:D}";

    private static string Revoke(Guid user) => $"revoke:{GateSharingWorld.Record}:{RecordId:D}:{user:D}";

    // ── Must fire: rule 2 (the audience) and rule 3 (while unfiled) ─────────────────────────────────────────────

    [Theory]
    [InlineData(Via.AtWrite)]
    [InlineData(Via.Mirror)]
    public async Task UnfiledDirect_BothInternalParticipantsWhoReadTheMessage_GetRead_AndNoOneElse(Via path)
    {
        var world = UnfiledDirectMessage();

        await Run(world, path);

        world.RecordSharees(RecordId).Should().Equal(new[] { Sender, Recipient }.OrderBy(g => g),
            "D-215 rule 2: the pair that reads the message; Third reads the message but is no participant");
        world.SharesOf(GateSharingWorld.Record, RecordId).Values.Should().OnlyContain(mask => mask == Read, "Read only");
        world.Calls.Should().NotContain(c => c.StartsWith("assign:", StringComparison.Ordinal), "the Direct team already owns it");
        OwnerTeamOf(world).Should().Be(DirectTeam);
        world.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Theory]
    [InlineData(Via.AtWrite)]
    [InlineData(Via.Mirror)]
    public async Task AParticipantWhoDoesNotReadTheMessage_IsNotGranted(Via path)
    {
        var world = UnfiledDirectMessage();
        world.SharesOf(GateSharingWorld.Communication, CommunicationId)[DataversePrincipalRef.User(Recipient)] = Write; // no Read bit

        await Run(world, path);

        world.RecordSharees(RecordId).Should().Equal(new[] { Sender }, "never more visible than the message");
    }

    [Theory]
    [InlineData(Via.AtWrite)]
    [InlineData(Via.Mirror)]
    public async Task AnExternalParticipant_IsFilteredOut(Via path)
    {
        var world = UnfiledDirectMessage().WithUser(Recipient, external: true);

        await Run(world, path);

        world.RecordSharees(RecordId).Should().Equal(new[] { Sender }, "internal only");
    }

    [Fact]
    public async Task AtWrite_ARecordTheWriterGaveAnotherTeam_IsReownedToTheDirectTeam_ReadBack_ThenGranted()
    {
        // Before fm piece 3 the resolver answers a Direct message's content with its business unit's default team.
        var world = UnfiledDirectMessage();
        world.OwnByTeam(RecordId, MatterTeam);

        await world.Sharing.ShareTeamOwnedAtWriteAsync(RecordId, CommunicationId, MatterTeam);

        OwnerTeamOf(world).Should().Be(DirectTeam);
        world.RecordSharees(RecordId).Should().Equal(new[] { Sender, Recipient }.OrderBy(g => g));
        var assign = world.Calls.IndexOf($"assign:{RecordId:D}:{DirectTeam:D}");
        var readBack = world.Calls.FindIndex(Math.Max(assign, 0), c => c.StartsWith("read-owner:", StringComparison.Ordinal));
        var firstGrant = world.Calls.FindIndex(c => c.StartsWith("grant:", StringComparison.Ordinal));
        assign.Should().BeGreaterThanOrEqualTo(0);
        readBack.Should().BeGreaterThan(assign);
        firstGrant.Should().BeGreaterThan(readBack, "grant only after the read-back names the Direct team");
        world.Cache.Verify(c => c.InvalidateRecordOwnerChangeAsync("sprk_decisionrecord", "sprk_decisionrecords", RecordId, It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Mirror_ARecordReownedToAnOrdinaryTeamBetweenItsGrantsAndTheReadBack_HasTheGrantsRevoked()
    {
        var world = UnfiledDirectMessage();
        var moved = false;
        world.Shares.BeforeGrant = (table, id, _) =>
        {
            if (table == GateSharingWorld.Record && !moved)
            {
                moved = true;
                world.OwnByTeam(id, MatterTeam); // a filing landed in between
            }

            return Task.CompletedTask;
        };

        await world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

        moved.Should().BeTrue();
        world.RecordSharees(RecordId).Should().BeEmpty("the read-back accepts exactly the Direct team; any other team revokes the pass");
    }

    // ── 198 pass 2 F3: a rejected read-back revokes only THIS call's grants ─────────────────────────────────────

    [Fact(DisplayName = "198 pass 2 F3 (a): shared at write with the pair, then a join-grant mirror whose owner read-back faults; the participants keep their Read")]
    public async Task F3_SharedAtWrite_ThenAMirrorWhoseOwnerReadBackFaults_TheParticipantsKeepTheirRead()
    {
        var world = UnfiledDirectMessage();
        await Run(world, Via.AtWrite);
        world.RecordSharees(RecordId).Should().Equal(new[] { Sender, Recipient }.OrderBy(g => g));
        world.OwnerReadFault = new InvalidOperationException("Test: the owner read-back faulted.");

        await world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

        world.RecordSharees(RecordId).Should().Equal(new[] { Sender, Recipient }.OrderBy(g => g),
            "the Read they held before this call is not this call's to revoke");
        world.Calls.Should().NotContain(c => c.StartsWith("revoke:", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "198 pass 2 F3 (b): a filing keeps the pair; a concurrent mirror's read-back then sees the matter team; the mirror does not revoke the pair")]
    public async Task F3_AFilingKeepsThePair_ThenAConcurrentMirrorSeesTheMatterTeam_ThePairIsNotRevoked()
    {
        var world = UnfiledDirectMessage();
        var filed = false;
        // The mirror has read the message (unfiled, Direct) and its record (Direct-owned); then the filing's ReownAsync(keepAudience: true)
        // lands: the record is re-owned to the matter's team with the pair's Read kept.
        world.Shares.BeforeStrictRead = (table, id) =>
        {
            if (table == GateSharingWorld.Communication && !filed)
            {
                filed = true;
                world.OwnByTeam(RecordId, MatterTeam);
                world.Share(GateSharingWorld.Record, RecordId, Sender).Share(GateSharingWorld.Record, RecordId, Recipient);
            }
        };

        await world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

        filed.Should().BeTrue();
        OwnerTeamOf(world).Should().Be(MatterTeam);
        world.RecordSharees(RecordId).Should().Equal(new[] { Sender, Recipient }.OrderBy(g => g), "D-215 rule 4: the filing kept them");
        world.Calls.Should().NotContain(c => c.StartsWith("revoke:", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "198 pass 2 F3: a pre-existing participant share is kept and the new grant is rolled back when the read-back names another team")]
    public async Task F3_Mirror_APriorShareIsKept_AndOnlyTheNewGrantIsRevoked_WhenReownedBetweenGrantAndReadBack()
    {
        // The sibling of Mirror_ARecordReownedToAnOrdinaryTeamBetweenItsGrantsAndTheReadBack_HasTheGrantsRevoked, with Sender's share seeded.
        var world = UnfiledDirectMessage().Share(GateSharingWorld.Record, RecordId, Sender);
        var moved = false;
        world.Shares.BeforeGrant = (table, id, _) =>
        {
            if (table == GateSharingWorld.Record && !moved)
            {
                moved = true;
                world.OwnByTeam(id, MatterTeam);
            }

            return Task.CompletedTask;
        };

        await world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

        moved.Should().BeTrue();
        world.RecordSharees(RecordId).Should().Equal(new[] { Sender }, "the share Sender held before is kept; Recipient's new grant is rolled back");
        world.Calls.Should().Contain(Revoke(Recipient)).And.NotContain(Revoke(Sender)).And.NotContain(Grant(Sender));
    }

    [Fact(DisplayName = "198 pass 2 F3 (D-169 mirror): a user-owned record's prior sharee keeps Read when a filing re-owns it between the grants and the read-back")]
    public async Task F3_D169_APriorSharee_IsKept_WhenTheRecordIsReownedBetweenTheGrantAndTheReadBack()
    {
        var world = new GateSharingWorld()
            .WithCommunication(CommunicationId, Sender)
            .WithUser(Sender).WithUser(Recipient).WithUser(Third)
            .Share(GateSharingWorld.Communication, CommunicationId, Recipient)
            .Share(GateSharingWorld.Communication, CommunicationId, Third)
            .WithGateRecord(RecordId, CommunicationId, Sender)
            .Share(GateSharingWorld.Record, RecordId, Recipient);
        var moved = false;
        world.Shares.BeforeGrant = (table, id, _) =>
        {
            if (table == GateSharingWorld.Record && !moved)
            {
                moved = true;
                world.OwnByTeam(id, MatterTeam);
            }

            return Task.CompletedTask;
        };

        await world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

        moved.Should().BeTrue();
        world.RecordSharees(RecordId).Should().Equal(new[] { Recipient }, "only this call's grant (Third) is rolled back");
        world.Calls.Should().Contain(Revoke(Third)).And.NotContain(Revoke(Recipient));
    }

    // ── 198 pass 2 N1/N4: at write, a message filed while its record was being written ──────────────────────────

    /// <summary>The race: the writer decided the owner (the Direct team) while the message was unfiled; the message was filed (owned by the
    /// matter's team) before the record landed, and that filing's gate step found no record yet. The record has no share.</summary>
    private static GateSharingWorld FiledWhileTheRecordWasWritten()
    {
        var world = FiledDirectMessage();
        world.SharesOf(GateSharingWorld.Record, RecordId).Clear();
        return world;
    }

    [Fact(DisplayName = "198 pass 2 N1 must-fire: at write, a Direct-owned record of a message filed meanwhile is re-owned as a filing would (keepAudience)")]
    public async Task N1_AtWrite_ADirectRecordOfAMessageFiledMeanwhile_IsReownedAsAFilingWould()
    {
        var world = FiledWhileTheRecordWasWritten();

        await world.Sharing.ShareTeamOwnedAtWriteAsync(RecordId, CommunicationId, DirectTeam);

        OwnerTeamOf(world).Should().Be(MatterTeam, "never left with the memberless Direct team, readable by nobody");
        world.Calls.Should().Contain($"assign:{RecordId:D}:{MatterTeam:D}");
        world.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact(DisplayName = "198 pass 2 N1 must-not-fire: at write, a filed message's record owned by another team is left as it is")]
    public async Task N1_AtWrite_AFiledMessagesRecordOwnedByAnotherTeam_WritesNothing()
    {
        var world = FiledWhileTheRecordWasWritten();
        world.OwnByTeam(RecordId, MatterTeam);

        await world.Sharing.ShareTeamOwnedAtWriteAsync(RecordId, CommunicationId, MatterTeam);

        Writes(world).Should().BeEmpty();
        OwnerTeamOf(world).Should().Be(MatterTeam);
    }

    [Fact(DisplayName = "198 pass 2 N1 fail closed: at write, a filed message's record whose owner cannot be identified as Direct writes nothing, logged")]
    public async Task N1_AtWrite_AFiledMessagesRecord_WhenTheDirectCheckIsUnknown_WritesNothing_AndIsLogged()
    {
        var world = FiledWhileTheRecordWasWritten();
        world.Directory.Faults = true;

        await world.Sharing.ShareTeamOwnedAtWriteAsync(RecordId, CommunicationId, DirectTeam);

        Writes(world).Should().BeEmpty();
        OwnerTeamOf(world).Should().Be(DirectTeam);
        world.Log.Entries.Should().Contain(e => e.Level >= LogLevel.Warning);
    }

    [Fact(DisplayName = "198 pass 2 N4: at write, a communication that is not found is logged as a share failure, not a silent return")]
    public async Task N4_AtWrite_ACommunicationNotFound_IsLogged()
    {
        var world = UnfiledDirectMessage();
        world.Rows.Remove((GateSharingWorld.Communication, CommunicationId));

        await world.Sharing.ShareTeamOwnedAtWriteAsync(RecordId, CommunicationId, DirectTeam);

        Writes(world).Should().BeEmpty();
        world.Log.Entries.Should().Contain(e => e.Level >= LogLevel.Warning && e.Message.Contains("could not be read", StringComparison.Ordinal));
    }

    // ── 198 pass 2 N3: the mirror also removes a leftover share on a Direct-owned record ─────────────────────────

    [Fact(DisplayName = "198 pass 2 N3 must-fire: the mirror revokes a leftover non-participant share on a Direct-owned record")]
    public async Task N3_Mirror_ADirectOwnedRecord_LosesALeftoverNonParticipantShare()
    {
        var world = UnfiledDirectMessage().Share(GateSharingWorld.Record, RecordId, Third); // a revoke that failed earlier

        await world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

        world.RecordSharees(RecordId).Should().Equal(new[] { Sender, Recipient }.OrderBy(g => g));
        world.Calls.Should().Contain(Revoke(Third));
    }

    [Fact(DisplayName = "198 pass 2 N3 must-not-fire: an invalid pair revokes no leftover share")]
    public async Task N3_Mirror_AnInvalidPair_RevokesNoLeftover()
    {
        var world = UnfiledDirectMessage().Share(GateSharingWorld.Record, RecordId, Third);
        world.Rows[(GateSharingWorld.Communication, CommunicationId)].Attributes.Remove("sprk_directrecipient");

        await world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

        world.RecordSharees(RecordId).Should().Equal(new[] { Third });
        world.Calls.Should().NotContain(c => c.StartsWith("revoke:", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "198 pass 2 N3 must-not-fire: a fault recomputing the audience revokes no leftover share")]
    public async Task N3_Mirror_AnAudienceFault_RevokesNoLeftover()
    {
        var world = UnfiledDirectMessage().Share(GateSharingWorld.Record, RecordId, Third);
        world.UserReadFault = new InvalidOperationException("Test: the users could not be read.");

        await world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);

        world.RecordSharees(RecordId).Should().Equal(new[] { Third });
        world.Calls.Should().NotContain(c => c.StartsWith("revoke:", StringComparison.Ordinal));
    }

    // ── Must fire: rule 4 (filing keeps the recomputed participants) ───────────────────────────────────────────

    [Fact]
    public async Task Filing_ADirectRecord_IsReowned_KeepsTheRecomputedParticipantsRead_AndRevokesTheRest_InOrder()
    {
        var world = FiledDirectMessage();

        await world.Sharing.ReownAfterFilingAsync(CommunicationId);

        OwnerTeamOf(world).Should().Be(MatterTeam);
        world.RecordSharees(RecordId).Should().Equal(new[] { Sender, Recipient }.OrderBy(g => g), "D-215 rule 4: the participants keep Read");
        world.Calls.Should().Contain(Revoke(Third)).And.NotContain(Revoke(Sender)).And.NotContain(Revoke(Recipient));
        var assign = world.Calls.IndexOf($"assign:{RecordId:D}:{MatterTeam:D}");
        var readBack = world.Calls.FindIndex(Math.Max(assign, 0), c => c.StartsWith("read-owner:", StringComparison.Ordinal));
        assign.Should().BeGreaterThanOrEqualTo(0);
        readBack.Should().BeGreaterThan(assign);
        world.Calls.IndexOf(Revoke(Third)).Should().BeGreaterThan(readBack, "revoke only after the read-back");
        world.Cache.Verify(c => c.InvalidateRecordOwnerChangeAsync("sprk_decisionrecord", "sprk_decisionrecords", RecordId, It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        world.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Filing_TheKeptAudienceIsRecomputed_AParticipantWhoNoLongerReadsTheMessage_LosesTheRecord()
    {
        var world = FiledDirectMessage();
        world.SharesOf(GateSharingWorld.Communication, CommunicationId).Remove(DataversePrincipalRef.User(Recipient));

        await world.Sharing.ReownAfterFilingAsync(CommunicationId);

        world.RecordSharees(RecordId).Should().Equal(new[] { Sender }, "the audience at filing time, never a stale copy");
    }

    [Fact]
    public async Task Filing_ALaterFilingsLeftoverRevoke_SparesTheParticipants_AndRevokesTheRest()
    {
        // The record is already the matter team's (an earlier filing's revoke of Third failed).
        var world = FiledDirectMessage();
        world.OwnByTeam(RecordId, MatterTeam);

        await world.Sharing.ReownAfterFilingAsync(CommunicationId);

        world.Calls.Should().NotContain(c => c.StartsWith("assign:", StringComparison.Ordinal));
        world.Calls.Where(c => c.StartsWith("revoke:", StringComparison.Ordinal)).Should().Equal(Revoke(Third));
        world.RecordSharees(RecordId).Should().Equal(new[] { Sender, Recipient }.OrderBy(g => g));
    }

    [Fact]
    public async Task Filing_IntoASecureRecord_TheResolversSecureAnswer_WritesNothing()
    {
        var world = FiledDirectMessage();
        world.OwnerAnswers(RecordOwnerResolution.Owned(Guid.NewGuid()) with { IsSecureOwner = true });

        await world.Sharing.ReownAfterFilingAsync(CommunicationId);

        Writes(world).Should().BeEmpty("the Secure mirror decides a secure target's owner and shares");
        OwnerTeamOf(world).Should().Be(DirectTeam);
    }

    [Fact]
    public async Task Filing_IntoASecureRecord_TheOneSecureCheck_WritesNothing_AndKeepsNoOne()
    {
        var secureTeam = Guid.NewGuid();
        var world = FiledDirectMessage().WithSecureTeam(secureTeam);
        world.OwnerAnswers(RecordOwnerResolution.Owned(secureTeam)); // not flagged by the resolver: the one Secure check decides

        await world.Sharing.ReownAfterFilingAsync(CommunicationId);

        Writes(world).Should().BeEmpty("D-215 rule 4: a Secure target is unchanged (no assign, no keep, no revoke)");
        world.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning, "a Secure target is a defined outcome");
    }

    // ── Must fire: unfile back to Direct (rule 5's order) ───────────────────────────────────────────────────────

    [Fact]
    public async Task Unfile_ReownsToTheDirectTeam_EvictsOnce_ReadsBack_ThenGrants_ThenRevokes_InThatOrder()
    {
        var world = UnfiledBackToDirect();
        string? reason = null;
        world.Cache.Setup(c => c.InvalidateRecordOwnerChangeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, Guid, string?, CancellationToken>((_, _, _, r, _) => reason = r)
            .Returns(Task.CompletedTask);

        await world.Sharing.ReownAfterFilingAsync(CommunicationId);

        OwnerTeamOf(world).Should().Be(DirectTeam);
        world.RecordSharees(RecordId).Should().Equal(new[] { Sender, Recipient }.OrderBy(g => g));
        var assign = world.Calls.IndexOf($"assign:{RecordId:D}:{DirectTeam:D}");
        var readBack = world.Calls.FindIndex(Math.Max(assign, 0), c => c.StartsWith("read-owner:", StringComparison.Ordinal));
        var grant = world.Calls.IndexOf(Grant(Recipient));
        var revoke = world.Calls.IndexOf(Revoke(Third));
        assign.Should().BeGreaterThanOrEqualTo(0, "(a) assigned to the Direct team");
        readBack.Should().BeGreaterThan(assign, "(c) read back after the assign");
        grant.Should().BeGreaterThan(readBack, "(d) grant only after the read-back names the Direct team");
        revoke.Should().BeGreaterThan(grant, "(d) then every other share is revoked");
        world.Calls.Should().NotContain(Grant(Sender), "Sender already holds Read").And.NotContain(Revoke(Sender));
        world.Cache.Verify(c => c.InvalidateRecordOwnerChangeAsync("sprk_decisionrecord", "sprk_decisionrecords", RecordId, It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once, "(b) evicted once per attempt");
        reason.Should().Be($"gate-dr-direct:{RecordId:D}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Unfile_AReadBackThatDoesNotNameTheDirectTeam_KeepsTheOwnerAndTheShares_WithAWarning(bool assignRefused)
    {
        var world = UnfiledBackToDirect();
        if (assignRefused)
        {
            world.AssignLands = false;
            world.AssignFault = new InvalidOperationException("Test: Dataverse refused the Assign.");
        }
        else
        {
            world.OwnerReadFault = new InvalidOperationException("Test: the owner read failed.");
        }

        await world.Sharing.ReownAfterFilingAsync(CommunicationId);

        if (assignRefused)
            OwnerTeamOf(world).Should().Be(MatterTeam, "the owner is kept");
        world.Calls.Should().NotContain(c => c.StartsWith("grant:", StringComparison.Ordinal) || c.StartsWith("revoke:", StringComparison.Ordinal));
        world.RecordSharees(RecordId).Should().Equal(new[] { Sender, Third }.OrderBy(g => g), "the shares are kept");
        world.Log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.EventId.Should().Be(DecisionRecordEvents.FilingReownFailed);
        world.Cache.Verify(c => c.InvalidateRecordOwnerChangeAsync("sprk_decisionrecord", "sprk_decisionrecords", RecordId, It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once, "a write that reports failure can have committed");
    }

    [Fact]
    public async Task Unfile_ASecureTeamOwnedRecord_IsLeftToTheSecureReconciler()
    {
        var secureTeam = Guid.NewGuid();
        var world = UnfiledBackToDirect().WithSecureTeam(secureTeam);
        world.OwnByTeam(RecordId, secureTeam);

        await world.Sharing.ReownAfterFilingAsync(CommunicationId);

        Writes(world).Should().BeEmpty();
        OwnerTeamOf(world).Should().Be(secureTeam);
    }

    // ── Must not fire: every other owner is byte-for-byte D-169 ─────────────────────────────────────────────────

    public enum OtherOwner { BusinessUnitDefaultTeam, SecureTeam, User }

    /// <summary>The same scenario with and without the Direct branch (the lookup and the Secure settings).</summary>
    private static GateSharingWorld OtherOwnedCommunication(OtherOwner owner, bool directLookup, bool filed)
    {
        var secureTeam = Guid.Parse("5ec00000-0000-4000-8000-000000000001");
        var world = new GateSharingWorld(directLookup)
            .WithTeam(DirectTeam, DirectMessageOwnerTeamDouble.OwnerTeamName, isDefault: false)
            .WithTeam(MatterTeam, "Matters", isDefault: true)
            .WithSecureTeam(secureTeam)
            .WithCommunication(CommunicationId, Sender,
                owningTeam: owner switch
                {
                    OtherOwner.BusinessUnitDefaultTeam => MatterTeam,
                    OtherOwner.SecureTeam => secureTeam,
                    _ => null,
                },
                filedUnder: filed ? MatterId : null)
            .WithPair(CommunicationId, Sender, Recipient) // even a stamped pair changes nothing for another owner
            .WithUser(Sender).WithUser(Recipient).WithUser(Third)
            .Share(GateSharingWorld.Communication, CommunicationId, Recipient)
            .Share(GateSharingWorld.Communication, CommunicationId, Third)
            .WithGateRecord(RecordId, CommunicationId, Sender);
        world.OwnerAnswers(owner == OtherOwner.SecureTeam
            ? RecordOwnerResolution.Owned(secureTeam) with { IsSecureOwner = true }
            : RecordOwnerResolution.Owned(MatterTeam));
        return world;
    }

    [Theory]
    [InlineData(OtherOwner.BusinessUnitDefaultTeam, false)]
    [InlineData(OtherOwner.BusinessUnitDefaultTeam, true)]
    [InlineData(OtherOwner.SecureTeam, false)]
    [InlineData(OtherOwner.SecureTeam, true)]
    [InlineData(OtherOwner.User, false)]
    [InlineData(OtherOwner.User, true)]
    public async Task AnotherOwner_WritesExactlyWhatD169Writes_WithOrWithoutTheDirectBranch(OtherOwner owner, bool filed)
    {
        async Task<List<string>> Scenario(bool directLookup)
        {
            var world = OtherOwnedCommunication(owner, directLookup, filed);
            await world.Sharing.ShareAtWriteAsync(RecordId, CommunicationId, Sender);
            world.Share(GateSharingWorld.Communication, CommunicationId, Sender);
            await world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);
            await world.Sharing.ReownAfterFilingAsync(CommunicationId);
            return world.Calls.ToList();
        }

        var without = await Scenario(directLookup: false);
        var with = await Scenario(directLookup: true);

        with.Should().Equal(without, "D-215 changes nothing for a message the Direct team does not own");
    }

    [Theory]
    [InlineData(OtherOwner.BusinessUnitDefaultTeam)]
    [InlineData(OtherOwner.SecureTeam)]
    public async Task AtWrite_ATeamOwnedRecordOfAnotherTeamsMessage_WritesNothing(OtherOwner owner)
    {
        var world = OtherOwnedCommunication(owner, directLookup: true, filed: false);
        world.OwnByTeam(RecordId, MatterTeam);

        await world.Sharing.ShareTeamOwnedAtWriteAsync(RecordId, CommunicationId, MatterTeam);

        world.Calls.Should().BeEmpty("D-169: a team-owned record is never shared");
        world.Log.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    // ── Must not fire: Direct identification unknown ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("at-write")]
    [InlineData("at-write-user-owned")]
    [InlineData("mirror")]
    [InlineData("unfile")]
    [InlineData("filing")]
    public async Task WhenTheDirectTeamCannotBeIdentified_NothingIsWritten_AndItIsLogged(string path)
    {
        var world = path switch
        {
            "unfile" => UnfiledBackToDirect(),
            "filing" => FiledDirectMessage(),
            _ => UnfiledDirectMessage(),
        };
        if (path == "at-write-user-owned")
        {
            world.WithGateRecord(RecordId, CommunicationId, Sender); // the record the writer left user-owned
        }

        world.Directory.Faults = true;
        var before = world.RecordSharees(RecordId);
        var ownerBefore = world.Rows[(GateSharingWorld.Record, RecordId)].GetAttributeValue<EntityReference>("ownerid")!.Id;

        await (path switch
        {
            "at-write" => world.Sharing.ShareTeamOwnedAtWriteAsync(RecordId, CommunicationId, DirectTeam),
            "at-write-user-owned" => world.Sharing.ShareAtWriteAsync(RecordId, CommunicationId, Sender),
            "mirror" => world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None),
            _ => world.Sharing.ReownAfterFilingAsync(CommunicationId),
        });

        Writes(world).Should().BeEmpty("D-215 rule 1: an unknown identification changes nothing");
        world.RecordSharees(RecordId).Should().Equal(before);
        world.Rows[(GateSharingWorld.Record, RecordId)].GetAttributeValue<EntityReference>("ownerid")!.Id.Should().Be(ownerBefore);
        world.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }

    // ── Must not fire: an invalid pair ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("no-recipient", "mirror")]
    [InlineData("no-recipient", "unfile")]
    [InlineData("same-user", "mirror")]
    [InlineData("same-user", "unfile")]
    [InlineData("same-user", "filing")]
    public async Task AnInvalidPair_GrantsNothing_AndRevokesNothing(string pair, string path)
    {
        var world = path switch
        {
            "unfile" => UnfiledBackToDirect(),
            "filing" => FiledDirectMessage(),
            _ => UnfiledDirectMessage(),
        };
        var message = world.Rows[(GateSharingWorld.Communication, CommunicationId)];
        if (pair == "no-recipient")
            message.Attributes.Remove("sprk_directrecipient");
        else
            message["sprk_directrecipient"] = new EntityReference("systemuser", Sender);
        var before = world.RecordSharees(RecordId);

        if (path == "mirror")
            await world.Sharing.MirrorCommunicationSharesAsync(CommunicationId, CancellationToken.None);
        else
            await world.Sharing.ReownAfterFilingAsync(CommunicationId);

        world.Calls.Should().NotContain(c => c.StartsWith("grant:", StringComparison.Ordinal));
        if (path != "filing")
        {
            world.Calls.Should().NotContain(c => c.StartsWith("revoke:", StringComparison.Ordinal), "an invalid pair is settled: no revoke");
            world.RecordSharees(RecordId).Should().Equal(before);
        }
        else
        {
            // Filing is D-169's re-own: with no valid pair nobody is kept (every share goes, as for a message that is not Direct).
            world.RecordSharees(RecordId).Should().BeEmpty();
        }
    }
}
