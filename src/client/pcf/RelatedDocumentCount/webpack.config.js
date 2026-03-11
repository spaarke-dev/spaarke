// Custom webpack configuration for PCF
// Enables tree-shaking for @fluentui/react-icons to reduce bundle size
// Without this, the full icon library (~6.8MB) gets bundled

module.exports = {
  optimization: {
    usedExports: true,
    sideEffects: true,
    innerGraph: true,
    providedExports: true
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
