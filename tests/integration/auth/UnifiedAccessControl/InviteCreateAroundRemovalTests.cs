using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Slice C2c: the invite's contact create checks for a removal immediately before it writes, through the binder's guarded
/// POST (<see cref="ContactIdentityBinder.CreateInviteContactGuardedAsync"/>), so no contact is created around a removal even
/// when the resolution that answered "create" was read earlier or handed in by the caller (<c>/invite-and-grant</c>). Through
/// the PRODUCTION invite core over the task-142 harness. KEEP path: <c>tests/integration/auth/**</c>.
/// </summary>
public class InviteCreateAroundRemovalTests
{
    private const string Email = "new.partner@outside.test";
    private const string PortalUrl = "https://external.test";
    private static readonly Guid CustomerBu = Guid.Parse("cb15f587-c2c0-4111-8111-00000000000a");
    private static readonly Guid CustomerTeam = Guid.Parse("7ea70000-c2c0-4222-8222-00000000000b");

    private readonly Harness _h = new();

    public InviteCreateAroundRemovalTests()
    {
        _h.Grants.AcceptContactCreates = true;
        _h.Identities.DefaultTeams[CustomerBu] = new List<Guid> { CustomerTeam };
    }

    /// <summary>The resolution an earlier read answered: no active contact carries the email, so create one.</summary>
    private static InviteContactResolution HandedInCreate() => new(InviteContactAction.CreateContact, null, null, null, null);

    private Task<InviteExternalUserEndpoint.ProvisionOutcome> Invite(InviteContactResolution? resolution)
        => InviteExternalUserEndpoint.ProvisionAsync(
            new InviteExternalUserRequest(Email, Guid.Empty, (int)ExternalAccessLevel.Collaborate, "Pat", "Partner", null, null,
                "matter", Guid.Empty),
            _h.Grants, _h.Ciam, _h.Emails, IdentityBindingTestKit.OwnerAwareBinder(_h.Identities, CustomerBu), PortalUrl, "acme",
            NullLogger<Program>.Instance, resolution, CancellationToken.None);

    /// <summary>An inactive contact for the email whose external access was removed after the resolution was read.</summary>
    private void RemovedInactiveContactAppears(bool unproven = false)
    {
        var id = Guid.NewGuid();
        _h.Identities.AddContact(id, email: Email, stateCode: 1);
        if (unproven)
            _h.Identities.RemovedMarkerProof = Sprk.Bff.Api.Infrastructure.ExternalAccess.ColumnReadProofOutcome.Unproven;
        else
            _h.Identities.Contacts[id].RemovedOn = DateTimeOffset.UtcNow;
    }

    private void AssertNothingCreatedOrSent()
    {
        _h.Grants.ContactCreates.Should().BeEmpty("no contact is created around a removal");
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Emails.Sent.Should().BeEmpty();
        _h.Emails.Onboarding.Should().BeEmpty();
        _h.Identities.Writes.Should().BeEmpty();
    }

    // ── Must fire ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AHandedInCreate_IsRefused409_WhenARemovedInactiveContactNowCarriesTheEmail()
    {
        RemovedInactiveContactAppears();

        var outcome = await Invite(HandedInCreate());

        outcome.Refusal!.ReasonCode.Should().Be(ContactBindingDecision.InviteContactRemovedAdminReinstate);
        AssertNothingCreatedOrSent();
    }

    [Fact]
    public async Task AHandedInCreate_Is503StateUnavailable_WhenAnInactiveContactsMarkerIsUnproven()
    {
        RemovedInactiveContactAppears(unproven: true);

        var outcome = await Invite(HandedInCreate());

        outcome.Refusal.Should().BeNull();
        outcome.Failure!.ReasonCode.Should().Be(ContactBindingDecision.InviteContactRemovedStateUnavailable);
        outcome.Failure.StatusCode.Should().Be(503);
        AssertNothingCreatedOrSent();
    }

    // ── Must not fire ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AHandedInCreate_WithNoRemovedContact_CreatesOneOwnedContact()
    {
        _h.Identities.AddContact(Guid.NewGuid(), email: Email, stateCode: 1); // inactive, not removed: not a removal

        var outcome = await Invite(HandedInCreate());

        outcome.Refusal.Should().BeNull();
        // The harness's created contact is not in the identity store, so the later marker step answers its own 503; what this
        // pins is that the create itself was made (the guard did not refuse it).
        outcome.Failure?.ReasonCode.Should().NotBe(ContactBindingDecision.InviteContactRemovedStateUnavailable);
        _h.Grants.ContactCreates.Should().ContainSingle()
            .Which.Should().Contain($"/teams({CustomerTeam:D})", "the owner team is unchanged");
    }
}
