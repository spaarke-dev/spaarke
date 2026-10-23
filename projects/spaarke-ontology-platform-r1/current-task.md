# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-23 ~03:30Z (main session, context-handoff). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 and §6. Decisions: [`notes/decisions.md`](notes/decisions.md)
> (D-1..D-187). Deferred work: one self-contained GitHub issue per item, label `ontology-deferred`, plus a DI-nn row in both tables of
> `notes/defer-issues.md` (next free: **DI-126**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | UAT round 1 lanes. **Merged to docs:** 158 `539ab22ce9`, 159 `9b485535ce`, 161 `811167613f`, 162 `84636b650a`. **docs updated from master** `0a6e8820fd`. **Running:** scoped Sonnet re-check of 163 fix round 2 (`952477e0d0..c26909dfd5`, PR #2298); scoped Sonnet re-check of 164 fix round 1 plus my SKILL.md commit (`e45414a360..8492519d9c`, PR #2299); 160 lane (`C:\wts-160`, opus, D-187). ebilling is running its own F4(b) fix round; pass 2 follows when they send SHAs. |
| **Status** | Waiting on the two re-checks and the 160 lane. 163's K2 is filed as DI-125 (#2302). |
| **Next Action** | Clean re-check → merge that PR. Whichever of 163/164 merges second reconciles guide §7.8: keep 164's paragraph, drop 163's "Deploy-Release does not apply it" and the `-SkipPhase Solutions` workaround, keep 163's "old BFF + new row loses names and severity". 160 → Opus review; send the EmailComposer hunk to the email project and the GrantAccessNotifier hunk to R3. |

## Next, in order

1. **After 163 merges:** docs→master PR. Then ask R3 (`spaarke-wt-spa-external-access-platform-08`) for a dev BFF deploy, giving the **exact candidate SHA**. R3 gets the owner's own approval in its session (a relayed approval does not count), collects the peers' OKs and runs its 30-minute gate. The candidate must contain R3 077 (`283466d40`). Send before/after notices to word-add-in, R3 and uac-r2. The Console auto-deploys from master.
2. **After that BFF is on dev, in order:**
   - (a) Apply the worklist config: `Apply-WorklistGridConfiguration.ps1 -WhatIf`, then apply, then `-Verify -RequireRow`. That config is a dev data change, and we have not yet asked the owner about it; ask first.
   - (b) POL-MATTER-NOBUDGET v2: `Apply-DoLanePolicies.ps1 -DefinitionPath config/know-promotion.policies.json` (owner approved, D-186). The `-WhatIf` run was clean on 2026-10-23: it creates v2 (`assign-task`, next steps add-todo and assign-work), ends v1 at 2026-10-22T00:00Z and moves the pointer. Then `-Verify`.
   - (c) D-185 role change (owner-authorised, main session). Use `RemovePrivilegesRole`: Basic User loses `prvReadsprk_Policy`; Console User loses `prvReadsprk_Policy` and `prvReadsprk_PolicyVersion`. Read back before and after. Test the worklist as a non-Console user and as a Console user. Write a security-roles.md §19 "applied" entry and a deploy-log entry. Tell uac-r2 (#1355, read-back reference) and ebilling, before and after. Nobody exports roles from dev between the XML change and this step.
   - (d) Dev checks:
     - 158: side pane above the wizard; the record modal returns to the same step; light and dark.
     - 159: Assign Task creates a Task event.
     - 161: Create budget on a matter with no budget.
     - 162: upload a file to a work assignment, an invoice and a project (zz- rows) and confirm it shows on the parent's Documents list.
     - Then close 114. #2281 was verified by the owner on 1fa0cf5cb8.
3. **R2** (D-183): the owner answers design.md §9 Q1–Q6, then run `/design-to-spec` in the R2 worktree.
4. **105 UAT** with the owner (`notes/uat-v4-log.md`). Open: C2 (keyboard and screen reader), C4, and the rest of R1–R24.
5. Re-run `SignalReadRouteLiveTests` as a non-Console user (DI-83, #1882), then close #1882. 111 row 4 (owner upload URL test), then close 111. Then the 090 wrap-up.
6. Check tonight's `[SIGNAL-EVALUATOR]` heartbeat with `deploycheck.py`.

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
  - `C:\wts-160`, `C:\wts-163` and `C:\wts-164` are lane worktrees.
  - R2: `C:\code_files\spaarke-wt-spaarke-ontology-platform-r2`.
  - Remove a worktree only with `scripts/worktree/Remove-WorktreeSafe.ps1`.
- Pester 5.7.1 for local runs is at `C:\Users\RALPHS~1\AppData\Local\Temp\p571\Pester\5.7.1`. Pester 6 fails on this machine only; CI is fine.

## Peers (current session names)

ebilling `spaarke-wt-ebilling-r1-5b` · R3 `spaarke-wt-spa-external-access-platform-08` · uac-r2 `spaarke-wt-unified-access-control-r2-ac` ·
word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4` · field-mapping `spaarke-wt-field-mapping-server-write-pa-be`. Send before and after notices to
word-add-in, R3 and uac-r2 for any dev BFF, Console or landing change.
