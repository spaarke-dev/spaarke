# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-06 ~23:05Z (main session checkpoint). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–018, 037–039, 042c, 043, 046, 046b (#2592), 048, 049 (#2599), 070–074.
> **Live on dev:** BFF + Console at master 20669df6a9. "Project to Matter (Attorney Matrix)" profile created 20:42Z (owner-approved live check; keep/remove = owner question). 54 FMSWP-021 test matters (cleanup list in notes/task-021-live.md).
> **W3 direction:** named exceptions plus guards; no core redesign in R1 (decisions.md).

## Quick Recovery

| Field | Value |
|---|---|
| **Work branch** | dde7ae35d7 (pushed). Merged in: 030a (+fix, +fix2 transient OFFICE_010 text), 046c (+fix, +fix2), 6e9a6510a4 (046b guard: Create/Write on Signal/DR with no exception). verify-suites PASS before fix2 (Arch 1978, BFF 6984/0 fail). |
| **In flight (main)** | publish-delta running (master f9ba615b09 vs dde7ae35d7). master is 222 ahead, merge-tree clean. NEXT: merge origin/master into the work branch (Step 2.5), rebuild, ArchTests + verify-suites, then master PR (030a, 046c, 046b guard fix). After merge: tell ontology-r1 (mcp-bff re-merge) and UAC-r2 (master SHA). |
| **Agents running** | fm-049b (opus; maker-only inline grant, UAC-r2's 4 conditions; branch task/fm-049b from master). fm-040-fix (opus; 12 findings, single classifier per UAC-r2, wrapper type if small). rev-044 (opus adversarial pass 1 of 2 on task/fm-044-fix 9159cfb376; ontology + UAC-r2 already approved, no F). |
| **044 after review** | pass 2 (security) then merge task/fm-044-fix; tell UAC-r2 the master SHA. File neutral follow-up: read-only Test-SignalWriterRoles.ps1 -Verify (not R1). Ontology accepted the admin dry-run label limit (K). 052 POML wording agreed verbatim (in 9159cfb376). |
| **040 after fix** | scoped re-check; UAC-r2 signed #2188 (wrapper not a precondition). Master push today is impersonated update-only (no live exposure, UAC-r2 agreed). 075 ships with 040. |
| **049b after build** | review (opus), send to UAC-r2, PR. Then re-measure 021 (the queued path is ~+2 s over NFR-02 regardless). |
| **Owner questions open** | (1) NFR-02: still ~+2 s over even queued: more speed-up (profile cache, FieldMapping concurrent) or amend window to "record opened"? (2) keep the Project to Matter profile on dev? (3) 046c dev apply go: G17 -Apply/-Verify -ChangesOnly, then -Remove -Gap R3 -Apply/-Verify (rollback -Restore -Gap R3). (4) drop stash da43b8a2 "lint-staged automatic backup" (22:38Z, left by an interrupted commit hook; content committed). |
| **Re-scope pending** | 032 (mapping step in persister; inputs notes/task-032-design-inputs.md; design to UAC-r2 + ebilling first), 030b (association in existing create + guards; send to word-add-in for content read), 034 (named exceptions + guards), 033 (likely dropped). |
| **Gotchas this session** | Pester 6.2 is the machine default: use `Import-Module Pester -MaximumVersion 5.99`. App Insights drops whole requests (#2158): count from client logs + Dataverse read-back. Commits during verify-suites can exceed 100 s (dotnet format hook): use a longer timeout. |

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

Master PR (030a, 046c) → 044 (after 2 security passes) → 049b → 040 (+075) → 021 deploy (after the owner's NFR-02 call) → 032/030b/034 re-scopes → 019, 033 (before 052), 050.
