# Current Task State

> **Last Updated**: 2026-10-27 (creator-person schema live on dev; EB-D-153..157; 089, 092, 098 started for the 095 path)

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Branch** | work/ebilling-r1, draft PR #1911. Tip 78f7c390ff (pushed). |
| **Running** | Three background agents, each in its own worktree, branching from 78f7c390ff: **089** (task/ebill-089-assignments), **092** (task/ebill-092-submission-files), **098** (task/ebill-098-external-marker). They stop before any live step and before any peer review; each reports its review-request text. |
| **Next Action** | As each agent reports: check its diff and tests, send uac-r2 its review request, then merge into work after review and run the build of the 5 test projects plus the broad filter. 089's live probes and its Set-SecureRecordOwnerRolePrivileges -Apply need the owner's OK (ask when 089 reports). Then 086 (after 089), then 094 (after 086, 092, 098), then 095. |

## Why the 095 path now
word-add-in-r1 is waiting for 095 (POST /api/finance/invoice-submissions/from-email). Chain: 089 (4d) → 086 (1d) → 094 (3-4d) → 095 (3-4d); 092 and 098 run alongside 089. Estimate sent to word-add-in-r1 on 2026-10-27: code-complete 2026-11-13 at the earliest, realistically about 2026-11-20; on the dev BFF after #1911 merges and SPA-r3 deploys. They were told they can build against model §9.1 now, and asked what exactly blocks them. Update them when 094 merges.

## Full unit suite note
Sprk.Bff.Api.Tests holds 34,908 tests; a full run under load gives WebApplicationFactory timeouts. The gate is ArchTests, integration and the broad filter (Finance, Billing, Signals, RuleSet, Invoice, Compliance, Rejection, Decision, AccessTable, ParentLineage, Ebilling, Procedure, SecureChild, Submission).

## Status
- **Done:** 080, 081, 088, 090, 091, 099, 119, 120, 122 ✅.
- **2026-10-27, EB-D-153:** Set-ChildRecordCreatorPersonSchema.ps1 live on dev. Six tables secured; -Verify exit 0. uac-r2 confirmed it independently and closed 090 K1. The HARD RULE is met on dev. Record: notes/creator-person-dev-run-2026-10-27.md.
- **Owner decisions 2026-10-27:** EB-D-154 (dev uses System Administrator; the Billing Admin role must carry the Procedure tables, which is task 121); EB-D-155 (corrections as new versions; open invoices flagged and the new version applied on re-check unless pinned); EB-D-156 (suppression history kept); EB-D-157 (firm visibility follows the type in R1).
- ontology filed DI-128 / #2336 (ontology-authored policies record no creator person; deferred). If ontology widens its writer profile to the policy tables, the script's -Verify check "no permission outside sprk_createdbyperson on budgetrevision/decisionrecord/signal" needs updating.

## Open owner asks
- 089 and 086 live steps on dev (refusal probes, role -Apply): ask when the code is ready.
- Open question raised with EB-D-155: in R1 an open invoice is flagged, but the new version is applied only by a re-check. R1 re-checks only on submission and on a 101 line correction; the dedicated re-check action is ebilling-r2 (model 8.2). Should it come into R1?
- Owner machine: reinstall Pester 6.2.0. Leftover conflict markers on master in projects/ai-advanced-capabilities-agreements-r1/notes/uat4-execution-notes.md (not ours).

## Tasks queued
- **121** (Procedure grants in Billing Admin, EB-D-154): blocked on uac-r2 191 and their written agreement.
- **Filed:** DEF-044 #2296 (fix before StoredFileReconcileJob is enabled), DEF-045 #2297, DEF-046 #2312.

## Peer state
- **uac-r2:** reviews still owed from us: 086, 089, 094, 095, 098, 117; and the 091 items (what-if, live Test-EbillingFileStoreRole.ps1, tag-write proof).
- **R3:** ebilling OK given for dev BFF 3ff2c04bf5. The census findings come from user placement in the root BU (uac §5.2); nothing for ebilling to do.

## Standing rules (unchanged; see project CLAUDE.md)
- No bare git stash, no --no-verify. Agents work in their own worktree; remove one only with Remove-WorktreeSafe.ps1.
- Live writes and role changes need the owner's OK; schema runs need notices.
- StoredFileReconcileJob is not enabled anywhere before F4 merges and DEF-044 is fixed.
- After merges: build the 5 test projects and run the broad filter before pushing.
