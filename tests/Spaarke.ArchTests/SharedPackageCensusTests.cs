using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-012 amendment 2026-11-10: the shared-package census. Every package under
/// <c>src/client/shared/</c> is listed here with its status, an ADR citation and a written reason. The
/// list is checked against the folders on disk and against the census table in
/// <c>.claude/adr/ADR-012-shared-components.md</c>, so a new shared package needs both an ADR amendment
/// and a census entry.
///
/// <para><b>What this prevents.</b> The 2026-07-12 amendment sanctioned "the domain component libraries
/// (<c>@spaarke/events-components</c>, etc.)". That "etc." covered 11 of the 15 packages, and nine of
/// them were never named by any ADR. ADR-012 already set a three-test bar for a new sibling package, but
/// nothing ran it, so packages were added without meeting it. The risk here is over-promotion: too many
/// shared packages, not too few. This census turns the bar into a build failure.</para>
///
/// <para><b>Two statuses, deliberately.</b> <i>Sanctioned</i> packages have an ADR-backed contract, and
/// <see cref="EverySanctionedPackageIsNamedByItsCitedAdr"/> checks that the cited ADR names the package
/// outside the census table. <i>Grandfathered</i> packages existed before the census and were never
/// assessed. They may be used and extended, but each one is later either sanctioned by an amendment or
/// consolidated into a sanctioned package. The grandfathered list is a ratchet baseline: it may shrink,
/// and growing it means amending the ADR.</para>
///
/// <para>Source and file analysis only. No build output and no node_modules are read.</para>
/// </summary>
public class SharedPackageCensusTests
{
    // =============================================================================================
    // THE CENSUS
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE PROCEDURE — read before adding an entry.
    //
    //   1. A failure here is NOT a prompt to add an entry. It is a prompt to ask why a new shared
    //      package exists. ADR-012 requires ALL THREE of: a distinct contract (a different feature area
    //      is not enough), a dependency worth quarantining, and reuse across 2+ surfaces. If any one
    //      fails, extend an existing package (root CLAUDE.md §11) and delete the new folder.
    //
    //   2. If the package clears that bar, amend ADR-012 (Path B, root CLAUDE.md §6.5; the owner
    //      approves). Then add a row to the census table in .claude/adr/ADR-012-shared-components.md
    //      AND an entry here as Sanctioned, citing the ADR that defines the package's contract. That ADR
    //      must name the package outside the census table.
    //
    //   3. Never add a Grandfathered entry. That status is only for packages that existed when the
    //      census was taken (2026-11-10). To resolve one, either sanction it (step 2) or consolidate it
    //      into a sanctioned package, then delete its folder, its ADR row and its entry here.
    //
    //   4. Renaming a package or its folder: update the entry, the ADR row and every consumer's
    //      package.json in the same change.
    // =============================================================================================
    private static readonly IReadOnlyList<CensusEntry> Census = new[]
    {
        // ---- Sanctioned -----------------------------------------------------------------------
        new CensusEntry("@spaarke/ui-components", "Spaarke.UI.Components", PackageStatus.Sanctioned, "ADR-012",
            "The default home for reusable UI: UX components and abstracted-I/O services behind IDataService. "
            + "Every other package must justify itself against extending this one."),
        new CensusEntry("@spaarke/visuals", "Spaarke.Visuals", PackageStatus.Sanctioned, "ADR-012",
            "Presentational data-viz with no I/O. Quarantines @fluentui/react-charting and pins @types/react@18 "
            + "so PCF and Code Pages can both use it without casts (2026-07-12 amendment)."),
        new CensusEntry("@spaarke/auth", "Spaarke.Auth", PackageStatus.Sanctioned, "ADR-028",
            "The single SSO, token-acquisition and authenticated-fetch implementation shared by every surface; "
            + "a second copy would break the true-SSO requirement."),
        new CensusEntry("@spaarke/events-components", "Spaarke.Events.Components", PackageStatus.Sanctioned, "ADR-012",
            "Events and Tasks components shared by the EventsPage code page and the SpaarkeAi Calendar widget; "
            + "the one domain library the 2026-07-12 amendment named."),
        new CensusEntry("@spaarke/notifications", "Spaarke.Notifications", PackageStatus.Sanctioned, "ADR-047",
            "Host-agnostic client for the notification spine: SignalR delivery, routing by envelope kind and "
            + "poll fallback. ADR-047 defines the contract it implements."),
        new CensusEntry("@spaarke/ai-widgets", "Spaarke.AI.Widgets", PackageStatus.Sanctioned, "ADR-030",
            "Workspace and context widgets for the three-pane shell, built on the pane event bus (ADR-030) and "
            + "the stage lifecycle (ADR-031)."),

        // ---- Grandfathered — under review (baseline 2026-11-10; may shrink, never grow) ----------
        new CensusEntry("@spaarke/sdap-client", "Spaarke.SdapClient", PackageStatus.Grandfathered, "ADR-012",
            "SDAP API client used by 18 PCF controls and TS apps. Pre-dates the census; whether it overlaps "
            + "the BFF adapters in @spaarke/ui-components has not been assessed."),
        new CensusEntry("@spaarke/smart-todo-components", "Spaarke.SmartTodo.Components", PackageStatus.Grandfathered, "ADR-012",
            "Smart To Do components for Pattern D dual-use (section shim and widget). A feature area rather "
            + "than a distinct contract, so it does not yet meet the three-test bar."),
        new CensusEntry("@spaarke/communication-components", "Spaarke.Communication.Components", PackageStatus.Grandfathered, "ADR-012",
            "Communications workspace components for Pattern D dual-use (widget and code page). A feature area "
            + "rather than a distinct contract, so it does not yet meet the three-test bar."),
        new CensusEntry("@spaarke/daily-briefing-components", "Spaarke.DailyBriefing.Components", PackageStatus.Grandfathered, "ADR-012",
            "Daily Briefing components for Pattern D dual-use (code page and widget). A feature area rather "
            + "than a distinct contract, so it does not yet meet the three-test bar."),
        new CensusEntry("@spaarke/compose-components", "Spaarke.Compose.Components", PackageStatus.Grandfathered, "ADR-012",
            "TipTap-based Compose workspace. TipTap may justify quarantine as a heavy dependency, but the "
            + "package has not been assessed against the three tests."),
        new CensusEntry("@spaarke/document-operations", "Spaarke.DocumentOperations", PackageStatus.Grandfathered, "ADR-012",
            "Open-in-Word, download, delete and send-to-index hooks. Abstracted I/O is the same contract as "
            + "@spaarke/ui-components, which makes it a consolidation candidate."),
        new CensusEntry("@spaarke/ai-outputs", "Spaarke.AI.Outputs", PackageStatus.Grandfathered, "ADR-012",
            "AI output-pane and source-pane widgets and their registries. Its boundary with the sanctioned "
            + "@spaarke/ai-widgets package has not been assessed."),
        new CensusEntry("@spaarke/ai-context", "Spaarke.AI.Context", PackageStatus.Grandfathered, "ADR-012",
            "AI context providers, service clients and hooks with a single consumer, so it fails the "
            + "cross-surface reuse test as it stands."),
        new CensusEntry("@spaarke/legal-workspace", "Spaarke.LegalWorkspace", PackageStatus.Grandfathered, "ADR-012",
            "Source-only re-export boundary for the LegalWorkspace section registry (dataset-grid-framework-r2 "
            + "FR-10), with a single consumer; it exists to break a source-alias trap."),
    };

