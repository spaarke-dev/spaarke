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

## DEF-003 — FindSimilarDialog 3-copy consolidation deferred (name collision, 2 UX patterns, dead `embedded` prop)

| Field | Value |
|---|---|
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/714 |
| **Discovered** | 2026-08-02, P4 wave — task 061 (per its POML's explicit done-or-deferred criterion) |
| **Ownership** | Follow-on task; touches pcf-safe deep-import consumers (SemanticSearchControl, RelatedDocumentCount) |
| **Concrete failing behavior (drift risk)** | One name covers two unrelated UX patterns (iframe result-viewer vs WizardShell upload+search wizard) with no shared base/tests — task 061 itself needed 2 different re-basing techniques across 3 files for one visual goal. The viewer copy's `embedded` prop is dead code (zero live callers, grep-verified). |
| **Suggested follow-on** | Rename viewer → `FindSimilarViewerDialog` (+ consumer updates); audit the wizard copy's single-consumer shared-lib layer; wire-or-remove `embedded` |

---

## DEF-004 — LegalWorkspace `WorkAssignmentWizardDialog` forks the canonical WizardShell (pre-existing)

| Field | Value |
|---|---|
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/715 |
| **Discovered** | 2026-08-02, P6 — task 080 consumer-inventory adr-check (not caused/modified by this project) |
| **Ownership** | LegalWorkspace surface owner; follow-on task |
| **Concrete failing behavior** | **(Wording corrected at task 100 per the branch review gate)**: the file imports the CANONICAL `WizardShell` (verified — chrome updates DO reach it); what is duplicated is the `WorkAssignmentWizardDialog` ORCHESTRATOR/WRAPPER (business logic) vs the shared-lib equivalent — still an ADR-012 duplication risk, at the wrapper layer not the shell layer. It also carries a pre-existing undefined `navigationService` reference (~line 360, part of the LW tsc baseline / #712 noise) |
| **Suggested follow-on** | Replace the fork with the canonical `WizardShell` import (prop-map), or document the divergence requirement and extend the canonical shell; fix/remove the dead reference either way |

---

## DEF-005 — `sprk_DocumentOperations.js` pre-existing drift between the two copies

| Field | Value |
|---|---|
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/716 |
| **Discovered** | 2026-08-02, P7 — task 092 (its own conversion applied with zero new asymmetry; diff-of-diffs proof in `notes/task-092-completion.md`) |
| **Ownership** | DocumentRibbons surface owner; follow-on |
| **Concrete failing behavior** | 3 drift regions between the "byte-identical" copies (env-var fallback, BFF URL `/api`-suffix stripping, `sendToIndex` GUID-casing/error fields) — whichever copy deploys wins silently; any future one-way sync flips behaviors |
| **Suggested follow-on** | Reconcile the 3 regions to one intended behavior; add a sync check or single-source at build time |

---

## DEF-006 — Task-091 behavior deltas promoted at wrap-up (adr-check recommendation)

| Field | Value |
|---|---|
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/717 |
| **Discovered** | 2026-08-02, P7 task 091 (disclosed in `task-091-completion.md`); promoted to the ledger at task 100 per the branch adr-check gate |
| **Ownership** | LegalWorkspace surface owner + one-time visual review |
| **Concrete failing behavior** | (1) `FilePreviewDialog.tsx` record-open lost `openInNewWindow: true` (shared adapter has no such option) — now same-tab; (2) `MyPortfolioWidget.tsx` ×3 `openView` postMessages have NO receiver anywhere in the repo (orphaned dead affordance, preserved verbatim) |
| **Suggested follow-on** | If new-window is required, extend the shared adapter (no local helper re-pin); wire-or-remove the orphaned postMessages |

---

## Decision-pending (not deferred work — resolves inside this project)

- **030 escalation — legacy `SendEmailDialog` "v1.1.59 no-X" decision**: in-file comments document a 2026-07 UAT decision deliberately removing the title-bar × from `components/SendEmailDialog/SendEmailDialog.tsx`. Conflicts with the 2026-07-31 FR-12 mandate. Cluster NOT added (escalation per POML). **UPDATE 2026-08-02**: the planned resolver (P3/051 retirement) is itself DEFERRED (DEF-002 / Issue #713 — live DailyBriefingApp consumer), so this escalation **remains OPEN**: the legacy dialog stays live without the window-controls cluster until the follow-on retires it. Owner options unchanged: accept the interim gap (recommended — dialog is retirement-bound via #713) or direct the cluster be added to the legacy dialog despite v1.1.59. Evidence: `notes/task-030-completion.md` + `task-051-completion.md`.
