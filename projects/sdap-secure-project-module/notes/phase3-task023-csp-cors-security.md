# Phase 3 Task 023 — Configure CSP, CORS, and Security Settings

> **Task**: 023
> **Phase**: 3 — Power Pages Configuration
> **Status**: Documented (manual operator steps required)
> **Estimated effort**: 2 hours
> **Prerequisite**: Task 021 complete (web roles and table permissions configured)
> **Parallel with**: Task 022 (Web API site settings — independent, can run concurrently)

---

## Overview

This guide covers three distinct security configuration areas needed before the SPA can function:

1. **Content Security Policy (CSP)** on the Power Pages portal — allows the SPA to make `fetch()` calls to the BFF API domain.
2. **CORS on the BFF API** (Azure App Service) — allows the portal domain as an allowed origin for cross-origin requests.
3. **Privacy and Security settings** in Power Pages Admin — unblocks `.js` file uploads so the SPA bundle can be uploaded as a web resource.

**Architecture note on CORS vs CSP**:

| Concern | Where Configured | Why |
|---------|-----------------|-----|
| CSP | Power Pages site settings | Controls what the browser allows the SPA (running on the portal domain) to contact |
| CORS | BFF API (Azure App Service + CorsModule.cs) | Controls what origins the BFF API accepts cross-origin requests from |
| `.js` file upload | Power Pages Admin Center | Portal blocks certain file types by default for security |

Power Pages itself does not expose configurable response CORS headers — it is a consumer of Dataverse via its own server-side calls. CORS for the SPA-to-BFF API flow is exclusively managed on the BFF API side.

---

## Step 1 — Configure Content Security Policy on Power Pages

### What is CSP here?

Power Pages emits a `Content-Security-Policy` HTTP response header on every page. By default it does not include the BFF API domain in `connect-src`, which means the browser will block `fetch()` calls from the SPA to `https://spe-api-dev-67e2xz.azurewebsites.net`.

### How to configure

CSP is controlled via a site setting in the Portal Management app.

1. Open Portal Management: `https://spaarkedev1.crm.dynamics.com/main.aspx?appname=MicrosoftPortalApp`
2. Navigate to **Website** → **Site Settings**.
3. Search for an existing setting named `HTTP/ContentSecurityPolicy`.

**If the setting exists**:
- Open it and append to the existing value:
  ```
   connect-src 'self' https://spe-api-dev-67e2xz.azurewebsites.net
  ```
  Ensure the full value is a valid CSP policy string. Example resulting value:
  ```
  default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self' https://spe-api-dev-67e2xz.azurewebsites.net
  ```

**If the setting does not exist** (create it):

| Field | Value |
|-------|-------|
| **Name** | `HTTP/ContentSecurityPolicy` |
| **Value** | `default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self' https://spe-api-dev-67e2xz.azurewebsites.net` |
| **Website** | [your portal] |

4. Click **Save**.

> **Important**: Only append the BFF API domain to `connect-src`. Do not add wildcards (`*`) to `connect-src` or `default-src` — this would undermine CSP's protection against data exfiltration. The constraint from Task 023 is: _only allow the specific BFF API domain — do not use wildcard CORS origins or CSP sources_.

### Additional CSP directives for the SPA

If the SPA loads fonts or images from a CDN, add those origins explicitly. For the Fluent UI icon font (loaded from the `@fluentui/react-components` package), the font is bundled, so no external CDN is needed. Confirm no external font or image sources are used before finalizing the CSP.

### CSP for environment promotion

When the project is promoted to staging or production, update the CSP `connect-src` to reference the corresponding BFF API domain. Maintain one site setting per environment — do not use wildcard subdomains.

| Environment | BFF API Domain |
|-------------|---------------|
| Dev | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| Staging | TBD |
| Production | TBD |

---

## Step 2 — Configure CORS on the BFF API (Azure App Service)

### Why CORS must be on the BFF API side

When the SPA (running at `https://{portal-domain}`) calls `https://spe-api-dev-67e2xz.azurewebsites.net/api/...`, the browser sends a preflight `OPTIONS` request. The BFF API must respond with the appropriate `Access-Control-Allow-Origin` header for the browser to proceed with the actual request.

