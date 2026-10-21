# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-21
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode, #1911 draft PR. **Active work: the new Procedure and file model** (owner 2026-10-20/21), which replaces the Procedure Library / D-145 / rule-set record-scope design (tasks 079–087). |
| **Step** | Design review loop. **v5.1** of `notes/procedure-and-file-model.md` is written: see the commit "procedure and file model v5.1 (ontology-r1 and uac-r2 re-reviews folded in)" on origin work/ebilling-r1 (v5 = 70fc2cc616). It folds in ontology-r1's v5 re-check (reviews §8), uac-r2's (reviews §9: accept with changes, given read-time (a′)), word-add-in-r1's three flagged-attachment gaps, and the owner's answers relayed in ontology D-175 (Type "Internal Standard" confirmed; **no automatic re-check** — D, D2 and a version end set the banner "A new procedure that applies to this invoice has been published" on affected open invoices; ebilling-r2 owns the explicit re-check). |
| **v5.2** | `notes/procedure-and-file-model.md` v5.2 (commit "procedure and file model v5.2 (read-time banner; uac-r2 N1-N9)"): banner computed at read time, stored nowhere; uac-r2 re-checks the diff. Owner items now also include the banner text (N8) and §15(b) widened to override removal (N7). |
| **v5.3** | `notes/procedure-and-file-model.md` v5.3 (`cbee545d41`): uac-r2 C1–C7 applied (snapshot states, app-only comparison, cost bound, banner on `/review` only, step 0 on Finance invoice reads and every Procedure/override read). uac-r2 re-checks C1–C5 on `b5872265be..cbee545d41`. |
| **Status** | Waiting on the owner's answers, listed in v5.1 §15: Submit Invoice process (I, L); (a) whether "no automatic re-check" covers every automatic re-check; (b) whether a Procedure retire still closes open findings through the D-170 PolicyRetired sweep or leaves them open with the banner; the F2-d gap reading (accepted by ontology); export files and "the related external user"; whether an Override can release a gap hold; uac-r2's three (F1-1 firm doesn't see its own invoice while the source is internal-only; F1-4 firm match through the linked contact's organisation; K-3 network Deny). Engineering: proof of the guard's version bump. |
| **Next Action** | (1) Owner answers §15 → patch v5.1 → the owner finalizes. (2) Write decision row **EB-D-148** (project-prefixed ids per ontology O5). (3) Re-plan: revised POMLs for 079–087 plus the new tasks in v5.1 §13 (task-create). ontology-r1 then does the full schema review of the 080 rewrite; uac-r2 registers the new tables in 191 and updates `core-record-delete-design.md`. **Do not start any 079–087 implementation or schema before the owner finalizes.** |

### Owner decisions on the new model (in the note §2)
O-1 to O-17. Main points:
- **Files and records:** Procedure + Procedure Version tables; our own Blob storage; invoices and exports become Stored Files, not Documents; Submit Invoice copies email attachments.
- **How rules apply:** record → Procedures (all records / invoice firm / manual) with Procedure- and rule-level overrides; version by work date; v2 copies v1's rules; no BU filter.
- **Other:** a late fee is an added fee (AddedFee type); exceptions are retired with their record; no dev migration; the UI is prototyped separately and the back end goes first.

