import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";
import path from "path";

// NavigatorPane — Vite code page hosted as a pageType:'webresource' pane
// (ADR-006 Path C; mirrors src/solutions/EventDetailSidePane/vite.config.ts,
// the simpler of the two canonical-reference configs — NavigatorPane only
// consumes the pre-built @spaarke/ui-components dist/, so the
// resolveSharedLibDeps source-alias plugin CalendarSidePane needs (for
// @spaarke/events-components source-transpilation) is not required here).
// https://vitejs.dev/config/
export default defineConfig({
  plugins: [
    react(),
    // Inline all JS/CSS into HTML for simple Dataverse web resource deployment
    viteSingleFile(),
  ],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
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
        // Single bundle for webresource deployment
        manualChunks: undefined,
      },
    },
  },
  // Base path for Dataverse webresource deployment
  base: "./",
});
