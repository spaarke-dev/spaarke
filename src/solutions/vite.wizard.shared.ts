/**
 * Shared Vite config for the standalone wizard Code Pages
 * (CreateEvent / CreateInvoice / CreateMatter / CreateProject / CreateReportCard /
 * CreateWorkAssignment / CreateTodo / SummarizeFiles wizards and RecordFamily).
 *
 * Each page's `vite.config.ts` is one line:
 *
 *   import { createWizardViteConfig } from "../vite.wizard.shared";
 *   export default createWizardViteConfig(__dirname);
 *
 * Output shape is unchanged from the per-page copies this replaced: a single-file
 * HTML (`dist/index.html`, JS/CSS inlined) for Dataverse web-resource deployment.
 *
 * The e-billing pages use the same factory through `_ebilling-shared/vite.ebilling.ts` with the options below (#2965). With no options the
 * config is exactly what it was before them, so the wizard pages are unchanged.
 *
 * @param pageDir absolute path of the page folder (its `node_modules` and `src` are used).
 * @param options optional extras; see {@link CodePageViteOptions}.
 */
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";
import path from "path";
import fs from "fs";

const sharedRoot = path.resolve(__dirname, "../client/shared");

/** Optional extras for a family of pages that share source or need a stricter dedupe than the wizards. */
export interface CodePageViteOptions {
  /**
   * Absolute folders of source shared by a family of pages, outside the page itself. Their bare imports resolve from the page's
   * `node_modules` (like the shared libraries) and their JSX goes through the React plugin.
   */
  extraSourceDirs?: string[];
  /** `resolve.dedupe`: packages forced onto the page's single copy. */
  dedupe?: string[];
  /** Transform CommonJS modules that also use ES module syntax (`build.commonjsOptions`). */
  commonjsMixedEsModules?: boolean;
}

/**
 * Resolves bare module imports from shared library source files to THIS page's
 * node_modules. Required because @spaarke/auth and @spaarke/ui-components are
 * aliased to source directories outside the page root, so their bare imports
 * (e.g. "@azure/msal-browser") must be resolved from the page's node_modules.
 */
function resolveSharedLibDeps(pageDir: string, extraSourceDirs: string[] = []): import("vite").Plugin {
  const sharedLibPaths = [
    path.resolve(sharedRoot, "Spaarke.UI.Components/src"),
    path.resolve(sharedRoot, "Spaarke.Auth/src"),
    ...extraSourceDirs,
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

export function createWizardViteConfig(pageDir: string, options: CodePageViteOptions = {}) {
  const extraSourceDirs = options.extraSourceDirs ?? [];
  return defineConfig({
    plugins: [
      resolveSharedLibDeps(pageDir, extraSourceDirs),
      react({
        include: [
          "src/**/*.tsx",
          "src/**/*.ts",
          ...extraSourceDirs.flatMap((dir) => [path.resolve(dir, "**/*.tsx"), path.resolve(dir, "**/*.ts")]),
          path.resolve(sharedRoot, "Spaarke.UI.Components/src/**/*.tsx"),
          path.resolve(sharedRoot, "Spaarke.UI.Components/src/**/*.ts"),
          path.resolve(sharedRoot, "Spaarke.Auth/src/**/*.tsx"),
          path.resolve(sharedRoot, "Spaarke.Auth/src/**/*.ts"),
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
        "@fluentui/react-components": path.resolve(pageDir, "node_modules/@fluentui/react-components"),
        "@fluentui/react-icons": path.resolve(pageDir, "node_modules/@fluentui/react-icons"),
      },
      ...(options.dedupe ? { dedupe: options.dedupe } : {}),
    },
    build: {
      ...(options.commonjsMixedEsModules
        ? { commonjsOptions: { include: [/node_modules/], transformMixedEsModules: true } }
        : {}),
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
