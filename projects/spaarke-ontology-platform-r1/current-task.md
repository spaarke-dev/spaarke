# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-11-15 ~16:10Z (by context-handoff, at 86% context). State only; history in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-11.md` (previous checkpoint archived there verbatim). Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 and §6.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..**D-224**). Deferred work: one GitHub issue per item, label `ontology-deferred`,
> plus a DI-nn row in both tables of `notes/defer-issues.md` (next free: **DI-156**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Role** | Main session `ontology-r1`: orchestrate R1; review every PR touching ontology-owned files (`Services/Signals/**`, `Api/Signals/**`, `FinanceBaseValues.cs`, `Services/Signals/Billing/**`, the DecisionWizard UI); answer peers; hold wrap-up 090 until owner UAT done. Peer messages are not owner approvals. |
| **Working dir** | `C:\code_files\spaarke-wt-spaarke-ontology-platform-r1`, branch `docs/ontology-platform-design` (pushed). |
| **Active** | **Task 185 🔄 [wip]: DEPLOYED to dev 2026-11-15 14:48–14:52Z from master `acc96e1927`** (owner go "Yes proceed"): Console auto-deploy `f7174f6fc1`; 13 code-page web resources (9 create/document wizards + wizard_commands, SmartTodo, SpeAdmin, DocumentRelationshipViewer) and PCFs SemanticSearch 1.1.86 / CommunicationActions 1.3.22 / TrackingFieldTrio 1.0.64, all read back by SHA-256, scoped publishes only. Rebuilt PCF bundles recorded on master by #2836 (`d03537e133`). Evidence: `notes/deploy-log.md` (2026-11-15), `notes/task-185-notes.md`. |
| **Running** | Nothing. |
| **Next Action** | (1) **Owner runs `notes/dev-test-checklist-185.md` A–D** → record results in deploy-log + task-185-notes, then close 185 (POML completed + TASK-INDEX ✅ + `check-task-status-drift.ps1`). (2) Owner checks still owed: 192 default text, `notes/dev-test-checklist-190-191.md`, 160 visual checks (ride the 176 test). (3) Peers below. |

## Open items

**Owner**
- 185 checks (above). 192 / 190 / 191 checks. 160 open (visual checks ride the 176 test; zz- comm "zz- UAT 176 fee or scope change (budget inquiry) - MAT-34929-0040" exists; delete it after).
- Owner's half-done 176 decision (budget revised + SR created, no email/DR, Signal closed, 2026-11-13): needs owner choice on clean-up — ask, do not act.
- ebilling 095f part 2 options (adjustments A/B, correct/confirm A/B/C, Create Invoice path) are the owner's.
- Temp folders `C:\t191m`, `C:\t191p` for owner to delete. 105 (v4 UAT checklist) open; 189 (Office add-in templates route) not written. **090 held.**

**ebilling-r1**
- **#2839** (work→master, head `ced184c79a`): ontology sign-off POSTED (comment 6897983607) for writer/catalog/RecordProcedure tests/immutability test/BillingJudgedDates+evaluator, byte-identical to reviewed `4356eef89b` / `84e708682c`; **conditional on CI green**; any later push touching Signals → re-glance.
- 084h design PASSED (73ce24172c). 084h-1 reviewed in 2 rounds (PASS). Carried: **K1** — 084h-3 repair job retry list must classify BillingRecordLookupFailed, OwnerRefused (deleted core; needs a test), OwnerResolutionFailed, CreateFailed; **H-3** — 084h-2 service enforces the 2000-char note cap. Review both when 084h-2/3 arrive.
- 095f part 2 design PASS (fixes 1–3, Q6–Q8); catalog/exact-set changes come to us in its PR.
- **Held PRs ours:** 174 (#2580, head `9423f37004`) and 175 (#2579) wait for ebilling 112 (Invoice Review) on dev; then merge both, send SHAs to uac-r2, **and tell mcp-r1** (#2587 waits on them).

**mcp-r1**
- **#2587** (work/spaarkeai-mcp-bff-api-r1): ontology review PASS (one adversarial pass; F4 idempotent hint fixed in `efa348273f`); sign-off comment 6900723738. **Condition: must not merge before #2580 and #2579** (branch carries 174/175). Then DI-143 #2630 builds on `WorkItemPrivilege`.

**uac-r2** — dev BFF deploy owner. Dev BFF = `f451e0874d` (master `ce6c241964` + mcp spike, 2026-11-16 00:44Z; has a M.E.AI package mismatch that breaks AI draft, fix in their next build).

**Ours, after the critical path:** DI-144 #2632, DI-145 #2639, DI-146 #2650, DI-147 #2653, DI-148 #2671, DI-149 #2672, DI-154 #2813, DI-155 #2814; `OntologyAdminClassificationService.cs:658/685/705` waits for `sprk_publishstate`; stale `DecisionRecordWriter.cs` comments wait for ebilling 074; close #2212 when ebilling `f0884a175f` reaches master. 176/160 run findings (Graph sentitems 400 after sendMail, `sprk_pendingspend` PATCH 400, `sprk_regardingreportcard` query on thread): check whether filed; detail archived in `notes/handoff-history/2026-11.md`.

**Worktree cleanup** (`scripts/worktree/Remove-WorktreeSafe.ps1` only): task174 `agent-a87a71b050eceafb1` under `C:/code_files/spaarke/.claude/worktrees/` after #2580/#2579 merge. `C:\wtdep185`, `C:\tmp\rv-084h1`, `C:\tmp\rev2587-850451d` removed 2026-11-15.

## Dev state

- Every dev BFF deploy runs in uac-r2's session with the owner's go per SHA. Console auto-deploys from master (CLAUDE.md §6). Code pages/PCFs: ours, after owner go, from master (D-126).
- Not on dev (nothing to update): `sprk_communicationpage` (CommunicationPage), `sprk_rulereviewpage.html`.
- Stash: two old `lint-staged automatic backup` entries are not ours. Leave them.

## Peers (re-check with ListAgents)

Desktop: `uac-r2` · `provisioning-r1` · `ebilling-r1` · `mcp-r1` · `word-add-in-r1` · `acclerator-r1` · `spa-r4` · `field-mapping-r1` · `accelerator-claude-r2`.
