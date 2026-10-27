using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Ontology platform R1 task 102b fix round 3 (owner 2026-10-17): deleting a matter deletes a rule (<c>sprk_policy</c>) limited to it, so
/// <c>sprk_policy_matter</c> carries CascadeDelete=<c>Cascade</c> (<c>projects/spaarke-ontology-platform-r1/notes/102b-rule-matter-scope.md</c>,
/// "Fix round 3" and "Fix round 4"). This guards both halves of that schema decision in
/// <c>src/dataverse/solutions/SpaarkeMaster/Other/Relationships/*.xml</c>:
/// <list type="bullet">
/// <item><c>sprk_policy_matter</c> has CascadeDelete=Cascade. Back to RemoveLink, a matter's delete would WIDEN a rule limited to it to
/// all matters (the lookup is emptied and an empty matter means "all matters").</item>
/// <item><c>sprk_Policy</c> is the referencing (child) side of EXACTLY ONE relationship with CascadeDelete=Cascade. Dataverse allows a
/// table to be the child in only one parental (Delete=Cascade) relationship, and a second one would also delete rules on a path nobody
/// decided.</item>
/// </list>
/// Enforced here (ArchTest, run by the <c>Stop</c> hook and CI) because the rule lives in solution XML that no compiler or linter reads.
/// </summary>
public class PolicyMatterCascadeGuardTests
{
    private const string PolicyMatterRelationship = "sprk_policy_matter";
    private const string PolicyTable = "sprk_Policy";

    private static string RelationshipsRoot =>
        Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster", "Other", "Relationships");

    [Fact(DisplayName = "sprk_policy_matter cascades the delete, and it is sprk_Policy's only Delete=Cascade parent (102b)")]
    public void ThePolicyMatterCascadeHolds()
    {
        var files = Directory.EnumerateFiles(RelationshipsRoot, "*.xml", SearchOption.TopDirectoryOnly).ToList();
        Assert.True(files.Count >= 50, $"Found only {files.Count} relationship files under {RelationshipsRoot}; the scan is probably broken.");

        var problems = Problems(files.Select(f => XDocument.Load(f)));

        Assert.True(problems.Count == 0,
            "The matter -> rule delete cascade (task 102b fix round 3) no longer holds in src/dataverse/solutions/SpaarkeMaster/Other/Relationships:\n  "
            + string.Join("\n  ", problems));
    }

    // ── Controls on in-memory XML ───────────────────────────────────────────────────────────────────────────────────────────────

    private static XDocument Relationships(params (string Name, string Referencing, string Referenced, string CascadeDelete)[] relationships) =>
        new(new XElement("EntityRelationships", relationships.Select(r => new XElement("EntityRelationship",
            new XAttribute("Name", r.Name),
            new XElement("EntityRelationshipType", "OneToMany"),
            new XElement("ReferencingEntityName", r.Referencing),
            new XElement("ReferencedEntityName", r.Referenced),
            new XElement("CascadeDelete", r.CascadeDelete)))));

    /// <summary>The shape the real solution has: the matter cascade, plus the policy's other parents and children.</summary>
    private static XDocument[] Good(string matterCascade = "Cascade") =>
    [
        Relationships(
            (PolicyMatterRelationship, PolicyTable, "sprk_Matter", matterCascade),
            ("business_unit_sprk_policy", PolicyTable, "BusinessUnit", "Restrict"),
            ("sprk_policy_currentversion", PolicyTable, "sprk_PolicyVersion", "RemoveLink")),
        Relationships(
            // sprk_Policy as the PARENT (referenced) side: a cascade here deletes children, and is not sprk_Policy's own parental cascade.
            ("sprk_policyversion_policy", "sprk_PolicyVersion", PolicyTable, "Cascade"),
            ("sprk_signal_policy", "sprk_Signal", PolicyTable, "RemoveLink"),
            // Another table's parental cascade: not counted.
            ("sprk_analysis_matter", "sprk_Analysis", "sprk_Matter", "Cascade")),
    ];

    [Fact(DisplayName = "Policy cascade guard control (must not fire): the real shape, a parent-side cascade, another table's cascade")]
    public void GuardIsQuietOnTheGoodShape()
    {
        Assert.Empty(Problems(Good()));
    }

    [Theory(DisplayName = "Policy cascade guard control (must fire): sprk_policy_matter not Cascade")]
    [InlineData("RemoveLink")]
    [InlineData("Restrict")]
    [InlineData("NoCascade")]
    public void GuardFiresWhenTheMatterCascadeIsNotCascade(string cascade)
    {
        var problems = Problems(Good(cascade));

        Assert.Contains(problems, p => p.Contains(PolicyMatterRelationship, StringComparison.Ordinal) && p.Contains(cascade, StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("0 relationships", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Policy cascade guard control (must fire): sprk_policy_matter missing")]
    public void GuardFiresWhenTheMatterRelationshipIsMissing()
    {
        var problems = Problems([Relationships(("business_unit_sprk_policy", PolicyTable, "BusinessUnit", "Restrict"))]);

        Assert.Contains(problems, p => p.Contains($"{PolicyMatterRelationship} was not found", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Policy cascade guard control (must fire): a second Delete=Cascade parent of sprk_Policy, whatever the spelling")]
    public void GuardFiresOnASecondParentalCascade()
    {
        var docs = Good().Append(Relationships(("sprk_policy_project", "sprk_policy", "sprk_Project", "Cascade"))).ToArray();

        var problem = Assert.Single(Problems(docs));
        Assert.Contains("2 relationships", problem, StringComparison.Ordinal);
        Assert.Contains("sprk_policy_project", problem, StringComparison.Ordinal);
        Assert.Contains(PolicyMatterRelationship, problem, StringComparison.Ordinal);
    }

    /// <summary>What is wrong with the matter -> rule cascade in <paramref name="relationshipFiles"/>; empty when both rules hold.</summary>
    internal static List<string> Problems(IEnumerable<XDocument> relationshipFiles)
    {
        var all = relationshipFiles.SelectMany(d => d.Descendants().Where(e => e.Name.LocalName == "EntityRelationship")).ToList();
        var problems = new List<string>();

        var matter = all.Where(r => string.Equals((string?)r.Attribute("Name"), PolicyMatterRelationship, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matter.Count == 0)
        {
            problems.Add($"{PolicyMatterRelationship} was not found.");
        }

        foreach (var r in matter.Where(r => !string.Equals(r.Element("CascadeDelete")?.Value, "Cascade", StringComparison.Ordinal)))
        {
            problems.Add($"{PolicyMatterRelationship} has CascadeDelete={r.Element("CascadeDelete")?.Value ?? "(none)"}; it must be Cascade "
                         + "(a matter's delete deletes a rule limited to it; RemoveLink would widen the rule to all matters).");
        }

        var parental = all
            .Where(r => string.Equals(r.Element("ReferencingEntityName")?.Value, PolicyTable, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.Element("CascadeDelete")?.Value, "Cascade", StringComparison.Ordinal))
            .Select(r => (string?)r.Attribute("Name") ?? "(unnamed)")
            .Order(StringComparer.Ordinal)
            .ToList();
        if (parental.Count != 1)
        {
            problems.Add($"{PolicyTable} is the referencing side of {parental.Count} relationships with CascadeDelete=Cascade "
                         + $"({string.Join(", ", parental)}); it must be exactly one, {PolicyMatterRelationship}.");
        }

        return problems;
    }
}
