// -----------------------------------------------------------------------------
// InvoiceMoneyColumnLockPackagedTests.cs
//
// Task 268 (H7b S26). ebilling-r1 task 166 (owner decision EB-D-200) field-secures seven sprk_invoice columns — the billed
// total, the five derived totals and the firm lookup — so that only the BFF sets them. The lock ships in SpaarkeMaster;
// H7b S26 refuses a stamp where it is not whole. This structural test pins H7b's codified list
// (SecureRecordSetupProcedure.InvoiceMoneyColumns) to the two other sources of the same list, so none of the three can
// drift alone:
//   - ebilling's operator script scripts/ebilling/Set-EbillingInvoiceMoneyFieldSecurity.ps1 ($Columns), which ebilling's
//     own InvoiceMoneyFieldSecurityAgreementTests pins to the package;
//   - the package itself: Entities/sprk_Invoice/Entity.xml (IsSecured=1) and Other/FieldSecurityProfiles.xml (Readers
//     read 4 only, Writers 4/4/4, no other profile).
//
// Maintenance: ebilling adds or removes a column → change the script, the package and InvoiceMoneyColumns together; this
// test fails until all three agree. A new profile granting one of the columns is a change to EB-D-200: ask ebilling-r1.
//
// Controls (tests/CLAUDE.md): the script-list scan and the profile scan run against seeded text with and without what they
// look for.
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class InvoiceMoneyColumnLockPackagedTests
{
    private const string Table = SecureRecordSetupProcedure.InvoiceMoneyTable;
    private const string ScriptRelativePath = "scripts/ebilling/Set-EbillingInvoiceMoneyFieldSecurity.ps1";

    [Fact]
    public void InvoiceMoneyColumns_EqualEbillingsScriptColumns()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), ScriptRelativePath));

        ScriptColumns(script).Should().BeEquivalentTo(SecureRecordSetupProcedure.InvoiceMoneyColumns,
            $"H7b S26 checks exactly the columns {ScriptRelativePath} secures (EB-D-200)");
    }

    [Fact]
    public void InvoiceMoneyColumns_AreSecuredInThePackagedInvoiceTable()
    {
        var invoice = XDocument.Load(PackagePath("Entities", "sprk_Invoice", "Entity.xml"));

        foreach (var column in SecureRecordSetupProcedure.InvoiceMoneyColumns)
        {
            IsSecured(invoice, column).Should().BeTrue($"H7b S26 refuses a stamp where {Table}.{column} is not secured");
        }
    }

    [Fact]
    public void InvoiceMoneyColumns_ArePackagedWithReadersReadAndWritersReadCreateUpdate_AndNoOtherProfile()
    {
        var profiles = XDocument.Load(PackagePath("Other", "FieldSecurityProfiles.xml"));

        foreach (var column in SecureRecordSetupProcedure.InvoiceMoneyColumns)
        {
            var grants = Grants(profiles, column);
            grants.Should().BeEquivalentTo(new[]
            {
                (SecureRecordSetupProcedure.ReaderProfileName, Read: 4, Create: 0, Update: 0),
                (SecureRecordSetupProcedure.WriterProfileName, Read: 4, Create: 4, Update: 4),
            }, $"S26 passes only on exactly this lock for {Table}.{column}; a package that ships another one fails every new stamp at H7b");
        }
    }

    // ---------- controls: the scans on seeded text ----------

    [Fact]
    public void ScriptColumnsScan_ReadsTheArray_AndNothingOutsideIt()
    {
        const string seeded = """
            $Table = 'sprk_invoice'
            $Columns = @(
                'sprk_a'
                'sprk_b'   # a comment
            )
            $LookupColumns = @('sprk_c')
            """;

        ScriptColumns(seeded).Should().Equal("sprk_a", "sprk_b");
        ScriptColumns("$Other = @('sprk_a')").Should().BeEmpty();
    }

    [Fact]
    public void GrantsScan_FindsTheInvoiceRows_AndNotAnotherTablesRows()
    {
        var seeded = XDocument.Parse("""
            <FieldSecurityProfiles>
              <FieldSecurityProfile name="P"><FieldPermissions>
                <FieldPermission><EntityName>sprk_invoice</EntityName><AttributeName>sprk_a</AttributeName>
                  <CanRead>4</CanRead><CanUpdate>0</CanUpdate><CanCreate>4</CanCreate></FieldPermission>
              </FieldPermissions></FieldSecurityProfile>
              <FieldSecurityProfile name="Q"><FieldPermissions>
                <FieldPermission><EntityName>sprk_invoicecorrection</EntityName><AttributeName>sprk_a</AttributeName>
                  <CanRead>4</CanRead><CanUpdate>4</CanUpdate><CanCreate>4</CanCreate></FieldPermission>
              </FieldPermissions></FieldSecurityProfile>
            </FieldSecurityProfiles>
            """);

        Grants(seeded, "sprk_a").Should().Equal([("P", 4, 4, 0)]);
        Grants(seeded, "sprk_missing").Should().BeEmpty();
    }

    // ---------- scans ----------

    /// <summary>The quoted names inside the script's <c>$Columns = @( … )</c> block, comments ignored.</summary>
    private static IReadOnlyList<string> ScriptColumns(string script)
    {
        var block = Regex.Match(script, @"^\$Columns\s*=\s*@\((?<body>[^)]*)\)", RegexOptions.Multiline);
        if (!block.Success)
        {
            return [];
        }
        var body = Regex.Replace(block.Groups["body"].Value, "#[^\r\n]*", string.Empty);
        return Regex.Matches(body, "'(?<name>[^']+)'").Select(m => m.Groups["name"].Value).ToList();
    }

    /// <summary>Every profile's grant on <c>sprk_invoice.{column}</c>, as (profile, read, create, update).</summary>
    private static IReadOnlyList<(string Profile, int Read, int Create, int Update)> Grants(XDocument profiles, string column)
        => profiles.Descendants("FieldSecurityProfile")
            .SelectMany(p => p.Descendants("FieldPermission")
                .Where(f => string.Equals((string?)f.Element("EntityName"), Table, StringComparison.OrdinalIgnoreCase)
                            && string.Equals((string?)f.Element("AttributeName"), column, StringComparison.OrdinalIgnoreCase))
                .Select(f => ((string)p.Attribute("name")!, Level(f, "CanRead"), Level(f, "CanCreate"), Level(f, "CanUpdate"))))
            .ToList();

    private static int Level(XElement permission, string name)
        => int.TryParse((string?)permission.Element(name), out var level) ? level : 0;

    /// <summary>The attribute's IsSecured flag, or null when the package does not carry the attribute.</summary>
    private static bool? IsSecured(XDocument entity, string logicalName)
    {
        var attribute = entity.Descendants("attribute")
            .FirstOrDefault(a => string.Equals((string?)a.Element("LogicalName"), logicalName, StringComparison.Ordinal));
        return attribute is null ? null : (string?)attribute.Element("IsSecured") == "1";
    }

    private static string PackagePath(params string[] parts)
        => Path.Combine([RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", .. parts]);

    private static string RepoRoot()
    {
        // A worktree's .git is a FILE; a regular checkout's is a directory (parity with ContactIdentityBindingSchemaPackagedTests).
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
