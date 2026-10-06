using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.CreatorPerson;

/// <summary>
/// unified-access-control-r2 task 133 b2 — the schema script (<c>scripts/Set-RecordCreatorPersonSchema.ps1</c>) and the
/// BFF agree on the column every BFF create path writes and provisioning's resume reads: its name, the table it points
/// at, the three tables that carry it, and that it is FIELD-SECURED (only the BFF may write it).
/// </summary>
/// <remarks>
/// <para><b>Why a source guard.</b> The column does not exist until the script runs (a pending manual gate), so every
/// behavioural test runs against a double that admits whatever name the code uses. A script that created
/// <c>sprk_createdbypersonid</c>, pointed it at <c>contact</c>, skipped a table, or dropped the field security would leave
/// every test green — and in the environment, every Office quick-create would fail (Dataverse refuses a create naming a
/// column it does not have) or any user with Write could name someone else as a record's creator. The script is read as
/// text (no PowerShell host, no network). The parser is itself exercised on seeded snippets, so a regression in the
/// guard reddens too.</para>
/// </remarks>
[Trait("Category", "Security")]
public class RecordCreatorPersonSchemaAgreementTests
{
    [Fact]
    public void TheScript_CreatesAndSecures_TheColumnTheBffWrites()
    {
        var script = File.ReadAllText(ScriptPath());
        var schema = Analyse(script);

        schema.Column.Should().Be(RecordCreatorPerson.Column);
        Regex.Match(script, @"^\s*\$ColumnSchemaName\s*=\s*'([^']+)'", RegexOptions.Multiline).Groups[1].Value
            .ToLowerInvariant().Should().Be(RecordCreatorPerson.Column,
                "Dataverse derives the logical name from the schema name the script creates the lookup with");
        schema.TargetEntity.Should().Be(RecordCreatorPerson.TargetEntity);
        schema.Tables.Should().BeEquivalentTo(RecordCreatorPerson.StampedTables,
            "a table the script skips fails every create the BFF stamps on it");
        schema.Secured.Should().BeTrue("only the BFF may write who created a record");
        RecordCreatorPerson.ValueColumn.Should().Be($"_{RecordCreatorPerson.Column}_value",
            "the resume reads the lookup's Web API read form, which Dataverse derives from the column name");
    }

    // ── The guard bites: the parser on seeded scripts ───────────────────────────────────────────────

    private const string Seed = """
        $Column = 'sprk_createdbyperson'
        $ColumnSchemaName = 'sprk_CreatedByPerson'
        $TargetEntity = 'systemuser'
        $Tables = @('sprk_project', 'sprk_matter', 'sprk_workassignment')
        $Secured = $true
        """;

    [Fact]
    public void TheParser_ReadsTheSeed()
    {
        var schema = Analyse(Seed);

        schema.Column.Should().Be("sprk_createdbyperson");
        schema.TargetEntity.Should().Be("systemuser");
        schema.Tables.Should().Equal("sprk_project", "sprk_matter", "sprk_workassignment");
        schema.Secured.Should().BeTrue();
    }

    [Theory]
    [InlineData("$Secured = $true", "$Secured = $false")]
    [InlineData("'sprk_matter', ", "")]
    [InlineData("$TargetEntity = 'systemuser'", "$TargetEntity = 'contact'")]
    [InlineData("$Column = 'sprk_createdbyperson'", "$Column = 'sprk_createdbypersonid'")]
    public void TheParser_SeesEachDrift(string from, string to)
    {
        Agrees(Analyse(Seed)).Should().BeTrue("the faithful seed agrees with the BFF");
        Agrees(Analyse(Seed.Replace(from, to))).Should().BeFalse("each seeded drift must fail the agreement test above");
    }

    // ── Parser ───────────────────────────────────────────────────────────────────────────────────────

    private sealed record SchemaShape(string? Column, string? TargetEntity, IReadOnlyList<string> Tables, bool Secured);

    /// <summary>The same agreement <see cref="TheScript_CreatesAndSecures_TheColumnTheBffWrites"/> asserts.</summary>
    private static bool Agrees(SchemaShape schema) =>
        schema.Column == RecordCreatorPerson.Column
        && schema.TargetEntity == RecordCreatorPerson.TargetEntity
        && schema.Tables.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(RecordCreatorPerson.StampedTables)
        && schema.Secured;

    private static SchemaShape Analyse(string script)
    {
        static string? Single(string text, string pattern) =>
            Regex.Match(text, pattern, RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

        var tablesLine = Single(script, @"^\s*\$Tables\s*=\s*@\(([^)]*)\)");
        var tables = tablesLine is null
            ? Array.Empty<string>()
            : Regex.Matches(tablesLine, @"'([^']+)'").Select(m => m.Groups[1].Value).ToArray();

        return new SchemaShape(
            Single(script, @"^\s*\$Column\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$TargetEntity\s*=\s*'([^']+)'"),
            tables,
            string.Equals(Single(script, @"^\s*\$Secured\s*=\s*\$(\w+)"), "true", StringComparison.OrdinalIgnoreCase));
    }

    private static string ScriptPath() => Path.Combine(RepoRoot(), "scripts", "Set-RecordCreatorPersonSchema.ps1");

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // `.git` is a FILE in a git worktree, which is how this repo is normally developed.
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
