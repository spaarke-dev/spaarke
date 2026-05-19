---
🛑 STOP — THIS DOCUMENT IS FULLY DEPRECATED 🛑
═══════════════════════════════════════════════════════════════════════════
DELETED-IN-WAITING. This file is fully superseded by Spaarke Auth v2
and will be removed when v2 ships (Workstream F4, task 094).

Canonical v2 source: .claude/AUDIT-FINDINGS-AUTH-SYSTEM.md
ADR-027 will become canonical when v2 ships.

DO NOT use any pattern from this file in new code.
DO NOT cite this file in new POMLs, comments, or design docs.
DO NOT add `accessToken: string` props anywhere.
DO NOT write raw fetch() with `Authorization: Bearer ${...}` headers.
DO NOT reference BridgeStrategy, XrmStrategy, or window.__SPAARKE_BFF_TOKEN__.
DO use `authenticatedFetch()` from @spaarke/auth.
DO use `useAuth()` hook (after v2 ships).

What IS still canonical in this file: NOTHING — fully deprecated.
═══════════════════════════════════════════════════════════════════════════
---

# @spaarke/auth Initialization Pattern

> **Last Reviewed**: 2026-05-13
> **Status**: Current
>
> **Canonical reference for binding requirements + token chain**: [`spaarke-sso-binding.md`](spaarke-sso-binding.md). This file covers the *order of bootstrap calls*; that file covers *what to configure and why*.

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
