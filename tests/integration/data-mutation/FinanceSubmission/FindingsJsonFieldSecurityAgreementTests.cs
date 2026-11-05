using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceSubmission;

/// <summary>
/// ebilling-r1 task 095d-1c (uac-r2 final review F4-1): <c>sprk_invoicesubmission.sprk_findingsjson</c> holds the stored <c>assignFirm</c> member that
/// decides the immutable Decision Record, so field security is one of the two things that keep a person from writing it (the other is the BFF's
/// app-only refusal, <c>InheritedAccessPermission.AccessColumns</c>). The column is secured to the BFF application user ("Spaarke BFF-Managed Field
/// Writers": read, create, update = 4/4/4) and NO reader profile is granted (task 122, #2235). Three artifacts must agree: the BFF's column name
/// (<see cref="InvoiceSubmissionPersister.SubmissionFindingsJson"/>), <c>scripts/ebilling/Set-EbillingFindingsJsonFieldSecurity.ps1</c> (what an
/// operator applies) and the SpaarkeMaster solution source (<c>Entity.xml</c> IsSecured and <c>Other/FieldSecurityProfiles.xml</c>). Read as text (no
/// PowerShell host, no network). The solution check is a pure function of the two XML documents, so the controls feed it mutated copies (a reader
/// profile granted, a changed writer mask, an unsecured column) and the script parser is exercised on seeded drifts. Known limit (platform rule,
/// the same for every secured column): a System Administrator keeps full access to a secured column.
/// </summary>
[Trait("Category", "Security")]
public class FindingsJsonFieldSecurityAgreementTests
{
    private const string Table = "sprk_invoicesubmission";
    private const string Column = InvoiceSubmissionPersister.SubmissionFindingsJson;
    private const string ReaderProfile = "Spaarke BFF-Managed Field Readers";
    private const string WriterProfile = "Spaarke BFF-Managed Field Writers";
    private const string SystemAdministratorProfileId = "572329c1-a042-4e22-be47-367c6374ea45";

    [Fact(DisplayName = "095d-1c F4-1: the BFF's column name is the one the agreement pins")]
    public void TheColumnConstant_IsTheSecuredColumn() => Column.Should().Be("sprk_findingsjson");

    [Fact(DisplayName = "095d-1c F4-1: the script secures the column for the reused writer profile (read, create, update = 4/4/4) and grants no reader profile")]
    public void TheScript_SecuresTheColumn_TheWriterWritesIt_AndNoReaderIsGranted()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", "Set-EbillingFindingsJsonFieldSecurity.ps1"));

