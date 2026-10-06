using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Office;

/// <summary>
/// Proves <see cref="OfficeSearchService.MapReferenceRow"/> — the pure mapper behind
/// <c>GET /api/office/search/{list}</c> (matter types since task 038; practice areas and project types since task 100)
/// — maps reference-table Web API JSON rows to <see cref="Sprk.Bff.Api.Models.Office.ReferenceListOption"/> correctly,
/// using each list's own columns. No mocks, no HTTP — mirrors <c>OfficeEntitySearchMappingTests</c>'s shape.
/// </summary>
public class OfficeReferenceListMappingTests
{
    private static readonly OfficeReferenceList MatterTypes = OfficeSearchService.ReferenceLists["matter-types"];
    private static readonly OfficeReferenceList PracticeAreas = OfficeSearchService.ReferenceLists["practice-areas"];
    private static readonly OfficeReferenceList ProjectTypes = OfficeSearchService.ReferenceLists["project-types"];

    private static Dictionary<string, JsonElement> Row(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public void MatterType_WithNameAndCode_MapsIdNameAndCode()
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

        var result = OfficeSearchService.MapReferenceRow(row, MatterTypes);

        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.Name.Should().Be("Litigation");
        result.Code.Should().Be("LITG");
    }

    [Fact]
    public void MatterType_MissingCode_MapsWithNullCode()
    {
        var row = Row($$"""
        { "sprk_mattertype_refid": "{{Guid.NewGuid()}}", "sprk_mattertypename": "Commercial" }
        """);

        var result = OfficeSearchService.MapReferenceRow(row, MatterTypes);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Commercial");
        result.Code.Should().BeNull();
    }

    [Fact]
    public void MissingName_ReturnsNull()
    {
        var row = Row($$"""
        { "sprk_mattertype_refid": "{{Guid.NewGuid()}}", "sprk_mattertypecode": "PAT" }
        """);

        OfficeSearchService.MapReferenceRow(row, MatterTypes)
            .Should().BeNull("an unnamed reference row must never surface in the dropdown");
    }

    [Fact]
    public void BlankName_ReturnsNull()
    {
        var row = Row($$"""
        { "sprk_mattertype_refid": "{{Guid.NewGuid()}}", "sprk_mattertypename": "   " }
        """);

        OfficeSearchService.MapReferenceRow(row, MatterTypes).Should().BeNull();
    }

    [Fact]
    public void MissingId_ReturnsNull()
    {
        var row = Row("""
        { "sprk_mattertypename": "Employment" }
        """);

        OfficeSearchService.MapReferenceRow(row, MatterTypes)
            .Should().BeNull("a reference row with no parseable id cannot be sent back on quick-create");
    }

    [Fact]
    public void UnparseableId_ReturnsNull()
    {
        var row = Row($$"""
        { "sprk_mattertype_refid": "not-a-guid", "sprk_mattertypename": "Trademark" }
        """);

        OfficeSearchService.MapReferenceRow(row, MatterTypes).Should().BeNull();
    }

    [Fact]
    public void PracticeArea_MapsItsOwnColumns()
    {
        var id = Guid.NewGuid();
        var row = Row($$"""
        { "sprk_practicearea_refid": "{{id}}", "sprk_practiceareaname": "Appellate", "sprk_practiceareacode": "APPL" }
        """);

        var result = OfficeSearchService.MapReferenceRow(row, PracticeAreas);

        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.Name.Should().Be("Appellate");
        result.Code.Should().Be("APPL");
    }

    [Fact]
    public void ProjectType_HasNoCodeColumn_AndNamesItsRowsSprkName()
    {
        // Live metadata (spaarkedev1, 2026-10-05): sprk_projecttype_ref's primary name is sprk_name; it has no code.
        var id = Guid.NewGuid();
        var row = Row($$"""
        { "sprk_projecttype_refid": "{{id}}", "sprk_name": "Litigation", "sprk_mattertypecode": "IGNORED" }
        """);

        var result = OfficeSearchService.MapReferenceRow(row, ProjectTypes);

        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.Name.Should().Be("Litigation");
        result.Code.Should().BeNull("project types have no code column, so no code is read");
    }

    [Theory]
    [InlineData("matter-types")]
    [InlineData("practice-areas")]
    [InlineData("project-types")]
    [InlineData("PROJECT-TYPES")]
    public void TheOfferedLists_Resolve(string key)
        => OfficeSearchService.TryGetReferenceList(key, out _).Should().BeTrue();

    [Theory]
    [InlineData("entities")]
    [InlineData("contacts")]
    [InlineData("sprk_matters")]
    [InlineData("")]
    [InlineData(null)]
    public void AnyOtherName_DoesNotResolve(string? key)
        => OfficeSearchService.TryGetReferenceList(key, out _).Should().BeFalse("the route reads only the closed table");
}
