/**
 * xrmCapabilityAnalyzer — AST check that every `getXrm(...)` value is used only
 * for what its requested capability covers (task 081 rounds 5–8; reviews R4-2, R5-1, rounds 6–7).
 *
 * Not a test file itself: `xrmCapabilityUsage.guard.test.ts` runs it over the
 * repository's `src/`, and over synthetic fixtures that prove each tracked
 * shape fires.
 *
 * ## Default: anything not understood is a BLIND SPOT
 * Every context a value reaches is either FOLLOWED (listed below), an explicit
 * NOT-A-USE (listed below), or reported as a blind spot. There is no silent
 * catch-all.
 *
 * ## Origins
 * Modules are resolved: relative imports to scanned files, workspace package
 * names (`@spaarke/ui-components`, `…/sub/path`) to that package's `src/`. So the
 * type checker resolves symbols across files, through barrels, renamed
 * re-exports and namespace imports. A value ORIGINATES at:
 * - a call of anything whose resolved symbol is `getXrm` (the function in
 *   `xrmContext.ts`): a named / renamed / namespace import, a re-export, a
 *   `const g = getXrm` alias. A `getXrm` imported from a module that is NOT
 *   scanned is recognised by its original name and a `ui-components` /
 *   `xrmContext` specifier. Any other reference to `getXrm` (passed, stored) is
 *   a blind spot, and so is an unrecognised `getXrm` that is destructured
 *   (`const { getXrm } = ui`) or read off a dynamic import. A dynamic `import()`
 *   or `require()` of a module that exports `getXrm` — under ANY export name,
 *   matched by dealiased symbol — is a blind spot unless every use of its value
 *   provably avoids those names (`m.Other`, `{ Other }`).
 * - a call of a WRAPPER: a function, method, object-literal method or getter
 *   that RETURNS such a value, found by resolved symbol at every call site
 *   (`this.m()`, `svc.m()`, `new S().m()`, imports; a getter's `x.prop` read).
 *   A string-literal bracket call `svc['m']()` is a call. An IIFE is followed to
 *   its own call. Each `return` is its own origin (no union across returns).
 *   BLIND SPOTS: a wrapper referenced other than by a call (passed, stored,
 *   destructured `const { m } = svc` / `const { x } = new G()`, its object-literal
 *   holder passed to `Object.values` / `Object.entries`); an ANONYMOUS returning
 *   function (default export, JSX prop, `o.f = () => …`); a wrapper with NO
 *   resolved call; a class member that also exists on a type its class
 *   `extends` / `implements` (calls through that type are not resolved).
 * - FORWARDING: when the capability is a parameter of the enclosing function
 *   (`function f(cap) { getXrm(cap)… }`), the value is analysed ONCE PER CALL
 *   SITE of that function, with the caller's argument (or the default) bound —
 *   through chains of such functions. Uses inside the function body are checked
 *   with each caller's capability; a `return` of the value continues at that
 *   call site. A forwarding function with no call site, an anonymous one, or one
 *   that also exists on a base / interface type, is a blind spot.
 *
 * ## Followed
 * Parentheses, `!`, `as` / `<T>` / `satisfies`, `await`, the right side of `,`,
 * both sides of `??` / `||`, both branches of `?:`, the right side of `&&`;
 * `const` / `let` / `var` aliases, `=` assignments, default parameter values,
 * class property initialisers and `this.f = …` fields (every reference to the
 * resolved symbol, in any file); object destructuring (renames, nesting);
 * `useState(…)` / `useState(() => …)` (element 0 and values passed to its
 * setter); `useMemo(() => …)`; returns; call ARGUMENTS into functions resolved by
 * symbol (the parameter is followed like an alias, and a `return` of it continues
 * at THAT call only); an assignment to a setter, into its parameter; `new X(…)`
 * into X's constructor parameter (and the `this.field` it sets).
 *
 * ## Not a use (explicit allow-list)
 * A condition (`if` / `while` / `for` / `?:` test), `!x`, `typeof x`, a
 * comparison (`===`, `!==`, `==`, `!=`, `instanceof`, `in`), a type position
 * (`x is T`, `ReturnType<typeof getXrm>`), a declaration's own name or an
 * assignment target (a write, not a read), the left side of
 * `&&` or `,`, an expression statement, `void`, an import / export specifier, a
 * React dependency array (`[xrm]` as the last argument of a hook), `R` in
 * `R.m.bind(R)` / `R.m.call(R, …)` / `.apply(R, …)` (its `R.m` read is already a
 * use; any OTHER this-receiver is a blind spot), an argument to `console.*`.
 *
 * ## A USE and its COVERAGE
 * A use is `Root.member` with Root ∈ WebApi / Navigation / Utility / App / Page
 * / userSettings (dot or string-literal bracket); a bare root
 * (`if (!xrm?.Navigation)`) is `Root.?`. Any other member of an Xrm value
 * (`xrm.Device`, `xrm.Panel` …) is a blind spot. `WebApi` is ATOMIC: any WebApi
 * check covers `WebApi.*`, and a WebApi object handed anywhere is a `WebApi.*`
 * use.
 *   webApi → WebApi.* · navigation → Navigation.navigateTo · openForm →
 *   Navigation.openForm · openUrl → Navigation.openUrl · utility, clientUrl →
 *   Utility.getGlobalContext · lookupObjects → Utility.lookupObjects ·
 *   metadata → Utility.getEntityMetadata · pageContext → Utility.getPageContext
 *   · sidePanes → App.sidePanes · page → Page.* · a list covers the union.
 * A PREDICATE covers what its body GUARANTEES when it returns true:
 * `typeof x.R.m === 'function'`, `!!x.R.m`, `x.R.m != null` → `R.m`; `!!x.R` →
 * `R.?` only; `A && B` → both; `!A` → nothing; `A || B` → the union of the
 * disjuncts only when EVERY disjunct guarantees something (noted per site), else
 * nothing; a predicate that guarantees nothing is a blind spot. A predicate that
 * passes its parameter to a helper (`x => !!read(x)`) covers passing the value to
 * that same helper.
 * A capability that is a `let` / `var` or a parameter WRITTEN anywhere (an
 * assignment, a destructuring-assignment target, a for-in / for-of initializer,
 * `++` / `--`), a spread, or any other non-literal expression is a blind spot.
 *
 * ## Remaining limits (assumptions, not reported)
 * 1. UNION credit, recorded as a note on each such site: a `?:` capability, a
 *    `??` / `||` fallback between two lookups, an `||` predicate. That the code
 *    calls only the member its branch guaranteed is not checked.
 * 2. `WebApi` is assumed atomic.
 * 3. A value passed to the helper its predicate calls is treated as covered.
 * 4. UNSCANNED modules: a `getXrm` imported from one is matched by its original
 *    name and a `ui-components` / `xrmContext` specifier; a dynamic `import()` /
 *    `require()` of one is checked only when its specifier matches those. (Calls
 *    INTO unscanned modules are blind spots, not passes.)
 * 5. Calls the type checker resolves to a DIFFERENT symbol are not seen as calls
 *    of the wrapper: through a type the class does not declare in `extends` /
 *    `implements` (structural typing), or reflectively (a computed key
 *    `svc[k]()`, `{ ...svc }`, `for…in`, a member destructured in a PARAMETER).
 *    Such calls are missed when the wrapper ALSO has resolved calls; with none it
 *    is a blind spot (above).
 * 6. A site whose value reaches no `Root.member` use (a presence check) is not
 *    reported by the analyzer; the guard test pins those sites to an explicit,
 *    justified allow-list.
 * 7. Only `src/` non-test files are scanned. Out of scope: reads of `window.Xrm`
 *    / `parent.Xrm` that never go through `getXrm`.
 */

import * as fs from 'fs';
import * as path from 'path';
import * as ts from 'typescript';

export const ROOTS = new Set(['WebApi', 'Navigation', 'Utility', 'App', 'Page', 'userSettings']);

/** Capability name → the `Root.member` entries it guarantees. */
export const CAPABILITY_COVERAGE: Record<string, string[]> = {
  webApi: ['WebApi.*'],
  navigation: ['Navigation.navigateTo'],
  openForm: ['Navigation.openForm'],
  openUrl: ['Navigation.openUrl'],
  utility: ['Utility.getGlobalContext'],
  clientUrl: ['Utility.getGlobalContext'],
  lookupObjects: ['Utility.lookupObjects'],
  metadata: ['Utility.getEntityMetadata'],
  pageContext: ['Utility.getPageContext'],
  sidePanes: ['App.sidePanes'],
  page: ['Page.*'],
};

export interface SourceInput {
  fileName: string;
  text: string;
}

export interface AnalyzeOptions {
  /** Package name → package directory (its `src/` is the entry), in addition to those found via package.json. */
  packages?: Record<string, string>;
}

export interface XrmSite {
  file: string;
  line: number;
  /** Source text of the requested capability ('(default)' when none). */
  requested: string;
  /** Coverage entries, sorted. `UNKNOWN` when the capability could not be resolved. */
  coverage: string[];
  /** `Root.member` uses reached from this site, sorted, with any uncovered ones flagged. */
  uses: string[];
  notes: string[];
}

export interface XrmViolation {
  file: string;
  line: number;
  use: string;
  site: string;
  requested: string;
}

