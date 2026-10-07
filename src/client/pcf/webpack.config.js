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
 * SCOPE (ORIGINAL): applied ONLY to TrackingFieldTrio (the sole control that
 * bundled a React-subpath importer at the time). Every other control received
 * an empty config — this file was a strict no-op for the other 16 controls.
 *
 * EXTENDED (task 092, master build/test baseline repair, 2026-10-04) —
 * UpdateRelatedButton: a FLAT-structure control (its `ControlManifest.Input.xml`
 * sits directly at the control's root, unlike nested controls such as
 * VisualHost/ScopeConfigEditor/the Communication PCFs, whose manifest is one
 * folder deeper). `pcf-scripts`' custom-webpack discovery always resolves
 * `<controlPath>/../webpack.config.js` — for a NESTED control that lands back
 * on the control's own root (where each of those controls keeps its own
 * `webpack.config.js`); for a FLAT control, `controlPath` IS the control's own
 * root, so `..` lands here, on this SHARED file, instead. (Confirmed
 * empirically: a `webpack.config.js` placed directly inside
 * `UpdateRelatedButton/` was silently never read — no error, the original
 * crash just persisted unchanged, the exact same class of silent-no-op this
 * file's own header already documents for the `pcfAllowCustomWebpack` gate.)
 *
 * UpdateRelatedButton's `index.ts` does
 * `import { resolveThemeWithUserPreference } from '@spaarke/ui-components'`
 * (the bare barrel specifier), pulling `useChatFileAttachment.ts`'s
 * `import('pdfjs-dist')` / `import('mammoth')` into its single-chunk bundle —
 * the same root cause as the other 8 PCFs fixed by task 092 (see
 * `../CommunicationActions/webpack.config.js` for the full writeup).
 * UpdateRelatedButton never calls the chat-attachment extraction path, so stub
 * both packages the same way.
 *
 * SCOPE (CURRENT): this file now branches on TWO control names
 * (TrackingFieldTrio, UpdateRelatedButton), each getting its own distinct
 * config; every other control still receives an empty config — a strict
 * no-op. If another FLAT control later needs a custom webpack tweak, add its
 * name as a new branch below (do not reuse an existing branch's config for a
 * different control unless the need is identical).
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
} else if (controlName === 'UpdateRelatedButton') {
  module.exports = {
    resolve: {
      alias: {
        // Loud stubs, not `false` — see ../shared/stubs/pdfjsDistUnreachable.js for why.
        'pdfjs-dist$': path.resolve(__dirname, 'shared/stubs/pdfjsDistUnreachable.js'),
        'mammoth$': path.resolve(__dirname, 'shared/stubs/mammothUnreachable.js'),
      },
    },
  };
} else {
  module.exports = {};
}
