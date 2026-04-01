import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";
import path from "path";
import fs from "fs";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

/**
 * Custom Vite plugin to resolve bare module imports from shared library
 * source files to THIS project's node_modules.
 *
 * Required because @spaarke/auth and @spaarke/ui-components are aliased to
 * source directories outside the project root. Their bare imports (e.g.,
 * "@fluentui/react-components") must be resolved from this project's node_modules.
 */
function resolveSharedLibDeps(): import("vite").Plugin {
  const sharedLibPaths = [
    path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src"),
    path.resolve(__dirname, "../../client/shared/Spaarke.Auth/src"),
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

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [
    resolveSharedLibDeps(),
    react({
      include: [
        "src/**/*.tsx",
        "src/**/*.ts",
        path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src/**/*.tsx"),
        path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src/**/*.ts"),
        path.resolve(__dirname, "../../client/shared/Spaarke.Auth/src/**/*.tsx"),
        path.resolve(__dirname, "../../client/shared/Spaarke.Auth/src/**/*.ts"),
      ],
    }),
    // Inline all JS/CSS into HTML for simple Dataverse web resource deployment
    viteSingleFile(),
  ],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
      "@spaarke/ui-components": path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src"),
      "@spaarke/auth": path.resolve(__dirname, "../../client/shared/Spaarke.Auth/src"),
      "@fluentui/react-components": path.resolve(__dirname, "node_modules/@fluentui/react-components"),
      "@fluentui/react-icons": path.resolve(__dirname, "node_modules/@fluentui/react-icons"),
    },
    // Force deduplication for shared packages
    dedupe: [
      "react",
      "react-dom",
      "scheduler",
      "@fluentui/react-components",
      "@fluentui/react-icons",
      "react-window",
    ],
  },
  build: {
    // Handle CJS modules from shared library dependencies (react-window, etc.)
    commonjsOptions: {
      include: [/node_modules/],
      transformMixedEsModules: true,
    },
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
        // Single bundle for web resource deployment
        manualChunks: undefined,
      },
    },
  },
  // Base path for Dataverse webresource deployment
  base: "./",
});
