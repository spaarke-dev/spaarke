# UI Dialog & Shell Standardization - Implementation Plan

## Executive Summary

### Purpose

Extract all wizard/dialog components from the Corporate Workspace monolith into standalone, reusable Code Pages backed by shared library components. Introduce PlaybookLibraryShell as a new reusable shell. Enable wizard reuse from entity forms, command bars, and Power Pages SPA.

### Scope

- 7 wizard extractions (CreateMatter, CreateProject, CreateEvent, CreateTodo, CreateWorkAssignment, SummarizeFiles, FindSimilar)
- 1 new shared shell (PlaybookLibraryShell from AnalysisBuilder)
- 1 refactored existing Code Page (DocumentUploadWizard webpack → Vite)
- 8+ new Code Page wrappers
- Corporate Workspace restructuring
- Power Pages SPA integration
- Entity form ribbon/command bar wiring

### Estimated Effort

25-35 development days across 5 phases.

---

## Architecture Context

### Three-Layer Component Architecture

```
Layer 1: @spaarke/ui-components (shared library)
  ├── Service interfaces (IDataService, IUploadService, INavigationService)
  ├── Shell components (WizardShell, PlaybookLibraryShell)
  ├── Generic wizards (CreateRecordWizard)
  ├── Domain wizards (CreateMatterWizard, CreateProjectWizard, etc.)
  └── Shared utilities (detectTheme, parseDataParams)

Layer 2: Code Page wrappers (src/solutions/{WizardName}/)
  ├── main.tsx (~30-50 LOC: parse params, init auth, render shared component)
  ├── index.html (Vite entry)
  ├── vite.config.ts (singlefile plugin)
  └── package.json (React 19, Fluent v9)

Layer 3: Consumers
  ├── Corporate Workspace (navigateTo calls)
  ├── Entity main forms (ribbon → navigateTo)
  └── Power Pages SPA (direct import, own Dialog)
```

### Key Technical Decisions

| Decision | Rationale |
|----------|-----------|
| IDataService abstraction in Phase 1 | ADR-012 requires portable services; prerequisite for shared library |
| Vite + vite-plugin-singlefile for all Code Pages | ADR-026 standard; migrate DocumentUploadWizard from webpack |
| React 19 for all Code Pages | ADR-021 standard; backward compatible with existing React 18 code |
| PlaybookLibraryShell from AnalysisBuilder | More efficient than building from scratch; ~508 LOC to extract |
| WizardShell for QuickStart (no separate shell) | Avoid over-engineering; QuickStart = playbook with preset intent |
| navigateTo Promise for post-dialog refresh | Sufficient for data refresh; BroadcastChannel only if needed later |

### Governing ADRs

| ADR | Key Constraint |
|-----|----------------|
| ADR-006 | Code Pages are default for new UI; PCF only for form binding |
| ADR-012 | Shared library with IDataService abstraction; no direct Xrm.WebApi in components |
| ADR-021 | Fluent v9 tokens only; React 19 for Code Pages; dark mode required |
| ADR-022 | PCF stays React 16/17; shared library peerDeps >=16.14.0 |
| ADR-026 | Vite + vite-plugin-singlefile; single HTML output |

### Discovered Resources

| Type | Resource | Path |
|------|----------|------|
| Pattern | Full-page Code Page template | `.claude/patterns/webresource/full-page-custom-page.md` |
| Pattern | Custom dialogs in Dataverse | `.claude/patterns/webresource/custom-dialogs-in-dataverse.md` |
| Pattern | Dialog patterns (navigateTo) | `.claude/patterns/pcf/dialog-patterns.md` |
| Pattern | Theme management | `.claude/patterns/pcf/theme-management.md` |
| Skill | code-page-deploy | `.claude/skills/code-page-deploy/SKILL.md` |
| Skill | dataverse-deploy | `.claude/skills/dataverse-deploy/SKILL.md` |
| Reference | EventsPage (canonical Code Page) | `src/solutions/EventsPage/` |
| Reference | AnalysisBuilder (PlaybookLibraryShell source) | `src/solutions/AnalysisBuilder/src/App.tsx` |
| Reference | DocumentUploadWizard (existing wizard Code Page) | `src/solutions/DocumentUploadWizard/` |
| Script | Deploy-EventsPage.ps1 | `scripts/Deploy-EventsPage.ps1` |
| Guide | Shared UI Components Guide | `docs/guides/SHARED-UI-COMPONENTS-GUIDE.md` |

---

## WBS (Work Breakdown Structure)

### Phase 1: Foundation — Service Abstraction + First Wizard Extraction

**Objectives**: Establish the IDataService pattern, extract CreateMatter and CreateProject as the first two standalone Code Pages, prove the end-to-end pattern.

**Deliverables**:

1.1 **Service interface definitions** — Define `IDataService`, `IUploadService`, `INavigationService` in `@spaarke/ui-components/types/`

1.2 **Xrm.WebApi adapter** — Create concrete `IDataService` implementation using Xrm.WebApi for Code Page consumers

