using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Communication;
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

    [Theory]
    [InlineData("Project,Contact")]      // 1 | 4 = 5 = WorkAssignment under Enum.TryParse's flag-combining
    [InlineData("Project, Contact")]
    [InlineData("contact,project")]
    [InlineData("Matter,Invoice")]       // 0 | 2 = 2 = Invoice: a combination of two names is not either name
    [InlineData("Matter,Project,Contact")]
    [InlineData(",WorkAssignment")]
    [InlineData("WorkAssignment,")]
    public void GetEntityTypesToSearch_CommaJoinedNames_MatchNothing_NotACombination(string requested)
    {
        // Must-not-fire: a comma-joined string is not a type name, so it names no type (the default five are searched)
        // and above all it does not flag-combine into WorkAssignment.
        var searched = OfficeSearchService.GetEntityTypesToSearch([requested]);

        searched.Should().NotContain(AssociationEntityType.WorkAssignment);
        searched.Should().BeEquivalentTo(OfficeSearchService.DefaultSearchTypes);
    }

    [Fact]
    public void GetEntityTypesToSearch_AValidNameAlongsideACommaJoinedOne_SearchesOnlyTheValidName()
    {
        OfficeSearchService.GetEntityTypesToSearch(["Matter", "Project,Contact"])
            .Should().BeEquivalentTo([AssociationEntityType.Matter]);
    }

    [Theory]
    [InlineData("Matter", AssociationEntityType.Matter)]
    [InlineData("MATTER", AssociationEntityType.Matter)]
    [InlineData("  project ", AssociationEntityType.Project)]
    [InlineData("Invoice", AssociationEntityType.Invoice)]
    [InlineData("account", AssociationEntityType.Account)]
    [InlineData("Contact", AssociationEntityType.Contact)]
    public void GetEntityTypesToSearch_EveryDefinedName_StillResolves_IgnoringCaseAndSpaces(
        string requested, AssociationEntityType expected)
    {
        OfficeSearchService.GetEntityTypesToSearch([requested]).Should().BeEquivalentTo([expected]);
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

    // ---- SearchEntitiesAsync, end to end over a fake of the caller-scoped read seam -----------------------------------

    private static readonly Guid Caller = Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>Records every impersonated query the search issues, and answers from <c>rowsBySet</c>.</summary>
    private sealed class RecordingQuery(Dictionary<string, string[]> rowsBySet) : IImpersonatedCommunicationQuery
    {
        public List<(string EntitySet, string? OData, Guid Caller)> Calls { get; } = [];

        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
            string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct)
        {
            Calls.Add((entitySetName, odataQuery, callerSystemUserId));
            var rows = rowsBySet.TryGetValue(entitySetName, out var json)
                ? json.Select(Row).ToList()
                : [];
            return Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>(rows);
        }
    }

    private static OfficeSearchService NewService(IImpersonatedCommunicationQuery query)
    {
        // SearchEntitiesAsync reads only through the impersonated seam; the other two collaborators are never touched.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            // DataverseWebApiClient's constructor validates its credentials config; this double never authenticates.
            ["Graph:ManagedIdentity:Enabled"] = "true",
            ["API_APP_ID"] = "00000000-0000-0000-0000-0000000000aa",
            ["API_CLIENT_SECRET"] = "test-secret",
            ["TENANT_ID"] = "00000000-0000-0000-0000-0000000000cc",
        }).Build();
        var dataverse = new Mock<DataverseWebApiClient>(config, NullLogger<DataverseWebApiClient>.Instance, null!, null!);
        var probe = new CallerRecordAccessProbe(new HttpClient(), config, NullLogger<CallerRecordAccessProbe>.Instance);
        return new OfficeSearchService(dataverse.Object, query, probe, NullLogger<OfficeSearchService>.Instance);
    }

    [Fact]
    public async Task SearchEntitiesAsync_WorkAssignmentByName_QueriesTheWorkAssignmentsTable_AsTheCaller()
    {
        var waId = Guid.NewGuid();
        var query = new RecordingQuery(new()
        {
            ["sprk_workassignments"] =
            [
                $$"""
                { "sprk_workassignmentid": "{{waId}}", "sprk_name": "Patent filing - EU",
                  "sprk_workassignmentnumber": "WA-000042", "modifiedon": "2026-10-20T10:00:00Z" }
                """,
            ],
        });

        var response = await NewService(query).SearchEntitiesAsync(
            new EntitySearchRequest { Query = "patent", EntityTypes = ["WorkAssignment"], Top = 10 },
            "user-oid", Caller);

        // Exactly one impersonated read, of the work assignments table, as the calling user.
        query.Calls.Should().ContainSingle();
        var call = query.Calls[0];
        call.EntitySet.Should().Be("sprk_workassignments");
        call.Caller.Should().Be(Caller);
        call.OData.Should().Contain("contains(sprk_name,'patent')");
        call.OData.Should().Contain("contains(sprk_workassignmentnumber,'patent')");
        call.OData.Should().Contain("$select=sprk_workassignmentid,sprk_name,modifiedon,sprk_workassignmentnumber,sprk_description");

        response.Results.Should().ContainSingle();
        var result = response.Results[0];
        result.Id.Should().Be(waId);
        result.EntityType.Should().Be(AssociationEntityType.WorkAssignment);
        result.LogicalName.Should().Be("sprk_workassignment");
        result.DisplayInfo.Should().Be("WA-000042");
    }

    [Fact]
    public async Task SearchEntitiesAsync_NoTypeNamed_NeverQueriesTheWorkAssignmentsTable()
    {
        var query = new RecordingQuery([]);

        await NewService(query).SearchEntitiesAsync(
            new EntitySearchRequest { Query = "patent" }, "user-oid", Caller);

        query.Calls.Select(c => c.EntitySet).Should().BeEquivalentTo(
            ["sprk_matters", "sprk_projects", "sprk_invoices", "accounts", "contacts"]);
        query.Calls.Should().OnlyContain(c => c.Caller == Caller);
    }

    [Fact]
    public async Task SearchEntitiesAsync_ACommaJoinedTypeThatWouldCombineIntoWorkAssignment_DoesNotQueryIt()
    {
        var query = new RecordingQuery([]);

        await NewService(query).SearchEntitiesAsync(
            new EntitySearchRequest { Query = "patent", EntityTypes = ["Project,Contact"] }, "user-oid", Caller);

        query.Calls.Select(c => c.EntitySet).Should().NotContain("sprk_workassignments");
    }
}
