using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 039, review R4 (FR-38, #2399): the wizard's REAL route
/// (<c>POST /api/v1/child-records/sprk_workassignment</c>, <see cref="ChildRecordEndpoints.CreateAsync"/> over the real
/// <c>RecordCreationService</c>) with a Web API body that names the search index. The service drops both index columns from
/// that body before it is mapped and before step 5 asks the caller anything, so a bind the caller cannot AppendTo, or one that is
/// malformed, is neither asked about nor refused, and the owning unit's index is what is written. Must fire: take the Web API
/// body drop out of <c>RecordCreationService</c> step 2 and the AppendTo case is refused and the malformed case is a 400.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid UnitIndexRow = Guid.Parse("a0390000-0000-4000-8000-0000000000b1");
    private static readonly Guid CallersIndexRow = Guid.Parse("a0390000-0000-4000-8000-0000000000b2");
    private const string UnitIndexName = "spaarke-child-unit-index";

    /// <summary>A value the server put in the create body, whether it is a plain string or a parsed JSON string.</summary>
    private static string? ServerValue(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out var value) ? (value is System.Text.Json.JsonElement e ? e.GetString() : value?.ToString()) : null;

    /// <summary>POST /api/v1/child-records/sprk_workassignment, the owning (child) business unit holding a search index.</summary>
    private Task<IResult> CreateChildUnderAUnitWithAnIndex(Dictionary<string, object?> payload, List<string> catalogReads)
    {
        var entities = RouteRecordCreation.Entities();
        entities
            .Setup(e => e.RetrieveAsync("businessunit", Directory.ChildBu, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var unit = new Entity("businessunit", Directory.ChildBu);
                unit["sprk_searchindexname"] = UnitIndexName;
                unit["sprk_ai_search_index"] = new EntityReference("sprk_aisearchindex", UnitIndexRow);
                return unit;
            });
        _user.GetOverride = path =>
        {
            if (path.Contains("sprk_aisearchindexes(", StringComparison.Ordinal))
                catalogReads.Add(path);
            return null;
        };

        return ChildRecordEndpoints.CreateAsync(
            "sprk_workassignment", Payload(payload), _user,
            RouteRecordCreation.Over(
                _world.Resolver(), _appOnly.Object, Restamper(), Shares(), SecureRootFilingGateFixtures.NothingSecure(),
                null, _routeMembership.Object, entities.Object),
            HttpContextOfCaller(), NullLogger<Program>.Instance, CancellationToken.None);
    }

    [Theory]
    [InlineData("sprk_AI_Search_Index@odata.bind")]
    [InlineData("sprk_ai_search_index@odata.bind")]
    public async Task WorkAssignmentCreate_ABodyBindingASearchIndexTheCallerCannotAppendTo_IsCreated_AskingNoAppendToOnTheCatalog(string bindKey)
    {
        _user.NoAppendToIn.Add(("sprk_aisearchindexes", CallersIndexRow));
        var payload = WizardWorkAssignment(OrdinaryMatter);
        payload[bindKey] = $"/sprk_aisearchindexes({CallersIndexRow:D})";
        var catalogReads = new List<string>();

        var result = await CreateChildUnderAUnitWithAnIndex(payload, catalogReads);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        catalogReads.Should().BeEmpty("the bind is dropped before step 5, so AppendTo is never asked on a catalog row (#2399)");
        var fields = _appCreates.Should().ContainSingle().Subject.Fields;
        fields.Keys.Where(k => k.StartsWith("sprk_ai_search_index", StringComparison.OrdinalIgnoreCase)).Should().ContainSingle()
            .Which.Should().Be("sprk_AI_Search_Index@odata.bind", "the body carries the server's bind only");
        ServerValue(fields, "sprk_AI_Search_Index@odata.bind").Should().Be($"/sprk_aisearchindexes({UnitIndexRow:D})", "the owning unit's index");
        ServerValue(fields, "sprk_searchindexname").Should().Be(UnitIndexName, "the owning unit's name; the wizard's own was dropped");
    }

    [Fact]
    public async Task WorkAssignmentCreate_ABodyWithBothSpellingsOfTheSearchIndexBind_IsCreated_WithTheOwningUnitsIndexOnly()
    {
        _user.NoAppendToIn.Add(("sprk_aisearchindexes", CallersIndexRow));
        var payload = WizardWorkAssignment(OrdinaryMatter);
        payload["sprk_AI_Search_Index@odata.bind"] = $"/sprk_aisearchindexes({CallersIndexRow:D})";
        payload["sprk_ai_search_index@odata.bind"] = $"/sprk_aisearchindexes({CallersIndexRow:D})";
        var catalogReads = new List<string>();

        var result = await CreateChildUnderAUnitWithAnIndex(payload, catalogReads);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        catalogReads.Should().BeEmpty();
        var fields = _appCreates.Should().ContainSingle().Subject.Fields;
        fields.Keys.Where(k => k.StartsWith("sprk_ai_search_index", StringComparison.OrdinalIgnoreCase)).Should().ContainSingle();
        ServerValue(fields, "sprk_AI_Search_Index@odata.bind").Should().Be($"/sprk_aisearchindexes({UnitIndexRow:D})");
    }

    [Theory]
    [InlineData("sprk_AI_Search_Index@odata.bind")]
    [InlineData("sprk_ai_search_index@odata.bind")]
    public async Task WorkAssignmentCreate_ABodyWithAMalformedSearchIndexBind_IsCreated_NotRefusedAs400(string bindKey)
    {
        var payload = WizardWorkAssignment(OrdinaryMatter);
        payload[bindKey] = "this is not a bind";
        var catalogReads = new List<string>();

        var result = await CreateChildUnderAUnitWithAnIndex(payload, catalogReads);

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        var fields = _appCreates.Should().ContainSingle().Subject.Fields;
        ServerValue(fields, "sprk_AI_Search_Index@odata.bind").Should().Be($"/sprk_aisearchindexes({UnitIndexRow:D})");
    }
}
