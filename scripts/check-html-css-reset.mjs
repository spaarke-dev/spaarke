#!/usr/bin/env node
// check-html-css-reset.mjs — Verify a Code Page's index.html contains the
// canonical CSS reset required for the DataGrid framework and consistent
// FluentProvider behavior.
//
// Required pattern (per docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md §2):
//
//   *, *::before, *::after {
//     box-sizing: border-box;
//   }
//
// Without this, every grid cell renders +24px wider than its declared
// width (content-box adds 12+12 padding) and the grid overflows the page.
// Multiple host surfaces had this latent bug for months because the reset
// was documented but not enforced at build time. Established 2026-06-09
// after iter-2 round 11 fix on ai-spaarke-ai-workspace-UI-r1.
//
// Usage 1 — Single-file check (in a surface's package.json build script):
//
//   "build": "node ../../../scripts/check-html-css-reset.mjs && vite build && ..."
//
// Or with explicit path from the repo root:
//
//   node scripts/check-html-css-reset.mjs src/solutions/MySurface/index.html
//
// Usage 2 — Repo-wide check (used by CI; auto-discovers all Code Page hosts):
//
//   node scripts/check-html-css-reset.mjs --all
//
// Scans:
//   - src/solutions/*/index.html        (Vite Code Pages)
//   - src/client/code-pages/*/index.html (Webpack Code Pages)
//
// Returns exit code 0 if every discovered index.html has the reset.
// Returns exit code 1 with a list of failures otherwise.

import { readFileSync, existsSync, readdirSync, statSync } from 'fs';
import { resolve, join } from 'path';

// Same regex used by both modes — single source of truth for the contract.
//
// Matched patterns (any of):
//   *, *::before, *::after { box-sizing: border-box; }
//   * { box-sizing: border-box; }                       (broader; also acceptable)
//   * { box-sizing: border-box; margin: 0; padding: 0; } (with sibling resets — common pattern)
//
// Rejected (insufficient — e.g. only html/body):
//   html, body { box-sizing: border-box; }
//
// The character-class `[^{}]*` lets sibling properties appear in the same rule
// block without rejecting the match, while still requiring the universal `*`
// selector at the start of the rule.
const universalReset = /\*\s*(,\s*\*::before\s*,\s*\*::after\s*)?\{[^{}]*box-sizing\s*:\s*border-box[^{}]*\}/;

function checkOne(htmlPath) {
  if (!existsSync(htmlPath)) {
    return { path: htmlPath, ok: false, reason: 'file-not-found' };
  }
  const html = readFileSync(htmlPath, 'utf8');
  if (!universalReset.test(html)) {
    return { path: htmlPath, ok: false, reason: 'missing-reset' };
  }
  return { path: htmlPath, ok: true };
}

function printSingleFailure(htmlPath, reason) {
  if (reason === 'file-not-found') {
    console.error(`✗ check-html-css-reset: file not found — ${htmlPath}`);
    return;
  }
  console.error(`✗ check-html-css-reset: ${htmlPath} is missing the universal box-sizing reset.`);
  console.error('');
  console.error('Add this <style> block to <head>:');
  console.error('');
  console.error('  *, *::before, *::after { box-sizing: border-box; }');
  console.error('');
  console.error('Why: docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md §2.');
  console.error('Without it, every DataGrid cell renders +24px wider than its');
  console.error('declared width (12+12 padding adds to content-box) and the grid');
  console.error('overflows the page.');
}

function discoverAllSurfaceHtmls(repoRoot) {
  const roots = [
    join(repoRoot, 'src', 'solutions'),
    join(repoRoot, 'src', 'client', 'code-pages'),
  ];
  const out = [];
  for (const root of roots) {
    if (!existsSync(root)) continue;
    for (const name of readdirSync(root)) {
      const dir = join(root, name);
      let s;
      try {
        s = statSync(dir);
      } catch {
        continue;
      }
      if (!s.isDirectory()) continue;
      const indexHtml = join(dir, 'index.html');
      if (existsSync(indexHtml)) out.push(indexHtml);
    }
  }
  return out;
}

const args = process.argv.slice(2);

if (args.includes('--all')) {
  // Repo-wide mode: scan all known Code Page roots; aggregate failures.
  // CWD here is the repo root when called from CI (`node scripts/check-html-css-reset.mjs --all`).
  const repoRoot = process.cwd();
  const surfaces = discoverAllSurfaceHtmls(repoRoot);

  if (surfaces.length === 0) {
    console.error('✗ check-html-css-reset --all: no Code Page index.htmls discovered.');
    console.error(`  Searched: src/solutions/*/index.html, src/client/code-pages/*/index.html`);
    console.error(`  CWD: ${repoRoot}`);
    process.exit(1);
  }

  const results = surfaces.map(checkOne);
  const failures = results.filter((r) => !r.ok);
  const passed = results.length - failures.length;

  if (failures.length > 0) {
    console.error(`✗ check-html-css-reset --all: ${failures.length} of ${results.length} Code Page hosts FAILED.`);
    console.error('');
    for (const f of failures) {
      const rel = f.path.startsWith(repoRoot) ? f.path.slice(repoRoot.length + 1) : f.path;
      console.error(`  - ${rel} (${f.reason})`);
    }
    console.error('');
    console.error('Add the canonical reset to each failing host:');
    console.error('');
    console.error('  *, *::before, *::after { box-sizing: border-box; }');
    console.error('');
    console.error('Reference: docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md §2.');
    console.error('History: surfaced 2026-06-09 by ai-spaarke-ai-workspace-UI-r1 iter-2 round 11.');
    process.exit(1);
  }

  console.log(`✓ check-html-css-reset --all: ${passed} Code Page host(s) verified clean.`);
  process.exit(0);
}

// Single-file mode (backward-compatible — used by per-surface build scripts).
const indexHtmlArg = args[0] ?? 'index.html';
const indexHtmlPath = resolve(process.cwd(), indexHtmlArg);
const result = checkOne(indexHtmlPath);

if (!result.ok) {
  printSingleFailure(indexHtmlPath, result.reason);
  process.exit(1);
}

console.log(`✓ check-html-css-reset: ${indexHtmlArg} has the universal box-sizing reset.`);
