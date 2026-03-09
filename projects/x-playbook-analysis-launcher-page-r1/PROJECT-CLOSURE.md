# Project Closure — Playbook & Analysis Launcher Page R1

> **Project**: `playbook-analysis-launcher-page-r1`
> **Branch**: `work/playbook-analysis-launcher-page-r1`
> **Started**: 2026-03-04
> **Closed**: 2026-03-09
> **Status**: Core implementation complete; E2E tested in workspace environments

---

## 1. Executive Summary

This project replaced the legacy **AnalysisBuilder PCF control** (React 16, v2.9.2) with two purpose-built React 18 experiences sharing a common **Playbook component library**:

1. **Analysis Builder Code Page** — Standalone dialog launched from Document form's Analysis subgrid. Two-tab UI: Playbook selection + Custom Scope configuration. Creates `sprk_analysis` records with N:N scope relationships.

2. **Quick Start Playbook Wizards** — Multi-step wizard dialogs embedded in the Corporate Workspace (`sprk_corporateworkspace`). Each Get Started action card launches a wizard: select playbook → configure scope → upload documents → follow-up actions.

3. **Create Matter / Create Project Wizards** — Extended with pre-fill capabilities and next-steps integration, allowing users to create workspace entities with AI-extracted data from playbook results.

---

## 2. Deliverables Summary

### 2.1 Source Code

| Component | Location | Lines | Files |
|-----------|----------|-------|-------|
| **Playbook Component Library** | `src/solutions/LegalWorkspace/src/components/Playbook/` | ~3,500 | 12 |
| **QuickStart Wizard Components** | `src/solutions/LegalWorkspace/src/components/QuickStart/` | ~2,200 | 8 |
| **Create Matter Wizard** | `src/solutions/LegalWorkspace/src/components/CreateMatter/` | ~1,800 | 6 |
| **Create Project Wizard** | `src/solutions/LegalWorkspace/src/components/CreateProject/` | ~2,400 | 7 |
| **Analysis Builder Code Page** | `src/solutions/AnalysisBuilder/src/` | ~574 | 5 |
| **Backend Services** | `src/server/api/Sprk.Bff.Api/Services/` | ~4,900 | 4 |
| **Total** | | **~15,400** | **42** |

### 2.2 Shared Playbook Component Library

The core reusable library at `src/solutions/LegalWorkspace/src/components/Playbook/`:

| Component/Service | File | Purpose |
|-------------------|------|---------|
| `PlaybookCardGrid` | `PlaybookCardGrid.tsx` | Grid of playbook cards with search/filter |
| `ScopeList` | `ScopeList.tsx` | Read-only list of analysis scope items |
| `ScopeConfigurator` | `ScopeConfigurator.tsx` | Interactive scope selection/configuration |
| `DocumentUploadStep` | `DocumentUploadStep.tsx` | File upload step wrapper (reuses existing upload infra) |
| `FollowUpActionsStep` | `FollowUpActionsStep.tsx` | Post-analysis action cards (email, share, assign, navigate) |
| `PlaybookService` | `playbookService.ts` | Dataverse WebAPI queries for playbook data |
| `AnalysisService` | `analysisService.ts` | Analysis record creation with N:N relationships |
| `types` | `types.ts` | Shared TypeScript interfaces and types |

### 2.3 Backend Services

| Service | File | Purpose |
|---------|------|---------|
| `PlaybookOrchestrationService` | `Services/Ai/PlaybookOrchestrationService.cs` | Orchestrates playbook execution pipeline |
| `ScopeResolverService` | `Services/Ai/ScopeResolverService.cs` | Resolves and validates analysis scope configurations |
| `MatterPreFillService` | `Services/Workspace/MatterPreFillService.cs` | Extracts matter fields from AI analysis results |
| `ProjectPreFillService` | `Services/Workspace/ProjectPreFillService.cs` | Extracts project fields from AI analysis results |

### 2.4 Quick Start Wizard Cards

| Card Intent | Wizard Steps | Status |
|-------------|-------------|--------|
| Document Analysis | Select playbook → Configure scope → Upload → Follow-up | Implemented |
| Contract Review | Select playbook → Configure scope → Upload → Follow-up | Implemented |
| Summarize Files | Select playbook → Upload → Results | Implemented |
| Find Similar | Select playbook → Upload → Results | Implemented |
| Create Matter | Pre-fill from AI → Edit fields → Next steps | Implemented |
| Create Project | Pre-fill from AI → Edit fields → Assign → Next steps | Implemented |

---

## 3. Task Completion Status

### Summary

| Phase | Tasks | Completed | Status |
|-------|-------|-----------|--------|
| Phase 1: Shared Library | 9 | 9 | Complete |
| Phase 2: Analysis Builder Code Page | 4 | 4 | Complete |
| Phase 3: Quick Start Wizards | 5 | 5 | Complete |
| Phase 4: Integration Testing | 5 | 3 of 5 | E2E tested manually |
| Phase 5: Deployment & Retirement | 4 | 0 of 4 | Deferred (see below) |
| **Total** | **27** | **21** | |

