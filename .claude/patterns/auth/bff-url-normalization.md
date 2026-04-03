# BFF Base URL Normalization Pattern

> **Entry Point**: `src/client/shared/Spaarke.Auth/src/resolveRuntimeConfig.ts:388` (Code Pages)
> **Entry Point**: `src/client/pcf/shared/utils/environmentVariables.ts:getApiBaseUrl()` (PCF Controls)
> **Full Reference**: `docs/architecture/AUTH-AND-BFF-URL-PATTERN.md`
> **Constraint**: `.claude/constraints/auth.md` → "BFF Base URL Convention"

## The Rule

```
bffBaseUrl = HOST ONLY → "https://spe-api-dev-67e2xz.azurewebsites.net"
fetch URL  = `${bffBaseUrl}/api/your/endpoint`
```

## Why

Dataverse env var `sprk_BffApiBaseUrl` = `"https://host/api"` (WITH `/api`).
Two normalization functions strip it:
- `normalizeUrl()` in `@spaarke/auth/src/resolveRuntimeConfig.ts:388`
- `normalizeBffUrl()` in `src/client/pcf/shared/utils/environmentVariables.ts`

Both use: `.replace(/\/+$/, '').replace(/\/api$/i, '')`

## When Writing New BFF API Code

1. Get base URL from `getBffBaseUrl()` or `getApiBaseUrl()` — ALREADY normalized
2. Construct: `${baseUrl}/api/...` — you add the `/api` prefix
3. NEVER assume the base URL has `/api` in it

## Tenant ID Resolution

`getTenantId()` / `getCachedTenantId()` in `SpaarkeAuthProvider` resolve tenant ID from:
1. **Cached token JWT `tid` claim** — works for ALL token sources (bridge, MSAL, Xrm)
2. MSAL accounts (only if MSAL was invoked)
3. Xrm frame-walk (unreliable on first load)

**Key insight**: In Dataverse web resources, the **bridge strategy** often provides the token
(from parent iframe). This means MSAL never runs, so MSAL accounts are empty. The JWT `tid`
extraction (step 1) is the only reliable source in this scenario.
