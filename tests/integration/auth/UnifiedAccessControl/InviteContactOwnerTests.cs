using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
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

    private Task<InviteExternalUserEndpoint.ProvisionOutcome> Invite(ContactIdentityBinder binder, ILogger? logger = null)
        => InviteExternalUserEndpoint.ProvisionAsync(
            Request(), _h.Grants, _h.Ciam, _h.Emails, binder, PortalUrl, CustomerKey, logger ?? NullLogger<Program>.Instance,
            resolution: null, CancellationToken.None);

    /// <summary>The exception the Web API client throws for a failed create: the status plus the Dataverse error CODE only.</summary>
    private static HttpRequestException Refused(System.Net.HttpStatusCode status, string? dataverseErrorCode)
    {
        var ex = new HttpRequestException(
            $"Response status code does not indicate success: {(int)status} ({status}).", null, status);
        if (dataverseErrorCode is not null)
        {
            ex.Data[DataverseWebApiClient.ErrorCodeDataKey] = dataverseErrorCode;
        }

        return ex;
    }

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

    // ── K1 (UAC re-check): Dataverse refuses the owner of the create (403, Read Privilege Check For Owner) ─────────

    [Fact]
    public async Task WhenDataverseRefusesTheOwnersPrivilege_TheInviteIsRefused500OwnerUnconfigured_NotAGeneric500()
    {
        GivenOneDefaultTeam();
        _h.Grants.ContactCreateFailure = Refused(System.Net.HttpStatusCode.Forbidden, "0x80040299");

        var outcome = await Invite(Owned(CustomerBu));

        var failure = outcome.Failure!;
        outcome.Failure.Should().NotBeNull("it is a defined refusal, not an exception");
        failure.ReasonCode.Should().Be(ContactBindingDecision.DenyOwnerUnconfigured);
        failure.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        failure.Message.Should().NotContainAny(CustomerBu.ToString(), CustomerTeam.ToString(), Email);
        _h.Ciam.Attempts.Should().BeEmpty("no CIAM account");
        _h.Emails.Sent.Should().BeEmpty();
        _h.Emails.Onboarding.Should().BeEmpty();
        _h.Identities.Writes.Should().BeEmpty("no invite marker, no binding");
    }

    // Must not fire (UAC round 3 K1): a 403 that is not the owner fault is not remapped. 0x80040220 is the BFF identity's OWN
    // missing privilege, and a bare 403 names no fault at all. Both keep the previous generic path (the exception propagates),
    // with a log that names the Dataverse code - never the message.
    [Theory]
    [InlineData("0x80040220")]
    [InlineData(null)]
    public async Task AForbiddenCreateThatIsNotTheOwnerFault_IsNotRemapped_ItStillThrows_AndLogsTheCode(string? code)
    {
        GivenOneDefaultTeam();
        _h.Grants.ContactCreateFailure = Refused(System.Net.HttpStatusCode.Forbidden, code);
        var log = new BindingLogCapture<Program>();

        var act = () => Invite(Owned(CustomerBu), log);

        await act.Should().ThrowAsync<HttpRequestException>();
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Emails.Sent.Should().BeEmpty();
        var entry = log.Entries.Should().ContainSingle(e => e.Message.Contains("not the owner-privilege fault")).Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().Contain(code ?? "(none)").And.NotContain("Read Privilege Check For Owner")
            .And.NotContain(ContactBindingDecision.DenyOwnerUnconfigured);
    }

    [Fact]
    public async Task TheOwnerFault_IsLoggedByCode_AndNoDataverseMessageIsLoggedOrSurfaced()
    {
        GivenOneDefaultTeam();
        _h.Grants.ContactCreateFailure = Refused(System.Net.HttpStatusCode.Forbidden, "0x80040299");
        var log = new BindingLogCapture<Program>();

        var outcome = await Invite(Owned(CustomerBu), log);

        outcome.Failure!.ReasonCode.Should().Be(ContactBindingDecision.DenyOwnerUnconfigured);
        log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error).Which.Message.Should().Contain("0x80040299")
            .And.NotContain(Email).And.NotContain(CustomerBu.ToString()).And.NotContain(CustomerTeam.ToString());
        outcome.Failure.Message.Should().NotContain("0x80040299").And.NotContain("Privilege");
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.BadRequest, "0x80040299")]          // the code on a non-403 is not the owner refusal
    [InlineData(System.Net.HttpStatusCode.InternalServerError, "0x80040299")]
    public async Task TheOwnerCode_OnAnotherStatus_IsNotRemapped(System.Net.HttpStatusCode status, string code)
    {
        GivenOneDefaultTeam();
        _h.Grants.ContactCreateFailure = Refused(status, code);

        var act = () => Invite(Owned(CustomerBu));

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task AnyOtherCreateFault_KeepsItsExistingMapping_ItStillThrows()
    {
        // Must not fire: a fault that is not a privilege refusal is not remapped (the endpoint's generic handler answers it).
        GivenOneDefaultTeam();
        _h.Grants.ContactCreateFailure = new HttpRequestException(
            "Response status code does not indicate success: 500 (Internal Server Error).", null, System.Net.HttpStatusCode.InternalServerError);

        var act = () => Invite(Owned(CustomerBu));

        await act.Should().ThrowAsync<HttpRequestException>();
        _h.Ciam.Attempts.Should().BeEmpty();
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
