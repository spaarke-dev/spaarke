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
      // task 041 — RecentTab deep-imports navItemRepository (DEF-10 tree-shaking
      // convention already used by Notepad's useSprkMemoRepository.ts for
      // PolymorphicResolverService). Without a package.json "exports" map on
      // @spaarke/ui-components, Node/Vite's default subpath resolution can't
      // find `services/navigator/navItemRepository` (it only lives under
      // `dist/`, not the package root). Aliased to `dist/services` (NOT
      // source, unlike Notepad's resolveSharedLibDeps approach) so
      // NavigatorPane keeps consuming the pre-built shared-lib bundle per this
      // file's original design note — matches jest.config.cjs's moduleNameMapper
      // and tsconfig.json's paths entry for the same subpath.
      "@spaarke/ui-components/services": path.resolve(
        __dirname,
        "../../client/shared/Spaarke.UI.Components/dist/services"
      ),
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
