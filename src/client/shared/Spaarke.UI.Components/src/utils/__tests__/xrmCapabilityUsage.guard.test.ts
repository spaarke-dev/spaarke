/**
 * Guard: every `getXrm(...)` value is used only for what its requested
 * capability covers (task 081 round 5, review R4-2). AST-based: see
 * `xrmCapabilityAnalyzer.ts` for exactly which flows are followed, how
 * coverage is computed, and what is reported as a blind spot.
 *
 * 1. The repository's `src/` has no violation and no blind spot.
 * 2. Synthetic fixtures prove each tracked shape FIRES (and a clean fixture
 *    stays silent), so the guard cannot pass vacuously.
 *
 * `XRM_AUDIT_OUT=<file>` writes the full call-site inventory as JSON (the PR's
 * audit table is generated from it).
 */
import * as fs from 'fs';
import * as path from 'path';
import { analyze, collectSources, type SourceInput } from './xrmCapabilityAnalyzer';

const SRC_ROOT = path.resolve(__dirname, '../../../../../..');

describe('getXrm capability guard — repository', () => {
  const result = analyze(collectSources(SRC_ROOT), SRC_ROOT);

  if (process.env.XRM_AUDIT_OUT) fs.writeFileSync(process.env.XRM_AUDIT_OUT, JSON.stringify(result, null, 1));

  it('finds the getXrm call sites (sanity: the scan is not empty)', () => {
    expect(result.sites.length).toBeGreaterThan(150);
  });

  it('no Xrm member is used that the requested capability does not cover', () => {
    expect(
      result.violations.map(v => `${v.file}:${v.line} ${v.use} (from ${v.site} requesting ${v.requested})`)
    ).toEqual([]);
  });

  it('no blind spot (every value is followed to its uses)', () => {
    expect(result.blindSpots.map(b => `${b.file}:${b.line} ${b.reason} (from ${b.site})`)).toEqual([]);
  });
});

// ---------------------------------------------------------------------------
// Fixtures — each shape the review listed must fire.
// ---------------------------------------------------------------------------

const ROOT = '/fx/src';
const IMPORT = "import { getXrm } from '@spaarke/ui-components';\nimport * as React from 'react';\n";

// A stub `@spaarke/ui-components` package, so fixture imports resolve by symbol like the repo's.
const UI_DIR = '/fx/ui';
const UI_STUB: SourceInput[] = [
  { fileName: `${UI_DIR}/src/index.ts`, text: "export * from './utils/xrmContext';\n" },
  {
    fileName: `${UI_DIR}/src/utils/xrmContext.ts`,
    text: 'export function getXrm(required?: any): any {\n  return required;\n}\n',
  },
];

function run(files: Record<string, string>) {
  const inputs: SourceInput[] = Object.entries(files).map(([name, text]) => ({
    fileName: path.join(ROOT, name),
    text,
  }));
  return analyze([...UI_STUB, ...inputs], ROOT, { packages: { '@spaarke/ui-components': UI_DIR } });
}

