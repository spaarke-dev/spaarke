using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 014 — the pure parts of the unified create's mapping step
/// (<c>RecordCreationService.Mapping.cs</c>): a mapped SDK value as the create's item value, whether it fits the column it
/// targets, the rule copy that makes a target a logical name, and which lookups FILE a row. No I/O — hence
/// <c>tests/unit/domain/**</c> (ADR-038 §2 path 6). The pipeline itself is pinned in
/// <c>tests/integration/seam/RecordCreation/RecordCreationServiceMappingSeamTests.cs</c>.
/// </summary>
public class RecordCreationMappedValueTests
{
    // ── ToItemValue: the Web API form of each mapped value ────────────────────────────────────────────────────────────

    [Fact]
    public void ToItemValue_Money_IsItsAmount()
    {
        // DI-001 / #2124: a Money column's value reaches the Web API body as its amount (a JSON number), the form the
        // PATCH create takes for a currency column.
        Raw(new Money(1234.5m), null).Should().Be("1234.5");
    }

    [Fact]
    public void ToItemValue_AChoice_IsItsNumber_AndAMultiSelectChoice_ItsCommaSeparatedNumbers()
    {
        Raw(new OptionSetValue(100000002), null).Should().Be("100000002");
        Raw(new OptionSetValueCollection { new OptionSetValue(100000002), new OptionSetValue(100000004) }, null)
            .Should().Be("\"100000002,100000004\"", "DataverseWriteItemMapper's contract for a multi-select choice");
        RecordCreationService.ToItemValue(new OptionSetValueCollection(), null).Should().BeNull("an empty selection has no form");
    }

