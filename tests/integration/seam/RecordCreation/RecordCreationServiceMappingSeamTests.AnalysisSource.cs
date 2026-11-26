using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 028 (review round 2): the create's mapping read gives no analysis decision, so a
/// <c>sprk_analysis</c> named as the mapping source maps nothing and is never asked about or read, whatever the caller may
/// read. Only the send-time fill, which reads an analysis through the same narrowing the analysis routes apply, maps from one.
/// </summary>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    private static readonly Guid AnalysisSource = Guid.Parse("a0140000-0000-4000-8000-00000000a028");

    /// <summary>Must fire: an explicit analysis source the caller holds Read on still maps nothing on a create.</summary>
    [Fact]
    public async Task CreatePath_AnAnalysisNamedAsTheMappingSource_MapsNothing_AndIsNeverAskedAboutOrRead()
    {
        Profile("sprk_analysis", "sprk_event", Rule("sprk_description", "sprk_description", Memo));
        _readable.Add(AnalysisSource);
        _sources[("sprk_analysis", AnalysisSource)] = new Entity("sprk_analysis", AnalysisSource) { ["sprk_description"] = "From the analysis" };

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, mappingSource: ("sprk_analysis", AnalysisSource)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _creates.Should().ContainSingle().Which.Fields.Should().NotContainKey("sprk_description");
        outcome.MappingWarnings.Should().ContainSingle().Which.Code.Should().Be(RecordCreationWarning.MappingSourceUnavailableCode);
        _probe.Asked.Should().NotContain(a => a.Id == AnalysisSource, "a route that gives no analysis decision does not even ask the caller's rights");
        _sourceReads.Should().NotContain(r => r.Table == "sprk_analysis");
    }

    /// <summary>Must not fire: a matter named as the source on the same create still maps, so the refusal is about the analysis only.</summary>
    [Fact]
    public async Task CreatePath_AMatterNamedAsTheMappingSource_StillMaps_WhileAnAnalysisDoesNot()
    {
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", OrdinaryMatter)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_description").Should().Be("From the matter");
    }
}
