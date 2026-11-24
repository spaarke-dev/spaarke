# Current Task State

> **Last Updated**: 2026-11-24 ~15:20Z (#3055 merged; D-34 done)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active** | None in progress. Next is **038** (P3 dev deploy + E2E). |
| **Status** | **PR #3055 merged** 2026-11-24 15:15:44Z, squash `8dd44cf528` (#3045 export: cells export shown text, dates as shown, export text hardening). #3045 closed; D-34 Done. Notes: `notes/task-033-followup-3045-notes.md`. Owner 2026-11-24: Date Only exports stay as shown (viewer's locale), not `yyyy-mm-dd`. Open owner question: whether to track the residual hardening gaps privately (chat only, never in the repo). |
| **Next action** | Plan **038** (P3 dev deploy + E2E) on the owner's per-SHA go; SPA deploy builds master HEAD (now includes `8dd44cf528`). uac-r2 told the #3048 merge `b64d988625` for a later BFF candidate (threads). |
| **Last merged** | PR #3048 at 06:14:00Z, merge `b64d988625`: tasks 033 (Money sort + export) and 035 (message threads, `threadId`). POMLs completed, TASK-INDEX ✅, drift check clean, board Tasks Completed = 30. |
| **Next in phase 3** | **038** (P3 dev deploy + E2E). 036 is cancelled. |
| **uac-r2** | Dev BFF `0362599b94` LIVE since ~05:21:45Z 2026-11-24; it does NOT carry 035's `threadId` (the SPA then shows ungrouped messages). Threads need a later BFF candidate that includes `b64d988625`, through uac-r2 with the owner's per-SHA go. |
| **ebilling-r1 FYI** | #3051 (submission result screen title/icon follow the outcome) merged `cf1b7027ce`; it ships with 038's SPA build. Master PR #3065 open: rejected-outcome `valuesRead` names become firm labels (SPA renders `name` as received, `InvoiceOutcomeReport.tsx:440`), and the submission route rejects a repeated `firmInvoiceNumber`/`poNumber` with 400 (SPA appends each once, `web-api-client.ts:954-955`). No SPA change. |

## Deploy order
Every SPA deploy builds master HEAD and needs the owner's go for the exact SHA, with rollback `9d08576e63`. For 035's threads to show, the BFF must also carry `b64d988625`.

## Live on dev
- SPA: master `9d08576e63` (rollback `2dcbe35e34`).
- BFF: `0362599b94`, live ~05:21:45Z 2026-11-24 (uac-r2; previous `996928732e`).

## For 038's E2E
- Wave 8: Information columns (matter: Open date, Matter type, Practice area, Assigned attorney/paralegal, Description, Last modified; project: Practice area, Assigned attorney/paralegal, Description; WA: Matter, Matter type, Assigned to, Description). Grid widths/sort per browser, "Clear all". Messages Refresh + "Show new messages".
- Wave 9: currency columns sort by amount; Export to Excel writes displayed text (labels, names, amounts, dates as shown); messages grouped into conversations (needs the threads BFF).
- Known limits: "Open date" is a date-time; a parent's Messages keeps polling under a child modal; amounts in different currencies sort by raw amount; an unlabelled currency cell shows USD (D-35 #3046).

## Owner questions open (older)
- Should a docx preview be filed as an Idea?
- Show "The client is reviewing this invoice." beyond the Information field?
- Add a `useHostRowSource` row to `.claude/patterns/CAPABILITY-INDEX.md` row 31 (`.claude` edit, owner approval)?

## Filed
- D-24 #2939 … D-30 #2975; D-31 #3008; D-32 #3009; D-33 #3010 closed.
- D-34 #3045 done (PR #3055); D-35 #3046 (unlabelled currency = USD); D-36 #3050 (MDA grids don't read OData label annotations; not verified live).

## Merge trap (seen 2026-11-22)
When merging master after a squash of our own work, watch for a line that one task changed and a later task changed back. It looks unchanged from the merge base, so git takes master's text. For files where `origin/master` equals the pre-squash branch tip, restore `HEAD^1`'s copy. 2026-11-24 merge `e288c59dc0`: every touched file was identical on both sides (clean).
