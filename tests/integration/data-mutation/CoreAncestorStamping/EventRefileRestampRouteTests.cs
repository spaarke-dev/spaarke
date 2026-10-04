using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;

/// <summary>
/// unified-access-control-r2 task 156, verifier round 2 item 8 — the REAL mapped <c>PUT /api/v1/events/{id}</c> route. The
/// update writes only the event's regarding PAIR, but on an event that carries two typed sources the pair decides which
/// one its copy comes from (<see cref="CoreAncestorResolver.ClassifyStampSource"/> rule 3). Moving the pair re-stamps the
/// event, and everything filed under it, in the same request.
/// </summary>
/// <remarks>
/// Only module boundaries are substituted: the event service (its PATCH applies the pair to the in-memory row, as
/// <c>DataverseWebApiService.UpdateEventAsync</c> writes it) and the Dataverse rows the cascade reads and PATCHes
/// (<see cref="StampWorld"/>). The restamper is the real one.
/// </remarks>
public class EventRefileRestampRouteTests : IClassFixture<EventRefileRestampFixture>
{
    private readonly EventRefileRestampFixture _fixture;

    public EventRefileRestampRouteTests(EventRefileRestampFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reseed();
    }

    [Fact(DisplayName = "Task 156 route (verifier round 2 item 8): PUT /api/v1/events/{id} moving the pair from the event's communication (matter A) to its invoice (matter B) re-stamps the event AND the to-do under it in the same request")]
    public async Task Put_MovingThePairToTheEventsOtherSource_RestampsTheEventAndItsChildren()
    {
        using var client = _fixture.Client();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/events/{EventRefileRestampFixture.Event}",
            new
            {
                regardingRecordType = 2, // Invoice
                regardingRecordId = EventRefileRestampFixture.Invoice,
                regardingRecordName = "INV-0001",
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.World.Lookup("sprk_event", EventRefileRestampFixture.Event, "sprk_regardingmatter")
            .Should().Be(EventRefileRestampFixture.MatterB, "the event's copy now comes from its invoice");
        _fixture.World.Lookup("sprk_todo", EventRefileRestampFixture.TodoUnderEvent, "sprk_regardingmatter")
            .Should().Be(EventRefileRestampFixture.MatterB, "and the cascade carries it to the to-do filed under the event");
    }

    [Fact(DisplayName = "Task 156 route (verifier round 2 item 8): an event update that does not touch the pair (a rename) re-stamps nothing and reads nothing")]
    public async Task Put_RenamingTheEvent_RestampsNothing_AndReadsNothing()
    {
        using var client = _fixture.Client();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/events/{EventRefileRestampFixture.Event}", new { subject = "Hearing (moved)" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.World.Patches.Should().BeEmpty();
        _fixture.World.Service.ReceivedCalls().Should().BeEmpty("a write that cannot move a stamp costs nothing");
    }
}

/// <summary>Test host for <see cref="EventRefileRestampRouteTests"/>: the BFF with the event service and a <see cref="StampWorld"/>.</summary>
public sealed class EventRefileRestampFixture : CustomWebAppFactory
{
    internal static readonly Guid MatterA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    internal static readonly Guid MatterB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    internal static readonly Guid Communication = Guid.Parse("15600000-0000-0000-0000-000000000c31");
    internal static readonly Guid Invoice = Guid.Parse("15600000-0000-0000-0000-000000000c32");
    internal static readonly Guid Event = Guid.Parse("15600000-0000-0000-0000-000000000e31");
    internal static readonly Guid TodoUnderEvent = Guid.Parse("15600000-0000-0000-0000-000000000131");

    internal StampWorld World { get; } = new();

    /// <summary>
    /// Fresh rows per test (the host is shared by the class): an event filed under a communication (matter A — the pair
    /// names it, so its copy says A) that ALSO names an invoice of matter B, and a to-do under the event copying A.
    /// </summary>
    internal void Reseed()
    {
        World.ReplaceWith(new StampWorld()
            .Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", MatterA)])
            .Row("sprk_invoice", Invoice, [("sprk_matter", "sprk_matter", MatterB)])
            .Row("sprk_event", Event,
            [
                ("sprk_regardingcommunication", "sprk_communication", Communication),
                ("sprk_regardinginvoice", "sprk_invoice", Invoice),
                ("sprk_regardingmatter", "sprk_matter", MatterA),
            ], pairId: Communication.ToString())
            .Row("sprk_todo", TodoUnderEvent,
                [("sprk_regardingevent", "sprk_event", Event), ("sprk_regardingmatter", "sprk_matter", MatterA)],
                pairId: Event.ToString()));
        World.Service.ClearReceivedCalls();
    }

    public HttpClient Client()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEventDataverseService>();
            services.AddSingleton(BuildEvents());

            services.RemoveAll<CoreAncestorRestamper>();
            services.AddSingleton(_ => World.Restamper);

            // Task 146 (merged with 156): a regarding change is a RE-FILE — authorized as the caller (Write on the event,
            // AppendTo on the record it is filed to) and its owner re-derived before the write. Not under test here: the
            // caller holds exactly those rights, and a double owns every re-file by one team.
            var access = new Sprk.Bff.Api.Tests.Api.Finance.FinanceEndpointsAuthorizationContractTests.RecordingAccessDataSource();
            access.Grant("sprk_events", Event, Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.Write);
            access.Grant("sprk_invoices", Invoice, Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.AppendTo);
            services.RemoveAll<IAccessDataSource>();
            services.AddSingleton<IAccessDataSource>(access);
            services.RemoveAll<IRecordOwnershipResolver>();
            services.AddSingleton<IRecordOwnershipResolver>(new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble());
        });
    }

    /// <summary>The event service: the event exists, and its update writes the pair exactly as the Web API service does.</summary>
    private IEventDataverseService BuildEvents()
    {
        var events = Substitute.For<IEventDataverseService>();
        events.GetEventAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<EventEntity?>(new EventEntity
            {
                Id = call.ArgAt<Guid>(0),
                Name = "Hearing",
                StatusCode = 3,
            }));
        events.UpdateEventAsync(Arg.Any<Guid>(), Arg.Any<UpdateEventRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.ArgAt<UpdateEventRequest>(1);
                if (request.RegardingRecordType.HasValue)
                {
                    World.SetPair("sprk_event", call.ArgAt<Guid>(0), request.RegardingRecordId);

                    // Task 146 r1: the update also binds the entity-specific regarding lookups beside the pair.
                    foreach (var (recordType, recordId) in request.RegardingLookupWrites())
                    {
                        World.OutOfBand(
                            "sprk_event", call.ArgAt<Guid>(0),
                            RegardingRecordType.GetLookupFieldName(recordType)!,
                            RegardingRecordType.GetEntityLogicalName(recordType)!,
                            recordId);
                    }
                }

                return Task.CompletedTask;
            });
        return events;
    }
}
