using System.Xml.Linq;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// The writer identity (<c>Spaarke Ontology Service</c>, the BFF's Signal and Decision Record writer) must hold
/// <c>prvAppendTo&lt;Table&gt;</c> on every table a lookup on <c>sprk_signal</c> or <c>sprk_decisionrecord</c> points at
/// (DI-107, #2046; <c>projects/spaarke-ontology-platform-r1/notes/security-roles.md</c> section 15). Setting a lookup needs
/// Append on the referencing row and AppendTo on the referenced row; without it the create or update fails 0x80040220.
/// Before the grant the writer reached Matter, Project, Invoice and the rest only through the root team's Basic User role.
/// </summary>
/// <remarks>
/// <para><b>Where the targets come from.</b> The lookup attributes in <c>Entities/sprk_Signal/Entity.xml</c> and
/// <c>Entities/sprk_DecisionRecord/Entity.xml</c> carry an empty <c>LookupTypes</c> element, so the target is not there. The solution
/// records each lookup as a OneToMany <c>EntityRelationship</c> under <c>Other/Relationships/*.xml</c> (filed under the referenced
/// entity); this guard reads every such relationship whose <c>ReferencingEntityName</c> is a ledger table and takes its
/// <c>ReferencedEntityName</c>. Ownership and system targets are excluded (owner, team, businessunit, systemuser,
/// transactioncurrency); <c>prvAppendToUser</c> is a separate standing grant.</para>
/// <para><b>Privilege suffix.</b> Mapped through the target's <c>Entities/&lt;Entity&gt;/Entity.xml</c> <c>Name</c> (the schema name),
/// falling back to the relationship's referenced name for a table the solution does not carry. Privilege names are compared
/// case-insensitively: Dataverse resolves them that way, and the repo already holds both <c>prvAppendTosprk_analysisaction</c>
/// (Spaarke AI Analysis Admin) and the live <c>prvAppendTosprk_AnalysisAction</c> spelling.</para>
/// <para><b>Two further sources.</b> Every key of <see cref="TodoRegardingBuilder.SignalAndDecisionRecordRegardingLookupByEntity"/>
/// (the parents the BFF actually writes a regarding lookup to), and <see cref="SubjectTablesWithoutLookup"/>.</para>
/// </remarks>
public class OntologyWriterAppendToGuardTests
{
    private const string WriterRoleFile = "Spaarke Ontology Service.xml";

    private static readonly string[] LedgerTables = ["sprk_signal", "sprk_decisionrecord"];

    /// <summary>Ownership and system lookups: not a table the writer needs its own AppendTo on (user is covered by prvAppendToUser).</summary>
    private static readonly string[] ExcludedTargets = ["owner", "team", "businessunit", "systemuser", "transactioncurrency"];

    /// <summary>
    /// Subject tables a Signal or Decision Record can concern without a lookup to them. ebilling 026 carries a
    /// <c>sprk_billingevent</c> invoice-line subject through the generic core-record pair (<c>sprk_corerecordtype</c> and
    /// <c>sprk_corerecordid</c>), so no relationship names it, yet the writer still needs AppendTo on it.
    /// </summary>
    private static readonly string[] SubjectTablesWithoutLookup = ["sprk_billingevent"];

    /// <summary>
    /// Grants the derivation excludes but the writer still needs: <c>prvAppendToUser</c> covers the <c>systemuser</c> lookups
    /// (<c>sprk_resolvedby</c>, <c>sprk_createdbyperson</c>), so it is asserted directly rather than derived.
    /// </summary>
    private static readonly string[] StandingGrants = ["prvAppendToUser"];

    private static string SolutionRoot => Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster");

