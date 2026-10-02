// R3 Part 1 Phase 2 — Task 082 (2026-06-22); rewritten by unified-access-control-r2 task 152 (2026-10-02).
//
// POST /api/v1/documents publishes the owner MembershipChangedEvent for the row it created. Task 152 (ADR-034 A3):
// the event states the row's REAL owner — the business-unit default owner TEAM the endpoint wrote (task 080) — as
// PersonIdType=Team, PersonId=teamid, the key MembershipReconciliationJob builds for the same row. Before task 152
// the endpoint published the caller's AAD oid as a User owner: false for a team-owned row, and in an identity space
// reconciliation never writes. The previous version of this file rebuilt that event by hand and asserted on its own
// copy, so it could not have caught the defect; these tests run the production publishing path with the exact
// arguments the endpoint passes, and a wiring guard pins that the endpoint passes them.

using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Tests.Services.Ai.Membership.Events;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Documents;

[Trait("status", "task-152-uac-r2")]
public class DataverseDocumentsEndpointsMembershipPublishingTests
{
    private static readonly Guid DocumentId = Guid.Parse("ffffffff-0000-0000-0000-000000000001");

    [Fact]
    public async Task DocumentCreate_TeamOwnedRow_PublishesTeamOwner_NotTheCallersOid()
    {
        var publisher = new RecordingMembershipEventPublisher();
        var dataverse = OwnerEventTestKit.Dataverse();

        // Exactly what POST /api/v1/documents passes: the team it wrote as the owner.
        await MembershipOwnerEvents.PublishOwnerAddedAsync(
            publisher, dataverse.Object, "sprk_document", DocumentId,
            new EntityReference("team", OwnerEventTestKit.TeamId), "trace-doc", NullLogger.Instance, CancellationToken.None);

        var evt = publisher.Published.Should().ContainSingle().Subject;
        evt.PersonIdType.Should().Be(PersonIdentityType.Team);
        evt.PersonId.Should().Be(OwnerEventTestKit.TeamId);
        evt.PersonId.Should().NotBe(OwnerEventTestKit.CallerOid, "no publisher emits an AAD oid");
        evt.SourceField.Should().Be("ownerid");
        evt.EntityLogicalName.Should().Be("sprk_document");
        evt.EntityRecordId.Should().Be(DocumentId);
        evt.MutationType.Should().Be(MembershipMutationType.Added);
        evt.CorrelationId.Should().Be("trace-doc", "NFR-08");
    }

    [Fact]
    public void DocumentCreateEndpoint_PublishesThroughTheOwnerPath_WithTheTeamItWrote()
    {
        var source = OwnerEventTestKit.ReadSource("Api", "DataverseDocumentsEndpoints.cs");

        source.Should().Contain("MembershipOwnerEvents.PublishOwnerAddedAsync(");
        source.Should().Contain("new Microsoft.Xrm.Sdk.EntityReference(\"team\", owningTeamId.Value)");
        source.Should().NotContain("PersonId = callerOid", "the caller's oid is never an event's person");
        source.Should().NotContain("defaulted by Dataverse to the OBO", "the false owner comment is removed");
    }
}
