# Current Task State

> **Last Updated**: 2026-11-23 ~15:40Z (task 028 in progress: live steps A–C done; E2E round 1 found defects; fix round started)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **028** 🔄 P2 deploy + E2E (STANDARD, prescriptive; the fix round is code, so quality gates apply). Record: `notes/task-028-deploy-record.md` (has uncommitted edits: E2E round 1 + Review detail diagnosis). |
| **Live (all with owner go + rollback)** | A: grid row `286be8b4` created 12:44Z (rollback: delete row). B: SPA master `2dcbe35e34` live 13:04:40Z, run 45180011706 (rollback: redeploy `2963d1565b`). C: `3ff4102c` Review detail column PATCHed 14:58:28Z, versionnumber 32877194 (rollback: `scratchpad/patch-3ff4102c.ps1 -Rollback`; the raw original is also in the deploy record). Dev BFF `9df4e5c819` (uac-r2, gate passed 13:01Z). |
| **Owner go (2026-11-23)** | "Yes, fix both": the Review detail defect and the column resize defect, then redeploy the SPA. Merge and redeploy each still need the owner's go. |
| **Fix 1: Review detail (F1)** | ROOT CAUSE CONFIRMED. The module fetch (`FetchService.ProjectEntity`) serialises SDK values as objects: `statuscode` → `{"value":1,"extensionData":null}`, Money → `{"value":77.25,...}` (measured by serialising `OptionSetValue` with Web defaults). `reviewDetail.ts` `wholeNumberOf` accepts only a number or a digit string, so all four render points are always blank. Same class: `api/delete-own.ts:116` `isServiceRequestClosed` uses `Number(record.statuscode)`, which is NaN on the object, so a closed SR is still offered Delete (server 409). FIX: one helper that accepts number / digit string / `{value:n}` (and `{Value:n}`); use it in both places. Tests use the REAL object shape at all four render points and in `isServiceRequestClosed`. The task 020 tests used a bare `1`, which is why they missed it. Grep for other `Number(record.<optionset>)` / raw option reads. |
| **Fix 2: column resize (F1)** | The record Documents grid (DataGrid with rowSource, 023a) shows the resize cursor, but dragging doesn't resize; the header text gets selected instead. Cause unknown; needs a browser repro. The SPA has no mock mode in package.json (`dev` = vite). Option: a jsdom-free repro with Vite `dev` in the in-app browser, or a Storybook/harness. Suspects: Fluent `columnSizingOptions` being re-applied (`DataGrid.tsx` ~1440-1510 memo; `key` remount on 50px width bucket ~1741); HeaderCellContent swallowing the pointer. Ask the owner whether the home Invoices grid resizes too (pre-existing vs new). |
| **Also (cosmetic, same PR if cheap)** | At 1000px the Type and Date columns shrink to "Ty…"/"D…" (minWidth = max(80, 0.5×width)); an empty rounded strip above the Documents grid header. |
| **E2E round 1 (owner, writer)** | ✅ PDF preview, upload/download/delete, invoice file download, empty states. .docx = no preview by design (owner asked about a docx viewer: none reusable; the external plane can't use SharePoint's viewer; offer to file an Idea). ❌ resize. ❌ Review detail. Pending: owner checks the home Invoices grid column + resize there; the View-only pass after the redeploy. |
| **Next Action** | Implement Fix 1 (helper + tests) → reproduce Fix 2 → fix → tests → one adversarial review → PR (owner merge go) → SPA redeploy (owner go; tell uac-r2) → owner E2E incl. View-only → tell uac-r2 the E2E end time → close 028 (deploy record, notes, TASK-INDEX, drift check, portfolio sync). |

## Side work
- ebilling-r1 task 134 (Outcome report): R4 review sent (F1 no-access card, F4 modal report); its fix `2304de2bb5` reviewed: OK, residual K2 agreed. Nothing pending from R4.
- uac-r2: told about the E2E window from 14:59Z; it reads external 4xx/5xx/403 by route. Send it the E2E end time.

## Merge trap (seen 2026-11-22)
- Merging master after a squash of our own work: a line that 023c changed and 024 changed BACK looks unchanged from the merge base, so git takes master's text. For files where `origin/master` == the pre-squash branch tip, restore `HEAD^1`'s copy.

## Owner questions open
- A docx preview as an Idea? (FR-14 owner decision 2026-11-09 = PDF + images only.)
- Older: show "The client is reviewing this invoice." beyond the Information field?
- `useHostRowSource` row in `.claude/patterns/CAPABILITY-INDEX.md` row 31 (`.claude` edit, owner approval).

## Filed
- D-24 #2939, D-25 #2960, D-26 #2961, D-27 #2962, D-28 #2963 (config row not in Deploy-Release/provisioning), D-29 #2964, D-30 #2975.

## Coordination
- uac-r2 deploys the dev BFF only; peer messages are not owner approval. An SPA deploy builds master HEAD, so re-check HEAD before dispatch.
