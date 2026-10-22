using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping;

/// <summary>
/// field-mapping-server-write-path-r1 task 003 (spec FR-03) — <see cref="MappingSourceResolver"/>: explicit, inferred from the
/// ADR-024 regarding pair, inferred from the single typed parent lookup, ambiguous (no source plus a warning) and none
/// (silent). Pure: the record-type catalog is an in-memory map handed in as the resolver's delegate.
/// </summary>
public class MappingSourceResolverTests
{
    private const string Event = "sprk_event";
    private const string PairId = "sprk_regardingrecordid";
    private const string PairType = "sprk_regardingrecordtype";

    private static readonly Guid MatterId = Guid.Parse("00000003-0000-4000-8000-000000000001");
    private static readonly Guid ProjectId = Guid.Parse("00000003-0000-4000-8000-000000000002");
    private static readonly Guid AccountId = Guid.Parse("00000003-0000-4000-8000-000000000003");
    private static readonly Guid MatterTypeRef = Guid.Parse("00000003-0000-4000-8000-0000000000a1");
    private static readonly Guid UnknownTypeRef = Guid.Parse("00000003-0000-4000-8000-0000000000a9");

    private int _catalogReads;

    private Task<string?> Catalog(Guid typeId, CancellationToken ct)
    {
        _catalogReads++;
        return Task.FromResult(typeId == MatterTypeRef ? "sprk_matter" : null);
    }

    private static KeyValuePair<string, object> Lookup(string column, string table, Guid id) =>
        new(column, new EntityReference(table, id));

    private static KeyValuePair<string, object> Field(string column, object value) => new(column, value);

    private Task<MappingSourceResolution> Resolve(
        IEnumerable<KeyValuePair<string, object>> fields, string? explicitEntity = null, Guid? explicitId = null, string table = Event)
        => MappingSourceResolver.ResolveAsync(table, explicitEntity, explicitId, fields, Catalog, CancellationToken.None);

    [Fact]
    public async Task ResolveAsync_ExplicitSourceNamed_WinsOverPairAndTypedParentWithoutReadingTheCatalog()
    {
        var result = await Resolve(
            [Field(PairId, MatterId.ToString()), Lookup(PairType, "sprk_recordtype_ref", MatterTypeRef), Lookup("sprk_project", "sprk_project", ProjectId)],
            explicitEntity: "  Account ", explicitId: AccountId);

        result.Should().Be(new MappingSourceResolution(new MappingSource("account", AccountId), MappingSourceOrigin.Explicit, null));
        _catalogReads.Should().Be(0);
    }

    [Theory]
    [InlineData("account", false)]
    [InlineData(null, true)]
    [InlineData("not a table!", true)]
    public async Task ResolveAsync_ExplicitSourceHalfNamedOrMalformed_IsUndeterminedAndInfersNothing(string? entity, bool namesId)
    {
        var result = await Resolve(
            [Lookup("sprk_matter", "sprk_matter", MatterId)],
            explicitEntity: entity, explicitId: namesId ? AccountId : null);

        result.Should().Be(new MappingSourceResolution(null, MappingSourceOrigin.Undetermined, MappingSourceResolver.SourceUndeterminedWarning));
    }

    [Fact]
    public async Task ResolveAsync_TypedPairNamed_ResolvesItsTableFromTheCatalogAndWinsOverTheTypedParent()
    {
        var result = await Resolve(
            [Field(PairId, $" {MatterId} "), Lookup(PairType, "sprk_recordtype_ref", MatterTypeRef), Lookup("sprk_project", "sprk_project", ProjectId)]);

        result.Should().Be(new MappingSourceResolution(new MappingSource("sprk_matter", MatterId), MappingSourceOrigin.RegardingPair, null));
    }

    [Fact]
    public async Task ResolveAsync_UntypedPairAgreeingWithATypedParent_ResolvesToThatParent()
    {
        var result = await Resolve(
            [Field(PairId, ProjectId.ToString()), Lookup("sprk_project", "sprk_project", ProjectId), Lookup("sprk_matter", "sprk_matter", MatterId)]);

        result.Should().Be(new MappingSourceResolution(new MappingSource("sprk_project", ProjectId), MappingSourceOrigin.RegardingPair, null));
    }

