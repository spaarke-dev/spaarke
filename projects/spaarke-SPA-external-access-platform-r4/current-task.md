# Current Task State

> **Last Updated**: 2026-11-15 (context checkpoint)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 021: invoice Files section on ebilling 093's routes (FR-13). FULL, sonnet high, directional. The owner pulled it ahead of 020/022/023 on 2026-11-15. |
| **Step** | DEPLOYED. BFF live 18:32Z (uac-r2 build aa7d6345f3 = master c1eb6e4a8e + mcp spike). SPA run 43747335450 (master c1eb6e4a8e) succeeded; the bundle carries /invoices/{id}/files, "Invoice files aren't available.", "Checking file…" and no invoice /documents root |
| **Status** | 021 done and deployed; waiting on the owner's invoice Files retest + the 012/013 retest |
| **Next Action** | Record the owner retest results (018 notes); fix any findings; then phase 2 (020/022/023/024/028). SPA rollback 04dbd8df05 only together with BFF rollback 5a9b687a0b (the old SPA calls the retired routes). uac-r2's 30-minute error gate is running. |

## Task 021 facts
- **Contract (not on master; ebilling PR #2839, head `3b33a8d8cc`, local ref `pr/2839`):**
  - `Services/Finance/Files/StoredFileContracts.cs:628/632`
  - `Api/ExternalAccess/ExternalInvoiceSubmissionEndpoints.cs:106` (list), `:122` (content), handlers `:148`/`:169`
  - `Api/Finance/StoredFileEndpoints.cs:31/34`, 409 body `:479`, 503 body `:491`
  - `ExternalProjectDtos.cs:13` (envelope `value`)
- **Gate deviation (owner, 2026-11-15):** the POML escalation said "routes not on master → stop". The owner chose to build against #2839 because the agreed order is that 021 merges first.
- **Files (uncommitted):**
  - `api/web-api-client.ts` (client; `'invoices'` removed from DocumentRoot)
  - new `components/record/sections/InvoiceFilesList.tsx`
  - `FilesSection.tsx`
  - `DocumentLibrary.tsx` (invoices noun and onUploadRightChange removed; formatFileSize/formatDate exported; `saveBlobAs` extracted)
  - `RecordDetailFields.tsx`
  - filesUpload plumbing removed from DocumentsSection, RecordTemplateModal, recordAccess and recordTemplate.types
  - `ChildRecordModal.tsx` (comment)
  - tests: new `tests/InvoiceFiles.test.tsx` (25); updated DetailDocumentsDelete, DetailEventsRoots, RecordTemplateAccess, RecordTemplateSections. 7 invoice-documents tests deleted (not KEEP paths).
- **Review K items:**
  - the shared download guard silently ignores a second row's click;
  - deploy-order and mock mode (no /files mock) give a 404 message;
  - a 403 `workforce_baseline_unavailable` shows "no access".

## Deploy chain (strict)
1. 021 is merged to master.
2. uac-r2 swaps the dev BFF with 093 (#2839 merged), with its owner's go.
3. The SPA deploys right after.

uac-r2 holds any 093 BFF until I confirm 021 is merged. **No SPA deploy between merging 021 and that swap.**

## Live state
- **Dev BFF:** `aa7d6345f3` (master `c1eb6e4a8e` + mcp spike), live 18:32Z. BFF rollback `5a9b687a0b`.
- **Dev SPA:** master `c1eb6e4a8e` (run 43747335450; tasks 012/013/021). SPA rollback: `04dbd8df05` (paired with the BFF rollback).
- The owner is retesting 012/013 on dev (no results yet).

## Done this stretch
- 012 ✅ `5a0d8e8771` and 013 ✅ (#2837, merged `04dbd8df05`), deployed.
- ebilling consumer review of 093 done. Agreed in principle to their optional `correction` wizard prop (conditions sent).

## Holds
- **Infrastructure/ExternalAccess/** is held by uac-r2** for #2845 (PR #2847, branch `fix/2845-external-request-latency`), from 2026-11-15. No R4 edits there until #2847 merges. The Event forms BFF task (014, `ExternalDataService.cs`) waits on this.
- **Event forms addendum** (`notes/event-forms-design.md`, draft) is waiting on the owner's answers to Q-1 to Q-9. After that: spec FR-35/36, then tasks 014/015/016.

## Open items / issues
- D-13 #2800, D-14 #2809, D-15 #2810, D-16 #2823 (silent DataGrid failures).
- Older owner questions: D-1 #2686, D-2 #2687, D-4 #2693, #2786 backfill (uac-r2).
- 018 is closed only after the owner retest results (`notes/task-018-notes.md`).
