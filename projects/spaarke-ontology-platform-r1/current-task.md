# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-22 ~13:40Z (main session). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 and §6. Decisions: [`notes/decisions.md`](notes/decisions.md)
> (D-1..D-177). Deferred work: one self-contained GitHub issue per item, label `ontology-deferred`, plus a DI-nn row in both tables of
> `notes/defer-issues.md` (next free: **DI-122**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | No lane in flight. 157 ✅ is on master (#2233 `aa3dc49897`). CI ArchTests timeout raised to 10 min (#2252 `2101d3d828`, owner-approved GATE REPAIR). `WithheldSentence` comment lane merged to docs (#2256 `d852945aa6`); worktree removed. |
| **Status** | Waiting on peers (ebilling 088 run + reviews, R3's next dev BFF deploy) and the owner (UAT items). |
| **Next Action** | When ebilling sends the 088 after-notice: independent read-back on dev (list below). When R3 deploys the next master build: confirm it contains `aa3dc49897`, smoke, log check. |

## Next, in order

1. **ebilling 088:** run 1 on dev stopped with exit 1 on 2026-10-22. The field-permission service refused the BigInt `sprk_sourceversion` (0x8004f508), even though CanBeSecuredFor* read true in both pre-checks.
   - **Partial state on dev:** the table, all columns, the marker and Superseded exist; 8 of 14 Stored File columns are secured.
   - **Retype (owner-approved in ebilling's session):** `sprk_sourceversion` becomes String 20 and `sprk_sizebytes` becomes Integer. ebilling deletes the two empty BigInt columns and re-creates them with the new types.
   - **Our scoped check (asked of ebilling 2026-10-22):**
     - (1) The re-run skips the partial state and secures only the 6 remaining columns. Our read-back on 2026-10-22 shows 9 columns secured, not 8: filestatus is secured too. Each has Readers R / Writers RCU rows plus the platform's own System Administrator row. The two BigInt columns are deleted, and there are 0 rows. The end state is 15 secured columns; blobpath gets Writers only.
     - (2) The delete is guarded by a dependency check and gives a clear exit if a name still resolves.
     - (3) The >2 GiB cap is fail-closed in 090 (checked conversion) and recorded as a known limit.
     - (4) `ForSourceVersion` and the column use one canonical string form.
     - (5) The 088 and 090 POMLs are updated to the new types.
   - **Then the read-back on dev:** `sprk_storedfile` SchemaName `sprk_StoredFile`; primary `sprk_Name`; the new types; 15 secured columns with grants (blobpath Writers only); 6 relationships (4 RemoveLink, 2 Restrict, none parental) with Related menu DoNotDisplay; `statuscode` Superseded `659490005`; a second run skips all. Log it in `notes/deploy-log.md`.
2. **Next R3 dev BFF from master** carries 157: check ancestry (`aa3dc49897`, R3 077 `283466d40`), run `scratchpad/live/smokeA_v2.py` on both matters, set `since` in `deploycheck.py` to the deploy time and run it; deploy-log entry.
3. **ebilling reviews when sent:** 097 (BillingFactLoader Superseded → null; our path), 116 (drafting re-keyed per Procedure Version), 082 (resolver uses `BillingRuleDefinition.PrecedenceKey`), 081 (creator-person zero-non-null assert). The 088 F1s are already filed into their POMLs 117, 094/095, 097 (`8d1394a4ed`). Every schema run: final script + `-WhatIf` to us, before/after notices, no run before our PASS; check all core records.
4. **105 UAT** with the owner: 102 wizard browser tests + keyboard/screen-reader; 064 §6.1 non-admin checks and UI tests (D-164); the 055 Next step To Do.
5. Re-run `SignalReadRouteLiveTests` as a non-Console user (DI-83 #1882), then close #1882.
6. **111 row 4** (owner): `/upload?entityType=sprk_project&entityId=<GUID>` with the GUID from `/project/<GUID>`; then close 111 → 114; then 090 wrap-up.

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

- Dev BFF `1283c41817` live (R3, 2026-10-22; contains R3 077, no ontology file changes, not yet 157); ontology smoke PASS ~13:35Z; no 503xx warnings or ontology exceptions since 12:50:18Z. Last nightly heartbeat 02:30:29Z succeeded. Every dev BFF candidate must contain R3 077 (`283466d40`). R3 owns the dev BFF line; we never deploy it.
- Console auto-deploys from master; landing tab = Worklist (D-164; revert `Set-DefaultWorkspaceLayout.ps1 -To 'Daily Briefing'`).
- ebilling on dev: 080 Procedure schema (read back), 115 rule-type options 100000015/100000016 (100000014 SubmissionIntegrity not yet on dev); 119 merged on their branch (`4a43d9a223`; `WithheldDuplicateTemplate = EarlierInvoiceReadability.WithheldSentence`).
- Policies: six on, including POL-MATTER-NOBUDGET; none has a matter. Roles: Ontology Service 58; Ontology Administrator 38; Console User 21; Secure Record Owner 40.
- Worktrees: none of ours (`C:\wts-c34` removed with Remove-WorktreeSafe). Non-worktree folders left alone: `C:\wb126-pub`, `C:\wts-126-pub`, `C:\wts-103-seed`, `C:\wts-132-out`, `C:\wts-132m-out`, `C:\wts-r1609`, `C:\wts-r1609c`, `C:\wts-rc046b`, `C:\wts-rc1653`.

## Peers (current session names)

ebilling `spaarke-wt-ebilling-r1-5b` · R3 `spaarke-wt-spa-external-access-platform-05` · uac-r2 `spaarke-wt-unified-access-control-r2-ac` ·
word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4` · field-mapping `spaarke-wt-field-mapping-server-write-pa-b5`. Before/after notices to
word-add-in, R3 and uac-r2 for any dev BFF, Console or landing change.