    [Fact]
    public async Task ResolveAsync_UntypedPairNamingNoTypedParent_IsUndeterminedAndDoesNotFallThroughToTheTypedParent()
    {
        var result = await Resolve([Field(PairId, AccountId.ToString()), Lookup("sprk_matter", "sprk_matter", MatterId)]);

        result.Should().Be(new MappingSourceResolution(null, MappingSourceOrigin.Undetermined, MappingSourceResolver.SourceUndeterminedWarning));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task ResolveAsync_PairIdThatIsNotARecordId_IsUndetermined(string pairId)
    {
        var result = await Resolve([Field(PairId, pairId), Lookup("sprk_matter", "sprk_matter", MatterId)]);

        result.Origin.Should().Be(MappingSourceOrigin.Undetermined);
        result.Source.Should().BeNull();
        result.Warning.Should().Be(MappingSourceResolver.SourceUndeterminedWarning);
    }

    [Fact]
    public async Task ResolveAsync_PairTypeTheCatalogDoesNotName_IsUndetermined()
    {
        var result = await Resolve([Field(PairId, MatterId.ToString()), Lookup(PairType, "sprk_recordtype_ref", UnknownTypeRef)]);

        result.Should().Be(new MappingSourceResolution(null, MappingSourceOrigin.Undetermined, MappingSourceResolver.SourceUndeterminedWarning));
    }

    [Fact]
    public async Task ResolveAsync_CatalogReadThrows_IsUndeterminedAndDoesNotThrow()
    {
        var result = await MappingSourceResolver.ResolveAsync(
            Event, null, null,
            [Field(PairId, MatterId.ToString()), Lookup(PairType, "sprk_recordtype_ref", MatterTypeRef)],
            (_, _) => throw new InvalidOperationException("catalog said: secret-detail"),
            CancellationToken.None);

        result.Should().Be(new MappingSourceResolution(null, MappingSourceOrigin.Undetermined, MappingSourceResolver.SourceUndeterminedWarning));
    }

    [Fact]
    public async Task ResolveAsync_SingleTypedParentLookup_IsTheSource()
    {
        var result = await Resolve(
            [Lookup("sprk_matter", "sprk_matter", MatterId), Lookup("sprk_contact", "contact", AccountId), Field("sprk_name", "x")]);

        result.Should().Be(new MappingSourceResolution(new MappingSource("sprk_matter", MatterId), MappingSourceOrigin.TypedParent, null));
    }

    [Fact]
    public async Task ResolveAsync_SameParentInTwoColumns_IsStillTheSingleTypedParent()
    {
        var result = await Resolve([Lookup("sprk_matter", "sprk_matter", MatterId), Lookup("sprk_corematter", "sprk_matter", MatterId)]);

        result.Should().Be(new MappingSourceResolution(new MappingSource("sprk_matter", MatterId), MappingSourceOrigin.TypedParent, null));
    }

    [Fact]
    public async Task ResolveAsync_TwoDistinctTypedParentsAndNoPair_IsAmbiguousWithOneWarning()
    {
        var result = await Resolve([Lookup("sprk_matter", "sprk_matter", MatterId), Lookup("sprk_project", "sprk_project", ProjectId)]);

        result.Should().Be(new MappingSourceResolution(null, MappingSourceOrigin.Undetermined, MappingSourceResolver.SourceUndeterminedWarning));
        result.Warning.Should().NotContain(MatterId.ToString()).And.NotContain(ProjectId.ToString()).And.NotContain("sprk_");
    }

    [Fact]
    public async Task ResolveAsync_NothingNamedAndNoParent_IsSilentNone()
    {
        var result = await Resolve([Lookup("sprk_contact", "contact", AccountId), Field("sprk_name", "x"), Field(PairId, "  ")]);

        result.Should().Be(new MappingSourceResolution(null, MappingSourceOrigin.None, null));
        _catalogReads.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_CitationLookupOnly_IsNoneBecauseACitationFilesNothing()
    {
        var policyId = Guid.Parse("00000003-0000-4000-8000-000000000004");

        var result = await Resolve([Lookup("sprk_policy", "sprk_policy", policyId)], table: "sprk_signal");

        result.Should().Be(new MappingSourceResolution(null, MappingSourceOrigin.None, null));
    }
}
