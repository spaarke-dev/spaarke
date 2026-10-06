/**
 * Custom webpack config merged on top of pcf-scripts defaults.
 *
 * Mirrors RegardingResolver/webpack.config.js. Why each piece exists:
 *
 *   1. `resolve.alias` — `@griffel/react` (pulled in by `@fluentui/react-icons`)
 *      imports `react/jsx-runtime` as a BARE specifier. React 16.14 ships the
 *      file at `node_modules/react/jsx-runtime.js`, but the requesting module
 *      declares `"type": "module"` so webpack 5 treats the request as fully-
 *      specified and rejects the extensionless path. The exact-match aliases
 *      below short-circuit the resolver and point to the actual `.js` file.
 *
 *   2. `module.rules[0]` — Backstop: relax fully-specified resolution for any
 *      `.m?js` file in `node_modules` so other ESM packages resolve cleanly.
 *
 *   3. `module.rules[1]` — Tree-shaking marker for `@fluentui/react-icons` so
 *      the bundle doesn't pull in the ~6.8MB icon set.
 *
 *   4. `@spaarke/sdap-client$: false` — the shared lib's services barrel
 *      re-exports EntityCreationService (imports @spaarke/sdap-client); this
 *      PCF only uses TODO_REGARDING_CATALOG + applyResolverFields +
 *      buildRecordUrl. Stub the unused package so webpack tree-shakes the
 *      dead import path (same workaround as RegardingResolver).
 *
 *   5. `pdfjs-dist$` / `mammoth$: false` (task 092, master build/test baseline
 *      repair, 2026-10-04) — `@spaarke/ui-components`'s root barrel does
 *      `export * from './SprkChat'` in the same `components/index.ts` module
 *      as everything else this PCF imports from the package, so webpack's
 *      single-chunk build (`LimitChunkCountPlugin({ maxChunks: 1 })`) must
 *      still resolve + babel-transform `useChatFileAttachment.ts`'s
 *      `import('pdfjs-dist')` / `import('mammoth')` (NFR-12 dynamic imports
 *      are NOT lazy under maxChunks:1) even though this PCF never calls that
 *      code path. The pre-built `pdfjs-dist/build/pdf.mjs` crashes
 *      `@babel/plugin-transform-optional-chaining` (`Cannot read properties
 *      of null (reading 'declarations')` in `Scope.push`) because
 *      pcf-scripts' babel-loader rule has no `node_modules` exclude. Stub
 *      both the same way `@spaarke/sdap-client` is stubbed above. Full
 *      rationale: `CommunicationActions/webpack.config.js`.
 */
const path = require('path');

module.exports = {
  optimization: {
    usedExports: true,
    sideEffects: true,
    innerGraph: true,
    providedExports: true,
  },
  resolve: {
    alias: {
      'react/jsx-runtime$': path.resolve(__dirname, 'node_modules/react/jsx-runtime.js'),
      'react/jsx-dev-runtime$': path.resolve(__dirname, 'node_modules/react/jsx-dev-runtime.js'),
      '@spaarke/sdap-client$': false,
      // v1.7.0 (UAC-r2 task 147 r1): the shared lib's root barrel reaches SprkChat's lazy `import('pdfjs-dist')`, whose
      // ESM build the PCF toolchain's babel cannot parse. This control never previews a PDF. Master's build repair
      // (#1123) replaced the silent `false` stub with loud ones — see pdfjsDistUnreachable.js for why.
      'pdfjs-dist$': path.resolve(__dirname, '../shared/stubs/pdfjsDistUnreachable.js'),
      'mammoth$': path.resolve(__dirname, '../shared/stubs/mammothUnreachable.js'),
    },
  },
  module: {
    rules: [
      {
        test: /\.m?js$/,
        resolve: { fullySpecified: false },
      },
      {
        test: /[\\/]node_modules[\\/]@fluentui[\\/]react-icons[\\/]/,
        sideEffects: false,
      },
    ],
  },
};
