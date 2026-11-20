using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceSubmission;

/// <summary>
/// ebilling-r1 ISS-040 (#2901): <c>sprk_ruleset.sprk_visibletofirms</c> is field-secured so that only the BFF changes it ("Spaarke BFF-Managed
/// Field Writers": read, create, update) while every user keeps reading it ("Spaarke BFF-Managed Field Readers": read only). Three artifacts must
/// agree: <c>scripts/ebilling/Set-EbillingRuleSetVisibilityFieldSecurity.ps1</c> (what an operator applies), the SpaarkeMaster solution source
/// (<c>Entity.xml</c> IsSecured and the two <c>Other/FieldSecurityProfiles.xml</c> permission rows) and the guard
/// (<c>RuleSetVisibilityFieldSecuredGuardTests</c>, which covers the IsSecured declaration). Read as text (no PowerShell host, no network); the
/// script parser and the permission-row check are exercised on seeded drifts, in the shape of
/// <see cref="InvoiceMoneyFieldSecurityAgreementTests"/>.
/// </summary>
[Trait("Category", "Security")]
public class RuleSetVisibilityFieldSecurityAgreementTests
{
    private const string Table = "sprk_ruleset";
    private const string Column = "sprk_visibletofirms";
    private const string ReaderProfile = "Spaarke BFF-Managed Field Readers";
    private const string WriterProfile = "Spaarke BFF-Managed Field Writers";
    private const string ReaderProfileId = "42e542c8-47bf-f111-aaaf-0022482913fc";
    private const string SystemAdministratorProfileId = "572329c1-a042-4e22-be47-367c6374ea45";
    private const string ScriptFile = "Set-EbillingRuleSetVisibilityFieldSecurity.ps1";

    private static string Solution => Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster");

    // ── The solution source: the two permission rows ──

    private sealed record Row(string Profile, string Read, string Create, string Update);

    /// <summary>Every permission any profile carries on the column, as the solution source states it.</summary>
    private static Row[] Rows(XDocument profiles) =>
        profiles.Descendants("FieldSecurityProfile")
            .SelectMany(p => p.Descendants("FieldPermission")
                .Where(f => (string?)f.Element("EntityName") == Table && (string?)f.Element("AttributeName") == Column)
                .Select(f => new Row((string?)p.Attribute("name") ?? "", (string?)f.Element("CanRead") ?? "", (string?)f.Element("CanCreate") ?? "", (string?)f.Element("CanUpdate") ?? "")))
            .ToArray();

    /// <summary>The gaps in the rows: the readers row must be 4/0/0, the writers row 4/4/4, and no other profile may create or update.</summary>
    private static string[] Gaps(Row[] rows)
    {
        var gaps = new List<string>();
        var readers = rows.Where(r => r.Profile == ReaderProfile).ToArray();
        var writers = rows.Where(r => r.Profile == WriterProfile).ToArray();

        if (readers.Length != 1) gaps.Add($"the readers profile carries {readers.Length} row(s) on {Column}, expected exactly one");
        else if (readers[0] is not { Read: "4", Create: "0", Update: "0" }) gaps.Add($"the readers row is {readers[0].Read}/{readers[0].Create}/{readers[0].Update}, expected read=4 create=0 update=0");

        if (writers.Length != 1) gaps.Add($"the writers profile carries {writers.Length} row(s) on {Column}, expected exactly one");
        else if (writers[0] is not { Read: "4", Create: "4", Update: "4" }) gaps.Add($"the writers row is {writers[0].Read}/{writers[0].Create}/{writers[0].Update}, expected 4/4/4");

        foreach (var other in rows.Where(r => r.Profile != ReaderProfile && r.Profile != WriterProfile && (r.Create != "0" || r.Update != "0")))
            gaps.Add($"profile '{other.Profile}' grants create={other.Create} update={other.Update} on {Column}; only the writers profile may");

        return [.. gaps];
    }

    private static XDocument LoadProfiles() => XDocument.Load(Path.Combine(Solution, "Other", "FieldSecurityProfiles.xml"));

