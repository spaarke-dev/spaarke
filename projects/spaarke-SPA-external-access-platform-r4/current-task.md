# Current Task State

> **Last Updated**: 2026-11-25 ~18:40Z (task 040 completed; PR being opened)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active** | None. Task 040 (FR-24, #2508) is ✅ completed: code, tests, reviews, notes (`notes/task-040-notes.md`), TASK-INDEX/POML, portfolio (32/46). |
| **In flight** | Task 040's PR to master: merge only on the owner's go. Then the dev deploy for its ui-tests: BFF through uac-r2 (owner go on the exact merged SHA in uac-r2's session), SPA with the owner's go. |
| **Next action** | After the PR: next 🔲 in `tasks/TASK-INDEX.md` through task-execute, when the owner says so. |

## Task 040 outcome (details in notes/task-040-notes.md)
- Server: `ModuleEntitlementResolver.IsGrantedWorkforceContact` adds `assigned-work` for a workforce contact that can read ≥ 1 project, matter or WA. The code is stripped from role-map extras, so a systemuser never gets it. No new endpoint, field, DI or package.
- SPA: the five record tabs share `RECORD_TAB_GATE` (ciam + workforce).
- Review F1 (owner 2026-11-25: hide it for now): Submit invoice is hidden on the workforce plane (`callerMaySubmitInvoices` in `recordAccess.ts`, plane from `activePlaneOrNull()`). The question went to ebilling as D-41 #3159.
- Known limits filed: D-39 #3153 (the ceiling maps one table to five tabs), D-40 #3154 (K3). K2 is unchanged.
- uac-r2 review: no objection (quoted in the notes).

## Live on dev
SPA `0267ee64a2` (rollback `3067fb3bdc`); BFF `a1b7db1def` since 2026-11-25 15:58:53Z (rollback `ef0c566d2c`; uac-r2 deploys; owner go per SHA). Task 040 is not in it. Master has moved past `0267ee64a2`, including ebilling ISS-052 #3082 (`2ac250cae4`): list it in the next SPA deploy proposal.

## Owner questions open
- Track the raw filing-reference footer seen in an older PRJ-000046 system email? (Told to the owner; not filed. The repo is public.)
- Track the residual export-hardening gaps privately?
- Docx preview as an Idea?
- "The client is reviewing this invoice." beyond the Information field?
- `useHostRowSource` row in `.claude/patterns/CAPABILITY-INDEX.md` row 31 (`.claude` edit, owner approval)?

## Filed
- D-24 #2939 … D-33 #3010; D-34 #3045 done; D-35 #3046; D-36 #3050; D-37 #3129; D-38 #3149; D-39 #3153; D-40 #3154; D-41 #3159.

## Merge trap (seen 2026-11-22, again 2026-11-25)
Master holds older squashes of this project's notes. On a master merge, keep the branch's copies of `projects/spaarke-SPA-external-access-platform-r4/**` (`git checkout --ours`).
