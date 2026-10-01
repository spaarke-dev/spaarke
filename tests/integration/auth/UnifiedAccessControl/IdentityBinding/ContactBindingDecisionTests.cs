using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.IdentityBindingTestKit;

namespace Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

/// <summary>
/// unified-access-control-r2 task 141 — the identity-binding decision table, asserted directly.
/// </summary>
/// <remarks>
/// <para>The decision is PUBLIC and pure (no I/O), so these tests use no double at all and call no internal
/// member — no <c>InternalsVisibleTo</c>, which ADR-038 §7 ban B8 forbids and task 013's predecessor of this
/// decision relied on. Each row of <c>notes/task-141-identity-binding.md</c> §3 has a test, and each guard is
/// shown to be load-bearing by perturbing ONE input and watching the outcome flip.</para>
/// </remarks>
public class ContactBindingDecisionTests
{
    private static readonly Guid Caller = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid Other = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    private static readonly Guid ContactA = Guid.Parse("cccccccc-0000-4000-8000-00000000000a");
    private static readonly Guid ContactB = Guid.Parse("cccccccc-0000-4000-8000-00000000000b");
    private const string Email = "person@customer.example";

    private static BindingRequest Workforce(BindingEligibility e = BindingEligibility.EmailBindAndCreate, Guid? oid = null)
        => new(BindingPlane.WorkforceToken, oid ?? Caller, Email, e, "sdap.access.deny.workforce_guest");

    private static BindingRequest Ciam(string? email = Email)
        => new(BindingPlane.CiamToken, Caller, email, BindingEligibility.EmailBindOnly);

    private static BindingRequest SystemUser(bool guest = false)
        => new(BindingPlane.SystemUser, Caller, Email,
            guest ? BindingEligibility.CreateUnlessEmailMatches : BindingEligibility.EmailBindAndCreate);

    private static ContactBindingRow Unbound(Guid id, int state = 0) => new(id, state, null, null);

    private static ContactBindingRow Bound(Guid id, Guid oid, IdentityPlaneMarker? plane = IdentityPlaneMarker.External, int state = 0)
        => new(id, state, oid.ToString("D"), plane is { } p ? (int)p : null);

    private static readonly ContactLookup NoRows = ContactLookup.Of();

    // ── ReadBinding: the three states ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, null, BindingKind.Unbound)]
    [InlineData("", null, BindingKind.Unbound)]
    [InlineData("aaaaaaaa-0000-4000-8000-000000000001", 100000000, BindingKind.Bound)]
    [InlineData("AAAAAAAA-0000-4000-8000-000000000001", 100000001, BindingKind.Bound)]
    [InlineData("aaaaaaaa-0000-4000-8000-000000000001", null, BindingKind.Bound)]   // pre-141 CIAM row
    [InlineData(null, 100000001, BindingKind.Unreadable)]                          // marker, no oid = masked/orphaned
    [InlineData("not-a-guid", 100000000, BindingKind.Unreadable)]
    [InlineData("00000000-0000-0000-0000-000000000000", 100000000, BindingKind.Unreadable)]
    [InlineData("aaaaaaaa-0000-4000-8000-000000000001", 999, BindingKind.Unreadable)] // unknown marker value
    public void ReadBinding_ClassifiesEveryShape(string? rawOid, int? rawPlane, BindingKind expected)
        => ContactBindingDecision.ReadBinding(rawOid, rawPlane).Kind.Should().Be(expected);

    [Fact]
    public void ReadBinding_AnOidWithNoMarker_IsAPre141CiamBinding()
    {
        // Every writer before task 141 was the CIAM path (6 of 6, verified live 2026-09-30).
        ContactBindingDecision.ReadBinding(Caller.ToString("D"), null).Plane.Should().Be(IdentityPlaneMarker.External);
    }

    [Fact]
    public void ReadBinding_ComparesParsedGuids_NotStrings()
    {
        var upper = ContactBindingDecision.ReadBinding(Caller.ToString("D").ToUpperInvariant(), 100000001);
        upper.Oid.Should().Be(Caller, "the same oid in another case is the same oid");
    }

    // ── D0..D4: the oid binding is authoritative ─────────────────────────────────────────────────────