    [Fact(DisplayName = "ISS-040: the solution carries the readers row 4/0/0 and the writers row 4/4/4 on the column, and no other profile creates or updates it")]
    public void TheSolution_PinsTheTwoPermissionRows()
    {
        var rows = Rows(LoadProfiles());

        Gaps(rows).Should().BeEmpty("the readers row is read=4 create=0 update=0, the writers row 4/4/4, and nobody else may create or update the column");
        rows.Should().HaveCount(2, "only the readers and the writers profile carry a row on the column");
    }

    [Fact(DisplayName = "ISS-040: the column is declared field-secured (IsSecured 1) in the SpaarkeMaster sprk_ruleset Entity.xml")]
    public void TheSolution_SecuresTheColumn()
    {
        var entity = Directory.GetDirectories(Path.Combine(Solution, "Entities"))
            .Single(d => string.Equals(Path.GetFileName(d), Table, StringComparison.OrdinalIgnoreCase));
        var attribute = XDocument.Load(Path.Combine(entity, "Entity.xml")).Descendants("attribute")
            .Single(a => (string?)a.Element("LogicalName") == Column);

        ((string?)attribute.Element("IsSecured")).Should().Be("1");
    }

    [Fact(DisplayName = "ISS-040: the readers profile id the script names is the id the solution source carries")]
    public void TheReaderProfileId_AgreesWithTheSolution()
    {
        var reader = LoadProfiles().Descendants("FieldSecurityProfile").Single(p => (string?)p.Attribute("name") == ReaderProfile);

        ((string?)reader.Attribute("fieldsecurityprofileid")).Should().BeEquivalentTo("{" + ReaderProfileId + "}");
        Analyse(Seed).ReaderId.Should().Be(ReaderProfileId);
    }

    [Fact(DisplayName = "ISS-040 must-fire: the permission-row check reports a missing readers row, a wrong grant and another profile that updates the column")]
    public void ThePermissionRowCheck_SeesEachDrift()
    {
        var real = Rows(LoadProfiles());
        Gaps(real).Should().BeEmpty("the shipped rows agree");

        Gaps(real.Where(r => r.Profile != ReaderProfile).ToArray()).Should().ContainSingle(g => g.Contains("readers profile carries 0"), "the readers row removed");
        Gaps(real.Where(r => r.Profile != WriterProfile).ToArray()).Should().ContainSingle(g => g.Contains("writers profile carries 0"), "the writers row removed");
        Gaps([.. real.Where(r => r.Profile != ReaderProfile), new Row(ReaderProfile, "4", "4", "0")]).Should().ContainSingle(g => g.Contains("readers row is"), "the readers row creates");
        Gaps([.. real.Where(r => r.Profile != ReaderProfile), new Row(ReaderProfile, "4", "0", "4")]).Should().ContainSingle(g => g.Contains("readers row is"), "the readers row updates");
        Gaps([.. real.Where(r => r.Profile != WriterProfile), new Row(WriterProfile, "4", "4", "0")]).Should().ContainSingle(g => g.Contains("writers row is"), "the writers row cannot update");
        Gaps([.. real, new Row("A third profile", "4", "0", "4")]).Should().ContainSingle(g => g.Contains("A third profile"), "a third profile updates");
        Gaps([.. real, new Row("A third profile", "4", "4", "0")]).Should().ContainSingle(g => g.Contains("A third profile"), "a third profile creates");
        Gaps([.. real, new Row(ReaderProfile, "4", "0", "0")]).Should().NotBeEmpty("a duplicate readers row");
        Gaps([.. real, new Row("A third profile", "4", "0", "0")]).Should().BeEmpty("a third profile that only reads is not a create or update grant");
    }

    [Fact(DisplayName = "ISS-040 must-fire: with the readers row removed from the real XML the real check reports it")]
    public void TheRealXml_WithoutTheReadersRow_IsReported()
    {
        var doc = LoadProfiles();
        doc.Descendants("FieldSecurityProfile").Single(p => (string?)p.Attribute("name") == ReaderProfile).Descendants("FieldPermission")
            .Single(f => (string?)f.Element("EntityName") == Table && (string?)f.Element("AttributeName") == Column).Remove();

        Gaps(Rows(doc)).Should().NotBeEmpty("a solution without the readers row would mask the flag from every user");
    }

    // ── The script ──

