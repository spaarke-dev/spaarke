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
/// <para>The PowerShell side has ONE constant too (<c>$SpeContainerCustomerMarkerProperty</c> in
/// <c>scripts/common/SpeContainerBinding.ps1</c>), equal to the C# one, and every script that binds a container it just
/// created passes <c>-CustomerId</c> — a container a script makes is otherwise unreachable to its own BFF. Containers the BFF
/// creates at runtime (secure-record containers, admin-plane creates) are marked by <c>SpeContainerOwnershipGuard</c>.</para>
/// </summary>
public class SpeContainerMarkerParityTests
{
    private const string Contract = "src/server/shared/Contracts/SpeContainerCustomerMarker.cs";
    private const string ScriptModule = "scripts/common/SpeContainerBinding.ps1";
    private const string ScriptBinder = "Invoke-SpeContainerBindOrRemove";

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
    public void MarkerName_IsOneConstantInPowerShell_AndAgreesWithTheContract()
    {
        var value = ContractValue();
        var spelledIn = Scripts()
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return text.Contains($"'{value}'", StringComparison.OrdinalIgnoreCase)
                       || text.Contains($"\"{value}\"", StringComparison.OrdinalIgnoreCase);
            })
            .Select(f => SourceScan.Relative(f).Replace(Path.DirectorySeparatorChar, '/'))
            .ToList();

        Assert.True(spelledIn.Count == 1 && spelledIn[0] == ScriptModule,
            $"The SPE ownership marker '{value}' must be spelled in exactly one script, {ScriptModule}; found in: " +
            $"{string.Join(", ", spelledIn)}. Use $SpeContainerCustomerMarkerProperty instead.");

        var module = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, ScriptModule));
        var match = Regex.Match(module, @"^\s*\$SpeContainerCustomerMarkerProperty\s*=\s*'(?<value>[^']*)'", RegexOptions.Multiline);
        Assert.True(match.Success, $"{ScriptModule} no longer declares $SpeContainerCustomerMarkerProperty = '...'.");
        Assert.Equal(value, match.Groups["value"].Value);
    }

    [Fact]
    public void EveryScriptBindCall_PassesTheCustomerId()
    {
        var calls = Scripts()
            .SelectMany(f => BindCalls(File.ReadAllText(f)).Select(c => (File: SourceScan.Relative(f), Call: c)))
            .ToList();
        Assert.True(calls.Count >= 3,
            $"the scan must find the script bind calls (New-BusinessUnitContainer, Provision-Customer, Create-NewContainerType) — " +
            $"found {calls.Count}, so it would pass vacuously");

        var unmarked = calls.Where(c => !PassesCustomerId(c.Call)).Select(c => $"{c.File}: {c.Call}").ToList();
        Assert.True(unmarked.Count == 0,
            $"Every {ScriptBinder} call must pass -CustomerId (the BFF's Customer__Id) — an unmarked container is refused by " +
            $"the BFF's SpeContainerOwnershipGuard:\n  {string.Join("\n  ", unmarked)}");
    }

    [Fact]
    public void BindCallScan_FlagsACallWithoutTheCustomerId_NegativeControl()
    {
        const string script = """
            <# Invoke-SpeContainerBindOrRemove in a block comment is not a call #>
            function Invoke-SpeContainerBindOrRemove { param($Token) }
            # Invoke-SpeContainerBindOrRemove -Token $t (a line comment)
            Invoke-SpeContainerBindOrRemove -Token $t -ContainerId $c `
                -BusinessUnitId $u -CustomerId $id -GraphBase $g
            Invoke-SpeContainerBindOrRemove -Token $t -ContainerId $c `
                -BusinessUnitId $u -GraphBase $g
            """;

        var calls = BindCalls(script);
        Assert.Equal(2, calls.Count);
        Assert.True(PassesCustomerId(calls[0]));
        Assert.False(PassesCustomerId(calls[1]));
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

    private static IEnumerable<string> Scripts() =>
        new[] { "scripts", "src" }
            .Select(root => Path.Combine(SourceScan.RepoRoot, root))
            .SelectMany(root => Directory.EnumerateFiles(root, "*.ps1", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(root, "*.psm1", SearchOption.AllDirectories)))
            .Where(f => !IsBuildOutput(f) && !f.Replace('\\', '/').Contains("/node_modules/", StringComparison.Ordinal));

    /// <summary>
    /// Each <see cref="ScriptBinder"/> call as one logical line (backtick continuations joined), comments and the function's
    /// own definition excluded.
    /// </summary>
    private static List<string> BindCalls(string script)
    {
        var code = Regex.Replace(script.Replace("\r\n", "\n"), @"<#.*?#>", string.Empty, RegexOptions.Singleline);
        code = Regex.Replace(code, @"^\s*#.*$", string.Empty, RegexOptions.Multiline);
        code = Regex.Replace(code, @"`[ \t]*\n", " ");
        return Regex.Matches(code, $@"(?<!function\s+)(?<![\w-]){ScriptBinder}(?![\w-])[^\n]*")
            .Select(m => Regex.Replace(m.Value, @"\s+", " ").Trim())
            .ToList();
    }

    private static bool PassesCustomerId(string call) =>
        Regex.IsMatch(call, @"(?<![\w-])-CustomerId(?![\w-])", RegexOptions.IgnoreCase);

    private static bool IsBuildOutput(string path)
    {
        var p = path.Replace('\\', '/');
        return p.Contains("/bin/", StringComparison.Ordinal) || p.Contains("/obj/", StringComparison.Ordinal);
    }
}