const FIRING: Array<[string, string, string]> = [
  ['plain default use', 'getXrm()?.Navigation?.navigateTo({});', 'Navigation.navigateTo'],
  ['aliasing', 'const a = getXrm();\nconst b = a;\nb.Navigation.navigateTo({});', 'Navigation.navigateTo'],
  ['destructuring', 'const { Navigation } = getXrm()!;\nNavigation.navigateTo({});', 'Navigation.navigateTo'],
  ['renamed destructuring', 'const { Utility: u } = getXrm() as any;\nu.lookupObjects({});', 'Utility.lookupObjects'],
  [
    'useState tuple (lazy)',
    'const [xrm] = React.useState<any>(() => getXrm());\nxrm.Navigation.openForm({});',
    'Navigation.openForm',
  ],
  [
    'useState setter',
    'const [xrm, setXrm] = React.useState<any>(null);\nsetXrm(getXrm());\nxrm.Navigation.openForm({});',
    'Navigation.openForm',
  ],
  ['useMemo', 'const x = React.useMemo(() => getXrm(), []);\nx!.Navigation.navigateTo({});', 'Navigation.navigateTo'],
  [
    '?? getXrm() fallback',
    "const x: any = getXrm('navigation') ?? getXrm();\nx.Utility.getGlobalContext();",
    'Utility.getGlobalContext',
  ],
  [
    'helper passing (untyped)',
    'function open(x) { x.Navigation.navigateTo({}); }\nopen(getXrm());',
    'Navigation.navigateTo',
  ],
  [
    'helper passing (typed)',
    'function open(x: any): void { x.Utility.getEntityMetadata("a"); }\nopen(getXrm());',
    'Utility.getEntityMetadata',
  ],
  ['non-null !', 'getXrm()!.Navigation.navigateTo({});', 'Navigation.navigateTo'],
  ['as any', '(getXrm() as any).Utility.lookupObjects({});', 'Utility.lookupObjects'],
  ['split chain', 'const nav = getXrm()!.Navigation;\nnav.openForm({});', 'Navigation.openForm'],
  ['var', 'var v: any = getXrm();\nv.App.sidePanes.createPane({});', 'App.sidePanes'],
  ['assignment', 'let z: any;\nz = getXrm();\nz.Page.data;', 'Page.data'],
  ['bracket access', "(getXrm() as any)['Navigation']['navigateTo']({});", 'Navigation.navigateTo'],
  [
    '>30 lines away',
    'const far: any = getXrm();\n' + '\n'.repeat(45) + 'far.Navigation.navigateTo({});',
    'Navigation.navigateTo',
  ],
  ['wrong non-default capability', "getXrm('navigation')!.Navigation.openForm({});", 'Navigation.openForm'],
  [
    'predicate covering a different member',
    "getXrm((x: any) => typeof x.Navigation?.openForm === 'function')!.Navigation.navigateTo({});",
    'Navigation.navigateTo',
  ],
  ['presence-only predicate', 'getXrm((x: any) => !!x.Utility)!.Utility.lookupObjects({});', 'Utility.lookupObjects'],
  [
    'wrapper forwarding its parameter',
    "function pick(cap: any = 'lookupObjects') { return getXrm(cap); }\npick('navigation')!.Utility.lookupObjects({});",
    'Utility.lookupObjects',
  ],
  [
    'wrapper with a fixed capability',
    "function nav() { return getXrm('navigation'); }\nnav()!.Navigation.openForm({});",
    'Navigation.openForm',
  ],
  [
    'constructor parameter property',
    'class S { constructor(private x: any) {} run() { this.x.Navigation.navigateTo({}); } }\nnew S(getXrm());',
    'Navigation.navigateTo',
  ],
  [
    'this.field assignment',
    'class C { f: any; init() { this.f = getXrm(); } go() { this.f.Navigation.navigateTo({}); } }',
    'Navigation.navigateTo',
  ],
];

describe('getXrm capability guard — fixtures fire', () => {
  it.each(FIRING)('%s', (_name, body, use) => {
    const r = run({ 'a.ts': IMPORT + body });
    expect(r.violations.map(v => v.use)).toContain(use);
  });

  it('import alias', () => {
    const r = run({
      'a.ts': "import { getXrm as lookup } from '@spaarke/ui-components';\nlookup()!.Navigation.navigateTo({});",
    });
    expect(r.violations.map(v => v.use)).toContain('Navigation.navigateTo');
  });

  it('cross-file helper (by import)', () => {
    const r = run({
      'helper.ts': 'export function go(x: any) { x.Utility.getEntityMetadata("a"); }',
      'a.ts': IMPORT + "import { go } from './helper';\ngo(getXrm('utility'));",
    });
    expect(r.violations.map(v => `${v.file} ${v.use}`)).toContain('helper.ts Utility.getEntityMetadata');
  });

  it('cross-file forwarding wrapper chain', () => {
    const r = run({
      'picker.ts':
        IMPORT + "export function getXrmForPicker(required: any = 'lookupObjects') { return getXrm(required); }",
      'a.ts':
        "import { getXrmForPicker } from './picker';\nfunction tab(cap: any) { return getXrmForPicker(cap); }\ntab('metadata')!.Navigation.navigateTo({});",
    });
    expect(r.violations.map(v => v.use)).toContain('Navigation.navigateTo');
  });

  it('shorthand object property is a blind spot (Xrm) / a WebApi.* use (WebApi root)', () => {
    const r = run({
      'a.ts':
        IMPORT +
        "const xrm = getXrm('navigation');\nconst bag = { xrm };\nconst webApi = getXrm('navigation')?.WebApi;\nconst deps = { webApi };",
    });
    expect(r.blindSpots.map(b => b.reason)).toContain('placed in an object / array literal');
    expect(r.violations.map(v => v.use)).toContain('WebApi.*');
  });

  it('an unfollowable escape is reported as a blind spot, not passed silently', () => {
    const r = run({ 'a.ts': IMPORT + 'const bag = { xrm: getXrm() };\nconsole.log(bag);' });
    expect(r.blindSpots.length).toBeGreaterThan(0);
  });
});

