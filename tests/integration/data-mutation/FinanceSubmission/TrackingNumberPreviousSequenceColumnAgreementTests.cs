using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceSubmission;

/// <summary>
/// ebilling-r1 task 095d-1a, K4 (owner decision 2026-11-04): <c>sprk_invoicesubmission.sprk_previoussequence</c>, the n a renumbered submission
/// gave up in the <c>none</c> scope, is a whole number (Integer, never BigInt), 1 to 2147483647 like the sequence it copies, and NOT field-secured
/// (a masked read would read as "no previous sequence" and the number would be issued again). Three artifacts must agree:
/// <c>scripts/ebilling/Deploy-EbillingSubmissionPreviousSequenceColumn.ps1</c> (what an operator applies and the deploy gate's <c>-Verify</c>), the
/// SpaarkeMaster solution source (<c>Entities/sprk_invoicesubmission/Entity.xml</c>, what a new stamp gets) and the allocator's column name
/// (<see cref="TrackingNumberAllocator.PreviousSequenceColumn"/>). Read as text (no PowerShell host, no network); the script parser is exercised
/// on seeded drifts. The allocator's behaviour is <c>TrackingNumberAllocatorTests</c> (the partial with the previous-sequence cases).
/// </summary>
public class TrackingNumberPreviousSequenceColumnAgreementTests
{
    private const string Column = "sprk_previoussequence";
    private const string Script = "Deploy-EbillingSubmissionPreviousSequenceColumn.ps1";

    [Fact(DisplayName = "095d-1a K4: the allocator's column name is the one the script creates and the solution carries")]
    public void TheAllocatorsColumnName_IsTheScriptsAndTheSolutions()
    {
        TrackingNumberAllocator.PreviousSequenceColumn.Should().Be(Column);

        var shape = Analyse(ReadScript());
        Agrees(shape).Should().BeTrue("the script must create sprk_invoicesubmission.sprk_previoussequence as a 1 to 2147483647 whole number");
    }

    [Fact(DisplayName = "095d-1a K4: the solution source has sprk_previoussequence as an optional, not-secured whole number with the sequence's range")]
    public void TheSolution_CarriesAWholeNumber_WithTheSequencesRange_NotSecured()
    {
        var attributes = XDocument.Load(EntityXml()).Descendants("attribute").ToArray();
        var previous = attributes.Single(a => (string?)a.Element("LogicalName") == Column);
        var sequence = attributes.Single(a => (string?)a.Element("LogicalName") == TrackingNumberAllocator.SequenceColumn);

        ((string?)previous.Element("Type")).Should().Be("int", "a whole number (Integer): Dataverse cannot field-secure a BigInt, and the value copies an Integer");
        ((string?)previous.Element("IsSecured")).Should().Be("0", "a masked read would read as 'no previous sequence'");
        ((string?)previous.Element("RequiredLevel")).Should().Be("none", "empty on every row that was never renumbered");
        ((string?)previous.Attribute("PhysicalName")).Should().Be("sprk_PreviousSequence");
        ((string?)previous.Element("MinValue")).Should().Be("1");
        ((string?)previous.Element("MaxValue")).Should().Be("2147483647");
        ((string?)previous.Element("MinValue")).Should().Be((string?)sequence.Element("MinValue"), "it copies the sequence, so it holds the same range");
        ((string?)previous.Element("MaxValue")).Should().Be((string?)sequence.Element("MaxValue"));
        ((string?)previous.Element("Type")).Should().Be((string?)sequence.Element("Type"));
    }

