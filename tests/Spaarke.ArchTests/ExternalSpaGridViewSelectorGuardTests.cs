using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 157 (owner round 4 item 7, 2026-10-01) — the external SPA never shows the
/// shared Spaarke <c>DataGrid</c>'s view picker.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a security invariant, not a UI preference.</b> The external module read seam
/// (<c>ExternalModuleDataEndpoints</c>) admits only the columns on each module's allow-list
/// (<c>ExternalAccessModule.cs</c>, task 134 defect C6). Task 157 shrank those lists to what each grid's own
/// <c>sprk_gridconfiguration</c> shows, plus the scope and <c>/record</c> default columns. That is correct
/// only while the grid cannot switch to another query. The shared <c>DataGrid</c> defaults
/// <c>showViewSelector</c> to <c>true</c> and then offers every INTERNAL MDA main view of the entity
/// (<c>/savedqueries/{entity}</c>). A mount that leaves the picker on brings it back, and every sibling view
/// errors with <c>DV_FETCHXML_COLUMN_NOT_PERMITTED</c>. The tempting "fix" for that error is to widen the
/// server allow-list again, which re-exposes internal columns (<c>ownerid</c>, <c>sprk_assignedto</c>,
/// <c>sprk_visibilitystate</c> …) to outside counsel. This guard stops the first step. (The BFF allow-list
/// is the data control and fails closed with a 400 on its own; this guard keeps the client from creating
/// the pressure to widen it.)
/// </para>
/// <para>
/// <b>Two layers (review round 3).</b> A regex scan cannot prove that no JavaScript expression ever
/// re-enables a prop — the verifier compiled <c>import.meta.glob</c>, a unicode-escaped identifier and
/// <c>React.cloneElement</c> past the round-2 scan. So the guarantee now rests on a RUNTIME guard plus a
/// scan that only has to prove a narrow, checkable fact:
/// </para>
/// <list type="number">
/// <item><b>Runtime:</b> <c>src/client/external-spa/src/widgets/ExternalDataGrid.tsx</c> mounts the shared grid
/// with <c>showViewSelector={false}</c> AFTER the caller's props, so no caller prop, spread,
/// <c>cloneElement</c> or <c>createElement</c> of <c>ExternalDataGrid</c> can turn the picker on. Its text is
/// PINNED (whitespace-insensitive) by <see cref="ExternalSpa_GridWrapper_IsPinnedAndForcesThePickerOff"/>.</item>
/// <item><b>Scan:</b> no OTHER file under <c>src/client/external-spa/src</c> may reach the shared grid. The rules
/// are written to refuse what they cannot attribute, but a regex scan is not a proof — three review rounds
/// each found a compiled bypass of the previous version. So the scan's job is narrow (keep every path to the
/// grid going through the pinned wrapper), and the known residuals are listed below.</item>
/// </list>
/// <para><b>Scan rules</b> (every script file under <c>src/client/external-spa/src</c>):</para>
/// <list type="number">
/// <item>A grid binding is ANY import of <c>DataGrid</c> / <c>DataGridDefault</c> from the shared library —
/// named, aliased, <c>{ default as X }</c>, default import of a <c>…/DataGrid</c> module, or mixed. The shared
/// library is any specifier naming <c>@spaarke/ui-components</c> or <c>Spaarke.UI.Components</c>. Only the
/// wrapper may hold a grid binding.</item>
/// <item>In a file holding a grid binding: every mount must end with a top-level
/// <c>showViewSelector={false}</c> and no spread after it (rounds 1–2: generics, string braces, comments,
/// nested elements … are refused); the binding may appear only as a JSX tag, after <c>typeof</c>, or as a
/// member name; <c>cloneElement</c>, <c>createElement</c> and any <c>\u</c> escape are refused (round 3,
/// V2/V3).</item>
/// <item><c>DataGridPageShell(Default)</c>, namespace imports and re-exports that could carry the grid are
/// refused.</item>
/// <item><b>Module specifiers (round 3).</b> Every <c>from '…'</c> must be a plain string (no <c>\</c>
/// escape, which would spell the library without naming it). One that names the shared library must belong
/// to an import or re-export the scan parsed (so <c>import { "DataGrid" as G }</c> cannot hide). A relative,
/// <c>/</c> or <c>@/</c> specifier must resolve inside the external SPA source or into the shared library;
/// a bare <c>@spaarke/…</c> specifier must be the shared library. A module elsewhere could re-export the grid
/// under any name.</item>
/// <item><b>Dynamic loads (round 3).</b> Every <c>import(…)</c> / <c>require(…)</c> must take one plain string
/// literal that resolves inside the external SPA source. <c>import.meta.glob</c> is refused in any form: its
/// pattern can match the grid's file without naming it (V1).</item>
/// </list>
/// <para><b>Shared-library fan-in (round 3).</b> The scan refuses <c>DataGridPageShell</c> by name, which is
/// only enough while it is the one shared component that mounts the grid.
/// <see cref="SharedLibrary_OnlyKnownModulesCarryTheGrid"/> pins that: inside the shared library (tests
/// excluded) only <c>DataGrid/index.ts</c>, <c>DataGrid/DataGridPageShell.tsx</c>, <c>components/index.ts</c>
/// and <c>src/index.ts</c> may import or re-export the grid. A new shared component that embeds it fails that
/// fact until it is reviewed (refuse it here by name, like the shell, or prove it switches the picker off).</para>
/// <para><b>Comments are NOT stripped.</b> Inside JSX children a line reading <c>// &lt;X /&gt;</c> is TEXT
/// followed by a live element (round 2, S2/S3), so every comment is scanned as code. In a file that holds a
/// grid binding, a comment must not quote a mount, name the binding, or spell an element-cloning API.</para>
/// <para><b>Residual (documented, not scanned).</b> (1) A NEW npm dependency (a <c>package.json</c> change) that
/// itself bundles and re-exports the shared grid under another name. (2) Inside the shared library, a dynamic
/// <c>import()</c> whose path is built in a variable elsewhere (the fan-in fact reads the call's own argument
/// text only, because the library's prose says "import (" in many comments). (3) Edits to the shared
/// library's own <c>DataGrid.tsx</c> (e.g. ignoring the prop). All three are reviewed changes outside
/// <c>external-spa/src</c>, and the BFF allow-list still refuses every column the picker's views would
/// need.</para>
/// <para><b>Crude by design</b> (see <see cref="SourceScan"/>): regex over source, not a TypeScript parse. Each
/// rule is paired with a negative control proving it fires and a positive control proving it does not fire
/// on the sanctioned shape. ADR-038 Amendment A1: <c>tests/Spaarke.ArchTests/**</c> is a deletion-protected
/// KEEP path.</para>
/// <para><b>Maintenance.</b> If an external grid ever genuinely needs a view picker, do NOT relax this guard
/// alone: re-derive the module's server allow-list with task 134's rule (b) (sibling-view columns) in the same
/// change and have the owner approve the wider column exposure.</para>
/// </remarks>
public class ExternalSpaGridViewSelectorGuardTests
{
    private static string ExternalSpaRoot => Path.Combine(SourceScan.RepoRoot, "src", "client", "external-spa");

