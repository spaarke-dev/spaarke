using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ISS-039 (data-setup plan, work item D2): a Dataverse row id hard-coded in product code must be a row that every
/// customer stamp receives, i.e. a primary key in <c>scripts/seed-data/reference/*.json</c> (seeded by H12b), or be listed in
/// the ratchet baseline with a written reason. An id that is a solution component id (a form, view or app module that ships in
/// <c>src/dataverse/solutions/**</c>) is portable too: component ids are preserved on import, so every stamp has it.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> Product code hard-codes dev row ids of <c>sprk_eventtype_ref</c> (the "Task" type in
/// <c>DailyBriefingCollector</c>, <c>PortfolioService</c>, <c>TaskActionCore</c>; eleven type ids in the Events page and the Event side
/// pane). A new stamp had no such rows, so every lookup matched nothing. The seed files fix the ids; this guard catches the NEXT
/// hard-coded id at PR time instead of on a customer stamp.</para>
/// <para><b>Enforced by:</b> this source scan (CI and the Stop hook). Scanned: <c>.cs .ts .tsx .js .jsx .json</c> under
/// <c>src/server</c>, <c>src/client</c> and <c>src/solutions/*/src</c>, excluding build output, tests (<c>__tests__</c>, <c>tests</c>,
/// <c>*.test.*</c>, <c>*.spec.*</c>, <c>*.Tests</c> projects) and the explicit <see cref="HarnessOnly"/> files. A dev-pattern id
/// (<c>xxxxxxxx-xxxx-f[01]11-xxxx-xxxxxxxxxxxx</c>) in a scanned file must be a seed-file primary key or in
/// <c>ReferenceRowIdLiteral.baseline.txt</c> (<c>path | id | reason</c>), unless it appears in the solution source. The list only shrinks: a
/// baseline entry whose id is no longer in its file, or whose id is now a seed key or a solution component id, fails until the line is
/// deleted.</para>
/// <para><b>Known limit:</b> since ISS-041 (#3090) the pattern is every id shape the dev environment has generated so far:
/// <c>xxxxxxxx-xxxx-f[01]11-xxxx-xxxxxxxxxxxx</c> (rows created in 2026 carry <c>-f011-</c> or <c>-f111-</c> and a varying fourth group).
/// A row created after Dataverse changes its sequential-id scheme, and rows of another environment (a customer stamp, a
/// <c>Guid.NewGuid()</c> id such as the external record-documents grid configuration), have other shapes and are not detected. A guard for
/// every id would need the id shape of every environment, which Dataverse does not promise. Prefer seeding or an alternate-key lookup
/// over baselining a new hit.</para>
/// <para><b>Controls:</b> a planted unlisted id fires; a seed key, a solution component id, a baselined id, a harness-only file and a test
/// file do not; a stale baseline line fires.</para>
/// </remarks>
public sealed class ReferenceRowIdLiteralGuardTests
{
    private static readonly Regex DevRowId = new(
        @"(?<![0-9A-Fa-f-])[0-9a-f]{8}-[0-9a-f]{4}-f[01]11-[0-9a-f]{4}-[0-9a-f]{12}(?![0-9A-Fa-f-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] ScannedExtensions = [".cs", ".ts", ".tsx", ".js", ".jsx", ".json"];

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "dist", "out", "obj", "bin", "coverage", "build", "__tests__", "__mocks__", "__fixtures__", "tests", "test",
        ".vite", ".turbo", "storybook-static",
    };

    private const string BaselineFile = "tests/Spaarke.ArchTests/ReferenceRowIdLiteral.baseline.txt";
    private const string SeedDirectory = "scripts/seed-data/reference";
    private const string SystemLayoutsFile = "scripts/system-layouts.json";
    private const string SolutionDirectory = "src/dataverse/solutions";

    /// <summary>
    /// Product-code files that carry dev ids on purpose because they are harness-only (never shipped data), each with its reason.
    /// </summary>
    private static readonly Dictionary<string, string> HarnessOnly = new(StringComparer.Ordinal)
    {
        // DEMO_LISTS is documented in the file as "Demo rows for the browser test harness ONLY": mirrors of dev lists so the form's
        // UX is iterable without the BFF. The production path reads the lists from the BFF.
        ["src/client/office-addins/shared/taskpane/hooks/useCreateRecordFormData.ts"] =
            "DEMO_LISTS for the browser test harness only (documented in the file)",
    };

