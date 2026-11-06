# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-06 ~08:10Z (main session checkpoint). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–016, 017, 018, 037, 039, 038, 070–074, 043 + 046 (#2573), 048, 042c, 046b (#2592, c5aa7ab6cd).
> **Live on dev:** BFF master 0f53e053bf (042c + 048). 046b additions and R1 applied by UAC-r2, VERIFY PASS. G-8 ON on dev since 2026-11-06 04:02Z.
> **Next dev deploy:** combined deploy of master with 049 (after #2599 merges): BFF first, then Spaarke.UI.Components → LegalWorkspace, SpaarkeAi, AI.Widgets (CreateProjectWizard optional). UAC-r2 puts it to the owner in its session.

## Quick Recovery

| Field | Value |
|---|---|
| **Active** | uac-r2: `spaarke-wt-unified-access-control-r2-20`; ontology: `spaarke-wt-spaarke-ontology-platform-r1-38`; ebilling: `spaarke-wt-ebilling-r1-c7`.
- **049 → PR #2599** (work branch head dc08e14990 = master b6dee2e590 + 049 squashed e8da405eb0), auto-merge on with --match-head-commit. UAC-r2 approved e8da405eb0. Verified: build 0 warnings, ArchTests 1973/1973, verify-suites 6968/0/7, client tsc 0, publish delta +19,759 B (39.23 MB), no CVE. Do NOT commit to the work branch until #2599 merges (this checkpoint is uncommitted on purpose).
- **History cleanup done** (owner go 2026-11-06): task/fm-049b squashed and force-pushed; remote task/fm-049c deleted. No remote branch holds the old commits.
- **Flaky client test** OntologyAdmin Rules "Retire" filed #2597 (not ours).
- **046c** (Basic User read-only on sprk_*): next task; POML has uac-r2's pre-apply evidence, rollback and window conditions.
- **021**: wizard deploy waits on 049's dev deploy + re-measure (task/fm-021 d6dc73e9f6).
- **033 must be re-scoped before it runs** (D-204).
- **Private:** uac-r2 by message only. |
| **Next Action** | 1. On #2599 merge: send master SHA to uac-r2; commit this checkpoint + TASK-INDEX 049 ✅ (POML status completed); push.
2. Start 046c via task-execute.
3. After the dev deploy: Project→Matter profile on dev, 021 re-measure.
4. Owner open: live checks on dev (042c/048 refusals, 043) need the owner's go in THIS session; inactive playbook at analysis start; Family 1.0.56; 076; #2556. |
| **Owner rulings 2026-11-06** (decisions.md) | R2 not applied; communication send/re-file needs Core User (G11/G12 not granted); Basic User is read-only on sprk_* (046c); G13 is not a gap (thread Read stays Basic, capped by a guard). 049 history rewrite + 049c delete approved. |
| **Open PRs** | #2599 (049). #2387 (075, HOLD, ships with 040). |
| **Issues** | #2597 (flaky Rules test), #2583, #2574, #2590, #2444, #2413, #2429, #2469, #1948. |
| **Owner to do** | 076 live check. Leftover worktrees for end-of-project Remove-WorktreeSafe: agent-aec6d06ca99a5a9f6 (075), agent-a7ed21b81867c4e2e (039), agent-ae68466fd18cb8df3 (046b, merged), agent-ab6391bd0d308344f (049b), agent-a3d88f8312b09e715 (049c, remote deleted), agent-a7d775a1f12d2a061 (043), agent-ada15efbfe379567a (046). |

## How wave-2 agents are run (lessons)

- `isolation: worktree` cuts from **origin/master**. Every brief starts with `git switch -c task/fm-NNN <tip>`.
- Launch adversarial-reviewer passes from the main session (one Opus per change set, two for security; a scoped Sonnet re-check per fix round). After an idle pause, start a fresh implementer.
- **After ANY master merge, run the full ArchTests AND the related suites.** Rebuild ArchTests first; a stale DLL gives false failures.
- **Never run the unfiltered `Sprk.Bff.Api.Tests`.** Use `scripts/verify-suites.sh <repo> <log-dir>`. Publish delta + CVE: `scripts/publish-delta.ps1 -wt <worktree> -MasterSha <sha> -BranchSha <sha>` (main session only).
- A reviewer must not build in a worktree where verify-suites is running (file locks). Give it read-only scope or its own detached worktree.
- Public repo: notes, POMLs, commits and PR bodies state rules and gaps only. A description of an open access condition on a pushed branch needs a history rewrite (uac-r2 2026-11-06).
- Call `Remove-WorktreeSafe.ps1` through PowerShell after `cd`-ing every shell out of the worktree; then `git worktree prune`.
- CI: the required checks are `Router` + `Client lint (type-aware ratchet)`; Router reports after the Tier 2 shards (~25 min).

## Cross-project obligations

- **UAC-r2:** reviews and runs every live role or app-setting change and every dev deploy, with the owner's go in its session.
- **ebilling-r1:** 164a makes sprk_billingevent create-only refused (not full writer-only; D-96 updates must keep working). It edits RecordTableRules, ChildRecordEndpoints (CreateTables) and the ribbon JS. Whoever merges second of 164a and 049b resolves ChildRecordEndpoints.
- **ontology-r1:** 174 touches Signals files only (no overlap). 033 needs a design-note review first; 052 is hard-blocked on 033.
- **R3:** deploys only batched candidates from UAC-r2.

## Next tasks

049 → merge (#2599), then 046c, then 021's deploy. Then 019, 033 (before 052), 040, 050.
