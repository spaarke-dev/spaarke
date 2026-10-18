# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-10-17
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file; never prepend new blocks above old ones. Target ≤ 10 KB.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Parallel mode. 42/79 complete (026 newly). Agents running from base 7c0daf8c9+: 075 (finishing mutation proofs), 015 fix round, 051 fix round, 076 fix round. Queued for the next free slots: 071 fix round; 026 follow-up for #2081 (invoice family) |
| **Step** | All on the local branch, not pushed: 015, 051, 071, 076 and 074 (cd44dbcca..0bc6a7ad9). ADR-010 ceiling 166; route ledger 137. Build 0/0; ArchTests 1160; targeted 2349; SPA vitest 1044; shared jest 38 |
| **Status** | in-progress (parallel mode) |
| **Next Action** | As each agent reports: review, cherry-pick (trailer rewrite), build and targeted tests, then send the fix range to the reviewer. 015 fix → ontology-r1 and uac-r2. 051 fix → uac-r2 and R3 (DEF-004 answers). 076 fix → adversarial pass 2 (auth). 074 → waiting on ontology-r1's adversarial pass. Mark 015 / 051 / 071 / 074 / 076 ✅ after their reviews clear. Next launches: 071 fix round (brief below); #2081 follow-up; then 072 (needs 071 ✅) and 077 (needs 076 ✅) |

### Queued briefs (not yet launched)
- **071 fix round.** Findings are from the adversarial review:
  - F1 credits per D-105: ApprovedRate and ExpenseRule skip negatives; InvoiceAge and CoreRecordWindow skip credit lines; NonBillableActivity never suggests disallowing a negative.
  - F3: compare the sub-cent rate as `BillingMath.ToCent(billed) > limit`.
  - F4: ApprovedRate adds Hours to its read fields only when Hours > 0, matching the notes.
  - F4: the notes cite stale hashes; the real range is 6b99e980f..d22c8e26d.
  - K4: the purity guard adds Guid.CreateVersion7, CultureInfo.CurrentCulture/CurrentUICulture and TimeZoneInfo.Local.
  - K2: list the DailyHoursCap double-count limit in the notes.
  - The D-89 enforcement belongs to 073 (constraint added).
- **#2081 follow-up (uac-r2 split it).** For sprk_invoice and sprk_billingevent targets, the guard reads the stored row and refuses not-found (an upsert must not create an orphan). Also:
  - DecisionRouteCores.cs:97 passes appOnlyEntities;
  - an end-to-end D-96 test through the three generic paths;
  - a test for UpdateRecordActionCore with no IGenericEntityService;
  - a code-safe hint for detach → move → re-attach;
  - ActionSeam.cs:163 prints the code twice.

### Critical Context
- **Decisions this session:**
  - D-96 to D-102 (see project CLAUDE.md §3).
  - D-103 supersedes D-98 (= ontology D-166): counted spend is Approved, Sent to AP, Paid.
  - D-104: Pending Spend (In Review, held excluded) is built in 049 on both paths. ontology-r1 adds the authoring entries once the column name and type are sent.
  - D-105: charge rules skip credit lines.
  - D-106: the firm must be linked to the core record.
  - D-107: firms see the statuscode labels.
  - D-108: billing admin works at any depth.
- **015 deploy sequence** (R3 deploys dev's BFF and SPA):
  1. R3 deploys candidate 2bda5ebe1 (admits both columns; SPA reads statuscode), BFF then SPA, with R3's owner approval.
  2. R3 confirms it is live.
  3. Run -GridConfig, WhatIf first.
  4. 015 reaches master and R3 merges it.
  5. R3 deploys a BFF containing 015.
  6. Run -Delete.
  7. Remove the transitional allow-list entries.
  After the delete, every dev BFF must contain 015, checked by content (R3 agreed).
- **ontology-r1:**
  - no seeding of dev invoices until their owner answers about REAL-2026-123456.01;
  - 049 (D-102 test and D-104) goes to them for review.
- **uac-r2:**
  - the ExternalWrites exemption rows go in whichever PR merges second;
  - the external invoice-submission POST is not wired to #2078's helper yet.
- Before the PR: merge current master, then run git merge-tree against #2078 and #2061, taking the union. Conflicts are expected in ExternalUserContextResponse, bff-client.ts, the ledger, ExternalRecordDocumentRoutesTests and config/secure-record-owner-role.json.
- Filed #2077: readers of columns that don't exist (outside our scope).
- **Standing:**
  - no line upload or storage route before #2047;
  - 026 must not touch Roles/Spaarke Ontology Service.xml;
  - remove worktrees with Remove-WorktreeSafe.ps1;
  - agents run filtered test sets only.
- **Owner actions:**
  - add the 7 new tables to the Matter Management and Spaarke Administration apps;
  - grant Write on sprk_ruleset to the billing admin role before 079.
- **Open:** G-18 (shared lint-staged stash, not yet filed); DEF-015; ISS-009; #2010, #2011, #2045.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 075 (implementer); 015, 051, 076 (fix rounds); 074 (external review) |

## Next Action

See Quick Recovery.
