using System.Text.Json;
using FluentAssertions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping;

/// <summary>
/// field-mapping-server-write-path-r1 task 005 (spec FR-05d) — every rule read returns <see cref="FieldMappingRuleEntity.SyncMode"/>
/// from <c>sprk_syncmode</c>: One-time (0) or Manual Refresh (1), with an empty column reading as One-time. /push (task 040)
/// selects the rules it re-applies by this value, so a rule that reads as 0 when stored as 1 is silently skipped.
/// </summary>
/// <remarks>
/// Seam: <see cref="DataverseWebApiService.MapToFieldMappingRuleEntity"/> is pure column-to-property mapping and internal
/// (InternalsVisibleTo, ADR-038 Amendment A2); the HTTP transport cannot be doubled (B1). Both rule reads (the by-profile
/// query and the profile-with-rules expand) go through it. KEEP path: tests/unit/domain/** (ADR-038 §2).
/// </remarks>
public class FieldMappingRuleSyncModeMappingTests
{
    private static FieldMappingRuleEntity Map(string json) =>
        DataverseWebApiService.MapToFieldMappingRuleEntity(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!);

    [Fact]
    public void Map_SyncModeManualRefresh_ReadsAsManualRefresh()
    {
        Map("""{"sprk_fieldmappingruleid":"00000005-0000-4000-8000-000000000001","sprk_syncmode":1}""")
            .SyncMode.Should().Be(SyncMode.ManualRefresh);
    }

    [Fact]
    public void Map_SyncModeOneTime_ReadsAsOneTime()
    {
        Map("""{"sprk_fieldmappingruleid":"00000005-0000-4000-8000-000000000002","sprk_syncmode":0}""")
            .SyncMode.Should().Be(SyncMode.OneTime);
    }

    [Theory]
    [InlineData("""{"sprk_fieldmappingruleid":"00000005-0000-4000-8000-000000000003","sprk_syncmode":null}""")]
    [InlineData("""{"sprk_fieldmappingruleid":"00000005-0000-4000-8000-000000000004"}""")]
    public void Map_SyncModeNullOrAbsent_ReadsAsOneTime(string json)
    {
        Map(json).SyncMode.Should().Be(SyncMode.OneTime);
    }

    [Fact]
    public void RuleSelect_IncludesSyncMode_SoEveryRuleReadCarriesIt()
    {
        FieldMappingColumns.RuleSelect.Split(',').Should().Contain("sprk_syncmode");
    }
}
