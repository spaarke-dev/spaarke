# Power Pages SPA Technical Guide

> **Domain**: External-Facing Web Applications
> **Status**: Reference
> **Last Updated**: 2026-03-16
> **Applies To**: Secure Project Module, future external access features

---

## Overview

Power Pages SPA ("code site") support reached **General Availability on February 8, 2026** (site version 9.8.1.x). A code site is a Power Pages site that runs entirely in the browser (client-side rendering) — no Liquid templates, no server-side page rendering, no low-code components. The SPA is managed exclusively via source code and PAC CLI.

This guide covers building a production React 18 SPA hosted on Power Pages that calls the Spaarke BFF API (`Sprk.Bff.Api`).

---

## Architecture: Spaarke External SPA

```
┌─────────────────────────────────────┐
│  Browser (External User)            │
│  React 18 Code Page SPA             │
│  ├── Fluent UI v9                   │
│  ├── @spaarke/ui-components         │
│  └── Vite build → dist/             │
└──────────┬──────────┬───────────────┘
           │          │
    Dataverse     BFF API
    Web API       (external)
           │          │
           ↓          ↓
┌──────────────┐ ┌─────────────────────┐
│ Power Pages  │ │ Sprk.Bff.Api        │
│ /_api/...    │ │ (Azure App Service)  │
│ (OData 4.0   │ │                     │
│  proxy to    │ │ Auth: portal-issued  │
│  Dataverse)  │ │ OAuth token          │
│              │ │                     │
│ Auth: session│ │ SPE, AI Search,     │
│ cookie +     │ │ Playbooks, Email    │
│ CSRF token   │ └─────────────────────┘
└──────────────┘
```

**Two API paths:**
1. **Power Pages Web API** (`/_api/...`) — for lightweight Dataverse reads that align with table permissions (contacts, projects, events, tasks)
2. **BFF API** — for business logic, SPE file operations, AI features, playbook execution, email

---

## Prerequisites

| Requirement | Version/Detail |
|-------------|----------------|
| Power Pages site version | **9.8.1.x or later** (GA) |
| Bearer auth (local dev) | **9.7.6.6 or later** |
| PAC CLI | **1.44.x or later** |
| Dataverse JS file uploads | Must unblock `.js` in Privacy + Security settings |
| Power Platform Git Integration | **NOT supported** for SPA sites — use GitHub/Azure DevOps |

**Unblock .js uploads** (required, common gotcha):
1. Power Platform Admin Center → Environments → Settings → Product → Privacy + Security
2. Remove `js` from the **Blocked Attachments** list

---

## Project Structure

```
src/client/external-spa/          # New directory for external SPA
├── src/
│   ├── App.tsx                   # Root component with FluentProvider
│   ├── main.tsx                  # React 18 createRoot entry point
│   ├── auth/
│   │   ├── portal-auth.ts        # Portal token, CSRF, user context
│   │   └── bff-client.ts         # BFF API client with Bearer auth
│   ├── pages/
│   │   ├── WorkspaceHome.tsx     # External workspace home
│   │   ├── ProjectPage.tsx       # Secure Project view
│   │   └── DocumentLibrary.tsx   # Document library with upload
│   ├── components/               # SPA-specific components
│   └── hooks/                    # SPA-specific hooks
├── public/
│   └── index.html
├── vite.config.ts
├── tsconfig.json
├── package.json
└── powerpages.config.json        # PAC CLI deployment config
```

### `powerpages.config.json`

```json
{
  "siteName": "Spaarke External Portal",
  "defaultLandingPage": "index.html",
  "compiledPath": "./dist"
}
```

---

## Authentication

### Production: Power Pages Session + OAuth Token

External users authenticate via **Entra External ID** through Power Pages. The SPA has two auth mechanisms:

#### 1. Portal Session (for Power Pages Web API calls)

The portal manages the session via HttpOnly cookie. The SPA must include a CSRF token with every `/_api/` request.

