using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 022 fix round 2 — the mapping-source rule's step-3 narrowing: a secure source maps
/// only into a create that is secure from creation (a) or that the caller files under that source (b); a source whose answer
/// cannot be told maps nothing. The owner check before the create (the invariant) is the seam suite's
/// (<c>RecordCreationServiceMappingSeamTests.MappingSourceRule.cs</c>).
/// </summary>
/// <remarks>The verdict and origin are internal types, so the theories name them by their member names.</remarks>
public class MappingSourceRuleTests
{
    private const string Event = "sprk_event";
    private static readonly Guid Source = Guid.Parse("02200000-0000-4000-8000-000000000001");
    private static readonly Guid Other = Guid.Parse("02200000-0000-4000-8000-000000000002");

    [Theory]
    [InlineData("Allowed", false, false, true)]
    [InlineData("Secure", true, false, true)]   // (a) secure from creation
    [InlineData("Secure", false, true, true)]   // (b) filed by the caller under the source
    [InlineData("Secure", false, false, false)] // must fire: neither
    [InlineData("Unknown", true, true, false)]  // not told: fail closed, whatever the create
    [InlineData("Unknown", false, false, false)]
    public void ASecureSource_MapsOnlyIntoACreateSecureFromCreation_OrFiledUnderIt(
        string verdict, bool secureFromCreation, bool filedUnderSource, bool maps)
    {
        RecordCreationService.SourceMapsBeforeOwner(
                Enum.Parse<MappingSourceCopyVerdict>(verdict), secureFromCreation, filedUnderSource)
            .Should().Be(maps);
    }

    [Theory]
    [InlineData("RegardingPair")]
    [InlineData("TypedParent")]
    public void ASourceResolvedFromTheCallersOwnFilingParent_IsTheFilingParent(string origin)
    {
        var resolution = new MappingSourceResolution(
            new MappingSource("sprk_matter", Source), Enum.Parse<MappingSourceOrigin>(origin), null);

        RecordCreationService.SourceIsCallersFilingParent(Event, resolution, new Entity(Event)).Should().BeTrue();
    }

    [Fact]
    public void AnExplicitSource_IsTheFilingParent_OnlyWhenTheCallersRowIsFiledUnderIt()
    {
        var resolution = new MappingSourceResolution(new MappingSource("sprk_matter", Source), MappingSourceOrigin.Explicit, null);
        var filedUnderIt = new Entity(Event) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Source) };
        var filedElsewhere = new Entity(Event) { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Other) };
        var pairNamesIt = new Entity(Event) { [SecureRootInheritance.PairIdColumn] = Source.ToString("D") };

        RecordCreationService.SourceIsCallersFilingParent(Event, resolution, filedUnderIt).Should().BeTrue();
        RecordCreationService.SourceIsCallersFilingParent(Event, resolution, pairNamesIt).Should().BeTrue();
        RecordCreationService.SourceIsCallersFilingParent(Event, resolution, filedElsewhere)
            .Should().BeFalse("a secure source the row is not filed under maps nothing into a create not secure from creation");
        RecordCreationService.SourceIsCallersFilingParent(Event, resolution, new Entity(Event))
            .Should().BeFalse("a row filed under nothing (the Project wizard's create) is not filed under the source");
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Undetermined")]
    public void NoResolvedSource_IsNoFilingParent(string origin)
    {
        RecordCreationService.SourceIsCallersFilingParent(
                Event, new MappingSourceResolution(null, Enum.Parse<MappingSourceOrigin>(origin), null), new Entity(Event))
            .Should().BeFalse();
    }
}
