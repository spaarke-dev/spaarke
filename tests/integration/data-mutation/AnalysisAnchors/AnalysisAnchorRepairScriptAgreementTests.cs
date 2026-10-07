using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Api.Filters;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.AnalysisAnchors;

/// <summary>
/// unified-access-control-r2 task 162 f1 (owner round 15 item 3) — the anchor backfill script
/// (<c>scripts/Repair-AnalysisAnchors.ps1</c>) and the analysis-read rule agree on WHICH lookups are an analysis's anchors.
/// </summary>
/// <remarks>
/// <para><b>Why a source guard.</b> The script's writes run only at a manual gate against live Dataverse, so no behavioural
/// test executes them. A script whose anchor list drifted from <see cref="AnalysisAuthorizationFilter.AnchorColumns"/> would
/// either census a row as "anchorless" that the rule decides by an anchor (a column missing from the script), or fill a
/// column the rule does not read as a parent (a column added to the script) — and every test would stay green. Read as text
/// (no PowerShell host, no network); the parser is exercised on seeded snippets, so a regression in the guard reddens too.
/// Mirrors task 146's <c>ChildRecordCreatorPersonSchemaAgreementTests</c>.</para>
/// </remarks>
[Trait("Category", "Security")]
public class AnalysisAnchorRepairScriptAgreementTests
{
    [Fact(DisplayName = "162 f1: the anchor backfill script's anchor columns are exactly the analysis-read rule's anchors")]
    public void TheScript_UsesExactlyTheReadRulesAnchorColumns()
    {
        var script = File.ReadAllText(ScriptPath());

        AnchorColumnsOf(script).Should().BeEquivalentTo(AnalysisAuthorizationFilter.AnchorColumns,
            "the backfill may fill only, and must census every, column the read rule treats as a parent");
        script.Should().Contain("[switch]$Apply").And.Contain("[switch]$Verify",
            "the repo's data-script pattern: dry run by default, -Apply writes, -Verify checks");
        Regex.IsMatch(script, @"if \(\$Apply -and \$Verify\)").Should().BeTrue("-Apply and -Verify are exclusive");
    }

    private const string Seed = "$AnchorColumns = @('sprk_documentid', 'sprk_regardingmatter')";

    [Theory(DisplayName = "162 f1: the anchor-list parser sees each drift")]
    [InlineData("'sprk_documentid', ", "")]
    [InlineData("'sprk_regardingmatter'", "'sprk_regardingmatter', 'sprk_playbook'")]
    public void TheParser_SeesEachDrift(string from, string to)
    {
        AnchorColumnsOf(Seed).Should().Equal("sprk_documentid", "sprk_regardingmatter");
        AnchorColumnsOf(Seed.Replace(from, to)).Should().NotEqual(new[] { "sprk_documentid", "sprk_regardingmatter" });
    }

    private static IReadOnlyList<string> AnchorColumnsOf(string script)
    {
        var line = Regex.Match(script, @"^\s*\$AnchorColumns\s*=\s*@\(([^)]*)\)", RegexOptions.Multiline);
        return line.Success
            ? Regex.Matches(line.Groups[1].Value, @"'([^']+)'").Select(m => m.Groups[1].Value).ToArray()
            : Array.Empty<string>();
    }

    private static string ScriptPath() => Path.Combine(RepoRoot(), "scripts", "Repair-AnalysisAnchors.ps1");

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
