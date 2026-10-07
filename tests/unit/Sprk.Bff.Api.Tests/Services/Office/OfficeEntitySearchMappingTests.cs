using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Office;

/// <summary>
/// Proves the real Dataverse "File to" entity search (task 026 / #229) maps Web API JSON rows to
/// <see cref="EntitySearchResult"/> correctly, replacing the old <c>GenerateStubResults</c> fixtures.
/// Tests the pure mapper (<see cref="OfficeSearchService.MapSearchRow"/>) — no mocks, no HTTP.
/// </summary>
public class OfficeEntitySearchMappingTests
{
    private static readonly OfficeSearchService.EntitySearchMeta MatterMeta =
        new("sprk_matters", "sprk_matterid", "sprk_mattername", "sprk_matternumber", "sprk_matterdescription");

    private static readonly OfficeSearchService.EntitySearchMeta ContactMeta =
        new("contacts", "contactid", "fullname", null, "jobtitle");

    // Task 091 (UAT-2): the real production metadata for Contact includes EmailField; ContactMeta above
    // (no EmailField) stays as-is to keep the two pre-existing tests below byte-identical.
    private static readonly OfficeSearchService.EntitySearchMeta ContactMetaWithEmail =
        new("contacts", "contactid", "fullname", null, "jobtitle", "emailaddress1");

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

        var result = OfficeSearchService.MapSearchRow(AssociationEntityType.Matter, MatterMeta, row);

        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.Name.Should().Be("Smith v Jones");
        result.EntityType.Should().Be(AssociationEntityType.Matter);
        result.LogicalName.Should().Be("sprk_matter");
        result.DisplayInfo.Should().Be("MAT-2026-001");   // reference number preferred for disambiguation
        result.PrimaryField.Should().Be("MAT-2026-001");
        result.ModifiedOn!.Value.UtcDateTime.Should().Be(new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc));
        result.Email.Should().BeNull("Matter has no EmailField — task 091's additive field is Contact-only");
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

        var result = OfficeSearchService.MapSearchRow(AssociationEntityType.Contact, ContactMeta, row);

        result.Should().NotBeNull();
        result!.LogicalName.Should().Be("contact");
        result.DisplayInfo.Should().Be("General Counsel"); // no ref field → description (jobtitle)
        result.PrimaryField.Should().Be("Jane Doe");       // no ref field → name
        result.Email.Should().BeNull("this meta has no EmailField configured — never a fallback onto PrimaryField/Name");
    }

    [Fact]
    public void MapSearchRow_Contact_WithEmailField_PopulatesEmail_SoDuplicateNamesCanBeToldApart()
    {
        var row = Row($$"""
        {
          "contactid": "{{Guid.NewGuid()}}",
          "fullname": "Jane Cooper",
          "jobtitle": "General Counsel",
          "emailaddress1": "jane.cooper@acme.com"
        }
        """);

        var result = OfficeSearchService.MapSearchRow(AssociationEntityType.Contact, ContactMetaWithEmail, row);

        result.Should().NotBeNull();
        result!.Email.Should().Be("jane.cooper@acme.com");
        // Email is additive — it does not change what PrimaryField/DisplayInfo already carried.
        result.PrimaryField.Should().Be("Jane Cooper");
        result.DisplayInfo.Should().Be("General Counsel");
    }

    [Fact]
    public void MapSearchRow_Contact_WithEmailField_ButNoEmailOnFile_EmailIsNull()
    {
        var row = Row($$"""
        {
          "contactid": "{{Guid.NewGuid()}}",
          "fullname": "Robert Fox",
          "jobtitle": "Paralegal"
        }
        """);

        var result = OfficeSearchService.MapSearchRow(AssociationEntityType.Contact, ContactMetaWithEmail, row);

        result.Should().NotBeNull();
        result!.Email.Should().BeNull("a contact with no email on file must render as name-only, never the literal 'contact'");
    }

    [Fact]
    public void MapSearchRow_MissingName_ReturnsNull()
    {
        var row = Row($$"""
        { "sprk_matterid": "{{Guid.NewGuid()}}", "sprk_matternumber": "MAT-2026-002" }
        """);

        OfficeSearchService.MapSearchRow(AssociationEntityType.Matter, MatterMeta, row)
            .Should().BeNull("an unnamed record must never surface in the picker");
    }

    [Fact]
    public void MapSearchRow_BlankName_ReturnsNull()
    {
        var row = Row($$"""
        { "contactid": "{{Guid.NewGuid()}}", "fullname": "   " }
        """);

        OfficeSearchService.MapSearchRow(AssociationEntityType.Contact, ContactMeta, row)
            .Should().BeNull();
    }
}
