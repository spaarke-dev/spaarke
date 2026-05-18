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
 * Problem: @spaarke/ui-components and @spaarke/auth are aliased to source
 * directories outside the project root. When Vite processes those files,
 * bare imports (e.g., "@fluentui/react-components", "@azure/msal-browser")
 * fail because Vite walks upward from the importer's directory and never
 * reaches this project's node_modules.
 *
 * Solution: Intercept resolution requests and redirect to this project's
 * node_modules directory, then let Vite's normal resolution handle the rest.
 *
 * @see src/solutions/LegalWorkspace/vite.config.ts — canonical pattern
 */
function resolveSharedLibDeps(): import("vite").Plugin {
  const sharedLibPaths = [
    path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src"),
    path.resolve(__dirname, "../../client/shared/Spaarke.Auth/src"),
    path.resolve(__dirname, "../../client/shared/Spaarke.AI.Widgets/src"),
    path.resolve(__dirname, "../../client/shared/Spaarke.AI.Outputs/src"),
    path.resolve(__dirname, "../../client/shared/Spaarke.AI.Context/src"),
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
      // Include shared lib source files for transpilation (ADR-022: Code Pages bundle React)
      include: [
        "src/**/*.tsx",
        "src/**/*.ts",
        path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src/**/*.tsx"),
        path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src/**/*.ts"),
        path.resolve(__dirname, "../../client/shared/Spaarke.AI.Widgets/src/**/*.tsx"),
        path.resolve(__dirname, "../../client/shared/Spaarke.AI.Widgets/src/**/*.ts"),
        path.resolve(__dirname, "../../client/shared/Spaarke.AI.Outputs/src/**/*.tsx"),
        path.resolve(__dirname, "../../client/shared/Spaarke.AI.Outputs/src/**/*.ts"),
        path.resolve(__dirname, "../../client/shared/Spaarke.AI.Context/src/**/*.tsx"),
        path.resolve(__dirname, "../../client/shared/Spaarke.AI.Context/src/**/*.ts"),
      ],
    }),
    // Inline all JS/CSS into a single HTML file for Dataverse web resource deployment (ADR-026)
    viteSingleFile(),
  ],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
      // Map shared libraries to their source directories for direct source transpilation.
      // This avoids the need for a pre-built dist/ and ensures tree-shaking works correctly.
      // Map bare imports (e.g. '@spaarke/auth') to /src for direct transpilation.
      // Deep imports (e.g. '@spaarke/ui-components/src/components/...') are handled
      // by the more-specific path alias that maps to the package root (no /src suffix),
      // avoiding double /src/src/ resolution.
      "@spaarke/ui-components/src": path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src"),
      "@spaarke/ui-components": path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src"),
      "@spaarke/auth": path.resolve(__dirname, "../../client/shared/Spaarke.Auth/src"),
      "@spaarke/ai-widgets/src": path.resolve(__dirname, "../../client/shared/Spaarke.AI.Widgets/src"),
      "@spaarke/ai-widgets": path.resolve(__dirname, "../../client/shared/Spaarke.AI.Widgets/src"),
      "@spaarke/ai-outputs/src": path.resolve(__dirname, "../../client/shared/Spaarke.AI.Outputs/src"),
      "@spaarke/ai-outputs": path.resolve(__dirname, "../../client/shared/Spaarke.AI.Outputs/src"),
      "@spaarke/ai-context/src": path.resolve(__dirname, "../../client/shared/Spaarke.AI.Context/src"),
      "@spaarke/ai-context": path.resolve(__dirname, "../../client/shared/Spaarke.AI.Context/src"),
    },
    // Force single copy of shared packages across the bundle (ADR-022: no duplicate React)
    dedupe: [
      "react",
      "react-dom",
      "scheduler",
      "@fluentui/react-components",
      "@fluentui/react-icons",
      "@fluentui/react-context-selector",
      "@lexical/react",
      "lexical",
    ],
  },
  build: {
    // Output to dist folder for deployment
    outDir: "dist",
    // Disable sourcemaps for inline build (not useful when inlined)
    sourcemap: false,
    // Ensure all assets are inlined (ADR-026: single self-contained HTML)
    assetsInlineLimit: 100000000,
    // Enable minification for production
    minify: true,
    rollupOptions: {
      output: {
        // Single bundle — no code splitting for web resource deployment
        manualChunks: undefined,
      },
    },
  },
  // Base path for Dataverse webresource deployment
  base: "./",
});
