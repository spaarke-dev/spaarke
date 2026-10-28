// unified-access-control-r2 task 194b fix round R (B3) — TaskActionCore asks the owner question for the sprk_event it writes in
// sprk_event's PRIMARY ORDER (RecordOwnershipContext.ForRow), the order the secure-child reconciler asks the written row in —
// never with the regarding named first. A writer whose primary differs from the reconciler's files rows the reconciler later
// moves (an ordinary row's business unit is its primary parent's).

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Services.Communication;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

[Trait("status", "task-194b-uac-r2")]
public class TaskActionCoreOwnershipOrderTests
{
    private static readonly Guid ActingUser = Guid.Parse("aaaaaaaa-0194-0000-0000-00000000000a");
    private static readonly Guid ReportCardId = Guid.Parse("19400000-0000-0000-0000-0000000000c1");
    private static readonly Guid MatterId = Guid.Parse("19400000-0000-0000-0000-0000000000a1");

    private readonly Mock<IGenericEntityService> _entities = new();
    private readonly List<Entity> _created = new();
    private readonly RecordOwnershipResolverDouble _ownership = new();

    public TaskActionCoreOwnershipOrderTests()
    {
        _entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => _created.Add(e))
            .ReturnsAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task ATaskRegardingAReportCardOfAMatter_IsAskedInTheEventTablesPrimaryOrder_TheMatterFirst()
    {
        // A report card is filed under a matter, so the task carries the report card (its regarding) AND the matter (the
        // FR-26 core-ancestor stamp). sprk_event's lineage lists the matter before the report card: the reconciler asks the
        // written row with the matter as its primary, so the writer must too.
        var core = new TaskActionCore(
            _entities.Object,
            CoreAncestorResolverFixtures.WithAncestors(("sprk_regardingmatter", MatterId)),
            _ownership,
            IdentityNormalizationFixtures.WithContact(null).Object,
            Mock.Of<ICommunicationDataverseService>(),
            Mock.Of<ILogger<TaskActionCore>>());

        await core.CreateAsync(
            new TaskActionInput("Review the scorecard", null, null, ReportCardId, "sprk_reportcard", null, ActingUserId: ActingUser),
            CancellationToken.None);

        var written = _created.Should().ContainSingle().Subject;
        var asked = _ownership.Requests.Should().ContainSingle().Subject;

        written.LogicalName.Should().Be("sprk_event");
        asked.Parents.Should().Equal(
            new RecordOwnershipParent("sprk_matter", MatterId),
            new RecordOwnershipParent("sprk_reportcard", ReportCardId));
        asked.TargetEntityLogicalName.Should().Be("sprk_matter", "the primary (an ordinary row's business unit) is the table's first");
        asked.TargetRecordId.Should().Be(MatterId);
        asked.Parents.Should().Equal(RecordOwnershipContext.OrderedParentsOf(written),
            "the writer asks exactly the question the reconciler asks about the written row");
    }
}
