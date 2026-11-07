using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 052a, adversarial review F1 / K4: Repair writes through the SDK, which refuses the
/// WHOLE update when one value is not the exact CLR type its column takes, is out of the column's range, points a lookup at a
/// table the column does not take, or names an option the column does not have — and with it the row's owner repair, on every
/// pass. Each such value is now converted to its column's type or skipped on its own, with its own reason; the rest of the row
/// is written. <see cref="RepairWorld"/>'s update refuses a mistyped value as the SDK does (review F3).
/// </summary>
[Trait("status", "new")]
public sealed class RecordRepairColumnValueTests
{
    private const string Amount = "sprk_amount";
    private const string Status = "sprk_eventstatus";
    private const string Areas = "sprk_areas";
    private const string Client = "sprk_clientorganization";
    private static readonly Guid ClientOrganization = Guid.Parse("a0520000-0000-4000-8000-0000000000b1");

    private readonly RepairWorld _world = new();

    // =====================================================================================
    // F1: a number is written as its column's CLR type
    // =====================================================================================

    public static TheoryData<string, object> WholeNumberDefaults() => new()
    {
        { "Decimal", 1500m },
        { "BigInt", 1500L },
        { "Double", 1500d },
        { "Integer", 1500 }, // must-not-fire: already the column's type
    };

    [Theory]
    [MemberData(nameof(WholeNumberDefaults))]
    public async Task AWholeNumberDefault_IsWrittenAsItsColumnsType(string columnType, object expected)
    {
        await RepairWithAmount(NumberDefault("1500"), columnType);

        Written(Amount).Should().BeOfType(expected.GetType()).And.Be(expected);
    }

    [Fact]
    public async Task AFractionalDefault_IntoADoubleColumn_IsWrittenAsADouble()
    {
        await RepairWithAmount(NumberDefault("1500.5"), "Double");

        Written(Amount).Should().BeOfType<double>().And.Be(1500.5d);
    }

    public static TheoryData<object, decimal> SourceNumbers() => new()
    {
        { new Money(2500.50m), 2500.50m },
        { 7, 7m },
    };

    [Theory]
    [MemberData(nameof(SourceNumbers))]
    public async Task AMoneyOrWholeNumberCopiedIntoADecimalColumn_IsWrittenAsADecimal(object source, decimal expected)
    {
        _world.Directory.Row("sprk_matter", RepairWorld.Matter)["sprk_budget"] = source;

        await RepairWithAmount(NumberCopy("sprk_budget"), "Decimal");

        Written(Amount).Should().BeOfType<decimal>().And.Be(expected);
    }

    // =====================================================================================
    // K4: a number no decimal can hold skips its one column, not the whole mapping
    // =====================================================================================

    [Theory]
    [InlineData("Money")]
    [InlineData("Decimal")]
    public async Task ADoubleNoDecimalCanHold_SkipsItsColumn_TheRestIsWritten(string columnType)
    {
        _world.Directory.Row("sprk_matter", RepairWorld.Matter)["sprk_budget"] = (double)decimal.MaxValue; // rounds up, past it

        var outcome = await RepairWithAmount(NumberCopy("sprk_budget"), columnType);

        TheRestIsWritten(outcome, Amount, RecordRepairReason.MappedValueTypeMismatch);
    }

    // =====================================================================================
    // F1: a number out of its column's range
    // =====================================================================================

    [Theory]
    [InlineData("Integer", 1499, false)]
    [InlineData("Integer", 1500, true)] // must-not-fire: at the bound
    [InlineData("Money", 1499, false)]
    [InlineData("Money", 1500, true)]   // must-not-fire
    [InlineData("Decimal", null, false)] // no range stated: fail closed
    public async Task ANumberIsWrittenOnlyWithinItsColumnsRange(string columnType, int? max, bool written)
    {
        _world.Map("sprk_matter", "sprk_event", NumberDefault("1500"), ClientTemplate());
        _world.ColumnOverrides[("sprk_event", Amount)] = RepairWorld.Typed(Amount, columnType) with { MaxValue = max };
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: "Tax", priority: "Urgent");

        var outcome = await _world.RepairAsync("sprk_event", id);

