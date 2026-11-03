# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-02 (main session checkpoint). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–016, 017, 018, 037, 039, 038, 070, 071, 072, 073, 074. The work branch was merged to master as #2435 (6b0c1e2fed); later doc commits are on the work branch.
> **Live on dev:** BFF 5796774091 since 2026-11-03T13:29Z (R3; carries 039, #2514, #2519, #2520; 019 live A/B ran on 3740e1a105). 017 (b083d62183, #2486) first went live 2026-10-31T19:52:25Z (UAC-r2 deploy; gate PASS: 23 req, 0 5xx, 0 OWNERSHIP-REFUSED, the reconciler wrote nothing). TrackingFieldTrio 1.0.55. SpaarkeMaster 1.2.4.0 is published but not imported. Check that the BU→Matter index map is gone after the model1-prod upgrade or the next provisioning run (H6).

## Quick Recovery

| Field | Value |
|---|---|
| **Active** | **2026-11-02, session 5c is the only field-mapping session.** The owner was asked to close the other tab (pid 53128, started 12:55).

**019:**
- Code gate done: two passes, all F-class findings fixed (#2514, #2520).
- Dev BFF is 3740e1a105 (R3, 18:52Z).
- Live checks A and B PASS (`notes/task-019-live-checks.md`). Check A's mapping clause moves to 020.
- C (Console chat) and the negative AppendTo check (as a narrower user) are with the owner.
- Findings, all filed:
  - #2522: no telemetry for the child-records create;
  - #2523: Office Project create takes ~18 s, with sequential grants (any change needs UAC review);
  - the junction exception is the known #2292 (not new).
- UAC-r2 found the check-B project has no `sprk_Project_Matter_nn` link row. No BFF path writes that link; only the wizards do. UAC-r2 is deciding whether it is still required and is filing it.

**042a:** PR #2521 (auto-merge).

**041 and 042b:** approved, waiting on 019. |

**019:**
- Code gate done: both adversarial passes ran, with no open F-class finding.
- Fixes: #2514 is merged; #2520 (privacy, payload logging) is auto-merging.
- Notes: `notes/task-019-notes.md`, covering P1/P2 and the publish size.
- Remaining: R3's batched dev deploy (039 + #2514 + #2520 + #2519), then live checks A-C. The owner approved the `FMSWP-019` test rows. Then App Insights.

**042a** (characterization tests for #2483): implementer fm-042a is running on `task/fm-042a`.

**Approved by the owner on 2026-11-02:** 041 (#2502 index routing) and 042b (#2483 fix). Both are gated on 019. UAC-r2 checks 041's diff (scoped) and 042b's diff (full) before merge.

**Done today:**
- #2513 Family fixes: PR #2516; TrackingFieldTrio 1.0.56 is live on dev.
- #2515 event number: `EVT-{SEQNUM:6}` is live on dev; the code is PR #2519 (auto-merging).
- 050 is done: notes/w5-open-questions.md.
- Design §14 and §15; spec FR-40 and FR-41; FR-21 ontology exception; FR-25 family-alignment outcome. |
| **Next Action** | 1. Owner runs check C in the Console on MAT-000032, prompt: "Create a to-do named "FMSWP-019 C" filed under matter MAT-000032 (Matter 123456.01 Word Add In 10-22-2026)". Then read the row back: stamp, team owner, number.
2. Owner runs the negative check as `# UAC Child BU Test User` under secure matter PAT-908068. Expect a uniform 404 and no row.
3. Get the owner's OK, then re-seed the chat tool description (`scripts/Seed-TypedHandlers.ps1`, `DATAVERSE-CREATE-RECORD` row).
4. Close 019: TASK-INDEX ✅, POML status, decisions entry.
5. Launch 041 and 042b in parallel. UAC-r2 gets 041 (scoped) and 042b (full) before merge.
6. Then W3: 020 onward, plus 030–034.
7. If UAC-r2 says the N:N link is required: take it through the scope path as a `RecordCreationService` step-12 write.
8. Stale wording in POMLs 020–034 and 052, and the 028 test path: fix each as its task starts. |
2. When R3 sends the deployed SHA: confirm it contains 0e3962069b, a50ada7330, #2520 and #2519. Then re-seed the chat tool description, which needs the owner's OK (#2519 deploy note). Then run 019 live checks A-C, where B includes P2-2, the Office 412 autonumber retry.
3. After 019: start 041 and 042b in parallel, then W3 (020 onward; 021-029 now wait on 041), and the W3 server tasks 030-034.
4. Mark 050 ✅ in TASK-INDEX.
5. Fix the stale "search-index default" wording in POMLs 020-034 and 052, and the 028 test path, as each task starts. |
| **Phase branches** | 2d-1 798936c47e (step 10 pre-create; new codes stamp_underivable 400 / stamp_unavailable 503; Office map rows added as generic 500; system principal still stamped post-create = known limit). 2d-2 e106b19d7c (task/contact/account Child + MappingExclusion; owner + row byte-identical vs today). 017a f758480f3c (ebilling OK, no findings). 017b 43b48f3249 (route, decision parity, R3-1, 037 K2 = copy after merge, #1608, documents, invoice-family protection from guard, sprk_servicerequest; NFR-07 table all same; 401 stays 401; traceId kept). 017c 9cc37b415a (chat refuses invoicesubmission/approval/correction; escalated 3 items, resolved 2026-10-30). R3-2 = K2 known limit. |
| **Open PRs** | #2387 (075, HOLD, ships with 040). |
| **Issues** | #2444, #2413, #2429 (provisioning; I review), #2469 (flaky ISS-005), #1948 (lint-staged uses the shared stash; 017b re-found it). |
| **Waiting on** | - Owner: check C, the negative check, the re-seed OK, the Family 1.0.56 look, closing the other tab, 005 `-Apply`, and the 076 narrower-user pass.
- UAC-r2: the N:N link decision.
- Rows to delete later: see `notes/task-019-live-checks.md`. |
- The owner's Family view check on 1.0.56.
- The owner's 076 narrower-user pass.
- 005 `-Apply` (owner).
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
