// unified-access-control-r2 task 152 — owner decision A7 (round 3, REVERSED; binding amendment R3 on task 152):
// an Office quick-created matter/project names its MAKER in sprk_assignedtointernal (editable). The create is app-only,
// so Created By cannot make the record "for" the maker in the Daily Briefing; this column does (ADR-034 A3).

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Office;

[Trait("status", "task-152-uac-r2")]
public class RecordCreationAssignedInternalTests
{
    private static readonly Guid Maker = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid MakersContact = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid Team = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");

    private readonly Mock<IGenericEntityService> _entities = new(MockBehavior.Loose);
    private readonly List<Entity> _created = new();

    public RecordCreationAssignedInternalTests()
    {
        _entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => _created.Add(e))
            .ReturnsAsync(Guid.NewGuid());
    }

    private RecordCreationService Sut(Guid? makersContact)
    {
        var ownership = new Mock<IRecordOwnershipResolver>();
        ownership.Setup(o => o.ResolveOwningTeamAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Team);
        return new RecordCreationService(
            _entities.Object,
            new Mock<IFieldMappingDataverseService>().Object,
            ownership.Object,
            IdentityNormalizationFixtures.WithContact(makersContact).Object,
            NullLogger<RecordCreationService>.Instance);
    }

    [Theory]
    [InlineData(QuickCreateEntityType.Matter)]
    [InlineData(QuickCreateEntityType.Project)]
    public async Task QuickCreate_NamesTheMakerInAssignedToInternal(QuickCreateEntityType type)
    {
        var result = await Sut(MakersContact).CreateAsync(new RecordCreationRequest
        {
            EntityType = type, Name = "New record", CallerUserId = "oid", OwnerSystemUserId = Maker.ToString("D"),
        });

        result.Succeeded.Should().BeTrue();
        var row = _created.Single();
        row["sprk_assignedtointernal"].Should().BeEquivalentTo(new EntityReference("contact", MakersContact));
        row["ownerid"].Should().BeEquivalentTo(new EntityReference("team", Team), "ACCESS still comes from the business-unit team");
    }

    [Fact]
    public async Task QuickCreate_MakerWithoutALinkedContact_LeavesItBlank_StillCreates()
    {
        var result = await Sut(makersContact: null).CreateAsync(new RecordCreationRequest
        {
            EntityType = QuickCreateEntityType.Matter, Name = "New matter", CallerUserId = "oid", OwnerSystemUserId = Maker.ToString("D"),
        });

        result.Succeeded.Should().BeTrue();
        _created.Single().Contains("sprk_assignedtointernal").Should().BeFalse("never an email match, never a team");
    }
}
