# Current Task State

> **Last Updated**: 2026-10-21 (context-handoff at 86%)
> **Format**: CURRENT state only. Rewrite each checkpoint; target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Wave W3-6 in progress (080, 091, 099, 115), plus D-176 follow-ups. Model FINAL (EB-D-148); 108 POMLs; draft PR #1911. |
| **Status** | Three owner decisions pending (below); 099 agent still running. |
| **Next Action** | On the owner's answers: (1) re-run 080 on dev: `pwsh -NoProfile -File scripts/ebilling/Deploy-EbillingProcedureSchema.ps1` from the worktree root (dev default URL), log to scratchpad, then send the after-notice and verify output to ontology-r1 and uac-r2; (2) tell the 115 agent the D-39 precedence key; (3) on option A, resume 091 for ebilling-file-store.bicep + custom role. When ontology-r1 PASSes the 115 re-check, merge task/ebill-115-ruletypes, then run its option script on dev. |

### Lanes
| Task | Branch / state | Waiting on |
|---|---|---|
| 080 Procedure schema | Merged on work/ebilling-r1 (934e41ec53 + K1 6232f7436e). **Dev is PARTIAL**: the five globals, sprk_procedure and sprk_procedureversion are complete; sprk_procedureassignment has only sprk_matter (Delete Cascade). Nothing is secured or published; 0 rows. The fix (core lookups RemoveLink, in-place repair of sprk_matter, order guard) has uac-r2 ACCEPT and ontology-r1 PASS. Pester 3.4: `Import-Module Pester -RequiredVersion 3.4.0` (85 pass). | **Owner OK to re-run** (the change from "Cascade" to "the planner" for assignment rows). |
| 091 storage | task/ebill-091-storage f8eee9be4b (account-scope grants removed, ai-chunks container grant, template test). Not merged. customer-provisioning OK to merge (customer.json conflict = recompile, never hand-merge). | **Owner: option A** (custom role = Contributor + blobs/tags/read, RG scope, per container). uac-r2 ACCEPT A with 4 conditions (in 091/090 notes). |
| 099 step 0 | Agent running (task/ebill-099-step0); last seen running the full unit suite. | Agent result, then uac-r2 review. |
| 115 rule types | task/ebill-115-ruletypes eb0b042e5a (ontology FINDINGS fixed). Option script -WhatIf ok (GET-only). | ontology-r1 re-check; **owner: D-39 precedence for AddedFee** (RuleType alone vs RuleType+feeKind; recommend +feeKind). |
| D-176 / #2118 | duplicateOf.invoiceId merged (f140542018). Ontology task 157 builds the SignalReadService check. New ebilling task 119 (/review read-time withholding) is gated on 157. | ontology 157 range for our review. |

### Owner answers 2026-10-21 (all as recommended) — in progress
1. 080 dev re-run: running (log: scratchpad 080-rerun.log); before-notices sent; after-notice must include the P9 secured-column list + permission rows (uac-r2 asked).
2. AddedFee D-39 key = RuleType + feeKind: agent eb-115-feekind on branch task/ebill-115-feekind; ontology-r1 reviews.
3. 091 option A: agent eb-091-roleA on task/ebill-091-storage (template only; I run the dev deploy after uac-r2 review).
4. O-14 prototype: spaarke-prototype started it (spaarke-prototype/projects/ebilling.r1); first cut to follow.
Also running: eb-099-fix (uac-r2 F4 1-4 + registration assertion + multipart-in-handler + namespace guard selector); review of ontology 157 (c7659c6fe1..9d8fc886d4).

### Filed this session
#2215 (dead AttachmentClassificationJobHandler), #2216 / DEF-032 (AddedFee forms wait for extraction).

### Owner decisions already given
- **2026-10-21 owner OKs (W3-6 live steps):** (1) dev schema runs of 080, 115 and 088, each after ontology-r1's review PASS of the final script and -WhatIf, with notices to ontology-r1 and uac-r2 before and after; (2) the dev Azure storage change of 091 (remove the account-scope grant, container-scoped grants), no Key Vault changes. (3) 084 UI: spaarke-prototype says the O-14 prototype is NOT started; it needs the owner to ask it (told the owner 2026-10-21). Its data-shape questions are in notes/084-prototype-questions.md, answered by 084 and 089. 096: word-add-in-r1 confirmed 2026-10-21 that no add-in Invoice task exists yet (their owner is asked to create it ahead of 095). Their release needs 118's component merged and 095 on dev, takes about a week, and production needs a manifest bump and an owner upload. 096 is one release later. 095's §9.1 shapes are frozen; any change goes to them first. (do not ask again)
- **Model:** A option 1 (record-scoped `sprk_ruleset`, `sprk_scope` Record 100000002 / Firm record 100000003); B `sprk_billingfirm` + `sprk_billingresponsible` (→ `sprk_organization`, all core types, not access-conferring); C; D/D2 superseded by no-auto-re-check; E hold + warning; F, G; H "Internal Standard"; I yes (process TBD); J A+B; K internal-only/Restricted/Private allowed with isolation carried; M; N; O all-or-nothing; P; Q, Q2 (no migration), R.
- **Access:** uac-r2 round 116 (F1-1 (a′) effect accepted, F1-4 confirmed); round 117 (K-3 a known limit).
- **Other:** extracted fields stay readable after a later source change. Agreement is a core record, deferred to R2. "Matter" is shorthand for any core record (project CLAUDE.md §3). `sprk_family` does not exist (answered in reviews §4).

### Agreed with peers
- **R3:** we own the BFF change (firm file routes, retire the four `/external/invoices/{id}/documents…` routes, Stored File storage), reviewed by uac-r2 with the 188 guard entry. The SPA side goes to R4. Dev deploy is BFF then SPA through R3. The 051 wizard is on our branch only.
- **field-mapping:** 013 is cleared (PR #2184, which renames the Office create service; check it at the #1911 merge). Reconcile review checklist: RegardingTrio tables get mapping-protected core lookups; the ratchet copy is replaced by the real `CoreRecordLookups`; 016/040 call `CheckGenericUpdateAsync`.
- **Dev BFF:** master 583f90c1d9 (live 2026-10-21 18:04Z, via R3; ebilling OK'd R3 deploying f1b3dadcf5 = +uac-r2 188 and #2186, SHA not yet confirmed); includes #2177, so 095's "#2177 merged and deployed" gate is met on dev. The 015 delete stays blocked; `sprk_invoicestatus` still exists on dev.

### 050a (closed)
Round 4 passed. Under the new model the re-file is retired and Submit Invoice replaces it; the D-140 chain and isolation check are kept.

### Standing rules (unchanged)
- **Worktrees and git:** agents work only in their own worktree; remove worktrees only with Remove-WorktreeSafe.ps1; never bare git stash; never --no-verify.
- **Verification:** run the broad filter after multi-range picks.
- **Deploys:** Import-SolutionScoped.ps1 only; dev BFF/SPA deploys only via SPA-r3.
- **Live changes:** live writes and role changes need the owner's OK. Schema runs need notices to ontology-r1 and uac-r2 before and after.
- **Gates:** no line upload route before #2047; 100/105 not to dev or master before the Core User check on dev; no dev invoice seeding until REAL-2026-123456.01 is answered.
