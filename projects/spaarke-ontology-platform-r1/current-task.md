# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-15 (main session). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path; D-123 secure family;
> D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership; D-138 owner approvals; D-139; D-140) and §6 gotchas.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-145; D-144 amended). Deferred work: one self-contained GitHub issue per item,
> label `ontology-deferred`, plus a DI-nn row in `notes/defer-issues.md` (next free: **DI-92**).
> Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | 152 (#1946) uac-r2 F3 fix round; 153 (#1953) adversarial pass 1; 146 blocked on the dev BFF connection's privilege cache. |
| **Blocker** | 146 items 1, 2, 3, 9, 11 need a dev BFF restart (my restart or R3's #1949 redeploy). R3 is redeploying `c20bff283` (owner-approved); waiting for R3 to report it. |
| **Next Action** | (1) 152 agent `af490c65f025a85b5` returns the F3 test SHA, then send it to uac-r2 for a re-check, then merge on green, then close #1921. (2) 153 pass-1 reviewer `a97a4b7bbe382f169` returns, then a fix round that keeps uac-r2's anti-spoof property, then pass 2, then uac-r2, then merge. (3) After the BFF restarts, wait about 15 minutes, then resume the 146 agent to re-run its blocked items. |

## Lanes in flight (all PRs into `docs/ontology-platform-design`)

| Task | PR | Head | Worktree | State |
|---|---|---|---|---|
| 152 Do-lane suppression per person (D-145a) | #1946 | `06c5dd473` → fix | `C:\wts-152` | uac-r2 F3 is the Do-lane secure wall. Test: Bob's 3 dismissals in secure family S must not stamp or mute his open Do item in M's ordinary partition, with a seeding proof. Agent running. |
| 153 satisfied close writes a Routine DR (D-145b) | #1953 | `09e2d6432` | `C:\wts-153` | Adversarial pass 1 is running and was given uac-r2's anti-spoof check: `FindGateRecordAsync` matched only `sprk_confirmedby IS NULL`, and the switch to `Plan.IsGate` must not let a human, condition-cleared or crafted-`sprk_steps` DR satisfy or block the gate lookup. Also watch: "a failed record write fails the run" vs the fail-closed rule and the evaluator-stalled alert; the 7-day backfill. |
| 146 child-contract live gate | #1933 | `ef64dbfb6` | `C:\wts-146` | Docs only. Items 5, 6, 7, 10 pass; 4 and 9 are partial; 8 is the UAC known limit. Items 1, 2, 3, 9, 11 are refused because the BFF connection still caches the writer's privileges after the AppendTo-contact grant (`RetrievePrincipalAccess` flipped at 17:51Z). All test data is deleted. |

Merged into the docs branch today: #1926 (master sync), #1903 (151, `fe05faa4f`), #1905 (150, `223cc7d47`), #1907 (149, `5a30d11dc`).

## Dev state (spaarke-bff-dev)

- Runs R3's `3115b90f2`, which contains our `f292ad119` and the writer setting `Ontology__Writer__ManagedIdentityClientId=69040982-…`.
- **Writes stay OFF** for 144 (`CoreAncestor__StampReconciliation__ReportOnlyTables`) and 148 (`Ontology__TodoAssigneeOwner__WritesEnabled`). Order: 146 merged → one report-only pass of both → uac-r2 CLEAN → set `ReportOnlyTables=","` and `WritesEnabled=true`. Notify word-add-in, R3 and uac-r2 before and after each restart.

## Next, in order

1. Merge 152 and 153 (each needs uac-r2 sign-off).
2. 146 re-run → merge #1933 → 144/148 report-only pass → uac-r2 → enable writes.
3. Re-run `SignalReadRouteLiveTests` as the non-Console user (DI-83 #1882), then close #1882.
4. **059** worklist widget, then the **055** Console deploy, then 049, 064, 105.
5. ebilling: their 070, 013, 015, 026, 050 and 074 come to us for review.

## Promises to peers

- **uac-r2:** tell them when `docs/ontology-platform-design` merges to master (they then update the route-authorization ledger for #1903/#1905/#1907/#1946). On #1953 they check that the gate lookup still cannot be satisfied or blocked by a human review DR (the `sprk_confirmedby IS NULL` anti-spoof property of #1907).
- **R3:** message before the two app-setting restarts (144/148 writes).

## Open owner questions

- None open. The owner approved R3's redeploy of `c20bff283` (2026-10-15), and ontology cleared it with the window open now. Wait for R3's SHA, healthz and restart time, then re-run 146 about 15 minutes later. Backlog: DI-82 #1881 (`sprk_assignedto` vs `sprk_assignedtointernal`).

## Peers (current session names)

word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-45`; R3 `spaarke-wt-spa-external-access-platform-49`; uac-r2 `spaarke-wt-unified-access-control-r2-19`; ebilling `spaarke-wt-ebilling-r1-de`; provisioning `spaarke-wt-customer-provisioning-orchest-db`. Quality alert #1906 (client-tests) is not ours.
