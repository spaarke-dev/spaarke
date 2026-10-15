# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-15 (main session). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path; D-123 secure family;
> D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership; D-138 owner approvals; D-139; D-140) and §6 gotchas.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-144). Deferred work: one self-contained GitHub issue per item,
> label `ontology-deferred`, plus a DI-nn row in `notes/defer-issues.md` (next free: **DI-90**).
> Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Lanes 149 / 150 / 151 in review; 146 live gate running. |
| **Blocker** | None. One owner question open (below). |
| **Next Action** | Act on each agent result as it lands: 150 scoped re-check → CLEAN SHA to uac-r2; 149 and 151 adversarial pass 2 → fix round if F, else CLEAN SHA to uac-r2. Merge each on uac-r2 CLEAN + green with `scratchpad/merge-when-green3.sh <pr> <sha> docs/ontology-platform-design`. |

## Lanes in flight (all PRs into `docs/ontology-platform-design`)

| Task | PR | Head | Worktree | State |
|---|---|---|---|---|
| 149 gate DR queue job (D-139, D-144, K3 log → Warning) | #1907 | `fe7c5cd0d` | `C:\wts-149` | Adversarial pass 2 running. uac-r2 looks hardest at D-144 ownership. |
| 150 Do items private to assignee (D-141) | #1905 | `e5fb90b09` | `C:\wts-150` | Fix round for pass-2 F4 (resume / repair replay skipped "mine") done; scoped Sonnet re-check running. PR body needs pass-2 K1–K4: K1 oracle only under faults; K2 ledger-peek fault → 404 not 503; K3 gate copies worklist error classification, derives subject table from lookup, repeats WhoAmI per Do id; K4 pre-150 ledger entries. |
| 151 evidence hidden unless witness readable (D-142) | #1903 | `98b5a1ee0` | `C:\wts-151` | Adversarial pass 2 running. DI-89 (#1913) filed for the write-time limit. |
| 146 child-contract live gate | — | — | `C:\wts-146` | Agent running writer-dependent items 1, 2, 3, 7, 9 on dev. Cleanup owed: ZZ-146-RECIPE policy `f93129b9-9dc8-f111-a05c-0022482913fc`, version `527b54d8-9dc8-f111-a05c-0022482913fc`, matter `0f8514b9-9dc8-f111-a05c-3833c5e9614d`, test Signal; confirm 0 zz rows. Then PR → review → merge. |

uac-r2 asked for the head SHA with each CLEAN; also post on #1355.

## Dev state (spaarke-bff-dev)

- Runs master `f292ad119` + app setting `Ontology__Writer__ManagedIdentityClientId=69040982-612e-469e-a85f-26d5172367c5` (added 13:46:55Z). Healthz 200.
- 035 done (`c9fd04c66`): writer-failure alert fired on the negative run (13:44:53Z); positive run wrote 1 Signal (13:48:04Z).
- **Writes stay OFF** for 144 (`CoreAncestor__StampReconciliation__ReportOnlyTables`) and 148 (`Ontology__TodoAssigneeOwner__WritesEnabled`) until: 146 makes real Signals → one report-only pass of both → uac-r2 CLEAN → set `ReportOnlyTables=","` and `WritesEnabled=true` (BFF restarts: notify word-add-in, R3, uac-r2 before and after).
- R3 has not redeployed yet; they notify us when they do. Our app settings survive their deploy.

## Next, in order

1. Merge 149 / 150 / 151 as each goes CLEAN; mark them ✅ in `TASK-INDEX.md`.
2. 146 PR → review → merge; then the 144/148 report-only pass → uac-r2 → enable writes.
3. Re-run `SignalReadRouteLiveTests` as the non-Console user (DI-83 #1882; Basic User has `prvReadsprk_Policy` Deep) → close #1882.
4. **059** worklist widget (render `withheld: true` evidence lines, not "No value recorded") → **055** Console deploy → 049, 064, 105.
5. ebilling-r1 PRs for review when they open: their tasks 013, 015, 026, 050, 074 (their D-71, D-75).

## Open owner question

- Should a Do item's dismissals count only toward its assignee? Today Do-lane suppression is keyed (policy, core), so a colleague's Do dismissals can suppress the caller's items (150 K item). Recommendation: yes, per assignee. Still open: DI-82 #1881 (`sprk_assignedto` vs `sprk_assignedtointernal`).

## Peers (current session names)

word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-45`; R3 `spaarke-wt-spa-external-access-platform-49`; uac-r2 `spaarke-wt-unified-access-control-r2-19`; ebilling `spaarke-wt-ebilling-r1-de`; provisioning `spaarke-wt-customer-provisioning-orchest-db`. Quality alerts #1898 and #1906 are not ours.
