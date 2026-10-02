using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 157 (owner round 4 item 7, 2026-10-01) — every Spaarke <c>DataGrid</c> the
/// external SPA mounts must pass <c>showViewSelector={false}</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a security invariant, not a UI preference.</b> The external module read seam
/// (<c>ExternalModuleDataEndpoints</c>) admits only the columns on each module's allow-list
/// (<c>ExternalAccessModule.cs</c>, task 134 defect C6). Task 157 shrank those lists to what each grid's own
/// <c>sprk_gridconfiguration</c> shows, plus the scope and <c>/record</c> default columns. That is correct
/// only while the grid cannot switch to another query. The shared <c>DataGrid</c> defaults
/// <c>showViewSelector</c> to <c>true</c> and then offers every INTERNAL MDA main view of the entity
/// (<c>/savedqueries/{entity}</c>). A mount that drops the prop brings that picker back, and every sibling
/// view errors with <c>DV_FETCHXML_COLUMN_NOT_PERMITTED</c>. The tempting "fix" for that error is to widen the
/// server allow-list again, which re-exposes internal columns (<c>ownerid</c>, <c>sprk_assignedto</c>,
/// <c>sprk_visibilitystate</c> …) to outside counsel. This guard stops the first step.
/// </para>
/// <para>
/// <b>What it scans.</b> Every <c>.ts</c>/<c>.tsx</c> file under <c>src/client/external-spa/src</c>. A file
/// that imports <c>DataGrid</c> from <c>@spaarke/ui-components</c> (under any local alias) must pass
/// <c>showViewSelector={false}</c> on every JSX mount of it. Fluent's own <c>DataGrid</c> (from
/// <c>@fluentui/react-components</c>) has no view picker and is ignored. Two shapes the per-mount check
/// cannot see are refused outright: importing <c>DataGridPageShell</c> (it mounts the shared grid itself and
/// has no way to switch the picker off), and any namespace import from the shared library (a
/// <c>&lt;UI.DataGrid&gt;</c> mount could not be attributed).
/// </para>
/// <para>
/// <b>Crude by design</b> (see <see cref="SourceScan"/>): regex over source, not a TypeScript parse. Each rule
/// is paired with a negative control proving it fires and a positive control proving it does not fire on the
/// sanctioned shape. ADR-038 Amendment A1: <c>tests/Spaarke.ArchTests/**</c> is a deletion-protected KEEP path.
/// </para>
/// <para><b>Maintenance.</b> If an external grid ever genuinely needs a view picker, do NOT relax this guard
/// alone: re-derive the module's server allow-list with task 134's rule (b) (sibling-view columns) in the same
/// change and have the owner approve the wider column exposure.</para>
/// </remarks>
public class ExternalSpaGridViewSelectorGuardTests
{
    private static string ExternalSpaSource =>
        Path.Combine(SourceScan.RepoRoot, "src", "client", "external-spa", "src");

    private static readonly Regex NamedImport = new(
        @"import\s+(?:type\s+)?\{(?<names>[^}]*)\}\s*from\s*['""](?<module>@spaarke/ui-components[^'""]*)['""]",
        RegexOptions.Compiled);

    private static readonly Regex NamespaceImport = new(
        @"import\s+\*\s+as\s+\w+\s+from\s*['""](?<module>@spaarke/ui-components[^'""]*)['""]",
        RegexOptions.Compiled);

    // One import specifier: `DataGrid`, `type DataGridProps`, `DataGrid as Grid` — any whitespace, newlines included.
    private static readonly Regex ImportSpecifier = new(
        @"^\s*(?:type\s+)?(?<imported>\w+)(?:\s+as\s+(?<local>\w+))?\s*$", RegexOptions.Compiled);

    private static readonly Regex SelectorOff = new(@"\bshowViewSelector\s*=\s*\{\s*false\s*\}", RegexOptions.Compiled);

    /// <summary>The result of scanning one file: every violation, and how many sanctioned mounts it holds.</summary>
    internal sealed record ScanResult(IReadOnlyList<string> Violations, int CompliantMounts);

