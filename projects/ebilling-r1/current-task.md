# Current Task State

> **Last Updated:** 2026-11-16. Session `ebilling-r1` (Claude Desktop) is the only writer to this worktree.
> Older detail: `notes/handoff-history/current-task-2026-11-16a.md` (and `-2026-11-15a.md`, `-2026-11-13a.md`).

## Owner mode

- "Continue with open work items that are available"; keep `notes/owner-testing-checklist.md` current.

## NOW — read this first

### Active: none running. Next available: 130e-2

- **130e-1 ✅ merged into work `a6f1363070`** (from `56bf6dce82`). ontology-r1 O6 PASS and uac-r2 PASS on that tip. Column-first met: `sprk_firmdescription` was live on dev and `-Verify` passed before the merge. Any later push touching `Services/Signals/**` needs ontology-r1's re-glance.
- **Next: 130e-2**, the Rule Review editor field (`src/solutions/RuleReview`). The contract is in `notes/130e-firm-description-writer.md`.
  - Render the description as plain text, never HTML or markdown (ontology-r1).
  - Show the "firms see this even when the rule set is hidden; do not quote the guideline or name the rule set" notice.
  - Explain that a re-sent invalid published text is dropped, and that short names can over-refuse.
  - There's no POML yet. Write it (STANDARD, sonnet) before building.
- **Deploy order elsewhere:** the column first (task 079 syncs `sprk_firmdescription` and `sprk_rejectionreason` into SpaarkeMaster and empties `PolicyVersionColumnsGuardTests`' pending list), then a BFF carrying 130e-1. uac-r2's pre-deploy gate checks the column on dev.
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

1. ~~Ten design answers~~ ANSWERED 2026-11-16: EB-D-239 to EB-D-243 (decisions-log). POMLs written: 084h-2/3/4, 095f-2s/2a/2c/2d1/2e1/2b/2d2/2ui. **Open:** the owner picks the internal Correction path for 095f-2e2 (design §16 recommends B′: Correction on the internal multipart route, reached from Invoice Review), and gives the go for the dev apply of 095f-2s's two columns.
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
- **Leftover folders a human must delete:** `C:\tmp\wt-iss021`, `C:\tmp\wt-ciw`, `...\.claude\worktrees\agent-a7122a26394c93f4f`.
