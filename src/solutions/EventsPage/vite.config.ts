import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";
import path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Task 087 (2026-10-03, C-25): the `resolveSharedLibDeps` plugin + the
// `@spaarke/events-components` source alias below were removed. Both existed
// solely to source-alias `@spaarke/events-components` (Task 114, 2026-05-22);
// EventsPage was rewritten onto `@spaarke/ui-components` (task 031) and has
// had zero imports of `@spaarke/events-components` since. See
// projects/spaarke-ontology-platform-r1/notes/reuse-verification-2026-10-02.md
// §8.8 (root-cause note) + §8.9 C-25.

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [
    react({
      // Include shared lib source for transpilation (ADR-022: Code Pages bundle React)
      include: ["src/**/*.tsx", "src/**/*.ts"],
    }),
    // Inline all JS/CSS into HTML for simple Dataverse web resource deployment
    viteSingleFile(),
  ],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
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
