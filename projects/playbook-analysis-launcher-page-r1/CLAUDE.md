# CLAUDE.md — Playbook & Analysis Launcher Page R1

> Project-specific AI context for Claude Code task execution.

## Project Identity

| Field | Value |
|-------|-------|
| Project | `playbook-analysis-launcher-page-r1` |
| Branch | `work/playbook-analysis-launcher-page-r1` |
| Spec | `projects/playbook-analysis-launcher-page-r1/spec.md` |
| Plan | `projects/playbook-analysis-launcher-page-r1/plan.md` |

## Applicable ADRs

| ADR | Relevance |
|-----|-----------|
| ADR-001 | Minimal API + BackgroundService — BFF API pattern |
| ADR-006 | PCF for forms, Code Pages for dialogs — Analysis Builder is a Code Page |
| ADR-007 | SpeFileStore facade — upload services |
| ADR-008 | Endpoint filters for auth |
| ADR-012 | Shared component library — Playbook components reuse |
| ADR-013 | AI Architecture — AI Tool Framework, playbook model |
| ADR-021 | Fluent UI v9, dark mode, semantic tokens — MANDATORY |
| ADR-022 | PCF Platform Libraries — PCF uses React 16, Code Pages bundle React 18 |

## Key Constraints

- **Zero hardcoded colors** — all Fluent v9 semantic tokens (ADR-021)
- **No new upload code** — reuse FileUploadZone, MultiFileUploadService, EntityCreationService
- **No Dataverse schema changes** — reuse all 7 entities + 7 N:N relationships
- **App-level theming** — localStorage → URL → navbar → system preference
- **Portable Playbook Library** — no workspace-specific imports in QuickStart/ or Playbook/
- **Command bar reuse** — only update `openAnalysisBuilderDialog()` function

## Key Files

### Existing (Reuse)
- `src/client/webresources/js/sprk_analysis_commands.js` — Command bar launcher
- `src/client/code-pages/AnalysisWorkspace/launcher/sprk_AnalysisWorkspaceLauncher.js` — Reference pattern
- `src/solutions/LegalWorkspace/src/components/CreateMatter/FileUploadZone.tsx` — Upload UI
- `src/solutions/LegalWorkspace/src/components/CreateMatter/UploadedFileList.tsx` — File list
- `src/client/pcf/UniversalQuickCreate/control/services/MultiFileUploadService.ts` — Parallel upload
- `src/solutions/LegalWorkspace/src/services/EntityCreationService.ts` — Record creation
- `src/solutions/LegalWorkspace/src/providers/ThemeProvider.ts` — Theme resolution
- `src/solutions/LegalWorkspace/src/components/Wizard/WizardShell.tsx` — Wizard component
- `src/client/pcf/AnalysisBuilder/control/` — Current PCF (retire after code page deployed)

### New (Create)
- `src/solutions/LegalWorkspace/src/components/Playbook/` — Shared component library
- `src/solutions/LegalWorkspace/src/components/QuickStart/` — Workspace wizard dialogs
- `src/solutions/AnalysisBuilder/` — Standalone code page

## Dataverse Entities

| Entity | Purpose |
|--------|---------|
| `sprk_analysisplaybook` | Playbook templates |
| `sprk_analysisaction` | Actions (radio select) |
| `sprk_analysisskill` | Skills (multi-select) |
| `sprk_analysisknowledge` | Knowledge sources (multi-select) |
| `sprk_analysistool` | Tools (multi-select) |
| `sprk_analysis` | Created analysis record |
| `sprk_document` | Source document (lookup) |

## N:N Relationships

| Relationship | Entities |
|-------------|----------|
| `sprk_playbook_skill` | playbook → skill |
| `sprk_playbook_knowledge` | playbook → knowledge |
| `sprk_playbook_tool` | playbook → tool |
| `sprk_analysisplaybook_action` | playbook → action |
| `sprk_analysis_skill` | analysis → skill |
| `sprk_analysis_knowledge` | analysis → knowledge |
| `sprk_analysis_tool` | analysis → tool |

## Deployment Scripts

- `scripts/Deploy-CustomPage.ps1` — Code page deployment reference
- `scripts/Deploy-PCFWebResources.ps1` — PCF deployment (for retirement step)

## 🚨 MANDATORY: Task Execution Protocol

When executing tasks in this project, Claude Code MUST:
1. Use the `task-execute` skill — DO NOT read POML files and implement manually
2. Follow rigor level declared in each task (FULL/STANDARD/MINIMAL)
3. Checkpoint every 3 steps via `context-handoff`
4. Run quality gates (code-review + adr-check) at Step 9.5 for FULL rigor tasks
5. Update `current-task.md` after each step
