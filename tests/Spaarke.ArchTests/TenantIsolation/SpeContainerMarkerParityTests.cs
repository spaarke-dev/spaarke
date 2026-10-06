using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests.TenantIsolation;

/// <summary>
/// Task 227e: the SPE ownership marker has one name in two assemblies. The BFF recognises a container as its customer's
/// by the custom property <c>SpeContainerOwnershipGuard.MarkerPropertyName</c> (T227d); L2's H8 writes that property on
/// the customer's container as <c>SpeContainerMarker.PropertyName</c>. A rename on either side alone would leave every
/// H8 container unreachable to its own BFF (a refusal, 404 <c>spe_container_not_owned</c>) — so the two constants are
/// read from source and must agree.
/// </summary>
public class SpeContainerMarkerParityTests
{
    private const string BffGuard = "src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeContainerOwnershipGuard.cs";
    private const string L2Contract = "src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SpeContainer/ISpeContainerProvisioner.cs";

    [Fact]
    public void MarkerPropertyName_IsTheSameInTheBffAndInL2()
    {
        var bff = ConstantValue(BffGuard, "MarkerPropertyName");
        var l2 = ConstantValue(L2Contract, "PropertyName");

        Assert.True(bff == l2,
            $"The SPE ownership marker differs: BFF {BffGuard} MarkerPropertyName = \"{bff}\", L2 {L2Contract} " +
            $"SpeContainerMarker.PropertyName = \"{l2}\". H8 must write the property the BFF reads.");
    }

    [Fact]
    public void ConstantValue_ReadsAStringConstant_PositiveControl()
    {
        Assert.Equal("x-y", ConstantValueIn("public const string PropertyName = \"x-y\";", "PropertyName"));
        Assert.Null(ConstantValueIn("public static string PropertyName => Compute();", "PropertyName"));
    }

    private static string ConstantValue(string relativePath, string name)
    {
        var text = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, relativePath));
        var value = ConstantValueIn(text, name);
        Assert.True(value is not null, $"{relativePath} no longer declares `const string {name} = \"...\"`.");
        return value!;
    }

    private static string? ConstantValueIn(string text, string name)
    {
        var match = Regex.Match(text, $@"const\s+string\s+{Regex.Escape(name)}\s*=\s*""(?<value>[^""]+)""");
        return match.Success ? match.Groups["value"].Value : null;
    }
}
