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
| Stream C | 036 action catalog → 026 rule describer | Branch `stream/c-036-026` (`C:\wts-c`) | Merge into this branch (route-ledger edits one at a time) → ✅ |
| Stream C2 | 047 response columns (D-58) → D-60 drop foreign tables from `OntologyPlatformSolution` → 046 WA create on `RecordCreationService` (D-59, uac-r2 review) | Branch `stream/c2-047-046` (`C:\wts-c2`) | Give the D-60 job a POML number (draft in notes) → merge → ✅ |
| Stream D | 072 classifier guidance | Branch `stream/d-072` (`C:\wts-d`) | Merge → ✅ → start **073**, then **074** (recall exit gate) |
| Stream B | 057 Console UI kit | Own PR to master (`C:\wts-b`, `feat/console-ui-kit-057`) | Review → ask owner to merge → ✅ → start **056** (110 is merged) |
| Stream E | 060 re-scoped (D-57): Briefing uses `IsOpenWork`, no data change | Own PR to master (`C:\wts-e`, `fix/briefing-open-work-060`) | Review → ask owner to merge → ✅ |

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
