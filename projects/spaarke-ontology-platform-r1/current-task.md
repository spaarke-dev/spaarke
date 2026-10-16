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
| **Task** | 055 prep. 155 merged (#1990); docs→master PR for it open. #1989 already on master. R3 redeploying the dev BFF from `eb5ba4011` (R3 + master `7315b3e85`). |
| **Blocker** | 055 BFF deploy: dev runs R3's `c20bff283`, NOT an ancestor of master (89 R3-only commits). A master-only BFF deploy would remove R3's work. Asked R3 (twice) for a plan; preferred (a) R3 merges master and redeploys the dev BFF from R3, ontology deploys only the Console + grid config. |
| **Next Action** | (1) Merge the docs→master PR (155). (2) R3's answer, then 055: before any deploy run `git merge-base --is-ancestor <live sha> <deploy sha>`; Console solution from master; `Apply-WorklistGridConfiguration.ps1` (-WhatIf, -Verify, apply, -Verify -RequireRow); notices before/after to word-add-in, R3, uac-r2. (3) D-146: once R3 confirms the BFF live, PATCH `sprk_enabled=true` on the five policies, trigger `POST /api/admin/jobs/signal-reevaluation/trigger` (token: `az account get-access-token --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c`), read history; then the 144/148 report-only pass, then uac-r2, then the writes. 049 (D-17/D-62/D-147) after 055. |

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
