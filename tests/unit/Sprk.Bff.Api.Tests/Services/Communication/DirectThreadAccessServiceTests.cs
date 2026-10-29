using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Membership;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of <see cref="DirectThreadAccessService"/> — the Direct 1:1 thread access mechanics (task 043 /
/// FR-09) AND the generalized Open/record-anchored message-access grant (task 052 / FR-11). Protects the
/// closed set the acceptance criteria name: exactly-two membership (owner ∪ POA share), ordered-pair reuse
/// (no duplicate thread on a second "start"), no ADR-024 regarding anchor on a Direct thread, a THIRD user
/// never receiving a grant, a Direct thread's grant being UNCHANGED (no regression), an Open thread granting
/// EXACTLY the task-041 derived systemuser set, a non-member never being granted, a contact participant
/// being skipped (R2), and a derivation/grant failure being swallowed (best-effort, NFR-02). Module
/// boundaries (<see cref="IGenericEntityService"/> — SDK Dataverse — <see cref="IDataverseRecordShareService"/>
/// — the POA testing seam, ADR-010 — and <see cref="IThreadMembershipDerivationService"/> — task 041's shared
/// derivation contract) are mocked; no <c>Mock&lt;HttpMessageHandler&gt;</c> (ADR-038).
/// </summary>
public class DirectThreadAccessServiceTests
{
    private static readonly Guid Caller = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ThirdUser = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ThreadId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid CommunicationId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TeamId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private const int ThreadTypeDirect = 100000001;
    private const int ThreadTypeRecordAnchored = 100000000;

    private readonly Mock<IGenericEntityService> _entityService = new();
    private readonly Mock<IDataverseRecordShareService> _accessGrant = new();
    private readonly Mock<IThreadMembershipDerivationService> _membershipDerivation = new();

    /// <summary>A systemuser principal reference — the shape the task-060 seam takes.</summary>
    private static DataversePrincipalRef User(Guid systemUserId) => DataversePrincipalRef.User(systemUserId);

    /// <summary>
    /// POA rows as the task-060 seam returns them: systemuser shares with ReadAccess. The tests care only
    /// about WHO holds a share, so the mask and timestamp are fixed.
    /// </summary>
    private static IReadOnlyList<DataversePrincipalAccess> Shares(params Guid[] systemUserIds) =>
        systemUserIds
            .Select(id => new DataversePrincipalAccess(User(id), 1, DateTimeOffset.UtcNow))
            .ToList();

    /// <summary>
    /// Task 149 r1: the message-access grant first reads the message's owner (a Secure-team-owned message is never
    /// granted to participants). Every message here is USER-owned unless a test says otherwise — the shape these
    /// tests were written for. The secure cases are pinned against an evaluating Dataverse in SecureChildShareMirrorTests.
    /// </summary>
    public DirectThreadAccessServiceTests()
    {
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communication"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                new("sprk_communication") { Id = CommunicationId, ["owninguser"] = new EntityReference("systemuser", Caller) },
            }));
    }

    private DirectThreadAccessService BuildSut() => BuildSut(new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing);

    // ── FindOrCreateDirectThreadAsync: create + no regarding anchor ────────────────────────────

    [Fact]
    public async Task FindOrCreateDirectThreadAsync_NoExistingThread_CreatesDirectThreadOwnedByCallerWithNoRegardingAnchor()
    {
        // No candidates owned by either side.
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        Entity? created = null;
        _entityService
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => created = e)
            .ReturnsAsync(ThreadId);

        var result = await BuildSut().FindOrCreateDirectThreadAsync(Caller, Other);

        result.Should().Be(ThreadId);
        created.Should().NotBeNull();
        created!.LogicalName.Should().Be("sprk_communicationthread");
        created.GetAttributeValue<OptionSetValue>("sprk_threadtype").Value.Should().Be(ThreadTypeDirect);
        created.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Caller);
        created.Contains("sprk_regardingrecordid").Should().BeFalse();
        created.Contains("sprk_regardingrecordtype").Should().BeFalse();

        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communicationthreads", ThreadId, User(Other), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── FindOrCreateDirectThreadAsync: ordered-pair reuse (both directions) ────────────────────

    [Fact]
    public async Task FindOrCreateDirectThreadAsync_ExistingThreadOwnedByCallerSharedToOther_ReusesWithoutCreatingDuplicate()
    {
        SetupCandidates(ownerId: Caller, candidateThreadIds: new[] { ThreadId });
        _accessGrant
            .Setup(g => g.GetPrincipalAccessAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(Other));

        var result = await BuildSut().FindOrCreateDirectThreadAsync(Caller, Other);

        result.Should().Be(ThreadId);
        _entityService.Verify(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FindOrCreateDirectThreadAsync_ExistingThreadOwnedByOtherSharedToCaller_ReusesWithoutCreatingDuplicate()
    {
        // The FIRST direction (owner=caller) has no candidates; the SECOND direction (owner=other) does.
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => HasOwnerCondition(q, Caller)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => HasOwnerCondition(q, Other)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { new("sprk_communicationthread") { Id = ThreadId } }));
        _accessGrant
            .Setup(g => g.GetPrincipalAccessAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(Caller));

        var result = await BuildSut().FindOrCreateDirectThreadAsync(Caller, Other);

        result.Should().Be(ThreadId);
        _entityService.Verify(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FindOrCreateDirectThreadAsync_CandidateThreadNotSharedToOther_CreatesNewThreadInstead()
    {
        // A Direct thread owned by the caller exists, but it is NOT shared to "Other" — must not false-match.
        SetupCandidates(ownerId: Caller, candidateThreadIds: new[] { Guid.NewGuid() });
        _accessGrant
            .Setup(g => g.GetPrincipalAccessAsync("sprk_communicationthread", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(ThirdUser)); // shared to someone else entirely
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => HasOwnerCondition(q, Other)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        var newThreadId = Guid.NewGuid();
        _entityService
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newThreadId);

        var result = await BuildSut().FindOrCreateDirectThreadAsync(Caller, Other);

        result.Should().Be(newThreadId);
        _entityService.Verify(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GetParticipantSystemUserIdsAsync: exactly-two membership + third-user exclusion ────────

    [Fact]
    public async Task GetParticipantSystemUserIdsAsync_DirectThread_ReturnsExactlyOwnerAndSharedParticipant()
    {
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        _accessGrant
            .Setup(g => g.GetPrincipalAccessAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(Other));

        var participants = await BuildSut().GetParticipantSystemUserIdsAsync(ThreadId);

        participants.Should().BeEquivalentTo(new[] { Caller, Other });
        participants.Should().NotContain(ThirdUser);
    }

    [Fact]
    public async Task GetParticipantSystemUserIdsAsync_TeamShareOnThread_IsNotCountedAsAParticipant()
    {
        // Task 060: the POA read is now principal-KIND-typed, because teams share through the same
        // client that users do. Before that, a team share on a thread would have been returned as a
        // participating systemuser id — the pre-060 primitive selected only principalid and ASSUMED
        // every share was a user. A team id landing in this list is a membership leak.
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        _accessGrant
            .Setup(g => g.GetPrincipalAccessAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new DataversePrincipalAccess(DataversePrincipalRef.User(Other), 1, DateTimeOffset.UtcNow),
                new DataversePrincipalAccess(DataversePrincipalRef.Team(TeamId), 1, DateTimeOffset.UtcNow),
            });

        var participants = await BuildSut().GetParticipantSystemUserIdsAsync(ThreadId);

        participants.Should().BeEquivalentTo(new[] { Caller, Other });
        participants.Should().NotContain(TeamId, "a team is not a participant of a Direct 1:1 thread");
    }

    [Fact]
    public async Task GetParticipantSystemUserIdsAsync_RecordAnchoredThread_ReturnsEmptyAndDoesNotReadShares()
    {
        SetupThread(ThreadId, ThreadTypeRecordAnchored, ownerId: Caller);

        var participants = await BuildSut().GetParticipantSystemUserIdsAsync(ThreadId);

        participants.Should().BeEmpty();
        _accessGrant.Verify(
            g => g.GetPrincipalAccessAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── GrantMessageAccessAsync: the CRITICAL RISK fix — grants both participants, no-op otherwise ─

    private static readonly Guid MatterA = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid MatterX = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private static ThreadAssignment DirectJoin => ThreadAssignment.Direct(ThreadId);
    private static ThreadAssignment AnchoredJoin(Guid matter) => ThreadAssignment.Anchored(ThreadId, "sprk_matter", matter);

    private void SetupGrantees(Guid matter, params Guid[] users) =>
        _membershipDerivation
            .Setup(m => m.DeriveAnchorGranteeUserIdsAsync("sprk_matter", matter, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

    // ── Ontology task 042b (D-169): a user-owned message's gate Decision Records follow its shares ───────────

    private static readonly Guid GateRecordId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    /// <summary>The message's gate Decision Record (owned by the message's user) and the shares the grant below leaves on the message.</summary>
    private static Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld GateRecordWorld() =>
        new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld()
            .WithCommunication(CommunicationId, Caller)
            .WithUser(Caller).WithUser(Other)
            .WithGateRecord(GateRecordId, CommunicationId, Caller)
            .Share(Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld.Communication, CommunicationId, Other);

    private DirectThreadAccessService BuildSut(Sprk.Bff.Api.Services.Signals.GateDecisionRecordSharing gateDecisionRecords) => new(
        _entityService.Object,
        _accessGrant.Object,
        new Lazy<IThreadMembershipDerivationService>(() => _membershipDerivation.Object),
        new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
        Mock.Of<ILogger<DirectThreadAccessService>>(),
        gateDecisionRecords);

    [Fact]
    public async Task GrantMessageAccessAsync_AUserOwnedMessage_MirrorsTheGrantToItsGateDecisionRecord_D169()
    {
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        _accessGrant
            .Setup(g => g.GetPrincipalAccessAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(Other));
        var world = GateRecordWorld();

        await BuildSut(world.Sharing).GrantMessageAccessAsync(CommunicationId, DirectJoin);

        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Other), "ReadAccess", It.IsAny<CancellationToken>()), Times.Once);
        world.RecordSharees(GateRecordId).Should().Equal(new[] { Other }, "the message's new sharee reads its gate record too (the owner already owns it)");
    }

    [Fact]
    public async Task GrantMessageAccessAsync_ATeamOwnedMessage_IsNotMirrored_D169()
    {
        MessageOwnedByOrdinaryTeam(TeamId);
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        _accessGrant
            .Setup(g => g.GetPrincipalAccessAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(Other));
        var world = GateRecordWorld();

        await BuildSut(world.Sharing).GrantMessageAccessAsync(CommunicationId, DirectJoin);

        world.Calls.Should().BeEmpty();
        world.Entities.Invocations.Should().BeEmpty("a team-owned message has no user-owned gate record to look for");
    }

    [Fact]
    public async Task GrantMessageAccessAsync_DirectThread_GrantsReadAccessToBothParticipantsOnTheMessage()
    {
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        _accessGrant
            .Setup(g => g.GetPrincipalAccessAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(Other));

        await BuildSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Caller), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Other), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(ThirdUser), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // One grant mechanism per audience: the Direct branch never touches the record derivation.
        _membershipDerivation.Verify(
            m => m.DeriveAnchorGranteeUserIdsAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _membershipDerivation.Verify(
            m => m.DeriveAuthorizedSetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GrantMessageAccessAsync_RecordThreadWithNoGrantees_GrantsNoAccess()
    {
        SetupThread(ThreadId, ThreadTypeRecordAnchored, ownerId: Caller);
        SetupGrantees(MatterA);

        await BuildSut().GrantMessageAccessAsync(CommunicationId, AnchoredJoin(MatterA));

        _accessGrant.Verify(
            g => g.GrantAccessAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DataversePrincipalRef>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GrantMessageAccessAsync_NoAudience_ReadsAndGrantsNothing()
    {
        // The per-user master (Tier 3) and an unanchored record thread grant nobody (#1629).
        await BuildSut().GrantMessageAccessAsync(CommunicationId, ThreadAssignment.NoGrant(ThreadId));

        _accessGrant.Invocations.Should().BeEmpty();
        _membershipDerivation.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task GrantMessageAccessAsync_WhenGrantThrowsForOneParticipant_StillGrantsTheOther()
    {
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        _accessGrant
            .Setup(g => g.GetPrincipalAccessAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(Other));
        _accessGrant
            .Setup(g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Caller), "ReadAccess", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("grant boom"));

        var act = () => BuildSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        await act.Should().NotThrowAsync(); // best-effort (NFR-02)
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Other), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── GrantMessageAccessAsync: record thread — task 052 / FR-11, audience fixed at the join (#1629) ──

    [Fact]
    public async Task GrantMessageAccessAsync_RecordThread_GrantsReadAccessToExactlyTheAnchorsGrantees()
    {
        SetupThread(ThreadId, ThreadTypeRecordAnchored, ownerId: Caller);
        SetupGrantees(MatterA, Caller, Other);

        await BuildSut().GrantMessageAccessAsync(CommunicationId, AnchoredJoin(MatterA));

        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Caller), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Other), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
        // EXACTLY the grantees — no more, no less (no over-grant), and a non-member never.
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(ThirdUser), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, It.IsAny<DataversePrincipalRef>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task GrantMessageAccessAsync_ThreadFlippedToDirectAfterAJoinAsARecordThread_StillGrantsOnlyTheAnchorsGrantees()
    {
        // #1629 F-C: the thread's owner flipped sprk_threadtype to Direct (and shared it) after the join. The grant follows
        // the join's audience: the anchor's grantees. The owner and their sharee are never granted.
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        _accessGrant
            .Setup(g => g.GetPrincipalAccessAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(ThirdUser));
        SetupGrantees(MatterA, Other);

        await BuildSut().GrantMessageAccessAsync(CommunicationId, AnchoredJoin(MatterA));

        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Other), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, It.IsAny<DataversePrincipalRef>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _entityService.Verify(
            s => s.RetrieveAsync("sprk_communicationthread", ThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GrantMessageAccessAsync_AnchorSwappedAfterTheJoin_GrantsTheJoinTimeAnchorsGrantees()
    {
        // #1629 F-C: the owner swapped the thread's lookup from matter A to secure-adjacent matter X after the join. The grant
        // derives from A (the join's anchor) and never asks for X, nor for the thread's current set.
        SetupThread(ThreadId, ThreadTypeRecordAnchored, ownerId: Caller);
        SetupGrantees(MatterA, Other);
        SetupGrantees(MatterX, ThirdUser);

        await BuildSut().GrantMessageAccessAsync(CommunicationId, AnchoredJoin(MatterA));

        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Other), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(ThirdUser), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _membershipDerivation.Verify(
            m => m.DeriveAnchorGranteeUserIdsAsync("sprk_matter", MatterX, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        _membershipDerivation.Verify(
            m => m.DeriveAuthorizedSetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GrantMessageAccessAsync_ThreadFlippedToRecordAnchoredAfterADirectJoin_StillGrantsTheTwoParties()
    {
        // The reverse flip: a Direct join keeps its two-party audience; no record membership is derived.
        SetupThread(ThreadId, ThreadTypeRecordAnchored, ownerId: Caller);
        _accessGrant
            .Setup(g => g.GetPrincipalAccessAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(Other));

        await BuildSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, It.IsAny<DataversePrincipalRef>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        _membershipDerivation.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task GrantMessageAccessAsync_RecordThreadWhenDerivationThrows_SwallowsAndGrantsNothing()
    {
        SetupThread(ThreadId, ThreadTypeRecordAnchored, ownerId: Caller);
        _membershipDerivation
            .Setup(m => m.DeriveAnchorGranteeUserIdsAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("derivation boom"));

        var act = () => BuildSut().GrantMessageAccessAsync(CommunicationId, AnchoredJoin(MatterA));

        await act.Should().NotThrowAsync(); // best-effort (NFR-02) — a derivation failure never fails ingest/send
        _accessGrant.Verify(
            g => g.GrantAccessAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DataversePrincipalRef>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── #1629 F-A / F2: the grant never expands the MESSAGE's owning team, and only that team (real derivation) ──

    [Fact]
    public async Task GrantMessageAccessAsync_PassesTheMessagesOwningTeamToTheDerivation()
    {
        var messageTeam = Guid.Parse("9a9a9a9a-0000-0000-0000-00000000009a");
        MessageOwnedByOrdinaryTeam(messageTeam);
        SetupGrantees(MatterA, Other);

        await BuildSut().GrantMessageAccessAsync(CommunicationId, AnchoredJoin(MatterA));

        _membershipDerivation.Verify(
            m => m.DeriveAnchorGranteeUserIdsAsync("sprk_matter", MatterA, messageTeam, It.IsAny<CancellationToken>()), Times.Once);
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Other), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GrantMessageAccessAsync_TeamOwnedMatter_MessageOwnedByTheSameTeam_GrantsOnlyTheLookupNamedUser()
    {
        // The common case: an ordinary matter owned by the business unit's default team (174 members) with an assigned
        // attorney; the message filed under it is owned by that same team (task 146), so its members already read it.
        // Only the lookup-named user is granted — one share, not 175.
        var ownerTeam = Guid.Parse("99999999-0000-0000-0000-000000000009");
        var attorney = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
        var teamMembers = Enumerable.Range(1, 174).Select(i => Guid.Parse($"bbbbbbbb-0000-0000-0000-{i:D12}")).ToArray();
        MessageOwnedByOrdinaryTeam(ownerTeam);
        var sut = RealDerivationSut("sprk_matter", MatterA, new Dictionary<string, EntityReference>
        {
            ["ownerid"] = new EntityReference("team", ownerTeam),
            ["owningteam"] = new EntityReference("team", ownerTeam),
            ["sprk_assignedattorney"] = new EntityReference("systemuser", attorney),
        }, teamMembers);

        await sut.GrantMessageAccessAsync(CommunicationId, AnchoredJoin(MatterA));

        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(attorney), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, It.IsAny<DataversePrincipalRef>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _entityService.Verify(
            s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "teammembership"), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GrantMessageAccessAsync_AccountAnchorOwnedByATeam_GrantsThatTeamsMembers()
    {
        // F2 (a): an account is never an ownership parent, so the message is owned elsewhere (here: another BU's default
        // team). The account's owner team's members cannot read the message through ownership, so each is granted.
        var accountId = Guid.Parse("acacacac-0000-0000-0000-0000000000ac");
        var accountTeam = Guid.Parse("9b9b9b9b-0000-0000-0000-00000000009b");
        var messageTeam = Guid.Parse("9c9c9c9c-0000-0000-0000-00000000009c");
        var members = new[] { Guid.Parse("cccccccc-0000-0000-0000-000000000001"), Guid.Parse("cccccccc-0000-0000-0000-000000000002") };
        MessageOwnedByOrdinaryTeam(messageTeam);
        var sut = RealDerivationSut("account", accountId, new Dictionary<string, EntityReference>
        {
            ["ownerid"] = new EntityReference("team", accountTeam),
            ["owningteam"] = new EntityReference("team", accountTeam),
        }, members);

        await sut.GrantMessageAccessAsync(CommunicationId, ThreadAssignment.Anchored(ThreadId, "account", accountId));

        foreach (var member in members)
            _accessGrant.Verify(
                g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(member), "ReadAccess", It.IsAny<CancellationToken>()),
                Times.Once);
    }

    [Fact]
    public async Task GrantMessageAccessAsync_MatterOwnedByANonDefaultTeamInAnotherBu_GrantsThatTeamsMembers()
    {
        // F2 (b): matter owned by non-default owner team T2 (its members sit in another business unit); the message is
        // owned by the default team of the matter's business unit (RecordOwnershipResolver), so T2's members are granted.
        var t2 = Guid.Parse("9d9d9d9d-0000-0000-0000-00000000009d");
        var buDefaultTeam = Guid.Parse("9e9e9e9e-0000-0000-0000-00000000009e");
        var t2Member = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
        MessageOwnedByOrdinaryTeam(buDefaultTeam);
        var sut = RealDerivationSut("sprk_matter", MatterA, new Dictionary<string, EntityReference>
        {
            ["ownerid"] = new EntityReference("team", t2),
            ["owningteam"] = new EntityReference("team", t2),
        }, t2Member);

        await sut.GrantMessageAccessAsync(CommunicationId, AnchoredJoin(MatterA));

        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(t2Member), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GrantMessageAccessAsync_MessageOwnedByALookalikeOfTheSecureTeam_IsGrantedAndSkipsThatTeam()
    {
        // The full secure-team resolution decides: a non-default Owner team carrying the Secure team's name, in another
        // business unit, is NOT the Secure Record owner team. The message is ordinary, so it is granted, and the team that
        // owns it is the one the derivation skips.
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var lookalike = Guid.Parse("9f9f9f9f-0000-0000-0000-00000000009f");
        var secureTeam = Guid.Parse("5e5e5e5e-0000-0000-0000-00000000005e");
        var secureBu = Guid.Parse("5b5b5b5b-0000-0000-0000-00000000005b");
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communication"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                new("sprk_communication") { Id = CommunicationId, ["owningteam"] = new EntityReference("team", lookalike) },
            }));
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "team" && q.Criteria.Conditions.Any(c => c.AttributeName == "teamid")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                new("team")
                {
                    Id = lookalike,
                    ["name"] = Sprk.Bff.Api.Infrastructure.Dataverse.SecureRecordOwnerTeam.OwnerTeamName(config),
                    ["isdefault"] = false,
                    ["teamtype"] = new OptionSetValue(Sprk.Bff.Api.Infrastructure.Dataverse.SecureRecordOwnerTeam.OwnerTeamType),
                },
            }));
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "businessunit"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { new("businessunit") { Id = secureBu } }));
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "team" && q.Criteria.Conditions.Any(c => c.AttributeName == "businessunitid")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { new("team") { Id = secureTeam } }));
        SetupGrantees(MatterA, Other);

        await BuildSut().GrantMessageAccessAsync(CommunicationId, AnchoredJoin(MatterA));

        _membershipDerivation.Verify(
            m => m.DeriveAnchorGranteeUserIdsAsync("sprk_matter", MatterA, lookalike, It.IsAny<CancellationToken>()), Times.Once);
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Other), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>The message is owned by an ordinary (default, non-Secure) team.</summary>
    private void MessageOwnedByOrdinaryTeam(Guid teamId)
    {
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communication"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                new("sprk_communication") { Id = CommunicationId, ["owningteam"] = new EntityReference("team", teamId) },
            }));
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "team"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                new("team") { Id = teamId, ["name"] = "Business Unit Default", ["isdefault"] = true },
            }));
    }

    /// <summary>
    /// A grant SUT over the REAL derivation: <paramref name="anchorEntity"/> discovers <c>ownerid</c> (user/team),
    /// <c>owningteam</c> and <c>sprk_assignedattorney</c>; every team expands to <paramref name="teamMembers"/>.
    /// </summary>
    private DirectThreadAccessService RealDerivationSut(
        string anchorEntity, Guid anchorId, Dictionary<string, EntityReference> lookups, params Guid[] teamMembers)
    {
        var discovery = new Mock<Sprk.Bff.Api.Services.Ai.Membership.IMembershipFieldDiscoveryService>();
        discovery
            .Setup(d => d.DiscoverAsync(anchorEntity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sprk.Bff.Api.Services.Ai.Membership.DiscoveryResult(
                anchorEntity, DateTimeOffset.UtcNow,
                new[]
                {
                    new Sprk.Bff.Api.Services.Ai.Membership.Models.MembershipDescriptor("ownerid", "ownerid", "systemuser", "systemuser", "auto"),
                    new Sprk.Bff.Api.Services.Ai.Membership.Models.MembershipDescriptor("ownerid", "ownerid", "team", "team", "auto"),
                    new Sprk.Bff.Api.Services.Ai.Membership.Models.MembershipDescriptor("owningteam", "owningteam", "team", "team", "auto"),
                    new Sprk.Bff.Api.Services.Ai.Membership.Models.MembershipDescriptor("sprk_assignedattorney", "sprk_assignedattorney", "systemuser", "systemuser", "auto"),
                },
                Array.Empty<Sprk.Bff.Api.Services.Ai.Membership.IgnoredField>(),
                Array.Empty<Sprk.Bff.Api.Services.Ai.Membership.IgnoredField>()));
        var anchor = new Entity(anchorEntity) { Id = anchorId };
        foreach (var (field, reference) in lookups)
            anchor[field] = reference;
        _entityService
            .Setup(s => s.RetrieveAsync(anchorEntity, anchorId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(anchor);
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "teammembership"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new EntityCollection(teamMembers.Select(m => new Entity("teammembership") { ["systemuserid"] = m }).ToList()));

        var derivation = new ThreadMembershipDerivationService(
            _entityService.Object, discovery.Object, Mock.Of<IThreadExplicitParticipantReader>(),
            Mock.Of<ILogger<ThreadMembershipDerivationService>>());
        return new DirectThreadAccessService(
            _entityService.Object, _accessGrant.Object,
            new Lazy<IThreadMembershipDerivationService>(() => derivation),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            Mock.Of<ILogger<DirectThreadAccessService>>(), new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void SetupThread(Guid threadId, int threadType, Guid ownerId)
    {
        var thread = new Entity("sprk_communicationthread")
        {
            Id = threadId,
            ["sprk_threadtype"] = new OptionSetValue(threadType),
            ["ownerid"] = new EntityReference("systemuser", ownerId),
        };
        _entityService
            .Setup(s => s.RetrieveAsync("sprk_communicationthread", threadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(thread);
    }

    private void SetupCandidates(Guid ownerId, Guid[] candidateThreadIds)
    {
        var entities = candidateThreadIds.Select(id => new Entity("sprk_communicationthread") { Id = id }).ToList();
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => HasOwnerCondition(q, ownerId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(entities));
    }

    private static bool HasOwnerCondition(QueryExpression query, Guid ownerId) =>
        query.EntityName == "sprk_communicationthread" &&
        query.Criteria.Conditions.Any(c =>
            c.AttributeName == "ownerid" && c.Values.Count == 1 && Equals(c.Values[0], ownerId));
}
