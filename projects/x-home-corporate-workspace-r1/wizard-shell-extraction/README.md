# WizardShell Extraction

## Status: In Progress

## Overview

Extract a reusable `WizardShell` component from the existing "Create New Matter" wizard dialog. The Playbook Library needs to support multiple "Create New..." wizards sharing the same dialog shell pattern (sidebar stepper + content area + footer navigation).

## Scope

- Extract generic wizard shell (types, reducer, component, success screen)
- Refactor CreateMatter/WizardDialog.tsx to consume the new shell
- Update pattern and architecture documentation

## Key Deliverables

| Deliverable | Status |
|-------------|--------|
| `components/Wizard/WizardShell.tsx` | Pending |
| `components/Wizard/wizardShellTypes.ts` | Pending |
| `components/Wizard/wizardShellReducer.ts` | Pending |
| `components/Wizard/WizardSuccessScreen.tsx` | Pending |
| Refactored `CreateMatter/WizardDialog.tsx` | Pending |
| Documentation updates | Pending |

## Graduation Criteria

1. TypeScript compiles with zero errors
2. Create New Matter wizard works identically after refactoring
3. WizardShell API supports: static steps, dynamic steps, success screen, error handling
4. Documentation updated in patterns and architecture docs
