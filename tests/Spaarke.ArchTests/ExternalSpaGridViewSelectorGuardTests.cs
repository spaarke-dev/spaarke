using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 157 (owner round 4 item 7, 2026-10-01) — the external SPA never shows the
/// shared Spaarke <c>DataGrid</c>'s view picker, and never fetches the entity's saved-query list.
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
/// <b>The guarantee is a RUNTIME rule inside the shared grid (fix round c1).</b> Seven review rounds each found a
/// shape the source scan below let through and <c>vite build</c> compiled into a live mount with the picker on
/// (round 7, V7: a carrier file with no extension, or one the scan does not read). A regex scan cannot prove what
/// every JavaScript expression does, so the invariant no longer rests on it. The shared <c>DataGrid.tsx</c> reads
/// <c>useDataGridExternalHost()</c> (<c>DataGridExternalHost.tsx</c>) and, when it is true, forces the picker off,
/// ignores <c>externalViews</c> and any picked view, refuses a <c>savedquery-set</c> source, and never calls
/// <c>retrieveSavedQueriesForEntity</c>, whatever props, wrappers, clones, spreads or imports reach it. Two
/// independent switches turn it on; both are OFF in every other host, so no other host changes behaviour:
/// </para>
/// <list type="number">
/// <item>the React context <c>DataGridExternalHostProvider</c>, mounted by the SPA's root component above every
/// route, asserted by <see cref="ExternalSpa_RootMountsTheExternalHostProviderAboveEveryRoute"/>;</item>
/// <item>the build constant <c>__SPAARKE_DATAGRID_EXTERNAL_HOST__</c>, which the SPA's <c>vite.config.ts</c> defines as
/// <c>true</c>. Vite replaces it in every module it bundles, so every copy of the grid in that bundle is external,
/// whatever tree, React root or module instance renders it. In the built bundle the picker branches are dead code
/// and the picker component is dropped (fix round c1 build: 0 occurrences of its <c>Select view (currently</c>
/// label). Asserted by <see cref="ExternalSpa_ViteBuildDefinesTheExternalHostConstant"/>.</item>
/// </list>
/// <para>
/// <see cref="SharedDataGrid_EnforcesTheExternalHostRule"/> pins the rule's text in the shared library (the host
/// module verbatim, and the statements and counts in <c>DataGrid.tsx</c> and in <c>resolveGridSource.ts</c>, where
/// ontology task 054 moved <c>fetchConfigRecord</c> / <c>resolveSource</c> and with them the savedquery-set discovery
/// call; the view-retrieval calls are counted over the union of the two files), so removing or rewriting a pinned statement
/// fails this guard in CI. It does NOT see an ADDED statement that shadows the forced value in a nested scope (review
/// round 8, seed J4: <c>const showViewSelector = true;</c> in the load effect passed this class 194/194); see residual 4.
/// The behavioural proof is the jest suite
/// <c>Spaarke.UI.Components/src/components/DataGrid/__tests__/DataGrid.externalHost.test.tsx</c>: under either
/// switch, with <c>showViewSelector={true}</c> passed directly, no picker renders and the list is never requested.
/// That suite catches J4. Since fix round c2 (2026-10-03) it runs in CI: Tier 1's <c>datagrid-external-host-gate</c>
/// job runs the DataGrid jest folder on every change to <c>Spaarke.UI.Components</c> or to a CI workflow. Owner round
/// 13 item 11 (fix round c2-r2): the job is ADVISORY (it reports red, it does not fail <c>CI / Router</c>) until three
/// consecutive green runs on ubuntu-latest, then blocking (<see cref="DataGridGateAdvisory"/>).
/// <see cref="SharedDataGrid_ExternalHostJestSuiteRunsAsATier1Gate"/> refuses the named ways that job could stop
/// running or stop reporting (a text check, each disarming proven by a seeded row; not every conceivable one, see its
/// summary). Owner round 13 item 12: <c>ci-router.yml</c> is not changed by this project. Its <c>docs_only</c> skip
/// used to apply to a PR that edits the grid, the external SPA or this guard together with only docs-class files; since
/// 2026-10-06 (ontology task 081 round 8, PR #1309) a diff is docs-only only when EVERY changed file is documentation,
/// so such a PR runs Tier 1. The pin stays as a fast first check.
/// </para>
/// <para>
/// <b>The scan is defence in depth.</b> The rules below stay because each is zero-false-positive on the current
/// source. They no longer carry the guarantee. <c>src/client/external-spa/src/widgets/ExternalDataGrid.tsx</c>
/// still mounts the shared grid with <c>showViewSelector={false}</c> AFTER the caller's props, and its text is
/// pinned by <see cref="ExternalSpa_GridWrapper_IsPinnedAndForcesThePickerOff"/>. No other file under
/// <c>src/client/external-spa/src</c> may reach the shared grid, and no other file may use the wrapper except as a
/// JSX tag.
/// </para>
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
/// <item><b>Module specifiers (round 3).</b> The rules read a specifier where only whitespace separates it from
/// <c>from</c> or a side-effect <c>import</c>; a comment there is refused (round 6, below), so these are the
/// specifiers of the file. Each must be a plain string (no <c>\</c> escape, which would spell the library without
/// naming it). One that names the shared library must belong
/// to an import or re-export the scan parsed (so <c>import { "DataGrid" as G }</c> cannot hide). A relative,
/// <c>/</c> or <c>@/</c> specifier must resolve inside the external SPA source or into the shared library;
/// a bare <c>@spaarke/…</c> specifier must be the shared library. A module elsewhere could re-export the grid
/// under any name. <b>Round 4:</b> side-effect imports (<c>import '…'</c>) get the same rules, because a module
/// outside the SPA source can import the grid and park it on <c>window</c> with no binding name to see (a
/// seed in fix round b2 compiled into a live mount with the picker on). Any other bare specifier must name a
/// package declared in <c>external-spa/package.json</c>, so a resolve alias, a <c>#…</c> subpath import or a
/// <c>node:</c> builtin is refused.</item>
/// <item><b>Dynamic loads (round 3).</b> Every <c>import(…)</c> / <c>require(…)</c> (the keyword, whitespace,
/// then <c>(</c>; a comment after the keyword is refused, round 6) must take one plain string literal, with nothing
/// else inside the parentheses, that resolves inside the external SPA source or names a declared package. <c>import.meta.glob</c> is
/// refused in any form: its pattern can match the grid's file without naming it (V1). <b>Round 4:</b>
/// <c>eval(…)</c> and the <c>Function</c> constructor are refused, because code built from a string can import
/// anything.</item>
/// <item><b>Build entry (round 4).</b> Every <c>&lt;script&gt;</c> in <c>external-spa/index.html</c> must load a file
/// under <c>src/</c> and hold no inline code. Vite bundles each entry, and the scan reads only <c>src/</c>.</item>
/// <item><b>The wrapper's binding (round 5, Q1/Q2).</b> Outside the wrapper, any import of the wrapper module (by
/// any path-like spelling, extension and case ignored) binds names that may appear only as a JSX tag, after
/// <c>typeof</c>, or as a member name; in such a file <c>cloneElement</c>, <c>createElement</c> and <c>\u</c> are
/// refused. A namespace import, a re-export, an unattributed (string-named or side-effect) import and a dynamic
/// <c>import()</c> / <c>require()</c> of the wrapper module are refused.</item>
/// <item><b>Shipped library source only (round 5, Q3–Q5).</b> A specifier that names the shared library must
/// resolve into its <c>src</c> (directly or through the npm link) and not into a <c>__*</c> segment or a
/// <c>.test</c> / <c>.spec</c> / <c>.stories</c> file: a carrier beside <c>src/</c> or in a test path is not read
/// by the fan-in fact. <c>@spaarke/ui-components/…</c> is resolved through the Vite alias, not trusted, so
/// <c>@spaarke/ui-components/../…</c> cannot step out. A bare package specifier may not hold a <c>.</c> /
/// <c>..</c> segment (<c>react-window/../../x</c> compiled into a live mount: the package has no
/// <c>exports</c> map). A <c>?query</c> or <c>#fragment</c> in a specifier is refused (<c>…/DataGrid?v=1</c>
/// compiled into a second instance of the grid module, whose default import no name rule could attribute).</item>
/// <item><b>What the rules above do not read (round 6, R1–R5; each compiled into a live picker-on mount).</b> A
/// comment directly after <c>from</c>, <c>import</c> or <c>require</c> is refused: <c>from /* c */ '…'</c> hid a
/// grid import (R1) and a wrapper import (R2, reopening Q1/Q2), and <c>import /* c */ (…)</c> a dynamic load (R3).
/// The specifier regexes do not skip comments instead, because inside JSX children <c>/*</c> is text, so a skipped
/// "comment" could span live code. <c>import.meta.glob</c> is matched across comments and <c>?.</c>, and any
/// computed <c>import.meta[…]</c> is refused. A JSX pragma (<c>@jsx</c>, <c>@jsxImportSource</c>,
/// <c>@jsxRuntime</c>, <c>@jsxFrag</c>) is refused anywhere: <c>@jsxImportSource</c> imports a runtime from any
/// path with no import statement (R4). A <c>\u</c> escape is refused in every file, not only in a binding file
/// (<c>import.meta.glob</c> with one letter of <c>glob</c> escaped). A literal U+FEFF is refused: JavaScript reads it
/// as whitespace and .NET's <c>\s</c> does not, so <c>from</c>, U+FEFF, <c>'…'</c> would slip past every
/// <c>from\s*</c>. A specifier into a <c>node_modules</c> folder inside <c>src</c> is refused, and no such folder may
/// exist under the SPA's or the library's <c>src</c>: the scans skip <c>node_modules</c>, and a bare specifier
/// resolves through the nearest one (R5).</item>
/// <item><b>Every import lands on a file the scans read (round 7, V7, fix round c1).</b> A path-like specifier
/// (<c>./</c>, <c>../</c>, <c>/</c>, <c>@/</c>, <c>@spaarke/ui-components</c>), in a static, side-effect or dynamic
/// import of an external-SPA file or a shipped library file, is resolved the way Vite resolves it: the exact file
/// first, then Vite's default extensions, then a directory's <c>index</c>. The file it lands on must be one these
/// scans read (a script file under the SPA's or the library's <c>src</c>, not a test path). A file with no
/// extension or another extension, and a directory holding a <c>package.json</c>, are refused: the scan cannot
/// follow them, so it fails closed instead of going blind. A specifier with nothing to load is left alone (a real
/// import of it fails the build; the library's JSDoc examples quote such paths). Neither <c>package.json</c> may
/// declare a <c>browser</c> field, through which Vite remaps a relative import to another file. See
/// <see cref="UnscannedImportTargets"/> and <see cref="ScanPackageManifest"/>.</item>
/// </list>
/// <para><b>Shared-library fan-in (round 3).</b> The scan refuses <c>DataGridPageShell</c> by name, which is
/// only enough while it is the one shared component that mounts the grid.
/// <see cref="SharedLibrary_OnlyKnownModulesCarryTheGrid"/> pins that: inside the shared library (tests
/// excluded) only <c>DataGrid/index.ts</c>, <c>DataGrid/DataGridPageShell.tsx</c>, <c>components/index.ts</c>
/// and <c>src/index.ts</c> may import or re-export the grid. A new shared component that embeds it fails that
/// fact until it is reviewed (refuse it here by name, like the shell, or prove it switches the picker off).
/// <b>Round 5:</b> the test exclusion is sound only while nothing ships a test path, so every shipped library file
/// (the allowed carriers included) is refused if it loads a module outside <c>src/</c> or from a test /
/// <c>__*</c> / <c>node_modules</c> path, and the external SPA may not import one either (rule above). <b>Round 6:</b>
/// the fan-in regexes read only whitespace after the keyword too, so a library file is also refused for a comment or
/// U+FEFF between <c>from</c> / <c>import</c> / <c>require</c> and a specifier or argument that could load a library
/// module (relative, root-relative or alias path, or text naming the library or <c>DataGrid</c>), and for a JSX
/// pragma. The library rule is narrower than the SPA's because library prose often ends a comment line with "from"
/// above another comment line and a quoted object key; a bare package there is residual 1.</para>
/// <para><b>Comments are NOT stripped.</b> Inside JSX children a line reading <c>// &lt;X /&gt;</c> is TEXT
/// followed by a live element (round 2, S2/S3), so every comment is scanned as code. In a file that holds a
/// grid binding, a comment must not quote a mount, name the binding, or spell an element-cloning API. In every
/// file, a comment may not sit directly after a module keyword (round 6).</para>
/// <para><b>Residuals (honest; the runtime rule does not cover them).</b></para>
/// <list type="number">
/// <item>A NEW npm dependency (a <c>package.json</c> change) that ships its OWN grid or view picker. It is not the
/// shared grid, so the runtime rule does not apply to it. Server-side, the external <c>savedquery</c> /
/// <c>savedqueries</c> routes return only the views a module grid is registered to use (404 for every other view,
/// task 157 F1), and the column allow-lists refuse any other view's columns with a 400.</item>
/// <item>Build configuration: an edit to <c>vite.config.ts</c> that drops or overrides the constant (the define
/// fact fails; the provider still covers the app tree), or a resolve alias / plugin that re-points the shared
/// grid's module at a copy without the rule.</item>
/// <item>A NEW picker UI added inside <c>DataGrid.tsx</c> under another component name and gated on something other
/// than the pinned switch. The pins refuse a third <c>&lt;ViewSelector</c>, any other use of the raw prop, and any
/// other DIRECT call of <c>retrieveSavedQueriesForEntity</c> (the call regex misses an alias such as
/// <c>const f = c.retrieveSavedQueriesForEntity</c>, a <c>.bind</c> and bracket access, as it did before ontology 054
/// moved the discovery into <c>resolveGridSource.ts</c>); a differently named picker is a reviewed change to the shared
/// grid.</item>
/// </list>
/// <para>Items 1–3 are reviewed changes outside <c>external-spa/src</c>. In every case the BFF allow-list still refuses
/// every column the picker's views would need, with a 400; it is the data control.</para>
/// <para><b>Residual 4, reported by CI since fix round c2 (2026-10-03); blocking after the flip.</b> An edit to
/// <c>DataGrid.tsx</c> or <c>resolveGridSource.ts</c> that ignores the switch WITHOUT touching a pinned statement. The pin refuses removing or
/// rewriting the rule's statements, but not an added statement that shadows the forced value in a nested scope (review
/// round 8, seed J4: <c>const showViewSelector = true;</c> in the load effect passed this class 194/194, so the grid
/// would request <c>/savedqueries/{entity}</c> again on the external host). The jest suite
/// <c>DataGrid.externalHost.test.tsx</c> catches J4 (5 of 7 red), and it runs in Tier 1
/// (<c>datagrid-external-host-gate</c>; seed J4 re-planted in fix round c2 failed that job's jest step), guarded by
/// <see cref="SharedDataGrid_ExternalHostJestSuiteRunsAsATier1Gate"/>. Until the flip (owner round 13 item 11,
/// <see cref="DataGridGateAdvisory"/>) a red run there is a visible failed job, not a failed <c>CI / Router</c>: J4
/// would be reported, not refused. One more gap stays open until then and after: a PR that edits no classified path.
/// (The router's <c>docs_only</c> skip of a code-plus-docs PR, listed here before, was closed 2026-10-06 by ontology
/// task 081 round 8.) Data impact, had J4 shipped: nil, because
/// the BFF's external <c>savedqueries</c> / <c>savedquery</c> routes 404 every view no module grid registers (task 157
/// F1), which today is every view.</para>
/// <para><b>Closed by the runtime rule, and removed from this list in fix round c1</b> (residuals 3 and 5 through fix
/// round b2-r2): a shared-library dynamic <c>import()</c> whose path is built in a variable; and element-tree peeling
/// (Q8, <c>.props.children</c> / <c>.type</c> by hand, or a fiber walk). Whatever element, tree or module instance
/// reaches the shared grid, the grid applies the rule itself. V7 (round 7) is closed the same way, and the round-7 scan
/// rule above stops the remaining scan going blind on it. Residual 4 through b2-r2 (edits to <c>DataGrid.tsx</c>) was
/// narrowed by the pin in fix round c1-r1 (fix round c1 had listed it as closed too early) and is reported by the
/// CI run of the jest suite since fix round c2, advisory until its flip (above).</para>
/// <para><b>Crude by design</b> (see <see cref="SourceScan"/>): regex over source, not a TypeScript parse. Each
/// rule is paired with a negative control proving it fires and a positive control proving it does not fire
/// on the sanctioned shape. ADR-038 Amendment A1: <c>tests/Spaarke.ArchTests/**</c> is a deletion-protected
/// KEEP path.</para>
/// <para><b>Maintenance.</b> If an external grid ever genuinely needs a view picker, do NOT relax this guard or the
/// runtime rule alone: register the views on the module (<c>ExternalModuleDescriptor.SavedQueryIds</c>), re-derive
/// the module's server allow-list with task 134's rule (b) (sibling-view columns) in the same
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
         * The picker rule itself lives in the shared grid (DataGridExternalHost.tsx). In this SPA the app root mounts
         * DataGridExternalHostProvider and the Vite build defines __SPAARKE_DATAGRID_EXTERNAL_HOST__, so the grid shows
         * no view picker and never requests the entity's saved-query list, whatever props reach it. Neither a call of
         * this wrapper as a plain function nor a clone of the element it returns can turn the picker on. With the picker
         * on, the grid would offer the entity's internal MDA views, whose columns the BFF's external allow-lists
         * (ExternalAccessModule.cs) do not admit.
         *
         * This wrapper is defence in depth: it also passes `showViewSelector={false}` AFTER the caller's props, and its
         * props type omits the switch. ExternalSpaGridViewSelectorGuardTests pins this file's text and refuses every other
         * import of the shared grid under src/client/external-spa/src, so a change here is a change to that guard.
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

    // Round 5: `import <clause> from '<any module>'` / `export <clause> from '<any module>'`, used to find the
    // bindings and re-exports of the WRAPPER (whose specifier does not name the shared library).
    private static readonly Regex AnyStaticImport = new(
        @"\bimport\b\s*(?<clause>[\w{*][^;'""`]*?)\s*\bfrom\s*['""](?<module>[^'""\n]*)['""]", RegexOptions.Compiled);

    private static readonly Regex AnyReExport = new(
        @"\bexport\b\s*(?<clause>[\w{*][^;'""`]*?)\s*\bfrom\s*['""](?<module>[^'""\n]*)['""]", RegexOptions.Compiled);

    // Every `from '<specifier>'` in the file, whatever precedes it (round 3: the specifier rules), and every
    // side-effect `import '<specifier>'` (round 4: such a module can hand the grid over through a global). Only
    // whitespace may separate the keyword and the quote: a comment there is refused by CommentAfterModuleKeyword
    // (round 6), so these two find every specifier the file has.
    private static readonly Regex AnyFrom = new(@"\bfrom\s*(?<q>['""])(?<spec>[^'""\n]*)\k<q>", RegexOptions.Compiled);
    private static readonly Regex SideEffectImport = new(@"\bimport\s*(?<q>['""])(?<spec>[^'""\n]*)\k<q>", RegexOptions.Compiled);

    // Code built from a string at runtime: its imports cannot be read (round 4).
    private static readonly Regex DynamicCode = new(@"(?<![\w$.])(?:eval|Function)\s*\(", RegexOptions.Compiled);

    // `<script …>` tags in the SPA's index.html, Vite's build entry (round 4).
    private static readonly Regex ScriptTag = new(
        @"<script\b(?<attrs>[^>]*)>(?<body>.*?)</script\s*>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex ScriptSrc = new(@"\bsrc\s*=\s*(?<q>['""])(?<src>[^'""]*)\k<q>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Every `import(` / `require(`; the argument is then checked with DynamicLiteralArgAt.
    private static readonly Regex DynamicCall = new(@"\b(?:import|require)\s*\(", RegexOptions.Compiled);

    // One plain string literal (no escape, no template, no concatenation) and then `)` or `,`.
    private static readonly Regex DynamicLiteralArgAt = new(
        @"\G\s*(?<q>['""])(?<spec>[^'""`\n\\]*)\k<q>\s*[),]", RegexOptions.Compiled);

    // Whitespace or comments between tokens. Used ONLY in rules whose every match is a violation: a regex that
    // skips a "comment" can be made to skip live code (inside JSX children `/*` is text), and in a rule whose
    // matches are attributed or excluded that would hide what the "comment" spans. Here it can only add matches.
    private const string Comment = @"(?>/\*[\s\S]*?\*/)|(?>//[^\n\r\u2028\u2029]*)";

    // U+FEFF is JavaScript whitespace (not .NET `\s`), so it belongs in the gap too.
    private const string TokenGap = @"(?:[\s\uFEFF]|" + Comment + ")*";

    // `import.meta.glob(…)`, `import.meta.globEager(…)`, `import . meta /* c */ .glob`, `import.meta?.glob`, and any
    // computed `import.meta[…]` / `import.meta?.[…]` access (round 3, V1; round 6: comments and `?.` between tokens).
    private static readonly Regex ImportMetaGlob = new(
        @"\bimport" + TokenGap + @"\." + TokenGap + "meta" + TokenGap + @"(?:\??\." + TokenGap + @"glob|(?:\?\.)?" + TokenGap + @"\[)",
        RegexOptions.Compiled);

    // Round 6 (fix round b2-r2, R1–R3): a comment between a module keyword and its specifier or `(`. Every specifier
    // rule reads `from '…'`, `import '…'` and `import(` / `require(` with only whitespace between; `from /* c */ '…'`
    // and `import /* c */ (…)` hid the module from all of them. Refused, not skipped: see TokenGap for why a
    // specifier regex must not skip comments. Only a keyword whose comment(s) are followed by a quote or `(` is
    // refused, because library prose often ends a comment line with "from" or "import" above another comment line.
    // The comment bounds are JavaScript's (a block ends at the first `*/`, a line at any line terminator). A comment
    // INSIDE the parentheses already fails the plain-literal rule.
    private static readonly Regex CommentAfterModuleKeyword = new(
        @"(?<![\w$.])(?:from|import|require)\s*(?:" + Comment + ")" + TokenGap + @"['""(]", RegexOptions.Compiled);

    // Round 6: U+FEFF is JavaScript whitespace but not .NET `\s`, so `from\uFEFF'…'` would slip past every
    // `from\s*['"]`. The literal character is refused (File.ReadAllText already drops a leading byte-order mark).
    private static readonly Regex ZeroWidthNoBreakSpace = new("\uFEFF", RegexOptions.Compiled);

    // Round 6 (R4): a JSX pragma. `/** @jsxImportSource ../../x */` makes esbuild import `../../x/jsx-runtime` with
    // no import statement in the file, so a module outside src/ runs every jsx() call of that file. `@jsx`,
    // `@jsxFrag` and `@jsxRuntime` re-point the factory the same way. Refused anywhere, comment or not.
    private static readonly Regex JsxPragma = new(@"@jsx", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

        // ── Round 5 (fix round b2-r1): bindings of the WRAPPER. ExternalDataGrid has no hooks, so calling it as a
        // plain function returns the INNER `<DataGrid … showViewSelector={false} />` element; cloning that element
        // (Q1) or mounting its `.type` (Q2) turns the picker on. So outside the wrapper, its binding may appear only
        // as a JSX tag (or after `typeof`), exactly like a grid binding inside the wrapper; and the wrapper module
        // may not be namespace-imported, re-exported or loaded dynamically. ──
        var wrapperLocals = new List<string>();
        var parsedWrapperSpans = new List<(int Start, int End)>();
        if (!isWrapper)
        {
            foreach (Match import in AnyStaticImport.Matches(text))
            {
                var module = import.Groups["module"].Value;
                if (!IsWrapperModule(module, fileName))
                {
                    continue;
                }
                importSpans.Add((import.Index, import.Index + import.Length));
                parsedWrapperSpans.Add((import.Index, import.Index + import.Length));
                var clauseText = import.Groups["clause"].Value.Trim();
                if (IsTypeOnlyClause(clauseText))
                {
                    continue; // `import type { … }` binds no runtime value
                }
                var clause = ImportClause.Match(clauseText);
                if (!clause.Success)
                {
                    violations.Add($"{Where(import.Index)}: import clause from '{module}' (the ExternalDataGrid wrapper) could "
                                   + "not be parsed. Import it with a plain named import.");
                    continue;
                }
                if (clause.Groups["ns"].Success)
                {
                    violations.Add($"{Where(import.Index)}: namespace import of '{module}' (the ExternalDataGrid wrapper). "
                                   + "Import ExternalDataGrid by name so every reference to it can be checked.");
                }
                if (clause.Groups["default"].Success)
                {
                    wrapperLocals.Add(clause.Groups["default"].Value);
                }
                if (clause.Groups["names"].Success)
                {
                    foreach (var raw in clause.Groups["names"].Value.Split(','))
                    {
                        if (string.IsNullOrWhiteSpace(raw) || IsTypeOnlyClause(raw.Trim()))
                        {
                            continue; // trailing comma, or `type ExternalDataGridProps`
                        }
                        var specifier = ImportSpecifier.Match(raw);
                        if (!specifier.Success)
                        {
                            violations.Add($"{Where(import.Index)}: import specifier '{raw.Trim()}' from '{module}' (the "
                                           + "ExternalDataGrid wrapper) could not be parsed.");
                            continue;
                        }
                        wrapperLocals.Add(specifier.Groups["local"].Success
                            ? specifier.Groups["local"].Value
                            : specifier.Groups["imported"].Value);
                    }
                }
            }

            foreach (Match reExport in AnyReExport.Matches(text))
            {
                var module = reExport.Groups["module"].Value;
                if (!IsWrapperModule(module, fileName))
                {
                    continue;
                }
                importSpans.Add((reExport.Index, reExport.Index + reExport.Length));
                parsedWrapperSpans.Add((reExport.Index, reExport.Index + reExport.Length));
                if (!IsTypeOnlyClause(reExport.Groups["clause"].Value.Trim()))
                {
                    violations.Add($"{Where(reExport.Index)}: re-exports from '{module}' (the ExternalDataGrid wrapper). Another "
                                   + "file could then reference ExternalDataGrid under a name this scan cannot attribute.");
                }
            }
        }

        // ── Round 3: module specifiers. A module the scan does not see could re-export the grid under any name. ──
        // Round 4: side-effect imports get the same rules (a module outside the SPA source can import the grid
        // and park it on `window`, with no binding name for this scan to see).
        foreach (Match fromClause in AnyFrom.Matches(text).Concat(SideEffectImport.Matches(text)))
        {
            var spec = fromClause.Groups["spec"].Value;
            if (spec.Contains('\\'))
            {
                violations.Add($"{Where(fromClause.Index)}: escape in a module specifier ('{spec}'). An escape can spell the "
                               + "shared library without naming it; write the specifier plainly.");
                continue;
            }
            if (HasQueryOrFragment(spec))
            {
                // Round 5: `…/DataGrid?v=1` is a SECOND instance of the grid module whose default import the
                // default-export rule could not name (it compiled into a live picker-on mount).
                violations.Add($"{Where(fromClause.Index)}: a query or fragment in a module specifier ('{spec}'). Vite loads "
                               + "it as another instance of the module, under a name this scan cannot attribute.");
                continue;
            }
            if (!isWrapper && IsWrapperModule(spec, fileName)
                && !parsedWrapperSpans.Any(span => fromClause.Index >= span.Start && fromClause.Index < span.End))
            {
                violations.Add($"{Where(fromClause.Index)}: import of '{spec}' (the ExternalDataGrid wrapper) is not attributed "
                               + "to a parsed import (e.g. a string-named specifier, or a side-effect import). Import "
                               + "ExternalDataGrid with a plain named import.");
            }
            if (NamesSharedLibrary.IsMatch(spec))
            {
                if (!parsedSharedSpans.Any(span => fromClause.Index >= span.Start && fromClause.Index < span.End))
                {
                    violations.Add($"{Where(fromClause.Index)}: import from '{spec}' is not attributed to a parsed import or "
                                   + "re-export (e.g. a string-named specifier such as { \"DataGrid\" as G }, or a "
                                   + "side-effect import). Import from the shared library with plain identifiers.");
                }
                if (ResolvesOutsideSanctionedRoots(spec, fileName, allowSharedLibrary: true) is { } where)
                {
                    violations.Add($"{Where(fromClause.Index)}: '{spec}' names the shared library but resolves to {where}. A "
                                   + "shared-library specifier must resolve into the library's shipped src (not beside "
                                   + "it, not a test or __* path).");
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
            if (!isWrapper && IsWrapperModule(spec, fileName))
            {
                violations.Add($"{Where(call.Index)}: dynamic import/require of '{spec}' (the ExternalDataGrid wrapper). The "
                               + "loaded module's ExternalDataGrid could be called as a plain function; import it statically "
                               + "by name and mount it only as JSX.");
                continue;
            }
            if (NamesSharedLibrary.IsMatch(spec) || HasQueryOrFragment(spec)
                || ResolvesOutsideSanctionedRoots(spec, fileName, allowSharedLibrary: false) is not null)
            {
                violations.Add($"{Where(call.Index)}: dynamic import/require of '{spec}' (e.g. React.lazy). Only a "
                               + "module inside the external SPA source may be loaded dynamically; import the shared "
                               + "library statically by name so every DataGrid import can be checked.");
            }
        }

        foreach (Match glob in ImportMetaGlob.Matches(text))
        {
            violations.Add($"{Where(glob.Index)}: import.meta.glob (or a computed import.meta[…] access) is refused. Its "
                           + "pattern can match the shared DataGrid's file without naming it, so the grid it loads cannot "
                           + "be checked.");
        }

        foreach (Match code in DynamicCode.Matches(text))
        {
            violations.Add($"{Where(code.Index)}: eval / Function constructor is refused. Code built from a string can "
                           + "import a module this scan cannot read.");
        }

        // ── Round 6 (fix round b2-r2): R1–R3 hid a module behind a comment; R4 loaded one through a JSX pragma. ──
        foreach (Match comment in CommentAfterModuleKeyword.Matches(text))
        {
            violations.Add($"{Where(comment.Index)}: a comment directly after 'from' / 'import' / 'require'. The module "
                           + "specifier rules read only whitespace there, so the import behind the comment could not be "
                           + "checked; move the comment off the import.");
        }

        foreach (Match space in ZeroWidthNoBreakSpace.Matches(text))
        {
            violations.Add($"{Where(space.Index)}: a U+FEFF character. JavaScript reads it as whitespace and this scan "
                           + "does not, so it could separate a module keyword from its specifier unseen.");
        }

        foreach (Match pragma in JsxPragma.Matches(text))
        {
            violations.Add($"{Where(pragma.Index)}: a JSX pragma (@jsx, @jsxImportSource, @jsxRuntime, @jsxFrag) is "
                           + "refused. It makes the compiler import a JSX runtime or factory from a module this scan "
                           + "cannot read, with no import statement.");
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
            foreach (Match reference in NonJsxReference(local).Matches(text))
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

        // Round 5: the wrapper's binding, outside the wrapper. Same reference rule as a grid binding (a JSX tag, a
        // `typeof`, or a member name), plus the element-API and escape refusals; no mount check is needed, because
        // the wrapper forces the picker off for every prop a JSX mount can pass.
        var distinctWrapperLocals = wrapperLocals.Distinct(StringComparer.Ordinal).ToList();
        if (distinctWrapperLocals.Count > 0 && distinctLocals.Count == 0)
        {
            foreach (Match api in CloneOrCreate.Matches(text))
            {
                violations.Add($"{Where(api.Index)}: '{api.Value}' in a file that holds ExternalDataGrid. A cloned or created "
                               + "element of the grid the wrapper returns can switch showViewSelector back on.");
            }
            foreach (Match escape in UnicodeEscape.Matches(text))
            {
                violations.Add($"{Where(escape.Index)}: unicode escape in a file that holds ExternalDataGrid. An escaped "
                               + "identifier can reference the wrapper under a spelling this scan cannot see.");
            }
        }
        else if (distinctLocals.Count == 0)
        {
            // Round 6: in every OTHER file too. An escaped property name spells what a rule looks for without
            // naming it (`import.meta.glob(…)` with one letter of `glob` escaped); the SPA source holds no `\u` today,
            // so this costs nothing.
            foreach (Match escape in UnicodeEscape.Matches(text))
            {
                violations.Add($"{Where(escape.Index)}: unicode escape. An escaped identifier or property name can spell "
                               + "what this scan looks for (e.g. import.meta.gl\\u006fb) without naming it; write the "
                               + "character itself.");
            }
        }
        foreach (var local in distinctWrapperLocals)
        {
            foreach (Match reference in NonJsxReference(local).Matches(text))
            {
                if (importSpans.Any(span => reference.Index >= span.Start && reference.Index < span.End))
                {
                    continue;
                }
                violations.Add($"{Where(reference.Index)}: '{local}' (ExternalDataGrid) is referenced outside a JSX tag. "
                               + "Called as a plain function it returns the INNER shared-grid element, which can be cloned "
                               + "or whose .type can be mounted with the picker on. Mount it only as <ExternalDataGrid … />.");
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
    /// Any reference to <paramref name="local"/> that is NOT a JSX tag (<c>&lt;X</c> / <c>&lt;/X</c>), a
    /// <c>typeof X</c>, or a member name (<c>obj.X</c>); a spread <c>...X</c> IS a reference.
    /// </summary>
    private static Regex NonJsxReference(string local) =>
        new($@"(?<![\w$])(?<!<\s*)(?<!</\s*)(?<!\btypeof\s+)(?<!(?<!\.)\.){Regex.Escape(local)}(?![\w$])");

    /// <summary>The Vite alias for the shared library (<c>vite.config.ts</c> maps it to <c>Spaarke.UI.Components/src</c>).</summary>
    private const string SharedAlias = "@spaarke/ui-components";

    private static bool IsSharedAlias(string spec) =>
        spec == SharedAlias || spec.StartsWith(SharedAlias + "/", StringComparison.Ordinal);

    private static bool IsTypeOnlyClause(string clause) =>
        clause.StartsWith("type ", StringComparison.Ordinal) || clause.StartsWith("type{", StringComparison.Ordinal);

    private static bool HasQueryOrFragment(string spec) => spec.Contains('?') || spec.IndexOf('#', 1) >= 0;

    /// <summary>
    /// The full path a path-like specifier resolves to, mirroring the Vite config: <c>@/</c> → the SPA's
    /// <c>src</c>, <c>@spaarke/ui-components</c> → the shared library's <c>src</c> (round 5: resolved, not
    /// trusted, so <c>@spaarke/ui-components/../…</c> cannot step out — Q5), a leading <c>/</c> → the project root,
    /// <c>./</c> / <c>../</c> → relative to <paramref name="fileName"/>. <c>null</c> for any other bare specifier.
    /// </summary>
    private static string? ResolveSpecifier(string spec, string fileName)
    {
        if (spec == "@" || spec.StartsWith("@/", StringComparison.Ordinal))
        {
            return Path.GetFullPath(Path.Combine(ExternalSpaSource, spec.Length > 2 ? spec[2..] : "."));
        }
        if (IsSharedAlias(spec))
        {
            return Path.GetFullPath(Path.Combine(SharedLibrarySource, spec.Length > SharedAlias.Length + 1 ? spec[(SharedAlias.Length + 1)..] : "."));
        }
        if (spec.StartsWith('/'))
        {
            return Path.GetFullPath(Path.Combine(ExternalSpaRoot, spec.TrimStart('/')));
        }
        if (spec.StartsWith('.'))
        {
            var directory = Path.GetDirectoryName(fileName.Replace('/', Path.DirectorySeparatorChar)) ?? string.Empty;
            return Path.GetFullPath(Path.Combine(SourceScan.RepoRoot, directory, spec));
        }
        return null;
    }

    /// <summary>
    /// Whether <paramref name="spec"/> loads the wrapper module, by any path-like spelling (<c>./ExternalDataGrid</c>,
    /// <c>@/widgets/ExternalDataGrid.tsx</c>, <c>/src/widgets/externaldatagrid</c> …). Compared without the script
    /// extension and case-insensitively, as the Windows file system and Vite's extension resolution see it.
    /// </summary>
    private static bool IsWrapperModule(string spec, string fileName)
    {
        var full = ResolveSpecifier(spec.Split('?', '#')[0], fileName);
        if (full is null)
        {
            return false;
        }
        var wrapper = Path.GetFullPath(Path.Combine(SourceScan.RepoRoot, WrapperFile.Replace('/', Path.DirectorySeparatorChar)));
        return string.Equals(WithoutScriptExtension(full), WithoutScriptExtension(wrapper), StringComparison.OrdinalIgnoreCase);
    }

    private static string WithoutScriptExtension(string path) =>
        ScriptExtensions.FirstOrDefault(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) is { } ext
            ? path[..^ext.Length]
            : path;

    /// <summary>
    /// A path (relative to a library <c>src</c> root, with or without its extension) that is test or tooling code:
    /// a <c>__*</c> segment, or a <c>.test</c> / <c>.spec</c> / <c>.stories</c> file. The fan-in fact skips these, so
    /// (round 5, Q4) the external SPA may not import them and no other library file may either.
    /// </summary>
    private static bool IsTestPath(string relative) =>
        relative.Split('/', '\\').Any(segment => segment.StartsWith("__", StringComparison.Ordinal))
        || Regex.IsMatch(relative, @"\.(?:test|spec|stories)(?:\.[cm]?[jt]sx?)?$", RegexOptions.IgnoreCase);

    /// <summary>The roots a shared-library specifier may resolve into: the library's <c>src</c>, directly or via the npm link.</summary>
    private static IEnumerable<string> SharedSourceRoots =>
        [SharedLibrarySource, Path.Combine(ExternalSpaRoot, "node_modules", "@spaarke", "ui-components", "src")];

    /// <summary>
    /// Why <paramref name="full"/> is not shipped shared-library source (outside <c>src</c>, or a test path);
    /// <c>null</c> when it is.
    /// </summary>
    private static string? NotSharedLibrarySource(string full)
    {
        foreach (var root in SharedSourceRoots)
        {
            if (IsUnder(full, root))
            {
                var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
                if (HasNodeModulesSegment(relative))
                {
                    return $"a node_modules path in the shared library's src ({relative}), which the fan-in fact skips";
                }
                return IsTestPath(relative) ? $"a test or __* path in the shared library ({relative})" : null;
            }
        }
        return $"{Path.GetRelativePath(SourceScan.RepoRoot, full).Replace('\\', '/')} (outside the shared library's src)";
    }

    /// <summary>Whether a relative path passes through a <c>node_modules</c> folder (round 6, R5).</summary>
    private static bool HasNodeModulesSegment(string relative) =>
        relative.Split('/', '\\').Any(segment => segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Every <c>node_modules</c> folder under <paramref name="root"/>, as a violation (round 6, R5). The scans skip
    /// <c>node_modules</c>, and a bare specifier resolves through the nearest one walking up from the importer, so a
    /// folder inside a scanned <c>src</c> would supply unscanned modules even to a declared package name.
    /// </summary>
    private static IEnumerable<string> NodeModulesFoldersUnder(string root) =>
        Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .Where(directory => Path.GetFileName(directory).Equals("node_modules", StringComparison.OrdinalIgnoreCase))
            .Select(directory => $"{RepoRelative(directory)}: a node_modules folder inside a scanned src. The scan skips "
                                 + "node_modules, and Vite resolves bare specifiers through it; keep packages in the "
                                 + "project's own node_modules.");

    /// <summary>
    /// Where a module specifier resolves, when that is OUTSIDE the external SPA source (and, when
    /// <paramref name="allowSharedLibrary"/>, outside the shared library's shipped <c>src</c> too); <c>null</c>
    /// when it stays inside, or when it is a bare package the SPA's <c>package.json</c> declares.
    /// </summary>
    private static string? ResolvesOutsideSanctionedRoots(string spec, string fileName, bool allowSharedLibrary)
    {
        if (spec.StartsWith("@spaarke/", StringComparison.Ordinal) && !IsSharedAlias(spec))
        {
            return "a @spaarke package other than the shared library";
        }

        var full = ResolveSpecifier(spec, fileName);
        if (full is null)
        {
            // Round 4: a bare specifier must name a package the SPA's package.json declares. Anything else — a
            // `#…` subpath import, a resolve alias, `node:…` — can point at any file, the shared grid included.
            // Round 5: and it may not hold a `.` / `..` segment: `react-window/../../x` steps out of a package
            // with no `exports` map to any file (it compiled into a live picker-on mount). A declared third-party
            // package is otherwise a residual (see the class remarks).
            if (spec.Split('/').Any(segment => segment is "." or ".."))
            {
                return "a bare specifier with a '.' or '..' segment (it can step out of the package to any file)";
            }
            return DeclaredPackages.Value.Contains(PackageNameOf(spec))
                ? null
                : "an undeclared bare specifier: not a package in external-spa/package.json (a resolve alias, a '#' "
                  + "subpath import or a builtin can point at any file)";
        }

        if (IsSharedAlias(spec))
        {
            // Round 5 (Q5): the alias is resolved, so `@spaarke/ui-components/../…` must stay in shipped library source.
            return allowSharedLibrary ? NotSharedLibrarySource(full) : "the shared library";
        }
        if (IsUnder(full, ExternalSpaSource))
        {
            // Round 6 (R5): ScriptFilesUnder skips node_modules, so a node_modules folder inside src is not scanned.
            return HasNodeModulesSegment(Path.GetRelativePath(ExternalSpaSource, full))
                ? $"{Path.GetRelativePath(SourceScan.RepoRoot, full).Replace('\\', '/')}: a node_modules folder, which the scan skips"
                : null;
        }
        if (allowSharedLibrary)
        {
            // Round 5 (Q3/Q4): into the library's src, not its root (a carrier beside src/ is not fan-in scanned) and
            // not a test or `__*` path (the fan-in fact skips those).
            return NotSharedLibrarySource(full);
        }
        return Path.GetRelativePath(SourceScan.RepoRoot, full).Replace('\\', '/');
    }

    /// <summary>The package names external-spa/package.json declares, in any dependency section.</summary>
    private static readonly Lazy<HashSet<string>> DeclaredPackages = new(() =>
    {
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(ExternalSpaRoot, "package.json")));
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in new[] { "dependencies", "devDependencies", "peerDependencies", "optionalDependencies" })
        {
            if (manifest.RootElement.TryGetProperty(section, out var deps))
            {
                foreach (var dep in deps.EnumerateObject())
                {
                    names.Add(dep.Name);
                }
            }
        }
        return names;
    });

    /// <summary><c>@scope/name/sub</c> → <c>@scope/name</c>; <c>name/sub</c> → <c>name</c>.</summary>
    private static string PackageNameOf(string spec)
    {
        var segments = spec.Split('/');
        return spec.StartsWith('@') && segments.Length > 1 ? $"{segments[0]}/{segments[1]}" : segments[0];
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

    /// <summary>
    /// Every <c>&lt;script&gt;</c> in the SPA's <c>index.html</c> must load a file inside the scanned source and carry no
    /// inline code: Vite bundles each one, so another entry would put an unscanned module in the build (round 4).
    /// </summary>
    internal static IReadOnlyList<string> ScanEntryHtml(string html)
    {
        var violations = new List<string>();
        var tags = ScriptTag.Matches(html);
        if (tags.Count == 0 || Regex.Matches(html, @"<script\b", RegexOptions.IgnoreCase).Count != tags.Count)
        {
            violations.Add("src/client/external-spa/index.html: its <script> tags could not be read (none, or one "
                           + "without a closing tag). Keep one plain <script type=\"module\" src=\"/src/…\"></script>.");
            return violations;
        }
        foreach (Match tag in tags)
        {
            var src = ScriptSrc.Match(tag.Groups["attrs"].Value);
            var full = src.Success && src.Groups["src"].Value.StartsWith('/')
                ? Path.GetFullPath(Path.Combine(ExternalSpaRoot, src.Groups["src"].Value.TrimStart('/')))
                : null;
            if (full is null || !IsUnder(full, ExternalSpaSource) || !string.IsNullOrWhiteSpace(tag.Groups["body"].Value))
            {
                violations.Add($"src/client/external-spa/index.html: <script{tag.Groups["attrs"].Value}> must load a file "
                               + "under src/ by a root-relative src and hold no inline code. Vite bundles it, and the "
                               + "scan only reads src/.");
            }
        }
        return violations;
    }

    // ── Round 7 (fix round c1, V7): every import must land on a file these scans read ──

    /// <summary>Vite's default <c>resolve.extensions</c>, in Vite's order (the SPA's vite.config.ts does not set them).</summary>
    private static readonly string[] ViteResolveExtensions = [".mjs", ".js", ".mts", ".ts", ".jsx", ".tsx", ".json"];

    /// <summary>
    /// The script files the two production facts read: every script file under the SPA's <c>src</c> and under the
    /// shared library's <c>src</c> (<see cref="ScriptFilesUnder"/>), minus the library's test paths, which the
    /// fan-in fact skips.
    /// </summary>
    private static readonly Lazy<HashSet<string>> ScannedFiles = new(() =>
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in ScriptFilesUnder(ExternalSpaSource))
        {
            files.Add(Path.GetFullPath(file));
        }
        foreach (var file in ScriptFilesUnder(SharedLibrarySource))
        {
            if (!IsSharedLibraryTestFile(RepoRelative(file)))
            {
                files.Add(Path.GetFullPath(file));
            }
        }
        return files;
    });

    /// <summary>
    /// The file Vite loads for a path-like specifier that resolved to <paramref name="full"/> (query removed): the exact
    /// file if it exists, else the first of Vite's default extensions that exists, else a directory's <c>index</c> with
    /// the same extensions. A directory that holds a <c>package.json</c> is not followed: its <c>main</c> /
    /// <c>exports</c> can point at any file, so it is REFUSED (<c>null</c> with a non-empty <paramref name="why"/>).
    /// <c>null</c> with an empty <paramref name="why"/> means there is nothing to load: a real import of it fails the
    /// build ("Failed to resolve import"), and a comment that quotes one (library JSDoc examples do) loads nothing.
    /// </summary>
    internal static string? ViteResolvedFile(
        string full, Func<string, bool> fileExists, Func<string, bool> directoryExists, out string why)
    {
        why = string.Empty;
        if (fileExists(full))
        {
            return full; // Vite tries the exact path first, so `./carrier` beside `carrier.ts` loads `carrier`
        }
        foreach (var ext in ViteResolveExtensions)
        {
            if (fileExists(full + ext))
            {
                return full + ext;
            }
        }
        if (!directoryExists(full))
        {
            return null; // nothing to load
        }
        if (fileExists(Path.Combine(full, "package.json")))
        {
            why = "is a directory holding a package.json, whose main / exports this scan does not follow";
            return null;
        }
        foreach (var ext in ViteResolveExtensions)
        {
            var index = Path.Combine(full, "index" + ext);
            if (fileExists(index))
            {
                return index;
            }
        }
        return null; // a directory with no index: nothing to load
    }

    /// <summary>
    /// Round 7 (V7): every path-like module specifier of the file — <c>from '…'</c>, a side-effect <c>import '…'</c>, or
    /// the literal argument of <c>import(…)</c> / <c>require(…)</c> — must land, as Vite resolves it, on a file in
    /// <paramref name="scannedFiles"/>. Otherwise the module behind it is one these scans never read: a carrier with no
    /// extension or an unscanned one could import the grid unseen. Fails closed on anything it cannot resolve. A bare
    /// package is left to the declared-package rule (residual 1). The file-system probes are parameters so the
    /// controls can run on a virtual tree.
    /// </summary>
    internal static IReadOnlyList<string> UnscannedImportTargets(
        string source, string fileName, IReadOnlySet<string> scannedFiles,
        Func<string, bool> fileExists, Func<string, bool> directoryExists)
    {
        var found = new List<string>();
        var specifiers = AnyFrom.Matches(source).Concat(SideEffectImport.Matches(source))
            .Select(m => (m.Index, Spec: m.Groups["spec"].Value))
            .Concat(DynamicCall.Matches(source)
                .Select(call => (call.Index, Arg: DynamicLiteralArgAt.Match(source, call.Index + call.Length)))
                .Where(x => x.Arg.Success)
                .Select(x => (x.Index, Spec: x.Arg.Groups["spec"].Value)));
        foreach (var (index, spec) in specifiers)
        {
            var where = $"{fileName}:{LineOf(source, index)}";
            if (spec.Contains('\\'))
            {
                found.Add($"{where}: '{spec}' holds an escape, so the file it loads cannot be resolved; write it plainly.");
                continue;
            }
            var full = ResolveSpecifier(spec.Split('?', '#')[0], fileName);
            if (full is null)
            {
                continue; // a bare package: the declared-package rule, and residual 1
            }
            var target = ViteResolvedFile(full, fileExists, directoryExists, out var why);
            if (target is null)
            {
                if (why.Length > 0)
                {
                    found.Add($"{where}: '{spec}' {why}. These scans cannot follow it to a file they read, so they refuse "
                              + "it rather than go blind (round 7).");
                }
                // else: nothing to load (a real import of it fails the build; a quoted example loads nothing)
            }
            else if (!scannedFiles.Contains(Path.GetFullPath(target)))
            {
                found.Add($"{where}: '{spec}' resolves to {RepoRelative(target)}, a file these scans do not read (not a "
                          + $"{string.Join("/", ScriptExtensions)} file under the external SPA's or the shared library's src, "
                          + "or a test / node_modules path). A module the scan cannot read could carry the grid unseen.");
            }
        }
        return found;
    }

    /// <summary>
    /// Round 7: a <c>browser</c> field in a <c>package.json</c> makes Vite remap a relative import of that package to
    /// another file, which <see cref="ViteResolvedFile"/> does not model. Neither the SPA's nor the library's manifest
    /// may declare one (neither does).
    /// </summary>
    internal static IReadOnlyList<string> ScanPackageManifest(string json, string fileName)
    {
        using var manifest = System.Text.Json.JsonDocument.Parse(json);
        return manifest.RootElement.TryGetProperty("browser", out _)
            ? [$"{fileName}: declares a \"browser\" field. Vite remaps imports through it to files the round-7 rule does not "
               + "follow; remove it."]
            : [];
    }

    // ── Fix round c1: the runtime rule's two switches, and its text in the shared library ──

    /// <summary>The external SPA's root component: the one place that mounts the host provider.</summary>
    internal const string AppRootFile = "src/client/external-spa/src/App.tsx";

    /// <summary>The external SPA's build configuration: the one place that defines the host constant.</summary>
    internal const string ViteConfigFile = "src/client/external-spa/vite.config.ts";

    /// <summary>The shared library's host module: the context, the build-constant read, and the hook.</summary>
    internal const string HostModuleFile = "src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGridExternalHost.tsx";

    /// <summary>The shared grid itself, which applies the rule.</summary>
    internal const string SharedGridFile = "src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx";

    /// <summary>
    /// The shared library's grid-source resolver. Ontology task 054 (commit f60c36688) moved <c>fetchConfigRecord</c> and
    /// <c>resolveSource</c> here VERBATIM out of <see cref="SharedGridFile"/>, so the savedquery-set discovery call
    /// (<c>retrieveSavedQueriesForEntity</c>) and the configured-view fetch (<c>retrieveSavedQuery</c>) now live in this file.
    /// </summary>
    internal const string GridSourceFile = "src/client/shared/Spaarke.UI.Components/src/components/DataGrid/resolveGridSource.ts";

    private const string HostProvider = "DataGridExternalHostProvider";

    private const string HostConstant = "__SPAARKE_DATAGRID_EXTERNAL_HOST__";

    private static readonly Regex ProviderImport = new(
        @"\bimport\s*\{\s*DataGridExternalHostProvider\s*\}\s*from\s*'@spaarke/ui-components/components/DataGrid/DataGridExternalHost'\s*;",
        RegexOptions.Compiled);

    // From `export const App` to the end of the file: one `return (` whose whole tree is ONE provider element, then the
    // component's closing `};` and `export default App;` with nothing after them.
    private static readonly Regex AppRootReturn = new(
        @"\breturn\s*\(\s*<DataGridExternalHostProvider>(?<inner>[\s\S]*)</DataGridExternalHostProvider>\s*\)\s*;\s*\}\s*;\s*export\s+default\s+App\s*;\s*\z",
        RegexOptions.Compiled);

    private static readonly Regex JsxReturn = new(@"\breturn\s*[(<]", RegexOptions.Compiled);

    private static readonly Regex BrowserRouterOpen = new(@"<\s*BrowserRouter(?![\w$])", RegexOptions.Compiled);

    /// <summary>
    /// The one robust assertion about the context switch: the SPA's root component <c>App</c> returns its WHOLE tree
    /// inside one <c>&lt;DataGridExternalHostProvider&gt;</c> (imported by name from the shared library's host module),
    /// and the app's only <c>&lt;BrowserRouter&gt;</c> is inside it. Every route needs a router, and
    /// <see cref="ScanRouterImports"/> keeps every other router out of the SPA, so every route renders under the
    /// provider.
    /// </summary>
    internal static IReadOnlyList<string> ScanAppRoot(string source)
    {
        var violations = new List<string>();
        if (ProviderImport.Matches(source).Count != 1)
        {
            violations.Add($"{AppRootFile}: must import {{ {HostProvider} }} once, by name, from "
                           + "'@spaarke/ui-components/components/DataGrid/DataGridExternalHost'.");
        }
        var app = Regex.Matches(source, @"\bexport\s+const\s+App\b");
        if (app.Count != 1)
        {
            violations.Add($"{AppRootFile}: must declare exactly one `export const App` (found {app.Count}).");
            return violations;
        }
        var tail = source[app[0].Index..];
        var root = AppRootReturn.Match(tail);
        if (!root.Success || JsxReturn.Matches(tail).Count != 1)
        {
            violations.Add($"{AppRootFile}: App must have ONE JSX return, `return ( <{HostProvider}> … </{HostProvider}> );`, "
                           + "as the last statement before `export default App;`, so the provider sits above every route.");
            return violations;
        }
        var inner = root.Groups["inner"].Value;
        if (inner.Contains(HostProvider, StringComparison.Ordinal))
        {
            violations.Add($"{AppRootFile}: App's tree holds another {HostProvider} tag. ONE provider element must enclose "
                           + "the whole tree (a closed provider followed by more JSX would leave that JSX outside it).");
        }
        if (BrowserRouterOpen.Matches(source).Count != 1 || BrowserRouterOpen.Matches(inner).Count != 1
            || !inner.Contains("</BrowserRouter>", StringComparison.Ordinal))
        {
            violations.Add($"{AppRootFile}: the app's one <BrowserRouter> must be inside the {HostProvider}, so every "
                           + "route renders under it.");
        }
        return violations;
    }

    private static readonly Regex RouterModule = new(@"^react-router(?:-dom)?(?:/|$)", RegexOptions.Compiled);

    /// <summary>
    /// Keeps every OTHER router out of the external SPA, so every route is under the root provider: an import or
    /// re-export from <c>react-router</c> / <c>react-router-dom</c> may not bind a router (any name containing
    /// <c>Router</c>, e.g. <c>MemoryRouter</c>, <c>RouterProvider</c>, <c>createBrowserRouter</c>), except
    /// <c>BrowserRouter</c>, unaliased, in <see cref="AppRootFile"/>. Namespace, default and star forms and dynamic
    /// loads of the router package are refused (they would bind a router under a name this rule cannot read).
    /// </summary>
    internal static IReadOnlyList<string> ScanRouterImports(string source, string fileName)
    {
        var violations = new List<string>();
        string Where(int index) => $"{fileName}:{LineOf(source, index)}";
        foreach (Match statement in AnyStaticImport.Matches(source).Concat(AnyReExport.Matches(source)))
        {
            var module = statement.Groups["module"].Value;
            if (!RouterModule.IsMatch(module))
            {
                continue;
            }
            var clauseText = statement.Groups["clause"].Value.Trim();
            if (IsTypeOnlyClause(clauseText))
            {
                continue;
            }
            var clause = ImportClause.Match(clauseText);
            if (!clause.Success || clause.Groups["ns"].Success || clause.Groups["default"].Success || clauseText.StartsWith('*'))
            {
                violations.Add($"{Where(statement.Index)}: '{clauseText}' from '{module}'. Import from the router package by "
                               + "plain names only, so no second router can reach the SPA outside the host provider.");
                continue;
            }
            foreach (var raw in clause.Groups["names"].Value.Split(','))
            {
                if (string.IsNullOrWhiteSpace(raw) || IsTypeOnlyClause(raw.Trim()))
                {
                    continue;
                }
                var specifier = ImportSpecifier.Match(raw);
                if (!specifier.Success)
                {
                    violations.Add($"{Where(statement.Index)}: router import '{raw.Trim()}' could not be parsed.");
                    continue;
                }
                var imported = specifier.Groups["imported"].Value;
                if (!imported.Contains("Router", StringComparison.Ordinal))
                {
                    continue;
                }
                var sanctioned = fileName == AppRootFile && imported == "BrowserRouter" && !specifier.Groups["local"].Success;
                if (!sanctioned)
                {
                    violations.Add($"{Where(statement.Index)}: '{raw.Trim()}' from '{module}'. Only App.tsx may bind a router "
                                   + $"(BrowserRouter, inside the {HostProvider}); a second router would put routes outside it.");
                }
            }
        }
        foreach (Match call in DynamicCall.Matches(source))
        {
            var arg = DynamicLiteralArgAt.Match(source, call.Index + call.Length);
            if (arg.Success && RouterModule.IsMatch(arg.Groups["spec"].Value))
            {
                violations.Add($"{Where(call.Index)}: dynamic import/require of the router package. Its routers could be "
                               + "bound under any name; import from it statically by name.");
            }
        }
        return violations;
    }

    private static readonly Regex DefineBlock = new(@"\bdefine\s*:\s*\{(?<body>[^{}]*)\}", RegexOptions.Compiled);

    private static readonly Regex HostConstantTrue = new(
        @"(?<![\w$])__SPAARKE_DATAGRID_EXTERNAL_HOST__\s*:\s*(?<q>['""])true\k<q>", RegexOptions.Compiled);

    /// <summary>
    /// The build switch: <see cref="ViteConfigFile"/> has ONE <c>define: { … }</c> block that maps the host constant to
    /// <c>'true'</c>, and names the constant nowhere else (a second mention, in a plugin's config hook or a mode
    /// branch, could redefine it).
    /// </summary>
    internal static IReadOnlyList<string> ScanViteConfig(string rawSource)
    {
        // A config file, not JSX: comments can be dropped safely here, so a commented-out define does not count.
        var source = WithoutJsComments(rawSource);
        var violations = new List<string>();
        var blocks = DefineBlock.Matches(source);
        if (blocks.Count != 1 || !HostConstantTrue.IsMatch(blocks[0].Groups["body"].Value))
        {
            violations.Add($"{ViteConfigFile}: must have one `define: {{ {HostConstant}: 'true' }}` block. It switches every "
                           + "copy of the shared DataGrid in the external SPA bundle to the external host (no view picker, no "
                           + "saved-query list).");
        }
        var mentions = Regex.Matches(source, Regex.Escape(HostConstant)).Count;
        if (mentions != 1)
        {
            violations.Add($"{ViteConfigFile}: names {HostConstant} {mentions} times in code. Name it once, in the define "
                           + "block: another mention (a plugin config hook, a mode branch) could redefine it.");
        }
        return violations;
    }

    /// <summary>
    /// <paramref name="source"/> with its <c>//</c> and <c>/* */</c> comments blanked (newlines kept) and its string
    /// literals kept as they are. For a plain config file only: inside JSX children <c>//</c> is text, which is why the
    /// SPA scan above never strips comments.
    /// </summary>
    internal static string WithoutJsComments(string source)
    {
        var text = new System.Text.StringBuilder(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (c is '\'' or '"' or '`')
            {
                var j = i + 1;
                while (j < source.Length && source[j] != c)
                {
                    j += source[j] == '\\' ? 2 : 1;
                }
                var end = Math.Min(j, source.Length - 1);
                text.Append(source, i, end - i + 1);
                i = end;
                continue;
            }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] is not ('\n' or '\r') && source[i] != (char)0x2028 && source[i] != (char)0x2029)
                {
                    text.Append(' ');
                    i++;
                }
                if (i < source.Length)
                {
                    text.Append(source[i]);
                }
                continue;
            }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var close = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                var end = close < 0 ? source.Length : close + 2;
                for (var k = i; k < end; k++)
                {
                    text.Append(source[k] == '\n' ? '\n' : ' ');
                }
                i = end - 1;
                continue;
            }
            text.Append(c);
        }
        return text.ToString();
    }

    /// <summary>
    /// The host module's text, pinned (whitespace-insensitive, like the wrapper): the context defaults to <c>false</c>,
    /// the provider sets <c>true</c>, the build constant is read through a <c>typeof</c> guard, and the hook ORs them.
    /// </summary>
    private const string PinnedHostSource = """
        /**
         * DataGridExternalHost — the external-SPA rule of the shared `<DataGrid>` (unified-access-control-r2 task 157,
         * owner round 4 item 7).
         *
         * Inside the external SPA (outside counsel over CIAM, and the Teams tab over workforce SSO) a grid must offer
         * NO view picker and must never fetch the entity's saved-query list. The picker lists the entity's INTERNAL
         * MDA views, and the BFF's external column allow-lists (ExternalAccessModule.cs) admit only each grid's own
         * configured columns, so a sibling view either errors or creates pressure to widen those lists. The rule lives
         * HERE, inside the shared grid, so it holds by construction: no prop, wrapper, clone, spread or import path
         * that reaches the grid can turn the picker back on. `DataGrid` reads {@link useDataGridExternalHost} and, when
         * it is true, ignores `showViewSelector`, `externalViews` and any picked view, and never calls
         * `retrieveSavedQueriesForEntity`.
         *
         * Two independent switches, either one is enough:
         *   1. {@link DataGridExternalHostProvider} — a React context the external SPA's root component mounts above
         *      every route (`src/client/external-spa/src/App.tsx`).
         *   2. A build-time constant, `__SPAARKE_DATAGRID_EXTERNAL_HOST__`, that the external SPA's `vite.config.ts`
         *      defines as `true`. Vite replaces it in every module it bundles, this one included, so every copy of the
         *      grid in that bundle is external whatever tree, root or module instance renders it.
         *
         * Both default to OFF. No other host mounts the provider or defines the constant, so no other host changes
         * behaviour: internal surfaces keep the picker.
         *
         * ExternalSpaGridViewSelectorGuardTests (tests/Spaarke.ArchTests) asserts the provider mount and the define.
         */
        import * as React from 'react';

        /**
         * Defined as `true` by the external SPA's Vite build (`define`). In every other host it is never defined, so
         * the `typeof` guard below reads it as off without a ReferenceError.
         */
        declare const __SPAARKE_DATAGRID_EXTERNAL_HOST__: boolean | undefined;

        const DataGridExternalHostContext = React.createContext<boolean>(false);

        /** True when this bundle was built by the external SPA (its `vite.config.ts` defines the constant). */
        export function isDataGridExternalHostBuild(): boolean {
          return typeof __SPAARKE_DATAGRID_EXTERNAL_HOST__ !== 'undefined' && __SPAARKE_DATAGRID_EXTERNAL_HOST__ === true;
        }

        /** Props for {@link DataGridExternalHostProvider}. */
        export interface DataGridExternalHostProviderProps {
          children?: React.ReactNode;
        }

        /**
         * Marks everything below it as the external SPA: every `<DataGrid>` in the subtree renders with no view picker
         * and never requests the saved-query list. Mount it once, at the external SPA's root, above every route.
         */
        export const DataGridExternalHostProvider: React.FC<DataGridExternalHostProviderProps> = ({ children }) => (
          <DataGridExternalHostContext.Provider value={true}>{children}</DataGridExternalHostContext.Provider>
        );

        /** True when the calling grid renders inside the external SPA (the provider above it, or the external build). */
        export function useDataGridExternalHost(): boolean {
          const insideProvider = React.useContext(DataGridExternalHostContext);
          return insideProvider || isDataGridExternalHostBuild();
        }
        """;

    /// <summary>
    /// The statements of <see cref="SharedGridFile"/> that apply the rule (compared whitespace-insensitively). Each one is
    /// what the jest suite DataGrid.externalHost.test.tsx exercises; the counts below keep a second path from appearing
    /// beside them.
    /// </summary>
    private static readonly string[] RequiredGridStatements =
    [
        "import { useDataGridExternalHost } from './DataGridExternalHost';",
        "showViewSelector: showViewSelectorProp = true,",
        "const externalHost = useDataGridExternalHost();",
        "const showViewSelector = !externalHost && showViewSelectorProp;",
        "if (externalHost && configRecord?.source?.type === 'savedquery-set') {",
        "import { fetchConfigRecord, resolveSource } from './resolveGridSource';",
        ": await resolveSource(dataverseClient, configRecord, undefined, externalHost ? 'external' : 'internal');",
        "const pickedViewId = externalHost ? undefined : activeSavedQueryId;",
        "showViewSelector ? dataverseClient.retrieveSavedQueriesForEntity(entityName)",
        "{showViewSelector && externalViews && externalViews.views.length > 0 ? (",
        ": showViewSelector && selectorViews.length > 0 ? (",
    ];

    /// <summary>
    /// The statements of <see cref="GridSourceFile"/> that hold the savedquery-set discovery: the REQUIRED <c>host</c>
    /// parameter, and the branch that returns null for <c>'external'</c> before its <c>try</c>. That refusal is why
    /// <c>resolveSource</c>, exported from the package for ontology 054's card, cannot list saved views for an external caller
    /// that passes its host; the card passes <c>'internal'</c> explicitly. These are presence and count pins, not scope pins:
    /// hoisting the discovery line above the branch passes the scan (it would run for every source type, but only on an
    /// internal host once the refusal is in place); <c>DataGrid.externalHost.test.tsx</c> and
    /// <c>resolveGridSource.host.test.ts</c> catch behaviour, and the jest gate runs them in Tier 1.
    /// </summary>
    private static readonly string[] RequiredResolverStatements =
    [
        "fallbackEntityName: string | undefined, host: GridSourceHost",
        "if (source.type === 'savedquery-set') { if (host === 'external') return null; try {",
        "const queries = await dataverseClient.retrieveSavedQueriesForEntity(source.entityLogicalName);",
    ];

    /// <summary>
    /// The runtime rule's text in the shared library: the host module verbatim, the grid's statements present, and
    /// exactly as many uses of the raw prop, the list call, the picker element and the hook as the rule needs. It
    /// checks statements and counts, not scopes: an ADDED nested-scope shadow of the forced value (seed J4,
    /// <c>const showViewSelector = true;</c> in the load effect) passes it. That is residual 4 in the class remarks,
    /// reported by the CI run of the jest suite, advisory until its flip (<see cref="SharedDataGrid_ExternalHostJestSuiteRunsAsATier1Gate"/>).
    /// </summary>
    internal static IReadOnlyList<string> ScanSharedGridRule(string gridSource, string hostSource, string gridSourceResolver)
    {
        var violations = new List<string>();
        if (Squash(hostSource) != Squash(PinnedHostSource))
        {
            violations.Add($"{HostModuleFile} changed. It is the external-host switch every shared DataGrid reads (context "
                           + "default false, provider true, the build constant behind a typeof guard, OR'd by the hook). If the "
                           + "change is deliberate and keeps that, update PinnedHostSource in the same change.");
        }
        var squashedGrid = Squash(gridSource);
        foreach (var statement in RequiredGridStatements)
        {
            if (!squashedGrid.Contains(Squash(statement), StringComparison.Ordinal))
            {
                violations.Add($"{SharedGridFile}: missing `{statement}`. It is part of the external-host rule (no picker, "
                               + "no saved-query list on the external SPA).");
            }
        }
        void ExpectCount(string pattern, int expected, string what)
        {
            var count = Regex.Matches(gridSource, pattern).Count;
            if (count != expected)
            {
                violations.Add($"{SharedGridFile}: {what} occurs {count} times, expected {expected}. Another use could bypass "
                               + "the external-host rule; route it through the pinned statements.");
            }
        }
        // Ontology 054 moved the grid-source resolution into GridSourceFile. The two view-retrieval calls are counted over
        // the UNION of the two files (an exact total, not a glob), and the resolver's host parameter and its external
        // refusal are pinned (presence, not scope). The grid refuses a savedquery-set source before it calls resolveSource,
        // ignores any picked view on the external host, and passes its host (statements above); resolveSource itself
        // returns null for 'external' before it lists.
        void ExpectUnionCount(string pattern, int expected, string what)
        {
            var count = Regex.Matches(gridSource, pattern).Count + Regex.Matches(gridSourceResolver, pattern).Count;
            if (count != expected)
            {
                violations.Add($"{SharedGridFile} + {GridSourceFile}: {what} occurs {count} times across the two files, expected "
                               + $"{expected}. Another use could bypass the external-host rule; route it through the pinned statements.");
            }
        }
        var squashedResolver = Squash(gridSourceResolver);
        foreach (var statement in RequiredResolverStatements)
        {
            if (!squashedResolver.Contains(Squash(statement), StringComparison.Ordinal))
            {
                violations.Add($"{GridSourceFile}: missing `{statement}`. The grid refuses a savedquery-set source on the external "
                               + "host before it reaches this branch, and resolveSource refuses 'external' itself; the pinned text keeps both.");
            }
        }
        ExpectCount(@"(?<![\w$])showViewSelectorProp(?![\w$])", 2, "the raw showViewSelector prop (showViewSelectorProp)");
        ExpectUnionCount(@"(?<![\w$])retrieveSavedQueriesForEntity\s*\(", 2, "a retrieveSavedQueriesForEntity( call");
        ExpectCount(@"(?<![\w$])retrieveSavedQueriesForEntity\s*\(", 1, "a retrieveSavedQueriesForEntity( call (the sibling list)");
        ExpectUnionCount(@"(?<![\w$])retrieveSavedQuery\s*\(", 3, "a retrieveSavedQuery( call");
        ExpectCount(@"(?<![\w$])resolveSource\s*\(", 1, "a resolveSource( call");
        ExpectCount(@"(?<![\w$])fetchConfigRecord\s*\(", 1, "a fetchConfigRecord( call");
        ExpectCount(@"<\s*ViewSelector(?![\w$])", 2, "a <ViewSelector element");
        ExpectCount(@"(?<![\w$])useDataGridExternalHost(?![\w$])", 2, "useDataGridExternalHost");
        ExpectCount(@"(?<![\w$])props\s*(?:\??\.|\[)", 0, "a direct props member access (props.x / props[...])");
        ExpectCount(@"retrieveSavedQuery\s*\(\s*activeSavedQueryId", 0, "retrieveSavedQuery(activeSavedQueryId");
        return violations;
    }

    [Fact(DisplayName = "The external SPA's root component mounts DataGridExternalHostProvider above every route")]
    public void ExternalSpa_RootMountsTheExternalHostProviderAboveEveryRoute()
    {
        var path = Path.Combine(SourceScan.RepoRoot, AppRootFile.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"the external SPA's root component must exist at {AppRootFile}");

        var violations = new List<string>(ScanAppRoot(File.ReadAllText(path)));
        foreach (var file in ScriptFilesUnder(ExternalSpaSource))
        {
            violations.AddRange(ScanRouterImports(File.ReadAllText(file), RepoRelative(file)));
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "The external SPA's Vite build defines __SPAARKE_DATAGRID_EXTERNAL_HOST__ as true")]
    public void ExternalSpa_ViteBuildDefinesTheExternalHostConstant()
    {
        var path = Path.Combine(SourceScan.RepoRoot, ViteConfigFile.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"the external SPA's build configuration must exist at {ViteConfigFile}");

        var violations = ScanViteConfig(File.ReadAllText(path));

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact(DisplayName = "The shared DataGrid applies the external-host rule (pinned host module and grid statements)")]
    public void SharedDataGrid_EnforcesTheExternalHostRule()
    {
        var grid = Path.Combine(SourceScan.RepoRoot, SharedGridFile.Replace('/', Path.DirectorySeparatorChar));
        var host = Path.Combine(SourceScan.RepoRoot, HostModuleFile.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(grid), $"the shared grid must exist at {SharedGridFile}");
        Assert.True(File.Exists(host), $"the host module must exist at {HostModuleFile}");

        var resolver = Path.Combine(SourceScan.RepoRoot, GridSourceFile.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(resolver), $"the grid-source resolver must exist at {GridSourceFile}");

        var violations = ScanSharedGridRule(File.ReadAllText(grid), File.ReadAllText(host), File.ReadAllText(resolver));

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    internal const string Tier1WorkflowFile = ".github/workflows/ci-tier1-blocking.yml";

    internal const string DataGridGateJob = "datagrid-external-host-gate";

    /// <summary>
    /// Owner round 13 item 11 (2026-10-03): the gate lands ADVISORY (job-level <c>continue-on-error: true</c>) and
    /// becomes blocking after three consecutive green runs on ubuntu-latest (the FLIP CONDITION comment on the job in
    /// <c>ci-tier1-blocking.yml</c>; the procedure is in the task 157 note, section 7, "Fix round c2-r2"). The flip is ONE
    /// change: delete that line from the job AND set this constant to <c>false</c>. The guard refuses either without
    /// the other, so the gate cannot be made advisory again by a one-line YAML edit once flipped, and cannot be flipped
    /// without a reviewed change here.
    /// </summary>
    internal static readonly bool DataGridGateAdvisory = true; // static readonly, not const: a const makes one branch unreachable (CS0162)

    /// <summary>The job's only <c>needs:</c>. <c>classify-tier1</c> always runs (no job-level <c>if:</c>, pinned below).</summary>
    internal const string DataGridGateNeeds = "classify-tier1";

    /// <summary>
    /// The job's <c>if:</c>, pinned. Fail closed: the gate runs unless <c>classify-tier1</c> classified it out with an
    /// explicit <c>'false'</c> (an empty output runs it), and always on a manual <c>workflow_dispatch</c>.
    /// </summary>
    internal const string DataGridGateIf =
        "${{ github.event_name == 'workflow_dispatch' || needs.classify-tier1.outputs.datagrid_gate != 'false' }}";

    /// <summary>
    /// The paths <c>classify-tier1</c>'s <c>datagrid_gate</c> filter must hold, exactly: the shared library, and every
    /// workflow (so an edit to the gate job runs the gate). Classified inside Tier 1 because owner round 13 item 12
    /// leaves <c>ci-router.yml</c> unchanged by this project.
    /// </summary>
    internal static readonly string[] DataGridGateFilterPaths =
        ["'src/client/shared/Spaarke.UI.Components/**'", "'.github/workflows/**'"];

    /// <summary>
    /// The only job-level keys <c>datagrid-external-host-gate</c> may carry. Any other is refused: <c>strategy:</c>,
    /// <c>environment:</c> and the rest change whether or when the gate can fail. <c>needs:</c>, <c>if:</c> and
    /// <c>continue-on-error:</c> are allowed only with their pinned values. Adding a key is a reviewed change to this list.
    /// </summary>
    internal static readonly string[] DataGridGateJobKeys = ["name", "runs-on", "timeout-minutes", "needs", "if", "continue-on-error", "steps"];

    internal const string DataGridGateAssertStep = "Assert the external-host suite ran";

    /// <summary>
    /// The suite-assertion step's script, pinned verbatim (each line compared trimmed, blank lines ignored). Any edit,
    /// e.g. its condition rewritten to <c>if (false)</c> or an exit code changed, is refused, because a weakened
    /// assertion lets a renamed, deleted or <c>it.skip</c>'d external-host suite pass again. Changing it is a reviewed
    /// change to this constant.
    /// </summary>
    internal const string DataGridGateAssertScript = """
        node -e '
          const [file, suffix, min] = process.argv.slice(1);
          const r = require(require("path").resolve(file));
          const s = r.testResults.find(t => t.name.split(String.fromCharCode(92)).join("/").endsWith(suffix));
          if (!s) { console.log("::error::" + suffix + " did not run"); process.exit(1); }
          const n = s.assertionResults.length;
          const passed = s.assertionResults.filter(a => a.status === "passed").length;
          if (n < Number(min) || passed !== n) {
            console.log("::error::" + suffix + ": " + passed + " of " + n + " passed (at least " + min + ", all passing, required)");
            process.exit(1);
          }
          console.log(suffix + ": " + passed + " of " + n + " passed");
        ' datagrid-jest-results.json src/components/DataGrid/__tests__/DataGrid.externalHost.test.tsx 7
        """;

    /// <summary>Splits a job's code (comment lines removed) into its steps, each re-indented so its keys sit at 8 spaces.</summary>
    private static List<string> SplitSteps(string jobCode)
    {
        var steps = new List<string>();
        var at = Regex.Match(jobCode, @"(?m)^    steps:[ \t]*$");
        if (!at.Success)
        {
            return steps;
        }

        foreach (var chunk in Regex.Split(jobCode[(at.Index + at.Length)..], @"(?m)^      - ").Skip(1))
        {
            steps.Add("        " + chunk);
        }

        return steps;
    }

    /// <summary>The trimmed, non-blank lines of a <c>run: |</c> block whose key sits at 8 spaces; null when absent.</summary>
    private static List<string>? RunBlockLines(string stepText)
    {
        var lines = stepText.Split('\n');
        var start = Array.FindIndex(lines, line => Regex.IsMatch(line, @"^        run:\s*\|\s*$"));
        if (start < 0)
        {
            return null;
        }

        var result = new List<string>();
        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0)
            {
                continue;
            }

            if (line.Length - line.TrimStart().Length <= 8)
            {
                break;
            }

            result.Add(line.Trim());
        }

        return result;
    }

    /// <summary>A top-level job of a workflow: its body up to the next job, and that body with comment lines removed.</summary>
    private static (bool Found, string Code) JobCode(string workflow, string jobName)
    {
        var job = Regex.Match(workflow, @"(?m)^  " + Regex.Escape(jobName) + @":[ \t]*\n(?<body>(?:(?:[ \t]*#.*|[ \t]*|    .*)\n)*)");
        return job.Success
            ? (true, string.Join("\n", job.Groups["body"].Value.Split('\n').Where(line => !line.TrimStart().StartsWith('#'))))
            : (false, string.Empty);
    }

    /// <summary>A job-level <c>key:</c> value with any trailing <c># comment</c> removed; one entry per occurrence.</summary>
    private static List<string> JobLevelValues(string jobCode, string key) =>
        Regex.Matches(jobCode, @"(?m)^    " + Regex.Escape(key) + @"\s*:(?<v>.*)$")
            .Select(m => Regex.Replace(m.Groups["v"].Value, @"\s+#.*$", string.Empty).Trim())
            .ToList();

    /// <summary>
    /// Residual 4 (task 157 fix round c2, hardened in c2-r1, made advisory-then-blocking in c2-r2 per owner round 13
    /// items 11 and 12): the jest suite <c>DataGrid.externalHost.test.tsx</c>, the only check that catches an added
    /// nested-scope shadow in <c>DataGrid.tsx</c> (seed J4), runs in Tier 1. It is ADVISORY until its flip
    /// (<see cref="DataGridGateAdvisory"/>), then blocking. This reads <c>ci-tier1-blocking.yml</c> as text and refuses
    /// these ways of making the gate stop running, or stop being able to report a failure, without a test failing: the
    /// job, its <c>jest</c> call or its suite assertion removed; the assertion script changed at all (pinned verbatim);
    /// a job-level <c>continue-on-error</c> that disagrees with <see cref="DataGridGateAdvisory"/> (exactly one
    /// <c>true</c> while advisory, none after the flip); a <c>continue-on-error</c> anywhere else in the job;
    /// <c>|| true</c>; any job-level key outside <see cref="DataGridGateJobKeys"/>; its <c>needs:</c> or job-level
    /// <c>if:</c> changed; an <c>if:</c> on any step except <c>always()</c>, or <c>failure()</c> on the artifact upload;
    /// <c>classify-tier1</c> gaining a job-level <c>if:</c>; or its <c>datagrid_gate</c> output or filter changed. It
    /// reads nothing in <c>ci-router.yml</c>: owner round 13 item 12 leaves the router (and its <c>docs_only</c>
    /// classification, every-file since ontology task 081 round 8) to <c>ci-cd-unit-test-remediation-r1</c>. Crude by design: line and regex checks over
    /// YAML, not a YAML parse. It refuses the disarmings it names, each proven by a row of
    /// <see cref="DataGridCiGateSeeds"/> or by
    /// <see cref="ScanDataGridCiGate_RefusesAnAdvisoryStateTheConstantDoesNotRecord"/>, not every conceivable one.
    /// </summary>
    internal static IReadOnlyList<string> ScanDataGridCiGate(string tier1Yaml)
    {
        var violations = new List<string>();
        var tier1 = tier1Yaml.Replace("\r\n", "\n");

        var (found, code) = JobCode(tier1, DataGridGateJob);
        if (!found)
        {
            violations.Add($"{Tier1WorkflowFile}: the job `{DataGridGateJob}` is missing. It is the CI run of the shared "
                           + "DataGrid's external-host jest suite (task 157 residual 4).");
            return violations;
        }

        // continue-on-error: exactly one job-level `true` while advisory, none once flipped, never anywhere else.
        var jobLevelCoe = JobLevelValues(code, "continue-on-error");
        if (DataGridGateAdvisory && (jobLevelCoe.Count != 1 || jobLevelCoe[0] != "true"))
        {
            violations.Add($"{Tier1WorkflowFile}: `{DataGridGateJob}` must carry exactly one job-level `continue-on-error: true` "
                           + "while DataGridGateAdvisory is true (owner round 13 item 11). To make the gate blocking, delete that "
                           + "line AND set DataGridGateAdvisory to false in the same change (the job's FLIP CONDITION).");
        }
        else if (!DataGridGateAdvisory && jobLevelCoe.Count != 0)
        {
            violations.Add($"{Tier1WorkflowFile}: `{DataGridGateJob}` carries continue-on-error, but DataGridGateAdvisory is false: "
                           + "the gate was flipped to blocking and must be able to fail the build.");
        }

        if (Regex.Matches(code, @"(?m)^\s*(?:-\s+)?continue-on-error\s*:").Count != jobLevelCoe.Count)
        {
            violations.Add($"{Tier1WorkflowFile}: `{DataGridGateJob}` carries a step-level continue-on-error; a step's failure "
                           + "would then never reach the job's result.");
        }

        if (Regex.IsMatch(code, @"\|\|\s*(?:true\b|exit\s+0\b|:(?=\s|$))", RegexOptions.Multiline))
        {
            violations.Add($"{Tier1WorkflowFile}: `{DataGridGateJob}` swallows a failure with `|| true` (or similar).");
        }

        foreach (Match key in Regex.Matches(code, @"(?m)^    (?<key>[^\s:#][^:\n]*?)\s*:"))
        {
            if (!DataGridGateJobKeys.Contains(key.Groups["key"].Value, StringComparer.Ordinal))
            {
                violations.Add($"{Tier1WorkflowFile}: `{DataGridGateJob}` carries the job-level key `{key.Groups["key"].Value}:`; only "
                               + string.Join(", ", DataGridGateJobKeys) + " are allowed.");
            }
        }

        var needs = JobLevelValues(code, "needs");
        if (needs.Count != 1 || needs[0] != DataGridGateNeeds)
        {
            violations.Add($"{Tier1WorkflowFile}: `{DataGridGateJob}` must carry exactly `needs: {DataGridGateNeeds}` (a `needs:` on a "
                           + "job that is skipped, e.g. changed-surface-smoke, skips the gate).");
        }

        var ifs = JobLevelValues(code, "if");
        if (ifs.Count != 1 || ifs[0] != DataGridGateIf)
        {
            violations.Add($"{Tier1WorkflowFile}: `{DataGridGateJob}` must carry exactly `if: {DataGridGateIf}` "
                           + "(fail closed: an empty classification runs the gate).");
        }

        var steps = SplitSteps(code);
        foreach (var step in steps)
        {
            var stepName = Regex.Match(step, @"(?m)^        name:\s*(?<n>.*?)\s*$").Groups["n"].Value;
            foreach (Match stepIf in Regex.Matches(step, @"(?m)^        if\s*:\s*(?<cond>.*)$"))
            {
                var cond = Regex.Replace(stepIf.Groups["cond"].Value, @"\s+#.*$", string.Empty).Trim();
                var isUpload = Regex.IsMatch(step, @"(?m)^        uses:\s*actions/upload-artifact@") && !Regex.IsMatch(step, @"(?m)^        run\s*:");
                if (cond != "always()" && !(cond == "failure()" && isUpload))
                {
                    violations.Add($"{Tier1WorkflowFile}: `{DataGridGateJob}` step `{stepName}` carries `if: {cond}`; a step may carry only "
                                   + "`always()`, or `failure()` on the artifact upload, or a false condition skips the gate's work while the job stays green.");
                }
            }
        }

        if (!Regex.IsMatch(code, @"(?m)^\s*working-directory:\s*src/client/shared/Spaarke\.UI\.Components\s*\n\s*run:\s*npx jest --ci\b[^\n]*\ssrc/components/DataGrid/(?:\s|$)"))
        {
            violations.Add($"{Tier1WorkflowFile}: `{DataGridGateJob}` no longer runs `npx jest --ci … src/components/DataGrid/` in "
                           + "src/client/shared/Spaarke.UI.Components.");
        }

        var assertStep = steps.FirstOrDefault(step => Regex.IsMatch(step, @"(?m)^        name:\s*" + Regex.Escape(DataGridGateAssertStep) + @"\s*$"));
        var assertLines = assertStep is null ? null : RunBlockLines(assertStep);
        var expectedAssert = DataGridGateAssertScript.Replace("\r\n", "\n").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
        if (assertLines is null || !assertLines.SequenceEqual(expectedAssert, StringComparer.Ordinal))
        {
            violations.Add($"{Tier1WorkflowFile}: `{DataGridGateJob}` step `{DataGridGateAssertStep}` must run the pinned assertion script "
                           + "verbatim (DataGridGateAssertScript): DataGrid.externalHost.test.tsx ran, with at least 7 tests, all passing. "
                           + "A weakened assertion lets a renamed, deleted or skipped suite pass.");
        }

        // The classification the job's `if:` reads. classify-tier1 always runs; an `if:` on it would skip the gate.
        var (classifyFound, classify) = JobCode(tier1, DataGridGateNeeds);
        if (!classifyFound || JobLevelValues(classify, "if").Count != 0)
        {
            violations.Add($"{Tier1WorkflowFile}: `{DataGridGateNeeds}` must exist and carry no job-level `if:`; when it is skipped, "
                           + $"`{DataGridGateJob}` is skipped too.");
        }

        if (Regex.Matches(classify, @"(?m)^      datagrid_gate:[^\n]*$").Count != 1
            || !Regex.IsMatch(classify, @"(?m)^      datagrid_gate: \$\{\{ steps\.filter\.outputs\.datagrid_gate \}\}\s*$"))
        {
            violations.Add($"{Tier1WorkflowFile}: `{DataGridGateNeeds}` must output `datagrid_gate: ${{{{ steps.filter.outputs.datagrid_gate }}}}`, once.");
        }

        var filters = Regex.Matches(classify, @"(?m)^            datagrid_gate:[ \t]*\n(?<items>(?:              [^\n]*\n)*)");
        var items = filters.Count == 1
            ? filters[0].Groups["items"].Value.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList()
            : new List<string>();
        var expectedItems = DataGridGateFilterPaths.Select(path => "- " + path).ToList();
        if (filters.Count != 1 || !items.SequenceEqual(expectedItems, StringComparer.Ordinal))
        {
            violations.Add($"{Tier1WorkflowFile}: `{DataGridGateNeeds}`'s paths filter `datagrid_gate` must be defined once and hold exactly "
                           + string.Join(" and ", DataGridGateFilterPaths) + ".");
        }

        return violations;
    }

    [Fact(DisplayName = "The shared DataGrid's external-host jest suite runs as a Tier 1 gate, advisory until its flip (task 157 residual 4)")]
    public void SharedDataGrid_ExternalHostJestSuiteRunsAsATier1Gate()
    {
        var tier1 = Path.Combine(SourceScan.RepoRoot, Tier1WorkflowFile.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(tier1), $"the Tier 1 workflow must exist at {Tier1WorkflowFile}");
        var suite = Path.Combine(SourceScan.RepoRoot, "src", "client", "shared", "Spaarke.UI.Components", "src", "components",
            "DataGrid", "__tests__", "DataGrid.externalHost.test.tsx");
        Assert.True(File.Exists(suite), "the gated jest suite must exist at …/DataGrid/__tests__/DataGrid.externalHost.test.tsx");

        var violations = ScanDataGridCiGate(File.ReadAllText(tier1));

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    private static string RealTier1Text() =>
        File.ReadAllText(Path.Combine(SourceScan.RepoRoot, Tier1WorkflowFile.Replace('/', Path.DirectorySeparatorChar))).Replace("\r\n", "\n");

    private const string GateIfLine = "    if: " + DataGridGateIf + "\n";

    public static TheoryData<string, string, string> DataGridCiGateSeeds() => new()
    {
        // (name, find, replace) applied to the REAL tier1 text; each must produce a violation. The fact above proves the
        // real text scans clean, so any violation here is the seed's. Every row holds in both states of
        // DataGridGateAdvisory; the state-specific disarming is the fact below.
        { "job-renamed", "  datagrid-external-host-gate:\n", "  datagrid-gate-renamed:\n" },
        { "job-continue-on-error-duplicated", "    timeout-minutes: 15\n", "    timeout-minutes: 15\n    continue-on-error: true\n" },
        { "step-continue-on-error", "      - name: DataGrid jest folder (external-host rule)\n", "      - name: DataGrid jest folder (external-host rule)\n        continue-on-error: true\n" },
        { "or-true", "src/components/DataGrid/\n", "src/components/DataGrid/ || true\n" },
        { "if-false", GateIfLine, "    if: ${{ false }}\n" },
        { "if-fails-open-to-skip", GateIfLine, GateIfLine.Replace("!= 'false'", "== 'true'", StringComparison.Ordinal) },
        { "if-duplicated", GateIfLine, GateIfLine + "    if: ${{ false }}\n" },
        // `needs: classify-tier1` alone is not unique (changed-surface-smoke and auth-smoke carry it), so the gate's own
        // `if:` line anchors these two rows to the gate job.
        { "needs-skipped-job", "    needs: classify-tier1\n" + GateIfLine, "    needs: changed-surface-smoke\n" + GateIfLine },
        { "needs-second-job", "    needs: classify-tier1\n" + GateIfLine, "    needs: [classify-tier1, changed-surface-smoke]\n" + GateIfLine },
        { "environment-added", "    timeout-minutes: 15\n", "    timeout-minutes: 15\n    environment: manual-approval\n" },
        { "jest-narrowed", "--outputFile=datagrid-jest-results.json src/components/DataGrid/\n", "--outputFile=datagrid-jest-results.json src/components/DataGrid/chips/\n" },
        { "assert-dropped", "DataGrid.externalHost.test.tsx 7", "DataGrid.externalHost.test.tsx 0" },
        { "step-if-jest", "      - name: DataGrid jest folder (external-host rule)\n", "      - name: DataGrid jest folder (external-host rule)\n        if: github.event_name == 'never'\n" },
        { "step-if-assert", "      - name: Assert the external-host suite ran\n", "      - name: Assert the external-host suite ran\n        if: github.event_name == 'never'\n" },
        { "step-if-install", "      - name: Install Spaarke.UI.Components deps\n", "      - name: Install Spaarke.UI.Components deps\n        if: github.event_name == 'never'\n" },
        { "step-if-on-dash-line", "      - name: DataGrid jest folder (external-host rule)\n", "      - if: github.event_name == 'never'\n        name: DataGrid jest folder (external-host rule)\n" },
        { "failure-on-jest", "      - name: DataGrid jest folder (external-host rule)\n", "      - name: DataGrid jest folder (external-host rule)\n        if: failure()\n" },
        { "assert-condition", "if (n < Number(min) || passed !== n) {", "if (false) {" },
        { "assert-exit-zero", "\" did not run\"); process.exit(1); }", "\" did not run\"); process.exit(0); }" },
        { "assert-line-added", "            const n = s.assertionResults.length;\n", "            process.exit(0);\n            const n = s.assertionResults.length;\n" },
        // Fix round c2-r2: the classification moved from ci-router.yml into classify-tier1 (owner round 13 item 12).
        { "classify-output-forced", "      datagrid_gate: ${{ steps.filter.outputs.datagrid_gate }}\n", "      datagrid_gate: 'false'\n" },
        { "classify-filter-narrowed", "              - 'src/client/shared/Spaarke.UI.Components/**'\n", "              - 'src/client/shared/Spaarke.UI.Components/README.md'\n" },
        { "classify-filter-workflows-dropped", "              - '.github/workflows/**'\n", string.Empty },
        { "classify-job-if", "  classify-tier1:\n    name: Classify Tier 1 Surfaces\n", "  classify-tier1:\n    name: Classify Tier 1 Surfaces\n    if: ${{ false }}\n" },
    };

    [Theory(DisplayName = "Negative control: each way of disarming the DataGrid CI gate is refused")]
    [MemberData(nameof(DataGridCiGateSeeds))]
    public void ScanDataGridCiGate_RefusesEachDisarming(string name, string find, string replace)
    {
        var tier1 = RealTier1Text();
        Assert.True(tier1.Contains(find, StringComparison.Ordinal), $"seed {name}: the text to perturb must exist in the real workflow");

        var violations = ScanDataGridCiGate(tier1.Replace(find, replace, StringComparison.Ordinal));

        Assert.NotEmpty(violations);
    }

    [Fact(DisplayName = "Negative control: a gate whose advisory state disagrees with DataGridGateAdvisory is refused")]
    public void ScanDataGridCiGate_RefusesAnAdvisoryStateTheConstantDoesNotRecord()
    {
        var tier1 = RealTier1Text();
        string seeded;
        if (DataGridGateAdvisory)
        {
            // Flipped in the YAML only: the job's line deleted, the constant left true.
            var job = tier1.IndexOf("  " + DataGridGateJob + ":\n", StringComparison.Ordinal);
            Assert.True(job >= 0, "the gate job must exist");
            var line = new Regex(@"(?m)^    continue-on-error: true[^\n]*\n").Match(tier1, job);
            Assert.True(line.Success, "the advisory gate must carry its job-level continue-on-error line");
            seeded = tier1.Remove(line.Index, line.Length);
        }
        else
        {
            // Made advisory again in the YAML only.
            Assert.Contains("    timeout-minutes: 15\n", tier1, StringComparison.Ordinal);
            seeded = tier1.Replace("    timeout-minutes: 15\n", "    timeout-minutes: 15\n    continue-on-error: true\n", StringComparison.Ordinal);
        }

        Assert.NotEmpty(ScanDataGridCiGate(seeded));
    }

    [Fact(DisplayName = "No external-SPA file reaches the shared DataGrid except ExternalDataGrid.tsx, whose mount passes showViewSelector={false}")]
    public void ExternalSpa_EverySpaarkeDataGridMount_TurnsTheViewSelectorOff()
    {
        Assert.True(Directory.Exists(ExternalSpaSource), $"the external SPA source must exist at {ExternalSpaSource}");

        var violations = new List<string>();
        var compliantByFile = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in ScriptFilesUnder(ExternalSpaSource))
        {
            var relative = RepoRelative(file);
            var source = File.ReadAllText(file);
            var result = Scan(source, relative);
            violations.AddRange(result.Violations);
            // Round 7 (V7): every import must land on a file these scans read.
            violations.AddRange(UnscannedImportTargets(source, relative, ScannedFiles.Value, File.Exists, Directory.Exists));
            if (result.CompliantMounts > 0)
            {
                compliantByFile[relative] = result.CompliantMounts;
            }
        }

        // Round 4: Vite builds every script index.html names, so an entry outside the scanned source would be
        // bundled unscanned.
        violations.AddRange(ScanEntryHtml(File.ReadAllText(Path.Combine(ExternalSpaRoot, "index.html"))));

        // Round 6 (R5): the loop above skips node_modules, so none may exist inside the scanned source.
        violations.AddRange(NodeModulesFoldersUnder(ExternalSpaSource));

        // Round 7 (fix round c1): no browser-field remap in either manifest the SPA build reads.
        violations.AddRange(ScanPackageManifest(File.ReadAllText(Path.Combine(ExternalSpaRoot, "package.json")),
            "src/client/external-spa/package.json"));
        violations.AddRange(ScanPackageManifest(File.ReadAllText(Path.Combine(SharedLibraryRoot, "package.json")),
            "src/client/shared/Spaarke.UI.Components/package.json"));

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

    /// <summary>
    /// Round 5 (Q3/Q4): the fan-in fact reads only the library's <c>src</c> and skips test and <c>__*</c> paths, so a
    /// shipped library file may not load a module from either place — such a module could carry the grid unread.
    /// Returns a description per offending specifier. Reported for EVERY file, the allowed carriers included.
    /// </summary>
    internal static IReadOnlyList<string> SharedEscapes(string source, string fileName)
    {
        var found = new List<string>();
        string Where(int index) => $"{fileName}:{LineOf(source, index)}";
        var literalSpecs = AnyFrom.Matches(source).Concat(SideEffectImport.Matches(source))
            .Select(m => (m.Index, Spec: m.Groups["spec"].Value))
            .Concat(DynamicCall.Matches(source)
                .Select(call => (call.Index, Arg: DynamicLiteralArgAt.Match(source, call.Index + call.Length)))
                .Where(x => x.Arg.Success)
                .Select(x => (x.Index, Spec: x.Arg.Groups["spec"].Value)));
        foreach (var (index, spec) in literalSpecs)
        {
            if (!spec.StartsWith('.') && !IsSharedAlias(spec))
            {
                continue; // a bare package: residual 1 (see the class remarks)
            }
            var full = ResolveSpecifier(spec.Split('?', '#')[0], fileName)!;
            if (NotSharedLibrarySource(full) is { } where)
            {
                found.Add($"{Where(index)}: loads '{spec}' ({where}), a module this fact does not read");
            }
        }

        // Round 6: the same comment gap (LibStatement, AnyFrom and DynamicCall read only whitespace after the
        // keyword, so `from /* c */ '../DataGrid'` was invisible to the fan-in too). Library prose often ends a
        // comment line with "from" above another comment line and then a quoted object key, so here a keyword
        // followed by a comment or U+FEFF is refused only when what follows could load a library module.
        foreach (Match keyword in LibKeywordGap.Matches(source))
        {
            if (keyword.Groups["gap"].Value.All(char.IsWhiteSpace))
            {
                continue; // plain whitespace: the rules above read this one
            }
            var loads = keyword.Groups["spec"].Success
                ? CouldLoadALibraryModule(keyword.Groups["spec"].Value)
                : CouldLoadALibraryModule(keyword.Groups["arg"].Value) || keyword.Groups["arg"].Value.Contains('`');
            if (loads)
            {
                found.Add($"{Where(keyword.Index)}: a comment (or U+FEFF) between '{keyword.Value}' and the module hides it "
                          + "from this fact");
            }
        }
        foreach (Match pragma in JsxPragma.Matches(source))
        {
            found.Add($"{Where(pragma.Index)}: a JSX pragma loads a runtime or factory module this fact does not read");
        }
        return found;
    }

    // Round 6, library only: a module keyword, the gap after it (whitespace, comments, U+FEFF), then a quoted
    // specifier or a parenthesised argument. A lookahead, so a "comment" the regex misreads (inside a string) cannot
    // swallow a later keyword: each keyword is matched on its own.
    private static readonly Regex LibKeywordGap = new(
        @"(?<![\w$.])(?:from|import|require)(?=(?<gap>" + TokenGap + @")(?:(?<q>['""])(?<spec>[^'""\n]*)\k<q>|\((?<arg>[^)]*)))",
        RegexOptions.Compiled);

    /// <summary>
    /// Whether a specifier (or the text of a call's argument) could load a module of the shared library: a relative,
    /// root-relative or alias path, or any text naming the library or <c>DataGrid</c>. A bare package is residual 1.
    /// </summary>
    private static bool CouldLoadALibraryModule(string text) =>
        Regex.IsMatch(text, @"(?:^|['""`]\s*)(?:\.|/|@spaarke/)")
        || NamesSharedLibrary.IsMatch(text)
        || text.Contains("DataGrid", StringComparison.OrdinalIgnoreCase);

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

    private static bool IsSharedLibraryTestFile(string relative) => IsTestPath(relative);

    [Fact(DisplayName = "Inside the shared library, only the known barrels and DataGridPageShell import or re-export the grid")]
    public void SharedLibrary_OnlyKnownModulesCarryTheGrid()
    {
        Assert.True(Directory.Exists(SharedLibrarySource), $"the shared library must exist at {SharedLibrarySource}");

        var unexpected = new List<string>(NodeModulesFoldersUnder(SharedLibrarySource)); // round 6 (R5)
        var carriersSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in ScriptFilesUnder(SharedLibrarySource))
        {
            var relative = RepoRelative(file);
            if (IsSharedLibraryTestFile(relative))
            {
                continue; // tests are not bundled
            }
            var source = File.ReadAllText(file);
            // Round 7 (V7, fix round c1): every import of a shipped file, the grid's own included, must land on a file
            // these scans read.
            unexpected.AddRange(UnscannedImportTargets(source, relative, ScannedFiles.Value, File.Exists, Directory.Exists));
            if (relative.EndsWith("/components/DataGrid/DataGrid.tsx", StringComparison.Ordinal))
            {
                continue; // DataGrid.tsx is the grid itself (its rule is pinned by SharedDataGrid_EnforcesTheExternalHostRule)
            }
            unexpected.AddRange(SharedEscapes(source, relative)); // round 5: always reported
            var hits = SharedFanIn(source, relative);
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
            + "the picker off, then add it to SharedFanInAllowed. A shipped library file that loads a module outside "
            + "src/ or from a test / __* path is refused outright: this fact does not read that module." + Environment.NewLine
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
    [InlineData("W5-library-name-elsewhere", OtherSpaFile, "import { Grid } from '../../../../solutions/Spaarke.UI.Components-fork/grid';", "names the shared library but resolves to")]
    // Dynamic loads: a variable, a template, a concatenation, a module outside the SPA
    [InlineData("X1-dynamic-variable", OtherSpaFile, "const p = '@spaarke/ui-components/components/DataGrid/DataGrid';\nconst G = React.lazy(() => import(p));", "dynamic import")]
    [InlineData("X2-dynamic-template", OtherSpaFile, "const G = React.lazy(() => import(`../../../shared/Spaarke.UI.Components/src/components/${n}/DataGrid`));", "dynamic import")]
    [InlineData("X3-dynamic-concatenation", OtherSpaFile, "const G = React.lazy(() => import('../../../shared/' + 'Spaarke.UI.Components/src/components/DataGrid/DataGrid'));", "dynamic import")]
    [InlineData("X4-dynamic-outside", OtherSpaFile, "const G = React.lazy(() => import('../../../../solutions/Other/src/grid'));", "dynamic import")]
    // The wrapper rule: a fully compliant mount in any OTHER file is still refused
    [InlineData("Y1-compliant-mount-outside-wrapper", OtherSpaFile, SpaarkeImport + "const A = () => <DataGrid configId=\"x\" showViewSelector={false} />;", "outside ExternalDataGrid.tsx")]
    // Round 4 (task 157 fix round b2): a side-effect import of a module outside the SPA that parks the grid on
    // `window` compiled (vite build) into a live mount with the picker on; plus its class
    [InlineData("Z1-side-effect-outside", OtherSpaFile, "import '../../zzoutside/gridGlobal';\nconst G = (window as any).SprkGrid;\nconst A = () => <G configId=\"x\" />;", "outside the external SPA")]
    [InlineData("Z2-side-effect-library", OtherSpaFile, "import '@spaarke/ui-components/components/DataGrid/DataGrid';", "not attributed")]
    [InlineData("Z3-subpath-import", OtherSpaFile, "import { Grid } from '#grid';\n<Grid configId=\"x\" />", "undeclared bare specifier")]
    [InlineData("Z4-resolve-alias", OtherSpaFile, "import { Grid } from '@grid/DataGrid';\n<Grid configId=\"x\" />", "undeclared bare specifier")]
    [InlineData("Z5-builtin", OtherSpaFile, "import { createRequire } from 'node:module';", "undeclared bare specifier")]
    [InlineData("Z6-dynamic-undeclared", OtherSpaFile, "const G = React.lazy(() => import('#grid'));", "dynamic import")]
    [InlineData("Z7-eval", OtherSpaFile, "const m = eval('im' + 'port(p)');", "eval / Function")]
    [InlineData("Z8-function-constructor", OtherSpaFile, "const load = new Function('p', 'return im' + 'port(p)');", "eval / Function")]
    public void Scan_WhenARound3EvasionShapeIsSeeded_ReportsIt(string shape, string fileName, string source, string expected)
    {
        // CompliantMounts is not asserted: V3 rows hold a compliant JSX mount and are refused for the clone.
        var result = Scan(source, fileName);

        Assert.True(
            result.Violations.Any(v => v.Contains(expected, StringComparison.Ordinal)),
            $"{shape}: expected a violation containing '{expected}', got:{Environment.NewLine}{string.Join(Environment.NewLine, result.Violations)}");
    }

    // ── Review round 5 (task 157 fix round b2-r1): five shapes compiled (vite build) into a live mount with the picker
    // on past the round-4 scan (Q1–Q5), two more found while fixing them (Q6 `?query`, Q7 bare `..`), and their
    // neighbours. Each one MUST be reported. Files are scanned as OtherSpaFile (src/widgets/Seeded.tsx). ──

    private const string WrapperImport = "import { ExternalDataGrid } from './ExternalDataGrid';\n";
    private const string NotAJsxTag = "(ExternalDataGrid) is referenced outside a JSX tag";

    [Theory(DisplayName = "Control: every evasion shape seeded in review round 5 is reported")]
    // Q1 / Q2: the wrapper called as a plain function returns the INNER grid element
    [InlineData("Q1-clone-of-returned-element", WrapperImport + "const A = () => React.cloneElement(ExternalDataGrid({ configId: 'q1' } as any) as any, { showViewSelector: true });", NotAJsxTag)]
    [InlineData("Q1b-clone-in-wrapper-file", WrapperImport + "const A = () => React.cloneElement(<ExternalDataGrid configId=\"x\" />, {});", "in a file that holds ExternalDataGrid")]
    [InlineData("Q2-type-of-returned-element", WrapperImport + "const Inner = (ExternalDataGrid({} as any) as any).type;\nconst A = () => <Inner configId=\"q2\" />;", NotAJsxTag)]
    [InlineData("Q2b-alias-at-alias-with-extension", "import { ExternalDataGrid as E } from '@/widgets/ExternalDataGrid.tsx';\nconst I = E({}).type;", NotAJsxTag)]
    [InlineData("Q2c-root-relative-other-case", "import { ExternalDataGrid as E } from '/src/widgets/externaldatagrid';\nconst f = E;", NotAJsxTag)]
    [InlineData("Q2d-default-import", "import E from './ExternalDataGrid.js';\nconst f = E;", NotAJsxTag)]
    [InlineData("Q2e-spread", WrapperImport + "const p = { ...ExternalDataGrid };", NotAJsxTag)]
    [InlineData("Q2f-local-reexport", WrapperImport + "export { ExternalDataGrid as Grid };", NotAJsxTag)]
    [InlineData("Q2g-escaped-identifier", WrapperImport + "const f = External\\u0044ataGrid;", "unicode escape in a file that holds ExternalDataGrid")]
    [InlineData("Q2h-namespace", "import * as W from './ExternalDataGrid';\nconst I = W.ExternalDataGrid({}).type;", "namespace import of")]
    [InlineData("Q2i-reexport", "export { ExternalDataGrid as Grid } from './ExternalDataGrid';", "re-exports from")]
    [InlineData("Q2j-reexport-star", "export * from '../widgets/ExternalDataGrid';", "re-exports from")]
    [InlineData("Q2k-dynamic", "const m = import('./ExternalDataGrid');", "(the ExternalDataGrid wrapper)")]
    [InlineData("Q2l-require", "const { ExternalDataGrid: E } = require('./ExternalDataGrid');", "(the ExternalDataGrid wrapper)")]
    [InlineData("Q2m-string-named", "import { \"ExternalDataGrid\" as E } from './ExternalDataGrid';\nconst I = E({}).type;", "(the ExternalDataGrid wrapper) is not attributed")]
    // Q3: a carrier in the library ROOT, beside src/ (directly, or through the npm link)
    [InlineData("Q3-beside-src", "import { Grid } from '../../../shared/Spaarke.UI.Components/zzoutside/gridAlias';\n<Grid configId=\"q3\" />", "outside the shared library's src")]
    [InlineData("Q3b-beside-src-via-link", "import { Grid } from '../../node_modules/@spaarke/ui-components/zzoutside/gridAlias';", "outside the shared library's src")]
    // Q4: a carrier in a test or __* path inside src/ (the fan-in fact skips those)
    [InlineData("Q4-dunder-folder", "import { Grid } from '@spaarke/ui-components/components/DataGrid/__zz__/grid';\n<Grid configId=\"q4\" />", "a test or __* path")]
    [InlineData("Q4b-test-file", "import { Grid } from '@spaarke/ui-components/components/DataGrid/DataGrid.test';", "a test or __* path")]
    [InlineData("Q4c-stories-file", "import { Grid } from '../../../shared/Spaarke.UI.Components/src/components/X/X.stories.tsx';", "a test or __* path")]
    // Q5: the alias, resolved, steps out of the library with `..`
    [InlineData("Q5-alias-escape", "import { Grid } from '@spaarke/ui-components/../../../external-spa/zzoutside/gridAlias2';\n<Grid configId=\"q5\" />", "names the shared library but resolves to")]
    [InlineData("Q5b-alias-into-the-spa", "import { X } from '@spaarke/ui-components/../../../external-spa/src/widgets/Other';", "names the shared library but resolves to")]
    // Q6: a query or fragment makes another module instance whose default import no name rule attributes
    [InlineData("Q6-query", "import Table from '@spaarke/ui-components/components/DataGrid/DataGrid?v=1';\n<Table configId=\"q6\" />", "a query or fragment")]
    [InlineData("Q6b-fragment", "import T from './Other#x';", "a query or fragment")]
    [InlineData("Q6c-dynamic-query", "const m = import('./Other?x');", "dynamic import")]
    // Q7: a declared bare package whose subpath steps out with `..`
    [InlineData("Q7-bare-dotdot", "import { Grid } from 'react-window/../../zzoutside/gridAlias2';\n<Grid configId=\"q7\" />", "'.' or '..' segment")]
    public void Scan_WhenARound5EvasionShapeIsSeeded_ReportsIt(string shape, string source, string expected)
    {
        var result = Scan(source, OtherSpaFile);

        Assert.True(
            result.Violations.Any(v => v.Contains(expected, StringComparison.Ordinal)),
            $"{shape}: expected a violation containing '{expected}', got:{Environment.NewLine}{string.Join(Environment.NewLine, result.Violations)}");
    }

    // ── Review round 6 (task 157 fix round b2-r2): five shapes compiled (vite build) into a live mount with the picker
    // on past the round-5 scan (R1–R5), and their neighbours. Each one MUST be reported. Scanned as OtherSpaFile. ──

    private const string CommentAfterKeyword = "a comment directly after 'from' / 'import' / 'require'";

    [Theory(DisplayName = "Control: every evasion shape seeded in review round 6 is reported")]
    // R1: a comment between `from` and the specifier hid the import from every specifier rule
    [InlineData("R1-block-comment-after-from", "import { DataGrid as G } from /* c */ '@spaarke/ui-components/components/DataGrid/DataGrid';\n<G configId=\"zzr1\" showViewSelector />", CommentAfterKeyword)]
    [InlineData("R1b-line-comment-after-from", "import { DataGrid as G } from // c\n  '@spaarke/ui-components/components/DataGrid/DataGrid';", CommentAfterKeyword)]
    [InlineData("R1c-reexport", "export { DataGrid as G } from /* c */ '@spaarke/ui-components/components/DataGrid/DataGrid';", CommentAfterKeyword)]
    [InlineData("R1d-side-effect", "import /* c */ '../../zzoutside/gridGlobal';", CommentAfterKeyword)]
    [InlineData("R1e-outside-module", "import { Grid } from /* c */ '../../../../solutions/Other/src/grid';", CommentAfterKeyword)]
    // R2: the same trick on the wrapper reopened Q1/Q2 (its binding was never recognised)
    [InlineData("R2-wrapper-behind-comment", "import { ExternalDataGrid as E } from /* c */ '../widgets/ExternalDataGrid';\nconst Inner = (E as any)({ configId: 'zzr2' }).type;\nconst A = () => <Inner showViewSelector />;", CommentAfterKeyword)]
    // R3: a comment between `import` / `require` and `(` hid the dynamic load
    [InlineData("R3-dynamic-import", "const G = React.lazy(() => import /* c */ ('@spaarke/ui-components/components/DataGrid/DataGrid'));", CommentAfterKeyword)]
    [InlineData("R3b-require-line-comment", "const m = require // c\n  ('../../zzoutside/grid');", CommentAfterKeyword)]
    [InlineData("R3c-comment-inside-parens", "const m = import(/* @vite-ignore */ './Other');", "dynamic import")]
    [InlineData("R3d-comment-before-meta", "const m = import /* c */ .meta.glob('./x/*.tsx');", "import.meta.glob")]
    [InlineData("R3i-line-separator-ends-the-comment", "import { Grid } from // c\u2028'../../zzoutside/grid';", CommentAfterKeyword)]
    [InlineData("R3j-feff-whitespace", "import { Grid } from\uFEFF'../../zzoutside/grid';", "a U+FEFF character")]
    [InlineData("R3e-comment-before-glob", "const m = import.meta /* c */ .glob('./x/*.tsx');", "import.meta.glob")]
    [InlineData("R3f-optional-chain-glob", "const m = import.meta?.glob('./x/*.tsx');", "import.meta.glob")]
    [InlineData("R3g-computed-meta", "const k = 'gl' + 'ob';\nconst m = import.meta[k]('./x/*.tsx');", "import.meta.glob")]
    [InlineData("R3h-escaped-glob", "const m = import.meta.gl\\u006fb('./x/*.tsx');", "unicode escape")]
    // R4: a JSX pragma loads a runtime outside src/ with no import statement
    [InlineData("R4-jsx-import-source", "/** @jsxImportSource ../../zzoutside/rt */\nconst A = () => <div data-zz=\"grid\" />;", "a JSX pragma")]
    [InlineData("R4b-jsx-factory", "/** @jsx h */\nconst A = () => <div />;", "a JSX pragma")]
    [InlineData("R4c-jsx-runtime", "// @jsxRuntime classic\nconst A = () => <div />;", "a JSX pragma")]
    [InlineData("R4d-jsx-frag", "/* @jsxFrag F */\nconst A = () => <></>;", "a JSX pragma")]
    // R5: a carrier in a node_modules folder inside src (the scan skips node_modules)
    [InlineData("R5-src-node-modules", "import { T } from '../node_modules/zzc';\n<T configId=\"zzr5\" />", "a node_modules folder, which the scan skips")]
    [InlineData("R5b-at-alias-node-modules", "import '@/node_modules/zzc';", "a node_modules folder, which the scan skips")]
    [InlineData("R5c-dynamic-node-modules", "const m = import('../node_modules/zzc');", "dynamic import")]
    [InlineData("R5d-library-node-modules", "import { T } from '@spaarke/ui-components/node_modules/zzc';", "a node_modules path in the shared library's src")]
    public void Scan_WhenARound6EvasionShapeIsSeeded_ReportsIt(string shape, string source, string expected)
    {
        var result = Scan(source, OtherSpaFile);

        Assert.True(
            result.Violations.Any(v => v.Contains(expected, StringComparison.Ordinal)),
            $"{shape}: expected a violation containing '{expected}', got:{Environment.NewLine}{string.Join(Environment.NewLine, result.Violations)}");
    }

    [Fact(DisplayName = "Control: round-6 neighbours pass — comments that do not follow a module keyword, Array.from, import.meta.env")]
    public void Scan_WhenCommentsAndImportMetaAreUsedOrdinarily_Passes()
    {
        var text = "import { makeStyles } from '@fluentui/react-components'; // from the docs\n" +
                   "// imported from the shared library, see import.meta.env\n" +
                   "/* the jsx runtime is the default one */\n" +
                   "const rows = Array.from(items); // copy\n" +
                   "const env = import.meta.env.VITE_BFF_URL; /* import.meta is fine */\n" +
                   "const word = 'require'; const label = 'from';\n" +
                   "// Re-export the public types so callers can import them directly from\n" +
                   "// this barrel (prose that ends a comment line with a module keyword)\n" +
                   "export { tokens } from './tokens';\n" +
                   "/* the server takes it from */ const n = 1;\n" +
                   "const L = () => import('../widgets/ProjectsWidget');";

        var result = Scan(text, OtherSpaFile);

        Assert.Empty(result.Violations);
    }

    [Fact(DisplayName = "Control: sanctioned uses of the wrapper pass — JSX tags, typeof, type-only imports and re-exports, a comment quoting the tag")]
    public void Scan_WhenTheWrapperIsUsedOnlyAsJsx_Passes()
    {
        var text = "import * as React from 'react';\n" +
                   "import { ExternalDataGrid, type ExternalDataGridProps } from './ExternalDataGrid';\n" +
                   "import type { ExternalDataGridProps as P2 } from '@/widgets/ExternalDataGrid';\n" +
                   "export type { ExternalDataGridProps } from './ExternalDataGrid';\n" +
                   "import { resolveCodePageTheme } from '@spaarke/ui-components';\n" +
                   "import { setUserThemePreference } from '@spaarke/ui-components/components/../utils/themeStorage';\n" +
                   "// mounted as <ExternalDataGrid> only\n" +
                   "type P = React.ComponentProps<typeof ExternalDataGrid>;\n" +
                   "const A = (p: ExternalDataGridProps) => <ExternalDataGrid {...p} configId=\"x\"></ExternalDataGrid>;";

        var result = Scan(text, OtherSpaFile);

        Assert.Empty(result.Violations);
        Assert.Equal(0, result.CompliantMounts);
    }

    [Fact(DisplayName = "Control: sanctioned external-SPA neighbours pass — the wrapper's consumer, local dynamic imports, @/ and npm imports")]
    public void Scan_WhenAnExternalSpaFileUsesOnlySanctionedImports_Passes()
    {
        var text = "import * as React from 'react';\n" +
                   "import { makeStyles } from '@fluentui/react-components';\n" +
                   "import { resolveCodePageTheme } from '@spaarke/ui-components/utils/themeStorage';\n" +
                   "import type { DataGridProps } from '@spaarke/ui-components/components/DataGrid/DataGrid';\n" +
                   "import { ExternalDataGrid } from './ExternalDataGrid';\n" +
                   "import { createRoot } from 'react-dom/client';\n" +
                   "import 'react';\n" +
                   "import './local-side-effect';\n" +
                   "const isFn = typeof x === 'function'; const ev = evaluate(1); const f = myFunction(2);\n" +
                   "import { isEntitled } from '../registry/widgetRegistry';\n" +
                   "import { config } from '@/config';\n" +
                   "const L = () => import('../widgets/ProjectsWidget').then(m => ({ default: m.ProjectsWidgetBody }));\n" +
                   "const n = Array.from('abc');\n" +
                   "const A = () => <ExternalDataGrid configId=\"x\" />;";

        var result = Scan(text, OtherSpaFile);

        Assert.Empty(result.Violations);
        Assert.Equal(0, result.CompliantMounts);
    }

    [Fact(DisplayName = "Control: index.html may only load scripts from src/ (round 4)")]
    public void ScanEntryHtml_RefusesAnEntryOutsideTheScannedSource()
    {
        const string Sanctioned = "<body><div id=\"root\"></div>\n<script type=\"module\" src=\"/src/main.tsx\"></script></body>";
        Assert.Empty(ScanEntryHtml(Sanctioned));

        Assert.Single(ScanEntryHtml(Sanctioned + "<script type=\"module\" src=\"/zzoutside/mountGrid.tsx\"></script>"));
        Assert.Single(ScanEntryHtml(Sanctioned + "<script type=\"module\" src=\"/src/../zzoutside/mountGrid.tsx\"></script>"));
        Assert.Single(ScanEntryHtml(Sanctioned + "<script type=\"module\">import '/zzoutside/mountGrid.tsx';</script>"));
        Assert.Single(ScanEntryHtml(Sanctioned + "<script type=\"module\" src=\"../shared/Spaarke.UI.Components/src/x.tsx\"></script>"));
        Assert.Single(ScanEntryHtml(Sanctioned + "<script type=\"module\" src=\"/zzoutside/a.tsx\" />"));
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

    [Fact(DisplayName = "Control: a shipped library file may not load a module outside src/ or from a test / __* path (round 5)")]
    public void SharedEscapes_ReportsALoadOutsideShippedSourceAndIgnoresNeighbours()
    {
        const string NewWidget = "src/client/shared/Spaarke.UI.Components/src/components/NewWidget/NewWidget.tsx";

        Assert.Single(SharedEscapes("export { Grid } from '../../../zzoutside/gridAlias';", NewWidget));
        Assert.Single(SharedEscapes("export { Grid } from '../DataGrid/__zz__/grid';", NewWidget));
        Assert.Single(SharedEscapes("import { x } from '../Foo/Foo.test';", NewWidget));
        Assert.Single(SharedEscapes("import '@spaarke/ui-components/../zzoutside/x';", NewWidget));
        Assert.Single(SharedEscapes("const m = import('../../../zzoutside/x');", NewWidget));
        // Round 6: a comment after the keyword, a JSX pragma, a node_modules folder inside src
        Assert.Single(SharedEscapes("export { DataGrid as T } from /* c */ '../DataGrid';", NewWidget));
        Assert.Single(SharedEscapes("const G = React.lazy(() => import /* c */ ('../DataGrid/DataGrid'));", NewWidget));
        Assert.Single(SharedEscapes("/** @jsxImportSource ../../../zzoutside/rt */", NewWidget));
        Assert.Single(SharedEscapes("import { T } from '../node_modules/zzc';", NewWidget));
        Assert.Single(SharedEscapes("export { DataGrid as T } from // c\n  '@spaarke/ui-components/components/DataGrid';", NewWidget));
        Assert.Single(SharedEscapes("export { DataGrid as T } from\uFEFF'../DataGrid';", NewWidget));
        // Library prose that ends a comment line with a module keyword, a quoted key after it, a literal BOM
        Assert.Empty(SharedEscapes(
            "const registry = {\n  // resolves the SAME surface identity from\n  // this ONE place (ADR-039)\n  'nda-review': { kind: 'tab' },\n" +
            "  // the jsx runtime is imported from\n  // React itself\n  (x as any)\n};\n" +
            "export const UTF8_BOM = '\uFEFF';", NewWidget));

        Assert.Empty(SharedEscapes(
            "import { tokens } from '../DataGrid/tokens';\n" +
            "import '@spaarke/ui-components/utils/themeStorage';\n" +
            "import { Button } from '@fluentui/react-components';\n" +
            "import type { AuthenticatedFetchFn } from '@spaarke/auth';\n" +
            "const pdf = import('pdfjs-dist');", NewWidget));
    }

    // ── Fix round c1 controls: the runtime rule's switches, its pins, and the round-7 import rule ──

    private const string SanctionedApp =
        "import * as React from 'react';\n" +
        "import { BrowserRouter, Routes, Route } from 'react-router-dom';\n" +
        "import { DataGridExternalHostProvider } from '@spaarke/ui-components/components/DataGrid/DataGridExternalHost';\n" +
        "const AppShell: React.FC = () => <Routes><Route path=\"/\" element={<div />} /></Routes>;\n" +
        "export const App: React.FC = () => {\n" +
        "  React.useEffect(() => { const cleanup = () => undefined; return cleanup; }, []);\n" +
        "  return (\n" +
        "    <DataGridExternalHostProvider>\n" +
        "      <FluentProvider theme={theme}>\n" +
        "        <BrowserRouter>\n" +
        "          <AppShell />\n" +
        "        </BrowserRouter>\n" +
        "      </FluentProvider>\n" +
        "    </DataGridExternalHostProvider>\n" +
        "  );\n" +
        "};\n\n" +
        "export default App;\n";

    private const string ProviderTree =
        "    <DataGridExternalHostProvider>\n" +
        "      <FluentProvider theme={theme}>\n" +
        "        <BrowserRouter>\n" +
        "          <AppShell />\n" +
        "        </BrowserRouter>\n" +
        "      </FluentProvider>\n" +
        "    </DataGridExternalHostProvider>\n";

    [Fact(DisplayName = "Control (c1): the sanctioned App root passes")]
    public void ScanAppRoot_WhenTheProviderEnclosesTheWholeTree_Passes()
    {
        Assert.Empty(ScanAppRoot(SanctionedApp));
    }

    [Theory(DisplayName = "Control (c1): an App root that leaves a route outside the provider is reported")]
    [InlineData("no-provider", "  return (\n      <FluentProvider theme={theme}>\n        <BrowserRouter>\n          <AppShell />\n        </BrowserRouter>\n      </FluentProvider>\n  );\n")]
    [InlineData("router-beside-a-closed-provider",
        "  return (\n    <DataGridExternalHostProvider></DataGridExternalHostProvider> && <BrowserRouter><AppShell /></BrowserRouter> || <DataGridExternalHostProvider></DataGridExternalHostProvider>\n  );\n")]
    [InlineData("early-return-outside", "  if (window.name) return (<BrowserRouter><AppShell /></BrowserRouter>);\n  return (\n" + ProviderTree + "  );\n")]
    [InlineData("second-router-in-app", "  const other = <BrowserRouter><AppShell /></BrowserRouter>;\n  return (\n" + ProviderTree + "  );\n")]
    [InlineData("router-not-inside", "  return (\n    <DataGridExternalHostProvider>\n      <AppShell />\n    </DataGridExternalHostProvider>\n  );\n")]
    public void ScanAppRoot_WhenARouteCanRenderOutsideTheProvider_ReportsIt(string shape, string body)
    {
        var source = SanctionedApp[..SanctionedApp.IndexOf("  return (", StringComparison.Ordinal)] + body + "};\n\nexport default App;\n";

        Assert.True(ScanAppRoot(source).Count > 0, $"{shape}: expected a violation");
    }

    [Theory(DisplayName = "Control (c1): the provider import and the end of App.tsx are pinned")]
    [InlineData("import-missing", "import { DataGridExternalHostProvider } from '@spaarke/ui-components/components/DataGrid/DataGridExternalHost';\n", "")]
    [InlineData("import-aliased", "import { DataGridExternalHostProvider } from", "import { DataGridExternalHostProvider as DataGridExternalHostProvider2 } from")]
    [InlineData("code-after-export-default", "export default App;\n", "export default App;\nrenderElsewhere(<AppShell />);\n")]
    [InlineData("second-app", "export const App: React.FC", "export const App = () => null;\nexport const App: React.FC")]
    public void ScanAppRoot_WhenTheImportOrTheTailChanges_ReportsIt(string shape, string find, string replace)
    {
        var source = SanctionedApp.Replace(find, replace, StringComparison.Ordinal);
        Assert.NotEqual(SanctionedApp, source);

        Assert.True(ScanAppRoot(source).Count > 0, $"{shape}: expected a violation");
    }

    [Theory(DisplayName = "Control (c1): a second router anywhere in the SPA is reported")]
    [InlineData("memory-router", OtherSpaFile, "import { MemoryRouter } from 'react-router-dom';")]
    [InlineData("browser-router-elsewhere", OtherSpaFile, "import { BrowserRouter } from 'react-router-dom';")]
    [InlineData("aliased-in-app", AppRootFile, "import { BrowserRouter as BR } from 'react-router-dom';")]
    [InlineData("hash-router-in-app", AppRootFile, "import { BrowserRouter, HashRouter } from 'react-router-dom';")]
    [InlineData("data-router", OtherSpaFile, "import { createBrowserRouter, RouterProvider } from 'react-router-dom';")]
    [InlineData("namespace", OtherSpaFile, "import * as RR from 'react-router-dom';")]
    [InlineData("default", OtherSpaFile, "import RR from 'react-router';")]
    [InlineData("star-reexport", OtherSpaFile, "export * from 'react-router-dom';")]
    [InlineData("named-reexport", OtherSpaFile, "export { MemoryRouter as M } from 'react-router-dom';")]
    [InlineData("server-subpath", OtherSpaFile, "import { StaticRouter } from 'react-router-dom/server';")]
    [InlineData("dynamic", OtherSpaFile, "const rr = import('react-router-dom');")]
    public void ScanRouterImports_WhenASecondRouterIsBound_ReportsIt(string shape, string fileName, string source)
    {
        Assert.True(ScanRouterImports(source, fileName).Count > 0, $"{shape}: expected a violation");
    }

    [Fact(DisplayName = "Control (c1): ordinary router hooks, and BrowserRouter in App.tsx, pass")]
    public void ScanRouterImports_WhenOnlyHooksAndTheRootRouterAreImported_Passes()
    {
        Assert.Empty(ScanRouterImports("import { BrowserRouter, Routes, Route, useNavigate, useLocation } from 'react-router-dom';", AppRootFile));
        Assert.Empty(ScanRouterImports(
            "import { useNavigate, useParams, useSearchParams, Link } from 'react-router-dom';\n" +
            "import type { NavigateFunction } from 'react-router-dom';\n" +
            "import { makeStyles } from '@fluentui/react-components';", OtherSpaFile));
    }

    private const string SanctionedVite =
        "export default defineConfig({\n" +
        "  // the external host constant: see DataGridExternalHost.tsx\n" +
        "  define: {\n" +
        "    __SPAARKE_DATAGRID_EXTERNAL_HOST__: 'true',\n" +
        "  },\n" +
        "  plugins: [react({ include: ['src/**/*.tsx', '../shared/Spaarke.UI.Components/src/**/*.ts'] })],\n" +
        "});\n";

    [Fact(DisplayName = "Control (c1): the sanctioned Vite define passes (other keys and double quotes too)")]
    public void ScanViteConfig_WhenTheConstantIsDefinedTrueOnce_Passes()
    {
        Assert.Empty(ScanViteConfig(SanctionedVite));
        Assert.Empty(ScanViteConfig(SanctionedVite.Replace(
            "__SPAARKE_DATAGRID_EXTERNAL_HOST__: 'true',", "__APP_VERSION__: '\"1.0\"', __SPAARKE_DATAGRID_EXTERNAL_HOST__: \"true\"", StringComparison.Ordinal)));
    }

    [Theory(DisplayName = "Control (c1): a Vite config that does not switch the external host on is reported")]
    [InlineData("missing", "    __SPAARKE_DATAGRID_EXTERNAL_HOST__: 'true',\n", "")]
    [InlineData("false", "'true',", "'false',")]
    [InlineData("commented-out", "    __SPAARKE_DATAGRID_EXTERNAL_HOST__: 'true',\n", "    // __SPAARKE_DATAGRID_EXTERNAL_HOST__: 'true',\n")]
    [InlineData("block-commented", "  define: {\n    __SPAARKE_DATAGRID_EXTERNAL_HOST__: 'true',\n  },\n", "  /* define: {\n    __SPAARKE_DATAGRID_EXTERNAL_HOST__: 'true',\n  }, */\n")]
    [InlineData("redefined-by-a-plugin", "plugins: [", "plugins: [{ name: 'x', config: () => ({ define: { __SPAARKE_DATAGRID_EXTERNAL_HOST__: 'false' } }) }, ")]
    [InlineData("misspelt", "__SPAARKE_DATAGRID_EXTERNAL_HOST__: 'true'", "__SPAARKE_DATAGRID_EXTERNAL_HOSTS__: 'true'")]
    public void ScanViteConfig_WhenTheConstantIsNotDefinedTrueOnce_ReportsIt(string shape, string find, string replace)
    {
        var source = SanctionedVite.Replace(find, replace, StringComparison.Ordinal);
        Assert.NotEqual(SanctionedVite, source);

        Assert.True(ScanViteConfig(source).Count > 0, $"{shape}: expected a violation");
    }

    // Line endings normalised so the in-memory seeds below match a Windows (CRLF) checkout and a Linux (LF) one alike.
    private static string RepoFileText(string relative) =>
        File.ReadAllText(Path.Combine(SourceScan.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    public static TheoryData<string, string, string> SharedGridRuleBreaks => new()
    {
        { "picker-ignores-host", "const showViewSelector = !externalHost && showViewSelectorProp;", "const showViewSelector = showViewSelectorProp;" },
        { "sibling-fetch-ungated", "showViewSelector\n            ? dataverseClient.retrieveSavedQueriesForEntity(entityName)", "true\n            ? dataverseClient.retrieveSavedQueriesForEntity(entityName)" },
        { "savedquery-set-allowed", "if (externalHost && configRecord?.source?.type === 'savedquery-set') {", "if (false && configRecord?.source?.type === 'savedquery-set') {" },
        { "picked-view-honoured", "const pickedViewId = externalHost ? undefined : activeSavedQueryId;", "const pickedViewId = activeSavedQueryId;" },
        { "hook-not-called", "const externalHost = useDataGridExternalHost();", "const externalHost = false;" },
        { "raw-prop-read-again", "    externalViews,\n    className,\n  } = props;", "    externalViews,\n    className,\n  } = props;\n  const rawPicker = props.showViewSelector;" },
        { "third-picker", "            ) : (\n              <span aria-hidden=\"true\" />", "            ) : (\n              <ViewSelector views={[]} activeViewId=\"\" onViewChange={() => undefined} />" },
        { "third-list-call", "const entityName =", "void dataverseClient.retrieveSavedQueriesForEntity('x');\n        const entityName =" },
        { "second-resolve-source-call", "const entityName =", "void resolveSource(dataverseClient, configRecord, undefined);\n        const entityName =" },
        { "resolve-source-always-internal", "undefined, externalHost ? 'external' : 'internal');", "undefined, 'internal');" },
        { "second-config-read", "const entityName =", "void fetchConfigRecord(dataverseClient, configId);\n        const entityName =" },
        { "picked-view-fetched-again", "const entityName =", "void dataverseClient.retrieveSavedQuery(configId);\n        const entityName =" },
    };

    /// <summary>Breaks inside <see cref="GridSourceFile"/>, where ontology 054 moved the savedquery-set discovery.</summary>
    public static TheoryData<string, string, string> GridSourceResolverBreaks => new()
    {
        { "external-refusal-removed", "if (host === 'external') return null;", "" },
        { "external-refusal-inverted", "if (host === 'external') return null;", "if (host === 'internal') return null;" },
        { "host-argument-made-optional", "host: GridSourceHost", "host?: GridSourceHost" },
        { "discovery-call-removed", "await dataverseClient.retrieveSavedQueriesForEntity(source.entityLogicalName)", "[] as { id: string; isDefault?: boolean }[]" },
        { "discovery-condition-rewritten", "if (source.type === 'savedquery-set') {", "if (source.type === 'savedquery-set' || true) {" },
        { "second-discovery-call", "const def = queries.find", "void dataverseClient.retrieveSavedQueriesForEntity('x');\n      const def = queries.find" },
        { "second-view-fetch", "const def = queries.find", "void dataverseClient.retrieveSavedQuery('x');\n      const def = queries.find" },
    };

    [Theory(DisplayName = "Control (c1): each break of the shared grid's external-host rule is reported")]
    [MemberData(nameof(SharedGridRuleBreaks))]
    public void ScanSharedGridRule_WhenTheGridStopsApplyingTheRule_ReportsIt(string shape, string find, string replace)
    {
        var grid = RepoFileText(SharedGridFile);
        var host = RepoFileText(HostModuleFile);
        var resolver = RepoFileText(GridSourceFile);
        Assert.Empty(ScanSharedGridRule(grid, host, resolver)); // the real files pass, so the row below is the break
        var broken = grid.Replace(find, replace, StringComparison.Ordinal);
        Assert.True(broken != grid, $"{shape}: the seed text was not found in {SharedGridFile}");

        Assert.True(ScanSharedGridRule(broken, host, resolver).Count > 0, $"{shape}: expected a violation");
    }

    [Theory(DisplayName = "Control (c1): each break of the grid-source resolver's savedquery-set discovery is reported")]
    [MemberData(nameof(GridSourceResolverBreaks))]
    public void ScanSharedGridRule_WhenTheResolverStopsHoldingTheDiscovery_ReportsIt(string shape, string find, string replace)
    {
        var grid = RepoFileText(SharedGridFile);
        var host = RepoFileText(HostModuleFile);
        var resolver = RepoFileText(GridSourceFile);
        Assert.Empty(ScanSharedGridRule(grid, host, resolver));
        var broken = resolver.Replace(find, replace, StringComparison.Ordinal);
        Assert.True(broken != resolver, $"{shape}: the seed text was not found in {GridSourceFile}");

        Assert.True(ScanSharedGridRule(grid, host, broken).Count > 0, $"{shape}: expected a violation");
    }

    [Theory(DisplayName = "Control (c1): each break of the host module is reported")]
    [InlineData("context-check-dropped", "return insideProvider || isDataGridExternalHostBuild();", "return isDataGridExternalHostBuild();")]
    [InlineData("build-check-dropped", "return insideProvider || isDataGridExternalHostBuild();", "return insideProvider;")]
    [InlineData("context-default-on-elsewhere", "React.createContext<boolean>(false)", "React.createContext<boolean>(true)")]
    [InlineData("provider-sets-false", "<DataGridExternalHostContext.Provider value={true}>", "<DataGridExternalHostContext.Provider value={false}>")]
    public void ScanSharedGridRule_WhenTheHostModuleChanges_ReportsIt(string shape, string find, string replace)
    {
        var grid = RepoFileText(SharedGridFile);
        var host = RepoFileText(HostModuleFile);
        var resolver = RepoFileText(GridSourceFile);
        var broken = host.Replace(find, replace, StringComparison.Ordinal);
        Assert.True(broken != host, $"{shape}: the seed text was not found in {HostModuleFile}");

        Assert.True(ScanSharedGridRule(grid, broken, resolver).Count > 0, $"{shape}: expected a violation");
    }

    /// <summary>A virtual file tree for the round-7 controls (repo-relative paths): files, the directories above them, and the scanned subset.</summary>
    private static (Func<string, bool> FileExists, Func<string, bool> DirectoryExists, IReadOnlySet<string> Scanned) VirtualTree(
        string files, string scanned)
    {
        static string Full(string relative) => Path.GetFullPath(Path.Combine(SourceScan.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        var fileSet = files.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(Full).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in fileSet)
        {
            for (var dir = Path.GetDirectoryName(file); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
            {
                directories.Add(dir);
            }
        }
        var scannedSet = scanned.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(Full).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (path => fileSet.Contains(Path.GetFullPath(path)), path => directories.Contains(Path.GetFullPath(path)), scannedSet);
    }

    private const string Widgets = "src/client/external-spa/src/widgets/";
    private const string SpaSrc = "src/client/external-spa/src/";
    private const string LibSrc = "src/client/shared/Spaarke.UI.Components/src/";

    [Theory(DisplayName = "Control (c1, round 7 / V7): an import that lands on a file the scans do not read is reported")]
    [InlineData("V7-no-extension", "import './carrier';", Widgets + "carrier", "", "do not read")]
    [InlineData("V7b-other-extension", "import { G } from './carrier.txt';", Widgets + "carrier.txt", "", "do not read")]
    [InlineData("V7c-exact-file-beats-ts", "import { G } from './carrier';", Widgets + "carrier;" + Widgets + "carrier.ts", Widgets + "carrier.ts", "do not read")]
    [InlineData("V7d-json-by-extension-search", "import data from './carrier';", Widgets + "carrier.json", "", "do not read")]
    [InlineData("V7e-dynamic", "const m = import('./carrier.mdx');", Widgets + "carrier.mdx", "", "do not read")]
    [InlineData("V7f-require", "const m = require('./carrier.cjs.txt');", Widgets + "carrier.cjs.txt", "", "do not read")]
    [InlineData("V7g-at-alias", "import '@/zz/carrier.html';", SpaSrc + "zz/carrier.html", "", "do not read")]
    [InlineData("V7h-library-alias", "import { G } from '@spaarke/ui-components/components/Zz/carrier.vue';", LibSrc + "components/Zz/carrier.vue", "", "do not read")]
    [InlineData("V7i-library-test-path", "import { G } from '@spaarke/ui-components/components/X/X.test';", LibSrc + "components/X/X.test.ts", "", "do not read")]
    [InlineData("V7j-src-node-modules", "import '../node_modules/zzc';", SpaSrc + "node_modules/zzc/index.ts", "", "do not read")]
    [InlineData("V7k-package-json-directory", "import { G } from './pkgdir';", Widgets + "pkgdir/package.json;" + Widgets + "pkgdir/index.ts", Widgets + "pkgdir/index.ts", "package.json")]
    [InlineData("V7n-escape", "import { G } from './car\\u0072ier';", Widgets + "carrier.ts", Widgets + "carrier.ts", "holds an escape")]
    [InlineData("V7o-query-on-an-unscanned-file", "import raw from './carrier.txt?raw';", Widgets + "carrier.txt", "", "do not read")]
    public void UnscannedImportTargets_WhenAnImportLandsOnAFileTheScansDoNotRead_ReportsIt(
        string shape, string source, string files, string scanned, string expected)
    {
        var (fileExists, directoryExists, scannedSet) = VirtualTree(files, scanned);

        var found = UnscannedImportTargets(source, OtherSpaFile, scannedSet, fileExists, directoryExists);

        Assert.True(found.Any(v => v.Contains(expected, StringComparison.Ordinal)),
            $"{shape}: expected a violation containing '{expected}', got:{Environment.NewLine}{string.Join(Environment.NewLine, found)}");
    }

    [Fact(DisplayName = "Control (c1, round 7): imports that land on scanned files pass — extensionless, index, @/, the library alias, dynamic, bare; and a specifier with nothing to load")]
    public void UnscannedImportTargets_WhenEveryImportLandsOnAScannedFile_Passes()
    {
        const string Scanned =
            Widgets + "Ok.tsx;" + Widgets + "dir/index.ts;" + LibSrc + "utils/themeStorage.ts;" + SpaSrc + "pages/Page.tsx;" +
            SpaSrc + "config.ts;" + Widgets + "Both.mjs;" + Widgets + "Both.ts";
        var (fileExists, directoryExists, scannedSet) = VirtualTree(Scanned + ";" + Widgets + "emptydir/readme.md", Scanned);
        const string Source =
            "import { A } from './Ok';\n" +
            "import './dir';\n" +
            "import * as React from 'react';\n" +
            "import { t } from '@spaarke/ui-components/utils/themeStorage';\n" +
            "import { c } from '@/config';\n" +
            "import { b } from './Both';\n" +
            "const L = () => import('../pages/Page');\n" +
            // Nothing to load: a real import of these fails the build, and the library's JSDoc examples quote such paths.
            " *   import { CreateEventWizard } from './components/CreateEventWizard';\n" +
            "import './emptydir';";

        Assert.Empty(UnscannedImportTargets(Source, OtherSpaFile, scannedSet, fileExists, directoryExists));
    }

    [Fact(DisplayName = "Control (c1, round 7): a browser field in a package.json is reported; a manifest without one passes")]
    public void ScanPackageManifest_ReportsABrowserFieldOnly()
    {
        Assert.Single(ScanPackageManifest("{ \"name\": \"x\", \"browser\": { \"./src/a.ts\": \"./carrier.txt\" } }", "package.json"));
        Assert.Single(ScanPackageManifest("{ \"name\": \"x\", \"browser\": \"./carrier.txt\" }", "package.json"));
        Assert.Empty(ScanPackageManifest("{ \"name\": \"x\", \"main\": \"dist/index.js\", \"sideEffects\": false }", "package.json"));
    }

    [Fact(DisplayName = "Control (c1, round 7): ViteResolvedFile follows Vite's order — exact file, extensions in order, then index")]
    public void ViteResolvedFile_FollowsVitesResolutionOrder()
    {
        var (fileExists, directoryExists, _) = VirtualTree(
            Widgets + "x;" + Widgets + "x.ts;" + Widgets + "y.mjs;" + Widgets + "y.ts;" + Widgets + "z/index.tsx", "");
        string Full(string relative) => Path.GetFullPath(Path.Combine(SourceScan.RepoRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Equal(Full(Widgets + "x"), ViteResolvedFile(Full(Widgets + "x"), fileExists, directoryExists, out _));
        Assert.Equal(Full(Widgets + "y.mjs"), ViteResolvedFile(Full(Widgets + "y"), fileExists, directoryExists, out _));
        Assert.Equal(Full(Widgets + "z/index.tsx"), ViteResolvedFile(Full(Widgets + "z"), fileExists, directoryExists, out _));
        Assert.Null(ViteResolvedFile(Full(Widgets + "none"), fileExists, directoryExists, out var why));
        Assert.Equal(string.Empty, why); // nothing to load is not a refusal
    }
}
