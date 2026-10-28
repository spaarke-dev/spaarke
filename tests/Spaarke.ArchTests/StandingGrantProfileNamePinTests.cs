using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// The BFF decides who is an external-access administrator by membership of ONE field security profile, which it finds by
/// name (<c>ExternalAccessAdministratorAuthority.StandingGrantProfileName</c>). Provisioning creates that profile under its
/// own constant (<c>SecureRecordSetupProcedure.StandingGrantProfileName</c>). The two must be the same string: if they
/// drift, the BFF finds no profile and refuses every administrator (fail closed, but the remove and reinstate routes stop
/// working). The BFF does not reference the provisioning project, so this guard reads both constants from source BY
/// SYMBOL NAME and asserts they are equal. Enforced by: this guard (ArchTests, CI and the Stop hook).
/// </summary>
public sealed class StandingGrantProfileNamePinTests
{
    private const string ProvisioningFile =
        "src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SecureRecordSetup/SecureRecordSetupProcedure.cs";

    private const string BffFile =
        "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalAccessAdministratorAuthority.cs";

    private const string SymbolName = "StandingGrantProfileName";

    private static readonly string RepoRoot = FindRepoRoot();

    // A const string declaration of the named symbol: `... const string StandingGrantProfileName = "...";`
    private static readonly Regex ConstDeclaration = new(
        @"\bconst\s+string\s+" + SymbolName + @"\s*=\s*""(?<value>(?:[^""\\]|\\.)*)""\s*;",
        RegexOptions.Compiled);

    [Fact(DisplayName = "Slice C3: the BFF's standing-grant profile name equals provisioning's, found by symbol name")]
    public void TheBffProfileName_EqualsProvisioningsProfileName()
    {
        var provisioning = ConstValueOf(File.ReadAllText(Path.Combine(RepoRoot, ProvisioningFile)), ProvisioningFile);
        var bff = ConstValueOf(File.ReadAllText(Path.Combine(RepoRoot, BffFile)), BffFile);

        Assert.True(
            string.Equals(provisioning, bff, StringComparison.Ordinal),
            $"{BffFile} {SymbolName} = \"{bff}\" but {ProvisioningFile} {SymbolName} = \"{provisioning}\". The BFF finds the "
            + "administrators profile by this name, so a drift refuses every administrator. Change both together.");
    }

    [Fact(DisplayName = "Slice C3 controls: the profile-name pin fires on a drifted literal and passes a matching one")]
    public void Control_ThePinFiresOnDriftAndPassesAMatch()
    {
        const string provisioning = """
            public sealed class SecureRecordSetupProcedure
            {
                public const string StandingGrantProfileName = "Standing Grant Administrators";
            }
            """;
        const string matching = """
            public sealed class ExternalAccessAdministratorAuthority
            {
                public const string StandingGrantProfileName = "Standing Grant Administrators";
            }
            """;
        const string drifted = """
            public sealed class ExternalAccessAdministratorAuthority
            {
                public const string StandingGrantProfileName = "Standing Grant Admins";
            }
            """;
        const string missing = """
            public sealed class ExternalAccessAdministratorAuthority
            {
                public const string SomeOtherName = "Standing Grant Administrators";
            }
            """;

        var expected = ConstValueOf(provisioning, "provisioning");

        // Must not fire: the same literal.
        Assert.Equal(expected, ConstValueOf(matching, "matching"));

        // Must fire: a drifted literal differs.
        Assert.NotEqual(expected, ConstValueOf(drifted, "drifted"));

        // Must fire: the symbol renamed away is not silently skipped.
        Assert.Throws<InvalidOperationException>(() => ConstValueOf(missing, "missing"));

        // Must fire: the symbol declared twice is ambiguous, not "first wins".
        Assert.Throws<InvalidOperationException>(() => ConstValueOf(provisioning + "\n" + drifted, "twice"));
    }

    private static string ConstValueOf(string source, string where)
    {
        var code = StripLineComments(source);
        var matches = ConstDeclaration.Matches(code);
        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                $"{where}: expected exactly one 'const string {SymbolName} = \"...\";' declaration, found {matches.Count}. "
                + "The pin finds the profile name by this symbol; keep the symbol name in both projects.");
        }

        return Regex.Unescape(matches[0].Groups["value"].Value);
    }

    private static string StripLineComments(string source)
        => string.Join('\n', source.Split('\n').Select(line =>
        {
            var trimmed = line.TrimStart();
            return trimmed.StartsWith("//", StringComparison.Ordinal) ? string.Empty : line;
        }));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
               ?? throw new InvalidOperationException(
                   "Could not locate the repository root from the test output directory.");
    }
}
