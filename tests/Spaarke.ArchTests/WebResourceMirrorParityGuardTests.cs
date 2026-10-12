using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Issue #1651 — the committed SpaarkeMaster solution copy of each web resource must not drift from the
/// maintained source it was exported from.
/// </summary>
/// <remarks>
/// <para><b>Why a fitness function.</b> <c>src/dataverse/solutions/SpaarkeMaster/WebResources/</c> is an EXPORT from dev
/// (the files that get packaged into the solution). The maintained sources live in <c>src/client/webresources/**</c>,
/// <c>src/solutions/webresources/**</c>, <c>infrastructure/dataverse/ribbon/*/WebResources/**</c> and the top-level
/// <c>src/solutions/*/sprk_*.js|css</c> files (a code page's own ribbon-command script, e.g.
/// <c>src/solutions/EventCommands/sprk_event_ribbon_commands.js</c>). Nothing tied the two
/// together, so they drifted silently: the mirror's <c>sprk_subgrid_parent_rollup.js</c> was 1.0.0 and called the BFF
/// recalculate endpoints anonymously (now 401) while the source was 2.0.0 (MSAL bearer token). Anyone who imports the
/// committed solution would have shipped the broken script.</para>
///
/// <para><b>Name-matching rule</b> (checked against the real trees on 2026-10-12). A mirror file's logical name is its
/// BASENAME, whatever folder it sits in. It corresponds to a source file whose basename is that name or the name prefixed
/// <c>sprk_</c> (case-insensitive — Dataverse names are):</para>
/// <list type="bullet">
/// <item><c>WebResources/sprk_/scripts/bff_auth.js</c> ↔ <c>sprk_bff_auth.js</c> or <c>bff_auth.js</c>;</item>
/// <item><c>WebResources/sprk_/js/registrationribbon.js</c> ↔ <c>sprk_registrationribbon.js</c>;</item>
/// <item><c>WebResources/sprk_foo.js</c> ↔ <c>sprk_foo.js</c> only (already prefixed, so no further prefix is tried).</item>
/// </list>
/// <para>A mirror file with no source counterpart in those trees is not a finding (a resource whose source lives
/// elsewhere, or a resource authored in the maker portal).</para>
///
/// <para><b>Comparison.</b> CRLF→LF and a leading UTF-8 BOM are normalised away: the working tree is CRLF on Windows
/// (<c>core.autocrlf</c>) and LF in CI, and neither is a content difference. Everything else is. A pair is in parity when
/// EVERY source candidate equals the mirror; one source candidate that differs from the mirror is a finding, and so is a
/// pair of source candidates that differ from each other (one of them necessarily differs from the mirror).</para>
///
/// <para><b>Ratchet.</b> The mismatches that existed when this guard was armed are listed in
/// <c>WebResourceMirrorParity.baseline.txt</c> (one line each: mirror path, then its source path(s)). The guard fails on a
/// mismatch NOT in the baseline and on a baseline line that is stale (its pair now matches, no longer exists, or lists
/// different source paths). The list only shrinks: resolve an entry by re-exporting the mirror from the source (or fixing
/// the source), then delete the line.</para>
///
/// <para><b>Enforced by:</b> this class. Per <c>tests/CLAUDE.md</c> "Structural fitness functions" it is MAINTAIN-class.</para>
/// </remarks>
public sealed class WebResourceMirrorParityGuardTests
{
    private const string MirrorRoot = "src/dataverse/solutions/SpaarkeMaster/WebResources";

    private static readonly string[] SourceRoots =
    [
        "src/client/webresources",
        "src/solutions/webresources",
        "infrastructure/dataverse/ribbon",   // */WebResources/** below it — see IsRibbonWebResource
    ];

    /// <summary>
    /// <c>src/solutions/*</c> directories are code pages, not web-resource folders; only a <c>sprk_*.js|css</c> file sitting
    /// DIRECTLY in one of them (not in a subfolder: those are build inputs or bootstrap helpers) is a web-resource source.
    /// </summary>
    private const string SolutionsRoot = "src/solutions";

    private static readonly string[] ExcludedSegments =
    [
        "node_modules", "dist", "out", "obj", "bin", "coverage", "__tests__", "__mocks__",
    ];

