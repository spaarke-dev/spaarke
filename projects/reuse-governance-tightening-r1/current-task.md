# Current Task — Reuse Governance Tightening R1

> State, not history (root CLAUDE.md §5). Rewrite at each checkpoint; keep ≤ 10 KB.
> Decisions live in `design.md` §11 and `notes/decisions.md`; narrative lives in the commit messages.

---

**Checkpoint 2026-11-12** (after task 020). Branch `work/reuse-governance-tightening-r1`, all committed and pushed. No PR open.

**Done (9/19; board synced 9)**: 001, 002, 041, 042 (2026-11-11); 010, 011, 012, 021, 020 (2026-11-12). 041/042 merged to master (#2705, #2706); master merged into the branch (`29bcda5fe3`).
- Items 1 (capability index), 2 (reuse scan: design-to-spec, pipeline fallback, task-create citation) and 3 (jscpd sweep + adversarial-reviewer checks) are complete on the branch.
- Task 020: weekly `duplication-sweep` job in `nightly-health.yml` (Mon 07:00 UTC cron, dispatch input `duplication_sweep_only`), `.jscpd.json`. Verified by dispatch runs 43289750101 and 43293619731: 38 s, artifact uploaded, 17 new / 192 old cross-area clusters, status failing in dry-run. The first master run after merge will open the `weekly/duplication-sweep` alert for those 17 (intended).

**Active task**: none. **Next**: propose the items 1–3 PR (NFR-05) — **owner approves before it is opened**. Then 030 (package decision records, owner gate; merge master first, check PR #935), 040 (Learn MCP; owner does the per-machine approval), 043, 080, 090.
**Open, reported to owner**
- ISS-001 #2683 — `node-executor-authoring.md` still names `ActionType` (≈15 min; offered to fix here).
- ISS-003 #2713 — fixed on branch (`65224ce73f`, power-page-deploy archived); closes when the branch reaches master.
- 042 known limits: S2 prose-only (K1); three integration-test projects set `TreatWarningsAsErrors=false` (K4, `notes/042-cs0618.md`).

**Registered**: Project Issue #2682 under Epic #427, Board #2 (item `PVTI_lAHODW0Pv84BEgWuzhHuX4E`, Tasks Completed field `PVTF_lAHODW0Pv84BEgWuzhWPlLY`), label `reuse-governance-tightening-r1`.

## Standing directives (owner)

- No PR unless the owner asks or approves. Never `git stash` (shared stack). Never commit secrets.
- OQ-2: if the reuse scan needs effort above `low`, show the owner the measured token/cost impact first.
- OQ-5 / NFR-10: the 3-package limit limits effort, never significance — every defect is fixed or reported by name.
- Do not silently re-adopt design.md §10 rejected alternatives or §10.7 ROI cuts.
- `.claude/` edits run in the main session only. DEV deploy (035) needs the owner's explicit OK.
- nightly-health changes: verify by `workflow_dispatch` on the branch (alerts dry-run off master).

## Gotchas

- `code-mapper` is read-only and fact-only; the orchestrator judges and writes `notes/reuse-scan.md`.
- Repo-wide scanners must exclude `.claude/worktrees/agent-*/`.
- Full `Sprk.Bff.Api.Tests` takes ~2 h here and times out host-based contract tests when other sessions load the machine (#1724). Verify failures by rerunning the affected classes in small chunks.
- `Test-InstructionPaths.ps1` scans `git ls-files` — stage a new file before checking it. Archived skills go to `.claude/archive/YYYY-MM-DD/` (the checker skips it), not `_archived/`.
- Pre-commit hook needs prettier: prepend `C:\code_files\spaarke\node_modules\.bin` to PATH; no `--no-verify`. Commit with an explicit pathspec and `git commit -F <file>`.
- `gh project item-list` can miss new items — resolve item ids via GraphQL `issue.projectItems`.
- Bash can hang on this machine — prefer PowerShell; never `python3` in Bash (use `py -3`).
- The advisory Tier 2 api-seam unit-test job times out (cancelled at 40 min) on many branches; not caused by this project.

## Context sources

- `design.md` §11 (decisions), `spec.md`, `plan.md`, `notes/decisions.md`, `notes/defer-issues.md`, `notes/reuse-scan-dry-run.md`.
- Auto-memory: `project_reuse-governance-tightening-r1.md` in the `C--code-files-spaarke` memory folder.
