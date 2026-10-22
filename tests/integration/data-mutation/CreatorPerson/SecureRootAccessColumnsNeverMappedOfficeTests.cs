using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.CreatorPerson;

/// <summary>
/// #2220 (field-mapping-server-write-path-r1 DI-005) — the Office quick-create (the LIVE matter and project create) never
/// lets a field-mapping rule write a secure root's flag or access columns: <c>sprk_issecure</c>,
/// <c>sprk_accesspermission</c>, <c>sprk_accessinheritance</c> and <c>sprk_securitybu</c> are the BFF's own, changed only
/// through the secure endpoints and the Secure Record setup. A rule targeting one is skipped with the one protected-column
/// warning, and the column is not written. Driven through the real <see cref="OfficeRecordCreationService"/> over the
/// same doubles as <c>RecordCreatorPersonStampTests</c>.
/// </summary>
[Trait("status", "new")]
public class SecureRootAccessColumnsNeverMappedOfficeTests
{
    private static readonly Guid Caller = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000c1");
    private static readonly Guid Team = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid SourceRecord = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid SourceBu = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");

    private readonly Mock<IGenericEntityService> _entities = new(MockBehavior.Loose);
    private readonly Mock<IFieldMappingDataverseService> _fieldMappings = new();
    private readonly List<Entity> _created = [];

    public SecureRootAccessColumnsNeverMappedOfficeTests()
    {
        _entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => _created.Add(e))
            .ReturnsAsync(Guid.NewGuid());
        _entities.AnswersRecordTypeCatalog();
    }

    /// <summary>The four columns a source could carry, with a value of the type each takes and the rule's field type.</summary>
    private static readonly (string Column, object Value, int FieldType)[] AccessColumns =
    [
        (SecurableEntityRegistry.SecureFlagAttribute, true, 5),
        (InheritedAccessPermission.Column, new OptionSetValue(InheritedAccessPermission.Restricted), 2),
        (AccessInheritance.Column, "{\"parents\":[]}", 6),
        (SecurableEntityRegistry.SecurityBusinessUnitAttribute, new EntityReference("businessunit", SourceBu), 1),
    ];

    public static TheoryData<QuickCreateEntityType, string> EveryRootAndAccessColumn()
    {
        var data = new TheoryData<QuickCreateEntityType, string>();
        foreach (var type in new[] { QuickCreateEntityType.Matter, QuickCreateEntityType.Project })
            foreach (var (column, _, _) in AccessColumns)
                data.Add(type, column);
        return data;
    }

    // ── must fire ───────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(EveryRootAndAccessColumn))]
    public async Task OfficeQuickCreate_AMappingRuleTargetingASecureRootAccessColumn_IsSkippedWithTheWarning_AndNothingIsWritten(
        QuickCreateEntityType type, string column)
    {
        var (sourceEntity, targetEntity) = Pair(type);
        var (_, value, fieldType) = AccessColumns.Single(c => c.Column == column);
        ArrangeProfile(sourceEntity, targetEntity, Rule(column, column, fieldType));
        ArrangeSource(sourceEntity, new Entity(sourceEntity, SourceRecord) { [column] = value });

        var result = await Create(type, sourceEntity);

        result.Succeeded.Should().BeTrue(result.Failure?.Detail);
        _created.Single().Contains(column).Should().BeFalse($"{column} is the BFF's own; a mapped value never lands");
        result.Warnings.Should().ContainSingle(w =>
            w.Contains($"\"{column}\"") && w.Contains("was skipped") && w.Contains("managed by the server"));
    }

    // ── must not fire ───────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(QuickCreateEntityType.Matter)]
    [InlineData(QuickCreateEntityType.Project)]
    public async Task OfficeQuickCreate_AnOrdinaryColumnRule_IsStillMapped_BesideASkippedAccessColumn(QuickCreateEntityType type)
    {
        var (sourceEntity, targetEntity) = Pair(type);
        ArrangeProfile(sourceEntity, targetEntity,
            Rule("sprk_description", "sprk_description", 6),
            Rule(SecurableEntityRegistry.SecureFlagAttribute, SecurableEntityRegistry.SecureFlagAttribute, 5));
        ArrangeSource(sourceEntity, new Entity(sourceEntity, SourceRecord)
        {
            ["sprk_description"] = "From the source",
            [SecurableEntityRegistry.SecureFlagAttribute] = true,
        });

        var result = await Create(type, sourceEntity);

        result.Succeeded.Should().BeTrue(result.Failure?.Detail);
        _created.Single().GetAttributeValue<string>("sprk_description").Should().Be("From the source");
        _created.Single().Contains(SecurableEntityRegistry.SecureFlagAttribute).Should().BeFalse();
        result.Warnings.Should().ContainSingle(w => w.Contains("was skipped"));
    }

    // ── harness ─────────────────────────────────────────────────────────────────────────────────────────────────

    private static (string Source, string Target) Pair(QuickCreateEntityType type) =>
        type == QuickCreateEntityType.Matter ? ("sprk_project", "sprk_matter") : ("sprk_matter", "sprk_project");

    private static FieldMappingRuleEntity Rule(string sourceField, string targetField, int targetFieldType) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"copy-{targetField}",
        SourceField = sourceField,
        SourceFieldType = targetFieldType,
        TargetField = targetField,
        TargetFieldType = targetFieldType,
        MappingType = 0,
        ExecutionOrder = 1,
        IsActive = true,
    };

    private void ArrangeProfile(string source, string target, params FieldMappingRuleEntity[] rules) =>
        _fieldMappings
            .Setup(m => m.GetFieldMappingProfileWithRulesAsync(source, target, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldMappingProfileEntity
            {
                Id = Guid.NewGuid(),
                Name = "copy access columns",
                SourceEntity = source,
                TargetEntity = target,
                IsActive = true,
                Rules = [.. rules],
            });

    private void ArrangeSource(string sourceEntity, Entity row) =>
        _entities
            .Setup(e => e.RetrieveAsync(sourceEntity, SourceRecord, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);

    private Task<RecordCreationResult> Create(QuickCreateEntityType type, string sourceEntity)
    {
        var ownership = new Mock<IRecordOwnershipResolver>();
        ownership.Setup(o => o.ResolveOwningTeamAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Team);
        var caller = OfficeCallerRightsSetup.Granting();
        var creator = new OfficeRecordCreationService(
            _entities.Object, _fieldMappings.Object, ownership.Object,
            IdentityNormalizationFixtures.NoLinkedContact(),
            SecureRootFilingGateFixtures.NothingSecure(),
            Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.InertMaterializer(),
            NullLogger<OfficeRecordCreationService>.Instance,
            caller.Probe, caller.Http, MappingSourceCallerReadDouble.MirroringAppReads(_entities));

        return creator.CreateAsync(new RecordCreationRequest
        {
            EntityType = type,
            Name = "New record",
            CallerUserId = "oid",
            OwnerSystemUserId = Caller.ToString("D"),
            SourceEntityLogicalName = sourceEntity,
            SourceRecordId = SourceRecord,
        });
    }
}
