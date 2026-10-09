using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// Task 175 fix round (verifier F1-1): <c>scripts/Set-AccessInheritanceSchema.ps1</c> locks the column the BFF's cascade
/// reads and writes (<see cref="AccessInheritance.Column"/>) on exactly the tables that follow a parent, with task 133's
/// writer profile (the one the <c>sprk_issecure</c> lock reuses, never a second one), and checks the same three
/// maker-authored write channels task 150 checks. Read as text (no PowerShell host, no network); the parser is exercised on
/// seeded drifts.
/// </summary>
[Trait("Category", "Security")]
[Trait("status", "task-175-uac-r2")]
public class AccessInheritanceSchemaScriptAgreementTests
{
    [Fact]
    public void TheSchemaScript_LocksTheColumnTheCascadeUses_OnTheTablesThatFollowAParent_WithTheBffWriterProfile()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "Set-AccessInheritanceSchema.ps1"));

        Agrees(Analyse(script)).Should().BeTrue(
            $"the script must field-secure {AccessInheritance.Column} on the work assignment and project with the BFF writer profile");
        Analyse(script).Channels.Should().BeEquivalentTo(
            SecureFlagFieldSecurityAssertion.ConfiguredWriterChannels.Select(c => $"{c.Table}.{c.TargetColumn}"),
            "(p6) checks the same configured write channels task 150 checks for sprk_issecure");
        Regex.IsMatch(script, @"""IsSecured""\s*=\s*\$Secured").Should().BeTrue("the column is CREATED secured (no user-writable moment)");
        Regex.IsMatch(script, @"\$ReaderProfileName\s*=").Should().BeFalse("no reader profile is granted this column: only the BFF reads it");
    }

    private const string Seed = """
        $Column = 'sprk_accessinheritance'
        $Tables = @('sprk_workassignment', 'sprk_project')
        $WriterProfileName = 'Spaarke BFF-Managed Field Writers'
        $Secured = $true
        """;

    [Theory]
    [InlineData("$Secured = $true", "$Secured = $false")]
    [InlineData(", 'sprk_project'", "")]
    [InlineData("'sprk_workassignment', ", "'sprk_workassignment', 'sprk_matter', ")]
    [InlineData("$Column = 'sprk_accessinheritance'", "$Column = 'sprk_accesspermission'")]
    [InlineData("Field Writers'", "Field Readers'")]
    public void TheParser_SeesEachDrift(string from, string to)
    {
        Agrees(Analyse(Seed)).Should().BeTrue("the faithful seed agrees");
        Agrees(Analyse(Seed.Replace(from, to))).Should().BeFalse("each seeded drift must fail the agreement test");
    }

    private sealed record ScriptShape(string? Column, IReadOnlyList<string> Tables, string? Writer, bool Secured, IReadOnlyList<string> Channels);

    private static bool Agrees(ScriptShape s) =>
        s.Column == AccessInheritance.Column
        && s.Tables.Count == 2
        && s.Tables.All(SecureRootInheritance.Inherits)
        && s.Tables.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2
        && s.Writer == SecureFlagFieldSecurityAssertion.WriterProfileName
        && s.Secured;

    private static ScriptShape Analyse(string script)
    {
        static string? Single(string text, string pattern) =>
            Regex.Match(text, pattern, RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

        var tablesLine = Single(script, @"^\s*\$Tables\s*=\s*@\(([^)]*)\)");
        var tables = tablesLine is null
            ? Array.Empty<string>()
            : Regex.Matches(tablesLine, @"'([^']+)'").Select(m => m.Groups[1].Value).ToArray();
        var channels = Regex.Matches(script, @"@\{\s*T\s*=\s*'([^']+)';\s*C\s*=\s*'([^']+)'\s*\}")
            .Select(m => $"{m.Groups[1].Value}.{m.Groups[2].Value}").ToArray();

        return new ScriptShape(
            Single(script, @"^\s*\$Column\s*=\s*'([^']+)'"),
            tables,
            Single(script, @"^\s*\$WriterProfileName\s*=\s*'([^']+)'"),
            string.Equals(Single(script, @"^\s*\$Secured\s*=\s*\$(\w+)"), "true", StringComparison.OrdinalIgnoreCase),
            channels);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
