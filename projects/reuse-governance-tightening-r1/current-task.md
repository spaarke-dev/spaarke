# Current Task — Reuse Governance Tightening R1

> State, not history (root CLAUDE.md §5). Rewrite at each checkpoint; keep ≤ 10 KB.
> Decisions live in `design.md` §11 and `notes/decisions.md`; narrative lives in the commit messages.

---

**Checkpoint 2026-11-13** (after task 033; items 1–3 state below is from 2026-11-12). Branch `work/reuse-governance-tightening-r1`, all committed and pushed. **Items 1–3 merged to master: PR #2751 (`8c05f5b70f`, squash)**; master merged back, branch content = master.

**Done (9/19 as of 2026-11-12)**: 001, 002, 041, 042 (2026-11-11); 010, 011, 012, 021, 020 (2026-11-12). 041/042 merged to master (#2705, #2706); master merged into the branch (`29bcda5fe3`).
- Items 1 (capability index), 2 (reuse scan: design-to-spec, pipeline fallback, task-create citation) and 3 (jscpd sweep + adversarial-reviewer checks) are complete on the branch.
- Task 020: weekly `duplication-sweep` job in `nightly-health.yml` (Mon 07:00 UTC cron, dispatch input `duplication_sweep_only`), `.jscpd.json`. Verified by dispatch runs 43289750101 and 43293619731: 38 s, artifact uploaded, 17 new / 192 old cross-area clusters, status failing in dry-run. The first master run after merge will open the `weekly/duplication-sweep` alert for those 17 (intended).

**Done (10/19)**: + 030 (2026-11-13). Owner approved all 9 package decisions ("Approve all"); `notes/package-reviews.md` (findings P-1..P-14: 8 Medium, 6 Low, 0 High), `notes/decisions.md` row.
**Done (11/19)**: + 031 (2026-11-13). `@spaarke/ai-context` retired: types in ai-widgets `src/types/aiContext.ts`; census row + test entry removed; CI `client-typecheck`, deploy trigger, `Build-AllClientComponents.ps1`, `master-deploy` skill fixed. All builds/tests green (`notes/031-ai-context.md`). Item-4 changes stay on the branch — no PR yet (owner decides PR shape; merging auto-deploys SpaarkeAi to DEV).
**Done (12/19)**: + 032 (2026-11-13). `@spaarke/legal-workspace` facade retired; SpaarkeAi declares `"legal-workspace": "file:../LegalWorkspace"` (LegalWorkspace `exports` `./src/index.ts`); census updated; docs + code-page-deploy skill corrected (the dual-deploy trap was never "resolved" — covered by the deploy path trigger). `notes/032-legal-workspace.md`.
**Done (13/19)**: + 033 (2026-11-13). `@spaarke/document-operations` retired: `useDocumentActions` + test in ui-components `src/hooks` (main barrel, not pcf-safe); SemanticSearch (deep import), Compose, SpaarkeAi, LegalWorkspace re-pointed; CI compose gate, build script, client-lint list, CAPABILITY-INDEX fixed. PCF bundle unchanged. `notes/033-document-operations.md`.
**Active task**: none. **Next**: **034** — file the other six package actions + P-findings (P-1 as its own issue) + the pre-existing standalone-tsc errors (LegalWorkspace: `notes/032-legal-workspace.md`; SemanticSearch: `notes/033-document-operations.md`, both "Found in passing"); then 035 DEV deploy (owner OK), 040, 043, 080, 090.
- Worktree has node_modules/dist for all 12 shared libs, SpaarkeAi, LegalWorkspace, SemanticSearch, ScopeConfigEditor (PCF) and scripts/quality/client-lint.
**Open, reported to owner**
- ISS-001 #2683 — `node-executor-authoring.md` still names `ActionType` (≈15 min; offered to fix here).
- ISS-003 #2713 — done (PR #2751), closed.
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
