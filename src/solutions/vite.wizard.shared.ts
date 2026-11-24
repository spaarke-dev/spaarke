/**
 * Shared Vite config for the standalone wizard Code Pages
 * (CreateEvent / CreateInvoice / CreateMatter / CreateProject / CreateReportCard /
 * CreateWorkAssignment wizards and RecordFamily).
 *
 * Each page's `vite.config.ts` is one line:
 *
 *   import { createWizardViteConfig } from "../vite.wizard.shared";
 *   export default createWizardViteConfig(__dirname);
 *
 * Output shape is unchanged from the per-page copies this replaced: a single-file
 * HTML (`dist/index.html`, JS/CSS inlined) for Dataverse web-resource deployment.
 *
 * @param pageDir absolute path of the page folder (its `node_modules` and `src` are used).
 */
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";
import path from "path";
import fs from "fs";

const sharedRoot = path.resolve(__dirname, "../client/shared");

/**
 * Resolves bare module imports from shared library source files to THIS page's
 * node_modules. Required because @spaarke/auth and @spaarke/ui-components are
 * aliased to source directories outside the page root, so their bare imports
 * (e.g. "@azure/msal-browser") must be resolved from the page's node_modules.
 */
function resolveSharedLibDeps(pageDir: string): import("vite").Plugin {
  const sharedLibPaths = [
    path.resolve(sharedRoot, "Spaarke.UI.Components/src"),
    path.resolve(sharedRoot, "Spaarke.Auth/src"),
    path.resolve(sharedRoot, "Spaarke.SdapClient/src"),
  ].map((p) => p.replace(/\\/g, "/"));

  const nodeModulesDir = path.resolve(pageDir, "node_modules");

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

      const fakeImporter = path.join(pageDir, "__virtual_importer__.ts");
      return this.resolve(source, fakeImporter, { ...options, skipSelf: true });
    },
  };
}

export function createWizardViteConfig(pageDir: string) {
  return defineConfig({
    plugins: [
      resolveSharedLibDeps(pageDir),
      react({
        include: [
          "src/**/*.tsx",
          "src/**/*.ts",
          path.resolve(sharedRoot, "Spaarke.UI.Components/src/**/*.tsx"),
          path.resolve(sharedRoot, "Spaarke.UI.Components/src/**/*.ts"),
          path.resolve(sharedRoot, "Spaarke.Auth/src/**/*.tsx"),
          path.resolve(sharedRoot, "Spaarke.Auth/src/**/*.ts"),
          path.resolve(sharedRoot, "Spaarke.SdapClient/src/**/*.ts"),
        ],
      }),
      // Inline all JS/CSS into HTML for simple Dataverse web resource deployment
      viteSingleFile(),
    ],
    resolve: {
      alias: {
        "@": path.resolve(pageDir, "./src"),
        "@spaarke/ui-components": path.resolve(sharedRoot, "Spaarke.UI.Components/src"),
        "@spaarke/auth": path.resolve(sharedRoot, "Spaarke.Auth/src"),
        "@spaarke/sdap-client": path.resolve(sharedRoot, "Spaarke.SdapClient/src"),
        "@fluentui/react-components": path.resolve(pageDir, "node_modules/@fluentui/react-components"),
        "@fluentui/react-icons": path.resolve(pageDir, "node_modules/@fluentui/react-icons"),
      },
    },
    build: {
      outDir: "dist",
      // Disable sourcemaps for inline build (not useful when inlined)
      sourcemap: false,
      // Increase inline limit to ensure everything is inlined
      assetsInlineLimit: 100000000,
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
}
