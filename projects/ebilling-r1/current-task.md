# Current Task State

> **Last Updated**: 2026-11-12 (session `ebilling-r1`, Claude Desktop; the only writer to this worktree). The previous long version is `notes/handoff-history/current-task-2026-11-11a.md`; read it for history.
> **State at 2026-11-12:**
> - **#2689, #2708 and #2724 (116) MERGED** to master. #2724 merged at 05:25Z (merge commit, auto-merge, owner "Yes proceed").
> - **Dev:** BFF `6dbe69b9e2` (deployed by uac-r2 2026-11-12; a local merge of master `a654da59c4` and mcp-r1's spike `755987b385`; carries #2724 (116), #2708, #2695, #2719, #2721, #2723; every later dev deploy must include the spike). 164 and 096b ribbons applied (with 029's hides); SecureChild `-Verify` exits 0.
> - **116's column `sprk_procedureversion.sprk_notconvertedlist` IS on dev** (Memo, secured, audited, created 2026-11-10 14:35 by 116's own run of 080's script). A 2026-11-12 `-WhatIf` of `Deploy-EbillingProcedureSchema.ps1` reported 0 created, 0 changed; no write was made. The earlier "not on dev" note was wrong; uac-r2 and provisioning-r1 were told so. #2724 may go into a dev deploy. **Stamps** still need SpaarkeMaster 1.3.1.0 (which adds the column to source) before they get a BFF carrying 116.
> - **SpaarkeMaster 1.3.1.0** (Build over the published 1.3.0.0, `d53d67f604`) MERGED to master in #2727 (`2b79f06b4b`, 2026-11-12 14:41Z); NOT yet published. It contains:
>   - the version bump and 116's memo in source, with `sprk_notconverted`'s grants and the EB-D-211 Description;
>   - a release-history row in `docs/procedures/SPAARKE-SOLUTION-RELEASE-PROCESS.md`;
>   - `spaarkemaster-1.3.1.0.json`.
>
>   ArchTests 2505/2505; agreement and procedure tests 551/551. Publishing (`publish-dataverse-solutions-manifest.yml`, `publish: true`, from master) is the owner's step after merge. provisioning-r1 is waiting for the published version, because its H7b S26 stops new runs until 166's lock ships.
> - **#2725 (profile matched by name):** our three scripts are fixed (`f7240c397b`, merged in #2727). The other 15 sites belong to uac-r2 and R3.
> - **Next (checkpoint 2026-11-12, late):**
>   - **084: REOPENED, fix round 3 MERGED** (work `1421e49296`). uac-r2 F-A/F-B (fail-open), F-D, K2 fixed; deny-each-row and version-removal guards; 761 Procedure tests. **Sent to uac-r2 for the scoped re-check (2026-11-12, with the 084f role diff). WAITING. When it passes: re-close 084 (POML completed, TASK-INDEX ✅, drift check).** The CurrentEpisodeVersion duplication of the resolver's private RecordRuleVersionOn goes to 084e (constraint added, `docs` commit before the merge).
>   - **EB-D-220 (owner typed "Billing admins (Recommended)" 2026-11-12):** Billing Administrator and the Billing Rule Reader role get Read at Deep on sprk_procedureversion and sprk_procedureassignment. Task **084f MERGED** (work `226012d72e`; ArchTests 2513); role diff sent to uac-r2 (waiting). Known limits: config/feature-role-privileges.json does not list the Reads (Set-FeatureRolePrivileges never removes outside its set, so it's docs drift, also for 089e's sprk_procedure Read); the next solution version's role-expectations file (at 079) must list them; 112's dry run must confirm prvReadsprk_procedureversion exists in metadata. Dev apply only at 112, with the owner's typed OK plus uac-r2 notices.
>   - **084a: DONE, MERGED** (work `88c3ab01ea`): RecordProcedures code page, web resource `sprk_recordprocedures`, 107 jest tests, build OK. ui-tests run at 112. For 112: the Work Assignment form has no Billing tab (add one); Matter and Project use `tab_billing`.
>   - **uac-r2 condition 2: DONE.** 112 step 9 runs the private check (session memory `eb-d-218-access-invariant-check.md`); message uac-r2 only on FAIL.
>   - ISS-015 (#2743, eBilling pages not linted) filed.
>   - Agent worktrees to remove with Remove-WorktreeSafe once done: `agent-afdaef28d1031b1cf` (locked by its pid), `agent-adeafd262169b50f5` (084a), `agent-ae189e2e785c30cb8` (084 r4), `agent-a63ad511b8b772864` (084f), `agent-ae6525e6c0d46e864` if it exists.
>   - Owner step: publish SpaarkeMaster 1.3.1.0, then tell provisioning-r1. ontology-r1 note (DEF-059 role) still owed. word-add-in-r1's #2552 waits on the owner.
>   - **Next tasks:** 084b → 084c; 084d after 084b; 084e (opus; ontology-r1 reviews Signals; now also extracts the episode filter) after uac-r2 signs off 084.

## Quick recovery

| Field | Value |
|---|---|
| **Mode** | Owner: "keep progressing on open tasks so we can complete the project"; run the uac-r2 review cycle quickly. At most 4 agents; every launch states a model. One adversarial pass per change set, plus a scoped re-check when the approach changes. Never use `--no-verify` or `git stash`. |
| **Agent worktrees** | `isolation: worktree` starts on MASTER. Every brief names the base sha and checks `merge-base --is-ancestor`. Never cd into one; use `git -C`. Remove one with `git worktree unlock`, then `scripts/worktree/Remove-WorktreeSafe.ps1 -Path <one>`. |
| **Public-repo rule** | No access-gap mechanisms, identity names, role memberships or privilege maps in notes, comments, commits, PRs or issues. Private findings go to uac-r2 (and provisioning-r1) by message only. |
| **Live-change rule** | A live Dataverse write or role change needs the owner's OK typed in THIS session, plus notices to uac-r2 before and after (and to ontology-r1 for `sprk_policyversion`). |
| **Peers** | uac-r2, ontology-r1, spa-r4, provisioning-r1, word-add-in-r1 (reachable again 2026-11-11), mcp-r1, field-mapping-r1. |

## Owner answers, 2026-11-11 (typed in this session)

1. **Delete `sprk_sourcedocument`** (rule set and rate card): YES. Run it only after 079 deploys the forms without it AND a BFF containing 116 is on dev. Rationale: the citation entry is removed in the same change as the `-Delete` (116 round 3). Send uac-r2 notices before and after.
2. **Dev deploy of the batch: YES.** Before the deploy:
   - update this worktree to the latest master;
   - run `/merge-to-master` so other projects see the code;
   - check conflicts with the other projects.
   The deploy itself goes through uac-r2's session, coordinated with mcp-r1 (dev carries mcp-r1's spike `755987b385`). uac-r2 may want the owner's go typed in its own session; tell the owner if so.
