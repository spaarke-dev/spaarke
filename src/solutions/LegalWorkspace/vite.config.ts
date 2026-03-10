import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";
import path from "path";

export default defineConfig({
  plugins: [
    react(),
    viteSingleFile(),
  ],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
      "@spaarke/auth": path.resolve(__dirname, "../../client/shared/Spaarke.Auth/src"),
      "@spaarke/ui-components": path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src"),
    },
    // Ensure shared library files resolve @fluentui/* and react from LegalWorkspace's node_modules
    dedupe: ["react", "react-dom", "@fluentui/react-components", "@fluentui/react-icons", "@azure/msal-browser"],
  },
  build: {
    outDir: "dist",
    sourcemap: false,
    assetsInlineLimit: 100000000,
    minify: true,
    rollupOptions: {
      output: {
        manualChunks: undefined,
      },
    },
  },
  base: "./",
});
