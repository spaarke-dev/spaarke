/**
 * Shared, type-aware ESLint 9 flat config for the Spaarke client packages.
 *
 * ONE toolchain (this directory's package.json) lints every covered package against that package's
 * own tsconfig. Packages do NOT get their own ESLint setups.
 *
 * Rules (P2 of the client error-handling cleanup): ONLY `@typescript-eslint/no-unnecessary-condition`
 * (NUC) at `error`, scoped to non-test `src/**`. It is what flags a dead `if (!res.ok)` after
 * `AuthenticatedFetchFn` (which resolves to `OkResponse`: `ok: true`, 2xx). Existing violations are
 * recorded in `eslint-suppressions.json` (ESLint bulk suppressions); only NEW ones fail.
 *
 * Run through `scripts/quality/client-lint/client-lint.mjs`, not directly. cwd must be the repo root.
 *
 * Type resolution: each package's tsconfig maps `@spaarke/*` to a sibling `dist/` that CI would have to
 * build first. This config instead generates (into `.generated/`, gitignored) a lint tsconfig per
 * package that `extends` the package's own tsconfig and re-points every `@spaarke/*` at sibling SOURCE,
 * so no package needs building. Third-party types still come from each package's own node_modules
 * (`client-lint.mjs install`).
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import tseslint from "typescript-eslint";

const HERE = path.dirname(fileURLToPath(import.meta.url));
export const REPO_ROOT = path.resolve(HERE, "..", "..", "..");
const GENERATED = path.join(HERE, ".generated");

/** Covered packages. `id` is the `--package` filter value. */
export const PACKAGES = [
  { id: "ui-components", dir: "src/client/shared/Spaarke.UI.Components" },
  { id: "ai-widgets", dir: "src/client/shared/Spaarke.AI.Widgets" },
  { id: "compose-components", dir: "src/client/shared/Spaarke.Compose.Components" },
  { id: "communication-components", dir: "src/client/shared/Spaarke.Communication.Components" },
  { id: "document-operations", dir: "src/client/shared/Spaarke.DocumentOperations" },
  { id: "daily-briefing-components", dir: "src/client/shared/Spaarke.DailyBriefing.Components" },
  { id: "spaarkeai", dir: "src/solutions/SpaarkeAi" },
  { id: "legal-workspace-app", dir: "src/solutions/LegalWorkspace" },
  { id: "reporting", dir: "src/solutions/Reporting" },
  { id: "semantic-search", dir: "src/client/code-pages/SemanticSearch" },
];

/** Packages whose source is only read for types (siblings that the covered ones import). Installed, not linted. */
export const TYPE_ONLY_PACKAGES = [
  "src/client/shared/Spaarke.Auth",
];

const SHARED_ROOT = path.join(REPO_ROOT, "src/client/shared");

const rel = (from, to) => path.relative(from, to).split(path.sep).join("/");

function stripJsonc(text) {
  // Remove // and /* */ comments outside strings, and trailing commas.
  let out = "";
  let inStr = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    const n = text[i + 1];
    if (inStr) {
      out += c;
      if (c === "\\") out += text[++i];
      else if (c === '"') inStr = false;
    } else if (c === '"') {
      inStr = true;
      out += c;
    } else if (c === "/" && n === "/") {
      while (i < text.length && text[i] !== "\n") i++;
      out += "\n";
    } else if (c === "/" && n === "*") {
      i += 2;
      while (i < text.length && !(text[i] === "*" && text[i + 1] === "/")) i++;
      i++;
    } else out += c;
  }
  return out.replace(/,(\s*[}\]])/g, "$1");
}

/** `@spaarke/<name>` -> sibling source, for every shared package that has one. */
function spaarkePaths() {
  const map = {};
  for (const d of fs.readdirSync(SHARED_ROOT)) {
    const pj = path.join(SHARED_ROOT, d, "package.json");
    const entry = path.join(SHARED_ROOT, d, "src/index.ts");
    if (!fs.existsSync(pj) || !fs.existsSync(entry)) continue;
    const name = JSON.parse(fs.readFileSync(pj, "utf8")).name;
    if (!name?.startsWith("@spaarke/")) continue;
    map[name] = [rel(REPO_ROOT, entry)];
    map[`${name}/*`] = [rel(REPO_ROOT, path.join(SHARED_ROOT, d, "src")) + "/*"];
  }
  return map;
}

function writeIfChanged(file, content) {
  if (fs.existsSync(file) && fs.readFileSync(file, "utf8") === content) return;
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, content);
}

/**
 * Generate `.generated/<id>.tsconfig.json`: extends the package tsconfig, same include/exclude, but with
 * `@spaarke/*` re-pointed at source. Non-@spaarke path entries of the package (`@fluentui/...` pinned to
 * the package's own node_modules, `@/*`) are kept and re-based onto the repo root.
 */
