# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-17 ~03:45Z (main session; owner answered the 11 open questions, D-149..D-159). State only — history is in git log,
> task `<completion>` blocks and `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path;
> D-123 secure family; D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership; D-138 owner approvals; D-139; D-140) and §6.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-159). Deferred work: one self-contained GitHub issue per item, label
> `ontology-deferred`, plus a DI-nn row in both tables of `notes/defer-issues.md` (next free: **DI-109**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Lane 102a running; 063 PR #2053 in Opus review |
| **Status** | 055 done, 073 deferred (#2051), admin Append live (§16), 049 live (§17), #2048 merged `adc213d9b` (#2046 closed) |
| **Next Action** | On each lane notification: independent review → fix round → re-check → merge into docs. Merge #2050 after its Opus pass (uac-r2 already OK) and mark 049 done |

## Lanes in flight

Shared brief: scratchpad `lane-brief.md`. Each lane: review → fix round → re-check → merge into `docs/ontology-platform-design`.

| Task | Worktree / branch | Model | State |
|---|---|---|---|
| 063 first Know rule | `C:\wts-063` `task/ontology-063` | sonnet/high (agent `lane-063`) | Running. Owner D-156: add `(sprk_matter, sprk_budget)` VerifiedJoin inside 063 + real-data proof. Live policy seed prepared, main session runs it |
| 102a authoring contract + allow-list [auth] | `C:\wts-102a` `task/ontology-102a` | opus/high (agent `lane-102a`) | Running. D-149 + D-151(b). Agent writes the POML `102a-authoring-contract-for-rule-wizard.poml`; add the TASK-INDEX row (`🔄 [wip] 102a`, after 102) when its PR merges. Approved extra scope: nextSteps[] and authorableFields[] in the catalog. Two adversarial passes + uac-r2 |
| 102 rule wizard UI | `C:\wts-102` `task/ontology-102` | sonnet/xhigh | Waits on 102a. Local commit `8993db08c` (Retire dialog jest timeouts) not pushed. On restart: rebase onto docs after 102a merges; contract per D-149 (rebuild condition on New version) |

## Open owner questions

1. **Ontology Administrator still holds `prvDeletesprk_Signal` and `prvAssignsprk_Signal` (Deep)** (uac-r2 flag). No shipped flow deletes or assigns a Signal as the caller; Console User holds neither; no human role holds Write/Delete/Assign on DRs. Recommend removing both (same pattern as 049; guard extended). security-roles §17.
2. **111 row 4** needs the external SPA on dev. R3 (2026-10-17) is asking the owner to approve a redeploy from the R3 branch head (has master e702c3401) and will tell us when live; then the owner checks row 4. **Never dispatch `deploy-external-spa.yml` from master** (task 122) while R3's SPA work (050–057, 061, 066, UAC 185) is off master: it would strip it from dev.

## Next, in order

1. 049 done (#2050 merged `32a923ca4`).
2. Lane results: 063 (then run its prepared live seed with owner OK if it is a write), 102a (two passes + uac-r2), then relaunch 102.
3. ebilling: 070 PR (paired F3 change) and 026 fix round (K1–K5, FLS on `sprk_firmreason`) come back for re-check. Temp worktrees `C:\wts-eb070`, `C:\wts-eb026`.
4. Re-run `SignalReadRouteLiveTests` as the non-Console user (DI-83 #1882), close #1882.
5. 064 (after 063), 114 (after 111), 105 UAT (include the Next step To Do not exercised in 055), 090 wrap-up.
6. docs→master sync for #2048, the 049 PR, 063, 102a (sync branch in `C:\wts-sync`; full ArchTests first; endpoint census recompute — 102a adds a route).
7. Cleanup: `C:\wts-eb070`, `C:\wts-eb026`, `C:\wts-sync`, `C:\wts-di107`, `C:\wb126-pub`, `C:\wts-126-pub` via `scripts/worktree/Remove-WorktreeSafe.ps1`.

## Dev state (spaarkedev1 / spaarke-bff-dev)

- BFF `1.0.0+d5401cf2df7e…` (R3, 2026-10-17 02:29:47Z; contains master e702c3401 = 100/101/103). healthz 200.
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
