using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.CreatorPerson;

/// <summary>
/// unified-access-control-r2 task 133 b2 — owner round 7 item 2, option (a): every BFF APP-ONLY create of a project,
/// matter or work assignment records the PERSON who asked for it in <c>sprk_createdbyperson</c>, because its
/// <c>createdby</c> is the BFF application user.
/// </summary>
/// <remarks>
/// <para><b>The failure mode.</b> Secure provisioning's resume shares a stranded secure record to the person who created
/// it. For an app-created row there was nobody to share to (createdonbehalfby is empty, live 2026-10-01), so the resume
/// could only refuse. A writer that forgot the stamp would pass every create test and silently bring that back.</para>
/// <para>The chat create (<c>dataverse.create_record</c>, created by the app since task 146; its stamp rides that create
/// per owner round 10) is pinned in <c>SecureChildOwnershipAiToolTests</c>, and its refusal of an item naming the column
/// in <c>DataverseCreateRecordHandlerTests</c>;
/// the column's schema (name, target, tables, field security) against these constants in
/// <see cref="RecordCreatorPersonSchemaAgreementTests"/>.</para>
/// </remarks>
[Trait("status", "task-133-uac-r2")]
public class RecordCreatorPersonStampTests
{
    private static readonly Guid Caller = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000c1");
    private static readonly Guid Team = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid SourceRecord = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid SomeoneElse = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");

    private readonly Mock<IGenericEntityService> _entities = new(MockBehavior.Loose);
    private readonly Mock<IFieldMappingDataverseService> _fieldMappings = new();
    private readonly List<Entity> _created = new();

    public RecordCreatorPersonStampTests()
    {
        _entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => _created.Add(e))
            .ReturnsAsync(Guid.NewGuid());
    }

    private static EntityReference PersonOf(Entity row) =>
        row.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column);

    // =====================================================================================
    // Office quick-create — RecordCreationService (matter, project)
    // =====================================================================================

    private RecordCreationService OfficeCreator()
    {
        var ownership = new Mock<IRecordOwnershipResolver>();
        ownership.Setup(o => o.ResolveOwningTeamAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Team);
        return new RecordCreationService(
            _entities.Object, _fieldMappings.Object, ownership.Object,
            IdentityNormalizationFixtures.NoLinkedContact(),
            Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(),
            Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.InertMaterializer(),
            NullLogger<RecordCreationService>.Instance);
    }

    [Theory]
    [InlineData(QuickCreateEntityType.Matter)]
    [InlineData(QuickCreateEntityType.Project)]
    public async Task OfficeQuickCreate_RecordsTheCallerAsTheCreatorPerson(QuickCreateEntityType type)
    {
        var result = await OfficeCreator().CreateAsync(new RecordCreationRequest
        {
            EntityType = type,
            Name = "New record",
            CallerUserId = "oid",
            OwnerSystemUserId = Caller.ToString("D"),
        });

        result.Succeeded.Should().BeTrue();
        PersonOf(_created.Single()).Should().BeEquivalentTo(new EntityReference("systemuser", Caller),
            "the create is app-only, so createdby is the BFF application user — this column is the person");
        _created.Single()["ownerid"].Should().BeEquivalentTo(new EntityReference("team", Team),
            "access still comes from the team; the column decides only who a resume shares to");
    }

    /// <summary>
    /// A field-mapping Copy rule targeting the column cannot name the SOURCE record's creator as this one's: the column is
    /// protected from mapping like the owner, and the caller is what lands.
    /// </summary>
    [Theory]
    [InlineData(QuickCreateEntityType.Matter, "sprk_project", "sprk_matter")]
    [InlineData(QuickCreateEntityType.Project, "sprk_matter", "sprk_project")]
    public async Task OfficeQuickCreate_AMappingRuleCannotChooseTheCreatorPerson(
        QuickCreateEntityType type, string sourceEntity, string targetEntity)
    {
        _fieldMappings
            .Setup(m => m.GetFieldMappingProfileWithRulesAsync(sourceEntity, targetEntity, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldMappingProfileEntity
            {
                Id = Guid.NewGuid(),
                Name = "copy creator",
                SourceEntity = sourceEntity,
                TargetEntity = targetEntity,
                IsActive = true,
                Rules =
                [
                    new FieldMappingRuleEntity
                    {
                        Id = Guid.NewGuid(), Name = "copy-creator-person",
                        SourceField = RecordCreatorPerson.Column, SourceFieldType = 1,
                        TargetField = RecordCreatorPerson.Column, TargetFieldType = 1,
                        MappingType = 0, ExecutionOrder = 1, IsActive = true,
                    },
                ],
            });
        _entities
            .Setup(e => e.RetrieveAsync(sourceEntity, SourceRecord, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(sourceEntity, SourceRecord)
            {
                [RecordCreatorPerson.Column] = new EntityReference("systemuser", SomeoneElse),
            });

        var result = await OfficeCreator().CreateAsync(new RecordCreationRequest
        {
            EntityType = type,
            Name = "New record",
            CallerUserId = "oid",
            OwnerSystemUserId = Caller.ToString("D"),
            SourceEntityLogicalName = sourceEntity,
            SourceRecordId = SourceRecord,
        });

        result.Succeeded.Should().BeTrue();
        PersonOf(_created.Single()).Id.Should().Be(Caller, "the source record's creator is not this record's creator");
    }

    // POST /api/v1/work-assignments (WorkAssignmentEndpoints) no longer exists: task 166 deleted the route as caller-less
    // (sweep finding S-76, owner round 10 item 1; absence pinned by DeadRouteRetirementTests), so the work-assignment
    // stamp tests that drove its handler went with it. Work assignments are created through the MDA and the Create Work
    // Assignment wizard, which run as the user (createdby is the person).
}
