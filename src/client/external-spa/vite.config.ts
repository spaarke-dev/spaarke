import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "path";
import { fileURLToPath } from "url";
import fs from "fs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

/**
 * Post-build plugin to remove type="module" and crossorigin from the script tag
 * that references the external JS bundle.
 *
 * Problem: Vite always emits <script type="module" crossorigin src="..."> for the
 * entry script. The Power Pages Module Federation host intercepts type="module"
 * script tags and substitutes its React 16 singleton, crashing our React 18 IIFE.
 *
 * Fix: Strip type="module" and crossorigin so the browser sees a plain
 * <script src="..."> that the MF host has no hook into. The IIFE content
 * executes correctly as a plain script — it doesn't need module semantics.
 */
function removeModuleScriptType(): import("vite").Plugin {
  return {
    name: "remove-module-script-type",
    enforce: "post",
    apply: "build", // Dev server needs type="module" for ES modules — only strip for production IIFE build
    transformIndexHtml(html) {
      // Replace type="module" with defer (not remove): defer preserves
      // "run after DOM is ready" semantics without the module flag that
      // causes the Power Pages MF host to intercept the script.
      return html
        .replace(/ type="module"/g, " defer")
        .replace(/ crossorigin(?:="[^"]*")?/g, "");
    },
  };
}

/**
 * Custom Vite plugin to resolve bare module imports from shared library
 * source files to THIS project's node_modules.
 *
 * Problem: @spaarke/ui-components is aliased to a source directory outside
 * the project root. When Vite processes those files, bare imports
 * (e.g., "@fluentui/react-components") fail because Vite walks upward from
 * the importer's directory and never reaches this project's node_modules.
 *
 * Solution: Intercept resolution requests and redirect to this project's
 * node_modules directory, then let Vite's normal resolution handle the rest.
 */
function resolveSharedLibDeps(): import("vite").Plugin {
  const sharedLibPaths = [
    path.resolve(__dirname, "../shared/Spaarke.UI.Components/src"),
  ].map((p) => p.replace(/\\/g, "/"));

  const nodeModulesDir = path.resolve(__dirname, "node_modules");

  return {
    name: "resolve-shared-lib-deps",
    enforce: "pre",
    async resolveId(source, importer, options) {
      if (!importer) return null;
      const normalizedImporter = importer.replace(/\\/g, "/");
      const isSharedLib = sharedLibPaths.some((p) =>
        normalizedImporter.startsWith(p)
      );
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

export default defineConfig({
  plugins: [
    resolveSharedLibDeps(),
    removeModuleScriptType(),
    react({
      // Include shared lib source for transpilation
      include: [
        "src/**/*.tsx",
        "src/**/*.ts",
        path.resolve(
          __dirname,
          "../shared/Spaarke.UI.Components/src/**/*.tsx"
        ),
        path.resolve(
          __dirname,
          "../shared/Spaarke.UI.Components/src/**/*.ts"
        ),
      ],
    }),
  ],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
      "@spaarke/ui-components": path.resolve(
        __dirname,
        "../shared/Spaarke.UI.Components/src"
      ),
    },
    // Force deduplication for shared packages to avoid multiple React instances
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
    outDir: "dist",
    sourcemap: false,
    minify: true,
    // Use IIFE format to prevent the Power Pages portal's Module Federation host
    // from intercepting our ES module imports and substituting its React 16
    // singleton in place of our bundled React 18 (which causes invariant failures).
    // IIFE format produces a plain <script src="..."> tag — no type="module" attribute
    // for the MF host to intercept, and JS is served as a separate CDN file (no
    // inline truncation issues that affected viteSingleFile approach).
    rollupOptions: {
      output: {
        format: "iife",
        name: "ExternalWorkspaceSPA",
        // Predictable filenames for code site deployment
        entryFileNames: "assets/app.js",
        chunkFileNames: "assets/[name].js",
        assetFileNames: "assets/[name].[ext]",
      },
    },
  },
  // Development server with proxy to Power Pages portal.
  // Proxy rules forward /_api, /_layout, and /_services to the real Power Pages
  // site so that authentication (cookies, CSRF tokens) works transparently while
  // the SPA is served locally with hot module replacement.
  // Set VITE_PORTAL_URL in .env.local to target a different environment.
  server: {
    port: 3000,
    proxy: {
      // Route BFF API calls through the dev server to avoid browser CORS restrictions.
      // When VITE_BFF_API_URL is empty, bffApiCall uses relative /api/... paths
      // which hit this proxy. Vite forwards server-to-server — no CORS needed.
      "/api": {
        target: "https://spe-api-dev-67e2xz.azurewebsites.net",
        changeOrigin: true,
        secure: true,
      },
      "/_api": {
        target:
          process.env.VITE_PORTAL_URL ||
          "https://sprk-external-workspace.powerappsportals.com",
        changeOrigin: true,
        secure: false,
        cookieDomainRewrite: "localhost",
      },
      "/_layout": {
        target:
          process.env.VITE_PORTAL_URL ||
          "https://sprk-external-workspace.powerappsportals.com",
        changeOrigin: true,
        secure: false,
      },
      "/_services": {
        target:
          process.env.VITE_PORTAL_URL ||
          "https://sprk-external-workspace.powerappsportals.com",
        changeOrigin: true,
        secure: false,
      },
    },
  },
  base: "/",
});
