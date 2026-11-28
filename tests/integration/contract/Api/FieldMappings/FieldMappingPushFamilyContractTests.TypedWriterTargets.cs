using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Spaarke.Dataverse;
using Xunit;
using PushHost = Sprk.Bff.Api.Tests.Api.FieldMappings.FieldMappingPushAuthorizationContractTests.PushHost;

namespace Sprk.Bff.Api.Tests.Api.FieldMappings;

/// <summary>
/// field-mapping-server-write-path-r1 DI-053 (#3326), family mode: a rule aimed at a column only its typed writers may write is
/// refused at rule selection for a preview and for an apply that names its confirmed children, as for an ordinary push
/// (<c>FieldMappingPushAuthorizationContractTests.TypedWriterTargets.cs</c>). The caller may write every child, and the host's
/// Dataverse double accepts every column of every PATCH.
/// </summary>
public partial class FieldMappingPushFamilyContractTests
{
    private static FieldMappingRuleEntity[] OrdinaryRuleAnd(string typedWriterColumn) =>
    [
        PushHost.Rule(Column, 0, typedWriterColumn, 0),
        PushHost.Rule(Column, 0, Column, 0),
    ];

    private static Task<World> InvoiceFamilyAsync(string typedWriterColumn) => World.StartAsync(
        writable: [ChildA, ChildB, ChildC], target: "sprk_invoice", targetSet: "sprk_invoices",
        rules: OrdinaryRuleAnd(typedWriterColumn), filingColumn: "sprk_matter");

    /// <summary>Must fire (DI-053): the confirmed invoice's PATCH carries nothing for the typed-writer column; must not fire: the
    /// ordinary column of the same push is written. MUTATION: removing the refusal puts the column in the payload.</summary>
    [Theory]
    [InlineData("sprk_firmhold")]
    [InlineData("sprk_vendororg")]
    [InlineData("sprk_totalamount")]
    public async Task Apply_WithConfirmedInvoices_ARuleOntoATypedWriterColumn_IsRefused_AndTheOrdinaryColumnIsWritten(string column)
    {
        await using var world = await InvoiceFamilyAsync(column);

        var response = await world.PostAsync(new
        {
            sourceEntity = "sprk_matter",
            sourceRecordId = MatterId,
            targetEntity = "sprk_invoice",
            childIds = new[] { ChildA },
        });

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        world.Host.Writes.Should().ContainSingle().Which.Payload.Should().BeEquivalentTo(
            new Dictionary<string, object?> { [Column] = "REF-166" },
            $"{column} is written only by its typed writers (must fire); the ordinary column still is (must not fire)");
        world.Host.UpdateOnlyWrites.Should().ContainSingle().Which.ImpersonatedAs.Should().Be(CallerSystemUserId, "written as the caller");
        FieldMappingPushAuthorizationContractTests.ShouldNameTheRefusedTarget(JsonNode.Parse(text)!, column);
    }

    /// <summary>Must fire (DI-053): a preview never lists the typed-writer column in <c>changes</c>; must not fire: the ordinary
    /// column's change is listed. Nothing is written.</summary>
    [Theory]
    [InlineData("sprk_firmhold")]
    [InlineData("sprk_vendororg")]
    [InlineData("sprk_totalamount")]
    public async Task Preview_ARuleOntoATypedWriterColumn_IsNotInTheChanges_AndTheOrdinaryChangeIs(string column)
    {
        await using var world = await InvoiceFamilyAsync(column);
        world.Dataverse.AttributeNames.Add(column);
        world.Dataverse.Current[ChildA] = "OLD";

        var response = await world.PostAsync(new { sourceEntity = "sprk_matter", sourceRecordId = MatterId, targetEntity = "sprk_invoice", preview = true });

        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        var body = JsonNode.Parse(text)!;
        body["preview"]!.GetValue<bool>().Should().BeTrue();
        var changes = body["changes"]!.AsArray();
        changes.Should().NotContain(c => string.Equals(c!["column"]!.GetValue<string>(), column, StringComparison.OrdinalIgnoreCase),
            $"{column} is never a push's change (must fire)");
        changes.Should().Contain(c => c!["column"]!.GetValue<string>() == Column && c["recordId"]!.GetValue<string>() == ChildA.ToString("D"),
            "the ordinary column's change is listed (must not fire)");
        world.Host.Writes.Should().BeEmpty("a preview writes nothing");
        FieldMappingPushAuthorizationContractTests.ShouldNameTheRefusedTarget(body, column);
    }
}
