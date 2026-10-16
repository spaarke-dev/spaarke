# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-16 ~03:50Z (main session; 156 deployed, waiting for the owner's 055 browser check). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path; D-123 secure family;
> D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership; D-138 owner approvals; D-139; D-140) and §6 gotchas.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-147). Deferred work: one self-contained GitHub issue per item,
> label `ontology-deferred`, plus a DI-nn row in `notes/defer-issues.md` (next free: **DI-104**).
> Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | 055 — waiting for the owner's browser check. 156 done (#2004 → master #2008 `a8c3182ec`). |
| **Status** | Console live from master `a8c3182ec` (03:44:12Z, hash `06d39356…218e1`); "Worklist" system layout row `202bcde0-…` seeded (sortorder 13). Peers notified. |
| **Next Action** | When the owner reports the browser check: verify in Dataverse the DR, budget revision, To Do and Signal = Acted; then mark 055 done (POML + TASK-INDEX) and finish `notes/deploy-log.md`. |

## Lanes in flight

- None.

## Dev state (spaarkedev1 / spaarke-bff-dev)

- BFF `1.0.0+eb5ba4011844…` (R3's line; read the version via Kudu VFS, project CLAUDE.md §6).
- Console from master `a8c3182ec`; Worklist reachable via the workspace dropdown.
- Grid config row `…5010` in sync. Five policies on; 87 Signals. 144/148 writes on and converged; uac-r2 post-enable check CLEAN.

## 055 owner browser check

Open https://spaarkedev1.crm.dynamics.com/WebResources/sprk_spaarkeai → workspace dropdown → Worklist. Check: both lanes render; one full review (Send budget inquiry with recipient = self, Revise budget, Next step Add To Do); network log; no console errors; Daily Briefing still fine.

## Next, in order (after 055)

1. 049 (Signal Write + DR Create removals from users and Ontology Administrator, D-147; PR to uac-r2 first).
2. Re-run `SignalReadRouteLiveTests` as the non-Console user (DI-83 #1882), close #1882.
3. 062, 063, 064, 105, 045, 104.
4. ebilling: their ConsoleKit PR plus 070, 013, 015, 026, 050, 074 come to us for review.

## Promises to peers

- **R3** owns the dev BFF line: message first for any BFF change; `git merge-base --is-ancestor <live> <deploy>` before any deploy. R3 asked how to read ProductVersion (asked them back).
- **word-add-in** (owner UAT on the Console): before/after notices with start/finish times for any Console or BFF change.
- **uac-r2:** notify before/after deploys; PR for 049 before the live role removal.

## Open owner questions

- None.

## Peers (current session names)

word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-45`; R3 `spaarke-wt-spa-external-access-platform-49`; uac-r2 `spaarke-wt-unified-access-control-r2-19`; ebilling `spaarke-wt-ebilling-r1-de`; provisioning `spaarke-wt-customer-provisioning-orchest-db`. Quality alert #1906 (client-tests) is not ours.
