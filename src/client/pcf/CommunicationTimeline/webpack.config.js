/**
 * Custom webpack config merged on top of pcf-scripts defaults.
 *
 * Copied unchanged from `CommunicationMessageActions/webpack.config.js` (task 062,
 * itself copied from `CommunicationActions/webpack.config.js`, task 044) — same
 * React 16.14 / `@fluentui/react-icons` jsx-runtime resolution issue applies here.
 * See that file's header comment for the full rationale of each piece.
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
