/**
 * xrmCapabilityAnalyzer — AST check that every `getXrm(...)` value is used only
 * for what its requested capability covers (task 081 round 5, review R4-2).
 *
 * Not a test file itself: `xrmCapabilityUsage.guard.test.ts` runs it over the
 * repository's `src/`, and over synthetic fixtures that prove each tracked
 * shape fires.
 *
 * ## Model
 * A value ORIGINATES at a call of `getXrm` (any import alias of it, or the
 * definition in `xrmContext.ts`) or at a call of a WRAPPER — any function or
 * method, in this file or another scanned file, that returns such a value
 * (`getXrmForPicker`, `getXrmWithWebApiAnd`, `resolveXrm`,
 * `this.getXrm()`, a local `getXrm = (cap) => shared(cap)` …). A wrapper that
 * forwards one of its parameters as the capability takes the capability from
 * each of its call sites.
 *
 * The value is followed through: parentheses, `!`, `as` / `<T>` / `satisfies`,
 * `await`, both sides of `??` / `||`, both branches of `?:`; `const` / `let` /
 * `var` aliases and plain `=` assignments (by symbol, anywhere in the file);
 * `this.x = …` fields (within the class); object destructuring (incl.
 * renames and nested patterns); `useState(…)` / `useState(() => …)` tuples
 * (element 0, and values passed to the element-1 setter); `useMemo(() => …)`;
 * returns (making the function a wrapper); and call ARGUMENTS into helper
 * functions resolved in the same file or, by import name, in another scanned
 * file (the parameter is followed like an alias). A root object taken off the
 * value (`const nav = xrm.Navigation`, `const { Utility } = xrm`) is followed the
 * same way.
 *
 * A USE is `Root.member` with Root ∈ WebApi / Navigation / Utility / App / Page
 * / userSettings, by dot or by string-literal bracket access; a bare root
 * (`if (!xrm?.Navigation)`) is `Root.?`. Not uses: React dependency arrays
 * (`[xrm]`), the receiver of `.bind` / `.call` / `.apply`. A value passed to
 * `new X(…)` is followed into X's constructor and its `this.field` uses.
 *
 * `WebApi` is treated as ATOMIC: a frame has the whole object or none, so any
 * WebApi check (the default, 'webApi', or a predicate on one WebApi method)
 * covers `WebApi.*`, and a WebApi object handed anywhere is a `WebApi.*` use.
 *
 * COVERAGE of the requested capability:
 *   webApi → WebApi.* · navigation → Navigation.navigateTo · openForm →
 *   Navigation.openForm · openUrl → Navigation.openUrl · utility, clientUrl →
 *   Utility.getGlobalContext · lookupObjects → Utility.lookupObjects ·
 *   metadata → Utility.getEntityMetadata · pageContext → Utility.getPageContext
 *   · sidePanes → App.sidePanes · page → Page.* · a predicate covers exactly
 *   the `param.Root.member` chains it reads (also inside a helper it calls with
 *   the parameter); `!!x.Root` proves presence only (`Root.?`). A list
 *   covers the union. A `?:` capability or a `??`/`||` fallback between two
 *   lookups is treated as the UNION of the alternatives (the code then checks
 *   which member exists before calling it) — recorded per site.
 *
 * ## Blind spots (reported, never silently passed)
 * The value escaping into anything not followed above: an object / array
 * literal, a JSX prop, a `new` expression whose class is not found, a call
 * whose callee cannot be resolved to a function declaration in a scanned file
 * (e.g. a method on some other object, a function from a non-scanned package),
 * a non-literal bracket access, a capability that is not a literal / list /
 * predicate / forwarded parameter. Each is listed in the result's `blindSpots`.
 *
 * ## Not detected (assumptions, not reported)
 * - Cross-file resolution is by imported NAME inside the imported module or
 *   package directory (no type checker across files); if one package exports
 *   several functions with that name, the first exported one is used.
 * - `?:` / `??` / `||` alternatives and predicates combined with `||` are
 *   credited with the UNION of their members; that the code calls only the
 *   member its branch guaranteed is not checked.
 * - `WebApi` is assumed atomic (above).
 * - Members outside the roots above (`Xrm.Device`, `Xrm.Panel`, …), `this.x`
 *   read outside its class, and module variables read from another file are
 *   not followed (no `export const x = getXrm(…)` exists in `src/`).
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

type Coverage = { entries: Set<string>; unknown: boolean; text: string; notes: string[]; forwarded?: boolean };

/** Parameter bindings while evaluating a forwarded capability expression. */
type Env = Map<ts.Symbol, { expr: ts.Expression; env: Env }>;
const EMPTY_ENV: Env = new Map();

