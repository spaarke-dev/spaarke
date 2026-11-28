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

    // ── Task 067 fix round: a value only the app-only read returns is written, never returned ──────────────────────────

    private const string Secret = "SECRET-X";

    private const string PushLogMarker = "[FIELD-MAPPING-SOURCE] push";

    /// <summary>The caller's own read of the source returns the mapped column empty; the app-only read of the same row holds it.</summary>
    private static async Task<World> StartWithAValueOnlyTheAppOnlyReadReturnsAsync(Guid[] writable, string target = "sprk_event", string targetSet = "sprk_events")
    {
        var world = await World.StartAsync(writable: writable, target: target, targetSet: targetSet, sourceValue: null);
        world.Host.AppOnly.ExtraColumns[Column] = System.Text.Json.JsonSerializer.SerializeToElement(Secret);
        return world;
    }

    /// <summary>
    /// Must fire (F1): a preview never returns a value the caller's own read did not return. The caller's read of the source is
    /// empty and the app-only read holds the value, so the preview lists no change for that column and the response does not
    /// carry the value anywhere; a preview logs no copy (C7). MUTATION: building the preview from the lifted values puts the
    /// value in <c>changes[].to</c>.
    /// </summary>
    [Fact]
    public async Task Preview_OnAFilingEdge_AValueOnlyTheAppOnlyReadReturns_IsNeverInTheResponse_AndNothingIsLogged()
    {
        await using var world = await StartWithAValueOnlyTheAppOnlyReadReturnsAsync([ChildA, ChildB, ChildC]);
        world.Dataverse.Current[ChildA] = "OLD";

        var response = await world.PostAsync(new { sourceEntity = "sprk_matter", sourceRecordId = MatterId, targetEntity = "sprk_event", preview = true });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().NotContain(Secret, "the preview shows only what the caller's own read returned");
        JsonNode.Parse(body)!["changes"]!.AsArray().Should().BeEmpty("the caller's own read of the column is empty");
        world.Host.AppOnly.Reads.Should().ContainSingle("the filing parent is still read app-only, as the apply reads it");
        world.Host.Writes.Should().BeEmpty();
        world.Host.Logs.Should().NotContain(l => l.Message.Contains(PushLogMarker), "a preview copies nothing");
        world.Host.Logs.Should().NotContain(l => l.Message.Contains(Secret));
    }

    /// <summary>
    /// Must fire (F1, K): the apply writes the value only the app-only read returned into the confirmed child, never returns it
    /// (no field result, error, warning or change carries it), and logs the copy once, after the write, without the value.
    /// </summary>
    [Fact]
    public async Task Apply_WithConfirmedChildIds_AValueOnlyTheAppOnlyReadReturns_IsWritten_NeverReturned_AndLoggedAfterTheWrite()
    {
        await using var world = await StartWithAValueOnlyTheAppOnlyReadReturnsAsync([ChildA, ChildB, ChildC]);

        var response = await world.PostAsync(new
        {
            sourceEntity = "sprk_matter",
            sourceRecordId = MatterId,
            targetEntity = "sprk_event",
            childIds = new[] { ChildA },
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().NotContain(Secret, "the apply's response echoes no value it wrote");
        JsonNode.Parse(body)!["updatedCount"]!.GetValue<int>().Should().Be(1);
        world.Host.Writes.Should().ContainSingle().Which.Payload[Column].Should().Be(Secret, "the child is filed under the source");
        world.Host.Logs.Should().ContainSingle(l => l.Message.Contains(PushLogMarker)).Which.Message
            .Should().Contain($"sprk_matter.{Column} into {Column}").And.NotContain(Secret);
    }

    /// <summary>Must not fire (K): a push that writes no child logs no copy, though the app-only read was made.</summary>
    [Fact]
    public async Task Apply_WhenNoChildIsWritten_LogsNoCopy()
    {
        await using var world = await StartWithAValueOnlyTheAppOnlyReadReturnsAsync([ChildA, ChildB, ChildC]);

        var response = await world.PostAsync(new
        {
            sourceEntity = "sprk_matter",
            sourceRecordId = MatterId,
            targetEntity = "sprk_event",
            childIds = new[] { Stranger },
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().NotContain(Secret);
        world.Host.AppOnly.Reads.Should().ContainSingle();
        world.Host.Writes.Should().BeEmpty();
        world.Host.Logs.Should().NotContain(l => l.Message.Contains(PushLogMarker), "nothing was written, so nothing was copied");
    }

    /// <summary>
    /// Must fire (F2): in family mode the listed Filing edge is required before the source is read in any way. A pair with no
    /// such edge is the 409, with no read of the source (as the caller or app-only) and no copy logged, for a preview and for
    /// an apply that names its children. MUTATION: deciding the edge after the source reads makes the app-only read.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task APushToATableNoFilingEdgeJoins_ReadsNothingOfTheSource_AsTheCallerOrAppOnly(bool preview)
    {
        await using var world = await StartWithAValueOnlyTheAppOnlyReadReturnsAsync([ChildA], target: "sprk_fileversion", targetSet: "sprk_fileversions");

        var response = await world.PostAsync(preview
            ? new { sourceEntity = "sprk_matter", sourceRecordId = MatterId, targetEntity = "sprk_fileversion", preview = true, childIds = (Guid[]?)null }
            : new { sourceEntity = "sprk_matter", sourceRecordId = MatterId, targetEntity = "sprk_fileversion", preview = false, childIds = (Guid[]?)new[] { ChildA } });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        body.Should().NotContain(Secret);
        world.Host.AppOnly.Reads.Should().BeEmpty("no Filing edge, so the source is never read app-only");
        world.Host.SourceReads.Should().BeEmpty("nor as the caller");
        world.Host.Writes.Should().BeEmpty();
        world.Host.Logs.Should().NotContain(l => l.Message.Contains(PushLogMarker));
    }
}
