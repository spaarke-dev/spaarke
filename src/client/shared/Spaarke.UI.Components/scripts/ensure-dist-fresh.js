#!/usr/bin/env node
/**
 * Guards against the class of bug found 2026-07-07: PCFs that import
 * `@spaarke/ui-components/dist/*` deep subpaths silently bundle STALE
 * compiled JS (or fail to resolve modules entirely) whenever this package's
 * `dist/` predates its `src/`. There is no npm workspace here — the symlink
 * consumers get is never told to rebuild, so nothing forces `dist/` current.
 *
 * EXTENDED 2026-07-10 (visual-host-version-update VHVU-001): consumers that
 * bundle this package's `src/` directly (e.g. VisualHost) also compile the
 * bare-specifier imports inside those src files — `@spaarke/sdap-client` and
 * `@spaarke/auth` — resolving them against THIS package's node_modules. Those
 * sibling packages ship as `tsc` output too, so their `dist/` must be fresh or
 * the consumer build fails with TS2307 (missing type declarations). We now
 * freshen the sibling dists before our own, so the whole chain is deterministic
 * from a clean worktree (given each package's node_modules is installed).
 *
 * Consumers (PCF `package.json`s) wire this in as a `prebuild` / `prebuild:prod`
 * script — npm auto-runs any `pre<script>` before the matching `npm run
 * <script>`, for arbitrary script names, not just reserved lifecycle events
 * (verified empirically 2026-07-07). Fast no-op when everything is already
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
const SHARED_ROOT = path.resolve(LIB_ROOT, '..'); // src/client/shared

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

/**
 * Rebuild `pkgRoot`'s dist via `npm run build` if its dist is stale/missing
 * relative to its src. No-op fast path when fresh. Graceful skip (with a loud
 * warning) when the package has no node_modules — the caller's own install
 * step is expected to provide it; we never run `npm install` ourselves.
 */
function ensureFresh(pkgRoot, label) {
  const srcDir = path.join(pkgRoot, 'src');
  const distDir = path.join(pkgRoot, 'dist');

  const srcNewest = newestMtimeMs(srcDir, ['.ts', '.tsx']);
  if (srcNewest === 0) return; // no source (unexpected layout) — nothing to validate

  const distNewest = newestMtimeMs(distDir, ['.js', '.d.ts']);
  if (distNewest !== 0 && distNewest >= srcNewest) return; // fresh — fast path

  if (!fs.existsSync(path.join(pkgRoot, 'node_modules'))) {
    console.warn(
      `[ensure-dist-fresh] ${label} dist is stale/missing but node_modules is absent — ` +
        `run \`npm install\` in ${pkgRoot}. Skipping its build (downstream may fail).`
    );
    return;
  }

  console.log(`[ensure-dist-fresh] ${label} dist is stale or missing — rebuilding (npm run build)...`);
  try {
    execSync('npm run build', { cwd: pkgRoot, stdio: 'inherit' });
  } catch (err) {
    console.error(
      `[ensure-dist-fresh] ${label} build FAILED — downstream build will likely fail too. ` +
        `Run \`npm run build\` in ${pkgRoot} to see the full error.`
    );
    process.exit(1);
  }
}

// Freshen sibling deps that ui-components imports (needed when a consumer bundles
// ui-components src) BEFORE ui-components itself, so the whole chain is current.
ensureFresh(path.join(SHARED_ROOT, 'Spaarke.SdapClient'), '@spaarke/sdap-client');
ensureFresh(path.join(SHARED_ROOT, 'Spaarke.Auth'), '@spaarke/auth');
ensureFresh(LIB_ROOT, '@spaarke/ui-components');
