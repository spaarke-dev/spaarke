using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Ebilling;

/// <summary>
/// ebilling-r1 DEF-061 (owner decision 2026-11-09): <c>sprk_procedureversion.sprk_publishedon</c>, the server's publish stamp, is an optional,
/// not-secured Date and Time column (user local: an instant). Three artifacts must agree:
/// <c>scripts/ebilling/Deploy-EbillingProcedureVersionPublishedOn.ps1</c> (what an operator applies, and the deploy gate's <c>-Verify</c>), the
/// SpaarkeMaster solution source (<c>Entities/sprk_procedureversion/Entity.xml</c>, what a new stamp gets) and the BFF's column name
/// (<see cref="ProcedureEntity.VersionPublishedOnColumn"/>). Read as text (no PowerShell host, no network); the script parser is exercised on
/// seeded drifts. A source scan pins that only <c>ProcedureService</c> writes the column. Behaviour: <c>ProcedureServiceTests</c> (the writer),
/// <c>ProcedureResolverTests</c> (the reader) and <c>ProcedureVersionPublishedOnWriterOnlyTests</c> (the generic writers' refusal).
/// </summary>
public class ProcedureVersionPublishedOnAgreementTests
{
    private const string Table = "sprk_procedureversion";
    private const string Column = "sprk_publishedon";
    private const string Script = "Deploy-EbillingProcedureVersionPublishedOn.ps1";

    [Fact(DisplayName = "DEF-061: the BFF's column name is the one the script creates and the solution carries")]
    public void TheBffsColumnName_IsTheScriptsAndTheSolutions()
    {
        ProcedureEntity.VersionPublishedOnColumn.Should().Be(Column);
        ProcedureEntity.VersionEntity.Should().Be(Table);

        Agrees(Analyse(ReadScript())).Should().BeTrue("the script must create sprk_procedureversion.sprk_publishedon");
    }

    [Fact(DisplayName = "DEF-061: the solution source has sprk_publishedon as an optional, not-secured, user-local Date and Time column")]
    public void TheSolution_CarriesAnOptionalUserLocalDateTime_NotSecured()
    {
        var attributes = XDocument.Load(EntityXml()).Descendants("attribute").ToArray();
        var stamp = attributes.Single(a => (string?)a.Element("LogicalName") == Column);

        ((string?)stamp.Attribute("PhysicalName")).Should().Be("sprk_PublishedOn");
        ((string?)stamp.Element("Type")).Should().Be("datetime");
        ((string?)stamp.Element("Format")).Should().Be("datetime", "Date and Time (the date-only form is 'date')");
        ((string?)stamp.Element("Behavior")).Should().Be("1", "user local: an instant, stored UTC (the time-zone-independent form, 3, would store a wall-clock literal)");
        ((string?)stamp.Element("RequiredLevel")).Should().Be("none", "empty on a Draft and on a version published before the column existed");
        ((string?)stamp.Element("IsSecured")).Should().Be("0", "no field security is added by DEF-061 (the report says why it may be needed)");
        ((string?)stamp.Element("IsAuditEnabled")).Should().Be("1", "the sibling columns of the table are audited, and the stamp is evidence");
        stamp.Element("DefaultValue").Should().BeNull("no default: an empty stamp is a fact");
    }

    // Behaviours of the script that no host runs in a unit test, pinned as source facts. Each pattern is checked against the shipped script and
    // against a seeded drift of it.
    [Theory(DisplayName = "DEF-061: the script keeps its column shape, its -Verify gaps, its entity-scoped publish, its mode guard and its backfill rules")]
    [InlineData(@"Add-Column \$Column \(New-DateTimeDef \$ColumnSchemaName 'Published On' \$Description\) 'DateTime'", "New-DateTimeDef $ColumnSchemaName", "New-DateOnlyDef $ColumnSchemaName")]
    [InlineData(@"DateTimeBehavior\.Value -ne 'UserLocal'", "-ne 'UserLocal'", "-ne 'TimeZoneIndependent'")]
    [InlineData(@"Format -ne 'DateAndTime'", "-ne 'DateAndTime'", "-ne 'DateOnly'")]
    [InlineData(@"RequiredLevel\.Value -ne 'None'", "-ne 'None'", "-ne 'Recommended'")]
    [InlineData(@"<entities><entity>\$Table</entity></entities>", "<entities><entity>$Table</entity></entities>", "<entities></entities>")]
    [InlineData(@"if \(\$Verify -and \(\$DryRun -or \$Backfill\)\) \{ throw", "$Verify -and ($DryRun -or $Backfill)", "$Verify -and $DryRun")]
    [InlineData(@"if \(\$status -eq \$StatusRetired\) \{ \$plan\.RetiredLeft\+\+; continue \}", "$plan.RetiredLeft++; continue", "$plan.RetiredLeft++")]
    [InlineData(@"\$StatusRetired = 100000003", "100000003", "100000002")]
    [InlineData(@"\$StatusActive = 100000001", "$StatusActive = 100000001", "$StatusActive = 100000000")]
    [InlineData(@"\$StatusSuperseded = 100000002", "$StatusSuperseded = 100000002", "$StatusSuperseded = 100000003")]
    [InlineData(@"if \(\$HasColumn -and \$r\.\$Column\) \{ \$plan\.AlreadyStamped\+\+; continue \}", "$plan.AlreadyStamped++; continue", "$plan.AlreadyStamped++")]
    [InlineData(@"if \(\$fresh\.\$Column\) \{ Write-Host", "if ($fresh.$Column)", "if ($false)")]
    [InlineData(@"if \(\$plan\.Masked -gt 0\) \{\s+throw", "if ($plan.Masked -gt 0) {\n            throw", "if ($plan.Masked -gt 99) {\n            throw")]
    [InlineData(@"exit 2\r?\n\}\r?\ncatch \{", "exit 2\n}\ncatch {", "exit 0\n}\ncatch {")]
    public void TheScript_KeepsItsBehaviours(string pattern, string from, string to)
    {
        var script = ReadScript().ReplaceLineEndings("\n");

        Regex.IsMatch(script, pattern).Should().BeTrue("the shipped script has this behaviour");
        script.Should().Contain(from);
        Regex.IsMatch(script.Replace(from, to), pattern).Should().BeFalse("a seeded drift of it must fail the pin");
    }

