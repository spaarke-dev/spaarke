# Current Task State

> **Last Updated**: 2026-10-23 (checkpoint after the F1/F4/#2294 fix round, the task 120 live run, and the 120 merge into work)

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Branch** | work/ebilling-r1, draft PR #1911. The local tip has 3 fix commits (b2f9c28e61, 2fdffba5ef, 0bba46c1ba) plus the task 120 merge, plus bookkeeping. Push only after the broad filter passes. |
| **Running** | The broad filter on the combined tip (6 builds, unit, ArchTests, integration). |
| **Next Action** | (1) When the filter is green: commit the bookkeeping (120 ✅, DEF-043 text, this file), then push. (2) Send uac-r2 the range a9f07cb04b..tip (F1/F4/#2294 plus the 120 merge), and send ontology the same SHAs for pass 2. (3) Update the #2294 issue text: the vendor-org lookup was removed, number-only findings get the generic sentence, K1 is recorded. (4) Then task 122 (#2235), which is startable. |

## Status
- **Done this session:** 080, 081, 088, 090, 091, 099, 119, 120 ✅.
  - **090:** merged into work (91568010c4), accepted by uac-r2.
  - **120:** live on dev on 2026-10-23 with the owner's direct OK. Spaarke Billing Administrator (810926e0-a97c-44ea-9891-e80dadef73f0): Read at Global; Create, Write, Append and AppendTo at Basic on sprk_ruleset, sprk_Policy and sprk_PolicyVersion. Core User lost prvReadsprk_ruleset. -Verify exits 0. uac-r2's server gate is clean. The role is held by nobody; **the owner assigns it to users** and then does a browser check of the billing-admin routes.
- **The fix round under review** (uac-r2 re-check of F1/F4/#2294; ontology pass 2):
  - sprk_signal's regarding pair, subject lookups and core pair are refused to app-only generic writers.
  - Number-only Duplicates show the generic sentence on the worklist, /review and /blocked; the tracking-number and vendor-org lookup was removed.
  - line_same_invoice is judged by its own invoice inside the frozen core bound.
  - Ontology's F1(a) (invoice subject unbounded) is resolved by the generic-sentence rule.
- **Next master merge:**
  - take #2301 (uac F2: quoted-token matcher), then run `--filter CreatorPersonColumnSecuredGuardTests`;
  - take #2303 (ontology D-185 policy Reads, plus EventType_Ref on Core User): one adjacent-add conflict at Core User.xml:143, keep both;
  - expect conflicts in SignalReadService and EarlierInvoiceReadability (master changed both; our fix round rewrote the number lookup).

## Open owner asks
- Dev schema run of Set-ChildRecordCreatorPersonSchema.ps1 for 081 and 090 (dry run, then -Apply, then -Verify; notices to ontology-r1 and uac-r2 before and after). **HARD RULE:** -Verify exits 0 in an environment before any BFF carrying ebilling 081+ deploys there.
- 082 business rules: retroactive Procedure corrections; clean slate vs D-11 suppression. Whether firm visibility can be set on its own.
- Owner machine: reinstall Pester 6.2.0, whose ScriptScope file is broken. Leftover conflict markers on master in projects/ai-advanced-capabilities-agreements-r1/notes/uat4-execution-notes.md (not ours).

## Tasks queued
- **122** (#2235: field-secure sprk_findingsjson, generic sentence in Decision Records). Startable. Its live step needs ontology review, notices and the owner's OK.
- **121** (Procedure grants). Blocked on uac-r2 191 plus their written agreement.
- **Filed:** DEF-043 #2294 (fix pending re-check), DEF-044 #2296 (fix before StoredFileReconcileJob is enabled), DEF-045 #2297.

## Peer state
- **R3:** ebilling OK given for the dev BFF at master 0317579272 (Signals and ontology only).
- **Unexplained:** live Basic User lost prvReadsprk_ruleset and prvReadsprk_Policy before task 120. Neither ebilling nor ontology did it; recorded in notes/120.

## Standing rules (unchanged; see project CLAUDE.md)
- No bare git stash, no --no-verify.
- Agents work in their own worktree; remove one only with Remove-WorktreeSafe.ps1.
- Live writes and role changes need the owner's OK; schema runs need notices.
- StoredFileReconcileJob is not enabled anywhere before F4 merges and DEF-044 is fixed.
- After merges: build 5 test projects and run the broad filter before pushing.
