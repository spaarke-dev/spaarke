using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Office;

/// <summary>
/// Proves the real Dataverse "File to" entity search (task 026 / #229) maps Web API JSON rows to
/// <see cref="EntitySearchResult"/> correctly, replacing the old <c>GenerateStubResults</c> fixtures.
/// Tests the pure mapper (<see cref="OfficeService.MapSearchRow"/>) — no mocks, no HTTP.
/// </summary>
public class OfficeEntitySearchMappingTests
{
    private static readonly OfficeService.EntitySearchMeta MatterMeta =
        new("sprk_matters", "sprk_matterid", "sprk_mattername", "sprk_matternumber", "sprk_matterdescription");

    private static readonly OfficeService.EntitySearchMeta ContactMeta =
        new("contacts", "contactid", "fullname", null, "jobtitle");

    private static Dictionary<string, JsonElement> Row(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public void MapSearchRow_Matter_WithNumber_MapsIdNameTypeAndUsesNumberForDisplay()
    {
        var id = Guid.NewGuid();
        var row = Row($$"""
        {
          "sprk_matterid": "{{id}}",
          "sprk_mattername": "Smith v Jones",
          "sprk_matternumber": "MAT-2026-001",
          "sprk_matterdescription": "Commercial dispute",
          "modifiedon": "2026-08-20T10:00:00Z"
        }
        """);

        var result = OfficeService.MapSearchRow(AssociationEntityType.Matter, MatterMeta, row);

        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.Name.Should().Be("Smith v Jones");
        result.EntityType.Should().Be(AssociationEntityType.Matter);
        result.LogicalName.Should().Be("sprk_matter");
        result.DisplayInfo.Should().Be("MAT-2026-001");   // reference number preferred for disambiguation
        result.PrimaryField.Should().Be("MAT-2026-001");
        result.ModifiedOn!.Value.UtcDateTime.Should().Be(new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void MapSearchRow_Contact_NoRefField_FallsBackToDescriptionForDisplay()
    {
        var row = Row($$"""
        {
          "contactid": "{{Guid.NewGuid()}}",
          "fullname": "Jane Doe",
          "jobtitle": "General Counsel"
        }
        """);

        var result = OfficeService.MapSearchRow(AssociationEntityType.Contact, ContactMeta, row);

        result.Should().NotBeNull();
        result!.LogicalName.Should().Be("contact");
        result.DisplayInfo.Should().Be("General Counsel"); // no ref field → description (jobtitle)
        result.PrimaryField.Should().Be("Jane Doe");       // no ref field → name
    }

    [Fact]
    public void MapSearchRow_MissingName_ReturnsNull()
    {
        var row = Row($$"""
        { "sprk_matterid": "{{Guid.NewGuid()}}", "sprk_matternumber": "MAT-2026-002" }
        """);

        OfficeService.MapSearchRow(AssociationEntityType.Matter, MatterMeta, row)
            .Should().BeNull("an unnamed record must never surface in the picker");
    }

    [Fact]
    public void MapSearchRow_BlankName_ReturnsNull()
    {
        var row = Row($$"""
        { "contactid": "{{Guid.NewGuid()}}", "fullname": "   " }
        """);

        OfficeService.MapSearchRow(AssociationEntityType.Contact, ContactMeta, row)
            .Should().BeNull();
    }
}
