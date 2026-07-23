using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Communication.Membership;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication.Membership;

/// <summary>
/// Behavior of <see cref="ThreadMembershipDerivationService"/> — the shared authorized-set contract (task 041 /
/// FR-07). Protects the three derivation paths: open (record-anchored) = reverse-ADR-034 over the anchor;
/// direct = explicit two-party list; private = open ∪ overlay grants. Reverse ADR-034 reuses the canonical
/// <see cref="IMembershipFieldDiscoveryService"/> (discovered fields), NOT ad-hoc FetchXML. Module boundaries
/// (Dataverse, discovery, explicit-grant reader) are mocked.
/// </summary>
public class ThreadMembershipDerivationServiceTests
{
    private static readonly Guid ThreadId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MatterId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string AnchorEntity = "sprk_matter";

    private readonly Mock<IGenericEntityService> _entityService = new();
    private readonly Mock<IMembershipFieldDiscoveryService> _discovery = new();
    private readonly Mock<IThreadExplicitParticipantReader> _explicitReader = new();

    public ThreadMembershipDerivationServiceTests()
    {
        _explicitReader
            .Setup(s => s.GetExplicitParticipantsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ThreadExplicitParticipant>());
    }

    private ThreadMembershipDerivationService BuildSut() => new(
        _entityService.Object, _discovery.Object, _explicitReader.Object,
        Mock.Of<ILogger<ThreadMembershipDerivationService>>());

    // ── Open (record-anchored) = reverse ADR-034 over the anchor ────────────

    [Fact]
    public async Task DeriveAuthorizedSetAsync_OpenRecordAnchored_ReturnsAnchorAuthorizedUsersFromDiscoveredFields()
    {
        var owner = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var attorney = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        SetupThread(ThreadTopology.RecordAnchored, ThreadPrivacyState.Open, anchorId: MatterId, anchorType: AnchorEntity);
        SetupDiscovery(("ownerid", "systemuser"), ("sprk_assignedattorney1", "contact"));
        SetupAnchor(new Dictionary<string, EntityReference>
        {
            ["ownerid"] = new EntityReference("systemuser", owner),
            ["sprk_assignedattorney1"] = new EntityReference("contact", attorney),
        });

        var set = await BuildSut().DeriveAuthorizedSetAsync(ThreadId);

        set.Participants.Select(p => (p.Participant.EntityLogicalName, p.Participant.RecordId))
            .Should().BeEquivalentTo(new[] { ("systemuser", owner), ("contact", attorney) });
        set.Participants.Should().OnlyContain(p => p.Reason == AuthorizationReason.RecordMembership);
    }