    [Fact]
    public void D0_AnUnusableCallerOid_Denies()
        => ContactBindingDecision.Decide(Workforce(oid: Guid.Empty), NoRows).DenyCode
            .Should().Be(ContactBindingDecision.DenyUnidentifiableCaller);

    [Theory]
    [InlineData(LookupStatus.Failed, ContactBindingDecision.DenyContactLookupFailed)]
    [InlineData(LookupStatus.ColumnMissing, ContactBindingDecision.DenyBindingColumnMissing)]
    public void D1_AnUnreadableOidLookup_Denies_AndNeverFallsThroughToEmail(LookupStatus status, string code)
    {
        var lookup = status == LookupStatus.Failed ? ContactLookup.Failed : ContactLookup.ColumnMissing;

        // Even with an email lookup that WOULD bind, a could-not-read oid lookup denies.
        var decision = ContactBindingDecision.Decide(Workforce(), lookup, ContactLookup.Of(Unbound(ContactA)),
            ReferenceLookup.Of());

        decision.Action.Should().Be(BindingAction.Deny);
        decision.DenyCode.Should().Be(code);
    }

    [Fact]
    public void D2_AnOidOnTwoContacts_IsACollision_FlaggingBoth()
    {
        var decision = ContactBindingDecision.Decide(Workforce(),
            ContactLookup.Of(Bound(ContactA, Caller), Bound(ContactB, Caller)));

        decision.Action.Should().Be(BindingAction.Collision);
        decision.Reason.Should().Be(IdentityCollisionReason.OidOnMultipleContacts);
        decision.FlagContactIds.Should().BeEquivalentTo(new[] { ContactA, ContactB });
        decision.DenyCode.Should().Be(ContactBindingDecision.DenyContactOidAmbiguous);
    }

    [Fact]
    public void D3_AnOidOnlyOnAnInactiveContact_DeniesWithItsOwnCode_AndNeverEmailBindsOrCreates()
    {
        var decision = ContactBindingDecision.Decide(Workforce(), ContactLookup.Of(Bound(ContactA, Caller, state: 1)),
            ContactLookup.Of(Unbound(ContactB)), ReferenceLookup.Of());

        decision.Action.Should().Be(BindingAction.Deny);
        decision.DenyCode.Should().Be(ContactBindingDecision.DenyContactInactive);
    }

    [Fact]
    public void D3_PerturbTheContactToActive_Resolves()
        => ContactBindingDecision.Decide(Workforce(), ContactLookup.Of(Bound(ContactA, Caller)))
            .Should().Be(new BindingDecision(BindingAction.ResolveByOid, ContactA));

    [Theory]
    [InlineData(BindingEligibility.None)]
    [InlineData(BindingEligibility.EmailBindOnly)]
    [InlineData(BindingEligibility.EmailBindAndCreate)]
    public void D4_AnExistingOidBinding_ResolvesForEveryEligibility_WithoutConsultingEmail(BindingEligibility eligibility)
    {
        // No email lookup is supplied: a decision that needed one would return NeedEmailLookup.
        var decision = ContactBindingDecision.Decide(Workforce(eligibility), ContactLookup.Of(Bound(ContactA, Caller)));

        decision.Action.Should().Be(BindingAction.ResolveByOid);
        decision.ContactId.Should().Be(ContactA);
    }

    // ── E0..E11: the first-sign-in email stage ───────────────────────────────────────────────────────

    [Fact]
    public void E0_AnIneligibleCaller_WithNoOidBinding_IsDeniedWithItsMemberTestCode()
        => ContactBindingDecision.Decide(Workforce(BindingEligibility.None), NoRows).DenyCode
            .Should().Be("sdap.access.deny.workforce_guest");

    [Fact]
    public void E0_AnIneligibleCaller_IsNeverAskedForAnEmailLookup()
        => ContactBindingDecision.Decide(Workforce(BindingEligibility.None), NoRows).Action
            .Should().Be(BindingAction.Deny);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void E1_NoEmail_AMemberCreates_ButNeverMatchesOnABlankEmail(string? email)
    {
        var request = Workforce() with { Email = email };
        ContactBindingDecision.Decide(request, NoRows).Action.Should().Be(BindingAction.CreateByOid);
    }

