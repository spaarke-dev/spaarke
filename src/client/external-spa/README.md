# External SPA — Secure External Workspace

A React 18 application built with Vite and deployed as a static site to **Azure Static
Web Apps** (`swa-spaarke-external-spa-dev`). It serves external (CIAM) users of the
Spaarke Secure Project Workspace, and can also run embedded as a Teams tab (workforce
collaboration host).

---

## Quick Start (local development)

```bash
# 1. Install dependencies
cd src/client/external-spa
npm install --legacy-peer-deps --no-audit --no-fund

# 2. Copy the example env file and fill in your values
cp .env.example .env.local

# 3. Start the Vite dev server (http://localhost:3000)
npm run dev
```

Authentication is handled by MSAL against an Entra External ID (CIAM) tenant — see
`src/auth/` for the implementation. No portal session or proxy login step is required.

---

## Environment Variables

| Variable | Purpose |
|---|---|
| `VITE_BFF_API_URL` | Spaarke BFF API base URL |
| `VITE_MSAL_AUTHORITY` | Full CIAM authority URL (`https://{subdomain}.ciamlogin.com/{tenant-id}`) |
| `VITE_MSAL_CLIENT_ID` | SPA public-client app registration ID (registered in the CIAM tenant) |
| `VITE_MSAL_TENANT_ID` | CIAM external tenant ID |
| `VITE_MSAL_BFF_SCOPE` | BFF API scope exposed in the CIAM tenant (must match BFF `Ciam:Audience`) |
| `VITE_TEAMS_MSAL_CLIENT_ID` | Optional — workforce multitenant app client ID (Teams tab host only) |
| `VITE_TEAMS_MSAL_BFF_SCOPE` | Optional — workforce BFF scope (Teams tab host only) |

Copy `.env.example` to `.env.local` and set real values.
`.env.local` is gitignored — never commit secrets.

---

## Dev Server Proxy

The Vite dev server proxies `/api/*` to the Spaarke BFF API so the SPA can call it
from `localhost` without hitting browser CORS restrictions (`bffApiCall` uses relative
`/api/...` paths when `VITE_BFF_API_URL` is empty). See `server.proxy` in
`vite.config.ts`.

---

## Build

```bash
npm run build
```

Output is `dist/` — an `index.html` plus a predictable `assets/app.js` bundle (IIFE
format). This is what the deploy workflow uploads to Azure Static Web Apps.

---

## Deployment

Deployment is handled by the `.github/workflows/deploy-external-spa.yml` GitHub Actions
workflow, which builds the app and uploads `dist/` to Azure Static Web Apps
(`swa-spaarke-external-spa-dev`) via `Azure/static-web-apps-deploy@v1`.
`staticwebapp.config.json` (navigation fallback + security headers) is staged into
`dist/` as part of the build step since SWA only reads it from the deployed content
root.
