// Custom webpack configuration for PCF
// Enables tree-shaking for @fluentui/react-icons to reduce bundle size
// Without this, the full icon library (~6.8MB) gets bundled
//
// `resolve.alias` (ontology task 183, fix round 1): the manifest declares the platform React library (16.14.0), but
// `@spaarke/ui-components` is a `file:` dependency with its own node_modules, so the shared EmailComposer's body editor
// (Lexical) and `@griffel/react` import `react/jsx-runtime` and webpack resolved it from
// `Spaarke.UI.Components/node_modules/react` (React 19). That jsx-runtime creates elements tagged
// `Symbol.for('react.transitional.element')`, which React 16 rejects at render time ("Objects are not valid as a React
// child"). The exact-match aliases pin both runtimes to THIS PCF's React 16.14 (same fix as CommunicationActions).
// Guard: Spaarke.UI.Components/src/utils/__tests__/pcfBundleReactRuntime.guard.test.ts fails when a committed PCF
// bundle contains the React 19 marker.
const path = require('path');

module.exports = {
  optimization: {
    usedExports: true,
    sideEffects: true,
    innerGraph: true,
    providedExports: true
  },
  resolve: {
    alias: {
      'react/jsx-runtime$': path.resolve(__dirname, 'node_modules/react/jsx-runtime.js'),
      'react/jsx-dev-runtime$': path.resolve(__dirname, 'node_modules/react/jsx-dev-runtime.js')
    }
  },
  module: {
    rules: [
      {
        // Mark @fluentui/react-icons as side-effect-free for tree-shaking
        test: /[\\/]node_modules[\\/]@fluentui[\\/]react-icons[\\/]/,
        sideEffects: false
      }
    ]
  }
};