    [Fact(DisplayName = "ISS-040: the script secures the column for the writer (read/create/update) and the reader (read), pinned to the solution's names")]
    public void TheScript_AgreesWithTheSolution()
    {
        Agrees(Analyse(File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", ScriptFile)))).Should().BeTrue();
    }

    [Fact(DisplayName = "ISS-040: the script has -WhatIf, a plain write, -Verify, -Revert and a mandatory -BffApplicationIds")]
    public void TheScript_HasItsModesAndTheBffApplicationIds()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", ScriptFile));

        script.Should().Contain("SupportsShouldProcess = $true", "-WhatIf and -Confirm");
        script.Should().Contain("[switch]$Verify");
        script.Should().Contain("[switch]$Revert");
        script.Should().Contain("[Parameter(Mandatory)][string[]]$BffApplicationIds");
    }

    [Fact(DisplayName = "ISS-040: the writers-membership check and the write-run refusal both run before the column is secured")]
    public void TheMembershipCheck_RunsBeforeTheColumnIsSecured()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", ScriptFile)).ReplaceLineEndings("\n");

        var membership = script.IndexOf("is NOT a member of $WriterProfileName", StringComparison.Ordinal);
        var refusal = script.IndexOf("REFUSED: a write run needs every precondition to pass", StringComparison.Ordinal);
        var secure = script.IndexOf("Set-ColumnSecured $true", StringComparison.Ordinal);

        membership.Should().BePositive();
        refusal.Should().BePositive();
        secure.Should().BePositive();
        membership.Should().BeLessThan(refusal);
        refusal.Should().BeLessThan(secure, "a missing BFF member must stop the script with nothing changed");
    }

    private const string Seed = """
        $Table = 'sprk_ruleset'
        $Column = 'sprk_visibletofirms'
        $WriterProfileName = 'Spaarke BFF-Managed Field Writers'
        $ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
        $ReaderProfileFixedId = '42e542c8-47bf-f111-aaaf-0022482913fc'
        $SystemAdministratorProfileId = '572329c1-a042-4e22-be47-367c6374ea45'
        if ($reader) { $specs += @{ Id = $reader.fieldsecurityprofileid; Name = $ReaderProfileName; Create = 0 } }
        if ($writer) { $specs += @{ Id = $writer.fieldsecurityprofileid; Name = $WriterProfileName; Create = 4 } }
                canread = 4; cancreate = $Create; canupdate = $Create
        """;

    [Theory(DisplayName = "ISS-040 must-fire: the script parser sees each drift (another table or column, a swapped profile or id, a changed grant, another admin profile)")]
    [InlineData("$Table = 'sprk_ruleset'", "$Table = 'sprk_rulesets'")]
    [InlineData("$Column = 'sprk_visibletofirms'", "$Column = 'sprk_visibletofirm'")]
    [InlineData("$WriterProfileName = 'Spaarke BFF-Managed Field Writers'", "$WriterProfileName = 'Spaarke BFF-Managed Field Readers'")]
    [InlineData("$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'", "$ReaderProfileName = 'Spaarke BFF-Managed Field Writers'")]
    [InlineData("42e542c8-47bf-f111-aaaf-0022482913fc", "11111111-1111-1111-1111-111111111111")]
    [InlineData("Name = $ReaderProfileName; Create = 0", "Name = $ReaderProfileName; Create = 4")]
    [InlineData("Name = $WriterProfileName; Create = 4", "Name = $WriterProfileName; Create = 0")]
    [InlineData("canread = 4; cancreate = $Create; canupdate = $Create", "canread = 4; cancreate = 4; canupdate = 4")]
    [InlineData("'572329c1-a042-4e22-be47-367c6374ea45'", "'11111111-1111-1111-1111-111111111111'")]
    public void TheParser_SeesEachDrift(string from, string to)
    {
        Agrees(Analyse(Seed)).Should().BeTrue("the faithful seed agrees");
        Agrees(Analyse(Seed.ReplaceLineEndings("\n").Replace(from, to))).Should().BeFalse("each seeded drift must fail the agreement test");
    }