    internal sealed record Hit(string Path, string Id, int Line);

    // ── Pure logic (unit-tested below with synthetic inputs) ───────────────────────────────────

    private static bool IsExcludedDirectory(string name)
        => ExcludedDirectories.Contains(name)
           || name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
           || name.EndsWith(".Test", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a repo-relative path (forward slashes) is product code this guard scans.</summary>
    internal static bool IsScannedPath(string path)
    {
        var segments = path.Split('/');
        var name = segments[^1];
        if (!ScannedExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)
            || name.Contains(".test.", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".spec.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (segments[..^1].Any(IsExcludedDirectory))
        {
            return false;
        }

        if (path.StartsWith("src/server/", StringComparison.Ordinal) || path.StartsWith("src/client/", StringComparison.Ordinal))
        {
            return true;
        }

        // src/solutions/<Page>/src/** only (the other folders there are solution packaging, not product code).
        return segments.Length > 4 && segments[0] == "src" && segments[1] == "solutions" && segments[3] == "src";
    }

    /// <summary>Every dev-pattern row id literal in the given files (lower-cased ids, 1-based lines).</summary>
    internal static IReadOnlyList<Hit> FindHits(IEnumerable<(string Path, string Content)> files)
    {
        var hits = new List<Hit>();
        foreach (var (path, content) in files)
        {
            var lines = content.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in DevRowId.Matches(lines[i]))
                {
                    hits.Add(new Hit(path, m.Value.ToLowerInvariant(), i + 1));
                }
            }
        }

        return hits;
    }

    /// <summary>
    /// The failures: an id that is neither a seed key nor baselined (and not in a harness-only file), a baseline entry that no longer
    /// matches a hit, and a baseline entry whose id has since become a seed key.
    /// </summary>
    internal static IReadOnlyList<string> FindProblems(
        IReadOnlyList<Hit> hits,
        ISet<string> seedIds,
        IReadOnlyDictionary<(string Path, string Id), string> baseline,
        IReadOnlySet<string> harnessOnlyPaths,
        ISet<string>? solutionIds = null)
    {
        solutionIds ??= new HashSet<string>();
        var problems = new List<string>();
        var scoped = hits.Where(h => !harnessOnlyPaths.Contains(h.Path)).ToList();

        foreach (var group in scoped.Where(h => !seedIds.Contains(h.Id) && !solutionIds.Contains(h.Id) && !baseline.ContainsKey((h.Path, h.Id)))
                     .GroupBy(h => (h.Path, h.Id)))
        {
            problems.Add($"{group.Key.Path}:{group.First().Line}: hard-codes Dataverse row id {group.Key.Id}, which is not a primary key in " +
                         $"{SeedDirectory}/*.json or a component of the solution source ({SolutionDirectory}), so a customer stamp will not have that row. Add the table to the reference export " +
                         $"(Export-ReferenceData.ps1) and re-export, or stop hard-coding the id; as a last resort add '{group.Key.Path} | {group.Key.Id} | <reason>' " +
                         $"to {BaselineFile} with an owner decision.");
        }

        var present = scoped.Select(h => (h.Path, h.Id)).ToHashSet();
        foreach (var (key, _) in baseline)
        {
            if (!present.Contains(key))
            {
                problems.Add($"{BaselineFile}: stale entry '{key.Path} | {key.Id}': the id is no longer in that file. Delete the line (the list only shrinks).");
            }
            else if (seedIds.Contains(key.Id) || solutionIds.Contains(key.Id))
            {
                problems.Add($"{BaselineFile}: entry '{key.Path} | {key.Id}': the id is now a seed-file primary key or a solution component id. Delete the line.");
            }
        }

        return problems;
    }