    private const string BaselineFile = "tests/Spaarke.ArchTests/WebResourceMirrorParity.baseline.txt";

    /// <summary>A file's repo-relative path (forward slashes) and its text.</summary>
    internal sealed record Item(string Path, string Content);

    // ── Pure logic (unit-tested below with synthetic inputs) ──────────────────────────────────

    /// <summary>CRLF→LF and a leading BOM stripped — the only differences the guard ignores.</summary>
    internal static string Normalize(string text)
    {
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>The source basenames a mirror file corresponds to: its own basename, and that basename prefixed <c>sprk_</c>.</summary>
    internal static IReadOnlyList<string> CandidateNames(string mirrorPath)
    {
        var name = mirrorPath[(mirrorPath.LastIndexOf('/') + 1)..];
        return name.StartsWith("sprk_", StringComparison.OrdinalIgnoreCase) ? [name] : [name, "sprk_" + name];
    }

    /// <summary>
    /// The baseline-format line for every mirror file that differs from a source counterpart:
    /// <c>mirror path | source path ; source path</c> (sources ordinal-sorted, all candidates listed, matching or not,
    /// so the line is stable and a reader sees everything the mirror was compared with).
    /// </summary>
    internal static IReadOnlyList<string> FindMismatches(IEnumerable<Item> mirror, IEnumerable<Item> sources)
    {
        var sourceList = sources.ToList();
        var lines = new List<string>();

        foreach (var m in mirror.OrderBy(x => x.Path, StringComparer.Ordinal))
        {
            var names = CandidateNames(m.Path);
            var candidates = sourceList
                .Where(s => names.Contains(s.Path[(s.Path.LastIndexOf('/') + 1)..], StringComparer.OrdinalIgnoreCase))
                .DistinctBy(s => s.Path, StringComparer.Ordinal)
                .OrderBy(s => s.Path, StringComparer.Ordinal)
                .ToList();

            if (candidates.Count == 0)
            {
                continue;   // no source counterpart: nothing to compare
            }

            var expected = Normalize(m.Content);
            if (candidates.Any(c => !string.Equals(Normalize(c.Content), expected, StringComparison.Ordinal)))
            {
                lines.Add($"{m.Path} | {string.Join(" ; ", candidates.Select(c => c.Path))}");
            }
        }

        return lines;
    }

    /// <summary>Parses the baseline: one entry per non-blank line, <c>#</c> lines are comments.</summary>
    internal static IReadOnlyList<string> ParseBaseline(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();

    /// <summary>Failure messages for mismatches missing from the baseline and baseline lines that no longer describe a mismatch.</summary>
    internal static (IReadOnlyList<string> New, IReadOnlyList<string> Stale) Reconcile(
        IReadOnlyList<string> mismatches, IReadOnlyList<string> baseline)
    {
        static string MirrorOf(string line) => line.Split('|')[0].Trim();

        var baselineSet = baseline.ToHashSet(StringComparer.Ordinal);
        var mismatchSet = mismatches.ToHashSet(StringComparer.Ordinal);
        var mismatchByMirror = mismatches.ToDictionary(MirrorOf, StringComparer.Ordinal);
        var baselineMirrors = baseline.Select(MirrorOf).ToHashSet(StringComparer.Ordinal);

        var added = mismatches
            .Where(l => !baselineSet.Contains(l) && !baselineMirrors.Contains(MirrorOf(l)))
            .Select(l => $"new mirror drift (not in the baseline): {l}")
            .ToList();

        var stale = new List<string>();
        foreach (var line in baseline.Where(l => !mismatchSet.Contains(l)))
        {
            stale.Add(mismatchByMirror.TryGetValue(MirrorOf(line), out var now)
                ? $"stale baseline entry — remove it (its source paths changed; the pair now reads: {now}): {line}"
                : $"stale baseline entry — remove it (the pair now matches, or the file no longer exists): {line}");
        }

        return (added, stale);
    }

    // ── The scan ──────────────────────────────────────────────────────────────────────────────

    private static string Repo(string relative) => Path.Combine(SourceScan.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string RelativeSlash(string file)
        => Path.GetRelativePath(SourceScan.RepoRoot, file).Replace('\\', '/');

    private static IEnumerable<string> Walk(string directory)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            yield return file;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (ExcludedSegments.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var file in Walk(child))
            {
                yield return file;
            }
        }
    }

    private static bool IsScript(string file)
        => file.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".css", StringComparison.OrdinalIgnoreCase);

