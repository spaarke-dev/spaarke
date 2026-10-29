# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-10-29 (by context-handoff). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–016, 018, 037, 038, 070, 071, 073 (072 in PR #2459). The work branch was merged to master as #2435 (6b0c1e2fed); later doc commits are on the work branch.
> **Live on dev:** BFF 136568cf70 (UAC gate passed). SpaarkeMaster 1.2.4.0 is published but not imported anywhere yet. Check that the BU→Matter index map is gone after the model1-prod upgrade or the next provisioning run (H6).

## Quick Recovery

| Field | Value |
|---|---|
| **Active** | **072 ✅ done** (PR #2459, auto-merge; task/fm-072 @ 8378b3ee1d). recordType = table display name via MetadataService (DTO DisplayName, key v2); root = walk top on full, omitted on down (owner told; no reply yet). **Next: 074** (#2383, HOLD until #2459 merges) — mark ready, run the TrackingFieldTrio suite against the merged endpoint. |
| **Next Action** | 1) Watch #2459 merge; then sync the work branch with master. 2) #2383 (074): rebase on master, mark ready, run its suites. 3) 076 (W7 deploy + live Family check) once 074 lands; needs the owner's deploy go in R3's session. |
| **Owner decisions today** | Q1 drop; Q2 (B), seeding the 7 canonical index rows per customer, team-owned, no BU defaults. Depth cap counted from the TOP (exception to UAC C4; design §12.11, quoted on #2442; UAC adopted it). WA owning unit follows its matter (UAC #2451). |
| **#2399** | Live part FIXED: the owner re-tested the WA create as Test User 1 on 2026-10-29 (saves, with number and Assigned To). Open for the durable fix: FR-38/W3 still need the scope procedure (spec amendment → task-create → TASK-INDEX → owner approval); FR-39 and the index rows are provisioning's #2429. |
| **Open PRs** | #2459 (072, auto-merge). #2383 (074, HOLD until #2459). #2387 (075, HOLD, ships with 040). They overlap current-task.md: keep the work branch's version. |
| **Issues** | #2444 (UAC: shared reads-agnostic ordering helper). #2413 (known limit). #2429 (provisioning H7c team + H12b index-row seed + H4b allow-list; I review the PR). |
| **Waiting on** | UAC 195 → 040, 076. Provisioning's #2429 PR. 017 blocked on ebilling #1911. |
| **Owner to do** | Leftover locked worktree agent-aec6d06ca99a5a9f6 (075): end of project. |

## Blocked: 017

Re-pointing the route and the chat create needs ebilling #1911 (still a draft) plus its reconcile, and these owner items:
All four owner decisions are made (2026-10-22/23: 2a-2c option a, 2d option c); only ebilling #1911 remains.

## How wave-2 agents are run (lessons)

- `isolation: worktree` cuts from **origin/master**. Every brief starts with `git switch -c task/fm-NNN <tip>`.
- Launch adversarial-reviewer passes from the main session (one Opus per change set; a scoped Sonnet re-check per fix
  round). Resume an implementer with SendMessage for its fix rounds.
- **After ANY master merge, run the full ArchTests AND the related suites.** Rebuild ArchTests first; a stale DLL gives
  false failures.
- **Never run the unfiltered `Sprk.Bff.Api.Tests`** in an agent (the host reaches ~8 GB). Use the filtered set in
  `projects/field-mapping-server-write-path-r1/scripts/verify-suites.sh <repo> <log-dir>`. Publish-size delta + CVE:
  `scripts/publish-delta.ps1 -wt <worktree> -MasterSha <sha> -BranchSha <sha>` (same folder).
- Call `Remove-WorktreeSafe.ps1` through PowerShell. If the delete is partial it leaves an unregistered folder; report it.
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
2. **017 decisions:** FR-06 step 10 (UAC-r2 text recommended); #1608 columns; the chat matter/project number (recommend
   refusing it); SystemPrincipal.
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