        if (written)
            Field(outcome, Amount).State.Should().Be(RecordRepairFieldState.Repaired);
        else
            TheRestIsWritten(outcome, Amount, RecordRepairReason.MappedValueOutOfRange);
    }

    // =====================================================================================
    // F1: a lookup to a table its column does not take
    // =====================================================================================

    [Theory]
    [InlineData("account", false)]
    [InlineData("sprk_organization", true)] // must-not-fire
    [InlineData(null, false)]               // no targets stated: fail closed
    public async Task AMappedLookupIsWrittenOnlyToATableItsColumnTakes(string? target, bool written)
    {
        _world.Directory.Row("sprk_matter", RepairWorld.Matter)[Client] = new EntityReference("sprk_organization", ClientOrganization);
        _world.Map("sprk_matter", "sprk_event", RepairWorld.CopyLookup(Client, Client, 1), ClientTemplate());
        _world.ColumnOverrides[("sprk_event", Client)] =
            RepairWorld.Typed(Client, "Lookup") with { Targets = target is null ? null : [target] };
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: "Tax", priority: "Urgent");

        var outcome = await _world.RepairAsync("sprk_event", id);

        if (written)
            Written(Client).Should().Be(new EntityReference("sprk_organization", ClientOrganization));
        else
            TheRestIsWritten(outcome, Client, RecordRepairReason.MappedLookupTableNotAccepted);
    }

    // =====================================================================================
    // F1: an option its column does not have
    // =====================================================================================

    [Theory]
    [InlineData(new[] { 100000000, 100000001 }, true)] // must-not-fire
    [InlineData(new[] { 100000000 }, false)]
    [InlineData(null, false)]                          // no choices stated: fail closed
    public async Task AMappedOptionIsWrittenOnlyWhenItsColumnHasIt(int[]? choices, bool written)
    {
        _world.Map("sprk_matter", "sprk_event", new FieldMappingRuleEntity
        {
            Id = Guid.NewGuid(), Name = "status", SourceField = string.Empty, TargetField = Status,
            TargetFieldType = CreateTimeFieldMapping.FieldTypeOptionSet, MappingType = CreateTimeFieldMapping.MappingTypeDefault,
            DefaultValue = "100000001", ExecutionOrder = 1, IsActive = true,
        }, ClientTemplate());
        _world.ColumnOverrides[("sprk_event", Status)] = RepairWorld.Typed(Status, "Picklist") with { OptionValues = choices };
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: "Tax", priority: "Urgent");

        var outcome = await _world.RepairAsync("sprk_event", id);

        if (written)
            Written(Status).Should().Be(new OptionSetValue(100000001));
        else
            TheRestIsWritten(outcome, Status, RecordRepairReason.MappedOptionNotAccepted);
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3 }, true)] // must-not-fire
    [InlineData(new[] { 1 }, false)]      // one of the two values is not a choice
    public async Task AMappedMultiSelectIsWrittenOnlyWhenItsColumnHasEveryValue(int[] choices, bool written)
    {
        _world.Directory.Row("sprk_matter", RepairWorld.Matter)[Areas] = new OptionSetValueCollection([new(1), new(2)]);
        _world.Map("sprk_matter", "sprk_event", new FieldMappingRuleEntity
        {
            Id = Guid.NewGuid(), Name = "areas", SourceField = Areas, TargetField = Areas,
            TargetFieldType = CreateTimeFieldMapping.FieldTypeOptionSet, MappingType = CreateTimeFieldMapping.MappingTypeCopy,
            ExecutionOrder = 1, IsActive = true,
        }, ClientTemplate());
        _world.ColumnOverrides[("sprk_event", Areas)] = RepairWorld.Typed(Areas, "Virtual") with { OptionValues = choices };
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: "Tax", priority: "Urgent");

        var outcome = await _world.RepairAsync("sprk_event", id);

        if (written)
            Written(Areas).Should().BeOfType<OptionSetValueCollection>().Which.Select(o => o.Value).Should().Equal(1, 2);
        else
            TheRestIsWritten(outcome, Areas, RecordRepairReason.MappedOptionNotAccepted);
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<RecordRepairOutcome> RepairWithAmount(FieldMappingRuleEntity rule, string columnType)
    {
        _world.Map("sprk_matter", "sprk_event", rule, ClientTemplate());
        _world.ColumnOverrides[("sprk_event", Amount)] = RepairWorld.Typed(Amount, columnType);
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: "Tax", priority: "Urgent");
        return await _world.RepairAsync("sprk_event", id);
    }

    /// <summary>The one write, applied (the world refuses a mistyped value as the SDK does), and its value for <paramref name="column"/>.</summary>
    private object Written(string column)
    {
        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Applied.Should().BeTrue("the world applies only a write whose every value is its column's type");
        return write.Fields[column];
    }

    /// <summary><paramref name="column"/> was skipped for <paramref name="reason"/>; the other mapped column and the owner were written.</summary>
    private void TheRestIsWritten(RecordRepairOutcome outcome, string column, string reason)
    {
        outcome.Status.Should().Be(RecordRepairStatus.Repaired);
        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Applied.Should().BeTrue();
        write.Fields.Should().NotContainKey(column);
        write.Fields["sprk_clientname"].Should().Be(RepairWorld.MatterClient);
        write.Fields.Should().ContainKey("ownerid");
        Field(outcome, column).ReasonCode.Should().Be(reason);
    }

    private static FieldMappingRuleEntity NumberDefault(string value) => new()
    {
        Id = Guid.NewGuid(), Name = "amount", SourceField = string.Empty, TargetField = Amount,
        TargetFieldType = CreateTimeFieldMapping.FieldTypeNumber, MappingType = CreateTimeFieldMapping.MappingTypeDefault,
        DefaultValue = value, ExecutionOrder = 1, IsActive = true,
    };

    private static FieldMappingRuleEntity NumberCopy(string source) => new()
    {
        Id = Guid.NewGuid(), Name = "amount", SourceField = source, TargetField = Amount,
        TargetFieldType = CreateTimeFieldMapping.FieldTypeNumber, MappingType = CreateTimeFieldMapping.MappingTypeCopy,
        ExecutionOrder = 1, IsActive = true,
    };

    private static FieldMappingRuleEntity ClientTemplate() =>
        RepairWorld.Expression(CreateTimeFieldMapping.MappingTypeTemplate, "{sprk_clientname}", "sprk_clientname", 2);

    private static RecordRepairField Field(RecordRepairOutcome outcome, string column) =>
        outcome.Fields.Should().ContainSingle(f => f.Part == RecordRepairPart.Mapping && f.Column == column).Subject;
}
