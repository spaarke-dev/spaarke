# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-22 ~17:00Z (main session). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 and §6. Decisions: [`notes/decisions.md`](notes/decisions.md)
> (D-1..D-185). Deferred work: one self-contained GitHub issue per item, label `ontology-deferred`, plus a DI-nn row in both tables of
> `notes/defer-issues.md` (next free: **DI-124**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Lanes running (owner go 2026-10-22): **158** `C:\wts-158` `task/158-wizard-lookups`; **159** `C:\wts-159` `task/159-assign-task`; **161** `C:\wts-161` `task/161-create-budget`; **163** `C:\wts-163` `task/163-policy-server-read` (security: 2 review passes). Each stops at its PR (no deploy, no policy apply). **162** merged to docs `84636b650a` (dev check after the docs→master sync; owner question sprk_X vs sprk_related*). **159** PR #2289 fix round 1 `4e9e295a30` pushed; Opus review running. **158** `539ab22ce9`, **159** `9b485535ce`, **161** `811167613f` merged to docs (dev checks after the docs→master sync + deploys; 159's v2 policy apply after the BFF deploy and the owner's OK on -WhatIf). **docs updated from master** `0a6e8820fd` (owner ask 2026-10-23). **163** PR #2298: pass 2 found F2 (resolver fault → 503) + F1 docs; fix round 2 running, then a scoped re-check, then merge. **164** PR #2299 (Deploy-Release Phase 2b, owner D-186): Opus review running. **160** relaunched with owner decisions D-187 (composeOnly prop, split commits; flagged-external systemusers get the app link). **ebilling F4(b)** (5f3bdbb201, d600410d2b): review pass 1 running. Next: docs→master PR, then ask R3 for a dev BFF deploy with the exact SHA (R3 needs the owner's own approval in its session); then POL-MATTER-NOBUDGET v2 apply (owner approved D-186), then the D-185 role change after 163 is on dev. |
| **Status** | Waiting on lane reports. Owner is reviewing R2 design §9. |
| **Next Action** | Per lane report: one Opus adversarial review of the PR diff, a fix round, a Sonnet re-check, then merge into docs. 159 and 161 both edit `DecisionActionCatalog.cs`, `DecisionActionExecutors.cs` and `SignalsModule.cs`: merge one, rebase the other. After 158 merges, launch 160 (opus/high) in `C:\wts-160`. Then a docs→master sync for the Console, R3's BFF deploy, dev checks, and 159's policy v2 apply after the owner OKs `-WhatIf`. |

## Next, in order

1. **UAT round 1 lanes** (owner go): 158 side-pane lookups + subject link; 159 Assign Task (`sprk_event` type Task; POL-MATTER-NOBUDGET v2 applied to dev only after owner OK on `-WhatIf`); 160 EmailComposer + link by recipient (opus; after 158; R3 reviews the GrantAccessNotifier overload); 161 Create budget (plan substitution); 162 exact document column (closes 114). Each: implementer lane from docs, independent review, fix, re-check.
2. **Dev BFF is `1fa0cf5cb8`** (R3, 2026-10-23 00:00Z; carries 157 + #2282): smoke and logs PASS (deploy-log). Check tonight's `[SIGNAL-EVALUATOR]` heartbeat (`deploycheck.py`, since=2026-10-23T00:00:00Z).
2b2. **D-185 role change (owner-authorised, main session):** after 163's BFF is on dev AND ebilling's caller-read inventory (invoice review, Rule Review, SPA) reports no ordinary-user caller read of sprk_policy/policyversion: (1) no billing role exists yet, so there is nothing to grant first (ebilling 2026-10-23; UAC 191 will create the roles with explicit Read), (2) remove `prvReadsprk_Policy` from Basic User and `prvReadsprk_Policy` + `prvReadsprk_PolicyVersion` from Console User, (3) read back before and after, then test the worklist as a non-Console user and as a Console user, (4) write security-roles.md §15 and a deploy-log entry, and tell uac-r2 on #1355.
2a. **#2281** fixed on master `1fa0cf5cb8` (#2282, reviewed; #2281 closed). Seeder defect it uncovered: #2283 (customer-provisioning). #2285 (ebilling: internal Read on sprk_policy/ruleset): our Basic User policy Read is intended (§12), commented; owner decides on hiding Billing policy rows. Dev check after the BFF candidate after `f5bf481185`: Create Work Assignment on a matter with an attorney copies `sprk_assignedattorney1`, no warning.
2b. **R2** (D-183): owner answers design.md §9 Q1–Q6, then `/design-to-spec` in the R2 worktree. R2 does not edit R1-owned files while R1 lanes are open.
3. **ebilling reviews when sent:** the D-184 `SignalReadService` change (vendor org from the submission; security review, 2 passes; must merge with or before Duplicate-raising code); 097 (BillingFactLoader Superseded → null; our path), 116 (drafting re-keyed per Procedure Version), 082 (resolver uses `BillingRuleDefinition.PrecedenceKey`; must remove the `sprk_procedure` null filter, and nothing may set `sprk_ruleset.sprk_procedure` before 082). 081 ✅ re-check PASS (`cac386fa6b`, ArchTest `b160493311`). 088 ✅ on dev (read-back PASS). Owner gets ebilling's questions on retroactive correction and the D-11 clean slate. The 088 F1s are already filed into their POMLs 117, 094/095, 097 (`8d1394a4ed`). Every schema run: final script + `-WhatIf` to us, before/after notices, no run before our PASS; check all core records.
4. **105 UAT** with the owner, per `notes/uat-v4-log.md` (R1–R24 + C1–C4; R19, C1, C3 pass). Open: keyboard/screen-reader (C2), the 055 Next step To Do (C4), the rest of R1–R24.
5. Re-run `SignalReadRouteLiveTests` as a non-Console user (DI-83 #1882), then close #1882.
6. **111 row 4** (owner): https://green-dune-0c4f1221e.7.azurestaticapps.net/upload?entityType=sprk_project&entityId=e42aa40d-361b-f111-88b3-7ced8d1dc988 ; then close 111. **114** closes after 162 (and #2281 on dev); then 090 wrap-up.

## Blocked follow-ups (ours)

- `OntologyAdminClassificationService.cs:658/685/705` labels an in-force billing version Draft — blocked until `sprk_publishstate` is in code on master.
- 5 stale `DecisionRecordWriter.cs` comments (:556 :1547 :1660 :2164 :2326) + `DecisionRecordContextImmutabilityTests.CreateEntryPoints` check — after ebilling 074 reaches master.
- Close **#2212** when ebilling's fix (`f0884a175f`) reaches master.
- When ebilling 015 reaches master: update `notes/102a-authoring-contract.md` residual row and D-161 to name `statuscode`.
- The advisory ADR Compliance CI job also hits its 5-minute timeout; left to the ci-cd owners (noted on #2009).

## Open owner / peer items

1. **POL-MATTER-NOBUDGET** is on (D-163): the owner bulk-closes the dev Signals after the next evaluation.
2. **Dev spend figure:** seeded 25,000 on REAL-2026-123456.01 becomes 0 at the next rollup (all dev invoices In Review; D-166). Offer an Approved invoice seed only if the owner asks.
3. **CORS** on `/api/v1/external/notifications` (R3 owns): if it recurs, get the UTC time and the OPTIONS status. Never dispatch `deploy-external-spa.yml` from master while R3's SPA work is off master.
4. **uac-r2 #2091 follow-up** (`SecureChildReconciler`/`RefileStepsAsync`): review when sent — gate-DR step still runs on thread join/reply re-file; same-parent skip hides no real parent change; gate-DR failure logs 50317 and never fails the re-file.
5. **field-mapping 033** (SignalWriter + DecisionRecordWriter onto RecordCreationService): our owner review + uac-r2 review of their design note when sent.
6. Next docs→master sync: re-check master's `GateDecisionRecordSharing.IsFiled` keeps `ParentsOf(communication.Attributes, CommunicationTable)`; carries #2256 (comment only).

## Dev state (spaarkedev1 / spaarke-bff-dev)

- Dev BFF `1283c41817` live (R3, 2026-10-22; contains R3 077, not yet 157); R3's next candidate `f5bf481185` (carries 157) OK'd by us; ontology smoke PASS ~13:35Z; no 503xx warnings or ontology exceptions since 12:50:18Z. Last nightly heartbeat 02:30:29Z succeeded. Every dev BFF candidate must contain R3 077 (`283466d40`). R3 owns the dev BFF line; we never deploy it.
- Console auto-deploys from master; landing tab = Worklist (D-164; revert `Set-DefaultWorkspaceLayout.ps1 -To 'Daily Briefing'`).
- ebilling on dev: 080 Procedure schema (read back), 115 rule-type options 100000015/100000016 (100000014 SubmissionIntegrity not yet on dev); 119 merged on their branch (`4a43d9a223`; `WithheldDuplicateTemplate = EarlierInvoiceReadability.WithheldSentence`).
- Policies: six on, including POL-MATTER-NOBUDGET; none has a matter. Roles: Ontology Service 58; Ontology Administrator 38; Console User 21; Secure Record Owner 40.
- Worktrees: R2 `C:\code_files\spaarke-wt-spaarke-ontology-platform-r2` (docs only); no lane worktrees (`C:\wts-c34` removed with Remove-WorktreeSafe). Non-worktree folders left alone: `C:\wb126-pub`, `C:\wts-126-pub`, `C:\wts-103-seed`, `C:\wts-132-out`, `C:\wts-132m-out`, `C:\wts-r1609`, `C:\wts-r1609c`, `C:\wts-rc046b`, `C:\wts-rc1653`.

## Peers (current session names)

ebilling `spaarke-wt-ebilling-r1-5b` · R3 `spaarke-wt-spa-external-access-platform-05` · uac-r2 `spaarke-wt-unified-access-control-r2-ac` ·
word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4` · field-mapping `spaarke-wt-field-mapping-server-write-pa-be`. Before/after notices to
word-add-in, R3 and uac-r2 for any dev BFF, Console or landing change.
