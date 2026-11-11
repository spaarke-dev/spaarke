using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 082b (ontology-r1 D-203 (8c); notes/082-procedure-resolver.md "082b re-check and fix round 2"): no writer in
/// <c>src/server</c> sets <c>overriddencreatedon</c>, in particular on <c>sprk_policyversion</c> or <c>sprk_procedureassignment</c>.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> <c>ProcedureResolver</c> decides which versions of a record rule belong to a change's current episode by comparing
/// the <c>createdon</c> of the policy version with the <c>createdon</c> of the change's Exclude row (a version counts when it is created on
/// or after the row). <c>overriddencreatedon</c> is the Dataverse column that lets a writer stamp <c>createdon</c> with a chosen value
/// (data migration); a writer that set it on either table could make an older change's version look newer than the row and revive a
/// removed change. No BFF path sets it, and none may.</para>
/// <para><b>Enforced by:</b> this source scan of every <c>.cs</c> file under <c>src/server</c> (CI and the Stop hook). It is wider than the
/// two tables: ANY mention of the column name (any casing, so an early-bound <c>OverriddenCreatedOn</c> too), comments ignored, fails,
/// except the one known mention in the baseline below, which is the create-path REFUSAL list
/// (<c>RecordTableRules.CommonServerOwnedColumns</c>: a caller payload that names the column is refused, never honoured). That is a
/// ratchet: a new file, or a second mention in the baseline file, fails; a baseline entry that no longer has its mention fails too, so
/// the list shrinks and never goes stale. Controls: planted writes in the two real writer files fire (a quoted name, another casing, the
/// assignment table); a comment, the real writer files and the refusal list do not.</para>
/// <para><b>Not covered:</b> a name built at run time (<c>"overridden" + "createdon"</c>); a <c>//</c> inside a string literal before the
/// name on the same line, which hides the rest of that line from <see cref="SourceScan.CodeText"/>; a write from outside <c>src/server</c>
/// (client TypeScript through <c>Xrm.WebApi</c>, scripts: <c>scripts/Migrate-DataverseData.ps1</c> only strips the column).</para>
/// </remarks>
public sealed class OverriddenCreatedOnWriteGuardTests
{
    private static readonly Regex ColumnName = new("overriddencreatedon", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private const string RefusalListFile = "src/server/api/Sprk.Bff.Api/Services/RecordCreation/RecordTableRules.cs";

    /// <summary>Known mentions, by repo-relative path (forward slashes): file → number of mentions. Each is a refusal, not a write.</summary>
    private static readonly Dictionary<string, int> Baseline = new(StringComparer.Ordinal)
    {
        [RefusalListFile] = 1,
    };

    private static readonly string[] WriterFiles =
    [
        Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", "Services", "Finance", "RuleSetService.cs"),
        Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", "Services", "Finance", "Procedures", "ProcedureAssignmentService.cs"),
    ];

    /// <summary>Each mention of the column name in the code (line comments removed), as "line N".</summary>
    internal static IReadOnlyList<string> Mentions(IEnumerable<string> lines)
    {
        var code = SourceScan.CodeText(lines);
        return ColumnName.Matches(code).Select(m => $"line {SourceScan.LineOf(code, m.Index)}").ToList();
    }

    private static string Key(string file) => SourceScan.Relative(file).Replace('\\', '/');

    [Fact(DisplayName = "Task 082b (D-203 (8c)): no writer in src/server sets overriddencreatedon; the one mention is the create-path refusal list")]
    public void NoWriter_SetsOverriddenCreatedOn()
    {
        var files = SourceScan.ServerSourceFiles().ToList();
        Assert.True(files.Count > 100, $"Only {files.Count} server source files were found; the guard would be vacuous.");

        var found = files
            .Select(f => (Key: Key(f), Mentions: Mentions(File.ReadAllLines(f))))
            .Where(x => x.Mentions.Count > 0)
            .ToDictionary(x => x.Key, x => x.Mentions, StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (var (file, mentions) in found)
        {
            var allowed = Baseline.GetValueOrDefault(file);
            if (mentions.Count > allowed)
            {
                problems.Add($"{file}: {mentions.Count} mention(s) of overriddencreatedon ({string.Join(", ", mentions)}), {allowed} allowed; "
                    + "no writer may stamp createdon (the record-rule episode boundary reads it)");
            }
        }

        foreach (var (file, allowed) in Baseline)
        {
            if (found.GetValueOrDefault(file)?.Count is not { } count || count < allowed)
            {
                problems.Add($"{file}: the baseline expects {allowed} mention(s) and finds {found.GetValueOrDefault(file)?.Count ?? 0}; remove or lower the baseline entry");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact(DisplayName = "Task 082b (D-203 (8c)): the baseline mention really is the refusal list, not a write")]
    public void TheBaselineMention_IsTheRefusalList()
    {
        var file = Path.Combine(SourceScan.RepoRoot, RefusalListFile.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(file), $"{file} was not found; the guard would be vacuous.");

        var statement = SourceScan.Statements(File.ReadAllLines(file)).Single(s => ColumnName.IsMatch(s.Statement));

        Assert.Contains("CommonServerOwnedColumns", statement.Statement, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "Task 082b (D-203 (8c)): the guard fires on a planted write of overriddencreatedon in a policy-version or assignment writer (must-fire control)")]
    [InlineData(0, "version[\"overriddencreatedon\"] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);")]
    [InlineData(0, "[\"OverriddenCreatedOn\"] = importedAt,")]
    [InlineData(1, "new Entity(\"sprk_procedureassignment\") { [\"overriddencreatedon\"] = DateTime.UtcNow };")]
    [InlineData(1, "row.OverriddenCreatedOn = DateTime.UtcNow;")]
    public void TheGuard_FiresOnAPlantedWrite(int writer, string planted)
    {
        Assert.True(File.Exists(WriterFiles[writer]), $"{WriterFiles[writer]} was not found; the control would be vacuous.");
        var lines = File.ReadAllLines(WriterFiles[writer]).Append(planted);

        Assert.NotEmpty(Mentions(lines));
    }

    [Fact(DisplayName = "Task 082b (D-203 (8c)): the guard stays quiet on a comment naming the column and on the real writers (must-not-fire control)")]
    public void TheGuard_StaysQuiet_OnAComment_AndOnTheRealWriters()
    {
        Assert.Empty(Mentions(["// never set overriddencreatedon on sprk_policyversion: the episode boundary reads createdon", "/// <c>OverriddenCreatedOn</c> is refused"]));
        foreach (var writer in WriterFiles)
        {
            Assert.True(File.Exists(writer), $"{writer} was not found; the control would be vacuous.");
            Assert.Empty(Mentions(File.ReadAllLines(writer)));
        }
    }
}