    // -Verify's assertions and the membership checks cannot run without a host, so each is pinned as a source fact: the shipped script has it,
    // and a seeded drift of it does not.
    [Theory(DisplayName = "ISS-040: the script keeps its IsSecured, grant, other-profile and writers-membership checks")]
    [InlineData(@"Report 'MISSING' ""\$Table\.\$Column is NOT field-secured", "Report 'MISSING' \"$Table.$Column is NOT", "Report 'INFO' \"$Table.$Column is NOT")]
    [InlineData(@"elseif \(\$Verify\) \{ Report 'MISSING' ""\$\(\$s\.Name\) on \$Table\.\$Column"" \}", "elseif ($Verify) { Report 'MISSING' \"$($s.Name) on", "elseif ($Verify) { Report 'INFO' \"$($s.Name) on")]
    [InlineData(@"\(canupdate eq 4 or cancreate eq 4\)", "canupdate eq 4 or cancreate eq 4", "canupdate eq 4")]
    [InlineData(@"Report 'FAIL' ""profile '\$\(\$o\.fieldsecurityprofileid\.name\)'", "Report 'FAIL' \"profile '$($o.fieldsecurityprofileid.name)'", "Report 'INFO' \"profile '$($o.fieldsecurityprofileid.name)'")]
    [InlineData(@"Report 'FAIL' ""\(p1\) BFF app user '\$\(\$u\.fullname\)' is NOT a member of \$WriterProfileName", "Report 'FAIL' \"(p1) BFF app user '$($u.fullname)' is NOT a member", "Report 'INFO' \"(p1) BFF app user '$($u.fullname)' is NOT a member")]
    [InlineData(@"Report 'FAIL' ""\(p1\) no application user for appId", "Report 'FAIL' \"(p1) no application user for appId", "Report 'INFO' \"(p1) no application user for appId")]
    [InlineData(@"\$kind = if \(\$m\.applicationid\) \{ ""an application user \(appId \$\(\$m\.applicationid\)\) not in -BffApplicationIds"" \} else \{ 'a HUMAN user' \}", "else { 'a HUMAN user' }", "else { 'a user' }")]
    [InlineData(@"Report 'FAIL' ""\(p1\) \$WriterProfileName member '", "Report 'FAIL' \"(p1) $WriterProfileName member '", "Report 'INFO' \"(p1) $WriterProfileName member '")]
    [InlineData(@"Report 'FAIL' ""\(p1\) \$WriterProfileName has TEAM", "Report 'FAIL' \"(p1) $WriterProfileName has TEAM", "Report 'INFO' \"(p1) $WriterProfileName has TEAM")]
    [InlineData(@"\$perm\.canread -eq 4 -and \$perm\.cancreate -eq \$s\.Create -and \$perm\.canupdate -eq \$s\.Create", "$perm.canread -eq 4 -and $perm.cancreate -eq $s.Create", "$perm.canread -eq 4 -and $perm.cancreate -eq 4")]
    public void TheScript_KeepsItsVerifyChecks(string pattern, string from, string to)
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", ScriptFile)).ReplaceLineEndings("\n");

        Regex.IsMatch(script, pattern).Should().BeTrue("the shipped script has this check");
        script.Should().Contain(from);
        Regex.IsMatch(script.Replace(from, to), pattern).Should().BeFalse("a seeded drift of it must fail the pin");
    }

    private sealed record ScriptShape(
        string? Table, string? Column, string? Writer, string? Reader, string? ReaderId, string? SystemAdministrator, string? WriterCreate, string? ReaderCreate, string? GrantShape);

    private static bool Agrees(ScriptShape s) =>
        s.Table == Table && s.Column == Column && s.Writer == WriterProfile && s.Reader == ReaderProfile && s.ReaderId == ReaderProfileId
        && s.SystemAdministrator == SystemAdministratorProfileId && s.WriterCreate == "4" && s.ReaderCreate == "0" && s.GrantShape == "4,$Create,$Create";

    private static ScriptShape Analyse(string script)
    {
        static string? Single(string text, string pattern) =>
            Regex.Match(text, pattern, RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

        var grant = Regex.Match(script, @"canread\s*=\s*(\d);\s*cancreate\s*=\s*(\$?\w+);\s*canupdate\s*=\s*(\$?\w+)");
        return new ScriptShape(
            Single(script, @"^\s*\$Table\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$Column\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$WriterProfileName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$ReaderProfileName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$ReaderProfileFixedId\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$SystemAdministratorProfileId\s*=\s*'([^']+)'"),
            Single(script, @"Name\s*=\s*\$WriterProfileName;\s*Create\s*=\s*(\d)"),
            Single(script, @"Name\s*=\s*\$ReaderProfileName;\s*Create\s*=\s*(\d)"),
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
