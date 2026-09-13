using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-052 anti-drift guard (unified-access-control-r2 task 102). ADR-052 is the ONLY place the
/// workload-placement rule — where background, scheduled and event-driven work runs — is stated in full. Every
/// other document states it as a one-line summary plus a link. This test fails the build when a phrasing that
/// CONTRADICTS ADR-052 reappears anywhere an agent or engineer takes direction from.
///
/// <para><b>Why a guard and not just an edit.</b> By 2026-09 four incompatible placement rules existed across
/// ~70 documents. Each amendment reached some documents and not others, and the stale ones kept seeding new work:
/// the project-setup template wrote a flat ban into every new project's CLAUDE.md, and an agent following the
/// newest ADR was flagged for violating an older constraint. A one-time alignment pass decays the same way unless
/// something fails when the old wording comes back.</para>
///
/// <para><b>A tripwire, not a parser.</b> It matches a fixed list of phrasings. Paraphrases slip through; that is
/// accepted. Every banned pattern has a negative-control sample, and the reasoned-marker and aligned-wording cases
/// have positive controls, per <c>tests/CLAUDE.md</c> "Structural fitness functions".</para>
/// </summary>
public class WorkloadPlacementDocDriftTests
{
    // =============================================================================================
    // MAINTENANCE PROCEDURE — read before changing anything here.
    //
    //   1. When this test fails, the named text contradicts ADR-052. Rewrite it as a ONE-LINE summary plus a link
    //      to docs/adr/ADR-052-workload-placement.md (or .claude/adr/ADR-052-workload-placement.md). Do not
    //      restate the policy — restating is how the drift started.
    //
    //   2. If the text is genuinely HISTORICAL — a superseded rule quoted as history, a dated assessment, a
    //      decision-log row, a project's own recorded decision — keep the words and wrap them in a REASONED marker:
    //         Markdown:  <!-- adr052-drift:allow reason="why this is history" --> ... <!-- /adr052-drift:allow -->
    //                    (block or inline on one line; never between the rows of a table — wrap the table)
    //         C#/Bicep:  // adr052-drift:allow reason="..."          covers its own line
    //                    // adr052-drift:allow-begin reason="..."  ...  // adr052-drift:allow-end
    //         YAML/PS1:  the same with '#'.
    //      A marker without a non-empty reason is itself a failure. An unclosed block is a failure.
    //
    //   3. NEVER widen the path excludes to make a directive pass. The excludes are records that are not
    //      directives: .claude/archive, .claude/agent-memory, knowledge/, and projects/ other than the
    //      CLAUDE.md of an active project (a row in projects/INDEX.md).
    //
    //   4. Adding a banned pattern: add a sample for it to BannedSamples. BannedSamplesCoverEveryPattern fails
    //      until you do. Write multi-word patterns with \s+ between words (see BannedPhrasings).
    //
    //   5. A BFF-scoped statement ("Azure Functions are not permitted inside the BFF assembly") is ADR-001's
    //      current rule, not drift. The flat-ban patterns therefore skip a match followed, within the same
    //      sentence, by "inside / in / within the BFF" or "Sprk.Bff.Api" (BffScope).
    //
    //   Known limits: paraphrases ("in-process only") are missed; inventory statements ("zero Azure Functions
    //   projects today") need wording that does not read as a rule, or a marker.
    // =============================================================================================

