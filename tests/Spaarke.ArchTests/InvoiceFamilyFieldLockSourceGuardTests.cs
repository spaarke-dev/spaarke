using System.Xml.Linq;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// #2859 (#2799, unified-access-control-r2; owner decision 2026-11-16 "path A"): the invoice-family field lock ships in the
/// SpaarkeMaster solution SOURCE, so a new stamp gets it from the import alone (provisioning H7b S27 only verifies it). The source
/// must say exactly what <c>scripts/Set-DocumentPointerFieldSecurity.ps1 -Target InvoiceFamilyLinks</c> applies live, for exactly
/// the columns of THE ONE LIST (<c>scripts/common/InvoiceFamilyLockedColumns.psd1</c>), both ways:
/// <list type="bullet">
/// <item>every listed column is in its table's <c>Entity.xml</c> once, with <c>IsSecured</c> 1;</item>
/// <item>it has exactly one permission in "Spaarke BFF-Managed Field Readers" (read 4, create 0, update 0) and one in
/// "Spaarke BFF-Managed Field Writers" (read 4, create 4, update 4), and no other profile names it;</item>
/// <item>nothing else in the source is family-shaped: every lookup (a relationship in <c>Other/Relationships</c>) from a family
/// table to a table the family's locked lookups name (<see cref="InvoiceFamilyLockedWrite.LockedColumns"/>' targets: matter,
/// project, work assignment, invoice) is on the list, and every listed column is such a lookup.</item>
/// </list>
/// <see cref="InvoiceFamilyFieldLockGuardTests"/> pins the list to the BFF; this pins the source to the list.
/// Enforced by: these ArchTests (CI and the Stop hook).
/// </summary>
public class InvoiceFamilyFieldLockSourceGuardTests
{
    internal const string ReaderProfileId = "42e542c8-47bf-f111-aaaf-0022482913fc";
    internal const string ReaderProfileName = "Spaarke BFF-Managed Field Readers";
    internal const string WriterProfileId = "917b64c5-47bf-f111-a05b-3833c5e9614d";
    internal const string WriterProfileName = "Spaarke BFF-Managed Field Writers";

    private static string SolutionRoot => Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster");

    private static string ListPath => Path.Combine(SourceScan.RepoRoot, "scripts", "common", "InvoiceFamilyLockedColumns.psd1");

    /// <summary>The solution source the guard reads: each family table's Entity.xml (by lower-case table), the profiles, the relationships.</summary>
    internal sealed record SourceInput(
        IReadOnlyDictionary<string, XDocument> Entities,
        XDocument Profiles,
        IReadOnlyList<XDocument> Relationships);

    /// <summary>The grant a profile must hold on a locked column (what the script's Grant-Permission writes: canread 4, cancreate = canupdate).</summary>
    private static readonly IReadOnlyDictionary<string, (string Name, string Read, string Create, string Update, string ReadUnmasked)> Expected =
        new Dictionary<string, (string, string, string, string, string)>(StringComparer.Ordinal)
        {
            [ReaderProfileId] = (ReaderProfileName, "4", "0", "0", "0"),
            [WriterProfileId] = (WriterProfileName, "4", "4", "4", "0"),
        };

    private static string Norm(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string ProfileId(XElement profile) => Norm((string?)profile.Attribute("fieldsecurityprofileid")).Trim('{', '}');

    /// <summary>Loads the real SpaarkeMaster source for <paramref name="tables"/> (entity folders matched ignoring case: sprk_Invoice, sprk_BillingEvent).</summary>
    internal static SourceInput LoadSource(IEnumerable<string> tables)
    {
        var directories = Directory.GetDirectories(Path.Combine(SolutionRoot, "Entities"));
        var entities = new Dictionary<string, XDocument>(StringComparer.Ordinal);
        foreach (var table in tables.Distinct(StringComparer.Ordinal))
        {
            var directory = directories.SingleOrDefault(d => string.Equals(Path.GetFileName(d), table, StringComparison.OrdinalIgnoreCase));
            var file = directory is null ? null : Path.Combine(directory, "Entity.xml");
            if (file is not null && File.Exists(file))
                entities[table] = XDocument.Load(file);
        }

        var relationships = Directory.GetFiles(Path.Combine(SolutionRoot, "Other", "Relationships"), "*.xml")
            .Select(f => XDocument.Load(f))
            .ToList();
        return new SourceInput(entities, XDocument.Load(Path.Combine(SolutionRoot, "Other", "FieldSecurityProfiles.xml")), relationships);
    }

    /// <summary>The family-shaped lookups of the source: (table.column, target) for each relationship from a family table to a family target.</summary>
    internal static IReadOnlyDictionary<string, string> FamilyShapedLookups(
        IReadOnlySet<string> familyTables, IReadOnlySet<string> familyTargets, IEnumerable<XDocument> relationships)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relationship in relationships.SelectMany(r => r.Descendants("EntityRelationship")))
        {
            var from = Norm((string?)relationship.Element("ReferencingEntityName"));
            var to = Norm((string?)relationship.Element("ReferencedEntityName"));
            var column = Norm((string?)relationship.Element("ReferencingAttributeName"));
            if (familyTables.Contains(from) && familyTargets.Contains(to) && column.Length > 0)
                found[$"{from}.{column}"] = to;
        }

