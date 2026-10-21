# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-21 (checkpoint at context 85%)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | #1911 draft PR. **The Procedure and file model is FINAL (EB-D-148, owner 2026-10-21)** and the tasks are **re-planned** (brief `notes/replan-eb-d-148.md`): 079–086 rewritten, 087 cancelled, 088–099, 113–117, 189 added, 150 amended; 106 POMLs, validator and drift check clean. Start with wave W3-6 (080, 091, 099, 115) once its gates are met (TASK-INDEX Owner gates). |
| **Design** | `notes/procedure-and-file-model.md`, now **v5.9**: every owner question answered (2026-10-21). v5.5 recorded the owner's Submit Invoice process (I, L) and the `from-email` contract (word-add-in-r1 reviewed it; folded in). v5.6 applies uac-r2's CHANGES-NEEDED review of v5.5 (reviews §14). **uac-r2: ACCEPT on v5.8** (the whole model). **FINAL (EB-D-148).** **Next:** re-plan 079–087 plus the new tasks from model §13 (task-create). All peer reviews are in `notes/procedure-model-reviews.md` §1–§14. |
| **Peer status** | ontology-r1: **PASS** on v5.1 (reviews §10); ready for the 080 schema review. word-add-in-r1: §9.1 matches; its three flagged-attachment gaps are folded in. uac-r2: accepts with changes each round; v5.3 → v5.4 is text only. |
| **Background** | **D-140 done:** `task/ebill-d140-parentsof` + uac-r2's arch fix (`task/ebill-d140-archfix`, af038b2bbd; uac-r2 ACCEPT) merged into work/ebilling-r1 as 13b310e1c4 and pushed. Combined tip: build 0 errors, ArchTests 1309/1309, broad filter 14256 passed / 2 failed under load (both pass alone, 77/77; unrelated; filed #2208). Agent worktrees `C:\code_files\spaarke\.claude\worktrees\agent-ac2cb36d489b624e8` and `agent-a63d1a942dbb89891` can be removed with Remove-WorktreeSafe.ps1. |
| **Next Action** | uac-r2 task 188 (#2170, then #2186) awaits owner sign-off; it blocks 093/098. (1) Owner answers the five re-plan questions below. (2) W3-6: 099 (code-only, uac-r2 review) can start now; 080 / 115 need ontology-r1's schema review and notices first; 091 needs the owner's OK and the storage-role answer. (3) Peers: ontology-r1 full 080/088 schema review; uac-r2 registers the new tables in 191 and reviews per the gates. |

### Open owner questions (raised by the re-plan POMLs' escalation triggers)
None. All five answered 2026-10-21 (model §15 tail): Q1 email UI = new task 118 (toolbar Submit Invoice, flagged attachments via profile `sprk_documenttype`, Email Review Invoice tab); Q2 inactive invoices 409 (097); Q3 step 0 on every `/api/finance` route (099); Q4 Override releases any hold (114); Q5 container-scoped storage in customer bicep (091).
- **118 marking component:** agreed with word-add-in-r1 2026-10-21. 118 builds `InvoiceAttachmentMarking` in Spaarke.Communication.Components (file subpath export, pure validate/default functions, no submit callback); word-add-in-r1 reviews its PR and the add-in adopts it after 095. Non-invoice attachments default to 'supporting' (owner 2026-10-21); attachments with a disabledReason default to 'skip'. word-add-in-r1 creates its add-in Invoice task once 095 is built.

### Live dev state (080) — PARTIAL, 2026-10-21
The first 080 dev run (script on work/ebilling-r1 4ca826aef8) stopped at sprk_procedureassignment.sprk_project. Dataverse allows only one parental relationship per table, and Delete = Cascade counts as parental (0x80047007). Created on dev: the five globals, sprk_procedure and sprk_procedureversion (complete), and sprk_procedureassignment with only sprk_matter (Cascade). Nothing is secured or published yet, and there are 0 rows. Proposed fix, sent to uac-r2 and ontology-r1: the three core lookups become RemoveLink (the delete planner plus the 117 sweep remove the rows; 082 ignores orphans), and sprk_excludedpolicy stays the one Cascade. The script PUTs the existing sprk_matter relationship to RemoveLink, then re-runs. Log: scratchpad 080-run1.log.

### Owner decisions already given
- **2026-10-21 owner OKs (W3-6 live steps):** (1) dev schema runs of 080, 115 and 088, each after ontology-r1's review PASS of the final script and -WhatIf, with notices to ontology-r1 and uac-r2 before and after; (2) the dev Azure storage change of 091 (remove the account-scope grant, container-scoped grants), no Key Vault changes. (3) 084 UI: spaarke-prototype says the O-14 prototype is NOT started; it needs the owner to ask it (told the owner 2026-10-21). Its data-shape questions are in notes/084-prototype-questions.md, answered by 084 and 089. 096: word-add-in-r1 confirmed 2026-10-21 that no add-in Invoice task exists yet (their owner is asked to create it ahead of 095). Their release needs 118's component merged and 095 on dev, takes about a week, and production needs a manifest bump and an owner upload. 096 is one release later. 095's §9.1 shapes are frozen; any change goes to them first. (do not ask again)
- **Model:** A option 1 (record-scoped `sprk_ruleset`, `sprk_scope` Record 100000002 / Firm record 100000003); B `sprk_billingfirm` + `sprk_billingresponsible` (→ `sprk_organization`, all core types, not access-conferring); C; D/D2 superseded by no-auto-re-check; E hold + warning; F, G; H "Internal Standard"; I yes (process TBD); J A+B; K internal-only/Restricted/Private allowed with isolation carried; M; N; O all-or-nothing; P; Q, Q2 (no migration), R.
- **Access:** uac-r2 round 116 (F1-1 (a′) effect accepted, F1-4 confirmed); round 117 (K-3 a known limit).
- **Other:** extracted fields stay readable after a later source change. Agreement is a core record, deferred to R2. "Matter" is shorthand for any core record (project CLAUDE.md §3). `sprk_family` does not exist (answered in reviews §4).

### Agreed with peers
- **R3:** we own the BFF change (firm file routes, retire the four `/external/invoices/{id}/documents…` routes, Stored File storage), reviewed by uac-r2 with the 188 guard entry. The SPA side goes to R4. Dev deploy is BFF then SPA through R3. The 051 wizard is on our branch only.
- **field-mapping:** 013 is cleared (PR #2184, which renames the Office create service; check it at the #1911 merge). Reconcile review checklist: RegardingTrio tables get mapping-protected core lookups; the ratchet copy is replaced by the real `CoreRecordLookups`; 016/040 call `CheckGenericUpdateAsync`.
- **Dev BFF:** master 583f90c1d9 (live 2026-10-21 18:04Z, via R3); includes #2177, so 095's "#2177 merged and deployed" gate is met on dev. The 015 delete stays blocked; `sprk_invoicestatus` still exists on dev.

### 050a (closed)
Round 4 passed. Under the new model the re-file is retired and Submit Invoice replaces it; the D-140 chain and isolation check are kept.

### Standing rules (unchanged)
- **Worktrees and git:** agents work only in their own worktree; remove worktrees only with Remove-WorktreeSafe.ps1; never bare git stash; never --no-verify.
- **Verification:** run the broad filter after multi-range picks.
- **Deploys:** Import-SolutionScoped.ps1 only; dev BFF/SPA deploys only via SPA-r3.
- **Live changes:** live writes and role changes need the owner's OK. Schema runs need notices to ontology-r1 and uac-r2 before and after.
- **Gates:** no line upload route before #2047; 100/105 not to dev or master before the Core User check on dev; no dev invoice seeding until REAL-2026-123456.01 is answered.
