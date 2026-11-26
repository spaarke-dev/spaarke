using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceSubmission;

/// <summary>
/// ebilling-r1 task 132 (review round 2): <c>sprk_invoicesubmission.sprk_firmnoticetext</c> is field-secured so that only the BFF touches it ("Spaarke
/// BFF-Managed Field Writers": read, create, update) and NO reader profile carries it, as for <c>sprk_findingsjson</c>. Three artifacts must agree:
/// <c>scripts/ebilling/Set-EbillingFirmNoticeTextFieldSecurity.ps1</c> (what an operator applies), the SpaarkeMaster solution source
/// (<c>Other/FieldSecurityProfiles.xml</c>; the IsSecured declaration is the ArchTest <c>FirmNoticeTextFieldSecuredGuardTests</c>) and the column the
/// handler writes (<see cref="EbillingNotificationJobHandler.FirmNoticeTextColumn"/>). Read as text (no PowerShell host, no network); the permission-row
/// check and the script parser are exercised on seeded drifts, in the shape of <see cref="RuleSetVisibilityFieldSecurityAgreementTests"/>.
/// </summary>
[Trait("Category", "Security")]
public class FirmNoticeTextFieldSecurityAgreementTests
{
    private const string Table = "sprk_invoicesubmission";
    private const string Column = "sprk_firmnoticetext";
    private const string ReaderProfile = "Spaarke BFF-Managed Field Readers";
    private const string WriterProfile = "Spaarke BFF-Managed Field Writers";
    private const string SystemAdministratorProfileId = "572329c1-a042-4e22-be47-367c6374ea45";
    private const string ScriptFile = "Set-EbillingFirmNoticeTextFieldSecurity.ps1";

    private static string Solution => Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster");

    private sealed record Row(string Profile, string Read, string Create, string Update);

    /// <summary>Every permission any profile carries on the column, as the solution source states it.</summary>
    private static Row[] Rows(XDocument profiles) =>
        profiles.Descendants("FieldSecurityProfile")
            .SelectMany(p => p.Descendants("FieldPermission")
                .Where(f => (string?)f.Element("EntityName") == Table && (string?)f.Element("AttributeName") == Column)
                .Select(f => new Row((string?)p.Attribute("name") ?? "", (string?)f.Element("CanRead") ?? "", (string?)f.Element("CanCreate") ?? "", (string?)f.Element("CanUpdate") ?? "")))
            .ToArray();

    /// <summary>The gaps: exactly one writers row 4/4/4, no readers row, and no other profile that reads, creates or updates the column.</summary>
    private static string[] Gaps(Row[] rows)
    {
        var gaps = new List<string>();
        var writers = rows.Where(r => r.Profile == WriterProfile).ToArray();

        if (writers.Length != 1) gaps.Add($"the writers profile carries {writers.Length} row(s) on {Column}, expected exactly one");
        else if (writers[0] is not { Read: "4", Create: "4", Update: "4" }) gaps.Add($"the writers row is {writers[0].Read}/{writers[0].Create}/{writers[0].Update}, expected 4/4/4");

        foreach (var reader in rows.Where(r => r.Profile == ReaderProfile))
            gaps.Add($"the readers profile carries a row on {Column} (read={reader.Read}): it is on every default team, so everyone would read the notice text");

        foreach (var other in rows.Where(r => r.Profile != WriterProfile && r.Profile != ReaderProfile && (r.Read != "0" || r.Create != "0" || r.Update != "0")))
            gaps.Add($"profile '{other.Profile}' grants read={other.Read} create={other.Create} update={other.Update} on {Column}; only the writers profile may");

        return [.. gaps];
    }

    private static XDocument LoadProfiles() => XDocument.Load(Path.Combine(Solution, "Other", "FieldSecurityProfiles.xml"));

    [Fact(DisplayName = "132: the solution carries the writers row 4/4/4 on the column, no readers row, and no other profile that reads, creates or updates it")]
    public void TheSolution_PinsTheWritersRowAndNoReadersRow()
    {
        var rows = Rows(LoadProfiles());

        Gaps(rows).Should().BeEmpty("the writers row is read=4 create=4 update=4, and nobody else may read, create or update the column");
        rows.Should().ContainSingle("only the writers profile carries a row on the column (sprk_findingsjson's shape: no readers row)");
    }

    [Fact(DisplayName = "132: the column the handler writes is the column the solution and the script secure")]
    public void TheHandlersColumn_IsTheSecuredColumn()
    {
        EbillingNotificationJobHandler.FirmNoticeTextColumn.Should().Be(Column);
        Analyse(Seed).Column.Should().Be(Column);
    }

