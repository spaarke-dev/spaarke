# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-03 (main session checkpoint). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–016, 017, 018, 037, 039, 038, 070, 071, 072, 073, 074. The work branch was merged to master as #2435 (6b0c1e2fed); later doc commits are on the work branch.
> **Live on dev:** BFF 5796774091 since 2026-11-03T13:29Z (R3; carries 039, #2514, #2519, #2520; 019 live A/B ran on 3740e1a105). 017 (b083d62183, #2486) first went live 2026-10-31T19:52:25Z (UAC-r2 deploy; gate PASS: 23 req, 0 5xx, 0 OWNERSHIP-REFUSED, the reconciler wrote nothing). TrackingFieldTrio 1.0.55. SpaarkeMaster 1.2.4.0 is published but not imported. Check that the BU→Matter index map is gone after the model1-prod upgrade or the next provisioning run (H6).

## Quick Recovery

| Field | Value |
|---|---|
| **Active** | **Task 041 (FR-40, #2502), step 2–5 with the Opus implementer `fm-041` on branch `task/fm-041`** (cut from c0b543aedc). Rigor FULL, opus/high, prescriptive. Session 5c is the only field-mapping session; pid 53128 (pre-crash, no window) is still alive, and the owner can say "kill it".
- Step 0 done: master merged into the work branch (9935ef2dcf); no open PR touches 041's files.
- Step 1 done: the read-only dev count is in `notes/task-041-notes.md` (c0b543aedc). No BU names a non-files index, so there is no escalation. 77 old document stamps (knowledge-index-v2 ×76, file-index ×1) are left alone.
- AC9 rulings to fm-041: (1) ChatCreate + SearchIndexDefaultTests: only the applied-value, Warning→Information and BU-bind fragments change; (2) RelocatedFileIndexingTests Issue1510: only the `ResolveAsync` Verify line is deleted; (3) §14.4 row 10 (moving the constants) is skipped because AC9 pins SearchIndexDropGuardTests. All three are listed in the notes as "AC9 exceptions" / "Prescribed steps not done" for UAC-r2.
- **041 state:** rounds 1 and 2 are merged and pushed (f6124e8fab, then fde1b0aa5b with the decisions entry). Verified: sln 0/0, ArchTests 1911/1911, affected suites green. The Opus review plus the scoped Sonnet re-check have no F-class finding open. UAC-r2's verdict is **PASS WITH NOTES** (decisions.md 2026-11-03). Round 3 (UAC-r2 NOTE 1, the document trigger-2 test) is merged; RecordRepair 66/66. Master was merged in (bdd152a521; client add-ins only). **PR #2541** (work branch → master) is open with auto-merge `--merge`, and it closes #2502, #2206 and #2413. **Do NOT push 020 onto the work branch until #2541 merges.** After the merge: mark DI-012 closed in defer-issues, TASK-INDEX 041 ✅, POML completed, run `scripts/check-task-status-drift.ps1`, and fast-forward the main checkout per merge-to-master A.6.
- **020 state:** `task/fm-020` e4ef0e5ad6 is pushed and NOT merged into the work branch. A clean merge was made and then backed out so the 041 PR stays 041-only; the local ref `hold/fm-020-merge` is now obsolete. fm-020 is on its F-1 fix round on task/fm-020 (it merges the work branch in first). When it reports: send UAC-r2 the F-1 diff, run one Opus adversarial review over all of 020, and merge into the work branch only after #2541 has merged. All ACs pass per fm-020. Filed DI-015 #2540 (ribbon-only tables). **F-1, to fix in 020's scope after 041 merges:** a Matter create through the route can write `sprk_issecure` (and maybe the access-inheritance column). The refusal at `SecureRootInheritance.PlanCreateAsync:1083` only covers Inherits tables. Live, only field security stops it. Plan: merge 020 after 041, resume fm-020 to refuse it the way Project does (in the service, ADR-008 path A), and check whether chat or Office Matter creates share the gap. Ping UAC-r2 before merge (refusal-code change). Then one Opus review, then the live check on dev (needs an R3 BFF deploy). Open for 022: the secure-toggle branching (see the task-020 notes).
- **019 ✅ (2026-11-03):** the negative check passed as the test user. Check C passed: the owner supplied the name and parent; event `6d1563bb` stays. The chat tool row was re-seeded. **042a ✅** (#2521). |
| **Next Action** | 1. When fm-041 reports: merge `task/fm-041` into the work branch `--no-ff`, verify the build and filtered tests, and run ONE adversarial-reviewer pass (opus). Fix every F-class finding.
2. Send the diff to UAC-r2 for its scoped check (AC17: only routing removed, no trim moved). Record its verdict line.
3. Open the PR (cite ADR-008 path A; release note: index-file ignores `searchIndexName`). After merge: close #2206, #2413, #2502 and DI-012 (AC15). TASK-INDEX ✅.
4. Then 042b (sequential: both edit `RecordCreationService*.cs`; UAC-r2 does a full review). 020 depends only on 019 and may start beside 041.
5. Then W3: 021–029 (wait on 041), plus 030–034. Task 043 (#2524, N:N link) goes through the scope path.
6. Fix the stale wording in POMLs 020–034 and 052, and the 028 test path, as each task starts. |
| **Phase branches** | 2d-1 798936c47e (step 10 pre-create; new codes stamp_underivable 400 / stamp_unavailable 503; Office map rows added as generic 500; system principal still stamped post-create = known limit). 2d-2 e106b19d7c (task/contact/account Child + MappingExclusion; owner + row byte-identical vs today). 017a f758480f3c (ebilling OK, no findings). 017b 43b48f3249 (route, decision parity, R3-1, 037 K2 = copy after merge, #1608, documents, invoice-family protection from guard, sprk_servicerequest; NFR-07 table all same; 401 stays 401; traceId kept). 017c 9cc37b415a (chat refuses invoicesubmission/approval/correction; escalated 3 items, resolved 2026-10-30). R3-2 = K2 known limit. |
| **Open PRs** | #2387 (075, HOLD, ships with 040). |
| **Issues** | #2444, #2413, #2429 (provisioning; I review), #2469 (flaky ISS-005), #1948 (lint-staged uses the shared stash; 017b re-found it). |
| **Waiting on** | - fm-041 (implementer).
- Owner: the Family 1.0.56 look, 005 `-Apply`, the 076 narrower-user pass, and "kill it" for pid 53128.
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
