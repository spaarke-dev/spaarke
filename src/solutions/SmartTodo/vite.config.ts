import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";
import path from "path";

const sharedLibRoot = path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src");

export default defineConfig({
  plugins: [
    react(),
    viteSingleFile(),
  ],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
      "@spaarke/ui-components/PanelSplitter": path.resolve(sharedLibRoot, "components/PanelSplitter/index.ts"),
      "@spaarke/ui-components/hooks": path.resolve(sharedLibRoot, "hooks/useTwoPanelLayout.ts"),
      "@spaarke/ui-components/TodoDetail": path.resolve(sharedLibRoot, "components/TodoDetail/index.ts"),
      "@spaarke/ui-components/utils": path.resolve(sharedLibRoot, "utils/index.ts"),
      "@spaarke/ui-components": path.resolve(sharedLibRoot, "index.ts"),
    },
    // Ensure shared lib imports resolve from SmartTodo's node_modules
    dedupe: ["react", "react-dom", "@fluentui/react-components", "@fluentui/react-icons"],
  },
  // Allow Vite to resolve shared lib dependencies from SmartTodo's node_modules
  optimizeDeps: {
    include: ["@fluentui/react-components", "@fluentui/react-icons", "react", "react-dom"],
  },
  build: {
    outDir: "dist",
    sourcemap: false,
    assetsInlineLimit: 100000000,
    commonjsOptions: {
      include: [/node_modules/],
    },
    rollupOptions: {
      output: {
        manualChunks: undefined,
      },
    },
  },
  base: "./",
});
