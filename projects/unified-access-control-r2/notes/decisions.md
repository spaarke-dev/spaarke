# Decisions — `unified-access-control-r2`

> **What this file is.** The decisions index for this project, plus every rule that has been superseded or withdrawn, with its date. The project `CLAUDE.md` §3 keeps only the one-line binding outcome of each decision still in force; the reasoning lives here or in the logs below.
> **Created 2026-10-07** by the CLAUDE.md restructure (procedure update, PRs #1335/#1336/#1354). The previous CLAUDE.md is archived verbatim at `handoff-history/CLAUDE-archive-2026-10-07.md`.

## Where the full decision record lives

Kept in place rather than moved: dozens of POMLs, notes and PRs link to them by path.

| Record | Holds |
|---|---|
| [`session27-owner-decisions-and-research.md`](session27-owner-decisions-and-research.md) | The numbered rounds, 2026-09-30 onward: owner rounds and main-session rounds under owner directives. The canonical log; new decisions are appended there as the next round. |
| [`design-register.md`](design-register.md) | Findings, decisions (D-xx), deferrals and prerequisites from design through 2026-09 (§A–I). |
| [`decisions/`](decisions/) | Long-form decision records: external grant expiry, Function impersonation (ADR-052/ADR-028 A5), SPE paging and revoke honesty, workload placement. |
| [`owner-decision-brief-2026-09-18.md`](owner-decision-brief-2026-09-18.md) | The 2026-09-18 owner brief (external licensed users, among others). |
| `task-NNN-*.md` | Each task's own decisions, escalations and known limits. |

## Index: where each standing decision in CLAUDE.md §3 comes from

| CLAUDE.md §3 item | Source |
|---|---|
| Continue autonomously; batch questions | session 27 working agreement (2026-10-01/03) |
| Main session decides the complete fix | round 15 (2026-10-03), as narrowed by rounds 56/59 |
| ADR delegation inside 036 | round 75 |
| "Save the email as documents" | 2026-10-06 owner terminology |
| D1/C9 access model; Created By = FOR, not OPEN | session 27 answers (C9), round 3 (D1) |
| Grant Access rules (C4; cap; contact-to-own-org; Assigned-To uncapped Collaborate) | session 27 answers (C4), round 2 (Q1, Q2, Q5), round 3 (A1), round 3b (A1), round 11 item 1 (ADR-034 A4) |
| Restricted vs Secure | round 2 item 3, round 67 |
| `sprk_isexternal` (blank = internal; Restricted refuses/removes; secure-only allows; guest script first) | rounds 67, 76, 78; task 114 V5 |
| Accepted business-unit read gap | round 77 |
| `sprk_issecure` locked; F3; filed-under-secure is secure; no unsecure cascade | round 2 item 2, round 3b (F3), round 6 |
| A secure record always has a reader (S5), except Restricted wins | round 3 (S5), round 76 |
| Team ownership; record-first; `sprk_createdbyperson`; G5 pattern | D-11 (2026-09-22), round 5, round 3b (G5), rounds 7, 9 |
| Access changes in minutes; ≤2 min child mirror | round 3 (R3/R4), round 11 item 2 |
| Two-root intersection; invoices follow matter; communications inherit; children show the parent's access permission | round 11 item 4, round 10 item 11, round 2 Q6, round 81 |
| No Access on internal users; author-Write enforcement; hidden in SPA; parent permissions control a filed child | round 2 Q4, round 3b (N2, N5), round 82 |
| A child's access follows its parent both ways, locked while it has a parent | round 84 (replaces round 6 item 4) |
| Notifications: Created By + Assigned To; no team fan-out | session 27 answers, round 2 item 9 |
| Broker-only SPE; standing BU writers; JIT Office edit | rounds 69, 70 |
| Locked item-id copy; share-link refusal; Modified by = BFF | round 72 |
| Upload binding for app-uploaded files | task 171 hotfix, PR #1353 (2026-10-07) |
| Reconciliation writes on dev; R4 | round 7 item 1, round 71, round 79 |
| Every route declares authorization; unused unpublished routes are deleted | round 9 item 3, round 14 item 2, round 10 item 1 |
| Finding bar | rounds 56, 59, 74; repo-wide in task-execute Step 9.5 |

## Superseded and withdrawn rules

Each entry: the old rule, what replaced it, and the date. Do not apply these.

- **"At most 2 fix rounds; escalate any F1 still open instead of starting round 3."**
  - WITHDRAWN 2026-10-06 (round 74; repo-wide PR #1336).
  - Review limits cut ceremony, never fixing. Every defect found is fixed in scope, or filed and reported.
- **"Never defer, never sideline; fix every finding in the lane"** (round 15, 2026-10-03).
  - REPLACED 2026-10-05 by round 56: fix real defects, record adversarial or rare fail-closed edges as known limits, guard against over-engineering.
  - Round 59 then clarified that required functionality is still built fully.
  - Round 56's "one more fix round per lane" is itself withdrawn by round 74.
- **"The external flag is irrelevant to sharing"** (2026-09-18 ruling, ISS-024).
  - REPLACED 2026-10-06 by round 67: the flag is load-bearing for Restricted, and blank is internal.
- **"A user flagged `sprk_isexternal` blank is refused" / "an unreadable flag is external"** (task 063's fail-closed reading).
  - REPLACED 2026-10-06 by round 67: blank = internal everywhere. Implemented by task 114.
- **"No user is ever granted an SPE container permission"** (2026-08-25, SECURE-DOCUMENTS-BUILD-PLAN §1).
  - REPLACED 2026-10-06 by rounds 69/70: standing writers on BU containers, and JIT writers on secure containers for Office edit only.
- **SPE writer-identity matching** (`.claude/patterns/.../spe-writer-identity-matching.md`).
  - SUPERSEDED 2026-10-06 by broker-only document bytes (task 171).
- **"The last reader can never be removed," applied to external-flagged users on Restricted records** (S5 as applied by task 114's first build).
  - SUPERSEDED 2026-10-06 by round 76 (Restricted wins).
- **"Integration branches merge with a merge commit and keep the branch."**
  - SUPERSEDED 2026-10-06: PRs squash-merge on Router green; delete the branch only with no check pending.
- **"`work/unified-access-control-r2` is ~314 commits behind master; never build or run code there."**
  - SUPERSEDED 2026-10-07: the work branch was merged with master.
  - Task and fix work still happens in fresh short-path worktrees from `origin/master`, because the work branch lags between merges.
- **"`gh pr checks` never lists Router; probe it by name via the check-runs API."**
  - SUPERSEDED 2026-10-07 (verified): `gh pr checks <N>` lists `Router` with its state.
  - Still gate on the full rollup with no check pending.
- **"The skill files reach this worktree on the next master merge."**
  - DONE 2026-10-07: the merge brought PRs #1335, #1336 and #1354.
- **"Master publish size measured 36.13 MB (2026-10-06)."** Removed as a copied number.
  - Re-measure master fresh for every BFF task (root `CLAUDE.md` §10 / `.claude/rules/bff-hygiene.md`).
- **NFR-07 hard gate** ("characterization suite exists BEFORE Phase 1 changes behaviour").
  - SATISFIED: Phase 0 built it.
  - The gate no longer constrains new work and was dropped from CLAUDE.md's gate list.
- **Line-numbered reuse pointers** (`DataverseWebApiService.RetrieveMultipleImpersonatedAsync:953-989`, `PlaybookSharingService.cs:302-350`). Line numbers removed as stale; the type and member names remain in CLAUDE.md §2.

## Background moved out of CLAUDE.md (still true, kept for its reasoning)

### The MDA PCF disclosure (2026-08-25)

CLAUDE.md fact 1 used to read "on the MDA, Dataverse enforces natively and we write no code". That is false for MDA-hosted PCFs that read through the BFF.
- **What happened:** a non-admin who was denied Read on all 442 documents by Dataverse saw a matter's full document list, and opened and downloaded the files. The path was `SemanticSearchControl` → `POST /api/ai/search` on an MDA form.
- **What it means:** native MDA forms, grids and views are Dataverse-enforced; embedded PCFs that read via the BFF are not.
- **Sources:** design §4.1 correction, and `task-046-secure-project-owner-role.md` §7b.

### Service request is core but not externally grantable (2026-09-09, task 028)

- A service request is **core**: nothing else confers access to it. But it is **never grantable to an external contact**. Internal workforce users submit service requests through the SPA, and a law firm must never reach one.
- `sprk_externalrecordaccess` carries lookups for project, matter, work assignment, invoice and organization, with deliberately no service request. `CallerPrincipal` composes exactly three externally grantable root sets.
- Service-request scoping is a different mechanism: the `service-requests` external module scopes by requester (`sprk_requestedby == caller`), shipped by SPA-external-access-platform-r2 #028.
- Do not "complete the fourth root". Source: `task-028-service-request-root.md`.

### Why CLAUDE.md has a "standing directives" layer (2026-10-06)

`current-task.md` grew from 5 KB to 483 KB because every checkpoint stacked a new block over the old ones. The repo procedure now rewrites it as current state only (context-handoff "State, not history"). Standing items moved to CLAUDE.md. The old journal is at `handoff-history/current-task-archive-2026-10-06.md`, and the conversion review is at `handoff-history/2026-10-06-conversion-review.md`.
- **"`ExternalAccessReconciliationJob` is report-only until the owner reviews a report"** (round 7).
  - SUPERSEDED on dev 2026-10-08 by round 79: writes enabled after a 0-change report. Other environments still need the explain-and-confirm step.
- **"Communications inherit the parent's access permission; a child carries no permission of its own"** (round 2 Q6, as written in `docs/data-model/sprk_communication.md`).
  - EXTENDED 2026-10-08 by round 81: To Do, Event, Communication and Document show the parent's value in their own column; a parentless child keeps its own value (recorded only).
- **"Parent unsecured → its secured work assignments and projects STAY secure; no unsecure cascade"** (round 6 item 4, 2026-10-02; task 158 constraint).
  - REPLACED 2026-10-08 by round 84: a child's access always follows its parent, both ways, and is locked while it has a parent.