    private const string ConciseAdrRelativePath = ".claude/adr/ADR-012-shared-components.md";
    private const string CensusStartMarker = "<!-- shared-package-census:start -->";
    private const string CensusEndMarker = "<!-- shared-package-census:end -->";

    private static readonly Regex AdrTableRow = new(
        @"^\|\s*`(?<package>@spaarke/[a-z0-9.-]+)`\s*\|\s*`(?<folder>[^`]+)`\s*\|\s*(?<status>[^|]+?)\s*\|",
        RegexOptions.Compiled);

    // =============================================================================================

    [Fact(DisplayName = "ADR-012: every src/client/shared package is in the census, under the census name")]
    public void EverySharedPackageIsCensused()
    {
        var problems = CompareWithDisk(ScanSharedPackages(), Census);

        Assert.True(
            problems.Count == 0,
            "The shared-package census does not match src/client/shared/.\n\n"
            + "A failure here is NOT a prompt to add a census entry. ADR-012 allows a new shared package only "
            + "when it has a distinct contract, a dependency worth quarantining AND reuse across 2+ surfaces. "
            + "Otherwise extend an existing package. If the package is justified, amend ADR-012 and add a "
            + "Sanctioned entry, following the MAINTENANCE PROCEDURE above the census.\n\n"
            + string.Join("\n", problems));
    }

