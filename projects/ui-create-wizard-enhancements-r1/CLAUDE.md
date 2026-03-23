# CLAUDE.md - UI Create Wizard Enhancements R1

> **Project**: UI Create Wizard Enhancements R1
> **Branch**: work/ui-create-wizard-enhancements-r1
> **Last Updated**: 2026-03-23

## Project Context

This project delivers 18 post-UAT enhancements to the create wizard and workspace ecosystem. Work spans shared library extensions, Code Page auth standardization, BFF API additions, wizard flow improvements, Code Page consolidation, theme/color compliance, and runtime bug fixes.

## Applicable ADRs

| ADR | Constraint | Relevance |
|-----|-----------|-----------|
| ADR-001 | Minimal API pattern | New `POST /api/ai/analysis/create` endpoint |
| ADR-006 | Code Pages for standalone dialogs | All wizards are Code Pages |
| ADR-008 | Endpoint filters for authorization | New BFF endpoint auth |
| ADR-010 | Concrete DI, <=15 registrations | New service registration |
| ADR-012 | Shared component library | WorkspaceShell, AssociateToStep, adapters |
| ADR-013 | AI extends BFF, not separate service | Analysis creation via BFF |
| ADR-021 | Fluent v9, tokens only, dark mode | Color tokens, theme cascade |
| ADR-022 | PCF React 16/17, Code Pages React 19 | React 19 upgrade for Code Pages |

## Key Files

### Shared Library (Layer 1)
- `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/` — NEW shared component
- `src/client/shared/Spaarke.UI.Components/src/components/AssociateToStep/` — NEW shared component
- `src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/` — Modified (new steps)
- `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/` — Modified (new steps)
- `src/client/shared/Spaarke.UI.Components/src/components/SummarizeFilesWizard/` — Modified (hideTitle, doc creation)
- `src/client/shared/Spaarke.UI.Components/src/components/Playbook/analysisService.ts` — Rewritten for BFF
- `src/client/shared/Spaarke.UI.Components/src/types/serviceInterfaces.ts` — openLookup() added
- `src/client/shared/Spaarke.UI.Components/src/utils/codePageTheme.ts` — Theme cascade fix
- `src/client/shared/Spaarke.UI.Components/src/utils/adapters/` — openLookup adapters

### Code Page Wrappers (Layer 2)
- `src/solutions/CreateMatterWizard/` — MSAL auth + bffBaseUrl
- `src/solutions/CreateProjectWizard/` — MSAL auth + bffBaseUrl
- `src/solutions/SummarizeFilesWizard/` — Document creation on follow-on
- `src/solutions/PlaybookLibrary/` — Merge AnalysisBuilder params + doc selector
- `src/solutions/AnalysisBuilder/` — RETIRE after migration
- All Code Pages — React 18 -> React 19 upgrade

### Consumers (Layer 3)
- `src/solutions/LegalWorkspace/` — Consume WorkspaceShell
- `src/client/webresources/js/sprk_wizard_commands.js` — Dialog size + bffBaseUrl + launch points
- SprkChat pane BFF client — Fix double `/api/` prefix

### BFF API (Layer 4)
- `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` — New analysis create endpoint
- `src/server/shared/Spaarke.Dataverse/IAnalysisDataverseService.cs` — Scope association method

## MUST Rules (From Spec)

- MUST use MSAL as single canonical auth path for Code Page -> BFF calls
- MUST use `@spaarke/ui-components` for all shared wizard/workspace components
- MUST use Fluent UI v9 exclusively (no v8)
- MUST support dark mode for all new/changed components
- MUST use endpoint filters for new BFF endpoints
- MUST use concrete types for DI registrations
- MUST NOT use Xrm.WebApi.online.execute from Code Page iframes
- MUST NOT use fetch.bind(window) for authenticated BFF calls
- MUST NOT use OS prefers-color-scheme as theme fallback
- MUST NOT use hard-coded hex/rgb/rgba colors in component styles

## Reference Implementations

- **MSAL Auth**: `src/solutions/DocumentUploadWizard/` (canonical pattern)
- **Associate To**: `src/solutions/DocumentUploadWizard/AssociateToStep.tsx`
- **Follow-on Steps**: `src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignment/`
- **Analysis Service**: `src/client/shared/Spaarke.UI.Components/src/components/Playbook/analysisService.ts`
- **Theme Utils**: `src/client/shared/Spaarke.UI.Components/src/utils/codePageTheme.ts`

## Relationship Schema

| Relationship | Schema Name |
|-------------|-------------|
| Matter <-> Project (N:N) | `sprk_Project_Matter_nn` |
| WorkAssignment -> Matter (N:1) | `sprk_workassignment_RegardingMatter_sprk_matter_n1` |
| WorkAssignment -> Project (N:1) | `sprk_workassignment_RegardingProject_sprk_project_n1` |
| Analysis -> Document (N:1) | `sprk_documentid` lookup |
| Analysis <-> Skill (N:N) | `sprk_analysis_skill` |
| Analysis <-> Knowledge (N:N) | `sprk_analysis_knowledge` |
| Analysis <-> Tool (N:N) | `sprk_analysis_tool` |

---

## Task Execution Protocol

**MANDATORY**: When executing tasks in this project, ALWAYS use the `task-execute` skill. Do NOT read POML files directly and implement manually.

Task execution trigger phrases: "work on task X", "continue", "next task", "keep going", "resume task X"

All tasks are in `projects/ui-create-wizard-enhancements-r1/tasks/` in POML format.

---

*Generated by Claude Code project-pipeline*