Power Pages does not intercept or add CORS headers for outbound SPA requests — it only serves the portal pages. CORS policy is enforced server-side on the BFF API.

### Option A: Configure via Azure App Service CORS (platform-level)

The Azure App Service has a built-in CORS configuration that applies to all requests regardless of the application code.

```bash
az webapp cors add \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --allowed-origins "https://{your-portal-domain}.powerappsportals.com"
```

Replace `{your-portal-domain}` with the actual portal subdomain.

Verify:

```bash
az webapp cors show \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz
```

> **Caution**: Azure App Service CORS and application-level CORS (`CorsModule.cs`) can conflict. If the BFF API already uses `CorsModule.cs` (see Option B), do NOT also enable App Service CORS — use one mechanism only.

### Option B: Configure via CorsModule.cs (application-level — preferred)

The BFF API has a `CorsModule.cs` file that configures CORS policies in the .NET middleware. This is the preferred approach because it keeps CORS configuration in source control alongside the endpoint definitions.

**File location**: `src/server/api/Sprk.Bff.Api/` — check for `CorsModule.cs` or CORS configuration in `Program.cs`.

The required CORS configuration for the external portal:

```csharp
// In CorsModule.cs or wherever CORS policy is registered:
builder.Services.AddCors(options =>
{
    options.AddPolicy("ExternalPortalPolicy", policy =>
    {
        policy
            .WithOrigins("https://{your-portal-domain}.powerappsportals.com")
            .AllowAnyMethod()                         // GET, POST, PUT, DELETE, OPTIONS
            .WithHeaders(
                "Authorization",
                "Content-Type",
                "X-Requested-With",
                "RequestVerificationToken")           // Power Pages antiforgery token
            .AllowCredentials();                      // Allows cookies (portal session cookie)
    });
});

// Apply to external endpoints:
app.MapGroup("/api/external")
   .RequireCors("ExternalPortalPolicy")
   .AddEndpointFilter<ExternalCallerAuthorizationFilter>();
```

> **AllowCredentials + specific origin**: When `AllowCredentials()` is used (needed for the portal session cookie), `WithOrigins` must specify exact origins — `AllowAnyOrigin()` is not permitted in this combination. This enforces the constraint: _only the specific BFF API domain, no wildcards_.

**After modifying CorsModule.cs**: redeploy the BFF API using the `bff-deploy` skill or `scripts/Deploy-BffApi.ps1`.

### CORS headers that must be present on BFF API responses

The BFF API responses to cross-origin requests from the portal must include:

| Header | Value |
|--------|-------|
| `Access-Control-Allow-Origin` | `https://{portal-domain}` (exact, not wildcard) |
| `Access-Control-Allow-Methods` | `GET, POST, PUT, DELETE, OPTIONS` |
| `Access-Control-Allow-Headers` | `Authorization, Content-Type, X-Requested-With` |
| `Access-Control-Allow-Credentials` | `true` |
| `Access-Control-Max-Age` | `86400` (preflight cache duration in seconds) |

---

## Step 3 — Unblock .js File Uploads in Power Pages Admin

Power Pages Admin Center has a Privacy and Security section that controls which file types can be uploaded as web resources (used when deploying the SPA bundle to the portal).

### Steps