    [Fact(DisplayName = "DEF-061: the script publishes entity-scoped only and never alters an existing column or writes without a PATCH of the stamp alone")]
    public void TheScript_NeverPublishesTheWholeTenant_AndWritesOnlyTheStamp()
    {
        var script = ReadScript();

        // The tenant-wide publish action, built from parts so the repo-wide D-83 lint finds no literal here.
        script.Should().NotContain("Publish" + "All" + "Xml", "the publish is entity-scoped only");
        script.Should().NotContain("-Method DELETE").And.NotContain("-Method PUT");
        Regex.Matches(script, @"-Method PATCH -Body @\{ \$Column = \$iso \}").Count.Should().Be(1, "the one data write sets the stamp and nothing else");
        Regex.Matches(script, @"-Method (PATCH|POST)").Count.Should().Be(2, "the PATCH of the stamp and the entity-scoped PublishXml, nothing else");
        script.Should().NotContain("UpdateAttribute", "an existing column is escalated, never altered");
    }

    private const string Seed = """
        $Table = 'sprk_procedureversion'
        $EntitySet = 'sprk_procedureversions'
        $Column = 'sprk_publishedon'
        $ColumnSchemaName = 'sprk_PublishedOn'
        """;

    [Theory(DisplayName = "DEF-061: the script parser sees each drift")]
    [InlineData("$Table = 'sprk_procedureversion'", "$Table = 'sprk_policyversion'")]
    [InlineData("'sprk_publishedon'", "'sprk_publishedat'")]
    [InlineData("'sprk_PublishedOn'", "'sprk_PublishedAt'")]
    [InlineData("$EntitySet = 'sprk_procedureversions'", "$EntitySet = 'sprk_procedureversion'")]
    public void TheParser_SeesEachDrift(string from, string to)
    {
        Agrees(Analyse(Seed)).Should().BeTrue("the faithful seed agrees");
        Agrees(Analyse(Seed.ReplaceLineEndings("\n").Replace(from, to))).Should().BeFalse("each seeded drift must fail the agreement test");
    }

    // The writer is ProcedureService alone. The readers (the resolver) and the protection (the registry) name the column too; nothing else in the
    // BFF mentions it. A second writer would be a second owner of an invariant (root CLAUDE.md §17: one server-side owner).
    [Fact(DisplayName = "DEF-061: only ProcedureService assigns the stamp; only the resolver, the registry and the service name it")]
    public void OnlyProcedureService_WritesTheStamp()
    {
        var bff = Path.Combine(RepoRoot(), "src", "server");
        var mentions = Directory.EnumerateFiles(bff, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(f => (File: Path.GetFileName(f), Text: File.ReadAllText(f)))
            .Where(f => f.Text.Contains("VersionPublishedOnColumn") || f.Text.Contains(Column, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        mentions.Select(m => m.File).Should().BeEquivalentTo(["ProcedureService.cs", "ProcedureResolver.cs", "RecordTableRules.cs"]);

        var assigns = new Regex(@"\[\s*(ProcedureEntity\.)?VersionPublishedOnColumn\s*\]\s*=(?!=)");
        mentions.Where(m => assigns.IsMatch(m.Text)).Select(m => m.File).Should().Equal(["ProcedureService.cs"], "the publish write is the only assignment");
        mentions.Single(m => m.File == "ProcedureResolver.cs").Text.Should().NotContain("SetAttributeValue").And.NotContain("UpdateAsync");
    }

    private sealed record ScriptShape(string? Table, string? EntitySet, string? Column, string? SchemaName);

    private static bool Agrees(ScriptShape s) =>
        s.Table == ProcedureEntity.VersionEntity && s.EntitySet == "sprk_procedureversions"
        && s.Column == ProcedureEntity.VersionPublishedOnColumn && s.SchemaName == "sprk_PublishedOn";

    private static ScriptShape Analyse(string script)
    {
        static string? Single(string text, string pattern) =>
            Regex.Match(text, pattern, RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

        return new ScriptShape(
            Single(script, @"^\s*\$Table\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$EntitySet\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$Column\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$ColumnSchemaName\s*=\s*'([^']+)'"));
    }

    private static string ReadScript() => File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", Script));

    /// <summary>The Procedure Version's Entity.xml (the folder's case differs between the index and some checkouts).</summary>
    private static string EntityXml()
    {
        var entities = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", "Entities");
        return Path.Combine(
            Directory.GetDirectories(entities).Single(d => string.Equals(Path.GetFileName(d), Table, StringComparison.OrdinalIgnoreCase)),
            "Entity.xml");
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
