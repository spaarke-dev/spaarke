# Current Task — field-mapping-server-write-path-r1

> **Last updated**: 2026-11-07 ~02:00Z (main session checkpoint). Recovery: read Quick Recovery first.
> **Done on master**: 001–006, 010–018, 030a, 037–039, 042c, 043, 046, 046b (#2592), 046c repo side, 048, 049 (#2599), 070–074. #2610 merged at fb5738b60d (030a, 046c source, 046b guard fix).
> **Live on dev:** BFF + Console at master 20669df6a9. "Project to Matter (Attorney Matrix)" profile (keep/remove = owner question). 54 FMSWP-021 test matters (cleanup list in notes/task-021-live.md). 046c NOT applied on dev.
> **W3 direction:** named exceptions plus guards; no core redesign in R1 (decisions.md).

## Quick Recovery

| Field | Value |
|---|---|
| **Work branch** | = master fb5738b60d + the TASK-INDEX commit 304b6af504 (pushed). |
| **044** | Branch origin/task/fm-044-fix3 37946fc15c (rounds 1–3 on task/fm-044-fix → fix2 cdb7c30232 → fix3). Opus pass 1 and pass 2: no F. Ontology APPROVED round 3. Waiting: UAC-r2 OK (sent pass-2 result + fix3 tip). NEXT: squash-merge fix3 into the work branch, master PR, send the master SHA to ontology + UAC-r2. Follow-up filed: #2612 (live holder check, not R1). Known limits: per-Signal re-read before the lane check (cosmetic); ParseFetches doesn't require the core columns (Decide lane, pre-existing). |
| **049b** | origin/task/fm-049b aa552ce32c + origin/task/fm-049b-fix1 41ee8952a9 (a one-subject pass acts only on a share; Evaluated doc). Opus review: no F; UAC-r2's 4 conditions met. WAITING on UAC-r2: (a) accept that the maker case's enqueue-failure path relies on the reconciler (no invite for an external assignee), or (b) keep the full inline fallback there (one line in GrantAssignedAccessAsync). Then merge, PR, re-measure 021. |
| **040 + 075** | Combined on origin/task/fm-040-075 9daf41cdff (040 fix2 123f19783f + 075 ed1cf9b5d1 + legacy push helpers deleted, FamilyPage wires onAction to RecordFamilyActionPanel, deploy-script entry, lint fixes). Scoped Sonnet re-check rc-040-075 RUNNING. Then: edit the 040 POML (drop "a non-Filing kind is refused" at line 117 and the kinds constraint at line 53), merge into the work branch, send UAC-r2 the re-check result + merge SHA, master PR (close #2387 as superseded). Deploy order: sprk_recordfamily code page BEFORE sprk_fieldmapping_push.js v1.3.0 (task 076); the SpaarkeMaster web resource entry comes from the first dev export after deploy; K2 live check (failed save → Push Updates) at deploy. |
| **Owner questions open** | (1) NFR-02: still ~+2 s over even queued: more speed-up (profile cache, FieldMapping concurrent) or amend the window to "record opened"? (2) Keep the Project to Matter profile on dev? (3) 046c dev apply go: G17 -Apply/-Verify -ChangesOnly, then -Remove -Gap R3 -Apply/-Verify (rollback -Restore -Gap R3). (4) Drop stash da43b8a2 "lint-staged automatic backup" (content committed). (5) mcp-bff session can't be reached by message (permission mode): the owner relays "re-merge master fb5738b60d". |
| **Re-scope pending** | 032 (mapping step in persister; inputs notes/task-032-design-inputs.md; design to UAC-r2 + ebilling first), 030b (association in existing create + guards; send to word-add-in for content read), 034 (named exceptions + guards), 033 (likely dropped). |
| **Gotchas this session** | Pester 6.2 is the machine default: use `Import-Module Pester -MaximumVersion 5.99`. App Insights drops whole requests (#2158): count from client logs + Dataverse read-back. Commits can exceed 100 s (dotnet format hook): `timeout 400 git commit`. Don't push to the work branch while its master PR is open (auto-merge would take the commits). |

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

044 → 049b → 040 (+075) → 021 deploy (after the owner's NFR-02 call) → 032/030b/034 re-scopes → 019, 033 (before 052), 050.