    private static string ExternalSpaSource => Path.Combine(ExternalSpaRoot, "src");

    private static string SharedLibraryRoot =>
        Path.Combine(SourceScan.RepoRoot, "src", "client", "shared", "Spaarke.UI.Components");

    private static string SharedLibrarySource => Path.Combine(SharedLibraryRoot, "src");

    /// <summary>The runtime guard: the ONLY external-SPA file allowed to import the shared grid.</summary>
    internal const string WrapperFile = "src/client/external-spa/src/widgets/ExternalDataGrid.tsx";

    /// <summary>
    /// The wrapper's text, pinned. Compared with ALL whitespace removed, so a formatter cannot break it, but
    /// any change to a token (a prop, a spread, the order of the two) fails the pin and must be reviewed here.
    /// </summary>
    private const string PinnedWrapperSource = """
        /**
         * ExternalDataGrid — the ONLY way the external SPA mounts the shared Spaarke grid
         * (unified-access-control-r2 task 157, owner round 4 item 7).
         *
         * It switches the view picker off at RUNTIME: `showViewSelector={false}` comes AFTER the caller's props,
         * so no caller prop, spread or cloned element can turn the picker back on. With the picker on, the grid
         * would offer the entity's internal MDA views, whose columns the BFF's external allow-lists
         * (ExternalAccessModule.cs) do not admit.
         *
         * ExternalSpaGridViewSelectorGuardTests refuses every other import of the shared grid under
         * src/client/external-spa/src and pins this file's text, so a change here is a change to that guard.
         */
        import * as React from 'react';
        import { DataGrid, type DataGridProps } from '@spaarke/ui-components/components/DataGrid/DataGrid';

        /** The shared grid's props without the view picker switch: an external grid never shows the picker. */
        export type ExternalDataGridProps = Omit<DataGridProps, 'showViewSelector'>;

        export const ExternalDataGrid: React.FC<ExternalDataGridProps> = props => (
          <DataGrid {...props} showViewSelector={false} />
        );
        """;

    private static readonly string[] ScriptExtensions = [".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs", ".mts", ".cts"];

    /// <summary>The shared grid itself: a binding whose JSX mounts are checked.</summary>
    private static readonly HashSet<string> GridNames = new(StringComparer.Ordinal) { "DataGrid", "DataGridDefault" };

    /// <summary>The shared shell that mounts the grid with the picker on and no way to switch it off.</summary>
    private static readonly HashSet<string> ShellNames = new(StringComparer.Ordinal) { "DataGridPageShell", "DataGridPageShellDefault" };

    // Names the shared UI library ANYWHERE — `@spaarke/ui-components/…`, `../../shared/Spaarke.UI.Components/…`,
    // or `../../node_modules/@spaarke/ui-components/…` (review round 2, S5).
    private const string SharedLibraryName = @"(?i:@spaarke/ui-components|Spaarke\.UI\.Components)";

    private const string SharedModule = @"(?<module>[^'""`\n]*" + SharedLibraryName + @"[^'""`\n]*)";

    private static readonly Regex NamesSharedLibrary = new(SharedLibraryName, RegexOptions.Compiled);

    // `import <clause> from '<shared>'`. The clause starts at a name, `{` or `*` (so `import.meta` and
    // `import(` never match) and cannot cross a quote or `;`, so it cannot swallow a neighbouring statement.
    private static readonly Regex StaticImport = new(
        @"\bimport\b\s*(?<clause>[\w{*][^;'""`]*?)\s*\bfrom\s*['""]" + SharedModule + @"['""]",
        RegexOptions.Compiled);

    private static readonly Regex ReExport = new(
        @"\bexport\b\s*(?<clause>[\w{*][^;'""`]*?)\s*\bfrom\s*['""]" + SharedModule + @"['""]",
        RegexOptions.Compiled);

    // Every `from '<specifier>'` in the file, whatever precedes it (round 3: the specifier rules).
    private static readonly Regex AnyFrom = new(@"\bfrom\s*(?<q>['""])(?<spec>[^'""\n]*)\k<q>", RegexOptions.Compiled);

