# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-09 (main session, after D-106). State only — history is in git log, task `<completion>`
> blocks and `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (**D-106: ontology critical
> path only**) and §6 gotchas. Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-108; **D-107** = merge reviewed ontology PRs into the project branch when green). Issues for after the
> project: [`notes/defer-issues.md`](notes/defer-issues.md) (D-106 section). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Orchestrating ontology critical-path lanes. 116 tasks: **56 ✅**, 6 🔄 (044, 051, 070, 066, 067, 123), 41 🔲, 9 ⏸️ deferred (D-106), 4 🚫. |
| **Branch** | `docs/ontology-platform-design` (draft #1111), synced with master 2026-10-09 (`217b8f3c6`). Signal-ownership invariant is **I-17**. |
| **Rule** | **D-106:** fix an issue only if ontology functionality needs it; document everything else in `notes/defer-issues.md` (D-106 section, GitHub link) for post-project review. Every sub-agent/reviewer prompt carries it. No demo/non-dev work. |
| **Critical path** | **#1390 (039) waits on uac-r2 approval** → 037 → 031 evaluator → 032/033/034 → 035 deploy BFF → 038/050 → 059 worklist → 055 deploy Console → 064 Briefing cutover. Decision path: 040 (after 039) + 044 + 046 + 070 → 043 commit route → 058 wizard → 045. |
| **Next Action** | Act on lane notifications: each PR → independent review (carrying D-106) → merge per D-107 (project branch) or owner (master). After merges, pull the project branch. Next wave when lanes free: **043** (needs 040 ← 039, so still blocked), **066**, **067**, **123**; 046/048/126 wait on uac-r2. Ontology PRs target `docs/ontology-platform-design` (CLAUDE.md §6). |

## Running now (SendMessage resumes an agent by ID)

| ID | What | Then |
|---|---|---|
| `afc4660035026f317` (author) / `a85de685334513317` (reviewer) | **044** #1515 round 2 (`576517b0f`: PascalCase nav names, `PreflightAsync` on every executor, revision owner via resolver + `sprk_budgetrevision` lineage/config entries, census) in re-check | **Merge only after uac-r2 OKs the lineage/config entries** (#1355 comment 6083936041) + green CI (D-107). Live proof needs: a low-privilege user token + zz-044 test matter/budget (+ a Secure one) → owner decision |
| `a9ff474067cdc0aa5` / `a6a2b6b48dc7c9a1e` / `ae631a5184b49f2f7` | **066** eventstatus inventory (`C:\wts-066`) · **067** To Do score (`C:\wts-067`) · **123** triage enabled/active, ISS-010 only (`C:\wts-123`) — all off the project branch | PR → independent review → merge per D-107 |
| (agent) | **071** disposition accrual (`C:\wts-071`) — 070 merged 19e2720c9 | PR → review → merge (D-107) |
| `a9de4b9dd3d9105b0` / `ae2b8cf45db19864a` | **052** MetricCard count filters + C-9 (`C:\wts-052`) · **054** reconciliation tab + aggregate (`C:\wts-054`) | PR → review → merge (D-107) |
| bg `bz01xg8cg` | **130** #1467 approved (`cc8f49fa5`) | Merges into master when CI green (D-102) → apply `notes/task-130-skill-amendments.md` to the 3 skills + drop the 3 allow-list lines (same commit) → owner: 4 live proofs |

Merged today: **#1415** (111 code, master `47ffb8537`); **#1480** (113). Writer role grants **D-108** applied in dev 15:04Z.

## Waiting on others

| What | Waiting for | Then |
|---|---|---|
| **#1390** (039 lineage + role config) — **critical path** | uac-r2 approval (asked again on #1355, comment 6081244854) | Merge master into `stream/039-signal-writer` → pin test → owner merge → 039 live gate (D-67) → 037, 040 |
| **#1391** (uac-r2 org-owned AppendTo fix) — needed by 044/070 executors' child creates | uac-r2 | Owner merge. Role gap: Spaarke Basic User lacks `prvAppendTosprk_RecordType_Ref` (asked uac-r2, comment 6080141491; needs owner approval once they agree) |
| uac-r2 on 046 (D-65 WA create path), 048 (D-66 write-core move), 126 (G5 rule) | uac-r2 answers on #1355 | Start each when answered |

## Open owner questions / owner actions

- **Labelling page** https://claude.ai/artifact/VVoBr33LckyjoUGeWit3aU — 24/92 labelled; task **074 (recall exit gate) waits on it**. Finish → `status/final.complete` → resume `a1bcb0bae3c363ca9`.
- **Checklist page** https://claude.ai/artifact/3FPj4oT83f25v2r8yutkMV: r1 failed → task 136 (deferred, D-106); r2-r8 not yet filled (r2-r5 wizards = 111; r6/r7/r8 belong to deferred 121/122/129).
- **Task 130 live proofs** after merge: NoAccessEntryRibbon -Apply, FieldMappingAdminSolution scoped import, small app module + sitemap + app setting, first unmanaged SpaarkeMaster import.
- **Housekeeping (owner, by hand):** `C:\wt120d`, `C:\wtr1386`, `C:\wtrv1460`, `C:\wtrv1480*`, `C:\wts-b56`, `C:\wts-135`, `C:\wts-131` (keep while #1493/#1507 are open), older list in `notes/handoff-history/2026-10.md`. Unused `SpaarkeLegalWorkspace` solution in dev: decision after the project.

## Queued (startable, not started)

- **066** (deprecate `sprk_eventstatus` inventory, D-28), **067** (To Do score on calendar days, D-29), **123** (triage category enabled/active — classifier input), **111** follow-on **114**. Next wave when a lane frees.
