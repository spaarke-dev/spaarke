# Task 022 — Inline PDF/image preview on the Document record (FR-14, FR-15)

Executed by a sonnet @ high sub-agent, rigor FULL, steps mode directional. Not committed: the main session commits.

## What was built

- `src/client/external-spa/src/components/record/sections/DocumentPreview.tsx` (new). Placed next to the section, beside `InvoiceFilesList.tsx` (the other section helper), not under `components/detail/` (the POML predates task 005 and `RecordDetailModal.tsx` is deleted).
  - `previewKindOf(fileType, name)`: PDF, PNG, JPG/JPEG, GIF, WebP only. `sprk_filetype` decides, case-insensitive, leading dot ignored. The name's extension is used only when the type is blank or absent. A present, non-previewable type (DOCX) is final even when the name says `.pdf`. SVG is not previewable.
  - `DocumentPreview`: states loading, PDF (`iframe`, `title="Preview of {name}"`), image (`img`, `alt="Preview of {name}"`), download-only (other type), too-large, error, no-file. Stage styled as `PreviewModal`'s `previewStage` (neutral background 3, centred, clamped, overflow hidden, height `min(70vh, 720px)`), semantic tokens only.
  - Bytes: `bffApiBlob(documentContentPath(...), { signal }, { maxBytes: 26_214_400 })`, then `new Blob([bytes], { type: mime })` (re-typed from the decided MIME), then `createObjectURL`. Parents asked in the BFF's order, moving on only on a 403 (mirror of `downloadDocumentFromParents`' loop; that function saves the file, so a read needed its own loop). Failures use `downloadFailureMessage` / `downloadFailureLogText` from DocumentLibrary. A failed preview never touches the toolbar Download.
  - Lifetime: the effect aborts the request and revokes the object URL in its cleanup, so the URL is revoked on unmount and whenever the document or its parents change. A state belongs to a key (`documentId#parents#mime`) and is not shown for another key, so a paged-to document never shows the previous frame. A late answer after cleanup is dropped (no object URL made).
