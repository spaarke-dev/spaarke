// Custom webpack configuration for PCF
// Enables tree-shaking for @fluentui/react-icons to reduce bundle size
// Without this, the full icon library (~6.8MB) gets bundled.
//
// Matches SemanticSearchControl / DocumentRelationshipViewer reference PCFs.
// Combined with featureconfig.json:
//   { "pcfReactPlatformLibraries": "on", "pcfAllowCustomWebpack": "on" }
// and <platform-library> entries in ControlManifest.Input.xml, this drives
// bundle size from ~1.6 MB down to the expected ~400-600 KB range per the
// pcf-deploy skill's Bundle Size & Production Mode section.

module.exports = {
  optimization: {
    usedExports: true,
    sideEffects: true,
    innerGraph: true,
    providedExports: true,
  },
  module: {
    rules: [
      {
        // Mark @fluentui/react-icons as side-effect-free for tree-shaking
        test: /[\\/]node_modules[\\/]@fluentui[\\/]react-icons[\\/]/,
        sideEffects: false,
      },
    ],
  },
};
