using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 052a — Repair mode reads column metadata app-only
/// (<see cref="IGenericEntityService.GetColumnMetadataAsync"/>), as the system principal has no caller to read it as:
/// <list type="bullet">
/// <item>the mapping SOURCE: a field-secured column is neither read for mapping nor copied (owner decision 2026-10-21, option 1;
/// UAC-r2 conditions 1 and 4), and metadata that cannot be read, or does not say, maps nothing (fail closed);</item>
/// <item>the row's own TARGET table: a mapped column that does not exist or that an update may not set, or text longer than its
/// column, is not written (one mis-typed rule no longer fails the row's whole update), and a Default into a Money column is
/// written as <see cref="Money"/> (#2124).</item>
/// </list>
/// </summary>
/// <remarks>The same doubled boundary as <see cref="RecordRepairModeTests"/> (<see cref="RepairWorld"/>). Each must-not-fire
/// control also asserts the metadata was read, so it proves the check ran and let the value through.</remarks>
[Trait("status", "new")]
public sealed class RecordRepairColumnMetadataTests
{
    private const string FeeColumn = "sprk_estimatedfee";
    private const string FlagTarget = "sprk_isurgent";
    private const string ClientOrganizationColumn = "sprk_clientorganization";
    private static readonly Guid ClientOrganization = Guid.Parse("a0520000-0000-4000-8000-0000000000a1");

    private readonly RepairWorld _world = new();

    // =====================================================================================
    // The source: field security (UAC-r2 condition 4, must-fire and must-not-fire)
    // =====================================================================================

    [Fact]
    public async Task ASecuredSourceColumn_IsNeitherReadNorCopied_TheOtherColumnsAre()
    {
        _world.ColumnOverrides[("sprk_matter", "sprk_clientname")] = Secured("sprk_clientname");
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null);