3. **DEF-060 column `sprk_firmdescription`: YES.** Add it on dev when the DEF-060 build reaches that step (after 116). Send notices to uac-r2 and ontology-r1 before and after.
4. **164 ribbon `-Apply`: YES.** Run it once our ribbon source removal is on master; afterwards send the read-backs to uac-r2 (`notes/164-add-line.md`).
5. **084: DECIDED, "Full prototype" (EB-D-217).** The tasks are 084 and 084a–084d (see the state block above).
7. **096b on dev: YES**, plus restoring 029's seven Activate/Deactivate hides. Done 2026-11-11.
8. **#2708: "Merge on green".** Merged.
6. **Peers:** use cross-session messages; tell the owner if one fails.

## Agents running

- None. **116 rounds 5–6 are done:** squashed into `03745c9483` on `origin/task/ebilling-116-r5` (on top of round 4 `ae0a9cffa3`).
  - The Opus re-check of round 5 found no defects in the code paths. Its two F4s (wording; the `/draft` 404 now answers the missing-version body) and K-4 (job-lock owner) were fixed in round 6, and the session reviewed the round-6 diff.
  - Build 0/0, ArchTests 2206/2206, filtered 9493/0.
  - **uac-r2 APPROVED it** (2026-11-11). Its one note, accepted: Read-but-no-Write answers 403 where an unknown Procedure answers 404, and that's fine under NFR-01.
  - `task/ebilling-116` is fast-forwarded to `03745c9483`.
  - **Merge it into work only AFTER #2689 has merged** (otherwise it rides in that PR). Expect a comment conflict in `RuleSetEndpoints.cs`: keep work's neutral wording. Then close out.
- **#2689:** the scoped-publish-lint failure is fixed (`c9267ccf0c`; Pester 77/0). uac-r2's approval stands, since everything after its read is notes and lint only.
- The master-sync blockers are done (modal keep-mounted shape at 5 sites; ontology's 176 and BudgetInquiry test doubles; uac-r2's boundary test helper was missing `readDocumentChains`). Peers have been told.

## Next actions, in order

