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
| **Design** | `notes/procedure-and-file-model.md`, now **v5.9**: every owner question answered (2026-10-21). v5.5 recorded the owner's Submit Invoice process (I, L) and the `from-email` contract (word-add-in-r1 reviewed it; folded in). v5.6 applies uac-r2's CHANGES-NEEDED review of v5.5 (reviews §14). **uac-r2: ACCEPT on v5.8** (the whole model). **Next:** the owner finalizes; then EB-D-148 and the re-plan (task-create). All peer reviews are in `notes/procedure-model-reviews.md` §1–§14. |
| **Peer status** | ontology-r1: **PASS** on v5.1 (reviews §10); ready for the 080 schema review. word-add-in-r1: §9.1 matches; its three flagged-attachment gaps are folded in. uac-r2: accepts with changes each round; v5.3 → v5.4 is text only. |
| **Background** | **D-140 done:** `task/ebill-d140-parentsof` + uac-r2's arch fix (`task/ebill-d140-archfix`, af038b2bbd; uac-r2 ACCEPT) merged into work/ebilling-r1 as 13b310e1c4 and pushed. Combined tip: build 0 errors, ArchTests 1309/1309, broad filter 14256 passed / 2 failed under load (both pass alone, 77/77; unrelated; filed #2208). Agent worktrees `C:\code_files\spaarke\.claude\worktrees\agent-ac2cb36d489b624e8` and `agent-a63d1a942dbb89891` can be removed with Remove-WorktreeSafe.ps1. |
| **Next Action** | (1) Handle the v5.4 and D-140 agent completions as above. (2) Owner answers the open items below → patch → the owner finalizes. (3) Decision row **EB-D-148** (project-prefixed ids); re-plan 079–087 plus the new tasks in the model's §13 (task-create). ontology-r1 then does the full 080 schema review; uac-r2 registers the tables in 191. |

### Open owner questions (model §15)
None. All answered 2026-10-21 (reviews §4.x, §4.y; model v5.9). **Waiting for the owner to finalize**, then EB-D-148 and the re-plan.

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
