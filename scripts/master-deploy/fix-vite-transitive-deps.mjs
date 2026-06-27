#!/usr/bin/env node
// Add @spaarke/sdap-client and @spaarke/smart-todo-components to every Vite
// solution's package.json that depends on @spaarke/ui-components.
//
// Why: PR #369 (multi-container) added `@spaarke/sdap-client` as a TRANSITIVE
// import via UI.Components/services/EntityCreationService.ts. PR #377
// (smart-todo-r4) added `@spaarke/smart-todo-components` via LegalWorkspace's
// todo.registration.ts. Neither PR updated CONSUMING surfaces' package.json
// files. Without explicit declaration, vite/rollup fails with "Rollup failed
// to resolve import" during the build — even though the shared lib has the
// dep declared, npm only links it through the IMMEDIATE consumer.
//
// This script is the F-1 failure-mode handler in the master-deploy skill.
// Idempotent — re-running has no effect if the deps are already present.

import { readFileSync, writeFileSync, readdirSync, existsSync } from 'fs';
import { join } from 'path';

const repoRoot = 'c:/code_files/spaarke';
const solutionsRoot = join(repoRoot, 'src/solutions');

const TRANSITIVE_DEPS = {
  '@spaarke/sdap-client': 'file:../../client/shared/Spaarke.SdapClient',
  '@spaarke/smart-todo-components': 'file:../../client/shared/Spaarke.SmartTodo.Components',
};

const solutions = readdirSync(solutionsRoot, { withFileTypes: true })
  .filter((d) => d.isDirectory())
  .map((d) => d.name);

let touched = 0;
let skipped = 0;

for (const sol of solutions) {
  const pkgPath = join(solutionsRoot, sol, 'package.json');
  if (!existsSync(pkgPath)) {
    console.log(`(no package.json) ${sol}`);
    continue;
  }
  const pkg = JSON.parse(readFileSync(pkgPath, 'utf8'));
  pkg.dependencies = pkg.dependencies || {};

  if (!pkg.dependencies['@spaarke/ui-components']) {
    console.log(`(no @spaarke/ui-components) ${sol}`);
    skipped++;
    continue;
  }

  let changed = false;
  for (const [dep, val] of Object.entries(TRANSITIVE_DEPS)) {
    if (!pkg.dependencies[dep]) {
      pkg.dependencies[dep] = val;
      changed = true;
    }
  }

  if (!changed) {
    console.log(`(deps already present) ${sol}`);
    skipped++;
    continue;
  }

  // Re-sort dependencies alphabetically for stable diffs.
  pkg.dependencies = Object.fromEntries(
    Object.entries(pkg.dependencies).sort(([a], [b]) => a.localeCompare(b))
  );

  writeFileSync(pkgPath, JSON.stringify(pkg, null, 2) + '\n', 'utf8');
  console.log(`✓ ${sol} — added missing transitive deps`);
  touched++;
}

console.log(`\nDone. Touched: ${touched}, Skipped: ${skipped}, Total: ${solutions.length}`);
