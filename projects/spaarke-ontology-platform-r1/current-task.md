# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-09 ~03:30 UTC, checkpoint 4 (main session, `/context-handoff`). State only — history is in git log,
> task `<completion>` blocks and `notes/handoff-history/2026-10.md`. Standing rules + coordination: [`CLAUDE.md`](CLAUDE.md) (§3
> includes the **no-parking rule, D-81**). Decisions: [`notes/decisions.md`](notes/decisions.md) + spec §9 (D-1..D-101). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | No single main-session task — orchestrating parallel lanes (owner choice). 113 tasks, **55 ✅** (board synced). |
| **Branch** | `docs/ontology-platform-design` (draft #1111), pushed, clean. **206 commits behind origin/master** → first action next session: merge master (expect route-ledger / write-path-doc conflicts; see CLAUDE.md §6 gotchas). |
| **Critical context** | Every PR gets an independent review + fix-round re-check before the owner's merge; owner pre-approves "merge when green" per PR (I build/merge-tree against current master first). No tenant-wide publish (D-83). Dev deploys/data changes need an owner decision. Never park an issue (D-81): every defect found becomes a task here. |
| **Next Action** | (1) Merge origin/master into this branch. (2) Act on agent notifications below as they arrive. (3) Bring the owner the open questions at the bottom. |

## Running now (agent / background IDs — SendMessage resumes them)

| ID | What | Then |
|---|---|---|
| `a05ace738dcb47fe5` / `acfdd3ea462a111e9` / `ada91107a9efd8931` / `aaa74682a6e89bfc8` | Tasks **131** (retire notification playbooks, `C:\wts-131`) / **132** (appnotification option fix, `C:\wts-132`) / **133** (Designer read-only, `C:\wts-133`) / **135** (Matter Health fix + demo/junk deactivation, `C:\wts-135`) | Each: PR → independent review → owner merge; 131 asks before deleting the Azure alert and any code; 135 does approved dev data changes (full GUIDs) |
| `aae6c79edd0f863de` | Task 120 author — **D-91 pause DONE** 02:42Z (statecode 1/2 on Overdue, Matter Activity, Documents, Emails, Events; scheduler honours statecode only; 03:00Z tick skipped them). Resume = statecode 0 / statuscode 1. Due Soon + Work Assignments stay on (daily, next run ~02:34Z 10-10) | Idle; waits for the sweep |
| `afa95b62d109541c3` | Task 130 author: round-3 re-check APPROVED pending CI; fixing the last items on **#1467** (per-chunk read-back for entities/forms/views/charts + site maps; chunk order option sets/web resources first, app modules last; resume command; app-setting parent-app pre-flight; workflow activation step documented; 0x80060888 probe rationale) | Light re-check by `afead9298bddac72a` → owner merge + live proofs (4 listed by reviewer: NoAccessEntryRibbon -Apply, FieldMappingAdminSolution scoped import, small app module + sitemap + app setting, first unmanaged SpaarkeMaster import) → main session applies `notes/task-130-skill-amendments.md` to the 3 skills, removing the 3 allow-list lines in the same commit |
| `a6aba9e72ed9b1343` | Task 113 author: last 3 small fixes on **#1480** (busy-warning text, name-read failure test, lock churn) | bg `bcpu6whdj` merges #1480 when green (**D-93 pre-approved**) → Console auto-deploys to dev → mark 113 ✅ → task 114 = live checklist only |

## Playbooks — state (D-100 / D-101)

Task 120 ✅ (#1461 merged; engine fix stays). Owner decided **D-100: retire the notification playbooks** (bell-only; the Briefing never reads appnotification) — author `aae6c79edd0f863de` is turning the last two off in dev. Then: **131** retire (deploy path, alert removal, code-deletion inventory — approvals needed), **132** non-playbook appnotification option fix, **133** Designer read-only for system playbooks, **134** uac-r2 membership/AiAnalysis follow-through (blocked on #1355), **135** Matter Health fix + deactivate demo/junk playbooks (D-101). Sweep report: `notes/playbook-engine-defect-sweep.md` (PB-06 was wrong: based on dead Briefing code).

## Waiting on others

| What | Waiting for | Then |
|---|---|---|
| **#1390** (039 lineage + role config) | uac-r2 approval on #1355 | Owner merge → merge master into `stream/039-signal-writer` → pin test → writer into this branch → 039 live gate (D-67) |
| **#1391** (uac-r2 AppendTo fix; follow-ups done, #1399 → task 126) | uac-r2 approval | Owner merge; task 126 starts when uac-r2 names the business-owned rule |
| uac-r2 on **#1355** | Roles for notification Lookup nodes; access filter for Docs/Emails/Events; D-65 canonical WA create path (046); D-66 write-core move OK (048) | 120's last 3 playbooks; 046; 048 |

## Open owner questions / owner actions

- **Task 130 entity publish** (explained 2026-10-08, no answer yet): when a scoped publish would also publish someone else's pending edits on the same table → **stop unless flagged (recommended)** or warn and continue.
- **Task 130 live proofs** (after re-check): one SpaarkeMaster import, one app-setting import, VisualHost `customcontrols.version` (still 1.4.38 after scoped publish).
- **Owner checklist page** https://claude.ai/artifact/3FPj4oT83f25v2r8yutkMV (db `results/r1..r8`, `status/final`): r1-r5 wizard checks (111), r6 To Do dates (106, incl. LW badge → 121), r7 external app (122, closes #1428), r8 due-date cards/calendar/counts + VisualHost version (129). Read back with ArtifactData `list results`; then close 111, 121, 122, 129.
- **Labelling page** https://claude.ai/artifact/VVoBr33LckyjoUGeWit3aU — owner labelled 24/92; finish (~17 min) → `status/final.complete` → resume `a1bcb0bae3c363ca9` (074 run).
- **Housekeeping (owner, by hand)**: `C:\wtr1386`, `C:\wtrv1460`, `C:\wtrv1480`, `C:\wtrv1480b`, `C:\wtrv1480m`, `C:\wts-b56`, plus the older list in `notes/handoff-history/2026-10.md`. The unused `SpaarkeLegalWorkspace` solution in dev needs a decision/task (no parking).

## Queued (not started)

- **123** (ISS-010 triage enabled filter + ISS-002 failure counters; on this branch), **125** (ISS-001 AI action mirror drift; live writes need approval) — launch when lanes free up.
- **114** — live checklist after #1480 merges (deploy is automatic, D-88).
- **031** — critical path: 039 live gate → 037 → 031 (098 ✅, 106 ✅).
