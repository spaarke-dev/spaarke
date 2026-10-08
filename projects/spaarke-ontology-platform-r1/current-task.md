# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-07 late (main session, context-handoff before /compact). State only — history is in git,
> task `<completion>` blocks and `notes/handoff-history/`. Standing rules: [`CLAUDE.md`](CLAUDE.md). Decisions:
> [`notes/decisions.md`](notes/decisions.md) (D-1..D-62, none open). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | No main-session task — **orchestrating five parallel streams** (owner choice 2026-10-07). Index: 94 tasks; counts + critical path + stream table at the top of `tasks/TASK-INDEX.md`. |
| **Branch** | `docs/ontology-platform-design` (draft PR #1111), pushed, in sync. Master merged in 2026-10-07. ArchTests 818/818 (I5 fixed by `0977c274d`). |
| **Next Action** | Wait for agent notifications; for each: verify, merge its branch into this branch (stream branches) or check its PR, mark the POML + TASK-INDEX (both — drift check), and launch the next task in that stream. Ask the owner only for merges of PRs to master and genuine decisions. |

### Running agents (each reports by notification; none merges)

| Agent | Work | Where it lands | On completion |
|---|---|---|---|
| 098 | PR #1359 follow-ups: fold in 065 (D-27 `sprk_duedate`) + fix 9 non-working TZ-pin tests | PR #1359 (`C:\wt098`) | Focused independent review → ask owner to merge → mark 098 ✅ (065 already marked ✅ in index — it lands with #1359) → start **106** |
| 039 | Secure-child Signals/Decision Records (uac-r2 conditions A/B on #1355) | Own PR to master (`C:\wts-039`, `feat/secure-child-signals-039`) + possibly writer change on this branch | Review → ask owner to merge → ✅ → start **037** |
| Stream C | **036 + 026 MERGED into this branch** (`f7a12a24a`, ArchTests 818/818, Signals 454/454) — stream idle; next free tasks per TASK-INDEX stream table | — | — |
| Stream C2 | 047 response columns (D-58) → D-60 drop foreign tables from `OntologyPlatformSolution` → 046 WA create on `RecordCreationService` (D-59, uac-r2 review) | Branch `stream/c2-047-046` (`C:\wts-c2`) | Give the D-60 job a POML number (draft in notes) → merge → ✅ |
| Stream D | 072 DONE on branch `stream/d-072` @ `ff001fb54` (agent could not run Step 9.5) — review PASS-WITH-FINDINGS (F1 `sprk_enabled` not filtered — confirmed); **round 2 running** (per-taxonomy enabled filter, shared URL builder, cap tests, cancellation, total 8k budget, bare names on guidance failure, re-measure size, file issue for email project resolver) | Branch `stream/d-072` (`C:\wts-d`) | Fix review findings → merge into this branch → mark 072 ✅ (POML already completed; index pending) → start **073**, then **074** (measure via real ActionRunner path) |
| Stream B | 057 → PR #1382 (review running); **056 running** (own PR, `C:\wts-b56`) | Own PRs to master | Review → ask owner to merge → ✅ |
| Stream E | 060 → PR #1384 (review running); next **068** (D-63) after #1359 merges | Own PRs to master | Review → ask owner to merge → ✅ |

### Agent IDs (for SendMessage after a reset — a finished agent resumes with its context when messaged)

| ID | Role | State |
|---|---|---|
| a810b83c36e7d8f39 | 039 secure-child Signals/DRs (own PR, uac-r2 review) | running |
| a87c76386416d9b61 | Stream C2: 047 → D-60 solution cleanup → 046 | running |
| af7588b2ed6612c5f | Stream B: 056 WizardShell on SprkModal (own PR) | running |
| a7d2ba5e803c9cc47 | Reviewer: 072 (stream D branch) | done |
| a3f2fdb8356621906 | Reviewer: stream C round 2 (036/026) | done |
| aeaf761bb094a0bd4 | Reviewer: PR #1359 (098 + 065 + TZ tests) | running |
| a6065b4aa9ef36798 | Reviewer: PRs #1382 (057) and #1384 (060) | running |
| a67ad20f7bead7126 | 098 author (PR #1359) — resume for review fixes / rebase | idle |
| a0c39a85690aeed2a | Stream C author (036/026) — round 3 | running |
| ab5429413019d8fb9 | Stream D author (072) — round 2 running; then 073/074 | running |
| a18a719c189250af3 | Stream B author of 057 (PR #1382) — resume for fixes | idle |
| a17154f00e69eeced | Stream E author of 060 (PR #1384) — resume for fixes; then 068 after #1359 merges | idle |

### Critical path to 031

039 → 037 → **031**; 031 also needs **098** merged and **106** (To Do dates). D-61 binds 031: never stamp
unchanged Signals nightly.

### Recent outcomes (2026-10-07)

- Merged to master: **#1302** (097), **#1309** (081), **#1346** (099), **#1380** (110, ADR-050 amendment, `36ff14147`).
- Done on this branch: 007, 008, 009, 024, 079 (uac-r2 review received, #1355), writer-credential I5 fix.
- Filed: **#1383** (central `ManagedIdentityCredentialFactory` empty `AZURE_TENANT_ID` leaves tenant unpinned — out of scope, reported).
- Reported to owner, no action needed: D-25 in dev falls back to UTC (dev events fill only `sprk_assignedto`, which
  `AssignedToDefaults` doesn't read); 008 kept the writer's Read on `sprk_budget`; read-only Platform forms show only Name/Owner.

### Housekeeping for the owner

Delete by hand (sandbox can't delete under `C:\`): `C:\wt081-base`, `C:\wt097m`, `C:\wtz`, `C:\wt097\TestResults097`,
`C:\wt081r`, `C:\wt081s`, `C:\wt21m`, `C:\wt21h`, `C:\wt21t`. Registered worktrees removable now: `C:\wt081b`,
`C:\wt081c`, `C:\wt081m`, `C:\wt081`, `C:\wt092`, `C:\wt094`, `C:\wt095`, `C:\wt096`, `C:\wt097`, `C:\wts-110`;
`C:\wt098` after #1359.
