# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-19 ~04:30Z (main session; dev BFF live on ad0e9e1a7 with 102b; first nightly sweep on it checked). State only — history is in git log,
> task `<completion>` blocks and `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path;
> D-123 secure family; D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership; D-138 owner approvals; D-139; D-140) and §6.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-169). Deferred work: one self-contained GitHub issue per item, label
> `ontology-deferred`, plus a DI-nn row in both tables of `notes/defer-issues.md` (next free: **DI-119**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | No lane in flight. 042b ✅ (#2096 → docs `f66aa811a`; not yet on master). uac-r2 PR #2102 (#2084, D-168 A3): passes 1+2 + re-checks CLEAN at `c757caf8b` (ArchTests 1150/0); uac-r2 merges on green CI |
| **Next Action** | 1) #2102: on uac-r2's merge SHA, finalise D-168 (race/un-scope -> 409; `owner_unavailable` 503 "Nothing was changed"), close DI-115 (#2084); SRO Read apply notices from uac-r2; then merge + R3 deploy (BFF before/with Console; fresh owner approval). 2) Next docs→master sync carries 042b (full ArchTests first; then R3 BFF deploy with fresh owner approval). 3) Re-check ebilling fix ranges when sent: 071 r3 / 072 combined, 073 (K1 guard + K2 comment), 074 follow-up (K1-K4 + ParentUndetermined). 5) After 074 reaches master, one small code lane: immutability ArchTest entry + 5 stale writer comments + a comment on `SignalClosureService.ExistingPolicyCodesAsync` (:278) that it must stay UNFILTERED (billing depends on it; ebilling pins it in c3d8616e2) |

## Open owner / peer items

1. **#2084 / DI-115 ADOPTED (D-168):** UAC builds SecureChildLineage for `sprk_policy` + `sprk_policyversion` after 099b (#2063), with publish refusing (`ontology.admin.rule_held_secure`, before any write) when the resolver answers a Secure team (A3, D-168 amended; no ownerid bind: the admin lacks prvAssign); RuleWizard maps the code; we give the PR two adversarial passes. **UAC #2091:** three re-file writers will call `AfterRefileAsync` (then 042b's re-own follows).
2. **111 row 4** (owner): `/upload?entityType=sprk_project&entityId=<GUID>`, with the GUID taken from `/project/<GUID>`. Then close 111 → 114.
3. **CORS** on `/api/v1/external/notifications` (R3 owns): if it recurs, get the UTC time and the OPTIONS status. **Never dispatch `deploy-external-spa.yml` from master** while R3's SPA work is off master.
4. **POL-MATTER-NOBUDGET** is on (D-163): the owner bulk-closes the dev Signals after the next evaluation.
5. **Dev spend figure:** the seeded 25,000 on matter REAL-2026-123456.01 becomes 0 at the next rollup (all dev invoices are In Review; D-166). Offer the owner an Approved invoice seed (ebilling would add it); none until the owner asks.

## ebilling reviews (ontology owns DecisionRecordWriter, FinanceBaseValues, FinanceRollupService)

- **015** ✅ (ebilling; re-checks CLEAN through `ad338f34a`). The PR #1911 Q24 condition is met. The delete script requires `-BffDeployAttestation`/`-SpaDeployAttestation`. After the delete, every dev BFF must contain `e0df77339` (R3 agreed). **When 015 reaches master:** update `notes/102a-authoring-contract.md` residual row and D-161 to name `statuscode`.
- **074** (`WriteBillingDecisionAsync`, D-100/D-101): pass done; all 7 conditions MET. ebilling is fixing F4-1 fully (resolved Signals' subjects in ownership), F3-1, K2 hash, K3 (no contactId), K4. Consumer obligations are in 049/100/103-105. **Re-check their fix range.** Ours after 074 reaches master: `DecisionRecordContextImmutabilityTests.CreateEntryPoints` + synthetic member + must-fire control; 5 stale writer comments (`DecisionRecordWriter.cs` :556 :1547 :1660 :2164 :2326).
- **049** (held invoices D-102, Pending Spend D-104): review when sent. The held exclusion must be in both sum paths. Pending Spend needs §11 answers and owner schema approval. If it should be authorable as a Threshold, we add allow-list entries (`PolicyVersionValidator.AuthorableColumns`, `PredicateCompiler`, `RuleBodyDescriber`).
- **071** (billing rule handlers): pass done; 4 fix-now items sent (read fields after IsCreditLine; D-123 cross-wall Duplicate evidence, with their owner + uac-r2; Describe text vs handler; stale hashes) and K4 purity guard, K1 currency, K1 SubmissionId, K2. Re-check their fix range when it arrives.
- **070**: re-check CLEAN through `cc01731b3`. Their master PR and our 102b touch the same authoring files; whoever reaches master second resolves the conflict.

## Next, in order

1. docs→master sync (102b), then an R3 BFF deploy (owner approval). After the deploy: the nightly sweep closes and redacts deleted-rule Signals.
2. 105 UAT with the owner: 102 wizard browser tests + keyboard/screen-reader; 064 §6.1 non-admin checks and UI tests (D-164); the 055 Next step To Do.
3. Re-run `SignalReadRouteLiveTests` as the non-Console user (DI-83 #1882), then close #1882.
4. 114 after 111; 090 wrap-up.
5. Cleanup via `scripts/worktree/Remove-WorktreeSafe.ps1`: `C:\wts-102b`, `C:\wts-eb015`, `C:\wts-eb074`, `C:\wts-eb070`, `C:\wts-eb026`, `C:\wts-sync`, `C:\wts-di107`, `C:\wts-049b`, `C:\wts-064`, `C:\wts-102a`, `C:\wts-102`, `C:\wb126-pub`, `C:\wts-126-pub`.

## Dev state (spaarkedev1 / spaarke-bff-dev)

- **First nightly sweep on 102b BFF (2026-10-19 02:30Z) OK:** `[SIGNAL-EVALUATOR] heartbeat outcome=succeeded policies=6 hits=142 written=0 unchanged=142 closed=0 policyRetiredClosed=0 policyFailures=0` (36.7 s). 0 PolicyRetired is expected: the 102b live-check rows (S2) were deleted at cleanup. Rows are not re-stamped when unchanged (D-61). App Insights app `6a76b012-…` (spe-insights-dev-67e2xz), query via `az rest` to api.applicationinsights.io.
- BFF `1.0.0+ad0e9e1a7…` (R3, 2026-10-18 22:31Z; owner-approved directly to R3) = master 20fb86dc4 (#2088, 102b) + UAC #2094 + R3. Rollback `ebc74b331`. Does NOT contain ebilling 015 (`e0df77339`) nor 042b.
- Console from master `20fb86dc4` (04:36Z). Console and BFF aligned for 102b. **Landing tab = Worklist** (D-164; revert `Set-DefaultWorkspaceLayout.ps1 -To 'Daily Briefing'`).
- **`sprk_policy_matter` CascadeDelete = Cascade** since 2026-10-18 ~03:35Z (D-167; Archive RemoveLink). Live facts: an admin delete removes only the rule; a Core User's matter delete also deletes the matter's rules (no privilege check on the child; audited). See notes/102b "Live on spaarkedev1".
- Policies: six on, including POL-MATTER-NOBUDGET (D-163); none has a matter.
- Roles: Ontology Service 58; Ontology Administrator 38 (keeps Signal Assign/Delete, D-162); Console User 21.

## Promises to peers

- **R3** owns the dev BFF line and the external SPA: message first for any BFF change.
- **word-add-in**: before/after notices for any Console or BFF change (and before any landing-tab revert).
- **uac-r2**: before/after notices for live role or schema changes; #2084 decision when the owner gives it.
- **ebilling**: re-check 074 fix range; review 049.

## Peers (current session names)

uac-r2 `spaarke-wt-unified-access-control-r2-c8`; R3 `spaarke-wt-spa-external-access-platform-75`; ebilling `spaarke-wt-ebilling-r1-99`;
word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-54`; provisioning `spaarke-wt-customer-provisioning-orchest-bb`.
