// unified-access-control-r2 task 152 — the two Office owner-event publishers, which had no publishing tests.
// Verifier round 1 (item 7): the tests now RUN each site instead of the shared helper plus a whitespace-exact grep.
//
//   - Quick-create matter (POST /office/quickcreate/matter): field mapping task 018b moved the matter create onto the
//     unified RecordCreationService, whose post-create step publishes the owner event with the team it wrote. The endpoint's
//     own publish is removed (UAC-r2 condition (b) item i: exactly once). The single publish is asserted over HTTP in
//     tests/integration/contract/Api/Office/OfficeQuickCreateMembershipPublishTests.cs; here a guard pins the removal.
//   - Office save (OfficeService, POST /office/save): the document is owned by the team the save resolved (task 080).
//     SaveAsync depends on a dozen concrete collaborators (SPE upload, job store, persistence) and has no unit harness,
//     so the site's own decisions — the table, the team reference, the read-back fallback — live in
//     OfficeService.PublishSavedDocumentOwnerAsync, which SaveAsync calls with (documentId, owningTeamId). The tests run
//     that method; one guard pins that SaveAsync calls it with those two values.
//
// Both state the row's REAL owner (PersonIdType=Team, PersonId=teamid) instead of the caller's AAD oid as a User.

using System.Security.Claims;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Office;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.Services.Ai.Membership.Events;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

[Trait("status", "task-152-uac-r2")]
public class OfficeMembershipPublishingTests
{
    private static readonly Guid RecordId = Guid.Parse("ffffffff-0000-0000-0000-0000000000aa");
    private static readonly Guid OwnerSystemUserId = Guid.Parse("abababab-3333-3333-3333-abababababab");

    // ── Quick-create: the endpoint no longer publishes (field mapping task 018b) ──────────────────────────────

    [Fact]
    public void QuickCreate_TheEndpointPublishesNoOwnerEvent_TheUnifiedCreatePublishesTheMatters()
    {
        // UAC-r2 condition (b) item i: one publish per Office matter create. The unified create's step 12 publishes it
        // (MembershipPublishingTables names sprk_matter); a second publish at the endpoint would make it two.
        var endpoints = OwnerEventTestKit.ReadSource("Api", "Office", "OfficeEndpoints.cs");
        endpoints.Should().NotContain("PublishOwnerAddedAsync", "the quick-create endpoint's own publish is removed");
        endpoints.Should().NotContain("IMembershipEventPublisher", "no Office endpoint publishes an owner event itself");

        Regex.Replace(OwnerEventTestKit.ReadSource("Services", "RecordCreation", "RecordCreationService.cs"), @"\s+", " ")
            .Should().Contain("{ \"sprk_matter\", \"sprk_event\", \"sprk_document\" }",
                "the unified create publishes the matter's owner event in its post-create step");
    }

    // ── Office save: the site's publish decision ────────────────────────────────────────────────────────────

    [Fact]
    public async Task OfficeSave_PublishesTheTeamTheSaveResolved_WithoutAReadBack()
    {
        var publisher = new RecordingMembershipEventPublisher();
        var dataverse = OwnerEventTestKit.Dataverse();

        await OfficeService.PublishSavedDocumentOwnerAsync(
            publisher, dataverse.Object, RecordId, OwnerEventTestKit.TeamId, "corr-save", NullLogger.Instance, CancellationToken.None);

        var evt = publisher.Published.Should().ContainSingle().Subject;
        evt.EntityLogicalName.Should().Be("sprk_document");
        evt.EntityRecordId.Should().Be(RecordId);
        evt.PersonIdType.Should().Be(PersonIdentityType.Team);
        evt.PersonId.Should().Be(OwnerEventTestKit.TeamId, "the team this save resolved and wrote");
        evt.SourceField.Should().Be("ownerid");
        evt.CorrelationId.Should().Be("corr-save");
        dataverse.Verify(d => d.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "a team owner the save already holds needs no read");
    }

    [Fact]
    public async Task OfficeSave_WithNoResolvedTeam_ReadsTheOwnerBackFromTheDocument()
    {
        var publisher = new RecordingMembershipEventPublisher();
        var dataverse = OwnerEventTestKit.Dataverse(new EntityReference("systemuser", OwnerEventTestKit.HumanUserId));

        await OfficeService.PublishSavedDocumentOwnerAsync(
            publisher, dataverse.Object, RecordId, owningTeamId: null, "corr-save", NullLogger.Instance, CancellationToken.None);

        var evt = publisher.Published.Should().ContainSingle().Subject;
        evt.PersonIdType.Should().Be(PersonIdentityType.User);
        evt.PersonId.Should().Be(OwnerEventTestKit.HumanUserId);
        dataverse.Verify(d => d.RetrieveAsync("sprk_document", RecordId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void OfficeSave_CallsItsPublishWithTheSavedDocumentAndTheResolvedTeam()
    {
        // SaveAsync has no unit harness (see file header); this pins the one call it makes, whitespace-insensitively.
        var save = Regex.Replace(OwnerEventTestKit.ReadSource("Services", "Office", "OfficeService.cs"), @"\s+", " ");

        save.Should().Contain(
            "_ = PublishSavedDocumentOwnerAsync( _membershipEventPublisher, _genericEntityService, documentId, owningTeamId, correlationId,");
        save.Should().NotContain("PersonId = callerOid");
    }

    [Fact]
    public void QuickCreate_FalseOwnerCommentIsGone()
    {
        OwnerEventTestKit.ReadSource("Api", "Office", "OfficeEndpoints.cs")
            .Should().NotContain("resolve oid → systemuserid via Dataverse lookup", "the false comment is removed");
    }
}
