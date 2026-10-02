// R3 Part 1 Phase 2 — Task 082 (2026-06-22); rewritten by unified-access-control-r2 task 152 (2026-10-02).
//
// POST /api/v1/events creates the row on the app-only IEventDataverseService and sets no owner, so the publisher
// READS THE OWNER BACK. Task 152 (ADR-034 A3): the event states that real owner, in the junction's identity space —
// and an application-user owner (today's case: the BFF app user) is not a person, so no event is published, exactly
// as MembershipReconciliationJob records no junction row for it. The previous file rebuilt the old event (caller oid
// as a User, under a comment claiming Dataverse defaulted the owner to the OBO caller) and asserted on its own copy.

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Tests.Services.Ai.Membership.Events;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Events;

[Trait("status", "task-152-uac-r2")]
public class EventEndpointsMembershipPublishingTests
{
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    [Fact]
    public async Task EventCreate_RowOwnedByTheBffApplicationUser_PublishesNothing()
    {
        var publisher = new RecordingMembershipEventPublisher();
        var dataverse = OwnerEventTestKit.Dataverse(new EntityReference("systemuser", OwnerEventTestKit.ApplicationUserId));

        var evt = await MembershipOwnerEvents.PublishOwnerAddedAsync(
            publisher, dataverse.Object, "sprk_event", EventId, knownOwner: null, "trace-evt", NullLogger.Instance, CancellationToken.None);

        evt.Should().BeNull();
        publisher.Published.Should().BeEmpty("an application user is not a person — reconciliation applies the same rule");
    }

    [Fact]
    public async Task EventCreate_TeamOwnedRow_PublishesTeamOwner()
    {
        var publisher = new RecordingMembershipEventPublisher();
        var dataverse = OwnerEventTestKit.Dataverse(new EntityReference("team", OwnerEventTestKit.TeamId));

        await MembershipOwnerEvents.PublishOwnerAddedAsync(
            publisher, dataverse.Object, "sprk_event", EventId, knownOwner: null, "trace-evt", NullLogger.Instance, CancellationToken.None);

        var evt = publisher.Published.Should().ContainSingle().Subject;
        evt.PersonIdType.Should().Be(PersonIdentityType.Team);
        evt.PersonId.Should().Be(OwnerEventTestKit.TeamId);
        evt.SourceField.Should().Be("ownerid");
        dataverse.Verify(d => d.RetrieveAsync("sprk_event", EventId, It.Is<string[]>(c => c.Contains("ownerid")), It.IsAny<CancellationToken>()),
            Times.Once, "the owner is read back from the row this create wrote");
    }

    [Fact]
    public async Task EventCreate_HumanUserOwnedRow_PublishesUserOwnerBySystemUserId()
    {
        var publisher = new RecordingMembershipEventPublisher();
        var dataverse = OwnerEventTestKit.Dataverse(new EntityReference("systemuser", OwnerEventTestKit.HumanUserId));

        await MembershipOwnerEvents.PublishOwnerAddedAsync(
            publisher, dataverse.Object, "sprk_event", EventId, knownOwner: null, "trace-evt", NullLogger.Instance, CancellationToken.None);

        var evt = publisher.Published.Should().ContainSingle().Subject;
        evt.PersonIdType.Should().Be(PersonIdentityType.User);
        evt.PersonId.Should().Be(OwnerEventTestKit.HumanUserId, "a Dataverse systemuserid — never the AAD oid");
    }

    [Fact]
    public async Task EventCreate_PublisherThrows_DoesNotPropagate()
    {
        var throwing = new Mock<IMembershipEventPublisher>();
        throwing.Setup(p => p.PublishAsync(It.IsAny<MembershipChangedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transport"));
        var dataverse = OwnerEventTestKit.Dataverse(new EntityReference("team", OwnerEventTestKit.TeamId));

        var act = () => MembershipOwnerEvents.PublishOwnerAddedAsync(
            throwing.Object, dataverse.Object, "sprk_event", EventId, null, "trace-evt", NullLogger.Instance, CancellationToken.None);

        await act.Should().NotThrowAsync("ADR-034 Q2: fire-and-forget; reconciliation is the backstop");
    }

    [Fact]
    public void EventCreateEndpoint_PublishesThroughTheOwnerPath_ReadingTheOwnerBack()
    {
        var source = OwnerEventTestKit.ReadSource("Api", "Events", "EventEndpoints.cs");

        source.Should().Contain("MembershipOwnerEvents.PublishOwnerAddedAsync(");
        source.Should().Contain("\"sprk_event\",\n                eventId,\n                knownOwner: null,");
        source.Should().NotContain("PersonId = callerOid");
        source.Should().NotContain("defaulted by Dataverse to the OBO caller).");
    }
}
