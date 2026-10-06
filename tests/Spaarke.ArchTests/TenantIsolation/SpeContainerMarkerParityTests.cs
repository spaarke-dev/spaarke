using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests.TenantIsolation;

/// <summary>
/// Tasks 227d/227e: the SPE ownership marker has ONE name across two deployables. The BFF recognises a container as its
/// customer's by the custom property <c>SpeContainerOwnershipGuard.MarkerPropertyName</c>; L2's H8 writes it on the
/// customer's container. A rename on one side alone would leave every H8 container unreachable to its own BFF (404
/// <c>spe_container_not_owned</c>). So the name is spelled as a literal in exactly one <c>.cs</c> file under <c>src/</c> —
/// the source-linked contract <c>src/server/shared/Contracts/SpeContainerCustomerMarker.cs</c> — and both projects compile
/// that file (the mechanism of <c>SpeContainerBusinessUnitBinding.cs</c>, pinned by
/// <c>SpeAdminContainerBindingGuardTests</c>).
/// </summary>
public class SpeContainerMarkerParityTests
{
    private const string Contract = "src/server/shared/Contracts/SpeContainerCustomerMarker.cs";

    private static readonly string[] LinkingProjects =
    [
        "src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj",
        "src/server/services/Sprk.Provisioning.ControlPlane.Core/Sprk.Provisioning.ControlPlane.Core.csproj",
    ];

    [Fact]
    public void MarkerName_IsSpelledOnlyInTheSharedContract()
    {
        var literal = $"\"{ContractValue()}\"";
        var spelledIn = Directory.EnumerateFiles(Path.Combine(SourceScan.RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildOutput(f))
            .Where(f => File.ReadAllText(f).Contains(literal, StringComparison.Ordinal))
            .Select(f => SourceScan.Relative(f).Replace(Path.DirectorySeparatorChar, '/'))
            .ToList();

        Assert.True(spelledIn.Count == 1 && spelledIn[0] == Contract,
            $"The SPE ownership marker {literal} must be spelled only in {Contract}; found in: {string.Join(", ", spelledIn)}. " +
            "Reference Spaarke.Contracts.Spe.SpeContainerCustomerMarker.PropertyName instead.");
    }

    [Fact]
    public void BothDeployables_CompileTheSharedContract()
    {
        foreach (var project in LinkingProjects)
        {
            var text = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, project));
            Assert.True(text.Contains(@"shared\Contracts\SpeContainerCustomerMarker.cs", StringComparison.Ordinal),
                $"{project} must <Compile Include> {Contract}: the BFF and H8 must use the same marker name.");
        }
    }

    [Fact]
    public void ContractValue_ReadsTheConstant_PositiveControl()
        => Assert.Equal("x-y", ConstantValueIn("public const string PropertyName = \"x-y\";"));

    private static string ContractValue()
    {
        var value = ConstantValueIn(File.ReadAllText(Path.Combine(SourceScan.RepoRoot, Contract)));
        Assert.True(value is not null, $"{Contract} no longer declares `const string PropertyName = \"...\"`.");
        return value!;
    }

    private static string? ConstantValueIn(string text)
    {
        var match = Regex.Match(text, @"const\s+string\s+PropertyName\s*=\s*""(?<value>[^""]+)""");
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static bool IsBuildOutput(string path)
    {
        var p = path.Replace('\\', '/');
        return p.Contains("/bin/", StringComparison.Ordinal) || p.Contains("/obj/", StringComparison.Ordinal);
    }
}
