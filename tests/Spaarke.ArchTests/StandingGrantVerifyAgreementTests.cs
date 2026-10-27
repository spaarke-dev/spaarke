using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// C4: provisioning's S19 (<c>SecureRecordSetupProcedure</c>) and the schema script's read-only <c>-Verify</c>
/// (<c>scripts/Set-ExternalAccessContactSecuritySchema.ps1</c>, step h: the BFF updates <c>contact.sprk_standinggrant</c>
/// only through its direct Standing Grant Administrators membership) read the SAME pinned profile names, table and column.
/// If they drift, -Verify checks a profile S19 does not manage. Neither project references the other, so this guard reads
/// both from source BY SYMBOL NAME (a C# <c>const string</c> and a PowerShell <c>$variable = '...'</c>) and asserts each pair
/// is equal, and that step h passes those variables. Enforced by: this guard (ArchTests, CI and the Stop hook).
/// </summary>
public sealed class StandingGrantVerifyAgreementTests
{
    private const string ProvisioningFile =
        "src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SecureRecordSetup/SecureRecordSetupProcedure.cs";

    private const string ScriptFile = "scripts/Set-ExternalAccessContactSecuritySchema.ps1";

    /// <summary>(script variable, provisioning constant): every name S19 and -Verify must agree on.</summary>
    public static readonly TheoryData<string, string> Pairs = new()
    {
        { "StandingGrantProfileName", "StandingGrantProfileName" },
        { "SystemAdministratorProfileName", "SystemAdministratorProfileName" },
        { "ReaderProfileName", "ReaderProfileName" },
        { "WriterProfileName", "WriterProfileName" },
        { "LinkReaderProfileName", "IdentityLinkReaderProfileName" },
        { "LinkWriterProfileName", "IdentityLinkWriterProfileName" },
        { "Table", "StandingGrantTable" },
        { "StandingGrantColumn", "StandingGrantColumn" },
    };

    private static readonly string RepoRoot = SourceScan.RepoRoot;

    [Theory(DisplayName = "C4: -Verify reads the same pinned name as S19, found by symbol name")]
    [MemberData(nameof(Pairs))]
    public void TheScriptsName_EqualsS19s(string scriptVariable, string provisioningConstant)
    {
        var script = ScriptValueOf(Read(ScriptFile), scriptVariable, ScriptFile);
        var provisioning = ConstValueOf(Read(ProvisioningFile), provisioningConstant, ProvisioningFile);

        Assert.True(string.Equals(script, provisioning, StringComparison.Ordinal),
            $"{ScriptFile} ${scriptVariable} = '{script}' but {ProvisioningFile} {provisioningConstant} = \"{provisioning}\". "
            + "S19 and -Verify must check the same profile and column. Change both together.");
    }

    [Fact(DisplayName = "C4: -Verify's standing-grant step passes the pinned variables, not literals")]
    public void TheStandingGrantStep_UsesThePinnedVariables()
    {
        var script = Read(ScriptFile);
        Assert.Matches(new Regex(@"Get-StandingGrantWriterQuery\s+-SystemUserId\s+\S+\s+-Table\s+\$Table\s+-Column\s+\$StandingGrantColumn\b"), script);
        Assert.Matches(new Regex(@"-StandingGrantProfileName\s+\$StandingGrantProfileName\b"), script);
        Assert.Matches(new Regex(@"\$sgaId\s*=\s*\$profileIds\[\$StandingGrantProfileName\]"), script);
    }

    [Fact(DisplayName = "C4 controls: the agreement fires on a drifted, missing or doubled name and passes a match")]
    public void Control_TheAgreementFiresOnDriftAndPassesAMatch()
    {
        const string provisioning = """
            public sealed class SecureRecordSetupProcedure
            {
                public const string StandingGrantProfileName = "Standing Grant Administrators";
            }
            """;
        const string matching = "$StandingGrantProfileName = 'Standing Grant Administrators'\n";
        const string drifted = "$StandingGrantProfileName = 'Standing Grant Admins'\n";
        const string commentedOnly = "# $StandingGrantProfileName = 'Standing Grant Administrators'\n";
        const string doubled = matching + drifted;

        var expected = ConstValueOf(provisioning, "StandingGrantProfileName", "provisioning");

        // Must not fire: the same literal.
        Assert.Equal(expected, ScriptValueOf(matching, "StandingGrantProfileName", "matching"));

        // Must fire: a drifted literal differs.
        Assert.NotEqual(expected, ScriptValueOf(drifted, "StandingGrantProfileName", "drifted"));

        // Must fire: a name only in a comment (renamed away) is not silently read.
        Assert.Throws<InvalidOperationException>(() => ScriptValueOf(commentedOnly, "StandingGrantProfileName", "commented"));

        // Must fire: two assignments are ambiguous, not "first wins".
        Assert.Throws<InvalidOperationException>(() => ScriptValueOf(doubled, "StandingGrantProfileName", "doubled"));

        // Must fire: a missing provisioning constant.
        Assert.Throws<InvalidOperationException>(() => ConstValueOf(provisioning, "StandingGrantColumn", "provisioning"));
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>The value of the ONE top-level <c>$name = '...'</c> assignment (comment lines skipped).</summary>
    private static string ScriptValueOf(string source, string name, string where)
    {
        var code = string.Join('\n', source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Where(line => !line.TrimStart().StartsWith('#')));
        var matches = Regex.Matches(code, @"(?m)^\$" + Regex.Escape(name) + @"\s*=\s*'(?<value>(?:[^']|'')*)'\s*$");
        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                $"{where}: expected exactly one top-level '${name} = '...'' assignment, found {matches.Count}. The agreement "
                + "finds the name by this variable; keep the variable name.");
        }

        return matches[0].Groups["value"].Value.Replace("''", "'", StringComparison.Ordinal);
    }

    /// <summary>The value of the ONE <c>const string name = "...";</c> declaration (line comments skipped).</summary>
    private static string ConstValueOf(string source, string name, string where)
    {
        var code = string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
        var matches = Regex.Matches(code, @"\bconst\s+string\s+" + Regex.Escape(name) + @"\s*=\s*""(?<value>(?:[^""\\]|\\.)*)""\s*;");
        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                $"{where}: expected exactly one 'const string {name} = \"...\";' declaration, found {matches.Count}.");
        }

        return Regex.Unescape(matches[0].Groups["value"].Value);
    }
}