    [Fact]
    public async Task DeriveAuthorizedSetAsync_OpenRecordAnchored_ExpandsTeamLookupToItsMembers()
    {
        var member1 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var member2 = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        var teamId = Guid.Parse("77777777-0000-0000-0000-000000000007");
        SetupThread(ThreadTopology.RecordAnchored, ThreadPrivacyState.Open, anchorId: MatterId, anchorType: AnchorEntity);
        SetupDiscovery(("owningteam", "team"));
        SetupAnchor(new Dictionary<string, EntityReference> { ["owningteam"] = new EntityReference("team", teamId) });
        // teammembership intersect expansion → two member systemusers.
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                new("teammembership") { ["systemuserid"] = member1 },
                new("teammembership") { ["systemuserid"] = member2 },
            }));

        var set = await BuildSut().DeriveAuthorizedSetAsync(ThreadId);

        set.Participants.Select(p => p.Participant.RecordId).Should().BeEquivalentTo(new[] { member1, member2 });
        set.Participants.Should().OnlyContain(p => p.Participant.EntityLogicalName == "systemuser");
    }

    // ── Private = open ∪ overlay grants (spike 002 option B) ─────────────────

    [Fact]
    public async Task DeriveAuthorizedSetAsync_Private_UnionsDerivedMembershipWithOverlayGrants()
    {
        var owner = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var grantedContact = Guid.Parse("eeeeeeee-0000-0000-0000-000000000005");
        SetupThread(ThreadTopology.RecordAnchored, ThreadPrivacyState.Private, anchorId: MatterId, anchorType: AnchorEntity);
        SetupDiscovery(("ownerid", "systemuser"));
        SetupAnchor(new Dictionary<string, EntityReference> { ["ownerid"] = new EntityReference("systemuser", owner) });
        _explicitReader
            .Setup(s => s.GetExplicitParticipantsAsync(ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ThreadExplicitParticipant
                {
                    Participant = ParticipantReference.Contact(grantedContact),
                    Kind = ThreadExplicitParticipantKind.PrivateOverlayGrant,
                },
            });

        var set = await BuildSut().DeriveAuthorizedSetAsync(ThreadId);

        set.Participants.Select(p => p.Participant.RecordId).Should().BeEquivalentTo(new[] { owner, grantedContact });
        set.Participants.Should().ContainSingle(p => p.Participant.RecordId == owner && p.Reason == AuthorizationReason.RecordMembership);
        set.Participants.Should().ContainSingle(p => p.Participant.RecordId == grantedContact && p.Reason == AuthorizationReason.PrivateOverlayGrant);
    }

    // ── Direct 1:1 = explicit two-participant list (no anchor derivation) ────

    [Fact]
    public async Task DeriveAuthorizedSetAsync_Direct_ReturnsExplicitParticipantsAndDoesNotDeriveFromAnchor()
    {
        var p1 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var p2 = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
        SetupThread(ThreadTopology.Direct, ThreadPrivacyState.Open, anchorId: null, anchorType: null);
        _explicitReader
            .Setup(s => s.GetExplicitParticipantsAsync(ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ThreadExplicitParticipant { Participant = ParticipantReference.SystemUser(p1), Kind = ThreadExplicitParticipantKind.DirectParticipant },
                new ThreadExplicitParticipant { Participant = ParticipantReference.SystemUser(p2), Kind = ThreadExplicitParticipantKind.DirectParticipant },
            });

        var set = await BuildSut().DeriveAuthorizedSetAsync(ThreadId);

        set.Participants.Select(p => p.Participant.RecordId).Should().BeEquivalentTo(new[] { p1, p2 });
        set.Participants.Should().OnlyContain(p => p.Reason == AuthorizationReason.DirectParticipant);
        // Direct threads never reverse-derive from an anchor.
        _discovery.Verify(s => s.DiscoverAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeriveAuthorizedSetAsync_WhenThreadUnknown_ReturnsEmptySet()
    {
        _entityService
            .Setup(s => s.RetrieveAsync("sprk_communicationthread", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communicationthread")); // Id == Guid.Empty → unknown

        var set = await BuildSut().DeriveAuthorizedSetAsync(ThreadId);

        set.Participants.Should().BeEmpty();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void SetupThread(ThreadTopology topology, ThreadPrivacyState privacy, Guid? anchorId, string? anchorType)
    {
        var thread = new Entity("sprk_communicationthread")
        {
            Id = ThreadId,
            ["sprk_threadtype"] = new OptionSetValue((int)topology),
            ["sprk_privacystate"] = new OptionSetValue((int)privacy),
        };
        if (anchorId is { } id) thread["sprk_regardingrecordid"] = id.ToString();
        if (anchorType is { } t) thread["sprk_regardingrecordtype"] = t;

        _entityService
            .Setup(s => s.RetrieveAsync("sprk_communicationthread", ThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(thread);
    }

    private void SetupDiscovery(params (string field, string target)[] fields)
    {
        var descriptors = fields
            .Select(f => new MembershipDescriptor(f.field, f.field, f.target, f.target, "auto"))
            .ToList();
        _discovery
            .Setup(s => s.DiscoverAsync(AnchorEntity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DiscoveryResult(AnchorEntity, DateTimeOffset.UtcNow, descriptors,
                Array.Empty<IgnoredField>(), Array.Empty<IgnoredField>()));
    }

    private void SetupAnchor(Dictionary<string, EntityReference> lookups)
    {
        var anchor = new Entity(AnchorEntity) { Id = MatterId };
        foreach (var (field, reference) in lookups)
            anchor[field] = reference;

        _entityService
            .Setup(s => s.RetrieveAsync(AnchorEntity, MatterId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(anchor);
    }
}
