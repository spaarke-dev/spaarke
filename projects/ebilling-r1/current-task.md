# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-21 (checkpoint at context 85%)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | #1911 draft PR. **Active work: the new Procedure and file model** (owner 2026-10-20/21). It replaces the Procedure Library / D-145 / rule-set record-scope design (tasks 079–087). **Do not start any 079–087 implementation or schema before the owner finalizes.** |
| **Design** | `notes/procedure-and-file-model.md`, now **v5.5** (owner I/L Submit Invoice process and the email-only save contract; v5.4 09b147ec9a; uac-r2's v5.3 re-check applied: step 0 defined once in model §5.3, "false or blank = internal", route-group flag in `FinanceAuthorizationFilter` over every `/api/finance` invoice/submission route incl. `/blocked`, plus `/rulesets/*`; reviews §13). uac-r2 **ACCEPTED** v5.4; its citation nit was applied (5f89605c1a). All peer reviews are in `notes/procedure-model-reviews.md` §1–§13. |
| **Peer status** | ontology-r1: **PASS** on v5.1 (reviews §10); ready for the 080 schema review. word-add-in-r1: §9.1 matches; its three flagged-attachment gaps are folded in. uac-r2: accepts with changes each round; v5.3 → v5.4 is text only. |
| **Background** | **D-140 switch:** `task/ebill-d140-parentsof` (bec420861d) plus the uac-r2 ArchTest fix on `task/ebill-d140-archfix` (tip **af038b2bbd**, pushed). That fix adds the reasoned exemption for the 3 external submit POSTs plus a systemuser 403 refusal in `InvoiceSubmissionService` (test plus mutant), and moves the shared submit HTTP members to `Api/Finance/InvoiceSubmissionHttp.cs` (036 C). Gates: ArchTests 1309/0; filtered unit 12431 passed, 5 skipped, 0 failed. **Sent to uac-r2 for a scoped check of bec420861d..af038b2bbd.** After its pass: merge archfix into work/ebilling-r1, run the broad filter on the combined tip, push. Publish size before the fix: master 131,450,308 B, tip 133,675,200 B (+2.2 MB). |
| **Next Action** | (1) Handle the v5.4 and D-140 agent completions as above. (2) Owner answers the open items below → patch → the owner finalizes. (3) Decision row **EB-D-148** (project-prefixed ids); re-plan 079–087 plus the new tasks in the model's §13 (task-create). ontology-r1 then does the full 080 schema review; uac-r2 registers the tables in 191. |

### Open owner questions (model §15)
1. Does "no automatic re-check" (owner via ontology D-175) cover every case: override removal (D), Procedure retire (D2), version end? The model assumes yes.
2. Does switching a rule Off (Procedure retire, or override removal) still close open findings on In Review invoices through the D-170 PolicyRetired sweep, or leave them open with the banner? The model keeps the sweep until answered.
3. Can an Override release an invoice held because no Procedure version is in force?
4. Exports and "the related external user": did the owner mean firms see their own invoices, not the export file? The model keeps exports internal-only.
5. *(Answered 2026-10-21 via word-add-in-r1, recorded in reviews §4.x and model v5.5: explicit Invoice/Supporting/Don't save marks, separate add-in Invoice module, corrections as resubmissions, the .eml always saved without attachment children through one `from-email` endpoint, LEDES out of scope.)*
6. The F2-d gap reading (accepted by ontology-r1): draft-only is not Active; work before the first version is not applicable; hold only for a hole between versions or work after a final end date.
7. The banner text: the owner's "A new procedure that applies to this invoice has been published", or the neutral "The procedures that apply to this invoice have changed; re-check to apply them."

### Owner decisions already given (do not ask again)
- **Model:** A option 1 (record-scoped `sprk_ruleset`, `sprk_scope` Record 100000002 / Firm record 100000003); B `sprk_billingfirm` + `sprk_billingresponsible` (→ `sprk_organization`, all core types, not access-conferring); C; D/D2 superseded by no-auto-re-check; E hold + warning; F, G; H "Internal Standard"; I yes (process TBD); J A+B; K internal-only/Restricted/Private allowed with isolation carried; M; N; O all-or-nothing; P; Q, Q2 (no migration), R.
- **Access:** uac-r2 round 116 (F1-1 (a′) effect accepted, F1-4 confirmed); round 117 (K-3 a known limit).
- **Other:** extracted fields stay readable after a later source change. Agreement is a core record, deferred to R2. "Matter" is shorthand for any core record (project CLAUDE.md §3). `sprk_family` does not exist (answered in reviews §4).

### Agreed with peers
- **R3:** we own the BFF change (firm file routes, retire the four `/external/invoices/{id}/documents…` routes, Stored File storage), reviewed by uac-r2 with the 188 guard entry. The SPA side goes to R4. Dev deploy is BFF then SPA through R3. The 051 wizard is on our branch only.
- **field-mapping:** 013 is cleared (PR #2184, which renames the Office create service; check it at the #1911 merge). Reconcile review checklist: RegardingTrio tables get mapping-protected core lookups; the ratchet copy is replaced by the real `CoreRecordLookups`; 016/040 call `CheckGenericUpdateAsync`.
- **Dev BFF:** master a10e814d02 (live 2026-10-21 14:35Z). The 015 delete stays blocked; `sprk_invoicestatus` still exists on dev.

### 050a (closed)
Round 4 passed. Under the new model the re-file is retired and Submit Invoice replaces it; the D-140 chain and isolation check are kept.

### Standing rules (unchanged)
- **Worktrees and git:** agents work only in their own worktree; remove worktrees only with Remove-WorktreeSafe.ps1; never bare git stash; never --no-verify.
- **Verification:** run the broad filter after multi-range picks.
- **Deploys:** Import-SolutionScoped.ps1 only; dev BFF/SPA deploys only via SPA-r3.
- **Live changes:** live writes and role changes need the owner's OK. Schema runs need notices to ontology-r1 and uac-r2 before and after.
- **Gates:** no line upload route before #2047; 100/105 not to dev or master before the Core User check on dev; no dev invoice seeding until REAL-2026-123456.01 is answered.
