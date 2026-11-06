using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// field-mapping-server-write-path-r1 task 044 (owner decision 3 of 2026-11-04, ontology D-204, UAC-r2 decisions P1 and P2 of
/// 2026-11-06): the core record pair of a Signal and a Decision Record (<c>sprk_corerecordtype</c> + <c>sprk_corerecordid</c>)
/// is written only by the server. Four cells are field-secured: every user reads them (task 133's reader profile, on every
/// business-unit default team, read only); the BFF application users write them (task 133's BFF writer profile); the ontology
/// writer writes and reads them (its own narrow profile, "Spaarke Ontology Writer Core Pair Writers", read/create/update);
/// no other profile grants them. Three artifacts must agree:
/// <list type="bullet">
/// <item><c>scripts/Set-CorePairFieldSecurity.ps1</c> (what an operator applies);</item>
/// <item>the SpaarkeMaster solution source (<c>sprk_Signal</c> / <c>sprk_DecisionRecord</c> <c>Entity.xml</c> IsSecured,
/// <c>Other/FieldSecurityProfiles.xml</c>, and the new profile's root component in <c>Other/Solution.xml</c>);</item>
/// <item>the BFF code: the cells are <c>RecordOwnershipContext.CorePairTables</c> x the two core columns, both tables are
/// created only by their writers with the pair fixed after create, and NO caller-check exemption covers the pair (task 044
/// leaves <c>CallerWriteAuthorization</c> unchanged; its step 3 would refuse a caller-sent core pair).</item>
/// </list>
/// Read as text (no PowerShell host, no network). Every check runs on seeded drifts (must fire) and on the committed files
/// (must not fire).
/// </summary>
[Trait("Category", "Security")]
public class CorePairFieldSecurityAgreementTests
{
    private static readonly string[] Tables = ["sprk_signal", "sprk_decisionrecord"];
    private static readonly string[] Columns = ["sprk_corerecordtype", "sprk_corerecordid"];
    private const string ReaderProfile = "Spaarke BFF-Managed Field Readers";
    private const string BffWriterProfile = "Spaarke BFF-Managed Field Writers";
    private const string WriterProfile = "Spaarke Ontology Writer Core Pair Writers";
    private const string WriterProfileId = "169feee6-7a77-4d00-9e27-ffc27bee6262";
    private const string D136Profile = "Spaarke Ontology Writer Field Writers";

    private static readonly (string Table, string Column)[] Cells =
        [.. Tables.SelectMany(t => Columns.Select(c => (t, c)))];

    // ── The script ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheScript_SecuresTheFourCells_WithTheReader_TheBffWriters_AndTheCorePairWriters()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "Set-CorePairFieldSecurity.ps1"));

