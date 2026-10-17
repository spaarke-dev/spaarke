using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// ebilling-r1 task 026 fix-round-1 item 2 (owner decision D-95(2)): <c>sprk_signal.sprk_firmreason</c>, the reason text a
/// firm sees, is field-secured — every user who reads Signals reads it (task 133's reader profile, on every business-unit
/// default team), and ONLY the Ontology Service writer can create or update it (its own narrow profile). Two artifacts
/// must agree: <c>scripts/ebilling/Set-EbillingFirmReasonFieldSecurity.ps1</c> (what an operator applies) and the
/// SpaarkeMaster solution source (<c>sprk_Signal/Entity.xml</c> IsSecured, <c>Other/FieldSecurityProfiles.xml</c> and the
/// profile's root component in <c>Other/Solution.xml</c>). Read as text (no PowerShell host, no network); the script
/// parser is exercised on seeded drifts.
/// </summary>
[Trait("Category", "Security")]
public class FirmReasonFieldSecurityAgreementTests
{
    private const string Table = "sprk_signal";
    private const string Column = "sprk_firmreason";
    private const string ReaderProfile = "Spaarke BFF-Managed Field Readers";
    private const string WriterProfile = "Spaarke Ontology Writer Firm Reason Writers";
    private const string WriterProfileId = "ec323a05-f95c-4308-aac5-6d899032691e";

    [Fact]
    public void TheScript_SecuresTheFirmReason_WithTheReaderProfile_AndTheOntologyWritersOwnProfile()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", "Set-EbillingFirmReasonFieldSecurity.ps1"));

        Agrees(Analyse(script)).Should().BeTrue("the script must secure sprk_signal.sprk_firmreason with the reader and the narrow writer profile");
        Regex.IsMatch(script, @"canread\s*=\s*4;\s*cancreate\s*=\s*\$Create;\s*canupdate\s*=\s*\$Create").Should().BeTrue(
            "the reader is granted read only (Create 0), the writer read/create/update (Create 4)");
    }

    [Fact]
    public void TheSolution_SecuresTheFirmReason_TheReaderReadsIt_AndOnlyTheNarrowProfileWritesIt()
    {
        var solution = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster");

        var attribute = XDocument.Load(Path.Combine(solution, "Entities", "sprk_Signal", "Entity.xml"))
            .Descendants("attribute")
            .Single(a => (string?)a.Element("LogicalName") == Column);
        ((string?)attribute.Element("IsSecured")).Should().Be("1", $"{Table}.{Column} is field-secured in the solution");

        var profiles = XDocument.Load(Path.Combine(solution, "Other", "FieldSecurityProfiles.xml")).Descendants("FieldSecurityProfile").ToArray();
        static XElement[] OnTheColumn(XElement profile) => profile.Descendants("FieldPermission")
            .Where(p => (string?)p.Element("EntityName") == Table && (string?)p.Element("AttributeName") == Column).ToArray();

        var reader = profiles.Single(p => (string?)p.Attribute("name") == ReaderProfile);
        var read = OnTheColumn(reader).Should().ContainSingle("the reader profile carries one permission on the column").Subject;
        ((string?)read.Element("CanRead")).Should().Be("4");
        ((string?)read.Element("CanCreate")).Should().Be("0");
        ((string?)read.Element("CanUpdate")).Should().Be("0");

        var writer = profiles.Single(p => (string?)p.Attribute("name") == WriterProfile);
        ((string?)writer.Attribute("fieldsecurityprofileid")).Should().Be("{" + WriterProfileId + "}", "the id the script creates live");
        var write = writer.Descendants("FieldPermission").Should().ContainSingle("the narrow profile holds the firm reason and nothing else").Subject;
        ((string?)write.Element("EntityName")).Should().Be(Table);
        ((string?)write.Element("AttributeName")).Should().Be(Column);
        ((string?)write.Element("CanRead")).Should().Be("4");
        ((string?)write.Element("CanCreate")).Should().Be("4");
        ((string?)write.Element("CanUpdate")).Should().Be("4");

        profiles.Where(p => (string?)p.Attribute("name") != WriterProfile)
            .SelectMany(OnTheColumn)
            .Should().OnlyContain(p => (string?)p.Element("CanCreate") == "0" && (string?)p.Element("CanUpdate") == "0",
                "no other profile in the solution may write the firm reason");

        File.ReadAllText(Path.Combine(solution, "Other", "Solution.xml"))
            .Should().Contain($"<RootComponent type=\"70\" id=\"{{{WriterProfileId}}}\" behavior=\"0\" />", "the profile travels in SpaarkeMaster");
    }

    private const string Seed = """
        $Table = 'sprk_signal'
        $Column = 'sprk_firmreason'
        $ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
        $WriterProfileName = 'Spaarke Ontology Writer Firm Reason Writers'
        $WriterProfileFixedId = 'ec323a05-f95c-4308-aac5-6d899032691e'
        $Secured = $true
        """;

    [Theory]
    [InlineData("$Secured = $true", "$Secured = $false")]
    [InlineData("$Table = 'sprk_signal'", "$Table = 'sprk_decisionrecord'")]
    [InlineData("'sprk_firmreason'", "'sprk_description'")]
    [InlineData("Firm Reason Writers'", "Field Writers'")]
    [InlineData("Managed Field Readers'", "Managed Field Writers'")]
    [InlineData("'ec323a05-f95c-4308-aac5-6d899032691e'", "'b710d90c-0f27-4d6a-a146-2769c8aa921f'")]
    public void TheParser_SeesEachDrift(string from, string to)
    {
        Agrees(Analyse(Seed)).Should().BeTrue("the faithful seed agrees");
        Agrees(Analyse(Seed.ReplaceLineEndings("\n").Replace(from, to))).Should().BeFalse("each seeded drift must fail the agreement test");
    }

    private sealed record ScriptShape(string? Table, string? Column, string? Reader, string? Writer, string? WriterId, bool Secured);

    private static bool Agrees(ScriptShape s) =>
        s.Table == Table && s.Column == Column && s.Reader == ReaderProfile && s.Writer == WriterProfile
        && s.WriterId == WriterProfileId && s.Secured;

    private static ScriptShape Analyse(string script)
    {
        static string? Single(string text, string pattern) =>
            Regex.Match(text, pattern, RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

        return new ScriptShape(
            Single(script, @"^\s*\$Table\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$Column\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$ReaderProfileName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$WriterProfileName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$WriterProfileFixedId\s*=\s*'([^']+)'"),
            string.Equals(Single(script, @"^\s*\$Secured\s*=\s*\$(\w+)"), "true", StringComparison.OrdinalIgnoreCase));
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