    [Fact]
    public void E1_NoEmail_TheCiamPlaneDenies()
        => ContactBindingDecision.Decide(Ciam(email: null), NoRows).DenyCode
            .Should().Be(ContactBindingDecision.DenyContactNotFound);

    [Fact]
    public void E2_AnUnreadableEmailLookup_Denies_AndNeverCreates()
        => ContactBindingDecision.Decide(Workforce(), NoRows, ContactLookup.Failed).DenyCode
            .Should().Be(ContactBindingDecision.DenyContactLookupFailed);

    [Fact]
    public void E3_TwoActiveContactsCarryTheEmail_IsACollision_NoFirstRowPick()
    {
        var decision = ContactBindingDecision.Decide(Workforce(), NoRows,
            ContactLookup.Of(Unbound(ContactA), Unbound(ContactB)));

        decision.Action.Should().Be(BindingAction.Collision);
        decision.Reason.Should().Be(IdentityCollisionReason.EmailAmbiguous);
        decision.FlagContactIds.Should().BeEquivalentTo(new[] { ContactA, ContactB });
    }

    [Fact]
    public void E3_PerturbToOneContact_Proceeds()
        => ContactBindingDecision.Decide(Workforce(), NoRows, ContactLookup.Of(Unbound(ContactA)), ReferenceLookup.Of())
            .Action.Should().Be(BindingAction.BindByEmail);

    [Fact]
    public void E4_NoEmailMatch_AMemberCreatesKeyedByOid()
        => ContactBindingDecision.Decide(Workforce(), NoRows, NoRows).Action.Should().Be(BindingAction.CreateByOid);

    [Fact]
    public void E4_NoEmailMatch_TheCiamPlaneNeverCreates()
        => ContactBindingDecision.Decide(Ciam(), NoRows, NoRows).DenyCode
            .Should().Be(ContactBindingDecision.DenyContactNotFound);

    [Fact]
    public void E4_AnInactiveEmailMatch_IsNeverABindTarget()
    {
        // The store filters statecode server-side; the decision re-checks it (the filter bounds volume only).
        var decision = ContactBindingDecision.Decide(Workforce(), NoRows, ContactLookup.Of(Unbound(ContactA, state: 1)));
        decision.Action.Should().Be(BindingAction.CreateByOid, "an inactive contact is invisible to the email bind");
    }

    [Fact]
    public void E5_AnUnreadableBinding_IsACollision_NeverABind()
    {
        var unreadable = new ContactBindingRow(ContactA, 0, "not-a-guid", (int)IdentityPlaneMarker.External);
        var decision = ContactBindingDecision.Decide(Workforce(), NoRows, ContactLookup.Of(unreadable));

        decision.Reason.Should().Be(IdentityCollisionReason.BindingUnreadable);
        decision.DenyCode.Should().Be(ContactBindingDecision.DenyContactBindingUnreadable);
    }

    [Fact]
    public void E5_AMarkerWithNoOid_IsUnreadable_TheMaskingSignature()
    {
        // What a field-secured oid column looks like to a reader without FLS Read.
        var masked = new ContactBindingRow(ContactA, 0, null, (int)IdentityPlaneMarker.External);
        ContactBindingDecision.Decide(Workforce(), NoRows, ContactLookup.Of(masked)).Reason
            .Should().Be(IdentityCollisionReason.BindingUnreadable);
    }

    [Fact]
    public void E6_AnEmailRowBoundToThisCaller_Resolves()
        => ContactBindingDecision.Decide(Workforce(), NoRows, ContactLookup.Of(Bound(ContactA, Caller)))
            .Should().Be(new BindingDecision(BindingAction.ResolveByOid, ContactA));