        return found;
    }

    /// <summary>Every way the source differs from the one list's lock, both ways. Empty when the source ships exactly the lock.</summary>
    internal static List<string> SourceProblems(IReadOnlySet<string> listPairs, IReadOnlySet<string> familyTargets, SourceInput source)
    {
        var problems = new List<string>();
        var familyTables = listPairs.Select(p => p.Split('.', 2)[0]).ToHashSet(StringComparer.Ordinal);

        var profiles = source.Profiles.Descendants("FieldSecurityProfile").ToList();
        foreach (var (id, expected) in Expected.OrderBy(e => e.Value.Name, StringComparer.Ordinal))
        {
            var profile = profiles.Where(p => ProfileId(p) == id).ToList();
            if (profile.Count != 1)
                problems.Add($"profile {{{id}}} ('{expected.Name}') appears {profile.Count} times in FieldSecurityProfiles.xml, expected once");
            else if (!string.Equals((string?)profile[0].Attribute("name"), expected.Name, StringComparison.Ordinal))
                problems.Add($"profile {{{id}}} is named '{(string?)profile[0].Attribute("name")}', expected '{expected.Name}'");
        }

        var permissions = profiles
            .SelectMany(p => p.Descendants("FieldPermission").Select(f => (Profile: p, Permission: f)))
            .Select(x => (x.Profile, x.Permission, Pair: $"{Norm((string?)x.Permission.Element("EntityName"))}.{Norm((string?)x.Permission.Element("AttributeName"))}"))
            .ToList();

        foreach (var pair in listPairs.Order(StringComparer.Ordinal))
        {
            var (table, column) = (pair.Split('.', 2)[0], pair.Split('.', 2)[1]);

            // (1) IsSecured=1 in the table's Entity.xml.
            if (!source.Entities.TryGetValue(table, out var entity))
            {
                problems.Add($"{pair}: no Entities/{table}/Entity.xml in the SpaarkeMaster source");
            }
            else
            {
                var attributes = entity.Descendants("attribute").Where(a => Norm((string?)a.Element("LogicalName")) == column).ToList();
                if (attributes.Count != 1)
                    problems.Add($"{pair}: {attributes.Count} attribute definitions in Entity.xml, expected 1");
                else if ((string?)attributes[0].Element("IsSecured") != "1")
                    problems.Add($"{pair} is IsSecured={(string?)attributes[0].Element("IsSecured") ?? "(none)"} in Entity.xml, expected 1");
            }

            // (2) Exactly the reader and the writer grant, at exactly those values; no other profile.
            var onPair = permissions.Where(p => p.Pair == pair).ToList();
            foreach (var (id, expected) in Expected.OrderBy(e => e.Value.Name, StringComparer.Ordinal))
            {
                var mine = onPair.Where(p => ProfileId(p.Profile) == id).Select(p => p.Permission).ToList();
                if (mine.Count != 1)
                {
                    problems.Add($"{pair}: '{expected.Name}' has {mine.Count} permissions, expected 1");
                    continue;
                }

                var actual = ((string?)mine[0].Element("CanRead"), (string?)mine[0].Element("CanCreate"), (string?)mine[0].Element("CanUpdate"), (string?)mine[0].Element("CanReadUnmasked"));
                if (actual != (expected.Read, expected.Create, expected.Update, expected.ReadUnmasked))
                {
                    problems.Add($"{pair}: '{expected.Name}' is read={actual.Item1} create={actual.Item2} update={actual.Item3} unmasked={actual.Item4}; "
                        + $"expected read={expected.Read} create={expected.Create} update={expected.Update} unmasked={expected.ReadUnmasked}");
                }
            }

            foreach (var other in onPair.Where(p => !Expected.ContainsKey(ProfileId(p.Profile))))
                problems.Add($"{pair}: profile '{(string?)other.Profile.Attribute("name")}' names it; only '{ReaderProfileName}' and '{WriterProfileName}' may");
        }

        // (3) Both ways against the source's family-shaped lookups.
        var shaped = FamilyShapedLookups(familyTables, familyTargets, source.Relationships);
        foreach (var (pair, target) in shaped.Where(s => !listPairs.Contains(s.Key)).OrderBy(s => s.Key, StringComparer.Ordinal))
            problems.Add($"{pair} is a lookup from a family table to {target} in the source but not in the one list (InvoiceFamilyLockedColumns.psd1)");
        foreach (var pair in listPairs.Where(p => !shaped.ContainsKey(p)).Order(StringComparer.Ordinal))
            problems.Add($"{pair} is on the one list but the source has no relationship from it to a family target ({string.Join(", ", familyTargets.Order(StringComparer.Ordinal))})");

        return problems;
    }

    private static IReadOnlySet<string> ListPairs() => InvoiceFamilyFieldLockGuardTests.ParseList(File.ReadAllText(ListPath)).Pairs;

    private static IReadOnlySet<string> FamilyTargets() =>
        InvoiceFamilyLockedWrite.LockedColumns.Select(c => c.TargetTable).ToHashSet(StringComparer.Ordinal);

    [Fact(DisplayName = "#2859: SpaarkeMaster source secures exactly the one list's 24 columns, with the reader (4/0/0) and writer (4/4/4) grants only, and nothing else is family-shaped")]
    public void TheSolutionSource_ShipsExactlyTheInvoiceFamilyLock()
    {
        var pairs = ListPairs();
        var targets = FamilyTargets();
        Assert.True(pairs.Count == 24, $"{ListPath} parsed to {pairs.Count} columns, not 24 — its format changed");
        Assert.True(targets.SetEquals(["sprk_invoice", "sprk_matter", "sprk_project", "sprk_workassignment"]),
            "the family's locked lookups name " + string.Join(", ", targets.Order(StringComparer.Ordinal)));

        var source = LoadSource(pairs.Select(p => p.Split('.', 2)[0]));
        Assert.True(source.Relationships.Count > 50, $"only {source.Relationships.Count} relationship files were read — the source path is wrong");
        Assert.Equal(24, FamilyShapedLookups(pairs.Select(p => p.Split('.', 2)[0]).ToHashSet(StringComparer.Ordinal), targets, source.Relationships).Count);

        var problems = SourceProblems(pairs, targets, source);
        Assert.True(problems.Count == 0,
            "The invoice-family field lock (#2799) ships in SpaarkeMaster (#2859): every column of scripts/common/InvoiceFamilyLockedColumns.psd1 "
            + "is IsSecured=1 in its Entity.xml, and Other/FieldSecurityProfiles.xml grants it to 'Spaarke BFF-Managed Field Readers' "
            + "(read 4) and 'Spaarke BFF-Managed Field Writers' (read/create/update 4) only — what "
            + "Set-DocumentPointerFieldSecurity.ps1 -Target InvoiceFamilyLinks applies live. Problems:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "#2859 control: the real source with one column flipped to IsSecured=0 fails (must fire); unchanged it passes (must not fire)")]
    public void TheRealSource_WithOneColumnUnsecured_Fails()
    {
        var pairs = ListPairs();
        var targets = FamilyTargets();
        var source = LoadSource(pairs.Select(p => p.Split('.', 2)[0]));
        Assert.Empty(SourceProblems(pairs, targets, source));

        // A scratch copy of sprk_invoice's Entity.xml, sprk_matter flipped to IsSecured=0.
        var scratch = new XDocument(source.Entities["sprk_invoice"]);
        scratch.Descendants("attribute").Single(a => (string?)a.Element("LogicalName") == "sprk_matter").Element("IsSecured")!.Value = "0";
        var entities = new Dictionary<string, XDocument>(source.Entities, StringComparer.Ordinal) { ["sprk_invoice"] = scratch };

        var problems = SourceProblems(pairs, targets, source with { Entities = entities });
        Assert.Equal(new[] { "sprk_invoice.sprk_matter is IsSecured=0 in Entity.xml, expected 1" }, problems);
    }

    // ── Synthetic input ──────────────────────────────────────────────────────────────────────────────────────────

    private static readonly IReadOnlySet<string> SyntheticPairs = new HashSet<string>(StringComparer.Ordinal) { "sprk_invoice.sprk_matter", "sprk_billingevent.sprk_invoice" };

    private static readonly IReadOnlySet<string> SyntheticTargets = new HashSet<string>(StringComparer.Ordinal) { "sprk_matter", "sprk_invoice" };

    private static XDocument Entity(params (string Column, string Secured)[] columns) =>
        new(new XElement("Entity", new XElement("EntityInfo", new XElement("entity", new XElement("attributes",
            columns.Select(c => new XElement("attribute", new XAttribute("PhysicalName", c.Column),
                new XElement("Type", "lookup"), new XElement("Name", c.Column), new XElement("LogicalName", c.Column), new XElement("IsSecured", c.Secured))))))));

    private static XElement Permission(string table, string column, int read, int create, int update) =>
        new("FieldPermission", new XElement("EntityName", table), new XElement("AttributeName", column),
            new XElement("CanRead", read), new XElement("CanUpdate", update), new XElement("CanCreate", create), new XElement("CanReadUnmasked", 0));

    private static XElement Profile(string id, string name, params XElement[] permissions) =>
        new("FieldSecurityProfile", new XAttribute("name", name), new XAttribute("fieldsecurityprofileid", $"{{{id}}}"),
            new XElement("FieldPermissions", permissions));

    private static XDocument Relationships(params (string From, string Column, string To)[] lookups) =>
        new(new XElement("EntityRelationships", lookups.Select(l => new XElement("EntityRelationship",
            new XElement("ReferencingEntityName", l.From), new XElement("ReferencedEntityName", l.To), new XElement("ReferencingAttributeName", l.Column)))));

    /// <summary>The faithful synthetic source, plus things the guard must NOT count as the family: a secured non-family column on a family
    /// table with the same grants, a family table's lookup to a non-family target, and a non-family table's lookup to an invoice.</summary>
    private static (Dictionary<string, XDocument> Entities, List<XElement> Reader, List<XElement> Writer, List<XElement> Other, List<(string, string, string)> Lookups) Faithful() =>
    (
        new Dictionary<string, XDocument>(StringComparer.Ordinal)
        {
            ["sprk_invoice"] = Entity(("sprk_matter", "1"), ("sprk_totalamount", "1"), ("sprk_vendororg", "0")),
            ["sprk_billingevent"] = Entity(("sprk_invoice", "1")),
        },
        [Permission("sprk_invoice", "sprk_matter", 4, 0, 0), Permission("sprk_billingevent", "sprk_invoice", 4, 0, 0), Permission("sprk_invoice", "sprk_totalamount", 4, 0, 0)],
        [Permission("sprk_invoice", "sprk_matter", 4, 4, 4), Permission("sprk_billingevent", "sprk_invoice", 4, 4, 4), Permission("sprk_invoice", "sprk_totalamount", 4, 4, 4)],
        [Permission("sprk_analysis", "sprk_regardinginvoice", 4, 4, 4)],
        [("sprk_Invoice", "sprk_Matter", "sprk_Matter"), ("sprk_BillingEvent", "sprk_Invoice", "sprk_Invoice"),
         ("sprk_Invoice", "sprk_VendorOrg", "sprk_Organization"), ("sprk_analysis", "sprk_RegardingInvoice", "sprk_Invoice")]
    );

    private static SourceInput Build(
        Dictionary<string, XDocument> entities, List<XElement> reader, List<XElement> writer, List<XElement> other, List<(string, string, string)> lookups) =>
        new(entities,
            new XDocument(new XElement("FieldSecurityProfiles",
                Profile(ReaderProfileId, ReaderProfileName, reader.ToArray()),
                Profile(WriterProfileId, WriterProfileName, writer.ToArray()),
                Profile("5d02ed7f-89be-f111-aaaf-0022482913fc", "Spaarke Identity Link Readers", other.ToArray()))),
            [Relationships(lookups.ToArray())]);

    [Fact(DisplayName = "#2859 control: each seeded drift of the source is reported (must fire); the faithful source and non-family columns are not (must not fire)")]
    public void SourceProblems_MustFireAndMustNotFire()
    {
        List<string> Run(Action<Dictionary<string, XDocument>, List<XElement>, List<XElement>, List<XElement>, List<(string, string, string)>> seed)
        {
            var (e, r, w, o, l) = Faithful();
            seed(e, r, w, o, l);
            return SourceProblems(SyntheticPairs, SyntheticTargets, Build(e, r, w, o, l));
        }

        // Must not fire: the faithful source (with its non-family secured column, non-family lookups and another profile on another table).
        Assert.Empty(Run((_, _, _, _, _) => { }));

        void Fires(string expectedStart, Action<Dictionary<string, XDocument>, List<XElement>, List<XElement>, List<XElement>, List<(string, string, string)>> seed)
        {
            var problems = Run(seed);
            Assert.True(problems.Any(p => p.StartsWith(expectedStart, StringComparison.Ordinal)),
                $"expected a problem starting '{expectedStart}'; got:\n" + string.Join("\n", problems));
        }

        // (1) Entity.xml.
        Fires("sprk_invoice.sprk_matter is IsSecured=0 in Entity.xml", (e, _, _, _, _) => e["sprk_invoice"] = Entity(("sprk_matter", "0"), ("sprk_totalamount", "1")));
        Fires("sprk_invoice.sprk_matter: 0 attribute definitions", (e, _, _, _, _) => e["sprk_invoice"] = Entity(("sprk_totalamount", "1")));
        Fires("sprk_invoice.sprk_matter: 2 attribute definitions", (e, _, _, _, _) => e["sprk_invoice"] = Entity(("sprk_matter", "1"), ("sprk_matter", "1")));
        Fires("sprk_billingevent.sprk_invoice: no Entities/sprk_billingevent/Entity.xml", (e, _, _, _, _) => e.Remove("sprk_billingevent"));

        // (2) The profile entries.
        Fires("sprk_invoice.sprk_matter: 'Spaarke BFF-Managed Field Readers' is read=4 create=4 update=0", (_, r, _, _, _) => r[0] = Permission("sprk_invoice", "sprk_matter", 4, 4, 0));
        Fires("sprk_billingevent.sprk_invoice: 'Spaarke BFF-Managed Field Writers' is read=4 create=4 update=0", (_, _, w, _, _) => w[1] = Permission("sprk_billingevent", "sprk_invoice", 4, 4, 0));
        Fires("sprk_billingevent.sprk_invoice: 'Spaarke BFF-Managed Field Writers' has 0 permissions", (_, _, w, _, _) => w.RemoveAt(1));
        Fires("sprk_invoice.sprk_matter: 'Spaarke BFF-Managed Field Readers' has 2 permissions", (_, r, _, _, _) => r.Add(Permission("sprk_invoice", "sprk_matter", 4, 0, 0)));
        Fires("sprk_invoice.sprk_matter: profile 'Spaarke Identity Link Readers' names it", (_, _, _, o, _) => o.Add(Permission("sprk_invoice", "sprk_matter", 4, 0, 0)));
        Fires("sprk_invoice.sprk_matter: profile 'Spaarke Identity Link Readers' names it", (_, _, _, o, _) => o.Add(Permission("sprk_Invoice", "sprk_Matter", 0, 0, 0)));

        // (3) Family-shaped lookups, both ways.
        Fires("sprk_invoice.sprk_supersedesinvoice is a lookup from a family table to sprk_invoice in the source but not in the one list",
            (_, _, _, _, l) => l.Add(("sprk_Invoice", "sprk_SupersedesInvoice", "sprk_Invoice")));
        Fires("sprk_billingevent.sprk_invoice is on the one list but the source has no relationship", (_, _, _, _, l) => l.RemoveAt(1));
    }

    [Fact(DisplayName = "#2859 control: a missing or renamed BFF profile is reported (must fire)")]
    public void SourceProblems_MissingProfile_Fires()
    {
        var (e, r, w, o, l) = Faithful();
        var source = Build(e, r, w, o, l);
        source.Profiles.Root!.Elements("FieldSecurityProfile").First(p => ProfileId(p) == WriterProfileId).Attribute("name")!.Value = "Writers";
        Assert.Contains(SourceProblems(SyntheticPairs, SyntheticTargets, source),
            p => p.StartsWith($"profile {{{WriterProfileId}}} is named 'Writers'", StringComparison.Ordinal));

        source.Profiles.Root!.Elements("FieldSecurityProfile").First(p => ProfileId(p) == ReaderProfileId).Remove();
        Assert.Contains(SourceProblems(SyntheticPairs, SyntheticTargets, source),
            p => p.StartsWith($"profile {{{ReaderProfileId}}} ('{ReaderProfileName}') appears 0 times", StringComparison.Ordinal));
    }
}
