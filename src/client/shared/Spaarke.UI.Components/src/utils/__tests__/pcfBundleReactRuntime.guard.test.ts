/**
 * Guard: a committed PCF Solution bundle must not carry React 19's JSX runtime when its manifest declares the platform
 * React library (spaarke-ontology-platform-r1 task 183, fix round 1).
 *
 * Why. A PCF whose manifest declares `<platform-library name="React" />` runs on the platform's React (16.14), and
 * pcf-scripts externalises `react` / `react-dom`. `react/jsx-runtime` is NOT externalised: webpack bundles whichever copy it
 * resolves. `@spaarke/ui-components` is a `file:` dependency with its own node_modules (React 19), so a PCF without a
 * `resolve.alias` for `react/jsx-runtime$` bundles React 19's runtime. Its elements carry
 * `Symbol.for('react.transitional.element')`; React 16 does not know that tag and throws "Objects are not valid as a React
 * child" the first time the shared EmailComposer (Lexical, `@griffel/react`) renders. Unit tests run on one React and the
 * build succeeds, so only this scan of the committed bundle sees it. (CommunicationActions carries the fix in its
 * webpack.config.js; ADR-012 records RichTextEditor as not PCF-safe for the same reason.)
 *
 * What it checks. Every `src/client/pcf/<control>/Solution/**\/bundle.js`: when the `ControlManifest.xml` beside it declares
 * the React platform library and the bundle contains the React 19 marker, the control is a violation. It reads committed
 * files only, so it runs in CI without a build (the `xrm-capability-guard` job of ci-tier1-blocking.yml).
 *
 * Fix a violation by copying the `resolve.alias` block from `src/client/pcf/CommunicationActions/webpack.config.js`, then
 * rebuilding with `npm run build:prod` and committing the rebuilt Solution bundle.
 *
 * Ratchet: BASELINE lists controls that violate today and are not yet fixed; a control that violates and is not listed
 * fails, and a listed control that no longer violates fails until it is removed from the list.
 *
 * Enforced by: this test.
 */
import * as fs from 'fs';
import * as path from 'path';

/** The tag React 19's jsx-runtime puts on every element it creates. React 16 and 17 tag `react.element`. */
export const REACT_19_ELEMENT_MARKER = 'react.transitional.element';

const MANIFEST_DECLARES_REACT = /<platform-library\s+name="React"/;

/** Controls (path under src/client/pcf, forward slashes) that violate today. Empty: none do after task 183. */
const BASELINE: readonly string[] = [];

export interface IPcfBundle {
  /** Display path, relative to src/client/pcf. */
  name: string;
  bundle: string;
  /** The ControlManifest.xml beside the bundle; empty when there is none. */
  manifest: string;
}

/** The bundles whose manifest declares platform React and that contain the React 19 marker. */
export function findReactRuntimeViolations(bundles: readonly IPcfBundle[]): string[] {
  return bundles
    .filter(b => MANIFEST_DECLARES_REACT.test(b.manifest) && b.bundle.includes(REACT_19_ELEMENT_MARKER))
    .map(b => b.name)
    .sort();
}

function collectBundles(pcfRoot: string): IPcfBundle[] {
  const found: IPcfBundle[] = [];
  const walk = (dir: string): void => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      if (entry.name === 'node_modules') continue;
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.name === 'bundle.js') {
        const manifestPath = path.join(dir, 'ControlManifest.xml');
        found.push({
          name: path.relative(pcfRoot, full).split(path.sep).join('/'),
          bundle: fs.readFileSync(full, 'utf-8'),
          manifest: fs.existsSync(manifestPath) ? fs.readFileSync(manifestPath, 'utf-8') : '',
        });
      }
    }
  };
  for (const control of fs.readdirSync(pcfRoot, { withFileTypes: true })) {
    const solution = path.join(pcfRoot, control.name, 'Solution');
    if (control.isDirectory() && fs.existsSync(solution)) walk(solution);
  }
  return found;
}

// src/client/shared/Spaarke.UI.Components/src/utils/__tests__ -> src/
const PCF_ROOT = path.resolve(__dirname, '../../../../../..', 'client/pcf');

describe('PCF bundles do not carry React 19 jsx-runtime — repository', () => {
  const bundles = collectBundles(PCF_ROOT);
  const violations = findReactRuntimeViolations(bundles);

  it('finds the committed PCF bundles (sanity: the scan is not empty)', () => {
    expect(bundles.length).toBeGreaterThan(10);
    expect(bundles.some(b => MANIFEST_DECLARES_REACT.test(b.manifest))).toBe(true);
  });

  it('no control outside the baseline bundles React 19 elements under the platform React library', () => {
    expect(violations.filter(v => !BASELINE.includes(v))).toEqual([]);
  });

  it('every baseline entry still violates (remove a control from the list once it is fixed)', () => {
    expect(BASELINE.filter(b => !violations.includes(b))).toEqual([]);
  });

  it('SemanticSearchControl, which mounts the shared EmailComposer, is clean', () => {
    const ssc = bundles.find(b => b.name.startsWith('SemanticSearchControl/'));
    expect(ssc).toBeDefined();
    expect(ssc!.bundle.includes(REACT_19_ELEMENT_MARKER)).toBe(false);
  });
});

describe('PCF bundle React-runtime guard — controls (it can fire, and it can stay quiet)', () => {
  const reactManifest =
    '<manifest><control><resources><platform-library name="React" version="16.14.0" /></resources></control></manifest>';
  const noReactManifest = '<manifest><control><resources><code path="index.ts" /></resources></control></manifest>';
  const seeded = `var a=Symbol.for("${REACT_19_ELEMENT_MARKER}");function jsx(){return{$$typeof:a}}`;
  const clean = 'var a=Symbol.for("react.element");function jsx(){return{$$typeof:a}}';

  it('fires on a seeded bundle under the platform React library', () => {
    expect(findReactRuntimeViolations([{ name: 'Seeded/bundle.js', bundle: seeded, manifest: reactManifest }])).toEqual(
      ['Seeded/bundle.js']
    );
  });

  it('stays quiet on a clean bundle', () => {
    expect(findReactRuntimeViolations([{ name: 'Clean/bundle.js', bundle: clean, manifest: reactManifest }])).toEqual(
      []
    );
  });

  it('stays quiet when the control bundles its own React (no platform-library declaration)', () => {
    expect(findReactRuntimeViolations([{ name: 'Own/bundle.js', bundle: seeded, manifest: noReactManifest }])).toEqual(
      []
    );
    expect(findReactRuntimeViolations([{ name: 'NoManifest/bundle.js', bundle: seeded, manifest: '' }])).toEqual([]);
  });

  it('reports only the violating control among several', () => {
    const result = findReactRuntimeViolations([
      { name: 'B/bundle.js', bundle: clean, manifest: reactManifest },
      { name: 'A/bundle.js', bundle: seeded, manifest: reactManifest },
    ]);
    expect(result).toEqual(['A/bundle.js']);
  });
});
