# Reuse Governance Tightening R1 — project operating manual

> Read with `current-task.md` (current state). Repo-wide rules are in root `CLAUDE.md`; this file holds only what is specific to this project. Guidance: `.claude/skills/project-setup/references/claudemd-template.md`.

## 1. Scope and status

Delivers: a hand-curated capability index (`.claude/patterns/CAPABILITY-INDEX.md`) wired into `task-create` 3.5.6 / `code-review` 6.6 / root §11; a dry-run-gated reuse scan in `/design-to-spec` with a `project-pipeline` fallback; a weekly `jscpd` sweep (exit 0, one quality alert) and F2/F4 checks in `adversarial-reviewer.md`; owner decisions on the 9 grandfathered `@spaarke/*` packages with execution of three (`ai-context`, `legal-workspace`, `document-operations`); and four tooling items (Learn MCP, knowledge-refresh watchdog, re-armed `CS0618`, C# LSP trial). Out: 5(b) TS strictness (deferred), any blocking duplication gate, the other six package actions, repo-wide type-aware ESLint, new BFF surface.

Status: see `tasks/TASK-INDEX.md` and `current-task.md`. Spec: `spec.md`. Design: `design.md`. Plan: `plan.md`.

## 2. Binding rules for this project

- Do not re-add anything in design.md §10 (rejected alternatives) or §10.7 (ROI-review cuts) without new evidence and an owner decision — no `CapabilityIndexTests`, no new `Validate-TaskPoml.ps1` warnings, no duplication baseline file, no delta re-scan, no cloud routine, no `SPRK00x` superseded-API process.
- The sweep and the watchdog never fail a build or PR (NFR-01). No scheduled agent runs (NFR-02).
- Every agent call names model and effort: `code-mapper` sonnet/low; one `adversarial-reviewer` (fable) pass over items 1–4 (NFR-03). Raising scan effort above `low` needs the measured token/cost impact shown to the owner first.
- Tasks that edit `.claude/` run in the main session, never in a sub-agent (NFR-04, root §3).
- PR shape (NFR-05): items 1–3 → one PR; item 4 package actions → own PR(s) with `code-review`; each item-5 sub-item → own PR with `code-review`.
- `code-mapper` is not edited and does not write files; the orchestrator judges and writes `reuse-scan.md` (FR-04).
- Index cells are verified with Grep/Glob before they are written; paths and ADRs are backticked repo paths; symbols in their own cell (FR-01).
- Item 4: owner approves each decision in `notes/package-reviews.md` before any execution, autonomous mode included (FR-08). Sanction = ADR-012 amendment + census row + `SharedPackageCensusTests` in the same PR (FR-09).
- The three-package limit limits effort, never significance: every defect found on a real path is fixed or named to the owner (NFR-10).
- 5(e): the blanket `CS0618` suppression never comes back; any remaining suppression is a named `DiagnosticId` with a one-line reason (FR-12). Measure publish-size delta if BFF files change (NFR-07).
- Learn MCP queries never contain customer data, tenant names or secrets (NFR-09).
- Deferred work and newly found issues: `/project-defer-issue-tracking` writes `notes/defer-issues.md` AND a GitHub issue — never one without the other.

ADR tensions approved for this project (root CLAUDE.md §6.5):
- ADR-012 — Path B — a package sanctioned by item 4 is added through an ADR-012 amendment merged with its census-table and `SharedPackageCensusTests` change (spec.md "ADR Tensions").

## 3. Owner directives and standing decisions

- 2026-11-10 — All OQ-1..OQ-16 decided; ROI review trims applied (design.md §11, §10.7).
- 2026-11-10 — SpaarkeAi hot-path = Y (item 4 touches `src/solutions/SpaarkeAi`).
- 2026-11-10 — Dry-run rule: both duplicates caught at `low` → go; one missed → owner sees `medium` cost and decides; both missed → no-go (tasks 011/012 not done).
- 2026-11-10 — Never `git stash` (shared stack). No PR until a task's PR step or the owner asks.
- Full log: `notes/decisions.md`.

## 4. Coordination

- `work/code-quality-and-assurance-r4` (PR #935) touches `.claude/adr/ADR-012-shared-components.md` and `tests/Spaarke.ArchTests/SharedPackageCensusTests.cs` — merge master before task 030 and check for conflicts before the item-4 PR.
- Dependabot PR #909 touches `.github/workflows/nightly-health.yml` — rebase-sensitive for tasks 020 and 041.
- SpaarkeAi hot path (12 active projects, incl. `spaarkeai-compose-r4`, `spaarke-dataset-grid-framework-r2` which created `@spaarke/legal-workspace`) — run `conflict-check` before tasks 031–033.
- Skill-directive hot path (many projects edit `task-create`, `code-review`, `design-to-spec`, `project-pipeline`, `task-execute`) — keep edits small and local to the cited steps; never renumber steps (external contract).

## 5. Environment and live actions

- No Dataverse or Azure changes. Task 035 deploys SpaarkeAi / SemanticSearch to DEV via `code-page-deploy` — needs the owner's explicit OK first.
- `nightly-health.yml` changes: verify with `workflow_dispatch` (alerts dry-run off master); never create real alert issues from a branch.
- Learn MCP in `.mcp.json` needs one-time per-machine approval by the user.

## 6. Gotchas — do not re-learn

- 2026-11-10 — `code-mapper` is read-only and fact-only; it cannot write `reuse-scan.md` or judge candidates.
- 2026-11-10 — Repo-wide scanners must exclude `.claude/worktrees/agent-*/` (full source copies → phantom duplication).
- 2026-11-10 — `Directory.Build.props:18` comment (14 `DemoProvisioningOptions` callers) is stale; that class has no `[Obsolete]` member. `src/` has 21 `[Obsolete]` in 15 files, 17 of them provisioning rejection-code constants — check for in-repo callers.
- 2026-11-10 — The `knowledge-refresh` watchdog fires on the day it lands (newest REFRESH-LOG entry 2026-08-26); that is intended.
- 2026-11-10 — Bash can hang on this machine; prefer PowerShell. Never `python3` in Bash. For commits use `git commit -F <file>` (here-string mis-parses).

## 7. Key documents

- `spec.md` · `design.md` · `plan.md` · `notes/decisions.md` · `notes/defer-issues.md`
- Applicable ADRs: `.claude/adr/ADR-012-shared-components.md`, ADR-022 (React 16/17 for PCF-consumed packages), `docs/adr/ADR-038-testing-strategy.md`
- Related projects: `code-quality-program-r3` / `code-quality-and-assurance-r4` (Epic #427), `spaarke-dataset-grid-framework-r2` (`legal-workspace` origin)
