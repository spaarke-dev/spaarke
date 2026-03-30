# @spaarke/auth Initialization Pattern

> **Applies to**: Code Pages (`src/solutions/*`) that call BFF API endpoints
> **NOT needed for**: Code Pages using only `Xrm.WebApi` (e.g., SmartTodo, TodoDetailSidePane)
> **Source**: `src/client/shared/Spaarke.Auth/`
> **Canonical impl**: `src/solutions/LegalWorkspace/src/main.tsx`

---

## When to Use

Use `@spaarke/auth` when a Code Page needs to call BFF API endpoints (workspace layouts, AI operations, document operations, SharePoint/Graph via OBO). Do NOT add this bootstrap to Code Pages that only use `Xrm.WebApi` — it adds unnecessary overhead.

See: `xrm-webapi-vs-bff-auth.md` for the decision guide.

---

## Bootstrap Sequence (MANDATORY)

```typescript
// main.tsx — async bootstrap BEFORE React render
import { resolveRuntimeConfig } from "@spaarke/auth";
import { setRuntimeConfig } from "./config/runtimeConfig";
import { ensureAuthInitialized } from "./services/authInit";

async function bootstrap(): Promise<void> {
  // 1. Query Dataverse Environment Variables for BFF URL, OAuth scope, MSAL client ID
  const config = await resolveRuntimeConfig();

  // 2. Store in singleton — getBffBaseUrl() / getMsalClientId() now work
  setRuntimeConfig(config);

  // 3. Initialize MSAL — warm token cache, resolve tenant ID
  try {
    await ensureAuthInitialized();
  } catch (err) {
    console.warn("[App] Auth init failed, will retry on first use:", err);
  }

  // 4. Render AFTER config + auth are ready
  const root = createRoot(document.getElementById("root")!);
  root.render(<App />);
}

bootstrap();
```

**Order matters**: `resolveRuntimeConfig()` → `setRuntimeConfig()` → `ensureAuthInitialized()` → `render()`. Rendering before `setRuntimeConfig()` causes `getBffBaseUrl()` to throw.

---

## Key Exports from @spaarke/auth

| Export | Purpose | When Called |
|--------|---------|------------|
| `resolveRuntimeConfig()` | Queries Dataverse env vars via session cookie | Once at bootstrap |
| `initAuth(options)` | Initializes SpaarkeAuthProvider + MSAL | Once at bootstrap |
| `authenticatedFetch(url, init)` | fetch() with BFF Bearer token | Every BFF API call |
| `getAuthProvider()` | Get current auth provider instance | For tenant ID, token info |
| `resolveTenantIdSync()` | Synchronous tenant ID from MSAL/Xrm | URL construction, headers |

---

## authInit.ts Wrapper Pattern

Each Code Page that uses BFF should have a thin `authInit.ts`:

```typescript
// services/authInit.ts
import { initAuth, authenticatedFetch as sharedAuthFetch, getAuthProvider } from "@spaarke/auth";
import { getBffBaseUrl, getBffOAuthScope, getMsalClientId } from "../config/runtimeConfig";

let _initPromise: Promise<void> | null = null;

export function ensureAuthInitialized(): Promise<void> {
  if (!_initPromise) {
    _initPromise = (async () => {
      await initAuth({
        clientId: getMsalClientId(),
        bffBaseUrl: getBffBaseUrl(),
        bffApiScope: getBffOAuthScope(),
        proactiveRefresh: true,
      });
    })();
  }
  return _initPromise;
}

export async function authenticatedFetch(url: string, init?: RequestInit): Promise<Response> {
  await ensureAuthInitialized();
  return sharedAuthFetch(url, init);
}
```

---

## MUST Rules

- **MUST** call `resolveRuntimeConfig()` + `setRuntimeConfig()` before any `getBffBaseUrl()` / `getMsalClientId()` call
- **MUST** use lazy functions (not module-level constants) for runtime config values
- **MUST** import `authenticatedFetch` from `authInit.ts` or `@spaarke/auth` — not from legacy `bffAuthProvider.ts`
- **MUST** await bootstrap before calling `createRoot().render()`

## MUST NOT

- **MUST NOT** use `const CLIENT_ID = getMsalClientId()` at module level — throws before bootstrap
- **MUST NOT** use `const BASE_URL = getBffBaseUrl()` at module level — same issue
- **MUST NOT** import from legacy `bffAuthProvider.ts` in new code
- **MUST NOT** create new `msalConfig.ts` files with module-level MSAL constants

---

## Anti-Patterns

```typescript
// ❌ WRONG — module-level constant throws before bootstrap
const CLIENT_ID = getMsalClientId();
export const msalConfig = { auth: { clientId: CLIENT_ID } };

// ❌ WRONG — legacy import
import { authenticatedFetch } from "../services/bffAuthProvider";

// ✅ CORRECT — lazy function
export function getMsalConfig(): Configuration {
  return { auth: { clientId: getMsalClientId() } };
}

// ✅ CORRECT — import from authInit (standard pattern)
import { authenticatedFetch } from "../services/authInit";
```

---

## References

- Canonical bootstrap: `src/solutions/LegalWorkspace/src/main.tsx`
- Auth wrapper: `src/solutions/LegalWorkspace/src/services/authInit.ts`
- Runtime config: `src/solutions/LegalWorkspace/src/config/runtimeConfig.ts`
- Shared library: `src/client/shared/Spaarke.Auth/src/index.ts`
- Full auth patterns: `docs/architecture/sdap-auth-patterns.md` (Pattern 9)
