import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';
import { fileURLToPath } from 'url';
import fs from 'fs';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

/**
 * Custom Vite plugin to resolve bare module imports from shared library
 * source files to THIS project's node_modules.
 *
 * Problem: @spaarke/ui-components is aliased to a source directory outside
 * the project root. When Vite processes those files, bare imports
 * (e.g., "@fluentui/react-components") fail because Vite walks upward from
 * the importer's directory and never reaches this project's node_modules.
 *
 * Solution: Intercept resolution requests and redirect to this project's
 * node_modules directory, then let Vite's normal resolution handle the rest.
 */
function resolveSharedLibDeps(): import('vite').Plugin {
  const sharedLibPaths = [path.resolve(__dirname, '../shared/Spaarke.UI.Components/src')].map(p =>
    p.replace(/\\/g, '/')
  );

  const nodeModulesDir = path.resolve(__dirname, 'node_modules');

  return {
    name: 'resolve-shared-lib-deps',
    enforce: 'pre',
    async resolveId(source, importer, options) {
      if (!importer) return null;
      const normalizedImporter = importer.replace(/\\/g, '/');
      const isSharedLib = sharedLibPaths.some(p => normalizedImporter.startsWith(p));
      if (!isSharedLib) return null;
      // Only intercept bare module imports
      if (source.startsWith('.') || source.startsWith('/')) return null;
      if (source.startsWith('@spaarke/')) return null;

      // Extract the package name (handle scoped packages like @fluentui/react-button)
      let pkgName: string;
      if (source.startsWith('@')) {
        const parts = source.split('/');
        pkgName = parts.slice(0, 2).join('/');
      } else {
        pkgName = source.split('/')[0];
      }

      // Check if the package exists in our node_modules
      const pkgDir = path.join(nodeModulesDir, pkgName);
      if (!fs.existsSync(pkgDir)) return null;

      // Re-resolve this import as if it came from a file in THIS project
      const fakeImporter = path.join(__dirname, '__virtual_importer__.ts');
      const result = await this.resolve(source, fakeImporter, {
        ...options,
        skipSelf: true,
      });
      return result;
    },
  };
}

export default defineConfig({
  plugins: [
    resolveSharedLibDeps(),
    react({
      // Include shared lib source for transpilation
      include: [
        'src/**/*.tsx',
        'src/**/*.ts',
        path.resolve(__dirname, '../shared/Spaarke.UI.Components/src/**/*.tsx'),
        path.resolve(__dirname, '../shared/Spaarke.UI.Components/src/**/*.ts'),
      ],
    }),
  ],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
      '@spaarke/ui-components': path.resolve(__dirname, '../shared/Spaarke.UI.Components/src'),
    },
    // Force deduplication for shared packages to avoid multiple React instances
    dedupe: [
      'react',
      'react-dom',
      'scheduler',
      '@fluentui/react-components',
      '@fluentui/react-icons',
      '@fluentui/react-context-selector',
    ],
  },
  build: {
    outDir: 'dist',
    sourcemap: false,
    minify: true,
    // IIFE format bundles the app into a single predictable JS asset (assets/app.js)
    // served alongside index.html from the SWA static origin.
    rollupOptions: {
      output: {
        format: 'iife',
        name: 'ExternalWorkspaceSPA',
        // Predictable filenames for code site deployment
        entryFileNames: 'assets/app.js',
        chunkFileNames: 'assets/[name].js',
        assetFileNames: 'assets/[name].[ext]',
      },
    },
  },
  // Development server. Proxies BFF API calls to avoid browser CORS restrictions.
  server: {
    port: 3000,
    proxy: {
      // Route BFF API calls through the dev server to avoid browser CORS restrictions.
      // When VITE_BFF_API_URL is empty, bffApiCall uses relative /api/... paths
      // which hit this proxy. Vite forwards server-to-server — no CORS needed.
      '/api': {
        target: 'https://spaarke-bff-dev.azurewebsites.net',
        changeOrigin: true,
        secure: true,
      },
    },
  },
  base: '/',
});
