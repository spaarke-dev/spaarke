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
/// with <c>showViewSelector={false}</c> AFTER the caller's props, so for every JSX mount of the wrapper no caller
/// prop or spread, and no <c>cloneElement</c> / <c>createElement</c> of an <c>&lt;ExternalDataGrid&gt;</c> ELEMENT,
/// can turn the picker on. It does NOT protect the element it RETURNS (review round 5, Q1/Q2): the wrapper has no
/// hooks, so <c>ExternalDataGrid(props)</c> called as a plain function returns the inner grid element, which can
/// be cloned with the picker on or whose <c>.type</c> (the shared grid) can be mounted. Its text is PINNED
/// (whitespace-insensitive) by <see cref="ExternalSpa_GridWrapper_IsPinnedAndForcesThePickerOff"/>.</item>
/// <item><b>Scan:</b> no OTHER file under <c>src/client/external-spa/src</c> may reach the shared grid, and
/// (round 5) no other file may use the wrapper except as a JSX tag. The rules are written to refuse what they
/// cannot attribute, but a regex scan is not a proof: each of the six review rounds so far found a compiled bypass
/// of the previous version (round 6: a single comment after <c>from</c> hid an import from every rule). So the
/// scan refuses the routes to the grid that it knows of, each spelling it recognises; it does not guarantee that
/// every route goes through a JSX mount of the pinned wrapper. The known residuals are listed below.</item>
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
/// <para><b>Residual (documented, not scanned).</b> The scan is NOT a proof that no code path can mount the grid
/// with the picker on. What it does not cover:</para>
/// <list type="number">
/// <item>A NEW npm dependency (a <c>package.json</c> change) that itself bundles and re-exports the shared grid
/// under another name.</item>
/// <item>Build configuration: a <c>vite.config.ts</c> resolve alias or plugin that re-points a DECLARED name
/// (e.g. <c>react</c> or <c>@spaarke/ui-components</c> itself) at another file, or a JSX option in
/// <c>tsconfig.json</c> / the React plugin (<c>jsxImportSource</c>, <c>jsxFactory</c>) that re-points the JSX
/// runtime the way the pragma refused above would.</item>
/// <item>Inside the shared library, a dynamic <c>import()</c> whose path is built in a variable elsewhere (the
/// fan-in fact reads the call's own argument text only, because the library's prose says "import (" in many
/// comments).</item>
/// <item>Edits to the shared library's own <c>DataGrid.tsx</c> (e.g. ignoring the prop).</item>
/// <item>Peeling an element tree by hand (round 5, widened from "React internals"): starting from ANY element that
/// holds an <c>&lt;ExternalDataGrid&gt;</c>, reading <c>.props.children</c> / <c>.type</c> and calling the component
/// functions it finds returns the inner grid element, whose <c>.type</c> is the shared grid. Example (compiled with
/// <c>vite build</c> in fix round b2-r1 and NOT caught): in a file that never names the wrapper,
/// <c>const outer = createGridWidgetBody('x')({}).props.children; const T = outer.type(outer.props).type;</c> then
/// <c>&lt;T configId="x" /&gt;</c>. A fiber walk over React internals is the same class. No fixed spelling identifies
/// it (computed keys, <c>Object.values</c>, a helper in another file), a scan that refused every <c>.type</c> read
/// would refuse ordinary code (<c>event.type</c>, <c>file.type</c>), and no wrapper written in the same JavaScript
/// realm can hide the element it must hand to React. It is deliberate obfuscation, not a regression a developer
/// makes while fixing a column error.</item>
/// </list>
/// <para>Items 1–4 are reviewed changes outside <c>external-spa/src</c>. In every case the BFF allow-list still
/// refuses every column the picker's views would need, with a 400: this guard removes the pressure to widen that
/// list; it is not the data control.</para>
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
         * It switches the view picker off at RUNTIME for every JSX mount: `showViewSelector={false}` comes AFTER
         * the caller's props, so no caller prop or spread, and no clone or re-creation of an <ExternalDataGrid>
         * element, can turn the picker back on. With the picker on, the grid would offer the entity's internal MDA
         * views, whose columns the BFF's external allow-lists (ExternalAccessModule.cs) do not admit.
         *
         * It does NOT protect the element it RETURNS. Called as a plain function, it hands back the inner grid
         * element, which can be cloned, or whose `.type` can be mounted, with the picker on. So
         * ExternalSpaGridViewSelectorGuardTests refuses every other import of the shared grid under
         * src/client/external-spa/src, refuses any use of ExternalDataGrid there except as a JSX tag, and pins this
         * file's text, so a change here is a change to that guard. Walking an element tree by hand to reach the
         * inner element is a documented residual of that guard.
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

        // Round 4: Vite builds every script index.html names, so an entry outside the scanned source would be
        // bundled unscanned.
        violations.AddRange(ScanEntryHtml(File.ReadAllText(Path.Combine(ExternalSpaRoot, "index.html"))));

        // Round 6 (R5): the loop above skips node_modules, so none may exist inside the scanned source.
        violations.AddRange(NodeModulesFoldersUnder(ExternalSpaSource));

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
            if (IsSharedLibraryTestFile(relative)
                || relative.EndsWith("/components/DataGrid/DataGrid.tsx", StringComparison.Ordinal))
            {
                continue; // tests are not bundled; DataGrid.tsx is the grid itself
            }
            var source = File.ReadAllText(file);
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
}
