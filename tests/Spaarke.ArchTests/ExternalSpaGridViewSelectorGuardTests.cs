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
/// <b>What it scans.</b> Every script file under <c>src/client/external-spa/src</c>. The shared library is
/// any module specifier that names <c>@spaarke/ui-components</c> or <c>Spaarke.UI.Components</c> ANYWHERE
/// (the package alias, a path into the library's source, or a <c>…/node_modules/@spaarke/ui-components/…</c>
/// path). Fluent's own <c>DataGrid</c> (from <c>@fluentui/react-components</c>) has no view picker and is
/// ignored.
/// </para>
/// <para><b>Fail closed.</b> The rules are written so that a shape the scan cannot attribute is REFUSED, not
/// passed (task 157 review round 1 seeded nine shapes that the first version let through; review round 2
/// seeded five more):</para>
/// <list type="number">
/// <item>A grid binding is ANY import of <c>DataGrid</c> / <c>DataGridDefault</c> from the shared library —
/// named, aliased, <c>{ default as X }</c>, default import, or mixed default + named — where a default import
/// counts when the module's last path segment is <c>DataGrid</c>. A mount is <c>&lt;X</c> followed by
/// anything that cannot continue the identifier, so <c>&lt;X&lt;T&gt;</c>, <c>&lt;X{...p}</c> and
/// <c>&lt;X.Y</c> are mounts too. Every mount must pass <c>showViewSelector={false}</c> as its LAST top-level
/// <c>showViewSelector</c> attribute (a bare <c>showViewSelector</c> counts as a later <c>true</c>), with no
/// attribute spread after it (a later <c>{...rest}</c> could switch it back on).</item>
/// <item>A mount whose opening tag the scan cannot walk with certainty is refused: type arguments
/// (<c>&lt;X&lt;T&gt;</c>), a string attribute holding <c>{ } &lt; &gt;</c>, a string or template literal
/// holding <c>{ }</c> inside a prop expression, a <c>/</c> (comment, regex, division, nested closing tag)
/// anywhere in the tag except its closing <c>/&gt;</c>, a bare JSX element as an attribute value, an
/// unbalanced <c>}</c>, or a tag that never closes. Hoist such expressions to a const above the JSX.</item>
/// <item>A binding may appear only as a JSX tag (<c>&lt;X</c>, <c>&lt;/X</c>), after <c>typeof</c>, or as a
/// member name (<c>.X</c>). Any other reference — <c>React.createElement(X, …)</c>, <c>const G = X</c>,
/// <c>export { X }</c>, <c>export default X</c>, <c>memo(X)</c> — lets the grid leave this file unchecked and
/// is refused.</item>
/// <item><c>DataGridPageShell</c> / <c>DataGridPageShellDefault</c> (by name, by <c>default as</c>, or by
/// default import of a <c>…/DataGridPageShell</c> module) is refused: it mounts the shared grid itself and
/// cannot switch the picker off.</item>
/// <item>Namespace imports, re-exports that could carry the grid (<c>export *</c>, a grid name, <c>default</c>,
/// or a module path naming <c>DataGrid</c>), any dynamic <c>import()</c> / <c>require()</c> of the shared
/// library (<c>React.lazy</c>), and any import clause the scan cannot parse are refused.</item>
/// </list>
/// <para>
/// <b>Comments are NOT stripped.</b> Without a parser the scan cannot tell a comment from JSX text: inside JSX
/// children a line reading <c>// &lt;X configId="x" /&gt;</c> or <c>/* &lt;X /&gt; */</c> is TEXT followed by a
/// live element, and a <c>{…}</c> on such a line is a live expression (review round 2, S2/S3). So every
/// comment is scanned as code. The cost is fail-closed: in a file that imports the grid, a comment must not
/// quote a mount or name the binding (write "the shared grid" instead) — GridWidgetBody's JSDoc is worded
/// that way.
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

    private static readonly string[] ScriptExtensions = [".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".mts", ".cts"];

    /// <summary>The shared grid itself: a binding whose JSX mounts are checked.</summary>
    private static readonly HashSet<string> GridNames = new(StringComparer.Ordinal) { "DataGrid", "DataGridDefault" };

    /// <summary>The shared shell that mounts the grid with the picker on and no way to switch it off.</summary>
    private static readonly HashSet<string> ShellNames = new(StringComparer.Ordinal) { "DataGridPageShell", "DataGridPageShellDefault" };

    // A module specifier that resolves into the shared UI library: one that names the package alias or the
    // library's folder ANYWHERE — `@spaarke/ui-components/…`, `../../shared/Spaarke.UI.Components/…`, or
    // `../../node_modules/@spaarke/ui-components/…` (review round 2, S5: a start-anchored alias missed the last).
    private const string SharedModule =
        @"(?<module>[^'""`\n]*(?i:@spaarke/ui-components|Spaarke\.UI\.Components)[^'""`\n]*)";

    // `import <clause> from '<shared>'`. The clause starts at a name, `{` or `*` (so `import.meta` and
    // `import(` never match) and cannot cross a quote or `;`, so it cannot swallow a neighbouring statement.
    private static readonly Regex StaticImport = new(
        @"\bimport\b\s*(?<clause>[\w{*][^;'""`]*?)\s*\bfrom\s*['""]" + SharedModule + @"['""]",
        RegexOptions.Compiled);

    private static readonly Regex ReExport = new(
        @"\bexport\b\s*(?<clause>[\w{*][^;'""`]*?)\s*\bfrom\s*['""]" + SharedModule + @"['""]",
        RegexOptions.Compiled);

    private static readonly Regex DynamicLoad = new(
        @"\b(?:import|require)\s*\(\s*['""`]" + SharedModule + @"['""`]", RegexOptions.Compiled);

    // `[type] [Default] [, ] [{ names } | * as NS]`
    private static readonly Regex ImportClause = new(
        @"^(?:type\s+)?(?:(?<default>[\w$]+)\s*(?:,\s*|$))?(?:\{(?<names>[^}]*)\}|\*\s*as\s+(?<ns>[\w$]+))?$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // One import specifier: `DataGrid`, `type DataGridProps`, `DataGrid as Grid`, `default as Grid`.
    private static readonly Regex ImportSpecifier = new(
        @"^\s*(?:type\s+)?(?<imported>[\w$]+)(?:\s+as\s+(?<local>[\w$]+))?\s*$", RegexOptions.Compiled);

    private static readonly Regex GridNameInClause = new(@"(?<![\w$])(?:DataGrid|DataGridDefault|DataGridPageShell|DataGridPageShellDefault|default)(?![\w$])", RegexOptions.Compiled);

    // Anchored (\G) probes used while walking an opening tag at brace depth 0. SelectorProp matches the
    // attribute with OR without a value (a bare `showViewSelector` is `true`), and not a namespaced or
    // hyphenated neighbour (`x:showViewSelector`, `data-showViewSelector`) — those are different props.
    private static readonly Regex SelectorProp = new(@"\G(?<![\w$.:-])showViewSelector(?![\w$:-])", RegexOptions.Compiled);
    private static readonly Regex SelectorOffAt = new(@"\GshowViewSelector\s*=\s*\{\s*false\s*\}", RegexOptions.Compiled);
    private static readonly Regex SpreadAt = new(@"\G\{\s*\.\.\.", RegexOptions.Compiled);

    /// <summary>The result of scanning one file: every violation, and how many sanctioned mounts it holds.</summary>
    internal sealed record ScanResult(IReadOnlyList<string> Violations, int CompliantMounts);

    /// <summary>Scans one file's text. Pure, so the controls below exercise exactly the production detector.</summary>
    internal static ScanResult Scan(string source, string fileName)
    {
        // Comments are scanned as code: a line-leading `//` or `/*` inside JSX children is text, not a comment.
        var text = source;
        var violations = new List<string>();
        var localNames = new List<string>();
        // Import / re-export / dynamic-load statements: their module paths name `DataGrid` and are not references.
        var importSpans = new List<(int Start, int End)>();

        string Where(int index) => $"{fileName}:{LineOf(text, index)}";

        foreach (Match import in StaticImport.Matches(text))
        {
            importSpans.Add((import.Index, import.Index + import.Length));
            var module = import.Groups["module"].Value;
            var clause = ImportClause.Match(import.Groups["clause"].Value.Trim());
            if (!clause.Success)
            {
                violations.Add($"{Where(import.Index)}: import clause from '{module}' could not be parsed. Import "
                               + "from the shared library with a plain named import so DataGrid mounts can be checked.");
                continue;
            }

            if (clause.Groups["ns"].Success)
            {
                // `import * as UI from '@spaarke/ui-components'` then `<UI.DataGrid>` would mount the grid under a
                // name this scan cannot attribute.
                violations.Add($"{Where(import.Index)}: namespace import of '{module}'. Import from the shared "
                               + "library by name so every DataGrid mount can be checked for showViewSelector={false}.");
            }

            if (clause.Groups["default"].Success)
            {
                Classify(DefaultExportOf(module), clause.Groups["default"].Value, import.Index);
            }

            if (clause.Groups["names"].Success)
            {
                foreach (var raw in clause.Groups["names"].Value.Split(','))
                {
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        continue; // trailing comma
                    }
                    var specifier = ImportSpecifier.Match(raw);
                    if (!specifier.Success)
                    {
                        violations.Add($"{Where(import.Index)}: import specifier '{raw.Trim()}' from '{module}' could "
                                       + "not be parsed.");
                        continue;
                    }
                    var imported = specifier.Groups["imported"].Value;
                    var local = specifier.Groups["local"].Success ? specifier.Groups["local"].Value : imported;
                    Classify(imported == "default" ? DefaultExportOf(module) : imported, local, import.Index);
                }
            }
        }

        foreach (Match reExport in ReExport.Matches(text))
        {
            importSpans.Add((reExport.Index, reExport.Index + reExport.Length));
            var clause = reExport.Groups["clause"].Value.Trim();
            var module = reExport.Groups["module"].Value;
            if (clause.StartsWith("type ", StringComparison.Ordinal) || clause.StartsWith("type{", StringComparison.Ordinal))
            {
                continue; // a type-only re-export cannot carry a mountable component
            }
            if (clause.StartsWith('*') || GridNameInClause.IsMatch(clause)
                || module.Contains("DataGrid", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add($"{Where(reExport.Index)}: re-exports '{clause}' from '{module}'. A local re-export "
                               + "lets another file mount the shared DataGrid under a name this scan cannot attribute.");
            }
        }

        foreach (Match load in DynamicLoad.Matches(text))
        {
            importSpans.Add((load.Index, load.Index + load.Length));
            violations.Add($"{Where(load.Index)}: dynamic import/require of '{load.Groups["module"].Value}' "
                           + "(e.g. React.lazy). Import the shared library statically by name so every DataGrid mount "
                           + "can be checked.");
        }

        var compliant = 0;
        foreach (var local in localNames.Distinct(StringComparer.Ordinal))
        {
            var name = Regex.Escape(local);

            // A mount is `<X` followed by anything that cannot continue the identifier — including `<` (type
            // arguments, review round 2 S1), `{` (a spread with no space) and `.` (a member tag).
            var mountPattern = new Regex($@"<\s*{name}(?![\w$])");
            foreach (Match mount in mountPattern.Matches(text))
            {
                var verdict = AnalyzeOpeningTag(text, mount.Index + mount.Length);
                if (verdict.Uncheckable is { } reason)
                {
                    violations.Add($"{Where(mount.Index)}: <{local}> mount cannot be checked: {reason}. Write the tag "
                                   + "plainly (hoist complex prop values to a const above the JSX) so the scan can see "
                                   + "showViewSelector={false}.");
                }
                else if (verdict.TurnsOff)
                {
                    compliant++;
                }
                else
                {
                    violations.Add($"{Where(mount.Index)}: <{local}> mounted without a final top-level "
                                   + "showViewSelector={false} (missing, not false, nested, overridden by a later "
                                   + "showViewSelector or {...spread}). The shared DataGrid would offer the entity's "
                                   + "internal MDA views, and the server column allow-list (task 157) does not admit "
                                   + "their columns.");
                }
            }

            // Any reference that is not a JSX tag, a `typeof`, or a member name lets the binding escape this file.
            var escapePattern = new Regex(
                $@"(?<![\w$])(?<!<\s*)(?<!</\s*)(?<!\btypeof\s+)(?<!(?<!\.)\.){name}(?![\w$])");
            foreach (Match reference in escapePattern.Matches(text))
            {
                if (importSpans.Any(span => reference.Index >= span.Start && reference.Index < span.End))
                {
                    continue;
                }
                violations.Add($"{Where(reference.Index)}: '{local}' (the shared DataGrid) is referenced outside a JSX "
                               + "tag (createElement, aliasing, re-export, HOC …). Mount it only as JSX in this file, "
                               + "with showViewSelector={false}.");
            }
        }

        return new ScanResult(violations, compliant);

        void Classify(string exported, string local, int index)
        {
            if (GridNames.Contains(exported))
            {
                localNames.Add(local);
            }
            else if (ShellNames.Contains(exported))
            {
                violations.Add($"{Where(index)}: imports DataGridPageShell (as '{local}'), which mounts the shared "
                               + "DataGrid with its default view selector and cannot switch it off. Mount DataGrid "
                               + "directly with showViewSelector={false}.");
            }
        }
    }

    /// <summary>
    /// What a default import of <paramref name="module"/> binds, by the module's last path segment
    /// (<c>…/DataGrid/DataGrid</c> → <c>DataGrid</c>, <c>…/DataGrid/DataGridPageShell.tsx</c> →
    /// <c>DataGridPageShell</c>, <c>…/DataGrid/index</c> → <c>DataGrid</c>).
    /// </summary>
    private static string DefaultExportOf(string module)
    {
        var segments = module.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        var last = Path.GetFileNameWithoutExtension(segments[^1]);
        if (last.Equals("index", StringComparison.OrdinalIgnoreCase) && segments.Length > 1)
        {
            last = segments[^2];
        }
        if (last.Equals("DataGrid", StringComparison.OrdinalIgnoreCase))
        {
            return "DataGrid";
        }
        return last.Equals("DataGridPageShell", StringComparison.OrdinalIgnoreCase) ? "DataGridPageShell" : last;
    }

    private static int LineOf(string text, int index) => text[..index].Count(c => c == '\n') + 1;

    /// <summary>The walk of one opening tag: whether it turns the picker off, or why it cannot be checked.</summary>
    private readonly record struct TagVerdict(bool TurnsOff, string? Uncheckable);

    private static TagVerdict Refuse(string reason) => new(false, reason);

    private const string BraceOrAngle = "{}<>";

    /// <summary>
    /// Walks the opening tag whose element name ends at <paramref name="nameEnd"/>, up to its closing
    /// <c>&gt;</c> at brace depth 0 (so an arrow function's <c>=&gt;</c> inside a prop does not end it early).
    /// The tag turns the picker off when its LAST top-level <c>showViewSelector</c> attribute is <c>{false}</c>
    /// and no attribute spread follows it; a <c>showViewSelector</c> inside a prop expression (depth &gt; 0)
    /// belongs to some other element and does not count. String literals are skipped as strings, so a brace or
    /// <c>&gt;</c> inside one cannot shift the depth or end the tag (review round 2, S4). Anything the walk cannot
    /// follow with certainty is refused rather than guessed.
    /// </summary>
    private static TagVerdict AnalyzeOpeningTag(string text, int nameEnd)
    {
        var next = nameEnd;
        while (next < text.Length && char.IsWhiteSpace(text[next]))
        {
            next++;
        }
        if (next < text.Length && text[next] == '<')
        {
            return Refuse("type arguments (<X<T>>) — the scan cannot tell where they end");
        }

        var depth = 0;
        var lastProp = -1;
        var spreadAfterLastProp = false;
        for (var i = nameEnd; i < text.Length; i++)
        {
            var c = text[i];
            if (depth == 0)
            {
                switch (c)
                {
                    case '>':
                        return new TagVerdict(
                            lastProp >= 0 && !spreadAfterLastProp && SelectorOffAt.IsMatch(text, lastProp), null);
                    case '<':
                        return Refuse("a bare JSX element as an attribute value");
                    case '/':
                    {
                        var j = i + 1;
                        while (j < text.Length && char.IsWhiteSpace(text[j]))
                        {
                            j++;
                        }
                        if (j < text.Length && text[j] == '>')
                        {
                            i = j - 1; // the self-closing `/>`; the next iteration ends the tag
                            continue;
                        }
                        return Refuse("a comment or '/' between attributes");
                    }
                    case '"' or '\'':
                    {
                        // A JSX attribute string: no escapes, ends at the same quote.
                        var close = text.IndexOf(c, i + 1);
                        if (close < 0)
                        {
                            return Refuse("an unterminated string attribute");
                        }
                        if (text.AsSpan(i + 1, close - i - 1).IndexOfAny(BraceOrAngle) >= 0)
                        {
                            return Refuse("a string attribute holding { } < or >");
                        }
                        i = close;
                        continue;
                    }
                    case '}':
                        return Refuse("an unbalanced }");
                }

                if (SelectorProp.IsMatch(text, i))
                {
                    lastProp = i;
                    spreadAfterLastProp = false;
                }
                else if (lastProp >= 0 && SpreadAt.IsMatch(text, i))
                {
                    spreadAfterLastProp = true;
                }
                if (c == '{')
                {
                    depth++;
                }
            }
            else
            {
                switch (c)
                {
                    case '"' or '\'' or '`':
                    {
                        // A JS string or template literal inside a prop expression: skip it, honouring escapes.
                        var j = i + 1;
                        var holdsBrace = false;
                        while (j < text.Length && text[j] != c)
                        {
                            if (text[j] == '\\')
                            {
                                j++;
                            }
                            else if (text[j] is '{' or '}')
                            {
                                holdsBrace = true;
                            }
                            j++;
                        }
                        if (j >= text.Length)
                        {
                            return Refuse("an unterminated string inside a prop expression");
                        }
                        if (holdsBrace)
                        {
                            return Refuse("a string or template literal holding { or } inside a prop expression");
                        }
                        i = j;
                        continue;
                    }
                    case '/':
                        return Refuse("a '/' (comment, regex, division or nested closing tag) inside a prop expression");
                    case '{':
                        depth++;
                        break;
                    case '}':
                        depth--;
                        break;
                }
            }
        }
        return Refuse("the opening tag never closes");
    }

    private static IEnumerable<string> ExternalSpaFiles() =>
        Directory.EnumerateFiles(ExternalSpaSource, "*.*", SearchOption.AllDirectories)
            .Where(f => ScriptExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
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

    [Fact(DisplayName = "Control: a mount inside a comment is reported (inside JSX children a // or /* line is text)")]
    public void Scan_WhenAMountIsQuotedInAComment_ReportsIt()
    {
        var text = SpaarkeImport +
                   "/**\n * Example: <DataGrid configId={id} />\n */\n" +
                   "// <DataGrid configId={id} />\n" +
                   "const u = 'https://x.test'; const A = () => <DataGrid configId={id} />;";

        var result = Scan(text, "comments.tsx");

        Assert.Equal(3, result.Violations.Count);
        Assert.Contains(result.Violations, v => v.StartsWith("comments.tsx:3", StringComparison.Ordinal));
        Assert.Contains(result.Violations, v => v.StartsWith("comments.tsx:5", StringComparison.Ordinal));
        Assert.Contains(result.Violations, v => v.StartsWith("comments.tsx:6", StringComparison.Ordinal));
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

    // ── Review round 1 (2026-10-02): the shapes the first version let through. Each one MUST be reported. ──

    private const string GridModule = "'@spaarke/ui-components/components/DataGrid/DataGrid'";
    private const string Bad = " configId={id} />";

    [Theory(DisplayName = "Control: every evasion shape seeded in review round 1 is reported")]
    // A: default import (DataGrid.tsx has `export default DataGrid`)
    [InlineData("A-default", "import DataGrid from " + GridModule + ";\n<DataGrid" + Bad, "<DataGrid>")]
    // B: the barrel's DataGridDefault alias
    [InlineData("B-barrel-default-alias", "import { DataGridDefault } from '@spaarke/ui-components/components/DataGrid';\n<DataGridDefault" + Bad, "<DataGridDefault>")]
    // C: mixed default + named import
    [InlineData("C-mixed", "import Grid, { type DataGridProps } from " + GridModule + ";\n<Grid" + Bad, "<Grid>")]
    // C2: `{ default as X }`
    [InlineData("C2-default-as", "import { default as Grid } from " + GridModule + ";\n<Grid" + Bad, "<Grid>")]
    // D: a later spread could switch the picker back on
    [InlineData("D-spread-after", SpaarkeImport + "<DataGrid configId={id} showViewSelector={false} {...rest} />", "<DataGrid>")]
    // D2: a later showViewSelector wins in JSX
    [InlineData("D2-later-prop-wins", SpaarkeImport + "<DataGrid showViewSelector={false} showViewSelector={true} />", "<DataGrid>")]
    // D3: the prop on a NESTED element does not count for this mount
    [InlineData("D3-nested-only", SpaarkeImport + "<DataGrid configId={id} empty={<X showViewSelector={false} />} />", "<DataGrid>")]
    // E: the barrel's DataGridPageShellDefault alias, and a default import of the shell module
    [InlineData("E-shell-default-alias", "import { DataGridPageShellDefault } from '@spaarke/ui-components/components/DataGrid';", "DataGridPageShell")]
    [InlineData("E2-shell-default-import", "import Shell from '@spaarke/ui-components/components/DataGrid/DataGridPageShell';", "DataGridPageShell")]
    // F: a re-export from the shared library, and a two-step local re-export
    [InlineData("F-reexport", "export { DataGrid } from '@spaarke/ui-components/components/DataGrid';", "re-exports")]
    [InlineData("F2-reexport-star", "export * from '@spaarke/ui-components';", "re-exports")]
    [InlineData("F3-local-reexport", SpaarkeImport + "export { DataGrid };", "outside a JSX tag")]
    [InlineData("F4-export-default", SpaarkeImport + "export default DataGrid;", "outside a JSX tag")]
    // G: createElement / aliasing
    [InlineData("G-createElement", SpaarkeImport + "React.createElement(DataGrid, { configId: id });", "outside a JSX tag")]
    [InlineData("G2-alias-variable", SpaarkeImport + "const G = DataGrid;\n<G configId={id} />", "outside a JSX tag")]
    // H: React.lazy over a dynamic import
    [InlineData("H-lazy", "const G = React.lazy(() => import(" + GridModule + "));", "dynamic import")]
    [InlineData("H2-require", "const { DataGrid } = require(" + GridModule + ");", "dynamic import")]
    // A path into the shared library's source instead of the package alias
    [InlineData("I-relative-path", "import { DataGrid } from '../../../shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid';\n<DataGrid" + Bad, "<DataGrid>")]
    // An import clause the scan cannot parse fails closed
    [InlineData("J-unparsed", "import DataGrid, Other from " + GridModule + ";", "could not be parsed")]
    public void Scan_WhenAnEvasionShapeIsSeeded_ReportsIt(string shape, string source, string expected)
    {
        var result = Scan(source, shape + ".tsx");

        Assert.Contains(result.Violations, v => v.Contains(expected, StringComparison.Ordinal));
        Assert.Equal(0, result.CompliantMounts);
    }

    // ── Review round 2 (2026-10-02): five more shapes compiled (esbuild) into a live mount with the picker on,
    // plus the neighbours the string-aware tag walk now refuses. Each one MUST be reported. ──

    private const string JsxOpen = "const A = () => (\n  <div>\n";
    private const string JsxClose = "  </div>\n);";

    [Theory(DisplayName = "Control: every evasion shape seeded in review round 2 is reported")]
    // S1: a generic JSX element — the old mount regex needed whitespace, / or > after the name
    [InlineData("S1-generic", SpaarkeImport + "const A = () => <DataGrid<any> configId=\"x\" />;", "type arguments")]
    [InlineData("S1b-generic-with-prop", SpaarkeImport + "<DataGrid<any> showViewSelector={false} />", "type arguments")]
    // S2 / S3: inside JSX children a line-leading // or /* is TEXT, and the element after it renders
    [InlineData("S2-jsx-text-slashes", SpaarkeImport + JsxOpen + "    // <DataGrid configId=\"x\" />\n" + JsxClose, "<DataGrid>")]
    [InlineData("S3-jsx-text-block", SpaarkeImport + JsxOpen + "    /* <DataGrid configId=\"x\" /> */\n" + JsxClose, "<DataGrid>")]
    [InlineData("S2b-jsx-text-expression", SpaarkeImport + JsxOpen + "    // {React.createElement(DataGrid, { configId: id })}\n" + JsxClose, "outside a JSX tag")]
    // S4: a brace inside a string attribute shifted the depth, hiding the later spread
    [InlineData("S4-brace-in-string", SpaarkeImport + "<DataGrid showViewSelector={false} configId=\"{\" {...p} />", "string attribute holding")]
    [InlineData("S4b-gt-in-string", SpaarkeImport + "<DataGrid showViewSelector={false} title=\">\" {...p} />", "string attribute holding")]
    [InlineData("S4c-brace-in-js-string", SpaarkeImport + "<DataGrid showViewSelector={false} configId={\"}\"} {...p} />", "string or template literal")]
    [InlineData("S4d-template-brace", SpaarkeImport + "<DataGrid showViewSelector={false} configId={`${a}`} {...p} />", "string or template literal")]
    [InlineData("S4e-comment-between-attributes", SpaarkeImport + "<DataGrid showViewSelector={false} /* > */ {...p} />", "comment or '/'")]
    [InlineData("S4f-bare-jsx-attribute-value", SpaarkeImport + "<DataGrid showViewSelector={false} empty=<X/> {...p} />", "bare JSX element")]
    [InlineData("S4g-unterminated-tag", SpaarkeImport + "<DataGrid showViewSelector={false}", "never closes")]
    // A bare `showViewSelector` after the false one is `true`; a namespaced neighbour is a different prop
    [InlineData("S4h-bare-prop-later", SpaarkeImport + "<DataGrid showViewSelector={false} showViewSelector />", "<DataGrid> mounted without")]
    [InlineData("S4i-namespaced-prop", SpaarkeImport + "<DataGrid x:showViewSelector={false} />", "<DataGrid> mounted without")]
    // A spread straight after the name (no whitespace) was not read as a mount
    [InlineData("S4j-spread-no-space", SpaarkeImport + "<DataGrid{...p} />", "<DataGrid> mounted without")]
    // S5: a node_modules-relative path to the shared library
    [InlineData("S5-node-modules-path", "import { DataGrid } from '../../node_modules/@spaarke/ui-components/src/components/DataGrid/DataGrid';\n<DataGrid" + Bad, "<DataGrid>")]
    public void Scan_WhenARound2EvasionShapeIsSeeded_ReportsIt(string shape, string source, string expected)
    {
        var result = Scan(source, shape + ".tsx");

        Assert.Contains(result.Violations, v => v.Contains(expected, StringComparison.Ordinal));
        Assert.Equal(0, result.CompliantMounts);
    }

    [Fact(DisplayName = "Control: a /* or // inside a string does not hide the code after it (fail closed)")]
    public void Scan_WhenAStringHoldsCommentMarkers_StillChecksTheCodeAfterIt()
    {
        var text = SpaarkeImport +
                   "const glob = 'src/**/*.ts';\n" +
                   "const A = () => <DataGrid configId={id} />;\n" +
                   "const z = 'end */';\n" +
                   "const s = 'a // b'; const B = () => <DataGrid configId={id} />;";

        var result = Scan(text, "strings.tsx");

        Assert.Equal(2, result.Violations.Count);
        Assert.Contains(result.Violations, v => v.StartsWith("strings.tsx:3", StringComparison.Ordinal));
        Assert.Contains(result.Violations, v => v.StartsWith("strings.tsx:5", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Control: sanctioned neighbours pass — spread BEFORE the prop, closing tag, typeof, type-only re-export, plain string and arrow props")]
    public void Scan_WhenOnlySanctionedShapesArePresent_Passes()
    {
        var text = "import DataGrid, { type DataGridProps } from " + GridModule + ";\n" +
                   "export type { DataGridProps } from '@spaarke/ui-components/components/DataGrid';\n" +
                   "type P = React.ComponentProps<typeof DataGrid>;\n" +
                   "const A = () => (\n  <div>\n" +
                   "    <DataGrid {...base} configId={id} showViewSelector={false}></DataGrid>\n" +
                   "    <DataGrid aria-label=\"Matters & invoices\" onRecordsLoaded={rows => setRows(rows.map(r => ({ ...r, k: 'a' })))}\n" +
                   "      showViewSelector={ false } / >\n  </div>\n);";

        var result = Scan(text, "sanctioned.tsx");

        Assert.Empty(result.Violations);
        Assert.Equal(2, result.CompliantMounts);
    }
}
