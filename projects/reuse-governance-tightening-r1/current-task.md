# Current Task — Reuse Governance Tightening R1

> State, not history (root CLAUDE.md §5). Rewrite at each checkpoint; keep ≤ 10 KB.
> Decisions live in `design.md` §11 and `notes/decisions.md`; narrative lives in the commit messages.

---

**Phase**: execution. Wave 1 (001, 041, 042) **complete 2026-11-11**, all pushed to `origin/work/reuse-governance-tightening-r1`. No PR open.
**Active task**: none. Next per `tasks/TASK-INDEX.md`:
- **002** wire the capability index into task-create 3.5.6 / code-review 6.6 / root §11 (main session, `.claude`; depends on 001 ✅).
- **010** reuse-scan dry-run → owner go/no-go (owner gate).
- **021** reviewer F2/F4 checks (main session, `.claude`).
- **040** Learn MCP for the researcher (main session, `.claude`; `.mcp.json` needs per-machine approval by the user).
- **030** package decision records (owner gate). Merge master first and check PR #935 for ADR-012 / census conflicts.
- **020** (Group B) jscpd sweep is unblocked (001 ✅, 041 ✅). Check Dependabot PR #909 on `nightly-health.yml` first.

**Wave 1 results**
- 001 — `.claude/patterns/CAPABILITY-INDEX.md` (32 rows, every path/symbol verified; checker 0 findings; seeded dead path reported once). Proof: `notes/001-index-proof.md`.
- 041 — `nightly-health.yml` row `monthly/knowledge-refresh` (35-day limit). `workflow_dispatch` run 43058895412 succeeded; alert dry-run only. It is red today on purpose (newest REFRESH-LOG entry 2026-08-26).
- 042 — CS0618 re-armed. The only remaining suppression is `SPRK001` × 2 in `GenericAnalysisHandler.cs`. Publish-size delta +0.001 MB. Full BFF unit suite: 468 load timeouts (known #1724), all 165 affected classes pass in chunks. Details: `notes/042-cs0618.md`.

**PR plan (owner approves each)**: 041 and 042 each get their own PR (NFR-05, item 5). 001 ships with items 1–3 in one PR after 002/011/012/020/021.

**Open, reported to owner, not this project's to fix**
- ISS-001 #2683 — `node-executor-authoring.md` still names `ActionType` (≈15 min; offered to fix here).
- ISS-002 #2684 — `Test-InstructionPaths.ps1` exits 1 on master (2 false positives).
- 042 code-review K-class: S2 "never re-add CS0618" is prose only (guard cut per §10.7); S3 `knowledge/` / spike `.csproj`s now fail closed on obsolete calls.

**Registered**: Project Issue #2682 under Epic #427, Board #2, label `reuse-governance-tightening-r1`. Board "Tasks Completed" not yet synced for Wave 1 (`/devops-project-sync`).

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
- Full `Sprk.Bff.Api.Tests` takes ~2 h here and times out host-based contract tests when other sessions load the machine (#1724). Verify failures by rerunning the affected classes in small chunks (`--filter` of ~12 classes).
- `Test-InstructionPaths.ps1` scans `git ls-files` — stage a new file before checking it.
- Pre-commit hook needs prettier: prepend `C:\code_files\spaarke\node_modules\.bin` to PATH; no `--no-verify`. Commit with an explicit pathspec and `git commit -F <file>`.
- `gh project item-list` can miss new items — resolve item ids via GraphQL `issue.projectItems`.
- Bash can hang on this machine — prefer PowerShell; never `python3` in Bash.

## Context sources

- `design.md` §11 (decisions), `spec.md`, `plan.md`, `notes/decisions.md`, `notes/defer-issues.md`.
- Auto-memory: `project_reuse-governance-tightening-r1.md` in the `C--code-files-spaarke` memory folder.
