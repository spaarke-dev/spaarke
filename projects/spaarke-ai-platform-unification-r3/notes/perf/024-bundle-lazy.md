# Task 024 — Bundle Lazy-Load Verification

> **Status**: Source-level verification complete. Full bundle-analyzer verification deferred to task 061 (after task 025 wires the hook into SprkChat / SpaarkeAi).
> **Date**: 2026-05-20
> **Owner**: task 024 (`useChatFileAttachment` hook)

---

## Source-level guarantee (verified now)

The hook at `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatFileAttachment.ts` references `pdfjs-dist` and `mammoth` exclusively via dynamic `import(...)` expressions inside the `ensurePdfJs` / `ensureMammoth` memoized callbacks. No top-level `import ... from 'pdfjs-dist'` or `import ... from 'mammoth'` statement exists.

Verification regex (run against the hook source):

```
^\s*import\s+[^;]*from\s+['"]pdfjs-dist['"]   → 0 matches
^\s*import\s+[^;]*from\s+['"]mammoth['"]     → 0 matches
import('pdfjs-dist')                          → 1 match
import('mammoth')                             → 1 match
```

This is also encoded as the final unit test in `useChatFileAttachment.test.ts` under the `NFR-12 lazy-load guarantee (source-level)` describe block, providing a CI guardrail against regressions.

---

## Bundle-analyzer verification (deferred to task 061)

Reason for deferral: the hook is not yet imported by any production consumer in this branch. Tasks 025 (toolbar restructure) and 026 (payload wiring) introduce the consumer paths in SpaarkeAi. Until then, `npm run build:prod` on SpaarkeAi will not produce a chunk containing the hook + its dynamic imports.

When tasks 025 + 026 land, task 061 will verify by:

1. `npm run build:prod` (or `build` for Vite) on `src/solutions/SpaarkeAi/`
2. Inspect `dist/assets/` filenames — pdfjs/mammoth MUST appear in separate chunk filenames (e.g., `pdfjs-dist-[hash].js`, `mammoth-[hash].js`) and NOT inside the main `index-[hash].js` chunk
3. Check gzipped size delta vs R2 baseline is < 250 KB for the main chunk (NFR-12)

If at that point pdfjs/mammoth show up in the main chunk, the regression has occurred either (a) via an accidental static import in a consumer file, or (b) via a Vite/Rollup chunking config issue. Mitigation: enforce `manualChunks` in `vite.config.ts` for those two libs.

---

## Lib versions added

| Package | Version (^ ranges) | Notes |
|---|---|---|
| `pdfjs-dist` | `^5.7.284` | Modern API (`getDocument({ data: buffer })`, `numPages`, `getPage()`, `getTextContent()`) |
| `mammoth` | `^1.12.0` | Browser build exports `extractRawText({ arrayBuffer })` |

Both added as `dependencies` (not `devDependencies`) of `@spaarke/ui-components` per task 024 instructions.

---

## Hook surface (for consumer reference)

```ts
import {
  useChatFileAttachment,
  type ChatAttachment,
  type AttachmentChip,
  type AttachmentError,
} from '@spaarke/ui-components';

const {
  files,        // AttachmentChip[]   — toolbar strip
  attachments,  // ChatAttachment[]   — outbound payload
  errors,       // AttachmentError[]  — UI-actionable rejections
  addFiles,     // (FileList | File[]) => Promise<void>
  removeFile,   // (index: number) => void
  clearAll,     // () => void
} = useChatFileAttachment({
  onExtractionError: (filename, mimeType, sizeBytes, error) => {
    logTelemetryError(TELEMETRY_FILE_EXTRACTION_FAILURE, {
      filename, mimeType, sizeBytes, errorMessage: error.message,
    });
  },
});
```

The optional `onExtractionError` callback is the FR-24 / OC-09 telemetry escape hatch and avoids cross-package coupling (`@spaarke/ui-components` does NOT import `errorTelemetry` from SpaarkeAi).

---

## Sign-off

- ✅ Source-level lazy-import verified (regex + unit test)
- ✅ TypeScript compile clean (`npm run build` passes with zero errors)
- ⏳ Bundle-analyzer verification deferred to task 061 (after consumer wiring lands)
