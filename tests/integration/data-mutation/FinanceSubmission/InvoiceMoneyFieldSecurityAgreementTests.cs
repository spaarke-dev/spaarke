using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceSubmission;

/// <summary>
/// ebilling-r1 task 166 (EB-D-200 part 1; owner + uac-r2 option D): the seven <c>sprk_invoice</c> money and firm columns (the billed total, the four
/// derived totals, the approved total and the firm lookup) are field-secured so that only the server writes them: the BFF application user
/// ("Spaarke BFF-Managed Field Writers": read, create, update) writes them, and every other user reads them ("Spaarke BFF-Managed Field Readers":
/// read only). Four artifacts must agree: <c>scripts/ebilling/Set-EbillingInvoiceMoneyFieldSecurity.ps1</c> (what an operator applies), the
/// SpaarkeMaster solution source (<c>Entity.xml</c> IsSecured and <c>Other/FieldSecurityProfiles.xml</c>), the invoice forms (no user types a
/// secured column: every control disabled in the source and kept so by <c>Deploy-EbillingFormsViews.ps1</c>) and the BFF's column names
/// (<see cref="InvoiceTotalsService"/>, <see cref="InvoiceSubmissionPersister"/>). Read as text (no PowerShell host, no network); the script
/// parser is exercised on seeded drifts, in the shape of <c>InvoiceFirmHoldFieldSecurityAgreementTests</c> and
/// <c>SourceEmailFieldSecurityAgreementTests</c>.
/// </summary>
[Trait("Category", "Security")]
public class InvoiceMoneyFieldSecurityAgreementTests
{
    private const string Table = "sprk_invoice";
    private const string ReaderProfile = "Spaarke BFF-Managed Field Readers";
    private const string WriterProfile = "Spaarke BFF-Managed Field Writers";
    private const string SystemAdministratorProfileId = "572329c1-a042-4e22-be47-367c6374ea45";
    private const string ScriptFile = "Set-EbillingInvoiceMoneyFieldSecurity.ps1";

    /// <summary>The seven columns, named by the BFF constants that write them (so a rename in the BFF breaks this test, not production).</summary>
    private static readonly string[] Columns =
    [
        InvoiceTotalsService.InvoiceBilledTotal,
        InvoiceTotalsService.InvoiceApprovedTotal,
        InvoiceTotalsService.InvoiceFeesTotal,
        InvoiceTotalsService.InvoiceExpensesTotal,
        InvoiceTotalsService.InvoiceLineAdjustmentsTotal,
        InvoiceTotalsService.InvoiceInvoiceAdjustmentsTotal,
        InvoiceSubmissionPersister.InvoiceVendorOrg,
    ];

    [Fact(DisplayName = "166: the BFF's own column constants are the seven EB-D-200 columns")]
    public void TheBffConstants_AreTheSevenColumns()
    {
        Columns.Should().BeEquivalentTo(new[]
        {
            "sprk_totalamount", "sprk_approvedtotal", "sprk_feestotal", "sprk_expensestotal",
            "sprk_lineadjustmentstotal", "sprk_invoiceadjustmentstotal", "sprk_vendororg",
        });
    }

    [Fact(DisplayName = "166: the script secures the seven columns for the writer (read/create/update) and the reader (read), with the verify checks in place")]
    public void TheScript_SecuresTheColumns_TheWriterWritesThem_AndTheReaderOnlyReadsThem()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", ScriptFile));

