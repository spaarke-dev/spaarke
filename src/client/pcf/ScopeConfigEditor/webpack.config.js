/**
 * Custom webpack config merged on top of pcf-scripts defaults.
 *
 * Added by task 092 (master build/test baseline repair) on 2026-10-04. Like
 * VisualHost (see `../VisualHost/webpack.config.js` for the fuller writeup),
 * ScopeConfigEditor had no `webpack.config.js` before this task — it is an
 * EIGHTH PCF affected by the same root cause as the original 6 Communication
 * PCFs, discovered only as a side effect of fixing this PCF's separate,
 * unrelated missing-`eslint`-devDependency failure (see `package.json`):
 * once `npm run build:prod` could get PAST the "Running ESLint" step, it
 * reached "Compiling and bundling control" and hit the identical crash.
 *
 * `ScopeConfigEditor/index.ts` does
 * `import { resolveThemeWithUserPreference } from '@spaarke/ui-components'`
 * — the bare barrel specifier. That barrel's `components/index.ts` does
 * `export * from './SprkChat'` alongside every other component, so this
 * import pulls `useChatFileAttachment.ts`'s `import('pdfjs-dist')` /
 * `import('mammoth')` (NFR-12 dynamic imports) into the SAME module graph.
 * PCF's single-chunk webpack config (`LimitChunkCountPlugin({maxChunks: 1})`)
 * means "dynamic" doesn't mean "lazy" here, so webpack must still resolve +
 * babel-transform `pdfjs-dist/build/pdf.mjs` into the one bundle — and that
 * pre-built, already-ES2020+ vendor file crashes
 * `@babel/plugin-transform-optional-chaining` (`TypeError: Cannot read
 * properties of null (reading 'declarations')` in `Scope.push`) because
 * pcf-scripts' babel-loader rule has no `node_modules` exclude.
 *
 * ScopeConfigEditor never calls the chat-attachment extraction path, so stub
 * both packages the same way the other 7 affected PCFs do — smallest
 * surface, no change to `@spaarke/ui-components` public exports, no change
 * to any consumer's import statements. Confirmed via a synchronous local
 * `npm run build:prod` that, pre-fix, this reproduces the identical
 * `ERROR in .../pdfjs-dist/build/pdf.mjs` / `Scope.push` crash.
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