    [Theory]
    [InlineData(BindingPlane.WorkforceToken)]
    [InlineData(BindingPlane.CiamToken)]
    [InlineData(BindingPlane.SystemUser)]
    public void E7_AnEmailRowBoundToAnotherOid_IsACollision_OnEveryPlane(BindingPlane plane)
    {
        var request = new BindingRequest(plane, Caller, Email,
            plane == BindingPlane.CiamToken ? BindingEligibility.EmailBindOnly : BindingEligibility.EmailBindAndCreate);
        var decision = ContactBindingDecision.Decide(request, NoRows, ContactLookup.Of(Bound(ContactA, Other)));

        decision.Action.Should().Be(BindingAction.Collision);
        decision.Reason.Should().Be(IdentityCollisionReason.BoundToDifferentOid);
        decision.DenyCode.Should().Be(ContactBindingDecision.DenyContactBoundToDifferentOid);
    }

    [Fact]
    public void E7_PerturbTheBoundOidToTheCallers_Resolves()
        => ContactBindingDecision.Decide(Workforce(), NoRows, ContactLookup.Of(Bound(ContactA, Caller))).Action
            .Should().Be(BindingAction.ResolveByOid);

    [Fact]
    public void E8_AGuestSystemUsersEmailMatch_IsACollision_NeverABind()
        => ContactBindingDecision.Decide(SystemUser(guest: true), NoRows, ContactLookup.Of(Unbound(ContactA))).Reason
            .Should().Be(IdentityCollisionReason.GuestEmailMatch);

    [Fact]
    public void E8_AGuestSystemUserWithNoEmailMatch_Creates()
        => ContactBindingDecision.Decide(SystemUser(guest: true), NoRows, NoRows).Action
            .Should().Be(BindingAction.CreateByOid);

    [Fact]
    public void E9_UnreadableReferences_Deny()
        => ContactBindingDecision.Decide(Workforce(), NoRows, ContactLookup.Of(Unbound(ContactA)), ReferenceLookup.Failed)
            .DenyCode.Should().Be(ContactBindingDecision.DenyContactLookupFailed);

    [Fact]
    public void E10_AContactAnotherUserLinksTo_IsACollision()
    {
        var refs = ReferenceLookup.Of(new SystemUserReference(Guid.NewGuid(), Other, ContactA));
        ContactBindingDecision.Decide(Workforce(), NoRows, ContactLookup.Of(Unbound(ContactA)), refs).Reason
            .Should().Be(IdentityCollisionReason.LinkedToOtherUser);
    }

    [Fact]
    public void E10_AContactThisUserLinksTo_IsNotACollision_OnTheWorkforcePlane()
    {
        var refs = ReferenceLookup.Of(new SystemUserReference(Guid.NewGuid(), Caller, ContactA));
        ContactBindingDecision.Decide(SystemUser(), NoRows, ContactLookup.Of(Unbound(ContactA)), refs).Action
            .Should().Be(BindingAction.BindByEmail);
    }

    [Fact]
    public void E10_OnTheCiamPlane_AnySystemUserLink_IsACollision()
    {
        // Even a link from a systemuser whose oid equals the CIAM caller's: an internal user's contact is never
        // the repair target of an external sign-in.
        var refs = ReferenceLookup.Of(new SystemUserReference(Guid.NewGuid(), Caller, ContactA));
        ContactBindingDecision.Decide(Ciam(), NoRows, ContactLookup.Of(Unbound(ContactA)), refs).Reason
            .Should().Be(IdentityCollisionReason.LinkedToOtherUser);
    }

    [Fact]
    public void E11_ExactlyOneActiveUnboundUnlinkedMatch_BindsByEmail()
        => ContactBindingDecision.Decide(Ciam(), NoRows, ContactLookup.Of(Unbound(ContactA)), ReferenceLookup.Of())
            .Should().Be(new BindingDecision(BindingAction.BindByEmail, ContactA));

    [Fact]
    public void TheDecisionAsksForEachLookupOnlyWhenItNeedsIt()
    {
        ContactBindingDecision.Decide(Workforce(), NoRows).Action.Should().Be(BindingAction.NeedEmailLookup);
        ContactBindingDecision.Decide(Workforce(), NoRows, ContactLookup.Of(Unbound(ContactA))).Action
            .Should().Be(BindingAction.NeedReferenceLookup);
    }

    // ── L1..L12: an existing link is verified, bound, or flagged — never re-pointed ──────────────────

