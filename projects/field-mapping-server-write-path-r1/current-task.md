# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-10-29 ~02:15Z (by context-handoff). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–016, 018, 037, 038, 070, 073. Work branch merged to master as #2392 (14b9335a6c); later doc commits are on the work branch (e2771ff315, 86dc5c1f92 and this one).
> **Live on dev: 136568cf70** (R3, live 2026-10-29 02:23:53Z; UAC gate passed, no 5xx). SpaarkeMaster 1.2.4.0 published 02:24:48Z (run 40806065681, master a4b6459da3), not yet imported anywhere: map check follows model1-prod's upgrade or the next provisioning run (H6). #2408 merged a4b6459da3; C:\wt2400 removed.

## Quick Recovery

| Field | Value |
|---|---|
| **Active** | **#2399 (live defect)**: a child-BU user's WA create returns 404, because the wizard binds BU1's AI search index and Core User's Deep Read and AppendTo can't reach root-owned `sprk_aisearchindex` rows. Not caused by the deploy. Owner chose re-ownership; UAC-r2 rejected Global. Mapping profiles and rules have the same defect (root-owned). |
| **Plan (design §13, accepted by UAC-r2 at 86dc5c1f92)** | (a) UAC builds the memberless team "Spaarke Customer Configuration Owners" and the role "Customer Configuration Owner" (Read at Basic on sprk_aisearchindex, sprk_fieldmappingprofile, sprk_fieldmappingrule) in 194b R4b-3b. **Wait for their sha.** (b) After the owner's go: create the team and role in BU1 (cb15f587-baa0-f111-aaac-000d3a99d1d7) on dev, then re-own 10 index rows + 7 profiles + 40 rules with a read-back (nothing cascades; checked). (c) FR-38: the server drops a client index on every BFF write path and applies the FINAL owning BU's index (empty and logged if none). (d) FR-39: provisioning owns it (one PR, H10 team + H12b owner bind; I review). (e) W3 wizards stop binding the index. |
| **Next Action** | 1) **Owner decided 2026-10-29:** §13.5 Q2 = (A) no `sprk_aisearchindex` rows in customer environments; Q1 = drop. Provisioning and UAC-r2 told. 2) **Dev re-own DONE 2026-10-29** (owner's go): 57 rows → team 2dae7e6e… in BU1, read-back 57/57; Test User 1 has Read+AppendTo on BU1's index. Owner to re-test the WA create in the UI; then close #2399's live part. Rollback list: session scratchpad `reown-2399-backup.json`. Provisioning creates the team in H7c (#2429); UAC 194b = c4fd7175cd. 3) Scope procedure for FR-38/FR-39: spec amendment → task-create → TASK-INDEX → owner approval. 4) R3: when 1.2.4.0 reaches model1-prod, check the BU→Matter index map is gone and how it was upgraded; stamp proof = the next new provisioning run (H6 StageAndUpgrade); don't check ACME. Dev is already clean (checked 2026-10-28). bafe310ca4 (#2382) doesn't change Core User depth on index/profile/rule; §13 (e) removes the index entry from `config/core-user-wizard-linking-role.json` (0e5904d4aa). |
| **Open PRs** | #2383 (074, draft, HOLD until 072). #2387 (075, draft, HOLD, ships with 040; head ed1cf9b5d1). #2408 (#2400 error icon, auto-merge). |
| **Issues filed today** | #2399 (above), #2400 (icon; PR #2408), #2410 (CLOSED, not planned: owner says Basic users need no profile access), #2411 (CLOSED: duplicate inactive index row deleted on dev with the owner's OK), #2413 (known limit: index not recomputed on a BU move; discoverability only). |
| **Running** | Nothing. |
| **Waiting on** | UAC 194b sha → 071 → 072 (then mark #2383 ready). UAC 195 → 040, 076. UAC R4b-3b sha → #2399 re-own. 017 blocked on ebilling #1911. |
| **Owner to do** | Drop stash d5d17bcc7f (074 agent's lint-staged backup). Leftover agent worktree `C:\code_files\spaarke\.claude\worktrees\agent-aec6d06ca99a5a9f6` (locked by a claude.exe; contents on origin) and C:\wt2400 (fix/2400 worktree) to remove with Remove-WorktreeSafe at the end. |

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
