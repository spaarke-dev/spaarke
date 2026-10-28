// unified-access-control-r2 task 194b fix round R (R3b-2) — POST /api/v1/events asks the owner question for the sprk_event it
// writes over EVERY parent the row will hold (the regarding's typed lookup and its FR-26 core stamps), in sprk_event's primary
// order (RecordOwnershipContext.ForRow) — the order the secure-child reconciler asks the written row in.

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Events;

[Trait("status", "task-194b-uac-r2")]
public class EventOwnershipContextTests
{
    private static readonly Guid ProjectId = Guid.Parse("194b0000-0000-4000-8000-0000000000e1");
    private static readonly Guid MatterId = Guid.Parse("194b0000-0000-4000-8000-0000000000e2");

    [Fact]
    public void AnEventRegardingAProjectStampedWithItsMatter_IsAskedInTheEventTablesPrimaryOrder_TheMatterFirst()
    {
        var question = EventEndpoints.OwnershipContextFor(
            Spaarke.Dataverse.RegardingRecordType.Project, ProjectId,
            [("sprk_regardingmatter", "sprk_matters", MatterId)],
            new DefaultHttpContext());

        question.Parents.Should().Equal(
            new RecordOwnershipParent("sprk_matter", MatterId),
            new RecordOwnershipParent("sprk_project", ProjectId));
        question.TargetEntityLogicalName.Should().Be("sprk_matter", "sprk_event lists the matter before the project");
        question.TargetRecordId.Should().Be(MatterId);
    }

    [Fact]
    public void AnEventRegardingNothing_NamesNoParent_TheCallersOwnUnitDecides()
    {
        var question = EventEndpoints.OwnershipContextFor(null, null, null, new DefaultHttpContext());

        question.Parents.Should().BeEmpty();
        question.HasParent.Should().BeFalse();
    }
}
