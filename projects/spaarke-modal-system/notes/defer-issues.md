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

## DEF-002 — Task 051 deferred: SendEmailDialog FormModal re-base + legacy retirement blocked

| Field | Value |
|---|---|
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/713 |
| **Discovered** | 2026-08-02, P3 wave — task 051 escalation (zero source changes; full evidence in `notes/task-051-completion.md`) |
| **Ownership** | Follow-on project/task; needs EmailComposer surface-owner + DailyBriefing owner coordination |
| **Concrete failing behavior (why blocked)** | (1) `EmailComposer mount="dialog"` is SELF-CHROMED (own header incl. ModalWindowControls + own `ComposerActionBar` footer, no suppression prop) — any SprkModal-based wrap double-renders chrome; `mount="inline"` kills the action bar (forking it is forbidden). (2) Legacy `components/SendEmailDialog/` has two live consumers: `pcf-safe.ts:27-28` deep-exports it (zero actual PCF imports — dead surface) and `DailyBriefingApp.tsx` uses the legacy `onSend` API with two bespoke send flows that don't map to the canonical engine. |
| **Interim mitigation shipped (this project)** | `maxHeight: 720px` cap added to the wrapper → numerically identical to `md`; wrapper already had `modalType="alert"` + ModalWindowControls → **FR-14 satisfied in substance** |
| **Carried consequence** | The P1 "v1.1.59 no-X" escalation on the LEGACY dialog remains OPEN until retirement lands (see Decision-pending below) |

---

## Decision-pending (not deferred work — resolves inside this project)

- **030 escalation — legacy `SendEmailDialog` "v1.1.59 no-X" decision**: in-file comments document a 2026-07 UAT decision deliberately removing the title-bar × from `components/SendEmailDialog/SendEmailDialog.tsx`. Conflicts with the 2026-07-31 FR-12 mandate. Cluster NOT added (escalation per POML). **UPDATE 2026-08-02**: the planned resolver (P3/051 retirement) is itself DEFERRED (DEF-002 / Issue #713 — live DailyBriefingApp consumer), so this escalation **remains OPEN**: the legacy dialog stays live without the window-controls cluster until the follow-on retires it. Owner options unchanged: accept the interim gap (recommended — dialog is retirement-bound via #713) or direct the cluster be added to the legacy dialog despite v1.1.59. Evidence: `notes/task-030-completion.md` + `task-051-completion.md`.