// ---------------------------------------------------------------------------
// Round 6 (review R5-1): shapes that used to pass SILENTLY. Each must now be a
// violation or a reported blind spot.
// ---------------------------------------------------------------------------

const R6_VIOLATIONS: Array<[string, Record<string, string>, string]> = [
  [
    'forwarding function that uses the value in its own body',
    { 'a.ts': IMPORT + "function f(cap: any) {\n  getXrm(cap)!.Navigation.openForm({});\n}\nf('navigation');" },
    'Navigation.openForm',
  ],
  [
    'class property initialiser',
    {
      'a.ts':
        IMPORT +
        "class K {\n  private x: any = getXrm('navigation');\n  go() {\n    this.x.Navigation.openForm({});\n  }\n}",
    },
    'Navigation.openForm',
  ],
  [
    'default parameter value',
    { 'a.ts': IMPORT + "function d(x: any = getXrm('navigation')) {\n  x.Navigation.openForm({});\n}" },
    'Navigation.openForm',
  ],
  [
    'object-literal method wrapper (svc.get())',
    {
      'a.ts':
        IMPORT +
        "const svc = {\n  get() {\n    return getXrm('navigation');\n  },\n};\nsvc.get()!.Navigation.openForm({});",
    },
    'Navigation.openForm',
  ],
  [
    'class method called on an instance (new S().get())',
    {
      'a.ts':
        IMPORT +
        "class S {\n  get() {\n    return getXrm('navigation');\n  }\n}\nnew S().get()!.Navigation.openForm({});",
    },
    'Navigation.openForm',
  ],
  ['comma operator', { 'a.ts': IMPORT + "(0, getXrm('navigation'))!.Navigation.openForm({});" }, 'Navigation.openForm'],
  [
    'namespace import (import * as ui; ui.getXrm)',
    { 'a.ts': "import * as ui from '@spaarke/ui-components';\nui.getXrm('navigation')!.Navigation.openForm({});" },
    'Navigation.openForm',
  ],
  [
    'const g = getXrm',
    { 'a.ts': IMPORT + "const g = getXrm;\ng('navigation')!.Navigation.openForm({});" },
    'Navigation.openForm',
  ],
  [
    'renamed re-export (export { getXrm as lookupXrm })',
    {
      'barrel.ts': "export { getXrm as lookupXrm } from '@spaarke/ui-components';\n",
      'a.ts': "import { lookupXrm } from './barrel';\nlookupXrm('navigation')!.Navigation.openForm({});",
    },
    'Navigation.openForm',
  ],
  [
    'unscanned module: renamed named import',
    { 'a.ts': "import { getXrm as gx } from '@acme/ui-components';\ngx('navigation')!.Navigation.openForm({});" },
    'Navigation.openForm',
  ],
  [
    'unscanned module: namespace import',
    { 'a.ts': "import * as ui from '@acme/ui-components';\nui.getXrm('navigation')!.Navigation.openForm({});" },
    'Navigation.openForm',
  ],
  [
    'wrapper whose returns request different capabilities (no union across returns)',
    {
      'a.ts':
        IMPORT +
        "function w(b: boolean) {\n  if (b) return getXrm('openForm');\n  return getXrm('navigation');\n}\nw(true)!.Navigation.openForm({});",
    },
    'Navigation.openForm',
  ],
];