    /// <summary>Scans one file's text. Pure, so the controls below exercise exactly the production detector.</summary>
    internal static ScanResult Scan(string source, string fileName)
    {
        var text = StripComments(source);
        var violations = new List<string>();
        var localNames = new List<string>();

        foreach (Match import in NamedImport.Matches(text))
        {
            foreach (var raw in import.Groups["names"].Value.Split(','))
            {
                var specifier = ImportSpecifier.Match(raw);
                if (!specifier.Success)
                {
                    continue;
                }
                var imported = specifier.Groups["imported"].Value;
                var local = specifier.Groups["local"].Success ? specifier.Groups["local"].Value : imported;

                if (imported == "DataGrid")
                {
                    localNames.Add(local);
                }
                else if (imported == "DataGridPageShell")
                {
                    violations.Add($"{fileName}: imports DataGridPageShell, which mounts the shared DataGrid with "
                                   + "its default view selector and cannot switch it off. Mount DataGrid directly "
                                   + "with showViewSelector={false}.");
                }
            }
        }

        foreach (Match ns in NamespaceImport.Matches(text))
        {
            // Any module of the shared lib: `import * as UI from '@spaarke/ui-components'` then `<UI.DataGrid>`
            // would mount the grid under a name this scan cannot attribute. None exists today.
            violations.Add($"{fileName}: namespace import of '{ns.Groups["module"].Value}'. Import from the shared "
                           + "library by name so every DataGrid mount can be checked for showViewSelector={false}.");
        }

        var compliant = 0;
        foreach (var local in localNames.Distinct())
        {
            var open = new Regex($@"<{Regex.Escape(local)}(?=[\s/>])");
            foreach (Match mount in open.Matches(text))
            {
                var tag = OpeningTag(text, mount.Index);
                if (SelectorOff.IsMatch(tag))
                {
                    compliant++;
                }
                else
                {
                    var line = text[..mount.Index].Count(c => c == '\n') + 1;
                    violations.Add($"{fileName}:{line}: <{local}> mounted without showViewSelector={{false}}. The "
                                   + "shared DataGrid would offer the entity's internal MDA views, and the server "
                                   + "column allow-list (task 157) does not admit their columns.");
                }
            }
        }

        return new ScanResult(violations, compliant);
    }

    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

