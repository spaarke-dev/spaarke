using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.FieldMappings;

/// <summary>
/// field-mapping-server-write-path-r1 task 065 (owner 2026-11-26): family mode finds the children through a listed Filing edge,
/// so the field-secured-source rule is lifted there: a rule that reads a field-secured source column pushes like any other, and
/// the source catalog is not asked for <c>IsSecured</c>.
/// </summary>
public partial class FieldMappingPushFamilyContractTests
{
    [Fact]
    public async Task Apply_WithConfirmedChildIds_ARuleWhoseSourceColumnIsFieldSecured_Pushes_WithNoWarning()
    {
        await using var world = await World.StartAsync(writable: [ChildA, ChildB, ChildC]);
        world.Host.SourceColumnSecurity[Column] = true;

        var response = await world.PostAsync(new
        {
            sourceEntity = "sprk_matter",
            sourceRecordId = MatterId,
            targetEntity = "sprk_event",
            childIds = new[] { ChildA },
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var json = JsonNode.Parse(body)!;
        json["updatedCount"]!.GetValue<int>().Should().Be(1);
        json["skippedRuleCount"]!.GetValue<int>().Should().Be(0);
        (json["warnings"]?.AsArray() ?? []).Should().BeEmpty();
        world.Host.Writes.Should().ContainSingle().Which.Payload[Column].Should().Be("REF-166",
            "the child is filed under the source by a Filing edge, so a field-secured source value copies");
        world.Host.MetadataQueries.Should().NotContain(q => q.Query != null && q.Query.Contains(FieldSecuredSourceRule.IsSecuredProperty),
            "family mode does not read the source columns' field security");
    }
}
