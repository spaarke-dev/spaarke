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