    [Fact(DisplayName = "132 must-fire: the permission-row check reports a missing writers row, a wrong grant, a readers row and another profile with access")]
    public void ThePermissionRowCheck_SeesEachDrift()
    {
        var real = Rows(LoadProfiles());
        Gaps(real).Should().BeEmpty("the shipped rows agree");

        Gaps([]).Should().ContainSingle(g => g.Contains("writers profile carries 0"), "the writers row removed");
        Gaps([new Row(WriterProfile, "4", "4", "0")]).Should().ContainSingle(g => g.Contains("writers row is"), "the writers row cannot update");
        Gaps([new Row(WriterProfile, "4", "0", "4")]).Should().ContainSingle(g => g.Contains("writers row is"), "the writers row cannot create");
        Gaps([.. real, new Row(WriterProfile, "4", "4", "4")]).Should().ContainSingle(g => g.Contains("writers profile carries 2"), "a duplicate writers row");
        Gaps([.. real, new Row(ReaderProfile, "4", "0", "0")]).Should().ContainSingle(g => g.Contains("readers profile carries a row"), "a readers row would let everyone read it");
        Gaps([.. real, new Row("A third profile", "4", "0", "0")]).Should().ContainSingle(g => g.Contains("A third profile"), "a third profile that reads");
        Gaps([.. real, new Row("A third profile", "0", "4", "0")]).Should().ContainSingle(g => g.Contains("A third profile"), "a third profile that creates");
        Gaps([.. real, new Row("A third profile", "0", "0", "4")]).Should().ContainSingle(g => g.Contains("A third profile"), "a third profile that updates");
        Gaps([.. real, new Row("A third profile", "0", "0", "0")]).Should().BeEmpty("a third profile with no access is not a grant");
    }

    [Fact(DisplayName = "132 must-fire: with a readers row added to the real XML the real check reports it")]
    public void TheRealXml_WithAReadersRow_IsReported()
    {
        var doc = LoadProfiles();
        var readers = doc.Descendants("FieldSecurityProfile").Single(p => (string?)p.Attribute("name") == ReaderProfile);
        var permissions = readers.Descendants("FieldPermission").First().Parent!;
        permissions.Add(new XElement("FieldPermission",
            new XElement("EntityName", Table), new XElement("AttributeName", Column),
            new XElement("CanRead", "4"), new XElement("CanUpdate", "0"), new XElement("CanCreate", "0"), new XElement("CanReadUnmasked", "0")));

        Gaps(Rows(doc)).Should().ContainSingle(g => g.Contains("readers profile"));
    }

    [Fact(DisplayName = "132 must-fire: with the writers row removed from the real XML the real check reports it")]
    public void TheRealXml_WithoutTheWritersRow_IsReported()
    {
        var doc = LoadProfiles();
        doc.Descendants("FieldSecurityProfile").Single(p => (string?)p.Attribute("name") == WriterProfile).Descendants("FieldPermission")
            .Single(f => (string?)f.Element("EntityName") == Table && (string?)f.Element("AttributeName") == Column).Remove();

        Gaps(Rows(doc)).Should().NotBeEmpty("a solution without the writers row would leave the BFF unable to write the column");
    }

    // ── The script ──

    [Fact(DisplayName = "132: the script secures the column for the writers profile (read/create/update) only, pinned to the solution's names")]
    public void TheScript_AgreesWithTheSolution()
    {
        Agrees(Analyse(File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", ScriptFile)))).Should().BeTrue();
    }

    private const string Seed = """
        $Table = 'sprk_invoicesubmission'
        $Column = 'sprk_firmnoticetext'
        $WriterProfileName = 'Spaarke BFF-Managed Field Writers'
        $SystemAdministratorProfileId = '572329c1-a042-4e22-be47-367c6374ea45'
        $ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
                canread = 4; cancreate = 4; canupdate = 4
        """;

    [Theory(DisplayName = "132 must-fire: the script parser sees each drift (another table or column, a swapped profile, a changed grant, another admin profile)")]
    [InlineData("$Table = 'sprk_invoicesubmission'", "$Table = 'sprk_invoicesubmissions'")]
    [InlineData("$Column = 'sprk_firmnoticetext'", "$Column = 'sprk_firmnoticetext2'")]
    [InlineData("$WriterProfileName = 'Spaarke BFF-Managed Field Writers'", "$WriterProfileName = 'Spaarke BFF-Managed Field Readers'")]
    [InlineData("$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'", "$ReaderProfileName = 'Spaarke BFF-Managed Field Writers'")]
    [InlineData("canread = 4; cancreate = 4; canupdate = 4", "canread = 4; cancreate = 4; canupdate = 0")]
    [InlineData("canread = 4; cancreate = 4; canupdate = 4", "canread = 0; cancreate = 4; canupdate = 4")]
    [InlineData("'572329c1-a042-4e22-be47-367c6374ea45'", "'11111111-1111-1111-1111-111111111111'")]
    public void TheParser_SeesEachDrift(string from, string to)
    {
        Agrees(Analyse(Seed)).Should().BeTrue("the faithful seed agrees");
        Agrees(Analyse(Seed.ReplaceLineEndings("\n").Replace(from, to))).Should().BeFalse("each seeded drift must fail the agreement test");
    }

    // The recovery text names only what this column's failure costs (the stored notice text), not the findings column's consequences.
    [Fact(DisplayName = "132: the script's recovery message names this column's consequence (a Warning on storing the notice), not another column's")]
    public void TheRecoveryMessage_NamesThisColumnsConsequence()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", ScriptFile)).ReplaceLineEndings("\n");

        script.Should().Contain("only keeping the sent firm notice fails");
        script.Should().NotContain("Override and Return refuse", "that is the findings column's consequence");
        script.Should().NotContain("the submission job cannot finish");
    }

    private sealed record ScriptShape(
        string? Table, string? Column, string? Writer, string? Reader, string? SystemAdministrator, string? GrantShape);

    private static bool Agrees(ScriptShape s) =>
        s.Table == Table && s.Column == Column && s.Writer == WriterProfile && s.Reader == ReaderProfile
        && s.SystemAdministrator == SystemAdministratorProfileId && s.GrantShape == "4,4,4";

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
            grant.Success ? string.Join(',', grant.Groups[1].Value, grant.Groups[2].Value, grant.Groups[3].Value) : null);
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