    [Fact]
    public void L3_ALinkToTheContactCarryingTheOid_IsVerified()
        => ContactBindingDecision.DecideExistingLink(SystemUser(), ContactA, ContactLookup.Of(Bound(ContactA, Caller)))
            .Action.Should().Be(BindingAction.LinkVerified);

    [Fact]
    public void L4_ALinkToAnInactiveBoundContact_IsFlagged()
        => ContactBindingDecision.DecideExistingLink(SystemUser(), ContactA,
                ContactLookup.Of(Bound(ContactA, Caller, state: 1)))
            .Reason.Should().Be(IdentityCollisionReason.LinkedContactInactive);

    [Fact]
    public void L5_ALinkToOneContactWhileTheOidIsOnAnother_IsFlagged_NotRePointed()
    {
        var decision = ContactBindingDecision.DecideExistingLink(SystemUser(), ContactA, ContactLookup.Of(Bound(ContactB, Caller)));

        decision.Reason.Should().Be(IdentityCollisionReason.LinkedContactMismatch);
        decision.FlagContactIds.Should().Equal(ContactA);
    }

    [Fact]
    public void L6_ALinkToAMissingContact_Denies()
        => ContactBindingDecision.DecideExistingLink(SystemUser(), ContactA, NoRows, NoRows).DenyCode
            .Should().Be(ContactBindingDecision.DenyLinkedContactMissing);

    [Fact]
    public void L8_ALinkToAContactBoundToAnotherOid_IsFlagged_AndTheLinkIsKept()
    {
        // Ralph's dev row: linked to 8e9918a9, which a CIAM identity (6a9fa229) owns. Owner decision (a).
        var decision = ContactBindingDecision.DecideExistingLink(SystemUser(), ContactA, NoRows,
            ContactLookup.Of(Bound(ContactA, Other)));

        decision.Action.Should().Be(BindingAction.Collision);
        decision.Reason.Should().Be(IdentityCollisionReason.LinkedContactBoundToDifferentOid);
        decision.Action.Should().NotBe(BindingAction.BindLinkedContact);
    }

    [Fact]
    public void L10_AGuestLinkedToAnUnboundContact_IsFlagged()
        => ContactBindingDecision.DecideExistingLink(SystemUser(guest: true), ContactA, NoRows,
                ContactLookup.Of(Unbound(ContactA)))
            .Reason.Should().Be(IdentityCollisionReason.GuestLinkUnverified);

    [Fact]
    public void L11_AnUnboundLinkedContactAnotherUserAlsoLinks_IsFlagged()
        => ContactBindingDecision.DecideExistingLink(SystemUser(), ContactA, NoRows, ContactLookup.Of(Unbound(ContactA)),
                ReferenceLookup.Of(new SystemUserReference(Guid.NewGuid(), Other, ContactA)))
            .Reason.Should().Be(IdentityCollisionReason.LinkedToOtherUser);

    [Fact]
    public void L12_AnUnboundLinkedContact_GetsTheUsersOid_AndKeepsItsLink()
        => ContactBindingDecision.DecideExistingLink(SystemUser(), ContactA, NoRows, ContactLookup.Of(Unbound(ContactA)),
                ReferenceLookup.Of(new SystemUserReference(Guid.NewGuid(), Caller, ContactA)))
            .Should().Be(new BindingDecision(BindingAction.BindLinkedContact, ContactA));

    // ── Invite ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Invite_AWorkforceBoundContact_IsRefused()
    {
        var decision = ContactBindingDecision.DecideInvite(
            ContactLookup.Of(Bound(ContactA, Other, IdentityPlaneMarker.Workforce)));

        decision.Action.Should().Be(InviteContactAction.Refuse);
        decision.ReasonCode.Should().Be(ContactBindingDecision.InviteWorkforceBoundContact);
        decision.FlagContactIds.Should().Equal(ContactA);
    }

    [Fact]
    public void Invite_ACiamBoundContact_StaysIdempotent()
        => ContactBindingDecision.DecideInvite(ContactLookup.Of(Bound(ContactA, Other)), ReferenceLookup.Of())
            .Action.Should().Be(InviteContactAction.AlreadyProvisioned);

