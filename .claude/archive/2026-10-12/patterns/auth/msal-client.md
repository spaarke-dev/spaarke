> **Archived 2026-10-12.** Both exemplars lived in the UniversalQuickCreate PCF, retired in `5b4cca898`; no control in `src/client/` defines a local `MsalAuthProvider` any more. Use `@spaarke/auth` — see `.claude/patterns/auth/spaarke-auth-initialization.md` and `spaarke-sso-binding.md`.

# MSAL Client Pattern (Legacy)

## When
Maintaining legacy PCF controls that use direct MSAL singleton for BFF API auth. New code should use `@spaarke/auth`.

## Read These Files
1. `src/client/pcf/UniversalQuickCreate/control/services/auth/MsalAuthProvider.ts` — Legacy MSAL singleton with token caching
2. `src/client/pcf/UniversalQuickCreate/control/services/auth/msalConfig.ts` — Scope and client ID config

## Constraints
- **ADR-006**: PCF controls use platform-provided React 16 — MSAL must be compatible
- MUST NOT use this pattern in new Code Pages — use `@spaarke/auth` instead (see spaarke-auth-initialization.md)

## Key Rules
- Legacy pattern: module-level `msalConfig` constant + `MsalAuthProvider` singleton
- New pattern: `@spaarke/auth` bootstrap (resolveRuntimeConfig → setRuntimeConfig → ensureAuthInitialized)
- See xrm-webapi-vs-bff-auth.md to decide if MSAL is even needed
