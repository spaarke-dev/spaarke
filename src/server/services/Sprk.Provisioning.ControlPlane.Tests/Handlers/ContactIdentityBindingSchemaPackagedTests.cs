// -----------------------------------------------------------------------------
// ContactIdentityBindingSchemaPackagedTests.cs
//
// Task 255 (INCOMING-141 §5, unified-access-control-r2 task 141). The contact identity-binding schema a stamp BFF
// carrying task 141 SELECTS at every CIAM / Type-2 sign-in (binding_column_missing without it) must reach a new stamp
// BEFORE H9 deploys the BFF — without a shell tool (the L2 Worker runs none, G38/T253). It does, through the package:
// H6 imports the CI-built SpaarkeMaster (src/dataverse/solutions/SpaarkeMaster), and H9 waits for H6 (and H7b).
// scripts/Set-ContactIdentityBindingSchema.ps1 stays the tool for EXISTING environments (its mirror copy and backfill
// have nothing to do in a new one); what no package carries — the field-security profile MEMBERSHIPS — is per
// environment (reported for H7b, which already adds the BFF-managed field profiles' memberships the same way).
//
// This structural test reads the committed export and fails when a component the BFF reads leaves the package:
// the binding column (field-secured), its unsecured uniqueness mirror, the plane + collision columns and their two
// global choices, systemuser.sprk_primarycontact (field-secured), both identity-link field-security profiles with
// their permissions, the collisions view — and the alternate key on the mirror, which the BFF creates contacts by.
//
// KNOWN GAP (pinned, T255): the export has no alternate key on contact. The package rule covered OOB-table columns
// but not OOB-table keys; T255 extends the rule (SpaarkePackageScope.psm1, EntityKey on OOB tables). The key exists in
// spaarkedev1 (UAC-r2 task 141 live gate G-1, 2026-10-02), so the next Assemble → Export (an owner-approved live step,
// docs/procedures/SPAARKE-SOLUTION-RELEASE-PROCESS.md) brings it into git. Until then KeyAwaitingExport is true; when
// the key arrives this test fails until the pin is removed — the gap cannot be closed silently or forgotten.
//
// Controls (tests/CLAUDE.md): the attribute / key / profile scans are run against seeded XML with and without the
// thing they look for.
// -----------------------------------------------------------------------------