1. **Master sync: DONE (work at `64ec7a15b9`); waiting on uac-r2's `--remerge-diff` read.** Then re-check `merge-tree` against the newest master before the PR. The notes below are kept for the record:
   - When the blockers are fixed, MERGE `merge/ebilling-master-sync` into work. Don't fast-forward: work has newer commits.
   - Re-run `merge-tree` against current master. uac-r2 (2026-11-11) warns that master now has #2673 (F-198-1) and #2679 (#2669: `ExternalModuleDataEndpoints.cs` and `ExternalDataService.cs` around the document narrowing), with #2677 (198b) coming. Our batch also edits `ExternalModuleDataEndpoints.cs`, so expect a hand-resolve there and list it for uac-r2.
   - Send uac-r2 the sync SHA and the hand-resolved files. They read `--remerge-diff` for the ExternalAccess files and `FeatureRolePrivilegeGuardTests` before the PR opens. Guards and ratchets: union only.
   - Hand-resolved so far: `Set-CustomerOwnerTeams.ps1` fixes `2011574c2c` and `50c6b9ee76` (provisioning-r1 confirmed them); test signature fix `7171d01aa6`.
2. **`/merge-to-master`** (owner-authorized). **PR #2689 is open** (head `ec11c6d0e2`; publish delta +0.46 MB, 191/191 files; ArchTests 2430/2430; build 0/0; no vulnerable packages). Auto-merge is OFF. **ontology-r1 APPROVED** at `ec11c6d0e2`, with public Ks K1, K3 and K5 on the PR. It also explicitly approved DEF-061 `340b6c53ad`. After merge we owe ontology-r1 a comment-only fix: the O3 offline guard's "the evaluator's reading role" comment claims more than the guard checks. Its private K stays in messages only. **spa-r4:** its owner chose to continue R4 tasks instead of reviewing #2689 first (2026-11-11). R4 resolves the overlaps after #2689 merges, **Owner (typed here, 2026-11-11): "Merge on green CI"**, without R4's review. GitHub auto-merge is on (merge method) and the app's Auto-fix watches CI. After the merge: send the SHA to uac-r2 (redeploy), ontology-r1, spa-r4, provisioning-r1 and word-add-in-r1. Then run the 164 `-Apply`, merge 116 into work, and fix ontology's K4 comment. Client-lint fix `bbfebcc15b`; CI re-running. **uac-r2:** review pending. Merge only after all three reviews and a full green rollup (`mergeStateStatus` CLEAN). Then send the merge SHA to uac-r2 (it runs the dev deploy) and to all peers. The original plan, for the record:
   - Measure the publish-size delta against fresh master (root §10).
   - The PR gets reviews from ontology-r1, uac-r2 and spa-r4.
   - Notify every peer with the SHA.
