// field-mapping-server-write-path-r1 task 052a — the app-only column metadata Repair reads (IGenericEntityService
// .GetColumnMetadataAsync): each column's type, update validity, field security and text length, projected from the SDK's
// attribute metadata.

using FluentAssertions;
using Microsoft.Xrm.Sdk.Metadata;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

[Trait("status", "new")]
public sealed class ColumnMetadataProjectionTests
{
    [Fact]
    public void ATextColumn_CarriesItsMaxLength_AndItsFlagsAsStated()
    {
        var columns = DataverseServiceClientImpl.ColumnMetadataOf(Entity(
            new StringAttributeMetadata { LogicalName = "sprk_clientname", MaxLength = 100, IsSecured = true, IsValidForUpdate = true }));

        var column = columns["SPRK_ClientName"]; // keyed case-insensitively
        column.LogicalName.Should().Be("sprk_clientname");
        column.AttributeType.Should().Be("String");
        column.MaxLength.Should().Be(100);
        column.IsSecured.Should().BeTrue();
        column.IsValidForUpdate.Should().BeTrue();
    }

    [Fact]
    public void AMemoColumn_CarriesItsMaxLength()
    {
        DataverseServiceClientImpl.ColumnMetadataOf(Entity(new MemoAttributeMetadata { LogicalName = "sprk_notes", MaxLength = 4000 }))
            ["sprk_notes"].MaxLength.Should().Be(4000);
    }

    [Fact]
    public void AMoneyColumn_IsTypedMoney_AndHasNoMaxLength()
    {
        var column = DataverseServiceClientImpl.ColumnMetadataOf(Entity(
            new MoneyAttributeMetadata { LogicalName = "sprk_estimatedfee", IsSecured = false }))["sprk_estimatedfee"];

        column.AttributeType.Should().Be("Money");
        column.MaxLength.Should().BeNull();
        column.IsSecured.Should().BeFalse();
    }

    [Fact]
    public void NumberAndMoneyColumns_CarryTheirRange()
    {
        var columns = DataverseServiceClientImpl.ColumnMetadataOf(Entity(
            new IntegerAttributeMetadata { LogicalName = "sprk_count", MinValue = 0, MaxValue = 1000 },
            new DecimalAttributeMetadata { LogicalName = "sprk_rate", MinValue = -1.5m, MaxValue = 99.25m },
            new DoubleAttributeMetadata { LogicalName = "sprk_ratio", MinValue = -100d, MaxValue = 100.5d },
            new MoneyAttributeMetadata { LogicalName = "sprk_fee", MinValue = 0d, MaxValue = 1_000_000d }));

        (columns["sprk_count"].MinValue, columns["sprk_count"].MaxValue).Should().Be((0m, 1000m));
        (columns["sprk_rate"].MinValue, columns["sprk_rate"].MaxValue).Should().Be((-1.5m, 99.25m));
        (columns["sprk_ratio"].MinValue, columns["sprk_ratio"].MaxValue).Should().Be((-100m, 100.5m));
        (columns["sprk_fee"].MinValue, columns["sprk_fee"].MaxValue).Should().Be((0m, 1_000_000m));
    }

    [Fact]
    public void ALookup_CarriesItsTargets_AndAChoice_ItsOptionValues()
    {
        var choice = new PicklistAttributeMetadata { LogicalName = "sprk_status", OptionSet = new OptionSetMetadata() };
        choice.OptionSet.Options.Add(new OptionMetadata(new Microsoft.Xrm.Sdk.Label(), 100000000));
        choice.OptionSet.Options.Add(new OptionMetadata(new Microsoft.Xrm.Sdk.Label(), 100000001));
        var columns = DataverseServiceClientImpl.ColumnMetadataOf(Entity(
            new LookupAttributeMetadata { LogicalName = "sprk_client", Targets = ["account", "contact"] },
            choice));

        columns["sprk_client"].Targets.Should().Equal("account", "contact");
        columns["sprk_status"].OptionValues.Should().Equal(100000000, 100000001);
        columns["sprk_client"].OptionValues.Should().BeNull();
        columns["sprk_status"].Targets.Should().BeNull();
    }

    [Fact]
    public void ADoubleBoundNoDecimalHolds_IsNotStated()
    {
        DataverseServiceClientImpl.ColumnMetadataOf(Entity(
                new DoubleAttributeMetadata { LogicalName = "sprk_ratio", MinValue = double.MinValue, MaxValue = 1d }))
            ["sprk_ratio"].MinValue.Should().BeNull();
    }

    [Fact]
    public void FlagsTheMetadataDoesNotState_StayUnknown()
    {
        var column = DataverseServiceClientImpl.ColumnMetadataOf(Entity(new StringAttributeMetadata { LogicalName = "sprk_name" }))
            ["sprk_name"];

        column.IsSecured.Should().BeNull("an unstated flag is unknown, and Repair fails closed on it");
        column.IsValidForUpdate.Should().BeNull();
    }

    [Fact]
    public void NoEntity_OrAnAttributeWithNoName_GivesNoColumn()
    {
        DataverseServiceClientImpl.ColumnMetadataOf(null).Should().BeEmpty();
        DataverseServiceClientImpl.ColumnMetadataOf(Entity(new StringAttributeMetadata())).Should().BeEmpty();
    }

    private static EntityMetadata Entity(params AttributeMetadata[] attributes)
    {
        var entity = new EntityMetadata { LogicalName = "sprk_matter" };
        typeof(EntityMetadata).GetProperty(nameof(EntityMetadata.Attributes))!.SetValue(entity, attributes);
        return entity;
    }
}
