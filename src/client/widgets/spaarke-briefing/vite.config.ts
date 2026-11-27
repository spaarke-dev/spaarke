import { defineConfig, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';
import { viteSingleFile } from 'vite-plugin-singlefile';
import path from 'path';
import fs from 'fs';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// The shared library SOURCE, imported by deep path (ADR-012, design 9.1): the barrel would pull pdfjs-dist, mammoth and
// App Insights into the bundle. Shape follows the Decisions widget's config.
const sharedUiSrc = path.resolve(__dirname, '../../shared/Spaarke.UI.Components/src');

// The Console's Daily Briefing component package (task 010's seam), consumed as source.
const briefingSrc = path.resolve(__dirname, '../../shared/Spaarke.DailyBriefing.Components/src');

// The shared widget MCP transport (spaarkeai-mcp-bff-api-r2 task 021, FR-16): a source folder imported by relative path, not a package.
const sharedWidgetSrc = path.resolve(__dirname, '../shared');

// The build-time stand-ins for the two packages the briefing package imports by name (see the files for why).
const authShim = path.resolve(__dirname, 'src/shims/spaarkeAuth.ts');
const uiComponentsShim = path.resolve(__dirname, 'src/shims/uiComponents.ts');
const uiServicesShim = path.resolve(__dirname, 'src/shims/uiServices.ts');

/**
 * Resolves the bare imports of the shared sources (for example "@fluentui/react-components", "@modelcontextprotocol/ext-apps/react-with-deps")
 * from THIS package's node_modules, because that source lives outside the package root.
 */
function resolveSharedLibDeps(): Plugin {
  const sharedPaths = [sharedUiSrc, briefingSrc, sharedWidgetSrc].map(p => p.replace(/\\/g, '/') + '/');
  const nodeModulesDir = path.resolve(__dirname, 'node_modules');

  return {
    name: 'resolve-shared-lib-deps',
    enforce: 'pre',
    async resolveId(source, importer, options) {
      if (!importer) return null;
      const from = importer.replace(/\\/g, '/');
      if (!sharedPaths.some(p => from.startsWith(p))) return null;
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
    react({
      include: [
        'src/**/*.tsx',
        'src/**/*.ts',
        path.join(sharedUiSrc, '**/*.{ts,tsx}'),
        path.join(briefingSrc, '**/*.{ts,tsx}'),
        path.join(sharedWidgetSrc, '**/*.{ts,tsx}'),
      ],
    }),
    // All JS and CSS inline: the BFF serves ONE html file as the MCP resource (FR-12).
    viteSingleFile(),
  ],
  resolve: {
    // Order matters: the exact package names first, then the deep paths.
    alias: [
      { find: /^@\//, replacement: path.resolve(__dirname, './src') + '/' },
      { find: /^@spaarke\/auth$/, replacement: authShim },
      { find: /^@spaarke\/ui-components$/, replacement: uiComponentsShim },
      { find: /^@spaarke\/ui-components\/services$/, replacement: uiServicesShim },
      { find: /^@spaarke\/ui-components\/(.*)$/, replacement: `${sharedUiSrc}/$1` },
      { find: /^@spaarke\/daily-briefing-components$/, replacement: path.join(briefingSrc, 'index.ts') },
      { find: /^@spaarke\/daily-briefing-components\/(.*)$/, replacement: `${briefingSrc}/$1` },
      {
        find: /^@fluentui\/react-components$/,
        replacement: path.resolve(__dirname, 'node_modules/@fluentui/react-components'),
      },
    ],
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