    [Fact(DisplayName = "Spaarke Ontology Service holds AppendTo on every Signal / Decision Record lookup target (DI-107)")]
    public void WriterRoleHoldsAppendToOnEveryLookupTarget()
    {
        var required = RequiredPrivileges();
        // Sanity floor: the real lookup targets are 15 tables plus the subject-only table (the regarding map's 9 are among them).
        Assert.True(required.Count >= 16,
            $"Derived only {required.Count} required AppendTo privileges ({string.Join(", ", required)}); the solution scan is probably broken.");

        var role = File.ReadAllText(Path.Combine(SolutionRoot, "Roles", WriterRoleFile));
        var missing = Missing(role, required.Concat(StandingGrants));

        Assert.True(missing.Count == 0,
            "Spaarke Ontology Service is missing AppendTo on a table a Signal / Decision Record lookup points at; the writer's create/update "
            + "fails 0x80040220 for such a record. Add (Global) to src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Ontology Service.xml "
            + "and to the live role:\n  " + string.Join("\n  ", missing));
    }

    [Fact(DisplayName = "Writer AppendTo guard derivation: the lookup targets, the regarding map and the billing-event subject are all required")]
    public void RequiredSetCoversTheKnownTargets()
    {
        var required = RequiredPrivileges();

        // From Other/Relationships (the lookups), including a DecisionRecord-only target (analysisaction) and a system table (Contact).
        foreach (var expected in new[]
        {
            "prvAppendTosprk_analysisaction", "prvAppendTosprk_Matter", "prvAppendTosprk_Document", "prvAppendTosprk_Invoice",
            "prvAppendTosprk_Project", "prvAppendTosprk_RecordType_Ref", "prvAppendTosprk_ServiceRequest", "prvAppendTosprk_Policy",
            "prvAppendTosprk_PolicyVersion", "prvAppendToContact",
        })
        {
            Assert.Contains(expected, required, StringComparer.OrdinalIgnoreCase);
        }

        // Every parent the BFF writes a regarding lookup to.
        foreach (var parent in TodoRegardingBuilder.SignalAndDecisionRecordRegardingLookupByEntity.Keys)
            Assert.Contains("prvAppendTo" + SchemaSuffix(parent, EntitySchemaNames()), required, StringComparer.OrdinalIgnoreCase);

        // The ebilling 026 subject.
        Assert.Contains("prvAppendTosprk_BillingEvent", required, StringComparer.OrdinalIgnoreCase);

        // The exclusions stay out.
        foreach (var excluded in new[] { "prvAppendToUser", "prvAppendToTeam", "prvAppendToBusinessUnit", "prvAppendToOwner", "prvAppendToTransactionCurrency" })
            Assert.DoesNotContain(excluded, required, StringComparer.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Writer AppendTo guard control: removing any one required grant from the real role is reported, exactly that one")]
    public void GuardFiresOnEachRemovedGrant()
    {
        var required = RequiredPrivileges();
        var role = File.ReadAllText(Path.Combine(SolutionRoot, "Roles", WriterRoleFile));

        // Start from a complete in-memory role (the real one plus whatever it lacks), so the control does not depend on the real
        // file's current state: with the grants missing, the real role is the guard's own failure, not this control's.
        var complete = XDocument.Parse(role);
        var privileges = complete.Descendants().First(e => e.Name.LocalName == "RolePrivileges");
        foreach (var lacking in Missing(role, required))
            privileges.Add(new XElement("RolePrivilege", new XAttribute("name", lacking), new XAttribute("level", "Global")));
        var completeXml = complete.ToString();
        Assert.Empty(Missing(completeXml, required));

        foreach (var privilege in required)
        {
            var doc = XDocument.Parse(completeXml);
            var removed = doc.Descendants()
                .Where(e => e.Name.LocalName == "RolePrivilege"
                            && string.Equals((string?)e.Attribute("name"), privilege, StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.True(removed.Count == 1, $"Expected exactly one '{privilege}' in the real role, found {removed.Count}.");
            removed[0].Remove();

            var missing = Missing(doc.ToString(), required);

            var only = Assert.Single(missing);
            Assert.Equal(privilege, only, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact(DisplayName = "Writer AppendTo guard control: the match is by exact name, not by level, prefix, Read or Append")]
    public void GuardMatchesTheExactAppendToName()
    {
        string[] required = ["prvAppendTosprk_Matter"];
        const string lookAlikes = """
            <Role name="Spaarke Ontology Service">
              <RolePrivileges>
                <RolePrivilege name="prvAppendTosprk_MatterType_Ref" level="Global" />
                <RolePrivilege name="prvAppendsprk_Matter" level="Global" />
                <RolePrivilege name="prvReadsprk_Matter" level="Global" />
                <RolePrivilege name="xprvAppendTosprk_Matter" level="Global" />
              </RolePrivileges>
            </Role>
            """;
        const string held = """
            <Role name="Spaarke Ontology Service">
              <RolePrivileges>
                <RolePrivilege name="prvappendtosprk_matter" level="Global" />
              </RolePrivileges>
            </Role>
            """;

        Assert.Equal(["prvAppendTosprk_Matter"], Missing(lookAlikes, required));
        Assert.Empty(Missing(held, required));
    }

    /// <summary>The required privileges the role XML does not grant, in the required set's own spelling.</summary>
    internal static List<string> Missing(string roleXml, IEnumerable<string> required)
    {
        var held = XDocument.Parse(roleXml).Descendants()
            .Where(e => e.Name.LocalName == "RolePrivilege")
            .Select(e => (string?)e.Attribute("name") ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return required.Where(p => !held.Contains(p)).ToList();
    }

    /// <summary>Every <c>prvAppendTo&lt;Table&gt;</c> the writer needs, sorted.</summary>
    internal static List<string> RequiredPrivileges()
    {
        var schemaNames = EntitySchemaNames();
        var tables = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in LookupTargets())
            tables.Add(SchemaSuffix(target, schemaNames));
        foreach (var parent in TodoRegardingBuilder.SignalAndDecisionRecordRegardingLookupByEntity.Keys)
            tables.Add(SchemaSuffix(parent, schemaNames));
        foreach (var subject in SubjectTablesWithoutLookup)
            tables.Add(SchemaSuffix(subject, schemaNames));

        return tables.Select(t => "prvAppendTo" + t).ToList();
    }

    /// <summary>Referenced entity names of every OneToMany relationship whose referencing side is a ledger table, minus the exclusions.</summary>
    private static List<string> LookupTargets()
    {
        var files = Directory.EnumerateFiles(Path.Combine(SolutionRoot, "Other", "Relationships"), "*.xml", SearchOption.TopDirectoryOnly).ToList();
        Assert.True(files.Count >= 50, $"Found only {files.Count} relationship files under {SolutionRoot}\\Other\\Relationships.");

        var targets = new List<string>();
        foreach (var file in files)
        {
            foreach (var rel in XDocument.Load(file).Descendants().Where(e => e.Name.LocalName == "EntityRelationship"))
            {
                var referencing = rel.Element("ReferencingEntityName")?.Value;
                var referenced = rel.Element("ReferencedEntityName")?.Value;
                if (referencing is null || referenced is null
                    || !LedgerTables.Contains(referencing, StringComparer.OrdinalIgnoreCase)
                    || !string.Equals(rel.Element("EntityRelationshipType")?.Value, "OneToMany", StringComparison.Ordinal)
                    || ExcludedTargets.Contains(referenced, StringComparer.OrdinalIgnoreCase))
                    continue;
                targets.Add(referenced);
            }
        }

        return targets.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Entity logical name (lower case) to the schema name its <c>Entity.xml</c> declares.</summary>
    private static Dictionary<string, string> EntitySchemaNames()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(SolutionRoot, "Entities"), "Entity.xml", SearchOption.AllDirectories))
        {
            var name = XDocument.Load(file).Root?.Element("Name")?.Value;
            if (!string.IsNullOrWhiteSpace(name))
                map[name] = name;
        }

        Assert.True(map.Count >= 50, $"Found only {map.Count} Entity.xml files under {SolutionRoot}\\Entities.");
        return map;
    }

    private static string SchemaSuffix(string logicalOrSchemaName, Dictionary<string, string> schemaNames) =>
        schemaNames.TryGetValue(logicalOrSchemaName, out var schema) ? schema : logicalOrSchemaName;
}
