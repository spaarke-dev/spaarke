using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.FinanceSubmission;

/// <summary>
/// ebilling-r1 task 095c-5 (uac-r2 2026-11-04): <c>sprk_invoice.sprk_firmhold</c>, the invoice's own "firm needed" hold, is field-secured so that only
/// the server sets it: the BFF application user ("Spaarke BFF-Managed Field Writers": read, create, update) writes it, and every other user reads it
/// ("Spaarke BFF-Managed Field Readers": read only). Three artifacts must agree: <c>scripts/ebilling/Set-EbillingInvoiceFirmHoldColumn.ps1</c> (what
/// an operator applies), the SpaarkeMaster solution source (<c>Entity.xml</c> IsSecured and <c>Other/FieldSecurityProfiles.xml</c>) and the BFF's
/// column name and hold value (<see cref="InvoiceFirmHold"/>). Read as text (no PowerShell host, no network); the script parser is exercised on
/// seeded drifts. The BFF side (every read is app-only) is pinned by <c>InvoiceFirmHoldAppOnlyGuardTests</c> in Spaarke.ArchTests.
/// </summary>
[Trait("Category", "Security")]
public class InvoiceFirmHoldFieldSecurityAgreementTests
{
    private const string Table = "sprk_invoice";
    private const string Column = InvoiceFirmHold.InvoiceHoldColumn;
    private const string ReaderProfile = "Spaarke BFF-Managed Field Readers";
    private const string WriterProfile = "Spaarke BFF-Managed Field Writers";
    private const string SystemAdministratorProfileId = "572329c1-a042-4e22-be47-367c6374ea45";

    [Fact(DisplayName = "095c-5: the script creates and secures sprk_invoice.sprk_firmhold for the writer (read/create/update) and the reader (read), and backfills the BFF's hold value")]
    public void TheScript_SecuresTheColumn_TheWriterWritesIt_AndTheReaderOnlyReadsIt()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", "Set-EbillingInvoiceFirmHoldColumn.ps1"));

