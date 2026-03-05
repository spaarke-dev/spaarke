# Implementation Plan — Playbook & Analysis Launcher Page R1

> **Project**: `playbook-analysis-launcher-page-r1`
> **Created**: 2026-03-04
> **Status**: Active

---

## Architecture Context

### Discovered Resources

**ADRs**: ADR-001 (Minimal API), ADR-006 (PCF vs Code Pages), ADR-007 (SpeFileStore), ADR-008 (Endpoint Filters), ADR-012 (Shared Components), ADR-013 (AI Architecture), ADR-021 (Fluent v9/Dark Mode), ADR-022 (PCF Platform Libraries)

**Constraints**: `.claude/constraints/api.md`, `code-pages.md`, `pcf.md`, `fluent-ui.md`, `testing.md`, `dataverse.md`

**Patterns**: `.claude/patterns/pcf-control.md`, `code-page.md`, `endpoint-definition.md`, `fluent-ui-theming.md`, `dataverse-webapi.md`

**Guides**: `docs/guides/PCF-V9-PACKAGING.md`, `docs/guides/SPAARKE-AI-ARCHITECTURE.md`, `docs/guides/DATAVERSE-N-TO-N-RELATIONSHIPS.md`

**Scripts**: `Deploy-CustomPage.ps1`, `Deploy-PCFWebResources.ps1`, `Deploy-BffApi.ps1`

### Key Architectural Decisions

1. **Two shells, shared components** — Analysis Builder as standalone code page, Quick Start wizards embedded in workspace
2. **Shared source (Option C)** — Both consume Playbook library from same source tree via path aliases
3. **Reuse existing upload stack** — FileUploadZone → MultiFileUploadService → EntityCreationService
4. **App-level theming** — Not OS-level; localStorage → URL → navbar → system
5. **Config-driven wizards** — QuickStartWizardDialog accepts intent string, portable

---

## Phase Breakdown

### Phase 1: Shared Playbook Component Library (Foundation)

**Goal**: Extract and create the shared Playbook components that both experiences depend on.

**Deliverables**:
| # | Deliverable | Parallel Group |
|---|-------------|----------------|
| 1.1 | Playbook types & interfaces (`types.ts`) | A |
| 1.2 | PlaybookService — Dataverse WebAPI queries for all 5 playbook entities + N:N scopes | A |
| 1.3 | AnalysisService — Create analysis record + N:N associations | A |
| 1.4 | PlaybookCardGrid component — Card selector grid (from PCF PlaybookSelector) | B |
| 1.5 | ScopeList component — Generic checkbox/radio list (from PCF ScopeList) | B |
| 1.6 | ScopeConfigurator component — Tabbed scope config using ScopeList | B (after 1.5) |
| 1.7 | DocumentUploadStep — Wrapper wiring existing FileUploadZone + MultiFileUploadService | C |
| 1.8 | FollowUpActionsStep — Post-analysis action cards | C |
| 1.9 | Library barrel export (`index.ts`) | After all above |

**Parallel Groups**:
- **Group A** (Services + Types): Tasks 1.1, 1.2, 1.3 — independent, no UI dependencies
- **Group B** (UI Components): Tasks 1.4, 1.5, 1.6 — 1.6 depends on 1.5
- **Group C** (Wizard Steps): Tasks 1.7, 1.8 — independent of A and B

### Phase 2: Analysis Builder Code Page

**Goal**: Build the standalone code page for Document subgrid "+New Analysis".

**Deliverables**:
| # | Deliverable | Parallel Group |
|---|-------------|----------------|
| 2.1 | Code page scaffold — Vite + singlefile config, React 18 entry, ThemeProvider | D |
| 2.2 | AnalysisBuilderApp — 2-tab layout (Playbook | Custom Scope) using shared components | After 2.1 |
| 2.3 | Build pipeline verification — `npm run build` → `dist/analysisbuilder.html` | After 2.2 |
| 2.4 | Update command bar script — `openAnalysisBuilderDialog()` to use `pageType: "webresource"` | D |

**Parallel Groups**:
- **Group D**: Tasks 2.1 and 2.4 can run in parallel (scaffold vs command bar update)

### Phase 3: Quick Start Playbook Wizards (Workspace)

