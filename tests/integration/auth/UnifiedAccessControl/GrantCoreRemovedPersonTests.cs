using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Slice C3: the grant core refuses a person whose external access an administrator removed, or whose removed marker cannot
/// be read, on every caller — <c>/grant</c>, <c>/invite-and-grant</c> and the Assigned-To rule (which logs and skips). 409
/// <c>contact_removed_admin_reinstate</c>, nothing written. An unmarked contact is granted exactly as before. Through the
/// PRODUCTION handlers and materializer over the task-142 harness. KEEP path: <c>tests/integration/auth/**</c>.
/// </summary>
public class GrantCoreRemovedPersonTests
{
    private const ExternalGrantRootType Matter = ExternalGrantRootType.Matter;
    private const string Attorney1 = "sprk_assignedattorney1";
    private const string Email = "person@customer.example";
    private const string CallerOid = "0a0a0a0a-1111-2222-3333-4444444444c3";

    private readonly Harness _h = new();
    private readonly Guid _matter = Guid.NewGuid();

    public GrantCoreRemovedPersonTests()
    {
        _h.Store.Root(Matter, _matter);
        _h.Grants.RootOwners[_matter] = Guid.NewGuid();
    }

    private static HttpContext GranterContext() => new DefaultHttpContext
    {
        TraceIdentifier = "trace-c3-grant",
        User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[] { new System.Security.Claims.Claim("oid", CallerOid) }, "Test")),
    };

    private Task<IResult> Grant(Guid contactId) =>
        GrantExternalAccessEndpoint.GrantAccessAsync(
            new GrantAccessRequest(contactId, Guid.Empty, ExternalAccessLevel.Collaborate, null, null, "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, new RecordOwnershipResolverDouble(), new TeamPrivilegeCheckerDouble(),
            new EverythingProbe(), _h.Materializer, _h.Notifier, _h.Invites, GranterContext(), NullLogger<Program>.Instance, _h.Time,
            CancellationToken.None);

    private Task<IResult> InviteAndGrant() =>
        InviteAndGrantExternalUserEndpoint.InviteAndGrantAsync(
            new InviteExternalUserRequest(Email, Guid.Empty, (int)ExternalAccessLevel.Collaborate, "Pat", "Partner", null, null, "matter", _matter),
            _h.Grants, _h.Participations, _h.AccessibleRecords, new RecordOwnershipResolverDouble(), new TeamPrivilegeCheckerDouble(), new EverythingProbe(),
            _h.Ciam, _h.Emails, IdentityBindingTestKit.Binder(_h.Identities), InviteOrganizationTestKit.PortalOnly(),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["ExternalAccess:PortalUrl"] = "https://external.test" })
                .Build(),
            TestSpeOwnership.Customer("acme"),
            _h.Materializer, _h.Notifier, GranterContext(), NullLogger<Program>.Instance, _h.Time, CancellationToken.None);

    private Task<AssignedAccessOutcome> Sync() => _h.SyncAsync(Matter, _matter);

    private static ProblemHttpResult Problem(IResult result) => result.Should().BeOfType<ProblemHttpResult>().Subject;

    private static string? ReasonCode(IResult result)
        => Problem(result).ProblemDetails.Extensions.TryGetValue("reasonCode", out var code) ? code as string : null;


    [Theory]
    [InlineData("removed")]
    [InlineData("unreadable")]
    public async Task Grant_ToARemovedOrUnreadableMarker_Is409_AndWritesNothing(string marker)
    {
        var contact = _h.Contact();
        if (marker == "removed")
            _h.Identities.Contacts[contact].RemovedOn = _h.Time.Now;
        else
            _h.Identities.RemovedMarkerProof = ColumnReadProofOutcome.Unproven;

        var result = await Grant(contact);

        Problem(result).StatusCode.Should().Be(StatusCodes.Status409Conflict);
        Problem(result).ProblemDetails.Title.Should().Be("External access removed");
        ReasonCode(result).Should().Be(ContactBindingDecision.InviteContactRemovedAdminReinstate);
        _h.Grants.WriteCount.Should().Be(0);
        _h.Shares.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Grant_ToAnUnmarkedContact_IsUnchanged()
    {
        var contact = _h.Contact();

        (await Grant(contact)).Should().BeOfType<Ok<GrantAccessResponse>>();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
    }

    /// <summary>
    /// /invite-and-grant: the person is removed between the invite's contact read and the grant core's own live read. The
    /// grant core refuses, before any onboarding, account or grant.
    /// </summary>
    [Fact]
    public async Task InviteAndGrant_ToAPersonRemovedAfterTheInviteRead_Is409_AndNothingIsOnboardedOrGranted()
    {
        var contact = Guid.NewGuid();
        _h.Identities.AddContact(contact, email: Email);
        _h.Identities.LiveRemovedMarkerOverride = RemovedMarkerRead.Removed;

        var result = await InviteAndGrant();

        Problem(result).StatusCode.Should().Be(StatusCodes.Status409Conflict);
        ReasonCode(result).Should().Be(ContactBindingDecision.InviteContactRemovedAdminReinstate);
        _h.Grants.WriteCount.Should().Be(0);
        _h.Ciam.Attempts.Should().BeEmpty();
        _h.Emails.Onboarding.Should().BeEmpty();
        _h.Identities.Contacts.Should().ContainSingle();
    }

    [Fact]
    public async Task InviteAndGrant_ToAnUnmarkedContact_IsUnchanged()
    {
        var contact = Guid.NewGuid();
        _h.Identities.AddContact(contact, email: Email);

        (await InviteAndGrant()).Should().NotBeOfType<ProblemHttpResult>();
        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
    }

    [Theory]
    [InlineData("removed")]
    [InlineData("unreadable")]
    public async Task TheAssignedToRule_SkipsARemovedOrUnreadableMarker_AndWritesNoGrant(string marker)
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);
        if (marker == "removed")
            _h.Identities.Contacts[contact].RemovedOn = _h.Time.Now;
        else
            _h.Identities.LiveRemovedMarkerOverride = RemovedMarkerRead.Unreadable;

        await Sync();

        _h.Grants.ActiveRowsOf(_matter, contact).Should().BeEmpty();
        _h.Grants.Creates.Should().BeEmpty();
    }

    [Fact]
    public async Task TheAssignedToRule_GrantsAnUnmarkedContact_AsBefore()
    {
        var contact = _h.Contact();
        _h.Store.Assign(Matter, _matter, Attorney1, contact);

        await Sync();

        _h.Grants.ActiveRowsOf(_matter, contact).Should().ContainSingle();
    }

    /// <summary>A granter who holds every right on the record, so the grant ceiling passes.</summary>
    private sealed class EverythingProbe : CallerRecordAccessProbe
    {
        public EverythingProbe()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
        {
        }

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
            => Task.FromResult(AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo
                               | AccessRights.Share | AccessRights.Delete);
    }
}
