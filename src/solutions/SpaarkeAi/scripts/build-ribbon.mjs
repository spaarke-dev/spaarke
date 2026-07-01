/**
 * build-ribbon.mjs — Build Dataverse ribbon JS web resources from TS sources.
 *
 * Scans src/ribbon/*.ts (excluding __tests__ and xrm-globals.d.ts) and produces
 * one IIFE-format JS bundle per file, each attached to the Sprk.SpaarkeAi
 * global namespace. The dotted global-name matches the ribbon XML
 * FunctionName conventions:
 *
 *   src/ribbon/DocumentComposeLaunch.ts
 *     → dist-ribbon/DocumentComposeLaunch.js
 *     → globalName: Sprk.SpaarkeAi.DocumentComposeLaunch
 *     → ribbon XML: Sprk.SpaarkeAi.DocumentComposeLaunch.openInCompose
 *
 * The dotted globalName is a canonical esbuild feature — the emitted bundle
 * safely initializes each namespace segment with `X = X || {}` before assigning
 * the IIFE return value, so multiple ribbon scripts can coexist without
 * clobbering each other.
 *
 * Web-resource naming convention (matches ribbon XML $webresource references):
 *   src/ribbon/DocumentComposeLaunch.ts
 *     → web resource name: sprk_spaarkeai_documentcomposelaunch
 *
 * Deploy step lives in a separate deploy script (see
 * scripts/Deploy-SpaarkeAiRibbon.ps1) so that build and deploy are independent
 * (parallels the code-page Deploy-SpaarkeAi.ps1 pattern).
 *
 * @see src/solutions/SpaarkeAi/src/ribbon/DocumentComposeLaunch.ts
 * @see infrastructure/dataverse/ribbon/DocumentRibbons/opencompose-button.xml
 */

import { build } from "esbuild";
import { mkdirSync, readdirSync, statSync } from "node:fs";
import { basename, dirname, extname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);
const solutionRoot = resolve(__dirname, "..");
const ribbonSourceDir = join(solutionRoot, "src", "ribbon");
const ribbonOutputDir = join(solutionRoot, "dist-ribbon");

const GLOBAL_NAMESPACE = "Sprk.SpaarkeAi";

/**
 * Returns true if the given path is a compilable ribbon TS file:
 *   - lives directly under src/ribbon/ (not a subdirectory)
 *   - has extension .ts
 *   - is NOT a declaration file (*.d.ts)
 *   - is NOT a test file
 */
function isRibbonSource(entry) {
  const stats = statSync(join(ribbonSourceDir, entry));
  if (!stats.isFile()) return false;
  if (extname(entry) !== ".ts") return false;
  if (entry.endsWith(".d.ts")) return false;
  if (entry.includes(".test.") || entry.includes(".spec.")) return false;
  return true;
}

function discoverRibbonSources() {
  return readdirSync(ribbonSourceDir).filter(isRibbonSource);
}

async function buildOne(sourceFile) {
  const sourceName = basename(sourceFile, ".ts");
  const entryPath = join(ribbonSourceDir, sourceFile);
  const outFile = join(ribbonOutputDir, `${sourceName}.js`);
  const globalName = `${GLOBAL_NAMESPACE}.${sourceName}`;

  await build({
    entryPoints: [entryPath],
    outfile: outFile,
    bundle: true,
    format: "iife",
    globalName,
    target: "es2019",
    platform: "browser",
    sourcemap: false,
    minify: false,
    logLevel: "info",
    legalComments: "none",
  });

  return { sourceName, outFile, globalName };
}

async function main() {
  mkdirSync(ribbonOutputDir, { recursive: true });
  const sources = discoverRibbonSources();

  if (sources.length === 0) {
    console.error(`No ribbon TS sources found under ${ribbonSourceDir}`);
    process.exit(1);
  }

  console.log(`Building ${sources.length} ribbon script(s) → ${ribbonOutputDir}`);
  const results = [];
  for (const source of sources) {
    results.push(await buildOne(source));
  }

  console.log();
  console.log("Ribbon build complete:");
  for (const r of results) {
    console.log(`  ${r.sourceName.padEnd(28)} globalName=${r.globalName}`);
  }
}

main().catch((err) => {
  console.error("Ribbon build failed:", err);
  process.exit(1);
});