const R6_BLIND_SPOTS: Array<[string, string, RegExp]> = [
  [
    'negated predicate',
    'getXrm((x: any) => !x.Navigation?.openForm)!.Navigation.openForm({});',
    /predicate guarantees no member/,
  ],
  [
    '|| true predicate',
    "getXrm((x: any) => typeof x.Navigation?.navigateTo === 'function' || true)!.Navigation.openForm({});",
    /predicate guarantees no member/,
  ],
  [
    'let capability reassigned later',
    "let cap: any = 'openForm';\ncap = 'navigation';\ngetXrm(cap)!.Navigation.openForm({});",
    /reassigned/,
  ],
  ['getXrm passed as a value', '[1].map(getXrm);', /getXrm referenced other than by a call/],
  ['unrecognised context (template literal)', 'const s = `${getXrm()}`;', /unrecognised context/],
  ['member outside the checked roots', 'getXrm()!.Device.captureImage();', /outside the checked roots/],
  [
    'wrapper referenced other than by a call',
    "function w() {\n  return getXrm('navigation');\n}\nconst h = [w];",
    /wrapper w referenced other than by a call/,
  ],
];

describe('getXrm capability guard — round 6 shapes fire (R5-1)', () => {
  it.each(R6_VIOLATIONS)('%s', (_name, files, use) => {
    const r = run(files);
    expect(r.violations.map(v => v.use)).toContain(use);
  });

  it.each(R6_BLIND_SPOTS)('%s → blind spot', (_name, body, reason) => {
    const r = run({ 'a.ts': IMPORT + body });
    expect(r.blindSpots.map(b => b.reason).some(x => reason.test(x))).toBe(true);
  });

  it('an || predicate is credited with the union of its disjuncts and NOTED on the site (R5-5)', () => {
    const r = run({
      'a.ts':
        IMPORT +
        "const x = getXrm((p: any) => typeof p.Navigation?.openForm === 'function' || typeof p.Navigation?.navigateTo === 'function');\nif (x?.Navigation?.openForm) x.Navigation.openForm({});",
    });
    expect(r.violations).toEqual([]);
    expect(r.sites.flatMap(s => s.notes)).toContain('|| predicate — union of the disjuncts');
  });
});

describe('getXrm capability guard — clean fixture stays silent', () => {
  it('correct capabilities raise nothing', () => {
    const r = run({
      'helper.ts': 'export function meta(x: any) { return x.Utility.getEntityMetadata("a"); }',
      'a.ts':
        IMPORT +
        "import { meta } from './helper';\n" +
        "getXrm('navigation')!.Navigation.navigateTo({});\n" +
        "getXrm('openForm')!.Navigation.openForm({});\n" +
        "const both = getXrm(['webApi', 'metadata'])!;\nboth.WebApi.retrieveRecord('a', 'b');\nmeta(both);\n" +
        "getXrm((x: any) => typeof x.Utility?.lookupObjects === 'function')!.Utility.lookupObjects({});\n" +
        "const f = getXrm('openForm') ?? getXrm('navigation');\nif (f?.Navigation?.openForm) f.Navigation.openForm({}); else f?.Navigation?.navigateTo({});\n" +
        "function pick(cap: any = 'lookupObjects') { return getXrm(cap); }\npick()!.Utility.lookupObjects({});\n" +
        'const [s, setS] = React.useState<any>(() => getXrm());\nReact.useEffect(() => {}, [s]);\ns.WebApi.createRecord("a", {});\nsetS(getXrm());\n' +
        "const nav = getXrm('navigation')!.Navigation;\nnav.navigateTo.bind(nav);\n",
    });
    expect(r.violations).toEqual([]);
    expect(r.blindSpots).toEqual([]);
  });
});
