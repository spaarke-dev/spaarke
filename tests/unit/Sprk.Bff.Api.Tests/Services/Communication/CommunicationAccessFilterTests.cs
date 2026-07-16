using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Communication.Access;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Security-enforcement behavior of <see cref="CommunicationAccessFilter"/> — R1's highest-risk control (FR-08 /
/// NFR-06). These are the closed set of negative/authorization cases that would leak privileged/private content if
/// they regressed silently:
/// <list type="bullet">
///   <item>a PRIVATE thread's messages are INVISIBLE to a caller without an active overlay grant;</item>
///   <item>an INTERNAL-ONLY message is INVISIBLE to a non-internal caller (D-05);</item>
///   <item>a PRIVILEGED message is NOT returned to an unauthorized caller, and privilege NEVER gates the read on
///         its own (ADR-015 — the filter takes NO AI dependency, so "no AI at read time" is structural);</item>
///   <item>POINT-FORWARD: opening a thread exposes ONLY messages authored at/after the open moment; prior private
///         messages STAY restricted;</item>
///   <item>the visible set is a SUBSET of Dataverse-derived access (agrees with task 041's ACS projection) — a
///         caller with neither membership nor grant sees NOTHING;</item>
///   <item>DEFAULT-DENY / fail closed: a thread that will not load hides everything on it.</item>
/// </list>
/// The canonical <see cref="IGenericEntityService"/> + <see cref="IMembershipResolverService"/> +
/// <see cref="IThreadPrivateGrantProvider"/> are mocked at the module boundary (no live Dataverse/membership in R1).
/// </summary>
public class CommunicationAccessFilterTests
{
    private static readonly Guid ThreadId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnchorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CallerId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string AnchorType = "sprk_matter";

    private const int PrivacyOpen = 100000000;
    private const int PrivacyPrivate = 100000001;
    private const int PrivilegeNone = 100000000;
    private const int PrivilegePrivileged = 100000002;