    // A `//` that starts a line or follows whitespace. A URL inside a string ('https://…') has ':' before the
    // slashes, so it is left alone and the code after it on that line is still scanned.
    private static readonly Regex LineComment = new(@"(?<=^|\s)//[^\n]*", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Removes comments so a documentation example (GridWidgetBody's JSDoc names <c>&lt;DataGrid configId=… /&gt;</c>)
    /// is not read as a mount. Block comments keep their newlines, so reported line numbers stay true.
    /// </summary>
    private static string StripComments(string source)
    {
        var withoutBlocks = BlockComment.Replace(source, m => new string('\n', m.Value.Count(c => c == '\n')));
        return LineComment.Replace(withoutBlocks, string.Empty);
    }

    /// <summary>
    /// The opening tag starting at <paramref name="start"/>: up to the first <c>&gt;</c> outside any
    /// <c>{…}</c> expression, so an arrow function in a prop (<c>=&gt;</c>) does not end the tag early.
    /// </summary>
    private static string OpeningTag(string text, int start)
    {
        var depth = 0;
        for (var i = start + 1; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    break;
                case '>' when depth == 0:
                    return text[start..(i + 1)];
            }
        }
        return text[start..];
    }

    private static IEnumerable<string> ExternalSpaFiles() =>
        Directory.EnumerateFiles(ExternalSpaSource, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".ts", StringComparison.Ordinal) || f.EndsWith(".tsx", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact(DisplayName = "Every Spaarke DataGrid mounted by the external SPA passes showViewSelector={false}")]
    public void ExternalSpa_EverySpaarkeDataGridMount_TurnsTheViewSelectorOff()
    {
        Assert.True(Directory.Exists(ExternalSpaSource), $"the external SPA source must exist at {ExternalSpaSource}");

        var violations = new List<string>();
        var compliantByFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in ExternalSpaFiles())
        {
            var relative = Path.GetRelativePath(SourceScan.RepoRoot, file).Replace('\\', '/');
            var result = Scan(File.ReadAllText(file), relative);
            violations.AddRange(result.Violations);
            if (result.CompliantMounts > 0)
            {
                compliantByFile[relative] = result.CompliantMounts;
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
        // Not vacuous: the one known mount (every outside-counsel widget resolves to it) was found and passed.
        Assert.Contains("src/client/external-spa/src/widgets/GridWidgetBody.tsx", compliantByFile.Keys);
    }

    // ── Controls: the detector fires on each violation shape and is silent on the sanctioned one ──

    private const string SpaarkeImport = "import { DataGrid } from '@spaarke/ui-components/components/DataGrid/DataGrid';\n";

    [Fact(DisplayName = "Control: a mount without the prop is reported")]
    public void Scan_WhenTheSharedDataGridIsMountedWithoutTheProp_ReportsIt()
    {
        var result = Scan(SpaarkeImport + "const A = () => <DataGrid configId={id} dataverseClient={c} />;", "seeded.tsx");

        Assert.Contains("seeded.tsx:2", Assert.Single(result.Violations));
        Assert.Equal(0, result.CompliantMounts);
    }

    [Fact(DisplayName = "Control: showViewSelector={true} is reported")]
    public void Scan_WhenTheSelectorIsExplicitlyOn_ReportsIt()
    {
        var result = Scan(SpaarkeImport + "<DataGrid configId={id} showViewSelector={true} />", "seeded.tsx");

        Assert.Single(result.Violations);
    }

    [Fact(DisplayName = "Control: the sanctioned mount passes, including a multi-line tag with an arrow-function prop")]
    public void Scan_WhenTheSelectorIsOff_PassesAndCountsTheMount()
    {
        var text = SpaarkeImport +
                   "<DataGrid\n  configId={id}\n  onRecordsLoaded={rows => setRows(rows)}\n  showViewSelector={false}\n/>";

        var result = Scan(text, "ok.tsx");

        Assert.Empty(result.Violations);
        Assert.Equal(1, result.CompliantMounts);
    }

    [Fact(DisplayName = "Control: an aliased import is still checked")]
    public void Scan_WhenTheSharedDataGridIsImportedUnderAnAlias_ChecksTheAlias()
    {
        var text = "import {\n  DataGrid\n    as Grid,\n} from '@spaarke/ui-components';\n<Grid configId={id} />";

        Assert.Contains("<Grid>", Assert.Single(Scan(text, "alias.tsx").Violations));
    }

    [Fact(DisplayName = "Control: DataGridPageShell and namespace imports are refused")]
    public void Scan_WhenTheGridIsReachedThroughAShellOrANamespace_ReportsIt()
    {
        Assert.Contains("DataGridPageShell",
            Assert.Single(Scan("import { DataGridPageShell } from '@spaarke/ui-components';", "shell.tsx").Violations));
        Assert.Contains("namespace import",
            Assert.Single(Scan("import * as UI from '@spaarke/ui-components';\n<UI.DataGrid configId={id} />", "ns.tsx").Violations));
    }

    [Fact(DisplayName = "Control: a commented-out mount is ignored; a real one after a URL string is still checked")]
    public void Scan_WhenAMountIsOnlyInAComment_IgnoresItButStillChecksCodeAfterAUrl()
    {
        var text = SpaarkeImport +
                   "/**\n * Example: <DataGrid configId={id} />\n */\n" +
                   "// <DataGrid configId={id} />\n" +
                   "const u = 'https://x.test'; const A = () => <DataGrid configId={id} />;";

        var result = Scan(text, "comments.tsx");

        Assert.Contains("comments.tsx:6", Assert.Single(result.Violations));
    }

    [Fact(DisplayName = "Control: Fluent's own DataGrid (no view picker) is ignored")]
    public void Scan_WhenFluentsDataGridIsMounted_IgnoresIt()
    {
        var text = "import { DataGrid, DataGridHeader } from '@fluentui/react-components';\n" +
                   "<DataGrid items={rows} columns={cols}><DataGridHeader /></DataGrid>";

        var result = Scan(text, "fluent.tsx");

        Assert.Empty(result.Violations);
        Assert.Equal(0, result.CompliantMounts);
    }
}
