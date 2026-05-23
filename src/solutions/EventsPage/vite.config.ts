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
 * Problem: @spaarke/events-components and @spaarke/ui-components are aliased
 * to source directories outside the project root. When Vite processes those
 * files, bare imports (e.g., "@fluentui/react-components") fail because Vite
 * walks upward from the importer's directory and never reaches this
 * project's node_modules.
 *
 * Solution: Intercept resolution requests and redirect to this project's
 * node_modules directory, then let Vite's normal resolution handle the rest.
 *
 * @see src/solutions/LegalWorkspace/vite.config.ts — canonical pattern
 */
function resolveSharedLibDeps(): import("vite").Plugin {
  const sharedLibPaths = [
    // Task 114 (2026-05-22): @spaarke/events-components is the only new
    // source-aliased shared lib for EventsPage. @spaarke/ui-components is
    // intentionally NOT source-aliased here to preserve baseline bundle
    // size parity (EventsPage has historically consumed ui-components from
    // its prebuilt dist/ — switching to source-alias would tree-shake +90 KB
    // out, an unintended scope change).
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
      // by creating a virtual importer path inside our project root
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
      // Task 114 (2026-05-22): map @spaarke/events-components to its source so
      // Vite transpiles + bundles directly (no pre-built dist/). Mirrors the
      // SpaarkeAi/LegalWorkspace alias pattern.
      //
      // @spaarke/ui-components is NOT aliased here (kept on its prebuilt
      // dist/ resolution path from node_modules) to preserve EventsPage's
      // baseline bundle size. Switching it to source-alias would
      // transparently tree-shake out ~90 KB gzip — that's a separate
      // optimization decision outside this hoist task's scope.
      "@spaarke/events-components/src": path.resolve(__dirname, "../../client/shared/Spaarke.Events.Components/src"),
      "@spaarke/events-components": path.resolve(__dirname, "../../client/shared/Spaarke.Events.Components/src"),
    },
    // Prefer .ts/.tsx over .js so stale tsc-emit siblings (if any escape
    // .gitignore) never silently shadow source. See Task 112 (2026-05-22).
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
    // Enable minification for production
    minify: true,
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
