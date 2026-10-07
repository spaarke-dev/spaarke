'use strict';

/**
 * Loud stub for `mammoth` (task 092, master build/test baseline repair, 2026-10-04).
 *
 * Sibling of `pdfjsDistUnreachable.js` — see that file for the full rationale.
 * `mammoth` is the DOCX counterpart of the same `useChatFileAttachment.ts`
 * dynamic-import pair (`import('pdfjs-dist')` / `import('mammoth')`), pulled
 * into these PCFs' single-chunk bundles for the same reason (the
 * `@spaarke/ui-components` root barrel co-locates `SprkChat` with everything
 * these PCFs actually import). `mammoth` itself does not crash babel the way
 * `pdfjs-dist` does, but it is stubbed for the same dead-weight + silent-vs-loud
 * reasons: `useChatFileAttachment()` is called from exactly one production
 * file (`SprkChat.tsx`), which none of these PCFs render.
 *
 * Throws (rather than aliasing to `false`) so that if this is ever reached —
 * a future change starts rendering `<SprkChat>` here without updating this
 * alias — the failure surfaces LOUDLY through `useChatFileAttachment`'s
 * existing `extraction-failed` / `Attachment.ExtractionFailure` error path
 * (FR-24/OC-09), not as a silent no-op.
 *
 * If you are reading this because that error fired: remove the `mammoth$`
 * resolve.alias entry from this PCF's webpack.config.js and add the real
 * `mammoth` dependency explicitly if this PCF now needs it.
 */
throw new Error(
  '[task-092-stub] mammoth is stubbed out in this PCF (see ' +
    'src/client/pcf/shared/stubs/mammothUnreachable.js) because no code path ' +
    'here was expected to need it. A DOCX-attachment extraction path was reached ' +
    'that should not exist in this bundle — remove the `mammoth$` alias in ' +
    'webpack.config.js and add the real dependency if this PCF now needs it.'
);
