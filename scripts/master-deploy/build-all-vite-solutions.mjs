#!/usr/bin/env node
// Build all Vite solutions sequentially using their existing node_modules.
//
// This is the canonical fallback for the master-deploy skill (Step 2). The
// alternative — running scripts/Build-AllClientComponents.ps1 -SkipSharedLibs —
// has been observed to fail every Vite solution in batch (see master-deploy
// SKILL.md Failure Mode F-2). Root cause not fully diagnosed; until it is,
// this Node serial builder is the proven path.
//
// Each solution that has a vite.config.ts is built via `npm run build`. No
// install is run — the caller is responsible for ensuring node_modules is
// hydrated (either prior `npm install` or shared lib build chain that
// transitively installed deps).
//
// Skips: directories without package.json or without vite.config.ts.
//        Specifically: CopilotAgent, DemoRegistration, EventCommands,
//        SpaarkeCore, spaarke_insights, TodoDetailSidePane, webresources.

import { execSync } from 'child_process';
import { readdirSync, existsSync, writeFileSync } from 'fs';
import { join } from 'path';

const repoRoot = 'c:/code_files/spaarke';
const solutionsRoot = join(repoRoot, 'src/solutions');

const solutions = readdirSync(solutionsRoot, { withFileTypes: true })
  .filter((d) => d.isDirectory())
  .map((d) => d.name)
  .filter((name) => {
    const dir = join(solutionsRoot, name);
    return existsSync(join(dir, 'package.json')) && existsSync(join(dir, 'vite.config.ts'));
  });

const results = [];

for (const sol of solutions) {
  const dir = join(solutionsRoot, sol);
  const start = Date.now();
  const logFile = `c:/tmp/build-${sol}.log`;
  let status = 'SUCCESS';

  try {
    console.log(`Building ${sol}...`);
    execSync(`npm run build`, {
      cwd: dir,
      stdio: ['ignore', 'pipe', 'pipe'],
      encoding: 'utf8',
      timeout: 5 * 60 * 1000, // 5-minute hard cap per solution
    });
    const dur = ((Date.now() - start) / 1000).toFixed(1);
    console.log(`  ✓ ${sol} (${dur}s)`);
    results.push({ sol, status, dur });
  } catch (e) {
    const dur = ((Date.now() - start) / 1000).toFixed(1);
    status = 'FAILED';
    const detail = (e.stdout || '') + '\n' + (e.stderr || '');
    writeFileSync(logFile, detail);
    const errMatch = detail.match(/failed to resolve.*?\n.*?\.ts/s);
    const summary = errMatch ? errMatch[0].slice(0, 200) : detail.slice(-300);
    console.log(`  ✗ ${sol} (${dur}s) — log: ${logFile}`);
    console.log(`    ${summary.replace(/\n/g, '\n    ')}`);
    results.push({ sol, status, dur });
  }
}

console.log('\n=== SUMMARY ===');
const success = results.filter((r) => r.status === 'SUCCESS').length;
const failed = results.filter((r) => r.status === 'FAILED').length;
console.log(`Total: ${results.length}, SUCCESS: ${success}, FAILED: ${failed}`);
for (const r of results) {
  console.log(`  ${r.status === 'SUCCESS' ? '✓' : '✗'} ${r.sol.padEnd(30)} ${r.dur}s`);
}
process.exit(failed > 0 ? 1 : 0);