```typescript
// portal-auth.ts

/** Get the current authenticated user from the portal shell */
export function getPortalUser(): PortalUser | null {
    const portal = (window as any)["Microsoft"]?.Dynamic365?.Portal;
    if (!portal?.User?.userName) return null;
    return {
        userName: portal.User.userName,
        firstName: portal.User.firstName,
        lastName: portal.User.lastName,
        tenantId: portal.tenant
    };
}

/** Fetch CSRF token from portal (required for all Web API calls) */
export async function getAntiForgeryToken(): Promise<string> {
    const response = await fetch("/_layout/tokenhtml");
    const text = await response.text();
    const doc = new DOMParser().parseFromString(text, "text/xml");
    return doc.querySelector("input")?.getAttribute("value") ?? "";
}

/** Make an authenticated Power Pages Web API call */
export async function portalApiCall<T>(
    endpoint: string,
    method: "GET" | "POST" | "PATCH" | "DELETE" = "GET",
    body?: unknown
): Promise<T> {
    const headers: Record<string, string> = {
        "Accept": "application/json",
        "OData-MaxVersion": "4.0",
        "OData-Version": "4.0"
    };

    if (method !== "GET") {
        headers["Content-Type"] = "application/json";
        headers["__RequestVerificationToken"] = await getAntiForgeryToken();
    }

    const response = await fetch(`/_api${endpoint}`, {
        method,
        headers,
        body: body ? JSON.stringify(body) : undefined
    });

    if (!response.ok) throw new ApiError(response.status, await response.text());
    return method === "DELETE" ? (undefined as T) : response.json();
}
```

#### 2. Portal OAuth Token (for BFF API calls)

The SPA obtains a short-lived token from the portal's OAuth endpoint and passes it to the BFF API.

```typescript
// bff-client.ts

const BFF_BASE_URL = import.meta.env.VITE_BFF_API_URL;
const PORTAL_CLIENT_ID = import.meta.env.VITE_PORTAL_CLIENT_ID;

/** Get a portal-issued OAuth token for BFF API calls */
async function getPortalToken(): Promise<string> {
    const csrfToken = await getAntiForgeryToken();
    const params = new URLSearchParams({
        client_id: PORTAL_CLIENT_ID,
        response_type: "token",
        nonce: crypto.randomUUID().substring(0, 20),
        state: crypto.randomUUID().substring(0, 20)
    });

    const response = await fetch("/_services/auth/token", {
        method: "POST",
        headers: {
            "__RequestVerificationToken": csrfToken,
            "Content-Type": "application/x-www-form-urlencoded"
        },
        body: params
    });

    return response.text();
}

/** Make an authenticated BFF API call */
export async function bffApiCall<T>(
    endpoint: string,
    method: "GET" | "POST" | "PATCH" | "DELETE" = "GET",
    body?: unknown
): Promise<T> {
    const token = await getPortalToken();

    const response = await fetch(`${BFF_BASE_URL}${endpoint}`, {
        method,
        headers: {
            "Authorization": `Bearer ${token}`,
            "Content-Type": "application/json"
        },
        body: body ? JSON.stringify(body) : undefined
    });

    if (!response.ok) throw new ApiError(response.status, await response.text());
    return response.json();
}
```

**Portal site settings required:**

| Setting | Value |
|---------|-------|
| `ImplicitGrantFlow/RegisteredClientId` | `{your-client-id}` |
| `Connector/ImplicitGrantFlowEnabled` | `True` |
| `ImplicitGrantFlow/TokenExpirationTime` | `900` (15 min, max 3600) |

**BFF API token validation:** Validate against the portal's public key at `<portal_url>/_services/auth/publickey`.

### Local Development: Vite + Bearer Auth

For `localhost` development with hot reload:

