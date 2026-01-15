// Custom webpack configuration for PlaybookBuilderHost PCF
// This file is merged with pcf-scripts default config when pcfAllowCustomWebpack is enabled

module.exports = {
  optimization: {
    // Enable aggressive tree-shaking
    usedExports: true,
    sideEffects: true,
    innerGraph: true,
    providedExports: true
  },
  // Override module rules to handle @fluentui/react-icons more efficiently
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