        var outcome = await _world.RepairAsync("sprk_event", id);

        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Fields.Should().NotContainKey("sprk_clientname");
        write.Fields.Values.OfType<string>().Should().NotContain(RepairWorld.MatterClient);
        write.Fields["sprk_practicearea"].Should().Be(RepairWorld.MatterPracticeArea);
        write.Fields["sprk_priorityname"].Should().Be(RepairWorld.DefaultPriority);
        _world.SourceReads.Should().ContainSingle().Which.Columns.Should().NotContain("sprk_clientname");
        Field(outcome, "sprk_clientname").ReasonCode.Should().Be(RecordRepairReason.MappingNoValue);
    }

    [Fact]
    public async Task ASecuredColumnTheSourceGateReads_IsNotCopied_EvenWhenARuleNamesIt()
    {
        // The matter's secure flag is read to decide whether the matter may be copied from at all (review F1-e (ii)); a rule
        // naming it must not turn that read into a copy.
        _world.Map("sprk_matter", "sprk_event",
            RepairWorld.CopyBoolean("sprk_issecure", FlagTarget, 1),
            RepairWorld.Expression(CreateTimeFieldMapping.MappingTypeTemplate, "{sprk_clientname}", "sprk_clientname", 2));
        _world.ColumnOverrides[("sprk_matter", "sprk_issecure")] = Secured("sprk_issecure");
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: "Tax", priority: "Urgent");

        var outcome = await _world.RepairAsync("sprk_event", id);

        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Fields.Should().NotContainKey(FlagTarget);
        write.Fields["sprk_clientname"].Should().Be(RepairWorld.MatterClient, "an unsecured column of the same source is still copied");
        Field(outcome, FlagTarget).ReasonCode.Should().Be(RecordRepairReason.MappingNoValue);
    }

    [Fact]
    public async Task AColumnSecuredOnlyOnAnotherTable_IsCopied_MustNotFire()
    {
        // Field security is per table: the same column secured on a project, or on the event being repaired, does not stop
        // the matter's (unsecured) value being copied.
        _world.ColumnOverrides[("sprk_project", "sprk_clientname")] = Secured("sprk_clientname");
        _world.ColumnOverrides[("sprk_event", "sprk_clientname")] = Secured("sprk_clientname");
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null);

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().ContainSingle().Which.Fields["sprk_clientname"].Should().Be(RepairWorld.MatterClient);
        Field(outcome, "sprk_clientname").State.Should().Be(RecordRepairFieldState.Repaired);
        _world.ColumnMetadataReads.Should().Contain(["sprk_matter", "sprk_event"], "the source's and the row's metadata were read");
    }

    [Fact]
    public async Task UnsecuredColumns_AreCopiedOntoAToDo_MustNotFire()
    {
        _world.Map("sprk_matter", "sprk_todo", RepairWorld.Expression(
            CreateTimeFieldMapping.MappingTypeTemplate, "{sprk_clientname}", "sprk_description", 1));
        var id = _world.RowOf("sprk_todo", Directory.ChildBu,
            new Dictionary<string, object>
            {
                ["ownerid"] = new EntityReference("team", Directory.ChildTeam),
                ["owningteam"] = new EntityReference("team", Directory.ChildTeam),
                ["sprk_regardingmatter"] = new EntityReference("sprk_matter", RepairWorld.Matter),
            });

        var outcome = await _world.RepairAsync("sprk_todo", id);

        _world.Writes.Should().ContainSingle().Which.Fields["sprk_description"].Should().Be(RepairWorld.MatterClient);
        Field(outcome, "sprk_description").State.Should().Be(RecordRepairFieldState.Repaired);
        _world.ColumnMetadataReads.Should().Contain(["sprk_matter", "sprk_todo"]);
    }

    // =====================================================================================
    // Fail closed on metadata (UAC-r2 condition 1)
    // =====================================================================================

    [Fact]
    public async Task TheSourcesMetadataFaulting_MapsNothing_AndReadsNothingFromTheSource()
    {
        _world.ColumnMetadataFaults["sprk_matter"] = new InvalidOperationException("Test: metadata read faulted.");
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null);

        var outcome = await _world.RepairAsync("sprk_event", id);

        NothingWasMapped(outcome, RecordRepairReason.MappingSourceMetadataUnavailable);
        _world.SourceReads.Should().BeEmpty();
        _world.Writes.Should().ContainSingle().Which.Fields.Should().ContainKey("ownerid", "the owner is still repaired");
    }

    [Fact]
    public async Task AMappedSourceColumnWhoseFieldSecurityIsUnknown_MapsNothing_NotEvenADefault()
    {
        _world.ColumnOverrides[("sprk_matter", "sprk_practicearea")] =
            new ColumnMetadata("sprk_practicearea", "String", IsValidForUpdate: true, IsSecured: null, MaxLength: null);
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null);

        var outcome = await _world.RepairAsync("sprk_event", id);

        NothingWasMapped(outcome, RecordRepairReason.MappingSourceMetadataUnavailable);
        _world.SourceReads.Should().BeEmpty();
    }

    [Fact]
    public async Task NoColumnMetadataForTheSource_MapsNothing()
    {
        // An empty answer cannot be told from a failed read: no column is known not to be secured.
        foreach (var column in new[] { "sprk_clientname", "sprk_practicearea", "sprk_priorityname" })
            _world.ColumnOverrides[("sprk_matter", column)] = null;
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null);

        var outcome = await _world.RepairAsync("sprk_event", id);

        NothingWasMapped(outcome, RecordRepairReason.MappingSourceMetadataUnavailable);
    }

    [Fact]
    public async Task TheRowsOwnMetadataFaulting_MapsNothing_AndReadsNothingFromTheSource()
    {
        _world.ColumnMetadataFaults["sprk_event"] = new InvalidOperationException("Test: metadata read faulted.");
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null);

        var outcome = await _world.RepairAsync("sprk_event", id);

        NothingWasMapped(outcome, RecordRepairReason.MappingTargetMetadataUnavailable);
        _world.SourceReads.Should().BeEmpty();
    }

    // =====================================================================================
    // The target: update validity and length (K-1 of task 016)
    // =====================================================================================

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task AMappedColumnAnUpdateMayNotSet_IsNotWritten_TheOthersAre(bool? validForUpdate)
    {
        _world.ColumnOverrides[("sprk_event", "sprk_practicearea")] =
            new ColumnMetadata("sprk_practicearea", "String", validForUpdate, IsSecured: false, MaxLength: null);
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null);

        var outcome = await _world.RepairAsync("sprk_event", id);

        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Fields.Should().NotContainKey("sprk_practicearea");
        write.Fields["sprk_clientname"].Should().Be(RepairWorld.MatterClient);
        Field(outcome, "sprk_practicearea").ReasonCode.Should().Be(RecordRepairReason.MappedColumnNotUpdatable);
        outcome.Status.Should().Be(RecordRepairStatus.Repaired);
    }

    [Fact]
    public async Task ARuleNamingAColumnTheRowsTableDoesNotHave_IsNotWritten_TheRowIsStillRepaired()
    {
        _world.ColumnOverrides[("sprk_event", "sprk_practicearea")] = null;
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null);

        var outcome = await _world.RepairAsync("sprk_event", id);

        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Applied.Should().BeTrue();
        write.Fields.Should().NotContainKey("sprk_practicearea");
        write.Fields.Should().ContainKeys("sprk_clientname", "sprk_priorityname");
        Field(outcome, "sprk_practicearea").ReasonCode.Should().Be(RecordRepairReason.MappedColumnNotUpdatable);
    }

    [Theory]
    [InlineData(-1, false)] // one character too long: not written
    [InlineData(0, true)]   // exactly the column's length: written (must-not-fire)
    public async Task MappedTextIsWrittenOnlyWhenItFitsTheColumn(int slack, bool written)
    {
        _world.ColumnOverrides[("sprk_event", "sprk_practicearea")] = new ColumnMetadata(
            "sprk_practicearea", "String", IsValidForUpdate: true, IsSecured: false,
            MaxLength: RepairWorld.MatterPracticeArea.Length + slack);
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null);

        var outcome = await _world.RepairAsync("sprk_event", id);

        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Fields["sprk_clientname"].Should().Be(RepairWorld.MatterClient);
        if (written)
        {
            write.Fields["sprk_practicearea"].Should().Be(RepairWorld.MatterPracticeArea);
            Field(outcome, "sprk_practicearea").State.Should().Be(RecordRepairFieldState.Repaired);
            _world.ColumnMetadataReads.Should().Contain("sprk_event");
        }
        else
        {
            write.Fields.Should().NotContainKey("sprk_practicearea");
            Field(outcome, "sprk_practicearea").ReasonCode.Should().Be(RecordRepairReason.MappedValueTooLong);
        }
    }

    // =====================================================================================
    // The target: the value's type (the coordinator's 052a ruling: one rule whose value does not fit its column must not fail
    // the row's update on every pass)
    // =====================================================================================

    [Fact]
    public async Task TextMappedIntoAWholeNumberColumn_IsNotWritten_TheRestOfTheRowIs()
    {
        _world.ColumnOverrides[("sprk_event", "sprk_priorityname")] =
            RepairWorld.Typed("sprk_priorityname", "Integer");
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null);

        var outcome = await _world.RepairAsync("sprk_event", id);

        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Applied.Should().BeTrue();
        write.Fields.Should().NotContainKey("sprk_priorityname");
        write.Fields["sprk_clientname"].Should().Be(RepairWorld.MatterClient);
        write.Fields["sprk_practicearea"].Should().Be(RepairWorld.MatterPracticeArea);
        Field(outcome, "sprk_priorityname").ReasonCode.Should().Be(RecordRepairReason.MappedValueTypeMismatch);
    }

    [Theory]
    [InlineData("String", false)] // a lookup into a text column: not written
    [InlineData("Lookup", true)]  // a lookup into a lookup column: written (must-not-fire)
    public async Task AMappedLookupIsWrittenOnlyIntoALookupColumn(string columnType, bool written)
    {
        _world.Directory.Row("sprk_matter", RepairWorld.Matter)[ClientOrganizationColumn] =
            new EntityReference("sprk_organization", ClientOrganization);
        _world.Map("sprk_matter", "sprk_event",
            RepairWorld.CopyLookup(ClientOrganizationColumn, ClientOrganizationColumn, 1),
            RepairWorld.Expression(CreateTimeFieldMapping.MappingTypeTemplate, "{sprk_clientname}", "sprk_clientname", 2));
        _world.ColumnOverrides[("sprk_event", ClientOrganizationColumn)] =
            RepairWorld.Typed(ClientOrganizationColumn, columnType);
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: "Tax", priority: "Urgent");

        var outcome = await _world.RepairAsync("sprk_event", id);

        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Fields["sprk_clientname"].Should().Be(RepairWorld.MatterClient);
        if (written)
        {
            write.Fields[ClientOrganizationColumn].Should().Be(new EntityReference("sprk_organization", ClientOrganization));
            Field(outcome, ClientOrganizationColumn).State.Should().Be(RecordRepairFieldState.Repaired);
        }
        else
        {
            write.Fields.Should().NotContainKey(ClientOrganizationColumn);
            Field(outcome, ClientOrganizationColumn).ReasonCode.Should().Be(RecordRepairReason.MappedValueTypeMismatch);
        }
    }

    [Fact]
    public async Task ADecimalCopiedIntoAMoneyColumn_IsWrittenAsMoney_MustNotFire()
    {
        // The engine applies the Money columns to Default and expression rules only; a Copy keeps the source's value, and the
        // SDK refuses a decimal in a Money column. The type check lets it through, and Repair writes it as a Money.
        _world.Directory.Row("sprk_matter", RepairWorld.Matter)["sprk_budget"] = 2500.50m;
        _world.Map("sprk_matter", "sprk_event", new FieldMappingRuleEntity
        {
            Id = Guid.NewGuid(), Name = "budget", SourceField = "sprk_budget", TargetField = FeeColumn,
            TargetFieldType = CreateTimeFieldMapping.FieldTypeNumber, MappingType = CreateTimeFieldMapping.MappingTypeCopy,
            ExecutionOrder = 1, IsActive = true,
        });
        _world.ColumnOverrides[("sprk_event", FeeColumn)] =
            RepairWorld.Typed(FeeColumn, "Money");
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent");

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().ContainSingle().Which.Fields[FeeColumn]
            .Should().BeOfType<Money>().Which.Value.Should().Be(2500.50m);
        Field(outcome, FeeColumn).State.Should().Be(RecordRepairFieldState.Repaired);
    }

    // =====================================================================================
    // Money (#2124)
    // =====================================================================================

    [Fact]
    public async Task ADefaultIntoAMoneyColumn_IsWrittenAsMoney()
    {
        MapAFeeDefault();
        _world.ColumnOverrides[("sprk_event", FeeColumn)] =
            RepairWorld.Typed(FeeColumn, "Money");
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent");

        await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().ContainSingle().Which.Fields[FeeColumn]
            .Should().BeOfType<Money>().Which.Value.Should().Be(1500.25m);
    }

    [Fact]
    public async Task ADefaultIntoADecimalColumn_StaysADecimal_MustNotFire()
    {
        MapAFeeDefault();
        _world.ColumnOverrides[("sprk_event", FeeColumn)] =
            RepairWorld.Typed(FeeColumn, "Decimal");
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent");

        await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().ContainSingle().Which.Fields[FeeColumn].Should().Be(1500.25m);
        _world.ColumnMetadataReads.Should().Contain("sprk_event");
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────────────────────────

    private void MapAFeeDefault() =>
        _world.Map("sprk_matter", "sprk_event", new FieldMappingRuleEntity
        {
            Id = Guid.NewGuid(), Name = "fee", SourceField = string.Empty, TargetField = FeeColumn,
            TargetFieldType = CreateTimeFieldMapping.FieldTypeNumber, MappingType = CreateTimeFieldMapping.MappingTypeDefault,
            DefaultValue = "1500.25", ExecutionOrder = 1, IsActive = true,
        });

    private static ColumnMetadata Secured(string column) =>
        new(column, "String", IsValidForUpdate: true, IsSecured: true, MaxLength: null);

    private void NothingWasMapped(RecordRepairOutcome outcome, string reason)
    {
        _world.Writes.SelectMany(w => w.Fields.Keys)
            .Should().NotContain(["sprk_clientname", "sprk_practicearea", "sprk_priorityname"]);
        outcome.Fields.Where(f => f.Part == RecordRepairPart.Mapping).Should().ContainSingle()
            .Which.Should().Be(RecordRepairField.Skipped(RecordRepairPart.Mapping, null, reason));
    }

    private static RecordRepairField Field(RecordRepairOutcome outcome, string column) =>
        outcome.Fields.Should().ContainSingle(f => f.Part == RecordRepairPart.Mapping && f.Column == column).Subject;
}