```typescript
// vite.config.ts
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
    plugins: [react()],
    server: {
        port: 3000,
        proxy: {
            "/_api": {
                target: "https://spaarke-external.powerappsportals.com",
                changeOrigin: true,
                secure: true
            },
            "/_layout": {
                target: "https://spaarke-external.powerappsportals.com",
                changeOrigin: true,
                secure: true
            },
            "/_services": {
                target: "https://spaarke-external.powerappsportals.com",
                changeOrigin: true,
                secure: true
            }
        }
    }
});
```

**Dev-only site settings** (do NOT apply in production):
```
Authentication/BearerAuthentication/Enabled = true
Authentication/BearerAuthentication/Protocol = OpenIdConnect
Authentication/BearerAuthentication/Provider = AzureAD
```

**Important**: Bearer auth requires Azure AD v1 (ADAL.js) — MSAL.js is NOT compatible (Power Pages issues v1 tokens).

---

## Power Pages Web API

The portals Web API (`/_api/...`) is an OData 4.0 proxy to Dataverse, scoped to what the authenticated contact can access via table permissions.

### Enabling Tables for Web API

For each table the SPA needs:

| Site Setting | Value |
|--------------|-------|
| `Webapi/sprk_project/enabled` | `true` |
| `Webapi/sprk_project/fields` | `sprk_name,sprk_description,sprk_referencenumber,sprk_issecure,...` |
| `Webapi/sprk_document/enabled` | `true` |
| `Webapi/sprk_document/fields` | `sprk_name,sprk_documenttype,sprk_summary,...` |
| `Webapi/sprk_event/enabled` | `true` |
| `Webapi/sprk_event/fields` | `sprk_name,sprk_duedate,sprk_status,...` |

**Use EntitySetName** (plural) in URLs, **logical name** (singular) in site settings.

### Supported Operations

| Method | Operation | CSRF Required |
|--------|-----------|---------------|
| GET | Read records | No |
| POST | Create records | Yes |
| PATCH | Update/upsert records | Yes |
| DELETE | Delete records | Yes |

**Not supported:** Calling Dataverse actions or functions via the portals Web API. Use BFF API for these.

### Error Handling

| HTTP Status | Error | Cause |
|-------------|-------|-------|
| 401 | `MissingPortalRequestVerificationToken` | Missing CSRF token |
| 401 | `MissingPortalSessionCookie` | No authenticated session |
| 403 | `TablePermissionCreateIsMissing` | Web role lacks permission |
| 403 | `AttributePermissionIsMissing` | Column not in `fields` site setting |
| 404 | Resource not found | Table not enabled for Web API |

---

## Deployment

### PAC CLI Commands

```powershell
# Upload (deploy) SPA to Power Pages
pac pages upload-code-site `
    --rootPath "src/client/external-spa" `
    --compiledPath "./dist" `
    --siteName "Spaarke External Portal"

# Download existing site config (always do this before redeploying
# to avoid overwriting auth provider settings)
pac pages download-code-site `
    --environment "https://spaarkedev1.crm.dynamics.com" `
    --path "./downloaded-site" `
    --webSiteId "{site-guid}" `
    --overwrite
```

**First deployment**: After upload, the site appears in **Inactive sites** in Power Pages Studio. Activate it manually once. Subsequent uploads update the active site automatically.

### CI/CD Integration

```yaml
# Example: Azure DevOps pipeline step
- script: |
    npm ci
    npm run build
    pac auth create --url $(DATAVERSE_URL) --applicationId $(APP_ID) --clientSecret $(CLIENT_SECRET) --tenant $(TENANT_ID)
    pac pages upload-code-site --rootPath "." --compiledPath "./dist" --siteName "Spaarke External Portal"
  workingDirectory: src/client/external-spa
