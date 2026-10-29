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

    // ── 194b V-fix (verifier A F1): a regarding ownership parent with no sprk_event column is refused, fail closed ──

    private TaskActionCore Core() => new(
        _entities.Object,
        CoreAncestorResolverFixtures.Inert(),
        _ownership,
        IdentityNormalizationFixtures.WithContact(null).Object,
        Mock.Of<ICommunicationDataverseService>(),
        Mock.Of<ILogger<TaskActionCore>>());

    [Theory]
    [InlineData("sprk_document")]
    [InlineData("sprk_todo")]
    [InlineData("sprk_agreement")]
    [InlineData("sprk_billingevent")]
    public async Task ATaskRegardingAnOwnershipParentTheTaskTableCannotFile_IsRefused_AndNothingIsWritten(string regardingType)
    {
        // Created without the regarding, the task would be asked about no parent and land in the acting user's team even
        // when the regarding record is Secure.
        var id = await Core().CreateAsync(
            new TaskActionInput("Review it", null, null, Guid.NewGuid(), regardingType, null, ActingUserId: ActingUser),
            CancellationToken.None);

        id.Should().Be(Guid.Empty);
        _created.Should().BeEmpty("a refused task writes nothing");
        _ownership.Requests.Should().BeEmpty("no owner question is asked about a row that will not be written");
        TaskActionCore.RegardingRefusalOf(regardingType, Guid.NewGuid()).Should().StartWith(TaskActionCore.RegardingNotFilableCode);
    }

    [Fact]
    public async Task ATaskRegardingASupportedRecord_IsStillCreatedAndAskedAboutIt()
    {
        var id = await Core().CreateAsync(
            new TaskActionInput("Review the matter", null, null, MatterId, "sprk_matter", null, ActingUserId: ActingUser),
            CancellationToken.None);

        id.Should().NotBe(Guid.Empty);
        var written = _created.Should().ContainSingle().Subject;
        _ownership.Requests.Should().ContainSingle().Which.Parents.Should()
            .Equal(new RecordOwnershipParent("sprk_matter", MatterId));
        written.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(MatterId);
    }

    [Fact]
    public async Task ATaskRegardingATypeThatIsNotAnOwnershipParent_AndHasNoColumn_IsStillCreated_Unfiled()
    {
        TaskActionCore.RegardingRefusalOf("systemuser", Guid.NewGuid()).Should().BeNull();

        var id = await Core().CreateAsync(
            new TaskActionInput("Call them", null, null, Guid.NewGuid(), "systemuser", null, ActingUserId: ActingUser),
            CancellationToken.None);

        id.Should().NotBe(Guid.Empty, "the degraded branch is kept for a regarding that files nothing");
        _created.Should().ContainSingle();
        _ownership.Requests.Should().ContainSingle().Which.Parents.Should().BeEmpty();
    }

    [Fact]
    public void ASupportedRegarding_OrNoRegardingId_IsNotRefused()
    {
        TaskActionCore.RegardingRefusalOf("sprk_matter", Guid.NewGuid()).Should().BeNull();
        TaskActionCore.RegardingRefusalOf("sprk_reportcard", Guid.NewGuid()).Should().BeNull();
        TaskActionCore.RegardingRefusalOf("sprk_document", null).Should().BeNull("no regarding id, no regarding");
    }
}