export interface XrmBlindSpot {
  file: string;
  line: number;
  site: string;
  reason: string;
}

export interface AnalysisResult {
  sites: XrmSite[];
  violations: XrmViolation[];
  blindSpots: XrmBlindSpot[];
}

// ---------------------------------------------------------------------------

type Coverage = {
  entries: Set<string>;
  /** Helpers a predicate passes its parameter to: passing the value to one of them is covered. */
  helpers: Set<ts.Node>;
  unknown: boolean;
  unknownReason?: string;
  /** Parameters the capability still refers to (resolved per call site of their function). */
  forwardParams: Set<ts.ParameterDeclaration>;
  text: string;
  notes: string[];
};

/** Parameter bindings while evaluating a forwarded capability expression. */
type Env = Map<ts.Symbol, { expr: ts.Expression | undefined }>; // undefined: argument omitted, no default
const EMPTY_ENV: Env = new Map();

/** One level of forwarding: the value's capability came from `fn`'s parameters, bound at `call`. */
interface ChainLink {
  fn: ts.FunctionLikeDeclaration;
  /** The call (or getter read) whose value a `return` inside `fn` becomes. */
  call: ts.Expression;
}

/** Bound on helper/forwarding nesting (recursion guard). */
const MAX_CHAIN = 12;

function argsOf(e: ts.Expression): readonly ts.Expression[] {
  return ts.isCallExpression(e) ? e.arguments : [];
}

interface Origin {
  id: number;
  kind: 'xrm' | 'root';
  root?: string;
  cov: Coverage;
  site: XrmSite;
  /** Innermost first: returning from chain[0].fn continues at chain[0].call. */
  chain: ChainLink[];
}

const TRANSPARENT = new Set([
  ts.SyntaxKind.ParenthesizedExpression,
  ts.SyntaxKind.AsExpression,
  ts.SyntaxKind.NonNullExpression,
  ts.SyntaxKind.TypeAssertionExpression,
  ts.SyntaxKind.SatisfiesExpression,
  ts.SyntaxKind.AwaitExpression,
]);

const COMPARISONS = new Set([
  ts.SyntaxKind.EqualsEqualsEqualsToken,
  ts.SyntaxKind.ExclamationEqualsEqualsToken,
  ts.SyntaxKind.EqualsEqualsToken,
  ts.SyntaxKind.ExclamationEqualsToken,
  ts.SyntaxKind.InstanceOfKeyword,
  ts.SyntaxKind.InKeyword,
]);

const HOOKS_WITH_DEPS = /^use(Effect|LayoutEffect|Memo|Callback|ImperativeHandle)$/;

function lineOf(node: ts.Node): number {
  const sf = node.getSourceFile();
  return sf.getLineAndCharacterOfPosition(node.getStart(sf)).line + 1;
}

function rel(fileName: string, root: string): string {
  return path.relative(root, fileName).replace(/\\/g, '/');
}

function norm(p: string): string {
  return path.resolve(p).replace(/\\/g, '/');
}

function calleeName(call: ts.CallExpression): string | undefined {
  const e = call.expression;
  if (ts.isIdentifier(e)) return e.text;
  if (ts.isPropertyAccessExpression(e)) return e.name.text;
  return undefined;
}

function skipT(e: ts.Expression): ts.Expression {
  while (TRANSPARENT.has(e.kind)) e = (e as ts.ParenthesizedExpression).expression;
  return e;
}

function newCov(text: string): Coverage {
  return { entries: new Set(), helpers: new Set(), unknown: false, forwardParams: new Set(), text, notes: [] };
}

