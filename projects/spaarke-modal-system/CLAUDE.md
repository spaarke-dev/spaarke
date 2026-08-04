# Spaarke Modal System — AI Context

> **Purpose**: Context for Claude Code when working on `spaarke-modal-system`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning → Development (initialization complete 2026-08-01)
- **Current Task**: Not started
- **Next Action**: Run `task-execute` against task 001 (P0 — size scale tokens)

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI-optimized specification (18 FRs, 6 NFRs) — permanent reference
- [`design.md`](design.md) — human design document (prototype-validated 2026-07-31)
- [`README.md`](README.md) — project overview
- [`plan.md`](plan.md) — implementation plan (8 phases P0–P7 + P0.5)
- [`current-task.md`](current-task.md) — **active task state** (context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task tracker

### Project Metadata
- **Type**: Shared React component library (`@spaarke/ui-components`) + design-system docs + phased conversion
- **Complexity**: Large (8 phases, ~13 dialogs + 3 overlays converted) — but **client-only**
- **Prototype**: `c:/code_files/spaarke-prototype/projects/2026-07-sprk-modal-system/` — the visual contract to copy

### Hot-Path Declaration
- **BFF**: N — no `Sprk.Bff.Api` touch; client-only; **CLAUDE.md §10 NOT triggered** (zero publish-size impact)
- **SpaarkeAi**: Y — P0.5 (app-shell `--sprk-ui-scale` control) + P3 (`QuickStartModal` re-base) touch `src/solutions/SpaarkeAi/**`
- **CI workflows**: N
- **Skill directives**: N — but tasks 011/012/013/100 write `.claude/` (ADR-050, pattern pointer, INDEX) → **main-session only** (§3 boundary)
- **Root CLAUDE.md**: Y — task 013 adds ONE §17 pointer row for `MODAL-DESIGN-SYSTEM.md`

---

## Context Loading Rules

1. **Always load this file first** when starting any task.
2. **Check current-task.md** for active state (especially after compaction).
3. **Reference spec.md** for FRs/NFRs/acceptance; **plan.md** for phasing.
4. **Load the prototype** component matching your task — it is the visual contract (copy it, adapt to shared-lib conventions + dual-React compat).
5. **Apply ADRs**: 012, 021, 023, 028 (loaded via `adr-aware`).

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" / "next task" / "keep going" | Execute next pending 🔲 (check TASK-INDEX.md) |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

### Parallel Task Execution
Tasks in the same Parallel Group (TASK-INDEX.md) run via ONE message with MULTIPLE Skill(task-execute) calls. Max 6 agents/wave. **`.claude/`-touching tasks (011, 012, 013, 100) MUST run in the main session** (sub-agents cannot write `.claude/` — §3).

---

## Key Technical Constraints

### MUST Rules
- ✅ **MUST** compose existing primitives (`RecordNavigationModalShell`, `ModalWindowControls`, `OrientationToggle`) — do NOT create parallel abstractions (CLAUDE.md §11; net component count DECREASES).
- ✅ **MUST** keep the Fluent `Dialog` envelope (transform-robust portal).
- ✅ **MUST** realize `--sprk-ui-scale` via a **scaled Fluent theme** (multiply px tokens); MUST NOT use CSS `zoom`.
- ✅ **MUST** scroll body natively (thin scrollbar); MUST NOT add chevron scroll overlays for vertical content.
- ✅ **MUST** use semantic tokens only — zero hex, `'1px'` literals, inline color styles in modal components (strengthens ADR-021 / NFR-03).
- ✅ **MUST** compile clean under `@types/react` 18 (PCF) and React 19 (Code Pages) — NFR-04.
- ✅ **MUST** keep OOB `navigateTo` for full main-form editing; MUST NOT iframe-embed OOB `main.aspx`.

### MUST NOT Rules
- ❌ **MUST NOT** add any endpoint/service/DI to `src/server/api/Sprk.Bff.Api/**` — NFR-05 (client-only).
- ❌ **MUST NOT** hand-roll `position:fixed`/`createElement` overlays — retire the 3 that exist.
- ❌ **MUST NOT** fork `ModalWindowControls` / `RecordNavigationModalShell` — compose them.
- ❌ **MUST NOT** vary OOB record-open size per entity — 85%×85% is the standard (record-modal-selection.md).

### ADR Tensions in effect (per CLAUDE.md §6.5)
**None.** The project **strengthens** ADR-021 and **preserves** ADR-023 (`ChoiceModal` re-base). ADR-012/028 apply without exception. The one reconciliation is cosmetic (shipped `ModalWindowControls` icon `ArrowMaximize` → Dataverse `FullScreenMaximize/Minimize` glyph per owner review 2026-07-31 — same owner, refined decision, not an ADR conflict).

---

## Key Facts from Discovery (2026-08-01)

- **Prototype** (copy from): `src/components/{SprkModal,ModalWindowControls,ModalScrollArea,presets,sizes}.tsx` + `src/theme.ts` (`scaleTheme`). All presets live in ONE `presets.tsx`; **`ChoiceModal` is NOT in the prototype — build fresh** (re-base `ChoiceDialog`, ADR-023). **`BrowseModal` = `PreviewModal` + `nav` prop** in the prototype.
- **Shipped `ModalWindowControls`** currently uses `ArrowMaximize20Regular` — reconcile to `FullScreenMaximize20Regular`/`FullScreenMinimize20Regular` in task 003 (P1 mandate).
- **Hand-rolled overlays** (confirmed): `SprkChat/ActionConfirmationDialog.tsx` (`position:absolute` div), `CommunicationConversationPanel/.../ConversationModal.tsx` (`createPortal` + `position:fixed`, already uses `ModalWindowControls`), `sprk_DocumentOperations.js` (DOM in `window.top.document`).
- **Duplicate copies to fold in during conversion** (discovered — spec listed only the shared-lib copy):
  - `FindSimilarDialog` — 3 copies: `components/FindSimilarDialog/`, `components/FindSimilar/`, `src/solutions/LegalWorkspace/src/components/FindSimilar/`
  - `CloseProjectDialog` — 2 copies: `components/CreateProjectWizard/`, `src/solutions/LegalWorkspace/src/components/CreateProject/`
  - `navigation.ts` — `src/solutions/LegalWorkspace/` ≡ `src/solutions/SmartTodo/` (byte-identical, 80%×80%) → dedup in P7
  - `sprk_DocumentOperations.js` — `src/client/webresources/js/` + `infrastructure/dataverse/ribbon/DocumentRibbons/WebResources/`
- **Canonical `SendEmailDialog`** = `EmailComposer/wrappers/SendEmailDialog.tsx` (1040×70vh, wins the barrel name at index.ts line 213); legacy at `components/SendEmailDialog/` (retire in P3).
- **Barrel**: add `components/SprkModal/` (folder + `index.ts` re-export) + `export * from './SprkModal'` in `components/index.ts`. Use the named-export override pattern (line 213 precedent) only on collision.
- **OOB hubs**: `utils/adapters/xrmNavigationServiceAdapter.ts` (records/dialogs) + `WorkspaceShell/wizardLaunchers.ts` (wizards, 60%×70%).

---

## Resources

### Applicable ADRs
- [ADR-012 — Shared Components](../../.claude/adr/ADR-012-shared-components.md)
- [ADR-021 — Fluent Design System (v9 semantic tokens)](../../.claude/adr/ADR-021-fluent-design-system.md) — **strengthened here**
- [ADR-023 — Choice Dialog Pattern](../../.claude/adr/ADR-023-choice-dialog-pattern.md) — **preserved via `ChoiceModal`**
- [ADR-028 — Spaarke Auth Architecture](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — pass `authenticatedFetch` as a function
- **ADR-050 (NEW)** — Canonical Modal Shell (authored by task 011)

### Applicable Skills
- `fluent-v9-component` (CRITICAL), `ui-test`, `code-review`, `adr-check`, `adr-aware`, `spaarke-conventions`, `context-handoff`

### Applicable Patterns
- `.claude/patterns/ui/fluent-v9-component-authoring.md`, `fluent-v9-react-version-boundaries.md`, `fluent-v9-theming.md`, `fluent-v9-portal-gotcha.md`, `fluent-v9-host-visual-fit.md`
- `.claude/patterns/ui/record-modal-selection.md`, `choice-dialog-pattern.md`

### Guides & Standards
- `docs/guides/SHARED-UI-COMPONENTS-GUIDE.md`
- `docs/standards/MODAL-DECISION-CRITERIA.md` (decision layer — gets component-layer cross-link), `CODING-STANDARDS.md`, `ANTI-PATTERNS.md`, `TEST-ARCHITECTURE.md`

### Deferrals & Issues
File deferred/discovered work to BOTH `notes/defer-issues.md` and a GitHub Issue via `/project-defer-issue-tracking` (`/defer`). §11 rule: every entry names a concrete failing behavior.

---

*Keep this file updated throughout the project lifecycle.*