**Goal**: Build config-driven wizard dialogs for the 5 workspace action cards.

**Deliverables**:
| # | Deliverable | Parallel Group |
|---|-------------|----------------|
| 3.1 | QuickStartWizardDialog — Generic wizard shell for all cards | — |
| 3.2 | quickStartConfig.ts — Per-card step configurations for 5 intents | E (with 3.1) |
| 3.3 | Update ActionCardHandlers — Replace postMessage with wizard dialog state | After 3.1 |
| 3.4 | Update WorkspaceGrid — Wire QuickStartWizardDialog rendering | After 3.3 |
| 3.5 | Workspace rebuild + verification | After 3.4 |

### Phase 4: Integration Testing & Polish

**Goal**: End-to-end verification, dark mode, edge cases.

**Deliverables**:
| # | Deliverable | Parallel Group |
|---|-------------|----------------|
| 4.1 | Analysis Builder E2E — Open from Document form, create analysis, verify record | F |
| 4.2 | Quick Start wizard E2E — All 5 cards, upload → analyze → follow-up | F |
| 4.3 | Dark mode verification — Both experiences in light/dark/high-contrast | F |
| 4.4 | Portability check — QuickStartWizardDialog has no workspace-specific imports | F |
| 4.5 | Edge cases — No playbooks, WebAPI errors, offline, large file uploads | G |

**Parallel Groups**:
- **Group F**: Tasks 4.1, 4.2, 4.3, 4.4 — independent verification tracks
- **Group G**: Task 4.5 — after initial E2E passes

### Phase 5: Deployment & PCF Retirement

**Goal**: Deploy to Dataverse, retire the AnalysisBuilder PCF.

**Deliverables**:
| # | Deliverable |
|---|-------------|
| 5.1 | Deploy Analysis Builder code page to Dataverse |
| 5.2 | Deploy updated command bar script |
| 5.3 | Deploy updated Corporate Workspace |
| 5.4 | Verify both experiences in Dataverse environment |
| 5.5 | Retire AnalysisBuilder PCF — delete source, remove from solution, delete Custom Page |
| 5.6 | Project wrap-up — README status update, lessons learned |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 001, 002, 003 | None | Services + types — independent |
| B | 004, 005, 006 | None (006 after 005) | UI components — 006 depends on 005 |
| C | 007, 008 | None | Wizard step wrappers — independent |
| D | 020, 024 | Phase 1 complete | Code page scaffold + command bar update |
| E | 030, 031 | Phase 1 complete | Wizard dialog + config — can co-develop |
| F | 040, 041, 042, 043 | Phases 2-3 complete | Independent verification tracks |
| G | 044 | Group F | Edge case testing after E2E |

---

## Critical Path

```
Phase 1 (Playbook Library)
  ├── Group A: types + services (001, 002, 003)
  ├── Group B: UI components (004, 005 → 006)
  └── Group C: wizard steps (007, 008)
       ↓ (all Phase 1 complete)
  ┌────┴────┐
Phase 2    Phase 3  ← CAN RUN IN PARALLEL
  │           │
  ↓           ↓
Phase 4 (Integration Testing)
  ↓
Phase 5 (Deployment & Retirement)
```

**Longest path**: Phase 1 → Phase 2/3 (parallel) → Phase 4 → Phase 5

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|-----------|
| Upload service imports fail across solution boundaries | Medium | High | Verify import paths early in Task 007 |
| N:N association logic complex to port from PCF | Low | Medium | Direct translation from AnalysisBuilderApp.tsx |
| Vite workspace path aliases break singlefile build | Medium | Medium | Test in Task 2.1 scaffold before building full app |
| Command bar script update breaks existing flow | Low | High | Test independently before PCF retirement |

---

## References

- [Design Specification](spec.md)
- [Project README](README.md)
- [Task Index](tasks/TASK-INDEX.md)
- ADRs: `.claude/adr/ADR-{001,006,007,008,012,013,021,022}.md`
- Constraints: `.claude/constraints/{api,code-pages,pcf,fluent-ui,testing,dataverse}.md`
- Patterns: `.claude/patterns/{code-page,fluent-ui-theming,dataverse-webapi}.md`