    [Fact]
    public void Invite_AContactAnInternalUserLinks_IsRefused_EvenWhenCiamBound()
        => ContactBindingDecision.DecideInvite(ContactLookup.Of(Bound(ContactA, Other)),
                ReferenceLookup.Of(new SystemUserReference(Guid.NewGuid(), Caller, ContactA)))
            .ReasonCode.Should().Be(ContactBindingDecision.InviteContactLinkedToInternalUser);

    [Fact]
    public void Invite_AnAmbiguousEmail_IsRefused()
        => ContactBindingDecision.DecideInvite(ContactLookup.Of(Unbound(ContactA), Unbound(ContactB)))
            .ReasonCode.Should().Be(ContactBindingDecision.InviteEmailAmbiguous);

    [Fact]
    public void Invite_AnUnreadableLookup_IsAFailureNotARefusal()
        => ContactBindingDecision.DecideInvite(ContactLookup.Failed).Action.Should().Be(InviteContactAction.Fail);

    [Fact]
    public void Invite_AnUnboundContact_IsProvisioned_AndNoContact_IsCreated()
    {
        ContactBindingDecision.DecideInvite(ContactLookup.Of(Unbound(ContactA)), ReferenceLookup.Of()).Action
            .Should().Be(InviteContactAction.ProvisionExisting);
        ContactBindingDecision.DecideInvite(NoRows).Action.Should().Be(InviteContactAction.CreateContact);
    }

    // ── Flags ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ShouldWriteFlag_IsIdempotentPerParty_AndRecordsASecondIdentity()
    {
        var mine = Party(IdentityCollisionReason.BoundToDifferentOid, Caller);
        ContactBindingDecision.ShouldWriteFlag(null, mine).Should().BeTrue();

        var flag = ContactBindingDecision.FlagWith(null, mine);
        ContactBindingDecision.ShouldWriteFlag(flag, mine with { FlaggedOn = Now.AddHours(3) })
            .Should().BeFalse("a repeated collision must not turn a deny path into a stream of writes");

        // Verifier finding 3: a SECOND identity colliding with an already-flagged contact is recorded, so its
        // collision does not vanish when the first one is resolved.
        var theirs = Party(IdentityCollisionReason.BoundToDifferentOid, Other, IdentityPlaneMarker.External);
        ContactBindingDecision.ShouldWriteFlag(flag, theirs).Should().BeTrue();
        var both = ContactBindingDecision.FlagWith(flag, theirs);
        both.Parties.Should().HaveCount(2);
        both.Primary.IsSameCollision(mine).Should().BeTrue("the summary columns keep the first party");
        ContactBindingDecision.ShouldWriteFlag(both, theirs).Should().BeFalse();
    }

    [Fact]
    public void ShouldWriteFlag_NeverOverwritesAFlagWhosePartiesCannotBeRead_OrOneAtCapacity()
    {
        var mine = Party(IdentityCollisionReason.BoundToDifferentOid, Caller);
        var unreadable = Flag(IdentityCollisionReason.EmailAmbiguous, Other) with { HasUnreadableParties = true };
        ContactBindingDecision.ShouldWriteFlag(unreadable, mine).Should().BeFalse("its unreadable parties would be lost");

        var full = CollisionFlag.FromParties(Enumerable.Range(0, ContactBindingDecision.MaxCollisionParties)
            .Select(_ => Party(IdentityCollisionReason.BoundToDifferentOid, Guid.NewGuid())).ToList())!;
        ContactBindingDecision.ShouldWriteFlag(full, mine).Should().BeFalse();
    }

    // ── ReconcileFlag: keep / prune / clear ──────────────────────────────────────────────────────────

    [Fact]
    public void ReconcileFlag_ClearsOnlyWhenNoPartyHolds_AndNoSystemUserCollidedThisRun()
    {
        var a = Party(IdentityCollisionReason.BoundToDifferentOid, Caller);
        var flag = CollisionFlag.FromParties(new[] { a })!;

        ContactBindingDecision.ReconcileFlag(flag, Array.Empty<CollisionParty>(), collidesThisRun: false).Action
            .Should().Be(FlagReconciliationAction.Clear);
        ContactBindingDecision.ReconcileFlag(flag, Array.Empty<CollisionParty>(), collidesThisRun: true).Action
            .Should().Be(FlagReconciliationAction.Keep, "a collision this run saw but has not recorded is still live");
        ContactBindingDecision.ReconcileFlag(flag, new[] { a }, collidesThisRun: false).Action
            .Should().Be(FlagReconciliationAction.Keep);
    }