- `PreviewSection.tsx`: the interim card is replaced by `<DocumentPreview documentId={recordId} file={documentFileOf(record)} fileType={record.sprk_filetype} />`. `documentFileOf` is unchanged, so `hasFile === false` and "no parent link" still mean `file = null`, which renders the `record-preview-no-file` note and makes no request (FR-15). The toolbar `DocumentDownloadAction` is untouched.
- `RecordDetailFields.tsx`: ONE additive line in `sprk_document`: `extraColumns: ['sprk_filetype']` (read, not shown, like the service request's `statecode`). `sprk_filetype` is already on the documents module's allow-list (`ExternalAccessModule.cs:554`), so no BFF change and no new column on the BFF. (The other agent in this wave also edits this file for other entities; my edit was a single-anchor replacement and applied cleanly.)
- `auth/bff-client.ts`: additive and optional. `bffApiBlob(path, options, limits?)` with `limits = { maxBytes }`; new exports `BlobLimits` and `BlobTooLargeError`. With no `limits` the call is exactly as before (the non-2xx handling moved into a private `readBlobBody` that both paths use; same `ApiError`). With `limits`: a `Content-Length` over the cap aborts the request (own `AbortController`, chained to the caller's `signal`) and the body is never read; with no usable header the body is read and discarded when `blob.size` is over the cap; either way it rejects `BlobTooLargeError`. The cap is inclusive (exactly 25 MB previews).

## Tests

- `tests/DocumentPreview.test.tsx` (new, 47 tests): `previewKindOf` table (types, name fallback, present-type-is-final, SVG/TIFF/`constructor`/no extension); each previewable type renders and is re-typed; loading; the read args (content path, `{signal}`, `{maxBytes: 26214400}`); `.docx` and a `DOCX` type with a `.pdf` name read nothing; too-large renders nothing and tries no second parent; `file === null` reads nothing; 403 moves to the next parent, all-403 and 5xx show the shared texts (no server detail); revoke on unmount, on document change (and the old frame is never shown for the new document), after paging back and forth only the current URL is alive, paging to a no-file document revokes; a late answer after unmount is dropped; a same-content re-render does not re-read; `PreviewSection` passes `sprk_filetype`, `hasFile === false` and a missing parent link read nothing, an older BFF (no `hasFile` member) still previews.
- `tests/BffBlobLimits.test.ts` (new, 8 tests; extra file justified: the cap lives in `bff-client.ts`, and DocumentPreview.test.tsx substitutes `bffApiBlob`): no-limits behaviour unchanged (no own signal, ApiError on non-2xx); header over the cap aborts and never reads the body; no header and an oversize body is discarded; a non-numeric header does not decide; exactly the cap is kept; non-2xx is the same ApiError; the caller's signal aborts the request, an already-aborted one at once.
- Mutation check: removing the revoke in the cleanup failed 4 DocumentPreview tests; removing the header check and the `blob.size` check failed 2 BffBlobLimits tests. Restored afterwards.
- Existing test edited: `tests/DocumentDownload.test.tsx` fixture row gets `sprk_filetype: 'DOCX'` (with a comment). Its fixture is `Contract.pdf` and its tests count `bffApiBlob` calls (1, 2, `[projects, matters]`); the new Preview section would now also read a `.pdf` document, so the fixture is made non-previewable to keep those tests about Download. No assertion changed.
- Results: full SPA suite `npx vitest run`: 68 files, 1908 passed, 0 failed (includes the other agent's concurrent in-progress work). `npx tsc --noEmit -p .`: clean. `npx eslint` on the changed source and test files: clean. `npm run build` NOT run (wave rule).

## Deviations and drift

- Spec/POML say bytes go "through `authenticatedFetch`"; this SPA does not use `@spaarke/auth`, its authenticated binary read is `bffApiBlob` (`bff-client.ts` header). Used `bffApiBlob`, as the project CLAUDE.md §2 says.
- POML paths (`components/detail/DocumentPreview.tsx`, `RecordDetailModal.tsx`) predate task 005; see Placement above. No `RecordDetailModal` change was possible or needed.
- Step 6 (`npm run build`) and Step 7 (TASK-INDEX / POML status) are for the main session per the wave rules.
- The loop that asks the parents (403 moves on) is mirrored in `DocumentPreview.tsx` rather than shared, because `DocumentLibrary.tsx` must not be edited here (task 023). Candidate for 023 to hoist as a "read bytes from parents" helper that `downloadDocumentFromParents` also uses.

## Known limits (K-class, no fix round)

- K4: the section body stays mounted while collapsed (`RecordSectionCard`), so a collapsed Preview still reads the file. It is one read per opened document and is revoked on unmount.
- K4: with no `Content-Length` (a compressed or chunked answer) an oversize body is downloaded in full and then discarded, as the spec allows; only a declared length aborts early.
- K4: Download and Preview each read the bytes (two reads of the same file for a previewable document). Reusing the preview blob for Download would need DocumentDownloadAction to share state with the section; out of scope.

## Checks left for the owner's E2E (task 028)

I cannot render a PDF here. Please check on the dev SPA (standalone Edge/Chrome AND the Teams host, desktop and web):
1. A PDF under 25 MB renders in the Preview section from the blob URL in an `iframe` (Teams desktop uses WebView2; the `iframe` has no `sandbox` attribute because a sandboxed frame blocks the browser's PDF viewer). If it does not render in Teams or Edge, STOP and report host and error; do not add a PDF library (POML escalation trigger).
2. PNG, JPEG, GIF and WebP render.
3. A `.docx`, a PDF over 25 MB (Content-Length path) and a no-file document show Download only / nothing; the toolbar Download still works for the first two.
4. Network panel: the only read is `.../documents/{id}/content` (no Graph, no signed URL). Paging "1 of N" through ten documents leaves one live blob URL.
5. Dark and high-contrast: stage background and notes follow the theme.
6. Whether real `sprk_filetype` values on dev documents are the upper-cased extension (the BFF's `DocumentProfileOutputMapper` writes it so; documents created by other paths may leave it blank, in which case the name's extension decides).

## Out-of-scope defects found

- D-23 #2928 (F2, from the wave 4 review): `readDocumentFromParents` mirrors `downloadDocumentFromParents` (`DocumentLibrary.tsx`). Failure: a parent-rule change in one copy makes Preview and Download disagree on which parent serves the file. Folded into task 023.

## Wave 4 review (main session, 2026-11-20)

- One `adversarial-reviewer` (opus) pass over 020 + 022; `adr-check` in the main session (no violations).
- **F3 fixed:** the content route streams a non-seekable SPE stream, so it answers chunked with no `Content-Length`, and the cap only fired after the whole file had arrived. `bffApiBlob` with `limits` now counts streamed bytes and aborts once past the cap (`blob.size` stays only for a runtime without a readable body). Two tests added in `BffBlobLimits.test.ts`: an over-cap stream stops being pulled and is cancelled; a within-cap stream returns whole.
- Known limits: the Preview body reads even when the section is collapsed; a previewable file is read again on Download; "Download stays available" on a preview error holds by construction (`DocumentDownloadAction` depends only on `documentFileOf`), not by a test.
- Owner E2E (task 028): PDF in an unsandboxed iframe from a blob URL (a sandbox blocks the PDF viewer). The blob is re-typed to a fixed PDF/image MIME list, SVG excluded, so a hostile upload renders as a broken PDF, never as SPA-origin script.

rework: fix-round-1 task 022
