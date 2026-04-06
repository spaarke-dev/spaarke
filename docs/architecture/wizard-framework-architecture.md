# Wizard Framework Architecture

> **Last Updated**: 2026-04-05
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: New
> **Purpose**: Describes the shared, domain-free multi-step wizard dialog framework and all wizard implementations that consume it.

---

## Overview

The Wizard Framework is a generic, domain-free dialog shell that drives all multi-step wizard experiences in the Spaarke platform. The framework separates navigation mechanics (step progression, dynamic step insertion/removal, finish flow, success screen) from domain-specific content, which is injected via `renderContent` callbacks and `IWizardStepConfig` arrays. This separation follows ADR-012 (Shared Component Library) and ensures every wizard across the platform has a consistent layout: sidebar stepper, content area, and footer navigation.

The framework supports two rendering modes: standard (Fluent Dialog overlay) and embedded (fills a host container, e.g. a Dataverse dialog iframe opened via `navigateTo`). All styling uses Fluent UI v9 semantic tokens with zero hardcoded colors, satisfying ADR-021 (dark mode support).

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| WizardShell | `src/client/shared/Spaarke.UI.Components/src/components/Wizard/WizardShell.tsx` | Generic dialog shell: layout, navigation state via `useReducer`, imperative handle for dynamic steps, finish/success flow |
| WizardStepper | `src/client/shared/Spaarke.UI.Components/src/components/Wizard/WizardStepper.tsx` | Vertical sidebar step indicator with pending/active/completed visual states |
| WizardSuccessScreen | `src/client/shared/Spaarke.UI.Components/src/components/Wizard/WizardSuccessScreen.tsx` | Post-finish success screen with icon, title, body, optional warnings |
| wizardShellReducer | `src/client/shared/Spaarke.UI.Components/src/components/Wizard/wizardShellReducer.ts` | Pure reducer: NEXT_STEP, PREV_STEP, GO_TO_STEP, ADD_DYNAMIC_STEP, REMOVE_DYNAMIC_STEP |
| wizardShellTypes | `src/client/shared/Spaarke.UI.Components/src/components/Wizard/wizardShellTypes.ts` | All type definitions: IWizardShellProps, IWizardStepConfig, IWizardShellHandle, IWizardSuccessConfig |
| CreateRecordWizard | `src/client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/CreateRecordWizard.tsx` | Reusable multi-step wizard for creating Dataverse records (file upload + entity form + follow-on steps) |

## Wizard Implementations

Every wizard in the platform consumes `WizardShell` from the shared library. Implementations fall into two categories: standalone Code Page wizards (React 18, opened via `navigateTo`) and inline workspace wizards (rendered inside LegalWorkspace).

| Wizard | Path | Steps | Mode |
|--------|------|-------|------|
| Document Upload | `src/solutions/DocumentUploadWizard/` | Associate To (optional), Add Files, Processing, Next Steps | Code Page (embedded) |
| Create Matter | `src/solutions/CreateMatterWizard/` via `CreateRecordWizard` | Add Files, Enter Info, Next Steps, dynamic follow-on | Code Page (embedded) |
| Create Project | `src/solutions/CreateProjectWizard/` via `CreateRecordWizard` | Add Files, Enter Info, Next Steps, dynamic follow-on | Code Page (embedded) |
| Create Work Assignment | `src/solutions/CreateWorkAssignmentWizard/` via `CreateRecordWizard` | Add Files, Enter Info, Next Steps, dynamic follow-on | Code Page (embedded) |
| Create Todo | `src/solutions/CreateTodoWizard/` via `CreateRecordWizard` | Add Files, Enter Info, Next Steps, dynamic follow-on | Code Page (embedded) |
| Create Event | `src/solutions/CreateEventWizard/` via `CreateRecordWizard` | Add Files, Enter Info, Next Steps, dynamic follow-on | Code Page (embedded) |
| Summarize Files | `src/solutions/SummarizeFilesWizard/` | AI summarization steps | Code Page (embedded) |
| Workspace Layout | `src/solutions/WorkspaceLayoutWizard/` | Choose Layout, Select Components, Arrange Sections | Code Page (embedded) |
| SPE Admin Register | `src/solutions/SpeAdminApp/src/components/container-types/RegisterWizard.tsx` | Container type registration steps | Inline |

