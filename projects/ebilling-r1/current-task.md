# Current Task State

> **Last Updated:** 2026-11-17. Session `ebilling-r1` (Claude Desktop) is the only writer to this worktree.
> Older detail: `notes/handoff-history/current-task-2026-11-16a.md` (and `-2026-11-15a.md`, `-2026-11-13a.md`).

## Owner mode

- "Continue with open work items that are available"; keep `notes/owner-testing-checklist.md` current.

## NOW — read this first

### Active (2026-11-17, night)

**Merged into work and pushed today (latest first):**
- **131 ✅** 35657bb81c. Firm notice Action, plus the Opus review and 2 fix rounds (prose screen: runes, NFKC/NFKD, marks, number words, any-script domains; rate limit; real negative test on 130's rig). ontology-r1 PASS (catalog), uac-r2 PASS (firm boundary; N2 added to the 132 POML). The fallback wording is approved (EB-D-255). **Dev seed (EB-D-256, approved) waits for a dev BFF containing 131**, which needs master. /healthz/catalog shows Degraded between that deploy and the seed (expected; uac-r2 told).
- **Guard d99b16120b:** FindingsJsonFieldSecuredGuardTests pins `sprk_findingsjson` IsSecured (uac-r2, iss034d K5).
- **iss034d ✅** 5976a9e491. History tab "Finished by" (EB-D-257). Opus: no F; fix round 1 (also hidden when completer = the record's decider). uac-r2 accepted K5. Checklist section K.
- **ISS-037 ✅** 2e763ad6c8. ArchTest C16 fixed: the other-content refusal is typed (`DecisionRecordRefusedException.IsOtherContent`); ontology-r1 PASS.
- **iss032 ✅** 0ec908c554. Finish a reject/return/approve stranded after its transition. Covers both cases: the record is missing, or the record exists but is unlinked. Opus F4 fixed in round 1; the Sonnet re-check found no F. ISS-032 is In Progress until master. Checklist section J.
- **iss034c ✅** ee94c7d4ea. The stamp records the completer (EB-D-253). AC5, the History-tab "Finished by", is deferred: OWNER QUESTION (about half a day).
- **084h-5 ✅** e3eae27ef6, squashed (uac-r2's condition: a removed notes paragraph stays out of public history). EB-D-251 option B. ontology-r1 PASS; uac-r2 closed EB-D-247.
- **iss028 dev apply ✅** (EB-D-250/252). Verify passed; uac-r2's re-probe passed. Checklist section I.
- Index hygiene: 095f, 165, 116 (on master via #2724) and 130 (delivered by 130-0..130e-2) are marked ✅.

**Running / in flight (2026-11-18 early; work tip a02126462e):**
- **ISS-038 ✅ ON MASTER** (PR 2902 merged e5da41db7a; #2896 closed; SHA sent to uac-r2). Master merged into work 3c86e6e2a4 (FieldSecurityProfiles.xml conflict: master rows + ISS-040 rows re-added) + a02126462e (regression test on the work ChangeRuleAsync signature). ISS-039 #2897 is the write side, next round. Dev count query run 2026-11-17: 0, sent to uac-r2 privately (dev has no rule sets or record rules yet).
- **130f ✅** merged into work f0ab99a83b (uac-r2 PASS). K6 (ApprovedAmount still in the API for Rejected/Returned) carried to the 134 POML.
- **ISS-040 (#2901) ✅ merged into work** b17e3da316 (Opus review + round 1, uac-r2 PASS): field-secures `sprk_ruleset.sprk_visibletofirms` (Readers 4/0/0, Writers 4/4/4). **Applied to dev 2026-11-17** on the owner's typed OK ("Yes, apply ISS-040"): WhatIf, run, -Verify all PASS; masked window 29.9s; both BFF app users CanRead/Create/Update=4 on the column; uac-r2 notified before and after. Release (SpaarkeMaster) still pending. Checklist section L. Private (uac-r2 only): Service Writer holds Global Write on sprk_ruleset (owner question for the owner summary).
- **132** (`task/ebilling-132`, agent worktree agent-ab36d191b98d84154): rounds 1-3 done (tip b5c7d0223f; work merged in at 19fb32e29d + index fix 5f29918b6e). **uac-r2 PASS on logic, one F2 test-only** (drive the feed's production row conversion: SDK Entity candidate with a live-parent secure root → Denied, twin Visible, CandidateColumns ⊇ InvoiceScopeColumnNames). An implementer is adding it; send uac-r2 the test diff, then merge 132 into work. **sprk_firmnoticetext field security applied to dev 2026-11-17** on the owner's typed OK ("1. Yes lock"): WhatIf, run, -Verify all PASS; masked window 25.3s (no BFF user masked); both BFF app users CanRead/Create/Update=4; uac-r2 notified before and after. Pass `-BffApplicationIds` as ONE comma string with `-File` (an array binds the 2nd id to -EnvironmentUrl; a first attempt failed that way with no write). K16 (no enqueue on AlreadyDecided) accepted.
- **135 ✅** merged into work (uac-r2 PASS incl. F2 test af64c29cb5, spa-r4 PASS). Checklist section N. Per-record host is spa-r4 R4 surface (N2).
- Then: 132ui, 130g, 134. Agent slips this session: bare python run 3 times by agents (no effect); keep the no-python line in every brief.
- Leftover scratch folders for a human to delete: C:\wt131a and C:\wt131b.

**Decided today:**
- **EB-D-254 (owner, option b): no PDF report in R1.** 133 is withdrawn. 132 sends the findings table in the 131 notice body. 134 prints cleanly; spa-r4's guidance is in the 134 POML: use record sections or action hosts, and scope the print stylesheet to the report. FR-40 is amended.

**Blocked on the owner:** nothing. Decided tonight:
- EB-D-259: Rejected/Returned invoices show their findings (130f, 130g).
- EB-D-260: guidelines show all firms, with a filter.
- EB-D-261: ISS-038 PR plus merge after review; the owner instructs uac-r2 to deploy.

**Master-merge obligation (from 131):** ConsumerTypes.All includes invoice-firm-notice. In every environment: BFF first, then seed (Ensure-ActionRow, then Seed-PlaybookConsumers), with the gap kept short. A row with no constant is a 503 on /healthz/catalog; a constant with no row is only Degraded. uac-r2 deploys dev from master, and it needs the owner's go naming the exact sha.

**2026-11-20: 132ui IN PROGRESS (owner: "Yes, start 132ui"). Rigor FULL, Sonnet @ high.** The POML was written from EB-D-258 and the 132 server contract (`tasks/132ui-invoice-review-notify-step.poml`, e11be217cc). An implementer agent (isolated worktree, branch `task/ebilling-132ui` from e11be217cc) is building it. The draft route is confirmed to answer for In Review invoices (`FirmNoticeComposer.Applies`). NEXT: on its report → code review (Opus) plus adr-check → fix round → merge into work → checklist M item for the notify step. The page deploy to dev needs the owner's OK.

**2026-11-19: DEV BFF 5ae2804884 LIVE (contains fe51619cfc; uac-r2, owner go there); 131 SEEDED on dev about 02:05Z (EB-D-256, uac-r2 notices before and after; the Action row and one Binding row only; see the 131 notes).** /healthz/catalog stays 503 because of pre-existing drift: create-project and create-todo rows have no constants (#1987, commented). Checklist M is testable now. N waits for spa-r4's SPA deploy. NEXT: tasks 132ui, 130g, 134, 117, ISS-036, ISS-039, and deploy 136 WP5.

**2026-11-18: MASTER MERGED — #2904 → master fe51619cfc (merge commit, 21:53 UTC; owner's go "2. Yes merge", then "wait and merge").** Head 8f16b888ca. CI round 2 failures were fixed in 8f16b888ca: the 166 C1 guard gained two task-132 Readers; the ISS-040 script's dot-source form; an ADR-052 comment; a format space; a client-lint Partial map. On that head: Router, all Tier 1, client lint and all five Tier 2 shards are green (api-office 570/0). The office-scope job was cancelled at its 20-min cap (the suite needs ~29 min there; master shows the same). Trivy: pre-existing, identical to master. The master SHA went to uac-r2, spa-r4, ontology-r1 and provisioning-r1 (provisioning-r1: no change needed). word-add-in-r1 got it after the owner confirmed its session. NEXT: uac-r2 builds the dev BFF from fe51619cfc, and the owner gives the exact-SHA go → uac-r2 tells me and spa-r4 → spa-r4 deploys the SPA (135 needs this BFF) → I run the 131 seed (Ensure-ActionRow, Seed-PlaybookConsumers) with uac-r2 notices before and after → checklists M and N become testable. Mark ISS-032, ISS-040 and the other master-waiting items Done.

**Master-merge obligations (owner go given 2026-11-18; all done in #2904):**
- **iss028 PR:**
  - add the five ISS028 entries to `scripts/provisioning/role-privilege-expectations/spaarkemaster-1.3.2.0.json` (Core User absent list);
  - amend the 1.3.2.0 release row: it removes privileges, needs a managed stage-and-upgrade, and dev rolls back with `-Restore -Gap ISS028`;
  - send uac-r2 the PR number.
- **ISS-040 (#2901, merged into work; uac-r2 N1):** the Entity.xml IsSecured and the two FieldSecurityProfiles.xml rows for `sprk_ruleset.sprk_visibletofirms` ship in the same SpaarkeMaster release as the iss028 removals (1.3.2.0 or the next version); add them to that release's notes; tell uac-r2 so it informs provisioning-r1. Readers profile on every default team: confirmed: documented deployment-guide step (`scripts/Set-RecordCreatorPersonSchema.ps1`, SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE ~:1028, re-run for a new business unit ~:1438); not in an automated run list (provisioning-r1's call; uac-r2 tells them). The association is per profile, so it already covers this column. Dev apply: owner's typed OK, `-BffApplicationIds` both dev BFF app ids, uac-r2 notices with `-Verify` output; a failed membership check stops it (never add members).
- **external-spa lockfile:** may need a re-resolve after R4 015 (#2886).
- **Before the merge:** the 5 CI shards, the publish-size delta against a fresh master build, and the owner's go.

**Next candidates:** 134 (SPA; waits on the finding-1 answer for what Rejected/Returned shows), 132 (131 done; uac-r2 reviews it separately, N2), 117 (FULL opus, p-safe false), ISS-036 (a11y, small).

- **095f-2b ✅ merged a3ef7e1c93** (fix 64c3e9ecbb: server-derived namespaced decision ids, neutral lookup 503; ontology-r1 PASS on catalog, exact-set amendment and fix; known limit ISS-030 / #2881).
- **095f-2ui ✅ merged d3a61cc426** (review + 3 fix rounds + tweak). Page-level locks keep a partly saved decision exact on repeat; drafts survive 409s. Known limits are in its notes (locks are memory only; the Override/Return switch guard is client-only, see ISS-031 #2882). ISS-032 #2883 covers a decision stranded after its transition. Checklist section F added.
- **130e-2 ✅ merged 58dcc56689** (Rule Review firm description field; the server validator now folds the Turkish i in the name check and refuses default-ignorable runes; ontology-r1 PASS on the server part). The 084h parent row is closed (084h-1..4).
- **After iss031:**
  - 2e2 waits on owner pick (a).
  - The remaining open tasks are larger or gated: 116/117/121/123/124, 130-136, deploys.
  - Choose the next one with its dependencies met, or ask the owner.
  - Filed today: ISS-030..034. ISS-032 #2883: a decision stranded after its transition can't be finished from the UI. ISS-033 #2885 is ontology-r1's (logged DI-156). ISS-034 #2888 is owner question (f).
- **Peer note:** ontology-r1's PR #2884 (the gate-job lock fix from our 084h-3 review) merged to master at 0de88473b1. It reaches dev with uac-r2's next BFF build.
- **Per-task routine:**
  1. Implementer agent: named model, worktree, brief names the base sha and "never python".
  2. One adversarial review.
  3. Fix rounds plus a scoped re-check; a second verifier pass when a fix changes the approach.
  4. Merge into work.
  5. Remove the worktree (unlock + `scripts/worktree/Remove-WorktreeSafe.ps1`) and its branches (also the empty `worktree-agent-<id>` branch).
  6. Mark the POML and TASK-INDEX.
  7. Push.
- **Wave 3 ✅ (pushed e171823a5b):**
  - **084h-4 ✅ merged b5740ebd79** (fix 35b574b0c9). Server words pinned by a source-scan test; no `maxLength` truncation on the Note, a counter instead. Deploy after the BFF with 084h-2/3.
  - **095f-2d2 ✅ merged 6ea1ceb837** (fix 740e5166fa). The firm-visibility guard now matches the note constants and refuses all-columns reads in firm-facing files.
  - **095f-2c ✅ merged 18733ac44a** (fix c7ffb82f5a; re-check fixes 9ae666fec8). Changes:
    - Every override now writes the invoice, so it retries an invoice-side version fault (bounded, as TransitionAsync).
    - A stranded override or blocked return finishes from its STORED record: stored reason/note, stored decider; another decider 409, unreadable 503.
    - The rights check comes before the stored-decider check.
    - One review-detail derivation, guard-pinned.
    - ontology-r1 PASS on the catalog line and the `DecisionRecordWriter` additive members.
  - **095f-2d1 ✅ merged f9d63c555b** (+ wording ac2e54840e). ontology-r1 catalog PASS. Known limit: ISS-029.
- **Filed this wave:** ISS-026 / #2877 (RecordProcedures has no ESLint config, outside client-lint), ISS-027 / #2878 (Reason boxes cut at 2000 silently), ISS-028 / #2879 (correction rows not writer-only — owner question), ISS-029 / #2880 (line repeat with other content → 503 that never completes; the shipped client can't reach it).
- **Wave 2 ✅:** 084h-3 bb4ad3f578 (ADR-004 Path A pending owner), 095f-2a 7849bd05a0; earlier 084h-2, 095f-2e1, 095f-2s.
- **Test gate rule:** never run the whole BFF test project in one process on this machine (load timeouts). Use CI's five shards (scratchpad `run-shards.ps1`); known flaky #2543. Run the shards again before any master merge. Implementer agents use filtered runs.
- **Dev column gate CLEARED 2026-11-17:** `Deploy-EbillingDecisionNoteColumns.ps1` ran on dev with the owner's go (question (c)) and uac-r2 notices before and after: -WhatIf, -Apply and -Verify all exit 0. Both `sprk_invoice.sprk_decisionnote` and `sprk_invoicecorrection.sprk_note` are in SpaarkeMaster on dev. Every other environment still needs the columns before a BFF carrying 095f-2a/2c (else 503); they get them through 079's SpaarkeMaster sync.
- **Deploy order elsewhere:**
  - Columns first: 079 syncs `sprk_firmdescription`, `sprk_rejectionreason`, `sprk_decisionnote` and `sprk_invoicecorrection.sprk_note`, and empties both pending lists. Then a BFF carrying 130e-1.
  - 079 takes the SpaarkeMaster version after uac-r2's 1.3.2.0 (merged 57431c8e6a) and keeps InvoiceFamilyFieldLockSourceGuardTests green.
  - The RecordProcedures page goes after the BFF with 084h-2/3.
### Done 2026-11-16 (pushed)

- **ISS-022 / #2844 DONE.**
  - The fix is live on dev. The dev invoice index got task 152's fields (`Deploy-AllIndexes.ps1`, in place; `-VerifyOnly` passes).
  - The re-index sweep (`scripts/ebilling/Invoke-EbillingInvoiceReindexSweep.ps1`) indexed all 12 invoices; uac-r2 confirmed 12 handler traces. The search dependency telemetry under-counts; the handler traces are authoritative.
  - uac-r2's post-deploy gate now runs `-VerifyOnly`.
  - ISS-023 / #2849 proposes the same check in `Deploy-BffApi.ps1`.
  - Dead letters: #2848 (job platform; don't replay InvoiceIndexing ones).
- **ISS-024 / #2858 DONE on dev.** The dev BFF selected `sprk_policyversion.sprk_rejectionreason` (task 077), which had never been created on dev.
- **Both columns live on dev** (owner: "Yes, add both"; uac-r2 and ontology-r1 notified before and after):
  - `sprk_rejectionreason` (077 script) and `sprk_firmdescription` (130e-1 script @99e380f997, `-SolutionUniqueName SpaarkeMaster`, `-Verify` PASS).
  - Both are Memo 2000, not required, not secured. Each publish touched `sprk_policyversion` only. Reads naming them return 200.
  - Both are in SpaarkeMaster and OntologyPlatformSolution (each includes the table with subcomponents).
- Earlier, 2026-11-15: 113 + 113b, 084h-1, 093, #2846, #2839. See the archive.

### Open owner questions

1. ~~Ten design answers~~ ANSWERED 2026-11-16: EB-D-239 to EB-D-243 (decisions-log). POMLs written: 084h-2/3/4, 095f-2s/2a/2c/2d1/2e1/2b/2d2/2ui. **ALL ANSWERED 2026-11-17:** (a) B′ → EB-D-245 (task 095f-2e2; the SPA firm resubmission after Return already exists, task 104); (b) no field security → EB-D-246; (c) dev apply done (see the cleared gate above); (d) build a duplicate check, no Path A → EB-D-247 (task 084h-5, design with ontology-r1); (e) writer-only → EB-D-248 (task iss028); (f) option 1 → EB-D-244 (iss034a/b). The original questions, for reference: (a) the internal Correction path for 095f-2e2: B′ recommended / A / D (design §16); (b) is the external allow-list enough for `sprk_decisionnote`, or field security so only reviewers see it (uac-r2: external-flagged licensed users can read it in the MDA by role depth); (c) the go for the dev apply of 095f-2s's two columns (uac-r2 notices before and after) — now a HARD GATE before any master merge; (d) [asked 2026-11-17] confirm the ADR-004 §6.5 Path A exception for 084h-3 (receive-side idempotency not atomic, #984; same as ontology 149 K2 and 084h-1 K-b; ontology-r1 no objection); (e) [asked 2026-11-17] ISS-028 / #2879: make `sprk_invoicecorrection` writer-only in RecordTableRules (`CreatedOnlyByItsWriter`) or keep it generic-writable and add a writers guard; (f) [asked 2026-11-17] ISS-034 / #2888: an operator path for a blocked decision stranded by a decider who is gone (admin "finish as operator" route recommended), made reachable by the ISS-031 guard.
2. ~~Second `word-add-in-r1` session~~ RESOLVED 2026-11-16 (owner): word-add-in-r1 runs on the Ryzen desktop, which is this machine (RGS_RYZEN9950X). The local `word-add-in-r1` session is the right one; the remote one is no longer listed.

**Premise correction to log with EB-D-239+:**
- EB-D-237 assumed the Create Invoice wizard has a Correction path; it has none.
- from-document's client is the shared `emailInvoiceSubmission.ts`, used by OUR CommunicationActions PCF (task 118) and the EmailWorkspace, not the add-in.
- So whether from-document offers Correction is ebilling's call; fold it into 095f part 2.

### Waiting on peers

- **word-add-in-r1 (remote):** the 096 gate is NOT met. The add-in still offers Invoice as a Save target and quick-create. Removal is slice C (#2829), which waits for the owner's merge go in its session. Keep 096's server legs.
- **spa-r4:** the SPA deploy confirmation for checklist E. It accepts the optional `correction` prop (095f §13). It builds R4 021 against 093.

## Next actions (in order)

1. 130e-2 (above).
2. Build in waves (max 4 agents): W1 = 084h-2, 095f-2s, 095f-2e1; then 084h-3 + 095f-2a; then 084h-4, 095f-2c, 095f-2d1, 095f-2d2; then 095f-2b (ontology-r1 amendment review), 095f-2ui; 095f-2e2 after the owner's pick. 084h-2 never deploys without 084h-3.
3. Task 112 (deploy; owner go here plus uac-r2 notices):
   - the 084f role apply;
   - the first create of `sprk_recordprocedures` plus the SpaarkeMaster component;
   - the Billing tab;
   - the Invoice Review page's first deploy (with 113b).
   - Before 095f goes beyond dev: the correction-note column script.
4. Task 079 must sync `sprk_firmdescription` and `sprk_rejectionreason` into SpaarkeMaster and empty the guard's pending list.
   - uac-r2 ships SpaarkeMaster 1.3.2.0 first (#2859: the invoice-family lock moves into source, which sets IsSecured=1 on 24 columns of our six invoice-family tables, plus the profile entries). 079 takes the next version, rebases onto it and keeps those entries (export-to-temp, then diff).

## Standing gates

- **Live writes:** a live Dataverse, Azure or index write, or a role change, needs the owner's OK typed in THIS session, plus uac-r2 notices before and after (and ontology-r1 for `sprk_policyversion` / Signals). Merging to master needs the owner's go.
- **Agents:**
  - At most 4; name the model for each.
  - Launch from the worktree root. `isolation: worktree` starts on MASTER, so each brief names its base sha.
  - Remove a worktree only via unlock + `Remove-WorktreeSafe.ps1`.
- **Hard rules:** NEVER run python. Never `--no-verify` or a bare `git stash`.
- **Public repo:** security findings by mechanism only, and only privately to uac-r2. No identity, role or privilege names in notes, commits, PRs or issues.
- **Ebilling Pester:** run each file on its declared version (5.7.1 or 3.4.0). Plain Invoke-Pester loads 6.2 and fails.
- **Leftover folders a human must delete:** `C:\tmp\wt-iss021`, `C:\tmp\wt-ciw`, `...\.claude\worktrees\agent-a7122a26394c93f4f`, `C:\wt084h2m-pub`, `C:\wt084h2b-pub`, `C:\wt084h2base-pub` (134 MB each, publish outputs).
