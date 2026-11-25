# Current Task State

> **Last Updated**: 2026-11-25 ~15:00Z (task 038 closed)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active** | None. Task 038 (P3 dev deploy + E2E) is **done** (2026-11-25). Phase 3 is complete. |
| **Next** | Phase 4: the first 🔲 in `tasks/TASK-INDEX.md` (040, then 041/043–047…). Run it through task-execute. |
| **Live on dev** | SPA `0267ee64a2` (run 45552869227; rollback SPA `3067fb3bdc`). BFF `ef0c566d2c` (uac-r2 deploys; owner go per SHA in uac-r2's session). |
| **Master since the live SPA** | Master has moved past `0267ee64a2`, including ebilling #3084 at `2ac250cae4` (ISS-052 #3082, invoice-result links). The next SPA deploy proposal must list what master adds since `0267ee64a2`, and re-check HEAD just before dispatch (`deploy-external-spa.yml` builds master HEAD). |
| **038 record** | `notes/task-038-deploy-record.md` (evidence), `notes/task-038-notes.md` (outcome + deviations). |

## Owner questions open
- Track the raw filing-reference footer seen in an older PRJ-000046 system email? (Told to the owner; not filed. The repo is public, so describe areas only.)
- Track the residual export-hardening gaps privately (chat only, never in the repo)?
- Should a docx preview be filed as an Idea?
- Show "The client is reviewing this invoice." beyond the Information field?
- Add a `useHostRowSource` row to `.claude/patterns/CAPABILITY-INDEX.md` row 31 (`.claude` edit, owner approval)?

## Filed
- D-24 #2939 … D-30 #2975; D-31 #3008; D-32 #3009; D-33 #3010 closed.
- D-34 #3045 done (PR #3055); D-35 #3046 (unlabelled currency = USD); D-36 #3050 (MDA grids don't read OData label annotations).
- D-37 #3129 (export ignores the on-screen sort); D-38 #3149 (three low-priority browser checks not run in 038: storage blocked, remote image, high contrast).

## Merge trap (seen 2026-11-22)
When merging master after a squash of our own work, watch for a line that one task changed and a later task changed back: it looks unchanged from the merge base, so git takes master's text. For files where `origin/master` equals the pre-squash branch tip, restore `HEAD^1`'s copy.
