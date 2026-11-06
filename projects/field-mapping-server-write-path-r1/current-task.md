# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-05 (main session checkpoint). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–016, 017, 018, 037, 039, 038, 070, 071, 072, 073, 074. The work branch was merged to master as #2435 (6b0c1e2fed); later doc commits are on the work branch.
> **Live on dev:** BFF master 0f53e053bf (042c + 048) since 2026-11-05T21:39:06Z; UAC-r2 gate PASS 21:39-22:09Z (0 5xx, 0 OWNERSHIP-REFUSED, reconcile 0 writes; record `notes/deploy-gate-2026-11-05-0f53e053bf.md` on uac-r2's branch). No chat traffic in the window: fm's own live checks of the 042c/048 refusals need the owner's go in THIS session. TrackingFieldTrio 1.0.55. Check that the BU→Matter index map is gone after the model1-prod upgrade or the next provisioning run (H6).

## Quick Recovery

| Field | Value |
|---|---|
| **Active** | Checkpoint 2026-11-05 ~23:50Z. uac-r2: `spaarke-wt-unified-access-control-r2-20`; ontology: `spaarke-wt-spaarke-ontology-platform-r1-38`; ebilling: `spaarke-wt-ebilling-r1-c7`.
- **PR #2573** (043 + 046), head 20fa0a0c09, auto-merge on. UAC-r2's two 043 clears stand (+3d1ff16cc3 narrowing accepted). After merge: TASK-INDEX already ✅ for 043/046; uac-r2 takes the merge SHA to the owner as the next dev candidate (may wait for 049). fm live checks of 043/042c/048 need the owner's go in THIS session.
- **046b** (task/fm-046b, worktree agent-ae68466fd18cb8df3, tip 503b65063f): role files carry the rulings (G1, G2, G4-G8); `scripts/Set-FeatureRolePrivileges.ps1` + `config/feature-role-privileges.json`; dry run on dev = 8 changes (root + 5 BU copies), -Verify exit 1; ArchTests 1959/1959; 38 standalone add-on gaps + G9 sent to uac-r2 for the owner. **Opus pass 1 running** (agent on 046b); then pass 2 (security), fix rounds, merge into the work branch, uac-r2 runs -Apply. Holds 089e's G5 files byte-identical except ProcedureRolePrivilegeGuardTests (second to merge resolves by hand; ebilling told).
- **049** (NFR-02): fix round 1 running in two halves. **Part A** agent on task/fm-049b (worktree agent-ab6391bd0d308344f): maker = assignee inline via IIdentityNormalizationService (fault → queue), Office test, settle after failing write, isolated create waits for CompleteIsolatedCreateAsync, wizard secure-project premise check (uac-r2: if both premises hold, close; else fm files an issue, neutral wording), AND the delete-guard fix (CallerMetadataReadMemo.DeleteAsync is a pass-through decorator; exempt API implementations' own DeleteAsync with must-fire/must-not-fire controls). **Part B** agent on task/fm-049c (worktree agent-a3d88f8312b09e715): scan $orderby + recent window ≥3× tick, reconcile re-derive notes (if removals not re-derived: FILE it), record-busy shapes; notes in task-049-backstop-notes.md (fold into task-049-notes.md). Then: merge 049c into 049b, verify-suites, Opus pass 2, head SHA → uac-r2, merge into work branch, PR, dev deploy via uac-r2, Project→Matter profile on dev, 021 re-measure.
- **021**: wizard deploy waits on 049's re-measure (task/fm-021 d6dc73e9f6).
- **033 must be re-scoped before it runs** (D-204).
- **Live rows to delete later (owner):** task-021-live.md / task-019-live-checks.md.
- **Private:** uac-r2 by message only. |
| **Next Action** | 1. On agent reports (046b review, 049 A, 049 B): triage F/K, fix rounds, scoped re-checks, ranges to uac-r2.
2. Watch PR #2573; then tell uac-r2 the merge SHA.
3. Owner open: live checks on dev (042c/048 refusals, 043); standalone add-on batch (via uac-r2); inactive playbook at analysis start; Family 1.0.56; 076; #2556. |
| **Phase branches** | 2d-1 798936c47e (step 10 pre-create; new codes stamp_underivable 400 / stamp_unavailable 503; Office map rows added as generic 500; system principal still stamped post-create = known limit). 2d-2 e106b19d7c (task/contact/account Child + MappingExclusion; owner + row byte-identical vs today). 017a f758480f3c (ebilling OK, no findings). 017b 43b48f3249 (route, decision parity, R3-1, 037 K2 = copy after merge, #1608, documents, invoice-family protection from guard, sprk_servicerequest; NFR-07 table all same; 401 stays 401; traceId kept). 017c 9cc37b415a (chat refuses invoicesubmission/approval/correction; escalated 3 items, resolved 2026-10-30). R3-2 = K2 known limit. |
| **Open PRs** | #2387 (075, HOLD, ships with 040). |
| **Issues** | #2444, #2413, #2429 (provisioning; I review), #2469 (flaky ISS-005), #1948 (lint-staged uses the shared stash; 017b re-found it). |
| **Waiting on** | - Owner: the Family 1.0.56 look (the four #2513 items on dev) and the 076 narrower-user pass. **Done 2026-11-03:** 005 `-Apply` on dev (36 migrated, VERIFY PASS, idempotent); pid 53128 (the pre-crash session) stopped on the owner's "kill it".
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
