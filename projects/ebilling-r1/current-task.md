# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-20
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. #1911 draft PR. Origin work/ebilling-r1 = 83aacd9650 (050a round 4 pushed and sent to uac-r2 for scoped re-check: c0abbe44b6 F3 tests, 18bf358fdf F1 D-140 follows the communication's thread as a UNION matching SecureChildReconciler.ContextFor (mixed M/S case refused 409 both ways), 83aacd9650 notes; K4 accepted known limit, optional refile hardening backed out (precedence is a design call); gates: build 0, ArchTests 1,244/0, broad filter 16,921/0). Round 3 (bf44f1728e, 1af1877dda) confirmed by uac-r2. Human-decision Signal close is pushed (ontology PASS). D-142 applied on dev (sprk_ebillingoverride + Core User prvWrite Global). D-143 Return notifies firm (code done). |
| **Step** | OWNER PAUSE on the source-document / Procedure Library model. Owner asked for a detailed explanation of how procedure documents feed rule sets, rules, Signals, Decision Records and other uses BEFORE deciding storage (D-145 filing under the record vs. staying in the Procedure Library). Research done (code-mapper map, given to the owner in conversation). Owner's questions: (a) how rule sets / rules are associated to a matter/project/work assignment, (b) field or explicit association, (c) per-record add of a guideline / rate card applying to one or several records, (d) reuse of record-specific sets by other records; plus "a special internal procedures search index" (none exists or is planned). |
| **Status** | in-progress (owner discussion) |
| **Next Action** | (0) NEW MODEL (owner 2026-10-20/21): Procedure + Procedure Version + blob file store; invoices and exports as files; record → Procedures with Procedure- and rule-level overrides. Design = notes/procedure-and-file-model.md v4 (c260f9de11), Fable review folded in. Sent 2026-10-21 to ontology-r1, uac-r2, word-add-in-r1 for review; collect their findings, update the note, report to the owner, who finalizes approach + plan. Owner open: exceptions Procedure as secure child; default firm column (assignedlawfirm1/2 vs assignedoutsidecounsel). Items (1) and (3) below are SUPERSEDED by this model (kept for history until D-148). (1) Get owner decisions after the explanation: record-specific instructions process; storage (revise D-145?); marker (owner: Document Type; uac-r2: type is a label only, a move into a home needs a BFF-only relocation intent); procedures search index. (2) Await uac-r2's round-4 re-check of 38368ddbdc..83aacd9650; fix any F-class finding via an implementer in its own worktree, cherry-pick, gate, push. (3) After owner alignment: 080 fix round for ontology-r1 findings F4-1 (sprk_createdbyperson on 5 tables incl. sprk_policy/sprk_policyversion, D-173), F4-2 (field-secure sprk_allbusinessunits; stop BU-subgrid changes to Published sets), cheap list (writer-before-reader grant 12x10s retry, missing-lookup preflight exit 2, org isauditenabled, inactive cards, ruleset flag backfill Yes, publish before backfill, S8 non-null=0 verify, S8 AssociatedMenuBehavior DoNotDisplay, labels, PublishXml systemuser, 13 rows) → fresh -WhatIf → ontology scoped re-check → real run only on PASS. |

### Owner decisions this session (2026-10-20)
- D-139 050a 409s; D-140 confirm follows the email's matter; D-142 Core User holds the override privilege (applied on dev); D-143 Return notifies the firm; D-144 080 approved after ontology review; D-145 record-scoped source docs filed under the record (UNDER RE-REVIEW by the owner); D-146 core-record delete always keeps a scoped rule set / rate card, unlinked (supersedes D-141 Q10; uac-r2 applied 3fbf650fb); D-141 Q1 re-confirmed (BU snapshot at the Secure move). Owner: Procedure Library marker = Document Type (uac-r2: label only).

### Peer state
- **uac-r2:** to receive 050a round 3. Its own: #2150 (RecordOwnerUnsettledException → 409 record_owner_unsettled), #2159/#2161 re-file relocation + #2151 declared homes (Procedure Library / output config keys), T7 Restricted access, #2152. Holding D-145 and marker work until the owner answers.
- **ontology-r1:** 080 FINDINGS (above), D-173; reviews 081 and 082 when built.
- **R3:** dev BFF live 4f5f22694; keep the 015 delete blocked (e0df77339 not deployed); sprk_invoicestatus still exists on dev.
- **field-mapping:** 012 recipe OK with corrections (a)–(c), binding on their 013; they send 013 before merge.

### Standing rules (unchanged)
Agents work only in their own worktree; broad filter after multi-range picks; Remove-WorktreeSafe.ps1; Import-SolutionScoped.ps1 only; no line upload route before #2047; dev BFF/SPA deploy only via SPA-r3; never bare git stash or --no-verify; live writes and role changes need the owner's OK; schema runs need notices to ontology-r1 and uac-r2 before and after; 100/105 not to dev/master before the Core User check on dev; no dev invoice seeding until REAL-2026-123456.01 is answered.
