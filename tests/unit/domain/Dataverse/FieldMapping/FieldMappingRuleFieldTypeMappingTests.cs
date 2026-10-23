using System.Text.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping;

/// <summary>
/// #2281: <c>sprk_targetfieldtype</c> uses the global option set <c>sprk_fieldmappingtypes</c> (100000000 + n, Currency
/// 100000007), while older rows and <c>sprk_sourcefieldtype</c> hold the bare n. Both are live on dev. A rule stored as
/// 100000001 (Lookup) read as Text made the client select the lookup by its plain name; the 400 voided every Copy rule in
/// the profile. Every rule read goes through <see cref="DataverseWebApiService.MapToFieldMappingRuleEntity"/>, so both
/// encodings must read as the same engine type there.
/// </summary>
/// <remarks>Seam and KEEP path as <see cref="FieldMappingRuleSyncModeMappingTests"/> (ADR-038 Amendment A2, §2).</remarks>
public class FieldMappingRuleFieldTypeMappingTests
{
    private static FieldMappingRuleEntity Map(string json) =>
        DataverseWebApiService.MapToFieldMappingRuleEntity(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!);

    [Theory]
    [InlineData(0, 0)]                 // legacy Text
    [InlineData(1, 1)]                 // legacy Lookup (24 of 25 dev rules store 0/1)
    [InlineData(6, 6)]                 // legacy Memo
    [InlineData(100000000, 0)]         // option-set Text
    [InlineData(100000001, 1)]         // option-set Lookup: the #2281 rule
    [InlineData(100000002, 2)]         // option-set Option Set
    [InlineData(100000006, 6)]         // option-set Memo
    [InlineData(100000007, 3)]         // option-set Currency reads as Number (Money comes from the column's metadata)
    public void Map_EitherEncoding_ReadsAsTheSameEngineType(int stored, int expected)
    {
        var rule = Map($$"""{"sprk_fieldmappingruleid":"00002281-0000-4000-8000-000000000001","sprk_sourcefieldtype":{{stored}},"sprk_targetfieldtype":{{stored}}}""");

        rule.TargetFieldType.Should().Be(expected);
        rule.SourceFieldType.Should().Be(expected);
    }

    [Fact]
    public void Map_TheOptionSetLookup_IsTheEngineLookup()
    {
        // The value the server write path and the BFF projection test against ("Lookup", CreateTimeFieldMapping.FieldTypeLookup).
        Map("""{"sprk_fieldmappingruleid":"00002281-0000-4000-8000-000000000002","sprk_targetfieldtype":100000001}""")
            .TargetFieldType.Should().Be(Sprk.Bff.Api.Services.Dataverse.FieldMapping.CreateTimeFieldMapping.FieldTypeLookup);
    }

    [Fact]
    public void Map_TypeAbsent_ReadsAsText()
    {
        var rule = Map("""{"sprk_fieldmappingruleid":"00002281-0000-4000-8000-000000000003"}""");

        rule.TargetFieldType.Should().Be(0);
        rule.SourceFieldType.Should().Be(0);
    }
}