1.3 **Mock adapter** — Create mock `IDataService` for unit testing

1.4 **Shared utilities** — Extract `detectTheme()` and `parseDataParams()` into `@spaarke/ui-components/utils/`

1.5 **Extract CreateMatterWizard** — Move 15 files from `LegalWorkspace/components/CreateMatter/` to `@spaarke/ui-components/components/CreateMatterWizard/`. Refactor services to accept `IDataService`.

1.6 **CreateMatterWizard Code Page** — Create `src/solutions/CreateMatterWizard/` with Vite build → `sprk_creatematterwizard`

1.7 **Extract CreateProjectWizard** — Move 7+ files from `LegalWorkspace/components/CreateProject/`. Refactor services.

1.8 **CreateProjectWizard Code Page** — Create `src/solutions/CreateProjectWizard/` → `sprk_createprojectwizard`

1.9 **Update Corporate Workspace (partial)** — Replace inline CreateMatter and CreateProject dialogs with `navigateTo` calls. Verify post-dialog refresh.

1.10 **Deploy and verify** — Deploy `sprk_creatematterwizard` and `sprk_createprojectwizard` to Dataverse. Set display names. Test from workspace.

**Dependencies**: None (foundational phase)
**Outputs**: Working IDataService pattern, 2 standalone wizard Code Pages, Corporate Workspace partially restructured

---

### Phase 2: Complete Wizard Extraction

**Objectives**: Extract all remaining wizards. Migrate DocumentUploadWizard to Vite. Complete Corporate Workspace restructuring.

**Deliverables**:

2.1 **Extract CreateEventWizard** — Move 4 files, refactor to IDataService, create Code Page wrapper

2.2 **Extract CreateTodoWizard** — Move 4 files, refactor, create Code Page wrapper

2.3 **Extract CreateWorkAssignmentWizard** — Move 9 files (uses WizardShell directly, not CreateRecordWizard), refactor, create Code Page wrapper

2.4 **Extract SummarizeFilesWizard** — Move 8 files, refactor, create Code Page wrapper

2.5 **Extract FindSimilarDialog** — Move 4 files, refactor, create Code Page wrapper

2.6 **Migrate DocumentUploadWizard to Vite** — Replace webpack config with Vite + vite-plugin-singlefile. Extract shared content to library if not already done.

2.7 **Complete Corporate Workspace restructuring** — Remove all remaining inline dialog imports and state. All wizards now via navigateTo. Target WorkspaceGrid < 500 LOC.

2.8 **Deploy and verify all wizards** — Deploy all Code Pages. Set display names. Test from workspace.

**Dependencies**: Phase 1 complete (IDataService pattern established, shared utilities ready)
**Outputs**: All 7 wizards + DocumentUpload as standalone Code Pages, Corporate Workspace simplified

---

### Phase 3: PlaybookLibraryShell

**Objectives**: Extract AnalysisBuilder core into reusable PlaybookLibraryShell. Absorb QuickStart intents. Create standalone Code Page.

**Deliverables**:

3.1 **Extract PlaybookLibraryShell** — Move AnalysisBuilder `App.tsx` core (~508 LOC) to `@spaarke/ui-components/components/PlaybookLibraryShell/`. Decompose into: PlaybookLibraryShell (main), PlaybookBrowser (tab 1), CustomScopeBuilder (tab 2), ExecutionLauncher.

3.2 **Intent pre-selection support** — Add props for pre-selected playbook + locked scope (replaces QuickStart hardcoded configs). Validate against existing QuickStart configs (`email-compose`, `assign-counsel`, `meeting-schedule`).

3.3 **Refactor AnalysisBuilder** — Replace `App.tsx` with thin wrapper around PlaybookLibraryShell (~30-50 LOC). Verify `sprk_analysisbuilder` still works identically.

3.4 **PlaybookLibrary Code Page** — Create `src/solutions/PlaybookLibrary/` → `sprk_playbooklibrary` (or unify with `sprk_analysisbuilder` via URL params)

3.5 **Update Corporate Workspace playbook cards** — Get Started action cards for playbook intents use navigateTo to open PlaybookLibraryShell Code Page with intent params.

3.6 **Refactor QuickStart** — Remove `QuickStartWizardDialog.tsx` and `quickStartConfig.ts`. QuickStart intents now route through PlaybookLibraryShell with pre-selection.

3.7 **Deploy and verify** — Deploy PlaybookLibraryShell Code Page. Test from workspace cards, from standalone, and from AnalysisBuilder.

**Dependencies**: Phase 1 complete (IDataService, shared utilities). Phase 2 not strictly required but recommended.
**Outputs**: PlaybookLibraryShell as shared component, AnalysisBuilder refactored, QuickStart simplified

---

### Phase 4: Power Pages SPA Integration

**Objectives**: Enable the Power Pages external SPA to use shared wizard and playbook components.

**Deliverables**:

4.1 **BFF API adapter for IDataService** — Create `IDataService` implementation using authenticated BFF API fetch (for SPA context where Xrm.WebApi is unavailable)

