import { defineConfig, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';
import { viteSingleFile } from 'vite-plugin-singlefile';
import path from 'path';
import fs from 'fs';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// The shared library SOURCE, imported by deep path (ADR-012, design 9.1): the barrel would pull pdfjs-dist, mammoth and
// App Insights into the bundle. Shape follows src/solutions/CreateMatterWizard/vite.config.ts.
const sharedUiSrc = path.resolve(__dirname, '../../shared/Spaarke.UI.Components/src');

/**
 * Resolves the bare imports of the shared library's source files (for example "@fluentui/react-components") from THIS
 * package's node_modules, because that source lives outside the package root.
 */
function resolveSharedLibDeps(): Plugin {
  const sharedLibPath = sharedUiSrc.replace(/\\/g, '/');
  const nodeModulesDir = path.resolve(__dirname, 'node_modules');

  return {
    name: 'resolve-shared-lib-deps',
    enforce: 'pre',
    async resolveId(source, importer, options) {
      if (!importer) return null;
      if (!importer.replace(/\\/g, '/').startsWith(sharedLibPath)) return null;
      if (source.startsWith('.') || source.startsWith('/') || source.startsWith('@spaarke/')) return null;

      const pkgName = source.startsWith('@') ? source.split('/').slice(0, 2).join('/') : source.split('/')[0];
      if (!fs.existsSync(path.join(nodeModulesDir, pkgName))) return null;

      return this.resolve(source, path.join(__dirname, '__virtual_importer__.ts'), { ...options, skipSelf: true });
    },
  };
}

export default defineConfig({
  plugins: [
    resolveSharedLibDeps(),
    react({ include: ['src/**/*.tsx', 'src/**/*.ts', path.join(sharedUiSrc, '**/*.{ts,tsx}')] }),
    // All JS and CSS inline: the BFF serves ONE html file as the MCP resource (FR-12).
    viteSingleFile(),
  ],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
      '@spaarke/ui-components': sharedUiSrc,
      '@fluentui/react-components': path.resolve(__dirname, 'node_modules/@fluentui/react-components'),
      // Reached only through the type graph today (EntityCreationService); aliased so a future runtime import resolves.
      '@spaarke/sdap-client': path.resolve(__dirname, '../../shared/Spaarke.SdapClient/src/index.ts'),
    },
    // One React for the widget and the shared source (react-versioning constraint).
    dedupe: ['react', 'react-dom'],
  },
  build: {
    outDir: 'dist',
    sourcemap: false,
    assetsInlineLimit: 100000000,
    minify: true,
    // No modulepreload polyfill: it carries a fetch() call, and a single-file page has no preload links to fetch.
    modulePreload: false,
    rollupOptions: { output: { manualChunks: undefined } },
  },
  base: './',
});
