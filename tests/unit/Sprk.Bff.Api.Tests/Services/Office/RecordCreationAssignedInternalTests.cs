// unified-access-control-r2 task 152 — owner decision A7 (round 3, REVERSED; binding amendment R3 on task 152):
// an Office quick-created matter/project names its MAKER in sprk_assignedtointernal (editable). The create is app-only,
// so Created By cannot make the record "for" the maker in the Daily Briefing; this column does (ADR-034 A3).

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai.Membership;
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
    private static readonly Guid MappedContact = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");
    private static readonly Guid Team = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid SourceRecord = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    private readonly Mock<IGenericEntityService> _entities = new(MockBehavior.Loose);
    private readonly Mock<IFieldMappingDataverseService> _fieldMappings = new();
    private readonly List<Entity> _created = new();

    public RecordCreationAssignedInternalTests()
    {
        _entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => _created.Add(e))
            .ReturnsAsync(Guid.NewGuid());
    }

    private RecordCreationService Sut(Mock<IIdentityNormalizationService> identity)
    {
        var ownership = new Mock<IRecordOwnershipResolver>();
        ownership.Setup(o => o.ResolveOwningTeamAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Team);
        return new RecordCreationService(
            _entities.Object,
            _fieldMappings.Object,
            ownership.Object,
            identity.Object,
            NullLogger<RecordCreationService>.Instance);
    }

    private RecordCreationService Sut(Guid? makersContact) => Sut(IdentityNormalizationFixtures.WithContact(makersContact));

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

    /// <summary>
    /// A7 / round-3 amendment R3 ("a supplied value is never overwritten"): a field-mapping rule that already wrote
    /// <c>sprk_assignedtointernal</c> keeps its value — the maker does NOT replace it, and the maker's contact is not
    /// even looked up.
    /// </summary>
    [Theory]
    [InlineData(QuickCreateEntityType.Matter, "sprk_project", "sprk_matter")]
    [InlineData(QuickCreateEntityType.Project, "sprk_matter", "sprk_project")]
    public async Task QuickCreate_AFieldMappedAssignedToInternal_IsKept_NotOverwrittenByTheMaker(
        QuickCreateEntityType type, string sourceEntity, string targetEntity)
    {
        _fieldMappings
            .Setup(m => m.GetFieldMappingProfileWithRulesAsync(sourceEntity, targetEntity, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldMappingProfileEntity
            {
                Id = Guid.NewGuid(),
                Name = "Source to target",
                SourceEntity = sourceEntity,
                TargetEntity = targetEntity,
                IsActive = true,
                Rules =
                [
                    new FieldMappingRuleEntity
                    {
                        Id = Guid.NewGuid(),
                        Name = "copy-assigned-internal",
                        SourceField = "sprk_assignedtointernal",
                        SourceFieldType = 1,
                        TargetField = "sprk_assignedtointernal",
                        TargetFieldType = 1,
                        MappingType = 0,
                        ExecutionOrder = 1,
                        IsActive = true,
                    },
                ],
            });
        _entities
            .Setup(e => e.RetrieveAsync(sourceEntity, SourceRecord, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(sourceEntity, SourceRecord)
            {
                ["sprk_assignedtointernal"] = new EntityReference("contact", MappedContact),
            });
        var identity = IdentityNormalizationFixtures.WithContact(MakersContact);

        var result = await Sut(identity).CreateAsync(new RecordCreationRequest
        {
            EntityType = type, Name = "New record", CallerUserId = "oid", OwnerSystemUserId = Maker.ToString("D"),
            SourceEntityLogicalName = sourceEntity, SourceRecordId = SourceRecord,
        });

        result.Succeeded.Should().BeTrue();
        _created.Single()["sprk_assignedtointernal"].Should().BeEquivalentTo(
            new EntityReference("contact", MappedContact), "a value a field-mapping rule wrote is never overwritten");
        identity.Verify(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
