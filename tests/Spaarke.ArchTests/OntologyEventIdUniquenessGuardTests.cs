using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Every <c>EventId</c> number declared under <c>src/server/api/Sprk.Bff.Api/Services/Signals/**</c> is unique.
/// </summary>
/// <remarks>
/// <para><b>Why a fitness function.</b> The ontology lanes each carve their own EventId block (503xx), and two lanes once
/// both took 50316; every suite passed, because an EventId is only a number and nothing compiles or asserts it is unique.
/// The cost is silent: two different log events share an id, so an alert or query keyed on the id mixes them.</para>
///
/// <para><b>What it matches.</b> The declaration shapes used there: <c>new EventId(50316, ...)</c>, the target-typed
/// <c>EventId X = new(50316, ...)</c>, and <c>[LoggerMessage(EventId = 50316, ...)]</c>. Line comments are stripped first
/// (<see cref="SourceScan.CodeText"/>), so an id quoted in a comment does not count.</para>
///
/// <para><b>Ratchet.</b> There are no duplicates on the branch today, so there is no baseline list. If a duplicate is ever
/// baselined, list it here and fail on any not listed; do not renumber a shipped id (it is already in logs and alerts).</para>
///
/// <para><b>Enforced by:</b> this class. MAINTAIN-class structural fitness function (<c>tests/CLAUDE.md</c>).</para>
/// </remarks>
public sealed class OntologyEventIdUniquenessGuardTests
{
    private static readonly string SignalsDir = Path.Combine(
        SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api", "Services", "Signals");

    private static readonly Regex Declaration = new(
        @"(?:new\s+EventId\s*\(|EventId\s+\w+\s*=\s*new\s*\(|\bEventId\s*=\s*)\s*(?<id>\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // ── Pure logic ────────────────────────────────────────────────────────────────────────────

    /// <summary>The EventId numbers declared in one file's text, with the 1-based line of each.</summary>
    internal static IEnumerable<(int Id, int Line)> Declared(string source)
    {
        var code = SourceScan.CodeText(source.Split('\n'));
        foreach (Match m in Declaration.Matches(code))
        {
            yield return (int.Parse(m.Groups["id"].Value, System.Globalization.CultureInfo.InvariantCulture),
                SourceScan.LineOf(code, m.Index));
        }
    }

    /// <summary>Ids declared more than once across the files, each with every <c>file:line</c> that declares it.</summary>
    internal static IReadOnlyList<(int Id, IReadOnlyList<string> Sites)> Duplicates(
        IEnumerable<(string File, string Source)> files)
    {
        return files
            .SelectMany(f => Declared(f.Source).Select(d => (d.Id, Site: $"{f.File}:{d.Line}")))
            .GroupBy(d => d.Id)
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, (IReadOnlyList<string>)g.Select(d => d.Site).ToList()))
            .ToList();
    }

    // ── Real tree ─────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Every EventId number declared under Services/Signals is unique")]
    public void RealTree_EventIdsAreUnique()
    {
        var files = SignalFiles().ToList();
        var duplicates = Duplicates(files);

        Assert.True(duplicates.Count == 0,
            "Two log events share an EventId, so alerts and queries keyed on it mix them. Move the newer declaration to a free id "
            + "(check the ranges in OntologyWriterEvents.cs and the *Events classes beside each service): "
            + string.Join("; ", duplicates.Select(d => $"{d.Id} at {string.Join(", ", d.Sites)}")));
    }

    [Fact(DisplayName = "The real Services/Signals scan sees the declarations (the scan is not empty)")]
    public void RealTree_ScanSeesDeclarations()
    {
        var ids = SignalFiles().SelectMany(f => Declared(f.Source)).Select(d => d.Id).ToList();

        // 50300 (OntologyWriterEvents) and 50316 (DecisionRecordWriter) are the two ids that once collided.
        Assert.Contains(50300, ids);
        Assert.Contains(50316, ids);
        Assert.True(ids.Count >= 30, $"Found only {ids.Count} EventId declarations; the scan is probably broken.");
    }

    // ── Controls ──────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Control (must fire): the same id declared in two files is reported with both sites")]
    public void Control_DuplicateId_Fires()
    {
        var duplicates = Duplicates(
        [
            ("A.cs", "public static readonly EventId One = new(50316, nameof(One));"),
            ("B.cs", "public static readonly EventId Two = new EventId(50316, nameof(Two));"),
            ("C.cs", "[LoggerMessage(EventId = 50999, Level = LogLevel.Error)]"),
        ]);

        var only = Assert.Single(duplicates);
        Assert.Equal(50316, only.Id);
        Assert.Equal(["A.cs:1", "B.cs:1"], only.Sites);
    }

    [Fact(DisplayName = "Control (must fire): a LoggerMessage EventId that repeats a static EventId is reported")]
    public void Control_LoggerMessageDuplicate_Fires()
    {
        var duplicates = Duplicates(
        [
            ("A.cs", "public static readonly EventId One = new(50316, nameof(One));"),
            ("B.cs", "[LoggerMessage(EventId = 50316, Level = LogLevel.Error)]"),
        ]);

        Assert.Equal(50316, Assert.Single(duplicates).Id);
    }

    [Fact(DisplayName = "Control (must not fire): distinct ids, and an id quoted only in a comment, pass")]
    public void Control_DistinctIds_DoNotFire()
    {
        var duplicates = Duplicates(
        [
            ("A.cs", "public static readonly EventId One = new(50316, nameof(One));\n// was EventId Old = new(50317, nameof(Old));"),
            ("B.cs", "public static readonly EventId Two = new(50317, nameof(Two));"),
            ("C.cs", "[LoggerMessage(EventId = 50318, Level = LogLevel.Error)]"),
        ]);

        Assert.Empty(duplicates);
    }

    private static IEnumerable<(string File, string Source)> SignalFiles()
    {
        Assert.True(Directory.Exists(SignalsDir), $"{SignalsDir} not found");
        return Directory
            .EnumerateFiles(SignalsDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => (SourceScan.Relative(f), File.ReadAllText(f)));
    }
}