    [Fact(DisplayName = "ADR-012: the census table in the concise ADR matches the test census, row for row")]
    public void AdrCensusTableMatchesTheTestCensus()
    {
        var adrText = File.ReadAllLines(Path.Combine(SourceScan.RepoRoot, ConciseAdrRelativePath));
        var rows = ParseAdrCensusTable(adrText);

        Assert.True(rows.Count > 0, $"No census table found between the markers in {ConciseAdrRelativePath}.");

        var problems = CompareWithAdr(rows, Census);

        Assert.True(
            problems.Count == 0,
            $"The census table in {ConciseAdrRelativePath} and the census in this file disagree. They are "
            + "one decision written in two places: change both in the same PR, and the ADR change needs "
            + "owner approval (root CLAUDE.md §6.5, Path B).\n\n"
            + string.Join("\n", problems));
    }

    [Fact(DisplayName = "ADR-012: every census entry carries an ADR citation and a substantive reason")]
    public void EveryCensusEntryIsExplained()
    {
        var unexplained = Census
            .Where(e => !Regex.IsMatch(e.AdrCitation, @"^ADR-\d{3}$")
                        || string.IsNullOrWhiteSpace(e.Reason)
                        || e.Reason.Trim().Length < 60)
            .Select(e => e.Package)
            .ToList();

        Assert.True(
            unexplained.Count == 0,
            "Every census entry needs an ADR citation (ADR-NNN) and a reason of at least 60 characters. An "
            + "unexplained entry cannot be told apart from an oversight six months later. Entries missing one: "
            + string.Join(", ", unexplained));
    }

    [Fact(DisplayName = "ADR-012: every Sanctioned package is named by its cited ADR outside the census table")]
    public void EverySanctionedPackageIsNamedByItsCitedAdr()
    {
        // "Sanctioned" means an ADR defines the package's contract. The census table names every package,
        // so it is cut out before searching. Otherwise every package would count as named by ADR-012.
        var problems = new List<string>();

        foreach (var entry in Census.Where(e => e.Status == PackageStatus.Sanctioned))
        {
            var adrFiles = AdrFiles(entry.AdrCitation);
            if (adrFiles.Count == 0)
            {
                problems.Add($"{entry.Package}: cites {entry.AdrCitation}, but no file for it exists in .claude/adr or docs/adr.");
                continue;
            }

            var named = adrFiles.Any(f => WithoutCensusTable(File.ReadAllLines(f)).Contains(entry.Package, StringComparison.Ordinal));
            if (!named)
            {
                problems.Add($"{entry.Package}: {entry.AdrCitation} does not name the package outside the census table.");
            }
        }

        Assert.True(
            problems.Count == 0,
            "A Sanctioned entry must cite an ADR that defines the package's contract. If no ADR does, the "
            + "package is Grandfathered, not Sanctioned.\n\n"
            + string.Join("\n", problems));
    }

    // =============================================================================================
    // Negative controls: each detector fires on a seeded violation
    // =============================================================================================

    [Fact(DisplayName = "ADR-012: negative control — an unlisted folder, an absent entry and a renamed package are all reported")]
    public void CompareWithDisk_NegativeControl_ReportsEveryKindOfDrift()
    {
        var census = new[]
        {
            new CensusEntry("@spaarke/kept", "Spaarke.Kept", PackageStatus.Sanctioned, "ADR-012", Pad("kept")),
            new CensusEntry("@spaarke/gone", "Spaarke.Gone", PackageStatus.Grandfathered, "ADR-012", Pad("gone")),
            new CensusEntry("@spaarke/old-name", "Spaarke.Renamed", PackageStatus.Grandfathered, "ADR-012", Pad("renamed")),
        };
        var disk = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Spaarke.Kept"] = "@spaarke/kept",
            ["Spaarke.Renamed"] = "@spaarke/new-name",
            ["Spaarke.Sneaky"] = "@spaarke/sneaky",
        };

        var problems = CompareWithDisk(disk, census);

