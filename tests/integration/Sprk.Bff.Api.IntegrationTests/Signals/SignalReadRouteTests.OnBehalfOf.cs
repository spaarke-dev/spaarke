using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Signals;

/// <summary>
/// Ontology task 223 (DI-144, #2632): the read route shows a Decision Record only when <see cref="WriterCreatedRecord"/> holds:
/// <c>createdby</c> the writer AND <c>createdonbehalfby</c> empty. A record stamped with the writer as creator but created on its behalf by
/// another identity is omitted like an unreadable one; a writer-created record (no <c>createdonbehalfby</c>) is shown as before.
/// </summary>
public sealed partial class SignalReadRouteTests
{
    [Fact]
    public async Task ADecisionRecordCreatedOnTheWritersBehalf_IsOmitted_AndTheWritersOwnIsStillShown()
    {
        var world = new World();
        var m = world.Matter(readers: [Alice]);
        var onBehalf = world.Decision(readers: [Alice], number: "DR-77777", by: "Pat Partner", createdOnBehalfBy: SomePerson);
        var genuine = world.Decision(readers: [Alice], number: "DR-00001");
        var onBehalfItem = world.Signal(Decide, core: (MatterType, m), sets: ["closedEarlier", "decided"], resolvedOn: world.Now.AddHours(-49), decisionRecord: onBehalf);
        var genuineItem = world.Signal(Decide, core: (MatterType, m), sets: ["closedEarlier", "decided"], resolvedOn: world.Now.AddHours(-49), decisionRecord: genuine);

        var earlier = AllItems(await OkAsync(world, Alice, "?set=closedEarlier")).ToDictionary(i => i.GetProperty("signalId").GetGuid());
        earlier[onBehalfItem].GetProperty("decision").ValueKind.Should().Be(JsonValueKind.Null, "the item stays listed with no decision");
        earlier[genuineItem].GetProperty("decision").GetProperty("number").GetString().Should().Be("DR-00001");

        var decided = await OkAsync(world, Alice, "?set=decided");
        Ids(decided).Should().Equal([genuineItem], "the decided set drops it");
        decided.ToString().Should().NotContain("DR-77777").And.NotContain("Pat Partner");

        var fetch = Uri.UnescapeDataString(world.Requests.First(r => r.StartsWith("sprk_decisionrecords?", StringComparison.Ordinal))).Replace('\'', '"');
        fetch.Should().Contain($"<condition attribute=\"createdby\" operator=\"eq\" value=\"{WriterUser:D}\" />")
            .And.Contain("<condition attribute=\"createdonbehalfby\" operator=\"null\" />");
    }
}
