# PCF Error Handling Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

## When
Adding error boundaries, user-facing error states, or error logging in PCF controls.

## Read These Files
1. `src/client/shared/Spaarke.UI.Components/src/components/AppErrorBoundary/AppErrorBoundary.tsx` — app-root React error boundary
   (widget-scoped variant: `components/WidgetErrorBoundary/WidgetErrorBoundary.tsx`)
2. `src/client/shared/Spaarke.UI.Components/src/services/document-upload/SdapApiClient.ts` — API error handling with retry
   <!-- Corrected 2026-09-01: both pointers named `src/client/pcf/UniversalDatasetGrid/control/...`, a
        DELETED control. NOTE the exemplars moved LAYER, not just path: there is no ErrorBoundary anywhere
        under src/client/pcf/ — error boundaries live in the shared library and PCFs consume them. Do not
        re-add a per-control boundary. -->


## Constraints
- **ADR-006**: PCF controls must show inline error states — never crash silently
- **ADR-012**: Reuse shared ErrorBoundary from `@spaarke/ui-components` when available

## Key Rules
- Wrap root component in ErrorBoundary — catches render errors, shows fallback UI
- API errors: try/catch with user-friendly messages, log context for debugging
- Never expose stack traces or internal error details to users
- Use `context.navigation.openErrorDialog()` only for critical unrecoverable errors
