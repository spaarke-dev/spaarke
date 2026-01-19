// Custom webpack configuration for PCF
// Enables tree-shaking for @fluentui/react-icons
module.exports = {
  mode: 'production',
  optimization: {
    usedExports: true,
    sideEffects: true,
    innerGraph: true,
    providedExports: true,
    minimize: true
  },
  module: {
    rules: [
      {
        // Mark @fluentui/react-icons as side-effect-free
        test: /[\\/]node_modules[\\/]@fluentui[\\/]react-icons[\\/]/,
        sideEffects: false
      }
    ]
  }
};
