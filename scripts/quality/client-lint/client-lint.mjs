#!/usr/bin/env node
/**
 * Runner for the shared client type-aware lint (see eslint.client.config.mjs).
 *
 *   node scripts/quality/client-lint/client-lint.mjs install [--package <id>]   npm install the covered packages (types)
 *   node scripts/quality/client-lint/client-lint.mjs check   [--package <id>]   lint; FAILS on a new violation or an unpruned suppression
 *   node scripts/quality/client-lint/client-lint.mjs prune                      drop suppressions that no longer occur (after fixing code)
 *   node scripts/quality/client-lint/client-lint.mjs baseline                   re-record ALL current violations (rare; reviewed in the PR)
 *   node scripts/quality/client-lint/client-lint.mjs controls                   assert the must-fire / must-not-fire fixtures
 *
 * `check` is the default. Run from anywhere; the runner pins cwd to the repo root (suppression keys are repo-relative).
 * First-time setup: `npm install --legacy-peer-deps --no-audit --no-fund` in this directory, then `install`.
 */
import { spawn, spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(HERE, "..", "..", "..");
const CONFIG = path.join(HERE, "eslint.client.config.mjs");
const SUPPRESSIONS = path.join(HERE, "eslint-suppressions.json");
const ESLINT_BIN = path.join(HERE, "node_modules", "eslint", "bin", "eslint.js");
const NPM = process.platform === "win32" ? "npm.cmd" : "npm";

const { PACKAGES, TYPE_ONLY_PACKAGES, controlsConfig } = await import(pathToFileURL(CONFIG).href);

const args = process.argv.slice(2);
const command = args[0] && !args[0].startsWith("--") ? args.shift() : "check";
const pkgFlag = args.indexOf("--package");
const only = pkgFlag >= 0 ? args[pkgFlag + 1] : null;
const selected = only ? PACKAGES.filter((p) => p.id === only) : PACKAGES;
if (only && selected.length === 0) {
  console.error(`Unknown package "${only}". Known: ${PACKAGES.map((p) => p.id).join(", ")}`);
  process.exit(2);
}

// Marker comments in controls/*.ts (a marker is a whole-line comment, so prose that mentions one never counts).
const MARK_FIRE = /^\s*\/\/ @control must-fire\b/;
const MARK_QUIET = /^\s*\/\/ @control must-not-fire\b/;

const fmt = (ms) => `${(ms / 1000).toFixed(1)}s`;

function requireToolchain() {
  if (!fs.existsSync(ESLINT_BIN)) {
    console.error(
      `ESLint toolchain not installed. Run: npm install --legacy-peer-deps --no-audit --no-fund (in ${path.relative(REPO_ROOT, HERE)})`,
    );
    process.exit(2);
  }
}

function runEslint(extra, targets, configFile = CONFIG) {
  return new Promise((resolve) => {
    const started = Date.now();
    const child = spawn(
      process.execPath,
      [
        "--max-old-space-size=6144",
        ESLINT_BIN,
        "--config",
        configFile,
        "--no-warn-ignored",
        "--suppressions-location",
        SUPPRESSIONS,
        ...extra,
        ...targets,
      ],
      { cwd: REPO_ROOT, stdio: "inherit" },
    );
    child.on("exit", (code) => resolve({ code: code ?? 1, ms: Date.now() - started }));
  });
}

async function install() {
  const dirs = [...selected.map((p) => p.dir), ...TYPE_ONLY_PACKAGES];
  const started = Date.now();
  let failed = 0;
  // Sequential on purpose: CI runners are small and npm already parallelises downloads.
  for (const dir of dirs) {
    const t = Date.now();
    const r = spawnSync(NPM, ["install", "--legacy-peer-deps", "--no-audit", "--no-fund", "--ignore-scripts"], {
      cwd: path.join(REPO_ROOT, dir),
      stdio: ["ignore", "ignore", "inherit"],
      shell: process.platform === "win32",
    });
    console.log(`${r.status === 0 ? "ok  " : "FAIL"} ${dir} (${fmt(Date.now() - t)})`);
    if (r.status !== 0) failed++;
  }
  console.log(`install total ${fmt(Date.now() - started)}`);
  process.exit(failed ? 1 : 0);
}

async function check() {
  requireToolchain();
  const timings = [];
  let exit = 0;
  // One ESLint process per package keeps each TypeScript program (and heap) small and gives per-package timing.
  // The suppressions file is shared; ESLint only reconciles entries for files it linted.
  for (const p of selected) {
    const r = await runEslint([], [`${p.dir}/src`]);
    timings.push([p.id, r.ms, r.code]);
    if (r.code !== 0) exit = r.code;
  }
  console.log("\nclient-lint timings:");
  for (const [id, ms, code] of timings) console.log(`  ${id.padEnd(28)} ${fmt(ms).padStart(8)}  ${code === 0 ? "ok" : "FAILED"}`);
  console.log(`  ${"total".padEnd(28)} ${fmt(timings.reduce((a, t) => a + t[1], 0)).padStart(8)}`);
  if (exit !== 0) {
    console.error(
      "\nclient-lint FAILED: a NEW violation (fix it, or justify it with `// eslint-disable-next-line <rule> -- reason`),\n" +
        "or suppressions that no longer occur (run `prune` and commit eslint-suppressions.json).",
    );
  }
  process.exit(exit);
}

async function prune() {
  requireToolchain();
  let exit = 0;
  for (const p of selected) {
    const r = await runEslint(["--prune-suppressions"], [`${p.dir}/src`]);
    if (r.code > 1) exit = r.code;
  }
  process.exit(exit);
}

async function baseline() {
  requireToolchain();
  if (only) {
    console.error("baseline re-records every package; do not combine with --package.");
    process.exit(2);
  }
  fs.rmSync(SUPPRESSIONS, { force: true });
  let exit = 0;
  for (const p of PACKAGES) {
    const r = await runEslint(["--suppress-rule", "@typescript-eslint/no-unnecessary-condition"], [`${p.dir}/src`]);
    console.log(`${p.id}: ${fmt(r.ms)}`);
    if (r.code > 1) exit = r.code;
  }
  process.exit(exit);
}

async function controls() {
  requireToolchain();
  const { ESLint } = await import(pathToFileURL(path.join(HERE, "node_modules", "eslint", "lib", "api.js")).href);
  const eslint = new ESLint({
    cwd: REPO_ROOT,
    overrideConfigFile: true,
    overrideConfig: controlsConfig(),
    ignore: false,
  });
  const dir = "scripts/quality/client-lint/controls";
  const results = await eslint.lintFiles([`${dir}/**/*.ts`]);
  let bad = 0;
  let mustFire = 0;
  let mustNot = 0;
  for (const r of results) {
    const lines = fs.readFileSync(r.filePath, "utf8").split(/\r?\n/);
    const reported = new Map();
    for (const m of r.messages) {
      if (m.fatal) {
        console.error(`FAIL parse error in ${r.filePath}: ${m.message}`);
        bad++;
      } else reported.set(m.line, m.ruleId);
    }
    lines.forEach((text, i) => {
      const fire = MARK_FIRE.test(text);
      const quiet = MARK_QUIET.test(text);
      if (!fire && !quiet) return;
      const target = i + 2; // 1-based line after the marker
      const got = reported.get(target);
      if (fire) {
        mustFire++;
        if (got !== "@typescript-eslint/no-unnecessary-condition") {
          console.error(`FAIL ${path.basename(r.filePath)}:${target} must FIRE no-unnecessary-condition but ${got ? `got ${got}` : "was silent"}`);
          bad++;
        }
      } else {
        mustNot++;
        if (got) {
          console.error(`FAIL ${path.basename(r.filePath)}:${target} must be SILENT but reported ${got}`);
          bad++;
        }
      }
    });
    // Any report on a line that no marker accounts for is also a defect in the fixture or the toolchain.
    for (const [line, rule] of reported) {
      const marker = lines[line - 2] ?? "";
      if (!MARK_FIRE.test(marker) && !MARK_QUIET.test(marker)) {
        console.error(`FAIL ${path.basename(r.filePath)}:${line} unexpected ${rule} (no control marker above)`);
        bad++;
      }
    }
  }
  if (mustFire === 0 || mustNot === 0) {
    console.error(`FAIL control fixture is empty (must-fire=${mustFire}, must-not-fire=${mustNot})`);
    bad++;
  }
  console.log(`controls: ${mustFire} must-fire, ${mustNot} must-not-fire, ${bad} failure(s)`);
  process.exit(bad ? 1 : 0);
}

const commands = { install, check, prune, baseline, controls };
if (!commands[command]) {
  console.error(`Unknown command "${command}". Use: ${Object.keys(commands).join(" | ")}`);
  process.exit(2);
}
await commands[command]();
