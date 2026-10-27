using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.IdentityBindingTestKit;

namespace Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;

/// <summary>
/// Slice C, the removed marker (<c>contact.sprk_externalaccessremovedon</c>): a contact whose external access an
/// administrator removed neither resolves nor binds, on any plane and path, whatever its <c>statecode</c>
/// (<see cref="ContactBindingDecision.DenyContactRemoved"/>), and an ordinary invite to it is refused
/// (<see cref="ContactBindingDecision.InviteContactRemovedAdminReinstate"/>). A marker that read EMPTY counts only on a proven
/// read (its own readability proof); an unproven one is a removal (fail closed). An unmarked contact behaves exactly as before.
/// </summary>
public class RemovedMarkerBindingTests
{
    private static readonly Guid ContactA = Guid.Parse("eeeeeeee-7777-4777-8777-00000000000a");
    private static readonly Guid Caller = Guid.Parse("aaaaaaaa-7777-4777-8777-000000000001");
    private static readonly DateTimeOffset RemovedOn = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private const string Email = "person@customer.example";

    private readonly InMemoryContactIdentityStore _store = new();

    // ── The row: what counts as removed (pure) ────────────────────────────────────────────────────────────────

    [Fact]
    public void ARow_IsRemoved_WhenTheMarkerIsSet_OrItsEmptyReadIsUnproven_AndOnlyThen()
    {
        var row = new ContactBindingRow(ContactA, 0, null, null);

        row.IsRemoved.Should().BeFalse("MUST NOT FIRE: an empty marker on a proven read is not a removal");
        (row with { RemovedOn = RemovedOn }).IsRemoved.Should().BeTrue("MUST FIRE: the marker is set");
        (row with { RemovedMarkerUnproven = true }).IsRemoved.Should().BeTrue("MUST FIRE: an empty marker that may be hidden");
    }

    [Theory]
    [InlineData("{\"contactid\":\"eeeeeeee-7777-4777-8777-00000000000a\",\"sprk_externalaccessremovedon\":\"2026-09-30T12:00:00Z\"}", "set")]
    [InlineData("{\"contactid\":\"eeeeeeee-7777-4777-8777-00000000000a\",\"sprk_externalaccessremovedon\":\"not a date\"}", "set")]
    [InlineData("{\"contactid\":\"eeeeeeee-7777-4777-8777-00000000000a\",\"sprk_externalaccessremovedon\":null}", "empty")]
    [InlineData("{\"contactid\":\"eeeeeeee-7777-4777-8777-00000000000a\"}", "empty")]
    public void TheParsedRow_CarriesTheMarker_AndAValueThatIsNotADateIsStillAMarker(string json, string expected)
    {
        using var doc = JsonDocument.Parse(json);

        var row = DataverseContactIdentityStore.ParseContactRow(doc.RootElement)!;

        row.RemovedOn.HasValue.Should().Be(expected == "set");
    }

    [Fact]
    public void EveryBindingRead_SelectsTheMarker()
        => DataverseContactIdentityStore.ContactSelect.Split(',')
            .Should().Contain(ContactBindingDecision.ExternalAccessRemovedOnColumn);