    [Fact]
    public void ReconcileFlag_PrunesTheResolvedParty_AndKeepsTheOneThatStillCollides()
    {
        var a = Party(IdentityCollisionReason.LinkedContactBoundToDifferentOid, Caller);
        var b = Party(IdentityCollisionReason.BoundToDifferentOid, Other, IdentityPlaneMarker.External);
        var flag = CollisionFlag.FromParties(new[] { a, b })!;

        var verdict = ContactBindingDecision.ReconcileFlag(flag, new[] { b }, collidesThisRun: false);

        verdict.Action.Should().Be(FlagReconciliationAction.Prune);
        verdict.Remaining!.Parties.Should().ContainSingle().Which.IsSameCollision(b).Should().BeTrue();
        verdict.Remaining.CollidingOid.Should().Be(Other, "the operator's view now names the party that still collides");
    }

    [Fact]
    public void ReconcileFlag_KeepsAFlagWithUnrecordedParties()
    {
        var unreadable = Flag(IdentityCollisionReason.BoundToDifferentOid, Caller) with { HasUnreadableParties = true };
        ContactBindingDecision.ReconcileFlag(unreadable, Array.Empty<CollisionParty>(), false).Action
            .Should().Be(FlagReconciliationAction.Keep);

        var full = CollisionFlag.FromParties(Enumerable.Range(0, ContactBindingDecision.MaxCollisionParties)
            .Select(_ => Party(IdentityCollisionReason.BoundToDifferentOid, Guid.NewGuid())).ToList())!;
        ContactBindingDecision.ReconcileFlag(full, Array.Empty<CollisionParty>(), false).Action
            .Should().Be(FlagReconciliationAction.Keep, "a party past the cap was never recorded");
    }

    [Fact]
    public void CollisionStillHolds_BoundToDifferentOid_ClearsOnceTheOperatorRemovedTheOtherBinding()
    {
        var flag = Flag(IdentityCollisionReason.BoundToDifferentOid, Caller);
        Holds(flag, Bound(ContactA, Other)).Should().BeTrue();
        Holds(flag, Unbound(ContactA)).Should().BeFalse("the collision no longer holds");
        Holds(flag, Bound(ContactA, Caller)).Should().BeFalse("re-bound to the colliding identity by an operator");
    }

    [Fact]
    public void CollisionStillHolds_EmailAmbiguous_ClearsWhenOneDuplicateIsDeactivated()
    {
        var flag = Flag(IdentityCollisionReason.EmailAmbiguous, Caller);
        Holds(flag, Unbound(ContactA), email: ContactLookup.Of(Unbound(ContactA), Unbound(ContactB))).Should().BeTrue();
        Holds(flag, Unbound(ContactA), email: ContactLookup.Of(Unbound(ContactA))).Should().BeFalse();
    }

    [Fact]
    public void CollisionStillHolds_AnInactiveFlaggedContact_IsMoot()
        => Holds(Flag(IdentityCollisionReason.BoundToDifferentOid, Caller), Bound(ContactA, Other, state: 1))
            .Should().BeFalse();

    [Fact]
    public void CollisionStillHolds_AnUnreadableFact_KeepsTheFlag()
        => ContactBindingDecision.CollisionStillHolds(Party(IdentityCollisionReason.BoundToDifferentOid, Caller),
                Unbound(ContactA), ContactLookup.Failed, NoRows, ReferenceLookup.Of())
            .Should().BeTrue("clearing on a failed read is the fail-open direction");