```

---

## Security Configuration

### Content Security Policy

Add BFF API domain to CSP (Portal Management app → Site Settings):

```
HTTP/Content-Security-Policy = script-src 'self'; connect-src 'self' https://spe-api-dev-67e2xz.azurewebsites.net; style-src 'self' 'unsafe-inline'
```

### CORS (if needed)

| Site Setting | Value |
|--------------|-------|
| `HTTP/Access-Control-Allow-Origin` | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| `HTTP/Access-Control-Allow-Methods` | `GET, POST, PATCH, DELETE, OPTIONS` |
| `HTTP/Access-Control-Allow-Headers` | `Authorization, Content-Type` |
| `HTTP/Access-Control-Allow-Credentials` | `true` |

### Identity Provider: Entra External ID

Configure in Power Pages Studio → Security → Identity providers:
1. Add Microsoft Entra ID
2. Configure for Entra External ID (B2C tenant)
3. Map claims to Contact fields (email, first name, last name)

The `adx_externalidentity` table automatically maps Entra identity → Contact record on first login.

---

## What Is NOT Available in SPA Mode

| Feature | Available? | Alternative |
|---------|-----------|-------------|
| Liquid templates | No | React components + Web API |
| Pages workspace (low-code) | No | Code-only via PAC CLI |
| Style workspace | No | CSS / CSS-in-JS / Fluent UI v9 |
| Lists and Forms (drag-and-drop) | No | Build in React |
| Power Platform Git integration | No | GitHub / Azure DevOps |
| Server-side rendering / SEO | No | Client-side rendering only |
| Multi-language (built-in) | No | Client-side i18n |
| Out-of-the-box PCF components | No | Use shared component library |
| Dataverse actions/functions via Web API | No | Call via BFF API |

---

## Development Standards (Spaarke Conventions)

### React & UI

- **React 18** with `createRoot` (bundled, not platform-provided — same as internal Code Pages)
- **Fluent UI v9** exclusively (ADR-021) — no Fluent v8, no custom colors, dark mode support required
- **`@spaarke/ui-components`** shared library for common layouts (WizardDialog, SidePanel, DataGrid, etc.)
- TypeScript strict mode, PascalCase for components, camelCase for utilities

### API Patterns

- Use **Power Pages Web API** for simple Dataverse CRUD that table permissions already scope
- Use **BFF API** for: SPE file operations, AI features (playbook execution, summaries, search), email, complex business logic
- Never expose internal Dataverse operations directly — BFF enforces UAC
- All BFF API calls use portal-issued OAuth tokens (not session cookies)

### File Structure

Follow internal Code Pages patterns:
- Entry point: `main.tsx` with `createRoot`
- `FluentProvider` with `webLightTheme` at root
- URL parameters for context (`?projectId={id}`)
- Shared hooks for common patterns (`usePortalAuth`, `useBffApi`, `useProject`)

---

## Related Resources

- [UAC Access Control Architecture](uac-access-control.md) — Three-plane authorization model
- [Power Pages Access Control & UAC Configuration](power-pages-access-control.md) — Table permissions and web role setup
- [Create and deploy a SPA in Power Pages (MS Learn)](https://learn.microsoft.com/en-us/power-pages/configure/create-code-sites)
- [Power Pages Web API overview (MS Learn)](https://learn.microsoft.com/en-us/power-pages/configure/web-api-overview)
- [OAuth 2.0 implicit grant flow (MS Learn)](https://learn.microsoft.com/en-us/power-pages/security/oauth-implicit-grant-flow)
- [Set up Entra External ID (MS Learn)](https://learn.microsoft.com/en-us/power-pages/security/authentication/entra-external-id)
- [Power Pages security overview (MS Learn)](https://learn.microsoft.com/en-us/power-pages/security/power-pages-security)
- [Announcing GA of SPAs for Power Pages (Blog)](https://www.microsoft.com/en-us/power-platform/blog/power-pages/announcing-general-availability-ga-of-building-single-page-applications-on-power-pages/)
- [Power Pages SPA Authentication (Hack the Platform)](https://hacktheplatform.dev/blog/powerpages-spa-authentication-part-2)
