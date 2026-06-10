#!/usr/bin/env node
// tsc-surface-gate.mjs — Build-time TypeScript gate scoped to surface-owned files.
//
// Purpose: catch ReferenceError-class bugs (undestructured props, missing
// imports, type mismatches) in surface-owned files (src/**) before Vite/Webpack
// bundling, without blocking on pre-existing errors in shared libs at ../../client/shared.
//
// Why scoped: shared libs (@spaarke/ai-context, @spaarke/ai-outputs, @spaarke/ai-widgets,
// @spaarke/events-components, @spaarke/ui-components) have ~30+ pre-existing
// hygiene + import-path issues. Fixing those is Phase B follow-up. Until then,
// this gate fails ONLY on errors in the surface's own src/** so the bug class
// that produced the 2026-06-09 SpaarkeAi blank-page incident (tabs.some(...)
// referencing an undestructured prop) is caught at build time.
//
// Established 2026-06-09 by ai-spaarke-ai-workspace-UI-r1 brittleness Phase A.4.
// Phase B (shared-lib hygiene + re-enable noUnusedLocals/Parameters at base)
// will retire the scoping and revert to plain `tsc --noEmit`.

import { execSync } from 'child_process';

function runTsc() {
  try {
    execSync('npx tsc --noEmit', { stdio: 'pipe' });
    return { ok: true, output: '' };
  } catch (e) {
    return { ok: false, output: (e.stdout?.toString() ?? '') + (e.stderr?.toString() ?? '') };
  }
}

const result = runTsc();

if (result.ok) {
  console.log('✓ tsc-surface-gate: no errors anywhere.');
  process.exit(0);
}

const lines = result.output.split('\n').filter((l) => l.includes('error TS'));
const ownedErrors = lines.filter((l) => /^src[\\/]/.test(l));
const sharedLibErrors = lines.length - ownedErrors.length;

if (ownedErrors.length === 0) {
  console.warn(`⚠ tsc-surface-gate: ${sharedLibErrors} pre-existing error(s) in shared libs (deferred to Phase B). Surface-owned: 0. ✓`);
  process.exit(0);
}

console.error(`✗ tsc-surface-gate: ${ownedErrors.length} error(s) in surface-owned files (src/**):`);
console.error('');
for (const err of ownedErrors) console.error(err);
console.error('');
console.error(`(${sharedLibErrors} additional error(s) in shared libs ignored — addressed in Phase B.)`);
process.exit(1);
