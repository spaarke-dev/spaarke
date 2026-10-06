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
/// unified-access-control-r2 task 156, verifier round 2 item 8, restated at the sweep integration: the event re-file route
/// <c>PUT /api/v1/events/{id}</c> that 156 wired to re-stamp after the write was DELETED by task 159 (owner round 10 item 1:
/// no caller in the repo, not in the published Copilot description). The stamp-freshness concern 156 closed for it now holds
/// because NO HTTP path re-files an event: through the REAL Program, both shapes 156 pinned (a pair move, a rename) reach no
/// handler, and nothing is read, patched or re-stamped.
/// </summary>
/// <remarks>
/// Only module boundaries are substituted: the event service and the Dataverse rows the cascade would read and PATCH
/// (<see cref="StampWorld"/>). A route that came back would have to answer here, and would need 156's re-stamp again.
/// </remarks>
public class EventRefileRestampRouteTests : IClassFixture<EventRefileRestampFixture>
{
    private readonly EventRefileRestampFixture _fixture;

    public EventRefileRestampRouteTests(EventRefileRestampFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reseed();
    }

    [Theory(DisplayName = "Task 156 x 159: PUT /api/v1/events/{id} is not routed in the real Program -- a pair move and a rename both reach nothing, read nothing and re-stamp nothing")]
    [InlineData("pair-move")]
    [InlineData("rename")]
    public async Task Put_TheDeletedRefileRoute_ReachesNothing_AndRestampsNothing(string shape)
    {
        using var client = _fixture.Client();
        object body = shape == "pair-move"
            ? new { regardingRecordType = 2, regardingRecordId = EventRefileRestampFixture.Invoice, regardingRecordName = "INV-0001" }
            : new { subject = "Hearing (moved)" };

        var response = await client.PutAsJsonAsync($"/api/v1/events/{EventRefileRestampFixture.Event}", body);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        _fixture.Events.ReceivedCalls().Should().BeEmpty("no event handler ran");
        _fixture.World.Patches.Should().BeEmpty();
        _fixture.World.Service.ReceivedCalls().Should().BeEmpty("nothing was re-stamped");
        _fixture.World.Lookup("sprk_event", EventRefileRestampFixture.Event, "sprk_regardingmatter")
            .Should().Be(EventRefileRestampFixture.MatterA, "the event's copy is untouched");
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

    /// <summary>The event service at its module boundary (no member is configured: no route may reach it here).</summary>
    internal IEventDataverseService Events { get; } = Substitute.For<IEventDataverseService>();

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
        Events.ClearReceivedCalls();
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
            services.AddSingleton(Events);

            services.RemoveAll<CoreAncestorRestamper>();
            services.AddSingleton(_ => World.Restamper);

            // The caller holds every right and the owner double would answer, so a 404/405 here is the ROUTE's
            // absence, never an authorization or ownership refusal.
            services.RemoveAll<IAccessDataSource>();
            services.AddSingleton<IAccessDataSource>(new AllowAllAccessDataSource());
            services.RemoveAll<IRecordOwnershipResolver>();
            services.AddSingleton<IRecordOwnershipResolver>(new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble());
        });
    }

    /// <summary>The caller holds every right on every record (the re-file's authorization is not this test's subject).</summary>
    private sealed class AllowAllAccessDataSource : IAccessDataSource
    {
        public Task<AccessSnapshot> GetUserAccessAsync(
            string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default) =>
            Task.FromResult(new AccessSnapshot
            {
                UserId = userId,
                ResourceId = resourceId,
                AccessRights = DataverseAccessRightsMapper.FromAccessRightsString(
                    "ReadAccess,WriteAccess,AppendAccess,AppendToAccess,CreateAccess,DeleteAccess,ShareAccess,AssignAccess"),
            });

        public Task<AccessSnapshot> GetRecordAccessAsync(
            string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default) =>
            GetUserAccessAsync(userId, recordId.ToString(), userAccessToken, ct);
    }
}