        ScriptGaps(script).Should().BeEmpty("the script must secure the four core pair cells with the three named profiles");
        Regex.IsMatch(script, @"canread\s*=\s*4;\s*cancreate\s*=\s*\$Create;\s*canupdate\s*=\s*\$Create").Should().BeTrue(
            "every profile is granted read=4; the reader create/update 0, both writers create/update 4");
        Regex.IsMatch(script, @"Id = \$writerId; Name = \$WriterProfileName; Create = 4").Should().BeTrue(
            "the core pair writer profile is granted with Create 4 (read=4 create=4 update=4)");
    }

    private const string ScriptSeed = """
        $Tables = @('sprk_signal', 'sprk_decisionrecord')
        $Columns = @('sprk_corerecordtype', 'sprk_corerecordid')
        $ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
        $BffWriterProfileName = 'Spaarke BFF-Managed Field Writers'
        $WriterProfileName = 'Spaarke Ontology Writer Core Pair Writers'
        $WriterProfileFixedId = '169feee6-7a77-4d00-9e27-ffc27bee6262'
        $Secured = $true
        """;

    [Theory]
    [InlineData("$Secured = $true", "$Secured = $false")]
    [InlineData("'sprk_signal', 'sprk_decisionrecord'", "'sprk_signal'")]
    [InlineData("'sprk_signal', 'sprk_decisionrecord'", "'sprk_signal', 'sprk_budgetrevision'")]
    [InlineData("'sprk_corerecordtype', 'sprk_corerecordid'", "'sprk_corerecordtype'")]
    [InlineData("'sprk_corerecordtype', 'sprk_corerecordid'", "'sprk_corerecordtype', 'sprk_regardingrecordid'")]
    [InlineData("Core Pair Writers'", "Field Writers'")]
    [InlineData("Managed Field Readers'", "Managed Field Writers'")]
    [InlineData("$BffWriterProfileName = 'Spaarke BFF-Managed Field Writers'", "$BffWriterProfileName = 'Spaarke Identity Link Writers'")]
    [InlineData("'169feee6-7a77-4d00-9e27-ffc27bee6262'", "'b710d90c-0f27-4d6a-a146-2769c8aa921f'")]
    public void TheScriptCheck_FiresOnEachSeededDrift(string from, string to)
    {
        ScriptGaps(ScriptSeed).Should().BeEmpty("the faithful seed agrees (must not fire)");
        ScriptGaps(ScriptSeed.ReplaceLineEndings("\n").Replace(from, to)).Should().NotBeEmpty("each seeded drift must fail (must fire)");
    }

    // ── The solution source ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheSolution_SecuresTheFourCells_TheReaderReadsThem_AndOnlyTheTwoWriterProfilesWriteThem()
    {
        SolutionGaps(LoadSolution()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("secured:sprk_signal.sprk_corerecordtype")]
    [InlineData("secured:sprk_signal.sprk_corerecordid")]
    [InlineData("secured:sprk_decisionrecord.sprk_corerecordtype")]
    [InlineData("secured:sprk_decisionrecord.sprk_corerecordid")]
    [InlineData("reader-read:sprk_signal.sprk_corerecordid")]
    [InlineData("reader-missing:sprk_decisionrecord.sprk_corerecordtype")]
    [InlineData("reader-update:sprk_signal.sprk_corerecordtype")]
    [InlineData("bff-create:sprk_decisionrecord.sprk_corerecordid")]
    [InlineData("bff-update:sprk_signal.sprk_corerecordtype")]
    [InlineData("bff-missing:sprk_signal.sprk_corerecordid")]
    [InlineData("writer-create:sprk_signal.sprk_corerecordid")]
    [InlineData("writer-update:sprk_decisionrecord.sprk_corerecordtype")]
    [InlineData("writer-read:sprk_decisionrecord.sprk_corerecordid")]
    [InlineData("writer-missing:sprk_signal.sprk_corerecordtype")]
    [InlineData("writer-stray")]
    [InlineData("writer-id")]
    [InlineData("d136-grants:sprk_signal.sprk_corerecordtype")]
    [InlineData("other-reads:sprk_decisionrecord.sprk_corerecordid")]
    [InlineData("root-component")]
    public void TheSolutionCheck_FiresOnEachSeededDrift(string drift)
    {
        var solution = LoadSolution();
        Seed(solution, drift);

        SolutionGaps(solution).Should().NotBeEmpty($"the seeded drift '{drift}' must fail the agreement test (must fire)");
    }

    // ── The code ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheCode_NamesTheSameCells_RefusesTheTablesOnTheGeneralCreate_AndExemptsNoCorePairColumn()
    {
        CodeGaps(RecordOwnershipContext.CorePairTables, OwnedChildWrite.BffManagedPairColumnsOf, RecordTableRules.Find).Should().BeEmpty();
        RecordOwnershipContext.CoreRecordTypeColumn.Should().Be(Columns[0]);
        RecordOwnershipContext.CoreRecordIdColumn.Should().Be(Columns[1]);
        SignalCoreRecordAccess.CoreRecordTypeColumn.Should().Be(Columns[0]);
        SignalCoreRecordAccess.CoreRecordIdColumn.Should().Be(Columns[1]);
    }

    [Theory]
    [InlineData("exemption")]
    [InlineData("third-table")]
    [InlineData("one-table")]
    [InlineData("created-by-anyone")]
    [InlineData("pair-not-fixed")]
    public void TheCodeCheck_FiresOnEachSeededDrift(string drift)
    {
        IReadOnlySet<string> tables = RecordOwnershipContext.CorePairTables;
        Func<string, IReadOnlySet<string>> exemption = OwnedChildWrite.BffManagedPairColumnsOf;
        Func<string?, RecordTableRule?> find = RecordTableRules.Find;
        switch (drift)
        {
            case "exemption":
                exemption = t => t == "sprk_signal" ? new HashSet<string>(Columns) : OwnedChildWrite.BffManagedPairColumnsOf(t);
                break;
            case "third-table":
                tables = new HashSet<string>(Tables) { "sprk_budgetrevision" };
                break;
            case "one-table":
                tables = new HashSet<string> { "sprk_signal" };
                break;
            case "created-by-anyone":
                find = t => t == "sprk_decisionrecord" ? RecordTableRules.Find(t)! with { CreatedOnlyByItsWriter = false } : RecordTableRules.Find(t);
                break;
            case "pair-not-fixed":
                find = t => t == "sprk_signal" ? RecordTableRules.Find(t)! with { FixedAfterCreateColumns = new HashSet<string> { Columns[0] } } : RecordTableRules.Find(t);
                break;
        }

        CodeGaps(tables, exemption, find).Should().NotBeEmpty($"the seeded drift '{drift}' must fail (must fire)");
    }

    // ── The checks ──────────────────────────────────────────────────────────────────────────────────────────

    private static List<string> ScriptGaps(string script)
    {
        static string? One(string text, string pattern) =>
            Regex.Match(text, pattern, RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;
        static string[] List(string text, string name) =>
            One(text, @"^\s*\$" + name + @"\s*=\s*@\(([^)]*)\)") is { } inner
                ? [.. Regex.Matches(inner, "'([^']+)'").Select(m => m.Groups[1].Value)]
                : [];

        var gaps = new List<string>();
        if (!List(script, "Tables").SequenceEqual(Tables)) gaps.Add("$Tables");
        if (!List(script, "Columns").SequenceEqual(Columns)) gaps.Add("$Columns");
        if (One(script, @"^\s*\$ReaderProfileName\s*=\s*'([^']+)'") != ReaderProfile) gaps.Add("$ReaderProfileName");
        if (One(script, @"^\s*\$BffWriterProfileName\s*=\s*'([^']+)'") != BffWriterProfile) gaps.Add("$BffWriterProfileName");
        if (One(script, @"^\s*\$WriterProfileName\s*=\s*'([^']+)'") != WriterProfile) gaps.Add("$WriterProfileName");
        if (One(script, @"^\s*\$WriterProfileFixedId\s*=\s*'([^']+)'") != WriterProfileId) gaps.Add("$WriterProfileFixedId");
        if (!string.Equals(One(script, @"^\s*\$Secured\s*=\s*\$(\w+)"), "true", StringComparison.OrdinalIgnoreCase)) gaps.Add("$Secured");
        return gaps;
    }

    private sealed record SolutionFiles(Dictionary<string, XDocument> Entities, XDocument Profiles)
    {
        public string SolutionXml { get; set; } = "";
    }

    private static SolutionFiles LoadSolution()
    {
        var root = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster");
        return new SolutionFiles(
            new Dictionary<string, XDocument>
            {
                ["sprk_signal"] = XDocument.Load(Path.Combine(root, "Entities", "sprk_Signal", "Entity.xml")),
                ["sprk_decisionrecord"] = XDocument.Load(Path.Combine(root, "Entities", "sprk_DecisionRecord", "Entity.xml")),
            },
            XDocument.Load(Path.Combine(root, "Other", "FieldSecurityProfiles.xml")))
        {
            SolutionXml = File.ReadAllText(Path.Combine(root, "Other", "Solution.xml")),
        };
    }

    private static XElement? Attribute(SolutionFiles s, string table, string column) =>
        s.Entities[table].Descendants("attribute").SingleOrDefault(a => (string?)a.Element("LogicalName") == column);

    private static XElement? Profile(SolutionFiles s, string name) =>
        s.Profiles.Descendants("FieldSecurityProfile").SingleOrDefault(p => (string?)p.Attribute("name") == name);

    private static XElement[] OnCell(XElement? profile, string table, string column) =>
        profile is null
            ? []
            : [.. profile.Descendants("FieldPermission")
                .Where(p => (string?)p.Element("EntityName") == table && (string?)p.Element("AttributeName") == column)];

    private static string Rights(XElement p) =>
        $"{(string?)p.Element("CanRead")}/{(string?)p.Element("CanCreate")}/{(string?)p.Element("CanUpdate")}";

    private static List<string> SolutionGaps(SolutionFiles s)
    {
        var gaps = new List<string>();
        var reader = Profile(s, ReaderProfile);
        var bffWriter = Profile(s, BffWriterProfile);
        var writer = Profile(s, WriterProfile);
        if (reader is null) gaps.Add($"no profile '{ReaderProfile}'");
        if (bffWriter is null) gaps.Add($"no profile '{BffWriterProfile}'");
        if (writer is null) gaps.Add($"no profile '{WriterProfile}'");
        else if ((string?)writer.Attribute("fieldsecurityprofileid") != "{" + WriterProfileId + "}")
            gaps.Add($"'{WriterProfile}' does not carry the id the script creates ({WriterProfileId})");

        foreach (var (table, column) in Cells)
        {
            var cell = $"{table}.{column}";
            if ((string?)Attribute(s, table, column)?.Element("IsSecured") != "1") gaps.Add($"{cell} is not IsSecured 1");

            foreach (var (profile, name, rights) in new[] { (reader, ReaderProfile, "4/0/0"), (bffWriter, BffWriterProfile, "4/4/4"), (writer, WriterProfile, "4/4/4") })
            {
                var rows = OnCell(profile, table, column);
                if (rows.Length != 1) gaps.Add($"'{name}' has {rows.Length} permission(s) on {cell}, expected one");
                else if (Rights(rows[0]) != rights) gaps.Add($"'{name}' on {cell} is read/create/update {Rights(rows[0])}, expected {rights}");
            }

            // No other profile grants the cell at all (the ontology D-136 profile included).
            foreach (var other in s.Profiles.Descendants("FieldSecurityProfile")
                         .Where(p => (string?)p.Attribute("name") is not (ReaderProfile or BffWriterProfile or WriterProfile)))
            {
                if (OnCell(other, table, column).Any(p => Rights(p) != "0/0/0"))
                    gaps.Add($"'{(string?)other.Attribute("name")}' also grants {cell}");
            }
        }

        if (writer is not null)
        {
            var stray = writer.Descendants("FieldPermission")
                .Where(p => !Cells.Contains(((string?)p.Element("EntityName") ?? "", (string?)p.Element("AttributeName") ?? "")))
                .ToArray();
            if (stray.Length > 0) gaps.Add($"'{WriterProfile}' holds {stray.Length} permission(s) outside the four cells");
        }

        if (!s.SolutionXml.Contains($"<RootComponent type=\"70\" id=\"{{{WriterProfileId}}}\" behavior=\"0\" />", StringComparison.Ordinal))
            gaps.Add($"'{WriterProfile}' is not a root component of SpaarkeMaster");

        return gaps;
    }

    private static List<string> CodeGaps(
        IReadOnlySet<string> corePairTables, Func<string, IReadOnlySet<string>> exemption, Func<string?, RecordTableRule?> find)
    {
        var gaps = new List<string>();
        if (!corePairTables.SetEquals(Tables)) gaps.Add($"CorePairTables is [{string.Join(", ", corePairTables)}], the secured cells name [{string.Join(", ", Tables)}]");

        foreach (var table in corePairTables.Union(Tables))
        {
            // No caller-check exemption: a caller-sent core pair is refused by CallerWriteAuthorization step 3 (task 044 left
            // it unchanged), and the general create refuses the table before step 3 is asked.
            if (exemption(table).Count > 0) gaps.Add($"{table}: BffManagedPairColumnsOf exempts [{string.Join(", ", exemption(table))}]");

            var rule = find(table);
            if (rule is null) { gaps.Add($"{table}: no RecordTableRule"); continue; }
            if (!rule.CreatedOnlyByItsWriter) gaps.Add($"{table}: not CreatedOnlyByItsWriter (the general create would take it)");
            if (!rule.FixedAfterCreateColumns.SetEquals(Columns)) gaps.Add($"{table}: FixedAfterCreateColumns is not the core pair");
        }

        return gaps;
    }

    // ── Seeded drifts of the solution source ────────────────────────────────────────────────────────────────

    private static void Seed(SolutionFiles s, string drift)
    {
        var parts = drift.Split(':');
        var (table, column) = parts.Length > 1 ? (parts[1].Split('.')[0], parts[1].Split('.')[1]) : ("sprk_signal", "sprk_corerecordtype");
        XElement Row(string profile) => OnCell(Profile(s, profile), table, column).Single();

        switch (parts[0])
        {
            case "secured": Attribute(s, table, column)!.Element("IsSecured")!.Value = "0"; break;
            case "reader-read": Row(ReaderProfile).Element("CanRead")!.Value = "0"; break;
            case "reader-missing": Row(ReaderProfile).Remove(); break;
            case "reader-update": Row(ReaderProfile).Element("CanUpdate")!.Value = "4"; break;
            case "bff-create": Row(BffWriterProfile).Element("CanCreate")!.Value = "0"; break;
            case "bff-update": Row(BffWriterProfile).Element("CanUpdate")!.Value = "0"; break;
            case "bff-missing": Row(BffWriterProfile).Remove(); break;
            case "writer-create": Row(WriterProfile).Element("CanCreate")!.Value = "0"; break;
            case "writer-update": Row(WriterProfile).Element("CanUpdate")!.Value = "0"; break;
            case "writer-read": Row(WriterProfile).Element("CanRead")!.Value = "0"; break;
            case "writer-missing": Row(WriterProfile).Remove(); break;
            case "writer-stray":
                Profile(s, WriterProfile)!.Descendants("FieldPermissions").Single().Add(Permission("sprk_signal", "sprk_firmreason", "4"));
                break;
            case "writer-id": Profile(s, WriterProfile)!.Attribute("fieldsecurityprofileid")!.Value = "{b710d90c-0f27-4d6a-a146-2769c8aa921f}"; break;
            case "d136-grants": Profile(s, D136Profile)!.Descendants("FieldPermissions").Single().Add(Permission(table, column, "4")); break;
            case "other-reads":
                Profile(s, "Spaarke Identity Link Readers")!.Descendants("FieldPermissions").Single().Add(Permission(table, column, "0", read: "4"));
                break;
            case "root-component":
                s.SolutionXml = s.SolutionXml.Replace($"<RootComponent type=\"70\" id=\"{{{WriterProfileId}}}\" behavior=\"0\" />", "", StringComparison.Ordinal);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(drift), drift, "unknown seeded drift");
        }
    }

    private static XElement Permission(string table, string column, string write, string read = "4") =>
        new("FieldPermission",
            new XElement("EntityName", table),
            new XElement("AttributeName", column),
            new XElement("CanRead", read),
            new XElement("CanUpdate", write),
            new XElement("CanCreate", write),
            new XElement("CanReadUnmasked", "0"));

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
