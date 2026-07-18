using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Acs;
using Sprk.Bff.Api.Services.Communication.Membership;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication.Membership;

/// <summary>
/// Behavior of <see cref="MembershipReconciler"/> — the projection of Dataverse-derived access onto ACS
/// membership (task 041 / FR-07). These protect the load-bearing security invariant "ACS membership ⊆
/// Dataverse-derived access" plus the add/remove diff, the audit-per-change obligation, and the idempotent
/// no-op. Collaborators are mocked at genuine module boundaries (ACS SDK behind <see cref="IAcsThreadService"/>,
/// Dataverse behind <see cref="IGenericEntityService"/>, derivation behind
/// <see cref="IThreadMembershipDerivationService"/>) — no live ACS resource is provisioned (task constraint).
/// </summary>
public class MembershipReconcilerTests
{
    private const string ChatThreadId = "19:acs:thread_0000000000000000000000000000abcd@thread.v2";
    private static readonly Guid ThreadId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Mock<IThreadMembershipDerivationService> _derivation = new();
    private readonly Mock<IAcsIdentityService> _identity = new();
    private readonly Mock<IAcsThreadService> _acsThread = new();
    private readonly Mock<IGenericEntityService> _entityService = new();
    private readonly Mock<IMembershipReconcileAuditSink> _audit = new();
    private readonly TimeProvider _time = TimeProvider.System;

    private readonly List<string> _added = new();
    private readonly List<string> _removed = new();
    private readonly List<MembershipReconcileAuditEntry> _auditEntries = new();

    public MembershipReconcilerTests()
    {
        // Channel-ref read → the thread's ACS chat thread id lives on the Message channel-ref row.
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ChannelRefCollection(ChatThreadId));

        // Deterministic participant → MRI mapping (idempotent no-op mint).
        _identity
            .Setup(s => s.EnsureIdentityAsync(It.IsAny<ParticipantReference>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipantReference p, CancellationToken _) => new AcsIdentityMapping
            {
                CommunicationUserId = Mri(p),
                Participant = p,
                WasCreated = false,
            });

        _acsThread
            .Setup(s => s.AddParticipantsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IEnumerable<string>, CancellationToken>((_, ids, _) => _added.AddRange(ids))
            .ReturnsAsync((string t, IEnumerable<string> ids, CancellationToken _) => new AcsMembershipChange
            {
                ChatThreadId = t,
                Requested = ids.Count(),
                Applied = ids.Count(),
                NoOped = 0,
                BatchCount = 1,
            });

        _acsThread
            .Setup(s => s.RemoveParticipantAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, id, _) => _removed.Add(id))
            .ReturnsAsync((string t, string id, CancellationToken _) => new AcsMembershipChange
            {
                ChatThreadId = t,
                Requested = 1,
                Applied = 1,
                NoOped = 0,
                BatchCount = 1,
            });