    // Every `import(` / `require(`; the argument is then checked with DynamicLiteralArgAt.
    private static readonly Regex DynamicCall = new(@"\b(?:import|require)\s*\(", RegexOptions.Compiled);

    // One plain string literal (no escape, no template, no concatenation) and then `)` or `,`.
    private static readonly Regex DynamicLiteralArgAt = new(
        @"\G\s*(?<q>['""])(?<spec>[^'""`\n\\]*)\k<q>\s*[),]", RegexOptions.Compiled);

    // `import.meta.glob(…)`, `import.meta.globEager(…)`, `import . meta['glob']` … (round 3, V1).
    private static readonly Regex ImportMetaGlob = new(
        @"\bimport\s*\.\s*meta\s*(?:\.\s*|\[\s*['""`])glob", RegexOptions.Compiled);

    // Element-cloning APIs and identifier escapes, refused in a file that holds a grid binding (round 3, V2/V3).
    private static readonly Regex CloneOrCreate = new(@"(?<![\w$])(?:cloneElement|createElement)(?![\w$])", RegexOptions.Compiled);
    private static readonly Regex UnicodeEscape = new(@"\\u", RegexOptions.Compiled);

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

    /// <summary>
    /// Scans one file's text. <paramref name="fileName"/> is repo-relative with <c>/</c> separators: it decides
    /// whether the file is the wrapper and anchors relative specifiers. Pure, so the controls below exercise
    /// exactly the production detector.
    /// </summary>
    internal static ScanResult Scan(string source, string fileName)
    {
        // Comments are scanned as code: a line-leading `//` or `/*` inside JSX children is text, not a comment.
        var text = source;
        var violations = new List<string>();
        var localNames = new List<string>();
        var firstGridImport = -1; // where the first grid binding is imported (for the wrapper-only message)
        // Statements whose text names `DataGrid` in a module path, not as a reference.
        var importSpans = new List<(int Start, int End)>();
        // Parsed import / re-export statements from the shared library: their `from '…'` is attributed.
        var parsedSharedSpans = new List<(int Start, int End)>();
        var isWrapper = string.Equals(fileName, WrapperFile, StringComparison.Ordinal);

        string Where(int index) => $"{fileName}:{LineOf(text, index)}";

        foreach (Match import in StaticImport.Matches(text))
        {
            importSpans.Add((import.Index, import.Index + import.Length));
            parsedSharedSpans.Add((import.Index, import.Index + import.Length));
            var module = import.Groups["module"].Value;
            var clause = ImportClause.Match(import.Groups["clause"].Value.Trim());
            if (!clause.Success)
            {
                violations.Add($"{Where(import.Index)}: import clause from '{module}' could not be parsed. Import "
                               + "from the shared library with a plain named import so DataGrid imports can be checked.");
                continue;
            }

            if (clause.Groups["ns"].Success)
            {
                // `import * as UI from '@spaarke/ui-components'` then `<UI.DataGrid>` would mount the grid under a
                // name this scan cannot attribute.
                violations.Add($"{Where(import.Index)}: namespace import of '{module}'. Import from the shared "
                               + "library by name so every DataGrid import can be checked.");
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
            parsedSharedSpans.Add((reExport.Index, reExport.Index + reExport.Length));
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

        // ── Round 3: module specifiers. A module the scan does not see could re-export the grid under any name. ──
        foreach (Match fromClause in AnyFrom.Matches(text))
        {
            var spec = fromClause.Groups["spec"].Value;
            if (spec.Contains('\\'))
            {
                violations.Add($"{Where(fromClause.Index)}: escape in a module specifier ('{spec}'). An escape can spell the "
                               + "shared library without naming it; write the specifier plainly.");
                continue;
            }
            if (NamesSharedLibrary.IsMatch(spec))
            {
                if (!parsedSharedSpans.Any(span => fromClause.Index >= span.Start && fromClause.Index < span.End))
                {
                    violations.Add($"{Where(fromClause.Index)}: import from '{spec}' is not attributed to a parsed import or "
                                   + "re-export (e.g. a string-named specifier such as { \"DataGrid\" as G }). Import "
                                   + "from the shared library with plain identifiers.");
                }
                if (ResolvesOutsideSanctionedRoots(spec, fileName, allowSharedLibrary: true) is { } where)
                {
                    violations.Add($"{Where(fromClause.Index)}: '{spec}' names the shared library but resolves to {where}, "
                                   + "outside the external SPA source and the shared library.");
                }
                continue;
            }
            if (ResolvesOutsideSanctionedRoots(spec, fileName, allowSharedLibrary: false) is { } outside)
            {
                violations.Add($"{Where(fromClause.Index)}: imports '{spec}' ({outside}), a module outside the external SPA "
                               + "source. It could re-export the shared DataGrid under a name this scan cannot attribute.");
            }
        }

        // ── Round 3: dynamic loads. Only a plain local string literal is attributable. ──
        foreach (Match call in DynamicCall.Matches(text))
        {
            var arg = DynamicLiteralArgAt.Match(text, call.Index + call.Length);
            var end = arg.Success ? arg.Index + arg.Length : call.Index + call.Length;
            importSpans.Add((call.Index, end));
            if (!arg.Success)
            {
                violations.Add($"{Where(call.Index)}: dynamic import/require whose argument is not one plain string "
                               + "literal (a variable, template, concatenation or escape). Its target cannot be checked.");
                continue;
            }
            var spec = arg.Groups["spec"].Value;
            if (NamesSharedLibrary.IsMatch(spec)
                || ResolvesOutsideSanctionedRoots(spec, fileName, allowSharedLibrary: false) is not null)
            {
                violations.Add($"{Where(call.Index)}: dynamic import/require of '{spec}' (e.g. React.lazy). Only a "
                               + "module inside the external SPA source may be loaded dynamically; import the shared "
                               + "library statically by name so every DataGrid import can be checked.");
            }
        }

        foreach (Match glob in ImportMetaGlob.Matches(text))
        {
            violations.Add($"{Where(glob.Index)}: import.meta.glob is refused. Its pattern can match the shared "
                           + "DataGrid's file without naming it, so the grid it loads cannot be checked.");
        }

        var compliant = 0;
        var distinctLocals = localNames.Distinct(StringComparer.Ordinal).ToList();
        if (distinctLocals.Count > 0)
        {
            if (!isWrapper)
            {
                violations.Add($"{Where(firstGridImport)}: imports the shared DataGrid (as '{string.Join("', '", distinctLocals)}') "
                               + "outside ExternalDataGrid.tsx. Mount ExternalDataGrid instead: it switches the view "
                               + "picker off at runtime.");
            }

            foreach (Match api in CloneOrCreate.Matches(text))
            {
                violations.Add($"{Where(api.Index)}: '{api.Value}' in a file that holds the shared DataGrid. A cloned or "
                               + "created element can switch showViewSelector back on after the JSX mount is checked "
                               + "(cloneElement/createElement are refused here).");
            }

            foreach (Match escape in UnicodeEscape.Matches(text))
            {
                violations.Add($"{Where(escape.Index)}: unicode escape in a file that holds the shared DataGrid. An "
                               + "escaped identifier can reference the grid under a spelling this scan cannot see.");
            }
        }

        foreach (var local in distinctLocals)
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
                if (firstGridImport < 0)
                {
                    firstGridImport = index;
                }
            }
            else if (ShellNames.Contains(exported))
            {
                violations.Add($"{Where(index)}: imports DataGridPageShell (as '{local}'), which mounts the shared "
                               + "DataGrid with its default view selector and cannot switch it off. Mount "
                               + "ExternalDataGrid instead.");
            }
        }
    }

