'use strict';

/**
 * Loud stub for `pdfjs-dist` (task 092, master build/test baseline repair, 2026-10-04).
 *
 * Six Communication* PCFs alias `pdfjs-dist$` to THIS FILE in their
 * webpack.config.js instead of to `false`. Context: `@spaarke/ui-components`'s
 * root barrel does `export * from './SprkChat'` in the same `components/index.ts`
 * module as `EmailComposer` / `CommunicationTimeline` / etc. (what these PCFs
 * actually import), so webpack's single-chunk build
 * (`LimitChunkCountPlugin({ maxChunks: 1 })`) must still resolve
 * `useChatFileAttachment.ts`'s `import('pdfjs-dist')` even though NONE of
 * these PCFs render `<SprkChat>` — the ONLY call site of the hook that
 * triggers that import (confirmed by tracing: `useChatFileAttachment()` is
 * called from exactly one production file, `SprkChat.tsx`). The real
 * `pdfjs-dist/build/pdf.mjs` crashes `@babel/plugin-transform-optional-chaining`
 * under pcf-scripts' babel-loader (no `node_modules` exclude), so it cannot
 * simply be left unaliased.
 *
 * Aliasing to `false` (webpack's built-in empty-module convention) would make
 * this dead branch resolve SILENTLY to `{}` if it were ever reached — e.g. if
 * a future change starts rendering `<SprkChat>` in one of these PCFs without
 * updating this alias. Throwing here instead means: IF that ever happens, a
 * user who attaches a PDF gets a LOUD, immediate failure that flows through
 * `useChatFileAttachment`'s existing, designed error path (the `try/catch` in
 * `addFiles` around `ensurePdfJs()` turns this throw into an
 * `extraction-failed` `AttachmentError` + the `onExtractionError` /
 * `Attachment.ExtractionFailure` telemetry callback — FR-24/OC-09) — not a
 * confusing downstream `TypeError` or a swallowed no-op.
 *
 * If you are reading this because that error fired: remove the `pdfjs-dist$`
 * resolve.alias entry from this PCF's webpack.config.js so the real
 * `pdfjs-dist` dependency resolves, and add it as an explicit `dependency` in
 * package.json (it was only ever a transitive dependency of
 * `@spaarke/ui-components` before).
 */
throw new Error(
  '[task-092-stub] pdfjs-dist is stubbed out in this PCF (see ' +
    'src/client/pcf/shared/stubs/pdfjsDistUnreachable.js) because no code path ' +
    'here was expected to need it. A PDF-attachment extraction path was reached ' +
    'that should not exist in this bundle — remove the `pdfjs-dist$` alias in ' +
    'webpack.config.js and add the real dependency if this PCF now needs it.'
);