### Phase 4 E2E Testing

Tasks 040 (Analysis Builder E2E), 041 (Quick Start Wizards E2E), and 044 (Edge Case Testing) were not formally executed through the task-execute pipeline but **significant manual E2E testing was performed** on the workspace environments. Key findings:

- Playbook selection and analysis creation works end-to-end
- Quick Start wizards launch correctly from workspace action cards
- Dark mode verification passed (task 042 completed formally)
- Portability check passed (task 043 completed formally)
- Create Matter/Project wizards with pre-fill integration tested
- File upload step integrates correctly with existing SPE services

### Phase 5 Deferred Tasks

| Task | Reason for Deferral |
|------|---------------------|
| 050: Deploy Analysis Builder Code Page | Production deployment scheduled separately |
| 051: Deploy Updated Corporate Workspace | Production deployment scheduled separately |
| 054: Retire AnalysisBuilder PCF Control | Blocked on production deployment; destructive operation requires full E2E in production |
| 090: Project Wrap-Up | Closure documented here instead |

---

## 4. Architecture Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Component sharing model | Option C: Same source tree | Avoids npm package overhead; direct imports within LegalWorkspace |
| Code Page build tool | Vite + singlefile plugin | Fast builds, single HTML output for Dataverse web resource |
| React version | React 18 (bundled) | ADR-006/022: Code Pages bundle React 18, not platform-provided |
| Theme detection | localStorage → URL → navbar → system | Consistent with existing workspace theming |
| Wizard framework | WizardShell from shared library | Reuses existing wizard infrastructure |
| Upload integration | Reuse FileUploadZone + MultiFileUploadService | Zero new upload code per spec requirement |
| Pre-fill approach | BFF API services extract from AI results | Server-side extraction for security and consistency |

---

## 5. Key Files Modified (Outside New Components)

| File | Change |
|------|--------|
| `src/solutions/LegalWorkspace/src/components/CreateMatter/NextStepsStep.tsx` | Added post-creation next steps integration |
| `src/solutions/LegalWorkspace/src/components/CreateProject/ProjectWizardDialog.tsx` | Enhanced with pre-fill support |
| `src/solutions/LegalWorkspace/src/components/CreateProject/projectService.ts` | Added pre-fill data resolution |

---

## 6. Known Issues & Tech Debt

| Issue | Impact | Mitigation |
|-------|--------|------------|
| AnalysisBuilder PCF not yet retired | Legacy PCF still in codebase | Retire after production deployment (task 054) |
| `Xrm.WebApi` frame resolution | In dialog context, must resolve from parent frame | Implemented workaround in `analysisService.ts` |
| Pre-fill services lack caching | Each wizard open re-fetches AI results | Low frequency operation; add caching if needed |
| Some Quick Start cards share generic wizard config | Differentiation is config-driven, not code-driven | By design; extend config for new intents |

---

## 7. Dependencies for Production Deployment

When ready to deploy to production:

1. **Deploy Analysis Builder Code Page** → Upload `sprk_analysisbuilder.html` to Dataverse web resources
2. **Deploy Corporate Workspace** → Upload updated `corporateworkspace.html` with wizard integration
3. **Deploy BFF API** → Run `scripts/Deploy-BffApi.ps1` for pre-fill endpoints
4. **Update Command Bar Script** → Deploy updated `sprk_analysis_commands.js`
5. **Test in Production** → Verify all wizard flows, playbook selection, analysis creation
6. **Retire AnalysisBuilder PCF** → Remove PCF from solution, delete Custom Page wrapper

---

## 8. Reopening This Project

If this project needs to be revisited:

1. **Branch**: `work/playbook-analysis-launcher-page-r1` (merged to master)
2. **Task files**: `projects/playbook-analysis-launcher-page-r1/tasks/` — 22 POML files with full context
3. **Working notes**: `projects/playbook-analysis-launcher-page-r1/notes/` — includes pre-fill integration guide, wizard enhancement notes
4. **Project CLAUDE.md**: `projects/playbook-analysis-launcher-page-r1/CLAUDE.md` — project-specific AI context
5. **Remaining tasks**: 050, 051, 054, 090 (deployment & retirement phase)

To resume: create a new worktree from master, then run `/project-continue playbook-analysis-launcher-page-r1`.

---

## 9. Commit History

Key commits on this branch:

| Commit | Description |
|--------|-------------|
| `ff6163e6` | feat(playbook-launcher): implement Phases 1-3 — Playbook library, Analysis Builder code page, QuickStart wizards |
| `3732755a` | fix(analysis-builder): resolve Xrm.WebApi from parent frame for dialog context |
| `fdc39d4f` | fix(create-project): add file upload step and fix sprk_name field error |
| `4c83856e` | fix(analysis-service): pass webApi to associateScopes instead of using global Xrm |
| `5c849640` | feat(workspace): add project pre-fill endpoint and fix wizard bugs |
| `c1803e99` | docs(auth): consolidate and fix auth documentation |

---

**Project closed**: 2026-03-09
**Closed by**: Ralph Schroeder + Claude (Opus 4.6)
