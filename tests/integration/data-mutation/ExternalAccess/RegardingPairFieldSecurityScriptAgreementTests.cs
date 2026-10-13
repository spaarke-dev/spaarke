using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// #1616: the BFF treats a project's ADR-024 pair as BFF-MANAGED (<see cref="SecureRootInheritance.BffManagedPairTables"/>: the
/// readers honour it on its own, the chat create exempts it from the "no field-secured column" rule, the update tool writes
/// it as the application). That is safe only while the pair is field-secured so that ONLY the BFF can write it. Three
/// artifacts must agree with the code: <c>scripts/Set-RegardingPairFieldSecurity.ps1</c> (what an operator applies), and the
/// SpaarkeMaster solution's <c>sprk_Project/Entity.xml</c> (IsSecured) and <c>Other/FieldSecurityProfiles.xml</c> (task 133's
/// reader read-only, writer read/create/update). Read as text (no PowerShell host, no network); the parser is exercised on
/// seeded drifts.
/// </summary>
[Trait("Category", "Security")]
[Trait("status", "issue-1616-uac-r2")]
public class RegardingPairFieldSecurityScriptAgreementTests
{
    private static readonly string[] PairColumns = { SecureRootInheritance.PairIdColumn, SecureRootInheritance.PairTypeColumn };

    [Fact]
    public void TheScript_LocksBothPairColumns_OnEveryTableWhosePairIsBffManaged_WithTaskOnethirtythreesProfiles()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "Set-RegardingPairFieldSecurity.ps1"));

        Agrees(Analyse(script)).Should().BeTrue(
            "the script must field-secure the pair columns on exactly the BFF-managed tables with the BFF reader/writer profiles");
        Regex.IsMatch(script, @"canread\s*=\s*4;\s*cancreate\s*=\s*\$Create;\s*canupdate\s*=\s*\$Create").Should().BeTrue(
            "the reader is granted read only (Create 0), the writer read/create/update (Create 4)");
    }

    [Fact]
    public void TheSolution_SecuresBothPairColumnsOnTheProject_AndGrantsTheReaderReadAndTheWriterReadCreateUpdate()
    {
        var solution = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster");
        SecureRootInheritance.BffManagedPairTables.Should().BeEquivalentTo(new[] { "sprk_project" },
            "the solution checks below are written for the project; a new BFF-managed table needs its own entries");

        var entity = XDocument.Load(Path.Combine(solution, "Entities", "sprk_Project", "Entity.xml"));
        foreach (var column in PairColumns)
        {
            var attribute = entity.Descendants("attribute")
                .Single(a => string.Equals((string?)a.Element("LogicalName"), column, StringComparison.Ordinal));
            ((string?)attribute.Element("IsSecured")).Should().Be("1", $"sprk_project.{column} is field-secured in the solution");
        }

        var profiles = XDocument.Load(Path.Combine(solution, "Other", "FieldSecurityProfiles.xml"));
        foreach (var (profile, update) in new[]
                 {
                     (SecureFlagFieldSecurityAssertion.ReaderProfileName, "0"),
                     (SecureFlagFieldSecurityAssertion.WriterProfileName, "4"),
                 })
        {
            var permissions = profiles.Descendants("FieldSecurityProfile")
                .Single(p => (string?)p.Attribute("name") == profile)
                .Descendants("FieldPermission").ToArray();
            foreach (var column in PairColumns)
            {
                var permission = permissions.Should().ContainSingle(p =>
                        (string?)p.Element("EntityName") == "sprk_project" && (string?)p.Element("AttributeName") == column,
                        $"'{profile}' carries a permission on sprk_project.{column}")
                    .Subject;
                ((string?)permission.Element("CanRead")).Should().Be("4");
                ((string?)permission.Element("CanUpdate")).Should().Be(update);
                ((string?)permission.Element("CanCreate")).Should().Be(update);
            }
        }

        // No OTHER profile in the solution may write the pair (only the BFF writes it).
        profiles.Descendants("FieldSecurityProfile")
            .Where(p => (string?)p.Attribute("name") != SecureFlagFieldSecurityAssertion.WriterProfileName)
            .SelectMany(p => p.Descendants("FieldPermission"))
            .Where(p => (string?)p.Element("EntityName") == "sprk_project" && PairColumns.Contains((string?)p.Element("AttributeName")))
            .Should().OnlyContain(p => (string?)p.Element("CanUpdate") == "0" && (string?)p.Element("CanCreate") == "0");
    }

    [Fact]
    public void TheCreateExemption_IsExactlyThePairColumns_OfTheBffManagedTables()
    {
        OwnedChildWrite.BffManagedPairColumnsOf("sprk_project").Should().BeEquivalentTo(PairColumns);
        OwnedChildWrite.BffManagedPairColumnsOf("sprk_workassignment").Should().BeEmpty(
            "the Create Work Assignment wizard writes its pair as the user — it is not field-secured, so not exempt");
        OwnedChildWrite.BffManagedPairColumnsOf("sprk_matter").Should().BeEmpty();
    }

    private const string Seed = """
        $Table = 'sprk_project'
        $Columns = @('sprk_regardingrecordid', 'sprk_regardingrecordtype')
        $ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
        $WriterProfileName = 'Spaarke BFF-Managed Field Writers'
        $Secured = $true
        """;

    [Theory]
    [InlineData("$Secured = $true", "$Secured = $false")]
    [InlineData("$Table = 'sprk_project'", "$Table = 'sprk_workassignment'")]
    [InlineData(", 'sprk_regardingrecordtype'", "")]
    [InlineData("'sprk_regardingrecordid', ", "")]
    [InlineData("Field Writers'", "Field Readers'")]
    [InlineData("Field Readers'\n", "Field Writers'\n")]
    public void TheParser_SeesEachDrift(string from, string to)
    {
        Agrees(Analyse(Seed)).Should().BeTrue("the faithful seed agrees");
        Agrees(Analyse(Seed.ReplaceLineEndings("\n").Replace(from, to))).Should().BeFalse("each seeded drift must fail the agreement test");
    }

    private sealed record ScriptShape(string? Table, IReadOnlyList<string> Columns, string? Reader, string? Writer, bool Secured);

    private static bool Agrees(ScriptShape s) =>
        s.Table is not null
        && SecureRootInheritance.BffManagedPairTables.Count == 1
        && SecureRootInheritance.PairNamesAParentOnItsOwn(s.Table)
        && s.Columns.Count == PairColumns.Length
        && PairColumns.All(c => s.Columns.Contains(c, StringComparer.Ordinal))
        && s.Reader == SecureFlagFieldSecurityAssertion.ReaderProfileName
        && s.Writer == SecureFlagFieldSecurityAssertion.WriterProfileName
        && s.Secured;

    private static ScriptShape Analyse(string script)
    {
        static string? Single(string text, string pattern) =>
            Regex.Match(text, pattern, RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

        var columnsLine = Single(script, @"^\s*\$Columns\s*=\s*@\(([^)]*)\)");
        var columns = columnsLine is null
            ? Array.Empty<string>()
            : Regex.Matches(columnsLine, @"'([^']+)'").Select(m => m.Groups[1].Value).ToArray();

        return new ScriptShape(
            Single(script, @"^\s*\$Table\s*=\s*'([^']+)'"),
            columns,
            Single(script, @"^\s*\$ReaderProfileName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$WriterProfileName\s*=\s*'([^']+)'"),
            string.Equals(Single(script, @"^\s*\$Secured\s*=\s*\$(\w+)"), "true", StringComparison.OrdinalIgnoreCase));
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
