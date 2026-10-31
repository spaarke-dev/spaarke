using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceSubmission;

/// <summary>
/// ebilling-r1 (task 097 second review F2, owner decision 2026-10-30): <c>sprk_invoicesubmission.sprk_resubmissionof</c>, the earlier
/// submission a Correction replaces, is field-secured so that only the server sets it: the BFF application user ("Spaarke BFF-Managed
/// Field Writers": read, create, update) writes it, and every other user reads it ("Spaarke BFF-Managed Field Readers": read only).
/// Two artifacts must agree: <c>scripts/ebilling/Set-EbillingResubmissionOfFieldSecurity.ps1</c> (what an operator applies) and the
/// SpaarkeMaster solution source (<c>Entity.xml</c> IsSecured and <c>Other/FieldSecurityProfiles.xml</c>). Read as text (no PowerShell
/// host, no network); the script parser is exercised on seeded drifts. The BFF side (every write goes through the app-only client) is
/// pinned by <c>ResubmissionOfAppOnlyWriteGuardTests</c> in Spaarke.ArchTests.
/// </summary>
[Trait("Category", "Security")]
public class ResubmissionOfFieldSecurityAgreementTests
{
    private const string Table = "sprk_invoicesubmission";
    private const string Column = "sprk_resubmissionof";
    private const string ReaderProfile = "Spaarke BFF-Managed Field Readers";
    private const string WriterProfile = "Spaarke BFF-Managed Field Writers";
    private const string SystemAdministratorProfileId = "572329c1-a042-4e22-be47-367c6374ea45";

    [Fact]
    public void TheScript_SecuresTheColumn_TheWriterWritesIt_AndTheReaderOnlyReadsIt()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", "Set-EbillingResubmissionOfFieldSecurity.ps1"));

        Agrees(Analyse(script)).Should().BeTrue("the script must secure sprk_invoicesubmission.sprk_resubmissionof for the reused writer and reader profiles");
    }

    [Fact]
    public void TheSolution_SecuresTheColumn_OnlyTheWriterWritesIt_AndTheReaderReadsIt()
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

        var reader = OnTheColumn(profiles.Single(p => (string?)p.Attribute("name") == ReaderProfile))
            .Should().ContainSingle("the reader profile carries one permission on the column").Subject;
        ((string?)reader.Element("CanRead")).Should().Be("4", "every user keeps reading the Correction target");
        ((string?)reader.Element("CanCreate")).Should().Be("0");
        ((string?)reader.Element("CanUpdate")).Should().Be("0");

        profiles.Where(p => (string?)p.Attribute("name") != WriterProfile)
            .SelectMany(OnTheColumn)
            .Should().OnlyContain(p => (string?)p.Element("CanCreate") == "0" && (string?)p.Element("CanUpdate") == "0",
                "no other profile in the solution may create or update the column");
    }

    private const string Seed = """
        $Table = 'sprk_invoicesubmission'
        $Column = 'sprk_resubmissionof'
        $WriterProfileName = 'Spaarke BFF-Managed Field Writers'
        $ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
        $SystemAdministratorProfileId = '572329c1-a042-4e22-be47-367c6374ea45'
        if ($writer) { $specs += @{ Id = $writer.fieldsecurityprofileid; Name = $WriterProfileName; Create = 4 } }
        if ($reader) { $specs += @{ Id = $reader.fieldsecurityprofileid; Name = $ReaderProfileName; Create = 0 } }
        """;

    [Theory]
    [InlineData("$Table = 'sprk_invoicesubmission'", "$Table = 'sprk_invoice'")]
    [InlineData("'sprk_resubmissionof'", "'sprk_supersedesinvoice'")]
    [InlineData("Managed Field Writers'\n", "Managed Field Readers'\n")]
    [InlineData("Name = $ReaderProfileName; Create = 0", "Name = $ReaderProfileName; Create = 4")]
    [InlineData("Name = $WriterProfileName; Create = 4", "Name = $WriterProfileName; Create = 0")]
    [InlineData("'572329c1-a042-4e22-be47-367c6374ea45'", "'11111111-1111-1111-1111-111111111111'")]
    public void TheParser_SeesEachDrift(string from, string to)
    {
        Agrees(Analyse(Seed)).Should().BeTrue("the faithful seed agrees");
        Agrees(Analyse(Seed.ReplaceLineEndings("\n").Replace(from, to))).Should().BeFalse("each seeded drift must fail the agreement test");
    }

    private sealed record ScriptShape(
        string? Table, string? Column, string? Writer, string? Reader, string? SystemAdministrator, string? WriterCreate, string? ReaderCreate);

    private static bool Agrees(ScriptShape s) =>
        s.Table == Table && s.Column == Column && s.Writer == WriterProfile && s.Reader == ReaderProfile
        && s.SystemAdministrator == SystemAdministratorProfileId && s.WriterCreate == "4" && s.ReaderCreate == "0";

    private static ScriptShape Analyse(string script)
    {
        static string? Single(string text, string pattern) =>
            Regex.Match(text, pattern, RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

        return new ScriptShape(
            Single(script, @"^\s*\$Table\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$Column\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$WriterProfileName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$ReaderProfileName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$SystemAdministratorProfileId\s*=\s*'([^']+)'"),
            Single(script, @"Name\s*=\s*\$WriterProfileName;\s*Create\s*=\s*(\d)"),
            Single(script, @"Name\s*=\s*\$ReaderProfileName;\s*Create\s*=\s*(\d)"));
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
