import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { viteSingleFile } from 'vite-plugin-singlefile';

/**
 * ADR-026: Full-page Custom Page standard.
 *   - React 19, bundled (NOT PCF, NOT platform-library).
 *   - `vite-plugin-singlefile` inlines all JS/CSS into ONE self-contained HTML
 *     file — the only Dataverse-web-resource-deployable shape (no CDN, no
 *     separate .js/.css). The produced `out/index.html` has zero external asset
 *     URLs. `build:prod` copies it to `out/sprk_communicationpage.html`.
 *   - `dedupe` forces a single React instance so hooks/context from the shared
 *     lib (`@spaarke/ui-components`) resolve against the same React the page
 *     bundles (the classic "two React copies" failure the exemplar webpack
 *     config guarded against).
 */
export default defineConfig({
  plugins: [react(), viteSingleFile()],
  resolve: {
    dedupe: ['react', 'react-dom'],
  },
  build: {
    outDir: 'out',
    emptyOutDir: true,
    target: 'es2020',
    // Belt-and-suspenders with viteSingleFile: keep everything in one chunk.
    assetsInlineLimit: 100_000_000,
    cssCodeSplit: false,
    rollupOptions: {
      output: {
        inlineDynamicImports: true,
      },
    },
  },
});