        _audit
            .Setup(s => s.RecordAsync(It.IsAny<MembershipReconcileAuditEntry>(), It.IsAny<CancellationToken>()))
            .Callback<MembershipReconcileAuditEntry, CancellationToken>((e, _) => _auditEntries.Add(e))
            .Returns(Task.CompletedTask);
    }

    private MembershipReconciler BuildSut() => new(
        _derivation.Object, _identity.Object, _acsThread.Object, _entityService.Object,
        _audit.Object, _time, Mock.Of<ILogger<MembershipReconciler>>());

    // ── Add missing ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ReconcileAsync_WhenDataverseAuthorizesNewUser_AddsToAcs()
    {
        var userA = ParticipantReference.SystemUser(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
        var userB = ParticipantReference.SystemUser(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"));
        SetupDerived(userA, userB);
        _acsThread.Setup(s => s.ListParticipantsAsync(ChatThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Mri(userA) }); // B is newly authorized

        var result = await BuildSut().ReconcileAsync(ThreadId, MembershipReconcileTrigger.RecordAccessChanged, "user-x", "corr-1");

        _added.Should().ContainSingle().Which.Should().Be(Mri(userB));
        _removed.Should().BeEmpty();
        result.Added.Should().Be(1);
        result.Removed.Should().Be(0);
    }

    // ── Remove extra (over-exposure guard) ──────────────────────────────────

    [Fact]
    public async Task ReconcileAsync_WhenUserNoLongerAuthorized_RemovesFromAcs()
    {
        var userA = ParticipantReference.SystemUser(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
        var revoked = ParticipantReference.SystemUser(Guid.Parse("cccccccc-0000-0000-0000-000000000003"));
        SetupDerived(userA);
        _acsThread.Setup(s => s.ListParticipantsAsync(ChatThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Mri(userA), Mri(revoked) }); // revoked still in ACS — over-exposure

        var result = await BuildSut().ReconcileAsync(ThreadId, MembershipReconcileTrigger.RecordAccessChanged, "user-x", "corr-1");

        _removed.Should().ContainSingle().Which.Should().Be(Mri(revoked));
        _added.Should().BeEmpty();
        result.Removed.Should().Be(1);
    }

    // ── Projection invariant: ACS membership NEVER exceeds the derived set ──

    [Fact]
    public async Task ReconcileAsync_ResultingAcsSet_NeverExceedsDataverseDerivedSet()
    {
        var a = ParticipantReference.SystemUser(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
        var b = ParticipantReference.Contact(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"));
        var extra1 = "8:acs:intruder-1";
        var extra2 = "8:acs:intruder-2";
        SetupDerived(a, b);
        _acsThread.Setup(s => s.ListParticipantsAsync(ChatThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Mri(a), extra1, extra2 }); // two ACS members NOT in the derived set

        await BuildSut().ReconcileAsync(ThreadId, MembershipReconcileTrigger.PeriodicSweep, "system", "corr-1");

        var desired = new HashSet<string> { Mri(a), Mri(b) };
        // Resulting ACS = (current \ removed) ∪ added.
        var current = new HashSet<string> { Mri(a), extra1, extra2 };
        var resulting = new HashSet<string>(current.Except(_removed).Union(_added));

        resulting.Should().BeSubsetOf(desired, "ACS membership is a projection of Dataverse-derived access and must never exceed it");
        resulting.Should().BeEquivalentTo(desired, "reconcile converges ACS to exactly the derived set");
        _added.Should().OnlyContain(mri => desired.Contains(mri), "the reconcile only ever adds Dataverse-authorized participants");
        _removed.Should().BeEquivalentTo(new[] { extra1, extra2 });
    }

    // ── Audit entry per change ──────────────────────────────────────────────

    [Fact]
    public async Task ReconcileAsync_WritesOneAuditEntryPerMembershipChange()
    {
        var keep = ParticipantReference.SystemUser(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
        var addC = ParticipantReference.Contact(Guid.Parse("dddddddd-0000-0000-0000-000000000004"));
        var revoked = "8:acs:revoked-1";
        SetupDerived(keep, addC);
        _acsThread.Setup(s => s.ListParticipantsAsync(ChatThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Mri(keep), revoked }); // add addC, remove revoked

        await BuildSut().ReconcileAsync(ThreadId, MembershipReconcileTrigger.ParticipantEdited, "user-x", "corr-9");

        _auditEntries.Should().HaveCount(2); // exactly one per change (1 add + 1 remove)
        _auditEntries.Should().ContainSingle(e => e.Action == MembershipChangeAction.Added && e.CommunicationUserId == Mri(addC))
            .Which.Should().Match<MembershipReconcileAuditEntry>(e =>
                e.ThreadId == ThreadId && e.Trigger == MembershipReconcileTrigger.ParticipantEdited &&
                e.Actor == "user-x" && e.CorrelationId == "corr-9");
        _auditEntries.Should().ContainSingle(e => e.Action == MembershipChangeAction.Removed && e.CommunicationUserId == revoked);
    }

    // ── Idempotent no-op when already consistent ────────────────────────────

    [Fact]
    public async Task ReconcileAsync_WhenAcsAlreadyMatchesDerived_IsNoOpWithNoAcsMutationsOrAudit()
    {
        var a = ParticipantReference.SystemUser(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"));
        var b = ParticipantReference.SystemUser(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"));
        SetupDerived(a, b);
        _acsThread.Setup(s => s.ListParticipantsAsync(ChatThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Mri(a), Mri(b) }); // already consistent

        var result = await BuildSut().ReconcileAsync(ThreadId, MembershipReconcileTrigger.PeriodicSweep, "system", "corr-1");

        _added.Should().BeEmpty();
        _removed.Should().BeEmpty();
        _auditEntries.Should().BeEmpty();
        result.Added.Should().Be(0);
        result.Removed.Should().Be(0);
        _acsThread.Verify(s => s.AddParticipantsAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        _acsThread.Verify(s => s.RemoveParticipantAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── No ACS thread yet → successful no-op ────────────────────────────────

    [Fact]
    public async Task ReconcileAsync_WhenThreadHasNoAcsChatThread_IsSuccessfulNoOp()
    {
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection()); // no channel-ref row

        var result = await BuildSut().ReconcileAsync(ThreadId, MembershipReconcileTrigger.PeriodicSweep, "system", "corr-1");

        result.SkippedNoAcsThread.Should().BeTrue();
        _acsThread.Verify(s => s.ListParticipantsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _derivation.Verify(s => s.DeriveAuthorizedSetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Anti-thrashing: a transient derivation failure must NOT mass-remove ─

    [Fact]
    public async Task ReconcileAsync_WhenDerivationFails_PropagatesAndDoesNotRemoveAnyParticipant()
    {
        // A transient Dataverse failure inside derivation must propagate (→ job retry), NOT be treated as an
        // empty authorized set — otherwise the reconcile would remove every current ACS participant.
        _acsThread.Setup(s => s.ListParticipantsAsync(ChatThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "8:acs:member-1", "8:acs:member-2" });
        _derivation.Setup(s => s.DeriveAuthorizedSetAsync(ThreadId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Dataverse transient"));

        var act = () => BuildSut().ReconcileAsync(ThreadId, MembershipReconcileTrigger.PeriodicSweep, "system", "corr-1");

        await act.Should().ThrowAsync<TimeoutException>();
        _removed.Should().BeEmpty();
        _added.Should().BeEmpty();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void SetupDerived(params ParticipantReference[] participants)
    {
        var set = new ThreadAuthorizedSet
        {
            ThreadId = ThreadId,
            Participants = participants
                .Select(p => new AuthorizedParticipant { Participant = p, Reason = AuthorizationReason.RecordMembership })
                .ToList(),
        };
        _derivation.Setup(s => s.DeriveAuthorizedSetAsync(ThreadId, It.IsAny<CancellationToken>())).ReturnsAsync(set);
    }

    private static string Mri(ParticipantReference p) => $"8:acs:{p.EntityLogicalName}:{p.RecordId}";

    private static EntityCollection ChannelRefCollection(string chatThreadId)
    {
        var e = new Entity("sprk_communicationchannelref")
        {
            Id = Guid.NewGuid(),
            ["sprk_externalref"] = chatThreadId,
            ["sprk_channeltype"] = new OptionSetValue((int)CommunicationType.Message),
        };
        return new EntityCollection(new List<Entity> { e });
    }
}
