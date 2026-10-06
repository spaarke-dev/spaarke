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
            Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(),
            Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.InertMaterializer(),
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

    /// <summary>
    /// Task 100 (owner decision B): Assigned To is on the pane's form. A contact the REQUEST names is the user's
    /// explicit choice, so it wins over a field-mapping rule and over the maker default — and the maker's link is not
    /// even looked up. (The contact's Read right is authorized upstream, by QuickCreateSourceAccessFilter.)
    /// </summary>
    [Theory]
    [InlineData(QuickCreateEntityType.Matter, "sprk_project", "sprk_matter")]
    [InlineData(QuickCreateEntityType.Project, "sprk_matter", "sprk_project")]
    public async Task QuickCreate_ARequestedAssignee_WinsOverAMappedValueAndTheMaker(
        QuickCreateEntityType type, string sourceEntity, string targetEntity)
    {
        var requested = Guid.Parse("ffffffff-0000-0000-0000-00000000010f");
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
            AssignedToContactId = requested,
        });

        result.Succeeded.Should().BeTrue();
        _created.Single()["sprk_assignedtointernal"].Should().BeEquivalentTo(new EntityReference("contact", requested));
        identity.Verify(i => i.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Task 100: an EMPTY requested contact is a cleared field — the existing default applies (the maker).</summary>
    [Fact]
    public async Task QuickCreate_AnEmptyRequestedAssignee_FallsBackToTheMaker()
    {
        var result = await Sut(MakersContact).CreateAsync(new RecordCreationRequest
        {
            EntityType = QuickCreateEntityType.Matter, Name = "Cleared", CallerUserId = "oid",
            OwnerSystemUserId = Maker.ToString("D"), AssignedToContactId = Guid.Empty,
        });

        result.Succeeded.Should().BeTrue();
        _created.Single()["sprk_assignedtointernal"].Should().BeEquivalentTo(new EntityReference("contact", MakersContact));
    }

    /// <summary>
    /// Task 100: the pane's prefill and the server's default are ONE answer — <c>ResolveDefaultAssigneeAsync</c>
    /// names exactly the contact the create assigns, and returns no prefill when the maker has no link.
    /// </summary>
    [Fact]
    public async Task ResolveDefaultAssignee_IsTheContactTheCreateAssigns_AndNoneWithoutALink()
    {
        _entities
            .Setup(e => e.RetrieveAsync("contact", MakersContact, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("contact", MakersContact) { ["fullname"] = "Maker Person" });

        var prefill = await Sut(MakersContact).ResolveDefaultAssigneeAsync(Maker, CancellationToken.None);
        await Sut(MakersContact).CreateAsync(new RecordCreationRequest
        {
            EntityType = QuickCreateEntityType.Project, Name = "Uses default", CallerUserId = "oid", OwnerSystemUserId = Maker.ToString("D"),
        });

        prefill.Should().NotBeNull();
        prefill!.Id.Should().Be(((EntityReference)_created.Single()["sprk_assignedtointernal"]).Id);
        prefill.Name.Should().Be("Maker Person");
        prefill.Email.Should().BeNull();
        (await Sut(makersContact: null).ResolveDefaultAssigneeAsync(Maker, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ResolveDefaultAssignee_WhenTheContactCannotBeRead_GivesNoPrefill_NeverThrows()
    {
        _entities
            .Setup(e => e.RetrieveAsync("contact", MakersContact, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("dataverse slow"));

        (await Sut(MakersContact).ResolveDefaultAssigneeAsync(Maker, CancellationToken.None)).Should().BeNull();
    }
}