/** A capability expression still referring to the enclosing function's parameters. */
interface ForwardTemplate {
  expr: ts.Expression | undefined;
  env: Env;
}

interface Origin {
  id: number;
  kind: 'xrm' | 'root';
  root?: string;
  cov: Coverage;
  site: XrmSite;
  /** Set when the capability is a parameter of the enclosing function (resolved per call site). */
  forward?: ForwardTemplate;
}

interface WrapperInfo {
  kind: 'xrm' | 'root';
  root?: string;
  /** Fixed: coverage resolved at the definition. */
  fixed?: { cov: Coverage; site: XrmSite };
  /** Forwarding: the capability expression, evaluated at each call site with its arguments bound. */
  forward?: ForwardTemplate;
}

const TRANSPARENT = new Set([
  ts.SyntaxKind.ParenthesizedExpression,
  ts.SyntaxKind.AsExpression,
  ts.SyntaxKind.NonNullExpression,
  ts.SyntaxKind.TypeAssertionExpression,
  ts.SyntaxKind.SatisfiesExpression,
  ts.SyntaxKind.AwaitExpression,
]);

function lineOf(node: ts.Node): number {
  const sf = node.getSourceFile();
  return sf.getLineAndCharacterOfPosition(node.getStart(sf)).line + 1;
}

function rel(fileName: string, root: string): string {
  return path.relative(root, fileName).replace(/\\/g, '/');
}

function calleeName(call: ts.CallExpression): string | undefined {
  const e = call.expression;
  if (ts.isIdentifier(e)) return e.text;
  if (ts.isPropertyAccessExpression(e)) return e.name.text;
  return undefined;
}

