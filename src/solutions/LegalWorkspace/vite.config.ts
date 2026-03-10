import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";
import path from "path";

export default defineConfig({
  plugins: [
    react({
      // Include shared lib source for transpilation
      include: [
        "src/**/*.tsx",
        "src/**/*.ts",
        path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src/**/*.tsx"),
        path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src/**/*.ts"),
      ],
    }),
    viteSingleFile(),
  ],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
      "@spaarke/ui-components": path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src"),
      "@spaarke/auth": path.resolve(__dirname, "../../client/shared/Spaarke.Auth/src"),
    },
    // Force all shared packages to resolve from THIS project's node_modules
    dedupe: [
      "react",
      "react-dom",
      "scheduler",
      "@fluentui/react-components",
      "@fluentui/react-icons",
      "@fluentui/react-context-selector",
      "@lexical/react",
      "lexical",
    ],
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
