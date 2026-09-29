using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
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

    private DirectThreadAccessService BuildSut() => new(
        _entityService.Object,
        _accessGrant.Object,
        new Lazy<IThreadMembershipDerivationService>(() => _membershipDerivation.Object),
        Mock.Of<ILogger<DirectThreadAccessService>>());

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

    [Fact]
    public async Task GrantMessageAccessAsync_DirectThread_GrantsReadAccessToBothParticipantsOnTheMessage()
    {
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        _accessGrant
            .Setup(g => g.GetPrincipalAccessAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(Other));

        await BuildSut().GrantMessageAccessAsync(CommunicationId, ThreadId);

        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Caller), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Other), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(ThirdUser), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // No regression (task 052): the Direct branch returns before ever touching the task-041 derivation —
        // one grant mechanism per topology, not a second path layered on top.
        _membershipDerivation.Verify(
            m => m.DeriveAuthorizedSetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GrantMessageAccessAsync_RecordAnchoredThreadWithNoDerivedMembership_GrantsNoAccess()
    {
        SetupThread(ThreadId, ThreadTypeRecordAnchored, ownerId: Caller);
        _membershipDerivation
            .Setup(m => m.DeriveAuthorizedSetAsync(ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ThreadAuthorizedSet.Empty(ThreadId));

        await BuildSut().GrantMessageAccessAsync(CommunicationId, ThreadId);

        _accessGrant.Verify(
            g => g.GrantAccessAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DataversePrincipalRef>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
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

        var act = () => BuildSut().GrantMessageAccessAsync(CommunicationId, ThreadId);

        await act.Should().NotThrowAsync(); // best-effort (NFR-02)
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Other), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── GrantMessageAccessAsync: OPEN/record-anchored thread — task 052 / FR-11 gap closure ──────

    [Fact]
    public async Task GrantMessageAccessAsync_OpenThread_GrantsReadAccessToExactlyTheDerivedSystemUserSet()
    {
        SetupThread(ThreadId, ThreadTypeRecordAnchored, ownerId: Caller);
        _membershipDerivation
            .Setup(m => m.DeriveAuthorizedSetAsync(ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThreadAuthorizedSet
            {
                ThreadId = ThreadId,
                Participants = new[]
                {
                    new AuthorizedParticipant { Participant = ParticipantReference.SystemUser(Caller), Reason = AuthorizationReason.RecordMembership },
                    new AuthorizedParticipant { Participant = ParticipantReference.SystemUser(Other), Reason = AuthorizationReason.RecordMembership },
                },
            });

        await BuildSut().GrantMessageAccessAsync(CommunicationId, ThreadId);

        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Caller), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Other), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
        // Negative: a user NOT in the task-041 derived set is never granted (a non-member's impersonated
        // read must still return nothing).
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(ThirdUser), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // EXACTLY the derived set — no more, no less (no over-grant).
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, It.IsAny<DataversePrincipalRef>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task GrantMessageAccessAsync_OpenThreadWithContactParticipant_SkipsTheContact()
    {
        SetupThread(ThreadId, ThreadTypeRecordAnchored, ownerId: Caller);
        var contactId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        _membershipDerivation
            .Setup(m => m.DeriveAuthorizedSetAsync(ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ThreadAuthorizedSet
            {
                ThreadId = ThreadId,
                Participants = new[]
                {
                    new AuthorizedParticipant { Participant = ParticipantReference.SystemUser(Caller), Reason = AuthorizationReason.RecordMembership },
                    new AuthorizedParticipant { Participant = ParticipantReference.Contact(contactId), Reason = AuthorizationReason.RecordMembership },
                },
            });

        await BuildSut().GrantMessageAccessAsync(CommunicationId, ThreadId);

        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Caller), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
        // R2 scope: contact (external) participants are skipped in R1.
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(contactId), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, It.IsAny<DataversePrincipalRef>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once); // exactly one grant — the systemuser only
    }

    [Fact]
    public async Task GrantMessageAccessAsync_OpenThreadWhenDerivationThrows_SwallowsAndGrantsNothing()
    {
        SetupThread(ThreadId, ThreadTypeRecordAnchored, ownerId: Caller);
        _membershipDerivation
            .Setup(m => m.DeriveAuthorizedSetAsync(ThreadId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("derivation boom"));

        var act = () => BuildSut().GrantMessageAccessAsync(CommunicationId, ThreadId);

        await act.Should().NotThrowAsync(); // best-effort (NFR-02) — a derivation failure never fails ingest/send
        _accessGrant.Verify(
            g => g.GrantAccessAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DataversePrincipalRef>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