    internal static IReadOnlyDictionary<(string Path, string Id), string> ParseBaseline(IEnumerable<string> lines)
    {
        var result = new Dictionary<(string Path, string Id), string>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var parts = line.Split('|', 3, StringSplitOptions.TrimEntries);
            Assert.True(parts.Length == 3 && parts[2].Length > 0,
                $"Baseline line must be 'path | id | reason' with a non-empty reason: '{line}'");
            result[(parts[0], parts[1].ToLowerInvariant())] = parts[2];
        }

        return result;
    }

    // ── The real scan ───────────────────────────────────────────────────────────────────────────

    private static string Abs(string repoRelative) => Path.Combine(SourceScan.RepoRoot, repoRelative.Replace('/', Path.DirectorySeparatorChar));

    private static IEnumerable<string> ScannedFiles()
    {
        foreach (var root in new[] { "src/server", "src/client", "src/solutions" })
        {
            foreach (var file in Walk(Abs(root)))
            {
                var rel = file.Substring(SourceScan.RepoRoot.Length + 1).Replace('\\', '/');
                if (IsScannedPath(rel))
                {
                    yield return rel;
                }
            }
        }
    }

    /// <summary>Directory walk that prunes excluded directories, so node_modules is never enumerated.</summary>
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

        foreach (var sub in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(sub);
            if (IsExcludedDirectory(name))
            {
                continue;
            }

            foreach (var file in Walk(sub))
            {
                yield return file;
            }
        }
    }

    internal static HashSet<string> LoadSeedIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Abs(SeedDirectory), "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var key = doc.RootElement.GetProperty("primaryKey").GetString()!;
            foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray())
            {
                ids.Add(row.GetProperty(key).GetString()!.ToLowerInvariant());
            }
        }

        // ISS-045: system workspace layouts that declare a fixed "id" in scripts/system-layouts.json are created with that
        // primary key by the H12b workspace-layout seeder, so every stamp has them.
        using (var layouts = JsonDocument.Parse(File.ReadAllText(Abs(SystemLayoutsFile))))
        {
            foreach (var layout in layouts.RootElement.GetProperty("layouts").EnumerateArray())
            {
                if (layout.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    ids.Add(id.GetString()!.ToLowerInvariant());
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// Every dev-pattern id in the solution source (file names and XML content): forms, views, app modules and the like. These ids
    /// are preserved when the solution is imported, so product code that references one works on every stamp.
    /// </summary>
    internal static HashSet<string> LoadSolutionComponentIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Walk(Abs(SolutionDirectory)))
        {
            foreach (Match m in DevRowId.Matches(Path.GetFileName(file)))
            {
                ids.Add(m.Value.ToLowerInvariant());
            }

            if (string.Equals(Path.GetExtension(file), ".xml", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match m in DevRowId.Matches(File.ReadAllText(file)))
                {
                    ids.Add(m.Value.ToLowerInvariant());
                }
            }
        }

        return ids;
    }

    [Fact(DisplayName = "ISS-039: every dev row id hard-coded in product code is a seeded reference row, a solution component or in the ratchet baseline")]
    public void EveryHardCodedDevRowId_IsASeededRow_OrBaselined()
    {
        var seedIds = LoadSeedIds();
        Assert.True(seedIds.Count >= 37, $"Only {seedIds.Count} seed ids were read from {SeedDirectory}; the guard would be vacuous.");

        var files = ScannedFiles().ToList();
        Assert.True(files.Count > 300, $"Only {files.Count} product source files were scanned; the guard would be vacuous.");

        var hits = FindHits(files.Select(f => (f, File.ReadAllText(Abs(f)))));
        Assert.Contains(hits, h => h.Path.EndsWith("PortfolioService.cs", StringComparison.Ordinal) && seedIds.Contains(h.Id));

        var solutionIds = LoadSolutionComponentIds();
        Assert.Contains("eaf22dcb-9aff-f011-8406-7c1e525abd8b", solutionIds); // an Event main form: proves the solution scan is not vacuous

        var baseline = ParseBaseline(File.ReadAllLines(Abs(BaselineFile)));
        var problems = FindProblems(hits, seedIds, baseline, HarnessOnly.Keys.ToHashSet(StringComparer.Ordinal), solutionIds);

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact(DisplayName = "ISS-039: every sprk_sidepaneformguid in the event-type seed is a form that ships in SpaarkeMaster")]
    public void EventTypeSidePaneForms_ShipInTheSolution()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Abs($"{SeedDirectory}/sprk_eventtype_ref.json")));
        var forms = doc.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("sprk_sidepaneformguid").GetString())
            .Where(g => g is not null)
            .Distinct()
            .ToList();
        Assert.True(forms.Count >= 4, $"Expected at least 4 distinct side-pane forms in the seed; found {forms.Count}.");

        var formDirectory = Abs("src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Event/FormXml/main");
        var missing = forms.Where(g => !File.Exists(Path.Combine(formDirectory, "{" + g + "}.xml"))).ToList();

        Assert.True(missing.Count == 0,
            "sprk_sidepaneformguid values that are not a form in SpaarkeMaster (a stamp would route to a form that does not exist): "
            + string.Join(", ", missing));
    }

    // ── Controls ────────────────────────────────────────────────────────────────────────────────

    private const string SeedId = "124f5fc9-98ff-f011-8406-7c1e525abd8b";
    private const string OtherId = "aaaaaaaa-1111-f011-8406-000000000001";

    /// <summary>A later-generation dev id shape (<c>-f111-</c>, another fourth group), invisible to the pre-ISS-041 pattern.</summary>
    private const string LaterGenerationId = "aaaaaaaa-2222-f111-b8dc-000000000002";

    /// <summary>A random (v4) GUID: the third group is not <c>f011</c>/<c>f111</c>, so it is not a dev-generated id (e.g. a stamp's row).</summary>
    private const string NonDevId = "286be8b4-f96f-4d5a-b1d5-82fd83ca9693";

    private static readonly IReadOnlyDictionary<(string Path, string Id), string> NoBaseline = new Dictionary<(string, string), string>();
    private static readonly IReadOnlySet<string> NoHarness = new HashSet<string>();

    [Fact(DisplayName = "ISS-039 control (must fire): an unlisted dev row id in product code is a problem")]
    public void Control_UnlistedId_Fires()
    {
        var hits = FindHits([("src/server/api/X/Thing.cs", $"private const string T = \"{OtherId}\";")]);

        var problems = FindProblems(hits, new HashSet<string> { SeedId }, NoBaseline, NoHarness);

        Assert.Single(problems);
        Assert.Contains(OtherId, problems[0]);
        Assert.Contains("Thing.cs:1", problems[0]);
    }

    [Fact(DisplayName = "ISS-041 control (must fire): an unlisted -f111- shaped dev row id (a later generation than -f011-8406-) is a problem")]
    public void Control_UnlistedLaterGenerationId_Fires()
    {
        var hits = FindHits([("src/client/external-spa/src/widgets/XWidget.tsx", $"const CONFIG_ID = '{LaterGenerationId.ToUpperInvariant()}';")]);

        var problems = FindProblems(hits, new HashSet<string> { SeedId }, NoBaseline, NoHarness);

        Assert.Single(problems);
        Assert.Contains(LaterGenerationId, problems[0]);
        Assert.Contains("XWidget.tsx:1", problems[0]);
    }

    [Fact(DisplayName = "ISS-041 control (must not fire): a non-dev GUID (random v4, not -f011-/-f111-) is not a row id this guard detects")]
    public void Control_NonDevGuid_DoesNotFire()
    {
        var hits = FindHits(
        [
            ("src/server/api/X/Thing.cs", $"Guid.Parse(\"{NonDevId}\")"),
            ("src/server/api/X/Other.cs", "\"aaaaaaaa-1111-f211-8406-000000000001\""),   // f211: not a dev-generated third group
            ("src/server/api/X/Long.cs", $"\"{LaterGenerationId}-extra\""),               // longer hex run: not a whole id
        ]);

        Assert.Empty(hits);
        Assert.Empty(FindProblems(hits, new HashSet<string> { SeedId }, NoBaseline, NoHarness));
    }

    [Fact(DisplayName = "ISS-039 control (must not fire): a seeded id, a baselined id and a harness-only file are accepted")]
    public void Control_SeededBaselinedAndHarnessIds_DoNotFire()
    {
        var hits = FindHits(
        [
            ("src/server/api/X/Thing.cs", $"new Guid(\"{SeedId.ToUpperInvariant()}\")"),                 // seed key (any casing)
            ("src/solutions/Page/src/config.ts", $"id: '{OtherId}'"),                                      // baselined
            ("src/client/office-addins/Demo.ts", $"{{ id: '{OtherId}' }}"),                                // harness-only
            ("src/server/api/X/Other.cs", $"\"{NonDevId}\""),                                              // not a dev-generated shape
        ]);
        var baseline = new Dictionary<(string Path, string Id), string> { [("src/solutions/Page/src/config.ts", OtherId)] = "reason" };

        var problems = FindProblems(hits, new HashSet<string> { SeedId }, baseline, new HashSet<string> { "src/client/office-addins/Demo.ts" });

        Assert.Empty(problems);
    }

    [Fact(DisplayName = "ISS-039 control (must not fire): an id that is a solution component (an Event form) is portable, a lookalike is not")]
    public void Control_SolutionComponentId_DoesNotFire_ButAnUnlistedOneStillDoes()
    {
        // eaf22dcb-... is the id of an sprk_event main form that ships in SpaarkeMaster (FormXml/main/{eaf22dcb-...}.xml).
        const string FormId = "eaf22dcb-9aff-f011-8406-7c1e525abd8b";
        var hits = FindHits([("src/solutions/EventsPage/src/config/forms.ts", $"formId: '{FormId}'")]);

        Assert.Empty(FindProblems(hits, new HashSet<string> { SeedId }, NoBaseline, NoHarness, new HashSet<string> { FormId }));

        // Same hit without the solution set (the old behaviour) fires, so the pass above is the solution set's doing.
        Assert.Single(FindProblems(hits, new HashSet<string> { SeedId }, NoBaseline, NoHarness));

        // The set is read from the real solution source and contains that form.
        Assert.Contains(FormId, LoadSolutionComponentIds());
    }

    [Fact(DisplayName = "ISS-039 control (must fire): a stale or now-seeded baseline entry fails, so the list only shrinks")]
    public void Control_StaleBaselineEntry_Fires()
    {
        var hits = FindHits([("src/server/api/X/Thing.cs", $"\"{SeedId}\"")]);
        var baseline = new Dictionary<(string Path, string Id), string>
        {
            [("src/server/api/X/Gone.cs", OtherId)] = "id removed from the file",
            [("src/server/api/X/Thing.cs", SeedId)] = "became a seed key",
        };

        var problems = FindProblems(hits, new HashSet<string> { SeedId }, baseline, NoHarness);

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("stale entry") && p.Contains("Gone.cs"));
        Assert.Contains(problems, p => p.Contains("now a seed-file primary key"));
    }

    [Theory(DisplayName = "ISS-039 control: path scope")]
    [InlineData("src/server/api/Sprk.Bff.Api/Services/Workspace/PortfolioService.cs", true)]
    [InlineData("src/client/shared/Spaarke.UI.Components/src/hooks/useAiSummary.ts", true)]
    [InlineData("src/solutions/EventsPage/src/config/eventConfig.ts", true)]
    [InlineData("src/solutions/EventsPage/Solution/Other/Customizations.xml", false)]
    [InlineData("src/solutions/EventsPage/node_modules/x/index.js", false)]
    [InlineData("src/client/shared/Spaarke.UI.Components/src/hooks/__tests__/useAiSummary.test.ts", false)]
    [InlineData("src/client/external-spa/tests/DetailDocumentsDelete.test.tsx", false)]
    [InlineData("src/client/shared/Spaarke.UI.Components/src/hooks/useThing.spec.ts", false)]
    [InlineData("src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/X.cs", false)]
    [InlineData("src/server/api/Sprk.Bff.Api/bin/Debug/x.json", false)]
    [InlineData("src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Event/FormXml/main/x.xml", false)]
    public void Control_PathScope(string path, bool expected) => Assert.Equal(expected, IsScannedPath(path));
}