export function analyze(inputs: SourceInput[], displayRoot = '', opts: AnalyzeOptions = {}): AnalysisResult {
  const texts = new Map(inputs.map(i => [norm(i.fileName), i.text]));
  const fileNames = [...texts.keys()];

  // ---- workspace packages (name → dir) for module resolution
  const pkgRoots = new Map<string, string>();
  for (const [name, dir] of Object.entries(opts.packages ?? {})) pkgRoots.set(name, norm(dir));
  const seenPkgDirs = new Set<string>();
  for (const f of fileNames) {
    const m = /^(.*?\/(?:client\/shared|client\/pcf|client\/code-pages|solutions)\/[^/]+)\//.exec(f);
    if (m && !seenPkgDirs.has(m[1])) {
      seenPkgDirs.add(m[1]);
      try {
        const name = JSON.parse(fs.readFileSync(path.join(m[1], 'package.json'), 'utf8')).name as string;
        if (name && !pkgRoots.has(name)) pkgRoots.set(name, m[1]);
      } catch {
        /* no package.json */
      }
    }
  }
  const tryFile = (base: string): string | undefined => {
    for (const ext of ['', '.ts', '.tsx', '/index.ts', '/index.tsx']) if (texts.has(base + ext)) return base + ext;
    return undefined;
  };
  function resolveModule(spec: string, from: string): string | undefined {
    if (spec.startsWith('.')) return tryFile(norm(path.resolve(path.dirname(from), spec)));
    for (const [name, dir] of pkgRoots) {
      if (spec === name) return tryFile(`${dir}/src/index`) ?? tryFile(`${dir}/index`);
      if (spec.startsWith(name + '/')) {
        const sub = spec.slice(name.length + 1);
        return tryFile(`${dir}/src/${sub}`) ?? tryFile(`${dir}/${sub}`);
      }
    }
    return undefined;
  }

  const options: ts.CompilerOptions = {
    noLib: true,
    types: [],
    jsx: ts.JsxEmit.Preserve,
    target: ts.ScriptTarget.ES2020,
    allowJs: false,
    noEmit: true,
  };
  const host = ts.createCompilerHost(options);
  host.getSourceFile = (fileName, lang) => {
    const key = norm(fileName);
    const text = texts.get(key);
    return text === undefined
      ? undefined
      : ts.createSourceFile(key, text, lang, true, key.endsWith('x') ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
  };
  host.fileExists = f => texts.has(norm(f));
  host.readFile = f => texts.get(norm(f));
  host.resolveModuleNames = (names, containing) =>
    names.map(n => {
      const r = resolveModule(n, norm(containing));
      return r ? { resolvedFileName: r, extension: r.endsWith('x') ? ts.Extension.Tsx : ts.Extension.Ts } : undefined;
    });
  const program = ts.createProgram(fileNames, options, host);
  const checker = program.getTypeChecker();
  const sources = program.getSourceFiles();
  const disp = (f: string) => (displayRoot ? rel(f, displayRoot) : path.basename(f));

  const sites: XrmSite[] = [];
  const violations: XrmViolation[] = [];
  const blindSpots: XrmBlindSpot[] = [];
  let nextId = 1;

  // ---- symbols
  function dealias(sym: ts.Symbol | undefined): ts.Symbol | undefined {
    let s = sym;
    for (let i = 0; s && s.flags & ts.SymbolFlags.Alias && i < 10; i++) {
      try {
        const next = checker.getAliasedSymbol(s);
        if (!next || next === s || !next.declarations?.length) return s; // unresolved module: keep the alias
        s = next;
      } catch {
        return s;
      }
    }
    return s;
  }
  function symbolOf(id: ts.Identifier): ts.Symbol | undefined {
    // `{ webApi }`: the shorthand's name resolves to the PROPERTY; the variable read is the value symbol.
    if (ts.isShorthandPropertyAssignment(id.parent) && id.parent.name === id)
      return dealias(checker.getShorthandAssignmentValueSymbol(id.parent));
    return dealias(checker.getSymbolAtLocation(id));
  }

  // ---- global identifier index: dealiased symbol → references (any file)
  const refIndex = new Map<ts.Symbol, ts.Identifier[]>();
  const getXrmNamed: ts.Identifier[] = [];
  /** `{ name }` / `{ name: x }` in object binding patterns (member destructuring). */
  const bindingElements: ts.BindingElement[] = [];
  /** `obj['name']` with a string-literal key. */
  const literalElementAccesses: ts.ElementAccessExpression[] = [];
  for (const sf of sources) {
    const visit = (n: ts.Node) => {
      if (ts.isBindingElement(n) && ts.isObjectBindingPattern(n.parent)) bindingElements.push(n);
      if (ts.isElementAccessExpression(n) && ts.isStringLiteralLike(n.argumentExpression))
        literalElementAccesses.push(n);
      if (ts.isIdentifier(n)) {
        const s = symbolOf(n);
        if (s) {
          const l = refIndex.get(s);
          if (l) l.push(n);
          else refIndex.set(s, [n]);
        }
        if (n.text === 'getXrm') getXrmNamed.push(n);
      }
      ts.forEachChild(n, visit);
    };
    visit(sf);
  }
  function referencesOf(sym: ts.Symbol | undefined, exclude?: ts.Node): ts.Identifier[] {
    if (!sym) return [];
    return (refIndex.get(sym) ?? []).filter(id => id !== exclude);
  }

  function findAncestor<T extends ts.Node>(n: ts.Node, pred: (x: ts.Node) => x is T): T | undefined {
    let p: ts.Node | undefined = n.parent;
    while (p && !pred(p)) p = p.parent;
    return p as T | undefined;
  }

  /** The function-like node a (dealiased) symbol names, if any. */
  function functionOfSymbol(sym: ts.Symbol | undefined): ts.FunctionLikeDeclaration | undefined {
    if (!sym?.declarations?.length) return undefined;
    const withBody = sym.declarations.find(
      d => (ts.isFunctionDeclaration(d) || ts.isMethodDeclaration(d)) && d.body
    ) as ts.FunctionLikeDeclaration | undefined;
    if (withBody) return withBody;
    for (const d of sym.declarations) {
      if (
        (ts.isVariableDeclaration(d) || ts.isPropertyDeclaration(d) || ts.isPropertyAssignment(d)) &&
        d.initializer &&
        (ts.isArrowFunction(d.initializer) || ts.isFunctionExpression(d.initializer))
      )
        return d.initializer;
    }
    return undefined;
  }
  /** Resolve a callee to its function by symbol (identifier, `a.b`, `this.m`, `new S().m`). */
  function resolveFunction(callee: ts.Expression): ts.FunctionLikeDeclaration | undefined {
    const e = skipT(callee);
    if (ts.isIdentifier(e)) return functionOfSymbol(symbolOf(e));
    if (ts.isPropertyAccessExpression(e)) return functionOfSymbol(dealias(checker.getSymbolAtLocation(e.name)));
    return undefined;
  }
  function fnKey(fn: ts.FunctionLikeDeclaration): ts.Symbol | undefined {
    if ((ts.isFunctionDeclaration(fn) || ts.isMethodDeclaration(fn) || ts.isGetAccessorDeclaration(fn)) && fn.name)
      return dealias(checker.getSymbolAtLocation(fn.name));
    const p = fn.parent;
    if ((ts.isVariableDeclaration(p) || ts.isPropertyDeclaration(p) || ts.isPropertyAssignment(p)) && p.name)
      return dealias(checker.getSymbolAtLocation(p.name));
    return undefined;
  }
  function fnName(fn: ts.FunctionLikeDeclaration): string {
    if (fn.name && ts.isIdentifier(fn.name)) return fn.name.text;
    const p = fn.parent;
    if (
      (ts.isVariableDeclaration(p) || ts.isPropertyDeclaration(p) || ts.isPropertyAssignment(p)) &&
      ts.isIdentifier(p.name)
    )
      return p.name.text;
    return '(function)';
  }

  /** Where `id` (a reference to a function's symbol) sits: a call, a declaration / specifier, or an escape. */
  function classifyRef(id: ts.Identifier): { call?: ts.CallExpression; skip?: boolean } {
    const p = id.parent;
    if (ts.isCallExpression(p) && p.expression === id) return { call: p };
    if (ts.isPropertyAccessExpression(p) && p.name === id) {
      const pp = p.parent;
      if (ts.isCallExpression(pp) && pp.expression === p) return { call: pp };
    }
    if (
      ts.isImportSpecifier(p) ||
      ts.isExportSpecifier(p) ||
      ts.isImportClause(p) ||
      ts.isNamespaceImport(p) ||
      ts.isExportAssignment(p) ||
      // A type position (`ReturnType<typeof getXrm>`) is not a value use.
      findAncestor(
        id,
        (n): n is ts.TypeNode => n.kind >= ts.SyntaxKind.FirstTypeNode && n.kind <= ts.SyntaxKind.LastTypeNode
      ) ||
      ((ts.isFunctionDeclaration(p) ||
        ts.isMethodDeclaration(p) ||
        ts.isGetAccessorDeclaration(p) ||
        ts.isSetAccessorDeclaration(p) ||
        ts.isVariableDeclaration(p) ||
        ts.isPropertyDeclaration(p) ||
        ts.isPropertyAssignment(p) ||
        ts.isParameter(p)) &&
        p.name === id)
    )
      return { skip: true };
    // React dependency array (`useCallback(…, [fn])`).
    if (
      ts.isArrayLiteralExpression(p) &&
      ts.isCallExpression(p.parent) &&
      p.parent.arguments[p.parent.arguments.length - 1] === p &&
      HOOKS_WITH_DEPS.test(calleeName(p.parent) ?? '')
    )
      return { skip: true };
    return {};
  }
  /** `(() => …)()` / `(function () { … })()`: the call that invokes `fn` in place. */
  function iifeCall(fn: ts.FunctionLikeDeclaration): ts.CallExpression | undefined {
    if (!ts.isArrowFunction(fn) && !ts.isFunctionExpression(fn)) return undefined;
    let cur: ts.Node = fn;
    let p: ts.Node = fn.parent;
    while (ts.isParenthesizedExpression(p)) {
      cur = p;
      p = p.parent;
    }
    return ts.isCallExpression(p) && p.expression === cur ? p : undefined;
  }
  /**
   * Where `fn`'s return value goes: its call sites (an IIFE's own call; a getter's
   * `x.prop` reads), references that are neither (escapes), or `anonymous` when `fn`
   * has no name the analyzer can find references to.
   */
  function usesOfFunction(fn: ts.FunctionLikeDeclaration): {
    calls: ts.Expression[];
    escapes: ts.Node[];
    anonymous: boolean;
  } {
    const calls: ts.Expression[] = [];
    const escapes: ts.Node[] = [];
    const iife = iifeCall(fn);
    if (iife) return { calls: [iife], escapes, anonymous: false };
    const key = fnKey(fn);
    if (!key) return { calls, escapes, anonymous: true };
    let ids = referencesOf(key);
    if (
      ts.isMethodDeclaration(fn) ||
      ts.isGetAccessorDeclaration(fn) ||
      ts.isPropertyAssignment(fn.parent) ||
      ts.isPropertyDeclaration(fn.parent)
    ) {
      // `svc.get` / `new S().get` can resolve to a TRANSIENT property symbol (a widened or
      // instantiated type's member): match those through their declarations.
      const name = fnName(fn);
      for (const [s, l] of refIndex)
        if (
          s !== key &&
          s.name === name &&
          (functionOfSymbol(s) === fn || s.declarations?.includes(fn as unknown as ts.Declaration))
        )
          ids = ids.concat(l);
    }
    const isMember =
      ts.isMethodDeclaration(fn) ||
      ts.isGetAccessorDeclaration(fn) ||
      ts.isPropertyAssignment(fn.parent) ||
      ts.isPropertyDeclaration(fn.parent);
    if (isMember) {
      const name = fnName(fn);
      const isOurs = (s: ts.Symbol | undefined): boolean =>
        !!s &&
        (s === key ||
          functionOfSymbol(s) === fn ||
          !!s.declarations?.some(d => d === (fn as ts.Node) || d === fn.parent));
      // `svc['get']()`: a string-literal bracket access is a call (or a getter read) or an escape.
      for (const ea of literalElementAccesses) {
        if ((ea.argumentExpression as ts.StringLiteralLike).text !== name) continue;
        const s =
          dealias(checker.getSymbolAtLocation(ea.argumentExpression)) ??
          checker.getTypeAtLocation(ea.expression).getProperty(name);
        if (!isOurs(s)) continue;
        const pp = ea.parent;
        if (ts.isGetAccessorDeclaration(fn)) calls.push(ea);
        else if (ts.isCallExpression(pp) && pp.expression === ea) calls.push(pp);
        else escapes.push(ea);
      }
      // `const { get } = svc` / `const { x } = new G()`: the member leaves as a value.
      for (const el of bindingElements) {
        const prop = el.propertyName ?? el.name;
        if (!ts.isIdentifier(prop) || prop.text !== name) continue;
        const holder = el.parent.parent;
        const init = ts.isVariableDeclaration(holder) ? holder.initializer : undefined;
        const s = init ? checker.getTypeAtLocation(init).getProperty(name) : undefined;
        if (isOurs(s)) escapes.push(el);
      }
      // `Object.values(svc)` / `Object.entries(svc)` over an object literal holding the wrapper.
      const obj = ts.isPropertyAssignment(fn.parent) ? fn.parent.parent : fn.parent;
      if (
        ts.isObjectLiteralExpression(obj) &&
        ts.isVariableDeclaration(obj.parent) &&
        ts.isIdentifier(obj.parent.name)
      ) {
        for (const ref of referencesOf(checker.getSymbolAtLocation(obj.parent.name), obj.parent.name)) {
          const call = ref.parent;
          if (
            ts.isCallExpression(call) &&
            call.arguments.includes(ref) &&
            /^Object\.(values|entries)$/.test(call.expression.getText())
          )
            escapes.push(call);
        }
      }
    }
    for (const id of ids) {
      if (ts.isGetAccessorDeclaration(fn)) {
        // A getter's read `x.prop` is its call.
        const p = id.parent;
        if (ts.isPropertyAccessExpression(p) && p.name === id) {
          const pp = p.parent;
          const isWrite =
            ts.isBinaryExpression(pp) &&
            pp.left === p &&
            pp.operatorToken.kind >= ts.SyntaxKind.FirstAssignment &&
            pp.operatorToken.kind <= ts.SyntaxKind.LastAssignment;
          if (!isWrite) calls.push(p);
          continue;
        }
      }
      const c = classifyRef(id);
      if (c.call) calls.push(c.call);
      else if (!c.skip) escapes.push(id);
    }
    return { calls, escapes, anonymous: false };
  }

  // ---- getXrm identification (by resolved symbol)
  function isGetXrmDecl(d: ts.Declaration | undefined): boolean {
    return (
      !!d &&
      ts.isFunctionDeclaration(d) &&
      d.name?.text === 'getXrm' &&
      /xrmContext\.tsx?$/.test(d.getSourceFile().fileName)
    );
  }
  const getXrmMemo = new Map<ts.Symbol, boolean>();
  function isGetXrmSymbol(sym: ts.Symbol | undefined, depth = 0): boolean {
    if (!sym || depth > 6) return false;
    const memo = getXrmMemo.get(sym);
    if (memo !== undefined) return memo;
    getXrmMemo.set(sym, false);
    let r = false;
    const d = sym.declarations?.[0];
    if (sym.declarations?.some(x => isGetXrmDecl(x))) r = true;
    else if (d && ts.isImportSpecifier(d) && sym.flags & ts.SymbolFlags.Alias) {
      // Unresolved (unscanned) module: original name + specifier.
      const original = (d.propertyName ?? d.name).text;
      const spec = (d.parent.parent.parent.moduleSpecifier as ts.StringLiteral).text;
      r = original === 'getXrm' && /ui-components|xrmContext/.test(spec);
    } else if (d && ts.isVariableDeclaration(d) && d.initializer) {
      const init = skipT(d.initializer);
      if (ts.isIdentifier(init)) r = isGetXrmSymbol(symbolOf(init), depth + 1);
      else if (ts.isPropertyAccessExpression(init))
        r = isGetXrmSymbol(dealias(checker.getSymbolAtLocation(init.name)), depth + 1) || isNsGetXrm(init);
    }
    getXrmMemo.set(sym, r);
    return r;
  }
  /** `ui.getXrm` where `ui` is a namespace import of an unscanned ui-components / xrmContext module. */
  function isNsGetXrm(pa: ts.PropertyAccessExpression): boolean {
    if (pa.name.text !== 'getXrm' || !ts.isIdentifier(pa.expression)) return false;
    const d = checker.getSymbolAtLocation(pa.expression)?.declarations?.[0];
    if (!d || !ts.isNamespaceImport(d)) return false;
    const spec = (d.parent.parent.moduleSpecifier as ts.StringLiteral).text;
    return /ui-components|xrmContext/.test(spec);
  }
  function isGetXrmCallee(callee: ts.Expression): boolean {
    const e = skipT(callee);
    if (ts.isIdentifier(e)) return isGetXrmSymbol(symbolOf(e));
    if (ts.isPropertyAccessExpression(e))
      return isGetXrmSymbol(dealias(checker.getSymbolAtLocation(e.name))) || isNsGetXrm(e);
    return false;
  }

  // ---- capability resolution
  function chainGuarantee(e: ts.Expression, pSym: ts.Symbol): string | undefined {
    // Walk x.R.m(...)?.… down to the parameter; report the first two segments.
    const segs: string[] = [];
    let cur: ts.Expression = skipT(e);
    for (;;) {
      if (ts.isCallExpression(cur)) cur = skipT(cur.expression);
      else if (ts.isPropertyAccessExpression(cur)) {
        segs.unshift(cur.name.text);
        cur = skipT(cur.expression);
      } else if (ts.isElementAccessExpression(cur) && ts.isStringLiteralLike(cur.argumentExpression)) {
        segs.unshift(cur.argumentExpression.text);
        cur = skipT(cur.expression);
      } else break;
    }
    if (!ts.isIdentifier(cur) || checker.getSymbolAtLocation(cur) !== pSym || !segs.length) return undefined;
    const [root, member] = segs;
    if (!ROOTS.has(root)) return undefined;
    if (root === 'WebApi') return 'WebApi.*';
    return member ? `${root}.${member}` : `${root}.?`;
  }
  /** What a predicate body guarantees about its parameter when TRUTHY. */
  function guarantee(e: ts.Expression, pSym: ts.Symbol, out: Coverage, depth: number): Set<string> {
    const g = new Set<string>();
    if (depth > 12) return g;
    e = skipT(e);
    if (ts.isBinaryExpression(e)) {
      const op = e.operatorToken.kind;
      if (op === ts.SyntaxKind.AmpersandAmpersandToken) {
        guarantee(e.left, pSym, out, depth + 1).forEach(x => g.add(x));
        guarantee(e.right, pSym, out, depth + 1).forEach(x => g.add(x));
        return g;
      }
      if (op === ts.SyntaxKind.BarBarToken) {
        const l = guarantee(e.left, pSym, out, depth + 1);
        const r = guarantee(e.right, pSym, out, depth + 1);
        if (l.size && r.size) {
          l.forEach(x => g.add(x));
          r.forEach(x => g.add(x));
          const note = '|| predicate — union of the disjuncts';
          if (!out.notes.includes(note)) out.notes.push(note);
        }
        return g;
      }
      if (COMPARISONS.has(op) && op !== ts.SyntaxKind.InstanceOfKeyword && op !== ts.SyntaxKind.InKeyword) {
        const positive = op === ts.SyntaxKind.EqualsEqualsEqualsToken || op === ts.SyntaxKind.EqualsEqualsToken;
        for (const [a, b] of [
          [e.left, e.right],
          [e.right, e.left],
        ] as const) {
          const sa = skipT(a);
          const sb = skipT(b);
          if (ts.isTypeOfExpression(sa) && ts.isStringLiteralLike(sb)) {
            const ok = positive ? sb.text !== 'undefined' : sb.text === 'undefined';
            const c = ok ? chainGuarantee(sa.expression, pSym) : undefined;
            if (c) g.add(c);
            return g;
          }
          const nullish = sb.kind === ts.SyntaxKind.NullKeyword || (ts.isIdentifier(sb) && sb.text === 'undefined');
          if (nullish && !positive) {
            const c = chainGuarantee(sa, pSym);
            if (c) g.add(c);
            return g;
          }
        }
        return g;
      }
      return g;
    }
    if (ts.isPrefixUnaryExpression(e) && e.operator === ts.SyntaxKind.ExclamationToken) {
      const inner = skipT(e.operand);
      if (ts.isPrefixUnaryExpression(inner) && inner.operator === ts.SyntaxKind.ExclamationToken)
        return guarantee(inner.operand, pSym, out, depth + 1);
      return g; // a negation guarantees no member
    }
    if (ts.isCallExpression(e)) {
      if (ts.isIdentifier(e.expression) && e.expression.text === 'Boolean' && e.arguments[0])
        return guarantee(e.arguments[0], pSym, out, depth + 1);
      const passesParam = e.arguments.some(a => {
        const s = skipT(a);
        return ts.isIdentifier(s) && checker.getSymbolAtLocation(s) === pSym;
      });
      if (passesParam) {
        const helper = resolveFunction(e.expression);
        if (helper) {
          out.helpers.add(helper);
          g.add(`helper:${fnName(helper)}`);
        }
        return g;
      }
    }
    const c = chainGuarantee(e, pSym);
    if (c) g.add(c);
    return g;
  }
  function predicateCoverage(fn: ts.ArrowFunction | ts.FunctionExpression, cov: Coverage) {
    const p = fn.parameters[0];
    const pSym = p && ts.isIdentifier(p.name) ? checker.getSymbolAtLocation(p.name) : undefined;
    if (!pSym || !fn.body) return;
    const returns: ts.Expression[] = [];
    if (!ts.isBlock(fn.body)) returns.push(fn.body);
    else {
      const visit = (n: ts.Node) => {
        if (ts.isReturnStatement(n) && n.expression) returns.push(n.expression);
        if (!ts.isFunctionLike(n)) ts.forEachChild(n, visit);
      };
      ts.forEachChild(fn.body, visit);
    }
    let acc: Set<string> | undefined;
    for (const r of returns) {
      const g = guarantee(r, pSym, cov, 0);
      acc = acc ? new Set([...acc].filter(x => g.has(x))) : g;
    }
    for (const x of acc ?? []) if (!x.startsWith('helper:')) cov.entries.add(x);
  }
  /**
   * Whether `id` is WRITTEN: an assignment target (`x = …`, `x += …`), inside a
   * destructuring-assignment target (`({ x } = o)`, `[x] = a`), a for-in / for-of
   * initializer (`for (x of a)`), or `++` / `--`.
   */
  function isWriteRef(id: ts.Identifier): boolean {
    let cur: ts.Node = id;
    let p: ts.Node = id.parent;
    if (
      (ts.isPrefixUnaryExpression(p) || ts.isPostfixUnaryExpression(p)) &&
      (p.operator === ts.SyntaxKind.PlusPlusToken || p.operator === ts.SyntaxKind.MinusMinusToken)
    )
      return true;
    // Climb out of a destructuring-assignment pattern.
    while (
      ts.isParenthesizedExpression(p) ||
      ts.isArrayLiteralExpression(p) ||
      ts.isObjectLiteralExpression(p) ||
      ts.isSpreadElement(p) ||
      ts.isSpreadAssignment(p) ||
      (ts.isShorthandPropertyAssignment(p) && p.name === cur) ||
      (ts.isPropertyAssignment(p) && p.initializer === cur)
    ) {
      cur = p;
      p = p.parent;
    }
    if (
      ts.isBinaryExpression(p) &&
      p.left === cur &&
      p.operatorToken.kind >= ts.SyntaxKind.FirstAssignment &&
      p.operatorToken.kind <= ts.SyntaxKind.LastAssignment
    )
      return true;
    return (ts.isForInStatement(p) || ts.isForOfStatement(p)) && p.initializer === cur;
  }
  function isReassigned(sym: ts.Symbol, decl: ts.Node): boolean {
    return referencesOf(sym, (decl as ts.VariableDeclaration | ts.ParameterDeclaration).name).some(isWriteRef);
  }
  function resolveCoverage(arg: ts.Expression | undefined, env: Env = EMPTY_ENV, depth = 0): Coverage {
    const cov = newCov(arg ? arg.getText() : '(default)');
    if (!arg) {
      CAPABILITY_COVERAGE.webApi.forEach(e => cov.entries.add(e));
      return cov;
    }
    const unknown = (why: string) => {
      cov.unknown = true;
      cov.unknownReason ??= why;
    };
    if (depth > 8) {
      unknown('capability nested too deeply');
      return cov;
    }
    const e = skipT(arg);
    const merge = (sub: Coverage) => {
      sub.entries.forEach(x => cov.entries.add(x));
      sub.helpers.forEach(x => cov.helpers.add(x));
      sub.forwardParams.forEach(x => cov.forwardParams.add(x));
      if (sub.unknown) unknown(sub.unknownReason ?? 'unresolvable');
      sub.notes.forEach(n => cov.notes.includes(n) || cov.notes.push(n));
    };
    if (ts.isStringLiteralLike(e)) {
      const c = CAPABILITY_COVERAGE[e.text];
      if (c) c.forEach(x => cov.entries.add(x));
      else unknown(`unknown capability '${e.text}'`);
    } else if (ts.isArrayLiteralExpression(e)) {
      for (const el of e.elements) {
        if (ts.isSpreadElement(el)) unknown('spread in a capability list');
        else merge(resolveCoverage(el, env, depth + 1));
      }
    } else if (ts.isArrowFunction(e) || ts.isFunctionExpression(e)) {
      predicateCoverage(e, cov);
      if (cov.entries.size === 0 && cov.helpers.size === 0) unknown('predicate guarantees no member');
    } else if (ts.isConditionalExpression(e)) {
      merge(resolveCoverage(e.whenTrue, env, depth + 1));
      merge(resolveCoverage(e.whenFalse, env, depth + 1));
      cov.notes.push('capability chosen by ?: — union of branches');
    } else if (ts.isIdentifier(e)) {
      const sym = symbolOf(e);
      const bound = sym && env.get(sym);
      const decl = sym?.declarations?.[0];
      if (decl && ts.isParameter(decl) && isReassigned(sym!, decl))
        unknown(`capability parameter '${e.text}' is reassigned`);
      else if (bound) merge(resolveCoverage(bound.expr, env, depth + 1));
      else if (decl && ts.isParameter(decl) && ts.isIdentifier(decl.name)) cov.forwardParams.add(decl);
      else if (decl && ts.isVariableDeclaration(decl) && decl.initializer) {
        const isConst = !!(ts.getCombinedNodeFlags(decl) & ts.NodeFlags.Const);
        if (!isConst && isReassigned(sym!, decl)) unknown(`capability variable '${e.text}' is reassigned`);
        else merge(resolveCoverage(decl.initializer, env, depth + 1));
      } else unknown(`capability '${e.text}' not statically resolvable`);
    } else unknown('capability not statically resolvable');
    return cov;
  }

  function covers(cov: Coverage, use: string): boolean {
    if (cov.unknown) return true; // already reported as a blind spot at the site
    const [root, member] = use.split('.');
    for (const ent of cov.entries) {
      const [r, m] = ent.split('.');
      if (r !== root) continue;
      if (m === '*' || member === '?' || m === member) return true;
    }
    return false;
  }

  // ---- propagation
  const seen = new Set<string>();

  function record(o: Origin, node: ts.Node, use: string) {
    const covered = covers(o.cov, use);
    const label = covered ? use : `${use} ✗`;
    if (!o.site.uses.includes(label)) o.site.uses.push(label);
    if (!covered)
      violations.push({
        file: disp(node.getSourceFile().fileName),
        line: lineOf(node),
        use,
        site: `${o.site.file}:${o.site.line}`,
        requested: o.site.requested,
      });
  }
  function blind(o: { site: XrmSite }, node: ts.Node, reason: string) {
    blindSpots.push({
      file: disp(node.getSourceFile().fileName),
      line: lineOf(node),
      site: `${o.site.file}:${o.site.line}`,
      reason,
    });
    const n = `blind: ${reason}`;
    if (!o.site.notes.includes(n)) o.site.notes.push(n);
  }
  const fork = (o: Origin, patch: Partial<Origin> = {}): Origin => ({ ...o, id: nextId++, ...patch });

  function propagateAlias(sym: ts.Symbol | undefined, declName: ts.Node, o: Origin) {
    for (const ref of referencesOf(sym, declName)) propagate(ref, o);
  }
  /** Every reference to a property/field symbol (`this.f`, `inst.f`), in any file. */
  function propagateMember(sym: ts.Symbol | undefined, declName: ts.Node, o: Origin) {
    for (const ref of referencesOf(sym, declName)) {
      const p = ref.parent;
      if ((ts.isPropertyAccessExpression(p) && p.name === ref) || ts.isElementAccessExpression(p)) {
        const pp = p.parent;
        if (ts.isBinaryExpression(pp) && pp.left === p && pp.operatorToken.kind === ts.SyntaxKind.EqualsToken) continue; // a write, not a read
        propagate(p as ts.Expression, o);
      } else if (!classifyRef(ref).skip) blind(o, ref, 'field referenced other than by member access');
    }
  }

  function bindPattern(pattern: ts.BindingName, o: Origin) {
    if (ts.isIdentifier(pattern)) {
      propagateAlias(checker.getSymbolAtLocation(pattern), pattern, o);
      return;
    }
    if (ts.isObjectBindingPattern(pattern)) {
      for (const el of pattern.elements) {
        if (el.dotDotDotToken) {
          blind(o, el, 'rest element in destructuring');
          continue;
        }
        const prop = el.propertyName
          ? ts.isIdentifier(el.propertyName) || ts.isStringLiteral(el.propertyName)
            ? el.propertyName.text
            : undefined
          : ts.isIdentifier(el.name)
            ? el.name.text
            : undefined;
        if (!prop) {
          blind(o, el, 'computed destructuring key');
          continue;
        }
        if (o.kind === 'xrm') {
          if (ROOTS.has(prop)) bindPattern(el.name, fork(o, { kind: 'root', root: prop }));
          else blind(o, el, `member '${prop}' outside the checked roots`);
        } else {
          record(o, el, `${o.root}.${prop}`);
        }
      }
      return;
    }
    blind(o, pattern, 'array destructuring of an Xrm value');
  }

  function propagate(node: ts.Expression, o: Origin): void {
    const key = `${node.getSourceFile().fileName}:${node.pos}:${node.end}:${o.id}`;
    if (seen.has(key)) return;
    seen.add(key);

    // A declaration's own name (`let x;`) or an assignment target (`x = …`) is not a read.
    {
      const p = node.parent;
      if (
        ((ts.isVariableDeclaration(p) ||
          ts.isParameter(p) ||
          ts.isBindingElement(p) ||
          ts.isPropertyDeclaration(p) ||
          ts.isPropertyAssignment(p)) &&
          p.name === node) ||
        (ts.isBinaryExpression(p) &&
          p.left === node &&
          p.operatorToken.kind >= ts.SyntaxKind.FirstAssignment &&
          p.operatorToken.kind <= ts.SyntaxKind.LastAssignment)
      )
        return;
    }

    let cur: ts.Node = node;
    let parent: ts.Node = cur.parent;
    // Ascend through value-preserving wrappers.
    for (;;) {
      if (TRANSPARENT.has(parent.kind)) {
        cur = parent;
        parent = parent.parent;
        continue;
      }
      if (ts.isBinaryExpression(parent) && parent.operatorToken.kind === ts.SyntaxKind.CommaToken) {
        if (parent.left === cur) return; // discarded: not a use
        cur = parent;
        parent = parent.parent;
        continue;
      }
      if (
        ts.isBinaryExpression(parent) &&
        [ts.SyntaxKind.QuestionQuestionToken, ts.SyntaxKind.BarBarToken].includes(parent.operatorToken.kind)
      ) {
        // The value may come from either side. When the other side is another
        // lookup, downstream code is checked against the UNION (noted).
        const other = skipT(parent.left === cur ? parent.right : parent.left);
        if (ts.isCallExpression(other) && isGetXrmCallee(other.expression)) {
          const oc = resolveCoverage(other.arguments[0]);
          const union: Coverage = {
            ...o.cov,
            entries: new Set([...o.cov.entries, ...oc.entries]),
            helpers: new Set([...o.cov.helpers, ...oc.helpers]),
            unknown: o.cov.unknown || oc.unknown,
          };
          const note = `?? fallback to ${oc.text} — union of both`;
          if (!o.site.notes.includes(note)) o.site.notes.push(note);
          o = fork(o, { cov: union });
        }
        cur = parent;
        parent = parent.parent;
        continue;
      }
      if (ts.isConditionalExpression(parent) && (parent.whenTrue === cur || parent.whenFalse === cur)) {
        cur = parent;
        parent = parent.parent;
        continue;
      }
      if (
        ts.isBinaryExpression(parent) &&
        parent.operatorToken.kind === ts.SyntaxKind.AmpersandAmpersandToken &&
        parent.right === cur
      ) {
        cur = parent;
        parent = parent.parent;
        continue;
      }
      break;
    }

    // Member access.
    if ((ts.isPropertyAccessExpression(parent) || ts.isElementAccessExpression(parent)) && parent.expression === cur) {
      let name: string | undefined;
      if (ts.isPropertyAccessExpression(parent)) name = parent.name.text;
      else if (ts.isStringLiteralLike(parent.argumentExpression)) name = parent.argumentExpression.text;
      if (name === undefined) {
        blind(o, parent, 'non-literal bracket access');
        return;
      }
      if (o.kind === 'xrm') {
        if (!ROOTS.has(name)) {
          blind(o, parent, `member '${name}' outside the checked roots`);
          return;
        }
        const ro = fork(o, { kind: 'root', root: name });
        // Bare root use (e.g. `if (!xrm?.Navigation)`) unless a member follows.
        let p2: ts.Node = parent.parent;
        let c2: ts.Node = parent;
        while (TRANSPARENT.has(p2.kind)) {
          c2 = p2;
          p2 = p2.parent;
        }
        const memberFollows =
          (ts.isPropertyAccessExpression(p2) || ts.isElementAccessExpression(p2)) && p2.expression === c2;
        if (!memberFollows && !isFlowingPosition(p2, c2)) record(o, parent, `${name}.?`);
        propagate(parent, ro);
      } else {
        record(o, parent, `${o.root}.${name}`);
      }
      return;
    }

    // `const x = …` / destructuring / useState tuple.
    if (ts.isVariableDeclaration(parent) && parent.initializer === cur) {
      if (ts.isArrayBindingPattern(parent.name)) {
        if (ts.isCallExpression(cur) && calleeName(cur) === 'useState') {
          const [first] = parent.name.elements;
          if (first && ts.isBindingElement(first)) bindPattern(first.name, o);
          return;
        }
        blind(o, parent, 'array destructuring of an Xrm value');
        return;
      }
      bindPattern(parent.name, o);
      return;
    }

    // Default parameter value.
    if (ts.isParameter(parent) && parent.initializer === cur) {
      bindPattern(parent.name, o);
      return;
    }

    // Class property initialiser: every reference to the field, in any file.
    if (ts.isPropertyDeclaration(parent) && parent.initializer === cur) {
      propagateMember(dealias(checker.getSymbolAtLocation(parent.name)), parent.name, o);
      return;
    }

    // Assignment.
    if (
      ts.isBinaryExpression(parent) &&
      parent.operatorToken.kind === ts.SyntaxKind.EqualsToken &&
      parent.right === cur
    ) {
      const left = skipT(parent.left);
      if (ts.isIdentifier(left)) {
        propagateAlias(symbolOf(left), left, o);
        return;
      }
      if (ts.isPropertyAccessExpression(left)) {
        const sym = dealias(checker.getSymbolAtLocation(left.name));
        // A setter: the value is its parameter (typically stored in a backing field).
        const setter = sym?.declarations?.find(ts.isSetAccessorDeclaration);
        if (setter) {
          if (setter.parameters[0]) bindPattern(setter.parameters[0].name, o);
          else blind(o, parent, 'setter without a parameter');
          return;
        }
        if (sym?.declarations?.length) {
          propagateMember(sym, left.name, o);
          return;
        }
        if (left.expression.kind === ts.SyntaxKind.ThisKeyword) {
          // Undeclared field (`this.f = …` in JS-style code): every `this.f` in the class.
          const cls = findAncestor(left, ts.isClassLike);
          const visit = (n: ts.Node) => {
            if (
              ts.isPropertyAccessExpression(n) &&
              n.expression.kind === ts.SyntaxKind.ThisKeyword &&
              n.name.text === left.name.text &&
              n !== left
            )
              propagate(n, o);
            ts.forEachChild(n, visit);
          };
          if (cls) visit(cls);
          return;
        }
      }
      blind(o, parent, 'assigned to a property of another object');
      return;
    }

    // Returned from a function.
    if (
      ts.isReturnStatement(parent) ||
      ((ts.isArrowFunction(parent) || ts.isFunctionExpression(parent)) && parent.body === cur)
    ) {
      const fn = ts.isReturnStatement(parent) ? findAncestor(parent, ts.isFunctionLike) : (parent as ts.ArrowFunction);
      if (!fn) {
        blind(o, parent, 'returned outside a function');
        return;
      }
      // useMemo(() => X) / useState(() => X) — the call's value.
      if (
        (ts.isArrowFunction(fn) || ts.isFunctionExpression(fn)) &&
        ts.isCallExpression(fn.parent) &&
        fn.parent.arguments.includes(fn as ts.Expression)
      ) {
        const hook = calleeName(fn.parent);
        if (hook === 'useMemo' || hook === 'useState') {
          propagate(fn.parent, o);
          return;
        }
        blind(o, fn, `returned from a callback passed to ${hook ?? 'a call'}`);
        return;
      }
      // The value entered this function through a parameter bound at ONE call (a helper
      // argument, or a forwarded capability): the return continues at that call only.
      if (o.chain.length && fn === o.chain[0].fn) {
        propagate(o.chain[0].call, fork(o, { chain: o.chain.slice(1) }));
        return;
      }
      followReturn(fn as ts.FunctionLikeDeclaration, o);
      return;
    }

    // Passed as an argument.
    if (ts.isCallExpression(parent) && parent.arguments.includes(cur as ts.Expression)) {
      const callee = skipT(parent.expression);
      // `R.m.bind(R)` / `.call(R, …)` / `.apply(R, …)`: R is the receiver of its own method,
      // whose `R.m` read is already a recorded use. Any OTHER `this` receiver is a blind spot.
      if (
        ts.isPropertyAccessExpression(callee) &&
        ['bind', 'call', 'apply'].includes(callee.name.text) &&
        parent.arguments[0] === cur
      ) {
        const method = skipT(callee.expression);
        const sameReceiver = (a: ts.Expression, b: ts.Expression): boolean => {
          const x = skipT(a);
          const y = skipT(b);
          if (ts.isIdentifier(x) && ts.isIdentifier(y)) return !!symbolOf(x) && symbolOf(x) === symbolOf(y);
          return x.getText() === y.getText();
        };
        if (
          (ts.isPropertyAccessExpression(method) || ts.isElementAccessExpression(method)) &&
          sameReceiver(method.expression, cur as ts.Expression)
        )
          return;
        blind(o, parent, `passed as the this-receiver of .${callee.name.text}()`);
        return;
      }
      // Logging is not a use.
      if (
        ts.isPropertyAccessExpression(callee) &&
        ts.isIdentifier(callee.expression) &&
        callee.expression.text === 'console'
      )
        return;
      // React dependency array handled below; useState(x).
      const idx = parent.arguments.indexOf(cur as ts.Expression);
      if (ts.isIdentifier(callee)) {
        const setter = setters.get(symbolOf(callee) as ts.Symbol);
        if (setter) {
          if (setter.state && ts.isBindingElement(setter.state)) bindPattern(setter.state.name, o);
          return;
        }
      }
      if (calleeName(parent) === 'useState') {
        propagate(parent, o);
        return;
      }
      const fn = resolveFunction(callee);
      if (fn && o.cov.helpers.has(fn)) {
        // The predicate selected a frame on which this same helper answers.
        const label = `helper ${fnName(fn)}(…)`;
        if (!o.site.uses.includes(label)) o.site.uses.push(label);
        return;
      }
      if (fn && fn.parameters[idx] && !fn.parameters[idx].dotDotDotToken) {
        if (o.chain.length >= MAX_CHAIN) {
          blind(o, parent, 'helper nesting too deep (recursion?)');
          return;
        }
        // Tie a `return` of this parameter to THIS call (an identity helper must not
        // send every caller's value to every call).
        bindPattern(fn.parameters[idx].name, fork(o, { chain: [{ fn, call: parent }, ...o.chain] }));
        return;
      }
      if (o.kind === 'root' && o.root === 'WebApi') {
        record(o, parent, 'WebApi.*'); // a WebApi object handed to a data helper (atomic)
        return;
      }
      blind(o, parent, `passed to unresolved call ${callee.getText().slice(0, 60)}`);
      return;
    }

    // React dependency array (`useEffect(fn, [xrm])`): not an escape.
    if (
      ts.isArrayLiteralExpression(parent) &&
      ts.isCallExpression(parent.parent) &&
      parent.parent.arguments[parent.parent.arguments.length - 1] === parent &&
      HOOKS_WITH_DEPS.test(calleeName(parent.parent) ?? '')
    )
      return;

    if (
      o.kind === 'root' &&
      o.root === 'WebApi' &&
      (ts.isNewExpression(parent) ||
        ts.isJsxExpression(parent) ||
        ts.isShorthandPropertyAssignment(parent) ||
        ts.isPropertyAssignment(parent) ||
        ts.isArrayLiteralExpression(parent) ||
        ts.isSpreadElement(parent))
    ) {
      record(o, parent, 'WebApi.*');
      return;
    }
    if (ts.isNewExpression(parent) && parent.arguments?.includes(cur as ts.Expression)) {
      const ctorParam = resolveConstructorParam(parent.expression, parent.arguments.indexOf(cur as ts.Expression));
      if (ctorParam) {
        followConstructorParam(ctorParam, o);
        return;
      }
      blind(o, parent, `passed to new ${parent.expression.getText().slice(0, 40)}`);
      return;
    }

    // ---- explicit NOT-A-USE contexts
    if (
      (parent.kind >= ts.SyntaxKind.FirstTypeNode && parent.kind <= ts.SyntaxKind.LastTypeNode) || // a type position
      ((ts.isIfStatement(parent) || ts.isWhileStatement(parent) || ts.isDoStatement(parent)) &&
        parent.expression === cur) ||
      (ts.isForStatement(parent) && parent.condition === cur) ||
      (ts.isConditionalExpression(parent) && parent.condition === cur) ||
      (ts.isPrefixUnaryExpression(parent) && parent.operator === ts.SyntaxKind.ExclamationToken) ||
      ts.isTypeOfExpression(parent) ||
      ts.isVoidExpression(parent) ||
      ts.isExpressionStatement(parent) ||
      (ts.isBinaryExpression(parent) &&
        (COMPARISONS.has(parent.operatorToken.kind) ||
          (parent.operatorToken.kind === ts.SyntaxKind.AmpersandAmpersandToken && parent.left === cur)))
    )
      return;

    // ---- everything else is a blind spot
    if (
      ts.isShorthandPropertyAssignment(parent) ||
      ts.isPropertyAssignment(parent) ||
      ts.isArrayLiteralExpression(parent) ||
      ts.isSpreadElement(parent)
    ) {
      blind(o, parent, 'placed in an object / array literal');
      return;
    }
    if (ts.isJsxExpression(parent)) {
      blind(o, parent, 'passed as a JSX prop');
      return;
    }
    blind(o, parent, `unrecognised context: ${ts.SyntaxKind[parent.kind]}`);
  }

  /** Whether the value at `c2` flows further (so a bare-root record would be premature). */
  function isFlowingPosition(p2: ts.Node, c2: ts.Node): boolean {
    return (
      (ts.isVariableDeclaration(p2) && p2.initializer === c2) ||
      (ts.isBinaryExpression(p2) && p2.operatorToken.kind === ts.SyntaxKind.EqualsToken && p2.right === c2) ||
      ts.isReturnStatement(p2) ||
      (ts.isCallExpression(p2) && p2.arguments.includes(c2 as ts.Expression)) ||
      (ts.isBinaryExpression(p2) &&
        [ts.SyntaxKind.QuestionQuestionToken, ts.SyntaxKind.BarBarToken, ts.SyntaxKind.CommaToken].includes(
          p2.operatorToken.kind
        )) ||
      (ts.isConditionalExpression(p2) && p2.condition !== c2) ||
      (ts.isBinaryExpression(p2) &&
        p2.operatorToken.kind === ts.SyntaxKind.AmpersandAmpersandToken &&
        p2.right === c2) ||
      ((ts.isArrowFunction(p2) || ts.isFunctionExpression(p2)) && p2.body === c2)
    );
  }

  /** `new X(…)` → X's constructor parameter `idx` (resolved by symbol). */
  function resolveConstructorParam(expr: ts.Expression, idx: number): ts.ParameterDeclaration | undefined {
    const e = skipT(expr);
    const sym = ts.isIdentifier(e) ? symbolOf(e) : undefined;
    const decl = sym?.declarations?.find(ts.isClassDeclaration);
    const ctor = decl?.members.find(ts.isConstructorDeclaration);
    return ctor?.parameters[idx];
  }
  /** Follow a constructor parameter: as an alias inside the constructor, and via the field it sets. */
  function followConstructorParam(p: ts.ParameterDeclaration, o: Origin) {
    if (!ts.isIdentifier(p.name)) {
      bindPattern(p.name, o);
      return;
    }
    const isParamProperty =
      ts.getCombinedModifierFlags(p) &
      (ts.ModifierFlags.Private | ts.ModifierFlags.Public | ts.ModifierFlags.Protected | ts.ModifierFlags.Readonly);
    if (isParamProperty) {
      // The parameter-property's field symbol: references are `this.x` / `inst.x`.
      const syms = checker.getSymbolsOfParameterPropertyDeclaration(p, p.name.text);
      for (const s of syms) if (s.flags & ts.SymbolFlags.Property) propagateMember(s, p.name, o);
    }
    propagateAlias(checker.getSymbolAtLocation(p.name), p.name, o);
  }

  /** Every `const [state, setState] = useState(…)` setter → its state binding (pre-pass). */
  const setters = new Map<ts.Symbol, { state: ts.ArrayBindingElement | undefined }>();
  for (const sf of sources) {
    const visit = (n: ts.Node) => {
      if (
        ts.isVariableDeclaration(n) &&
        ts.isArrayBindingPattern(n.name) &&
        n.initializer &&
        ts.isCallExpression(skipT(n.initializer)) &&
        calleeName(skipT(n.initializer) as ts.CallExpression) === 'useState'
      ) {
        const [first, second] = n.name.elements;
        if (second && ts.isBindingElement(second) && ts.isIdentifier(second.name)) {
          const sym = checker.getSymbolAtLocation(second.name);
          if (sym) setters.set(sym, { state: first });
        }
      }
      ts.forEachChild(n, visit);
    };
    visit(sf);
  }

  /** A fixed-capability value returned from `fn`: each return is followed at every call site of `fn`. */
  const followedReturns = new Set<string>();
  function followReturn(fn: ts.FunctionLikeDeclaration, o: Origin) {
    const chainKey = o.chain.map(l => `${l.call.pos}`).join(',');
    const k = `${fn.getSourceFile().fileName}:${fn.pos}:${o.site.file}:${o.site.line}:${o.cov.text}:${o.kind}:${o.root ?? ''}:${chainKey}`;
    if (followedReturns.has(k)) return;
    followedReturns.add(k);
    const { calls, escapes, anonymous } = usesOfFunction(fn);
    if (anonymous) {
      blind(o, fn, 'returned from an anonymous function (its calls cannot be found)');
      return;
    }
    for (const e of escapes) blind(o, e, `wrapper ${fnName(fn)} referenced other than by a call`);
    const base = baseMemberOf(fn);
    if (base) blind(o, fn, `wrapper ${fnName(fn)} may also be called through ${base} (not resolved)`);
    if (!calls.length)
      blind(
        o,
        fn,
        `returning wrapper ${fnName(fn)} has no resolved call (possibly called through an interface, base type or override)`
      );
    for (const c of calls) propagate(c, fork(o));
  }

  /**
   * A class member that also exists on a type its class extends or implements: calls
   * through that base / interface type resolve to the BASE member, not to this one.
   */
  function baseMemberOf(fn: ts.FunctionLikeDeclaration): string | undefined {
    if (!(ts.isMethodDeclaration(fn) || ts.isGetAccessorDeclaration(fn)) || !fn.name) return undefined;
    const cls = fn.parent;
    if (!ts.isClassLike(cls) || !cls.heritageClauses) return undefined;
    const name = fn.name.getText();
    for (const clause of cls.heritageClauses)
      for (const t of clause.types) {
        const type = checker.getTypeFromTypeNode(t);
        if (type.getProperty(name)) return t.getText();
      }
    return undefined;
  }

  function newSite(at: ts.Node, cov: Coverage, requested: string, push = true): XrmSite {
    const site: XrmSite = {
      file: disp(at.getSourceFile().fileName),
      line: lineOf(at),
      requested,
      coverage: cov.forwardParams.size
        ? ['(forwarded — resolved per call site)']
        : cov.unknown
          ? ['UNKNOWN']
          : [...cov.entries, ...[...cov.helpers].map(h => `helper ${fnName(h as ts.FunctionLikeDeclaration)}`)].sort(),
      uses: [],
      notes: [...cov.notes],
    };
    if (push) sites.push(site);
    return site;
  }

  /** Start an origin at the getXrm `call` whose capability is `expr`, evaluated in `env` (forwarding `chain`). */
  function seed(
    call: ts.CallExpression,
    expr: ts.Expression | undefined,
    env: Env,
    chain: ChainLink[],
    requested: string,
    depth: number,
    topDef?: XrmSite
  ) {
    const cov = resolveCoverage(expr, env);
    if (cov.forwardParams.size) {
      const fns = new Set([...cov.forwardParams].map(p => p.parent as ts.FunctionLikeDeclaration));
      // The definition is one inventory row; deeper forwarding levels are not.
      const def = newSite(chain.length ? chain[chain.length - 1].call : call, cov, requested, depth === 0);
      if (fns.size !== 1 || depth > 6) {
        blind(
          { site: def },
          call,
          fns.size !== 1 ? 'capability forwarded from several functions' : 'forwarding too deep'
        );
        return;
      }
      const [fn] = fns;
      def.notes.push(`forward: ${fnName(fn)} — checked once per call site`);
      const { calls, escapes, anonymous } = usesOfFunction(fn);
      if (anonymous) {
        blind({ site: def }, fn, 'capability forwarded from an anonymous function (its calls cannot be found)');
        return;
      }
      for (const e of escapes)
        blind({ site: def }, e, `forwarding function ${fnName(fn)} referenced other than by a call`);
      if (!calls.length)
        blind(
          { site: def },
          fn,
          `forwarding function ${fnName(fn)} has no call site (possibly called through an interface, base type or override)`
        );
      const base = baseMemberOf(fn);
      if (base)
        blind({ site: def }, fn, `forwarding function ${fnName(fn)} may also be called through ${base} (not resolved)`);
      for (const c of calls) {
        const env2: Env = new Map(env);
        const args = argsOf(c);
        fn.parameters.forEach((prm, i) => {
          const s2 = ts.isIdentifier(prm.name) ? checker.getSymbolAtLocation(prm.name) : undefined;
          if (s2) env2.set(s2, { expr: args[i] ?? prm.initializer });
        });
        seed(
          call,
          expr,
          env2,
          [...chain, { fn, call: c }],
          `${fnName(fn)}(${args.map(a => a.getText()).join(', ')})`,
          depth + 1,
          topDef ?? def
        );
      }
      return;
    }
    const site = newSite(chain.length ? chain[chain.length - 1].call : call, cov, requested);
    if (cov.unknown) blind({ site }, call, cov.unknownReason ?? 'capability not statically resolvable');
    propagate(call, { id: nextId++, kind: 'xrm', cov, site, chain });
    // A forwarding definition's row lists what its call sites reached (they are separate rows).
    if (topDef) for (const u of site.uses) if (!topDef.uses.includes(u)) topDef.uses.push(u);
  }

  // ---- seed: every reference to getXrm (by resolved symbol)
  const seededCalls = new Set<ts.CallExpression>();
  const getXrmRefs = new Set<ts.Identifier>(getXrmNamed);
  for (const [sym, ids] of refIndex) if (isGetXrmSymbol(sym)) ids.forEach(id => getXrmRefs.add(id));
  function pseudoBlind(at: ts.Node, reason: string) {
    const pseudo: XrmSite = {
      file: disp(at.getSourceFile().fileName),
      line: lineOf(at),
      requested: '(reference)',
      coverage: ['UNKNOWN'],
      uses: [],
      notes: [],
    };
    sites.push(pseudo);
    blind({ site: pseudo }, at, reason);
  }
  /** `import('…')` (through `await` / parentheses), or an identifier / parameter holding one's module value. */
  /** `import('…')` or `require('…')`. */
  function isModuleLoadCall(n: ts.Node): n is ts.CallExpression {
    return (
      ts.isCallExpression(n) &&
      (n.expression.kind === ts.SyntaxKind.ImportKeyword ||
        (ts.isIdentifier(n.expression) && n.expression.text === 'require' && n.arguments.length === 1))
    );
  }
  function isDynamicImportValue(e: ts.Expression): boolean {
    const x = skipT(e);
    if (isModuleLoadCall(x)) return true;
    if (!ts.isIdentifier(x)) return false;
    const d = checker.getSymbolAtLocation(x)?.declarations?.[0];
    if (d && ts.isVariableDeclaration(d) && d.initializer) return isDynamicImportValue(d.initializer);
    if (d && ts.isParameter(d)) {
      // `import(…).then(m => …)`: the callback's parameter is the module.
      const cb = d.parent;
      const call = cb.parent;
      return (
        ts.isCallExpression(call) &&
        call.arguments[0] === cb &&
        ts.isPropertyAccessExpression(call.expression) &&
        call.expression.name.text === 'then' &&
        isDynamicImportValue(call.expression.expression)
      );
    }
    return false;
  }
  /** Why an UNRECOGNISED identifier named `getXrm` is a blind spot (undefined: an unrelated `getXrm`, e.g. a method). */
  function unrecognisedGetXrm(id: ts.Identifier): string | undefined {
    const p = id.parent;
    if (ts.isBindingElement(p)) return 'getXrm destructured from an object (not followed)';
    if (ts.isPropertyAccessExpression(p) && p.name === id && isDynamicImportValue(p.expression))
      return 'getXrm read from a dynamic import (not followed)';
    return undefined;
  }

  // Dynamic `import()` of a module that exports getXrm: every way its value is used must
  // provably avoid getXrm (`m.Other`, `{ Other }`), else it is a blind spot.
  for (const sf of sources) {
    const visit = (n: ts.Node) => {
      if (isModuleLoadCall(n)) checkDynamicImport(n);
      ts.forEachChild(n, visit);
    };
    visit(sf);
  }
  function checkDynamicImport(call: ts.CallExpression) {
    const kind = call.expression.kind === ts.SyntaxKind.ImportKeyword ? 'dynamic import' : 'require';
    const arg = call.arguments[0];
    if (!arg || !ts.isStringLiteralLike(arg)) {
      pseudoBlind(call, `${kind} with a non-literal specifier`);
      return;
    }
    const spec = arg.text;
    const target = resolveModule(spec, call.getSourceFile().fileName);
    // The export NAMES under which the module exposes getXrm, matched by dealiased
    // symbol (a renamed re-export `export { getXrm as lookupXrm }` counts).
    let getXrmNames: Set<string>;
    if (target) {
      const modSym = checker.getSymbolAtLocation(program.getSourceFile(target)!);
      getXrmNames = new Set(
        (modSym ? checker.getExportsOfModule(modSym) : []).filter(e => isGetXrmSymbol(dealias(e))).map(e => e.name)
      );
      if (!getXrmNames.size) return;
    } else if (/ui-components|xrmContext/.test(spec)) getXrmNames = new Set(['getXrm']);
    else return;
    const unsafe = (why: string) => pseudoBlind(call, `${kind} of '${spec}' (exports getXrm): ${why}`);
    const bindingSafe = (name: ts.BindingName): boolean => {
      if (ts.isIdentifier(name)) {
        const sym = checker.getSymbolAtLocation(name);
        return referencesOf(sym, name).every(ref => {
          const p = ref.parent;
          return ts.isPropertyAccessExpression(p) && p.expression === ref && !getXrmNames.has(p.name.text);
        });
      }
      if (ts.isObjectBindingPattern(name))
        return name.elements.every(el => {
          if (el.dotDotDotToken) return false;
          const prop = (el.propertyName ?? el.name) as ts.Node;
          return ts.isIdentifier(prop) && !getXrmNames.has(prop.text);
        });
      return false;
    };
    let cur: ts.Node = call;
    let p: ts.Node = call.parent;
    while (TRANSPARENT.has(p.kind)) {
      cur = p;
      p = p.parent;
    }
    if (ts.isPropertyAccessExpression(p) && p.expression === cur) {
      if (p.name.text === 'then' && ts.isCallExpression(p.parent) && p.parent.expression === p) {
        const cb = p.parent.arguments[0];
        if (cb && (ts.isArrowFunction(cb) || ts.isFunctionExpression(cb)) && cb.parameters[0]) {
          if (!bindingSafe(cb.parameters[0].name)) unsafe('the module value reaches getXrm or is not followed');
          return;
        }
        if (cb) unsafe('then() callback not followed');
        return;
      }
      if (getXrmNames.has(p.name.text)) unsafe(`${p.name.text} (getXrm) read from it`);
      return;
    }
    if (ts.isVariableDeclaration(p) && p.initializer === cur) {
      if (!bindingSafe(p.name)) unsafe('the module value reaches getXrm or is not followed');
      return;
    }
    unsafe('the module value is not followed');
  }

  for (const id of getXrmRefs) {
    const sym = symbolOf(id);
    const p = id.parent;
    const isNs = ts.isPropertyAccessExpression(p) && p.name === id && isNsGetXrm(p);
    if (!isGetXrmSymbol(sym) && !isNs) {
      const why = unrecognisedGetXrm(id);
      if (why) pseudoBlind(id, why);
      continue;
    }
    const c = classifyRef(id);
    if (c.call) {
      if (seededCalls.has(c.call)) continue;
      seededCalls.add(c.call);
      const arg = c.call.arguments[0];
      seed(c.call, arg, EMPTY_ENV, [], arg ? arg.getText() : '(default)', 0);
      continue;
    }
    if (c.skip) continue;
    if (ts.isVariableDeclaration(p) && p.initializer === id) continue; // `const g = getXrm` (followed by symbol)
    if (ts.isPropertyAccessExpression(p) && p.name === id && ts.isVariableDeclaration(p.parent)) continue; // `const g = ui.getXrm`
    pseudoBlind(id, 'getXrm referenced other than by a call');
  }

  for (const s of sites) s.uses.sort();
  return { sites, violations, blindSpots };
}

/**
 * Collect EVERY non-test .ts/.tsx under `srcRoot` (not only files that mention
 * Xrm: a wrapper such as `getWebApi()` can be called from a file that never
 * says "xrm").
 */
export function collectSources(srcRoot: string): SourceInput[] {
  const skip = new Set([
    'node_modules',
    'dist',
    'out',
    'bin',
    'obj',
    '__tests__',
    '__mocks__',
    'test',
    'test-mocks',
    'stories',
  ]);
  const out: SourceInput[] = [];
  const walk = (dir: string) => {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      if (e.isDirectory()) {
        if (!skip.has(e.name) && !e.name.startsWith('.')) walk(path.join(dir, e.name));
      } else if (
        /\.(ts|tsx)$/.test(e.name) &&
        !/\.(test|spec|stories)\.tsx?$/.test(e.name) &&
        !e.name.endsWith('.d.ts')
      ) {
        const p = path.join(dir, e.name);
        out.push({ fileName: p, text: fs.readFileSync(p, 'utf8') });
      }
    }
  };
  walk(srcRoot);
  return out;
}
