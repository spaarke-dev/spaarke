using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// ISS-005 (#1673 review F1) - the contact an invite CREATES (an email that matches no contact) is owned by the default owner
/// team of <c>ExternalAccess:CustomerBusinessUnitId</c>, resolved by the binder's one owner lookup BEFORE anything is written,
/// and the invite is refused with the same reason code as registration and first sign-in when it does not resolve. A contact
/// the invite reuses is never re-owned. Through the PRODUCTION invite core over the task-142 harness's doubles.
/// KEEP path: <c>tests/integration/auth/**</c>.
/// </summary>
public class InviteContactOwnerTests
{
    private const string Email = "new.partner@outside.test";
    private const string PortalUrl = "https://external.test";
    private const string CustomerKey = "acme";
    private const string OwnerBind = "ownerid@odata.bind";
    private static readonly Guid CustomerBu = Guid.Parse("cb15f587-1111-4111-8111-00000000000a");
    private static readonly Guid CustomerTeam = Guid.Parse("7ea70000-2222-4222-8222-00000000000b");

    private readonly Harness _h = new();

    public InviteContactOwnerTests() => _h.Grants.AcceptContactCreates = true;

    private static InviteExternalUserRequest Request()
        => new(Email, Guid.Empty, (int)ExternalAccessLevel.Collaborate, "Pat", "Partner", null, null, "matter", Guid.Empty);

    private Task<InviteExternalUserEndpoint.ProvisionOutcome> Invite(ContactIdentityBinder binder)
        => InviteExternalUserEndpoint.ProvisionAsync(
            Request(), _h.Grants, _h.Ciam, _h.Emails, binder, PortalUrl, CustomerKey, NullLogger<Program>.Instance,
            resolution: null, CancellationToken.None);

    private ContactIdentityBinder Owned(Guid? businessUnit)
        => IdentityBindingTestKit.OwnerAwareBinder(_h.Identities, businessUnit);

    private void GivenOneDefaultTeam() => _h.Identities.DefaultTeams[CustomerBu] = new List<Guid> { CustomerTeam };

    private void AssertNothingCreatedOrSent()
    {
        _h.Grants.ContactCreates.Should().BeEmpty("no contact is created without an owner");
        _h.Ciam.Attempts.Should().BeEmpty("no CIAM account");
        _h.Emails.Sent.Should().BeEmpty();
        _h.Emails.Onboarding.Should().BeEmpty();
        _h.Identities.Writes.Should().BeEmpty("no invite marker, no binding");
    }

    // ── Must fire: the create is owned ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ANewContact_IsCreatedOwnedByTheCustomerBusinessUnitsDefaultTeam()
    {
        GivenOneDefaultTeam();

        await Invite(Owned(CustomerBu));

        var create = _h.Grants.ContactCreates.Should().ContainSingle("exactly one contact create").Subject;
        using var doc = JsonDocument.Parse(create);
        doc.RootElement.GetProperty(OwnerBind).GetString().Should().Be($"/teams({CustomerTeam:D})",
            "the exact team the customer business unit resolves to");
        doc.RootElement.GetProperty("emailaddress1").GetString().Should().Be(Email);
    }

    // ── Must fire: no owner, no create ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]   // the setting is unset
    [InlineData(true)]    // the setting names a business unit with no default owner team
    public async Task WhenNoOwnerTeamResolves_TheInviteIsRefused500_AndNothingIsCreated(bool settingPresent)
    {
        var outcome = await Invite(Owned(settingPresent ? CustomerBu : null));

        var failure = outcome.Failure!;
        outcome.Failure.Should().NotBeNull();
        failure.ReasonCode.Should().Be(ContactBindingDecision.DenyOwnerUnconfigured);
        failure.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        failure.Message.Should().NotBeNullOrWhiteSpace();
        failure.Message.Should().NotContainAny(CustomerBu.ToString(), Email, "ExternalAccess:CustomerBusinessUnitId");
        outcome.Refusal.Should().BeNull();
        AssertNothingCreatedOrSent();

        var result = InviteExternalUserEndpoint.LookupFailureResult(failure, new DefaultHttpContext());
        result.Should().BeAssignableTo<IStatusCodeHttpResult>().Which.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task WhenTheOwnerLookupCannotBeRead_TheInviteIsRetryable503_AndNothingIsCreated()
    {
        GivenOneDefaultTeam();
        _h.Identities.FailTeamLookup = true;

        var outcome = await Invite(Owned(CustomerBu));

        var failure = outcome.Failure!;
        outcome.Failure.Should().NotBeNull();
        failure.ReasonCode.Should().Be(ContactBindingDecision.InviteContactLookupFailed);
        failure.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        AssertNothingCreatedOrSent();
    }

    [Fact]
    public async Task ABinderThatBindsNoOwner_NeverCreatesAnInviteContact()
    {
        // Fail closed: the foreign-environment policy has no meaning for an invite, which always runs in this BFF's environment.
        var outcome = await Invite(IdentityBindingTestKit.Binder(_h.Identities));

        outcome.Failure.Should().NotBeNull();
        outcome.Failure!.ReasonCode.Should().Be(ContactBindingDecision.DenyOwnerUnconfigured);
        AssertNothingCreatedOrSent();
    }

    // ── Must not fire: an existing contact keeps the owner it has ────────────────────────────────────

    [Fact]
    public async Task AnExistingUnboundContact_IsNotCreatedAndNotReOwned_AndNoOwnerIsResolved()
    {
        GivenOneDefaultTeam();
        var contact = _h.Contact();
        _h.Identities.Contacts[contact].Email = Email;

        var outcome = await Invite(Owned(CustomerBu));

        outcome.Failure.Should().BeNull();
        outcome.ContactId.Should().Be(contact);
        _h.Grants.ContactCreates.Should().BeEmpty("the existing contact is reused, not created");
        _h.Identities.Contacts[contact].OwnerTeamId.Should().BeNull("the invite never re-owns a contact it reuses");
        _h.Identities.Writes.Should().NotContain(w => w.Detail.Contains("ownerid", StringComparison.OrdinalIgnoreCase));
        _h.Identities.Reads.Should().NotContain("owner-team", "the owner is looked up only for a create");
    }

    [Fact]
    public async Task AnExistingContact_IsInvited_EvenWhenTheOwnerSettingIsUnset()
    {
        var contact = _h.Contact();
        _h.Identities.Contacts[contact].Email = Email;

        var outcome = await Invite(Owned(null));

        outcome.Failure.Should().BeNull("an unset owner setting blocks creates, not invites of existing contacts");
        outcome.Refusal.Should().BeNull();
        outcome.ContactId.Should().Be(contact);
    }
}
