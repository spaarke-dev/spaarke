// R3 Part 1 Phase 2 — Task 082 (2026-06-22); rewritten by unified-access-control-r2 task 152 (2026-10-02), and again
// by its verifier round 1 (items 2 and 7) to RUN THE SITE instead of the shared helper plus a source grep.
//
// POST /api/v1/events (EventEndpoints.CreateEventAsync, internal for these tests) does two things task 152 owns:
//   - owner S1: the create is app-only, so Created By is the BFF application user and cannot say who the event is FOR.
//     The acting user's LINKED contact (task 141, PersonIdentity.ContactId) is written to sprk_assignedto — never an
//     email match; no link → blank.
//   - ADR-034 A3: it publishes the owner MembershipChangedEvent describing the row's REAL owner, read back from the
//     row (the create sets none) — an application-user owner (today's case) publishes nothing, exactly as
//     MembershipReconciliationJob records no junction row for it.
// Every test below executes the production handler with boundary fakes and observes what reached Dataverse and the
// publisher.

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Tests.Services.Ai.Membership.Events;
using Xunit;
using ApiCreateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest;
using DataverseCreateEventRequest = Spaarke.Dataverse.CreateEventRequest;

namespace Sprk.Bff.Api.Tests.Api.Events;

[Trait("status", "task-152-uac-r2")]
public class EventEndpointsMembershipPublishingTests
{
    private static readonly Guid EventId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid ActingSystemUserId = Guid.Parse("abababab-1111-1111-1111-abababababab");
    private static readonly Guid ActingUsersContactId = Guid.Parse("cdcdcdcd-2222-2222-2222-cdcdcdcdcdcd");

    // ── Owner S1: Assigned To names the acting user's linked contact ─────────────────────────────────────────

    [Fact]
    public async Task CreateEvent_ActingUserWithALinkedContact_WritesThatContactToAssignedTo()
    {
        var site = new Site(linkedContact: ActingUsersContactId);

        var result = await site.RunAsync();

        StatusOf(result).Should().Be(StatusCodes.Status201Created);
        site.Created.Should().NotBeNull();
        site.Created!.AssignedToContactId.Should().Be(ActingUsersContactId,
            "owner S1: the app-only create's Created By cannot say who the event is FOR — Assigned To does");
        site.Identity.Verify(i => i.ResolveAsync(ActingSystemUserId, It.IsAny<CancellationToken>()), Times.Once,
            "the contact comes from the acting user's linked contact (task 141), nothing else");
    }

    [Fact]
    public async Task CreateEvent_ActingUserWithNoLinkedContact_LeavesAssignedToBlank_NoEmailMatch()
    {
        var site = new Site(linkedContact: null);

        await site.RunAsync();

        site.Created!.AssignedToContactId.Should().BeNull("no link → blank; never an email/UPN/name match (C7)");
    }