    private static bool IsBuiltOrTestFile(string file)
    {
        var name = Path.GetFileName(file);
        return name.Equals("bundle.js", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".bundle.js", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase)
               || name.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase)
               || System.Text.RegularExpressions.Regex.IsMatch(name, @"\.(test|spec)\.[jt]sx?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>Under <c>infrastructure/dataverse/ribbon</c> only <c>*/WebResources/**</c> is a web-resource source.</summary>
    private static bool IsRibbonWebResource(string relative)
        => !relative.StartsWith("infrastructure/dataverse/ribbon/", StringComparison.Ordinal)
           || relative.Split('/').Skip(4).Take(1).Contains("WebResources", StringComparer.Ordinal);

    private static IReadOnlyList<Item> ReadItems(IEnumerable<string> files)
        => files.Where(IsScript).Where(f => !IsBuiltOrTestFile(f))
            .Select(f => new Item(RelativeSlash(f), File.ReadAllText(f)))
            .Where(i => IsRibbonWebResource(i.Path))
            .ToList();

    private static IReadOnlyList<Item> MirrorFiles() => ReadItems(Walk(Repo(MirrorRoot)));

    /// <summary>Top-level <c>src/solutions/*/sprk_*.js|css</c> files — see <see cref="SolutionsRoot"/>.</summary>
    private static IEnumerable<string> SolutionTopLevelScripts()
    {
        var root = Repo(SolutionsRoot);
        return !Directory.Exists(root)
            ? []
            : Directory.EnumerateDirectories(root)
                .Where(d => !ExcludedSegments.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
                .SelectMany(Directory.EnumerateFiles)
                .Where(f => Path.GetFileName(f).StartsWith("sprk_", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<Item> SourceFiles()
        => ReadItems(SourceRoots.SelectMany(r => Walk(Repo(r))).Concat(SolutionTopLevelScripts()));

    [Fact(DisplayName = "Issue #1651: every SpaarkeMaster web-resource mirror matches its maintained source, or is in the baseline")]
    public void MirrorMatchesSource_OrIsBaselined()
    {
        var mirror = MirrorFiles();
        var sources = SourceFiles();

        // An empty scan proves nothing — fail loudly if the roots moved.
        Assert.True(mirror.Count >= 20, $"only {mirror.Count} mirror files under {MirrorRoot} — the scan root is wrong");
        Assert.True(sources.Count >= 20, $"only {sources.Count} source files — the source roots are wrong");

        var mismatches = FindMismatches(mirror, sources);
        var baselinePath = Repo(BaselineFile);
        Assert.True(File.Exists(baselinePath), $"{BaselineFile} is missing");
        var baseline = ParseBaseline(File.ReadAllText(baselinePath));

        var (added, stale) = Reconcile(mismatches, baseline);
        var problems = added.Concat(stale).ToList();

        Assert.True(problems.Count == 0,
            "A SpaarkeMaster web-resource mirror drifted from its maintained source (issue #1651). Re-export the mirror from the "
            + "source (copy the source file over it), or fix the source if the MIRROR is right; do not add to the baseline — it "
            + "only shrinks. A stale baseline entry means that pair is fixed: delete its line from " + BaselineFile + "."
            + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    [Fact(DisplayName = "Issue #1651: the scan pairs real mirror files with real sources")]
    public void TheScanFindsPairs()
    {
        var sourceNames = SourceFiles().Select(s => s.Path[(s.Path.LastIndexOf('/') + 1)..]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var paired = MirrorFiles().Count(m => CandidateNames(m.Path).Any(sourceNames.Contains));
        Assert.True(paired >= 10, $"only {paired} mirror files have a source counterpart — the name rule or the roots are wrong");

        // the defect that motivated the guard: this pair exists and is compared
        Assert.Contains(MirrorFiles(), m => m.Path == $"{MirrorRoot}/sprk_subgrid_parent_rollup.js");
        Assert.Contains(SourceFiles(), s => s.Path == "src/solutions/webresources/sprk_subgrid_parent_rollup.js");

        // a code page's own top-level sprk_*.js (src/solutions/<page>/) is a source too — its mirror drifted unreported
        Assert.Contains(MirrorFiles(), m => m.Path == $"{MirrorRoot}/sprk_event_ribbon_commands.js");
        Assert.Contains(SourceFiles(), s => s.Path == "src/solutions/EventCommands/sprk_event_ribbon_commands.js");
        // ...but not a nested one, and not an unprefixed file
        Assert.DoesNotContain(SourceFiles(), s => s.Path.StartsWith("src/solutions/", StringComparison.Ordinal)
            && !s.Path.StartsWith("src/solutions/webresources/", StringComparison.Ordinal)
            && s.Path.Count(c => c == '/') != 3);
    }

    // ── Controls (synthetic inputs) ───────────────────────────────────────────────────────────

    private const string Mirror = MirrorRoot + "/sprk_foo.js";

    private static Item M(string content, string path = Mirror) => new(path, content);

    private static Item S(string content, string path = "src/solutions/webresources/sprk_foo.js") => new(path, content);

    [Fact(DisplayName = "Control (must fire): content differs")]
    public void Fires_WhenContentDiffers()
        => Assert.Single(FindMismatches([M("var a = 1;\n")], [S("var a = 2;\n")]));

    [Fact(DisplayName = "Control (must fire): differs only by a real one-character change at the end")]
    public void Fires_WhenOnlyARealTrailingChangeDiffers()
        => Assert.Single(FindMismatches([M("function f() {}\n")], [S("function f() {};\n")]));

    [Fact(DisplayName = "Control (must fire): differs by whitespace inside a line (only CRLF and BOM are ignored)")]
    public void Fires_WhenInnerWhitespaceDiffers()
        => Assert.Single(FindMismatches([M("a  b\n")], [S("a b\n")]));

    [Fact(DisplayName = "Control (must fire): line names the mirror and every source candidate")]
    public void Fires_WithMirrorAndSourcePathsInTheLine()
    {
        var line = Assert.Single(FindMismatches([M("x")], [S("y")]));
        Assert.Equal($"{Mirror} | src/solutions/webresources/sprk_foo.js", line);
    }

    [Fact(DisplayName = "Control (must not fire): differs only by CRLF vs LF")]
    public void DoesNotFire_OnLineEndingsOnly()
        => Assert.Empty(FindMismatches([M("a\r\nb\r\n")], [S("a\nb\n")]));

    [Fact(DisplayName = "Control (must not fire): differs only by a UTF-8 BOM")]
    public void DoesNotFire_OnBomOnly()
        => Assert.Empty(FindMismatches([M("﻿var a;\n")], [S("var a;\n")]));

    [Fact(DisplayName = "Control (must not fire): differs by both BOM and CRLF")]
    public void DoesNotFire_OnBomAndCrlf()
        => Assert.Empty(FindMismatches([M("﻿a\r\nb")], [S("a\nb")]));

    [Fact(DisplayName = "Control (must not fire): no source counterpart")]
    public void DoesNotFire_WhenThereIsNoSource()
        => Assert.Empty(FindMismatches([M("anything")], [S("other", "src/solutions/webresources/sprk_bar.js")]));

    [Fact(DisplayName = "Name rule: nested mirror foo.js matches sprk_foo.js and foo.js, case-insensitively, and not bar.js")]
    public void NameRule_NestedMirror()
    {
        var nested = M("old", MirrorRoot + "/sprk_/scripts/foo.js");
        Assert.Single(FindMismatches([nested], [S("new", "src/client/webresources/js/sprk_foo.js")]));
        Assert.Single(FindMismatches([nested], [S("new", "src/client/webresources/js/foo.js")]));
        Assert.Single(FindMismatches([nested], [S("new", "src/client/webresources/js/SPRK_FOO.JS")]));
        Assert.Empty(FindMismatches([nested], [S("new", "src/client/webresources/js/bar.js")]));
        Assert.Empty(FindMismatches([nested], [S("new", "src/client/webresources/js/sprk_sprk_foo.js")]));
    }

    [Fact(DisplayName = "Name rule: an already-prefixed mirror sprk_foo.js matches sprk_foo.js, not foo.js")]
    public void NameRule_PrefixedMirror()
    {
        Assert.Single(FindMismatches([M("old")], [S("new", "src/client/webresources/js/sprk_foo.js")]));
        Assert.Empty(FindMismatches([M("old")], [S("new", "src/client/webresources/js/foo.js")]));
    }

    [Fact(DisplayName = "Name rule: css files pair the same way")]
    public void NameRule_Css()
        => Assert.Single(FindMismatches(
            [M("a{}", MirrorRoot + "/sprk_/css/foo.css")],
            [S("b{}", "src/client/webresources/css/sprk_foo.css")]));

    [Fact(DisplayName = "Control (must fire): several sources that differ from each other, one equal to the mirror")]
    public void Fires_WhenSourceCandidatesDisagree_EvenIfOneMatchesTheMirror()
    {
        var line = Assert.Single(FindMismatches(
            [M("v1")],
            [S("v1", "src/client/webresources/js/sprk_foo.js"), S("v2", "infrastructure/dataverse/ribbon/X/WebResources/sprk_foo.js")]));
        Assert.Equal($"{Mirror} | infrastructure/dataverse/ribbon/X/WebResources/sprk_foo.js ; src/client/webresources/js/sprk_foo.js", line);
    }

    [Fact(DisplayName = "Control (must not fire): several sources, all identical to the mirror")]
    public void DoesNotFire_WhenEverySourceCandidateMatches()
        => Assert.Empty(FindMismatches(
            [M("same\r\n")],
            [S("same\n", "src/client/webresources/js/sprk_foo.js"), S("same\n", "infrastructure/dataverse/ribbon/X/WebResources/sprk_foo.js")]));

    // ── Ratchet controls ──────────────────────────────────────────────────────────────────────

    private const string Line = MirrorRoot + "/sprk_foo.js | src/solutions/webresources/sprk_foo.js";

    [Fact(DisplayName = "Ratchet (must not fire): every mismatch is baselined and every baseline line is a mismatch")]
    public void Ratchet_Clean()
    {
        var (added, stale) = Reconcile([Line], [Line]);
        Assert.Empty(added);
        Assert.Empty(stale);
    }

    [Fact(DisplayName = "Ratchet (must fire): a mismatch that is not in the baseline")]
    public void Ratchet_NewMismatch()
    {
        var (added, stale) = Reconcile([Line], []);
        Assert.Single(added);
        Assert.Empty(stale);
    }

    [Fact(DisplayName = "Ratchet (must fire): a baseline entry whose pair now matches is stale")]
    public void Ratchet_StaleBecausePairNowMatches()
    {
        var (added, stale) = Reconcile([], [Line]);
        Assert.Empty(added);
        var message = Assert.Single(stale);
        Assert.Contains("stale baseline entry — remove it", message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Ratchet (must fire): a baseline entry for a file that no longer exists is stale")]
    public void Ratchet_StaleBecauseFileIsGone()
    {
        var other = MirrorRoot + "/sprk_gone.js | src/solutions/webresources/sprk_gone.js";
        var (added, stale) = Reconcile([Line], [Line, other]);
        Assert.Empty(added);
        Assert.Contains("stale baseline entry — remove it", Assert.Single(stale), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Ratchet (must fire): a baseline entry whose source paths changed is stale, not silently accepted")]
    public void Ratchet_StaleBecauseSourcesChanged()
    {
        var now = MirrorRoot + "/sprk_foo.js | infrastructure/dataverse/ribbon/X/WebResources/sprk_foo.js ; src/solutions/webresources/sprk_foo.js";
        var (added, stale) = Reconcile([now], [Line]);
        Assert.Empty(added);
        Assert.Contains("source paths changed", Assert.Single(stale), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Baseline parsing ignores comments and blank lines")]
    public void Baseline_Parsing()
        => Assert.Equal([Line], ParseBaseline("# comment\r\n\r\n  " + Line + "  \r\n# another\n"));
}
