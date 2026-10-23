using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Services.Signals.Actions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals;

/// <summary>
/// Task 159 (fix round 2): <c>scripts/Apply-DoLanePolicies.ps1</c> lints a policy definition's decision plan against HARDCODED lists
/// of catalog codes (<c>$KindRules</c> per lane, and <c>$NextStepCodes</c>) because it must refuse a bad plan before it writes
/// anything. No guard tied those lists to <see cref="DecisionActionCatalog"/>, so a new catalog code (as <c>assign-task</c> was) could be
/// missing from the script and the script would refuse a valid plan, or keep accepting a code the catalog had dropped. This reads the
/// script's lists and fails when they and the catalog diverge, in either direction.
/// </summary>
/// <remarks>Maintain-class (ADR-038, <c>tests/unit/domain/**</c>).</remarks>
[Trait("status", "new")]
public class DecisionActionCatalogScriptListsTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    private static string ScriptText() => File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "Apply-DoLanePolicies.ps1"));

    private static string[] QuotedCodes(string list) =>
        Regex.Matches(list, "'([a-z0-9-]+)'").Select(m => m.Groups[1].Value).ToArray();

    private static string[] LaneCodes(string script, string kind)
    {
        var m = Regex.Match(script, @"'" + Regex.Escape(kind) + @"'\s*=\s*@\{[^}]*?Codes\s*=\s*@\(([^)]*)\)");
        m.Success.Should().BeTrue($"the script defines the {kind} Codes list");
        return QuotedCodes(m.Groups[1].Value);
    }

    private static string[] NextStepCodes(string script)
    {
        var m = Regex.Match(script, @"\$NextStepCodes\s*=\s*@\(([^)]*)\)");
        m.Success.Should().BeTrue("the script defines $NextStepCodes");
        return QuotedCodes(m.Groups[1].Value);
    }

    [Theory]
    [InlineData("do-lane-policies", DecisionLane.Do)]
    [InlineData("know-promotion-policies", DecisionLane.Decide)]
    public void TheScriptsPlanActionCodes_AreExactlyTheCatalogsCodesForThatLane(string kind, DecisionLane lane)
    {
        var catalog = DecisionActionCatalog.All.Where(a => a.PlanLane == lane).Select(a => a.Code);

        LaneCodes(ScriptText(), kind).Should().BeEquivalentTo(catalog,
            "a code missing from the script makes the lint refuse a valid plan; an extra one lets the lint pass a plan DecisionPlanService refuses at read time");
    }

    [Fact]
    public void TheScriptsNextStepCodes_AreExactlyTheCatalogsNextStepCodes_ForEveryKind()
    {
        var script = ScriptText();
        var catalog = DecisionActionCatalog.All.Where(a => a.IsNextStep).Select(a => a.Code);

        NextStepCodes(script).Should().BeEquivalentTo(catalog);
        // Both kinds read the one list (DecisionPlanService has no per-lane Next-step rule).
        Regex.Matches(script, @"NextSteps\s*=\s*\$NextStepCodes").Count.Should().Be(2);
    }

    [Fact]
    public void MustFire_TheComparison_NoticesAMissingAndAnExtraCode()
    {
        // Control for the guard itself: the parser reads real lists and a divergent list is not "equivalent".
        var parsed = QuotedCodes("'a', 'b-c'");
        parsed.Should().Equal("a", "b-c");
        new[] { "a" }.Should().NotBeEquivalentTo(parsed);
        new[] { "a", "b-c", "z" }.Should().NotBeEquivalentTo(parsed);
    }
}