using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class ContactIdentityBindingSchemaPackagedTests
{
    // The names the BFF reads (Sprk.Bff.Api ContactBindingDecision / DataverseContactIdentityStore — pinned against the
    // schema script by the BFF's ContactIdentitySchemaAgreementTests; L2 cannot reference the BFF).
    private const string BindingColumn = "sprk_externalobjectid";
    private const string MirrorColumn = "sprk_externalobjectidkey";
    private const string KeyLogicalName = "sprk_externalobjectiduniquekey";
    private const string ReaderProfile = "Spaarke Identity Link Readers";
    private const string WriterProfile = "Spaarke Identity Link Writers";

    private static readonly string[] PlaneAndCollisionColumns =
    [
        "sprk_identityplane", "sprk_identitycollisionon", "sprk_identitycollisionoid",
        "sprk_identitycollisionplane", "sprk_identitycollisionreason", "sprk_identitycollisionparties",
    ];

    /// <summary>T255 pin — true while the committed export lacks the contact alternate key (see file header).</summary>
    private static readonly bool KeyAwaitingExport = true;

    [Fact]
    public void ThePackageCarriesTheBindingColumns_FieldSecuredWhereTheBffRequiresIt()
    {
        var contact = XDocument.Load(PackagePath("Entities", "Contact", "Entity.xml"));
        var systemUser = XDocument.Load(PackagePath("Entities", "SystemUser", "Entity.xml"));

        IsSecured(contact, BindingColumn).Should().BeTrue("only the BFF may write the binding — it decides whose grants a caller inherits");
        IsSecured(contact, MirrorColumn).Should().BeFalse("Dataverse cannot key a field-secured column; the key lives on the mirror");
        foreach (var column in PlaneAndCollisionColumns)
        {
            IsSecured(contact, column).Should().NotBeNull($"the BFF selects contact.{column} (binding_column_missing without it)");
        }
        IsSecured(systemUser, "sprk_primarycontact").Should().BeTrue("the licensed-user link is BFF-written too");

        foreach (var optionSet in new[] { "sprk_identityplane", "sprk_identitycollisionreason" })
        {
            File.Exists(PackagePath("OptionSets", $"{optionSet}.xml")).Should().BeTrue($"global choice {optionSet} ships in the package");
        }

        Directory.EnumerateFiles(PackagePath("Entities", "Contact", "SavedQueries"), "*.xml")
            .Any(f => File.ReadAllText(f).Contains("Contacts with Identity Collisions", StringComparison.Ordinal))
            .Should().BeTrue("the operator's collisions view (deployment guide §6.5.3) ships in the package");
    }

    [Fact]
    public void ThePackageCarriesBothIdentityLinkProfiles_WithTheirPermissions()
    {
        var profiles = XDocument.Load(PackagePath("Other", "FieldSecurityProfiles.xml"));

        Permissions(profiles, ReaderProfile).Should().BeEquivalentTo(new[]
        {
            ("contact", BindingColumn, Read: true, Create: false, Update: false),
            ("systemuser", "sprk_primarycontact", Read: true, Create: false, Update: false),
        }, "every user reads both fields through the reader profile (a secured column is hidden without Read)");
        Permissions(profiles, WriterProfile).Should().BeEquivalentTo(new[]
        {
            ("contact", BindingColumn, Read: true, Create: true, Update: true),
            ("systemuser", "sprk_primarycontact", Read: true, Create: true, Update: true),
        }, "the BFF's application users write both fields through the writer profile");
    }

    [Fact]
    public void TheContactAlternateKeyOnTheMirror_IsPackaged_OrPinnedAsAwaitingExport()
    {
        var contact = XDocument.Load(PackagePath("Entities", "Contact", "Entity.xml"));
        var keyed = KeyAttributes(contact, KeyLogicalName);

        if (keyed is null)
        {
            KeyAwaitingExport.Should().BeTrue(
                "the contact alternate key is missing from the committed SpaarkeMaster export: every BFF contact create " +
                "(by contacts(sprk_externalobjectidkey='…')) fails on a stamp without it. Run the release process " +
                "(Assemble → Export) so the package carries it — the package rule includes OOB-table keys since T255");
        }
        else
        {
            keyed.Should().Equal([MirrorColumn], "the BFF creates by the mirror; a key on the secured binding is impossible");
            KeyAwaitingExport.Should().BeFalse(
                "the export now carries the key — remove the T255 KeyAwaitingExport pin in this test");
        }
    }

    // ---------- controls: the scans on seeded XML ----------

    [Fact]
    public void Scans_FindWhatIsThere_AndNotWhatIsNot()
    {
        var seeded = XDocument.Parse("""
            <Entity><EntityInfo><entity Name="Contact"><attributes>
              <attribute PhysicalName="sprk_A"><LogicalName>sprk_a</LogicalName><IsSecured>1</IsSecured></attribute>
              <attribute PhysicalName="sprk_B"><LogicalName>sprk_b</LogicalName><IsSecured>0</IsSecured></attribute>
            </attributes>
            <EntityKeys><EntityKey><Name>sprk_K</Name><LogicalName>sprk_k</LogicalName>
              <EntityKeyAttributes><AttributeName>sprk_b</AttributeName></EntityKeyAttributes></EntityKey></EntityKeys>
            </entity></EntityInfo></Entity>
            """);

        IsSecured(seeded, "sprk_a").Should().BeTrue();
        IsSecured(seeded, "sprk_b").Should().BeFalse();
        IsSecured(seeded, "sprk_missing").Should().BeNull();
        KeyAttributes(seeded, "sprk_k").Should().Equal("sprk_b");
        KeyAttributes(seeded, "sprk_other").Should().BeNull();

        var profiles = XDocument.Parse("""
            <FieldSecurityProfiles><FieldSecurityProfile name="P"><FieldPermissions>
              <FieldPermission><EntityName>contact</EntityName><AttributeName>sprk_a</AttributeName>
                <CanRead>4</CanRead><CanUpdate>0</CanUpdate><CanCreate>4</CanCreate></FieldPermission>
            </FieldPermissions></FieldSecurityProfile></FieldSecurityProfiles>
            """);
        Permissions(profiles, "P").Should().Equal([("contact", "sprk_a", true, true, false)]);
        Permissions(profiles, "Q").Should().BeEmpty();
    }

    // ---------- scans ----------

    /// <summary>The attribute's IsSecured flag, or null when the package does not carry the attribute.</summary>
    private static bool? IsSecured(XDocument entity, string logicalName)
    {
        var attribute = entity.Descendants("attribute")
            .FirstOrDefault(a => string.Equals((string?)a.Element("LogicalName"), logicalName, StringComparison.Ordinal));
        return attribute is null ? null : (string?)attribute.Element("IsSecured") == "1";
    }

    /// <summary>The key's attribute names, or null when the package does not carry the key.</summary>
    private static IReadOnlyList<string>? KeyAttributes(XDocument entity, string keyLogicalName)
    {
        var key = entity.Descendants("EntityKey")
            .FirstOrDefault(k => string.Equals((string?)k.Element("LogicalName"), keyLogicalName, StringComparison.OrdinalIgnoreCase));
        return key?.Descendants("AttributeName").Select(a => a.Value).ToList();
    }

    private static IReadOnlyList<(string Entity, string Attribute, bool Read, bool Create, bool Update)> Permissions(
        XDocument profiles, string profileName)
        => profiles.Descendants("FieldSecurityProfile")
            .Where(p => string.Equals((string?)p.Attribute("name"), profileName, StringComparison.Ordinal))
            .SelectMany(p => p.Descendants("FieldPermission"))
            .Select(f => ((string)f.Element("EntityName")!, (string)f.Element("AttributeName")!,
                Read: (string?)f.Element("CanRead") == "4",
                Create: (string?)f.Element("CanCreate") == "4",
                Update: (string?)f.Element("CanUpdate") == "4"))
            .ToList();

    private static string PackagePath(params string[] parts)
        => Path.Combine([RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", .. parts]);

    private static string RepoRoot()
    {
        // A worktree's .git is a FILE; a regular checkout's is a directory (parity with H11DefaultGuestRolePackagedTests).
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException($"No repository root above {AppContext.BaseDirectory}.");
    }
}
