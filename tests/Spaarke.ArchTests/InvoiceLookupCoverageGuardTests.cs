using System.Xml.Linq;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 130-0b F3b (K-a): EVERY lookup to <c>sprk_invoice</c> in the solution source is either decided by the invoice
/// visibility rule or excluded here with a one-line reason; a new lookup that is neither fails the build.
/// </summary>
/// <remarks>
/// <para><b>The source.</b> The lookups are read from <c>src/dataverse/solutions/SpaarkeMaster</c>: every <c>Entity.xml</c> attribute of
/// type <c>lookup</c> is a candidate, and its target comes from the relationship files (<c>Other/Relationships/*.xml</c>:
/// <c>ReferencingEntityName</c>, <c>ReferencingAttributeName</c>, <c>ReferencedEntityName</c>), because the entity files carry no
/// lookup target. A relationship whose attribute is not a lookup in the entity's <c>Entity.xml</c> fails (the scan went stale).</para>
/// <para><b>Decided.</b> The lookup is an invoice link column of a table the rule covers: a child, root or descendant-linked table
/// (<see cref="InvoiceLinks.Tables"/>) or a table of the invoice family (<see cref="ExternalInvoiceVisibility.FamilyTables"/>, its invoice
/// or line link).</para>
/// <para><b>Excluded.</b> <see cref="Exclusions"/>: a lookup that is no invoice link a caller reads rows by, each with its reason. The
/// ratchet: an exclusion for a lookup that no longer exists, or that has become decided, fails, so the list only shrinks.</para>
/// <para><b>Controls.</b> <see cref="Unclassified"/> and <see cref="MissingFromEntityFiles"/> are pure; a synthetic unlisted lookup and a
/// relationship lookup dropped from the entity attributes MUST be reported (must fire), and the real lookups with the real classification
/// MUST NOT (must not fire).</para>
/// <para><b>Limit.</b> "Decided" proves REGISTRY MEMBERSHIP only: the column is listed by <see cref="InvoiceLinks"/> or the invoice family.
/// It does not prove that every read path applies the rule to that table; that is held by the behaviour tests of each plane
/// (<c>InvoiceLinkedDescendantsTests</c>, <c>ExternalInvoiceLinkedDescendantRouteTests</c> and the route and handler tests).</para>
/// </remarks>
public class InvoiceLookupCoverageGuardTests
{
    private const string InvoiceTable = "sprk_invoice";