    [Fact]
    public void ToItemValue_ADate_IsWrittenAsTheColumnStoresIt()
    {
        var utc = new DateTime(2027, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        Raw(utc, "DateOnly").Should().Be("\"2027-01-02\"");
        Raw(utc, "UserLocal").Should().Be("\"2027-01-02T03:04:05Z\"");
        Raw(utc, "TimeZoneIndependent").Should().Be("\"2027-01-02T03:04:05Z\"");
        Raw(utc.ToLocalTime(), "UserLocal").Should().Be("\"2027-01-02T03:04:05Z\"", "a local time is converted, never relabelled");
    }

    [Fact]
    public void ToItemValue_ALookup_IsTheMappersObjectForm_WithTheTableLowerCased()
    {
        var id = Guid.Parse("a0140000-0000-4000-8000-0000000000c1");

        using var value = JsonDocument.Parse(Raw(new EntityReference(" Contact ", id), null)!);

        value.RootElement.GetProperty("relatedTable").GetString().Should().Be("contact");
        value.RootElement.GetProperty("recordId").GetString().Should().Be(id.ToString("D"));
        RecordCreationService.ToItemValue(new EntityReference("contact", Guid.Empty), null).Should().BeNull();
    }

    [Fact]
    public void ToItemValue_Scalars_PassThrough_AndAValueWithNoWebApiForm_IsNull()
    {
        Raw("text", null).Should().Be("\"text\"");
        Raw(true, null).Should().Be("true");
        Raw(42, null).Should().Be("42");
        Raw(42L, null).Should().Be("42");
        Raw(1.25m, null).Should().Be("1.25");
        Raw(0.5d, null).Should().Be("0.5");
        RecordCreationService.ToItemValue(double.NaN, null).Should().BeNull();
        RecordCreationService.ToItemValue(Guid.NewGuid(), null).Should().BeNull();
        RecordCreationService.ToItemValue(null, null).Should().BeNull();
    }

    // ── Fits: a mapped value against the target column's AttributeType ───────────────────────────────────────────────

    public static TheoryData<string, object?, bool> FitsCases() => new()
    {
        { "String", "x", true },
        { "Memo", "x", true },
        { "String", 1, false },
        { "Integer", 1, true },
        { "Integer", 1L, false },
        { "BigInt", 1L, true },
        { "Decimal", 1.5m, true },
        { "Money", new Money(1m), true },
        { "Money", "1", false },
        { "Boolean", true, true },
        { "Boolean", "true", false },
        { "Picklist", new OptionSetValue(1), true },
        { "Status", 1, false },
        { "Virtual", new OptionSetValueCollection(), true },
        { "DateTime", DateTime.UtcNow, true },
        { "Lookup", new EntityReference("contact", Guid.NewGuid()), true },
        { "Customer", new EntityReference("contact", Guid.NewGuid()), true },
        { "Lookup", "a0140000-0000-4000-8000-0000000000c1", false },
        { "Owner", new EntityReference("team", Guid.NewGuid()), false },
        { "Uniqueidentifier", Guid.NewGuid(), false },
    };

    [Theory]
    [MemberData(nameof(FitsCases))]
    public void Fits_AnswersByTheColumnsType(string attributeType, object? value, bool fits)
    {
        RecordCreationService.Fits(attributeType, value).Should().Be(fits);
    }

    // ── NormalizedRule: a copy, its target a logical name ─────────────────────────────────────────────────────────────

    [Fact]
    public void NormalizedRule_CopiesEveryProperty_TrimsAndLowerCasesTheTarget_AndLeavesTheProfilesRuleAlone()
    {
        var rule = new FieldMappingRuleEntity { Name = "n", SourceField = "s", TargetField = "t" };
        var properties = typeof(FieldMappingRuleEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite).ToArray();
        foreach (var property in properties)
            property.SetValue(rule, Distinct(property));
        rule.TargetField = "  SPRK_AssignedTo ";

        var copy = RecordCreationService.NormalizedRule(rule);

        copy.Should().NotBeSameAs(rule);
        copy.TargetField.Should().Be("sprk_assignedto");
        rule.TargetField.Should().Be("  SPRK_AssignedTo ", "the profile reader may cache its rules");
        foreach (var property in properties.Where(p => p.Name != nameof(FieldMappingRuleEntity.TargetField)))
            property.GetValue(copy).Should().Be(property.GetValue(rule), $"{property.Name} is carried over (a new property must be too)");
    }

    // ── IsFilingLookup: the FR-08 amendment's "mapped filing lookup" ──────────────────────────────────────────────────

    [Theory]
    [InlineData("sprk_event", "sprk_regardingmatter", true)] // a lineage lookup to an ownership parent
    [InlineData("sprk_event", "sprk_regardingproject", true)]
    [InlineData("sprk_communication", "sprk_communicationthread", true)] // lineage, not an ownership parent
    [InlineData("sprk_workassignment", "sprk_regardingmatter", true)] // a secure root's filing column
    [InlineData("sprk_project", "sprk_regardingrecordid", true)] // the pair files an inheriting root
    [InlineData("sprk_event", "sprk_assignedto", false)] // a person, not a filing
    [InlineData("sprk_matter", "sprk_assignedtointernal", false)]
    [InlineData("sprk_todo", "sprk_assignedto", false)]
    public void IsFilingLookup_LineageOwnershipParentAndSecureRootFilingColumns(string table, string column, bool filing)
    {
        RecordCreationService.IsFilingLookup(table, column).Should().Be(filing);
    }

    // Fix round 3 (UAC-r2 review, F-B): a lookup is filing when the lists say so OR the owner decision files the row under it.
    [Theory]
    [InlineData("sprk_workassignment", "sprk_regardingcommunication", "sprk_communication", true)] // no list names it (#2190)
    [InlineData("sprk_invoice", "sprk_regardingworkassignment", "sprk_workassignment", true)] // not in the invoice's lineage
    [InlineData("sprk_communication", "sprk_communicationthread", "sprk_communicationthread", true)] // lineage only: still filing
    [InlineData("sprk_signal", "sprk_policy", "sprk_policy", false)] // a citation files nothing (#2084)
    [InlineData("sprk_event", "sprk_assignedto", "contact", false)] // a person
    [InlineData("sprk_workassignment", "sprk_assignedto", "contact", false)]
    public void IsFilingLookup_OfALookup_IsTheListsOrTheOwnerDecisionsOwnTest(string table, string column, string target, bool filing)
    {
        var lookup = new DataverseWriteItemMapper.MappedLookup(column, target, target + "s", Guid.NewGuid());

        RecordCreationService.IsFilingLookup(table, lookup).Should().Be(filing);
        if (filing && !RecordCreationService.IsFilingLookup(table, column))
        {
            OwnedChildWrite.ParentsOf(table, new DataverseWriteItemMapper.MappedItem("{}", [column]) { Lookups = [lookup] })
                .Should().ContainSingle("the owner decision files the row under it");
        }
    }

    private static string? Raw(object? value, string? dateBehaviour) =>
        RecordCreationService.ToItemValue(value, dateBehaviour)?.GetRawText();

    private static object Distinct(PropertyInfo property) => property.PropertyType switch
    {
        var t when t == typeof(string) => $"value-of-{property.Name}",
        var t when t == typeof(Guid) => Guid.NewGuid(),
        var t when t == typeof(int) => 7 + property.Name.Length,
        var t when t == typeof(bool) => true,
        _ => throw new InvalidOperationException(
            $"FieldMappingRuleEntity.{property.Name} is a {property.PropertyType.Name}: give it a distinct value here."),
    };
}
