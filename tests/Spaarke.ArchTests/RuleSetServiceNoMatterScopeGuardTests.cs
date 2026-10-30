using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 (waiver condition 1, agreed with spaarke-ontology-platform-r1 2026-10-20): <c>RuleSetService</c> never writes a
/// matter onto a <c>sprk_policy</c> (or its version), on create or on update. #2084 made <c>sprk_policy</c> and
/// <c>sprk_policyversion</c> secure-family tables; the census waives RuleSetService's creates of them (owner resolved with no
/// parents) only because a billing rule hangs off <c>sprk_ruleset</c> and is never filed under a matter. The day a billing rule gains a
/// matter scope the waiver ends and the creates must be routed through <c>IRecordOwnershipResolver</c> with the matter as a parent.
/// </summary>
/// <remarks>
/// <b>How.</b> The comment-stripped source of <c>Services/Finance/RuleSetService.cs</c> must hold no <c>sprk_matter</c> and no
/// matter-named identifier (<c>matterId</c>, <c>MatterRef</c>, ...): any write of the lookup needs one of them, whether as a literal or
/// through a const. <b>Controls.</b> A planted write (a literal, a const-held column, a lookup built from a matter id) is caught; the
/// real file's rule-set, policy and version writes are not. The behavioural twin is
/// <c>RuleSetServiceOwnerTests.NoWrittenPolicyOrVersionRowCarriesAMatter_OnCreateOrUpdate</c> in the unit suite.
/// </remarks>
public sealed class RuleSetServiceNoMatterScopeGuardTests
{
    private const string RuleSetServicePath = "Services/Finance/RuleSetService.cs";

    /// <summary>The column literal, or an identifier that names a matter (a variable, parameter or const that could carry one).</summary>
    private static readonly Regex MatterReference = new(
        @"(?i:sprk_matter)|(?-i:\b[Mm]atter[A-Z_]\w*|\b[Mm]atter\b)", RegexOptions.Compiled);

    private static IReadOnlyList<string> Violations(string source)
    {
        var code = SourceScan.CodeText(source.Split('\n'));
        return MatterReference.Matches(code)
            .Select(m => $"line {SourceScan.LineOf(code, m.Index)}: {m.Value}")
            .ToList();
    }

    // Remove this guard only together with the routing change that ends the Pending census waiver for RuleSetService.
    [Fact(DisplayName = "ebilling-r1: RuleSetService never writes a matter onto sprk_policy or sprk_policyversion (waiver condition 1)")]
    public void RuleSetServiceNeverWritesAMatter()
    {
        var path = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", RuleSetServicePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{RuleSetServicePath} was not found; the guard would be vacuous.");
        var source = File.ReadAllText(path);

        // The guard reads the file it means to: the policy and version creates are there (a moved file would pass an empty scan).
        Assert.Contains("new Entity(PolicyEntity", source, StringComparison.Ordinal);
        Assert.Contains("new Entity(VersionEntity", source, StringComparison.Ordinal);

        var violations = Violations(source);
        Assert.True(violations.Count == 0,
            "RuleSetService names a matter (" + string.Join("; ", violations) + "). The census waiver for its sprk_policy / "
            + "sprk_policyversion creates (RecordOwnerAssignmentCensusTests, 'ebilling-r1 RuleSetService') holds only while a billing rule is "
            + "never filed under a matter. If a billing rule now has a matter scope the waiver has ended: route every create through "
            + "IRecordOwnershipResolver with the matter as a parent (secure-if-any) and move the entries to Routed.");
    }

    [Theory(DisplayName = "ebilling-r1: the matter-scope guard fires on a planted matter write (must-fire control)")]
    [InlineData("policy[\"sprk_matter\"] = new EntityReference(\"sprk_matter\", id);")]
    [InlineData("const string Col = \"sprk_matter\"; policy[Col] = r;")]
    [InlineData("policy[\"sprk_regardingmatter\"] = new EntityReference(MatterEntity, matterId);")]
    [InlineData("fields[\"sprk_regarding\"] = new EntityReference(\"sprk_matter\", request.MatterId);")]
    [InlineData("var matterRef = Lookup(request); version[Bind] = matterRef;")]
    public void TheGuardFiresOnAPlantedMatterWrite(string planted)
    {
        Assert.NotEmpty(Violations("class C { void M() {\n" + planted + "\n} }"));
    }

    [Fact(DisplayName = "ebilling-r1: the matter-scope guard does not fire on rule-set, policy and version writes or on a comment (must-not-fire control)")]
    public void TheGuardDoesNotFireOnTheRuleSetWritesOrAComment()
    {
        const string clean = """
            class C { void M() {
                // a billing rule is never filed under a matter (sprk_matter is not written here)
                var policy = new Entity(PolicyEntity) { ["sprk_ruleset"] = new EntityReference(RuleSetEntity, ruleSetId) };
                policy["sprk_domain"] = new OptionSetValue(1);
                var version = new Entity(VersionEntity) { ["sprk_policy"] = new EntityReference(PolicyEntity, policyId) };
                owner.ApplyTo(policy);
            } }
            """;

        Assert.Empty(Violations(clean));
    }
}
