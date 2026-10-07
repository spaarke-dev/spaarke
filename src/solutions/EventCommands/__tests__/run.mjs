// Runs this folder's node:test files for the client-tests CI workflow, which discovers any package.json with a test
// script and calls `npm test -- --ci --reporters=default --passWithNoTests=false` (jest flags). `node --test` would
// reject those, so this runner ignores argv. Exit code 1 on any failure. spaarke-ontology-platform-r1 task 098.
import { run } from 'node:test';
import { spec } from 'node:test/reporters';
import { readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const files = readdirSync(here).filter(f => f.endsWith('.test.mjs')).map(f => join(here, f));
if (files.length === 0) {
  console.error('No *.test.mjs files found - failing rather than reporting a vacuous green.');
  process.exit(1);
}

run({ files })
  .on('test:fail', () => {
    process.exitCode = 1;
  })
  .compose(spec)
  .pipe(process.stdout);