    /// <summary>Lookups to <c>sprk_invoice</c> that are not decided by the rule, each with the reason (table.column, logical names).</summary>
    private static readonly IReadOnlyDictionary<string, string> Exclusions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["sprk_externalrecordaccess.sprk_invoice"] =
            "the access grant row (who may open the invoice): read by the access engine, never returned to a caller as content",
        ["contact.sprk_invoice"] =
            "no writer in src; live count 0 (per RecordContainerResolver.cs:1938); re-decide when a writer is added",
        ["sprk_organization.sprk_invoice"] =
            "no writer in src; live count 0 (per RecordContainerResolver.cs:1938); re-decide when a writer is added",
        ["sprk_invoice.sprk_supersedesinvoice"] =
            "the invoice's link to the invoice it supersedes: the row is itself an invoice, decided by the invoice rule, and each invoice is decided on its own",
    };

    private static string SolutionRoot => Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster");

    /// <summary>Every (table, column) lookup to <c>sprk_invoice</c>, lower-cased logical names.</summary>
    private static IReadOnlyList<(string Table, string Column)> LookupsToInvoice()
    {
        var found = new List<(string, string)>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(SolutionRoot, "Other", "Relationships"), "*.xml"))
        {
            foreach (var relationship in XDocument.Load(file).Descendants("EntityRelationship"))
            {
                if (!string.Equals((string?)relationship.Element("ReferencedEntityName"), InvoiceTable, StringComparison.OrdinalIgnoreCase))
                    continue;
                var table = ((string?)relationship.Element("ReferencingEntityName"))?.ToLowerInvariant();
                var column = ((string?)relationship.Element("ReferencingAttributeName"))?.ToLowerInvariant();
                if (table is not null && column is not null)
                    found.Add((table, column));
            }
        }

        return found.Distinct().OrderBy(l => l.Item1, StringComparer.Ordinal).ThenBy(l => l.Item2, StringComparer.Ordinal).ToList();
    }

    /// <summary>The (table, column) pairs that are lookup attributes in some <c>Entity.xml</c> of the solution (lower-cased).</summary>
    private static IReadOnlySet<(string Table, string Column)> LookupAttributesInEntityFiles()
    {
        var set = new HashSet<(string, string)>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(SolutionRoot, "Entities"), "Entity.xml", SearchOption.AllDirectories))
        {
            var entity = new DirectoryInfo(Path.GetDirectoryName(file)!).Name.ToLowerInvariant();
            foreach (var attribute in XDocument.Load(file).Descendants("attribute"))
            {
                if (!string.Equals((string?)attribute.Element("Type"), "lookup", StringComparison.OrdinalIgnoreCase))
                    continue;
                var column = ((string?)attribute.Element("LogicalName") ?? (string?)attribute.Attribute("PhysicalName"))?.ToLowerInvariant();
                if (column is not null)
                    set.Add((entity, column));
            }
        }

        return set;
    }

    /// <summary>Whether the invoice rule decides rows of <paramref name="table"/> by <paramref name="column"/>.</summary>
    private static bool IsDecided(string table, string column) =>
        (InvoiceLinks.Tables.TryGetValue(table, out var linked) && linked.LinkColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
        || (ExternalInvoiceVisibility.FamilyTables.TryGetValue(table, out var family)
            && new[] { family.InvoiceLink, family.LineLink }
                .Any(link => link is not null && string.Equals(link, $"_{column}_value", StringComparison.OrdinalIgnoreCase)));

    /// <summary>The lookups that are neither decided (<paramref name="decided"/>) nor excluded (<paramref name="excluded"/>).</summary>
    internal static IReadOnlyList<string> Unclassified(
        IEnumerable<(string Table, string Column)> lookups, Func<string, string, bool> decided, IReadOnlySet<string> excluded) =>
        lookups.Where(l => !decided(l.Table, l.Column) && !excluded.Contains($"{l.Table}.{l.Column}"))
            .Select(l => $"{l.Table}.{l.Column}").ToList();

    private static readonly IReadOnlySet<string> ExcludedKeys = new HashSet<string>(Exclusions.Keys, StringComparer.OrdinalIgnoreCase);

    [Fact(DisplayName = "K-a: every lookup to sprk_invoice in the solution source is decided by the invoice rule or excluded with a reason")]
    public void EveryLookupToInvoice_IsDecidedOrExcluded()
    {
        var lookups = LookupsToInvoice();
        Assert.True(lookups.Count >= 20, $"The relationship scan found only {lookups.Count} lookup(s) to sprk_invoice (expected 22 or more); the scan is stale.");

        var unclassified = Unclassified(lookups, IsDecided, ExcludedKeys);

        Assert.True(unclassified.Count == 0,
            "These lookups to sprk_invoice are neither decided by InvoiceLinks / the invoice family nor excluded in InvoiceLookupCoverageGuardTests.Exclusions "
            + "(add the table to the rule, or exclude it with a reason): " + string.Join(", ", unclassified));
    }

    [Fact(DisplayName = "K-a: every relationship's lookup is a lookup attribute in its entity's Entity.xml (the scan is not stale)")]
    public void EveryRelationshipLookup_IsALookupAttributeInEntityXml()
    {
        var missing = MissingFromEntityFiles(LookupsToInvoice(), LookupAttributesInEntityFiles());

        Assert.True(missing.Count == 0, "Relationship lookups with no lookup attribute in Entity.xml: " + string.Join(", ", missing));
    }

    /// <summary>The relationship lookups with no lookup attribute in the entity files (pure, for the stale-scan control).</summary>
    internal static IReadOnlyList<string> MissingFromEntityFiles(
        IEnumerable<(string Table, string Column)> lookups, IReadOnlySet<(string Table, string Column)> attributes) =>
        lookups.Where(l => !attributes.Contains(l)).Select(l => $"{l.Table}.{l.Column}").ToList();

    [Fact(DisplayName = "K-a stale-scan control must fire: a relationship lookup whose Entity.xml attribute is dropped is reported; the real set is not")]
    public void Control_StaleScan_MustFire_WhenAnEntityLookupIsDropped()
    {
        var lookups = LookupsToInvoice();
        var attributes = LookupAttributesInEntityFiles();
        var dropped = lookups.First(l => l.Table == "sprk_todo" && l.Column == "sprk_regardinginvoice");
        var withoutOne = attributes.Where(a => a != dropped).ToHashSet();

        Assert.Equal(new[] { "sprk_todo.sprk_regardinginvoice" }, MissingFromEntityFiles(lookups, withoutOne));
        Assert.Empty(MissingFromEntityFiles(lookups, attributes));
    }

    [Fact(DisplayName = "K-a ratchet: every exclusion names a real lookup that the rule does not decide")]
    public void EveryExclusion_IsARealUndecidedLookup()
    {
        var lookups = LookupsToInvoice().Select(l => $"{l.Table}.{l.Column}").ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stale = Exclusions.Keys.Where(k => !lookups.Contains(k)).ToList();
        var nowDecided = Exclusions.Keys.Where(k => k.Split('.') is [var table, var column] && IsDecided(table, column)).ToList();

        Assert.True(stale.Count == 0, "Exclusions for lookups that no longer exist: " + string.Join(", ", stale));
        Assert.True(nowDecided.Count == 0, "Exclusions for lookups the rule now decides (remove them): " + string.Join(", ", nowDecided));
        Assert.All(Exclusions.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
    }

    [Fact(DisplayName = "K-a control must fire: a synthetic lookup to sprk_invoice that is neither decided nor excluded is reported")]
    public void Control_MustFire_OnAnUnlistedLookup()
    {
        var lookups = LookupsToInvoice().Append(("sprk_unlistedthing", "sprk_regardinginvoice")).ToList();

        var unclassified = Unclassified(lookups, IsDecided, ExcludedKeys);

        Assert.Equal(new[] { "sprk_unlistedthing.sprk_regardinginvoice" }, unclassified);
    }

    [Fact(DisplayName = "K-a control must fire: a lookup on a table the rule covers by ANOTHER column is still reported")]
    public void Control_MustFire_OnAnotherColumnOfACoveredTable()
    {
        var lookups = new[] { ("sprk_document", "sprk_someotherinvoice") };

        Assert.Equal(new[] { "sprk_document.sprk_someotherinvoice" }, Unclassified(lookups, IsDecided, ExcludedKeys));
    }

    [Fact(DisplayName = "K-a control must not fire: the decided and the excluded lookups are not reported, and the child, family and root classes are all decided")]
    public void Control_MustNotFire_OnDecidedAndExcludedLookups()
    {
        var decidedExamples = new[]
        {
            ("sprk_todo", "sprk_regardinginvoice"), ("sprk_document", "sprk_invoice"), ("sprk_document", "sprk_relatedinvoice"),
            ("sprk_servicerequest", "sprk_regardinginvoice"), ("sprk_signal", "sprk_regardinginvoice"), ("sprk_storedfile", "sprk_invoice"),
            ("sprk_workassignment", "sprk_regardinginvoice"), ("sprk_billingevent", "sprk_invoice"),
        };

        Assert.Empty(Unclassified(decidedExamples, IsDecided, ExcludedKeys));
        Assert.Empty(Unclassified(new[] { ("contact", "sprk_invoice") }, IsDecided, ExcludedKeys));
        Assert.Empty(Unclassified(LookupsToInvoice(), IsDecided, ExcludedKeys));
    }

    [Fact(DisplayName = "K-a: a service request follows the child rule (its invoice link is decided, not a root)")]
    public void TheServiceRequest_IsADecidedChild()
    {
        Assert.True(IsDecided("sprk_servicerequest", "sprk_regardinginvoice"));
        Assert.True(ExternalInvoiceVisibility.IsInvoiceLinkedChildTable("sprk_servicerequest"));
        Assert.False(InvoiceLinks.IsRootWithInvoiceLink("sprk_servicerequest"));
    }
}
