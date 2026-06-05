import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";
import path from "path";
import { fileURLToPath } from "url";
import fs from "fs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

/**
 * Custom Vite plugin to resolve bare module imports from shared library
 * source files to THIS project's node_modules.
 *
 * R4 task 055 (B-6 Option B, 2026-05-26): mirrors the EventsPage canonical
 * pattern. When `@spaarke/events-components` is consumed via source-alias
 * (file: dependency), Rollup walks into the shared lib's .tsx files and
 * encounters bare imports (e.g., "@fluentui/react-components") that can't
 * resolve because Vite walks upward from the importer's directory and never
 * reaches THIS project's node_modules. This plugin intercepts those
 * resolution requests and redirects to CalendarSidePane's node_modules.
 *
 * @see src/solutions/EventsPage/vite.config.ts — canonical pattern
 * @see src/solutions/LegalWorkspace/vite.config.ts — original source
 */
function resolveSharedLibDeps(): import("vite").Plugin {
  const sharedLibPaths = [
    path.resolve(__dirname, "../../client/shared/Spaarke.Events.Components/src"),
  ].map((p) => p.replace(/\\/g, "/"));

  const nodeModulesDir = path.resolve(__dirname, "node_modules");

  return {
    name: "resolve-shared-lib-deps",
    enforce: "pre",
    async resolveId(source, importer, options) {
      if (!importer) return null;
      const normalizedImporter = importer.replace(/\\/g, "/");
      const isSharedLib = sharedLibPaths.some((p) => normalizedImporter.startsWith(p));
      if (!isSharedLib) return null;
      // Only intercept bare module imports
      if (source.startsWith(".") || source.startsWith("/")) return null;
      if (source.startsWith("@spaarke/")) return null;

      // Extract the package name (handle scoped packages like @fluentui/react-button)
      let pkgName: string;
      if (source.startsWith("@")) {
        const parts = source.split("/");
        pkgName = parts.slice(0, 2).join("/");
      } else {
        pkgName = source.split("/")[0];
      }

      // Check if the package exists in our node_modules
      const pkgDir = path.join(nodeModulesDir, pkgName);
      if (!fs.existsSync(pkgDir)) return null;

      // Re-resolve this import as if it came from a file in THIS project
      const fakeImporter = path.join(__dirname, "__virtual_importer__.ts");
      const result = await this.resolve(source, fakeImporter, {
        ...options,
        skipSelf: true,
      });
      return result;
    },
  };
}

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [
    resolveSharedLibDeps(),
    react({
      // Include shared lib source for transpilation (ADR-022: Code Pages bundle React)
      include: [
        "src/**/*.tsx",
        "src/**/*.ts",
        path.resolve(__dirname, "../../client/shared/Spaarke.Events.Components/src/**/*.tsx"),
        path.resolve(__dirname, "../../client/shared/Spaarke.Events.Components/src/**/*.ts"),
      ],
    }),
    // Inline all JS/CSS into HTML for simple Dataverse web resource deployment
    viteSingleFile(),
  ],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
      // R4 task 055 (B-6 Option B): map @spaarke/events-components to its
      // source so Vite transpiles + bundles directly (no pre-built dist/).
      // Mirrors EventsPage / SpaarkeAi alias pattern.
      "@spaarke/events-components/src": path.resolve(__dirname, "../../client/shared/Spaarke.Events.Components/src"),
      "@spaarke/events-components": path.resolve(__dirname, "../../client/shared/Spaarke.Events.Components/src"),
    },
    // Prefer .ts/.tsx over .js so stale tsc-emit siblings never silently shadow source.
    extensions: [".ts", ".tsx", ".mts", ".cts", ".js", ".mjs", ".cjs", ".jsx", ".json"],
    // Force single copy of shared packages across the bundle (ADR-022: no duplicate React)
    dedupe: [
      "react",
      "react-dom",
      "scheduler",
      "@fluentui/react-components",
      "@fluentui/react-icons",
      "@fluentui/react-context-selector",
    ],
  },
  build: {
    // Output to dist folder for deployment
    outDir: "dist",
    // Disable sourcemaps for inline build (not useful when inlined)
    sourcemap: false,
    // Increase inline limit to ensure everything is inlined
    assetsInlineLimit: 100000000,
    rollupOptions: {
      output: {
        // Single bundle for Custom Page deployment
        manualChunks: undefined,
      },
    },
  },
  // Base path for Dataverse webresource deployment
  base: "./",
});