    // Behaviours of the script that no host runs in a unit test, pinned as source facts. Each pattern is checked against the shipped script and
    // against a seeded drift of it.
    [Theory(DisplayName = "095d-1a K4: the script keeps its Integer type, its -Verify gaps, its entity-scoped publish and its refusal to run without a mode")]
    [InlineData(@"'@odata\.type'\s*=\s*'Microsoft\.Dynamics\.CRM\.IntegerAttributeMetadata'", "IntegerAttributeMetadata'", "BigIntAttributeMetadata'")]
    [InlineData(@"-ne 'Integer'\)", "-ne 'Integer')", "-ne 'BigInt')")]
    [InlineData(@"\$attr\.IsSecured -eq \$true", "$attr.IsSecured -eq $true", "$attr.IsSecured -eq $null")]
    [InlineData(@"<entities><entity>\$Table</entity></entities>", "<entities><entity>$Table</entity></entities>", "<entities></entities>")]
    [InlineData(@"if \(-not \$Apply -and -not \$Verify -and -not \$DryRun\) \{ throw", "-not $Apply -and -not $Verify -and -not $DryRun", "-not $Apply -and -not $Verify")]
    [InlineData(@"exit 1\r?\n\}\r?\n\r?\ntry \{", "exit 1\n}\n\ntry {", "exit 0\n}\n\ntry {")]
    public void TheScript_KeepsItsBehaviours(string pattern, string from, string to)
    {
        var script = ReadScript().ReplaceLineEndings("\n");

        Regex.IsMatch(script, pattern).Should().BeTrue("the shipped script has this behaviour");
        script.Should().Contain(from);
        Regex.IsMatch(script.Replace(from, to), pattern).Should().BeFalse("a seeded drift of it must fail the pin");
        // The tenant-wide publish action, built from parts so the repo-wide D-83 lint finds no literal here.
        script.Should().NotContain("Publish" + "All" + "Xml", "the publish is entity-scoped only");
    }

    private const string Seed = """
        $Table = 'sprk_invoicesubmission'
        $Column = 'sprk_previoussequence'
        $ColumnSchemaName = 'sprk_PreviousSequence'
        $ColumnMin = 1
        $ColumnMax = 2147483647
        """;

    [Theory(DisplayName = "095d-1a K4: the script parser sees each drift")]
    [InlineData("$Table = 'sprk_invoicesubmission'", "$Table = 'sprk_invoice'")]
    [InlineData("'sprk_previoussequence'", "'sprk_previoussequences'")]
    [InlineData("'sprk_PreviousSequence'", "'sprk_PreviousSeq'")]
    [InlineData("$ColumnMin = 1", "$ColumnMin = 0")]
    [InlineData("$ColumnMax = 2147483647", "$ColumnMax = 99999")]
    public void TheParser_SeesEachDrift(string from, string to)
    {
        Agrees(Analyse(Seed)).Should().BeTrue("the faithful seed agrees");
        Agrees(Analyse(Seed.ReplaceLineEndings("\n").Replace(from, to))).Should().BeFalse("each seeded drift must fail the agreement test");
    }

    private sealed record ScriptShape(string? Table, string? Column, string? SchemaName, string? Min, string? Max);

    private static bool Agrees(ScriptShape s) =>
        s.Table == TrackingNumberAllocator.SubmissionEntity && s.Column == TrackingNumberAllocator.PreviousSequenceColumn
        && s.SchemaName == "sprk_PreviousSequence" && s.Min == "1" && s.Max == int.MaxValue.ToString();

    private static ScriptShape Analyse(string script)
    {
        static string? Single(string text, string pattern) =>
            Regex.Match(text, pattern, RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

        return new ScriptShape(
            Single(script, @"^\s*\$Table\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$Column\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$ColumnSchemaName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$ColumnMin\s*=\s*(\d+)"),
            Single(script, @"^\s*\$ColumnMax\s*=\s*(\d+)"));
    }

    private static string ReadScript() => File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", Script));

    /// <summary>The submission's Entity.xml (the folder's case differs between the index and some checkouts).</summary>
    private static string EntityXml()
    {
        var entities = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", "Entities");
        return Path.Combine(
            Directory.GetDirectories(entities).Single(d => string.Equals(Path.GetFileName(d), TrackingNumberAllocator.SubmissionEntity, StringComparison.OrdinalIgnoreCase)),
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
