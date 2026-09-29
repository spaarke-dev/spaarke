// Custom webpack configuration for PCF
// Enables tree-shaking for @fluentui/react-icons to reduce bundle size.
// Without this, the full icon library (~6.8MB) gets bundled.
//
// Matches SemanticSearchControl / DocumentRelationshipViewer / MatterHeader —
// the established repo pattern. Combined with featureconfig.json:
//   { "pcfReactPlatformLibraries": "on", "pcfAllowCustomWebpack": "on" }
// and the <platform-library> entries in ControlManifest.Input.xml.
//
// ⛔ DO NOT add a custom `externals` block mapping granular @fluentui/* packages
// onto the platform Fluent global. It was tried in v1.1.0/v1.1.1 to cut the
// date-picker's bundle cost and it FAILS AT RUNTIME with
// "Minified React error #31" once real Fluent components mount: it splits
// Fluent's internals between the host copy (react-input/field/popover) and the
// bundled copy (react-utilities slot machinery, jsx-runtime, @griffel/react),
// and a slot object crossing that boundary is not a valid React child.
// The build succeeds and static symbol checks pass — neither proves runtime.
// See notes/decisions/033-nfr02-externals-runtime-failure.md.
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
        test: /[\/]node_modules[\/]@fluentui[\/]react-icons[\/]/,
        sideEffects: false,
      },
    ],
  },
};
