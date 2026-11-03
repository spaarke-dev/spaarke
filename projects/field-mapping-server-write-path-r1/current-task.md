# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-03 (main session checkpoint). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–016, 017, 018, 037, 039, 038, 070, 071, 072, 073, 074. The work branch was merged to master as #2435 (6b0c1e2fed); later doc commits are on the work branch.
> **Live on dev:** BFF 5796774091 since 2026-11-03T13:29Z (R3; carries 039, #2514, #2519, #2520; 019 live A/B ran on 3740e1a105). 017 (b083d62183, #2486) first went live 2026-10-31T19:52:25Z (UAC-r2 deploy; gate PASS: 23 req, 0 5xx, 0 OWNERSHIP-REFUSED, the reconciler wrote nothing). TrackingFieldTrio 1.0.55. SpaarkeMaster 1.2.4.0 is published but not imported. Check that the BU→Matter index map is gone after the model1-prod upgrade or the next provisioning run (H6).

## Quick Recovery

| Field | Value |
|---|---|
| **Active** | **020 ✅** PR #2551 merged aef86b89f7 (2026-11-03T23:21Z); a lint-ratchet fix was added in the PR (94ce92d3c3). It is not on dev yet: the live Matter/Project route check needs a uac-r2 deploy and the owner's OK for test rows. **Task 042b: Opus implementer `fm-042b` on `task/fm-042b`**, from cfebe65f9b, stops after POML step 5. The main session then runs two adversarial passes and the UAC-r2 full pass. Session 5c is the only field-mapping session (the pre-crash pid 53128 was stopped 2026-11-03).
- **020 so far:**
  - the route creates sprk_matter and sprk_project;
  - the optional mapping source is passed as query params;
  - only matter and project infer a source;
  - a secure-parent project is created into isolation;
  - F-1: a Matter naming `sprk_issecure` is refused;
  - F-2: a JSON-null `sprk_issecure` / `sprk_accessinheritance` is refused on project and WA, through one service helper.

  Per fm-020 (not checked by me): 6618 passed / 0 failed / 7 skipped, ArchTests 1911/1911, publish +591 B, no CVE.
- **Reviews done:**
  - Opus adversarial review: 0 blocking, 11 K-class findings.
  - UAC-r2: "OK WITH NOTES, merge when CI green" (decisions.md 2026-11-03).
- **Round 3 (sent to fm-020):**
  - #1: Matter route refusal tests;
  - #5: an HTTP-binding test;
  - #6: comments in EntityCreationService.ts;
  - #7: lazy CallerRecordAccessProbe;
  - #8: normalise JsonBody keys in the helper;
  - UAC-r2 note 1: refusal text;
  - notes corrections #4, #10, #9, #11.

  Items #2 and #3 are written into POML 021/022 notes (uncommitted, see below).
- **041 ✅** PR #2541 merged 249809ce0b (2026-11-03T19:50Z). It is not on the dev BFF yet; that needs an R3 deploy.
- **005 `-Apply` done on dev 2026-11-03** (36 migrated, VERIFY PASS, idempotent).
- **019 ✅, 042a ✅.** |
| **Next Action** | 1. When fm-020 reports round 3:
   - read the fix diff, or run a scoped Sonnet re-check if it is large;
   - merge `origin/task/fm-020` into the work branch `--no-ff` (the work branch is level with master after #2541);
   - verify with `scripts/verify-suites.sh`, plus jest for the adapter;
   - push, then open the PR. Cite ADR-008 path A. Placement Justification: extends an existing route. Include the §11 answer for `mappingSource`. Release note: the route creates matter and project; a caller-named `sprk_issecure` on a matter create is now refused 403 `sdap.access.secure_flag_transition_only`. Then enable auto-merge. If Classify Tier 1 times out (#2350), rerun the failed jobs.
2. After the merge:
   - TASK-INDEX 020 ✅, POML completed, run the drift check;
   - the live check on dev: a Matter/Project create via the route (needs an R3 deploy with the owner's go).
3. Next tasks: 042b (sequential; full UAC-r2 review). Then W3 021 onward. Item #2 must be fixed in 021 before cutover.
4. Fix the stale wording in POMLs 020–034 as each task starts. |
| **Phase branches** | 2d-1 798936c47e (step 10 pre-create; new codes stamp_underivable 400 / stamp_unavailable 503; Office map rows added as generic 500; system principal still stamped post-create = known limit). 2d-2 e106b19d7c (task/contact/account Child + MappingExclusion; owner + row byte-identical vs today). 017a f758480f3c (ebilling OK, no findings). 017b 43b48f3249 (route, decision parity, R3-1, 037 K2 = copy after merge, #1608, documents, invoice-family protection from guard, sprk_servicerequest; NFR-07 table all same; 401 stays 401; traceId kept). 017c 9cc37b415a (chat refuses invoicesubmission/approval/correction; escalated 3 items, resolved 2026-10-30). R3-2 = K2 known limit. |
| **Open PRs** | #2387 (075, HOLD, ships with 040). |
| **Issues** | #2444, #2413, #2429 (provisioning; I review), #2469 (flaky ISS-005), #1948 (lint-staged uses the shared stash; 017b re-found it). |
| **Waiting on** | - fm-020 (round 3).
- Owner: the Family 1.0.56 look (the four #2513 items on dev) and the 076 narrower-user pass. **Done 2026-11-03:** 005 `-Apply` on dev (36 migrated, VERIFY PASS, idempotent); pid 53128 (the pre-crash session) stopped on the owner's "kill it".
- 041 live proof: dev BFF d763dc120e (carries #2541) is live since 2026-11-03T22:26:31Z. The post-deploy check passed on what existed (0 5xx; no index or child-records traffic; no rows since the deploy). The startup logger exception is pre-existing (#2549, DI-016). After the next real upload or create on dev, re-run the read-only checks (document `sprk_searchindexname` = spaarke-files-index; no `sprk_ai_search_index` on new rows). Send the results to **uac-r2** (it now runs dev deploys), and copy R3 (`spa-external-access-platform-f2`) only for its rollout record §17.
- Rows to delete later: see `notes/task-019-notes.md` (the check-C event is the owner's and stays).
- #2518 (ontology writer identity not provisioned): ontology's to own. |
| **Owner to do** | 076 live check (owner + narrower user). Leftover locked worktrees agent-aec6d06ca99a5a9f6 (075) and agent-a7ed21b81867c4e2e (039, merged; locked by its agent process): end of project, via Remove-WorktreeSafe after unlock. |

## How wave-2 agents are run (lessons)

- `isolation: worktree` cuts from **origin/master**. Every brief starts with `git switch -c task/fm-NNN <tip>`.
- Launch adversarial-reviewer passes from the main session (one Opus per change set; a scoped Sonnet re-check per fix
  round). Resume an implementer with SendMessage for its fix rounds.
- **After ANY master merge, run the full ArchTests AND the related suites.** Rebuild ArchTests first; a stale DLL gives
  false failures.
- **Never run the unfiltered `Sprk.Bff.Api.Tests`** in an agent (the host reaches ~8 GB). Use the filtered set in
  `projects/field-mapping-server-write-path-r1/scripts/verify-suites.sh <repo> <log-dir>`. Publish-size delta + CVE:
  `scripts/publish-delta.ps1 -wt <worktree> -MasterSha <sha> -BranchSha <sha>` (same folder).
- Call `Remove-WorktreeSafe.ps1` through PowerShell, and first `cd` every shell OUT of the worktree: a Bash shell whose cwd is the worktree holds its root, so git deletes every file, then fails "Permission denied" on the root (0 files left). Then `find <path> -depth -type d -empty -delete` + `git worktree prune`.
- CI: the required checks are `Router` + `Client lint (type-aware ratchet)`. Router reports only after the slow Tier 2
  unit shards finish (~25 min). The Tier 2 ADR job may be CANCELLED by its 5-min timeout after passing (#2224); read
  the log before merging.

## Open items for the owner

**Owner answers 2026-10-27:** group 3 (016 Repair) waits on UAC-r2's build review. #2137 approved → implementer on
`fix/fm-2137-timing-equalise` (review before merge). #2224 approved → PR #2328 (auto-merge). Leftover folders: clean
up at project end. 4a–4d: owner asked UAC-r2 for its recommendation (sent 2026-10-27; owner: no per-BU search except Secure Records). 5b drop HIPC DEV 2; 5c/5d done (read-only, see task-006 notes and item 6). Owner stated policy for 4b/4c: a mapping copy is not
user-scoped (not yet applied; it reverses 014's AppendTo/field-security checks on mapped values and needs UAC-r2).
Dev reads: 0 of 64 matters have an empty `sprk_issecure`; 40 active Copy rules (4 Manual Refresh, 16 One-time,
20 empty); documents point at several indexes (537 none, 76 knowledge-index-v2, 1 spaarke-file-index).

1. **016 Repair questions:** cross-BU re-own (skip for now); whether Repair re-owns roots; SYSTEM/INTEGRATION-owned
   unfiled rows.
2. ~~017 decisions~~: all decided 2026-10-22/23 (decisions.md).
3. **Live smoke of #2191 on dev** (Office Project-from-Matter quick-create, mapped values, a secured column stays empty).
4. **005 `-Apply` on dev**, then `-Verify`.
5. **014:** confirm the 403 for a mapped filing lookup that fails field security. **015:** index precedence notes.
6. ~~#2137~~ (PR #2331); ~~HIPC DEV 2~~ (owner: out of scope); **model1-prod** map check after the next managed release (checked 2026-10-27: still there, expected); ~~prod null `sprk_issecure`~~ (2026-10-27: prod has 0 matters/projects/WAs, flag defaults to No) before a
   prod #2191 deploy.
7. **#2224:** OK to raise the Tier 2 ADR job timeout.

## Cross-project obligations

- **UAC-r2:** reviews 018b and 017 on condition (b); holds #2190 and #2169; batches deploy candidates for R3.
- **ebilling-r1:** #1911 + reconcile before 017. Task 096 removes the Office Invoice leg. 016/040 call
  `CheckGenericUpdateAsync` once #1911 lands (016 has a `TODO(#1911)` seam).
- **R3:** deploys master to dev only on a batched candidate from UAC-r2, with the owner's go in R3's session.
- **ontology-r1:** 033 needs a design-note review first. 033 also carries the app-only metadata reader and the IsSecured
  skip; 052 is hard-blocked on 033.

## Next tasks

019 once 017 lands. 017 once unblocked. Then 033 (before 052), 040 (needs 005, 019), 050 (needs 019).