    [Theory]
    [InlineData(ColumnReadProofOutcome.Proven, false)]
    [InlineData(ColumnReadProofOutcome.Unproven, true)]
    [InlineData(ColumnReadProofOutcome.Faulted, true)]
    public void AnEmptyMarker_ReadWithoutProof_MarksTheRow_ASetMarkerIsKept(ColumnReadProofOutcome proof, bool emptyCountsAsRemoved)
    {
        var lookup = ContactLookup.Of(
            new ContactBindingRow(ContactA, 0, null, null),
            new ContactBindingRow(Guid.NewGuid(), 0, null, null) { RemovedOn = RemovedOn });

        var marked = DataverseContactIdentityStore.WithRemovedMarkerProof(lookup, proof);

        marked.Rows[0].IsRemoved.Should().Be(emptyCountsAsRemoved);
        marked.Rows[1].IsRemoved.Should().BeTrue("a marker that was read is a value");
        marked.Rows[1].RemovedMarkerUnproven.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, ColumnReadProofOutcome.Proven, RemovedMarkerRead.Removed)]
    [InlineData(true, ColumnReadProofOutcome.Unproven, RemovedMarkerRead.Removed)]
    [InlineData(false, ColumnReadProofOutcome.Proven, RemovedMarkerRead.NotRemoved)]
    [InlineData(false, ColumnReadProofOutcome.Unproven, RemovedMarkerRead.Unreadable)]
    [InlineData(false, ColumnReadProofOutcome.Faulted, RemovedMarkerRead.Unreadable)]
    public void TheLiveMarkerRead_IsNotRemoved_OnlyForAnEmptyMarkerOnAProvenRead(
        bool set, ColumnReadProofOutcome proof, RemovedMarkerRead expected)
        => DataverseContactIdentityStore.RemovedMarkerFrom(set ? RemovedOn : null, proof).Should().Be(expected);

    [Fact]
    public void TheLiveMarkerRead_AsksForTheMarkerOfThatContactById_WithNoStateFilter()
    {
        var path = DataverseContactIdentityStore.BuildRemovedMarkerReadPath(ContactA);

        path.Should().StartWith("contacts(eeeeeeee-7777-4777-8777-00000000000a)?$select=sprk_externalaccessremovedon");
        path.Should().NotContain("statecode", "a reactivated row is still removed, so no state may filter it out");
    }

    /// <summary>The marker read's answer: only a successful read of the row as an object is readable.</summary>
    [Theory]
    [InlineData(LookupStatus.Read, 200, "{\"sprk_externalaccessremovedon\":\"2026-09-30T12:00:00Z\"}", true, true)]
    [InlineData(LookupStatus.Read, 200, "{\"sprk_externalaccessremovedon\":null}", true, false)]
    [InlineData(LookupStatus.Read, 200, "{}", true, false)]
    [InlineData(LookupStatus.Read, 404, "{}", false, false)]
    [InlineData(LookupStatus.Read, 200, null, false, false)]
    [InlineData(LookupStatus.Read, 200, "not json", false, false)]
    [InlineData(LookupStatus.Read, 200, "[]", false, false)]
    [InlineData(LookupStatus.Failed, 503, "{}", false, false)]
    [InlineData(LookupStatus.ColumnMissing, 400, "{}", false, false)]
    public void TheLiveMarkerResponse_IsReadableOnlyWhenTheRowWasRead(
        LookupStatus status, int code, string? body, bool readable, bool removed)
    {
        var answer = DataverseContactIdentityStore.ParseRemovedMarkerResponse(status, (System.Net.HttpStatusCode)code, body);

        answer.Readable.Should().Be(readable);
        (answer.RemovedOn is not null).Should().Be(removed);
    }

    // ── The decisions (pure): every path that resolves, binds or registers a contact ─────────────────────────────

    private static ContactBindingRow Bound(bool removed, int stateCode = 0, bool unproven = false)
        => new ContactBindingRow(ContactA, stateCode, Caller.ToString("D"), (int)IdentityPlaneMarker.External)
        {
            RemovedOn = removed ? RemovedOn : null,
            RemovedMarkerUnproven = unproven,
        };

    private static ContactBindingRow Unbound(bool removed, int stateCode = 0)
        => new ContactBindingRow(ContactA, stateCode, null, null) { RemovedOn = removed ? RemovedOn : null };

    private static BindingRequest Ciam() => new(BindingPlane.CiamToken, Caller, Email, BindingEligibility.EmailBindOnly);

    /// <summary>The oid resolve, on every plane that runs it: a removed contact is denied, active or reactivated alike.</summary>
    [Theory]
    [InlineData("removed-active", ContactBindingDecision.DenyContactRemoved)]
    [InlineData("removed-inactive", ContactBindingDecision.DenyContactRemoved)]
    [InlineData("unproven-active", ContactBindingDecision.DenyContactRemoved)]
    [InlineData("unmarked-active", null)]
    [InlineData("unmarked-inactive", ContactBindingDecision.DenyContactInactive)]
    public void OidResolve_DeniesARemovedContact_WhateverItsState(string row, string? denyCode)
    {
        var contact = row switch
        {
            "removed-active" => Bound(removed: true),
            "removed-inactive" => Bound(removed: true, stateCode: 1),
            "unproven-active" => Bound(removed: false, unproven: true),
            "unmarked-active" => Bound(removed: false),
            _ => Bound(removed: false, stateCode: 1),
        };

        var decision = ContactBindingDecision.Decide(Ciam(), ContactLookup.Of(contact));
        var readOnly = ContactBindingDecision.DecideBoundContact(ContactLookup.Of(contact))!;

        foreach (var d in new[] { decision, readOnly })
        {
            if (denyCode is null)
            {
                d.Should().Be(new BindingDecision(BindingAction.ResolveByOid, ContactA), "MUST NOT FIRE: unchanged");
            }
            else
            {
                d.Action.Should().Be(BindingAction.Deny);
                d.DenyCode.Should().Be(denyCode);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmailBind_DeniesARemovedContact_BeforeAnyOtherCheck(bool removed)
    {
        var decision = ContactBindingDecision.Decide(Ciam(), ContactLookup.Of(), ContactLookup.Of(Unbound(removed)));

        if (removed)
        {
            decision.Should().Be(new BindingDecision(BindingAction.Deny, DenyCode: ContactBindingDecision.DenyContactRemoved));
        }
        else
        {
            decision.Should().Be(new BindingDecision(BindingAction.NeedReferenceLookup, ContactA), "MUST NOT FIRE: unchanged");
        }
    }

    [Theory]
    [InlineData("carrier")]
    [InlineData("linked-unbound")]
    public void ExistingLink_DeniesARemovedContact(string which)
    {
        var request = new BindingRequest(BindingPlane.SystemUser, Caller, Email, BindingEligibility.EmailBindAndCreate);
        var decision = which == "carrier"
            ? ContactBindingDecision.DecideExistingLink(request, ContactA, ContactLookup.Of(Bound(removed: true)))
            : ContactBindingDecision.DecideExistingLink(request, ContactA, ContactLookup.Of(), ContactLookup.Of(Unbound(removed: true)));

        decision.DenyCode.Should().Be(ContactBindingDecision.DenyContactRemoved);

        var control = which == "carrier"
            ? ContactBindingDecision.DecideExistingLink(request, ContactA, ContactLookup.Of(Bound(removed: false)))
            : ContactBindingDecision.DecideExistingLink(request, ContactA, ContactLookup.Of(), ContactLookup.Of(Unbound(removed: false)));
        control.DenyCode.Should().BeNull("MUST NOT FIRE: an unmarked linked contact is decided as before");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WorkforceHomeResolve_DeniesARemovedContact(bool removed)
    {
        var row = new ContactBindingRow(ContactA, 0, Caller.ToString("D"), ContactBindingDecision.WorkforceHomePlaneValue,
            RawExternalTenantId: CustomerTenant.ToString("D")) { RemovedOn = removed ? RemovedOn : null };

        var decision = ContactBindingDecision.DecideHomeContact(ContactLookup.Of(row), CustomerTenant, Caller)!;

        decision.Should().Be(removed
            ? new BindingDecision(BindingAction.Deny, DenyCode: ContactBindingDecision.DenyContactRemoved)
            : new BindingDecision(BindingAction.ResolveByOid, ContactA));
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("active", false)]
    [InlineData("inactive", true)]
    [InlineData("inactive", false)]
    public void Registration_DeniesARemovedContact_ActiveOrInactive(string state, bool removed)
    {
        var decision = state == "active"
            ? ContactBindingDecision.DecideRegistrationEmail(ContactLookup.Of(Unbound(removed)), Caller)
            : ContactBindingDecision.DecideInactiveRegistrationEmail(ContactLookup.Of(Unbound(removed, stateCode: 1)));

        if (removed)
        {
            decision.Action.Should().Be(RegistrationEmailAction.Deny);
            decision.DenyCode.Should().Be(ContactBindingDecision.DenyContactRemoved);
            decision.FlagContactIds.Should().BeNull("a removal is not a collision; nothing is flagged");
        }
        else
        {
            decision.Action.Should().Be(state == "active"
                ? RegistrationEmailAction.NeedReferenceLookup
                : RegistrationEmailAction.Conflict, "MUST NOT FIRE: unchanged");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Invite_RefusesARemovedActiveContact_WithTheReinstateCode_AndFlagsNothing(bool removed)
    {
        var decision = ContactBindingDecision.DecideInvite(ContactLookup.Of(Unbound(removed)));

        decision.Should().Be(removed
            ? new InviteDecision(InviteContactAction.Refuse, ContactA, ContactBindingDecision.InviteContactRemovedAdminReinstate)
            : new InviteDecision(InviteContactAction.NeedReferenceLookup, ContactA));
    }

    [Fact]
    public void Invite_OverAnInactiveMatch_RefusesARemovedOne_FailsOnAFailedRead_AndOtherwiseCreates()
    {
        ContactBindingDecision.DecideInviteInactiveMatch(ContactLookup.Of(Unbound(removed: true, stateCode: 1)))
            .Should().Be(new InviteDecision(InviteContactAction.Refuse, ContactA,
                ContactBindingDecision.InviteContactRemovedAdminReinstate));
        ContactBindingDecision.DecideInviteInactiveMatch(ContactLookup.Failed)!.Action.Should().Be(InviteContactAction.Fail);
        ContactBindingDecision.DecideInviteInactiveMatch(ContactLookup.Of(Unbound(removed: false, stateCode: 1)))
            .Should().BeNull("MUST NOT FIRE: an unmarked inactive contact does not stop the create");
        ContactBindingDecision.DecideInviteInactiveMatch(ContactLookup.Of()).Should().BeNull();
    }

    // ── Through the binder (the in-memory store applies the production marking) ─────────────────────────────

    private void RemovedBoundContact(int stateCode = 0)
    {
        _store.AddContact(ContactA, email: Email, oid: Caller.ToString("D"), plane: IdentityPlaneMarker.External, stateCode: stateCode);
        _store.Contacts[ContactA].RemovedOn = RemovedOn;
    }

    [Fact]
    public async Task CiamSignIn_ARemovedActiveContact_IsDenied_AndNothingIsWritten()
    {
        RemovedBoundContact();

        var result = await Binder(_store).ResolveCiamCallerAsync(Caller.ToString(), Email, inviteCode: null, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactRemoved);
        result.ContactId.Should().BeNull();
        _store.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task CiamSignIn_AnUnprovenMarker_IsDenied_AProvenEmptyOneResolves()
    {
        _store.AddContact(ContactA, email: Email, oid: Caller.ToString("D"), plane: IdentityPlaneMarker.External);

        (await Binder(_store).ResolveCiamCallerAsync(Caller.ToString(), Email, inviteCode: null, CancellationToken.None))
            .ContactId.Should().Be(ContactA, "MUST NOT FIRE: an unmarked contact resolves as before");

        _store.RemovedMarkerProof = ColumnReadProofOutcome.Unproven;
        (await Binder(_store).ResolveCiamCallerAsync(Caller.ToString(), Email, inviteCode: null, CancellationToken.None))
            .DenyCode.Should().Be(ContactBindingDecision.DenyContactRemoved, "an unreadable marker is a deny, not a pass");
    }

    [Fact]
    public async Task WorkforceSignIn_ARemovedContact_IsDenied()
    {
        _store.AddContact(ContactA, email: Email, oid: Caller.ToString("D"), plane: IdentityPlaneMarker.Workforce);
        _store.Contacts[ContactA].RemovedOn = RemovedOn;

        var result = await Binder(_store).ResolveWorkforceCallerAsync(
            WorkforceUser(Caller, tid: CustomerTenant), Caller, stampTenantId: null, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactRemoved);
        _store.Writes.Should().BeEmpty("a removed contact is never created around");
    }

    [Fact]
    public async Task WorkforceEmailBind_OntoARemovedContact_IsDenied_EvenWithAStamp()
    {
        _store.AddContact(ContactA, email: Email);
        _store.StampInvite(ContactA, Email, CustomerTenant);
        _store.Contacts[ContactA].RemovedOn = RemovedOn;
        var oid = Guid.NewGuid();

        var result = await Binder(_store).ResolveWorkforceCallerAsync(
            WorkforceUser(oid, tid: CustomerTenant), oid, stampTenantId: null, CancellationToken.None);

        result.DenyCode.Should().Be(ContactBindingDecision.DenyContactRemoved);
        _store.Contacts[ContactA].Oid.Should().BeNull();
        _store.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task HomeRegistration_OntoARemovedContact_IsDenied()
    {
        _store.AddContact(ContactA, email: Email);
        _store.StampInvite(ContactA, Email, CustomerTenant);
        _store.Contacts[ContactA].RemovedOn = RemovedOn;

        var result = await Binder(_store).RegisterHomeContactAsync(
            new HomeRegistrationRequest(CustomerTenant, Caller, Email, "Pat", "Example", null, true, Guid.NewGuid(),
                "Secure Record", null),
            CancellationToken.None);

        result.Should().Be(HomeRegistrationResult.Denied(ContactBindingDecision.DenyContactRemoved));
        _store.Contacts[ContactA].Oid.Should().BeNull();
        _store.Contacts.Should().ContainSingle("no contact is created around a removal");
    }

    [Theory]
    [InlineData("active-removed", InviteContactAction.Refuse)]
    [InlineData("inactive-removed", InviteContactAction.Refuse)]
    [InlineData("active-unproven", InviteContactAction.Refuse)]
    [InlineData("active-unmarked", InviteContactAction.NeedReferenceLookup)]
    [InlineData("inactive-unmarked", InviteContactAction.CreateContact)]
    public async Task InviteResolution_RefusesARemovedContact_ActiveOrInactive(string row, InviteContactAction expected)
    {
        _store.AddContact(ContactA, email: Email, stateCode: row.StartsWith("inactive", StringComparison.Ordinal) ? 1 : 0);
        if (row.EndsWith("removed", StringComparison.Ordinal)) _store.Contacts[ContactA].RemovedOn = RemovedOn;
        if (row.EndsWith("unproven", StringComparison.Ordinal)) _store.RemovedMarkerProof = ColumnReadProofOutcome.Unproven;

        var resolution = await Binder(_store).ResolveInviteContactAsync(Email, CancellationToken.None);

        if (expected == InviteContactAction.Refuse)
        {
            resolution.Action.Should().Be(InviteContactAction.Refuse);
            resolution.ReasonCode.Should().Be(ContactBindingDecision.InviteContactRemovedAdminReinstate);
            resolution.Message.Should().NotBeNullOrWhiteSpace();
            _store.Writes.Should().BeEmpty("a refusal for a removal flags nothing");
        }
        else
        {
            // NeedReferenceLookup is resolved inside the binder; an unbound, unlinked active contact is provisioned.
            resolution.Action.Should().Be(expected == InviteContactAction.NeedReferenceLookup
                ? InviteContactAction.ProvisionExisting
                : InviteContactAction.CreateContact, "MUST NOT FIRE: unchanged");
        }
    }
}
