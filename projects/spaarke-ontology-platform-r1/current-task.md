# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-23 ~03:30Z (main session, context-handoff). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 and §6. Decisions: [`notes/decisions.md`](notes/decisions.md)
> (D-1..D-187). Deferred work: one self-contained GitHub issue per item, label `ontology-deferred`, plus a DI-nn row in both tables of
> `notes/defer-issues.md` (next free: **DI-126**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | UAT round 1. **On master** `0317579272` (PR #2303): 158, 159, 161, 162, 163, 164 (164 ✅). Console deployed from it (run 39899451640, 04:20Z; after-notices sent). **160** (PR #2305, `C:\wts-160`, head `97baf3acd0`): Opus review → fix round 1 (F1 stored XSS via innerHTML in `EmailMessageFields.toPlainText` + same in `BodyEditor.htmlToPlainText`, F2, F3a/b, bad-address orphan, K1, K3, R3-a/b) → scoped re-check: **no F**, 4 K. |
| **Status** | Main session is fixing 160's re-check K items itself in `C:\wts-160` (uncommitted, nothing written yet): K-2 `EmailMessageFields.tsx:109` `catch { return null; }` → visible `lookup-failed` warning; K-3 clear the no-email/lookup warning when To changes (in `onStateChange`, if `changed` includes `to`); K-4 `InquiryRecordFacts.cs` ~:220 `catch (OperationCanceledException) when (!ct.IsCancellationRequested)` + rethrow on request cancel (observe `all` in both). Tests: Jest `__tests__/DecisionWizardEmailComposer.test.tsx` (copy the F2 tests at :199-249); .NET `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/BudgetInquiryComposerAndLinkTests.cs` (hang test :315, fixture `ReadDeadline` :482). |
| **Next Action** | Finish K-2/3/4 + tests → run Jest (DecisionWizard + EmailComposer) and the filtered BFF tests → commit, push → CI → merge PR #2305 into docs → docs→master PR (it changes Console + BFF; notices before and after). |

## Next, in order

1. **Dev BFF:** R3 (`spaarke-wt-spa-external-access-platform-08`) will deploy master **`cdb149cc2d`** (our `0317579272` + word-add-in 132; ontology OK sent). It waits for the **owner's approval naming that SHA in R3's session**. uac-r2 runs its server gate after.
2. **After that BFF is on dev, in order:**
   - (a) Worklist config: `Apply-WorklistGridConfiguration.ps1 -WhatIf`, apply, `-Verify -RequireRow`. **Ask the owner first** (a dev data change, not yet approved).
   - (b) POL-MATTER-NOBUDGET v2: `Apply-DoLanePolicies.ps1 -DefinitionPath config/know-promotion.policies.json` (owner-approved, D-186), then `-Verify`.
   - (c) D-185: `RemovePrivilegesRole` on **Console User only** (`prvReadsprk_Policy`, `prvReadsprk_PolicyVersion`). Basic User already lacks `prvReadsprk_Policy`: see security-roles §19, removed 2026-10-23 00:01:07Z by the owner's account, and every peer session has ruled itself out. Read back before and after; send uac-r2 the #1355 read-back with UTC times (they run an App Insights gate); write the §19 "applied" entry and a deploy-log entry; tell ebilling.
   - (d) Dev checks: 158 side pane plus record modal; 159 Assign Task → Task event; 161 Create budget; 162 uploads to WA, invoice and project show on the parent's Documents (zz- rows); 160 once merged. Then close 114.
3. **ebilling F4(b):** pass 2 found their 5 commits clean. They are adding `sprk_lane`, `sprk_policy` and `sprk_policyversion` to the `sprk_signal` AccessColumns in their PR (uac-r2 agrees, conditions sent). They also fix the duplicate `using` in `DecisionActionCatalogTests.cs` after merging master (CS0105). On their SHA → scoped re-check.
4. R2 §9 owner answers → `/design-to-spec`; 105 UAT; #1882 live re-run; 111 row 4; 090 wrap-up; the `[SIGNAL-EVALUATOR]` heartbeat.

## Open owner questions

- Did the owner edit Spaarke Basic User around 20:01 EDT on 10-22? It doesn't block anything.
- Approve `cdb149cc2d` in R3's session.
- Worklist config apply on dev (step 2a).
- Long-term document parent column (`sprk_X` vs `sprk_related*`).
- `C:\wb160p` (publish output folder) couldn't be deleted by the lane; remove it by hand.

## Lane facts to carry

- **163 (#2298), security, two passes done:**
  - Pass 2 found F2: a resolver fault was silently degrading internal worklists. Fix round 2 makes it a 503 for "lookup-failed" or an exception; external, no-match and non-GUID stay 200 with no enrichment. This is a deliberate deviation from master's rule (d).
  - The fix round also covers F1 (docs), K1 (the comment), K3 (ledger line references) and K2 (issue text for the three copies of the externality rule; file it as DI-125 when the lane reports).
  - The external/unknown guard is a private mirror in `SignalReadService`, as uac-r2 required; do not call Api/Filters.
- **164 (#2299):** Phase 2b in `Deploy-Release.ps1`. Fix round 1 covers:
  - F1: a failed BFF skips 2b and blocks Phase 3;
  - F3: extract a gate function and test it as a truth table, with mutants M1/M2/M3/M5 plus a BFF mutant;
  - F4: help text and `scripts/README.md`, plus the SKILL.md text the main session applies.
- **160:** composeOnly prop with split commits (D-187). An enabled systemuser gets the app link, including flagged-external. The client commits addresses (the inquiry executor does not resolve ids). The absolute app link uses `DataverseOptions.EnvironmentUrl`. `EntityName` is "{core name} ({number})" with no prefix, because it also writes `sprk_regardingrecordname`.
- **ebilling F4(b):**
  - The adopted design is the line's invoice plus the frozen-core bound. No SignalWriter change.
  - AccessColumns on sprk_signal: corerecordtype, corerecordid, regardinginvoice, regardinginvoiceline.
  - Pass 1 checks that no ontology core-stamp writer goes through the generic path.
  - The residual is DEF-043 / #2294.

## Blocked follow-ups (ours)

- `OntologyAdminClassificationService.cs:658/685/705` labels an in-force billing version Draft. Blocked until `sprk_publishstate` is in code on master.
- Five stale `DecisionRecordWriter.cs` comments (:556 :1547 :1660 :2164 :2326) and the `DecisionRecordContextImmutabilityTests.CreateEntryPoints` check. Do these after ebilling 074 reaches master.
- Close **#2212** when ebilling's `f0884a175f` reaches master. When ebilling 015 reaches master, update the `notes/102a-authoring-contract.md` residual row and D-161.
- The advisory ADR Compliance CI job times out; left to the ci-cd owners (#2009).

## Open owner / peer items

1. Owner question: the long-term document parent column, `sprk_X` or `sprk_related*` (DocumentLinkFieldMap.cs:42-49 marks `sprk_X` as legacy).
2. ebilling reviews when sent:
   - their D-185 role-XML PR (`prvReadsprk_ruleset` None on Basic and Core; it overlaps 163's Basic User XML on different lines);
   - 097, 116, 082 (082 must remove the `sprk_procedure` null filter).
   - Every ebilling schema run: the final script and `-WhatIf` come to us first, with before and after notices.
3. uac-r2 #2091 follow-up and field-mapping 033 design note: review when sent.
4. CORS on `/api/v1/external/notifications` is R3's. Never dispatch `deploy-external-spa.yml` from master while R3's SPA work is off master.
5. POL-MATTER-NOBUDGET is on (D-163): the owner bulk-closes the dev Signals after the next evaluation.

## Dev state (spaarkedev1 / spaarke-bff-dev)

- Dev BFF is `1fa0cf5cb8` (R3, 2026-10-23 ~00:00Z; carries 157 and #2282). Smoke and logs PASS. It does NOT yet carry 159, 161 or 163.
- Core User has AppendTo on `sprk_eventtype_ref` (Global), granted on 2026-10-23 for task 159; the repo XML now matches.
- The Console auto-deploys from master. The landing tab is the Worklist (D-164).
- Worktrees:
  - `C:\wts-160` is the only lane worktree (163 and 164 were removed).
  - R2: `C:\code_files\spaarke-wt-spaarke-ontology-platform-r2`.
  - Remove a worktree only with `scripts/worktree/Remove-WorktreeSafe.ps1`.
- Pester 5.7.1 for local runs is at `C:\Users\RALPHS~1\AppData\Local\Temp\p571\Pester\5.7.1`. Pester 6 fails on this machine only; CI is fine.

## Peers (current session names)

ebilling `spaarke-wt-ebilling-r1-5b` · R3 `spaarke-wt-spa-external-access-platform-08` · uac-r2 `spaarke-wt-unified-access-control-r2-ac` ·
word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4` · field-mapping `spaarke-wt-field-mapping-server-write-pa-be`. Send before and after notices to
word-add-in, R3 and uac-r2 for any dev BFF, Console or landing change.
