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

# MSAL Client Pattern (DEPRECATED — Use `@spaarke/auth`)

> **Last Reviewed**: 2026-05-13
> **Status**: **Deprecated** — kept only as a reference for historical context
>
> **Canonical replacement**: [`spaarke-sso-binding.md`](spaarke-sso-binding.md) — the binding requirements + 6-strategy chain. All new code, and any rebuild of existing code, MUST use `@spaarke/auth` via that pattern.

## Why this pattern is deprecated

The legacy "direct MSAL singleton" pattern (a module-level `PublicClientApplication` + `msalConfig` constant + `acquireTokenPopup` fallback) was found in 2026-05-12 debugging to be the proximate cause of the "Pick an account" popup on every tab open. The defects in this pattern:

1. **Module-level config** — `msalConfig` runs at import time, before `Xrm.Utility.getGlobalContext().organizationSettings.tenantId` is populated. Forces hardcoded authority or `/organizations`, both of which break `ssoSilent`.
2. **Per-component singleton** — each PCF/Code Page creates its own MSAL instance. Tokens can't be shared with neighbors via `__spaarke_bff_token_cache__` or the parent-frame bridge. Every neighbor independently re-authenticates.
3. **`acquireTokenPopup` fallback** — fires the popup whenever silent fails, with no diagnostics to tell us WHICH silent attempt failed and why.

`SpaarkeAuthProvider` solves all three (singleton across components via `getAuthProvider()`, runtime-resolved tenant authority, 6-strategy chain with diagnostic logging).

## When you might still touch this pattern

Only when reading a legacy PCF source file that hasn't been migrated yet, to understand what it does before replacing it. The migration target is always the `@spaarke/auth` pattern.

## Migration path (when rebuilding a legacy PCF)

1. Remove `services/auth/MsalAuthProvider.ts` and `services/auth/msalConfig.ts`.
2. Add `@spaarke/auth` to `devDependencies` (`file:../../shared/Spaarke.Auth`).
3. Create `control/authInit.ts` calling `initAuth(config)` with the 5 required fields (clientId, redirectUri, bffApiScope, bffBaseUrl, proactiveRefresh). **Omit `authority`** — the library resolves it.
4. Move the auth init call from the PCF class's `init()` into the React component's `useEffect` (required for virtual ReactControl per ADR-022 + `/pcf-deploy` skill).
5. Replace direct `acquireTokenSilent/Popup` calls with `authenticatedFetch` from `@spaarke/auth`.

Reference exemplar: `src/client/pcf/SemanticSearchControl/SemanticSearchControl/authInit.ts` + `SemanticSearchControl.tsx`.

## See Also

- [`spaarke-sso-binding.md`](spaarke-sso-binding.md) — canonical binding + token chain
- [`DEPRECATED-spaarke-auth-initialization.md`](DEPRECATED-spaarke-auth-initialization.md) — bootstrap order (⛔ also deprecated)
- [`xrm-webapi-vs-bff-auth.md`](xrm-webapi-vs-bff-auth.md) — decide whether you need auth at all
