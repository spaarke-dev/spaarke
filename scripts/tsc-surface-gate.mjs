#!/usr/bin/env node
// tsc-surface-gate.mjs — Build-time TypeScript gate scoped to surface-owned files.
//
// Purpose: catch ReferenceError-class bugs (undestructured props, missing
// imports, type mismatches) in surface-owned files (src/**) before Vite/Webpack
// bundling, without blocking on pre-existing errors in shared libs at ../../client/shared.
//
// Why scoped: shared libs (@spaarke/ai-widgets,
// @spaarke/events-components, @spaarke/ui-components) have ~30+ pre-existing
// hygiene + import-path issues. Fixing those is Phase B follow-up. Until then,
// this gate fails ONLY on errors in the surface's own src/** so the bug class
// that produced the 2026-06-09 SpaarkeAi blank-page incident (tabs.some(...)
// referencing an undestructured prop) is caught at build time.
//
// Established 2026-06-09 by ai-spaarke-ai-workspace-UI-r1 brittleness Phase A.4.
// Phase B (shared-lib hygiene + re-enable noUnusedLocals/Parameters at base)
// will retire the scoping and revert to plain `tsc --noEmit`.
//
// Ratchet mode (ISS-005 / #2808, 2026-11-18): `--baseline <file>` stops ignoring the
// errors outside src/** and pins them instead. Each is keyed by file + TS code + message
// (no line/column, so an unrelated edit above it does not move it) and counted:
//   - an error in src/**                      → fail (src/** is never baselined);
//   - an error outside src/** not in the file → fail (a new error);
//   - a baselined error that no longer occurs → warning, or failure with `--fail-on-stale`
//     (CI passes it, so the baseline is pruned in the PR that fixes the error; a local
//     build only warns, so a shared-lib cleanup elsewhere never breaks a deploy build).
// `--update-baseline` rewrites the file from the current errors (it refuses while src/**
// has errors). Without `--baseline` the gate behaves as it always has.
//
// `--owned <folder>` (issue 2965, repeatable) adds a folder outside src/** to the files that count as surface-owned (see below).
// Without the flag nothing changes, in either mode.
//
// Fails closed: tsc exiting non-zero without printing an `error TS` line means tsc did
// not run (missing install, bad tsconfig), which is a broken gate, not a clean tree.

import { execSync } from 'child_process';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const args = process.argv.slice(2);
const baselineArg = args.indexOf('--baseline');
const baselinePath = baselineArg >= 0 ? args[baselineArg + 1] : null;
const updateBaseline = args.includes('--update-baseline');
const failOnStale = args.includes('--fail-on-stale');

if (baselineArg >= 0 && !baselinePath) {
  console.error('✗ tsc-surface-gate: --baseline needs a file path.');
  process.exit(2);
}
if ((updateBaseline || failOnStale) && !baselinePath) {
  console.error('✗ tsc-surface-gate: --update-baseline and --fail-on-stale need --baseline <file>.');
  process.exit(2);
}

// `--owned <folder>` (repeatable): a folder outside this package's src/** whose files the package's own code depends on and that no
// other gate checks (e.g. `_ebilling-shared`, the e-billing pages' shared bootstrap). Its errors count as surface-owned: they fail the
// gate and are never baselined. tsc prints such a file as `..\_ebilling-shared\x.ts`; the path is normalised (`\` -> `/`, leading `../`
// dropped) and matched on whole folder names, so `--owned _ebilling-shared` never matches `_ebilling-shared-other`.
const ownedFolders = [];
for (let i = 0; i < args.length; i++) {
  if (args[i] !== '--owned') continue;
  const folder = args[i + 1];
  if (!folder || folder.startsWith('--')) {
    console.error('✗ tsc-surface-gate: --owned needs a folder name, e.g. --owned _ebilling-shared.');
    process.exit(2);
  }
  ownedFolders.push(folder.replace(/\\/g, '/').replace(/^(\.\.\/)+/, '').replace(/\/+$/, ''));
}

function isInOwnedFolder(line) {
  if (ownedFolders.length === 0) return false;
  const m = /^(.*?)\(\d+,\d+\): error TS\d+:/.exec(line.trim());
  if (!m) return false;
  const file = m[1].replace(/\\/g, '/').replace(/^(\.\.\/)+/, '');
  return ownedFolders.some((f) => file === f || file.startsWith(`${f}/`));
}

function runTsc() {
  try {
    execSync('npx tsc --noEmit', { stdio: 'pipe', maxBuffer: 64 * 1024 * 1024 });
    return { ok: true, output: '' };
  } catch (e) {
    return { ok: false, output: (e.stdout?.toString() ?? '') + (e.stderr?.toString() ?? '') };
  }
}

const result = runTsc();
const lines = result.output.split(/\r?\n/).filter((l) => l.includes('error TS'));

if (!result.ok && lines.length === 0) {
  console.error('✗ tsc-surface-gate: tsc exited non-zero but reported no `error TS` diagnostics — tsc did not run to completion:');
  console.error(result.output.trim() || '(no output)');
  process.exit(1);
}

