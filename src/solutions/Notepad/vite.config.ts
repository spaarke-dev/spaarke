import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";
import path from "path";
import fs from "fs";

const sharedLibRoot = path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src");

/**
 * Resolve bare-module imports from shared library source files to THIS
 * project's node_modules. Required because @spaarke/ui-components is aliased
 * to a source directory outside the project root — its internal bare imports
 * (e.g., "@fluentui/react-components") would otherwise fail to resolve.
 *
 * Mirrors the same plugin used by src/solutions/SmartTodo/vite.config.ts
 * and src/solutions/CalendarSidePane/vite.config.ts.
 */
function resolveSharedLibDeps(): import("vite").Plugin {
  const sharedLibPaths = [sharedLibRoot].map((p) => p.replace(/\\/g, "/"));
  const nodeModulesDir = path.resolve(__dirname, "node_modules");

  return {
    name: "resolve-shared-lib-deps",
    enforce: "pre",
    async resolveId(source, importer, options) {
      if (!importer) return null;
      const normalizedImporter = importer.replace(/\\/g, "/");
      const isSharedLib = sharedLibPaths.some((p) => normalizedImporter.startsWith(p));
      if (!isSharedLib) return null;
      if (source.startsWith(".") || source.startsWith("/")) return null;
      if (source.startsWith("@spaarke/")) return null;

      let pkgName: string;
      if (source.startsWith("@")) {
        const parts = source.split("/");
        pkgName = parts.slice(0, 2).join("/");
      } else {
        pkgName = source.split("/")[0];
      }

      const pkgDir = path.join(nodeModulesDir, pkgName);
      if (!fs.existsSync(pkgDir)) return null;

      const fakeImporter = path.join(__dirname, "__virtual_importer__.ts");
      const result = await this.resolve(source, fakeImporter, {
        ...options,
        skipSelf: true,
      });
      return result;
    },
  };
}

export default defineConfig({
  plugins: [
    resolveSharedLibDeps(),
    react({
      include: [
        "src/**/*.tsx",
        "src/**/*.ts",
        path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src/**/*.tsx"),
        path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src/**/*.ts"),
      ],
    }),
    viteSingleFile(),
  ],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
      "@spaarke/ui-components/PanelSplitter": path.resolve(sharedLibRoot, "components/PanelSplitter/index.ts"),
      // DEF-10 (2026-07-04): alias the subpath prefixes to DIRECTORIES rather
      // than to barrel `index.ts` files. This lets deep-path imports such as
      // `@spaarke/ui-components/services/PolymorphicResolverService` resolve
      // through Vite's default file lookup, skipping the barrel and its
      // transitive re-exports (services barrel pulls in EntityCreationService →
      // `mammoth` docx parser, ~300 KB; utils barrel pulls in top-level barrel;
      // top-level barrel pulls in Lexical + PDF.js + App Insights).
      // Barrel behavior is preserved because Vite still resolves the bare
      // subpath (e.g. `@spaarke/ui-components/utils`) to `utils/index.ts` by
      // default file-lookup, matching the previous alias exactly.
      "@spaarke/ui-components/hooks": path.resolve(sharedLibRoot, "hooks"),
      "@spaarke/ui-components/services": path.resolve(sharedLibRoot, "services"),
      "@spaarke/ui-components/utils": path.resolve(sharedLibRoot, "utils"),
      "@spaarke/ui-components": path.resolve(sharedLibRoot, "index.ts"),
    },
    // Ensure shared lib imports resolve from Notepad's node_modules
    dedupe: ["react", "react-dom", "@fluentui/react-components", "@fluentui/react-icons"],
  },
  // Allow Vite to resolve shared lib dependencies from Notepad's node_modules
  optimizeDeps: {
    include: ["@fluentui/react-components", "@fluentui/react-icons", "react", "react-dom"],
  },
  build: {
    outDir: "dist",
    sourcemap: false,
    assetsInlineLimit: 100000000,
    commonjsOptions: {
      include: [/node_modules/],
    },
    rollupOptions: {
      output: {
        manualChunks: undefined,
      },
    },
  },
  base: "./",
});
