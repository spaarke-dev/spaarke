# Code Page Configuration: Build-Time vs Runtime Resolution

**Date**: 2026-03-17
**Status**: Proposal
**Affects**: All code pages (AnalysisWorkspace, SprkChatPane, PlaybookBuilder, DocumentRelationshipViewer, LegalWorkspace, DocumentUploadWizard, SemanticSearch, EventsPage, SpeAdminApp)

---

## The Problem

Code pages currently bake configuration values (BFF API URL, MSAL client ID) into the JavaScript bundle at **build time** via `.env.production` files. This means:

- **Every new environment requires a separate build** of every code page
- **N code pages × M environments = N×M builds** to maintain
- Deploying to a new Dataverse org means rebuilding all code pages with new `.env.production` values
- A single typo in `.env.production` requires a full rebuild + redeploy cycle

PCF controls don't have this problem — they resolve the BFF URL at **runtime** from Dataverse Environment Variables (`sprk_BffApiBaseUrl`), so one build works in any environment.

## How It Works Today

### PCF Controls (Runtime — Good)

```
PCF init() → webApi.retrieveMultipleRecords('environmentvariabledefinition',
  filter: schemaname eq 'sprk_BffApiBaseUrl') → cached 5 min → use URL
```

- **Build once, deploy anywhere** — URL comes from Dataverse at runtime
- Configuration lives in `src/client/pcf/shared/utils/environmentVariables.ts`
- Reads from Dataverse Environment Variable entity (`sprk_BffApiBaseUrl`)
- Falls back to hardcoded dev URL only if env var missing

### Code Pages (Build-Time — Problematic)

```
.env.production → VITE_BFF_BASE_URL=https://spe-api-dev-67e2xz.azurewebsites.net/api
                → baked into bundle at build time via import.meta.env.VITE_BFF_BASE_URL
```

- **Must rebuild per environment** — URL is a string literal in the compiled JS
- Additional complication: some code pages use **Vite** (handles `import.meta.env` natively) while others use **Webpack** (requires a `DefinePlugin` shim to support the same API)

### Resolution Cascade in bffConfig.ts

```
1. window.__SPAARKE_BFF_BASE_URL__          (runtime global — never set in practice)
2. window.parent.__SPAARKE_BFF_BASE_URL__   (parent frame — never set in practice)
3. import.meta.env.VITE_BFF_BASE_URL        (build-time — the only one that works)
4. throw Error                               (no config found)
```

Steps 1-2 were designed as runtime overrides but nothing sets them. Step 3 is the only working path, and it requires per-environment builds.

## Proposed Resolution: Runtime Config via Dataverse Web API

Code pages already authenticate via MSAL and make Dataverse API calls. They can query Environment Variables the same way PCF controls do — just using `fetch()` + bearer token instead of the PCF `webApi` SDK.

### Shared Service: `@spaarke/auth` or `@spaarke/config`

```typescript
// New shared service — works in any code page (no PCF dependency)
export async function resolveConfig(token: string, orgUrl: string): Promise<SpaarkeConfig> {
  // Query Dataverse Environment Variables via REST
  const resp = await fetch(
    `${orgUrl}/api/data/v9.2/environmentvariabledefinitions?$filter=schemaname eq 'sprk_BffApiBaseUrl'&$select=defaultvalue,environmentvariabledefinitionid`,
    { headers: { Authorization: `Bearer ${token}` } }
  );
  // ... resolve override value, cache, return
}
```

### What Changes

| Component | Before | After |
|-----------|--------|-------|
| **bffConfig.ts** | `import.meta.env.VITE_BFF_BASE_URL` | `resolveConfig(token, orgUrl).bffBaseUrl` |
| **msalConfig.ts** | `import.meta.env.VITE_MSAL_CLIENT_ID` | Remains build-time (needed before auth) |
| **.env.production** | Required per environment | Only MSAL client ID (same across envs, or resolved from Xrm) |
| **webpack.config.js** | DefinePlugin shim for VITE vars | Remove shim (no build-time vars for BFF URL) |
| **Build pipeline** | Rebuild per environment | Build once, deploy anywhere |

### MSAL Client ID: The Exception

The MSAL client ID is needed **before authentication** (to initiate the auth flow), so it can't be resolved from Dataverse (which requires auth). Options:

1. **Keep as build-time env var** — acceptable since client ID rarely changes between environments (same Azure AD app registration)
2. **Read from Xrm context** — `Xrm.Utility.getGlobalContext()` is available in Dataverse web resources and may expose app registration info
3. **Convention-based** — if all environments use the same app registration, hardcode it

### Bootstrap Sequence (After)

```
1. Read MSAL client ID (build-time or Xrm context — needed for auth)
2. Initialize MSAL, acquire token
3. Resolve orgUrl from Xrm.Utility.getGlobalContext().getClientUrl()
4. Call resolveConfig(token, orgUrl) → gets BFF URL from Dataverse Environment Variables
5. Cache config (5 min, same as PCF pattern)
6. Render app with resolved config
```

## Impact Assessment

| Factor | Build-Time (Current) | Runtime (Proposed) |
|--------|---------------------|-------------------|
| Builds per environment | N code pages × M envs | N code pages × 1 |
| Deploy to new org | Rebuild all code pages | Just upload + set env var |
| Config change (new BFF URL) | Rebuild + redeploy all | Update Dataverse env var |
| Initial page load | Instant (URL baked in) | +1 Dataverse API call (~100ms, cached) |
| Complexity | `.env` files per page per env | Shared resolveConfig service |
| Consistency with PCF | Different pattern | Same pattern |

## Recommendation

1. **Create `resolveConfig()` in `@spaarke/auth`** — shared runtime config resolution using Dataverse Environment Variables
2. **Migrate code pages incrementally** — start with one (e.g., AnalysisWorkspace), verify the pattern, then roll out
3. **Remove `.env.production` BFF URL** — keep only MSAL client ID (if it varies by environment)
4. **Remove DefinePlugin shim** from webpack configs (no longer needed for BFF URL)
5. **Optionally migrate webpack→Vite** as a separate cleanup (lower priority once runtime config eliminates the env var issue)

## Where to Implement

This is a **cross-cutting infrastructure change** that affects all code pages. Best implemented as:
- A shared library enhancement (`@spaarke/auth` already handles MSAL — add config resolution)
- Incremental rollout (one code page at a time, verify, then batch the rest)
- Should be its own project/branch since it touches every code page
