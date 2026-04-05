# BFF API URL Construction Pattern

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2 + /api helper consolidation
> **Status**: Verified

> **Entry Point**: `src/client/shared/Spaarke.Auth/src/buildBffApiUrl.ts` (Code Pages & PCF — via @spaarke/auth)
> **Entry Point**: `src/client/pcf/shared/utils/environmentVariables.ts:buildBffApiUrl()` (PCF shared utils)
> **Full Reference**: `docs/architecture/AUTH-AND-BFF-URL-PATTERN.md`
> **Constraint**: `.claude/constraints/auth.md` → "BFF Base URL Convention"

## The Rule

**Always use `buildBffApiUrl()`. Never construct BFF URLs with template literals.**

```typescript
// ✅ CORRECT
import { buildBffApiUrl } from '@spaarke/auth';
const url = buildBffApiUrl(bffBaseUrl, '/ai/visualization/related/123');

// ❌ WRONG — banned as of 2026-04-05
const url = `${bffBaseUrl}/api/ai/visualization/related/123`;
```

## Why a Helper Exists

The Dataverse env var `sprk_BffApiBaseUrl` stores the URL WITH `/api` suffix (e.g., `https://host/api`). Two normalization functions strip it so the resolved base URL is HOST ONLY:

- `normalizeUrl()` in `@spaarke/auth/src/resolveRuntimeConfig.ts:388`
- `normalizeBffUrl()` in `src/client/pcf/shared/utils/environmentVariables.ts`

Every caller then had to manually add `/api/` back when constructing request URLs. This manual step was **the source of multiple production bugs**:

- URLs missing `/api/` → `/ai/search` → 404
- URLs duplicating `/api/` → `/api/api/ai/search` → 404
- Different hooks in the same PCF disagreeing on the format
- Copy-paste from incorrect examples propagating the bug

**`buildBffApiUrl()` is idempotent** — accepts paths with or without `/api/` and always produces the correct URL. It eliminates this class of bug entirely.

## Usage

### Code Pages

```typescript
import { resolveRuntimeConfig, buildBffApiUrl, authenticatedFetch } from '@spaarke/auth';

const config = await resolveRuntimeConfig();

// Pass path with or without /api/ — both work
const url1 = buildBffApiUrl(config.bffBaseUrl, '/ai/chat/sessions');
const url2 = buildBffApiUrl(config.bffBaseUrl, '/api/ai/chat/sessions'); // same result
const url3 = buildBffApiUrl(config.bffBaseUrl, 'ai/chat/sessions');      // same result

const response = await authenticatedFetch(url1);
```

### PCF Controls

```typescript
import { getApiBaseUrl, buildBffApiUrl } from '../../shared/utils/environmentVariables';
import { authenticatedFetch } from '@spaarke/auth';

const base = await getApiBaseUrl(context.webAPI);
const url = buildBffApiUrl(base, `/ai/visualization/related/${documentId}?limit=20`);
const response = await authenticatedFetch(url);
```

### Relative URLs via authenticatedFetch (safety net)

`authenticatedFetch()` routes relative URLs through `buildBffApiUrl()` internally. So passing a relative path always produces the correct URL — even if the caller forgets `/api/`:

```typescript
import { authenticatedFetch } from '@spaarke/auth';

// All three produce: https://host/api/ai/chat/sessions
await authenticatedFetch('/ai/chat/sessions');
await authenticatedFetch('/api/ai/chat/sessions');
await authenticatedFetch('ai/chat/sessions');
```

## Idempotency Guarantee

`buildBffApiUrl()` is safe to call with any of these inputs:

| Input path            | Output (base = `https://host`)         |
|----------------------|-----------------------------------------|
| `/ai/search`         | `https://host/api/ai/search`            |
| `/api/ai/search`     | `https://host/api/ai/search` (same)     |
| `ai/search`          | `https://host/api/ai/search` (same)     |
| `/api`               | `https://host/api`                      |
| `/api/`              | `https://host/api/`                     |

Trailing slashes on `baseUrl` are tolerated and stripped.

## What to Do with Existing Code

- **New code**: MUST use `buildBffApiUrl()`
- **Existing template literals** (`${bffBaseUrl}/api/...`): Migrate opportunistically when touching the file
- **Code review**: Flag any new template-literal BFF URL construction

## Tenant ID Resolution

Unchanged — see `SpaarkeAuthProvider.getTenantId()`:

1. **Cached token JWT `tid` claim** — works for ALL token sources (bridge, MSAL, Xrm)
2. MSAL accounts (only if MSAL was invoked)
3. Xrm frame-walk (unreliable on first load)

**Key insight**: In Dataverse web resources, the **bridge strategy** often provides the token (from parent iframe). MSAL never runs, so MSAL accounts are empty. The JWT `tid` extraction (step 1) is the only reliable source in this scenario.
