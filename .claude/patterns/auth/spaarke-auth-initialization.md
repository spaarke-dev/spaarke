# @spaarke/auth Initialization Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current (corrected export/ownership details)

## When
Code Pages that call BFF API endpoints need MSAL auth bootstrap. NOT needed for Xrm.WebApi-only pages.

## Read These Files
1. `src/client/shared/Spaarke.Auth/src/index.ts` — Shared library exports (`resolveRuntimeConfig`, `setRuntimeConfig`, `authenticatedFetch`, `SpaarkeAuthProvider`). NOTE: `ensureAuthInitialized` is defined per-solution, not in this library.
2. `src/client/shared/Spaarke.Auth/src/resolveRuntimeConfig.ts` — Resolves runtime config from Dataverse environment variables (7 variables including `sprk_BffApiBaseUrl`).
3. `src/solutions/LegalWorkspace/src/services/authInit.ts` — Example per-solution `ensureAuthInitialized` implementation.
4. `src/solutions/LegalWorkspace/src/main.tsx` — Canonical bootstrap exemplar showing the full sequence.

## Constraints
- MUST use lazy functions for runtime config values — module-level `const X = getConfig()` throws before bootstrap
- MUST NOT add auth bootstrap to Code Pages that only use Xrm.WebApi (see xrm-webapi-vs-bff-auth.md)

## Key Rules
- Bootstrap order: `resolveRuntimeConfig()` → `setRuntimeConfig()` → `ensureAuthInitialized()` → `render`
- `resolveRuntimeConfig` reads from Dataverse environment variable or falls back to defaults
- `authenticatedFetch` wraps `fetch` with automatic token acquisition and refresh
- See xrm-webapi-vs-bff-auth.md to decide if this bootstrap is needed