        Assert.Contains(problems, p => p.StartsWith("UNLISTED", StringComparison.Ordinal) && p.Contains("Spaarke.Sneaky", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("CENSUSED BUT ABSENT", StringComparison.Ordinal) && p.Contains("Spaarke.Gone", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("NAME MISMATCH", StringComparison.Ordinal) && p.Contains("@spaarke/new-name", StringComparison.Ordinal));
        Assert.Equal(3, problems.Count);
    }

    [Fact(DisplayName = "ADR-012: negative control — an ADR row with a different status, a missing row and an extra row are all reported")]
    public void CompareWithAdr_NegativeControl_ReportsEveryKindOfDrift()
    {
        var census = new[]
        {
            new CensusEntry("@spaarke/promoted", "Spaarke.Promoted", PackageStatus.Grandfathered, "ADR-012", Pad("promoted")),
            new CensusEntry("@spaarke/unrowed", "Spaarke.Unrowed", PackageStatus.Sanctioned, "ADR-012", Pad("unrowed")),
        };
        var markdown = new[]
        {
            "| Package | Folder | Status | Contract / reason |",
            "|---|---|---|---|",
            CensusStartMarker,
            "| `@spaarke/promoted` | `Spaarke.Promoted` | Sanctioned | quietly promoted in the ADR only |",
            "| `@spaarke/extra` | `Spaarke.Extra` | Grandfathered — under review | added to the ADR only |",
            CensusEndMarker,
        };

        var problems = CompareWithAdr(ParseAdrCensusTable(markdown), census);

        Assert.Contains(problems, p => p.StartsWith("STATUS MISMATCH", StringComparison.Ordinal) && p.Contains("@spaarke/promoted", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("MISSING FROM ADR", StringComparison.Ordinal) && p.Contains("@spaarke/unrowed", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("ONLY IN ADR", StringComparison.Ordinal) && p.Contains("@spaarke/extra", StringComparison.Ordinal));
        Assert.Equal(3, problems.Count);
    }

    [Fact(DisplayName = "ADR-012: negative control — the disk scan really reaches src/client/shared")]
    public void ScanSharedPackages_NegativeControl_FindsTheRealPackages()
    {
        // A scan that silently found nothing would turn every entry into CENSUSED BUT ABSENT, and that
        // failure would point at the wrong cause. Prove the scan reads real package names.
        var scanned = ScanSharedPackages();

        Assert.Equal("@spaarke/ui-components", scanned["Spaarke.UI.Components"]);
        Assert.Equal("@spaarke/auth", scanned["Spaarke.Auth"]);
    }

    // =============================================================================================
    // Positive controls: the detectors stay silent on the sanctioned shape
    // =============================================================================================

    [Fact(DisplayName = "ADR-012: positive control — matching disk, census and ADR table report nothing")]
    public void Comparers_PositiveControl_StaySilentWhenEverythingAgrees()
    {
        var census = new[]
        {
            new CensusEntry("@spaarke/one", "Spaarke.One", PackageStatus.Sanctioned, "ADR-012", Pad("one")),
            new CensusEntry("@spaarke/two", "Spaarke.Two", PackageStatus.Grandfathered, "ADR-012", Pad("two")),
        };
        var disk = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Spaarke.One"] = "@spaarke/one",
            ["Spaarke.Two"] = "@spaarke/two",
        };
        var markdown = new[]
        {
            "Rows outside the markers are ignored:",
            "| `@spaarke/outside` | `Spaarke.Outside` | Sanctioned | not part of the census |",
            CensusStartMarker,
            "| Package | Folder | Status | Contract / reason |",
            "|---|---|---|---|",
            "| `@spaarke/one` | `Spaarke.One` | Sanctioned | reason |",
            "| `@spaarke/two` | `Spaarke.Two` | Grandfathered — under review | reason |",
            CensusEndMarker,
        };

        Assert.Empty(CompareWithDisk(disk, census));
        Assert.Empty(CompareWithAdr(ParseAdrCensusTable(markdown), census));
    }

    // =============================================================================================
    // Machinery
    // =============================================================================================

    private enum PackageStatus
    {
        Sanctioned,
        Grandfathered,
    }

    private sealed record CensusEntry(
        string Package,
        string Folder,
        PackageStatus Status,
        string AdrCitation,
        string Reason);

    private sealed record AdrRow(string Package, string Folder, PackageStatus? Status, string RawStatus);

    /// <summary>Folder name → the <c>name</c> in its package.json, for every direct child of src/client/shared.</summary>
    private static Dictionary<string, string> ScanSharedPackages()
    {
        var sharedRoot = Path.Combine(SourceScan.RepoRoot, "src", "client", "shared");
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var dir in Directory.EnumerateDirectories(sharedRoot))
        {
            var manifest = Path.Combine(dir, "package.json");
            if (!File.Exists(manifest))
            {
                continue;
            }

            using var json = JsonDocument.Parse(File.ReadAllText(manifest));
            var name = json.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            found[Path.GetFileName(dir)] = name;
        }

        return found;
    }

    private static List<string> CompareWithDisk(IReadOnlyDictionary<string, string> disk, IReadOnlyList<CensusEntry> census)
    {
        var problems = new List<string>();
        var byFolder = census.ToDictionary(e => e.Folder, StringComparer.Ordinal);

        foreach (var (folder, name) in disk.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!byFolder.TryGetValue(folder, out var entry))
            {
                problems.Add($"UNLISTED shared package: src/client/shared/{folder} ({name}) is not in the census.");
            }
            else if (!string.Equals(entry.Package, name, StringComparison.Ordinal))
            {
                problems.Add($"NAME MISMATCH: src/client/shared/{folder} is named {name}, but the census lists it as {entry.Package}.");
            }
        }

        foreach (var entry in census.Where(e => !disk.ContainsKey(e.Folder)))
        {
            problems.Add(
                $"CENSUSED BUT ABSENT: {entry.Package} (src/client/shared/{entry.Folder}) has no package.json. "
                + "If it was consolidated or deleted, remove its entry and its ADR row.");
        }

        return problems;
    }

