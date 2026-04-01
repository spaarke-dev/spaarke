# Code Page Wizard Wrapper Pattern

## When
Use when a shared wizard/dialog component from `@spaarke/ui-components` must be opened as a standalone Dataverse modal via `Xrm.Navigation.navigateTo`. Do NOT use when the component only renders inline within a PCF control.

## Read These Files
1. `src/solutions/CreateMatterWizard/src/main.tsx` — canonical wrapper: theme, params, service adapters, `open={true}` + `embedded={true}`
2. `src/solutions/CreateMatterWizard/vite.config.ts` — required resolve aliases for `@spaarke/ui-components` and Fluent UI
3. `src/solutions/CreateMatterWizard/index.html` — CSS reset for Dataverse iframe context
4. `.claude/patterns/auth/spaarke-auth-initialization.md` — auth bootstrap required before BFF calls

## Constraints
- **ADR-006**: Standalone dialog surfaces MUST be Code Pages, not PCF controls
- **ADR-012**: Deep-import from `@spaarke/ui-components` (not barrel) to avoid Lexical/RichTextEditor bloat
- **ADR-022**: Code Pages use React 18 `createRoot`; never share PCF's platform-provided React 16

## Key Rules
- Always pass `open={true}` and `embedded={true}` — Code Page is the dialog chrome
- Use `resolveCodePageTheme()` + `setupCodePageThemeListener()`, not PCF theme detection
- Call `navigationService.closeDialog()` for `onClose` — closes the Dataverse navigateTo modal
- Auth: wizard Code Pages MUST call `resolveRuntimeConfig()` + `initAuth()` before rendering; NEVER `fetch.bind(window)` for BFF calls
- `vite.config.ts` resolve aliases are mandatory — build fails without them