4.2 **Document Upload in SPA** — Import DocumentUploadWizard from `@spaarke/ui-components`, render in SPA's own Fluent Dialog (no Dataverse chrome)

4.3 **PlaybookLibraryShell in SPA** — Embed with external-user-appropriate playbook filter. Configure allowed playbook subset.

4.4 **SPA theme integration** — Ensure shared components respect SPA theme provider (not Dataverse theme detection)

4.5 **Test and verify** — End-to-end testing of wizards in SPA context

**Dependencies**: Phase 1 (IDataService), Phase 3 (PlaybookLibraryShell)
**Outputs**: SPA can render Document Upload and Playbook Library using shared components

---

### Phase 5: Ribbon / Command Bar Wiring

**Objectives**: Enable all extracted wizards to be launched from entity main form command bars.

**Deliverables**:

5.1 **Create sprk_wizard_commands.js** — JS webresource with launch functions for each wizard (navigateTo calls with entity context params)

5.2 **Matter form ribbon** — Add `+ New` / `Create Matter`, `Upload Document`, `Run Playbook` buttons to `sprk_matter` main form via RibbonDiffXml

5.3 **Project form ribbon** — Add `Create Project` button to `sprk_project` main form

5.4 **Event form ribbon** — Add `Create Event`, `Create To Do` buttons to `sprk_event` main form

5.5 **Deploy ribbon customizations** — Export/import ribbon XML via ribbon-edit skill

5.6 **Test command bar integration** — Verify each button opens the correct wizard with entity context

**Dependencies**: Phase 2 (all wizard Code Pages deployed)
**Outputs**: All entity forms can launch wizards from command bars

---

### Phase 6: Wrap-Up

6.1 **Update documentation** — Update SHARED-UI-COMPONENTS-GUIDE.md with new component inventory and Code Page wrapper pattern

6.2 **Final testing** — End-to-end verification across all surfaces (workspace, entity forms, SPA)

6.3 **Lessons learned** — Document architectural decisions and patterns established

6.4 **Project wrap-up** — Update README status, run `/repo-cleanup`

---

## Dependencies

### External Dependencies

- Dataverse environment for webresource deployment and testing
- PAC CLI for solution import
- Azure CLI for authentication

### Internal Dependencies

| Dependency | Required By | Status |
|-----------|-------------|--------|
| `@spaarke/ui-components` | All phases | Exists (extending) |
| `@spaarke/auth` | Code Page auth | Exists |
| WizardShell | All wizard Code Pages | Exists (no changes) |
| CreateRecordWizard | CreateMatter/Project/Event/Todo | Exists (no changes) |
| Playbook shared components | PlaybookLibraryShell | Exists (PlaybookCardGrid, ScopeConfigurator) |

---

## Testing Strategy

### Unit Testing

- All shared library components: Jest + React Testing Library
- Mock IDataService adapter for all wizard tests
- Minimum 90% coverage on new shared components (ADR-012)

### Integration Testing

- Each Code Page: verify renders without errors in browser
- navigateTo pattern: verify dialog opens and closes correctly
- Post-dialog refresh: verify data grids update after wizard completion

### Acceptance Testing

- Visual comparison: all wizard dialogs show consistent Dataverse chrome
- Cross-context: same wizard works from workspace, entity form, and SPA
- Dark mode: all Code Pages render correctly in dark mode

---

## Risk Register

| Risk | Impact | Probability | Mitigation |
|------|--------|------------|------------|
| Wizard open latency (separate bundle) | Users notice delay | Medium | Small bundles (<100KB), browser caching |
| Cross-dialog state loss | Refetch flashes | Low | navigateTo Promise + targeted refetch |
| Service abstraction complexity | Over-engineering | Low | Keep IDataService minimal (5 methods) |
| Bundle duplication across Code Pages | Total download increase | Medium | vite-plugin-singlefile inlines; cached independently |
| PlaybookLibraryShell scope creep | Delays Phase 3 | Medium | Start with AnalysisBuilder parity only |
| Ribbon XML complexity | Deployment friction | Low | Use ribbon-edit skill |

---

## Acceptance Criteria

1. [ ] All 7 create/tool wizards launchable from workspace AND entity forms
2. [ ] Consistent Dataverse modal chrome across all dialogs
3. [ ] Zero wizard step components remaining in LegalWorkspace
4. [ ] PlaybookLibraryShell replaces AnalysisBuilder core
5. [ ] WorkspaceGrid.tsx < 500 LOC
6. [ ] DocumentUploadWizard on Vite (no webpack)
7. [ ] Power Pages SPA renders Upload Documents and PlaybookLibrary
8. [ ] Zero direct webApi calls in shared library (grep verification)
9. [ ] Clean display names in Dataverse modal title bars

---

## Next Steps

1. Run `/task-create` to decompose this plan into executable task files
2. Create feature branch: `feature/ui-dialog-shell-standardization`
3. Begin Phase 1 with service interface definitions

---

*Plan created: 2026-03-19*