    private static List<string> CompareWithAdr(IReadOnlyList<AdrRow> rows, IReadOnlyList<CensusEntry> census)
    {
        var problems = new List<string>();
        var byPackage = census.ToDictionary(e => e.Package, StringComparer.Ordinal);
        var rowPackages = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            rowPackages.Add(row.Package);

            if (!byPackage.TryGetValue(row.Package, out var entry))
            {
                problems.Add($"ONLY IN ADR: {row.Package} has an ADR census row but no entry in this file.");
                continue;
            }

            if (!string.Equals(row.Folder, entry.Folder, StringComparison.Ordinal))
            {
                problems.Add($"FOLDER MISMATCH: {row.Package} is in folder {row.Folder} per the ADR, {entry.Folder} per this file.");
            }

            if (row.Status != entry.Status)
            {
                problems.Add($"STATUS MISMATCH: {row.Package} is '{row.RawStatus}' in the ADR, {entry.Status} in this file.");
            }
        }

        foreach (var entry in census.Where(e => !rowPackages.Contains(e.Package)))
        {
            problems.Add($"MISSING FROM ADR: {entry.Package} is in this file's census but has no row in the ADR census table.");
        }

        return problems;
    }

    /// <summary>The package rows between the census markers. Header and separator rows do not match the row shape.</summary>
    private static List<AdrRow> ParseAdrCensusTable(IReadOnlyList<string> lines)
    {
        var rows = new List<AdrRow>();
        var inside = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed == CensusStartMarker) { inside = true; continue; }
            if (trimmed == CensusEndMarker) { inside = false; continue; }
            if (!inside) { continue; }

            var match = AdrTableRow.Match(trimmed);
            if (!match.Success) { continue; }

            var raw = match.Groups["status"].Value.Trim();
            PackageStatus? status =
                raw.StartsWith("Sanctioned", StringComparison.Ordinal) ? PackageStatus.Sanctioned
                : raw.StartsWith("Grandfathered", StringComparison.Ordinal) ? PackageStatus.Grandfathered
                : null;

            rows.Add(new AdrRow(match.Groups["package"].Value, match.Groups["folder"].Value, status, raw));
        }

        return rows;
    }

    private static string WithoutCensusTable(IReadOnlyList<string> lines)
    {
        var kept = new List<string>();
        var inside = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed == CensusStartMarker) { inside = true; continue; }
            if (trimmed == CensusEndMarker) { inside = false; continue; }
            if (!inside) { kept.Add(line); }
        }

        return string.Join("\n", kept);
    }

    /// <summary>Concise and full ADR files for a citation such as <c>ADR-047</c>.</summary>
    private static List<string> AdrFiles(string citation)
    {
        var dirs = new[]
        {
            Path.Combine(SourceScan.RepoRoot, ".claude", "adr"),
            Path.Combine(SourceScan.RepoRoot, "docs", "adr"),
        };

        return dirs
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, $"{citation}-*.md"))
            .ToList();
    }

    private static string Pad(string seed) => $"{seed}: a control entry whose reason is long enough to pass the explanation rule.";
}
