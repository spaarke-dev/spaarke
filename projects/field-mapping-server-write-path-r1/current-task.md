# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-10-30 (by context-handoff). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–016, 017, 018, 037, 039, 038, 070, 071, 072, 073, 074. The work branch was merged to master as #2435 (6b0c1e2fed); later doc commits are on the work branch.
> **Live on dev:** BFF fcf01480f4 since 2026-11-01T14:03Z (R3; external-access delta only; carries 017 but NOT 039 0e3962069b). 017 (b083d62183, #2486) first went live 2026-10-31T19:52:25Z (UAC-r2 deploy; gate PASS: 23 req, 0 5xx, 0 OWNERSHIP-REFUSED, the reconciler wrote nothing). TrackingFieldTrio 1.0.55. SpaarkeMaster 1.2.4.0 is published but not imported. Check that the BU→Matter index map is gone after the model1-prod upgrade or the next provisioning run (H6).

## Quick Recovery

| Field | Value |
|---|---|
| **Active** | **None in flight.** 039 COMPLETE: PR #2504 merged as 0e3962069b (2026-11-01T09:50:31Z); POML completed; TASK-INDEX ✅; drift clean (53/53); master merged into the work branch (4daae231ea). FR-38 widened by the owner (six tables, both columns). UAC-r2: no blocking findings, K5 accepted; ebilling: no objection. Not live: it ships with the next batched dev BFF deploy. 017 COMPLETE and live on dev since 2026-10-31T19:52Z; chat tool row re-seeded 22:55:34Z. |
| **Next Action** | 1) **019** (W2 deploy via the batched dev BFF deploy, plus live checks and the adversarial review; deps 017, 018, 039 all met) through task-execute. It needs a deploy candidate that carries 0e3962069b, run by whoever holds the batch (R3 or UAC-r2), with the owner's go in that session, and then this project's live checks. 2) 076: App Insights after the owner's live pass (the projects pair-filed check can run now that 195's backfill is in). 3) 040 still needs 005 (`-Apply` on hold, owner) and 019. 4) Owner decisions: #2502 (rag/index-file names the index); chat external invites; the #2483 fix change 1. 5) Housekeeping: the merged remote branches task/fm-017*, task/fm-039 (delete only if the user wants). |
| **Phase branches** | 2d-1 798936c47e (step 10 pre-create; new codes stamp_underivable 400 / stamp_unavailable 503; Office map rows added as generic 500; system principal still stamped post-create = known limit). 2d-2 e106b19d7c (task/contact/account Child + MappingExclusion; owner + row byte-identical vs today). 017a f758480f3c (ebilling OK, no findings). 017b 43b48f3249 (route, decision parity, R3-1, 037 K2 = copy after merge, #1608, documents, invoice-family protection from guard, sprk_servicerequest; NFR-07 table all same; 401 stays 401; traceId kept). 017c 9cc37b415a (chat refuses invoicesubmission/approval/correction; escalated 3 items, resolved 2026-10-30). R3-2 = K2 known limit. |
| **Open PRs** | #2387 (075, HOLD, ships with 040). |
| **Issues** | #2444, #2413, #2429 (provisioning; I review), #2469 (flaky ISS-005), #1948 (lint-staged uses the shared stash; 017b re-found it). |
| **Waiting on** | UAC 195 is MERGED (d0c1c5dc3c, #2496, 2026-10-31; master merged into the work branch, 70567b4dff). Its contract for 040 is in `notes/decisions.md` 2026-10-31. 195 is not live yet: UAC-r2 will send the deploy time, after the owner's go there for the deploy, the ribbon import and the backfill. 040 (+075) still needs 005 and 019. Provisioning's #2429 PR. Owner's 076 live pass. Owner OK'd the re-seed (done) and the go for 039 (started 2026-10-31). |
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
