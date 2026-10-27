using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Office;

/// <summary>
/// word-add-in-r1 task 133: <c>GET /api/office/search/entities</c> finds work assignments for the Outlook Invoice
/// view's core-record picker — and ONLY when asked for by name. Work Assignment does not become a quick-create type,
/// and a search that names no type returns exactly the five types it did before.
/// </summary>
public class OfficeEntitySearchWorkAssignmentTests
{
    private static Dictionary<string, JsonElement> Row(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Fact]
    public void SearchMeta_WorkAssignment_UsesTheTablesRealColumns()
    {
        var meta = OfficeSearchService._searchMeta[AssociationEntityType.WorkAssignment];

        meta.EntitySet.Should().Be("sprk_workassignments");
        meta.IdField.Should().Be("sprk_workassignmentid");
        meta.NameField.Should().Be("sprk_name", "sprk_workassignment has no sprk_workassignmentname");
        meta.RefField.Should().Be("sprk_workassignmentnumber");
        meta.DescField.Should().Be("sprk_description");
        meta.EmailField.Should().BeNull();
    }

    [Fact]
    public void MapSearchRow_WorkAssignment_MapsTypeLogicalNameAndNumber()
    {
        var id = Guid.NewGuid();
        var row = Row($$"""
        {
          "sprk_workassignmentid": "{{id}}",
          "sprk_name": "Patent filing - EU",
          "sprk_workassignmentnumber": "WA-000042",
          "modifiedon": "2026-10-20T10:00:00Z"
        }
        """);

        var result = OfficeSearchService.MapSearchRow(
            AssociationEntityType.WorkAssignment,
            OfficeSearchService._searchMeta[AssociationEntityType.WorkAssignment],
            row);

        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.EntityType.Should().Be(AssociationEntityType.WorkAssignment);
        result.LogicalName.Should().Be("sprk_workassignment");
        result.Name.Should().Be("Patent filing - EU");
        result.DisplayInfo.Should().Be("WA-000042");
    }

    [Fact]
    public void EntityType_SerializesAsTheFriendlyName_TheClientMatchesOn()
    {
        var json = JsonSerializer.Serialize(new EntitySearchResult
        {
            Id = Guid.NewGuid(),
            EntityType = AssociationEntityType.WorkAssignment,
            LogicalName = "sprk_workassignment",
            Name = "WA",
        });

        json.Should().Contain("\"WorkAssignment\"");
    }

    [Theory]
    [InlineData("WorkAssignment")]
    [InlineData("workassignment")]
    public void GetEntityTypesToSearch_WorkAssignmentByName_SearchesIt(string requested)
    {
        OfficeSearchService.GetEntityTypesToSearch([requested])
            .Should().BeEquivalentTo([AssociationEntityType.WorkAssignment]);
    }

    [Fact]
    public void GetEntityTypesToSearch_NoType_SearchesTheFiveTypesItDidBefore()
    {
        var expected = new[]
        {
            AssociationEntityType.Matter, AssociationEntityType.Project, AssociationEntityType.Invoice,
            AssociationEntityType.Account, AssociationEntityType.Contact,
        };

        OfficeSearchService.GetEntityTypesToSearch(null).Should().BeEquivalentTo(expected);
        OfficeSearchService.GetEntityTypesToSearch([]).Should().BeEquivalentTo(expected);
        OfficeSearchService.GetEntityTypesToSearch(["NotAType"]).Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void GetEntityTypesToSearch_NumericOrdinal_DoesNotReachWorkAssignment()
    {
        var ordinal = ((int)AssociationEntityType.WorkAssignment).ToString();

        OfficeSearchService.GetEntityTypesToSearch([ordinal])
            .Should().NotContain(AssociationEntityType.WorkAssignment);
    }

    [Theory]
    [InlineData("WorkAssignment")]
    [InlineData("workassignment")]
    [InlineData("sprk_workassignment")]
    public void QuickCreate_DoesNotAcceptWorkAssignment(string entityType)
    {
        QuickCreateFieldRequirements.TryParse(entityType, out _).Should().BeFalse(
            "task 133 adds Work Assignment to the SEARCH only; it is not a \"+ New\" type");
    }
}
