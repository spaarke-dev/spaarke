/**
 * Custom webpack config for PCF controls.
 *
 * pcf-scripts loads this file (path `<controlPath>/../webpack.config.js`, i.e.
 * shared across every `src/client/pcf/*` control) whenever the building
 * control's `featureconfig.json` sets `pcfAllowCustomWebpack: "on"`, and
 * `webpack-merge`s it into the generated pcf-scripts config.
 *
 * WHY (task 073 UAT #1 — TrackingFieldTrio email-icon crash, minified React #31):
 * TrackingFieldTrio bundles the shared `EmailComposer` → `RichTextEditor`, which
 * uses Lexical. Lexical imports the React SUBPATH `react/jsx-runtime` (the
 * automatic JSX runtime). pcf-scripts' platform-library externalization only
 * exact-matches the BARE specifiers `react` / `react-dom` — it does NOT
 * externalize React subpaths — so webpack bundles `react/jsx-runtime` by
 * resolving it from the nearest node_modules. Because Lexical lives under
 * `@spaarke/ui-components`, that nearest copy is the shared library's co-located
 * React 19, whose elements are marked with `Symbol.for('react.transitional.element')`.
 * The PCF platform React 16.14 reconciler tests for `Symbol.for('react.element')`,
 * so the element fails `isValidElement` and is rendered as a raw object →
 * minified React error #31 ("Objects are not valid as a React child … object with
 * keys {$$typeof,type,key,ref,props}"), which blanks the whole control the moment
 * the email dialog (SendEmailDialog → EmailComposer) mounts.
 *
 * FIX: redirect ONLY the leaking `react/jsx-runtime` (+ dev variant) subpaths to
 * the control's OWN React 16.14 copy, so they resolve to a runtime whose element
 * symbol matches the platform reconciler. Bare `react` / `react-dom` are
 * untouched — they stay platform-externalized (the externals matcher runs on the
 * request string before resolution) — and `react-dom/client` is deliberately NOT
 * aliased (it does not exist in React 16 and is not on the embedded RichTextEditor
 * code path). React 16.14 ships `jsx-runtime.js` / `jsx-dev-runtime.js` at its
 * package root (they were backported in 16.14 for the new JSX transform), so this
 * alias always has a valid, symbol-compatible target.
 *
 * SCOPE: applied ONLY to TrackingFieldTrio (the sole control that bundles a
 * React-subpath importer today). Every other control receives an empty config —
 * this file is a strict no-op for the other 16 controls. If another control later
 * bundles a dependency that imports React subpaths, add its name below.
 */
const path = require('path');

const controlName = path.basename(process.cwd());

if (controlName === 'TrackingFieldTrio') {
  const reactDir = path.dirname(require.resolve('react/package.json', { paths: [process.cwd()] }));
  module.exports = {
    resolve: {
      alias: {
        'react/jsx-runtime': path.join(reactDir, 'jsx-runtime.js'),
        'react/jsx-dev-runtime': path.join(reactDir, 'jsx-dev-runtime.js'),
      },
    },
  };
} else {
  module.exports = {};
}
