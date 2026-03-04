# Reusable WizardShell Component — Specification

## Executive Summary

Extract a reusable `WizardShell` component from the existing "Create New Matter" wizard dialog (`CreateMatter/WizardDialog.tsx`). The current 885-line monolithic component mixes generic wizard infrastructure (dialog layout, step navigation, sidebar stepper, footer buttons, error/loading/success states) with matter-specific domain logic. The Playbook Library needs to support 6 additional "Create New..." wizards — all sharing the same shell pattern. This extraction eliminates ~600 lines of duplicated shell code per new wizard.

## Scope

### In Scope
- Extract generic `WizardShell` component with types, reducer, and success screen
- Refactor `CreateMatter/WizardDialog.tsx` to consume WizardShell (proving the API works)
- Update documentation: `.claude/patterns/webresource/custom-dialogs-in-dataverse.md` and `docs/architecture/SPAARKE-UX-MANAGEMENT.md`

### Out of Scope
- Implementing the other 6 wizards (Create Project, Assign Counsel, etc.) — those are separate projects
- Promoting components to `@spaarke/ui-components` shared library (future consideration)
- Parameterizing FileUploadZone/UploadedFileList (not needed for this extraction)

## Requirements

### Functional
1. WizardShell renders: Fluent v9 Dialog (1100px) + left sidebar stepper + right content area + footer (Cancel/Back/Next/Finish)
2. Step navigation via useReducer (NEXT_STEP, PREV_STEP, GO_TO_STEP)
3. Dynamic step injection/removal at runtime (ADD_DYNAMIC_STEP, REMOVE_DYNAMIC_STEP with canonical ordering)
4. Consumer provides step configs: `{ id, label, renderContent(handle), canAdvance(), isEarlyFinish?(), footerActions? }`
5. `onFinish` async handler: returns `IWizardSuccessConfig` (success screen) or void (close), throws on error (error bar)
6. Imperative handle via `React.forwardRef` for dynamic step operations from consumer effects
7. Create New Matter wizard must work identically after refactoring

### Non-Functional
- All Fluent UI v9 semantic tokens (dark mode compatible, zero hardcoded colors)
- makeStyles (Griffel) for all styles
- React 18 (Code Page, not PCF)
- Lazy-loadable via React.lazy()

## Technical Approach

### New Component: `components/Wizard/WizardShell`

**API:**
```tsx
<WizardShell
  ref={shellRef}                    // IWizardShellHandle
  open={open}                       // boolean
  title="Create New Matter"         // string
  steps={stepConfigs}               // IWizardStepConfig[]
  onClose={onClose}                 // () => void
  onFinish={handleFinish}           // () => Promise<IWizardSuccessConfig | void>
  finishingLabel="Creating..."      // optional string
  finishLabel="Finish"              // optional string
/>
```

**Consumer step config:**
```typescript
{
  id: 'create-record',
  label: 'Create record',
  canAdvance: () => formIsValid,                    // closure over domain state
  renderContent: (handle) => <CreateRecordStep />,  // domain component
  isEarlyFinish: () => false,                       // optional
}
```

**Dynamic steps via handle:**
```typescript
const shellRef = useRef<IWizardShellHandle>(null);
useEffect(() => {
  shellRef.current?.addDynamicStep(stepConfig, canonicalOrder);
}, [selectedActions]);
```

### Refactored CreateMatter (~280 lines, down from ~885)
- Removes: Dialog, styles, reducer, stepper, footer, error/loading chrome
- Keeps: domain state (files, form, contacts, email), domain effects, step configs array, handleFinish

## Success Criteria
1. TypeScript compiles with zero errors
2. Vite build produces `corporateworkspace.html`
3. Create New Matter wizard opens from workspace cards and Playbook Library
4. All steps work: file upload → form → next steps → dynamic follow-ons
5. Finish flow: spinner → success screen with "View Matter"
6. Error handling: blocked Next, error bar on failure
7. Back button works through dynamic steps
8. Dark mode correct
9. Documentation updated

## Key Files

| File | Role |
|------|------|
| `components/CreateMatter/WizardDialog.tsx` | Source to refactor (~885 lines → ~280) |
| `components/CreateMatter/wizardTypes.ts` | Types to split (generic → new, domain → keep) |
| `components/CreateMatter/WizardStepper.tsx` | Already 100% generic — reused by WizardShell |
| `components/CreateMatter/SuccessConfirmation.tsx` | Delete — replaced by WizardSuccessScreen |
| `.claude/patterns/webresource/custom-dialogs-in-dataverse.md` | Pattern documentation |
| `docs/architecture/SPAARKE-UX-MANAGEMENT.md` | Architecture documentation |
