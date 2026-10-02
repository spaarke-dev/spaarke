// unified-access-control-r2 task 152 — the two Office owner-event publishers, which had no publishing tests.
//
//   - Office save (OfficeService, POST /office/save): the document is owned by the business-unit default owner TEAM
//     the save resolved (task 080); the publisher passes that team.
//   - Quick-create matter (OfficeEndpoints, POST /office/quickcreate/matter): RecordCreationService writes the team
//     owner and does not return it, so the publisher reads the owner back.
//
// Both now state the row's REAL owner (PersonIdType=Team, PersonId=teamid) instead of the caller's AAD oid as a User.

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Tests.Services.Ai.Membership.Events;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

[Trait("status", "task-152-uac-r2")]
public class OfficeMembershipPublishingTests
{
    private static readonly Guid RecordId = Guid.Parse("ffffffff-0000-0000-0000-0000000000aa");

    [Fact]
    public async Task OfficeSave_PublishesTheTeamTheSaveResolved()
    {
        var publisher = new RecordingMembershipEventPublisher();
        var dataverse = OwnerEventTestKit.Dataverse();

        await MembershipOwnerEvents.PublishOwnerAddedAsync(
            publisher, dataverse.Object, "sprk_document", RecordId,
            new EntityReference("team", OwnerEventTestKit.TeamId), "corr-save", NullLogger.Instance, CancellationToken.None);

        var evt = publisher.Published.Should().ContainSingle().Subject;
        evt.PersonIdType.Should().Be(PersonIdentityType.Team);
        evt.PersonId.Should().Be(OwnerEventTestKit.TeamId);
        evt.SourceField.Should().Be("ownerid");
        dataverse.Verify(d => d.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "a team owner the save already holds needs no read");
    }

    [Fact]
    public async Task QuickCreateMatter_ReadsTheOwnerBack_AndPublishesTheTeam()
    {
        var publisher = new RecordingMembershipEventPublisher();
        var dataverse = OwnerEventTestKit.Dataverse(new EntityReference("team", OwnerEventTestKit.TeamId));

        await MembershipOwnerEvents.PublishOwnerAddedAsync(
            publisher, dataverse.Object, "sprk_matter", RecordId, knownOwner: null, "trace-qc", NullLogger.Instance, CancellationToken.None);

        var evt = publisher.Published.Should().ContainSingle().Subject;
        evt.PersonIdType.Should().Be(PersonIdentityType.Team);
        evt.PersonId.Should().Be(OwnerEventTestKit.TeamId);
        evt.EntityLogicalName.Should().Be("sprk_matter");
        evt.PersonId.Should().NotBe(OwnerEventTestKit.CallerOid);
    }

    [Fact]
    public void OfficeSave_And_QuickCreate_PublishThroughTheOwnerPath()
    {
        var save = OwnerEventTestKit.ReadSource("Services", "Office", "OfficeService.cs");
        save.Should().Contain("MembershipOwnerEvents.PublishOwnerAddedAsync(");
        save.Should().Contain("owningTeamId is { } savedTeamId ? new Microsoft.Xrm.Sdk.EntityReference(\"team\", savedTeamId) : null");
        save.Should().NotContain("PersonId = callerOid");

        var quickCreate = OwnerEventTestKit.ReadSource("Api", "Office", "OfficeEndpoints.cs");
        quickCreate.Should().Contain("MembershipOwnerEvents.PublishOwnerAddedAsync(");
        quickCreate.Should().Contain("\"sprk_matter\",\n                    response.Id,\n                    knownOwner: null,");
        quickCreate.Should().NotContain("PersonId = callerOid");
        quickCreate.Should().NotContain("resolve oid → systemuserid via Dataverse lookup", "the false comment is removed");
    }
}
