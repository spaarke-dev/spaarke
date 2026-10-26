# Current Task State

> **Last Updated**: 2026-10-26 (after the master 58ffc37da3 merge, the task 122 live run and merge, and owner decisions EB-D-149..152)

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Branch** | work/ebilling-r1, draft PR #1911. Tip: bed558cb33 (master merge) + the task 122 merge + bookkeeping. Push only after the background build/test run is green. |
| **Running** | Background: build of the BFF and 5 test projects, ArchTests, the unit broad filter (Finance, Billing, Signals, RuleSet, Invoice, Compliance, Rejection, Decision, AccessTable, ParentLineage, Ebilling, Procedure, SecureChild, Submission) and integration, on the tip after the 122 merge. |
| **Next Action** | When green: commit the bookkeeping (TASK-INDEX 122 ✅, DEF-029 and DEF-035 closed, decisions-log EB-D-149..152, this file), push, send ontology the merge SHA (bed558cb33) for its scoped EarlierInvoiceReadability check (F1), then close GitHub #2235 and #2144 with the run evidence. Then: optional RuleSetRolePrivilegeGuardTests controls (Billing Admin exact set: a wrong depth, an extra privilege; Violations skips Billing Admin). |

## Full unit suite note
After the master merge, Sprk.Bff.Api.Tests holds 34,908 tests (it was 11,408). One full run took 86 minutes and ended with 225 WebApplicationFactory tests timing out (3-6 min each) under machine load, all in areas ebilling doesn't touch. Those classes run alone pass (99/0). Use the broad filter plus ArchTests and integration as the gate; run the full suite only when the machine is quiet.

## Status
- **Done:** 080, 081, 088, 090, 091, 099, 119, 120, 122 ✅.
  - **122 (2026-10-26, EB-D-149):** live on dev. The submission form no longer shows sprk_findingsjson; the column is field-secured to Spaarke BFF-Managed Field Writers (read, create, update), with no reader profile. The second run wrote nothing, -Verify exits 0, and -Revert is available. Notices went to ontology and uac.
- **Owner decisions 2026-10-26:** EB-D-150 (Billing Admin keeps Global Read on policy and policy version; accepted census exception in uac #2322, valid for a DIRECT user assignment only), EB-D-151 (D-90 stands for Core User; known limit recorded), EB-D-152 (#2310 accepted as is, closed; ontology records DI-126 only after the owner confirms it in their session).
- **Master merge (bed558cb33):** Core User.xml keeps both lines; EarlierInvoiceReadability takes master's doc comment; the CS0105 duplicate using was dropped. #2301 and #2303 are now in.
- **Basic User policy Read:** removed on the branch (b1fe73bd13), matching master accfc2d2f7 (D-185).

## Open owner asks
- Dev schema run of Set-ChildRecordCreatorPersonSchema.ps1 for 081 and 090: dry run, -Apply, -Verify, with notices to ontology-r1 and uac-r2 before and after. **HARD RULE:** -Verify exits 0 in an environment before any BFF carrying ebilling 081+ deploys there.
- 082 business rules: retroactive Procedure corrections; clean slate vs D-11 suppression; whether firm visibility can be set on its own.
- Assign Spaarke Billing Administrator to users (directly, not through a team), then do a browser check of the billing-admin routes.
- Owner machine: reinstall Pester 6.2.0. Leftover conflict markers on master in projects/ai-advanced-capabilities-agreements-r1/notes/uat4-execution-notes.md (not ours).

## Tasks queued
- **121** (Procedure grants): blocked on uac-r2 191 plus their written agreement.
- **Filed:** DEF-044 #2296 (fix before StoredFileReconcileJob is enabled), DEF-045 #2297, DEF-046 #2312 (forms script full run crashes on the dev sitemap; use -FormsOnly).

## Peer state
- **R3:** ebilling OK given for dev BFF 58ffc37da3, bc99fc3e6b and f183dd9c92 (uac census and proof code; no finance writes). The census reports human reach to the secure BU on sprk_invoice, event and memo. uac says the cause is user placement in the root BU and the fix is the decided BU layout (§5.2); nothing for ebilling. Expect the same finding on our four family tables after #1911 merges.
- **Unexplained (recorded in notes/120):** live Basic User lost its ruleset and Policy Reads before task 120. Ebilling never calls ReplacePrivilegesRole and never did a real solution import (answered to R3).

## Standing rules (unchanged; see project CLAUDE.md)
- No bare git stash, no --no-verify.
- Agents work in their own worktree; remove one only with Remove-WorktreeSafe.ps1.
- Live writes and role changes need the owner's OK; schema runs need notices.
- StoredFileReconcileJob is not enabled anywhere before F4 merges and DEF-044 is fixed.
- After merges: build the 5 test projects and run the broad filter before pushing.