    private static readonly DateTimeOffset T0 = new(2026, 7, 16, 8, 0, 0, TimeSpan.Zero);   // early
    private static readonly DateTimeOffset TFlip = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero); // flip moment
    private static readonly DateTimeOffset T2 = new(2026, 7, 16, 16, 0, 0, TimeSpan.Zero);   // late

    // ─────────────────────────── builders ───────────────────────────

    private static Entity Thread(int privacyState, DateTimeOffset? effectiveFrom, bool withAnchor = true)
    {
        var e = new Entity("sprk_communicationthread", ThreadId)
        {
            ["sprk_privacystate"] = new OptionSetValue(privacyState),
        };
        if (effectiveFrom is not null)
            e["sprk_privacyeffectivefrom"] = effectiveFrom.Value.UtcDateTime;
        if (withAnchor)
        {
            e["sprk_regardingrecordid"] = AnchorId.ToString();
            e["sprk_regardingrecordtype"] = AnchorType;
        }
        return e;
    }

    private static Entity Message(
        DateTimeOffset createdOn,
        bool isPrivate = false,
        bool isInternalOnly = false,
        int privilege = PrivilegeNone)
    {
        return new Entity("sprk_communication", Guid.NewGuid())
        {
            ["createdon"] = createdOn.UtcDateTime,
            ["sprk_isprivate"] = isPrivate,
            ["sprk_isinternalonly"] = isInternalOnly,
            ["sprk_privilegeclassification"] = new OptionSetValue(privilege),
        };
    }

    private static MembershipResponse Membership(params Guid[] ids) =>
        new(
            AnchorType,
            new PersonIdentity(CallerId),
            ids,
            new Dictionary<string, IReadOnlyList<Guid>>(),
            ids.Length,
            DateTimeOffset.UtcNow.AddMinutes(5));

    private sealed class Harness
    {
        public Mock<IGenericEntityService> Entity { get; } = new();
        public Mock<IMembershipResolverService> Membership { get; } = new();
        public Mock<IThreadPrivateGrantProvider> Grants { get; } = new();

        public Harness(Entity? thread, MembershipResponse? membership, IReadOnlyList<ThreadPrivateGrant>? grants)
        {
            if (thread is not null)
                Entity.Setup(s => s.RetrieveAsync("sprk_communicationthread", ThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(thread);
            else
                Entity.Setup(s => s.RetrieveAsync("sprk_communicationthread", ThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                    .ThrowsAsync(new InvalidOperationException("thread not found"));

            Membership.Setup(m => m.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(membership ?? Membership());

            Grants.Setup(g => g.GetActiveGrantsAsync(It.IsAny<Guid>(), It.IsAny<CommunicationAccessContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(grants ?? Array.Empty<ThreadPrivateGrant>());
        }

        public CommunicationAccessFilter Sut() =>
            new(Entity.Object, Membership.Object, Grants.Object, Mock.Of<ILogger<CommunicationAccessFilter>>());
    }

    private static CommunicationAccessContext Internal(bool isInternal = true) => new(CallerId, isInternal);

    // ─────────────────────────── open-thread membership ───────────────────────────

    [Fact]
    public async Task Open_message_is_visible_to_a_member()
    {
        var h = new Harness(Thread(PrivacyOpen, null), Membership(AnchorId), null);
        var result = await h.Sut().FilterThreadMessagesAsync(Internal(), ThreadId, new[] { Message(T0) });

        result.VisibleMessages.Should().HaveCount(1);
    }

    [Fact]
    public async Task Open_message_is_invisible_to_a_non_member()
    {
        // Membership set does NOT contain the anchor → caller is not a member.
        var h = new Harness(Thread(PrivacyOpen, null), Membership(/* empty */), null);
        var result = await h.Sut().FilterThreadMessagesAsync(Internal(), ThreadId, new[] { Message(T0) });

        result.VisibleMessages.Should().BeEmpty();
        result.Decisions[0].Decision.DenyReason.Should().Be("open-not-member");
    }

    // ─────────────────────────── private-thread grant ───────────────────────────

    [Fact]
    public async Task Private_thread_message_is_INVISIBLE_without_a_grant()
    {
        // Caller IS a member of the anchor, but the thread is Private → membership alone must NOT expose it.
        var h = new Harness(Thread(PrivacyPrivate, T0), Membership(AnchorId), grants: null /* none */);
        var result = await h.Sut().FilterThreadMessagesAsync(Internal(), ThreadId, new[] { Message(T2) });

        result.VisibleMessages.Should().BeEmpty();
        result.Decisions[0].Decision.DenyReason.Should().Be("private-no-grant");
    }

    [Fact]
    public async Task Private_thread_message_is_visible_WITH_a_covering_grant()
    {
        var grant = new ThreadPrivateGrant(ThreadId, CallerId, EffectiveFrom: T0);
        var h = new Harness(Thread(PrivacyPrivate, T0), Membership(/* not a member */), new[] { grant });
        var result = await h.Sut().FilterThreadMessagesAsync(Internal(), ThreadId, new[] { Message(T2) });

        result.VisibleMessages.Should().HaveCount(1);
    }

    [Fact]
    public async Task Message_level_private_flag_hides_an_otherwise_open_thread_message()
    {
        // Thread Open + caller is a member, but the MESSAGE is flagged private → needs a grant.
        var h = new Harness(Thread(PrivacyOpen, null), Membership(AnchorId), grants: null);
        var result = await h.Sut().FilterThreadMessagesAsync(Internal(), ThreadId, new[] { Message(T0, isPrivate: true) });

        result.VisibleMessages.Should().BeEmpty();
        result.Decisions[0].Decision.DenyReason.Should().Be("private-no-grant");
    }

    // ─────────────────────────── internal-only (D-05) ───────────────────────────

    [Fact]
    public async Task Internal_only_message_is_INVISIBLE_to_a_non_internal_caller()
    {
        var h = new Harness(Thread(PrivacyOpen, null), Membership(AnchorId), null);
        // Non-internal caller, even though a member of an open thread.
        var result = await h.Sut().FilterThreadMessagesAsync(Internal(isInternal: false), ThreadId,
            new[] { Message(T0, isInternalOnly: true) });

        result.VisibleMessages.Should().BeEmpty();
        result.Decisions[0].Decision.DenyReason.Should().Be("internal-only");
    }

    [Fact]
    public async Task Internal_only_message_is_visible_to_an_internal_member()
    {
        var h = new Harness(Thread(PrivacyOpen, null), Membership(AnchorId), null);
        var result = await h.Sut().FilterThreadMessagesAsync(Internal(isInternal: true), ThreadId,
            new[] { Message(T0, isInternalOnly: true) });

        result.VisibleMessages.Should().HaveCount(1);
    }

    // ─────────────────────────── privilege (ADR-015) ───────────────────────────

    [Fact]
    public async Task Privileged_message_is_NOT_leaked_to_an_unauthorized_caller()
    {
        // Privileged content is also private; an ungranted (even member) caller must not see it.
        var h = new Harness(Thread(PrivacyPrivate, T0), Membership(AnchorId), grants: null);
        var result = await h.Sut().FilterThreadMessagesAsync(Internal(), ThreadId,
            new[] { Message(T2, privilege: PrivilegePrivileged) });

        result.VisibleMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task Privilege_is_composed_metadata_and_does_NOT_by_itself_change_the_read_outcome()
    {
        // Two OPEN messages to a member — one None, one Privileged — differ ONLY in the privilege field.
        // Both are visible (privilege does not gate); the privileged one carries its classification as metadata.
        // The SUT has NO AI dependency, so "no AI at read time" (ADR-015) is structural.
        var h = new Harness(Thread(PrivacyOpen, null), Membership(AnchorId), null);
        var none = Message(T0, privilege: PrivilegeNone);
        var privileged = Message(T2, privilege: PrivilegePrivileged);

        var result = await h.Sut().FilterThreadMessagesAsync(Internal(), ThreadId, new[] { none, privileged });

        result.VisibleMessages.Should().HaveCount(2);
        result.Decisions.Single(d => d.Message == privileged).Decision.Privilege
            .Should().Be(CommunicationPrivilegeClassification.Privileged);
        result.Decisions.Single(d => d.Message == none).Decision.Privilege
            .Should().Be(CommunicationPrivilegeClassification.None);
    }

    // ─────────────────────────── point-forward (D-04) ───────────────────────────

    [Fact]
    public async Task Point_forward_open_flip_exposes_only_messages_after_the_open_moment()
    {
        // Thread was OPENED at TFlip (Open state + effective-from = TFlip). Caller is a member.
        // Message before TFlip stays PRIVATE (hidden without grant); message at/after TFlip is open-visible.
        var before = Message(T0);
        var after = Message(T2);
        var h = new Harness(Thread(PrivacyOpen, TFlip), Membership(AnchorId), grants: null);

        var result = await h.Sut().FilterThreadMessagesAsync(Internal(), ThreadId, new[] { before, after });

        result.VisibleMessages.Should().ContainSingle().Which.Should().BeSameAs(after);
        result.Decisions.Single(d => d.Message == before).Decision.DenyReason.Should().Be("private-no-grant");
    }

    [Fact]
    public async Task Point_forward_private_flip_keeps_prior_messages_at_prior_open_visibility()
    {
        // Thread flipped to PRIVATE at TFlip. Message before TFlip keeps prior OPEN visibility (member sees it);
        // message at/after TFlip is private (hidden without grant).
        var before = Message(T0);
        var after = Message(T2);
        var h = new Harness(Thread(PrivacyPrivate, TFlip), Membership(AnchorId), grants: null);

        var result = await h.Sut().FilterThreadMessagesAsync(Internal(), ThreadId, new[] { before, after });

        result.VisibleMessages.Should().ContainSingle().Which.Should().BeSameAs(before);
        result.Decisions.Single(d => d.Message == after).Decision.DenyReason.Should().Be("private-no-grant");
    }

    [Fact]
    public async Task Grant_effective_from_gates_private_messages_point_forward()
    {
        // Private thread, grant effective from TFlip. Only messages authored at/after TFlip are visible to the grantee.
        var grant = new ThreadPrivateGrant(ThreadId, CallerId, EffectiveFrom: TFlip);
        var before = Message(T0);
        var after = Message(T2);
        var h = new Harness(Thread(PrivacyPrivate, T0), Membership(/* not a member */), new[] { grant });

        var result = await h.Sut().FilterThreadMessagesAsync(Internal(), ThreadId, new[] { before, after });

        result.VisibleMessages.Should().ContainSingle().Which.Should().BeSameAs(after);
    }

    // ─────────────────────────── subset-of-Dataverse-access (agrees with 041) ───────────────────────────

    [Fact]
    public async Task Visible_set_is_a_subset_of_dataverse_access_no_membership_no_grant_sees_nothing()
    {
        // A caller with NEITHER membership NOR a grant must see NOTHING across a mixed message set —
        // the read filter never exceeds Dataverse-derived access (⊆ task 041's ACS-membership projection).
        var messages = new[]
        {
            Message(T0),
            Message(T2, isInternalOnly: true),
            Message(T2, isPrivate: true),
            Message(T0, privilege: PrivilegePrivileged),
        };
        var h = new Harness(Thread(PrivacyPrivate, T0), Membership(/* none */), grants: null);

        var result = await h.Sut().FilterThreadMessagesAsync(Internal(), ThreadId, messages);

        result.VisibleMessages.Should().BeEmpty();
    }

    // ─────────────────────────── fail closed ───────────────────────────

    [Fact]
    public async Task Thread_that_cannot_be_loaded_denies_every_message_fail_closed()
    {
        var h = new Harness(thread: null, Membership(AnchorId), null); // RetrieveAsync throws
        var result = await h.Sut().FilterThreadMessagesAsync(Internal(), ThreadId,
            new[] { Message(T0), Message(T2) });

        result.VisibleMessages.Should().BeEmpty();
        result.Decisions.Should().OnlyContain(d => d.Decision.DenyReason == "thread-unresolved");
    }

    [Fact]
    public async Task Membership_resolution_failure_denies_open_access_fail_closed()
    {
        var h = new Harness(Thread(PrivacyOpen, null), Membership(AnchorId), null);
        h.Membership.Setup(m => m.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("dataverse down"));

        var result = await h.Sut().FilterThreadMessagesAsync(Internal(), ThreadId, new[] { Message(T0) });

        result.VisibleMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task Default_deny_grant_provider_keeps_private_threads_invisible_to_a_member()
    {
        // Integration of the real R1 default: DenyAllThreadPrivateGrantProvider grants nothing, so a member of the
        // anchor still cannot read a PRIVATE thread's messages.
        var thread = Thread(PrivacyPrivate, T0);
        var entity = new Mock<IGenericEntityService>();
        entity.Setup(s => s.RetrieveAsync("sprk_communicationthread", ThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(thread);
        var membership = new Mock<IMembershipResolverService>();
        membership.Setup(m => m.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Membership(AnchorId));

        var sut = new CommunicationAccessFilter(
            entity.Object, membership.Object, new DenyAllThreadPrivateGrantProvider(),
            Mock.Of<ILogger<CommunicationAccessFilter>>());

        var result = await sut.FilterThreadMessagesAsync(Internal(), ThreadId, new[] { Message(T2) });

        result.VisibleMessages.Should().BeEmpty();
    }
}
