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
| **Task** | 149 F1 fix round (D-144 amended); 146 re-run of blocked items after R3's deploy; 152 ready to launch. |
| **Blocker** | None. |
| **Next Action** | (1) 149 fix SHA → uac-r2 re-check → empty CI commit not needed if the fix push itself triggers SDAP CI → merge on green. (2) When R3 reports their deploy done, re-run 146 items 1, 2, 3, 9, 11 (commit-route DRs) on dev, then review + merge #1933. (3) Launch 152 (opus/high) from `origin/docs/ontology-platform-design`. |

## Lanes in flight (all PRs into `docs/ontology-platform-design`)

| Task | PR | Head | Worktree | State |
|---|---|---|---|---|
| 149 gate DR queue job (D-139, D-144 amended) | #1907 | `b2551050a` → fix | `C:\wts-149` | uac-r2 F1: BU default-team owner exposed subject + privilege flag to the whole BU. Owner ruling ([comment](https://github.com/spaarke-dev/spaarke/pull/1907#issuecomment-6198338714)): owner = the communication's own owner after secure-if-any; missing/unreadable owner → skip; Console User / Ontology Admin Deep-read exposure accepted. Sonnet fix round running (same agent as the K round). |
| 146 child-contract live gate | #1933 | `02b2da9d8` | `C:\wts-146` | Docs-only PR, not merged. Pass: 5, 6, 7, 10; partial 4, 9; blocked 1, 2, 3, 8, 11. K-c (writer lacked AppendTo on contact) **fixed** 17:38:31Z (security-roles §13). Re-run 1, 2, 3, 9, 11 after R3's deploy. Item 8 gap (out-of-band Make Secure not re-owned in 420 s; uac-r2 reconcile `mode=ReportOnly` on dev) asked of uac-r2. Test data all deleted. |

Merged today into the docs branch: #1926 (master sync, NU1903 pin), #1903 (151, `fe05faa4f`), #1905 (150, `223cc7d47`).

## Dev state (spaarke-bff-dev)

- R3 is deploying `20b187d07` (R3 + master `f63b57f8d`, contains our `f292ad119`) with our "go"; they report SHA + healthz. Writer setting `Ontology__Writer__ManagedIdentityClientId=69040982-…` must survive (it did last time).
- **Writes stay OFF** for 144 (`CoreAncestor__StampReconciliation__ReportOnlyTables`) and 148 (`Ontology__TodoAssigneeOwner__WritesEnabled`) until: real Signals exist → one report-only pass of both → uac-r2 CLEAN → set `ReportOnlyTables=","` and `WritesEnabled=true` (BFF restarts: notify word-add-in, R3, uac-r2 before and after).

## Next, in order

1. 149 merge; mark ✅. Then launch **153** (D-145b, after 149).
2. **152** (D-145a, Do-lane suppression per person) — deps 150/151 merged; launch now.
3. 146 re-run → #1933 merge → 144/148 report-only pass → uac-r2 → enable writes.
4. Re-run `SignalReadRouteLiveTests` as the non-Console user (DI-83 #1882) → close #1882.
5. **059** worklist widget (render `withheld: true` evidence lines) → **055** Console deploy → 049, 064, 105.
6. ebilling: their 070 (policy readers filter `sprk_domain` null OR Ontology, admin tab too) comes to us for review; they message base + files first. Their 013/015/026/050/074 also come to us.

## Open owner questions

- None. Backlog: DI-82 #1881 (`sprk_assignedto` vs `sprk_assignedtointernal`).

## Peers (current session names)

word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-45`; R3 `spaarke-wt-spa-external-access-platform-49`; uac-r2 `spaarke-wt-unified-access-control-r2-19`; ebilling `spaarke-wt-ebilling-r1-de`; provisioning `spaarke-wt-customer-provisioning-orchest-db`. Quality alert #1906 (client-tests) is not ours.
