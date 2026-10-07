// R3 Part 1 Phase 2 — Task 082 (2026-06-22); rewritten by unified-access-control-r2 task 152 (2026-10-02), and again
// by its verifier round 1 (items 2 and 7) to RUN THE SITE instead of the shared helper plus a source grep.
//
// POST /api/v1/events (EventEndpoints.CreateEventAsync, internal for these tests) does two things task 152 owns:
//   - owner S1: the create is app-only, so Created By is the BFF application user and cannot say who the event is FOR.
//     The acting user's LINKED contact (task 141, PersonIdentity.ContactId) is written to sprk_assignedto — never an
//     email match; no link → blank.
//   - ADR-034 A3: it publishes the owner MembershipChangedEvent describing the row's REAL owner. Since task 146
//     (merged after 152) the create WRITES that owner — the team IRecordOwnershipResolver names — so the handler
//     already holds it and publishes it without reading the row back; an application-user owner cannot arise on
//     this path any more (the create refuses rather than leave the row app-owned).
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
using Sprk.Bff.Api.Tests.TestInfrastructure;
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
            new DataverseCreateEventRequest
            {
                Name = "Hearing", AssignedToContactId = ActingUsersContactId, OwningTeamId = OwnerEventTestKit.TeamId,
            });

        payload.Should().ContainKey("sprk_AssignedTo@odata.bind")
            .WhoseValue.Should().Be($"/contacts({ActingUsersContactId:D})");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateEventPayload_WithNoAssignee_WritesNoAssignedToBind(bool emptyGuid)
    {
        var payload = DataverseWebApiService.BuildCreateEventPayload(
            new DataverseCreateEventRequest
            {
                Name = "Hearing", AssignedToContactId = emptyGuid ? Guid.Empty : null, OwningTeamId = OwnerEventTestKit.TeamId,
            });

        payload.Keys.Should().NotContain(k => k.StartsWith("sprk_AssignedTo", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateEventPayload_WithNoOwner_Refuses_NoPayloadIsBuilt(bool emptyGuid)
    {
        // Task 146: the app-only create never leaves the row app-owned — no owner, no payload, nothing sent.
        var build = () => DataverseWebApiService.BuildCreateEventPayload(
            new DataverseCreateEventRequest { Name = "Hearing", OwningTeamId = emptyGuid ? Guid.Empty : null });

        build.Should().Throw<InvalidOperationException>().WithMessage("*OwningTeamId*");
    }

    [Fact]
    public void CreateEventPayload_BindsTheResolvedTeamAsOwner()
    {
        var payload = DataverseWebApiService.BuildCreateEventPayload(
            new DataverseCreateEventRequest { Name = "Hearing", OwningTeamId = OwnerEventTestKit.TeamId });

        payload.Should().ContainKey("ownerid@odata.bind").WhoseValue.Should().Be($"/teams({OwnerEventTestKit.TeamId})");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateEventPayload_BindsThePersonWhoAsked_OnlyWhenThereIsOne(bool hasPerson)
    {
        // Task 146 c1-r1 (owner round 13 item 9): the app-only create records the person; a writer for nobody records nobody.
        var person = Guid.Parse("c1c1c1c1-0000-4000-8000-0000000000e1");
        var payload = DataverseWebApiService.BuildCreateEventPayload(
            new DataverseCreateEventRequest
            {
                Name = "Hearing", OwningTeamId = OwnerEventTestKit.TeamId, CreatedByPersonId = hasPerson ? person : null,
            });

        if (hasPerson)
            payload.Should().ContainKey("sprk_CreatedByPerson@odata.bind").WhoseValue.Should().Be($"/systemusers({person:D})");
        else
            payload.Keys.Should().NotContain(k => k.StartsWith("sprk_CreatedByPerson", StringComparison.OrdinalIgnoreCase));
    }

    // ── ADR-034 A3: the owner event names the row's REAL owner — the team the create wrote (task 146) ─────────

    [Fact]
    public async Task CreateEvent_PublishesTheResolvedTeamItWrote_WithoutReadingTheRowBack()
    {
        var site = new Site(linkedContact: ActingUsersContactId);

        var result = await site.RunAsync();

        StatusOf(result).Should().Be(StatusCodes.Status201Created);
        site.Created!.OwningTeamId.Should().Be(RecordOwnershipResolverDouble.DefaultTeamId,
            "task 146: the app-only create is owned by the team the resolver named");
        var evt = site.Publisher.Published.Should().ContainSingle().Subject;
        evt.EntityLogicalName.Should().Be("sprk_event");
        evt.EntityRecordId.Should().Be(EventId, "the event is for the row this create wrote");
        evt.PersonIdType.Should().Be(PersonIdentityType.Team);
        evt.PersonId.Should().Be(RecordOwnershipResolverDouble.DefaultTeamId,
            "the owner the create wrote — not a read-back, and never the caller");
        evt.SourceField.Should().Be("ownerid");
        evt.MutationType.Should().Be(MembershipMutationType.Added);
        evt.CorrelationId.Should().Be(Site.TraceId);
        site.Dataverse.Verify(d => d.RetrieveAsync("sprk_event", EventId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "the create path already holds the owner it wrote");
    }

    [Fact]
    public async Task CreateEvent_PublishedOwnerIsNeverTheCallersOid()
    {
        var site = new Site(linkedContact: ActingUsersContactId, callerOid: OwnerEventTestKit.CallerOid);

        await site.RunAsync();

        var evt = site.Publisher.Published.Should().ContainSingle().Subject;
        evt.PersonId.Should().NotBe(OwnerEventTestKit.CallerOid);
        evt.PersonIdType.Should().Be(PersonIdentityType.Team);
    }

    [Fact]
    public async Task CreateEvent_OwnerRefused_CreatesNothing_PublishesNothing()
    {
        var site = new Site(linkedContact: ActingUsersContactId);
        site.Ownership.TeamId = null;

        var result = await site.RunAsync();

        StatusOf(result).Should().Be(StatusCodes.Status409Conflict);
        site.Created.Should().BeNull("a refusal writes nothing");
        site.Publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateEvent_PublisherThrows_TheCreateStillSucceeds()
    {
        var throwing = new Mock<IMembershipEventPublisher>();
        throwing.Setup(p => p.PublishAsync(It.IsAny<MembershipChangedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transport"));
        var site = new Site(linkedContact: ActingUsersContactId, publisher: throwing.Object);

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
        private readonly Guid? _callerOid;

        public Site(Guid? linkedContact, bool callerResolved = true,
            IMembershipEventPublisher? publisher = null, Guid? callerOid = null)
        {
            _callerResolved = callerResolved;
            _publisher = publisher ?? Publisher;
            _callerOid = callerOid;

            EventService.Setup(s => s.CreateEventAsync(It.IsAny<DataverseCreateEventRequest>(), It.IsAny<CancellationToken>()))
                .Callback<DataverseCreateEventRequest, CancellationToken>((r, _) => Created = r)
                .ReturnsAsync((EventId, DateTime.UtcNow));
            EventService.Setup(s => s.CreateEventLogAsync(
                    EventId, It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Guid.NewGuid());

            // A row owner the create did NOT write: were the handler to read it back, it would publish this user.
            Dataverse = OwnerEventTestKit.Dataverse(new EntityReference("systemuser", OwnerEventTestKit.HumanUserId));

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
        public RecordOwnershipResolverDouble Ownership { get; } = new();
        public DataverseCreateEventRequest? Created { get; private set; }

        // Task 159 added the regarding resolvers to the handler. These tests create an event with NO regarding, so
        // neither is reached: the strict record-type mock and the resolver over the strict Dataverse mock would fail
        // the test if either were.
        public Task<IResult> RunAsync() => EventEndpoints.CreateEventAsync(
            new ApiCreateEventRequest("Hearing prep"),
            EventService.Object,
            _publisher,
            Ownership,
            Dataverse.Object,
            CallerResolver.Object,
            Identity.Object,
            new Mock<ICommunicationDataverseService>(MockBehavior.Strict).Object,
            new Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver(
                Dataverse.Object,
                (_, _) => throw new InvalidOperationException("no regarding: the column probe must not be reached"),
                NullLogger<Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver>.Instance),
            HttpContextFor(_callerOid),
            NullLogger<Program>.Instance,
            CancellationToken.None);

        private static DefaultHttpContext HttpContextFor(Guid? oid)
        {
            var context = new DefaultHttpContext { TraceIdentifier = TraceId };
            if (oid is { } value)
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim("oid", value.ToString("D")) }, "test"));
            }

            return context;
        }
    }
}
