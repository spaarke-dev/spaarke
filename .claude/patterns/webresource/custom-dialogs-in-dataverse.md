# Custom Dialogs in Dataverse Web Resources

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Use when a web resource needs a rich multi-option dialog (icons, styled buttons, custom layout) that `Xrm.Navigation.openConfirmDialog` cannot provide. For simple yes/no confirmations, use `Xrm.Navigation` directly.

## Read These Files
1. `src/client/webresources/js/sprk_DocumentOperations.js` — reference `showChoiceDialog()`: `window.top.document` pattern, ESC handler, cleanup
2. `src/solutions/LegalWorkspace/src/components/Wizard/WizardShell.tsx` — multi-step wizard component
3. `src/solutions/LegalWorkspace/src/components/Wizard/wizardShellTypes.ts` — `IWizardStepConfig`, `IWizardShellHandle`, `IWizardSuccessConfig`
4. `docs/adr/ADR-023-choice-dialog-pattern.md` — full design specs and accessibility requirements

## Constraints
- **ADR-023**: Choice dialogs: 2-4 options, icon + title + description per option, vertical stack, no auto-selection
- **ADR-006**: Custom dialogs only for rich UI; use `Xrm.Navigation` for simple alerts/confirms

## Key Rules
- ALL DOM operations must use `var targetDoc = window.top ? window.top.document : document` — dialogs appended to iframe body are invisible
- Always use inline `style.cssText` — CSS classes from the page will not exist in `window.top` context
- Use `z-index: 10000` or higher — Dataverse UI uses high z-index values
- Wrap `window.top` access in try/catch for cross-origin fallback to `Xrm.Navigation`
- Register `beforeunload` listener if cleanup is critical when user navigates away
- WizardShell auth: standalone wizard Code Pages MUST initialize `resolveRuntimeConfig()` + `initAuth()` before rendering