    private const string GuardRelativePath = "tests/Spaarke.ArchTests/WorkloadPlacementDocDriftTests.cs";

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Not drift when the same sentence scopes the statement to the BFF assembly (ADR-001's live rule).</summary>
    private const string BffScope = @"(?![^.\n]{0,40}\b(?:inside|in|within)\s+(?:the\s+)?(?:BFF\b|`?Sprk\.Bff\.Api))";

    /// <summary>
    /// Matched against the text with markdown emphasis and code ticks blanked out (same length, so line numbers
    /// hold), and with <c>\s+</c> between words — otherwise <c>**MUST NOT** use Durable Functions</c>, a backticked
    /// phrase, or a phrase wrapped across two lines walks straight past a literal-space pattern. That hole was found
    /// on the first alignment pass, in decision-log lines written in bold.
    /// </summary>
    private static readonly Banned[] BannedPhrasings =
    {
        new(@"\bno\s+Azure\s+Functions\b" + BffScope, "a flat 'no Azure Functions' rule"),
        new(@"\bnot\s+Azure\s+Functions\b" + BffScope, "a flat 'not Azure Functions' rule"),
        new(@"\bdo(?:n'?t|\s+not)\s+use\s+Azure\s+Functions\b" + BffScope, "a flat ban on Azure Functions"),
        new(@"\bFunctions\s+are\s+(?:not\s+permitted|prohibited|forbidden|banned|discouraged)\b" + BffScope, "a flat ban on Azure Functions"),
        new(@"\b(?:avoid|prohibit\w*)\s+(?:Azure\s+)?Functions\b" + BffScope, "a flat ban on Azure Functions"),
        new(@"Functions[^.\n]{0,80}out-of-band|out-of-band[^.\n]{0,80}Functions", "Functions scoped to 'out-of-band' work (ADR-001's superseded 2026-05-19 wording)"),
        new(@"\b(?:no|not|never|do\s+not\s+(?:introduce|use)|don'?t\s+use|MUST\s+NOT\s+use)\s+Durable\s+Functions\b", "a Durable Functions ban (withdrawn — ADR-052 §7)"),
        new(@"Durable\s+Functions\s+\(always", "a Durable Functions ban (withdrawn — ADR-052 §7)"),
        new(@"\bIJobHandler<", "IJobHandler<T>, a type that does not exist — the contract is the non-generic IJobHandler"),
        new(@"event-driven\s+\(timer,\s+queue,\s+webhook\)", "the trigger deciding the host (superseded 2026-05-20 wording)"),
        new(@"\b(?:timer|queue|webhook)[^.\n]{0,40}(?:→|->)\s+(?:Azure\s+)?Functions\b", "the trigger deciding the host"),
        new(@"ADR-001\s+prefers\s+in-process", "a misstatement of ADR-001 (withdrawn by ADR-036 A1)"),
        new(@"in-process\s+workers;\s+no\s+Azure\s+Functions", "a misstatement of ADR-001 (withdrawn by ADR-036 A1)"),
        new(@"default\s+to\s+BackgroundService\s+when", "ADR-001's superseded 2026-05-19 tie-breaker"),
        new(@"\(use\s+Service\s+Bus\s+\+\s+state\s+machine", "the withdrawn Durable ban's replacement clause"),
    };

    private static readonly Regex MarkerStart =
        new(@"(?<open><!--|//|#)[ \t]*adr052-drift:allow(?<begin>-begin)?(?![-\w])(?<rest>[^\n]*)", Options);

    private static readonly Regex MarkdownMarkerEnd = new(@"<!--[ \t]*/adr052-drift:allow[ \t]*-->", Options);

    private static readonly Regex BlockMarkerEnd = new(@"(?://|#)[ \t]*adr052-drift:allow-end\b", Options);

    private static readonly Regex Reason = new("reason=\"(?<r>[^\"]*)\"", Options);

    private const string Guidance =
        "ADR-052 is the only full statement of where background, scheduled and event-driven work runs. Rewrite each " +
        "finding as a one-line pointer to docs/adr/ADR-052-workload-placement.md; if it is genuinely historical, " +
        "wrap it in <!-- adr052-drift:allow reason=\"...\" --> ... <!-- /adr052-drift:allow --> (or the // or # " +
        "forms). See the maintenance procedure in WorkloadPlacementDocDriftTests.cs. Findings:\n";

    [Fact(DisplayName = "ADR-052: no directive or document contradicts the workload-placement rule")]
    public void NoContradictingPlacementPhrasing()
    {
        var findings = new List<string>();
        var scanned = new List<string>();

        foreach (var file in PlacementRepoFiles.DriftScanFiles())
        {
            var relative = PlacementRepoFiles.Relative(file);
            scanned.Add(relative);
            findings.AddRange(Scan(relative, File.ReadAllText(file)));
        }

        // A guard that scans nothing passes. Prove the walk reached every kind of root.
        Assert.True(scanned.Count > 500, $"Only {scanned.Count} files scanned — the repository walk is broken.");
        Assert.Contains("docs/adr/ADR-052-workload-placement.md", scanned);
        Assert.Contains(".claude/constraints/bff-extensions.md", scanned);
        Assert.Contains("tests/Spaarke.ArchTests/ADR001_MinimalApiTests.cs", scanned);
        Assert.Contains(scanned, f => f.StartsWith("src/server/", StringComparison.Ordinal) && f.EndsWith(".cs", StringComparison.Ordinal));
        Assert.DoesNotContain(GuardRelativePath, scanned);

        Assert.True(findings.Count == 0, Guidance + string.Join("\n", findings));
    }

    // ---------------------------------------------------------------------------------------------
    // Negative controls — the detector fires
    // ---------------------------------------------------------------------------------------------

    private static readonly string[] BannedSamples =
    {
        "ADR-001: Minimal API, no Azure Functions",
        "This is not Azure Functions territory",
        "Do not use Azure Functions for this",
        "Don't use Azure Functions for this",
        "Azure Functions are not permitted.",
        "Functions are prohibited",
        "Avoid Azure Functions",
        "ADR-001 prohibits Functions",
        "Functions OK for narrow out-of-band integration",
        "out-of-band work belongs in Azure Functions",
        "No Durable Functions",
        "MUST NOT use Durable Functions for orchestration",
        "Do not use Durable Functions",
        "Violation: Durable Functions (always)",
        "Uses IJobHandler<T> per ADR-004",
        "Is it event-driven (timer, queue, webhook) with no synchronous user wait?",
        "timer-driven work → Functions",
        "queue triggers -> Azure Functions",
        "ADR-001 prefers in-process",
        "ADR-001: in-process workers; no Azure Functions",
        "Default to BackgroundService when the choice is close",
        "orchestration (use Service Bus + state machine instead)",
    };

    public static IEnumerable<object[]> BannedSampleData => BannedSamples.Select(s => new object[] { s });

    [Theory(DisplayName = "ADR-052 drift guard: negative control — every banned phrasing is reported")]
    [MemberData(nameof(BannedSampleData))]
    public void NegativeControl_EachBannedPhrasingIsReported(string sample)
    {
        Assert.NotEmpty(Scan("docs/sample.md", $"Some directive.\n- {sample}\n"));
        Assert.NotEmpty(Scan("src/server/Sample.cs", $"    // {sample}\n"));
    }

    [Fact(DisplayName = "ADR-052 drift guard: every banned pattern has a negative-control sample")]
    public void BannedSamplesCoverEveryPattern()
    {
        var uncovered = BannedPhrasings
            .Where(b => !BannedSamples.Any(s => b.Pattern.IsMatch(s)))
            .Select(b => b.Pattern.ToString())
            .ToList();

        Assert.True(uncovered.Count == 0, "Patterns with no sample in BannedSamples: " + string.Join(" | ", uncovered));
    }

    [Fact(DisplayName = "ADR-052 drift guard: negative control — emphasis, code ticks and line wraps do not defeat it")]
    public void NegativeControl_FormattingDoesNotDefeatTheGuard()
    {
        Assert.NotEmpty(Scan("x.md", "- **MUST NOT** use Durable Functions (D-20)\n"));
        Assert.NotEmpty(Scan("x.md", "| `No Azure Functions` | ADR-001 |\n"));
        Assert.NotEmpty(Scan("x.md", "*No* Azure Functions here.\n"));
        Assert.NotEmpty(Scan("x.md", "The rule is: no\nAzure Functions for sync work.\n"));
        Assert.NotEmpty(Scan("x.md", "Handlers implement `IJobHandler<T>`.\n"));
    }

    [Fact(DisplayName = "ADR-052 drift guard: negative control — a marker without a reason is reported")]
    public void NegativeControl_AMarkerWithoutAReasonIsReported()
    {
        Assert.Contains(Scan("x.md", "<!-- adr052-drift:allow -->\nhistory\n<!-- /adr052-drift:allow -->"), f => f.Contains("without a reason", StringComparison.Ordinal));
        Assert.Contains(Scan("x.md", "<!-- adr052-drift:allow reason=\"  \" -->history<!-- /adr052-drift:allow -->"), f => f.Contains("without a reason", StringComparison.Ordinal));
        Assert.Contains(Scan("x.cs", "var a = 1; // adr052-drift:allow\n"), f => f.Contains("without a reason", StringComparison.Ordinal));
        Assert.Contains(Scan("x.yml", "# adr052-drift:allow-begin\nkey: value\n# adr052-drift:allow-end\n"), f => f.Contains("without a reason", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "ADR-052 drift guard: negative control — an unclosed marker is reported, and exempts nothing")]
    public void NegativeControl_AnUnclosedMarkerIsReported()
    {
        var markdown = Scan("x.md", "<!-- adr052-drift:allow reason=\"history\" -->\nNo Azure Functions\n");
        Assert.Contains(markdown, f => f.Contains("unclosed", StringComparison.Ordinal));
        Assert.Contains(markdown, f => f.Contains("flat 'no Azure Functions'", StringComparison.Ordinal));

        var block = Scan("x.cs", "// adr052-drift:allow-begin reason=\"history\"\n// No Azure Functions\n");
        Assert.Contains(block, f => f.Contains("unclosed", StringComparison.Ordinal));
        Assert.Contains(block, f => f.Contains("flat 'no Azure Functions'", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "ADR-052 drift guard: negative control — a marker exempts only its own region")]
    public void NegativeControl_AMarkerExemptsOnlyItsRegion()
    {
        const string text =
            "<!-- adr052-drift:allow reason=\"quoted as history\" -->\n\"No Azure Functions\"\n<!-- /adr052-drift:allow -->\n" +
            "Current rule: No Azure Functions.\n" +
            "var s = 1; // adr052-drift:allow reason=\"this line only\"\n" +
            "// No Durable Functions\n";

        var findings = Scan("x.md", text);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.StartsWith("x.md:4:", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.StartsWith("x.md:6:", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------------
    // Positive controls — the detector does not fire on the sanctioned shapes
    // ---------------------------------------------------------------------------------------------

    [Theory(DisplayName = "ADR-052 drift guard: positive control — a reasoned marker exempts history, in every form")]
    [MemberData(nameof(BannedSampleData))]
    public void PositiveControl_AReasonedMarkerExemptsHistory(string sample)
    {
        Assert.Empty(Scan("x.md", $"<!-- adr052-drift:allow reason=\"quoted as history\" -->\n{sample}\n<!-- /adr052-drift:allow -->\n"));
        Assert.Empty(Scan("x.md", $"Earlier text <!-- adr052-drift:allow reason=\"history\" -->\"{sample}\"<!-- /adr052-drift:allow --> later.\n"));
        Assert.Empty(Scan("x.cs", $"var s = \"{sample}\"; // adr052-drift:allow reason=\"history\"\n"));
        Assert.Empty(Scan("x.cs", $"// adr052-drift:allow-begin reason=\"history\"\n// {sample}\n// adr052-drift:allow-end\n"));
        Assert.Empty(Scan("x.yml", $"# adr052-drift:allow-begin reason=\"history\"\n- {sample}\n# adr052-drift:allow-end\n"));
    }

    [Fact(DisplayName = "ADR-052 drift guard: positive control — the aligned wording is not reported")]
    public void PositiveControl_TheAlignedWordingIsNotReported()
    {
        var aligned = new[]
        {
            "BFF endpoints are never hosted in Azure Functions (ADR-001).",
            "Azure Functions are not permitted inside the BFF assembly (ADR-001).",
            "The BFF has no Azure Functions or Durable Task packages inside Sprk.Bff.Api.",
            "No Azure Functions or Durable Task packages inside `Sprk.Bff.Api`.",
            "Where background work runs is decided per workload under ADR-052.",
            "Durable Task runs in its own host, never inside the BFF (ADR-052 §7).",
            "Handlers implement the non-generic `IJobHandler`.",
            "Outside a reasoned `adr052-drift:allow` region the guard fails.",
            "Queue-driven → ADR-004; schedule-driven → ADR-036.",
            "A Function reuses the stamp's managed identity, app-only.",
        };

        foreach (var line in aligned)
        {
            Assert.True(Scan("x.md", line + "\n").Count == 0, $"Aligned wording was reported: {line}");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The detector
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Findings for one file: each banned phrasing outside a reasoned marker region, each marker without a
    /// reason, and each unclosed marker block. Returned as <c>path:line: message</c>.
    /// </summary>
    internal static IReadOnlyList<string> Scan(string relativePath, string text)
    {
        var findings = new List<string>();
        var regions = new List<(int Start, int End)>();

        foreach (Match marker in MarkerStart.Matches(text))
        {
            var line = LineOf(text, marker.Index);
            var reason = Reason.Match(marker.Groups["rest"].Value);
            if (!reason.Success || string.IsNullOrWhiteSpace(reason.Groups["r"].Value))
            {
                findings.Add($"{relativePath}:{line}: adr052-drift:allow marker without a reason — every exemption must say why the text is history");
            }

            if (marker.Groups["open"].Value == "<!--")
            {
                var openerClose = text.IndexOf("-->", marker.Index, StringComparison.Ordinal);
                var end = openerClose < 0 ? Match.Empty : MarkdownMarkerEnd.Match(text, openerClose + 3);
                if (!end.Success)
                {
                    findings.Add($"{relativePath}:{line}: unclosed adr052-drift:allow marker — add <!-- /adr052-drift:allow -->");
                    continue;
                }

                regions.Add((marker.Index, end.Index + end.Length));
            }
            else if (marker.Groups["begin"].Success)
            {
                var end = BlockMarkerEnd.Match(text, marker.Index + marker.Length);
                if (!end.Success)
                {
                    findings.Add($"{relativePath}:{line}: unclosed adr052-drift:allow-begin marker — add adr052-drift:allow-end");
                    continue;
                }

                regions.Add((marker.Index, end.Index + end.Length));
            }
            else
            {
                var lineStart = text.LastIndexOf('\n', Math.Max(marker.Index - 1, 0)) + 1;
                var lineEnd = text.IndexOf('\n', marker.Index);
                regions.Add((lineStart, lineEnd < 0 ? text.Length : lineEnd));
            }
        }

        // Blank out emphasis and code ticks without changing any index, so formatting cannot split a phrase.
        var normalized = text.Replace('*', ' ').Replace('`', ' ');

        var reportedLines = new HashSet<int>();
        foreach (var banned in BannedPhrasings)
        {
            foreach (Match match in banned.Pattern.Matches(normalized))
            {
                if (regions.Any(r => match.Index >= r.Start && match.Index < r.End))
                {
                    continue;
                }

                var line = LineOf(text, match.Index);
                if (reportedLines.Add(line))
                {
                    findings.Add($"{relativePath}:{line}: {banned.Label} — \"{Regex.Replace(match.Value, @"\s+", " ").Trim()}\"");
                }
            }
        }

        return findings;
    }

    private static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private sealed class Banned
    {
        public Banned(string pattern, string label)
        {
            Pattern = new Regex(pattern, Options);
            Label = label;
        }

        public Regex Pattern { get; }

        public string Label { get; }
    }
}

/// <summary>
/// The repository files the ADR-052 guards read. A plain walk that skips build output and package folders — a
/// recursive <c>Directory.EnumerateFiles</c> would descend into every <c>node_modules</c> under <c>src/</c>.
/// </summary>
internal static class PlacementRepoFiles
{
    private static readonly string[] DriftScanExtensions = { ".md", ".cs", ".yml", ".yaml", ".bicep", ".ps1", ".psm1" };

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "dist", "out", ".git", ".vs", "packages", "TestResults", "coverage",
    };

    /// <summary>Not directives — see the maintenance procedure, step 3.</summary>
    private static readonly string[] ExcludedPrefixes = { ".claude/archive/", ".claude/agent-memory/" };

    private static readonly Regex RegistryRow = new(@"^\|\s*`(?<name>[A-Za-z0-9._-]+)`\s*\|", RegexOptions.CultureInvariant);

    internal static string Relative(string file) => SourceScan.Relative(file).Replace('\\', '/');

    /// <summary>Every file under <paramref name="root"/>, skipping build output and package folders.</summary>
    internal static IEnumerable<string> Walk(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                yield return file;
            }

            foreach (var child in Directory.EnumerateDirectories(dir))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(child)))
                {
                    pending.Push(child);
                }
            }
        }
    }

    /// <summary>The drift guard's scope: directives and documentation, plus active projects' CLAUDE.md.</summary>
    internal static IEnumerable<string> DriftScanFiles()
    {
        var repo = SourceScan.RepoRoot;

        foreach (var name in new[] { "CLAUDE.md", "README.md", ".coderabbit.yaml" })
        {
            var path = Path.Combine(repo, name);
            if (File.Exists(path))
            {
                yield return path;
            }
        }

        var roots = new[] { ".claude", "docs", ".github", "src", "tests" }
            .Select(r => Path.Combine(repo, r))
            .Concat(Directory.EnumerateDirectories(repo, "infra*"));

        foreach (var root in roots)
        {
            foreach (var file in Walk(root))
            {
                if (!DriftScanExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relative = Relative(file);
                if (ExcludedPrefixes.Any(p => relative.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                    || relative.Equals("tests/Spaarke.ArchTests/WorkloadPlacementDocDriftTests.cs", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
        }

        foreach (var project in ActiveProjects())
        {
            var path = Path.Combine(repo, "projects", project, "CLAUDE.md");
            if (File.Exists(path))
            {
                yield return path;
            }
        }
    }

    /// <summary>Project folder names listed as rows in <c>projects/INDEX.md</c> (the active-project registry).</summary>
    internal static IReadOnlyList<string> ActiveProjects()
    {
        var index = Path.Combine(SourceScan.RepoRoot, "projects", "INDEX.md");
        if (!File.Exists(index))
        {
            return Array.Empty<string>();
        }

        return File.ReadLines(index)
            .Select(l => RegistryRow.Match(l))
            .Where(m => m.Success)
            .Select(m => m.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
