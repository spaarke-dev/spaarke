# Current Task — Reuse Governance Tightening R1

> State, not history (root CLAUDE.md §5). Rewrite at each checkpoint; keep ≤ 10 KB.
> Decisions live in `design.md` §11 and `notes/decisions.md`; narrative lives in the commit messages.

---

**Checkpoint 2026-11-15** (after task 035). Branch `work/reuse-governance-tightening-r1`. Items 1–3 merged to master (PR #2751). Item-4 changes (031–034) and 040 are on the branch only — no PR yet (owner decides the item-4 PR shape; merging SpaarkeAi to master auto-deploys it to DEV).

**Done (15/19)**: 001, 002, 010, 011, 012, 020, 021, 030, 031, 032, 033, 034, 035, 041, 042.
- 030–034: package reviews approved ("Approve all"); `@spaarke/ai-context`, `@spaarke/legal-workspace`, `@spaarke/document-operations` retired; six remaining actions filed as DEF-001..006 (#2801–#2806), ISS-004 #2807, ISS-005 #2808.
- 035 (2026-11-15): SpaarkeAi + SemanticSearch deployed to DEV; all four POML ui-tests pass (Open in web, Assistant context, Worklist/registry, SemanticSearch open + Download, dark mode); Delete not run by owner choice; no hotfix. `notes/035-smoke.md`.

**Active task: 040 (in progress, STANDARD)** — Learn MCP for the researcher. Committed + pushed in `cb613e3098` (`.mcp.json` `microsoft-learn` http server; `researcher.md` tool allow-list + research order + privacy line; `knowledge/README.md` approval note). Endpoint confirmed: no auth, streamable HTTP, three tools; escalation trigger not fired. Known limit (K): tool names may change → allow-list update.
- NEXT for 040: the owner approves the `microsoft-learn` project MCP server on this machine (it loads at session start — open a new session in this worktree, or run `claude` here and approve in `/mcp`). Then: one `researcher` run whose answer cites a Learn MCP result → `notes/040-learn-mcp.md` → POML completed + TASK-INDEX ✅ → board 16 → owner OK → own PR from a temp worktree off origin/master + cherry-pick `cb613e3098` (only 040's files).

**Remaining after 040**: 043 (C# LSP A/B trial — needs the operator), 080 (one adversarial review over items 1–4, then the item-4 PR with owner OK and owner-chosen shape), 090 (wrap-up incl. `/test-diet`).

**Open, reported to owner**
- ISS-001 #2683 — `node-executor-authoring.md` still names `ActionType` (≈15 min; offered to fix here).
- 042 known limits: S2 prose-only (K1); three integration-test projects set `TreatWarningsAsErrors=false` (K4, `notes/042-cs0618.md`).
- DEV data (not code): Navigator recent items (`sprk_navitems`) point at deleted records → 404 console noise on Console load.

**Registered**: Project Issue #2682 under Epic #427, Board #2 (item `PVTI_lAHODW0Pv84BEgWuzhHuX4E`, Tasks Completed field `PVTF_lAHODW0Pv84BEgWuzhWPlLY`), label `reuse-governance-tightening-r1`.

## Standing directives (owner)

- No PR unless the owner asks or approves. Never `git stash` (shared stack). Never commit secrets.
- OQ-2: if the reuse scan needs effort above `low`, show the owner the measured token/cost impact first.
- OQ-5 / NFR-10: the 3-package limit limits effort, never significance — every defect is fixed or reported by name.
- Do not silently re-adopt design.md §10 rejected alternatives or §10.7 ROI cuts.
- `.claude/` edits run in the main session only. DEV only; any further DEV deploy needs the owner's explicit OK.
- Learn MCP queries never contain customer data, tenant names or secrets.
- nightly-health changes: verify by `workflow_dispatch` on the branch (alerts dry-run off master).

## Gotchas

- `code-mapper` is read-only and fact-only; the orchestrator judges and writes `notes/reuse-scan.md`.
- Repo-wide scanners must exclude `.claude/worktrees/agent-*/`.
- Full `Sprk.Bff.Api.Tests` takes ~2 h here and times out host-based contract tests when other sessions load the machine (#1724). Verify failures by rerunning the affected classes in small chunks.
- `Test-InstructionPaths.ps1` scans `git ls-files` — stage a new file before checking it. Archived skills go to `.claude/archive/YYYY-MM-DD/`.
- Pre-commit hook needs prettier: prepend `C:\code_files\spaarke\node_modules\.bin` to PATH; no `--no-verify`. Commit with an explicit pathspec and `git commit -F <file>`. If prettier reformats a staged file, `git reset -q -- <path>` clears the stale index entry.
- In-app browser DEV sign-in: choose **Work or school account** (the personal account with the same address fails with AADSTS50020).
- `gh project item-list` can miss new items — resolve item ids via GraphQL `issue.projectItems`.
- Bash can hang on this machine — prefer PowerShell; never `python3` in Bash (use `py -3`).

## Context sources

- `design.md` §11 (decisions), `spec.md`, `plan.md`, `notes/decisions.md`, `notes/defer-issues.md`, `notes/package-reviews.md`, `notes/035-smoke.md`.
- Auto-memory: `project_reuse-governance-tightening-r1.md` in the `C--code-files-spaarke` memory folder.