### Open owner questions
- A–Q in `notes/procedure-model-reviews.md` §4/§5. **C, F, G and D (override removal) were answered by the owner in ontology-r1's session (ontology D-175, 2026-10-21); do not ask again.** D2 (ontology D-175 addendum), Q and Q2 (no migration; owner in SPA-r3's session) are answered too. **Owner answered A (option 1), E (hold + "No procedure associated with this invoice" warning), H ("Internal Standard"), N, P, M/O (as uac-r2), Q, Q2, R on 2026-10-21.** **B answered:** `sprk_billingfirm` (law firm → default Procedures) + `sprk_billingresponsible` (client billing entity, future), both → `sprk_organization`, on every core type. **Agreement:** a core record (stand-alone or under a matter/project), deferred to R2. Extracted fields stay readable after a later source change (owner). Only L (with I) open. **#2150 merged (a10e814d02):** D-140 thread hop → `RecordThreadFiling.ParentsOf`, plus removing the dead FinanceEndpoints catch, is in progress. Earlier: B was open (owner proposed `sprk_billingentity`, an organization lookup on each core record; we replied about the name, `sprk_billingfirm` vs client entity, and its relation to the assigned firms) and L (with I). Owner asked what "sprk_family" is: none exists (the v1 Family lookup was withdrawn for Procedure + Procedure Version; "secure family" is a uac concept); answered in reviews §4. New standing directive: "matter" = any core record (project CLAUDE.md §3). K1 race analysis is in reviews §7 and was sent to uac-r2. **Next: write v5.** Superseded detail follows: Previously open: A, B, E, L (with I), N, P. **M answered** (uac-r2 round 113, corrected): Procedure files are never served to `sprk_isexternal` callers; our choice is that the internal file route refuses every flagged-external caller, who gets invoice and export files only via the firm route (036 chain). **O answered: all or nothing.** A failed post-commit re-run undoes everything the submission created. We owe uac-r2 a race analysis (which races the in-transaction row guard catches, and any Signals sent outside); a code-mapper agent is running it. **K answered:** internal-only, Restricted and Private-thread sources are all allowed, and the file inherits the source's isolation (word-add-in-r1 bce36d0fc0). **H answered:** Type "Internal Operating Standard" (ontology D-175; the owner may still prefer "Internal Standard"). **J answered: A + B** (file the email first, and the invoice file follows its source through T7; word-add-in-r1 0b3810144e). H: the owner asked ontology why "guidance"; ontology relays the answer. **I–L (owner, relayed by word-add-in-r1):** Submit Invoice in the add-in = yes, with the process to discuss separately with the owner; internal-only sources are allowed (the isolation is carried on the Stored File; uac-r2 must review who reads the copy); J leans "file first", with more options to come.
- SPA-r3 asked which BFF routes we retire so the SPA drops the same. Proposed (final with D-148): the four `/external/invoices/{id}/documents…` routes in `ExternalProjectDataEndpoints.cs:378-412` (R3 task 031, uac-r2 187 F1) → replaced by the firm file routes (§6 of the reviews note). Submission routes stay; only their storage target changes. **Agreed with R3 (R3 decisions d5267adb9):** the BFF change (files routes + retire the four + Stored File storage) is ours, as a task in the re-plan, reviewed by uac-r2 with the 188 guard entry; the SPA change is R4 (or R3). Dev deploy is BFF then SPA back to back through R3. Until the SPA ships, the old SPA's invoice-documents call 404s. R3 was told the 051 wizard is on our branch only.
- Questions sent to SPA-r3 (2026-10-21): firm invoice-file route shape, the task 188 ArchTest, excluding the snapshot from the firm findings projection, what firms see while a scan is pending. Early views only.

### 050a (closed)
- Round 4 passed uac-r2 (38368ddbdc..83aacd9650). K4 accepted.
- K2 merge note: when #2150 merges, the D-140 thread hop switches to `RecordThreadFiling.ParentsOf`. uac-r2 will notify us.
- Under the new model, 050a's re-file is retired and Submit Invoice replaces it. Its D-140 chain and isolation check are kept.

### Other decisions this session
- D-140 to D-147 (decisions-log).
- D-147 = ontology D-174 (dev rate-card Firm list confirmed by 085 step 0). Moot under the new model.

### Peer state
- **uac-r2:** reviews v5. #2159 and #2161 still matter for us (strict pointer rule); #2151 is moot; T7 applies only to invoice-thread docs. Updating `core-record-delete-design.md` on their side.
- **ontology-r1:** scoped re-check of v5, then a full schema review of the 080 rewrite. Records the superseding decision row when D-148 is final. They may have asked the owner their [Owner] items directly; reuse those answers.
- **word-add-in-r1:** re-checks the revised §9.1.
- **R3:** dev BFF master a10e814d02 (live 2026-10-21 14:35Z); the 015 delete stays blocked; sprk_invoicestatus still exists on dev.
- **field-mapping:** the 012 recipe is OK with corrections (a)–(c). 013 reviewed 2026-10-21: no objection once F3 (seam-test assertions on Item/Caller, a throwing rule, cancellation) is fixed in 013. F1 (no invoice rule registered → 017 gate), F2 (500 split), F4 (the (c) test) and the arm-order pin go into the reconciliation commit. 014 must keep one `item`, set RelatedEntitySet and protect the family core lookups; 016/040 must call `CheckGenericUpdateAsync`. Round 2 (27bac15a42) re-checked, no objection; 013 + 001–006, 010–012 are in **PR #2184** to master (renames the Office create service; check it when merging #1911). They send the reconcile commit for review before 017. 014 done (706dad05a6; our three items met). Reconcile review checklist: the RegardingTrio tables (invoicesubmission, invoiceapproval, externalidentifier, invoicecorrection) get mapping-protected core lookups when they register; the ratchet copy is replaced by the real `CoreRecordLookups`.

### Standing rules (unchanged)
- **Worktrees and git:** agents work only in their own worktree; remove worktrees only with Remove-WorktreeSafe.ps1; never bare git stash; never --no-verify.
- **Verification:** run the broad filter after multi-range picks.
- **Deploys and imports:** Import-SolutionScoped.ps1 only; dev BFF/SPA deploys only via SPA-r3.
- **Live changes:** live writes and role changes need the owner's OK. Schema runs need notices to ontology-r1 and uac-r2 before and after.
- **Gates:** no line upload route before #2047; 100/105 not to dev or master before the Core User check on dev; no dev invoice seeding until REAL-2026-123456.01 is answered.
