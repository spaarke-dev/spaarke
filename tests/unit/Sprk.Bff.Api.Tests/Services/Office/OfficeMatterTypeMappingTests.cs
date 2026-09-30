using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Office;

/// <summary>
/// Proves <see cref="OfficeService.MapMatterTypeRow"/> — the pure mapper behind
/// <c>GET /api/office/search/matter-types</c> (spaarkeai-word-add-in-r1 task 038) — maps
/// <c>sprk_mattertype_ref</c> Web API JSON rows to <see cref="Sprk.Bff.Api.Models.Office.MatterTypeOption"/>
/// correctly. No mocks, no HTTP — mirrors <c>OfficeEntitySearchMappingTests</c>'s shape for
/// <see cref="OfficeService.MapSearchRow"/>.
/// </summary>
public class OfficeMatterTypeMappingTests
{
    private static Dictionary<string, JsonElement> Row(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public void MapMatterTypeRow_WithNameAndCode_MapsIdNameAndCode()
    {
        var id = Guid.NewGuid();
        var row = Row($$"""
        {
          "sprk_mattertype_refid": "{{id}}",
          "sprk_mattertypename": "Litigation",
          "sprk_mattertypecode": "LITG",
          "statecode": 0
        }
        """);

        var result = OfficeService.MapMatterTypeRow(row);

        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.Name.Should().Be("Litigation");
        result.Code.Should().Be("LITG");
    }

    [Fact]
    public void MapMatterTypeRow_MissingCode_MapsWithNullCode()
    {
        var row = Row($$"""
        { "sprk_mattertype_refid": "{{Guid.NewGuid()}}", "sprk_mattertypename": "Commercial" }
        """);

        var result = OfficeService.MapMatterTypeRow(row);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Commercial");
        result.Code.Should().BeNull();
    }

    [Fact]
    public void MapMatterTypeRow_MissingName_ReturnsNull()
    {
        var row = Row($$"""
        { "sprk_mattertype_refid": "{{Guid.NewGuid()}}", "sprk_mattertypecode": "PAT" }
        """);

        OfficeService.MapMatterTypeRow(row)
            .Should().BeNull("an unnamed reference row must never surface in the dropdown");
    }

    [Fact]
    public void MapMatterTypeRow_BlankName_ReturnsNull()
    {
        var row = Row($$"""
        { "sprk_mattertype_refid": "{{Guid.NewGuid()}}", "sprk_mattertypename": "   " }
        """);

        OfficeService.MapMatterTypeRow(row).Should().BeNull();
    }

    [Fact]
    public void MapMatterTypeRow_MissingId_ReturnsNull()
    {
        var row = Row("""
        { "sprk_mattertypename": "Employment" }
        """);

        OfficeService.MapMatterTypeRow(row)
            .Should().BeNull("a reference row with no parseable id cannot be sent back as matterTypeId");
    }

    [Fact]
    public void MapMatterTypeRow_UnparseableId_ReturnsNull()
    {
        var row = Row($$"""
        { "sprk_mattertype_refid": "not-a-guid", "sprk_mattertypename": "Trademark" }
        """);

        OfficeService.MapMatterTypeRow(row).Should().BeNull();
    }
}
