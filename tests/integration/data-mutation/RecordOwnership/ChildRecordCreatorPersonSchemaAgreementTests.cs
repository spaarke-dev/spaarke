using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// unified-access-control-r2 task 146 c1-r1 (owner round 13 item 9) — the CHILD schema script
/// (<c>scripts/Set-ChildRecordCreatorPersonSchema.ps1</c>) and the BFF agree on the column every app-create writer stamps
/// and F3 reads: its name, the navigation property a Web API write binds, the table it points at, the child tables that
/// carry it, and that it is FIELD-SECURED (only the BFF may write it).
/// </summary>
/// <remarks>
/// <para><b>Why a source guard.</b> The column does not exist until the script runs (a pending manual gate, G146-6), so
/// every behavioural test runs against a double that admits whatever name the code uses. A script that skipped a table
/// would leave every test green — and in the environment every app create of that table naming the column would FAIL
/// (Dataverse refuses a create naming a missing column). A wrong navigation property would fail every Web API bind. Read as
/// text (no PowerShell host, no network); the parser is exercised on seeded snippets, so a regression in the guard
/// reddens too. Mirrors task 133's <c>RecordCreatorPersonSchemaAgreementTests</c> for the roots.</para>
/// </remarks>
[Trait("Category", "Security")]
public class ChildRecordCreatorPersonSchemaAgreementTests
{
    [Fact]
    public void TheChildScript_CreatesAndSecures_TheColumnTheBffWrites_OnEveryStampedChildTable()
    {
        var schema = Analyse(File.ReadAllText(ScriptPath()));

        schema.Column.Should().Be(RecordCreatorPerson.Column);
        schema.ColumnSchemaName.Should().NotBeNull();
        schema.ColumnSchemaName!.ToLowerInvariant().Should().Be(RecordCreatorPerson.Column,
            "Dataverse derives the logical name from the schema name the script creates the lookup with");
        schema.NavigationProperty.Should().Be(RecordCreatorPerson.NavigationProperty,
            "every Web API create binds sprk_CreatedByPerson@odata.bind");
        schema.TargetEntity.Should().Be(RecordCreatorPerson.TargetEntity);
        schema.Tables.Should().BeEquivalentTo(RecordCreatorPerson.StampedChildTables,
            "a table the script skips fails every app create the BFF stamps on it");
        schema.Secured.Should().BeTrue("only the BFF may write who asked for a record");
    }

    // ── The guard bites: the parser on seeded scripts ───────────────────────────────────────────────

    private const string Seed = """
        $Column = 'sprk_createdbyperson'
        $ColumnSchemaName = 'sprk_CreatedByPerson'
        $NavigationProperty = 'sprk_CreatedByPerson'
        $TargetEntity = 'systemuser'
        $Tables = @('sprk_document', 'sprk_todo')
        $Secured = $true
        """;

    [Fact]
    public void TheParser_ReadsTheSeed()
    {
        var schema = Analyse(Seed);

        schema.Column.Should().Be("sprk_createdbyperson");
        schema.NavigationProperty.Should().Be("sprk_CreatedByPerson");
        schema.TargetEntity.Should().Be("systemuser");
        schema.Tables.Should().Equal("sprk_document", "sprk_todo");
        schema.Secured.Should().BeTrue();
    }

    [Theory]
    [InlineData("$Secured = $true", "$Secured = $false")]
    [InlineData("'sprk_document', ", "")]
    [InlineData("$TargetEntity = 'systemuser'", "$TargetEntity = 'contact'")]
    [InlineData("$Column = 'sprk_createdbyperson'", "$Column = 'sprk_createdbypersonid'")]
    [InlineData("$NavigationProperty = 'sprk_CreatedByPerson'", "$NavigationProperty = 'sprk_createdbyperson'")]
    public void TheParser_SeesEachDrift(string from, string to)
    {
        var expectedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_document", "sprk_todo" };

        Agrees(Analyse(Seed), expectedTables).Should().BeTrue("the faithful seed agrees with the BFF");
        Agrees(Analyse(Seed.Replace(from, to)), expectedTables).Should().BeFalse("each seeded drift must fail the agreement");
    }

    // ── Parser ───────────────────────────────────────────────────────────────────────────────────────

    private sealed record SchemaShape(
        string? Column, string? ColumnSchemaName, string? NavigationProperty, string? TargetEntity,
        IReadOnlyList<string> Tables, bool Secured);

    private static bool Agrees(SchemaShape schema, IReadOnlySet<string> tables) =>
        schema.Column == RecordCreatorPerson.Column
        && schema.NavigationProperty == RecordCreatorPerson.NavigationProperty
        && schema.TargetEntity == RecordCreatorPerson.TargetEntity
        && schema.Tables.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(tables)
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
            Single(script, @"^\s*\$ColumnSchemaName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$NavigationProperty\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$TargetEntity\s*=\s*'([^']+)'"),
            tables,
            string.Equals(Single(script, @"^\s*\$Secured\s*=\s*\$(\w+)"), "true", StringComparison.OrdinalIgnoreCase));
    }

    private static string ScriptPath() => Path.Combine(RepoRoot(), "scripts", "Set-ChildRecordCreatorPersonSchema.ps1");

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // `.git` is a FILE in a git worktree, which is how this repo is normally developed.
            var git = Path.Combine(dir, ".git");
            if (System.IO.Directory.Exists(git) || File.Exists(git))
                return dir;

            dir = System.IO.Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