        Agrees(Analyse(script)).Should().BeTrue("the script must secure the seven sprk_invoice columns for the reused writer and reader profiles");
    }

    [Fact(DisplayName = "166: the script has -WhatIf, a plain write, -Verify and -Revert")]
    public void TheScript_HasItsFourModes()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", ScriptFile));

        script.Should().Contain("SupportsShouldProcess = $true", "-WhatIf and -Confirm");
        script.Should().Contain("[switch]$Verify");
        script.Should().Contain("[switch]$Revert");
        script.Should().Contain("[Parameter(Mandatory)][string[]]$BffApplicationIds");
    }

    [Fact(DisplayName = "166: the solution source secures the seven columns, the writer has 4/4/4, the reader 4/0/0 and no other profile has any access")]
    public void TheSolution_SecuresTheColumns_WriterAndReaderOnly()
    {
        var solution = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster");
        var attributes = XDocument.Load(EntityXml(solution)).Descendants("attribute").ToArray();
        var profiles = XDocument.Load(Path.Combine(solution, "Other", "FieldSecurityProfiles.xml")).Descendants("FieldSecurityProfile").ToArray();

        static XElement[] OnTheColumn(XElement profile, string column) => profile.Descendants("FieldPermission")
            .Where(p => (string?)p.Element("EntityName") == Table && (string?)p.Element("AttributeName") == column).ToArray();

        foreach (var column in Columns)
        {
            ((string?)attributes.Single(a => (string?)a.Element("LogicalName") == column).Element("IsSecured"))
                .Should().Be("1", $"{Table}.{column} is field-secured in the solution");

            var writer = OnTheColumn(profiles.Single(p => (string?)p.Attribute("name") == WriterProfile), column)
                .Should().ContainSingle($"the writer profile carries one permission on {column}").Subject;
            ((string?)writer.Element("CanRead")).Should().Be("4", column);
            ((string?)writer.Element("CanCreate")).Should().Be("4", column);
            ((string?)writer.Element("CanUpdate")).Should().Be("4", column);

            var reader = OnTheColumn(profiles.Single(p => (string?)p.Attribute("name") == ReaderProfile), column)
                .Should().ContainSingle($"the reader profile carries one permission on {column}").Subject;
            ((string?)reader.Element("CanRead")).Should().Be("4", column);
            ((string?)reader.Element("CanCreate")).Should().Be("0", column);
            ((string?)reader.Element("CanUpdate")).Should().Be("0", column);

            profiles.Where(p => (string?)p.Attribute("name") != WriterProfile && (string?)p.Attribute("name") != ReaderProfile)
                .SelectMany(p => OnTheColumn(p, column))
                .Where(p => (string?)p.Element("CanRead") != "0" || (string?)p.Element("CanCreate") != "0" || (string?)p.Element("CanUpdate") != "0")
                .Should().BeEmpty($"no profile but the writer and the reader may have access to {column}");
        }
    }

    [Fact(DisplayName = "166: no invoice form lets a user type a secured column: every control is disabled in the source, and the forms script keeps them so")]
    public void TheForms_DisableEverySecuredColumn()
    {
        var solution = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster");
        var formXml = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(EntityXml(solution))!, "FormXml"), "*.xml", SearchOption.AllDirectories);
        formXml.Should().NotBeEmpty();

        var bound = 0;
        foreach (var form in formXml)
        {
            foreach (var column in Columns)
            {
                foreach (Match control in Regex.Matches(File.ReadAllText(form), $"<control\\b[^>]*datafieldname=\"{column}\"[^>]*>"))
                {
                    bound++;
                    control.Value.Should().Contain("disabled=\"true\"", $"{Path.GetFileName(form)} binds {column}, which only the server writes");
                }
            }
        }

        bound.Should().BeGreaterThanOrEqualTo(Columns.Length, "the main form binds all seven (the quick-create form binds the billed total)");

        var deploy = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", "Deploy-EbillingFormsViews.ps1"));
        var force = Regex.Match(deploy, @"^\s*ForceReadOnly\s*=\s*(?<list>'sprk_feestotal'[^\r\n]*)\r?$", RegexOptions.Multiline);
        force.Success.Should().BeTrue("the invoice entry of the forms script has a ForceReadOnly list");
        Regex.Matches(force.Groups["list"].Value, "'([^']+)'").Select(m => m.Groups[1].Value)
            .Should().BeEquivalentTo(Columns, "a forms redeploy keeps every secured column read-only");
    }

    // ── Script parser, with seeded drifts ──

    private const string Seed = """
        $Table = 'sprk_invoice'
        $Columns = @(
            'sprk_totalamount'
            'sprk_approvedtotal'
            'sprk_feestotal'
            'sprk_expensestotal'
            'sprk_lineadjustmentstotal'
            'sprk_invoiceadjustmentstotal'
            'sprk_vendororg'
        )
        $LookupColumns = @('sprk_vendororg')
        $WriterProfileName = 'Spaarke BFF-Managed Field Writers'
        $ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
        $SystemAdministratorProfileId = '572329c1-a042-4e22-be47-367c6374ea45'
        if ($writer) { $specs += @{ Id = $writer.fieldsecurityprofileid; Name = $WriterProfileName; Create = 4 } }
        if ($reader) { $specs += @{ Id = $reader.fieldsecurityprofileid; Name = $ReaderProfileName; Create = 0 } }
                canread = 4; cancreate = $Create; canupdate = $Create
        """;

    [Theory(DisplayName = "166: the script parser sees each drift (another table, a swapped, dropped or added column, a swapped profile, a changed grant, another admin profile)")]
    [InlineData("$Table = 'sprk_invoice'", "$Table = 'sprk_invoicesubmission'")]
    [InlineData("    'sprk_totalamount'\n", "    'sprk_totalamounts'\n")]
    [InlineData("    'sprk_vendororg'\n", "")]
    [InlineData("    'sprk_approvedtotal'\n", "")]
    [InlineData("    'sprk_vendororg'\n", "    'sprk_vendororg'\n    'sprk_matter'\n")]
    [InlineData("    'sprk_feestotal'\n", "    'sprk_feestotal'\n    'sprk_feestotal'\n")]
    [InlineData("Managed Field Writers'\n", "Managed Field Readers'\n")]
    [InlineData("Name = $ReaderProfileName; Create = 0", "Name = $ReaderProfileName; Create = 4")]
    [InlineData("Name = $WriterProfileName; Create = 4", "Name = $WriterProfileName; Create = 0")]
    [InlineData("canread = 4; cancreate = $Create; canupdate = $Create", "canread = 4; cancreate = 4; canupdate = 4")]
    [InlineData("'572329c1-a042-4e22-be47-367c6374ea45'", "'11111111-1111-1111-1111-111111111111'")]
    [InlineData("$LookupColumns = @('sprk_vendororg')", "$LookupColumns = @()")]
    [InlineData("$LookupColumns = @('sprk_vendororg')", "$LookupColumns = @('sprk_vendororg', 'sprk_totalamount')")]
    public void TheParser_SeesEachDrift(string from, string to)
    {
        Agrees(Analyse(Seed)).Should().BeTrue("the faithful seed agrees");
        Agrees(Analyse(Seed.ReplaceLineEndings("\n").Replace(from, to))).Should().BeFalse("each seeded drift must fail the agreement test");
    }

    [Fact(DisplayName = "166: a script with no column list, or with the list in another variable, is a drift")]
    public void TheParser_SeesAMissingColumnList()
    {
        Agrees(Analyse(Seed.Replace("$Columns = @(", "$Cols = @("))).Should().BeFalse();
        Agrees(Analyse(Seed.Replace("$Columns = @(", "$Columns = @() # ("))).Should().BeFalse();
    }

    // -Verify's assertions (IsSecured, both grants, no other profile, every BFF application user a writer member) cannot run without a host,
    // so each is pinned as a source fact: the shipped script has it, and a seeded drift of it does not.
    [Theory(DisplayName = "166: -Verify keeps its IsSecured, grant, other-profile, membership and default-team checks")]
    [InlineData(@"Report 'MISSING' ""\$Table\.\$Column is NOT field-secured", "Report 'MISSING' \"$Table.$Column is NOT", "Report 'INFO' \"$Table.$Column is NOT")]
    [InlineData(@"elseif \(\$Verify\) \{ Report 'MISSING' ""\$\(\$s\.Name\) on \$Table\.\$Column"" \}", "elseif ($Verify) { Report 'MISSING' \"$($s.Name) on", "elseif ($Verify) { Report 'INFO' \"$($s.Name) on")]
    [InlineData(@"\(canread eq 4 or canupdate eq 4 or cancreate eq 4\)", "canread eq 4 or canupdate eq 4 or cancreate eq 4", "canread eq 4 or canupdate eq 4")]
    [InlineData(@"Report 'FAIL' ""profile '\$\(\$o\.fieldsecurityprofileid\.name\)'", "Report 'FAIL' \"profile '$($o.fieldsecurityprofileid.name)'", "Report 'INFO' \"profile '$($o.fieldsecurityprofileid.name)'")]
    [InlineData(@"Report 'FAIL' ""\(p2\) BFF app user '\$\(\$u\.fullname\)' is NOT a member of \$WriterProfileName", "Report 'FAIL' \"(p2) BFF app user '$($u.fullname)' is NOT a member", "Report 'INFO' \"(p2) BFF app user '$($u.fullname)' is NOT a member")]
    [InlineData(@"Report 'FAIL' ""\(p8\) default team", "Report 'FAIL' \"(p8) default team", "Report 'INFO' \"(p8) default team")]
    [InlineData(@"\$perm\.canread -eq 4 -and \$perm\.cancreate -eq \$s\.Create -and \$perm\.canupdate -eq \$s\.Create", "$perm.canread -eq 4 -and $perm.cancreate -eq $s.Create", "$perm.canread -eq 4 -and $perm.cancreate -eq 4")]
    // Task 166 fix round (review K1, K2): the column definition goes through its typed path and a changed shape stops the run; a failed apply
    // names what completed and exits non-zero; a declined un-secure keeps the column's permission rows. The behaviour is run by
    // tests/scripts/Set-EbillingInvoiceMoneyFieldSecurity.Tests.ps1 (Pester); these pin the same lines as source facts, so a seeded drift of
    // each fails here too.
    [InlineData(@"'LookupAttributeMetadata' \} else \{ 'MoneyAttributeMetadata' \}", "'LookupAttributeMetadata' } else { 'MoneyAttributeMetadata' }", "'AttributeMetadata' } else { 'AttributeMetadata' }")]
    [InlineData(@"\$path = Get-TypedAttrPath \$Col\n\s+\$typed = Invoke-DvGet \$path", "$path = Get-TypedAttrPath $Col", "$path = Get-AttrPath $Col")]
    [InlineData(@"if \(\$after -ne \$before\) \{", "if ($after -ne $before) {", "if ($false) {")]
    [InlineData(@"Precision=\$\(\$Attr\.Precision\);MinValue=\$\(Format-Number \$Attr\.MinValue\);MaxValue=\$\(Format-Number \$Attr\.MaxValue\)", "MaxValue=$(Format-Number $Attr.MaxValue)", "")]
    [InlineData(@"else \{ \$unsecured = \$false \}", "else { $unsecured = $false }", "else { }")]
    [InlineData(@"if \(\$still\.Count -gt 0 -or \$revertFailures\.Count -gt 0\) \{ exit 1 \}", "if ($still.Count -gt 0 -or $revertFailures.Count -gt 0) { exit 1 }", "if ($still.Count -gt 0 -or $revertFailures.Count -gt 0) { exit 0 }")]
    [InlineData(@"\$secure\.Error\) \{[\s\S]*?    exit 1\n\}\nif \(\$securedThisRun\.Count", "    exit 1\n}\nif ($securedThisRun.Count", "    exit 0\n}\nif ($securedThisRun.Count")]
    public void TheScript_KeepsItsVerifyChecks(string pattern, string from, string to)
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", ScriptFile)).ReplaceLineEndings("\n");

        Regex.IsMatch(script, pattern).Should().BeTrue("the shipped script has this check");
        script.Should().Contain(from);
        Regex.IsMatch(script.Replace(from, to), pattern).Should().BeFalse("a seeded drift of it must fail the pin");
    }

    private sealed record ScriptShape(
        string? Table, string[] Columns, string[] Lookups, string? Writer, string? Reader, string? SystemAdministrator, string? WriterCreate, string? ReaderCreate, string? GrantShape);

    private static bool Agrees(ScriptShape s) =>
        s.Table == Table && s.Columns.Length == Columns.Length && s.Columns.OrderBy(c => c).SequenceEqual(Columns.OrderBy(c => c))
        && s.Lookups.SequenceEqual(new[] { InvoiceSubmissionPersister.InvoiceVendorOrg }) && s.Writer == WriterProfile && s.Reader == ReaderProfile && s.SystemAdministrator == SystemAdministratorProfileId
        && s.WriterCreate == "4" && s.ReaderCreate == "0" && s.GrantShape == "4,$Create,$Create";

    private static string[] LookupColumns(string script)
    {
        var list = Regex.Match(script, @"^\s*\$LookupColumns\s*=\s*@\(([^)]*)\)", RegexOptions.Multiline);
        return list.Success ? Regex.Matches(list.Groups[1].Value, "'([^']+)'").Select(m => m.Groups[1].Value).ToArray() : [];
    }

    private static ScriptShape Analyse(string script)
    {
        static string? Single(string text, string pattern) =>
            Regex.Match(text, pattern, RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

        var list = Regex.Match(script, @"^\s*\$Columns\s*=\s*@\(([^)]*)\)", RegexOptions.Multiline);
        var columns = list.Success
            ? Regex.Matches(list.Groups[1].Value, @"^\s*'([^']+)'\s*$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToArray()
            : [];
        var grant = Regex.Match(script, @"canread\s*=\s*(\d);\s*cancreate\s*=\s*(\$?\w+);\s*canupdate\s*=\s*(\$?\w+)");
        return new ScriptShape(
            Single(script, @"^\s*\$Table\s*=\s*'([^']+)'"),
            columns,
            LookupColumns(script),
            Single(script, @"^\s*\$WriterProfileName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$ReaderProfileName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$SystemAdministratorProfileId\s*=\s*'([^']+)'"),
            Single(script, @"Name\s*=\s*\$WriterProfileName;\s*Create\s*=\s*(\d)"),
            Single(script, @"Name\s*=\s*\$ReaderProfileName;\s*Create\s*=\s*(\d)"),
            grant.Success ? string.Join(',', grant.Groups[1].Value, grant.Groups[2].Value, grant.Groups[3].Value) : null);
    }

    /// <summary>The invoice's Entity.xml (the folder's case differs between the index and some checkouts).</summary>
    private static string EntityXml(string solution) =>
        Directory.GetDirectories(Path.Combine(solution, "Entities"))
            .Single(d => string.Equals(Path.GetFileName(d), Table, StringComparison.OrdinalIgnoreCase))
        is { } folder ? Path.Combine(folder, "Entity.xml") : throw new DirectoryNotFoundException(Table);

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
