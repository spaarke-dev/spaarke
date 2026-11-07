import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { viteSingleFile } from 'vite-plugin-singlefile';

// SPIKE: one self-contained HTML file (same plugin the Spaarke code pages use), no network origins.
export default defineConfig({
  plugins: [react(), viteSingleFile()],
  build: {
    outDir: 'dist',
    sourcemap: false,
    assetsInlineLimit: 100000000,
    minify: true,
    rollupOptions: { output: { manualChunks: undefined } },
  },
  base: './',
});
