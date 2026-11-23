import path from "node:path";
import { fileURLToPath } from "node:url";

/**
 * lint-staged configuration for Spaarke monorepo.
 *
 * - Prettier: runs on staged .ts, .tsx, .json, .yaml, .yml files
 * - ESLint:   runs on staged .ts, .tsx files (grouped by nearest eslint.config directory)
 * - dotnet format: whitespace-formats staged .cs files (--folder, scoped via --include)
 *
 * Uses function syntax (.mjs) because ESLint flat config requires running
 * from the directory containing the eslint.config.mjs file. Plain JSON
 * config cannot handle per-directory command generation.
 */

const ROOT_DIR = path.dirname(fileURLToPath(import.meta.url));

/**
 * Known directories containing eslint.config.mjs files (relative to repo root).
 * lint-staged groups files by their nearest config directory
 * and runs ESLint from that directory so flat config resolves correctly.
 */
const ESLINT_CONFIG_DIRS = [
  "src/client/pcf/DocumentRelationshipViewer",
  "src/client/pcf/RelatedDocumentCount",
  "src/client/pcf/ScopeConfigEditor",
  "src/client/pcf/SemanticSearchControl",
  "src/client/pcf/SpeFileViewer",
  "src/client/pcf/UniversalDatasetGrid",
  "src/client/pcf", // parent fallback for controls without their own config
];

/**
 * Convert an absolute file path to a path relative to the repo root,
 * normalized to forward slashes.
 */
function toRelative(filePath) {
  return path.relative(ROOT_DIR, filePath).replace(/\\/g, "/");
}

/**
 * Find the nearest eslint.config directory for a given file path.
 * Returns the most specific (deepest) matching directory, or null if none match.
 */
function findEslintConfigDir(relPath) {
  let bestMatch = null;
  let bestLength = 0;

  for (const dir of ESLINT_CONFIG_DIRS) {
    if (relPath.startsWith(dir + "/") && dir.length > bestLength) {
      bestMatch = dir;
      bestLength = dir.length;
    }
  }

  return bestMatch;
}

export default {
  // Prettier + ESLint: format and lint staged TypeScript files
  "**/*.{ts,tsx}": (files) => {
    const commands = [`prettier --write ${files.join(" ")}`];

    // Group files by their ESLint config directory
    const groups = new Map();
    for (const file of files) {
      const relPath = toRelative(file);
      const configDir = findEslintConfigDir(relPath);
      if (configDir) {
        if (!groups.has(configDir)) {
          groups.set(configDir, []);
        }
        groups.get(configDir).push(file);
      }
    }

    // Run ESLint from each config directory via wrapper script.
    // The wrapper handles cross-platform cwd issues (cd fails on Windows cmd.exe).
    for (const [configDir, groupFiles] of groups) {
      const absConfigDir = path.join(ROOT_DIR, configDir);
      const script = path.join(ROOT_DIR, "scripts/quality/lint-staged-eslint.mjs");
      commands.push(
        `node "${script}" "${absConfigDir}" ${groupFiles.join(" ")}`
      );
    }

    return commands;
  },

  // Prettier: format staged JSON, YAML files
  "**/*.{json,yaml,yml}": "prettier --write",

  // Provisioning prereqs manifest: run the SAME validator CI runs (parser parity
  // with .claude/skills/provision-environment SKILL.md Step 0.5a), so author-time
  // signal == CI signal. Catches yaml parse defects, missing required fields,
  // scope enum typos, duplicate ids, and SPE never_delete regressions BEFORE
  // they land in the manifest. Added 2026-08-27 after the 18-defect regression.
  "scripts/provisioning-prereqs/{prereqs.yaml,intake.schema.json}": () => [
    "pwsh -NoProfile -File scripts/provisioning-prereqs/validate.ps1",
  ],

  // dotnet format: whitespace-format staged C# files (scoped to staged files only).
  //
  // `whitespace --folder` formats from .editorconfig without loading the MSBuild workspace: about 2 s per
  // commit instead of 16-45 s, with no restore needed and no BuildHost to be killed under load. It makes the
  // same changes as the workspace mode, and it is exactly what CI's Tier 2 `Format (dotnet format)` job checks
  // (`dotnet format whitespace --verify-no-changes`). The slow workspace run was why commits were made with
  // `--no-verify`, and those commits left 2,569 violations in 230 files on master by 2026-11-10.
  // ROOT_DIR is the folder, so absolute and relative staged paths both resolve.
  "**/*.cs": (files) => {
    const fileList = files.map((f) => `"${f}"`).join(" ");
    return [`dotnet format whitespace "${ROOT_DIR}" --folder --verbosity quiet --include ${fileList}`];
  },
};
