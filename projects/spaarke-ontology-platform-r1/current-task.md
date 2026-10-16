# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-16 ~02:45Z (main session; 055 deployed, worklist unreachable → task 156 running). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path; D-123 secure family;
> D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership; D-138 owner approvals; D-139; D-140) and §6 gotchas.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-147). Deferred work: one self-contained GitHub issue per item,
> label `ontology-deferred`, plus a DI-nn row in `notes/defer-issues.md` (next free: **DI-103**).
> Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | 156 — make the worklist reachable in the Console (section shim + "Worklist" `sprk_workspacelayout` row). Gates 055's browser check. |
| **Status** | Implementer (sonnet/high) running in `C:\wts-156`, branch `task/ontology-156-worklist-reachable`; it opens a PR into `docs/ontology-platform-design` and does not merge. |
| **Next Action** | On its report: independent review → fix round → re-check → merge (`merge-when-green3.sh`, run_in_background) → docs→master PR → update `C:\wts-055` to new master, rebuild Console, before-notices (word-add-in, R3, uac-r2), `pwsh scripts/Deploy-SpaarkeAi.ps1`, hash check, after-notices → run `scripts/seed-worklist-workspace-layout.ps1 -WhatIf` then for real on dev → owner browser check (below). |

## Lanes in flight

- 156 (above). Nothing else.

## Dev state (spaarkedev1 / spaarke-bff-dev)

- BFF `1.0.0+eb5ba4011` (R3 + master `7315b3e85`), deployed by R3. Ontology does not deploy the BFF.
- Console (`sprk_spaarkeai`) live from master `44b6a72c1`, 01:56:32Z, hash `0470e7f1…f23f2`. The worklist widget is in the bundle but no dropdown entry reaches it (task 156).
- Grid config row `…5010` in sync.
- D-146: five policies enabled; manual evaluator pass 01:56Z wrote 87 Signals (Open task 21, To Do 43, WA 19, Decide 4, Coming due 0), all team-owned.
- 144/148 writes ON since 02:26:42Z (D-138 + owner "yes approve"). 144 repaired 50 at 02:30, then 0 stale of 130 at 02:35; 148 0 stale. Nightly evaluator 02:30: 87 unchanged, 0 written. Notices sent to R3, word-add-in, uac-r2.

## 055 owner browser check (after 156 deploy)

Open https://spaarkedev1.crm.dynamics.com/WebResources/sprk_spaarkeai → workspace dropdown → Worklist. Check: both lanes render; one full review (Send budget inquiry with recipient = self, Revise budget, Next step Add To Do); network log; no console errors; Daily Briefing still fine. Then the main session verifies the DR, budget revision, To Do and Signal = Acted in Dataverse, marks 055 done and updates `notes/deploy-log.md`.

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
