import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";
import path from "path";

// Spaarke DataGrid Framework R1 — task 023
// Mirrors the CalendarSidePane Vite config (canonical "simple shell" Code Page pattern).
// @spaarke/ui-components is resolved from its prebuilt dist/ via node_modules — same
// resolution path EventsPage uses for ui-components (per ADR-026 reference list).
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
        // Single bundle for Custom Page deployment
        manualChunks: undefined,
      },
    },
  },
  // Base path for Dataverse webresource deployment
  base: "./",
});
