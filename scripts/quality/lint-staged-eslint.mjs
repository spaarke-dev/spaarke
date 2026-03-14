#!/usr/bin/env node

/**
 * ESLint wrapper for lint-staged in a monorepo.
 *
 * Runs ESLint from the directory containing the nearest eslint.config.mjs
 * so that flat config resolves correctly. This avoids cross-platform issues
 * with `cd` commands in lint-staged shell execution.
 *
 * Behavior:
 *   --fix is applied so auto-fixable issues are corrected before commit.
 *   Non-fixable errors are reported to stdout but do NOT block the commit.
 *   This is intentional: the codebase has pre-existing violations that will
 *   be cleaned up in remediation tasks (033). The CI pipeline (task 035)
 *   will be the blocking enforcement gate.
 *
 * Usage: node scripts/quality/lint-staged-eslint.mjs <configDir> <file1> [file2...]
 *   configDir: absolute path to directory containing eslint.config.mjs
 *   file1..N:  absolute paths to files to lint
 */

import { execFileSync } from "node:child_process";
import path from "node:path";

const [configDir, ...files] = process.argv.slice(2);

if (!configDir || files.length === 0) {
  console.error(
    "Usage: node lint-staged-eslint.mjs <configDir> <file1> [file2...]"
  );
  process.exit(1);
}

// Convert file paths to be relative to the config directory
const relativeFiles = files.map((f) =>
  path.relative(configDir, f).replace(/\\/g, "/")
);

try {
  execFileSync("npx", ["eslint", "--fix", ...relativeFiles], {
    cwd: configDir,
    stdio: "inherit",
    shell: true,
  });
} catch (error) {
  // ESLint exit code 1 = lint errors found (not auto-fixable).
  // Exit 0 so pre-commit hook does not block — advisory only during
  // the graduated enforcement period (Phase 1-2). Task 040 will
  // switch this to exit(error.status) when blocking gates are enabled.
  if (error.status === 1) {
    console.log(
      "\n[lint-staged-eslint] ESLint found issues (advisory — not blocking commit)"
    );
    process.exit(0);
  }
  // Exit code 2 = ESLint configuration/runtime error — this SHOULD block.
  process.exit(error.status || 1);
}
