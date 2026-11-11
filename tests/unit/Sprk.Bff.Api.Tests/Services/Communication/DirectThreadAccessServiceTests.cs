using FluentAssertions;
using Microsoft.Extensions.Configuration;
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
using Sprk.Bff.Api.Tests.TestInfrastructure;
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
        // 198b: reuse reads the thread row too (fm's strict-pair rule): a Direct, Open thread with no typed regarding.
        PairThread(ThreadId, Caller, Shares(Other));

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
        PairThread(ThreadId, Other, Shares(Caller)); // 198b: the strict-pair rule reads the thread row too

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
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
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
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
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
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
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
        _accessGrant.Verify(
            g => g.GetPrincipalAccessOrThrowAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── uac-r2 198 pass 2 item 7: only a Read share on the thread makes a Direct participant, and a share-read fault is never
    // read as "no participant" by the membership reconcile (it would remove the other participant from the chat) ──────────

    /// <summary>The thread's POA rows, from both reads (the soft one the code used before item 7, and the strict one).</summary>
    private void ThreadShares(params DataversePrincipalAccess[] rows)
    {
        _accessGrant
            .Setup(g => g.GetPrincipalAccessAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        _accessGrant
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
    }

    /// <summary>A share-read fault: the soft read answers empty (as on a 429/5xx), the strict read throws.</summary>
    private void ThreadShareReadFaults(Guid threadId)
    {
        _accessGrant
            .Setup(g => g.GetPrincipalAccessAsync("sprk_communicationthread", threadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DataversePrincipalAccess>());
        _accessGrant
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", threadId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated 503 reading the thread's shares."));
    }

    private static DataversePrincipalAccess ShareRow(Guid user, int mask) => new(User(user), mask, DateTimeOffset.UtcNow);

    [Theory(DisplayName = "198 pass 2 item 7 must-fire: a systemuser row on the thread without the Read bit is not a participant")]
    [InlineData(0)] // inherited access: a zero-mask row
    [InlineData(2)] // Write without Read
    public async Task Item7_AThreadRowWithoutTheReadBit_IsNotAParticipant(int mask)
    {
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        ThreadShares(ShareRow(Other, mask));

        var participants = await BuildSut().GetParticipantSystemUserIdsAsync(ThreadId);

        participants.Should().Equal(new[] { Caller }, "only a row holding Read makes a participant");
    }

    [Fact(DisplayName = "198 pass 2 item 7 must-fire: a zero-mask row on the thread is not granted the message")]
    public async Task Item7_AZeroMaskThreadRow_IsNotGrantedTheMessage()
    {
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        ThreadShares(ShareRow(Other, 0));

        await BuildSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Other), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Caller), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory(DisplayName = "198 pass 2 item 7 must-not-fire: a Read share on the thread still makes a participant, and is granted")]
    [InlineData(1)]
    [InlineData(3)] // Read and Write
    public async Task Item7_AReadShareOnTheThread_StillCounts(int mask)
    {
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        ThreadShares(ShareRow(Other, mask));

        var participants = await BuildSut().GetParticipantSystemUserIdsAsync(ThreadId);
        await BuildSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        participants.Should().BeEquivalentTo(new[] { Caller, Other });
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Other), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(DisplayName = "198 pass 2 item 7 must-fire: a thread share-read fault fails the participant read, so the membership reconcile retries instead of removing the other participant")]
    public async Task Item7_AThreadShareReadFault_FailsTheParticipantRead()
    {
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        ThreadShareReadFaults(ThreadId);

        var read = () => BuildSut().GetParticipantSystemUserIdsAsync(ThreadId);

        await read.Should().ThrowAsync<Exception>("an unknown answer is not \"the owner alone\"");
    }

    [Fact(DisplayName = "198 pass 2 item 7 must-not-fire: a thread share-read fault on the grant still grants the owner only (fail closed, unchanged)")]
    public async Task Item7_AThreadShareReadFault_OnTheGrant_GrantsTheOwnerOnly()
    {
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        ThreadShareReadFaults(ThreadId);

        await BuildSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Caller), "ReadAccess", It.IsAny<CancellationToken>()),
            Times.Once);
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, User(Other), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(DisplayName = "198 pass 2 item 7 must-not-fire: a candidate thread whose share read faults is skipped, and a new thread is created (unchanged)")]
    public async Task Item7_FindOrCreate_ACandidateWhoseShareReadFaults_IsSkipped()
    {
        var candidate = Guid.NewGuid();
        SetupCandidates(ownerId: Caller, candidateThreadIds: new[] { candidate });
        ThreadShareReadFaults(candidate);
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
    }

    // ── uac-r2 198b: the pair lookup reuses only a STRICT 1:1 thread (fm 054a piece 2b's one rule,
    // DirectMessageRecipient.ReadStrictPairShapeAsync), and scans the owner's Direct threads newest-first in pages to a stated
    // bound, warning when the bound is hit (NFR-03: never a silent duplicate) ──────────────────────────────────────────────

    private const int PrivacyStateOpen = 100000000;
    private const int PrivacyStateNotOpen = 100000001;

    /// <summary>
    /// A Direct thread row as the strict-pair reader reads it: typed Direct, confirmed Open, owned by <paramref name="owner"/>,
    /// no typed regarding unless <paramref name="shape"/> adds one; and its POA rows (the strict read) are <paramref name="shares"/>.
    /// </summary>
    private void PairThread(Guid threadId, Guid owner, IReadOnlyList<DataversePrincipalAccess> shares, Action<Entity>? shape = null)
    {
        var thread = new Entity("sprk_communicationthread")
        {
            Id = threadId,
            ["sprk_threadtype"] = new OptionSetValue(ThreadTypeDirect),
            ["sprk_privacystate"] = new OptionSetValue(PrivacyStateOpen),
            ["ownerid"] = new EntityReference("systemuser", owner),
        };
        shape?.Invoke(thread);
        _entityService
            .Setup(s => s.RetrieveAsync("sprk_communicationthread", threadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(thread);
        _accessGrant
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", threadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(shares);
    }

    /// <summary>
    /// The Direct threads <paramref name="ownerId"/> owns, newest first, answered as Dataverse pages a query: by
    /// <see cref="PagingInfo"/> (page number and size, MoreRecords, a cookie) or, for a query that sets only a top count, its
    /// first <see cref="QueryExpression.TopCount"/> rows. The queries are kept for the order assertion.
    /// </summary>
    private List<QueryExpression> CandidatePages(Guid ownerId, IReadOnlyList<Guid> newestFirst)
    {
        var queries = new List<QueryExpression>();
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => HasOwnerCondition(q, ownerId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                queries.Add(q);
                IEnumerable<Guid> rows;
                var more = false;
                if (q.TopCount is { } top)
                {
                    rows = newestFirst.Take(top);
                }
                else
                {
                    var size = q.PageInfo?.Count is > 0 ? q.PageInfo.Count : 5000;
                    var page = q.PageInfo?.PageNumber is > 0 ? q.PageInfo.PageNumber : 1;
                    rows = newestFirst.Skip((page - 1) * size).Take(size);
                    more = newestFirst.Count > page * size;
                }

                return new EntityCollection(rows.Select(id => new Entity("sprk_communicationthread") { Id = id }).ToList())
                {
                    MoreRecords = more,
                    PagingCookie = more ? $"cookie-{Guid.NewGuid():N}" : null,
                };
            });
        return queries;
    }

    private Guid CreatesANewThread()
    {
        var newThreadId = Guid.NewGuid();
        _entityService
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newThreadId);
        return newThreadId;
    }

    [Theory(DisplayName = "198b must-fire: a candidate shared to the other user AND a third user is not reused; a new thread is created")]
    [InlineData(0, DirectPairShapeReason.ExtraPrincipal)]           // the third user's row grants no Read: an extra principal
    [InlineData(1, DirectPairShapeReason.NotTwoReadParticipants)]   // the third user reads it too: three readers
    public async Task F198b_ACandidateWithAThirdPrincipal_IsNotReused(int thirdUserMask, string reason)
    {
        var candidate = Guid.NewGuid();
        CandidatePages(Caller, new[] { candidate });
        CandidatePages(Other, Array.Empty<Guid>());
        PairThread(candidate, Caller, new[] { ShareRow(Other, 1), ShareRow(ThirdUser, thirdUserMask) });
        var newThreadId = CreatesANewThread();
        var log = new CapturingLogger<DirectThreadAccessService>();

        var result = await DirectTeamSut(logger: log).FindOrCreateDirectThreadAsync(Caller, Other);

        result.Should().Be(newThreadId, "a thread a third user reads is not the pair's 1:1 thread");
        _entityService.Verify(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
        log.Entries.Should().Contain(e => e.Level == LogLevel.Debug
            && e.Message.Contains(candidate.ToString(), StringComparison.OrdinalIgnoreCase)
            && e.Message.Contains(reason, StringComparison.Ordinal));
    }

    [Theory(DisplayName = "198b must-fire: a candidate with a typed regarding, or not confirmed Open, is not reused")]
    [InlineData("typed_regarding")]
    [InlineData("not_open")]
    [InlineData("no_privacy")]
    public async Task F198b_ACandidateThatIsNotAStrictDirectShape_IsNotReused(string variant)
    {
        var candidate = Guid.NewGuid();
        CandidatePages(Caller, new[] { candidate });
        CandidatePages(Other, Array.Empty<Guid>());
        PairThread(candidate, Caller, Shares(Other), thread =>
        {
            switch (variant)
            {
                case "typed_regarding":
                    thread[ThreadResolver.ThreadRegardingColumns[0]] = new EntityReference("sprk_matter", MatterA);
                    break;
                case "not_open":
                    thread["sprk_privacystate"] = new OptionSetValue(PrivacyStateNotOpen);
                    break;
                default:
                    thread.Attributes.Remove("sprk_privacystate");
                    break;
            }
        });
        var newThreadId = CreatesANewThread();

        var result = await BuildSut().FindOrCreateDirectThreadAsync(Caller, Other);

        result.Should().Be(newThreadId);
        _entityService.Verify(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory(DisplayName = "198b must-not-fire: a strict-pair candidate is reused in both directions, past a non-strict one")]
    [InlineData(true)]  // owner = caller, shared to the other user
    [InlineData(false)] // owner = the other user, shared to the caller
    public async Task F198b_AStrictPairCandidate_IsReused_InBothDirections(bool ownedByCaller)
    {
        var owner = ownedByCaller ? Caller : Other;
        var sharee = ownedByCaller ? Other : Caller;
        var widened = Guid.NewGuid();
        var strict = Guid.NewGuid();
        CandidatePages(owner, new[] { widened, strict });
        CandidatePages(sharee, Array.Empty<Guid>());
        PairThread(widened, owner, new[] { ShareRow(sharee, 1), new DataversePrincipalAccess(DataversePrincipalRef.Team(TeamId), 1, DateTimeOffset.UtcNow) });
        PairThread(strict, owner, Shares(sharee));

        var result = await BuildSut().FindOrCreateDirectThreadAsync(Caller, Other);

        result.Should().Be(strict);
        _entityService.Verify(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory(DisplayName = "198b must-fire: a candidate whose read faults is skipped with a Warning, never a match and never a throw")]
    [InlineData(true)]  // the thread's shares cannot be read
    [InlineData(false)] // the thread row itself cannot be read
    public async Task F198b_ACandidateWhoseReadFaults_IsSkipped_WithAWarning(bool shareReadFaults)
    {
        var candidate = Guid.NewGuid();
        CandidatePages(Caller, new[] { candidate });
        CandidatePages(Other, Array.Empty<Guid>());
        PairThread(candidate, Caller, Shares(Other));
        if (shareReadFaults)
        {
            ThreadShareReadFaults(candidate);
        }
        else
        {
            _entityService
                .Setup(s => s.RetrieveAsync("sprk_communicationthread", candidate, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Simulated 503 reading the thread."));
        }

        var newThreadId = CreatesANewThread();
        var log = new CapturingLogger<DirectThreadAccessService>();

        var act = () => DirectTeamSut(logger: log).FindOrCreateDirectThreadAsync(Caller, Other);

        (await act.Should().NotThrowAsync()).Subject.Should().Be(newThreadId);
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning
            && e.Message.Contains(candidate.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "198b must-fire: the pair's thread past the old 50-candidate window (the 60th newest) is found, scanning newest-first")]
    public async Task F198b_ThePairsThreadPastTheOld50Window_IsFound()
    {
        var newestFirst = Enumerable.Range(0, 70).Select(_ => Guid.NewGuid()).ToList();
        var match = newestFirst[59];
        var queries = CandidatePages(Caller, newestFirst);
        CandidatePages(Other, Array.Empty<Guid>());
        foreach (var other in newestFirst.Where(id => id != match))
            PairThread(other, Caller, Shares(ThirdUser)); // strict pairs with someone else
        PairThread(match, Caller, Shares(Other));

        var result = await BuildSut().FindOrCreateDirectThreadAsync(Caller, Other);

        result.Should().Be(match, "an owner with more than 50 Direct threads must not get a duplicate thread");
        _entityService.Verify(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
        queries.Should().OnlyContain(q => q.Orders.Count > 0
            && q.Orders[0].AttributeName == "createdon" && q.Orders[0].OrderType == OrderType.Descending);
    }

    [Theory(DisplayName = "198b must-fire/must-not-fire: hitting the scan bound with no match logs a Warning naming the bound; exactly the bound does not")]
    [InlineData(520, true)]
    [InlineData(500, false)]
    public async Task F198b_TheScanBound_IsVisible(int ownedThreads, bool boundHit)
    {
        var newestFirst = Enumerable.Range(0, ownedThreads).Select(_ => Guid.NewGuid()).ToList();
        CandidatePages(Caller, newestFirst);
        CandidatePages(Other, Array.Empty<Guid>());
        foreach (var id in newestFirst)
            PairThread(id, Caller, Shares(ThirdUser));
        var newThreadId = CreatesANewThread();
        var log = new CapturingLogger<DirectThreadAccessService>();

        var result = await DirectTeamSut(logger: log).FindOrCreateDirectThreadAsync(Caller, Other);

        result.Should().Be(newThreadId);
        var boundWarnings = log.Entries.Where(e => e.Level == LogLevel.Warning
            && e.Message.Contains("500", StringComparison.Ordinal)
            && e.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)).ToList();
        if (boundHit)
            boundWarnings.Should().ContainSingle("the cap is visible (NFR-03)");
        else
            boundWarnings.Should().BeEmpty("every thread was read; nothing was left unscanned");
        _entityService.Verify(
            s => s.RetrieveAsync("sprk_communicationthread", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Exactly(500), "the scan reads at most the bound's candidates");
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
        gateDecisionRecords,
        DirectMessageOwnerTeamDouble.Unconfigured(),
        new UserFlagDirectoryDouble(),
        HostColumnProbe());

    /// <summary>
    /// uac-r2 task 198 R3: the core-ancestor resolver the host always injects (its column probe is the Restricted bar's filing
    /// walk). The messages here carry no filing lookup and no Restricted value, so the bar finds them not Restricted and the
    /// record-anchored grant is exactly as before (no user is read).
    /// </summary>
    private Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver HostColumnProbe() =>
        new(_entityService.Object, Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.ColumnProbe(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver>.Instance);

    [Fact]
    public async Task GrantMessageAccessAsync_AUserOwnedMessage_MirrorsTheGrantToItsGateDecisionRecord_D169()
    {
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        _accessGrant
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
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
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
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
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
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
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
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
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
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
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
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

    // ── Task 198 Part B: a message owned by the Direct-message owner team (fm 054) ──────────────────────────
    // Its audience is exactly the join's two-party list, on the message and on the content rows the same team owns.
    // The Direct-team check is fm 054a's real lookup (DirectMessageOwnerTeam) over an in-memory directory.

    private static readonly Guid DirectTeamId = Guid.Parse("d1d1d1d1-0000-0000-0000-0000000000d1");
    private static readonly Guid OtherTeamId = Guid.Parse("d2d2d2d2-0000-0000-0000-0000000000d2");
    private static readonly Guid AttachmentRowId = Guid.Parse("a1a1a1a1-0000-0000-0000-0000000000a1");
    private static readonly Guid ParticipantRowId = Guid.Parse("a2a2a2a2-0000-0000-0000-0000000000a2");
    private static readonly Guid DocumentRowId = Guid.Parse("a3a3a3a3-0000-0000-0000-0000000000a3");
    private static readonly Guid OtherTeamAttachmentRowId = Guid.Parse("a4a4a4a4-0000-0000-0000-0000000000a4");
    private static readonly Guid ReviewLogRowId = Guid.Parse("a5a5a5a5-0000-0000-0000-0000000000a5");

    /// <summary>The Direct unit with its Direct team (<see cref="DirectTeamId"/>) and one other, differently named team.</summary>
    private static DirectMessageOwnerTeamDouble DirectDirectory() =>
        new DirectMessageOwnerTeamDouble()
            .WithDirectTeam(DirectTeamId)
            .Team(OtherTeamId, "Some Owner Team", DirectMessageOwnerTeamDouble.RootUnitId);

    /// <summary>
    /// A SUT over the real Direct-team lookup, configured, reading <paramref name="directory"/>. The participants are read through
    /// <paramref name="users"/> (Part C: the Restricted bar on the grant); by default every test user is internal.
    /// </summary>
    private DirectThreadAccessService DirectTeamSut(
        DirectMessageOwnerTeamDouble? directory = null, ILogger<DirectThreadAccessService>? logger = null,
        UserFlagDirectoryDouble? users = null, Sprk.Bff.Api.Services.Signals.GateDecisionRecordSharing? gate = null) => new(
        _entityService.Object,
        _accessGrant.Object,
        new Lazy<IThreadMembershipDerivationService>(() => _membershipDerivation.Object),
        new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
        logger ?? Mock.Of<ILogger<DirectThreadAccessService>>(),
        gate ?? new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing,
        (directory ?? DirectDirectory()).Lookup(),
        users ?? new UserFlagDirectoryDouble().Internal(Caller, Other, ThirdUser));

    /// <summary>Task 198 R2: the message's stamped sender and recipient (<c>null</c>: not stamped). Read when the message is.</summary>
    private Guid? _stampedSender = Caller;
    private Guid? _stampedRecipient = Other;

    /// <summary>The message row owned by <paramref name=teamId/>, with the stamped pair, projected to the query's select as
    /// Dataverse answers (a column not selected is absent).</summary>
    private Entity MessageRow(QueryExpression query, Guid teamId)
    {
        var row = new Entity("sprk_communication") { Id = CommunicationId, ["owningteam"] = new EntityReference("team", teamId) };
        if (_stampedSender is { } sender)
            row["sprk_sentby"] = new EntityReference("systemuser", sender);
        if (_stampedRecipient is { } recipient)
            row["sprk_directrecipient"] = new EntityReference("systemuser", recipient);
        var projected = new Entity(row.LogicalName, row.Id);
        foreach (var column in row.Attributes.Keys.Where(c => query.ColumnSet.AllColumns || query.ColumnSet.Columns.Contains(c)))
            projected[column] = row[column];
        return projected;
    }

    /// <summary>The message is owned by a non-default Owner team that is not the Secure team (the Direct team's shape).</summary>
    private void MessageOwnedByOwnerTeam(Guid teamId)
    {
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communication"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => new EntityCollection(new List<Entity> { MessageRow(q, teamId) }));
        TeamsAndUnitsMatchedAsDataverse(
            new[] { (teamId, DirectMessageOwnerTeamDouble.RootUnitId, "Spaarke Direct Message Owners", false, 0) },
            Array.Empty<(Guid, string)>());
    }

    /// <summary>The content rows of <paramref name="table"/>: the read answers with the rows whose owner team and message
    /// lookup match the query's conditions, as Dataverse would.</summary>
    private void ContentRows(string table, string messageLookup, params (Guid Id, Guid OwnerTeam)[] rows)
    {
        foreach (var row in rows)
            _contentOwners[row.Id] = row.OwnerTeam;
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == table), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                object? Value(string attribute) =>
                    q.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == attribute && c.Operator == ConditionOperator.Equal)?.Values.Single();

                // 198 pass 2 (F1): a read of one row by its id answers its CURRENT owner (the owner read-back after a grant).
                if (Value(table + "id") is Guid byId)
                {
                    return new EntityCollection(rows.Where(r => r.Id == byId)
                        .Select(r => new Entity(table) { Id = r.Id, ["owningteam"] = new EntityReference("team", _contentOwners[r.Id]) })
                        .ToList());
                }

                return new EntityCollection(rows
                    .Where(r => Equals(Value("owningteam"), _contentOwners[r.Id]) && Equals(Value(messageLookup), CommunicationId))
                    .Select(r => new Entity(table) { Id = r.Id })
                    .ToList());
            });
    }

    /// <summary>198 pass 2 (F1): each content row's current owner team (a test re-owns one mid-grant).</summary>
    private readonly Dictionary<Guid, Guid> _contentOwners = new();

    /// <summary>A Direct thread owned by <see cref="Caller"/> and shared to <see cref="Other"/>; a Direct-team-owned
    /// message with one content row per table owned by the Direct team, and one attachment owned by another team.</summary>
    private void DirectTeamMessageWorld()
    {
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        // Fix round 1: the Direct-team grant derives its participants through the one seam, which reads the shares strictly
        // (198 pass 2 item 7: so does the thread's participant read).
        _accessGrant
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(Other));
        MessageOwnedByOwnerTeam(DirectTeamId);
        ContentRows("sprk_communicationattachment", "sprk_communication",
            (AttachmentRowId, DirectTeamId), (OtherTeamAttachmentRowId, OtherTeamId));
        ContentRows("sprk_communicationparticipant", "sprk_communication", (ParticipantRowId, DirectTeamId));
        ContentRows("sprk_emailreviewlog", "sprk_communication", (ReviewLogRowId, DirectTeamId));
        ContentRows("sprk_document", "sprk_relatedcommunication", (DocumentRowId, DirectTeamId));
        // 198 pass 2 (F1): the content rows' own shares (none unless a test says otherwise), read strictly before a grant.
        foreach (var table in new[] { "sprk_communicationattachment", "sprk_communicationparticipant", "sprk_emailreviewlog", "sprk_document" })
        {
            _accessGrant
                .Setup(g => g.GetPrincipalAccessOrThrowAsync(table, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Shares());
        }
    }

    private void VerifyGrant(string entitySet, Guid rowId, Guid user) =>
        _accessGrant.Verify(
            g => g.GrantAccessAsync(entitySet, rowId, User(user), "ReadAccess", It.IsAny<CancellationToken>()), Times.Once);

    private void VerifyNoGrantAtAll() =>
        _accessGrant.Verify(
            g => g.GrantAccessAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DataversePrincipalRef>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

    [Fact]
    public async Task DirectTeamMessage_GrantsBothParticipants_TheSenderIncluded_OnTheMessage()
    {
        DirectTeamMessageWorld();

        await DirectTeamSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        VerifyGrant("sprk_communications", CommunicationId, Caller);
        VerifyGrant("sprk_communications", CommunicationId, Other);
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communications", CommunicationId, It.IsAny<DataversePrincipalRef>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task DirectTeamMessage_GrantsBothParticipants_OnTheContentRowsTheDirectTeamOwns()
    {
        DirectTeamMessageWorld();

        await DirectTeamSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        foreach (var user in new[] { Caller, Other })
        {
            VerifyGrant("sprk_communicationattachments", AttachmentRowId, user);
            VerifyGrant("sprk_communicationparticipants", ParticipantRowId, user);
            VerifyGrant("sprk_emailreviewlogs", ReviewLogRowId, user);
            VerifyGrant("sprk_documents", DocumentRowId, user);
        }

        // Gate Decision Records are Part E's (GateDecisionRecordSharing), never this grant's.
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_decisionrecords", It.IsAny<Guid>(), It.IsAny<DataversePrincipalRef>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DirectTeamMessage_TheGrantIsMirroredToTheMessagesGateDecisionRecords_D215()
    {
        // Task 198 Part E (D-215 rule 3): after the participant grant, the message's gate records (owned by the Direct team) are brought
        // in line with the participants who read it, through GateDecisionRecordSharing (its own world: the message, its pair and shares).
        DirectTeamMessageWorld();
        var gateWorld = new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld()
            .WithTeam(Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld.DirectTeam, DirectMessageOwnerTeamDouble.OwnerTeamName, isDefault: false)
            .WithCommunication(CommunicationId, Caller, owningTeam: Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld.DirectTeam)
            .WithPair(CommunicationId, Caller, Other)
            .WithUser(Caller).WithUser(Other)
            .Share(Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld.Communication, CommunicationId, Caller)
            .Share(Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld.Communication, CommunicationId, Other)
            .WithGateRecord(GateRecordId, CommunicationId, Caller);
        gateWorld.OwnByTeam(GateRecordId, Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld.DirectTeam);

        await DirectTeamSut(gate: gateWorld.Sharing).GrantMessageAccessAsync(CommunicationId, DirectJoin);

        gateWorld.RecordSharees(GateRecordId).Should().Equal(new[] { Caller, Other }.OrderBy(g => g));
    }

    [Fact]
    public async Task DirectTeamMessage_NeverGrantsANonParticipant_ATeam_OrARowAnotherTeamOwns()
    {
        DirectTeamMessageWorld();

        await DirectTeamSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        _accessGrant.Verify(
            g => g.GrantAccessAsync(It.IsAny<string>(), It.IsAny<Guid>(), User(ThirdUser), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _accessGrant.Verify(
            g => g.GrantAccessAsync(It.IsAny<string>(), It.IsAny<Guid>(),
                It.Is<DataversePrincipalRef>(p => p != User(Caller) && p != User(Other)), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _accessGrant.Verify(
            g => g.GrantAccessAsync(It.IsAny<string>(), OtherTeamAttachmentRowId, It.IsAny<DataversePrincipalRef>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // 2 participants x (message + 4 content rows); the anchor record's membership is never derived.
        _accessGrant.Verify(
            g => g.GrantAccessAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DataversePrincipalRef>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(10));
        _membershipDerivation.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task DirectTeamMessage_WithAJoinThatIsNotDirect_GrantsNothing()
    {
        DirectTeamMessageWorld();
        SetupGrantees(MatterA, ThirdUser);

        await DirectTeamSut().GrantMessageAccessAsync(CommunicationId, AnchoredJoin(MatterA));

        VerifyNoGrantAtAll();
        _membershipDerivation.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task DirectTeamMessage_WhenTheDirectTeamIsRefused_GrantsNothing()
    {
        // A user homed in the Direct unit: the lookup refuses (bu_has_users), so the team cannot be told.
        DirectTeamMessageWorld();
        var directory = DirectDirectory().WithUserInDirectUnit(ThirdUser);

        await DirectTeamSut(directory).GrantMessageAccessAsync(CommunicationId, DirectJoin);

        VerifyNoGrantAtAll();
    }

    [Fact]
    public async Task DirectTeamMessage_WhenTheLookupFaults_GrantsNothing()
    {
        DirectTeamMessageWorld();
        var directory = DirectDirectory();
        directory.Faults = true;

        var act = () => DirectTeamSut(directory).GrantMessageAccessAsync(CommunicationId, DirectJoin);

        await act.Should().NotThrowAsync();
        VerifyNoGrantAtAll();
    }

    [Fact]
    public async Task DirectTeamMessage_WhenTheOneRowCheckFaults_IsUnknown_ReachesTheOneFailClosedBranch()
    {
        // Ruling Q5: a fault is unknown, never "not Direct", and every unknown takes the same branch (nothing granted; the
        // re-grant hook of Part C), not the method's generic catch.
        DirectTeamMessageWorld();
        var directory = DirectDirectory();
        directory.Faults = true;
        var log = new CapturingLogger<DirectThreadAccessService>();

        await DirectTeamSut(directory, log).GrantMessageAccessAsync(CommunicationId, DirectJoin);

        VerifyNoGrantAtAll();
        log.Entries.Should().Contain(e => e.Message.Contains("cannot be determined", StringComparison.Ordinal));
        log.Entries.Should().NotContain(e => e.Message.Contains("lookup failed (non-fatal)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DirectTeamMessage_WhenOneParticipantsContentGrantThrows_StillGrantsTheRest()
    {
        DirectTeamMessageWorld();
        _accessGrant
            .Setup(g => g.GrantAccessAsync("sprk_communicationattachments", AttachmentRowId, User(Caller), "ReadAccess", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("grant boom"));

        var act = () => DirectTeamSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        await act.Should().NotThrowAsync();
        VerifyGrant("sprk_communicationattachments", AttachmentRowId, Other);
        VerifyGrant("sprk_documents", DocumentRowId, Caller);
    }

    [Fact(DisplayName = "198 pass 2 F1 must-fire (live): a content row re-owned during its grant has THIS call's grant revoked; a share held before is kept")]
    public async Task F1_Live_AContentRowReownedDuringItsGrant_HasThisCallsGrantRevoked_AndAPriorShareKept()
    {
        DirectTeamMessageWorld();
        // Caller already reads the attachment (e.g. an earlier run); only Other is granted here.
        _accessGrant
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationattachment", AttachmentRowId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(Caller));
        // A filing into a Secure record lands between the listing and the grant's read-back: the row now has another owner.
        _accessGrant
            .Setup(g => g.GrantAccessAsync("sprk_communicationattachments", AttachmentRowId, It.IsAny<DataversePrincipalRef>(), "ReadAccess", It.IsAny<CancellationToken>()))
            .Callback(() => _contentOwners[AttachmentRowId] = OtherTeamId)
            .Returns(Task.CompletedTask);
        var log = new CapturingLogger<DirectThreadAccessService>();

        await DirectTeamSut(logger: log).GrantMessageAccessAsync(CommunicationId, DirectJoin);

        _accessGrant.Verify(
            g => g.RevokeAccessAsync("sprk_communicationattachments", AttachmentRowId, User(Other), It.IsAny<CancellationToken>()), Times.Once);
        _accessGrant.Verify(
            g => g.RevokeAccessAsync(It.IsAny<string>(), It.IsAny<Guid>(), User(Caller), It.IsAny<CancellationToken>()), Times.Never,
            "a share Caller held before this call is not this call's to revoke");
        _accessGrant.Verify(
            g => g.GrantAccessAsync("sprk_communicationattachments", AttachmentRowId, User(Caller), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "Caller already reads it");
        VerifyGrant("sprk_documents", DocumentRowId, Caller);
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("retries", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "198 pass 2 F1 must-not-fire (live): content rows whose owner is unchanged are granted and nothing is revoked")]
    public async Task F1_Live_AnUnchangedContentRowOwner_IsGranted_AndNothingIsRevoked()
    {
        DirectTeamMessageWorld();

        await DirectTeamSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        foreach (var user in new[] { Caller, Other })
        {
            VerifyGrant("sprk_communicationattachments", AttachmentRowId, user);
            VerifyGrant("sprk_documents", DocumentRowId, user);
        }

        _accessGrant.Verify(
            g => g.RevokeAccessAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DataversePrincipalRef>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(DisplayName = "198 C must-not-fire: an external participant is not granted when whether the message is Restricted cannot be decided")]
    public async Task DirectTeamMessage_AnExternalParticipant_WhenRestrictedIsUndecidable_IsNotGranted()
    {
        // No column probe here, so the message's Access Permission cannot be read: an external-flagged participant is barred
        // (rootIsRestricted null), the internal one is granted.
        DirectTeamMessageWorld();
        var users = new UserFlagDirectoryDouble().Internal(Caller);
        users.Flags[Other] = true;

        await DirectTeamSut(users: users).GrantMessageAccessAsync(CommunicationId, DirectJoin);

        VerifyGrant("sprk_communications", CommunicationId, Caller);
        _accessGrant.Verify(
            g => g.GrantAccessAsync(It.IsAny<string>(), It.IsAny<Guid>(), User(Other), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(DisplayName = "198 C must-not-fire: when the participants' user flags cannot be read, nobody is granted")]
    public async Task DirectTeamMessage_WhenTheUserFlagsCannotBeRead_GrantsNothing()
    {
        DirectTeamMessageWorld();
        var users = new UserFlagDirectoryDouble().Internal(Caller, Other);
        users.Faults = true;

        var act = () => DirectTeamSut(users: users).GrantMessageAccessAsync(CommunicationId, DirectJoin);

        await act.Should().NotThrowAsync();
        VerifyNoGrantAtAll();
    }

    // ── Task 198 fix round 1, R1: a message whose owner is not PROVEN Direct behaves exactly as before task 198 ──

    [Fact(DisplayName = "198 R1: a default-team message keeps its anchor grant when the Direct lookup faults (decided by the team row)")]
    public async Task ADefaultTeamMessage_WhenTheDirectLookupFaults_StillGrantsTheAnchorGrantees()
    {
        MessageOwnedByOrdinaryTeam(OtherTeamId);
        SetupGrantees(MatterA, Other);
        var directory = DirectDirectory();
        directory.Faults = true;

        await DirectTeamSut(directory).GrantMessageAccessAsync(CommunicationId, AnchoredJoin(MatterA));

        VerifyGrant("sprk_communications", CommunicationId, Other);
    }

    [Fact(DisplayName = "198 R1: a non-default owner team's message keeps its anchor grant when the Direct lookup faults")]
    public async Task AnOwnerTeamMessage_WhenTheDirectLookupFaults_StillGrantsTheAnchorGrantees()
    {
        MessageOwnedByOwnerTeam(OtherTeamId);
        SetupGrantees(MatterA, Other);
        var directory = DirectDirectory();
        directory.Faults = true;

        await DirectTeamSut(directory).GrantMessageAccessAsync(CommunicationId, AnchoredJoin(MatterA));

        VerifyGrant("sprk_communications", CommunicationId, Other);
    }

    [Fact(DisplayName = "198 R1: a non-default owner team's message keeps its anchor grant when the Direct lookup refuses")]
    public async Task AnOwnerTeamMessage_WhenTheDirectLookupRefuses_StillGrantsTheAnchorGrantees()
    {
        // The configured Direct team does not exist: the lookup cannot identify it and refuses, so the owner is not proven Direct.
        MessageOwnedByOwnerTeam(OtherTeamId);
        SetupGrantees(MatterA, Other);

        await DirectTeamSut(new DirectMessageOwnerTeamDouble()).GrantMessageAccessAsync(CommunicationId, AnchoredJoin(MatterA));

        VerifyGrant("sprk_communications", CommunicationId, Other);
    }

    [Fact(DisplayName = "198 R5: a Direct message whose owner changes before the grant is not granted")]
    public async Task DirectTeamMessage_WhoseOwnerChangesBeforeTheGrant_GrantsNothing()
    {
        DirectTeamMessageWorld();
        var reads = 0;
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communication"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => new EntityCollection(new List<Entity>
            {
                // R2: the pair is stamped, so only the owner change (read again right before the grant) stops it.
                MessageRow(q, reads++ == 0 ? DirectTeamId : OtherTeamId),
            }));

        await DirectTeamSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        VerifyNoGrantAtAll();
    }

    // ── Task 198 fix round 1, R2: the live grant derives the pair from the message's stamped columns, as the job does ──

    [Fact(DisplayName = "198 R2 must-fire: the live grant grants the stamped pair, not the thread's sharee")]
    public async Task R2_Live_GrantsTheStampedPair_NotTheThreadsSharee()
    {
        // The thread is owned by Caller and shared to Other; the message's pair is Caller -> ThirdUser.
        DirectTeamMessageWorld();
        _stampedRecipient = ThirdUser;

        await DirectTeamSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        VerifyGrant("sprk_communications", CommunicationId, Caller);
        VerifyGrant("sprk_communications", CommunicationId, ThirdUser);
        VerifyGrant("sprk_documents", DocumentRowId, ThirdUser);
        _accessGrant.Verify(
            g => g.GrantAccessAsync(It.IsAny<string>(), It.IsAny<Guid>(), User(Other), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(DisplayName = "198 R2 must-fire: the live grant reads the sender and the recipient by name")]
    public async Task R2_Live_TheMessageRead_SelectsThePair()
    {
        DirectTeamMessageWorld();

        await DirectTeamSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        _entityService.Invocations
            .Where(i => i.Method.Name == nameof(IGenericEntityService.RetrieveMultipleAsync))
            .Select(i => (QueryExpression)i.Arguments[0])
            .Should().Contain(q => q.EntityName == "sprk_communication"
                && q.ColumnSet.Columns.Contains("sprk_sentby") && q.ColumnSet.Columns.Contains("sprk_directrecipient"));
    }

    [Fact(DisplayName = "198 R2 must-fire: no recipient stamped, the live grant grants nobody")]
    public async Task R2_Live_NoRecipient_GrantsNothing()
    {
        DirectTeamMessageWorld();
        _stampedRecipient = null;

        await DirectTeamSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        VerifyNoGrantAtAll();
    }

    [Fact(DisplayName = "198 R2 must-fire: sender and recipient the same user, the live grant grants nobody")]
    public async Task R2_Live_SenderIsRecipient_GrantsNothing()
    {
        DirectTeamMessageWorld();
        _stampedRecipient = Caller;

        await DirectTeamSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        VerifyNoGrantAtAll();
    }

    [Fact(DisplayName = "198 R2 must-fire: a disabled recipient is not granted live; the sender is")]
    public async Task R2_Live_ADisabledRecipient_IsNotGranted_TheSenderIs()
    {
        DirectTeamMessageWorld();
        var users = new UserFlagDirectoryDouble().Internal(Caller, Other, ThirdUser);
        users.Disabled[Other] = true;

        await DirectTeamSut(users: users).GrantMessageAccessAsync(CommunicationId, DirectJoin);

        VerifyGrant("sprk_communications", CommunicationId, Caller);
        VerifyGrant("sprk_communicationattachments", AttachmentRowId, Caller);
        _accessGrant.Verify(
            g => g.GrantAccessAsync(It.IsAny<string>(), It.IsAny<Guid>(), User(Other), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(DisplayName = "198 R2 must-fire: the message read for the pair faults, the live grant grants nobody")]
    public async Task R2_Live_WhenThePairReadFaults_GrantsNothing()
    {
        DirectTeamMessageWorld();
        var reads = 0;
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communication"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => reads++ == 0
                ? new EntityCollection(new List<Entity> { MessageRow(q, DirectTeamId) })
                : throw new InvalidOperationException("Test: the message could not be read."));

        var act = () => DirectTeamSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        await act.Should().NotThrowAsync();
        VerifyNoGrantAtAll();
    }

    [Fact]
    public async Task AnotherTeamOwnedMessage_KeepsTheOrdinaryGrant_AndReadsNoContentRow()
    {
        // A team that is not the Direct team: the message grant is unchanged (the join's participants), no content row is read.
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        _accessGrant
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(Other));
        MessageOwnedByOwnerTeam(OtherTeamId);

        await DirectTeamSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        VerifyGrant("sprk_communications", CommunicationId, Caller);
        VerifyGrant("sprk_communications", CommunicationId, Other);
        _entityService.Verify(
            s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationattachment"), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ALookalikeOfTheDirectTeamInAnotherUnit_IsNotTheDirectTeam_AndReadsNoContentRow()
    {
        // A non-default Owner team carrying the Direct team's name, outside the Direct unit: the full resolution names the
        // Direct team, which is another team, so the message keeps the ordinary grant.
        var lookalike = Guid.Parse("d3d3d3d3-0000-0000-0000-0000000000d3");
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        _accessGrant
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(Other));
        MessageOwnedByOwnerTeam(lookalike);
        var directory = DirectDirectory().Team(lookalike, DirectMessageOwnerTeamDouble.OwnerTeamName, DirectMessageOwnerTeamDouble.RootUnitId);

        await DirectTeamSut(directory).GrantMessageAccessAsync(CommunicationId, DirectJoin);

        VerifyGrant("sprk_communications", CommunicationId, Caller);
        VerifyGrant("sprk_communications", CommunicationId, Other);
        _entityService.Verify(
            s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationattachment"), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AUserOwnedMessage_NeverAsksTheDirectTeamCheck()
    {
        // The lookup faults on any read: a user-owned message never reaches it, so its grant is unchanged.
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        _accessGrant
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(Other));
        var directory = DirectDirectory();
        directory.Faults = true;

        await DirectTeamSut(directory).GrantMessageAccessAsync(CommunicationId, DirectJoin);

        VerifyGrant("sprk_communications", CommunicationId, Caller);
        VerifyGrant("sprk_communications", CommunicationId, Other);
    }

    [Fact]
    public async Task ASecureMessage_GrantsNothing()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var secureTeam = Guid.Parse("5e5e5e5e-0000-0000-0000-00000000005e");
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communication"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                new("sprk_communication") { Id = CommunicationId, ["owningteam"] = new EntityReference("team", secureTeam) },
            }));
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "team" && q.Criteria.Conditions.Any(c => c.AttributeName == "teamid")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                new("team")
                {
                    Id = secureTeam,
                    ["name"] = Sprk.Bff.Api.Infrastructure.Dataverse.SecureRecordOwnerTeam.OwnerTeamName(config),
                    ["isdefault"] = false,
                    ["teamtype"] = new OptionSetValue(Sprk.Bff.Api.Infrastructure.Dataverse.SecureRecordOwnerTeam.OwnerTeamType),
                },
            }));
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "businessunit"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { new("businessunit") { Id = Guid.NewGuid() } }));
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "team" && q.Criteria.Conditions.Any(c => c.AttributeName == "businessunitid")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { new("team") { Id = secureTeam } }));
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);

        await DirectTeamSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        VerifyNoGrantAtAll();
    }

    [Fact]
    public async Task ASecureMessage_WithAnAccentVariantOfTheSecureTeamSetting_GrantsNothing()
    {
        // Dataverse matches a team name without regard to case or accents, so a setting that differs from the Secure team's
        // name only by an accent still names that team: the message is secure, and no participant is granted.
        var secureTeam = Guid.Parse("5e5e5e5e-0000-0000-0000-0000000001e5");
        var secureBu = Guid.Parse("5b5b5b5b-0000-0000-0000-0000000001e5");
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [Sprk.Bff.Api.Infrastructure.Dataverse.SecureRecordOwnerTeam.OwnerTeamNameConfigKey] = "Secure Record Ówners",
            })
            .Build();
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communication"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                new("sprk_communication") { Id = CommunicationId, ["owningteam"] = new EntityReference("team", secureTeam) },
            }));
        TeamsAndUnitsMatchedAsDataverse(
            new[] { (secureTeam, secureBu, "Secure Record Owners", false, 0) },
            new[] { (secureBu, "Secure Record") });
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        _accessGrant
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(Other));
        var sut = new DirectThreadAccessService(
            _entityService.Object,
            _accessGrant.Object,
            new Lazy<IThreadMembershipDerivationService>(() => _membershipDerivation.Object),
            config,
            Mock.Of<ILogger<DirectThreadAccessService>>(),
            new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing,
            DirectMessageOwnerTeamDouble.Unconfigured());

        await sut.GrantMessageAccessAsync(CommunicationId, DirectJoin);

        VerifyNoGrantAtAll();
    }

    [Theory]
    [InlineData(null, "Secure Record Readers")]
    [InlineData("Secure Records", null)]
    public async Task ASecureMessage_WithSecureSettingsThatFindNothing_GrantsNothing(string? buSetting, string? teamSetting)
    {
        // The settings are present but name a unit or team Dataverse does not find: whether the owner is the Secure team
        // cannot be told, so no participant is granted (fail closed), even though the owner IS the real Secure team.
        var secureTeam = Guid.Parse("5e5e5e5e-0000-0000-0000-0000000002e5");
        var secureBu = Guid.Parse("5b5b5b5b-0000-0000-0000-0000000002e5");
        var settings = new Dictionary<string, string?>();
        if (buSetting is not null)
            settings[Sprk.Bff.Api.Infrastructure.Dataverse.SecureRecordOwnerTeam.BusinessUnitNameConfigKey] = buSetting;
        if (teamSetting is not null)
            settings[Sprk.Bff.Api.Infrastructure.Dataverse.SecureRecordOwnerTeam.OwnerTeamNameConfigKey] = teamSetting;
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communication"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                new("sprk_communication") { Id = CommunicationId, ["owningteam"] = new EntityReference("team", secureTeam) },
            }));
        TeamsAndUnitsMatchedAsDataverse(
            new[] { (secureTeam, secureBu, "Secure Record Owners", false, 0) },
            new[] { (secureBu, "Secure Record") });
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        _accessGrant
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(Other));
        var sut = new DirectThreadAccessService(
            _entityService.Object,
            _accessGrant.Object,
            new Lazy<IThreadMembershipDerivationService>(() => _membershipDerivation.Object),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            Mock.Of<ILogger<DirectThreadAccessService>>(),
            new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing,
            DirectMessageOwnerTeamDouble.Unconfigured());

        await sut.GrantMessageAccessAsync(CommunicationId, DirectJoin);

        VerifyNoGrantAtAll();
    }

    [Fact]
    public async Task AnOrdinaryTeamMessage_WithCorrectSecureSettings_KeepsTheParticipantGrant()
    {
        // Must not fire: the settings find the Secure team, and the message's owner is another (ordinary Owner) team.
        var secureTeam = Guid.Parse("5e5e5e5e-0000-0000-0000-0000000003e5");
        var secureBu = Guid.Parse("5b5b5b5b-0000-0000-0000-0000000003e5");
        var ordinaryTeam = Guid.Parse("9d9d9d9d-0000-0000-0000-0000000003e5");
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communication"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                new("sprk_communication") { Id = CommunicationId, ["owningteam"] = new EntityReference("team", ordinaryTeam) },
            }));
        TeamsAndUnitsMatchedAsDataverse(
            new[] { (secureTeam, secureBu, "Secure Record Owners", false, 0), (ordinaryTeam, Guid.NewGuid(), "Matter Team", false, 0) },
            new[] { (secureBu, "Secure Record") });
        SetupThread(ThreadId, ThreadTypeDirect, ownerId: Caller);
        _accessGrant
            .Setup(g => g.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Shares(Other));
        var sut = new DirectThreadAccessService(
            _entityService.Object,
            _accessGrant.Object,
            new Lazy<IThreadMembershipDerivationService>(() => _membershipDerivation.Object),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                [Sprk.Bff.Api.Infrastructure.Dataverse.SecureRecordOwnerTeam.BusinessUnitNameConfigKey] = "Secure Record",
                [Sprk.Bff.Api.Infrastructure.Dataverse.SecureRecordOwnerTeam.OwnerTeamNameConfigKey] = "Secure Record Owners",
            }).Build(),
            Mock.Of<ILogger<DirectThreadAccessService>>(),
            new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing,
            DirectMessageOwnerTeamDouble.Unconfigured());

        await sut.GrantMessageAccessAsync(CommunicationId, DirectJoin);

        VerifyGrant("sprk_communications", CommunicationId, Caller);
        VerifyGrant("sprk_communications", CommunicationId, Other);
    }

    /// <summary>
    /// The <c>team</c> and <c>businessunit</c> reads answered by EVALUATING their conditions over the given rows, with names
    /// compared as Dataverse's default collation compares them (case- and accent-insensitive), and only the selected columns
    /// returned.
    /// </summary>
    private void TeamsAndUnitsMatchedAsDataverse(
        (Guid Id, Guid BusinessUnit, string Name, bool IsDefault, int TeamType)[] teams,
        (Guid Id, string Name)[] businessUnits)
    {
        static bool SameName(string a, string b) => System.Globalization.CultureInfo.InvariantCulture.CompareInfo.Compare(
            a, b, System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) == 0;

        static bool Matches(QueryExpression q, string attribute, object actual)
        {
            var condition = q.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == attribute);
            if (condition is null)
                return true;
            var expected = condition.Values.Single();
            return actual is string a && expected is string e ? SameName(a, e) : actual.Equals(expected);
        }

        static Entity Project(QueryExpression q, Entity row)
        {
            var projected = new Entity(row.LogicalName, row.Id);
            foreach (var column in q.ColumnSet.Columns.Where(row.Contains))
                projected[column] = row[column];
            return projected;
        }

        _entityService
            .Setup(s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "team"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => new EntityCollection(teams
                .Where(t => Matches(q, "teamid", t.Id) && Matches(q, "businessunitid", t.BusinessUnit)
                            && Matches(q, "name", t.Name) && Matches(q, "isdefault", t.IsDefault) && Matches(q, "teamtype", t.TeamType))
                .Select(t => Project(q, new Entity("team", t.Id)
                {
                    ["teamid"] = t.Id,
                    ["name"] = t.Name,
                    ["isdefault"] = t.IsDefault,
                    ["teamtype"] = new OptionSetValue(t.TeamType),
                }))
                .ToList()));
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "businessunit"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => new EntityCollection(businessUnits
                .Where(b => Matches(q, "businessunitid", b.Id) && Matches(q, "name", b.Name))
                .Select(b => new Entity("businessunit", b.Id))
                .ToList()));
    }

    [Fact]
    public async Task WithNoDirectTeamConfigured_ATeamOwnedMessage_KeepsTheOrdinaryGrant_AndReadsNoContentRow()
    {
        // No DirectMessage:* settings: no message can be assigned to a Direct team, so the team-owned grant is unchanged.
        DirectTeamMessageWorld();

        await BuildSut().GrantMessageAccessAsync(CommunicationId, DirectJoin);

        VerifyGrant("sprk_communications", CommunicationId, Caller);
        VerifyGrant("sprk_communications", CommunicationId, Other);
        _accessGrant.Verify(
            g => g.GrantAccessAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DataversePrincipalRef>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
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
        TeamsAndUnitsMatchedAsDataverse(
            new[] { (teamId, DirectMessageOwnerTeamDouble.RootUnitId, "Business Unit Default", true, 0) },
            Array.Empty<(Guid, string)>());
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
            Mock.Of<ILogger<DirectThreadAccessService>>(), new Sprk.Bff.Api.Tests.Services.Signals.GateSharingWorld().Sharing,
            DirectMessageOwnerTeamDouble.Unconfigured(), new UserFlagDirectoryDouble(), HostColumnProbe());
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
