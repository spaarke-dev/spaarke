# WizardShell Extraction — AI Context

## Project Context

Extracting a reusable WizardShell from CreateMatter/WizardDialog.tsx in the LegalWorkspace solution. The shell handles dialog layout, step navigation, sidebar stepper, and footer buttons. Domain-specific logic (forms, services, file upload) stays in the consumer.

## Key Files

### Source (to extract from)
- `src/solutions/LegalWorkspace/src/components/CreateMatter/WizardDialog.tsx` — 885-line monolith
- `src/solutions/LegalWorkspace/src/components/CreateMatter/wizardTypes.ts` — mixed generic + domain types
- `src/solutions/LegalWorkspace/src/components/CreateMatter/WizardStepper.tsx` — already 100% generic
- `src/solutions/LegalWorkspace/src/components/CreateMatter/SuccessConfirmation.tsx` — to be replaced

### Target (new files)
- `src/solutions/LegalWorkspace/src/components/Wizard/` — new folder for shell components

## Applicable ADRs
- ADR-006: Anti-legacy-JS — standalone dialogs use Code Page pattern
- ADR-021: Fluent UI v9 — all UI uses semantic tokens, dark mode required

## Architecture Constraints
- React 18 (Code Page, not PCF)
- Fluent UI v9 only (makeStyles/Griffel)
- Zero hardcoded colors (semantic tokens throughout)
- Lazy-loadable via React.lazy()
