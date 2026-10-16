# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-15 (main session, master merged; 155 in review). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path; D-123 secure family;
> D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership; D-138 owner approvals; D-139; D-140) and §6 gotchas.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-145; D-144 amended). Deferred work: one self-contained GitHub issue per item,
> label `ontology-deferred`, plus a DI-nn row in `notes/defer-issues.md` (next free: **DI-103**).
> Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | 055 prep. Master `44b6a72c1` has all ontology work through 155 (#1989, #1996). Console built from master in worktree `C:\wts-055` (detached at `44b6a72c1`). |
| **Blocker** | Waiting for R3 to confirm the dev BFF is live on `eb5ba4011` (R3 + master `7315b3e85`; owner-approved plan (a); live `e276357a7` is its ancestor). Ontology does not touch the BFF. |
| **Next Action** | After R3 confirms (version + healthz): (1) notify word-add-in + uac-r2 (before); (2) D-146: PATCH `sprk_enabled=true` on policies 6101-6104 + `4d204810…`, trigger `POST /api/admin/jobs/signal-reevaluation/trigger` (token `az account get-access-token --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c`), read history; (3) grid config from `C:\wts-055`: `-Verify` showed DRIFT, `-WhatIf` = one UPDATE of row `…5010`; apply, then `-Verify -RequireRow`; (4) `pwsh scripts/Deploy-SpaarkeAi.ps1` from `C:\wts-055` (publishes only `sprk_spaarkeai`, D-83 ok); (5) 055 real-data checks (owner in the browser) + notify after; (6) 144/148 report-only pass → uac-r2 → writes; then 049. |

## Lanes in flight

None. 155 merged (#1990, `493bb8a49`); going to master in a docs→master PR for the 055 Console deploy.

## Dev state (spaarke-bff-dev)

- Runs R3's `1.0.0+e276357a7` (src `c20bff283` = R3 + master `98c5962d7`). It has our `f292ad119`, not #1907 or later.
- **Writes stay OFF** for 144 (`CoreAncestor__StampReconciliation__ReportOnlyTables`) and 148 (`Ontology__TodoAssigneeOwner__WritesEnabled`). Order: one report-only pass of both, then uac-r2 CLEAN, then `ReportOnlyTables=","` and `WritesEnabled=true`. Notify word-add-in, R3 and uac-r2 before and after each restart.
- 0 Signals, 1 DR (another lane's); all five policies disabled.

## Next, in order

1. 155 review → merge.
2. 055 deploy (coordinated with R3, above).
3. D-146 policies on (after R3's BFF) → evaluator pass → 144/148 report-only pass → uac-r2 → writes.
4. Re-run `SignalReadRouteLiveTests` as the non-Console user (DI-83 #1882), close #1882.
5. 049 (with the DR Create removal if approved), 062, 063, 064, 105, 045, 104.
6. ebilling: their ConsoleKit PR plus 070, 013, 015, 026, 050, 074 come to us for review.

## Promises to peers

- **uac-r2:** notify before/after the 055 deploy; they run the ledger follow-up on master.
- **R3:** do not touch the dev BFF until their plan is agreed; message before the 144/148 app-setting restarts.

## Open owner questions

- None. Answered 2026-10-15: D-146 (enable the five dev policies after R3's BFF is live, then one manual `signal-reevaluation` pass) and D-147 (049 also removes DR Create from Ontology Administrator).

## Peers (current session names)

word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-45`; R3 `spaarke-wt-spa-external-access-platform-49`; uac-r2 `spaarke-wt-unified-access-control-r2-19`; ebilling `spaarke-wt-ebilling-r1-de`; provisioning `spaarke-wt-customer-provisioning-orchest-db`. Quality alert #1906 (client-tests) is not ours.
