using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using XrmEntity = Microsoft.Xrm.Sdk.Entity;
using XrmReference = Microsoft.Xrm.Sdk.EntityReference;
using XrmOption = Microsoft.Xrm.Sdk.OptionSetValue;

namespace Sprk.Bff.Api.Tests.Api.FieldMappings;

/// <summary>
/// field-mapping-server-write-path-r1 task 022c — the Restricted-source rule through the route: the apply and the preview give
/// the same per-child code for a child that is not Restricted, and both write (or list) the Restricted child.
/// </summary>
public partial class FieldMappingPushFamilyContractTests
{
    /// <summary>A matter that is not secure, owned by a user, whose Access Permission is Restricted; ChildA is Standard and
    /// ChildB is Restricted (their stored values).</summary>
    private static void ArrangeRestrictedSourceAndChildren(World world)
    {
        world.Host.Entities.Setup(e => e.RetrieveAsync("sprk_matter", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) => new XrmEntity(table, id)
            {
                ["ownerid"] = new XrmReference("systemuser", Guid.NewGuid()),
                ["sprk_issecure"] = false,
                [InheritedAccessPermission.Column] = new XrmOption(InheritedAccessPermission.Restricted),
            });
        world.Host.Entities.Setup(e => e.RetrieveAsync("sprk_event", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) => new XrmEntity(table, id)
            {
                [InheritedAccessPermission.Column] = new XrmOption(
                    id == ChildB ? InheritedAccessPermission.Restricted : InheritedAccessPermission.Standard),
            });
    }

    [Fact]
    public async Task Apply_FromARestrictedSource_WritesOnlyTheRestrictedChild_AndGivesTheOtherTheRestrictedCode()
    {
        await using var world = await World.StartAsync(writable: [ChildA, ChildB]);
        ArrangeRestrictedSourceAndChildren(world);

        var response = await world.PostAsync(new
        {
            sourceEntity = "sprk_matter",
            sourceRecordId = MatterId,
            targetEntity = "sprk_event",
            childIds = new[] { ChildA, ChildB },
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var json = JsonNode.Parse(body)!;
        json["updatedCount"]!.GetValue<int>().Should().Be(1);
        var error = json["errors"]!.AsArray().Should().ContainSingle().Which!;
        error["recordId"]!.GetValue<string>().Should().Be(ChildA.ToString("D"));
        error["reasonCode"]!.GetValue<string>().Should().Be(FieldMappingPushSourceGate.RestrictedSourceNotRestrictedReasonCode);
        error["error"]!.GetValue<string>().Should().Be(FieldMappingPushSourceGate.RestrictedSourceNotRestrictedMessage);
        world.Host.Writes.Should().ContainSingle().Which.Id.Should().Be(ChildB);
    }

    [Fact]
    public async Task Preview_FromARestrictedSource_ListsOnlyTheRestrictedChild_AndReturnsTheOtherWithTheSameCode()
    {
        await using var world = await World.StartAsync(writable: [ChildA, ChildB]);
        world.Dataverse.Current[ChildA] = "OLD";
        world.Dataverse.Current[ChildB] = "OLD";
        ArrangeRestrictedSourceAndChildren(world);

        var response = await world.PostAsync(new { sourceEntity = "sprk_matter", sourceRecordId = MatterId, targetEntity = "sprk_event", preview = true });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var json = JsonNode.Parse(body)!;
        json["preview"]!.GetValue<bool>().Should().BeTrue();
        json["changes"]!.AsArray().Select(c => c!["recordId"]!.GetValue<string>()).Should().Equal(ChildB.ToString("D"));
        var skipped = json["errors"]!.AsArray().Should().ContainSingle().Which!;
        skipped["recordId"]!.GetValue<string>().Should().Be(ChildA.ToString("D"));
        skipped["reasonCode"]!.GetValue<string>().Should().Be(FieldMappingPushSourceGate.RestrictedSourceNotRestrictedReasonCode);
        world.Host.Writes.Should().BeEmpty();
    }
}