3. **After master:**
   - dev deploy via uac-r2 and mcp-r1. The owner approved it in uac-r2's session on 2026-11-11 (D-2): a dev BFF deploy from master once our batch lands, with mcp-r1 coordinated. uac-r2 runs it after `/merge-to-master`;
   - the 164 `-Apply`, plus read-backs to uac-r2;
   - one message to provisioning-r1 (master SHA + SpaarkeMaster version for DEF-059, 166 and 089d option R; the 166 field-security grants with `-Verify`; 116's `sprk_procedureversion.sprk_notconvertedlist` reader/writer line; the writer-unit team needs no grant);
   - word-add-in-r1 (session-move notice done 2026-11-11):
     - the master SHA carrying 095c-2..4 (it releases pane PR #2552);
     - the 095a/095b/151e PR plus SHA;
     - **the dev SHA that carries 095a**, so the owner can retest the already-submitted banner;
     - the 151e SHA.
     - Ours, with the owner's OK: backfilling the already-submitted marker for older submissions. The pane has no approve UI until 110/111/112.
     - They will tell us when Slice C (removing Invoice from Save and "+ New") is approved, for our 096.
4. **116, after round 5:**
   - a scoped Opus re-check;
   - the rounds 2–5 diff to uac-r2 (it waits for our re-check);
   - merge `task/ebilling-116-r5` into `task/ebilling-116`, then into work;
   - close-out: notes; TASK-INDEX; record the 080 dev apply in the 116 notes (`861008c591`; 3 created, 2 changed, 86 skipped; verify OK); the K note; PR text explaining why condition 1 was revised (memberless owner team).
   - 116 ships in a later deploy, separate from the batch.
5. **DEF-060 build** (`notes/def-060-firm-description-design.md`, accepted by both peers, EB-D-212/213, and the EB-D-216 line added):
   - after 116;
   - the column add on dev (owner go already given);
   - ontology-r1 reviews `Services/Signals/**` before merge.
6. **Task 134 (SPA invoice detail):** its RecordDetailModal trigger has fired. R4 task 005 (`8ad7e132f8`, PR #2681, not on master yet) deleted `RecordDetailModal.tsx`; records now open in the config-driven `components/record/RecordTemplateModal.tsx`.
   - 134's Outcome section becomes a new `record/sections/OutcomeSection.tsx`, registered in `record/sections/index.ts` and added to the `sprk_invoice` entry in `record/recordTemplates.ts`. No modal edit is needed.
   - R4 task 021 also edits the `sprk_invoice` entry (Files over our `/invoices/{id}/files`). **Ping spa-r4 before 134 starts**, to agree the order.
   - **To file after the merge (`/project-defer-issue-tracking`):** the Invoice Review page does not notice on its own that the Set-firm gate has opened. It needs a review re-read (an action, the Pending-line poll or a reload), because nothing polls while `firmNeeded`. The Codex P2 fix `546296be5c` covers the re-read case only.
7. **Remaining tasks:** 131–135 (notice, notifications, PDF, SPA detail and guidelines), 093, 096, 113, 117, 121, 123, 124; deploy tasks 053, 079, 112, 136, 153; then 160, 162, 163, 189, 190.

## Branches

| Branch | Head | State |
|---|---|---|
| `merge/ebilling-master-sync` | `7171d01aa6` + blocker fixes | master `c71414c7a0` merged; build 0/0; ArchTests 1 fail and the 176 test 1 fail (being fixed) |
| `task/ebilling-116` | `cf323d251f` | rounds 1–3 done; round 3 re-check APPROVED |
| `task/ebilling-116-c` | `ae0a9cffa3` | round 4 (deactivate on redraft); re-check NOT APPROVED, so round 5 |
| `task/ebilling-116-r5` | (running) | round 5 |
| `task/174-billing-refusal` | `9423f37004` | not in work; check what it is before removing |

Two orphan folders: `C:\code_files\spaarke\.claude\worktrees\agent-a28d8883986230c2f` and `agent-a4fe3b028fa655e29`. Both are deregistered and probably locked. Delete them by hand after a reparse-point check.

## Rulings in force (detail in `notes/decisions-log.md`; EB-D-213–215 are also in CLAUDE.md §3)

- **D-70.** List fault: hide the rows and set `truncated`. Single faulted record: 503. Decided-hidden: uniform 403/404. No firm: no invoices.
- **EB-D-214.** A replacement upload resets a drafted version.
- **EB-D-215.** The invoice walk is bounded at 5 hops (`InvoiceLinks.MaxWalkHops`) and the narrowing at 3. A service request counts as a hop.
- **EB-D-216 / 216a.** A redraft deactivates superseded drafts, never deletes them. The check is the reject-equivalent check as the caller (`DecideRule()` + Read on the set + Write on the Procedure), because the draft owner team is memberless by contract. Only still-Draft rows are touched, with a row-version check, in one transaction. Readers exclude inactive drafts (statecode guard). Citations still resolve. No role or team change.
- **The undecidable chain on a single-record GET answers 503** (uac-r2); writes answer 403.

## Owed or open (beyond the next actions)

- **ontology-r1:** the RowReads rerun after 095c-1's dev apply. PowerShell, with `SIGNALS_LIVE_DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com`, `SIGNALS_LIVE_CALLER_ID=3121bf1b-9fbf-f111-aaaf-0022482913fc`, `AZURE_TOKEN_CREDENTIALS=AzureCliCredential`, then `dotnet test --no-build`. Also owed: the DEF-059 outcome; the DEF-060 Signals diff; the 176 test fixture change.
- **uac-r2:** the 165e/165f batch SHA; the DEF-060 build diff.
- **Confirm the 130d TASK-INDEX row** (rigor and model were guessed as FULL, sonnet/high).
- **C3 dev depth query for F3b** (read-only): the deepest child-to-child chain; raise the bound if any firm-visible row exceeds 5. Issue #2667.
- **Issues filed:** #2667 (5-hop limit), #2668 (client resolver), #2669 (uac-r2, the 188 narrowing `truncated`).

## Standing gates

- The creator-person `-Verify` exits 0 before any BFF deploy.
- Reconcile stays OFF until DEF-044 is on master and L6 passes.
- No stamp gets a BFF with 092+ before SpaarkeMaster 1.3.0.0, nor with 095a before 1.3.0.1.
- One master PR, reviewed by ontology-r1, uac-r2 and R4.
- Nothing from a batch reaches dev until all of it is merged.
