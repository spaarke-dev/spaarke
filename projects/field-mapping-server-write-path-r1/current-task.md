# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-06 ~05:40Z (main session checkpoint). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–016, 017, 018, 037, 039, 038, 070–074, 043 + 046 (#2573, 0f6618094b), 048, 042c.
> **Live on dev:** BFF master 0f53e053bf (042c + 048). 046b role additions (from 98813dcaa2, 2026-11-05) and removal R1 (from e6c0c94fa3, 2026-11-06 02:35Z) are applied by UAC-r2, and VERIFY PASS. Gate G-8 (`JobRevokeOnChangeEnabled`) has been ON on dev since 2026-11-06 04:02Z (#2574); the watch passed. The thread filing sweep dry run found 0 threads to reassign, so the apply was skipped.
> **Next dev deploy:** one combined deploy of master with 049, plus the code pages Spaarke.UI.Components → LegalWorkspace, SpaarkeAi, AI.Widgets (CreateProjectWizard optional), BFF first. It needs the owner's go in uac-r2's session. The gate covers the 043 additions (analysis create 0 5xx; push with an unchanged 409 rate on non-analysis targets; child-records create 0 5xx).

## Quick Recovery

| Field | Value |
|---|---|
| **Active** | uac-r2: `spaarke-wt-unified-access-control-r2-20`; ontology: `spaarke-wt-spaarke-ontology-platform-r1-38`; ebilling: `spaarke-wt-ebilling-r1-c7`.
- **046b → PR #2592** (work branch head ad40add283 = 046b d7ab6de7dc + master 1f1c319480), auto-merge on. Verified: ArchTests 1973/1973, filtered comm tests 779/779, Pester 39/39. After merge: tell uac-r2 the SHA. Set TASK-INDEX 046b ✅ (done after merge, so the PR CI isn't re-triggered). Ebilling merge-second rule: whoever merges second takes 089e's ProcedureRolePrivilegeGuardTests.cs and Deploy-EbillingBillingAdminRole.ps1 in full and keeps one copy of the G5 privilege (ebilling told).
- **049** (task/fm-049b, worktree agent-ab6391bd0d308344f, tip 598805e9ac + fix round 3 in progress): Parts A, B (secure-from-creation, D1 shared DelegationRuleFilter.RefusalFor, D2 already_provisioned) and C (scans) are merged. verify-suites 6890/0/7; publish delta +15,331 B (39.22 MB, 191/191), no CVE. Opus pass 2 done; **fix round 3 agent running** (items 1–9; item 10 = Basic doesn't cover a team-owned target, UAC-approved, invert the 2 new tests; item 11 = drop the stale "27 of 35" from the CommunicationThreadFilingSweepJob remarks). Then: a scoped re-check, then the **history rewrite** of task/fm-049b (one squashed commit from the merge base; old notes :508-513 text about a race must not survive) and deletion of remote task/fm-049c. **ASK THE USER before the force-push/delete.** Then the head SHA → uac-r2, merge into the work branch, PR, combined dev deploy, Project→Matter profile on dev, 021 re-measure.
- **046c** (Basic User read-only on sprk_*; POML on the work branch, with uac-r2's pre-apply evidence, rollback and window conditions): starts after 046b merges.
- **021**: wizard deploy waits on 049's re-measure (task/fm-021 d6dc73e9f6).
- **033 must be re-scoped before it runs** (D-204).
- **Private:** uac-r2 by message only. |
| **Next Action** | 1. On the 049 fix round 3 report: scoped re-check → ask the user about the 049b rewrite + 049c delete → SHA to uac-r2.
2. Watch PR #2592 → merge SHA to uac-r2 → TASK-INDEX 046b ✅ → start 046c.
3. Owner open: live checks on dev (042c/048 refusals, 043) need the owner's go in THIS session; inactive playbook at analysis start; Family 1.0.56; 076; #2556. |
| **Owner rulings 2026-11-06** (decisions.md) | R2 not applied; communication send/re-file needs Core User (G11/G12 not granted); Basic User is read-only on sprk_* (046c); G13 is not a gap (thread Read stays Basic, capped by a guard). |
| **Open PRs** | #2592 (046b). #2387 (075, HOLD, ships with 040). |
| **Issues** | #2574 (G-8, flipped on dev), #2590 (uac: root-BU project create), #2444, #2413, #2429, #2469, #1948. |
| **Owner to do** | 076 live check. Leftover locked worktrees agent-aec6d06ca99a5a9f6 (075), agent-a7ed21b81867c4e2e (039): end of project via Remove-WorktreeSafe. Worktrees in use: agent-ae68466fd18cb8df3 (046b), agent-ab6391bd0d308344f (049b), agent-a3d88f8312b09e715 (049c), agent-a7d775a1f12d2a061 (043, done), agent-ada15efbfe379567a (046, done). |

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

049 → PR, then 046c, then 021's deploy. Then 019, 033 (before 052), 040, 050.