    [Fact]
    public async Task CreateEvent_CallerNotResolvedToASystemUser_LeavesAssignedToBlank_AndStillCreates()
    {
        var site = new Site(linkedContact: ActingUsersContactId, callerResolved: false);

        var result = await site.RunAsync();

        StatusOf(result).Should().Be(StatusCodes.Status201Created);
        site.Created!.AssignedToContactId.Should().BeNull();
        site.Identity.Verify(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void CreateEventPayload_WithAnAssignee_BindsSprkAssignedToTheContact()
    {
        var payload = DataverseWebApiService.BuildCreateEventPayload(
            new DataverseCreateEventRequest { Name = "Hearing", AssignedToContactId = ActingUsersContactId });

        payload.Should().ContainKey("sprk_AssignedTo@odata.bind")
            .WhoseValue.Should().Be($"/contacts({ActingUsersContactId:D})");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateEventPayload_WithNoAssignee_WritesNoAssignedToBind(bool emptyGuid)
    {
        var payload = DataverseWebApiService.BuildCreateEventPayload(
            new DataverseCreateEventRequest { Name = "Hearing", AssignedToContactId = emptyGuid ? Guid.Empty : null });

        payload.Keys.Should().NotContain(k => k.StartsWith("sprk_AssignedTo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateEvent_WhenTheAuditLogWriteFails_StillReturns201_NotA500ThatInvitesADuplicate()
    {
        // Task 097 review F1: before the fix POST /events wrote the event and THEN returned 500 (the audit payload named
        // a sprk_description column sprk_eventlog does not have); a client retry created a second event.
        var site = new Site(linkedContact: ActingUsersContactId, auditLogThrows: true);

        var result = await site.RunAsync();

        StatusOf(result).Should().Be(StatusCodes.Status201Created);
        site.EventService.Verify(s => s.CreateEventAsync(It.IsAny<DataverseCreateEventRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
    // ── ADR-034 A3: the owner event names the row's REAL owner, read back from the row ───────────────────────

    [Fact]
    public async Task CreateEvent_RowOwnedByTheBffApplicationUser_PublishesNothing()
    {
        var site = new Site(linkedContact: ActingUsersContactId,
            rowOwner: new EntityReference("systemuser", OwnerEventTestKit.ApplicationUserId));

        var result = await site.RunAsync();

        StatusOf(result).Should().Be(StatusCodes.Status201Created);
        site.Publisher.Published.Should().BeEmpty("an application user is not a person — reconciliation applies the same rule");
    }

    [Fact]
    public async Task CreateEvent_TeamOwnedRow_PublishesTheTeam_ReadBackFromTheRowItCreated()
    {
        var site = new Site(linkedContact: ActingUsersContactId,
            rowOwner: new EntityReference("team", OwnerEventTestKit.TeamId));

        await site.RunAsync();

        var evt = site.Publisher.Published.Should().ContainSingle().Subject;
        evt.EntityLogicalName.Should().Be("sprk_event");
        evt.EntityRecordId.Should().Be(EventId, "the event is for the row this create wrote");
        evt.PersonIdType.Should().Be(PersonIdentityType.Team);
        evt.PersonId.Should().Be(OwnerEventTestKit.TeamId);
        evt.SourceField.Should().Be("ownerid");
        evt.MutationType.Should().Be(MembershipMutationType.Added);
        evt.CorrelationId.Should().Be(Site.TraceId);
        site.Dataverse.Verify(d => d.RetrieveAsync("sprk_event", EventId, It.Is<string[]>(c => c.Contains("ownerid")), It.IsAny<CancellationToken>()),
            Times.Once, "the create sets no owner, so the owner is read back from the row it wrote");
    }

    [Fact]
    public async Task CreateEvent_HumanUserOwnedRow_PublishesTheSystemUserId_NeverTheCallersOid()
    {
        var site = new Site(linkedContact: ActingUsersContactId,
            rowOwner: new EntityReference("systemuser", OwnerEventTestKit.HumanUserId));

        await site.RunAsync();

        var evt = site.Publisher.Published.Should().ContainSingle().Subject;
        evt.PersonIdType.Should().Be(PersonIdentityType.User);
        evt.PersonId.Should().Be(OwnerEventTestKit.HumanUserId, "a Dataverse systemuserid — never the AAD oid");
        evt.PersonId.Should().NotBe(OwnerEventTestKit.CallerOid);
    }

    [Fact]
    public async Task CreateEvent_PublisherThrows_TheCreateStillSucceeds()
    {
        var throwing = new Mock<IMembershipEventPublisher>();
        throwing.Setup(p => p.PublishAsync(It.IsAny<MembershipChangedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transport"));
        var site = new Site(linkedContact: ActingUsersContactId,
            rowOwner: new EntityReference("team", OwnerEventTestKit.TeamId), publisher: throwing.Object);

        var result = await site.RunAsync();

        StatusOf(result).Should().Be(StatusCodes.Status201Created, "ADR-034 Q2: fire-and-forget; reconciliation is the backstop");
    }

    [Fact]
    public void EventCreateEndpoint_FalseOwnerCommentIsGone()
    {
        var source = OwnerEventTestKit.ReadSource("Api", "Events", "EventEndpoints.cs");

        source.Should().NotContain("PersonId = callerOid");
        source.Should().NotContain("defaulted by Dataverse to the OBO caller).");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static int? StatusOf(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

    /// <summary>The POST /api/v1/events site with boundary fakes; <see cref="RunAsync"/> executes the real handler.</summary>
    private sealed class Site
    {
        public const string TraceId = "trace-evt";

        private readonly IMembershipEventPublisher _publisher;
        private readonly bool _callerResolved;

        public Site(Guid? linkedContact, EntityReference? rowOwner = null, bool callerResolved = true,
            IMembershipEventPublisher? publisher = null, bool auditLogThrows = false)
        {
            _callerResolved = callerResolved;
            _publisher = publisher ?? Publisher;

            EventService.Setup(s => s.CreateEventAsync(It.IsAny<DataverseCreateEventRequest>(), It.IsAny<CancellationToken>()))
                .Callback<DataverseCreateEventRequest, CancellationToken>((r, _) => Created = r)
                .ReturnsAsync((EventId, DateTime.UtcNow));
            // Task 097 review F1: the audit row is pinned to the Created action (it was It.IsAny), and can be made to fail.
            var audit = EventService.Setup(s => s.CreateEventLogAsync(EventId, Spaarke.Dataverse.EventLogAction.Created, "Event created via API", It.IsAny<CancellationToken>()));
            if (auditLogThrows)
                audit.ThrowsAsync(new HttpRequestException("400: Invalid property 'sprk_description'"));
            else
                audit.ReturnsAsync(Guid.NewGuid());

            Dataverse = OwnerEventTestKit.Dataverse(rowOwner ?? new EntityReference("systemuser", OwnerEventTestKit.ApplicationUserId));

            CallerResolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(_callerResolved
                    ? CallerSystemUserResolution.Resolved(ActingSystemUserId.ToString("D"))
                    : CallerSystemUserResolution.Unresolved("no oid"));
            Identity.Setup(i => i.ResolveAsync(ActingSystemUserId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PersonIdentity(ActingSystemUserId, ContactId: linkedContact));
        }

        public Mock<IEventDataverseService> EventService { get; } = new(MockBehavior.Strict);
        public Mock<IGenericEntityService> Dataverse { get; }
        public Mock<ICallerSystemUserResolver> CallerResolver { get; } = new(MockBehavior.Strict);
        public Mock<IIdentityNormalizationService> Identity { get; } = new(MockBehavior.Strict);
        public RecordingMembershipEventPublisher Publisher { get; } = new();
        public DataverseCreateEventRequest? Created { get; private set; }

        public Task<IResult> RunAsync() => EventEndpoints.CreateEventAsync(
            new ApiCreateEventRequest("Hearing prep"),
            EventService.Object,
            _publisher,
            Dataverse.Object,
            CallerResolver.Object,
            Identity.Object,
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            new DefaultHttpContext { TraceIdentifier = TraceId },
            NullLogger<Program>.Instance,
            CancellationToken.None);
    }
}
