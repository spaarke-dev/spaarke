#!/usr/bin/env node
/**
 * Guards against the class of bug found 2026-07-07: PCFs that import
 * `@spaarke/ui-components/dist/*` deep subpaths silently bundle STALE
 * compiled JS (or fail to resolve modules entirely) whenever this package's
 * `dist/` predates its `src/`. There is no npm workspace here — the symlink
 * consumers get is never told to rebuild, so nothing forces `dist/` current.
 *
 * Consumers (PCF `package.json`s) wire this in as a `prebuild` / `prebuild:prod`
 * script — npm auto-runs any `pre<script>` before the matching `npm run
 * <script>`, for arbitrary script names, not just reserved lifecycle events
 * (verified empirically 2026-07-07). Fast no-op when `dist/` is already
 * fresh; only pays the `tsc` cost when something actually changed.
 *
 * Deliberately dependency-free (fs/path/child_process only) so it never
 * needs its own `npm install` step to run.
 */
'use strict';

const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

const LIB_ROOT = path.resolve(__dirname, '..');
const SRC_DIR = path.join(LIB_ROOT, 'src');
const DIST_DIR = path.join(LIB_ROOT, 'dist');

const IGNORED_DIR_NAMES = new Set(['node_modules', '__tests__', '__mocks__']);

/** Newest mtime (ms) of any file under `dir` matching one of `extensions`. 0 if dir/files absent. */
function newestMtimeMs(dir, extensions) {
  let newest = 0;
  if (!fs.existsSync(dir)) return newest;

  const stack = [dir];
  while (stack.length > 0) {
    const current = stack.pop();
    for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
      if (entry.name.startsWith('.')) continue;
      const full = path.join(current, entry.name);
      if (entry.isDirectory()) {
        if (IGNORED_DIR_NAMES.has(entry.name)) continue;
        stack.push(full);
      } else if (extensions.some(ext => entry.name.endsWith(ext))) {
        const mtime = fs.statSync(full).mtimeMs;
        if (mtime > newest) newest = mtime;
      }
    }
  }
  return newest;
}

const srcNewest = newestMtimeMs(SRC_DIR, ['.ts', '.tsx']);

if (srcNewest === 0) {
  // No source found (unexpected layout) — nothing to validate against.
  process.exit(0);
}

const distNewest = newestMtimeMs(DIST_DIR, ['.js', '.d.ts']);

if (distNewest === 0 || distNewest < srcNewest) {
  console.log(
    '[ensure-dist-fresh] @spaarke/ui-components dist/ is stale or missing relative to src/ — rebuilding (tsc)...'
  );
  try {
    execSync('npm run build', { cwd: LIB_ROOT, stdio: 'inherit' });
  } catch (err) {
    console.error(
      '[ensure-dist-fresh] @spaarke/ui-components build FAILED — downstream PCF build will likely fail too. ' +
        'Run `npm run build` in src/client/shared/Spaarke.UI.Components manually to see the full error.'
    );
    process.exit(1);
  }
}
// else: dist/ is already fresh — no-op, fast path.
