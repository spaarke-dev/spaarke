using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceSubmission;

/// <summary>
/// ebilling-r1 (task 095a review F2, uac-r2 binding): <c>sprk_invoicesubmission.sprk_sourceemaildocument</c>, the link from a from-email
/// submission to the <c>.eml</c> Document it was read from, is field-secured to the BFF application user ("Spaarke BFF-Managed Field Writers":
/// read, create, update) and NO reader profile is granted (the server-only shape of <c>sprk_findingsjson</c>, task 122; unlike
/// <c>sprk_resubmissionof</c>, no user reads it). Two artifacts must agree: <c>scripts/ebilling/Set-EbillingSourceEmailFieldSecurity.ps1</c>
/// (what an operator applies) and the SpaarkeMaster solution source (<c>Entity.xml</c> IsSecured and <c>Other/FieldSecurityProfiles.xml</c>).
/// Read as text (no PowerShell host, no network); the script parser is exercised on seeded drifts. The BFF side (every read and write goes
/// through the app-only client, so a user's null read never matters) is pinned by <c>SubmissionSourceEmailAppOnlyGuardTests</c> in
/// Spaarke.ArchTests.
/// </summary>
[Trait("Category", "Security")]
public class SourceEmailFieldSecurityAgreementTests
{
    private const string Table = "sprk_invoicesubmission";
    private const string Column = "sprk_sourceemaildocument";
    private const string ReaderProfile = "Spaarke BFF-Managed Field Readers";
    private const string WriterProfile = "Spaarke BFF-Managed Field Writers";
    private const string SystemAdministratorProfileId = "572329c1-a042-4e22-be47-367c6374ea45";

    [Fact(DisplayName = "095a F2: the script secures the column for the reused writer profile (read, create, update) and grants no reader profile")]
    public void TheScript_SecuresTheColumn_TheWriterWritesIt_AndNoReaderIsGranted()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", "Set-EbillingSourceEmailFieldSecurity.ps1"));

        Agrees(Analyse(script)).Should().BeTrue("the script must secure sprk_invoicesubmission.sprk_sourceemaildocument for the reused writer profile only");
    }

    [Fact(DisplayName = "095a F2: the solution secures the column, the writer profile reads, creates and updates it, and no other profile has any access")]
    public void TheSolution_SecuresTheColumn_OnlyTheWriterHasAccess()
    {
        var solution = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster");

        var attribute = XDocument.Load(Path.Combine(solution, "Entities", Table, "Entity.xml"))
            .Descendants("attribute")
            .Single(a => (string?)a.Element("LogicalName") == Column);
        ((string?)attribute.Element("IsSecured")).Should().Be("1", $"{Table}.{Column} is field-secured in the solution");

        var profiles = XDocument.Load(Path.Combine(solution, "Other", "FieldSecurityProfiles.xml")).Descendants("FieldSecurityProfile").ToArray();
        static XElement[] OnTheColumn(XElement profile) => profile.Descendants("FieldPermission")
            .Where(p => (string?)p.Element("EntityName") == Table && (string?)p.Element("AttributeName") == Column).ToArray();

        var writer = OnTheColumn(profiles.Single(p => (string?)p.Attribute("name") == WriterProfile))
            .Should().ContainSingle("the writer profile carries one permission on the column").Subject;
        ((string?)writer.Element("CanRead")).Should().Be("4");
        ((string?)writer.Element("CanCreate")).Should().Be("4");
        ((string?)writer.Element("CanUpdate")).Should().Be("4");

        // Server-only: the reader profile (on every default team) and every other profile carry no row, or a row that grants nothing.
        profiles.Single(p => (string?)p.Attribute("name") == ReaderProfile).Descendants("FieldPermission")
            .Where(p => (string?)p.Element("EntityName") == Table && (string?)p.Element("AttributeName") == Column)
            .Should().BeEmpty("the column is never read by users: the reader profile is not granted");
        profiles.Where(p => (string?)p.Attribute("name") != WriterProfile)
            .SelectMany(OnTheColumn)
            .Where(p => (string?)p.Element("CanRead") != "0" || (string?)p.Element("CanCreate") != "0" || (string?)p.Element("CanUpdate") != "0")
            .Should().BeEmpty("no profile but the writer may read, create or update the column");
    }

    private const string Seed = """
        $Table = 'sprk_invoicesubmission'
        $Column = 'sprk_sourceemaildocument'
        $WriterProfileName = 'Spaarke BFF-Managed Field Writers'
        $SystemAdministratorProfileId = '572329c1-a042-4e22-be47-367c6374ea45'
        $ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
                canread = 4; cancreate = 4; canupdate = 4
        if ($readerProfile -and ($r = Get-Permission $readerProfile.fieldsecurityprofileid)) {
            Report 'FAIL' "$ReaderProfileName has a row on $Table.$Column"
        }
        """;

    [Theory(DisplayName = "095a F2: the script parser sees each drift (another table or column, a swapped profile, a changed grant, a reader grant, no reader check)")]
    [InlineData("$Table = 'sprk_invoicesubmission'", "$Table = 'sprk_invoice'")]
    [InlineData("'sprk_sourceemaildocument'", "'sprk_resubmissionof'")]
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

    [Fact(DisplayName = "095a F2: a script that grants the reader profile read on the column is a drift")]
    public void TheParser_SeesAReaderGrant()
    {
        Agrees(Analyse(Seed)).Should().BeTrue("the faithful seed agrees");
        Agrees(Analyse(Seed + "\n$specs += @{ Id = $reader.fieldsecurityprofileid; Name = $ReaderProfileName; Create = 0 }")).Should()
            .BeFalse("the reader profile is on every default team: a grant would show the link to everyone");
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
