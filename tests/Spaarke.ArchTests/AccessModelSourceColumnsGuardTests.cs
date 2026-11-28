using System.Xml.Linq;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 067 (UAC-r2 condition C3): every column the repo's solution XML marks field-secured
/// (<c>&lt;IsSecured&gt;1&lt;/IsSecured&gt;</c> in an <c>Entity.xml</c>) on contact, systemuser, sprk_document, sprk_communication
/// or any table that carries <c>sprk_issecure</c> in any solution file is classified: either an ACCESS-MODEL column (<see cref="AccessModelSourceColumns"/>, never read past the
/// caller's column view by field mapping) or a BUSINESS column (<see cref="BusinessColumns"/>, copied from the row's filing parent
/// whatever the caller can read on it). A new field-secured column on those tables fails here until someone decides which.
/// </summary>
/// <remarks>
/// <para><b>The pinned constant stays the authority.</b> No field-security profile in the repo names these sets, so
/// <see cref="AccessModelSourceColumns"/> is the list; this guard only forces every secured column on the scoped tables to be
/// classified, and pins the list itself (<see cref="TheAccessModelList_IsExactlyThePinnedSet"/>) so removing an entry fails.</para>
/// <para><b>A wrong path cannot pass silently.</b> The scan fails when it finds no <c>Entity.xml</c> for contact or for
/// systemuser, or no field-secured column at all.</para>
/// <para>Enforced by: this source scan (CI and the Stop hook). Must-fire controls: an unclassified secured column on contact, on
/// systemuser and on a table that carries <c>sprk_issecure</c>; a business entry that is on the access-model list; a stale business
/// entry. Must-not-fire controls: the access-model columns, a pinned business column, a secured column on an out-of-scope table, an
/// unsecured column.</para>
/// </remarks>
public class AccessModelSourceColumnsGuardTests
{
    /// <summary>
    /// The field-secured columns on the scoped tables that are BUSINESS data, copied from the filing parent (task 067), keyed
    /// <c>"{table}.{column}"</c> (lower case), each with its reason. Every entry must still be field-secured in the solution XML
    /// (<see cref="StaleBusinessColumns"/>) and must not be on the access-model list.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> BusinessColumns = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sprk_invoice.sprk_matter"] = "the invoice's matter (a filing lookup; the target side keeps its own rules)",
        ["sprk_invoice.sprk_project"] = "the invoice's project (a filing lookup; the target side keeps its own rules)",
        ["sprk_invoice.sprk_regardingworkassignment"] = "the invoice's work assignment (a filing lookup; the target side keeps its own rules)",
        ["sprk_invoice.sprk_supersedesinvoice"] = "the invoice this one supersedes (a business lookup)",
        ["sprk_project.sprk_regardingrecordid"] = "the regarding pair's id (text; the target side is copy-invariant)",
        ["sprk_project.sprk_regardingrecordtype"] = "the regarding pair's type (the target side is copy-invariant)",
        ["sprk_invoice.sprk_createdbyperson"] = "the creator's person (descriptive; the target side is copy-invariant)",
        ["sprk_matter.sprk_createdbyperson"] = "the creator's person (descriptive; the target side is copy-invariant)",
        ["sprk_project.sprk_createdbyperson"] = "the creator's person (descriptive; the target side is copy-invariant)",
        ["sprk_workassignment.sprk_createdbyperson"] = "the creator's person (descriptive; the target side is copy-invariant)",
        ["sprk_document.sprk_createdbycontact"] = "the creator's contact (descriptive; the target side is copy-invariant)",
        ["sprk_document.sprk_createdbyperson"] = "the creator's person (descriptive; the target side is copy-invariant)",
        ["sprk_document.sprk_filename"] = "the file's name (descriptive)",
        ["sprk_document.sprk_relocatedversions"] = "the relocation's version record (the target side is copy-invariant)",
        ["sprk_document.sprk_relocationpending"] = "the relocator's ledger (the target side is copy-invariant)",
        ["sprk_communication.sprk_communicationthread"] = "the message's thread (a lookup; the target side keeps its own rules)",
        ["sprk_communication.sprk_createdbycontact"] = "the creator's contact (descriptive; the target side is copy-invariant)",
        ["sprk_communication.sprk_createdbyperson"] = "the creator's person (descriptive; the target side is copy-invariant)",
        ["sprk_communication.sprk_sentby"] = "who sent the message (descriptive)",
    };

    /// <summary>The access-model list as UAC-r2 condition C3 states it. A change to <see cref="AccessModelSourceColumns"/> must
    /// change this too, so removing an entry is a red build.</summary>
    private static readonly string[] PinnedAccessModelColumns =
    [
        "adx_identity_logonenabled", "adx_identity_passwordhash", "sprk_accessinheritance", "sprk_accesspermissiongrant",
        "sprk_externalaccessremovedon", "sprk_externalinvitecodehash", "sprk_externalinvitedemailhash", "sprk_externalinvitedon",
        "sprk_externalinvitedtenantid", "sprk_externalobjectid", "sprk_externaltenantid", "sprk_graphdriveid", "sprk_graphitemid",
        "sprk_graphitemidbound", "sprk_homeobjectid", "sprk_hometenantid", "sprk_identityplane", "sprk_issecure",
        "sprk_restrictedbysubmission", "sprk_standinggrant",
    ];

    private static readonly string[] AlwaysScopedTables = ["contact", "systemuser", "sprk_document", "sprk_communication"];

    /// <summary>One field-secured column of a scoped table, and where it was found.</summary>
    internal sealed record SecuredColumn(string Table, string Column, string File);

    /// <summary>What the scan of <paramref name="files"/> found: the scoped tables seen, and their field-secured columns.</summary>
    /// <remarks>Two passes, so the scope is decided per TABLE, not per solution file: the first collects every table that carries
    /// <c>sprk_issecure</c> in ANY <c>Entity.xml</c>; the second classifies every field-secured column of a scoped table, from
    /// every file (a solution that declares a secured column of a secure table without repeating <c>sprk_issecure</c> is still
    /// scanned).</remarks>
    internal static (IReadOnlySet<string> TablesSeen, IReadOnlyList<SecuredColumn> Secured) ScanEntities(
        IEnumerable<(string File, string Xml)> files)
    {
        var entities = new List<(string File, string Table, List<(string? Name, bool Secured)> Attributes)>();
        foreach (var (file, xml) in files)
        {
            var document = XDocument.Parse(xml);
            foreach (var entity in document.Descendants().Where(e => e.Name.LocalName == "entity"))
            {
                var table = ((string?)entity.Attribute("Name"))?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(table))
                    continue;

                var attributes = entity.Descendants().Where(e => e.Name.LocalName == "attribute")
                    .Select(a => (
                        Name: a.Elements().FirstOrDefault(e => e.Name.LocalName == "LogicalName")?.Value.Trim().ToLowerInvariant(),
                        Secured: a.Elements().FirstOrDefault(e => e.Name.LocalName == "IsSecured")?.Value.Trim() == "1"))
                    .Where(a => !string.IsNullOrEmpty(a.Name))
                    .ToList();
                entities.Add((file, table, attributes));
            }
        }

        // Pass 1: every table in scope, across all files.
        var scopedTables = new HashSet<string>(AlwaysScopedTables, StringComparer.Ordinal);
        scopedTables.UnionWith(entities.Where(e => e.Attributes.Any(a => a.Name == "sprk_issecure")).Select(e => e.Table));

        // Pass 2: every field-secured column of a scoped table, from every file.
        var tables = new HashSet<string>(StringComparer.Ordinal);
        var secured = new List<SecuredColumn>();
        foreach (var (file, table, attributes) in entities.Where(e => scopedTables.Contains(e.Table)))
        {
            tables.Add(table);
            secured.AddRange(attributes.Where(a => a.Secured).Select(a => new SecuredColumn(table, a.Name!, file)));
        }

        return (tables, secured);
    }

    /// <summary>The secured columns that are neither access-model nor a pinned business column.</summary>
    internal static IReadOnlyList<string> Unclassified(
        IEnumerable<SecuredColumn> secured, IReadOnlyDictionary<string, string> business) =>
        secured
            .Where(c => !AccessModelSourceColumns.Contains(c.Table, c.Column) && !business.ContainsKey($"{c.Table}.{c.Column}"))
            .Select(c => $"{c.Table}.{c.Column} ({c.File})")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>Business entries that no longer hold: no longer field-secured in the XML, on the access-model list, or without a reason.</summary>
    internal static IReadOnlyList<string> StaleBusinessColumns(
        IEnumerable<SecuredColumn> secured, IReadOnlyDictionary<string, string> business)
    {
        var present = secured.Select(c => $"{c.Table}.{c.Column}").ToHashSet(StringComparer.Ordinal);
        return business
            .Where(e => !present.Contains(e.Key) || AccessModelSourceColumns.Contains(e.Key[..e.Key.IndexOf('.')], e.Key[(e.Key.IndexOf('.') + 1)..])
                        || string.IsNullOrWhiteSpace(e.Value))
            .Select(e => e.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<(string File, string Xml)> RepoEntityFiles() =>
        Directory.EnumerateFiles(Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "solutions"), "Entity.xml", SearchOption.AllDirectories)
            .Select(f => (SourceScan.Relative(f).Replace('\\', '/'), File.ReadAllText(f)));

    [Fact(DisplayName = "Task 067 (C3): every field-secured column on contact, systemuser and the sprk_issecure tables is classified")]
    public void EveryFieldSecuredColumnOnTheScopedTables_IsClassified()
    {
        var (tables, secured) = ScanEntities(RepoEntityFiles());

        foreach (var table in AlwaysScopedTables)
            Assert.True(tables.Contains(table), $"No Entity.xml for {table} was found under src/dataverse/solutions; the scan no longer sees it.");
        Assert.True(secured.Count > 0, "The scan found no field-secured column at all; it no longer reads IsSecured.");
        Assert.Contains(secured, c => c.Table == "contact" && c.Column == "sprk_externalinvitecodehash");

        var unclassified = Unclassified(secured, BusinessColumns);
        Assert.True(unclassified.Count == 0,
            "A field-secured column is neither an access-model column (AccessModelSourceColumns) nor a pinned business column " +
            "(AccessModelSourceColumnsGuardTests.BusinessColumns). Decide which: an access-model column is never copied past the " +
            "caller's view; a business column is copied from the filing parent.\n" + string.Join("\n", unclassified));
    }

    [Fact(DisplayName = "Task 067 (C3): every pinned business column is still field-secured and is not an access-model column")]
    public void EveryBusinessColumn_IsStillUsed()
    {
        var stale = StaleBusinessColumns(ScanEntities(RepoEntityFiles()).Secured, BusinessColumns);
        Assert.True(stale.Count == 0, "Fix or remove these BusinessColumns entries (the ratchet): " + string.Join(", ", stale));
    }

    [Fact(DisplayName = "Task 067 (C3): the access-model list is exactly the pinned set, with the identity-collision prefix")]
    public void TheAccessModelList_IsExactlyThePinnedSet()
    {
        Assert.Equal(PinnedAccessModelColumns, AccessModelSourceColumns.Exact.Select(c => c.ToLowerInvariant()).Order(StringComparer.Ordinal));
        Assert.Equal("sprk_identitycollision", AccessModelSourceColumns.IdentityCollisionPrefix);

        // Must fire: the exact names and the prefix on every table (any table, none at all), spelled any way.
        Assert.True(AccessModelSourceColumns.Contains("contact", "adx_identity_passwordhash"));
        Assert.True(AccessModelSourceColumns.Contains("sprk_matter", " SPRK_IsSecure "));
        Assert.True(AccessModelSourceColumns.Contains(null, "sprk_identitycollisionreason"));
        Assert.True(AccessModelSourceColumns.Contains("account", "sprk_graphdriveid"));
        Assert.True(AccessModelSourceColumns.Contains("sprk_document", " SPRK_RestrictedBySubmission "));

        // Must fire: a column the access-column registry lists, on the table it lists it for (any spelling of either), and
        // every registry entry on its own table.
        Assert.True(AccessModelSourceColumns.Contains("team", "name"));
        Assert.True(AccessModelSourceColumns.Contains(" BusinessUnit ", " Name "));
        Assert.True(AccessModelSourceColumns.Contains("systemuser", " ParentSystemUserId "));
        foreach (var (table, columns) in Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission.AccessColumns)
        {
            Assert.All(columns, column => Assert.True(AccessModelSourceColumns.Contains(table, column),
                $"{table}.{column} is in the access-column registry but not denied"));
        }

        // Must not fire: a registry name on a table the registry does not list it for (the filing parent's own columns are
        // copied), or with no table; a business column; a name that only contains an access-model name; nothing.
        Assert.False(AccessModelSourceColumns.Contains("sprk_matter", "name"));
        Assert.False(AccessModelSourceColumns.Contains("account", "name"));
        Assert.False(AccessModelSourceColumns.Contains("sprk_event", "sprk_regardingmatter"));
        Assert.False(AccessModelSourceColumns.Contains(null, "parentsystemuserid"));
        Assert.False(AccessModelSourceColumns.Contains("sprk_matter", "sprk_clientname"));
        Assert.False(AccessModelSourceColumns.Contains("sprk_matter", "sprk_issecurebackup_x"));
        Assert.False(AccessModelSourceColumns.Contains("contact", "x_sprk_identitycollision"));
        Assert.False(AccessModelSourceColumns.Contains("contact", null));
    }

    private static string EntityXml(string table, params (string Column, bool Secured)[] attributes) =>
        $"""
        <Entity><EntityInfo><entity Name="{table}"><attributes>
        {string.Concat(attributes.Select(a => $"<attribute PhysicalName=\"{a.Column}\"><LogicalName>{a.Column}</LogicalName><IsSecured>{(a.Secured ? 1 : 0)}</IsSecured></attribute>"))}
        </attributes></entity></EntityInfo></Entity>
        """;

    public static TheoryData<string, string> UnclassifiedShapes() => new()
    {
        { "an unclassified secured column on contact", EntityXml("Contact", ("sprk_newsecret", true)) },
        { "an unclassified secured column on systemuser", EntityXml("SystemUser", ("sprk_newsecret", true)) },
        { "an unclassified secured column on a table that carries sprk_issecure",
            EntityXml("sprk_Matter", ("sprk_issecure", false), ("sprk_newsecret", true)) },
        { "an unclassified secured column on sprk_document", EntityXml("sprk_Document", ("sprk_newsecret", true)) },
        { "a registry name on a table the registry does not list it for",
            EntityXml("sprk_Matter", ("sprk_issecure", false), ("name", true)) },
        { "an unclassified secured column on sprk_communication", EntityXml("sprk_Communication", ("sprk_newsecret", true)) },
    };

    [Theory(DisplayName = "Task 067 (C3) control: an unclassified field-secured column on a scoped table fires")]
    [MemberData(nameof(UnclassifiedShapes))]
    public void TheClassification_Fires(string shape, string xml)
        => Assert.True(Unclassified(ScanEntities([("control.xml", xml)]).Secured, BusinessColumns).Count > 0,
            $"The guard did not fire on: {shape}.");

    [Fact(DisplayName = "Task 067 (C3) control: a secure table's secured column fires from a file that does not repeat sprk_issecure")]
    public void TheScope_IsDecidedPerTable_AcrossFiles()
    {
        var files = new[]
        {
            ("a/Entity.xml", EntityXml("sprk_Matter", ("sprk_issecure", true))),
            ("b/Entity.xml", EntityXml("sprk_Matter", ("sprk_newsecret", true))),
        };

        Assert.Equal(new[] { "sprk_matter.sprk_newsecret (b/Entity.xml)" }, Unclassified(ScanEntities(files).Secured, BusinessColumns));
        Assert.Empty(Unclassified(ScanEntities([files[1]]).Secured, BusinessColumns)); // must not fire: no file marks the table secure
    }

    public static TheoryData<string, string> ClassifiedShapes() => new()
    {
        { "the access-model columns on contact (adx_identity_passwordhash, a collision column)",
            EntityXml("Contact", ("adx_identity_passwordhash", true), ("sprk_identitycollisionnew", true), ("emailaddress1", false)) },
        { "sprk_issecure secured on a table that carries it", EntityXml("sprk_Project", ("sprk_issecure", true)) },
        { "a document's file binding", EntityXml("sprk_Document", ("sprk_graphdriveid", true), ("sprk_graphitemidbound", true)) },
        { "a pinned business column", EntityXml("sprk_Invoice", ("sprk_issecure", false), ("sprk_matter", true)) },
        { "an access-registry column (an invoice amount)", EntityXml("sprk_Invoice", ("sprk_issecure", false), ("sprk_totalamount", true)) },
        { "a secured column on a table that is not scoped", EntityXml("sprk_Event", ("sprk_newsecret", true)) },
        { "an unsecured column on contact", EntityXml("Contact", ("sprk_newsecret", false)) },
    };

    [Theory(DisplayName = "Task 067 (C3) control: classified, unsecured and out-of-scope columns do not fire")]
    [MemberData(nameof(ClassifiedShapes))]
    public void TheClassification_StaysQuiet(string shape, string xml)
    {
        var unclassified = Unclassified(ScanEntities([("control.xml", xml)]).Secured, BusinessColumns);
        Assert.True(unclassified.Count == 0, $"The guard fired on: {shape}: {string.Join("; ", unclassified)}");
    }

    [Fact(DisplayName = "Task 067 (C3) control: a stale, an access-model or a reasonless business entry is reported; a used one is not")]
    public void AStaleBusinessEntry_IsReported()
    {
        var secured = ScanEntities([("control.xml", EntityXml("Contact", ("sprk_note", true), ("sprk_standinggrant", true)))]).Secured;
        var entries = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["contact.sprk_note"] = "control: used",
            ["contact.sprk_gone"] = "control: no longer secured",
            ["contact.sprk_standinggrant"] = "control: on the access-model list",
        };

        Assert.Equal(new[] { "contact.sprk_gone", "contact.sprk_standinggrant" }, StaleBusinessColumns(secured, entries));
        Assert.Equal(new[] { "contact.sprk_note" },
            StaleBusinessColumns(secured, new Dictionary<string, string> { ["contact.sprk_note"] = " " }));
    }

    [Fact(DisplayName = "Task 067 (C3) control: a scan that sees no contact or systemuser Entity.xml is told apart")]
    public void AScanWithoutContactOrSystemUser_IsToldApart()
    {
        var (tables, _) = ScanEntities([("control.xml", EntityXml("sprk_Matter", ("sprk_issecure", true)))]);
        Assert.DoesNotContain("contact", tables);
        Assert.DoesNotContain("systemuser", tables);
    }
}
