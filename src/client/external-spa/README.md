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

The variables, their meaning and which are optional are documented in [`.env.example`](./.env.example) - that file is the
single source (backend selection: `VITE_BACKENDS` or `VITE_BFF_API_URL`; the CIAM and workforce authority / client /
tenant values; the BFF scopes, which on both planes must be `api://{app id}/user_impersonation` - ADR-028 A8).

The BFF URL and scope are chosen at run time from Spaarke-owned configuration (task 041, FR-17); a missing or invalid
backend fails closed - no request is sent and the sign-in screen says so.

Copy `.env.example` to `.env.local` and set real values.
`.env.local` is gitignored — never commit secrets.

---

## Dev Server Proxy

The Vite dev server has an `/api/*` proxy entry (`server.proxy` in `vite.config.ts`), but `bffApiCall` does not use
relative `/api/...` paths: it always builds its URL on the active backend's origin and fails closed when no backend is
configured (an empty `VITE_BFF_API_URL` is not a relative-path mode).

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