export function analyze(inputs: SourceInput[], displayRoot = ''): AnalysisResult {
  const texts = new Map(inputs.map(i => [path.resolve(i.fileName).replace(/\\/g, '/'), i.text]));
  const fileNames = [...texts.keys()];
  const options: ts.CompilerOptions = {
    noResolve: true,
    noLib: true,
    types: [],
    jsx: ts.JsxEmit.Preserve,
    target: ts.ScriptTarget.ES2020,
    allowJs: false,
  };
  const host = ts.createCompilerHost(options);
  host.getSourceFile = (fileName, lang) => {
    const key = path.resolve(fileName).replace(/\\/g, '/');
    const text = texts.get(key);
    return text === undefined
      ? undefined
      : ts.createSourceFile(key, text, lang, true, key.endsWith('x') ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
  };
  host.fileExists = f => texts.has(path.resolve(f).replace(/\\/g, '/'));
  host.readFile = f => texts.get(path.resolve(f).replace(/\\/g, '/'));
  const program = ts.createProgram(fileNames, options, host);
  const checker = program.getTypeChecker();
  const sources = new Map(program.getSourceFiles().map(sf => [sf.fileName, sf]));
  const disp = (f: string) => (displayRoot ? rel(f, displayRoot) : path.basename(f));

  const sites: XrmSite[] = [];
  const violations: XrmViolation[] = [];
  const blindSpots: XrmBlindSpot[] = [];
  let nextId = 1;

  // ---- identifier → symbol index per file (for alias reference search)
  const idIndex = new Map<string, Array<[ts.Identifier, ts.Symbol | undefined]>>();
  function identifiersOf(sf: ts.SourceFile): Array<[ts.Identifier, ts.Symbol | undefined]> {
    let list = idIndex.get(sf.fileName);
    if (!list) {
      list = [];
      const visit = (n: ts.Node) => {
        if (ts.isIdentifier(n)) {
          // `{ webApi }`: the shorthand's name resolves to the PROPERTY symbol; the
          // variable it reads is the shorthand assignment's value symbol.
          const sym =
            ts.isShorthandPropertyAssignment(n.parent) && n.parent.name === n
              ? checker.getShorthandAssignmentValueSymbol(n.parent)
              : checker.getSymbolAtLocation(n);
          list!.push([n, sym]);
        }
        ts.forEachChild(n, visit);
      };
      visit(sf);
      idIndex.set(sf.fileName, list);
    }
    return list;
  }
  function referencesOf(sym: ts.Symbol, sf: ts.SourceFile, exclude?: ts.Node): ts.Identifier[] {
    return identifiersOf(sf)
      .filter(([id, s]) => s === sym && id !== exclude)
      .map(([id]) => id);
  }

  // ---- exported functions by name per package/file (for cross-file helper + wrapper resolution)
  const pkgRoots = new Map<string, string>(); // package name → src dir
  for (const f of fileNames) {
    const m = /^(.*?\/(?:client\/shared|client\/pcf|client\/code-pages|solutions)\/[^/]+)\//.exec(f);
    if (m && !pkgRoots.has(m[1])) {
      const pj = path.join(m[1], 'package.json');
      try {
        const name = JSON.parse(fs.readFileSync(pj, 'utf8')).name as string;
        if (name) pkgRoots.set(name, m[1]);
      } catch {
        /* fixtures / no package.json */
      }
    }
  }
  const fnDecls = new Map<string, Array<ts.FunctionLikeDeclaration>>(); // name → decls (any file)
  const classDecls = new Map<string, ts.ClassDeclaration[]>();
  for (const sf of sources.values()) {
    const visit = (n: ts.Node) => {
      if (ts.isClassDeclaration(n) && n.name) classDecls.set(n.name.text, [...(classDecls.get(n.name.text) ?? []), n]);
      if (ts.isFunctionDeclaration(n) && n.name) push(n.name.text, n);
      else if (
        ts.isVariableDeclaration(n) &&
        ts.isIdentifier(n.name) &&
        n.initializer &&
        (ts.isArrowFunction(n.initializer) || ts.isFunctionExpression(n.initializer))
      )
        push(n.name.text, n.initializer);
      ts.forEachChild(n, visit);
    };
    visit(sf);
  }
  function push(name: string, d: ts.FunctionLikeDeclaration) {
    const l = fnDecls.get(name) ?? [];
    l.push(d);
    fnDecls.set(name, l);
  }

  /** Resolve an identifier callee to a function declaration (same file by symbol; other file by import). */
  function resolveFunction(callee: ts.Expression): ts.FunctionLikeDeclaration | undefined {
    if (ts.isPropertyAccessExpression(callee) && callee.expression.kind === ts.SyntaxKind.ThisKeyword) {
      const cls = findAncestor(callee, ts.isClassLike);
      const m = cls?.members.find(
        mm =>
          (ts.isMethodDeclaration(mm) || ts.isPropertyDeclaration(mm)) &&
          mm.name &&
          ts.isIdentifier(mm.name) &&
          mm.name.text === callee.name.text
      );
      if (m && ts.isMethodDeclaration(m)) return m;
      if (
        m &&
        ts.isPropertyDeclaration(m) &&
        m.initializer &&
        (ts.isArrowFunction(m.initializer) || ts.isFunctionExpression(m.initializer))
      )
        return m.initializer;
      return undefined;
    }
    if (!ts.isIdentifier(callee)) return undefined;
    const sym = checker.getSymbolAtLocation(callee);
    const decl = sym?.declarations?.[0];
    if (!decl) return undefined;
    if (ts.isFunctionDeclaration(decl)) return decl;
    if (
      ts.isVariableDeclaration(decl) &&
      decl.initializer &&
      (ts.isArrowFunction(decl.initializer) || ts.isFunctionExpression(decl.initializer))
    )
      return decl.initializer;
    if (ts.isImportSpecifier(decl)) {
      const original = (decl.propertyName ?? decl.name).text;
      const spec = (decl.parent.parent.parent.moduleSpecifier as ts.StringLiteral).text;
      const candidates = fnDecls.get(original) ?? [];
      const from = decl.getSourceFile().fileName;
      const target = resolveModuleDir(spec, from);
      const hit = candidates.filter(c => !target || c.getSourceFile().fileName.startsWith(target));
      return hit.length === 1 ? hit[0] : hit.length > 1 ? hit.find(h => isExported(h)) : undefined;
    }
    return undefined;
  }
  function resolveModuleDir(spec: string, from: string): string | undefined {
    if (spec.startsWith('.')) {
      const base = path.resolve(path.dirname(from), spec).replace(/\\/g, '/');
      for (const ext of ['.ts', '.tsx', '/index.ts', '/index.tsx']) if (texts.has(base + ext)) return base + ext;
      return base;
    }
    for (const [name, dir] of pkgRoots) if (spec === name || spec.startsWith(name + '/')) return dir;
    return undefined;
  }
  function isExported(d: ts.Node): boolean {
    const n = ts.isFunctionDeclaration(d) ? d : d.parent?.parent?.parent;
    return (
      !!n &&
      !!ts.getCombinedModifierFlags(n as ts.Declaration) &&
      (ts.getCombinedModifierFlags(n as ts.Declaration) & ts.ModifierFlags.Export) !== 0
    );
  }
  function findAncestor<T extends ts.Node>(n: ts.Node, pred: (x: ts.Node) => x is T): T | undefined {
    let p: ts.Node | undefined = n.parent;
    while (p && !pred(p)) p = p.parent;
    return p as T | undefined;
  }

  // ---- getXrm identification
  function isGetXrmCallee(callee: ts.Expression): boolean {
    if (!ts.isIdentifier(callee)) return false;
    const sym = checker.getSymbolAtLocation(callee);
    const decl = sym?.declarations?.[0];
    if (!decl) return false;
    if (ts.isImportSpecifier(decl)) {
      const original = (decl.propertyName ?? decl.name).text;
      const spec = (decl.parent.parent.parent.moduleSpecifier as ts.StringLiteral).text;
      return original === 'getXrm' && /ui-components|xrmContext/.test(spec);
    }
    return (
      ts.isFunctionDeclaration(decl) &&
      decl.name?.text === 'getXrm' &&
      /xrmContext\.tsx?$/.test(decl.getSourceFile().fileName)
    );
  }

  // ---- capability resolution
  function predicateCoverage(fn: ts.FunctionLikeDeclaration, depth = 0): Set<string> {
    const out = new Set<string>();
    const p = fn.parameters[0];
    if (!p || !ts.isIdentifier(p.name) || !fn.body || depth > 3) return out;
    const pname = p.name.text;
    const visit = (n: ts.Node) => {
      // A predicate that delegates to a helper (`x => !!read(x)`): the helper's reads.
      if (
        ts.isCallExpression(n) &&
        n.arguments.some(a => ts.isIdentifier(skipT(a)) && (skipT(a) as ts.Identifier).text === pname)
      ) {
        const helper = resolveFunction(n.expression);
        if (helper) predicateCoverage(helper, depth + 1).forEach(x => out.add(x));
      }
      if (
        ts.isPropertyAccessExpression(n) &&
        ts.isIdentifier(n.expression) &&
        n.expression.text === pname &&
        n.name.text === 'WebApi'
      ) {
        out.add('WebApi.*'); // WebApi is atomic: a frame has the whole object or none
      } else if (
        ts.isPropertyAccessExpression(n) &&
        ts.isIdentifier(n.expression) &&
        n.expression.text === pname &&
        ROOTS.has(n.name.text)
      ) {
        let parent: ts.Node = n.parent;
        while (TRANSPARENT.has(parent.kind)) parent = parent.parent;
        if (
          (ts.isPropertyAccessExpression(parent) && parent.expression === n) ||
          (ts.isPropertyAccessExpression(parent) && skipT(parent.expression) === n)
        )
          out.add(`${n.name.text}.${(parent as ts.PropertyAccessExpression).name.text}`);
        else out.add(`${n.name.text}.?`); // `!!x.Root` proves presence, not any method
      }
      ts.forEachChild(n, visit);
    };
    visit(fn.body);
    return out;
  }
  function skipT(e: ts.Expression): ts.Expression {
    while (TRANSPARENT.has(e.kind)) e = (e as ts.ParenthesizedExpression).expression;
    return e;
  }
  function resolveCoverage(arg: ts.Expression | undefined, env: Env = EMPTY_ENV, depth = 0): Coverage {
    const cov: Coverage = { entries: new Set(), unknown: false, text: arg ? arg.getText() : '(default)', notes: [] };
    if (!arg) {
      CAPABILITY_COVERAGE.webApi.forEach(e => cov.entries.add(e));
      return cov;
    }
    if (depth > 8) {
      cov.unknown = true;
      return cov;
    }
    const e = skipT(arg);
    const merge = (sub: Coverage) => {
      sub.entries.forEach(x => cov.entries.add(x));
      if (sub.unknown) cov.unknown = true;
      if (sub.forwarded) cov.forwarded = true;
      sub.notes.forEach(n => cov.notes.includes(n) || cov.notes.push(n));
    };
    const addCap = (name: string) => {
      const c = CAPABILITY_COVERAGE[name];
      if (c) c.forEach(x => cov.entries.add(x));
      else cov.unknown = true;
    };
    if (ts.isStringLiteralLike(e)) addCap(e.text);
    else if (ts.isArrayLiteralExpression(e)) {
      for (const el of e.elements) merge(resolveCoverage(ts.isSpreadElement(el) ? el.expression : el, env, depth + 1));
    } else if (ts.isArrowFunction(e) || ts.isFunctionExpression(e)) {
      predicateCoverage(e).forEach(x => cov.entries.add(x));
      if (cov.entries.size === 0) cov.unknown = true;
    } else if (ts.isConditionalExpression(e)) {
      merge(resolveCoverage(e.whenTrue, env, depth + 1));
      merge(resolveCoverage(e.whenFalse, env, depth + 1));
      cov.notes.push('capability chosen by ?: — union of branches');
    } else if (ts.isIdentifier(e)) {
      const sym = checker.getSymbolAtLocation(e);
      const bound = sym && env.get(sym);
      const decl = sym?.declarations?.[0];
      if (bound)
        merge(resolveCoverage(bound.expr, env, depth + 1)); // param symbols are unique: one env suffices
      else if (decl && ts.isParameter(decl)) cov.forwarded = true;
      else if (decl && ts.isVariableDeclaration(decl) && decl.initializer)
        merge(resolveCoverage(decl.initializer, env, depth + 1));
      else cov.unknown = true;
    } else cov.unknown = true;
    return cov;
  }

  function covers(cov: Coverage, use: string): boolean {
    if (cov.unknown) return true;
    const [root, member] = use.split('.');
    for (const ent of cov.entries) {
      const [r, m] = ent.split('.');
      if (r !== root) continue;
      if (m === '*' || member === '?' || m === member) return true;
      if (m === '?' && member === '?') return true;
    }
    return false;
  }

  // ---- propagation
  const seen = new Set<string>();
  const wrappers = new Map<ts.Node, WrapperInfo>();
  const pendingWrapperCalls: Array<() => void> = [];

  function record(o: Origin, node: ts.Node, use: string) {
    const tag = `${use}`;
    const covered = covers(o.cov, use);
    const label = covered ? tag : `${tag} ✗`;
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
  function blind(o: Origin, node: ts.Node, reason: string) {
    blindSpots.push({
      file: disp(node.getSourceFile().fileName),
      line: lineOf(node),
      site: `${o.site.file}:${o.site.line}`,
      reason,
    });
    const n = `blind: ${reason}`;
    if (!o.site.notes.includes(n)) o.site.notes.push(n);
  }

  function propagateAlias(sym: ts.Symbol | undefined, declName: ts.Node, o: Origin) {
    if (!sym) return;
    for (const ref of referencesOf(sym, declName.getSourceFile(), declName as ts.Identifier)) propagate(ref, o);
  }

  function bindPattern(pattern: ts.BindingName, o: Origin) {
    if (ts.isIdentifier(pattern)) {
      propagateAlias(checker.getSymbolAtLocation(pattern), pattern, o);
      return;
    }
    if (ts.isObjectBindingPattern(pattern)) {
      for (const el of pattern.elements) {
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
          if (ROOTS.has(prop)) {
            const ro: Origin = { ...o, id: nextId++, kind: 'root', root: prop };
            bindPattern(el.name, ro);
          }
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

    let cur: ts.Node = node;
    let parent: ts.Node = cur.parent;
    // Ascend through value-preserving wrappers.
    for (;;) {
      if (TRANSPARENT.has(parent.kind)) {
        cur = parent;
        parent = parent.parent;
        continue;
      }
      if (
        ts.isBinaryExpression(parent) &&
        [ts.SyntaxKind.QuestionQuestionToken, ts.SyntaxKind.BarBarToken].includes(parent.operatorToken.kind)
      ) {
        // Fallback: the value may come from either side. When the other side is
        // another lookup, the code downstream is checked against the UNION of
        // both coverages (it tests which member exists before calling it).
        const other = skipT(parent.left === cur ? parent.right : parent.left);
        if (ts.isCallExpression(other) && isGetXrmCallee(other.expression)) {
          const oc = resolveCoverage(other.arguments[0]);
          const union: Coverage = {
            entries: new Set([...o.cov.entries, ...oc.entries]),
            unknown: o.cov.unknown || oc.unknown,
            text: o.cov.text,
            notes: o.cov.notes,
          };
          const note = `?? fallback to ${oc.text} — union of both`;
          if (!o.site.notes.includes(note)) o.site.notes.push(note);
          o = { ...o, id: nextId++, cov: union };
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
        if (!ROOTS.has(name)) return; // e.g. xrm.foo — not an Xrm API root
        const ro: Origin = { ...o, id: nextId++, kind: 'root', root: name };
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

    // Assignment.
    if (
      ts.isBinaryExpression(parent) &&
      parent.operatorToken.kind === ts.SyntaxKind.EqualsToken &&
      parent.right === cur
    ) {
      const left = parent.left;
      if (ts.isIdentifier(left)) {
        const sym = checker.getSymbolAtLocation(left);
        const decl = sym?.declarations?.[0];
        if (sym && decl) for (const ref of referencesOf(sym, decl.getSourceFile())) if (ref !== left) propagate(ref, o);
        return;
      }
      if (ts.isPropertyAccessExpression(left) && left.expression.kind === ts.SyntaxKind.ThisKeyword) {
        const cls = findAncestor(left, ts.isClassLike);
        const field = left.name.text;
        const visit = (n: ts.Node) => {
          if (
            ts.isPropertyAccessExpression(n) &&
            n.expression.kind === ts.SyntaxKind.ThisKeyword &&
            n.name.text === field &&
            n !== left
          )
            propagate(n, o);
          ts.forEachChild(n, visit);
        };
        if (cls) visit(cls);
        return;
      }
      blind(o, parent, 'assigned to a property of another object');
      return;
    }

    // Returned from a function → the function is a wrapper; follow its calls.
    if (
      ts.isReturnStatement(parent) ||
      ((ts.isArrowFunction(parent) || ts.isFunctionExpression(parent)) && parent.body === cur)
    ) {
      const fn = ts.isReturnStatement(parent) ? findAncestor(parent, ts.isFunctionLike) : (parent as ts.ArrowFunction);
      if (!fn) return;
      // useMemo(() => X) / useState(() => X) / useCallback(() => X)() — the call's value.
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
      registerWrapper(fn as ts.FunctionLikeDeclaration, o);
      return;
    }

    // React dependency array (`useEffect(fn, [xrm])`): not an escape.
    if (
      ts.isArrayLiteralExpression(parent) &&
      ts.isCallExpression(parent.parent) &&
      parent.parent.arguments[parent.parent.arguments.length - 1] === parent &&
      /^use(Effect|LayoutEffect|Memo|Callback|ImperativeHandle)$/.test(calleeName(parent.parent) ?? '')
    ) {
      return;
    }

    // Passed as an argument.
    if (ts.isCallExpression(parent) && parent.arguments.includes(cur as ts.Expression)) {
      // `fn.bind(xrm.Navigation)` / `.call(...)` / `.apply(...)`: the receiver, not an escape.
      if (
        ts.isPropertyAccessExpression(parent.expression) &&
        ['bind', 'call', 'apply'].includes(parent.expression.name.text) &&
        parent.arguments[0] === cur
      ) {
        return;
      }
      const idx = parent.arguments.indexOf(cur as ts.Expression);
      const callee = parent.expression;
      if (ts.isIdentifier(callee)) {
        const sym = checker.getSymbolAtLocation(callee);
        const setter = sym && setters.get(sym);
        if (setter) {
          if (setter.state && ts.isBindingElement(setter.state)) bindPattern(setter.state.name, o);
          return;
        }
      }
      const name = calleeName(parent);
      if (name === 'useState') {
        propagate(parent, o);
        return;
      }
      const fn = resolveFunction(callee);
      if (fn && fn.parameters[idx]) {
        const p = fn.parameters[idx];
        if (ts.isIdentifier(p.name)) propagateAlias(checker.getSymbolAtLocation(p.name), p.name, o);
        else bindPattern(p.name, o);
        return;
      }
      if (o.kind === 'root' && o.root === 'WebApi') {
        // A WebApi object handed to a data helper: any WebApi member (atomic).
        record(o, parent, 'WebApi.*');
        return;
      }
      blind(o, parent, `passed to unresolved call ${callee.getText().slice(0, 60)}`);
      return;
    }

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
      const idx = parent.arguments.indexOf(cur as ts.Expression);
      const ctorParam = resolveConstructorParam(parent.expression, idx);
      if (ctorParam) {
        followConstructorParam(ctorParam, o);
        return;
      }
      blind(o, parent, `passed to new ${parent.expression.getText().slice(0, 40)}`);
      return;
    }
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
    // Conditions, comparisons, typeof, logging, etc.: not an API use.
  }

  /** Whether the value at `c2` flows further (so a bare-root record would be premature). */
  function isFlowingPosition(p2: ts.Node, c2: ts.Node): boolean {
    return (
      (ts.isVariableDeclaration(p2) && p2.initializer === c2) ||
      (ts.isBinaryExpression(p2) && p2.operatorToken.kind === ts.SyntaxKind.EqualsToken && p2.right === c2) ||
      ts.isReturnStatement(p2) ||
      (ts.isCallExpression(p2) && p2.arguments.includes(c2 as ts.Expression)) ||
      (ts.isBinaryExpression(p2) &&
        [ts.SyntaxKind.QuestionQuestionToken, ts.SyntaxKind.BarBarToken].includes(p2.operatorToken.kind)) ||
      ((ts.isArrowFunction(p2) || ts.isFunctionExpression(p2)) && p2.body === c2)
    );
  }

  /** `new X(…)` → X's constructor parameter `idx` (class in this file, or by import name in a scanned file). */
  function resolveConstructorParam(expr: ts.Expression, idx: number): ts.ParameterDeclaration | undefined {
    if (!ts.isIdentifier(expr)) return undefined;
    const sym = checker.getSymbolAtLocation(expr);
    let decl = sym?.declarations?.[0];
    if (decl && ts.isImportSpecifier(decl)) {
      const original = (decl.propertyName ?? decl.name).text;
      const spec = (decl.parent.parent.parent.moduleSpecifier as ts.StringLiteral).text;
      const dir = resolveModuleDir(spec, decl.getSourceFile().fileName);
      decl = classDecls.get(original)?.find(c => !dir || c.getSourceFile().fileName.startsWith(dir));
    }
    if (!decl || !ts.isClassDeclaration(decl)) return undefined;
    const ctor = decl.members.find(ts.isConstructorDeclaration);
    return ctor?.parameters[idx];
  }
  /** Follow a constructor parameter: as an alias inside the constructor, and via `this.field` (parameter property or `this.f = p`). */
  function followConstructorParam(p: ts.ParameterDeclaration, o: Origin) {
    if (!ts.isIdentifier(p.name)) return;
    const cls = findAncestor(p, ts.isClassLike);
    const fields = new Set<string>();
    if (
      ts.getCombinedModifierFlags(p) &
      (ts.ModifierFlags.Private | ts.ModifierFlags.Public | ts.ModifierFlags.Protected | ts.ModifierFlags.Readonly)
    )
      fields.add(p.name.text);
    const sym = checker.getSymbolAtLocation(p.name);
    if (sym) {
      for (const ref of referencesOf(sym, p.getSourceFile(), p.name)) {
        let par: ts.Node = ref.parent;
        while (TRANSPARENT.has(par.kind)) par = par.parent;
        if (
          ts.isBinaryExpression(par) &&
          par.operatorToken.kind === ts.SyntaxKind.EqualsToken &&
          ts.isPropertyAccessExpression(par.left) &&
          par.left.expression.kind === ts.SyntaxKind.ThisKeyword
        )
          fields.add(par.left.name.text);
        else propagate(ref, o);
      }
    }
    const visit = (n: ts.Node) => {
      if (
        ts.isPropertyAccessExpression(n) &&
        n.expression.kind === ts.SyntaxKind.ThisKeyword &&
        fields.has(n.name.text)
      ) {
        const par = n.parent;
        if (!(ts.isBinaryExpression(par) && par.left === n)) propagate(n, o);
      }
      ts.forEachChild(n, visit);
    };
    if (cls) visit(cls);
  }

  /** Every `const [state, setState] = useState(…)` setter → its state binding (pre-pass). */
  const setters = new Map<ts.Symbol, { state: ts.ArrayBindingElement | undefined }>();
  for (const sf of sources.values()) {
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

  function registerWrapper(fn: ts.FunctionLikeDeclaration, o: Origin) {
    const info: WrapperInfo = { kind: o.kind, root: o.root };
    if (o.forward) info.forward = o.forward;
    else info.fixed = { cov: o.cov, site: o.site };
    const prev = wrappers.get(fn);
    if (prev) {
      // A wrapper with several returns: union fixed coverage.
      if (prev.fixed && info.fixed) info.fixed.cov.entries.forEach(e => prev.fixed!.cov.entries.add(e));
      return;
    }
    wrappers.set(fn, info);
    pendingWrapperCalls.push(() => followWrapperCalls(fn, info));
  }

  function wrapperName(fn: ts.FunctionLikeDeclaration): string {
    if (fn.name && ts.isIdentifier(fn.name)) return fn.name.text;
    if (ts.isVariableDeclaration(fn.parent) && ts.isIdentifier(fn.parent.name)) return fn.parent.name.text;
    return '(wrapper)';
  }

  function followWrapperCalls(fn: ts.FunctionLikeDeclaration, info: WrapperInfo) {
    for (const sf of sources.values()) {
      const visit = (n: ts.Node) => {
        if (ts.isCallExpression(n) && resolveFunction(n.expression) === fn) {
          if (info.fixed) {
            propagate(n, {
              id: nextId++,
              kind: info.kind,
              root: info.root,
              cov: info.fixed.cov,
              site: info.fixed.site,
            });
          } else if (info.forward) {
            // Bind the wrapper's parameters to this call's arguments (or defaults).
            const env: Env = new Map(info.forward.env);
            fn.parameters.forEach((prm, i) => {
              const s2 = ts.isIdentifier(prm.name) ? checker.getSymbolAtLocation(prm.name) : undefined;
              const a = n.arguments[i] ?? prm.initializer;
              if (s2 && a) env.set(s2, { expr: a, env: EMPTY_ENV });
            });
            seedOrigin(
              n,
              info.forward.expr,
              env,
              info.kind,
              info.root,
              `${wrapperName(fn)}(${n.arguments.map(a => a.getText()).join(', ')})`
            );
          }
        }
        ts.forEachChild(n, visit);
      };
      visit(sf);
    }
  }

  function newSite(call: ts.CallExpression, cov: Coverage, requested: string): XrmSite {
    const site: XrmSite = {
      file: disp(call.getSourceFile().fileName),
      line: lineOf(call),
      requested,
      coverage: cov.forwarded
        ? ['(forwarded — resolved per call site)']
        : cov.unknown
          ? ['UNKNOWN']
          : [...cov.entries].sort(),
      uses: [],
      notes: [...cov.notes],
    };
    sites.push(site);
    return site;
  }

  /** Start an origin at `call` whose capability is `expr` evaluated in `env`. */
  function seedOrigin(
    call: ts.CallExpression,
    expr: ts.Expression | undefined,
    env: Env,
    kind: 'xrm' | 'root',
    root: string | undefined,
    requested: string
  ) {
    const cov = resolveCoverage(expr, env);
    const site = newSite(call, cov, requested);
    if (cov.forwarded) {
      // Still a parameter of the enclosing function: unchecked here, resolved where that function is called.
      propagate(call, { id: nextId++, kind, root, cov: { ...cov, unknown: true }, site, forward: { expr, env } });
      return;
    }
    if (cov.unknown) blind({ id: 0, kind, cov, site }, call, 'capability not statically resolvable');
    propagate(call, { id: nextId++, kind, root, cov, site });
  }

  // ---- seed: every direct getXrm call
  for (const sf of sources.values()) {
    const visit = (n: ts.Node) => {
      if (ts.isCallExpression(n) && isGetXrmCallee(n.expression)) {
        const arg = n.arguments[0];
        seedOrigin(n, arg, EMPTY_ENV, 'xrm', undefined, arg ? arg.getText() : '(default)');
      }
      ts.forEachChild(n, visit);
    };
    visit(sf);
  }
  while (pendingWrapperCalls.length) pendingWrapperCalls.shift()!();

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
