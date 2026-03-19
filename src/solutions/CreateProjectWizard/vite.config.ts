import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";
import path from "path";

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
      "@spaarke/ui-components": path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src"),
      "@fluentui/react-components": path.resolve(__dirname, "node_modules/@fluentui/react-components"),
      "@fluentui/react-icons": path.resolve(__dirname, "node_modules/@fluentui/react-icons"),
    },
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
        // Single bundle for web resource deployment
        manualChunks: undefined,
      },
    },
  },
  // Base path for Dataverse webresource deployment
  base: "./",
});
