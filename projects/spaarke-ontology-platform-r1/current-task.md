# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-17 ~07:30Z (main session; #2066 102 merged). State only — history is in git log,
> task `<completion>` blocks and `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path;
> D-123 secure family; D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership; D-138 owner approvals; D-139; D-140) and §6.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-159). Deferred work: one self-contained GitHub issue per item, label
> `ontology-deferred`, plus a DI-nn row in both tables of `notes/defer-issues.md` (next free: **DI-114**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | No lane running. 102 done (#2066 `b9603bab3`); 064 merged, switch pending owner |
| **Next Action** | docs→master sync PR (#2048, #2050, #2053, 102a, 064, 102; full ArchTests; endpoint census; sync branch in `C:\wts-sync`), then ask R3 for a BFF deploy (063 join + 102a routes). Owner questions below |

## Lanes in flight

Shared brief: scratchpad `lane-brief.md`. Each lane: review → fix round → re-check → merge into `docs/ontology-platform-design`.

| Task | Worktree / branch | Model | State |
|---|---|---|---|
| 064 replace Briefing tab | `C:\wts-064` `task/ontology-064` | — | **PR #2055 merged `640794bf2`** (reviewed, fix round, re-check). Task stays wip: the switch (`Set-DefaultWorkspaceLayout.ps1 -To Worklist`, runbook `notes/briefing-cutover.md` §6.2) waits on the §6.1 non-admin checks or an owner waiver, then owner approval; notify word-add-in, R3, uac-r2 before/after. Follow-ups DI-109..DI-111 (#2058, #2057, #2059) + #2006 comment |
## Open owner questions

1. **D-160 (relayed by uac-r2): keep admin Assign/Delete on `sprk_signal`.** Recorded; ask the owner to confirm directly (peer relay is not an approval, but it is a no-change decision).
2. **111 row 4**: external SPA live on dev (R3 run 39004032735, 04:51Z, R3 branch `88697cfc1`). Owner checks https://green-dune-0c4f1221e.7.azurestaticapps.net/upload (direct route, no in-app link per R3); then close 111. **Never dispatch `deploy-external-spa.yml` from master** (task 122) while R3's SPA work (050–057, 061, 066, UAC 185) is off master.
3. **101(c) A/B** (102a pass 1 F4): related-table filter columns are a value oracle, not one bit. A = amend + re-sign the spec §6 tension and D-151 (recommended); B = narrow the allow-list (breaks POL-COMMIT-BUDGET).
4. **POL-MATTER-NOBUDGET** enable (~58 Signals)?
5. **064 switch**: §6.1 non-admin checks done or waived, then approve the switch.
6. **Rule scope in R1** (102 review): the save route takes no tenant/matter, so the wizard shows scope read-only. Accept for R1, or add scope to the BFF?

## Next, in order

1. 049 done (#2050 merged `32a923ca4`).
2. Lane results: 102a (two passes + uac-r2), then relaunch 102; 064 (review, merge; ships with the next docs→master sync).
3. ebilling: 026 fix round re-check CLEAN (reported 2026-10-17, minor K1/K3/K4 + task-130 note sent). 070 PR (paired F3 change) comes back for re-check. Temp worktrees `C:\wts-eb070`, `C:\wts-eb026`.
4. Re-run `SignalReadRouteLiveTests` as the non-Console user (DI-83 #1882), close #1882.
5. 064 (after 063), 114 (after 111), 105 UAT (include the Next step To Do not exercised in 055), 090 wrap-up.
6. docs→master sync for #2048, the 049 PR, 063, 102a (sync branch in `C:\wts-sync`; full ArchTests first; endpoint census recompute — 102a adds a route).
7. Cleanup: `C:\wts-eb070`, `C:\wts-eb026`, `C:\wts-sync`, `C:\wts-di107`, `C:\wb126-pub`, `C:\wts-126-pub` via `scripts/worktree/Remove-WorktreeSafe.ps1`.

## Dev state (spaarkedev1 / spaarke-bff-dev)

- BFF `1.0.0+28720f9976c0…` (R3, 2026-10-17 06:03:49Z; master 1d435e695, contains e702c3401 = 100/101/103; NOT 063 join or 102a routes, which await the next docs→master sync). healthz 200, writer MI assigned.
- Console from master `a8c3182ec`; Worklist + Decision Records layouts seeded. Five policies on.
- Roles: Ontology Service 58 (§15); Ontology Administrator 38 (§16 +2 Append, §17 −DR Create −Signal Write); Console User 21 (§17).

## Promises to peers

- **R3** owns the dev BFF line and the external SPA: message first for any BFF change.
- **word-add-in**: before/after notices for any Console or BFF change.
- **uac-r2**: before/after notices for live role changes; PRs for role XML.
- **ebilling**: told D-92 confirmed (D-153) and the writer AppendTo is live.

## Peers (current session names)

uac-r2 `spaarke-wt-unified-access-control-r2-c8`; R3 `spaarke-wt-spa-external-access-platform-75`; ebilling `spaarke-wt-ebilling-r1-99`;
word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-54`; provisioning `spaarke-wt-customer-provisioning-orchest-bb`.