        Agrees(Analyse(script)).Should().BeTrue("the script must secure sprk_invoice.sprk_firmhold for the reused writer and reader profiles");
    }

    // 095c-5 pass-2 review: three behaviours of the script that no host runs in a unit test, pinned as source facts. Each pattern is checked
    // against the shipped script and against a seeded drift of it.
    [Theory(DisplayName = "095c-5 pass-2: the script keeps its p0 length check, the InvoiceIdFor derivation, and -Verify's empty-firm INFO")]
    [InlineData(@"\$MaxLength\s+-lt\s+\$HoldValue\.Length", "-lt $HoldValue.Length", "-gt $HoldValue.Length")]
    [InlineData(@"ebilling-invoice-\$\(\$SubmissionId\.ToString\('D'\)\)", "ebilling-invoice-$($SubmissionId", "ebilling-line-$($SubmissionId")]
    [InlineData(@"\[Guid\]::new\(\[byte\[\]\]\$bytes\[0\.\.15\]\)", "$bytes[0..15]", "$bytes[0..14]")]
    [InlineData(@"if \(\$emptyFirmToSet\.Count -gt 0\) \{ Report 'INFO' ", "Report 'INFO' \"$($emptyFirmToSet.Count) invoice(s) have no firm", "Report 'MISSING' \"$($emptyFirmToSet.Count) invoice(s) have no firm")]
    public void TheScript_KeepsItsPass2Behaviours(string pattern, string from, string to)
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", "Set-EbillingInvoiceFirmHoldColumn.ps1")).ReplaceLineEndings("\n");

        Regex.IsMatch(script, pattern).Should().BeTrue("the shipped script has this behaviour");
        script.Should().Contain(from);
        Regex.IsMatch(script.Replace(from, to), pattern).Should().BeFalse("a seeded drift of it must fail the pin");
    }

    // ── 095d-1c: check (f), "Set firm applied, no Decision Record" ──

    [Fact(DisplayName = "095d-1c: check (f) names the BFF's stored member, the assign-firm code, the Decision Record set and its invoice lookup")]
    public void CheckF_NamesWhatTheBffWrites()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", "Set-EbillingInvoiceFirmHoldColumn.ps1"));
        static string? Value(string text, string name) =>
            Regex.Match(text, $@"^\s*\${name}\s*=\s*'([^']+)'", RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

        Value(script, "AssignFirmMember").Should().Be(InvoiceAssignFirmService.StoredDecisionMember);
        Value(script, "AssignFirmActionCode").Should().Be(InvoiceAssignFirmService.AssignFirmActionCode);
        Value(script, "DecisionRecordSet").Should().Be("sprk_decisionrecords");
        Value(script, "DecisionRecordInvoiceLookup").Should().Be($"_{TodoRegardingBuilder.RegardingLookupsByHostTable["sprk_decisionrecord"]["sprk_invoice"]}_value",
            "the writer files a billing record under the invoice's regarding lookup");
        DecisionRecordWriter.BillingSubjectEntityOf(InvoiceAssignFirmService.AssignFirmActionCode).Should().Be(Table, "an assign-firm record is filed under the invoice");
    }

    /// <summary>
    /// The script's Get-BillingDecisionKey and Get-InvoiceIdFor were run in PowerShell 7.6 on 2026-11-04 for submission
    /// <c>095d1b00-0000-4000-8000-000000000001</c> and printed these three ids (notes 16.9). The C# derivations must give the same, so the
    /// script finds exactly the record the writer's "already recorded" lookup finds.
    /// </summary>
    [Fact(DisplayName = "095d-1c: check (f)'s key derivation equals BillingDecisionKey.IdOf (a vector computed by the script's own functions)")]
    public void CheckF_KeyDerivation_EqualsTheWriters()
    {
        var submission = Guid.Parse("095d1b00-0000-4000-8000-000000000001");
        var invoice = InvoiceSubmissionPersister.InvoiceIdFor(submission);
        var decision = InvoiceAssignFirmService.DecisionIdFor(submission);

        invoice.Should().Be(Guid.Parse("029c724a-2f72-4a72-ea7b-4afcf625895d"));
        decision.Should().Be(Guid.Parse("b2a5b9b4-fd9d-68e8-abd8-711ccfc0be29"));
        BillingDecisionKey.IdOf(Table, invoice, decision).Should().Be(Guid.Parse("cdd9ab2a-00b1-bbc7-bfbb-9a52d592570b"));
    }

    // Behaviours of check (f) that no host runs in a unit test, pinned as source facts (each against the shipped script and a seeded drift).
    [Theory(DisplayName = "095d-1c: check (f) keeps its key text, its sprk_steps match, its window FAIL in -Verify and its read-only query")]
    [InlineData(@"""billing-dr-\$Table-\$\(\$InvoiceId\.ToString\('D'\)\)-\$\(\$DecisionId\.ToString\('D'\)\)""", "billing-dr-$Table-", "billing-dr-$Column-")]
    [InlineData(@"-like ""\*`""reviewId`"":`""\$key`""\*""", "reviewId`\":`\"$key", "decisionId`\":`\"$key")]
    [InlineData(@"if \(\$ageMinutes -gt \$AppliedNoRecordWindowMinutes\) \{", "-gt $AppliedNoRecordWindowMinutes", "-lt $AppliedNoRecordWindowMinutes")]
    [InlineData(@"if \(\$Verify\) \{ Report 'FAIL' ""\$what \(older than", "if ($Verify) { Report 'FAIL' \"$what (older", "if ($Verify) { Report 'INFO' \"$what (older")]
    [InlineData(@"Invoke-DvGet ""\$DecisionRecordSet`\?", "Invoke-DvGet \"$DecisionRecordSet`?", "Invoke-DvWrite PATCH \"$DecisionRecordSet`?")]
    [InlineData(@"\$invoiceId = Get-InvoiceIdFor \$submissionId", "$invoiceId = Get-InvoiceIdFor $submissionId", "$invoiceId = $s._sprk_invoice_value")]
    public void CheckF_KeepsItsBehaviours(string pattern, string from, string to)
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", "Set-EbillingInvoiceFirmHoldColumn.ps1")).ReplaceLineEndings("\n");

        Regex.IsMatch(script, pattern).Should().BeTrue("the shipped script has this behaviour");
        script.Should().Contain(from);
        Regex.IsMatch(script.Replace(from, to), pattern).Should().BeFalse("a seeded drift of it must fail the pin");
    }

    // ── 095d-2b-1: check (g), "Set firm recorded, re-run not done" ──

    [Fact(DisplayName = "095d-2b-1: check (g) names the stored member's rulesRerun and the BFF's completed value")]
    public void CheckG_NamesWhatTheRerunWrites()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", "Set-EbillingInvoiceFirmHoldColumn.ps1"));
        static string? Value(string text, string name) =>
            Regex.Match(text, $@"^\s*\${name}\s*=\s*'([^']+)'", RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

        Value(script, "RulesRerunCompleted").Should().Be(InvoiceAssignFirmService.RulesRerunCompleted);
        var member = Value(script, "RulesRerunMember");
        member.Should().NotBeNull();
        new AssignFirmStoredDecision(
                Guid.NewGuid(), Guid.NewGuid(), null, "A.2", 2, "A.1", 1, Guid.NewGuid(), DateTimeOffset.UnixEpoch,
                new InvoiceSubmissionAfterSteps.ResolvedSignalSet(DateTime.UnixEpoch, []), [], 0, InvoiceAssignFirmService.RulesRerunPending)
            .ToJson().ContainsKey(member!).Should().BeTrue("the script reads the member the BFF stores");
    }

    // Behaviours of check (g) that no host runs in a unit test, pinned as source facts (each against the shipped script and a seeded drift).
    [Theory(DisplayName = "095d-2b-1: check (g) keeps its done test, its window FAIL in -Verify and its recorded-only scope")]
    [InlineData(@"if \(\$row\.Stored\.RulesRerun -eq \$RulesRerunCompleted\) \{ \$rerunDone\+\+; continue \}", "-eq $RulesRerunCompleted", "-ne $RulesRerunCompleted")]
    [InlineData(@"if \(\$ageMinutes -gt \$RecordedNoRerunWindowMinutes\) \{", "-gt $RecordedNoRerunWindowMinutes", "-lt $RecordedNoRerunWindowMinutes")]
    [InlineData(@"if \(\$Verify\) \{ Report 'FAIL' ""\$what \(older than the \$RecordedNoRerunWindowMinutes", "if ($Verify) { Report 'FAIL' \"$what (older than the $RecordedNoRerunWindowMinutes", "if ($Verify) { Report 'INFO' \"$what (older than the $RecordedNoRerunWindowMinutes")]
    [InlineData(@"\$recorded\+\+\n\s+# \(g\)[^\n]*\n\s+\$rerunRows\.Add\(", "$rerunRows.Add(", "$rerunRowsX.Add(")]
    public void CheckG_KeepsItsBehaviours(string pattern, string from, string to)
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", "Set-EbillingInvoiceFirmHoldColumn.ps1")).ReplaceLineEndings("\n");

        Regex.IsMatch(script, pattern).Should().BeTrue("the shipped script has this behaviour");
        script.Should().Contain(from);
        Regex.IsMatch(script.Replace(from, to), pattern).Should().BeFalse("a seeded drift of it must fail the pin");
    }

    [Fact(DisplayName = "095d-2b-1: check (g) writes nothing (no write between its heading and its OK line)")]
    public void CheckG_IsReadOnly()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "ebilling", "Set-EbillingInvoiceFirmHoldColumn.ps1")).ReplaceLineEndings("\n");
        var start = script.IndexOf("# ── (g) RECORDED, RE-RUN NOT DONE", StringComparison.Ordinal);
        var end = script.IndexOf("Report 'OK' \"(g) no recorded Set firm", start, StringComparison.Ordinal);

        start.Should().BePositive();
        end.Should().BeGreaterThan(start);
        script[start..end].Should().NotContain("Invoke-DvWrite").And.NotContain("Invoke-RestMethod");
    }

    [Fact(DisplayName = "095c-5: the solution source has sprk_invoice.sprk_firmhold as a secured text column, only the writer may write it, the reader reads it")]
    public void TheSolution_SecuresTheColumn_OnlyTheWriterWritesIt_AndTheReaderReadsIt()
    {
        var solution = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster");

        var attribute = XDocument.Load(EntityXml(solution))
            .Descendants("attribute")
            .Single(a => (string?)a.Element("LogicalName") == Column);
        ((string?)attribute.Element("IsSecured")).Should().Be("1", $"{Table}.{Column} is field-secured in the solution");
        ((string?)attribute.Element("Type")).Should().Be("nvarchar");
        ((string?)attribute.Element("RequiredLevel")).Should().Be("none", "only a held invoice carries it");

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
        ((string?)reader.Element("CanRead")).Should().Be("4");
        ((string?)reader.Element("CanCreate")).Should().Be("0");
        ((string?)reader.Element("CanUpdate")).Should().Be("0");

        profiles.Where(p => (string?)p.Attribute("name") != WriterProfile)
            .SelectMany(OnTheColumn)
            .Should().OnlyContain(p => (string?)p.Element("CanCreate") == "0" && (string?)p.Element("CanUpdate") == "0",
                "no other profile in the solution may create or update the column");
    }

    private const string Seed = """
        $Table = 'sprk_invoice'
        $Column = 'sprk_firmhold'
        $HoldValue = 'ebilling.submission.firm_missing'
        $WriterProfileName = 'Spaarke BFF-Managed Field Writers'
        $ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
        $SystemAdministratorProfileId = '572329c1-a042-4e22-be47-367c6374ea45'
        if ($writer) { $specs += @{ Id = $writer.fieldsecurityprofileid; Name = $WriterProfileName; Create = 4 } }
        if ($reader) { $specs += @{ Id = $reader.fieldsecurityprofileid; Name = $ReaderProfileName; Create = 0 } }
        """;

    [Theory(DisplayName = "095c-5: the script parser sees each drift")]
    [InlineData("$Table = 'sprk_invoice'", "$Table = 'sprk_invoicesubmission'")]
    [InlineData("'sprk_firmhold'", "'sprk_firmholds'")]
    [InlineData("'ebilling.submission.firm_missing'", "'firm_missing'")]
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
        string? Table, string? Column, string? HoldValue, string? Writer, string? Reader, string? SystemAdministrator, string? WriterCreate, string? ReaderCreate);

    private static bool Agrees(ScriptShape s) =>
        s.Table == Table && s.Column == Column && s.HoldValue == InvoiceFirmHold.InvoiceHoldValue && s.Writer == WriterProfile && s.Reader == ReaderProfile
        && s.SystemAdministrator == SystemAdministratorProfileId && s.WriterCreate == "4" && s.ReaderCreate == "0";

    private static ScriptShape Analyse(string script)
    {
        static string? Single(string text, string pattern) =>
            Regex.Match(text, pattern, RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

        return new ScriptShape(
            Single(script, @"^\s*\$Table\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$Column\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$HoldValue\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$WriterProfileName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$ReaderProfileName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$SystemAdministratorProfileId\s*=\s*'([^']+)'"),
            Single(script, @"Name\s*=\s*\$WriterProfileName;\s*Create\s*=\s*(\d)"),
            Single(script, @"Name\s*=\s*\$ReaderProfileName;\s*Create\s*=\s*(\d)"));
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
