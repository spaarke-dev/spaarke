using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.FieldMappings;

/// <summary>
/// field-mapping-server-write-path-r1 DI-053 (#3326): a column only its typed writers may write is never a field-mapping target,
/// on any surface. The push refuses a rule aimed at one at rule selection (<see cref="PushRuleSelection.TypedWriterTargetRefusal"/>,
/// the app-only predicate the create's mapping and Repair ask), before anything is read or written: an ordinary push, family mode,
/// a preview and an apply. The refused rule is counted in <c>skippedRuleCount</c> and named by its target column in a warning;
/// the rest of the push proceeds as the caller.
/// <para>The caller here holds Read and Write on the source and on every child, and the host's Dataverse double accepts every
/// column of every PATCH (Update and field-security update granted): the refusal does not depend on the caller's privileges.</para>
/// </summary>
public partial class FieldMappingPushAuthorizationContractTests
{
    /// <summary>The ordinary invoice column of the same push (must not fire).</summary>
    private const string OrdinaryInvoiceColumn = "sprk_clientreference";

    private static FieldMappingRuleEntity[] OrdinaryRuleAnd(string typedWriterColumn) =>
    [
        PushHost.Rule("sprk_clientreference", 0, typedWriterColumn, 0),
        PushHost.Rule("sprk_clientreference", 0, OrdinaryInvoiceColumn, 0),
    ];

    private async Task<PushHost> InvoicePushHostAsync(FieldMappingRuleEntity[] rules)
    {
        var host = await PushHost.StartAsync();
        host.Probe.Rights = AccessRights.Read | AccessRights.Write;
        host.ArrangeHappyPathData(targetEntity: "sprk_invoice", targetSet: "sprk_invoices", lookupsToSource: ["sprk_matter"], rules: rules);
        return host;
    }

    internal static void ShouldNameTheRefusedTarget(JsonNode body, string column, int refusedRules = 1)
    {
        body["skippedRuleCount"]!.GetValue<int>().Should().Be(refusedRules, "each refused rule is counted with the skipped rules");
        body["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).Should().Contain(w =>
            w.Contains($"\"{column}\"", StringComparison.Ordinal) && w.Contains("managed by the server", StringComparison.Ordinal),
            "the refused rule is named by its target column");
        body["warnings"]!.AsArray().Select(w => w!.GetValue<string>()).Should().NotContain(w => w.Contains("REF-166", StringComparison.Ordinal),
            "a warning names the column, never a value");
    }

    /// <summary>
    /// Must fire (DI-053): an ordinary push from a matter onto its invoices, with a rule aimed at the invoice's firm hold, firm or
    /// billed total: nothing for that column reaches any PATCH, though the caller may write it. Must not fire: the ordinary
    /// column of the same push is still written, as the caller. MUTATION: removing the refusal in
    /// <see cref="PushRuleSelection.Select"/> puts the column in the payload.
    /// </summary>
    [Theory]
    [InlineData("sprk_firmhold")]
    [InlineData("sprk_vendororg")]
    [InlineData("sprk_totalamount")]
    public async Task Push_MatterToInvoice_ARuleOntoATypedWriterColumn_IsRefused_AndTheOrdinaryColumnIsWrittenAsTheCaller(string column)
    {
        await using var host = await InvoicePushHostAsync(OrdinaryRuleAnd(column));

        var response = await host.SendAsync(Authenticated(PushBody(targetEntity: "sprk_invoice")));
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        host.UpdateOnlyWrites.Should().HaveCount(2).And.OnlyContain(w => w.Table == "sprk_invoice" && w.ImpersonatedAs == CallerSystemUserId,
            "the ordinary column is still written to each invoice, as the caller (must not fire)");
        host.Writes.Should().AllSatisfy(w => w.Payload.Should().BeEquivalentTo(
            new Dictionary<string, object?> { [OrdinaryInvoiceColumn] = "REF-166" },
            $"{column} is written only by its typed writers, never by a field-mapping rule (must fire)"));
        var body = JsonNode.Parse(text)!;
        body["updatedCount"]!.GetValue<int>().Should().Be(2);
        ShouldNameTheRefusedTarget(body, column);
    }

    /// <summary>
    /// Must fire (DI-053): all three in one push are each refused and named, and with only refused rules left the push answers
    /// that no rule is pushed, reading and writing no child.
    /// </summary>
    [Fact]
    public async Task Push_MatterToInvoice_OnlyTypedWriterTargets_RefusesEach_AndReadsNoChild()
    {
        string[] columns = ["sprk_firmhold", "sprk_vendororg", "sprk_totalamount"];
        await using var host = await InvoicePushHostAsync([.. columns.Select(c => PushHost.Rule("sprk_clientreference", 0, c, 0))]);

        var response = await host.SendAsync(Authenticated(PushBody(targetEntity: "sprk_invoice")));
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        host.Writes.Should().BeEmpty();
        host.ChildQueries.Should().BeEmpty("the refusal is decided before any child is read");
        host.SourceReads.Should().BeEmpty("and before the source is read");
        var body = JsonNode.Parse(text)!;
        foreach (var column in columns)
            ShouldNameTheRefusedTarget(body, column, refusedRules: 3);
        text.Should().NotContain("None of this profile's rules is set to be pushed",
            "every rule was set to push; the refusal warnings say why none was applied");
    }

    /// <summary>The predicate is the one the create's mapping asks for a mapping target (no list of its own).</summary>
    [Theory]
    [InlineData("sprk_invoice", "sprk_firmhold")]
    [InlineData("sprk_invoice", " SPRK_VendorOrg ")]
    [InlineData("sprk_invoice", "sprk_totalamount")]
    public void TypedWriterTargetRefusal_IsTheAppOnlyRefusal(string table, string column)
    {
        var refused = InheritedAccessPermission.RefuseAppOnlyWrite(table, [column], InheritedAccessPermission.WriteIdentity.AppOnly);
        refused.Should().NotBeNull();
        PushRuleSelection.TypedWriterTargetRefusal(table, column).Should().Be(refused!.RefusalCode);
    }
}
