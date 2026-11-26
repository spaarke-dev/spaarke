// -----------------------------------------------------------------------------
// BffManagedLinkColumnLockListTests.cs
//
// Task 270 (H7b S27/S28). Pins H7b's codified column lists to the other sources of the same lists, so none can drift alone:
//   - S27 SecureRecordSetupProcedure.InvoiceFamilyLinkColumns = scripts/common/InvoiceFamilyLockedColumns.psd1 (unified-access-
//     control-r2 #2799's ONE list, which tests/Spaarke.ArchTests/InvoiceFamilyFieldLockGuardTests pins to the BFF's
//     InvoiceFamilyLockedWrite.LockedColumns). The package carries this lock from SpaarkeMaster 1.3.2.0 (#2859), and
//     tests/Spaarke.ArchTests/InvoiceFamilyFieldLockSourceGuardTests (blocking) pins it to the same psd1, both ways, at
//     exactly S27's pass condition: IsSecured=1, Readers 4/0/0, Writers 4/4/4, no other profile. So this file pins H7b's
//     list to the psd1 only and does not repeat that package check (the S28 rows below have no such guard to rely on).
//   - S28 SecureRecordSetupProcedure.MessageAccessColumns + OptionalMessageAccessColumns = scripts/Set-ThreadFlagFieldSecurity.ps1
//     $Targets (field-mapping tasks 054b, 056b), and the four packaged columns are what SpaarkeMaster ships: Entity.xml
//     IsSecured=1, Other/FieldSecurityProfiles.xml Readers 4/0/0, Writers 4/4/4, no other profile. The optional four (the
//     direct recipient and the three private-context references) are NOT packaged (H7b checks each only where it exists).
//
// Maintenance: a column joins or leaves a lock → change the owning list (psd1 / the thread-flag script / the package) and
// H7b's list together; these tests fail until they agree. A new profile granting one of the columns is a change to the
// owning project's decision: ask it.
//
// Controls (tests/CLAUDE.md): each scan runs against seeded text with and without what it looks for.
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class BffManagedLinkColumnLockListTests
{
    private const string InvoiceFamilyListPath = "scripts/common/InvoiceFamilyLockedColumns.psd1";
    private const string ThreadFlagScriptPath = "scripts/Set-ThreadFlagFieldSecurity.ps1";

    [Fact]
    public void InvoiceFamilyLinkColumns_EqualTheOneListInThePsd1()
    {
        var psd1 = File.ReadAllText(Path.Combine(RepoRoot(), InvoiceFamilyListPath));

        Psd1Pairs(psd1).Should().BeEquivalentTo(SecureRecordSetupProcedure.InvoiceFamilyLinkColumns,
            $"H7b S27 checks exactly the columns {InvoiceFamilyListPath} lists (#2799)");
    }

    [Fact]
    public void MessageAccessColumnsAndOptionalColumns_EqualTheThreadFlagScriptTargets()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), ThreadFlagScriptPath));

        ScriptTargetPairs(script).Should().BeEquivalentTo(
            SecureRecordSetupProcedure.MessageAccessColumns
                .Concat(SecureRecordSetupProcedure.OptionalMessageAccessColumns.Select(c => (c.Table, c.Column))),
            $"H7b S28 checks exactly the columns {ThreadFlagScriptPath} secures (field-mapping tasks 054b, 056b)");
    }

    [Fact]
    public void MessageAccessColumns_AreSecuredAndGrantedInThePackage_AndTheOptionalColumnsAreNot()
    {
        var profiles = XDocument.Load(PackagePath("Other", "FieldSecurityProfiles.xml"));

        foreach (var (table, column) in SecureRecordSetupProcedure.MessageAccessColumns)
        {
            IsSecured(Entity(table), column).Should().BeTrue($"H7b S28 refuses a stamp where {table}.{column} is not secured");
            Grants(profiles, table, column).Should().BeEquivalentTo(new[]
            {
                (SecureRecordSetupProcedure.ReaderProfileName, Read: 4, Create: 0, Update: 0),
                (SecureRecordSetupProcedure.WriterProfileName, Read: 4, Create: 4, Update: 4),
            }, $"S28 passes only on exactly this lock for {table}.{column}");
        }

        foreach (var (table, column, _) in SecureRecordSetupProcedure.OptionalMessageAccessColumns)
        {
            IsSecured(Entity(table), column).Should().BeNull(
                $"H7b treats {table}.{column} as optional because the package does not carry it; once it does, move it into " +
                "MessageAccessColumns so S28 requires it");
        }
    }

    // ---------- controls: the scans on seeded text ----------

    [Fact]
    public void Psd1Scan_ReadsEveryTablesColumns_AndNothingElse()
    {
        const string seeded = """
            # sprk_comment = @{ EntitySet = 'x'; Columns = @('sprk_no') }
            @{
                sprk_a = @{ EntitySet = 'sprk_as'; Columns = @('sprk_x', 'sprk_y') }
                sprk_b = @{ EntitySet = 'sprk_bs'; Columns = @('sprk_z') }
            }
            """;

        Psd1Pairs(seeded).Should().BeEquivalentTo([("sprk_a", "sprk_x"), ("sprk_a", "sprk_y"), ("sprk_b", "sprk_z")]);
        Psd1Pairs("@{ }").Should().BeEmpty();
    }

    [Fact]
    public void ScriptTargetsScan_ReadsTheTargets_AndNotOtherArrays()
    {
        const string seeded = """
            $Targets = @(
                @{ Table = 'sprk_t'; Columns = @('sprk_a', 'sprk_b') }
                @{ Table = 'sprk_u'; Columns = @('sprk_c') }
            )
            $Lookups = @('sprk_d')
            """;

        ScriptTargetPairs(seeded).Should().BeEquivalentTo([("sprk_t", "sprk_a"), ("sprk_t", "sprk_b"), ("sprk_u", "sprk_c")]);
        ScriptTargetPairs("$Lookups = @('sprk_d')").Should().BeEmpty();
    }

    [Fact]
    public void GrantsScan_FindsTheTablesRows_AndNotAnotherTablesRows()
    {
        var seeded = XDocument.Parse("""
            <FieldSecurityProfiles>
              <FieldSecurityProfile name="P"><FieldPermissions>
                <FieldPermission><EntityName>sprk_t</EntityName><AttributeName>sprk_a</AttributeName>
                  <CanRead>4</CanRead><CanUpdate>0</CanUpdate><CanCreate>4</CanCreate></FieldPermission>
                <FieldPermission><EntityName>sprk_u</EntityName><AttributeName>sprk_a</AttributeName>
                  <CanRead>4</CanRead><CanUpdate>4</CanUpdate><CanCreate>4</CanCreate></FieldPermission>
              </FieldPermissions></FieldSecurityProfile>
            </FieldSecurityProfiles>
            """);

        Grants(seeded, "sprk_t", "sprk_a").Should().Equal([("P", 4, 4, 0)]);
        Grants(seeded, "sprk_t", "sprk_missing").Should().BeEmpty();
    }

    // ---------- scans ----------

    /// <summary>Every (table, column) of a psd1 hashtable of <c>table = @{ …; Columns = @('a', 'b') }</c> lines; comments ignored.</summary>
    private static IReadOnlyList<(string Table, string Column)> Psd1Pairs(string psd1)
    {
        var text = Regex.Replace(psd1, "#[^\r\n]*", string.Empty);
        return Regex.Matches(text, @"^\s*(?<table>sprk_\w+)\s*=\s*@\{[^}\r\n]*Columns\s*=\s*@\((?<cols>[^)]*)\)", RegexOptions.Multiline)
            .SelectMany(m => Regex.Matches(m.Groups["cols"].Value, "'(?<c>[^']+)'")
                .Select(c => (m.Groups["table"].Value, c.Groups["c"].Value)))
            .ToList();
    }

    /// <summary>Every (table, column) of the script's <c>@{ Table = 't'; Columns = @('a', 'b') }</c> entries.</summary>
    private static IReadOnlyList<(string Table, string Column)> ScriptTargetPairs(string script)
        => Regex.Matches(script, @"@\{\s*Table\s*=\s*'(?<table>[^']+)'\s*;\s*Columns\s*=\s*@\((?<cols>[^)]*)\)\s*\}")
            .SelectMany(m => Regex.Matches(m.Groups["cols"].Value, "'(?<c>[^']+)'")
                .Select(c => (m.Groups["table"].Value, c.Groups["c"].Value)))
            .ToList();

    /// <summary>Every profile's grant on <paramref name="table"/>.<paramref name="column"/>, as (profile, read, create, update).</summary>
    private static IReadOnlyList<(string Profile, int Read, int Create, int Update)> Grants(XDocument profiles, string table, string column)
        => profiles.Descendants("FieldSecurityProfile")
            .SelectMany(p => p.Descendants("FieldPermission")
                .Where(f => string.Equals((string?)f.Element("EntityName"), table, StringComparison.OrdinalIgnoreCase)
                            && string.Equals((string?)f.Element("AttributeName"), column, StringComparison.OrdinalIgnoreCase))
                .Select(f => ((string)p.Attribute("name")!, Level(f, "CanRead"), Level(f, "CanCreate"), Level(f, "CanUpdate"))))
            .ToList();

    /// <summary>The grant level, or -1 when the element is missing or unreadable (so a malformed row never reads as "no right").</summary>
    private static int Level(XElement permission, string name)
        => int.TryParse((string?)permission.Element(name), out var level) ? level : -1;

    /// <summary>The attribute's IsSecured flag, or null when the package does not carry the attribute.</summary>
    private static bool? IsSecured(XDocument entity, string logicalName)
    {
        var attribute = entity.Descendants("attribute")
            .FirstOrDefault(a => string.Equals((string?)a.Element("LogicalName"), logicalName, StringComparison.Ordinal));
        return attribute is null ? null : (string?)attribute.Element("IsSecured") == "1";
    }

    /// <summary>The packaged Entity.xml of a table; the folder name is the table's schema name, so it is matched ignoring case.</summary>
    private static XDocument Entity(string table)
    {
        var folder = Directory.GetDirectories(PackagePath("Entities"))
            .Single(d => string.Equals(Path.GetFileName(d), table, StringComparison.OrdinalIgnoreCase));
        return XDocument.Load(Path.Combine(folder, "Entity.xml"));
    }

    private static string PackagePath(params string[] parts)
        => Path.Combine([RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", .. parts]);

    private static string RepoRoot()
    {
        // A worktree's .git is a FILE; a regular checkout's is a directory (parity with InvoiceMoneyColumnLockPackagedTests).
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
