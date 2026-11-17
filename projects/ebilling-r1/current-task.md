# Current Task State

> **Last Updated:** 2026-11-17. Session `ebilling-r1` (Claude Desktop) is the only writer to this worktree.
> Older detail: `notes/handoff-history/current-task-2026-11-16a.md` (and `-2026-11-15a.md`, `-2026-11-13a.md`).

## Owner mode

- "Continue with open work items that are available"; keep `notes/owner-testing-checklist.md` current.

## NOW — read this first

### Active (2026-11-17): 095f part 2 DONE except 2e2 (owner pick)

- **095f-2b ✅ merged a3ef7e1c93** (fix 64c3e9ecbb: server-derived namespaced decision ids, neutral lookup 503; ontology-r1 PASS on catalog, exact-set amendment and fix; known limit ISS-030 / #2881).
- **095f-2ui ✅ merged d3a61cc426** (review + 3 fix rounds + tweak). Page-level locks keep a partly saved decision exact on repeat; drafts survive 409s. Known limits are in its notes (locks are memory only; the Override/Return switch guard is client-only, see ISS-031 #2882). ISS-032 #2883 covers a decision stranded after its transition. Checklist section F added.
- **130e-2 ✅ merged 58dcc56689** (Rule Review firm description field; the server validator now folds the Turkish i in the name check and refuses default-ignorable runes; ontology-r1 PASS on the server part). The 084h parent row is closed (084h-1..4).
- **Nothing in flight.** Next: 2e2 waits on owner pick (a). Remaining open tasks are larger or gated (116/117/121/123/124, 130-136, deploys).
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
- **084h parent row** is still 🔲 although 084h-1..4 are all ✅. Decide whether it closes or holds remaining scope.
- **Test gate rule:** never run the whole BFF test project in one process on this machine (load timeouts). Use CI's five shards (scratchpad `run-shards.ps1`); known flaky #2543. Run the shards again before any master merge. Implementer agents use filtered runs.
- **HARD GATE before work → master:** reject/return/override/blocked return always write `sprk_invoice.sprk_decisionnote`, so no BFF carrying 095f-2a/2c may reach an environment before `Deploy-EbillingDecisionNoteColumns.ps1 -Apply` ran there (else 503). Dev BFFs deploy from master (several projects), so the dev column apply (owner question (c), uac-r2 notices before and after; it also creates `sprk_invoicecorrection.sprk_note` for 2d2) must precede the master merge. Other environments get it via 079's SpaarkeMaster sync.
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

1. ~~Ten design answers~~ ANSWERED 2026-11-16: EB-D-239 to EB-D-243 (decisions-log). POMLs written: 084h-2/3/4, 095f-2s/2a/2c/2d1/2e1/2b/2d2/2ui. **Open (asked 2026-11-16, unanswered):** (a) the internal Correction path for 095f-2e2: B′ recommended / A / D (design §16); (b) is the external allow-list enough for `sprk_decisionnote`, or field security so only reviewers see it (uac-r2: external-flagged licensed users can read it in the MDA by role depth); (c) the go for the dev apply of 095f-2s's two columns (uac-r2 notices before and after) — now a HARD GATE before any master merge; (d) [asked 2026-11-17] confirm the ADR-004 §6.5 Path A exception for 084h-3 (receive-side idempotency not atomic, #984; same as ontology 149 K2 and 084h-1 K-b; ontology-r1 no objection); (e) [asked 2026-11-17] ISS-028 / #2879: make `sprk_invoicecorrection` writer-only in RecordTableRules (`CreatedOnlyByItsWriter`) or keep it generic-writable and add a writers guard; (f) [asked 2026-11-17] ISS-034 / #2888: an operator path for a blocked decision stranded by a decider who is gone (admin "finish as operator" route recommended), made reachable by the ISS-031 guard.
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
