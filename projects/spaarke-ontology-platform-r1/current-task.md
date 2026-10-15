# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-15 (main session). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path; D-123 secure family;
> D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership; D-138 owner approvals; D-139; D-140) and §6 gotchas.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-145). Deferred work: one self-contained GitHub issue per item,
> label `ontology-deferred`, plus a DI-nn row in `notes/defer-issues.md` (next free: **DI-90**).
> Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Merge 150/151 (uac-r2 CLEAN) once CI is green; 149 with uac-r2; 146 live gate running; 152/153 created (D-145). |
| **Blocker** | CI: every ontology PR fails restore on NU1903 (`Microsoft.OpenApi.Readers` 1.2.3, GHSA-7pxr). Sync PR **#1926** (master `f63b57f8d` → docs, clean merge) is merging on green (background watcher). |
| **Next Action** | When #1926 merges: close + reopen #1903 and #1905 (no push; heads stay `98b5a1ee0` / `e5fb90b09`, the SHAs uac-r2 passed) to re-run CI, then `merge-when-green3.sh` each. Then launch 152 (opus, after 150+151) and, after #1907 merges, 153. |

## Lanes in flight (all PRs into `docs/ontology-platform-design`)

| Task | PR | Head | Worktree | State |
|---|---|---|---|---|
| 149 gate DR queue job (D-139, D-144, K3 log → Warning) | #1907 | `b2551050a` | `C:\wts-149` | Pass 2 CLEAN + K round (ArchTest hardening, secure-BU unfiled test). **uac-r2 reviewing at `b2551050a`.** Will need the same close/reopen CI re-run. |
| 150 Do items private to assignee (D-141) | #1905 | `e5fb90b09` | `C:\wts-150` | **uac-r2 CLEAN** (K3/K4 accepted). Merge after CI re-run. uac-r2 updates the route ledger prose after merge. |
| 151 evidence hidden unless witness readable (D-142) | #1903 | `98b5a1ee0` | `C:\wts-151` | **uac-r2 CLEAN** (K2/K3 accepted; K4 Basic User prerequisite added to PR body). Merge after CI re-run. |
| 146 child-contract live gate | — | — | `C:\wts-146` (clean, no commits) | Agent resumed after R3's deploy (it re-queries what already landed). Writer-dependent items 1, 2, 3, 7, 9 on dev. Cleanup owed: ZZ-146-RECIPE policy `f93129b9-9dc8-f111-a05c-0022482913fc`, version `527b54d8-9dc8-f111-a05c-0022482913fc`, matter `0f8514b9-9dc8-f111-a05c-3833c5e9614d`; Signals `zz-146-Sx`/`-Sm`/`recipe`/2× `gate` (5 rows); communications `zz-146-Cx` `534e347f-…`, `zz-146-Cm` `554e347f-…`; their gate DRs `584e347f-…`, `bb67d283-…`. Confirm 0 zz rows. Then PR → review → merge. |

uac-r2 asked for the head SHA with each CLEAN; also post on #1355.

## Dev state (spaarke-bff-dev)

- Runs master `f292ad119` + app setting `Ontology__Writer__ManagedIdentityClientId=69040982-612e-469e-a85f-26d5172367c5` (added 13:46:55Z). Healthz 200.
- 035 done (`c9fd04c66`): writer-failure alert fired on the negative run (13:44:53Z); positive run wrote 1 Signal (13:48:04Z).
- **Writes stay OFF** for 144 (`CoreAncestor__StampReconciliation__ReportOnlyTables`) and 148 (`Ontology__TodoAssigneeOwner__WritesEnabled`) until: 146 makes real Signals → one report-only pass of both → uac-r2 CLEAN → set `ReportOnlyTables=","` and `WritesEnabled=true` (BFF restarts: notify word-add-in, R3, uac-r2 before and after).
- Dev runs R3's `9bb7f5724` (R3 + master `f292ad119`) since R3's deploy; healthz 200, writer setting intact (R3 confirmed). R3 knows the negative alert test is done.

## Next, in order

1. Merge 149 / 150 / 151 as each goes CLEAN; mark them ✅ in `TASK-INDEX.md`.
2. 146 PR → review → merge; then the 144/148 report-only pass → uac-r2 → enable writes.
3. Re-run `SignalReadRouteLiveTests` as the non-Console user (DI-83 #1882; Basic User has `prvReadsprk_Policy` Deep) → close #1882.
4. **059** worklist widget (render `withheld: true` evidence lines, not "No value recorded") → **055** Console deploy → 049, 064, 105.
5. ebilling-r1 PRs for review when they open: their tasks 013, 015, 026, 050, 074 (their D-71, D-75).

## Open owner questions

- None. D-145 answered #1921 (tasks 152, 153). Still open in the backlog: DI-82 #1881 (`sprk_assignedto` vs `sprk_assignedtointernal`).

## Peers (current session names)

word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-45`; R3 `spaarke-wt-spa-external-access-platform-49`; uac-r2 `spaarke-wt-unified-access-control-r2-19`; ebilling `spaarke-wt-ebilling-r1-de`; provisioning `spaarke-wt-customer-provisioning-orchest-db`. Quality alerts #1898 and #1906 are not ours.
