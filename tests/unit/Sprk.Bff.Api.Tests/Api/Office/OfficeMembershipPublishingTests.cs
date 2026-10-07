// unified-access-control-r2 task 152 — the two Office owner-event publishers, which had no publishing tests.
// Verifier round 1 (item 7): the tests now RUN each site instead of the shared helper plus a whitespace-exact grep.
//
//   - Quick-create matter (OfficeEndpoints.QuickCreateAsync, POST /office/quickcreate/matter): RecordCreationService
//     writes the business-unit default owner TEAM and does not return it, so the site reads the owner back. The tests
//     execute the real handler.
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

    // ── Quick-create: the real handler ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task QuickCreateMatter_ReadsTheOwnerBackFromTheRowItCreated_AndPublishesTheTeam()
    {
        var site = new QuickCreateSite(rowOwner: new EntityReference("team", OwnerEventTestKit.TeamId));

        var result = await site.RunAsync("matter");

        (result as IStatusCodeHttpResult)?.StatusCode.Should().Be(StatusCodes.Status201Created);
        var evt = site.Publisher.Published.Should().ContainSingle().Subject;
        evt.EntityLogicalName.Should().Be("sprk_matter");
        evt.EntityRecordId.Should().Be(RecordId, "the event is for the matter this request created");
        evt.PersonIdType.Should().Be(PersonIdentityType.Team);
        evt.PersonId.Should().Be(OwnerEventTestKit.TeamId);
        evt.PersonId.Should().NotBe(OwnerEventTestKit.CallerOid, "no publisher emits an AAD oid");
        evt.SourceField.Should().Be("ownerid");
        evt.CorrelationId.Should().Be(QuickCreateSite.TraceId);
        site.Dataverse.Verify(d => d.RetrieveAsync("sprk_matter", RecordId, It.Is<string[]>(c => c.Contains("ownerid")), It.IsAny<CancellationToken>()),
            Times.Once, "RecordCreationService does not return the owner, so the site reads it back");
    }

    [Fact]
    public async Task QuickCreateMatter_RowOwnedByAnApplicationUser_PublishesNothing()
    {
        var site = new QuickCreateSite(rowOwner: new EntityReference("systemuser", OwnerEventTestKit.ApplicationUserId));

        await site.RunAsync("matter");

        site.Publisher.Published.Should().BeEmpty("an application user is not a person — reconciliation applies the same rule");
    }

    [Fact]
    public async Task QuickCreateProject_PublishesNoOwnerEvent()
    {
        // The matter is the only quick-create entity this site publishes for (event-source-inventory §3A).
        var site = new QuickCreateSite(rowOwner: new EntityReference("team", OwnerEventTestKit.TeamId));

        await site.RunAsync("project");

        site.Publisher.Published.Should().BeEmpty();
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

    /// <summary>The POST /office/quickcreate/{entityType} site with boundary fakes; runs the real handler.</summary>
    private sealed class QuickCreateSite
    {
        public const string TraceId = "trace-qc";

        public QuickCreateSite(EntityReference rowOwner)
        {
            Dataverse = OwnerEventTestKit.Dataverse(rowOwner);
            CallerResolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CallerSystemUserResolution.Resolved(OwnerSystemUserId.ToString("D")));
            Office.Setup(o => o.QuickCreateAsync(
                    It.IsAny<QuickCreateEntityType>(), It.IsAny<QuickCreateRequest>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((QuickCreateEntityType type, QuickCreateRequest request, string _, string? _, CancellationToken _) =>
                    new QuickCreateResponse
                    {
                        Id = RecordId,
                        EntityType = type,
                        LogicalName = QuickCreateFieldRequirements.GetLogicalName(type),
                        Name = request.Name ?? string.Empty,
                    });
        }

        public Mock<IOfficeService> Office { get; } = new(MockBehavior.Strict);
        public Mock<ICallerSystemUserResolver> CallerResolver { get; } = new(MockBehavior.Strict);
        public Mock<Spaarke.Dataverse.IGenericEntityService> Dataverse { get; }
        public RecordingMembershipEventPublisher Publisher { get; } = new();

        public Task<IResult> RunAsync(string entityType)
        {
            var context = new DefaultHttpContext { TraceIdentifier = TraceId };
            context.Items[OfficeAuthFilter.UserIdKey] = OwnerEventTestKit.CallerOid.ToString("D");
            return OfficeEndpoints.QuickCreateAsync(
                entityType,
                new QuickCreateRequest { Name = "Acme v. Beta" },
                Office.Object,
                Publisher,
                CallerResolver.Object,
                Dataverse.Object,
                NullLogger<Program>.Instance,
                context,
                CancellationToken.None);
        }
    }
}
