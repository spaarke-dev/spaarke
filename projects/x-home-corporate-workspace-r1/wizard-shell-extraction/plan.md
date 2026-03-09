# WizardShell Extraction — Implementation Plan

## Phase 1: Shell Foundation (T1-T5)

Create the generic wizard shell components with zero domain coupling.

| Task | Deliverable | Deps |
|------|-------------|------|
| T1 | `Wizard/wizardShellTypes.ts` — IWizardShellProps, IWizardStepConfig, IWizardShellHandle, IWizardSuccessConfig | None |
| T2 | `Wizard/wizardShellReducer.ts` — pure reducer + buildInitialShellState | T1 |
| T3 | `Wizard/WizardSuccessScreen.tsx` — generic success layout | T1 |
| T4 | `Wizard/WizardShell.tsx` — reusable shell component | T1-T3 |
| T5 | `Wizard/index.ts` — barrel exports | T1-T4 |

## Phase 2: Refactor CreateMatter (T6-T8)

Refactor CreateMatter/WizardDialog.tsx to consume WizardShell, proving the API.

| Task | Deliverable | Deps |
|------|-------------|------|
| T6 | Refactor WizardDialog.tsx (~885 → ~280 lines) | T4 |
| T7 | Clean up wizardTypes.ts — remove migrated generic types | T6 |
| T8 | Delete SuccessConfirmation.tsx — replaced by WizardSuccessScreen | T6 |

## Phase 3: Documentation (T9-T10)

| Task | Deliverable | Deps |
|------|-------------|------|
| T9 | Update `.claude/patterns/webresource/custom-dialogs-in-dataverse.md` | T4 |
| T10 | Update `docs/architecture/SPAARKE-UX-MANAGEMENT.md` | T4 |

## Phase 4: Verify (T11-T12)

| Task | Deliverable | Deps |
|------|-------------|------|
| T11 | Build — zero TypeScript errors | T6-T10 |
| T12 | Functional verification — Create New Matter wizard works identically | T11 |
