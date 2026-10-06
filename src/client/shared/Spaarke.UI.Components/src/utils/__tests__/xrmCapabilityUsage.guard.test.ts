/**
 * Guard: a `getXrm()` call with the DEFAULT capability ('webApi') must only be
 * used for `WebApi` (task 081 round 4, review F1).
 *
 * The walker checks the requested capability PER FRAME. A site that calls the
 * default `getXrm()` and then uses `Navigation` / `Utility` / `App.sidePanes` /
 * `Page` gets the nearest frame that has WebApi — which may be a child frame
 * with a partial Xrm — instead of the nearest frame that has what it uses.
 * Such a site must pass that capability, e.g. `getXrm('navigation')`.
 *
 * Static scan of every non-test `.ts` / `.tsx` under the repository's `src/`:
 *   1. `const|let x = getXrm()` (any type annotation / cast), then `x.Navigation`,
 *      `x?.Utility`, `x.App`, `x.Page` within the next 30 lines of the same file;
 *   2. inline `getXrm()?.Navigation|Utility|App|Page`.
 */
import * as fs from 'fs';
import * as path from 'path';

const SRC_ROOT = path.resolve(__dirname, '../../../../../..');
const SKIP_DIRS = new Set([
  'node_modules',
  'dist',
  'out',
  'bin',
  'obj',
  '__tests__',
  '__mocks__',
  'test',
  'test-mocks',
]);

function* sourceFiles(dir: string): Generator<string> {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.isDirectory()) {
      if (!SKIP_DIRS.has(entry.name) && !entry.name.startsWith('.')) yield* sourceFiles(path.join(dir, entry.name));
    } else if (
      /\.(ts|tsx)$/.test(entry.name) &&
      !/\.(test|spec)\.tsx?$/.test(entry.name) &&
      !entry.name.endsWith('.d.ts')
    ) {
      yield path.join(dir, entry.name);
    }
  }
}

const NON_WEBAPI = '(?:Navigation|Utility|App|Page)\\b';

function offenders(): string[] {
  const found: string[] = [];
  for (const file of sourceFiles(SRC_ROOT)) {
    const text = fs.readFileSync(file, 'utf8');
    if (!text.includes('getXrm()')) continue;
    const lines = text.split(/\r?\n/);
    const rel = path.relative(SRC_ROOT, file).replace(/\\/g, '/');
    lines.forEach((line, i) => {
      if (/^\s*(\/\/|\*)/.test(line)) return;
      if (new RegExp(`getXrm\\(\\)\\s*\\??\\.${NON_WEBAPI}`).test(line)) {
        found.push(`${rel}:${i + 1}  ${line.trim()}`);
        return;
      }
      const decl = /\b(?:const|let)\s+(\w+)\s*(?::[^=]+)?=\s*\(?\s*getXrm\(\)/.exec(line);
      if (!decl) return;
      const use = new RegExp(`\\b${decl[1]}\\s*\\??\\.\\s*${NON_WEBAPI}`);
      for (let j = i; j < Math.min(i + 30, lines.length); j++) {
        if (j > i && /getXrm\(/.test(lines[j])) break;
        if (/^\s*(\/\/|\*)/.test(lines[j])) continue;
        if (use.test(lines[j])) {
          found.push(`${rel}:${i + 1}  ${line.trim()}  →  line ${j + 1}: ${lines[j].trim()}`);
          break;
        }
      }
    });
  }
  return found;
}

describe('getXrm() default-capability usage guard', () => {
  it('scans the repository src tree', () => {
    expect(fs.existsSync(path.join(SRC_ROOT, 'client/shared/Spaarke.UI.Components/src/utils/xrmContext.ts'))).toBe(
      true
    );
  });

  it('no default getXrm() call is used for Navigation / Utility / App / Page', () => {
    expect(offenders()).toEqual([]);
  }, 120_000);
});
