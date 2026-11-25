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
 *   (A fourth item, a `@spaarke/sdap-client$: false` stub, was removed on
 *   2026-11-24: that client moved into @spaarke/ui-components itself, so
 *   there is no separate package left to stub.)
 *
 *   4. `pdfjs-dist$` / `mammoth$: false` (task 092, master build/test baseline
 *      repair, 2026-10-04) — `@spaarke/ui-components`'s root barrel does
 *      `export * from './SprkChat'` alongside `export * from './EmailComposer'`
 *      in the SAME `components/index.ts` module, so ANY import from
 *      `@spaarke/ui-components` (this PCF imports `SendEmailPage` etc.) makes
 *      webpack discover `useChatFileAttachment.ts`'s `import('pdfjs-dist')` /
 *      `import('mammoth')` (NFR-12 dynamic imports) as part of the SAME module
 *      graph — PCF's single-chunk webpack config (`LimitChunkCountPlugin({
 *      maxChunks: 1 })`, required because the PCF runtime cannot load split
 *      chunks) means "dynamic" doesn't mean "lazy" here; webpack must still
 *      resolve + babel-transform `pdfjs-dist/build/pdf.mjs` into the one
 *      bundle. That pre-built, already-ES2020+ vendor file crashes
 *      `@babel/plugin-transform-optional-chaining` (`TypeError: Cannot read
 *      properties of null (reading 'declarations')` in `Scope.push`) because
 *      pcf-scripts' babel-loader rule (`test: /\.(jsx?|mjsx?)$/, use:
 *      [babelLoader]`) has no `node_modules` exclude, so it tries to
 *      re-transpile vendor ESM that was never meant to be re-transpiled. This
 *      PCF never calls the chat-attachment extraction path, so stub both
 *      packages (the way an unused `@spaarke/sdap-client` once was) —
 *      smallest surface, no change to `@spaarke/ui-components` public exports,
 *      no change to any consumer's import statements.
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
      // Loud stubs, not `false` — see pdfjsDistUnreachable.js for why.
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
