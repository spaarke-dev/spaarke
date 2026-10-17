using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 099b (D-11, #1010) — the contact-side route (<c>POST /contact-grants</c>, task 140) writes
/// through the same grant core, so its new rows are team-owned record-first too: the record's business unit's default Owner
/// team, never the application identity. A resolver read fault is a 503 and writes nothing. Through the PRODUCTION filter and
/// handler with the REAL <see cref="Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver"/> over an in-memory directory.
/// The other grant routes are covered in <c>GrantRowOwnershipTests</c>.
/// </summary>
public partial class ContactGrantAuthorizationTests
{
    [Fact]
    public async Task ContactGrant_OnAChildBusinessUnitRecord_IsOwnedByThatUnitsDefaultOwnerTeam_NeverAnApplicationUser()
    {
        Ownership = OwnershipDirectory.Standard().WithOrdinaryRoot("sprk_project", ProjectId).Resolver();

        var result = await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate));

        Ok<ContactGrantResponse>(result);
        var payload = _dataverse.CreatePayloads.Should().ContainSingle("exactly one grant row is created").Subject;
        payload.Should().ContainKey("ownerid@odata.bind", "a grant row is never created without its owner (D-11)");
        payload["ownerid@odata.bind"].Should().Be($"/teams({OwnershipDirectory.ChildTeam:D})",
            "record-first: the project's business unit's default Owner team");
    }

    [Fact]
    public async Task ContactGrant_WhenTheOwnerReadFaults_Is503OwnerUnreadable_AndWritesNothing()
    {
        Ownership = OwnershipDirectory.Standard().WithOrdinaryRoot("sprk_project", ProjectId)
            .Resolver(fault: new HttpRequestException("Simulated Dataverse 503 on the owner read."));

        var result = await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((StatusCodes.Status503ServiceUnavailable, ExternalGrantLifecycle.GrantOwnerUnreadableReasonCode));
        AssertNothingWritten();
    }

    [Fact]
    public async Task ContactGrant_WhenNoTeamResolves_Is500OwnerUnresolved_AndWritesNothing()
    {
        Ownership = OwnershipDirectory.Standard(withDefaultTeams: false).WithOrdinaryRoot("sprk_project", ProjectId).Resolver();

        var result = await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((StatusCodes.Status500InternalServerError, ExternalGrantLifecycle.GrantOwnerUnresolvedReasonCode));
        AssertNothingWritten();
    }

    [Fact]
    public async Task ContactGrant_WhenDataverseRefusesTheOwnerOnAPrivilegeCheck_Is500OwnerPrivilegeMissing_NotGrantFailed()
    {
        // Review round 1: the owner team's role lacks Read on sprk_externalrecordaccess, so Dataverse answers 403 to the create.
        Ownership = OwnershipDirectory.Standard().WithOrdinaryRoot("sprk_project", ProjectId).Resolver();
        _dataverse.BeforeCreate = () => throw new HttpRequestException(
            "Response status code does not indicate success: 403 (Forbidden).", null, System.Net.HttpStatusCode.Forbidden);

        var result = await Grant(Request(ExternalAccessLevel.Collaborate), Ciam(ExternalAccessLevel.Collaborate));

        Problem(result).Should().Be((StatusCodes.Status500InternalServerError, ExternalGrantLifecycle.GrantOwnerPrivilegeMissingReasonCode));
        AssertNothingWritten();
    }
}
