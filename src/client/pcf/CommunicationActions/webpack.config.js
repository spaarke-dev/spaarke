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
