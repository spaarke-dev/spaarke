# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-21
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode, #1911 draft PR. origin work/ebilling-r1 = b2df73488e plus this checkpoint. Tree clean. **Active work: the new Procedure and file model** (owner 2026-10-20/21), which replaces the Procedure Library / D-145 / rule-set record-scope design (tasks 079–087). |
| **Step** | Design review loop. **v4** (`notes/procedure-and-file-model.md`, c260f9de11) had the Fable review folded in. All three peer reviews of v4 are in and recorded in `notes/procedure-model-reviews.md` (b2df73488e): word-add-in-r1 (content), ontology-r1 (CHANGE; blocking 2b, 9a, O1), uac-r2 (6 F1, 3 F2, 3 F4, K1–K6). §4 of that file is the **consolidated owner decision list A–P, with recommendations**, given to the owner 2026-10-21. |
| **Status** | Waiting on the owner's answers to A–P. Then write **v5** and send it to all three peers for scoped re-checks: ontology-r1 asked for a scoped re-check; uac-r2 re-reviews; word-add-in-r1 gets the revised §9.1. Then the owner finalizes the approach and plan → decision row D-148 (project-prefixed ids per ontology O5) and revised POMLs for 079–087 plus new tasks. |
| **Next Action** | (1) On the owner's A–P answers: write v5 of `procedure-and-file-model.md`, folding in every finding in `procedure-model-reviews.md` §1–3 (all accepted) and the owner's choices. Key structural change if A = option 1: record exceptions become a **record-scoped `sprk_ruleset`** in the secure family; the exceptions Procedure and "Exceptions for" are dropped. (2) Commit, push, then send v5 to ontology-r1, uac-r2 and word-add-in-r1. (3) Fold in the re-check results; the owner finalizes; write D-148 and re-plan the tasks (task-create). **Do not start any 079–087 implementation or schema before the owner finalizes.** |

### Owner decisions on the new model (in the note §2)
O-1 to O-17. Main points:
- **Files and records:** Procedure + Procedure Version tables; our own Blob storage; invoices and exports become Stored Files, not Documents; Submit Invoice copies email attachments.
- **How rules apply:** record → Procedures (all records / invoice firm / manual) with Procedure- and rule-level overrides; version by work date; v2 copies v1's rules; no BU filter.
- **Other:** a late fee is an added fee (AddedFee type); exceptions are retired with their record; no dev migration; the UI is prototyped separately and the back end goes first.

### Open owner questions
- A–Q in `notes/procedure-model-reviews.md` §4/§5. **C, F, G and D (override removal) were answered by the owner in ontology-r1's session (ontology D-175, 2026-10-21); do not ask again.** D2 (ontology D-175 addendum), Q and Q2 (no migration; owner in SPA-r3's session) are answered too. Still open: A, B, E, K (Restricted, not-Open), L (with I), M–P. **H answered:** Type "Internal Operating Standard" (ontology D-175; the owner may still prefer "Internal Standard"). **J answered: A + B** (file the email first, and the invoice file follows its source through T7; word-add-in-r1 0b3810144e). H: the owner asked ontology why "guidance"; ontology relays the answer. **I–L (owner, relayed by word-add-in-r1):** Submit Invoice in the add-in = yes, with the process to discuss separately with the owner; internal-only sources are allowed (the isolation is carried on the Stored File; uac-r2 must review who reads the copy); J leans "file first", with more options to come.
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
- **R3:** dev BFF 4f5f22694; the 015 delete stays blocked; sprk_invoicestatus still exists on dev.
- **field-mapping:** the 012 recipe is OK with corrections (a)–(c). 013 reviewed 2026-10-21: no objection once F3 (seam-test assertions on Item/Caller, a throwing rule, cancellation) is fixed in 013. F1 (no invoice rule registered → 017 gate), F2 (500 split), F4 (the (c) test) and the arm-order pin go into the reconciliation commit. 014 must keep one `item`, set RelatedEntitySet and protect the family core lookups; 016/040 must call `CheckGenericUpdateAsync`. Round 2 (27bac15a42) re-checked, no objection; 013 + 001–006, 010–012 are in **PR #2184** to master (renames the Office create service; check it when merging #1911). They send the reconcile commit for review before 017. 014 done (706dad05a6; our three items met). Reconcile review checklist: the RegardingTrio tables (invoicesubmission, invoiceapproval, externalidentifier, invoicecorrection) get mapping-protected core lookups when they register; the ratchet copy is replaced by the real `CoreRecordLookups`.

### Standing rules (unchanged)
- **Worktrees and git:** agents work only in their own worktree; remove worktrees only with Remove-WorktreeSafe.ps1; never bare git stash; never --no-verify.
- **Verification:** run the broad filter after multi-range picks.
- **Deploys and imports:** Import-SolutionScoped.ps1 only; dev BFF/SPA deploys only via SPA-r3.
- **Live changes:** live writes and role changes need the owner's OK. Schema runs need notices to ontology-r1 and uac-r2 before and after.
- **Gates:** no line upload route before #2047; 100/105 not to dev or master before the Core User check on dev; no dev invoice seeding until REAL-2026-123456.01 is answered.
