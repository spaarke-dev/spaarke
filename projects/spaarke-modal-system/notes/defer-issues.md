# Deferred Work & Discovered Issues — spaarke-modal-system

> Two-write rule: every entry here MUST have a GitHub Issue (project CLAUDE.md / §11).

---

## DEF-001 — LegalWorkspace fresh-worktree build broken: `@spaarke/ai-outputs` not declared

| Field | Value |
|---|---|
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/712 |
| **Discovered** | 2026-08-01, P1 wave — task 030 Code-Page consumer build verification |
| **Ownership** | OUT of spaarke-modal-system scope — LegalWorkspace / Spaarke.AI.Widgets surface owners |
| **Concrete failing behavior** | `npm run build` in `src/solutions/LegalWorkspace` fails in any fresh worktree: Rollup cannot resolve `@spaarke/ai-outputs/output-widgets/BudgetDashboardWidget` from `Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` (LegalWorkspace aliases `@spaarke/ai-widgets` to SOURCE; `@spaarke/ai-outputs` missing from LegalWorkspace `package.json`; SpaarkeAi declares it and builds fine) |
| **Impact on this project** | Task 030's Code-Page-consumer AC leg satisfied via SpaarkeAi instead (React 19, builds green). Later waves touching LegalWorkspace (060/061, 091) cannot use `npm run build` there as a verification gate until #712 is fixed — use `npx tsc --noEmit` scoped to touched files instead. |
| **Suggested fix (1 line, for surface owner)** | Add `"@spaarke/ai-outputs": "file:../../client/shared/Spaarke.AI.Outputs"` to LegalWorkspace dependencies (mirror SpaarkeAi) |

---

## Decision-pending (not deferred work — resolves inside this project)

- **030 escalation — legacy `SendEmailDialog` "v1.1.59 no-X" decision**: in-file comments document a 2026-07 UAT decision deliberately removing the title-bar × from `components/SendEmailDialog/SendEmailDialog.tsx`. Conflicts with the 2026-07-31 FR-12 mandate. Cluster NOT added (escalation per POML). Resolves at **P3 / task 051**, which retires this legacy dialog entirely (canonical EmailComposer wrapper already has the controls). Owner may accept the interim gap (recommended — dialog is retirement-bound) or direct a path-A exception note. Evidence: `notes/task-030-completion.md`.