1. Sign in to the Power Platform admin center: [https://admin.powerplatform.microsoft.com](https://admin.powerplatform.microsoft.com).
2. Navigate to **Environments** → select the dev environment → **Power Pages sites** → **Manage**.
3. In the Power Pages admin center, go to **Security** → **Privacy + Security**.
4. Scroll to **Blocked file types** (or **Allowed file types** — the label depends on the admin center version).
5. Locate `.js` in the blocked list.
6. Remove `.js` from the blocked list (click the X or trash icon next to it).
7. Click **Save** or **Apply**.

> **Why .js is blocked by default**: Power Pages blocks JavaScript file uploads as a security measure to prevent malicious scripts from being served. Since the Spaarke SPA bundle (`portal-spa.js` or similar) is legitimate first-party code and must be deployed via PAC CLI as a web resource, this restriction must be lifted.

### After unblocking

Verify the change by attempting to upload a small `.js` file as a web resource in the Portal Management app or via `pac pages upload`. The upload should succeed without a file-type error.

---

## Step 4 — Clear Portal Cache

After modifying site settings:

1. Power Pages admin center → **Portal Actions** → **Clear cache**.
2. Or navigate to `https://{portal-domain}/_services/about` → **Clear Cache**.

---

## Step 5 — Verification Tests

### Test 1: CSP allows BFF API fetch

1. Sign in to the portal.
2. Open browser DevTools → Console.
3. Run:
   ```javascript
   fetch('https://spe-api-dev-67e2xz.azurewebsites.net/healthz')
     .then(r => console.log('CSP OK, status:', r.status))
     .catch(e => console.error('CSP blocked:', e));
   ```
4. Expected: `CSP OK, status: 200` (or the health endpoint status code).
5. If you see a CSP error in the Console: the `connect-src` directive is not yet active — clear portal cache and retry.

### Test 2: CORS preflight succeeds

1. From the portal page (or browser console while on the portal domain), make a cross-origin request:
   ```javascript
   fetch('https://spe-api-dev-67e2xz.azurewebsites.net/api/external/context', {
     method: 'GET',
     credentials: 'include',
     headers: { 'Content-Type': 'application/json' }
   })
   .then(r => console.log('CORS OK, status:', r.status))
   .catch(e => console.error('CORS error:', e));
   ```
2. Expected: request succeeds (200 or 401 — either means the preflight passed).
3. In DevTools Network tab, verify the OPTIONS preflight request received `Access-Control-Allow-Origin: https://{portal-domain}` in the response headers.

### Test 3: .js file upload succeeds

1. In Portal Management → **Basic Forms** (or any entity where you can upload a web file), or via PAC CLI:
   ```bash
   pac pages upload --path dist/portal-spa.js
   ```
2. Expected: upload succeeds without file-type error.

### Test 4: Blocked domain still blocked

1. From the portal, attempt a fetch to a domain NOT in the CSP allowlist:
   ```javascript
   fetch('https://untrusted-domain.example.com/test')
     .catch(e => console.log('Correctly blocked by CSP'));
   ```
2. Expected: CSP violation error in Console — the fetch is blocked.

---

## Acceptance Criteria Checklist

- [ ] `HTTP/ContentSecurityPolicy` site setting exists with `connect-src` including `https://spe-api-dev-67e2xz.azurewebsites.net`
- [ ] BFF API CORS configured to allow the portal domain (via `CorsModule.cs` or App Service CORS — not both)
- [ ] CORS `AllowedOrigins` does not use wildcards
- [ ] `Access-Control-Allow-Credentials: true` included in BFF API CORS response
- [ ] `.js` removed from Power Pages blocked file types list
- [ ] Portal cache cleared
- [ ] `fetch()` to BFF API from portal browser console succeeds (no CSP error)
- [ ] CORS preflight OPTIONS returns correct `Access-Control-Allow-Origin` header
- [ ] `.js` file upload succeeds via PAC CLI or Portal Management

---

## Troubleshooting

| Symptom | Likely Cause | Resolution |
|---------|-------------|------------|
| `Content Security Policy: The page's settings blocked the loading of a resource` | `connect-src` not updated, or cache not cleared | Add BFF API domain to `HTTP/ContentSecurityPolicy` site setting; clear portal cache |
| `Access to fetch at '...' from origin '...' has been blocked by CORS policy` | BFF API CORS not configured for portal origin | Add portal domain to `CorsModule.cs` `WithOrigins()`; redeploy BFF API |
| `Cannot use wildcard in Access-Control-Allow-Origin when credentials flag is true` | `AllowAnyOrigin()` used with `AllowCredentials()` | Change to `WithOrigins("https://{portal-domain}")` |
| File upload rejected with file type error | `.js` still in blocked list | Revisit Step 3; save changes in Privacy + Security admin panel |
| CORS preflight returns 405 (Method Not Allowed) | OPTIONS method not handled | Ensure `app.UseCors()` is called before `app.UseRouting()` in `Program.cs` |
| CSP header not present on portal pages | Site setting name misspelled | Setting name must be exactly `HTTP/ContentSecurityPolicy` (case-sensitive) |
| BFF API not receiving `Authorization` header | CORS `WithHeaders` missing `Authorization` | Add `Authorization` to the `WithHeaders()` call in `CorsModule.cs` |