## Data Flow

1. Consumer defines an array of `IWizardStepConfig` objects, each with `id`, `label`, `renderContent`, and `canAdvance`
2. `WizardShell` initializes navigation state via `buildInitialShellState` (first step active, rest pending)
3. User interacts with step content; `canAdvance()` is evaluated on every render to enable/disable Next
4. On Next click: reducer dispatches `NEXT_STEP`, rebuilding step statuses (completed/active/pending)
5. Dynamic steps: step content calls `handle.addDynamicStep(config, canonicalOrder?)` to insert follow-on steps; `handle.removeDynamicStep(id)` to remove them. Canonical ordering ensures consistent step sequence
6. On last step (or `isEarlyFinish() === true`): Finish button triggers `onFinish()` async callback
7. If `onFinish` returns `IWizardSuccessConfig`, the success screen replaces step content; otherwise dialog closes
8. On error during finish: `MessageBar` with error text is displayed; user can retry

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Consumed by | All Code Page wizards | `WizardShell` component | Embedded mode, `hideTitle=true` |
| Consumed by | LegalWorkspace inline dialogs | `WizardShell` component | Standard dialog mode |
| Consumed by | CreateRecordWizard | `WizardShell` component | Abstraction layer for entity-creation wizards |
| Depends on | Fluent UI v9 | Dialog, Button, Text, Spinner, MessageBar | ADR-021 |
| Depends on | @spaarke/ui-components | Shared library barrel export | ADR-012 |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Domain-free shell | Zero domain imports in WizardShell | Reusable across all entity types without modification | ADR-012 |
| useReducer for navigation | Pure reducer with discriminated union actions | Predictable state transitions, easy to test | — |
| Imperative handle for dynamic steps | `React.forwardRef` + `useImperativeHandle` | Step content needs to add/remove steps without prop drilling | — |
| Canonical ordering for dynamic steps | Optional `string[]` in `addDynamicStep` | Ensures follow-on steps appear in consistent order regardless of selection sequence | — |
| Embedded vs standard mode | `embedded` prop toggles Dialog wrapper | Code Pages already render inside Dataverse dialog iframes; avoid double-Dialog nesting | ADR-006 |
| Ref-based stale closure prevention | Refs for state values used in renderContent callbacks | Step configs are memoized; refs ensure latest state is always read | — |

## Constraints

- **MUST**: Use Fluent UI v9 exclusively in all wizard components (ADR-021)
- **MUST**: Keep WizardShell domain-free; all domain logic belongs in consumer components
- **MUST**: Use `embedded=true` and `hideTitle=true` for Code Page wizards opened via `navigateTo`
- **MUST NOT**: Import domain-specific types (entity models, services) into the shared Wizard module
- **MUST NOT**: Hardcode colors; use semantic tokens from `@fluentui/react-components`
- **MUST**: Use `CreateRecordWizard` for entity-creation wizards to avoid duplicating file upload + follow-on step logic

## Known Pitfalls

- **Stale closures in renderContent**: Because step configs are memoized, any state values read inside `renderContent` must use refs (`.current`) to avoid capturing stale closures. The `configMapRef` sync during render (not in an effect) was specifically added to fix the AI pre-fill stale-closure bug
- **Dynamic step ordering**: Without `canonicalOrder`, dynamic steps append to the end. When multiple follow-on steps are toggled on/off in different orders, the step sequence becomes inconsistent. Always pass canonical ordering
- **Reset on open**: WizardShell resets its reducer state when `open` transitions from false to true. Consumer state (file uploads, form values) must also be reset in a matching `useEffect`

## Related

- [ADR-006](../../.claude/adr/ADR-006-pcf-over-webresources.md) — Code Pages for standalone dialogs
- [ADR-012](../../.claude/adr/ADR-012-shared-components.md) — Shared Component Library
- [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) — Fluent UI v9 Design System