    /// <summary>
    /// Where a module specifier resolves, when that is OUTSIDE the external SPA source (and, when
    /// <paramref name="allowSharedLibrary"/>, outside the shared library and its node_modules link too);
    /// <c>null</c> when it stays inside, or when it is a bare npm package other than an <c>@spaarke/…</c> one.
    /// Mirrors the Vite config: <c>@/</c> → <c>src</c>, a leading <c>/</c> → the project root, and
    /// <c>@spaarke/ui-components</c> → the shared library.
    /// </summary>
    private static string? ResolvesOutsideSanctionedRoots(string spec, string fileName, bool allowSharedLibrary)
    {
        string full;
        if (spec == "@" || spec.StartsWith("@/", StringComparison.Ordinal))
        {
            full = Path.GetFullPath(Path.Combine(ExternalSpaSource, spec.Length > 2 ? spec[2..] : "."));
        }
        else if (spec.StartsWith('/'))
        {
            full = Path.GetFullPath(Path.Combine(ExternalSpaRoot, spec.TrimStart('/')));
        }
        else if (spec.StartsWith('.'))
        {
            var directory = Path.GetDirectoryName(fileName.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
            full = Path.GetFullPath(Path.Combine(SourceScan.RepoRoot, directory, spec));
        }
        else if (spec.StartsWith("@spaarke/", StringComparison.Ordinal))
        {
            var isSharedAlias = spec == "@spaarke/ui-components" || spec.StartsWith("@spaarke/ui-components/", StringComparison.Ordinal);
            return allowSharedLibrary && isSharedAlias ? null : "a @spaarke package other than the shared library";
        }
        else
        {
            return null; // a third-party npm package (residual: see the class remarks)
        }

        if (IsUnder(full, ExternalSpaSource))
        {
            return null;
        }
        if (allowSharedLibrary
            && (IsUnder(full, SharedLibraryRoot)
                || IsUnder(full, Path.Combine(ExternalSpaRoot, "node_modules", "@spaarke", "ui-components"))))
        {
            return null;
        }
        return Path.GetRelativePath(SourceScan.RepoRoot, full).Replace('\\', '/');
    }

    private static bool IsUnder(string full, string root) =>
        full.Equals(root, StringComparison.OrdinalIgnoreCase)
        || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

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

    private static IEnumerable<string> ScriptFilesUnder(string root) =>
        Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => ScriptExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string RepoRelative(string file) => Path.GetRelativePath(SourceScan.RepoRoot, file).Replace('\\', '/');

    [Fact(DisplayName = "No external-SPA file reaches the shared DataGrid except ExternalDataGrid.tsx, whose mount passes showViewSelector={false}")]
    public void ExternalSpa_EverySpaarkeDataGridMount_TurnsTheViewSelectorOff()
    {
        Assert.True(Directory.Exists(ExternalSpaSource), $"the external SPA source must exist at {ExternalSpaSource}");

        var violations = new List<string>();
        var compliantByFile = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in ScriptFilesUnder(ExternalSpaSource))
        {
            var relative = RepoRelative(file);
            var result = Scan(File.ReadAllText(file), relative);
            violations.AddRange(result.Violations);
            if (result.CompliantMounts > 0)
            {
                compliantByFile[relative] = result.CompliantMounts;
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
        // Not vacuous: the wrapper was found, scanned, and holds exactly the one sanctioned mount.
        var only = Assert.Single(compliantByFile);
        Assert.Equal(WrapperFile, only.Key);
        Assert.Equal(1, only.Value);
    }

    [Fact(DisplayName = "ExternalDataGrid.tsx is pinned: it mounts the shared grid with showViewSelector={false} after the caller's props")]
    public void ExternalSpa_GridWrapper_IsPinnedAndForcesThePickerOff()
    {
        var path = Path.Combine(SourceScan.RepoRoot, WrapperFile.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"the runtime guard must exist at {WrapperFile}");

        var actual = File.ReadAllText(path);
        Assert.True(
            Squash(actual) == Squash(PinnedWrapperSource),
            $"{WrapperFile} changed. It is the runtime guard that keeps the shared DataGrid's view picker off for "
            + "external callers: `showViewSelector={false}` must stay AFTER the caller's props, and nothing may "
            + "clone, re-create or re-export the grid. If the change is deliberate and keeps that, update "
            + "PinnedWrapperSource in this test in the same change (a reviewed change to this guard).");
        // The pinned text itself is the sanctioned shape the scan expects.
        var result = Scan(actual, WrapperFile);
        Assert.Empty(result.Violations);
        Assert.Equal(1, result.CompliantMounts);
    }

    private static string Squash(string text) => Regex.Replace(text, @"\s+", string.Empty);

    // ── Shared-library fan-in: which shared modules carry the grid (round 3) ──

    /// <summary>Repo-relative shared-library files allowed to import or re-export the shared grid / shell.</summary>
    private static readonly HashSet<string> SharedFanInAllowed = new(StringComparer.Ordinal)
    {
        "src/client/shared/Spaarke.UI.Components/src/components/DataGrid/index.ts",
        "src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGridPageShell.tsx",
        "src/client/shared/Spaarke.UI.Components/src/components/index.ts",
        "src/client/shared/Spaarke.UI.Components/src/index.ts",
    };

    private static readonly Regex LibStatement = new(
        @"\b(?:import|export)\b\s*(?<clause>[\w{*][^;'""`]*?)\s*\bfrom\s*['""](?<module>[^'""\n]+)['""]",
        RegexOptions.Compiled);

    private static readonly Regex GridOrShellToken = new(
        @"(?<![\w$])(?:DataGrid|DataGridDefault|DataGridPageShell|DataGridPageShellDefault)(?![\w$])", RegexOptions.Compiled);

    private static readonly Regex DefaultInClause = new(@"^(?:type\s+)?[\w$]+\s*(?:,|$)|(?<![\w$])default(?![\w$])", RegexOptions.Compiled);

    /// <summary>Shared-library paths (without extension) that ARE the grid, the shell, or a barrel above them.</summary>
    private static readonly string[] GridCarryingModules =
    [
        "src/components/DataGrid/DataGrid", "src/components/DataGrid/DataGridPageShell", "src/components/DataGrid/index",
        "src/components/DataGrid", "src/components/index", "src/components", "src/index", "src",
    ];

    /// <summary>
    /// Scans one shared-library file for statements that import or re-export the shared grid or shell. Returns
    /// a description per statement; <paramref name="fileName"/> is repo-relative.
    /// </summary>
    internal static IReadOnlyList<string> SharedFanIn(string source, string fileName)
    {
        var found = new List<string>();
        string Where(int index) => $"{fileName}:{LineOf(source, index)}";

        foreach (Match statement in LibStatement.Matches(source))
        {
            var module = statement.Groups["module"].Value;
            if (module.StartsWith("@fluentui/", StringComparison.Ordinal))
            {
                continue; // Fluent's own DataGrid has no view picker
            }
            var clause = statement.Groups["clause"].Value.Trim();
            var target = DefaultExportOf(module);
            var carriesByName = GridOrShellToken.IsMatch(clause);
            var carriesByDefault = target is "DataGrid" or "DataGridPageShell" && DefaultInClause.IsMatch(clause);
            var carriesByStar = clause.StartsWith('*') && IsGridCarryingModule(module, fileName);
            if (carriesByName || carriesByDefault || carriesByStar)
            {
                found.Add($"{Where(statement.Index)}: '{clause}' from '{module}'");
            }
        }

        // Prose in the library's comments says "import (" often, so a non-literal argument is not refused here
        // (unlike the external-SPA scan); the call is flagged when its argument text, up to the closing `)`,
        // names DataGrid, or when a plain literal resolves to a grid-carrying module.
        foreach (Match call in DynamicCall.Matches(source))
        {
            var argStart = call.Index + call.Length;
            var close = source.IndexOf(')', argStart);
            var argText = close < 0 ? source[argStart..] : source[argStart..close];
            var arg = DynamicLiteralArgAt.Match(source, argStart);
            if (argText.Contains("DataGrid", StringComparison.OrdinalIgnoreCase)
                || (arg.Success && IsGridCarryingModule(arg.Groups["spec"].Value, fileName)))
            {
                found.Add($"{Where(call.Index)}: a dynamic import/require that loads the grid or a barrel above it");
            }
        }

        foreach (Match glob in ImportMetaGlob.Matches(source))
        {
            found.Add($"{Where(glob.Index)}: import.meta.glob");
        }
        return found;
    }

    private static bool IsGridCarryingModule(string module, string fileName)
    {
        if (!module.StartsWith('.'))
        {
            return module.StartsWith("@spaarke/ui-components", StringComparison.Ordinal);
        }
        var directory = Path.GetDirectoryName(fileName.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
        var full = Path.GetFullPath(Path.Combine(SourceScan.RepoRoot, directory, module));
        var relative = Path.GetRelativePath(SharedLibraryRoot, full).Replace('\\', '/');
        var withoutExtension = Regex.Replace(relative, @"\.(?:tsx?|jsx?)$", string.Empty, RegexOptions.IgnoreCase);
        return GridCarryingModules.Contains(withoutExtension, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSharedLibraryTestFile(string relative) =>
        relative.Split('/').Any(segment => segment.StartsWith("__", StringComparison.Ordinal))
        || Regex.IsMatch(relative, @"\.(?:test|spec|stories)\.[cm]?[jt]sx?$", RegexOptions.IgnoreCase);

    [Fact(DisplayName = "Inside the shared library, only the known barrels and DataGridPageShell import or re-export the grid")]
    public void SharedLibrary_OnlyKnownModulesCarryTheGrid()
    {
        Assert.True(Directory.Exists(SharedLibrarySource), $"the shared library must exist at {SharedLibrarySource}");

        var unexpected = new List<string>();
        var carriersSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in ScriptFilesUnder(SharedLibrarySource))
        {
            var relative = RepoRelative(file);
            if (IsSharedLibraryTestFile(relative)
                || relative.EndsWith("/components/DataGrid/DataGrid.tsx", StringComparison.Ordinal))
            {
                continue; // tests are not bundled; DataGrid.tsx is the grid itself
            }
            var hits = SharedFanIn(File.ReadAllText(file), relative);
            if (hits.Count == 0)
            {
                continue;
            }
            if (SharedFanInAllowed.Contains(relative))
            {
                carriersSeen.Add(relative);
            }
            else
            {
                unexpected.AddRange(hits);
            }
        }

        Assert.True(unexpected.Count == 0,
            "A shared-library module now imports or re-exports the shared DataGrid (or DataGridPageShell). If the "
            + "external SPA can import it, it may mount the grid with the view picker on, and the external-SPA scan "
            + "would not see it. Review it: add it to ShellNames (refused for the external SPA) or prove it switches "
            + "the picker off, then add it to SharedFanInAllowed." + Environment.NewLine
            + string.Join(Environment.NewLine, unexpected));
        // Not vacuous: every allowed carrier still carries the grid (the detector reads the real barrels).
        Assert.Equal(SharedFanInAllowed.OrderBy(f => f, StringComparer.Ordinal), carriersSeen.OrderBy(f => f, StringComparer.Ordinal));
    }

    // ── Controls: the detector fires on each violation shape and is silent on the sanctioned one ──

    private const string SpaarkeImport = "import { DataGrid } from '@spaarke/ui-components/components/DataGrid/DataGrid';\n";

    /// <summary>A non-wrapper file inside the external SPA (relative specifiers resolve from its folder).</summary>
    private const string OtherSpaFile = "src/client/external-spa/src/widgets/Seeded.tsx";

    [Fact(DisplayName = "Control: a mount without the prop is reported")]
    public void Scan_WhenTheSharedDataGridIsMountedWithoutTheProp_ReportsIt()
    {
        var result = Scan(SpaarkeImport + "const A = () => <DataGrid configId={id} dataverseClient={c} />;", WrapperFile);

        Assert.Contains($"{WrapperFile}:2", Assert.Single(result.Violations));
        Assert.Equal(0, result.CompliantMounts);
    }

    [Fact(DisplayName = "Control: showViewSelector={true} is reported")]
    public void Scan_WhenTheSelectorIsExplicitlyOn_ReportsIt()
    {
        var result = Scan(SpaarkeImport + "<DataGrid configId={id} showViewSelector={true} />", WrapperFile);

        Assert.Single(result.Violations);
    }

    [Fact(DisplayName = "Control: the sanctioned mount passes, including a multi-line tag with an arrow-function prop")]
    public void Scan_WhenTheSelectorIsOff_PassesAndCountsTheMount()
    {
        var text = SpaarkeImport +
                   "<DataGrid\n  configId={id}\n  onRecordsLoaded={rows => setRows(rows)}\n  showViewSelector={false}\n/>";

        var result = Scan(text, WrapperFile);

        Assert.Empty(result.Violations);
        Assert.Equal(1, result.CompliantMounts);
    }

    [Fact(DisplayName = "Control: an aliased import is still checked")]
    public void Scan_WhenTheSharedDataGridIsImportedUnderAnAlias_ChecksTheAlias()
    {
        var text = "import {\n  DataGrid\n    as Grid,\n} from '@spaarke/ui-components';\n<Grid configId={id} />";

        Assert.Contains("<Grid>", Assert.Single(Scan(text, WrapperFile).Violations));
    }

    [Fact(DisplayName = "Control: DataGridPageShell and namespace imports are refused")]
    public void Scan_WhenTheGridIsReachedThroughAShellOrANamespace_ReportsIt()
    {
        Assert.Contains("DataGridPageShell",
            Assert.Single(Scan("import { DataGridPageShell } from '@spaarke/ui-components';", OtherSpaFile).Violations));
        Assert.Contains("namespace import",
            Assert.Single(Scan("import * as UI from '@spaarke/ui-components';\n<UI.DataGrid configId={id} />", OtherSpaFile).Violations));
    }

    [Fact(DisplayName = "Control: a mount inside a comment is reported (inside JSX children a // or /* line is text)")]
    public void Scan_WhenAMountIsQuotedInAComment_ReportsIt()
    {
        var text = SpaarkeImport +
                   "/**\n * Example: <DataGrid configId={id} />\n */\n" +
                   "// <DataGrid configId={id} />\n" +
                   "const u = 'https://x.test'; const A = () => <DataGrid configId={id} />;";

        var result = Scan(text, WrapperFile);

        Assert.Equal(3, result.Violations.Count);
        Assert.Contains(result.Violations, v => v.StartsWith($"{WrapperFile}:3", StringComparison.Ordinal));
        Assert.Contains(result.Violations, v => v.StartsWith($"{WrapperFile}:5", StringComparison.Ordinal));
        Assert.Contains(result.Violations, v => v.StartsWith($"{WrapperFile}:6", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Control: Fluent's own DataGrid (no view picker) is ignored")]
    public void Scan_WhenFluentsDataGridIsMounted_IgnoresIt()
    {
        var text = "import { DataGrid, DataGridHeader } from '@fluentui/react-components';\n" +
                   "<DataGrid items={rows} columns={cols}><DataGridHeader /></DataGrid>";

        var result = Scan(text, OtherSpaFile);

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
        // Scanned AS the wrapper, so each row proves its own rule fires (not just "imported outside the wrapper").
        AssertReported(shape, Scan(source, WrapperFile), expected);
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
    // S5: a node_modules-relative path to the shared library (from the wrapper's folder)
    [InlineData("S5-node-modules-path", "import { DataGrid } from '../../node_modules/@spaarke/ui-components/src/components/DataGrid/DataGrid';\n<DataGrid" + Bad, "<DataGrid>")]
    public void Scan_WhenARound2EvasionShapeIsSeeded_ReportsIt(string shape, string source, string expected)
    {
        AssertReported(shape, Scan(source, WrapperFile), expected);
    }

    private static void AssertReported(string shape, ScanResult result, string expected)
    {
        Assert.True(
            result.Violations.Any(v => v.Contains(expected, StringComparison.Ordinal)),
            $"{shape}: expected a violation containing '{expected}', got:{Environment.NewLine}{string.Join(Environment.NewLine, result.Violations)}");
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

        var result = Scan(text, WrapperFile);

        Assert.Equal(2, result.Violations.Count);
        Assert.Contains(result.Violations, v => v.StartsWith($"{WrapperFile}:3", StringComparison.Ordinal));
        Assert.Contains(result.Violations, v => v.StartsWith($"{WrapperFile}:5", StringComparison.Ordinal));
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

        var result = Scan(text, WrapperFile);

        Assert.Empty(result.Violations);
        Assert.Equal(2, result.CompliantMounts);
    }

    // ── Review round 3 (2026-10-02): three shapes compiled (vite build) into a live mount with the picker on
    // past the round-2 scan (V1–V3), plus the classes they belong to. Each one MUST be reported. ──

    private const string GridSource = "../../../shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx";

    [Theory(DisplayName = "Control: every evasion shape seeded in review round 3 is reported")]
    // V1: import.meta.glob — names the library, names nothing, and the spaced / bracket / eager forms
    [InlineData("V1-glob", WrapperFile, "const G = Object.values(import.meta.glob('" + GridSource + "', { eager: true, import: 'DataGrid' }))[0];\nconst A = () => <G configId=\"x\" />;", "import.meta.glob")]
    [InlineData("V1b-glob-no-library-name", OtherSpaFile, "const m = import.meta.glob('../../../shared/*/src/**/D*Grid.tsx', { eager: true });", "import.meta.glob")]
    [InlineData("V1c-glob-spaced", OtherSpaFile, "const m = import . meta . glob('./x/*.tsx');", "import.meta.glob")]
    [InlineData("V1d-glob-eager", OtherSpaFile, "const m = import.meta.globEager('./x/*.tsx');", "import.meta.glob")]
    [InlineData("V1e-glob-bracket", OtherSpaFile, "const m = import.meta['glob']('./x/*.tsx');", "import.meta.glob")]
    // V2: a unicode-escaped identifier, an escaped import specifier, an escaped module specifier
    [InlineData("V2-escaped-identifier", WrapperFile, SpaarkeImport + "const G = Data\\u0047rid;\nconst A = () => <G configId=\"x\" />;", "unicode escape")]
    [InlineData("V2b-escaped-import-name", OtherSpaFile, "import { Data\\u0047rid as G } from '@spaarke/ui-components/components/DataGrid/DataGrid';\n<G configId=\"x\" />", "could not be parsed")]
    [InlineData("V2c-escaped-module-specifier", OtherSpaFile, "import { DataGrid as G } from '@spaarke/ui-c\\u006fmponents/components/DataGrid/DataGrid';\n<G configId=\"x\" />", "escape in a module specifier")]
    [InlineData("V2d-hex-escaped-module-specifier", OtherSpaFile, "import { DataGrid as G } from '../../../shared/Spaarke.UI.C\\x6fmponents/src/components/DataGrid/DataGrid';", "escape in a module specifier")]
    [InlineData("V2e-string-named-specifier", OtherSpaFile, "import { \"DataGrid\" as G } from '@spaarke/ui-components/components/DataGrid/DataGrid';\n<G configId=\"x\" />", "not attributed")]
    // V3: a compliant mount, then cloned (or re-created) with the picker on
    [InlineData("V3-cloneElement", WrapperFile, SpaarkeImport + "const base = <DataGrid configId=\"x\" showViewSelector={false} />;\nconst A = () => React.cloneElement(base, { showViewSelector: true });", "cloneElement")]
    [InlineData("V3b-createElement-alias", WrapperFile, SpaarkeImport + "const make = React.createElement;\nconst A = () => <DataGrid configId=\"x\" showViewSelector={false} />;", "createElement")]
    [InlineData("V3c-destructured-clone", WrapperFile, SpaarkeImport + "const { cloneElement: c } = React;\nconst A = () => <DataGrid configId=\"x\" showViewSelector={false} />;", "cloneElement")]
    // The class: a module the scan cannot see could re-export the grid under any name
    [InlineData("W1-module-outside-the-spa", OtherSpaFile, "import { Grid } from '../../../../solutions/Other/src/grid';\n<Grid configId=\"x\" />", "outside the external SPA")]
    [InlineData("W2-alias-escaping-src", OtherSpaFile, "import { Grid } from '@/../../../solutions/Other/src/grid';", "outside the external SPA")]
    [InlineData("W3-root-relative", OtherSpaFile, "import { Grid } from '/../shared/Other/grid';", "outside the external SPA")]
    [InlineData("W4-other-spaarke-package", OtherSpaFile, "import { Grid } from '@spaarke/legal-workspace';", "outside the external SPA")]
    [InlineData("W5-library-name-elsewhere", OtherSpaFile, "import { Grid } from '../../../../solutions/Spaarke.UI.Components-fork/grid';", "outside the external SPA source and the shared library")]
    // Dynamic loads: a variable, a template, a concatenation, a module outside the SPA
    [InlineData("X1-dynamic-variable", OtherSpaFile, "const p = '@spaarke/ui-components/components/DataGrid/DataGrid';\nconst G = React.lazy(() => import(p));", "dynamic import")]
    [InlineData("X2-dynamic-template", OtherSpaFile, "const G = React.lazy(() => import(`../../../shared/Spaarke.UI.Components/src/components/${n}/DataGrid`));", "dynamic import")]
    [InlineData("X3-dynamic-concatenation", OtherSpaFile, "const G = React.lazy(() => import('../../../shared/' + 'Spaarke.UI.Components/src/components/DataGrid/DataGrid'));", "dynamic import")]
    [InlineData("X4-dynamic-outside", OtherSpaFile, "const G = React.lazy(() => import('../../../../solutions/Other/src/grid'));", "dynamic import")]
    // The wrapper rule: a fully compliant mount in any OTHER file is still refused
    [InlineData("Y1-compliant-mount-outside-wrapper", OtherSpaFile, SpaarkeImport + "const A = () => <DataGrid configId=\"x\" showViewSelector={false} />;", "outside ExternalDataGrid.tsx")]
    public void Scan_WhenARound3EvasionShapeIsSeeded_ReportsIt(string shape, string fileName, string source, string expected)
    {
        // CompliantMounts is not asserted: V3 rows hold a compliant JSX mount and are refused for the clone.
        var result = Scan(source, fileName);

        Assert.True(
            result.Violations.Any(v => v.Contains(expected, StringComparison.Ordinal)),
            $"{shape}: expected a violation containing '{expected}', got:{Environment.NewLine}{string.Join(Environment.NewLine, result.Violations)}");
    }

    [Fact(DisplayName = "Control: sanctioned external-SPA neighbours pass — the wrapper's consumer, local dynamic imports, @/ and npm imports")]
    public void Scan_WhenAnExternalSpaFileUsesOnlySanctionedImports_Passes()
    {
        var text = "import * as React from 'react';\n" +
                   "import { makeStyles } from '@fluentui/react-components';\n" +
                   "import { resolveCodePageTheme } from '@spaarke/ui-components/utils/themeStorage';\n" +
                   "import type { DataGridProps } from '@spaarke/ui-components/components/DataGrid/DataGrid';\n" +
                   "import { ExternalDataGrid } from './ExternalDataGrid';\n" +
                   "import { isEntitled } from '../registry/widgetRegistry';\n" +
                   "import { config } from '@/config';\n" +
                   "const L = () => import('../widgets/ProjectsWidget').then(m => ({ default: m.ProjectsWidgetBody }));\n" +
                   "const n = Array.from('abc');\n" +
                   "const A = () => <ExternalDataGrid configId=\"x\" />;";

        var result = Scan(text, OtherSpaFile);

        Assert.Empty(result.Violations);
        Assert.Equal(0, result.CompliantMounts);
    }

    [Fact(DisplayName = "Control: the shared-library fan-in detector reports a new carrier and ignores Fluent and type-only imports")]
    public void SharedFanIn_ReportsANewCarrierAndIgnoresNeighbours()
    {
        const string NewWidget = "src/client/shared/Spaarke.UI.Components/src/components/NewWidget/NewWidget.tsx";

        Assert.Single(SharedFanIn("import { DataGrid } from '../DataGrid';\n<DataGrid configId=\"x\" />", NewWidget));
        Assert.Single(SharedFanIn("import { DataGrid as Table } from '../../index';", NewWidget));
        Assert.Single(SharedFanIn("import Grid from '../DataGrid/DataGrid';", NewWidget));
        Assert.Single(SharedFanIn("import { DataGridPageShell } from '../DataGrid';", NewWidget));
        Assert.Single(SharedFanIn("export * from '../DataGrid';", NewWidget));
        Assert.Single(SharedFanIn("import * as C from '../index';", NewWidget));
        Assert.Single(SharedFanIn("const G = React.lazy(() => import('../DataGrid/DataGrid'));", NewWidget));
        Assert.Single(SharedFanIn("const G = React.lazy(() => import(`../DataGrid/${name}`));", NewWidget));
        Assert.Single(SharedFanIn("const G = React.lazy(() => import('../index'));", NewWidget));
        Assert.Single(SharedFanIn("const m = import.meta.glob('../**/*.tsx');", NewWidget));

        Assert.Empty(SharedFanIn(
            "import { DataGrid, DataGridBody } from '@fluentui/react-components';\n" +
            "import type { DataGridProps } from '../DataGrid/DataGrid';\n" +
            "export * from './NewWidgetTypes';\n" +
            "// No `@spaarke/auth` import (ADR-028); pdfjs is a dynamic import() below.\n" +
            "const pdf = import('pdfjs-dist');\n" +
            "import { tokens } from '../DataGrid/tokens';", NewWidget));
    }
}
