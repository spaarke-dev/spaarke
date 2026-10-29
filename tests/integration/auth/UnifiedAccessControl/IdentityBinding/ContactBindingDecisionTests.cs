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

    private static BindingRequest Ciam(string? email = Email, string? code = null)
        => new(BindingPlane.CiamToken, Caller, email, BindingEligibility.EmailBindOnly) { InviteCode = code };

    // Task 185: ContactA's current invite (its code goes in the email; only the stored value is on the contact).
    private static readonly (string Code, string StoredValue) InviteA = InviteCode.Create(ContactA);

    private static readonly BindingDecision BindsAndConsumes =
        new(BindingAction.BindByEmail, ContactA) { ConsumeInviteCode = true };

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
    [InlineData("aaaaaaaa-0000-4000-8000-000000000001", 100000002, BindingKind.Bound)] // r3 047: WorkforceHome
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
        // One match proceeds to E11, which on the Workforce token plane needs the invite stamp naming this caller's tenant.
        => ContactBindingDecision.Decide(Workforce() with { TokenTenantId = InvitedTenant }, NoRows,
                ContactLookup.Of(Unbound(ContactA)), ReferenceLookup.Of(), Stamped(Intent(invitedOn: Now), InvitedTenant))
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
        // Task 184: on the CIAM plane this bind also needs this environment's intent; task 185: a pending invite AND its
        // code. The workforce form of this row is E11_TheWorkforceAndSystemUserPlanes_BindWithoutAnyIntent, unchanged.
        => ContactBindingDecision.Decide(Ciam(code: InviteA.Code), NoRows, ContactLookup.Of(Unbound(ContactA)),
                ReferenceLookup.Of(), Invited())
            .Should().Be(BindsAndConsumes);

    // ── E11 intent gate (task 184, owner round 96): the CIAM plane binds only on THIS environment's intent ─────

    // Task 185 slice B2: the lookup carries no grant at all (option (c)): the store no longer reads one, so a grant can
    // neither bind nor deny. Pinned by TheIntentLookup_CarriesNoGrant; the binder-level twins seed a grant and show it
    // changes nothing (CiamContactBindingTests.AGrantHere_ChangesNoE11Outcome).
    private static BindIntentLookup Intent(
        DateTimeOffset? invitedOn = null, LookupStatus inviteStatus = LookupStatus.Read, Guid? contact = null,
        DateTimeOffset? asOf = null, string? hash = null)
        => new(contact ?? ContactA, inviteStatus, invitedOn) { AsOf = asOf ?? Now, InviteCodeHash = hash };

    /// <summary>The tenant an invite named, in the Workforce rows.</summary>
    private static readonly Guid InvitedTenant = Guid.Parse("11111111-aaaa-4aaa-8aaa-111111111111");

    /// <summary>
    /// <paramref name="intent"/> with the invite stamp an invite to <see cref="Email"/> wrote (naming
    /// <paramref name="tenant"/>), and the contact's current email still <see cref="Email"/>.
    /// </summary>
    private static BindIntentLookup Stamped(BindIntentLookup intent, Guid? tenant = null)
        => intent with
        {
            InvitedEmailHash = InviteEmailStamp.Create(Email),
            RawInvitedTenantId = tenant?.ToString("D"),
            CurrentEmail = Email,
        };

    /// <summary>
    /// ContactA's current invite: a marker at <paramref name="invitedOn"/> (default now), its stored code hash, and the
    /// invite stamp for <see cref="Email"/>.
    /// </summary>
    private static BindIntentLookup Invited(DateTimeOffset? invitedOn = null)
        => Stamped(Intent(invitedOn: invitedOn ?? Now, hash: InviteA.StoredValue));

    private static BindingDecision CiamE11(BindIntentLookup? intent, string? code = null)
        => ContactBindingDecision.Decide(Ciam(code: code), NoRows, ContactLookup.Of(Unbound(ContactA)), ReferenceLookup.Of(),
            intent);

    /// <summary>
    /// Task 185 (design note §5.1): the operator-only reason for each not-invited deny, in DecideIntent's order; none for a
    /// bind or a fault. The reason never carries the code or the hash (it is one of five fixed words).
    /// </summary>
    [Theory]
    [InlineData("no-marker")]
    [InlineData("column-missing")]
    [InlineData("marker-expired")]
    [InlineData("no-stored-hash")]
    [InlineData("no-code-presented")]
    [InlineData("code-mismatch")]
    [InlineData("binds")]
    [InlineData("faulted-read")]
    public void E11_Ciam_EachNotInvitedDeny_HasItsOperatorReason_AndOnlyThose(string row)
    {
        var (intent, code, expected) = row switch
        {
            "no-marker" => (Intent(invitedOn: null, hash: InviteA.StoredValue), InviteA.Code, "no-marker"),
            "column-missing" => (Intent(inviteStatus: LookupStatus.ColumnMissing), InviteA.Code, "no-marker"),
            "marker-expired" => (Invited(Now.AddDays(-31)), InviteA.Code, "marker-expired"),
            "no-stored-hash" => (Intent(invitedOn: Now), InviteA.Code, "no-stored-hash"),
            "no-code-presented" => (Invited(), (string?)null, "no-code-presented"),
            "code-mismatch" => (Invited(), InviteCode.Create(ContactA).Code, "code-mismatch"),
            "binds" => (Invited(), InviteA.Code, (string?)null),
            _ => (Intent(inviteStatus: LookupStatus.Failed), InviteA.Code, (string?)null),
        };

        ContactBindingDecision.NotInvitedReason(Ciam(code: code), intent, ContactA).Should().Be(expected);
        var deniedNotInvited = CiamE11(intent, code).DenyCode == ContactBindingDecision.DenyContactNotInvited;
        deniedNotInvited.Should().Be(expected is not null, "a reason exists exactly when the leg denies not-invited");
    }

    // ── The marker's 30-day window and its clock-skew tolerance (owner round 97) ─────────────────────────────

    /// <summary>
    /// A marker counts while it is at most 30 days old (exactly 30 days binds) and at most 5 minutes in the future (exactly
    /// 5 minutes binds). Older, or later than the skew, it is "no invite evidence": even with the right code (task 185) the
    /// deny is <c>contact_not_invited</c>, never a fault, and nothing is flagged.
    /// MUTATION PROOFS: dropping the window fails <c>30d+1s</c> and <c>expired-a-year</c>; <c>&lt;=</c> to <c>&lt;</c> at
    /// the boundary fails <c>exactly-30d</c>; flipping the age comparison fails <c>fresh</c> and <c>exactly-30d</c>; ignoring
    /// the future check fails <c>skew+1s</c> and <c>far-future</c>.
    /// </summary>
    [Theory]
    [InlineData("fresh", 0, true)]
    [InlineData("an-hour-old", -3_600, true)]
    [InlineData("exactly-30d", -30 * 86_400, true)]
    [InlineData("30d+1s", -30 * 86_400 - 1, false)]
    [InlineData("expired-a-year", -365 * 86_400, false)]
    [InlineData("within-skew", 60, true)]
    [InlineData("exactly-skew", 5 * 60, true)]
    [InlineData("skew+1s", 5 * 60 + 1, false)]
    [InlineData("far-future", 10 * 86_400, false)]
    public void E11_Ciam_AMarker_CountsOnlyWithinItsWindow(string label, int offsetSeconds, bool binds)
    {
        var decision = CiamE11(Invited(Now.AddSeconds(offsetSeconds)), InviteA.Code);

        if (binds)
        {
            decision.Should().Be(BindsAndConsumes, label);
        }
        else
        {
            decision.Action.Should().Be(BindingAction.Deny, label);
            decision.DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited,
                "an expired or far-future marker is no invite evidence — not a fault ({0})", label);
            decision.FlagContactIds.Should().BeNullOrEmpty();
        }
    }

    /// <summary>
    /// Task 185 (owner round 98, option (c); design §6): a grant no longer binds, so it does not extend a code past its
    /// marker's 30 days. Before 185 this test was <c>..._WithAnActiveGrant_BindsThroughTheGrant</c>. Slice B2 removed the
    /// grant from the lookup, so the grant half lives in the binder-level twin (a seeded grant, the same marker).
    /// </summary>
    [Fact]
    public void E11_Ciam_AnExpiredMarker_AndTheRightCode_IsDenied_AGrantCannotExtendACode()
        => CiamE11(Invited(Now.AddDays(-90)), InviteA.Code)
            .DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);

    /// <summary>Task 185 (design §6): before 185 this was <c>..._WithAnActiveGrant_StillBindsThroughTheGrant</c>.</summary>
    [Fact]
    public void E11_Ciam_AFarFutureMarker_AndTheRightCode_IsDenied()
        => CiamE11(Invited(Now.AddYears(5)), InviteA.Code)
            .DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);

    /// <summary>
    /// Task 185 slice B2 (option (c)): the intent lookup has no grant member at all, so no grant fact can reach the
    /// decision — neither as evidence nor as a fault. A grant field put back would have to delete this test.
    /// </summary>
    [Fact]
    public void TheIntentLookup_CarriesNoGrant()
        => typeof(BindIntentLookup)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance)
            .Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Grant", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void E11_Ciam_ALookupWithNoDecisionTime_CountsNoMarker()
        // Fail closed: a caller that forgets to stamp AsOf sees every marker as far-future, never as current, even with the
        // right code.
        => CiamE11(new BindIntentLookup(ContactA, LookupStatus.Read, Now)
        { InviteCodeHash = InviteA.StoredValue }, InviteA.Code)
            .DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);

    [Fact]
    public void TheWindowAndTheSkew_AreTheOwnersNumbers()
    {
        ContactBindingDecision.InviteMarkerWindow.Should().Be(TimeSpan.FromDays(30), "owner round 97");
        ContactBindingDecision.InviteMarkerClockSkew.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void E11_Ciam_AsksForTheIntent_BeforeItBinds()
        => CiamE11(intent: null).Should().Be(new BindingDecision(BindingAction.NeedIntentLookup, ContactA));

    /// <summary>
    /// Task 185 (owner round 98, option (c); design §6): a grant with no invite is no bind evidence, with or without a code.
    /// Before 185 this test was <c>E11_Ciam_AnActiveGrantHere_Binds</c>. Slice B2: the grant is no longer in the lookup
    /// (<see cref="TheIntentLookup_CarriesNoGrant"/>); the binder-level twin seeds one
    /// (<c>CiamContactBindingTests.AnUnboundContactWithOnlyAnActiveGrantHere_IsDeniedNotInvited_AndNothingIsWritten</c>).
    /// </summary>
    [Fact]
    public void E11_Ciam_NoMarker_IsDeniedNotInvited_WithOrWithoutACode()
    {
        CiamE11(Intent()).Should()
            .Be(new BindingDecision(BindingAction.Deny, DenyCode: ContactBindingDecision.DenyContactNotInvited));
        CiamE11(Intent(hash: InviteA.StoredValue), InviteA.Code).DenyCode
            .Should().Be(ContactBindingDecision.DenyContactNotInvited, "a code without a current marker never binds");
    }

    [Fact]
    public void E11_Ciam_APendingInviteHere_Binds()
        => CiamE11(Invited(), InviteA.Code).Should().Be(BindsAndConsumes);

    // ── Task 185 (owner round 98): the marker leg requires the invite code ──────────────────────────────────────
    // MUTATION PROOF (i): InviteCode.Matches dropped from DecideIntent fails every "denied" test in this block.

    [Fact]
    public void E11_Ciam_APendingInvite_WithNoCode_IsDeniedNotInvited_AndFlagsNothing()
    {
        // AC1: a squatted CIAM account carrying the invitee's email presents no code.
        var decision = CiamE11(Invited(), code: null);

        decision.Should().Be(new BindingDecision(BindingAction.Deny, DenyCode: ContactBindingDecision.DenyContactNotInvited));
        decision.FlagContactIds.Should().BeNull();
    }

    [Fact]
    public void E11_Ciam_APendingInvite_WithAWrongCode_IsDeniedNotInvited()
        => CiamE11(Invited(), InviteCode.Create(ContactA).Code).DenyCode
            .Should().Be(ContactBindingDecision.DenyContactNotInvited);

    [Fact]
    public void E11_Ciam_ACodeHashedForAnotherContact_IsDeniedNotInvited()
    {
        // ContactB's invite stored on ContactA: the contact id is in the hash input, so one code opens one contact.
        var invitedB = InviteCode.Create(ContactB);

        CiamE11(Intent(invitedOn: Now, hash: invitedB.StoredValue), invitedB.Code).DenyCode
            .Should().Be(ContactBindingDecision.DenyContactNotInvited);
    }

    [Fact]
    public void E11_Ciam_ASupersededCode_IsDenied_TheReInvitesCodeBinds()
    {
        // A re-invite overwrote the marker and the hash; the earlier email's code is presented.
        var superseded = InviteCode.Create(ContactA);
        var reInvite = InviteCode.Create(ContactA);
        var intent = Stamped(Intent(invitedOn: Now, hash: reInvite.StoredValue));

        CiamE11(intent, superseded.Code).DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
        CiamE11(intent, reInvite.Code).Should().Be(BindsAndConsumes);
    }

    [Fact]
    public void E11_Ciam_AnExpiredMarker_WithTheRightCode_IsDeniedNotInvited()
        // MUTATION PROOF (iii): IsInviteMarkerCurrent dropped from DecideIntent fails this test (and the window theory).
        => CiamE11(Invited(Now - ContactBindingDecision.InviteMarkerWindow - TimeSpan.FromSeconds(1)), InviteA.Code)
            .DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);

    [Fact]
    public void E11_Ciam_TheRightCode_UnderAMarkerFromTheFutureBeyondTheSkew_IsDeniedNotInvited()
        => CiamE11(Invited(Now + ContactBindingDecision.InviteMarkerClockSkew + TimeSpan.FromSeconds(1)), InviteA.Code)
            .DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);

    [Fact]
    public void E11_Ciam_AMarkerWithNoStoredHash_APre185Invite_IsDeniedNotInvited()
        => CiamE11(Intent(invitedOn: Now, hash: null), InviteA.Code).DenyCode
            .Should().Be(ContactBindingDecision.DenyContactNotInvited);

    [Theory]
    [InlineData("")]
    [InlineData("not-a-code")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void E11_Ciam_AMalformedPresentedCode_IsDeniedNotInvited(string presented)
        => CiamE11(Invited(), presented).DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);

    [Theory]
    [InlineData("")]
    [InlineData("v2.not-a-hash")]
    [InlineData("v1.")]
    public void E11_Ciam_AMalformedStoredHash_IsDeniedNotInvited(string storedValue)
        => CiamE11(Intent(invitedOn: Now, hash: storedValue), InviteA.Code).DenyCode
            .Should().Be(ContactBindingDecision.DenyContactNotInvited);

    [Fact]
    public void E11_Ciam_ATruncatedOrVersionChangedStoredHash_IsDeniedNotInvited()
    {
        CiamE11(Intent(invitedOn: Now, hash: InviteA.StoredValue[..^1]), InviteA.Code).DenyCode
            .Should().Be(ContactBindingDecision.DenyContactNotInvited);
        CiamE11(Intent(invitedOn: Now, hash: "v2" + InviteA.StoredValue[2..]), InviteA.Code).DenyCode
            .Should().Be(ContactBindingDecision.DenyContactNotInvited);
    }

    [Fact]
    public void TheCodeAndTheHash_AreNeverPrintedByTheRecords()
    {
        // Never log a code or a hash (CLAUDE.md §9): the compiler-generated ToString of either record leaves them out.
        Ciam(code: InviteA.Code).ToString().Should().NotContain(InviteA.Code);
        Invited().ToString().Should().NotContain(InviteA.StoredValue);
    }

    [Fact]
    public void E11_Ciam_NoGrantAndNoInvite_IsDeniedNotInvited_AndFlagsNothing()
    {
        // MUTATION (remove the intent gate): this test fails — the decision would answer BindByEmail.
        var decision = CiamE11(Intent());

        decision.Should().Be(new BindingDecision(BindingAction.Deny, DenyCode: ContactBindingDecision.DenyContactNotInvited));
        decision.FlagContactIds.Should().BeNull("no other person is involved, so there is no collision to flag");
    }

    /// <summary>
    /// A marker read that faulted never binds, even with the right code. Task 185 (design §6): the grant-fault rows were
    /// removed with the grant leg (no grant is read); the marker-fault case stays.
    /// </summary>
    [Fact]
    public void E11_Ciam_AFaultedIntentRead_NeverBinds_EvenWithTheRightCode()
    {
        // MUTATION (treat a fault as intent): this test fails.
        var decision = CiamE11(Intent(invitedOn: Now, inviteStatus: LookupStatus.Failed,
            hash: InviteA.StoredValue), InviteA.Code);

        decision.Action.Should().Be(BindingAction.Deny);
        decision.DenyCode.Should().Be(ContactBindingDecision.DenyContactLookupFailed);
    }

    /// <summary>
    /// A marker (or hash) column not provisioned yet is no invite evidence, and (task 185, design §6) no grant decides
    /// instead. Before 185 this test was <c>..._AndTheGrantLegStillDecides</c>.
    /// </summary>
    [Fact]
    public void E11_Ciam_AMarkerColumnNotProvisionedYet_IsNoInviteEvidence()
    {
        CiamE11(Intent(invitedOn: Now, inviteStatus: LookupStatus.ColumnMissing, hash: InviteA.StoredValue), InviteA.Code).DenyCode
            .Should().Be(ContactBindingDecision.DenyContactNotInvited);
        CiamE11(Intent(inviteStatus: LookupStatus.ColumnMissing)).DenyCode.Should().Be(ContactBindingDecision.DenyContactNotInvited);
    }

    [Fact]
    public void E11_Ciam_IntentReadForAnotherContact_IsNeverUsed()
        => CiamE11(Intent(invitedOn: Now, contact: ContactB, hash: InviteA.StoredValue), InviteA.Code).DenyCode
            .Should().Be(ContactBindingDecision.DenyContactLookupFailed);

    [Fact]
    public void E11_TheSystemUserPlane_BindsWithoutAnyIntent()
    {
        // The systemuser plane's email is directory-synced from a licensed user: it never asks for intent, and a "no intent"
        // answer handed to it changes nothing. Before the invite stamp this row also covered the Workforce token plane, which
        // now needs the stamp (E11_TheWorkforcePlane_NeedsTheInviteStamp).
        var request = new BindingRequest(BindingPlane.SystemUser, Caller, Email, BindingEligibility.EmailBindAndCreate);
        var rows = ContactLookup.Of(Unbound(ContactA));

        ContactBindingDecision.Decide(request, NoRows, rows, ReferenceLookup.Of()).Action.Should().Be(BindingAction.BindByEmail);
        ContactBindingDecision.Decide(request, NoRows, rows, ReferenceLookup.Of(), Intent()).Action
            .Should().Be(BindingAction.BindByEmail);
    }

    /// <summary>
    /// The Workforce token plane binds by email only on a current invite stamp: it asks for the intent, a lookup with no
    /// stamp is denied not-invited, and a stamp naming the caller's tenant binds (without consuming a code).
    /// </summary>
    [Fact]
    public void E11_TheWorkforcePlane_NeedsTheInviteStamp()
    {
        var request = Workforce() with { TokenTenantId = InvitedTenant };
        var rows = ContactLookup.Of(Unbound(ContactA));

        ContactBindingDecision.Decide(request, NoRows, rows, ReferenceLookup.Of())
            .Should().Be(new BindingDecision(BindingAction.NeedIntentLookup, ContactA));
        ContactBindingDecision.Decide(request, NoRows, rows, ReferenceLookup.Of(), Intent(invitedOn: Now)).DenyCode
            .Should().Be(ContactBindingDecision.DenyContactNotInvited);
        ContactBindingDecision.Decide(request, NoRows, rows, ReferenceLookup.Of(), Stamped(Intent(invitedOn: Now), InvitedTenant))
            .Should().Be(new BindingDecision(BindingAction.BindByEmail, ContactA));
    }

    [Theory]
    [InlineData(BindingPlane.WorkforceToken)]
    [InlineData(BindingPlane.SystemUser)]
    public void E11_TheNonCiamPlanes_BypassTheCodeCheck_AndNeverConsumeACode(BindingPlane plane)
    {
        // Task 185 changes nothing outside the CIAM block (owner round 98, R3's request): a presented code, a wrong code or
        // none changes nothing on these planes, and their bind never asks the binder to consume a code. (The Workforce
        // token plane needs the invite stamp, given here; the code is still not checked.)
        var rows = ContactLookup.Of(Unbound(ContactA));
        var plain = new BindingDecision(BindingAction.BindByEmail, ContactA);
        var stamped = Stamped(Invited(), InvitedTenant);

        foreach (var code in new[] { null, InviteA.Code, InviteCode.Create(ContactA).Code })
        {
            var request = new BindingRequest(plane, Caller, Email, BindingEligibility.EmailBindAndCreate)
            {
                InviteCode = code,
                TokenTenantId = InvitedTenant,
            };
            if (plane == BindingPlane.SystemUser)
            {
                ContactBindingDecision.Decide(request, NoRows, rows, ReferenceLookup.Of()).Should().Be(plain);
            }

            ContactBindingDecision.Decide(request, NoRows, rows, ReferenceLookup.Of(), stamped).Should().Be(plain);
        }
    }

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

    [Theory]
    [InlineData(IdentityPlaneMarker.Workforce)]
    [InlineData(IdentityPlaneMarker.WorkforceHome)] // r3 047: a home-registered employee is never re-invited as a partner
    public void Invite_AWorkforceBoundContact_IsRefused(IdentityPlaneMarker plane)
    {
        var decision = ContactBindingDecision.DecideInvite(
            ContactLookup.Of(Bound(ContactA, Other, plane)));

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

    // ── r3 047 fix round 2: a DEACTIVATED contact carrying the token email refuses and flags the registration ──────

    [Fact]
    public void DecideInactiveRegistrationEmail_AnInactiveMatch_IsAConflict_FlaggedInactiveContactEmailMatch_OverEveryInactiveRow()
    {
        var rows = new[] { Unbound(ContactA, state: 1), Unbound(ContactB, state: 1) };

        var decision = ContactBindingDecision.DecideInactiveRegistrationEmail(ContactLookup.Of(rows));

        decision.Action.Should().Be(RegistrationEmailAction.Conflict);
        decision.FlagReason.Should().Be(IdentityCollisionReason.InactiveContactEmailMatch);
        decision.FlagContactIds.Should().BeEquivalentTo(new[] { ContactA, ContactB });
    }

    [Fact]
    public void DecideInactiveRegistrationEmail_NoInactiveRow_Creates_AndAFailedReadDenies()
    {
        ContactBindingDecision.DecideInactiveRegistrationEmail(NoRows).Action.Should().Be(RegistrationEmailAction.Create);
        ContactBindingDecision.DecideInactiveRegistrationEmail(ContactLookup.Failed).Action
            .Should().Be(RegistrationEmailAction.Deny, "an inactive match cannot be ruled out, so nothing is created");
    }

    [Fact]
    public void CollisionStillHolds_InactiveContactEmailMatch_HoldsWhileInactiveAndCarryingAnEmail_ClearsWhenReactivatedOrEmailGone()
    {
        var party = Party(IdentityCollisionReason.InactiveContactEmailMatch, Caller, IdentityPlaneMarker.WorkforceHome);
        var inactive = Unbound(ContactA, state: 1) with { Email = "Mike.Okafor@Customer.example" };

        Holds(party, inactive).Should().BeTrue("the inactive contact still carries the email: the job must NOT clear it");
        Holds(party, inactive with { StateCode = 0 }).Should().BeFalse("an operator reactivated the contact: resolved");
        Holds(party, inactive with { Email = null }).Should().BeFalse("the email is gone from the contact: resolved");
        Holds(party, inactive with { Email = "  " }).Should().BeFalse();
        ContactBindingDecision.CollisionStillHolds(party, inactive, ContactLookup.Failed, NoRows, ReferenceLookup.Of())
            .Should().BeTrue("a failed read keeps the party (fail closed)");
    }

    [Fact]
    public void CollisionStillHolds_Control_EveryOtherReason_IsClearedOnAnInactiveContact()
    {
        // The control for the case above: without the dedicated reason an inactive flagged contact is cleared (the person was
        // removed) - which is why an existing reason could not carry the deactivated-contact refusal.
        var inactive = Unbound(ContactA, state: 1) with { Email = "mike.okafor@customer.example" };
        foreach (var reason in new[]
                 {
                     IdentityCollisionReason.BoundToDifferentOid, IdentityCollisionReason.EmailAmbiguous,
                     IdentityCollisionReason.BindingUnreadable, IdentityCollisionReason.LinkedToOtherUser,
                 })
        {
            Holds(Party(reason, Caller, IdentityPlaneMarker.WorkforceHome), inactive)
                .Should().BeFalse($"{reason} is moot once the contact is deactivated");
        }
    }

    // ── r3 047 fix round 1 (F-A): a single unbindable registration match is flagged by CAUSE ──────────

    private static ContactBindingRow UnreadableBinding(Guid id) => new(id, 0, null, (int)IdentityPlaneMarker.Workforce);

    private static readonly SystemUserReference LinkedToA = new(Guid.Parse("dddddddd-0000-4000-8000-00000000000d"), Other, ContactA);

    [Theory]
    [InlineData("bound-workforce", IdentityCollisionReason.BoundToDifferentOid)]
    [InlineData("bound-external", IdentityCollisionReason.BoundToDifferentOid)]
    [InlineData("bound-workforcehome", IdentityCollisionReason.BoundToDifferentOid)]
    [InlineData("unreadable", IdentityCollisionReason.BindingUnreadable)]
    [InlineData("linked", IdentityCollisionReason.LinkedToOtherUser)]
    [InlineData("two", IdentityCollisionReason.EmailAmbiguous)]
    public void DecideRegistrationEmail_UnbindableMatch_IsFlaggedByCause_AndOnlyTwoOrMoreAreEmailAmbiguous(
        string shape, IdentityCollisionReason expected)
    {
        var rows = shape switch
        {
            "bound-workforce" => new[] { Bound(ContactA, Other, IdentityPlaneMarker.Workforce) },
            "bound-external" => new[] { Bound(ContactA, Other, IdentityPlaneMarker.External) },
            "bound-workforcehome" => new[] { Bound(ContactA, Other, IdentityPlaneMarker.WorkforceHome) },
            "unreadable" => new[] { UnreadableBinding(ContactA) },
            "two" => new[] { Unbound(ContactA), Unbound(ContactB) },
            _ => new[] { Unbound(ContactA) },
        };
        var references = shape == "linked" ? ReferenceLookup.Of(LinkedToA) : ReferenceLookup.Of();

        var decision = ContactBindingDecision.DecideRegistrationEmail(ContactLookup.Of(rows), Caller, references);

        decision.Action.Should().Be(RegistrationEmailAction.Conflict);
        decision.FlagReason.Should().Be(expected);
        decision.FlagContactIds.Should().BeEquivalentTo(rows.Select(r => r.ContactId));
    }

    [Theory]
    [InlineData(IdentityCollisionReason.BoundToDifferentOid)]
    [InlineData(IdentityCollisionReason.BindingUnreadable)]
    [InlineData(IdentityCollisionReason.LinkedToOtherUser)]
    public void CollisionStillHolds_ASingleMatchReason_HoldsForAWorkforceHomeParty_WhileTheCauseHolds(IdentityCollisionReason reason)
    {
        // The registering party is a WorkforceHome identity whose home oid is never a systemuser's or the other binding's.
        var party = Party(reason, Caller, IdentityPlaneMarker.WorkforceHome);
        var oneCarrier = Unbound(ContactA);
        var (flagged, refs) = reason switch
        {
            IdentityCollisionReason.BoundToDifferentOid => (Bound(ContactA, Other, IdentityPlaneMarker.WorkforceHome), ReferenceLookup.Of()),
            IdentityCollisionReason.BindingUnreadable => (UnreadableBinding(ContactA), ReferenceLookup.Of()),
            _ => (oneCarrier, ReferenceLookup.Of(LinkedToA)),
        };

        // ONE active carrier of the email: the old EmailAmbiguous reason would have been cleared here.
        Holds(party, flagged, email: ContactLookup.Of(flagged), refs: refs)
            .Should().BeTrue("the cause still holds, so the reconciliation job must keep the flag");

        // Control: once the cause is gone it clears (the job is not made inert).
        Holds(party, Unbound(ContactA), email: ContactLookup.Of(Unbound(ContactA)))
            .Should().BeFalse("the cause is gone");
    }

    [Fact]
    public void CollisionStillHolds_EmailAmbiguousForAWorkforceHomeParty_StillClearsWhenOnlyOneCarrierRemains()
    {
        var party = Party(IdentityCollisionReason.EmailAmbiguous, Caller, IdentityPlaneMarker.WorkforceHome);
        Holds(party, Unbound(ContactA), email: ContactLookup.Of(Unbound(ContactA), Unbound(ContactB))).Should().BeTrue();
        Holds(party, Unbound(ContactA), email: ContactLookup.Of(Unbound(ContactA))).Should().BeFalse();
    }

    // ── B2 (owner round 4 item 4): the uniqueness mirror ─────────────────────────────────────────────────

    [Fact]
    public void MirrorHeldWithoutBinding_IsTrueOnlyWhenTheMirrorCarriesTheOid_AndTheBindingDoesNot()
    {
        var mirrorOnly = Unbound(ContactA) with { RawKeyMirror = Caller.ToString().ToUpperInvariant() };
        var mirrorOverOtherBinding = Bound(ContactA, Other) with { RawKeyMirror = Caller.ToString("D") };
        var mirrorOverUnreadable = new ContactBindingRow(ContactA, 0, null, (int)IdentityPlaneMarker.Workforce,
            RawKeyMirror: Caller.ToString("D"));
        var own = Bound(ContactA, Caller) with { RawKeyMirror = Caller.ToString("D") };

        ContactBindingDecision.MirrorHeldWithoutBinding(mirrorOnly, Caller).Should().BeTrue("a squatted mirror");
        ContactBindingDecision.MirrorHeldWithoutBinding(mirrorOverOtherBinding, Caller).Should().BeTrue();
        ContactBindingDecision.MirrorHeldWithoutBinding(mirrorOverUnreadable, Caller).Should().BeTrue();
        ContactBindingDecision.MirrorHeldWithoutBinding(own, Caller).Should().BeFalse("the mirror matches its own binding");
        ContactBindingDecision.MirrorHeldWithoutBinding(Unbound(ContactA), Caller).Should().BeFalse("no mirror");
        ContactBindingDecision.MirrorHeldWithoutBinding(mirrorOnly, Other).Should().BeFalse("another oid's slot");
        ContactBindingDecision.MirrorHeldWithoutBinding(mirrorOnly with { RawKeyMirror = "not-a-guid" }, Caller)
            .Should().BeFalse();
    }

    [Fact]
    public void CollisionStillHolds_AKeyMirrorConflict_HoldsWhileTheHolderKeepsTheSlot_EvenWhenInactive()
    {
        // The unique index counts inactive rows: deactivating the holder does NOT free the oid, so — unlike every
        // other reason — an inactive holder keeps the flag.
        var party = Party(IdentityCollisionReason.KeyMirrorConflict, Caller);
        var squat = Unbound(ContactA) with { RawKeyMirror = Caller.ToString("D") };

        Holds(party, squat).Should().BeTrue();
        Holds(party, squat with { StateCode = 1 }).Should().BeTrue("an inactive holder still holds the index slot");
        Holds(party, Unbound(ContactA)).Should().BeFalse("resolved: an operator cleared the mirror");
        Holds(party, Bound(ContactA, Caller) with { RawKeyMirror = Caller.ToString("D") })
            .Should().BeFalse("resolved: the holder is now that identity's contact");
        ContactBindingDecision.CollisionStillHolds(party, squat, ContactLookup.Failed, NoRows, ReferenceLookup.Failed)
            .Should().BeTrue("decided from the holder row alone");
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
    public void TheSharedReadOnlyQuery_ReadsTwoRowsInAnyState_ByTheBindingColumn()
    {
        // The SAME question the binder's oid lookup asks (any statecode, two rows): an inactive second contact on
        // the caller's oid must be visible to the read-only consumers too (verifier finding 6).
        var q = ContactBindingDecision.ContactsBoundToQuery(Caller);

        q.EntityName.Should().Be("contact");
        q.TopCount.Should().Be(2, "an ambiguous binding must be visible");
        q.Criteria.Conditions.Should().Contain(c => c.AttributeName == "sprk_externalobjectid"
            && (string)c.Values[0] == Caller.ToString("D"));
        q.Criteria.Conditions.Should().NotContain(c => c.AttributeName == "statecode",
            "filtering to active rows hides an inactive contact that makes the oid ambiguous");
        q.ColumnSet.Columns.Should().Contain("statecode", "the decision needs each row's state");
        q.Criteria.Conditions.Should().NotContain(c => c.AttributeName == "azureactivedirectoryobjectid");
    }

    // ── The read-only consumers' answer is the binder's answer (verifier finding 6) ───────────────────

    [Fact]
    public void DecideBoundContact_AnActiveAndAnInactiveContactOnOneOid_IsAmbiguous_ExactlyAsTheBinderSays()
    {
        var oid = ContactLookup.Of(Bound(ContactA, Caller), Bound(ContactB, Caller, state: 1));

        var reader = ContactBindingDecision.DecideBoundContact(oid);
        var binder = ContactBindingDecision.Decide(Workforce(), oid);

        reader!.Action.Should().Be(BindingAction.Collision);
        reader.DenyCode.Should().Be(ContactBindingDecision.DenyContactOidAmbiguous);
        reader.ContactId.Should().BeNull("never the active one of two");
        (binder.Action, binder.DenyCode).Should().Be((reader.Action, reader.DenyCode),
            "a reader and the binder must give the same answer to the same rows");
    }

    [Fact]
    public void DecideBoundContact_ResolvesOnlyTheOneActiveContact_AndDeniesAnInactiveOne()
    {
        ContactBindingDecision.DecideBoundContact(ContactLookup.Of(Bound(ContactA, Caller)))
            .Should().Be(new BindingDecision(BindingAction.ResolveByOid, ContactA));
        ContactBindingDecision.DecideBoundContact(ContactLookup.Of(Bound(ContactA, Caller, state: 1)))!
            .DenyCode.Should().Be(ContactBindingDecision.DenyContactInactive);
        ContactBindingDecision.DecideBoundContact(NoRows).Should().BeNull("no contact carries the oid");
        ContactBindingDecision.DecideBoundContact(ContactLookup.Failed)!
            .DenyCode.Should().Be(ContactBindingDecision.DenyContactLookupFailed);
    }

    [Fact]
    public void BoundContactLookup_ReadsTheStateFromTheRow_AndAMissingStateIsNotActive()
    {
        var active = new Microsoft.Xrm.Sdk.Entity("contact", ContactA)
        {
            ["statecode"] = new Microsoft.Xrm.Sdk.OptionSetValue(0),
            ["sprk_externalobjectid"] = Caller.ToString("D"),
        };
        var noState = new Microsoft.Xrm.Sdk.Entity("contact", ContactB) { ["sprk_externalobjectid"] = Caller.ToString("D") };

        var lookup = ContactBindingDecision.BoundContactLookup(new[] { active, noState });

        lookup.Status.Should().Be(LookupStatus.Read);
        lookup.Rows.Should().HaveCount(2);
        lookup.Rows[0].IsActive.Should().BeTrue();
        lookup.Rows[1].IsActive.Should().BeFalse("ADR-003: only statecode = 0 resolves");
        ContactBindingDecision.DecideBoundContact(ContactBindingDecision.BoundContactLookup(new[] { noState }))!
            .DenyCode.Should().Be(ContactBindingDecision.DenyContactInactive);
    }

    private static bool Holds(CollisionFlag flag, ContactBindingRow flagged, ContactLookup? email = null,
        ContactLookup? oid = null, ReferenceLookup? refs = null)
        => Holds(flag.Primary, flagged, email, oid, refs);

    private static bool Holds(CollisionParty party, ContactBindingRow flagged, ContactLookup? email = null,
        ContactLookup? oid = null, ReferenceLookup? refs = null)
        => ContactBindingDecision.CollisionStillHolds(party, flagged, email ?? NoRows, oid ?? NoRows, refs ?? ReferenceLookup.Of());

    // ── r3 task 048: the A9 link write (DecideHomeLinkWrite) ──────────────────────────────────────────

    private static readonly Guid LinkTenant = Guid.Parse("dddddddd-0000-4000-8000-0000000000d1");
    private static readonly Guid LinkOid = Guid.Parse("dddddddd-0000-4000-8000-0000000000d2");
    private static readonly Guid GuestUser = Guid.Parse("eeeeeeee-0000-4000-8000-0000000000e1");
    private static readonly Guid OtherUser = Guid.Parse("eeeeeeee-0000-4000-8000-0000000000e2");

    private static HomeLinkRow Guest(Guid id, bool? disabled = false, Guid? tenant = null, Guid? oid = null)
        => new(id, disabled, tenant?.ToString("D"), oid?.ToString("D"), "W/\"7\"");

    [Fact]
    public void DecideHomeLinkWrite_OneEnabledUnlinkedSystemUser_NobodyHoldingThePair_WritesWithItsVersion()
    {
        var decision = ContactBindingDecision.DecideHomeLinkWrite(
            HomeLinkLookup.Of(Guest(GuestUser)), HomeLinkLookup.Of(), LinkTenant, LinkOid);

        decision.Should().Be(new HomeLinkWriteDecision(HomeLinkWriteAction.Write, GuestUser, "W/\"7\""));
    }

    [Fact]
    public void DecideHomeLinkWrite_ItsOwnPairAlready_IsAlreadyLinked_AndADifferentPairIsNeverRePointed()
    {
        ContactBindingDecision.DecideHomeLinkWrite(
                HomeLinkLookup.Of(Guest(GuestUser, tenant: LinkTenant, oid: LinkOid)), HomeLinkLookup.Of(), LinkTenant, LinkOid)
            .Action.Should().Be(HomeLinkWriteAction.AlreadyLinked);

        // Perturb ONE input: the stored oid is another person's — and a half-written row (tenant only) is a link too.
        ContactBindingDecision.DecideHomeLinkWrite(
                HomeLinkLookup.Of(Guest(GuestUser, tenant: LinkTenant, oid: Guid.NewGuid())), HomeLinkLookup.Of(), LinkTenant, LinkOid)
            .Action.Should().Be(HomeLinkWriteAction.Collision);
        ContactBindingDecision.DecideHomeLinkWrite(
                HomeLinkLookup.Of(Guest(GuestUser, tenant: LinkTenant)), HomeLinkLookup.Of(), LinkTenant, LinkOid)
            .Action.Should().Be(HomeLinkWriteAction.Collision);
    }

    [Fact]
    public void DecideHomeLinkWrite_ItsOwnPair_InAFormThe046ReadCannotMatch_OrAlsoOnAnotherSystemUser_IsACollision()
    {
        // Braces: the same GUID, but the 046 resolution's D-form filter never finds it, so sign-in would still fail.
        var braced = new HomeLinkRow(GuestUser, false, LinkTenant.ToString("B"), LinkOid.ToString("D"), "W/\"7\"");
        ContactBindingDecision.DecideHomeLinkWrite(HomeLinkLookup.Of(braced), HomeLinkLookup.Of(), LinkTenant, LinkOid)
            .Action.Should().Be(HomeLinkWriteAction.Collision);

        // A second holder of the same pair: the 046 resolution denies it as ambiguous, so "already linked" would mislead.
        var own = Guest(GuestUser, tenant: LinkTenant, oid: LinkOid);
        var second = new HomeLinkRow(OtherUser, false, LinkTenant.ToString("D"), LinkOid.ToString("D"));
        ContactBindingDecision.DecideHomeLinkWrite(HomeLinkLookup.Of(own), HomeLinkLookup.Of(own, second), LinkTenant, LinkOid)
            .Action.Should().Be(HomeLinkWriteAction.Collision);
    }

    [Fact]
    public void DecideHomeLinkWrite_ThePairOnAnotherSystemUser_IsACollision_WhateverItsState()
    {
        var holder = new HomeLinkRow(OtherUser, true, LinkTenant.ToString("D"), LinkOid.ToString("D"));

        ContactBindingDecision.DecideHomeLinkWrite(HomeLinkLookup.Of(Guest(GuestUser)), HomeLinkLookup.Of(holder), LinkTenant, LinkOid)
            .Should().Be(new HomeLinkWriteDecision(HomeLinkWriteAction.Collision, GuestUser));
    }

    [Fact]
    public void DecideHomeLinkWrite_NoEnabledRow_IsNoSystemUserOrDisabled_AndTwoEnabledRowsAreAmbiguous()
    {
        ContactBindingDecision.DecideHomeLinkWrite(HomeLinkLookup.Of(), HomeLinkLookup.Of(), LinkTenant, LinkOid)
            .Action.Should().Be(HomeLinkWriteAction.NoSystemUser);
        ContactBindingDecision.DecideHomeLinkWrite(HomeLinkLookup.Of(Guest(GuestUser, disabled: true)), HomeLinkLookup.Of(), LinkTenant, LinkOid)
            .Action.Should().Be(HomeLinkWriteAction.SystemUserDisabled);
        ContactBindingDecision.DecideHomeLinkWrite(HomeLinkLookup.Of(Guest(GuestUser, disabled: null)), HomeLinkLookup.Of(), LinkTenant, LinkOid)
            .Action.Should().Be(HomeLinkWriteAction.SystemUserDisabled, "an unreadable state is never treated as enabled");
        ContactBindingDecision.DecideHomeLinkWrite(
                HomeLinkLookup.Of(Guest(GuestUser), Guest(OtherUser)), HomeLinkLookup.Of(), LinkTenant, LinkOid)
            .Should().Be(new HomeLinkWriteDecision(HomeLinkWriteAction.Deny, DenyCode: ContactBindingDecision.DenyGuestSystemUserAmbiguous));
    }

    [Fact]
    public void DecideHomeLinkWrite_AFailedOrMissingRead_OfEitherLookup_Denies_AndNeverWrites()
    {
        var target = HomeLinkLookup.Of(Guest(GuestUser));

        ContactBindingDecision.DecideHomeLinkWrite(HomeLinkLookup.Failed, HomeLinkLookup.Of(), LinkTenant, LinkOid)
            .DenyCode.Should().Be(ContactBindingDecision.DenyContactLookupFailed);
        ContactBindingDecision.DecideHomeLinkWrite(HomeLinkLookup.ColumnMissing, HomeLinkLookup.Of(), LinkTenant, LinkOid)
            .DenyCode.Should().Be(ContactBindingDecision.DenyBindingColumnMissing);
        ContactBindingDecision.DecideHomeLinkWrite(target, HomeLinkLookup.Failed, LinkTenant, LinkOid)
            .Should().Be(new HomeLinkWriteDecision(HomeLinkWriteAction.Deny, DenyCode: ContactBindingDecision.DenyContactLookupFailed));
    }

    private static CollisionParty Party(IdentityCollisionReason reason, Guid oid,
        IdentityPlaneMarker plane = IdentityPlaneMarker.Workforce)
        => new(oid, plane, reason, Now);
}
