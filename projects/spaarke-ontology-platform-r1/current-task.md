# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-23 ~07:00Z (main session, context-handoff). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 and §6. Decisions: [`notes/decisions.md`](notes/decisions.md)
> (D-1..D-189). Deferred work: one self-contained GitHub issue per item, label `ontology-deferred`, plus a DI-nn row in both tables of
> `notes/defer-issues.md` (next free: **DI-128**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | UAT round 1. **All UAT-1 code is on master.** `0317579272` (#2303) has 158, 159, 161, 162, 163, 164. `58ffc37da3` (#2313) has 160, plus #2314 (uac-r2 F1: the footer label is internal-only; uac-r2 ACCEPT) and the #2311 157 sentence pin. The Console is republished from `58ffc37da3` (run 39914343605, 06:57Z). After-notices are sent to R3, uac-r2 and word-add-in. |
| **Status** | No lane is running and there are no lane worktrees. Waiting on the **dev BFF**. R3 owns it; candidates are `6cbfb2ea00` (without 160) or `58ffc37da3` (with 160). Ontology OK'd both. uac-r2's OK covers `58ffc37da3`. **The owner must approve the SHA in R3's session.** |
| **Next Action** | On R3's after-notice (the live SHA), verify it with the DLL ProductVersion method (CLAUDE.md §6). Then run steps 2a–2d below in order. |

## Next, in order

1. **Dev BFF:** R3 deploys after the owner approves the SHA in R3's session. Every candidate must contain R3 077 (`283466d40`). uac-r2 runs its server gate after the deploy.
2. **After the BFF is on dev, in order:**
   - **(a) Worklist config:** `Apply-WorklistGridConfiguration.ps1 -WhatIf`, then apply, then `-Verify -RequireRow`. **Ask the owner first** (a dev data change, not yet approved).
   - **(b) POL-MATTER-NOBUDGET v2:** `Apply-DoLanePolicies.ps1 -DefinitionPath config/know-promotion.policies.json`, then `-Verify`. Owner-approved (D-186).
   - **(c) D-185:** `RemovePrivilegesRole` on **Console User only** (`prvReadsprk_Policy`, `prvReadsprk_PolicyVersion`). Basic User already lacks them (security-roles §19).
     - Read back before and after.
     - Send uac-r2 the #1355 read-back with UTC times.
     - Write the §19 "applied" entry and a deploy-log entry.
     - Tell ebilling.
   - **(d) Dev checks:**
     - 158: the side pane and the record modal.
     - 159: Assign Task creates a Task event.
     - 161: Create budget.
     - 162: uploads to a WA, an invoice and a project show on the parent's Documents (zz- rows).
     - 160, only if the BFF carries `58ffc37da3`: the composer, the To pre-fill, and the link and label for internal and external recipients (zz- contact).
     - Then close 114, 160 (✅ + POML) and 163.
3. **Next docs→master PR** carries the D-185 guard (#2316, `20d7cbd47c`) and today's docs. It is test-only, so it can ride with the next batch.
4. **ebilling #1911:** they send the F1 merge SHA. Check `git diff origin/master <sha> -- Services/Signals/EarlierInvoiceReadability.cs` against the #2294 round. K1 (Billing Administrator policy Read goes to Deep, uac-r2) and K2 (Core User CRUD on BFF-only tables) go to the owner through ebilling.
5. R2 §9 owner answers → `/design-to-spec`; 105 UAT; #1882 live re-run; 111 row 4; 090 wrap-up; the `[SIGNAL-EVALUATOR]` heartbeat.

## Open owner questions

- Approve the dev BFF SHA in R3's session (`58ffc37da3` includes 160).
- Worklist config apply on dev (step 2a).
- **DI-126 / #2310:** a direct Duplicate Signal read shows the earlier invoice's tracking number. Pick one of:
  - A: store only the id;
  - B: remove native Read;
  - C: accept;
  - D: field-secure the detail columns and the sentence (uac-r2's proposal).
- Did the owner edit Spaarke Basic User around 20:01 EDT on 10-22? (Doesn't block anything.)
- Long-term document parent column (`sprk_X` vs `sprk_related*`; `DocumentLinkFieldMap.cs:42-49`).
- `C:\wb160p` (publish output) couldn't be deleted by the lane; remove it by hand.

## Lane facts to carry

- **160 rules:**
  - Link by recipient (D-187): an enabled systemuser gets the app link, even when flagged external; anyone else gets the portal link.
  - The footer label "{name} ({number})" is used ONLY when the matter is exactly Standard and every recipient is an enabled systemuser with `IsExternalAsync` false. Otherwise the footer uses the subject (#2314).
  - **Known limits:** the `IsExternalAsync` cache lasts 10 minutes; Sent Items shows whether a typed address is an enabled user (uac-r2 K2, accepted); a null access permission gives no label (stricter than `ExternalParticipationService`).
- **163:** the external/unknown guard is a private mirror in `SignalReadService` (uac-r2). A resolver fault gives 503.

## ebilling (peer reviews)

- Task 122 (`Set-EbillingFindingsJsonFieldSecurity.ps1`): **PASS** sent, with K1–K6. Their dev run also needs the owner's OK in their session.
- `a0fdd8ff5e` (`sprk_lane`, `sprk_policy` and `sprk_policyversion` refused to app-only generic writers): re-check **clean**; 2 nits sent.
- **Pending reviews when sent:** their D-185 role-XML PR (`prvReadsprk_ruleset` None on Basic and Core); 097, 116 and 082 (082 must remove the `sprk_procedure` null filter); every schema run (final script and `-WhatIf` come to us first).

## Blocked follow-ups (ours)

- `OntologyAdminClassificationService.cs:658/685/705` labels an in-force billing version Draft. Waits for `sprk_publishstate` in code on master.
- Five stale `DecisionRecordWriter.cs` comments (:556 :1547 :1660 :2164 :2326) and the `DecisionRecordContextImmutabilityTests.CreateEntryPoints` check. Wait for ebilling 074 on master.
- Close **#2212** when ebilling's `f0884a175f` reaches master. When ebilling 015 reaches master, update the `notes/102a-authoring-contract.md` residual row and D-161.
- The advisory ADR Compliance CI job times out (cancelled at about 5 minutes; #2009). It is not blocking.

## Dev state (spaarkedev1 / spaarke-bff-dev)

- The dev BFF is `1fa0cf5cb8` (R3, about 00:00Z). It does NOT carry 159, 161, 163 or 160.
- The Console is `58ffc37da3`. Its new composer shows only when the BFF marks `composer: "email"`, so the old fields stay until the BFF has 160.
- Core User has AppendTo on `sprk_eventtype_ref` (Global; task 159). The repo XML matches.
- Worktrees: none for lanes. R2 is at `C:\code_files\spaarke-wt-spaarke-ontology-platform-r2`. Remove a worktree only with `Remove-WorktreeSafe.ps1`.
- Pester 5.7.1 for local runs is at `C:\Users\RALPHS~1\AppData\Local\Temp\p571\Pester\5.7.1`.

## Peers (current session names)

ebilling `spaarke-wt-ebilling-r1-5b` · R3 `spaarke-wt-spa-external-access-platform-08` · uac-r2 `spaarke-wt-unified-access-control-r2-ac` ·
word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4` · field-mapping `spaarke-wt-field-mapping-server-write-pa-be`. Send before and after notices to
word-add-in, R3 and uac-r2 for any dev BFF, Console or landing change.
