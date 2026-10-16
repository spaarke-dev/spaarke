# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-15 (main session, 154 at uac-r2). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path; D-123 secure family;
> D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership; D-138 owner approvals; D-139; D-140) and §6 gotchas.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-145; D-144 amended). Deferred work: one self-contained GitHub issue per item,
> label `ontology-deferred`, plus a DI-nn row in `notes/defer-issues.md` (next free: **DI-101**).
> Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | 154 (#1982) at uac-r2 review (head `19f61eeef`; two Opus passes done, pass-2 F1/F3/K4 fixed). Merged today: 151, 150, 149, 152, 059, 146, 153. |
| **Blocker** | None. |
| **Next Action** | (1) 154: await uac-r2 verdict (PR comment + message), then merge via merge-when-green3.sh 1982 19f61eeef199679ec2e17bb6dd5a58ab5beed274; then launch 155 (sonnet/high, worktree from the docs branch). (2) Docs branch to master: open a PR to master once 154 is in (D-126; tell uac-r2 for their ledger). (3) 055 Console + BFF deploy from master, plus the grid configuration apply. (4) After the owner answers: enable the dev policies, then the 144/148 report-only pass, then uac-r2, then the writes; and 049 role removals. |

## Lanes in flight (all PRs into `docs/ontology-platform-design`)

| Task | PR | Head | Worktree | State |
|---|---|---|---|---|
| 154 worklist route fields + inquiry lookup | #1982 | `19f61eeef` | `C:\wts-154` | Pass 1 fixed in `d23dc4521`: the guard applies only to an open, outbound inquiry; the hint is matched on any line, with a matter fallback on commit; 503 on timeout; K2 and K3. The docs branch with #1953 merged in cleanly (831 + 53 filtered tests pass). Pass 2 fixed in `19f61eeef`: plan shares the commit's inquiry check incl. matter fallback (F1), commit inquiry-fault tests + seeding proof (F3), finishing path skips the guard and matches a review under the reduced plan (K4, hash-covered). K1/K3 stay known limits. uac-r2 review running, then merge, then launch 155. **Live step at the 055 deploy:** `Apply-WorklistGridConfiguration.ps1`. |

Merged into the docs branch today: #1926 (master sync), #1903 (151, `fe05faa4f`), #1905 (150, `223cc7d47`), #1907 (149, `5a30d11dc`), #1946 (152, `7fc51a796`; #1921 closed), #1964 (059, `930c02c7f`), #1933 (146, `495faf82c`), #1953 (153, `53f19f0a2`).

## Dev state (spaarke-bff-dev)

- Runs R3's `3115b90f2`, which contains our `f292ad119` and the writer setting `Ontology__Writer__ManagedIdentityClientId=69040982-…`.
- **Writes stay OFF** for 144 (`CoreAncestor__StampReconciliation__ReportOnlyTables`) and 148 (`Ontology__TodoAssigneeOwner__WritesEnabled`). Order: 146 merged → one report-only pass of both → uac-r2 CLEAN → set `ReportOnlyTables=","` and `WritesEnabled=true`. Notify word-add-in, R3 and uac-r2 before and after each restart.

## Next, in order

1. Merge 154 (uac-r2 review running); then launch 155 (needs 154).
2. 146 re-run → merge #1933 → 144/148 report-only pass → uac-r2 → enable writes.
3. Re-run `SignalReadRouteLiveTests` as the non-Console user (DI-83 #1882), then close #1882.
4. Docs branch → master merge (tell uac-r2), then the **055** Console deploy, then 049, 064, 105.
5. ebilling: their 070, 013, 015, 026, 050 and 074 come to us for review.

## Promises to peers

- **uac-r2:** tell them when `docs/ontology-platform-design` merges to master (they then update the route-authorization ledger for #1903/#1905/#1907/#1946). On #1953 they check that the gate lookup still cannot be satisfied or blocked by a human review DR (the `sprk_confirmedby IS NULL` anti-spoof property of #1907).
- **R3:** message before the two app-setting restarts (144/148 writes).

## Open owner questions

- **Enable the five dev policies?** (4 Do: 6101, 6102, 6103, 6104; 1 Decide: `4d204810…`). Dev has 0 Signals and 1 DR, so the 144/148 report-only pass has nothing to check until the evaluator creates real Signals. The pass is also needed for 055's real-data verification and for UAT. Recommendation: enable all five on dev, run one nightly-equivalent evaluator pass, then the report-only pass, then uac-r2.
- Remove `prvCreatesprk_DecisionRecord` (Deep) from **Console User** (`Roles/Spaarke Console User.xml:17`) and **Ontology Administrator** (`:20`) as part of task 049, after 055. Only Ontology Service needs it (Global). uac-r2 confirmed on master that no client, OBO, flow or UAC code creates a DR as a human.
- None open. The owner approved R3's redeploy of `c20bff283` (2026-10-15), and ontology cleared it with the window open now. Wait for R3's SHA, healthz and restart time, then re-run 146 about 15 minutes later. Backlog: DI-82 #1881 (`sprk_assignedto` vs `sprk_assignedtointernal`).

## Peers (current session names)

word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-45`; R3 `spaarke-wt-spa-external-access-platform-49`; uac-r2 `spaarke-wt-unified-access-control-r2-19`; ebilling `spaarke-wt-ebilling-r1-de`; provisioning `spaarke-wt-customer-provisioning-orchest-db`. Quality alert #1906 (client-tests) is not ours.
