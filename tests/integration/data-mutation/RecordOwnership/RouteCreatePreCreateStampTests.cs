using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 017, Phase 2d-1 (owner decision 2a): POST /api/v1/child-records/{table} through the
/// REAL route and the REAL <see cref="RecordCreationService"/>, whose step 10 now derives the I-1 stamp before the create. The
/// two new refusals pass the route's edge (<see cref="ChildRecordEndpoints.RefusalResult"/>) unchanged, through the shared map:
/// 400 <c>record_creation.stamp_underivable</c> with the service's words, and 503 <c>record_creation.stamp_unavailable</c> with
/// the map's fixed words. Nothing is created on either. Every existing route row (017b's NFR-07 table) is pinned elsewhere and
/// unchanged.
/// </summary>
/// <remarks>A partial of <see cref="SecureChildOwnershipAiToolTests"/> (its route harness), as RouteDecisionCreateParityTests is.</remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    /// <summary>The route's create over a stamp world this test controls (the harness's own is empty).</summary>
    private Task<IResult> CreateChildOverStamps(string table, Dictionary<string, object?> payload, StampWorld stamps) =>
        ChildRecordEndpoints.CreateAsync(
            table, JsonSerializer.SerializeToElement(payload), _user,
            RouteRecordCreation.Over(
                _world.Resolver(), _appOnly.Object, stamps.Restamper, Shares(), SecureRootFilingGateFixtures.NothingSecure(),
                null, _routeMembership.Object),
            HttpContextOfCaller(), NullLogger<Program>.Instance, CancellationToken.None);

    private Dictionary<string, object?> AnEventUnderTheEvent() => new()
    {
        ["sprk_eventname"] = "Follow-up",
        ["sprk_RegardingEvent@odata.bind"] = $"/sprk_events({Event:D})",
    };

    private void TheEventIsFiledUnderTheOrdinaryMatter()
    {
        _world.WithRecord("sprk_event", Event, Directory.ChildBu, owningTeam: Directory.ChildTeam);
        ShareWorld.OrdinaryChild("sprk_event", Event, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
    }

    [Fact]
    public async Task RouteCreate_AnEventUnderAnEvent_CarriesItsMattersStampInTheCreatePayload()
    {
        TheEventIsFiledUnderTheOrdinaryMatter();
        var stamps = new StampWorld().Row("sprk_matter", OrdinaryMatter)
            .Row("sprk_event", Event, [("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)]);

        var result = await CreateChildOverStamps("sprk_event", AnEventUnderTheEvent(), stamps);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        _appCreates.Should().ContainSingle().Which.Fields["sprk_RegardingMatter@odata.bind"]
            .Should().Be($"/sprk_matters({OrdinaryMatter:D})", "step 10 derives the stamp into the one create");
    }

    [Fact]
    public async Task RouteCreate_WhenTheParentsRootCannotBeDetermined_Is400StampUnderivable_ThroughTheSharedMap_AndCreatesNothing()
    {
        TheEventIsFiledUnderTheOrdinaryMatter();
        var stamps = new StampWorld(); // the event is gone by the time its root is read: NotFound, a data condition

        var result = await CreateChildOverStamps("sprk_event", AnEventUnderTheEvent(), stamps);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be("record_creation.stamp_underivable");
        problem.ProblemDetails.Extensions["errorCode"].Should().Be("record_creation.stamp_underivable");
        problem.ProblemDetails.Detail.Should().StartWith("The event was not saved: the core record it belongs to cannot be determined");
        _appCreates.Should().BeEmpty("nothing is created");
        _user.Posts.Should().BeEmpty("and there is no caller-side fallback");
    }

    [Fact]
    public async Task RouteCreate_WhenTheParentsRootCannotBeReadRightNow_Is503StampUnavailable_ThroughTheSharedMap_AndCreatesNothing()
    {
        TheEventIsFiledUnderTheOrdinaryMatter();
        var stamps = new StampWorld().Row("sprk_event", Event, [("sprk_regardingmatter", "sprk_matter", OrdinaryMatter)])
            .FailRead("sprk_event", Event);

        var result = await CreateChildOverStamps("sprk_event", AnEventUnderTheEvent(), stamps);

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be("record_creation.stamp_unavailable");
        problem.ProblemDetails.Detail.Should().Be(
            "The event could not be saved right now, so nothing was created. Try again in a few minutes.");
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }
}
