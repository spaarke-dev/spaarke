/**
 * Custom webpack config merged on top of pcf-scripts defaults.
 *
 * Copied unchanged from `CommunicationTimelineRegarding/webpack.config.js`
 * (task 021 lineage) — same React 16.14 / `@fluentui/react-icons` jsx-runtime
 * resolution issue applies here. See that file's header comment for the full
 * rationale of each piece.
 *
 * `pdfjs-dist$` / `mammoth$: false` added by task 092 (master build/test
 * baseline repair, 2026-10-04) — stubs the heavy `useChatFileAttachment.ts`
 * dynamic imports that `@spaarke/ui-components`'s root barrel transitively
 * drags into this PCF's single-chunk bundle (NFR-12 dynamic import is not
 * lazy under `LimitChunkCountPlugin({ maxChunks: 1 })`); the unstubbed
 * `pdfjs-dist/build/pdf.mjs` crashes `@babel/plugin-transform-optional-chaining`
 * because pcf-scripts' babel-loader has no `node_modules` exclude. Full
 * rationale: `CommunicationActions/webpack.config.js`.
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