function generateTsconfig(pkg, extraSources = []) {
  const pkgDir = path.join(REPO_ROOT, pkg.dir);
  const base = path.join(pkgDir, "tsconfig.json");
  const raw = JSON.parse(stripJsonc(fs.readFileSync(base, "utf8")));
  const ownPaths = raw.compilerOptions?.paths ?? {};
  const ownBase = path.resolve(pkgDir, raw.compilerOptions?.baseUrl ?? ".");
  const paths = {};
  for (const [k, v] of Object.entries(ownPaths)) {
    if (k.startsWith("@spaarke/")) continue;
    paths[k] = v.map((p) => rel(REPO_ROOT, path.resolve(ownBase, p)));
  }
  Object.assign(paths, spaarkePaths());
  const out = path.join(GENERATED, `${pkg.id}.tsconfig.json`);
  const here = path.dirname(out);
  const cfg = {
    extends: rel(here, base),
    compilerOptions: {
      baseUrl: rel(here, REPO_ROOT),
      paths,
      noEmit: true,
      composite: false,
      declaration: false,
      declarationMap: false,
      emitDeclarationOnly: false,
      incremental: false,
      rootDir: rel(here, REPO_ROOT),
      allowImportingTsExtensions: true,
      skipLibCheck: true,
    },
    include: [rel(here, path.join(pkgDir, "src")) + "/**/*", ...extraSources],
    exclude: [
      rel(here, path.join(pkgDir, "node_modules")),
      rel(here, path.join(pkgDir, "dist")),
    ],
  };
  writeIfChanged(out, JSON.stringify(cfg, null, 2) + "\n");
  return out;
}

/** Test, mock, harness, declaration and story files: not linted (tests are out of scope for P2). */
export const IGNORES = [
  "**/node_modules/**",
  "**/dist/**",
  "**/.generated/**",
  "**/*.d.ts",
  "**/*.test.{ts,tsx}",
  "**/*.spec.{ts,tsx}",
  "**/__tests__/**",
  "**/__mocks__/**",
  "**/__testEnvironments__/**",
  "**/__test-harness__/**",
  "**/test/**",
  "**/tests/**",
  "**/*.stories.{ts,tsx}",
];

const NUC = { "@typescript-eslint/no-unnecessary-condition": "error" };

/**
 * Existing source carries `// eslint-disable-next-line react-hooks/exhaustive-deps`-style directives for rules this
 * toolchain deliberately does not run. ESLint 9 reports a directive naming an unknown rule as an error, so each
 * such namespace is registered with a no-op rule set (every rule name resolves, none ever reports).
 */
const noopRule = { meta: { schema: false }, create: () => ({}) };
const noopPlugin = { rules: new Proxy({}, { get: () => noopRule, has: () => true }) };
const DIRECTIVE_NAMESPACES = ["react-hooks", "react", "jsx-a11y", "import", "react-refresh", "jest", "testing-library"];
const PLUGINS = {
  "@typescript-eslint": tseslint.plugin,
  ...Object.fromEntries(DIRECTIVE_NAMESPACES.map((n) => [n, noopPlugin])),
};

function packageBlock(pkg) {
  return {
    name: `client-lint/${pkg.id}`,
    files: [`${pkg.dir}/src/**/*.{ts,tsx}`],
    languageOptions: {
      parser: tseslint.parser,
      parserOptions: { project: [generateTsconfig(pkg)], tsconfigRootDir: REPO_ROOT },
    },
    plugins: PLUGINS,
    linterOptions: { reportUnusedDisableDirectives: "off" },
    rules: NUC,
  };
}

/** Production config: all covered packages. */
export default [{ ignores: IGNORES }, ...PACKAGES.map(packageBlock)];

/** Control config: the committed must-fire / must-not-fire fixtures (no suppressions, no ignores of tests). */
export function controlsConfig() {
  const controlsDir = "scripts/quality/client-lint/controls";
  const tsconfig = path.join(GENERATED, "controls.tsconfig.json");
  const here = path.dirname(tsconfig);
  writeIfChanged(
    tsconfig,
    JSON.stringify(
      {
        compilerOptions: {
          target: "ES2020",
          module: "ESNext",
          moduleResolution: "bundler",
          lib: ["ES2020", "DOM"],
          strict: true,
          noEmit: true,
          skipLibCheck: true,
          baseUrl: rel(here, REPO_ROOT),
          paths: spaarkePaths(),
        },
        include: [rel(here, path.join(REPO_ROOT, controlsDir)) + "/**/*.ts"],
      },
      null,
      2,
    ) + "\n",
  );
  return [
    {
      files: [`${controlsDir}/**/*.ts`],
      languageOptions: {
        parser: tseslint.parser,
        parserOptions: { project: [tsconfig], tsconfigRootDir: REPO_ROOT },
      },
      plugins: PLUGINS,
      linterOptions: { reportUnusedDisableDirectives: "off" },
      rules: NUC,
    },
  ];
}
