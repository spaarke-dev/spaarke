import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";
import path from "path";

// Spaarke DataGrid Code Page — single-file bundle for Dataverse web resource deployment.
// Mirrors sprk_invoicespage / sprk_kpiassessmentspage / EventsPage configs. See
// docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md for the contract.
export default defineConfig({
  plugins: [
    react(),
    viteSingleFile(),
  ],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  build: {
    outDir: "dist",
    sourcemap: false,
    assetsInlineLimit: 100000000,
    rollupOptions: {
      output: {
        manualChunks: undefined,
      },
    },
  },
  base: "./",
});
