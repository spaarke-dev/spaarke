# PCF Error Handling Pattern

## When
Adding error boundaries, user-facing error states, or error logging in PCF controls.

## Read These Files
1. `src/client/pcf/UniversalDatasetGrid/control/components/ErrorBoundary.tsx` — React error boundary component
2. `src/client/pcf/UniversalDatasetGrid/control/services/SdapApiClient.ts` — API error handling with retry

## Constraints
- **ADR-006**: PCF controls must show inline error states — never crash silently
- **ADR-012**: Reuse shared ErrorBoundary from `@spaarke/ui-components` when available

## Key Rules
- Wrap root component in ErrorBoundary — catches render errors, shows fallback UI
- API errors: try/catch with user-friendly messages, log context for debugging
- Never expose stack traces or internal error details to users
- Use `context.navigation.openErrorDialog()` only for critical unrecoverable errors
