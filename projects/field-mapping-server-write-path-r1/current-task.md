# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-07 ~08:30Z (main session checkpoint). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–018, 030a, 037–040, 042c, 043, 044, 046, 046b, 046c repo side, 048, 049, 070–075. #2610 (fb5738b60d), #2617 (cc11294300: 040+075+044).
> **Live on dev:** BFF + Console at master 20669df6a9 (UAC-r2 is putting the batch #2292+#2610+#2617 to the owner). 046c APPLIED on dev 2026-11-07 (G17 03:33:12Z, R3 80 removals 03:36:46Z, -Verify PASS; UAC-r2 2 h watch PASS: 0 privilege faults, 0×403). Project to Matter profile kept (owner). 54 FMSWP-021 matters (cleanup list in notes/task-021-live.md).
> **W3 direction:** named exceptions plus guards; no core redesign in R1 (decisions.md). Owner decisions 2026-11-07 in decisions.md.

## Quick Recovery

| Field | Value |
|---|---|
| **Work branch** | 8a50a12def (= master cc11294300 + 049b merge + task index). **PR #2619 OPEN with auto-merge (049b)**: do NOT push to the work branch until it merges (monitor bqgt6y9y9). This file is uncommitted until then: commit it after the merge. verify-suites PASS (Arch 1998, BFF 7086/0 fail); publish delta +2,432 B (39.26 MB); no CVE. |
| **After #2619 merges** | Send UAC-r2 the master SHA (049b follows the current deploy batch, next window). Commit this checkpoint. |
| **049c** | origin/task/fm-049c 47504e3dae (overlaps step 3's reads with step 2, a shared request-scoped metadata memo, step 5's two checks run together; local pre-create ~1620 → ~980 ms; live estimate still ~0.6–0.8 s over p95). Opus review: no production F; F3 weak timing assertion; K cancel the merged check on the caller's refusal. **fm-049c-fix1 RUNNING** (Sonnet; branch task/fm-049c-fix1). **Asked UAC-r2 to ack (A) the FR-04 probe now asked before step 2 and (B) the merged check asked concurrently** (POML escalation "an order, a check"). Then merge into the work branch → PR → deploy → re-measure (Debug-level step timing needed). If still over: propose 049d to the owner (cross-request metadata cache with short TTL; owner/post-create only with UAC-r2). |
| **Deploy batch (UAC-r2)** | #2292 + #2610 + #2617 + the MCP spike commit; awaiting the owner's go in UAC-r2's session. #2617 client order: BFF → `sprk_recordfamily` (Deploy-WebResourceInline create + add to spaarke_core, read back) → `sprk_fieldmapping_push` v1.3.0 (Type 3). Live UI checks (a)–(d) (opens; push one FMSWP- child; K2 failed save opens nothing; no-Filing-edge 409) need either the owner clicking through or the owner's go to run them here. `Set-FieldMappingRuleSyncMode -Apply` stays out (owner review). |
| **Owner questions open** | (1) After the deploy: who runs the Push Updates live UI checks (owner, or fm with the owner's go for FMSWP- writes)? (2) After the 049c re-measure: 049d if still over NFR-02. (3) Sync-mode -Apply on dev (28 Assigned-To rules → One-time, no behaviour change): review. |
| **Re-scope pending** | 032 (mapping step in the persister; inputs notes/task-032-design-inputs.md; design to UAC-r2 + ebilling first), 030b (association in the existing create + guards; send to word-add-in for a content read), 034 (named exceptions + guards), 033 (likely dropped). |
| **Filed this session** | #2612 (live check of who holds the Signal/DR writer roles), #2613 (stale vite build list in Build-AllClientComponents.ps1). Ontology filed #2611 (Rules.test.tsx load flake). |
| **Gotchas this session** | Pester 6.2 is the machine default: `Import-Module Pester -MaximumVersion 5.99`. App Insights drops whole requests (#2158). `timeout 400 git commit`. Don't push to the work branch while its master PR is open. The mcp-bff session can't be messaged (permission mode); the owner relays. `git log --all --find-object` also searches refs/stash: use `--branches --remotes`. |

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

049b PR → 049c (+ UAC-r2 ack) → deploy batch + live UI checks → 049c re-measure → 021 deploy (after the owner's NFR-02 call) → 032/030b/034 re-scopes → 019, 033 (before 052), 050.
