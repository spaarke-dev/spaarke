# CLAUDE.md — Dark Mode Theme R2

## Project Context

This project fixes inconsistent light/dark mode by unifying theme utilities, removing OS preference fallback, deploying ribbons to all entities, and adding Dataverse persistence.

## Applicable ADRs

- **ADR-021**: Fluent v9, semantic tokens, dark mode support mandatory, Spaarke theme system controls all surfaces (NOT OS)
- **ADR-012**: Shared component library — theme utilities MUST live in `@spaarke/ui-components`
- **ADR-006**: Web resources minimal (invocation only) — `sprk_ThemeMenu.js`
- **ADR-022**: PCF platform libraries — React 16 APIs, deep imports for shared lib

## Key Constraints

- MUST NOT consult OS `prefers-color-scheme` for theme resolution
- MUST NOT inline theme detection logic — import from shared library
- MUST use localStorage key `spaarke-theme` exclusively (no variants)
- MUST use unified priority chain: localStorage → PCF context → navbar → light default
- Office Add-ins are intentional exception (sessionStorage + Office.context)

## Critical Files

| File | Role |
|------|------|
| `src/client/shared/Spaarke.UI.Components/src/utils/themeStorage.ts` | **Primary** — unified theme utility (modify) |
| `src/client/shared/Spaarke.UI.Components/src/utils/codePageTheme.ts` | **Delete** — merge into themeStorage |
| `src/client/webresources/js/sprk_ThemeMenu.js` | Ribbon handler (remove OS listener) |
| `infrastructure/dataverse/ribbon/ThemeMenuRibbons/` | Ribbon XML for all entities |

## Existing Patterns

- `DataverseService.getUserPreference()` / `setUserPreference()` — reuse for theme persistence
- `infrastructure/dataverse/ribbon/ThemeMenuRibbons/Other/Customizations.xml` — ribbon XML pattern per entity

## 🚨 MANDATORY: Task Execution Protocol

When executing tasks for this project, Claude Code MUST invoke the `task-execute` skill. DO NOT read POML files directly and implement manually. See root CLAUDE.md for full protocol.
