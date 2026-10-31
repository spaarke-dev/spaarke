# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-10-30 (by context-handoff). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–016, 017, 018, 037, 038, 070, 071, 072, 073, 074. The work branch was merged to master as #2435 (6b0c1e2fed); later doc commits are on the work branch.
> **Live on dev:** BFF 136568cf70 (UAC gate passed). SpaarkeMaster 1.2.4.0 is published but not imported anywhere yet. Check that the BU→Matter index map is gone after the model1-prod upgrade or the next provisioning run (H6).

## Quick Recovery

| Field | Value |
|---|---|
| **Active** | **None in flight.** 017 COMPLETE: PR #2486 merged as b083d62183 (2026-10-31T03:35Z; UAC-r2 approved at ff2f76e39e after its F1 fix f47192cb52; ebilling: no findings). POML completed with section 11 `<new-surface>` entries; TASK-INDEX ✅; drift check clean (53/53); work branch merged with master (c7ef8cbe76). **Re-seed (OURS to run, 2026-10-31):** UAC-r2, not R3, owns the next batched dev BFF deploy. R3 will not run the re-seed: an owner OK relayed from another session does not authorize a live write there. So, in THIS session, with the owner's OK given HERE, right after UAC-r2's after-notice for the deploy carrying b083d62183, re-seed `sprk_analysistool` row `DATAVERSE-CREATE-RECORD` from `infra/dataverse/sprk_analysistool-dataverse-create-record-row.json` (Seed-TypedHandlers.ps1; live write, owner OK), else RoutingConsumerTypeHealthCheck description parity = Unhealthy. **076**: BFF 9f4afe82ac + TrackingFieldTrio 1.0.55 live on dev; waiting on the owner's live pass. |
| **Next Action** | 0) DONE 2026-10-31: deploy note verified and corrected (RoutingConsumerTypeHealthCheck does compare sprk_description at :383-415, byte for byte; dev /healthz/catalog has been Unhealthy since at least 08-24 from 8 orphan binding rows + 4 legacy tool rows = #1987, which I commented on; the re-seed is still needed so the model stops seeing the old text that offers sprk_matternumber). The PR #2486 note is corrected; R3 has the note (no write without the owner OK through us); UAC-r2 is answered. 1) **039** (FR-38, server-owned search index; FULL, sonnet/high; deps 017 met) through task-execute; branch from origin/master (017 is on master). 2) Then 019 (W2 deploy via R3 + live checks; deps 017, 018, 039), including the tool-description re-seed above. 3) 076 App Insights after the owner's live pass. 4) Housekeeping: delete the merged remote sub-branches task/fm-017, -017a/b/c, -017-d1..d5, -017-r1..r3 (all in master). Owner decisions pending: chat external invites (product, own change); #2483 follow-up change 1 (ordinary row may move to the root's BU) when that fix is ready. |
| **Phase branches** | 2d-1 798936c47e (step 10 pre-create; new codes stamp_underivable 400 / stamp_unavailable 503; Office map rows added as generic 500; system principal still stamped post-create = known limit). 2d-2 e106b19d7c (task/contact/account Child + MappingExclusion; owner + row byte-identical vs today). 017a f758480f3c (ebilling OK, no findings). 017b 43b48f3249 (route, decision parity, R3-1, 037 K2 = copy after merge, #1608, documents, invoice-family protection from guard, sprk_servicerequest; NFR-07 table all same; 401 stays 401; traceId kept). 017c 9cc37b415a (chat refuses invoicesubmission/approval/correction; escalated 3 items, resolved 2026-10-30). R3-2 = K2 known limit. |
| **Open PRs** | #2387 (075, HOLD, ships with 040). |
| **Issues** | #2444, #2413, #2429 (provisioning; I review), #2469 (flaky ISS-005), #1948 (lint-staged uses the shared stash; 017b re-found it). |
| **Waiting on** | UAC 195 → 040 (+075). Provisioning's #2429 PR. Owner's 076 live pass. |
| **Owner to do** | 076 live check (owner + narrower user). Leftover locked worktree agent-aec6d06ca99a5a9f6 (075): end of project. |

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
