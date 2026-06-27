using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Registration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Registration;

/// <summary>
/// Unit tests for FR-11 (per-environment license SKUs) and FR-12 (malformed
/// license JSON rejected) in the environment provisioning flow.
/// </summary>
/// <remarks>
/// Covers:
/// <list type="bullet">
///   <item><see cref="GraphUserService.ResolveLicenses"/> fallback rule: per-environment wins only when it has at least one SKU</item>
///   <item><see cref="DataverseEnvironmentRecord.ParseLicenseConfig"/>: valid JSON, malformed JSON throws, empty returns null</item>
/// </list>
/// </remarks>
public class LicenseResolutionTests
{
    private static LicenseSkuConfig GlobalLicenses() => new()
    {
        PowerAppsPlan2TrialSkuId = "11111111-1111-1111-1111-111111111111",
        FabricFreeSkuId = "22222222-2222-2222-2222-222222222222",
        PowerAutomateFreeSkuId = "33333333-3333-3333-3333-333333333333"
    };

    [Fact]
    public void ResolveLicenses_NullPerEnvironment_ReturnsGlobal()
    {
        var global = GlobalLicenses();

        var result = GraphUserService.ResolveLicenses(global, null);

        result.Should().BeSameAs(global);
    }

    [Fact]
    public void ResolveLicenses_AllEmptyPerEnvironment_FallsBackToGlobal()
    {
        var global = GlobalLicenses();
        var perEnv = new LicenseSkuConfig
        {
            PowerAppsPlan2TrialSkuId = "",
            FabricFreeSkuId = "",
            PowerAutomateFreeSkuId = ""
        };

        var result = GraphUserService.ResolveLicenses(global, perEnv);

        result.Should().BeSameAs(global);
    }

    [Fact]
    public void ResolveLicenses_PerEnvironmentWithAnySku_WinsOverGlobal()
    {
        var global = GlobalLicenses();
        var perEnv = new LicenseSkuConfig
        {
            PowerAppsPlan2TrialSkuId = "44444444-4444-4444-4444-444444444444",
            FabricFreeSkuId = "",
            PowerAutomateFreeSkuId = ""
        };

        var result = GraphUserService.ResolveLicenses(global, perEnv);

        result.Should().BeSameAs(perEnv);
    }

    [Fact]
    public void ParseLicenseConfig_ValidJson_ReturnsTypedConfig()
    {
        var record = new DataverseEnvironmentRecord
        {
            LicenseConfigJson =
                "{\"PowerAppsPlan2TrialSkuId\":\"44444444-4444-4444-4444-444444444444\"," +
                "\"FabricFreeSkuId\":\"55555555-5555-5555-5555-555555555555\"," +
                "\"PowerAutomateFreeSkuId\":\"66666666-6666-6666-6666-666666666666\"}"
        };

        var config = record.ParseLicenseConfig();

        config.Should().NotBeNull();
        config!.PowerAppsPlan2TrialSkuId.Should().Be("44444444-4444-4444-4444-444444444444");
        config.FabricFreeSkuId.Should().Be("55555555-5555-5555-5555-555555555555");
        config.PowerAutomateFreeSkuId.Should().Be("66666666-6666-6666-6666-666666666666");
    }

    [Fact]
    public void ParseLicenseConfig_MalformedJson_ThrowsJsonException()
    {
        var record = new DataverseEnvironmentRecord
        {
            LicenseConfigJson = "{not valid json"
        };

        var act = () => record.ParseLicenseConfig();

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void ParseLicenseConfig_EmptyOrWhitespace_ReturnsNull()
    {
        new DataverseEnvironmentRecord { LicenseConfigJson = null }.ParseLicenseConfig().Should().BeNull();
        new DataverseEnvironmentRecord { LicenseConfigJson = "" }.ParseLicenseConfig().Should().BeNull();
        new DataverseEnvironmentRecord { LicenseConfigJson = "   " }.ParseLicenseConfig().Should().BeNull();
    }
}
