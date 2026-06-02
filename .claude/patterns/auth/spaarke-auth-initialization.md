# @spaarke/auth Initialization Pattern

## When
Code Pages that call BFF API endpoints need MSAL auth bootstrap. NOT needed for Xrm.WebApi-only pages.

## Read These Files
1. `src/client/shared/Spaarke.Auth/src/index.ts` — Shared auth library exports (resolveRuntimeConfig, ensureAuthInitialized)
2. `src/client/shared/Spaarke.Auth/src/config.ts` — Runtime config getters (lazy functions, not module-level constants)
3. `src/solutions/LegalWorkspace/src/main.tsx` — Canonical bootstrap exemplar

## Constraints
- MUST use lazy functions for runtime config values — module-level `const X = getConfig()` throws before bootstrap
- MUST NOT add auth bootstrap to Code Pages that only use Xrm.WebApi (see xrm-webapi-vs-bff-auth.md)

## Key Rules
- Bootstrap order: `resolveRuntimeConfig()` → `setRuntimeConfig()` → `ensureAuthInitialized()` → `render`
- `resolveRuntimeConfig` reads from Dataverse environment variable or falls back to defaults
- `authenticatedFetch` wraps `fetch` with automatic token acquisition and refresh
- See xrm-webapi-vs-bff-auth.md to decide if this bootstrap is needed
