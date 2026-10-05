/**
 * Custom webpack config merged on top of pcf-scripts defaults.
 *
 * Added by task 092 (master build/test baseline repair) on 2026-10-04, as a
 * direct follow-up to the same fix already applied to the 6 Communication
 * PCFs (see e.g. `../CommunicationActions/webpack.config.js` for the full
 * root-cause writeup). VisualHost did NOT need this file before: it had no
 * `webpack.config.js` at all, and `npm run build:prod` succeeded cleanly
 * against this task's original branch point.
 *
 * It needs one now because PR #1121 ("C-7 cleanGuid convergence", merged to
 * master as ec7211aaf AFTER this task's branch point) changed VisualHost's
 * `VisualHostRoot.tsx`, `DueDateCardList.tsx`, `ChartRenderer.tsx`,
 * `CalendarVisual.tsx`, and `DueDateCard.tsx` from narrow relative imports of
 * a local `cleanGuid` helper to `import { cleanGuid } from
 * '@spaarke/ui-components'` — the bare barrel specifier. That barrel's
 * `components/index.ts` does `export * from './SprkChat'` alongside
 * `export * from './EmailComposer'`, so ANY import from the package root
 * pulls `useChatFileAttachment.ts`'s `import('pdfjs-dist')` /
 * `import('mammoth')` (NFR-12 dynamic imports) into the SAME module graph.
 * PCF's single-chunk webpack config (`LimitChunkCountPlugin({maxChunks: 1})`)
 * means "dynamic" doesn't mean "lazy" here, so webpack must still resolve +
 * babel-transform `pdfjs-dist/build/pdf.mjs` into the one bundle — and that
 * pre-built, already-ES2020+ vendor file crashes
 * `@babel/plugin-transform-optional-chaining` (`TypeError: Cannot read
 * properties of null (reading 'declarations')` in `Scope.push`) because
 * pcf-scripts' babel-loader rule has no `node_modules` exclude.
 *
 * VisualHost never calls the chat-attachment extraction path, so stub both
 * packages the same way the 6 Communication PCFs do — smallest surface, no
 * change to `@spaarke/ui-components` public exports, no change to any
 * consumer's import statements. Confirmed via a synchronous local
 * `npm run build:prod` that, pre-fix, this reproduces the identical
 * `ERROR in .../pdfjs-dist/build/pdf.mjs` / `Scope.push` crash (webpack exits
 * 0 regardless — see task-092 PR body for the `pcf-scripts` exit-code gap).
 *
 * Unlike the Communication PCFs, VisualHost uses React 18 and needs none of
 * their `react/jsx-runtime` aliasing or `@spaarke/sdap-client` stub — the
 * pre-fix build here produced exactly ONE error (the pdf.mjs one), so this
 * file stays minimal.
 */
const path = require('path');

module.exports = {
  resolve: {
    alias: {
      // Loud stubs, not `false` — see pdfjsDistUnreachable.js for why.
      'pdfjs-dist$': path.resolve(__dirname, '../shared/stubs/pdfjsDistUnreachable.js'),
      'mammoth$': path.resolve(__dirname, '../shared/stubs/mammothUnreachable.js'),
    },
  },
};
