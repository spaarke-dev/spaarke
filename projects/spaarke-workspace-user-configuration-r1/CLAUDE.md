# CLAUDE.md - Workspace User Configuration R1

> **Project**: spaarke-workspace-user-configuration-r1
> **Last Updated**: 2026-03-29

## Project Context

User-personalized workspace dashboard layouts. Users create named workspaces by selecting layout templates, choosing sections from a registry, and arranging via drag-and-drop. Stored per-user in Dataverse (`sprk_workspacelayout`).

## Key Architecture

- **Section Registry**: Code-side array of `SectionRegistration` objects mapping section IDs to metadata + factory
- **SectionFactoryContext**: Standard contract passed to all section factories (webApi, userId, service, bffBaseUrl, onNavigate, onOpenWizard, onBadgeCountChange, onRefetchReady)
- **Dynamic Config Builder**: Merges stored layout JSON + registry factories -> `WorkspaceConfig` for `WorkspaceShell`
- **Layout Wizard**: Code Page (`sprk_workspacelayoutwizard`) — React 19, Vite, single-file build
- **WorkspaceShell**: Existing shared component — unchanged, consumes `WorkspaceConfig`

## Applicable ADRs

| ADR | Constraint | File |
|-----|-----------|------|
| ADR-001 | Minimal API for all endpoints | `.claude/adr/ADR-001-minimal-api.md` |
| ADR-006 | Wizard is Code Page, not PCF | `.claude/adr/ADR-006-pcf-over-webresources.md` |
| ADR-008 | Endpoint filters for auth | `.claude/adr/ADR-008-endpoint-filters.md` |
| ADR-010 | DI minimalism (<=15 registrations) | `.claude/adr/ADR-010-di-minimalism.md` |
| ADR-012 | Shared components via @spaarke/ui-components | `.claude/adr/ADR-012-shared-components.md` |
| ADR-021 | Fluent UI v9, dark mode, WCAG 2.1 AA | `.claude/adr/ADR-021-fluent-design-system.md` |
| ADR-026 | Vite + vite-plugin-singlefile, React 19 | `.claude/adr/ADR-026-full-page-custom-page-standard.md` |

## Key Files

| File | Purpose |
|------|---------|
| `src/client/shared/.../WorkspaceShell/types.ts` | WorkspaceConfig, SectionConfig, WorkspaceRowConfig types |
| `src/client/shared/.../WorkspaceShell/WorkspaceShell.tsx` | Shell component (unchanged) |
| `src/solutions/LegalWorkspace/src/workspaceConfig.tsx` | Current hardcoded config (to be replaced) |
| `src/server/api/Sprk.Bff.Api/Api/Documents/DocumentsEndpoints.cs` | Endpoint pattern reference |
| `src/server/api/Sprk.Bff.Api/Api/Filters/DocumentAuthorizationFilter.cs` | Filter pattern reference |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SpaarkeCore.cs` | DI registration pattern |
| `.claude/patterns/api/endpoint-definition.md` | Canonical endpoint structure |
| `.claude/patterns/webresource/full-page-custom-page.md` | Code Page project template |

## MUST Rules

- MUST store only section IDs and grid positions in Dataverse — no serialized JSX
- MUST gracefully handle missing section IDs (skip with console warning)
- MUST enforce max 10 user workspaces per user
- MUST NOT allow editing/deleting system workspaces
- MUST include `schemaVersion` in all sprk_sectionsjson JSON
- MUST cache layout in sessionStorage, invalidate on wizard save
- MUST use Fluent UI v9 exclusively with dark mode support
- MUST build wizard as single HTML via Vite + vite-plugin-singlefile

## Task Execution Protocol

All tasks in this project MUST be executed via the `task-execute` skill. See root CLAUDE.md for the mandatory task execution protocol.

Trigger phrases: "work on task X", "continue", "next task", "resume task X"

---

*Project-specific context for Claude Code. See [spec.md](spec.md) for full specification.*
