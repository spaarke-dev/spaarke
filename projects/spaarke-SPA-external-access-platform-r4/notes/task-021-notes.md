# Task 021 — Invoice Files section on ebilling 093's routes (FR-13)

**Owner:**
- Retest, 2026-11-14: "I don't see the actual invoice document/file". Also "a large upload blue button instead of our standard toolbar icon".
- 2026-11-15: start 021 now, ahead of 020/022/023.

## Gate deviation (owner decision 2026-11-15)
- **What the POML said.** Its escalation was "if the `/files` routes are not on master, STOP".
- **What was done.** The routes are in ebilling PR #2839 (head `3b33a8d8cc`), which passed uac-r2's review. The agreed chain is: 021 merges first, then uac-r2 swaps in the BFF with 093, then the SPA deploys straight after. So the owner chose to build against #2839.
- **Contract read at the `pr/2839` ref:**
  - `Services/Finance/Files/StoredFileContracts.cs:628` (`ExternalInvoiceFileItem`) and `:632` (scan statuses Clean / Checking / NotScanned).
  - `Api/ExternalAccess/ExternalInvoiceSubmissionEndpoints.cs:106` (list), `:122` (content), handlers `:148` / `:169`. Both routes sit behind `RequireEbillingEnabled()`.
  - `Api/Finance/StoredFileEndpoints.cs:31` (`file_scan_pending`) and `:34` (`file_store_unavailable`). The 409 body is at `:479` and the 503 body at `:491`.
  - `Api/ExternalAccess/Dtos/ExternalProjectDtos.cs:13` (`ExternalCollectionResponse` → `value`).
  - Casing is camelCase (default web JSON).
  - There is no failed state: a Malicious file is omitted from the list, and its content gives 404. The POML's "failed" branch is overridden (ebilling, 2026-11-14).

## What changed (src/client/external-spa)
- **`api/web-api-client.ts`:**
  - `getInvoiceFiles` and `getInvoiceFileContent` (content via `bffApiBlob`).
  - `isFileScanPendingError` (409 + code).
  - `isInvoiceFilesDisabledError` (reuses `isEbillingDisabledError`).
  - `INVOICE_FILE_FALLBACK_NAME`.
  - `'invoices'` removed from `DocumentRoot`, so the compiler enforces that no invoice documents path remains.
- **`components/record/sections/InvoiceFilesList.tsx` (new):** the read-only list. It sits with the other record-template sections rather than at the POML's `components/detail/InvoiceFiles.tsx`.
  - Rows:
    - Clean / NotScanned → Download, saved under `fileName`. The content route sends no Content-Disposition.
    - Checking → "Checking file…", no Download.
    - Content 409 `file_scan_pending` → the row switches to Checking, with a notice.
  - List states:
    - 404 → "aren't available".
    - 403 → "no access".
    - 503 `ebilling.disabled` → one neutral line, "Invoice files aren't available.", with no Retry.
    - Any other 503, or other failures → Retry.
    - Empty → the shared EmptyState.
  - Null size or date shows a dash. An empty name shows as "Invoice file" and saves as `invoice-file.{ext from contentType}`.
- **`FilesSection.tsx`:** mounts the list.
- **Removed from `DocumentLibrary.tsx`:** the `invoices` noun and `onUploadRightChange`. `formatFileSize` / `formatDate` are exported unchanged, and `saveBlobAs` is extracted with the same behaviour.
- **Removed from `RecordDetailFields.tsx`:** `documentsRoot: 'invoices'`.
- **Removed elsewhere:** the `filesUpload` right plumbing (DocumentsSection, RecordTemplateModal, recordAccess, recordTemplate.types). It existed only for the invoice Upload.
- **Comment fixes:** `ChildRecordModal.tsx` and `recordTemplates.ts`.
- **Unchanged:** invoice message threads (`messages-client.ts`).

## Tests
- **`tests/InvoiceFiles.test.tsx` (new).** Beyond the POML's named contract (clean, pending, empty, no Upload/Versions/Delete, scan), it also covers:
  - NotScanned (a downloadable state in the contract);
  - an unknown status (fail closed);
  - null fields (the DTO allows them);
  - list 403/404/503/network (the contract's answers);
  - `ebilling.disabled` vs `file_store_unavailable` (review F1);
  - the empty name (review K2);
  - mixed rows.
- **Updated:** `DetailDocumentsDelete`, `DetailEventsRoots`, `RecordTemplateAccess` (F2: the setup now matches its title), and `RecordTemplateSections`.
- **Deleted:** 7 "invoice documents on the detail view" tests. They covered the retired invoice root's upload, versions and delete. None is on an ADR-038 KEEP path, and the same behaviour on the other roots stays tested.
- **Results:** typecheck and lint clean; vitest 60 files / 1715 passed; `npm run build` passed.

## Quality gates
- **Adversarial review (opus, one full pass).** Every criterion except the overridden failed state is met.
  - F1 fixed: the eBilling kill switch was shown as a retryable fault. eBilling is off everywhere except dev.
  - F2 fixed: a test setup contradicted its title.
  - F4 fixed: this notes file.
  - K2 fixed: the empty-name fallback.
- **Known limits:**
  - K2: one download guard is shared across rows, so a second row's click is ignored while a download is in flight. Only the invoice PDF is listed today.
  - K4: before the BFF swap, and in mock mode, the list shows the 404 text. Deploy order is BFF first.
  - K4: a 403 `workforce_baseline_unavailable` shows "no access"; the BFF chooses that status.
- **Scoped re-check of the fix diff:** InvoiceFiles + RecordTemplateAccess, 172 passed.
- **adr-check:**
  - ADR-007 / ADR-028 A1: bytes come only via the content route and `bffApiBlob`.
  - ADR-021: Fluent v9 and tokens.
  - ADR-012: no shared-lib change.
  - Hide, don't disable.
  - Clean.

## Deploy (strict)
1. Merge 021.
2. uac-r2 swaps the dev BFF with 093 (#2839 merged), with its owner's go.
3. Deploy the SPA straight after.

No SPA deploy may run between merging 021 and that swap.
