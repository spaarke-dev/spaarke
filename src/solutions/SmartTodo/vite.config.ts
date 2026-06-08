import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";
import path from "path";
import fs from "fs";

const sharedLibRoot = path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src");
const authLibRoot = path.resolve(__dirname, "../../client/shared/Spaarke.Auth/src");

/**
 * Resolve bare-module imports from shared library source files to THIS
 * project's node_modules. Required because @spaarke/auth + @spaarke/ui-components
 * are aliased to source directories outside the project root — their internal
 * bare imports (e.g., "@azure/msal-browser") would otherwise fail to resolve.
 *
 * Mirrors the same plugin used by src/solutions/CreateTodoWizard/vite.config.ts.
 * Task 070b added this to SmartTodo when @spaarke/auth was introduced for the
 * launch-context wizard host.
 */
function resolveSharedLibDeps(): import("vite").Plugin {
  const sharedLibPaths = [sharedLibRoot, authLibRoot].map((p) => p.replace(/\\/g, "/"));
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
        path.resolve(__dirname, "../../client/shared/Spaarke.Auth/src/**/*.tsx"),
        path.resolve(__dirname, "../../client/shared/Spaarke.Auth/src/**/*.ts"),
      ],
    }),
    viteSingleFile(),
  ],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
      "@spaarke/ui-components/PanelSplitter": path.resolve(sharedLibRoot, "components/PanelSplitter/index.ts"),
      "@spaarke/ui-components/hooks": path.resolve(sharedLibRoot, "hooks/useTwoPanelLayout.ts"),
      "@spaarke/ui-components/TodoDetail": path.resolve(sharedLibRoot, "components/TodoDetail/index.ts"),
      "@spaarke/ui-components/AssociateToStep": path.resolve(sharedLibRoot, "components/AssociateToStep/index.ts"),
      "@spaarke/ui-components/services": path.resolve(sharedLibRoot, "services/index.ts"),
      "@spaarke/ui-components/utils": path.resolve(sharedLibRoot, "utils/index.ts"),
      "@spaarke/ui-components": path.resolve(sharedLibRoot, "index.ts"),
      "@spaarke/auth": authLibRoot,
    },
    // Ensure shared lib imports resolve from SmartTodo's node_modules
    dedupe: ["react", "react-dom", "@fluentui/react-components", "@fluentui/react-icons"],
  },
  // Allow Vite to resolve shared lib dependencies from SmartTodo's node_modules
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
