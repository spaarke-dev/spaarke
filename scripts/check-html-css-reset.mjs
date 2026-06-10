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
// Usage (in a surface's package.json build script):
//
//   "build": "node ../../../scripts/check-html-css-reset.mjs && vite build && ..."
//
// Or as a standalone check from the repo root:
//
//   node scripts/check-html-css-reset.mjs src/solutions/MySurface/index.html

import { readFileSync, existsSync } from 'fs';
import { resolve } from 'path';

const indexHtmlArg = process.argv[2] ?? 'index.html';
const indexHtmlPath = resolve(process.cwd(), indexHtmlArg);

if (!existsSync(indexHtmlPath)) {
  console.error(`✗ check-html-css-reset: file not found — ${indexHtmlPath}`);
  process.exit(1);
}

const html = readFileSync(indexHtmlPath, 'utf8');

// Look for the canonical universal selector + box-sizing border-box rule.
// Accept some whitespace + ordering variation. Reject configurations that
// only set box-sizing on specific elements (insufficient — the rule must be
// universal to catch every Fluent cell + descendants).
//
// Matched patterns (any of):
//   *, *::before, *::after { box-sizing: border-box; }
//   * { box-sizing: border-box; }  (broader; also acceptable)
//
// Rejected (insufficient — e.g. only html/body):
//   html, body { box-sizing: border-box; }
const universalReset = /\*\s*(,\s*\*::before\s*,\s*\*::after\s*)?\{\s*box-sizing\s*:\s*border-box\s*;?\s*\}/;

if (!universalReset.test(html)) {
  console.error(`✗ check-html-css-reset: ${indexHtmlPath} is missing the universal box-sizing reset.`);
  console.error('');
  console.error('Add this <style> block to <head>:');
  console.error('');
  console.error('  *, *::before, *::after { box-sizing: border-box; }');
  console.error('');
  console.error('Why: docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md §2.');
  console.error('Without it, every DataGrid cell renders +24px wider than its');
  console.error('declared width (12+12 padding adds to content-box) and the grid');
  console.error('overflows the page.');
  process.exit(1);
}

console.log(`✓ check-html-css-reset: ${indexHtmlArg} has the universal box-sizing reset.`);