// A diagnostic is surface-owned when its file is under this package's src/**, or under a folder named with --owned.
const isOwned = (line) => /^src[\\/]/.test(line) || isInOwnedFolder(line);
const ownedErrors = lines.filter(isOwned);
const otherErrors = lines.filter((l) => !isOwned(l));

// ---------------------------------------------------------------------------
// Default (scoped) mode — unchanged behaviour.
// ---------------------------------------------------------------------------
if (!baselinePath) {
  if (result.ok) {
    console.log('✓ tsc-surface-gate: no errors anywhere.');
    process.exit(0);
  }
  if (ownedErrors.length === 0) {
    console.warn(`⚠ tsc-surface-gate: ${otherErrors.length} pre-existing error(s) in shared libs (deferred to Phase B). Surface-owned: 0. ✓`);
    process.exit(0);
  }
  console.error(`✗ tsc-surface-gate: ${ownedErrors.length} error(s) in surface-owned files (src/** and any --owned folder):`);
  console.error('');
  for (const err of ownedErrors) console.error(err);
  console.error('');
  console.error(`(${otherErrors.length} additional error(s) in shared libs ignored — addressed in Phase B.)`);
  process.exit(1);
}

// ---------------------------------------------------------------------------
// Ratchet mode.
// ---------------------------------------------------------------------------
// A message can quote an absolute path (e.g. TS7016); key it repo-relative so the baseline
// matches on every machine.
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..').replace(/\\/g, '/');
const rootPattern = new RegExp(repoRoot.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'gi');

/** `file(l,c): error TSnnnn: message` → `file|TSnnnn|message`, path separators normalised. */
function keyOf(line) {
  const m = /^(.*?)\(\d+,\d+\): error (TS\d+): (.*)$/.exec(line.trim());
  if (!m) return line.trim();
  const file = m[1].replace(/\\/g, '/');
  const message = m[3].replace(/\\/g, '/').replace(rootPattern, '<repo>');
  return `${file}|${m[2]}|${message}`;
}

const current = {};
for (const l of otherErrors) {
  const k = keyOf(l);
  current[k] = (current[k] ?? 0) + 1;
}

const resolvedBaseline = path.resolve(baselinePath);

if (updateBaseline) {
  if (ownedErrors.length > 0) {
    console.error(`✗ tsc-surface-gate: refusing to update the baseline — ${ownedErrors.length} error(s) in src/** (never baselined; fix them):`);
    for (const err of ownedErrors) console.error(err);
    process.exit(1);
  }
  const sorted = Object.fromEntries(Object.keys(current).sort().map((k) => [k, current[k]]));
  const doc = {
    _comment:
      'tsc-surface-gate ratchet baseline. Errors OUTSIDE this package\'s src/** that its own `tsc --noEmit` reports today, keyed file|code|message with a count. A new one fails the gate; regenerate with `node <repo>/scripts/tsc-surface-gate.mjs --baseline <this file> --update-baseline` only after fixing, never to admit a new error.',
    errors: sorted,
  };
  fs.writeFileSync(resolvedBaseline, JSON.stringify(doc, null, 2) + '\n');
  console.log(`✓ tsc-surface-gate: baseline written — ${otherErrors.length} error(s), ${Object.keys(sorted).length} key(s) → ${baselinePath}`);
  process.exit(0);
}

let baseline;
try {
  baseline = JSON.parse(fs.readFileSync(resolvedBaseline, 'utf8')).errors ?? {};
} catch (e) {
  console.error(`✗ tsc-surface-gate: cannot read baseline ${baselinePath}: ${e.message}`);
  process.exit(1);
}

const added = [];
for (const l of otherErrors) {
  const k = keyOf(l);
  // Count down the allowance; anything past it is new.
  if ((baseline[k] ?? 0) > 0) baseline[k] -= 1;
  else added.push(l);
}
const stale = Object.entries(baseline).filter(([, n]) => n > 0);

let failed = false;
if (ownedErrors.length > 0) {
  failed = true;
  console.error(`✗ tsc-surface-gate: ${ownedErrors.length} error(s) in surface-owned files (src/** and any --owned folder):`);
  for (const err of ownedErrors) console.error(err);
  console.error('');
}
if (added.length > 0) {
  failed = true;
  console.error(`✗ tsc-surface-gate: ${added.length} new error(s) outside src/** (not in ${baselinePath}):`);
  for (const err of added) console.error(err);
  console.error('');
}
if (stale.length > 0) {
  const say = failOnStale ? console.error : console.warn;
  say(`${failOnStale ? '✗' : '⚠'} tsc-surface-gate: ${stale.length} baselined error key(s) no longer occur — prune them with --update-baseline:`);
  for (const [k, n] of stale) say(`  ${k}  (×${n})`);
  console.error('');
  if (failOnStale) failed = true;
}

if (failed) process.exit(1);
console.log(`✓ tsc-surface-gate: src/** clean; ${otherErrors.length} baselined error(s) outside src/**, no new ones.`);
process.exit(0);
