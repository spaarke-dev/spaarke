/**
 * Custom webpack config merged on top of pcf-scripts defaults.
 *
 * Two reasons it exists:
 *
 *   1. `@griffel/react` (pulled in by `@fluentui/react-icons` ≥ 2.0.300)
 *      declares `"type": "module"` and uses bare-specifier imports like
 *      `react/jsx-runtime`. With webpack 5's strict ESM resolution that
 *      fails with `BREAKING CHANGE: ... only because it was resolved as
 *      fully specified`. The `module.rules` entry below relaxes that for
 *      `.mjs` and `.js` files inside `node_modules` so the bareSpecifier
 *      resolves to `react/jsx-runtime.js` as expected.
 *
 *   2. Enables tree-shaking for `@fluentui/react-icons` so the bundle
 *      doesn't pull in the full ~6.8MB icon set (mirrors the
 *      SemanticSearchControl pattern).
 */
const path = require('path');

/**
 * Custom webpack config merged on top of pcf-scripts defaults.
 *
 * Why each piece exists:
 *
 *   1. `resolve.alias` — `@griffel/react` (pulled in by `@fluentui/react-icons`)
 *      imports `react/jsx-runtime` as a BARE specifier. React 16.14 ships the
 *      file at `node_modules/react/jsx-runtime.js`, but the requesting module
 *      declares `"type": "module"` so webpack 5 treats the request as fully-
 *      specified and rejects the extensionless path. The exact-match aliases
 *      below short-circuit the resolver and point to the actual `.js` file.
 *
 *   2. `module.rules[0]` — Backstop: relax fully-specified resolution for any
 *      `.m?js` file in `node_modules` so other ESM packages in the same
 *      situation (newer Griffel deps, Fluent v9 sub-packages) resolve cleanly.
 *
 *   3. `module.rules[1]` — Tree-shaking marker for `@fluentui/react-icons` so
 *      the bundle doesn't pull in the ~6.8MB icon set (mirrors the
 *      SemanticSearchControl pattern).
 */
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
      // PR #369 cascade workaround (project-wide fix tracked in task 092):
      // `@spaarke/ui-components/dist/services/index.js` re-exports
      // `EntityCreationService` which imports `@spaarke/sdap-client`. The
      // RegardingResolver PCF doesn't actually use EntityCreationService —
      // it only imports `TODO_REGARDING_CATALOG`, `applyResolverFields`, and
      // `buildRecordUrl`. Stub the unused package so webpack tree-shakes the
      // dead import path.
      '@spaarke/sdap-client$': false,
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
