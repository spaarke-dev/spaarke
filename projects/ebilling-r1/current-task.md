# Current Task State

> **Last Updated**: 2026-10-23 00:xx (checkpoint: two agents running; D-185 decided; this file edited while a work-branch agent may be mid-merge, so commit it with the next commit)

## LATEST (supersedes the table below where they differ)
- **Running agent A (work/ebilling-r1, this worktree, opus/high):** Part A = uac merge-review fixes on e371dce6bf: F4 (sprk_invoicesubmission AccessColumns + sprk_invoice/sprk_trackingnumber; Services/Signals in ScannedFolders; corrected reasons; drop AccessTableAppOnlyWriterTests:658 pin), F3 (delete sprk_invoicesubmissions false-match), K1 (WriteRollupAsync private/pinned), K2 (refusal above invoice guard in DataverseUpdateHandler), K3 (comment FinanceAuthorizationFilter:518-521); PLUS F4 vendor-org option (b) agreed by ontology+uac: SignalReadService.DuplicatesShownAsync takes vendor org from the invoice's submission(s) read AS CALLER (zero/differing -> withheld; fault -> nothing), sprk_billingevent.sprk_invoice refused; separate commit for ontology's 2 adversarial passes. Part B = merge origin/master with uac's #2257 DataverseUpdateHandler checklist; #2277 CreatorPersonColumnSecuredGuardTests entries (Entity.xml IsSecured preferred; SecuredOnlyByDeployScript only for tables in Set-ChildRecordCreatorPersonSchema.ps1 $Tables; none for unstamped tables). Agent commits, does NOT push. Then: verify broad filter + Arch, push, send uac-r2 the F4/F3 diff + merge SHA, and ontology the (b) commit SHA.
- **F4(b) billingevent decided (ontology + uac agreed):** sprk_billingevent.sprk_invoice NOT refused (D-96 generic writer); read bounded by Signal's frozen sprk_corerecordid vs the line's invoice core record (fail closed); sprk_signal.sprk_regardinginvoice/-invoiceline join AccessColumns; residual DEF-043 #2294 (update the issue with agent A's answer on what a reader sees). uac re-check items: fail-closed must-fire per case, Signal core-record column refused, LineInvoiceCoreRecordMismatchCode on every generic path incl. field-mapping push.
- **090 round 2 ACCEPTED by uac-r2** (notes §13, aba94b930f pushed). K1: #2277 entries — agent A adds 081's four tables in Part B; sprk_ruleset + sprk_procedureassignment entries when 090 merges into work. K2 DEF-044 #2296 (fix before StoredFileReconcileJob is enabled), K3 DEF-045 #2297. Now that agent A is finishing: merge origin/work into the 090 branch again (agent A's commits), then merge 090 into work, build 5, broad filter, push.
- **Billing-admin role(s) are ebilling's to create** (uac: 191 creates only External Collaborator). Need an ebilling task: billing-admin role with org Read (O-6) + Create/Write on sprk_ruleset/sprk_policy/sprk_policyversion (+ procedure tables), and flip ProcedureRolePrivilegeGuardTests' UntilTask191. Checked: NO existing POML covers it. Create ONE task (task-create, after agent A finishes) for: billing-admin role + D-185 (Basic/Core prvReadsprk_ruleset → None; guard test; dev role change with owner's direct OK + notices). Also still to create: the #2235 fix task. Uncommitted in this worktree: current-task.md, tasks/089 POML (uac secure-child constraint) — commit with the next commit.
- **D-185 (owner decided #2285 via ontology; CONFIRM DIRECTLY with the owner before any live role change):** only billing roles read rule sets/rules in Dataverse. ebilling: set prvReadsprk_ruleset to None on Spaarke Basic User + Spaarke Core User (solution role XML + dev role change with owner OK + notices). Inventory: no caller-scoped BFF/client read of sprk_ruleset; all billing reads app-only; only effect = sprk_ruleset views/forms in Spaarke Administration MDA app. Ontology: Ontology Administrator does NOT get prvReadsprk_ruleset (keep change minimal: System Administrators now, 191's billing roles later); send ontology the role-XML PR when ready. Ontology removes policy Reads after their task 163. Told uac: 191's billing roles must grant Read on sprk_ruleset/sprk_policy/sprk_policyversion. No billing role exists yet (billing admins = System Administrators). Needs a new ebilling task (task-create).
- **Dev BFF** is live at master 1fa0cf5cb8 (R3, 2026-10-23 00:00Z); still lacks e0df773395 (015).
> **Format**: CURRENT state only. Rewrite each checkpoint; target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 090 (file store service) round 2 RUNNING as a background opus/high implementer in worktree `C:/code_files/spaarke/.claude/worktrees/agent-acd148d1d111e78ef`, branch task/ebill-090-file-store (was 12329abebe). Model FINAL (EB-D-148); draft PR #1911. |
| **Status** | work/ebilling-r1 pushed at e371dce6bf: master merged (51591469df, UAC #2253 in), post-merge fixes (3cf4141bc0..dae66dab5c), 081 merged (1b1eda6fec, ontology PASS + uac accepted). Tip: 5 test projects build; broad unit filter 14149/0 fail; ArchTests 1516/1516. 080, 081, 088, 091, 099, 119 ✅. |
| **Next Action** | (1) When uac-r2 reviews e371dce6bf (master-merge ParentLineage resolution, sprk_invoicesubmission AccessColumns, NotAccessTables reasons, the 188 test rewrite), fix anything they raise. (2) When the 090 agent reports, run the broad filter in its worktree output, send its range to uac-r2 (F4 + round-1 Ks + stamping), then merge 090 into work, build 5, broad filter, push. (3) Owner asks (below). (4) task-create for the #2235 fix (after 090). (5) Later: merge #1911 to master (check #2184 first; next master merge conflicts in DataverseUpdateHandler with #2257 a94b1d7f14), then tell customer-provisioning and R3. |

### 090 round 2 brief (the running agent)
Merge origin/work/ebilling-r1 into the 090 branch (no force push; Stored File registry row becomes **I-23**); F4: `StoredFileSchema.Entity` in AccessTables (reason names StoredFileService, "no playbook node, output mapping or field-mapping rule writes it"), Services/Finance/Files in ScannedFolders, sprk_procedureversion + sprk_billingexport in NotAccessTables, must-fire exact-set row; uac round-1 re-check: production-constructor OrganizationId test (fix-now), K1 ETag ifMatch on both deletes (412 = kept), K2 "F4 merged" hard precondition for enabling StoredFileReconcileJob (notes + 079 POML), K4 size-cast misses as known limit, IOptionsDriftTests TenantId wording; stamp creator on sprk_ruleset + sprk_procedureassignment (StampedChildTables + schema script gate + #2277 allow-list if present).

### Owner asks (open; the user has not answered)
- **081 dev schema run**: `scripts/Set-ChildRecordCreatorPersonSchema.ps1` dry run → `-Apply` → `-Verify` on dev, notices to ontology-r1 and uac-r2 before/after. HARD RULE: -Verify exits 0 in an environment before any BFF carrying ebilling (081+) deploys there (UAC #2273/#2277). After 090 merges, its two tables join the same run.
- **#2285 (DEF-042)**: should Billing policy and rule-set rows be hidden from internal users using Dataverse directly? ontology: Read on sprk_policy is intended (Console worklist outer link) and must stay; any fix is row scoping or a split.
- Business rules from ontology (raise with 082): does a Procedure correction apply retroactively to already-evaluated invoices? Clean slate for a re-added rule vs D-11 30-day suppression?
- Firm visibility settable on its own (081 agent's question).
- Owner's machine: Pester 6.2.0 `Pester.ScriptScope.ps1` overwritten 2026-10-16 with the sidecar script (reinstall Pester). Leftover conflict markers on master in projects/ai-advanced-capabilities-agreements-r1/notes/uat4-execution-notes.md (not ours).

### Decisions this session (2026-10-22, recorded in notes)
- Registry renumber: master I-19 (email-derived document link) stays; invoice-family I-20, submission firm link I-21, Procedures I-22, Stored File I-23.
- ParentLineage: 088 submission marker sits in master's narrowing check (sprk_document only, both WriteIdentity tiers, shared AppOnlyDocumentWriteReasonCode, own message). uac agreed.
- sprk_invoicesubmission AccessColumns: sprk_vendororg + three sprk_regarding* lookups (pending uac review).
- 188 r4b finance reject tests rewritten to "refused at step 0" on one StatedCaller helper (uac approved, conditions met).
- InvoiceSubmissionPersister keeps copying the submission's sprk_createdbyperson (safe via universal empty gate + -Verify-before-deploy).
- I-P2: Procedure tables are not access tables.
- 082 must-dos from ontology are in its POML (remove the Procedure skip in BillingFactLoader + 2 must-fire tests; nothing may set sprk_ruleset.sprk_procedure on an in-force set before 082 is live).

### Filed this session (open)
#2274 (DEF-039), #2280 (DEF-040), #2284 (DEF-041), #2285 (DEF-042); earlier #2237 (uac, merged), #2254 (DEF-037), #2255 (applied on dev).

### Peer state
- R3: ebilling OK given for dev BFF deploy at f5bf481185 and 1fa0cf5cb8 (master; no ebilling branch code). The owner approves in R3's session.
- uac-r2: has e371dce6bf for one-pass review. customer-provisioning: #2232 after #1911 merges (dedicated account per stamp).

### Standing rules (unchanged)
- Worktrees: agents in their own worktree; remove only with Remove-WorktreeSafe.ps1; never bare git stash; never --no-verify.
- After merges: build all 5 test projects and run the broad filter on the combined tip before pushing or sending ranges.
- Deploys: Import-SolutionScoped.ps1 only; dev BFF and SPA only via SPA-r3.
- Live writes and role changes need the owner's OK; schema runs need notices to ontology-r1 and uac-r2, before and after.
- Gates: no line upload route before #2047; 100 and 105 not to dev or master before the Core User check on dev; no dev invoice seeding until REAL-2026-123456.01 is answered; StoredFileReconcileJob not enabled anywhere before F4 is merged.
- Dataverse BigInt columns can't be field-secured (memory note).
