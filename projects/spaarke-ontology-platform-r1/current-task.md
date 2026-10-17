# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-17 ~14:50Z (main session; owner answered Q1–Q7, D-161..D-165). State only — history is in git log,
> task `<completion>` blocks and `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path;
> D-123 secure family; D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership; D-138 owner approvals; D-139; D-140) and §6.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-165). Deferred work: one self-contained GitHub issue per item, label
> `ontology-deferred`, plus a DI-nn row in both tables of `notes/defer-issues.md` (next free: **DI-115**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | 102b in review (PR #2075) |
| **Next Action** | On fix-round-2 completion: scoped re-check of the fix diff (sonnet) → send new head to uac-r2 (fix diff only). **Merge HELD** until the owner chooses the cascade fix: (a) Restrict delete/archive on `sprk_policy_matter` (recommended) or (b) a scope-kind column. Both are schema changes. Owner: 111 row 4; CORS; Q24 spend set |

## Lanes in flight

Shared brief: scratchpad `lane-brief.md`. Each lane: review → fix round → re-check → merge into `docs/ontology-platform-design`.

| Task | Worktree / branch | Model | State |
|---|---|---|---|
| 102b rule matter scope [tenant-isolation] | `C:\wts-102b` `task/ontology-102b` | opus/high (lane done) | **PR #2075**. Pass 1 + uac-r2 → fix round 1 (head `d2b2ee72b`, uac-r2 approved) → pass 2 (two F3 untested guards: `SignalClosureService.cs:106` IsOpen, `SignalReevaluationJob.cs:412` out-of-scope redaction; K2 validate ignores scope intent; K4 log cap; K3 deploy BFF before Console). **Fix round 2 pushed, head `af6ce7171`** (all closed with fail-before tests; BFF filtered 2413, ArchTests 1076, jest 206+18). **Re-check CLEAN and uac-r2 APPROVED** the round-2 diff; known limits recorded in the notes (head `854c0b0b6`). **Code review is complete.** **Owner chose (2026-10-17): "if a rule linked to just one matter and the matter is deleted, the rule should be deleted"** (a third option: CascadeDelete = Cascade on `sprk_policy_matter`). Mapped: every downstream hop is RemoveLink, so a policy delete deletes only the policy row; versions, Signals and DRs stay, so no DR is ever deleted. Only rules that name the matter are affected; dev has 6 policies and none names a matter. Gap: the open Signals of a deleted policy are never closed. **Fix round 3 running** (opus implementer in `C:\wts-102b`): CascadeDelete=Cascade and CascadeArchive=Restrict on `sprk_policy_matter`, plus a nightly orphan sweep that closes Signals of deleted policies as PolicyRetired (with a must-not-fire seeding proof). A researcher is checking whether a cascade delete needs Delete on `sprk_policy` and how to apply the Web API relationship update. Then: record D-167, a re-check plus uac-r2 (tenant-isolation), the live metadata change with peer notices, and merge. **Open F1, owner decision:** `sprk_policy_matter` CascadeDelete/Archive = RemoveLink (`SpaarkeMaster/Other/Relationships/sprk_Matter.xml:563-564`), so deleting the matter widens the rule to all matters; no code guard is possible |

## Open owner / peer items

1. **111 row 4** (owner): `/upload` needs `?entityType=sprk_project&entityId=<GUID>`; the GUID comes from opening a project in My Projects (`/project/<GUID>`). No in-app link to `/upload` is a known R3 limit (R3 task 042 notes). Then close 111 → 114.
2. **CORS on `/api/v1/external/notifications`** (R3 owns): the owner saw it, time unknown (probably after the 14:32Z deploy). Owner to reload and report; R3 wants owner approval for live reads (platform CORS, anonymous preflight). **Never dispatch `deploy-external-spa.yml` from master** (task 122) while R3's SPA work is off master.
3. **POL-MATTER-NOBUDGET** is on (D-163): the owner bulk-closes the dev Signals after the next evaluation.
4. **ANSWERED (D-166): counted spend = Approved, Sent to AP, Paid. In Review NOT counted.** Sent to ebilling: 015 must drop In Review (it differs from their D-98); D-102 becomes automatic. The text below is background: **Owner: does ebilling 015 supersede D-137 Q24?** ebilling task 015 (retire `sprk_invoicestatus`, owner-OK'd live delete) changes `FinanceBaseValues.CountedInvoiceStatuses` from Reviewed only (Q24, "for now") to statuscode In Review, Approved, Sent to AP, Paid. That changes every ontology spend/budget Threshold (POL-COMMIT-BUDGET). Asked ebilling not to merge that change to master until the owner confirms directly. When 015 lands: update `notes/102a-authoring-contract.md` residual row and D-161 base columns.
3b. **ebilling 015 review** (retire `sprk_invoicestatus`; status on `statuscode`): pass DONE on `d4e555e38`. Findings sent to ebilling: F1, the counted set still includes In Review (D-166); F2, the delete script has no deploy-order check (wanted: `-BffDeployAttestation <sha>` with an `is-ancestor e0df77339` check); after the delete, every dev BFF must contain `e0df77339` (R3 told); K-items. Re-check their fix range. DI-114 #2082 (rollup not paged) filed. Worktree `C:\wts-eb015` goes on the cleanup list. ebilling confirmed the order: the delete runs only after an R3 BFF containing 015, enforced by `-BffDeployAttestation`/`-SpaDeployAttestation` (the BFF has no version stamp to check). Dev data: 10 invoices, all statuscode In Review, and only one has an amount (1,000 on matter REAL-2026-123456.01). That matter's seeded stored spend of 25,000 becomes 0 (old BFF) or 1,000 (015) at the next rollup, whichever build runs; budget is 55,000. **Owner asked:** seed an invoice to keep a meaningful figure? Told ebilling not to seed until the owner answers. When 015 merges, update the 102a residual note and D-161 (`statuscode` in place of `sprk_invoicestatus`).
4a. **ebilling 074** (their D-100 path B, D-101): review the range when it arrives (one adversarial pass + re-check) against the closed D-100 list (sent to ebilling 2026-10-17): both-direction code refusal; subject mismatch before I/O; idempotent DecisionId with createdby = writer via the sysadmin reader; lookup fault writes nothing (seeding proof); null confirmer needs "automated:"; 026 checks on the new path; additive and byte-identical. D-101: Decision Records file under sprk_invoice, so no sprk_decisionrecord schema change. **Held invoices: ebilling's owner decided D-102** — held invoices are excluded from spend until Override releases them; this ships with their task 049. Asked ebilling to put the exclusion in BOTH sum paths, `FinanceRollupService` (the stored total) and `FinanceBaseValues.ReadAsync`/`BuildGroupedSumFetchXml` (the live sum `SignalEvaluator.cs:233` uses), and to send 049 for review with 074. Q24 still needs OUR owner's direct confirmation; ebilling made it a merge condition on PR #1911.
5. **ebilling 070**: re-check CLEAN through `cc01731b3` (reported). Their master PR and our 102b touch the same authoring files; second to master resolves.

## Next, in order

1. 102b lane → review → merge; then a docs→master sync (102b) and an R3 BFF deploy (fresh owner approval each time).
2. 105 UAT with the owner: 102 wizard browser tests + keyboard/screen-reader after a confirmation closes; 064 §6.1 non-admin checks and UI tests (D-164); the 055 Next step To Do.
3. Re-run `SignalReadRouteLiveTests` as the non-Console user (DI-83 #1882), close #1882.
4. ebilling 070 PR re-check when it arrives (026 re-check was CLEAN, reported).
5. 114 after 111; 090 wrap-up.
6. Cleanup via `scripts/worktree/Remove-WorktreeSafe.ps1`: `C:\wts-eb070`, `C:\wts-eb026`, `C:\wts-sync`, `C:\wts-di107`, `C:\wts-049b`, `C:\wts-064`, `C:\wts-102a`, `C:\wts-102`, `C:\wb126-pub`, `C:\wts-126-pub`.

## Dev state (spaarkedev1 / spaarke-bff-dev)

- BFF `1.0.0+9721819edc15…` (R3, 2026-10-17 14:32:19Z; master `88b3ecb1f` = 063 join, 102a routes + allow-list; UAC #2074). healthz 200, writer MI assigned.
- Console from master `88b3ecb1f` (07:32:56Z): rule wizard (102) + 064 code. **Landing tab = Worklist** since 14:38Z (D-164; revert `Set-DefaultWorkspaceLayout.ps1 -To 'Daily Briefing'`).
- Policies: six on, including POL-MATTER-NOBUDGET since ~14:36Z (D-163).
- Roles: Ontology Service 58 (§15); Ontology Administrator 38 (§16 +2 Append, §17 −DR Create −Signal Write; keeps Signal Assign/Delete, D-162); Console User 21 (§17).

R3 candidate `a6513cd56` (live 9721819ed + the external SPA invoice `statuscode` allow-list fix): ontology OK sent (no Finance or Signals change); owner approval is R3's to get.

## Promises to peers

- **R3** owns the dev BFF line and the external SPA: message first for any BFF change.
- **word-add-in**: before/after notices for any Console or BFF change (and before any landing-tab revert).
- **uac-r2**: before/after notices for live role changes; PRs for role XML; the 102b PR number for review.
- **ebilling**: 070 re-check when its range arrives.

## Peers (current session names)

uac-r2 `spaarke-wt-unified-access-control-r2-c8`; R3 `spaarke-wt-spa-external-access-platform-75`; ebilling `spaarke-wt-ebilling-r1-99`;
word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-54`; provisioning `spaarke-wt-customer-provisioning-orchest-bb`.
