# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-22 ~02:35Z (main session; 157 merged). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md` (the previous 25 KB checkpoint is archived there verbatim). Standing rules: [`CLAUDE.md`](CLAUDE.md)
> §3 and §6. Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-177). Deferred work: one self-contained GitHub issue per item,
> label `ontology-deferred`, plus a DI-nn row in both tables of `notes/defer-issues.md` (next free: **DI-122**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | No lane in flight. **157 ✅** merged to docs via PR #2231 (`2a020e05ff`, 2026-10-22; ebilling PASS through round 3). Rides the next docs→master sync. |
| **Status** | Waiting on peers (ebilling reviews) and the owner (UAT items). |
| **Next Action** | Confirm the 2026-10-22 02:30Z nightly heartbeat on dev BFF `a7b956fb55` (background check queued; rerun `scratchpad/live/deploycheck.py` with `since` = 2026-10-22T02:18:52Z if needed), then pick up whichever ebilling review arrives first. |

## Next, in order

1. **Nightly heartbeat** on `a7b956fb55`: confirm `[SIGNAL-EVALUATOR] heartbeat outcome=succeeded` and 0 503xx warnings since 02:18:52Z (smoke already PASS).
2. **Next docs→master sync** (157 + #2174): main session runs the full ArchTests first; notify R3/uac-r2/word-add-in before and after.
3. **ebilling reviews when sent:** 088 (Stored File schema; sprk_document submission marker; invoice Superseded + resubmission-of link), 097 (per-invoice Superseded close wrapper over `SignalWriter.CloseAsync`), 116 (drafting re-keyed per Procedure Version), 119 (must call `EarlierInvoiceReadability.ShownAsync` and use its `WithheldSentence`; no `DuplicateOfEarlierLine` exemption), 082 (resolver must use `BillingRuleDefinition.PrecedenceKey`, condition in their POML), 081 (creator-person zero-non-null assert). Every schema run: final script + `-WhatIf` to us, before/after notices, no run before our PASS; check all core records, not matter alone.
4. **105 UAT** with the owner: 102 wizard browser tests + keyboard/screen-reader; 064 §6.1 non-admin checks and UI tests (D-164); the 055 Next step To Do.
5. Re-run `SignalReadRouteLiveTests` as a non-Console user (DI-83 #1882), then close #1882.
6. **111 row 4** (owner): `/upload?entityType=sprk_project&entityId=<GUID>` with the GUID from `/project/<GUID>`; then close 111 → 114; then 090 wrap-up.

## Blocked follow-ups (ours)

- `OntologyAdminClassificationService.cs:658/685/705` labels an in-force billing version Draft — blocked until `sprk_publishstate` is in code on master.
- 5 stale `DecisionRecordWriter.cs` comments (:556 :1547 :1660 :2164 :2326) + `DecisionRecordContextImmutabilityTests.CreateEntryPoints` check — after ebilling 074 reaches master.
- Close **#2212** when ebilling's fix (`f0884a175f`) reaches master.
- When ebilling 015 reaches master: update `notes/102a-authoring-contract.md` residual row and D-161 to name `statuscode`.
- After 157 and ebilling's billing code are both on master: equality guard that `EarlierInvoiceReadability.WithheldSentence` equals ebilling's withheld template (ontology owns the const).

## Open owner / peer items

1. **POL-MATTER-NOBUDGET** is on (D-163): the owner bulk-closes the dev Signals after the next evaluation.
2. **Dev spend figure:** seeded 25,000 on REAL-2026-123456.01 becomes 0 at the next rollup (all dev invoices In Review; D-166). Offer an Approved invoice seed only if the owner asks.
3. **CORS** on `/api/v1/external/notifications` (R3 owns): if it recurs, get the UTC time and the OPTIONS status. Never dispatch `deploy-external-spa.yml` from master while R3's SPA work is off master.
4. **uac-r2 #2091 follow-up** (`SecureChildReconciler`/`RefileStepsAsync`): review when sent — gate-DR step still runs on thread join/reply re-file; same-parent skip hides no real parent change; gate-DR failure logs 50317 and never fails the re-file.
5. **field-mapping 033** (SignalWriter + DecisionRecordWriter onto RecordCreationService): our owner review + uac-r2 review of their design note when sent (invariants already sent 2026-10-20).
6. Next docs→master sync: re-check master's `GateDecisionRecordSharing.IsFiled` keeps `ParentsOf(communication.Attributes, CommunicationTable)`; #2174 (docs-only ArchTest/comment lane) rides it.

## Dev state (spaarkedev1 / spaarke-bff-dev)

- Dev BFF `a7b956fb55` live (R3, 2026-10-22 02:18:52Z; adds field-mapping #2223/#2228 and UAC #2221, no ontology files); ontology smoke PASS (Secure 5d46df87 → `scope_matter_secured`; Standard REAL-2026-123456.01 → valid). Every dev BFF candidate must contain R3 077 (`283466d40`). R3 owns the dev BFF line; we never deploy it.
- Console auto-deploys from master; landing tab = Worklist (D-164; revert `Set-DefaultWorkspaceLayout.ps1 -To 'Daily Briefing'`).
- ebilling on dev: 080 Procedure schema run + idempotent second run (our independent read-back matches, see `notes/deploy-log.md`); 115 rule-type options 100000015 AddedFee / 100000016 Discount (100000014 SubmissionIntegrity not yet on dev).
- Policies: six on, including POL-MATTER-NOBUDGET; none has a matter. Roles: Ontology Service 58; Ontology Administrator 38; Console User 21; Secure Record Owner 40.
- Worktrees: none (all removed 2026-10-21/22 with Remove-WorktreeSafe, `C:\wts-157` last). Non-worktree folders left alone: `C:\wb126-pub`, `C:\wts-126-pub`, `C:\wts-103-seed`, `C:\wts-132-out`, `C:\wts-132m-out`, `C:\wts-r1609`, `C:\wts-r1609c`, `C:\wts-rc046b`, `C:\wts-rc1653`.

## Peers (current session names)

ebilling `spaarke-wt-ebilling-r1-5b` · R3 `spaarke-wt-spa-external-access-platform-05` · uac-r2 `spaarke-wt-unified-access-control-r2-ac` ·
word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4` · field-mapping `spaarke-wt-field-mapping-server-write-pa-b5`. Before/after notices to
word-add-in, R3 and uac-r2 for any dev BFF, Console or landing change.