        Agrees(Analyse(script)).Should().BeTrue("the script must secure sprk_invoicesubmission.sprk_findingsjson for the reused writer profile only");
    }

    [Fact(DisplayName = "095d-1c F4-1: the solution secures the column, the writer profile holds 4/4/4 on it, and no other profile has any access")]
    public void TheSolution_SecuresTheColumn_OnlyTheWriterHasAccess()
    {
        var (entity, profiles) = LoadSolution();

        Violations(entity, profiles).Should().BeEmpty();
    }

    // ── Controls: the check fires on each drift of the solution source (mutated in memory) ──

    [Fact(DisplayName = "095d-1c F4-1 control: a reader profile granted read on the column is a violation")]
    public void TheCheck_SeesAReaderProfileGrant()
    {
        var (entity, profiles) = LoadSolution();
        Violations(entity, profiles).Should().BeEmpty("the faithful solution agrees");

        var reader = profiles.Descendants("FieldSecurityProfile").Single(p => (string?)p.Attribute("name") == ReaderProfile);
        reader.Add(new XElement("FieldPermission",
            new XElement("EntityName", Table), new XElement("AttributeName", Column),
            new XElement("CanRead", "4"), new XElement("CanUpdate", "0"), new XElement("CanCreate", "0"), new XElement("CanReadUnmasked", "0")));

        Violations(entity, profiles).Should().NotBeEmpty("the reader profile is on every default team: a grant would show the findings to everyone");
    }

    [Theory(DisplayName = "095d-1c F4-1 control: a writer profile mask other than 4/4/4 is a violation")]
    [InlineData("CanRead", "0")]
    [InlineData("CanCreate", "0")]
    [InlineData("CanUpdate", "0")]
    [InlineData("CanUpdate", "1")]
    public void TheCheck_SeesAChangedWriterMask(string element, string value)
    {
        var (entity, profiles) = LoadSolution();
        Violations(entity, profiles).Should().BeEmpty("the faithful solution agrees");

        WriterRow(profiles).Element(element)!.Value = value;

        Violations(entity, profiles).Should().NotBeEmpty($"{element}={value} on the writer profile is not 4/4/4");
    }

    [Fact(DisplayName = "095d-1c F4-1 control: another profile granted update on the column is a violation, and the platform's System Administrator profile is exempt")]
    public void TheCheck_SeesAnotherProfileWithUpdate_ButNotTheSystemAdministratorProfile()
    {
        var (entity, profiles) = LoadSolution();

        var other = profiles.Descendants("FieldSecurityProfile").First(p => (string?)p.Attribute("name") != WriterProfile && (string?)p.Attribute("name") != ReaderProfile);
        other.Add(new XElement("FieldPermission",
            new XElement("EntityName", Table), new XElement("AttributeName", Column),
            new XElement("CanRead", "0"), new XElement("CanUpdate", "4"), new XElement("CanCreate", "0")));
        Violations(entity, profiles).Should().NotBeEmpty("no profile but the writer may update the column");

        other.SetAttributeValue("fieldsecurityprofileid", "{" + SystemAdministratorProfileId + "}");
        Violations(entity, profiles).Should().BeEmpty("the platform's own System Administrator profile has a row on every secured column");
    }

    [Fact(DisplayName = "095d-1c F4-1 control: an unsecured column is a violation")]
    public void TheCheck_SeesAnUnsecuredColumn()
    {
        var (entity, profiles) = LoadSolution();

        entity.Descendants("attribute").Single(a => (string?)a.Element("LogicalName") == Column).Element("IsSecured")!.Value = "0";

        Violations(entity, profiles).Should().NotBeEmpty();
    }

    private static (XDocument Entity, XDocument Profiles) LoadSolution()
    {
        var solution = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster");
        return (XDocument.Load(Path.Combine(solution, "Entities", Table, "Entity.xml")),
            XDocument.Load(Path.Combine(solution, "Other", "FieldSecurityProfiles.xml")));
    }

    private static XElement WriterRow(XDocument profiles) =>
        OnTheColumn(profiles.Descendants("FieldSecurityProfile").Single(p => (string?)p.Attribute("name") == WriterProfile)).Single();

    private static XElement[] OnTheColumn(XElement profile) => profile.Descendants("FieldPermission")
        .Where(p => (string?)p.Element("EntityName") == Table && (string?)p.Element("AttributeName") == Column).ToArray();

    /// <summary>Every way the solution source disagrees with "secured; the writer profile 4/4/4; no reader; no other profile".</summary>
    private static List<string> Violations(XDocument entity, XDocument profilesDocument)
    {
        var found = new List<string>();
        var attribute = entity.Descendants("attribute").SingleOrDefault(a => (string?)a.Element("LogicalName") == Column);
        if ((string?)attribute?.Element("IsSecured") != "1")
            found.Add($"{Table}.{Column} is not IsSecured=1 in Entity.xml");

        var profiles = profilesDocument.Descendants("FieldSecurityProfile").ToArray();
        var writers = profiles.Where(p => (string?)p.Attribute("name") == WriterProfile).ToArray();
        if (writers.Length != 1)
        {
            found.Add($"expected exactly one {WriterProfile} profile, found {writers.Length}");
            return found;
        }

        var rows = OnTheColumn(writers[0]);
        if (rows.Length != 1)
            found.Add($"the writer profile carries {rows.Length} permissions on the column, expected 1");
        else if ((string?)rows[0].Element("CanRead") != "4" || (string?)rows[0].Element("CanCreate") != "4" || (string?)rows[0].Element("CanUpdate") != "4")
            found.Add("the writer profile's read/create/update is not 4/4/4");

        foreach (var profile in profiles.Where(p => (string?)p.Attribute("name") != WriterProfile
                     && !((string?)p.Attribute("fieldsecurityprofileid") ?? string.Empty).Trim('{', '}').Equals(SystemAdministratorProfileId, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var row in OnTheColumn(profile))
            {
                if ((string?)row.Element("CanRead") != "0" || (string?)row.Element("CanCreate") != "0" || (string?)row.Element("CanUpdate") != "0")
                    found.Add($"profile '{(string?)profile.Attribute("name")}' grants access to the column" + ((string?)profile.Attribute("name") == ReaderProfile ? " (the reader profile)" : string.Empty));
            }
        }

        return found;
    }

    // ── The script ──

    private const string Seed = """
        $Table = 'sprk_invoicesubmission'
        $Column = 'sprk_findingsjson'
        $WriterProfileName = 'Spaarke BFF-Managed Field Writers'
        $SystemAdministratorProfileId = '572329c1-a042-4e22-be47-367c6374ea45'
        $ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
                canread = 4; cancreate = 4; canupdate = 4
        if ($readerProfile -and ($r = Get-Permission $readerProfile.fieldsecurityprofileid)) {
            Report 'FAIL' "$ReaderProfileName has a row on $Table.$Column"
        }
        """;

    [Theory(DisplayName = "095d-1c F4-1: the script parser sees each drift (another table or column, a swapped profile, a changed grant, no reader check)")]
    [InlineData("$Table = 'sprk_invoicesubmission'", "$Table = 'sprk_invoice'")]
    [InlineData("'sprk_findingsjson'", "'sprk_resubmissionof'")]
    [InlineData("Managed Field Writers'\n", "Managed Field Readers'\n")]
    [InlineData("canread = 4; cancreate = 4; canupdate = 4", "canread = 4; cancreate = 0; canupdate = 0")]
    [InlineData("canread = 4; cancreate = 4; canupdate = 4", "canread = 4; cancreate = 4; canupdate = 0")]
    [InlineData("'572329c1-a042-4e22-be47-367c6374ea45'", "'11111111-1111-1111-1111-111111111111'")]
    [InlineData("if ($readerProfile -and ($r = Get-Permission $readerProfile.fieldsecurityprofileid)) {", "if ($false) {")]
    public void TheParser_SeesEachDrift(string from, string to)
    {
        Agrees(Analyse(Seed)).Should().BeTrue("the faithful seed agrees");
        Agrees(Analyse(Seed.ReplaceLineEndings("\n").Replace(from, to))).Should().BeFalse("each seeded drift must fail the agreement test");
    }

    [Fact(DisplayName = "095d-1c F4-1: a script that grants the reader profile on the column is a drift")]
    public void TheParser_SeesAReaderGrant()
    {
        Agrees(Analyse(Seed)).Should().BeTrue("the faithful seed agrees");
        Agrees(Analyse(Seed + "\n$specs += @{ Id = $reader.fieldsecurityprofileid; Name = $ReaderProfileName; Create = 0 }")).Should()
            .BeFalse("the reader profile is on every default team: a grant would show the findings to everyone");
    }

    private sealed record ScriptShape(
        string? Table, string? Column, string? Writer, string? Reader, string? SystemAdministrator, string? Grant, bool ChecksReaderHasNoRow, bool GrantsReader);

    private static bool Agrees(ScriptShape s) =>
        s.Table == Table && s.Column == Column && s.Writer == WriterProfile && s.Reader == ReaderProfile
        && s.SystemAdministrator == SystemAdministratorProfileId && s.Grant == "4,4,4" && s.ChecksReaderHasNoRow && !s.GrantsReader;

    private static ScriptShape Analyse(string script)
    {
        static string? Single(string text, string pattern) =>
            Regex.Match(text, pattern, RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

        var grant = Regex.Match(script, @"canread\s*=\s*(\d);\s*cancreate\s*=\s*(\d);\s*canupdate\s*=\s*(\d)");
        return new ScriptShape(
            Single(script, @"^\s*\$Table\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$Column\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$WriterProfileName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$ReaderProfileName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$SystemAdministratorProfileId\s*=\s*'([^']+)'"),
            grant.Success ? string.Join(',', grant.Groups[1].Value, grant.Groups[2].Value, grant.Groups[3].Value) : null,
            Regex.IsMatch(script, @"if\s*\(\s*\$readerProfile\s+-and\s+\(\s*\$r\s*=\s*Get-Permission\s+\$readerProfile\.fieldsecurityprofileid\s*\)\s*\)\s*\{[^}]*Report\s+'FAIL'"),
            Regex.IsMatch(script, @"Name\s*=\s*\$ReaderProfileName\s*;\s*Create\s*="));
    }

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
