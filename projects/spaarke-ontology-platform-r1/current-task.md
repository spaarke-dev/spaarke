# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-15 (main session). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path; D-123 secure family;
> D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership; D-138 owner approvals; D-139; D-140) and §6 gotchas.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-145; D-144 amended). Deferred work: one self-contained GitHub issue per item,
> label `ontology-deferred`, plus a DI-nn row in `notes/defer-issues.md` (next free: **DI-93**).
> Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Four agents running: 153 pass 2 (#1953), 059 review (#1964), 154 implementer, 146 third re-run. |
| **Blocker** | None. |
| **Next Action** | (1) 059 review returns, then a fix round and a re-check, then merge. (2) 154 PR, then review, then uac-r2, then merge; then launch 155. (3) 153 pass 2 returns, then fixes, then uac-r2, then merge. (4) 146 results, then review and merge #1933, then the 144/148 report-only pass. |

## Lanes in flight (all PRs into `docs/ontology-platform-design`)

| Task | PR | Head | Worktree | State |
|---|---|---|---|---|
| 059 worklist widget | #1964 | `d8ff6491c` | `C:\wts-059` | Built. The D-111 picker escalated and was split into 154 and 155. Opus adversarial review running. |
| 154 worklist route fields + inquiry lookup | — | — | `C:\wts-154` | Opus/high implementer running. Gates 055. |
| 153 satisfied close writes a Routine DR (D-145b) | #1953 | `672cb8f58` → fix | `C:\wts-153` | Pass 2 found F1 (security): Console User's Deep Create on DRs plus computable keys mean a forged keyed DR suppresses the genuine record. The merged 149 `FindGateRecordAsync` has the same hole. Opus fix round: both lookups also require createdby = the writer; plus the F3 test, the foreign-link hardening (D-128 code), and Warnings on silent orphan and abandon paths. uac-r2 was told the gate lookup changes. |
| 146 child-contract live gate | #1933 | `ef64dbfb6` | `C:\wts-146` | R3 redeployed dev with `c20bff283` (`e276357a7`) and restarted at 20:16-20:18Z; the writer setting survived. A third re-run of items 1, 2, 3, 9, 11 started at 20:33Z (fresh Sonnet agent). |

Merged into the docs branch today: #1926 (master sync), #1903 (151, `fe05faa4f`), #1905 (150, `223cc7d47`), #1907 (149, `5a30d11dc`), #1946 (152, `7fc51a796`; #1921 closed).

## Dev state (spaarke-bff-dev)

- Runs R3's `3115b90f2`, which contains our `f292ad119` and the writer setting `Ontology__Writer__ManagedIdentityClientId=69040982-…`.
- **Writes stay OFF** for 144 (`CoreAncestor__StampReconciliation__ReportOnlyTables`) and 148 (`Ontology__TodoAssigneeOwner__WritesEnabled`). Order: 146 merged → one report-only pass of both → uac-r2 CLEAN → set `ReportOnlyTables=","` and `WritesEnabled=true`. Notify word-add-in, R3 and uac-r2 before and after each restart.

## Next, in order

1. Merge 153 (needs uac-r2 sign-off) and 059.
2. 146 re-run → merge #1933 → 144/148 report-only pass → uac-r2 → enable writes.
3. Re-run `SignalReadRouteLiveTests` as the non-Console user (DI-83 #1882), then close #1882.
4. Docs branch → master merge (tell uac-r2), then the **055** Console deploy, then 049, 064, 105.
5. ebilling: their 070, 013, 015, 026, 050 and 074 come to us for review.

## Promises to peers

- **uac-r2:** tell them when `docs/ontology-platform-design` merges to master (they then update the route-authorization ledger for #1903/#1905/#1907/#1946). On #1953 they check that the gate lookup still cannot be satisfied or blocked by a human review DR (the `sprk_confirmedby IS NULL` anti-spoof property of #1907).
- **R3:** message before the two app-setting restarts (144/148 writes).

## Open owner questions

- Remove `prvCreatesprk_DecisionRecord` (Deep) from **Console User** (`Roles/Spaarke Console User.xml:17`) and **Ontology Administrator** (`:20`) as part of task 049, after 055. Only Ontology Service needs it (Global). uac-r2 confirmed on master that no client, OBO, flow or UAC code creates a DR as a human.
- None open. The owner approved R3's redeploy of `c20bff283` (2026-10-15), and ontology cleared it with the window open now. Wait for R3's SHA, healthz and restart time, then re-run 146 about 15 minutes later. Backlog: DI-82 #1881 (`sprk_assignedto` vs `sprk_assignedtointernal`).

## Peers (current session names)

word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-45`; R3 `spaarke-wt-spa-external-access-platform-49`; uac-r2 `spaarke-wt-unified-access-control-r2-19`; ebilling `spaarke-wt-ebilling-r1-de`; provisioning `spaarke-wt-customer-provisioning-orchest-db`. Quality alert #1906 (client-tests) is not ours.