    [Fact]
    public void CollisionStillHolds_AnInviteRefusal_HoldsWhileTheContactIsAnEmployees_AndClearsOnceItIsNot()
    {
        // Verifier finding 2(4): an invite refusal records no oid, so the job re-evaluates it from data every
        // run. Were this branch false, every invite flag would be cleared 5 minutes after it was raised.
        var invite = new CollisionParty(null, IdentityPlaneMarker.External,
            IdentityCollisionReason.InviteMatchesWorkforceContact, Now);

        Holds(invite, Bound(ContactA, Other, IdentityPlaneMarker.Workforce))
            .Should().BeTrue("the contact is still bound to an employee's work identity");
        Holds(invite, Unbound(ContactA), refs: ReferenceLookup.Of(new SystemUserReference(Guid.NewGuid(), Other, ContactA)))
            .Should().BeTrue("the contact is still an internal user's");
        Holds(invite, Bound(ContactA, Other, IdentityPlaneMarker.External))
            .Should().BeFalse("resolved: the operator gave the contact to the external identity");
        Holds(invite, Unbound(ContactA)).Should().BeFalse("resolved: neither an employee's binding nor an internal link");
    }

    [Fact]
    public void CollisionStillHolds_AFlagWithNoReason_IsKept()
        => Holds(new CollisionParty(Caller, IdentityPlaneMarker.Workforce, null, Now), Unbound(ContactA))
            .Should().BeTrue();

    [Fact]
    public void CollisionStillHolds_CiamLinkedToOtherUser_HoldsWhileAnySystemUserLinksIt()
    {
        var flag = Flag(IdentityCollisionReason.LinkedToOtherUser, Caller, IdentityPlaneMarker.External);
        Holds(flag, Unbound(ContactA), refs: ReferenceLookup.Of(new SystemUserReference(Guid.NewGuid(), Other, ContactA)))
            .Should().BeTrue();
        Holds(flag, Unbound(ContactA)).Should().BeFalse();
    }

    // ── Codes ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryCollisionReason_HasItsOwnDistinctCode_InTheAuthMdFormat()
    {
        var codes = Enum.GetValues<IdentityCollisionReason>().Select(ContactBindingDecision.DenyCodeFor).ToList();

        codes.Should().OnlyHaveUniqueItems("a shared code makes two collisions indistinguishable in the audit trail");
        codes.Should().AllSatisfy(c => c.Should().MatchRegex(@"^sdap\.access\.(deny|invite)\.[a-z0-9_]+$"));
    }

    [Theory]
    [InlineData("ralph.schroeder_hotmail.com#EXT#@spaarke.onmicrosoft.com", true)]
    [InlineData("ralph.schroeder@spaarke.com", false)]
    [InlineData(null, false)]
    public void IsGuestDomainName_ReadsTheDirectorySyncedExtMarker(string? domainName, bool guest)
        => ContactBindingDecision.IsGuestDomainName(domainName).Should().Be(guest);

    [Fact]
    public void TheSharedReadOnlyQuery_ReadsTwoActiveRowsByTheBindingColumn()
    {
        var q = ContactBindingDecision.ActiveContactsBoundToQuery(Caller);

        q.EntityName.Should().Be("contact");
        q.TopCount.Should().Be(2, "an ambiguous binding must be visible");
        q.Criteria.Conditions.Should().Contain(c => c.AttributeName == "sprk_externalobjectid"
            && (string)c.Values[0] == Caller.ToString("D"));
        q.Criteria.Conditions.Should().Contain(c => c.AttributeName == "statecode" && (int)c.Values[0] == 0);
        q.Criteria.Conditions.Should().NotContain(c => c.AttributeName == "azureactivedirectoryobjectid");
    }

    private static bool Holds(CollisionFlag flag, ContactBindingRow flagged, ContactLookup? email = null,
        ContactLookup? oid = null, ReferenceLookup? refs = null)
        => Holds(flag.Primary, flagged, email, oid, refs);

    private static bool Holds(CollisionParty party, ContactBindingRow flagged, ContactLookup? email = null,
        ContactLookup? oid = null, ReferenceLookup? refs = null)
        => ContactBindingDecision.CollisionStillHolds(party, flagged, email ?? NoRows, oid ?? NoRows, refs ?? ReferenceLookup.Of());

    private static CollisionParty Party(IdentityCollisionReason reason, Guid oid,
        IdentityPlaneMarker plane = IdentityPlaneMarker.Workforce)
        => new(oid, plane, reason, Now);
}
