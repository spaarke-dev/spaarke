# PCF Control Initialization Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Creating or modifying a PCF control's lifecycle (init, updateView, destroy).

## Read These Files
1. `src/client/pcf/VisualHost/control/index.ts` — ReactControl exemplar (init / updateView / destroy lifecycle)
   <!-- Corrected 2026-09-01: was `UniversalDatasetGrid/control/index.ts`, a DELETED control. For
        list/dataset UX do NOT build a dataset PCF at all — use `<DataGrid configId=… />` from
        @spaarke/ui-components (docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md). Other live
        entry points if VisualHost is a poor fit: MatterHeader, RecordHeader, EmailProcessingMonitor. -->
2. `src/client/pcf/UniversalQuickCreate/control/index.ts` — Standard control exemplar (field-bound)

## Constraints
- **ADR-006**: Field-bound form controls → PCF; standalone dialogs → Code Page
- **ADR-022**: PCF uses React 16 APIs (platform-provided) — MUST NOT use React 18 `createRoot`
- **ADR-012**: Import shared components from `@spaarke/ui-components`

## Key Rules
- ReactControl: `updateView` returns `React.ReactElement` — wrap in `FluentProvider` with theme
- StandardControl: use `ReactDOM.render()` in `updateView`, `ReactDOM.unmountComponentAtNode` in `destroy`
- Always wrap root in `FluentProvider` with theme from `context.fluentDesignLanguage`
- Never bundle React — it's platform-provided via `pcf-scripts`
