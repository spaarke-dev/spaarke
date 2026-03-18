# External SPA — Power Pages Code Page

Single-file React 18 application deployed as a Power Pages Code Page web resource.
It is built with Vite and bundled via `vite-plugin-singlefile` into a single
self-contained `index.html` file that Power Pages serves directly.

---

## Quick Start (local development)

```bash
# 1. Install dependencies
cd src/client/external-spa
npm install

# 2. Copy the example env file and fill in your values
cp .env.example .env.local

# 3. Start the Vite dev server (http://localhost:3000)
npm run dev
```

Open http://localhost:3000 in a browser where you are **already signed in to the
Power Pages portal** (spaarkedev1.powerappsportals.com or whatever
`VITE_PORTAL_URL` points to).

---

## Environment Variables

| Variable | Purpose | Default |
|---|---|---|
| `VITE_BFF_API_URL` | Spaarke BFF API base URL | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| `VITE_PORTAL_URL` | Power Pages portal URL (proxy target) | `https://spaarkedev1.powerappsportals.com` |
| `VITE_PORTAL_CLIENT_ID` | OAuth client ID registered in the portal | *(required)* |

Copy `.env.example` to `.env.local` and set real values.
`.env.local` is gitignored — never commit secrets.

---

## Dev Server Proxy

The Vite dev server proxies three Power Pages route prefixes to the real portal
so that the SPA can call the portal APIs while being served from localhost.

| Proxy path | Forwarded to |
|---|---|
| `/_api/*` | `VITE_PORTAL_URL` |
| `/_layout/*` | `VITE_PORTAL_URL` |
| `/_services/*` | `VITE_PORTAL_URL` |

`changeOrigin: true` rewrites the `Host` header so the portal accepts the
request.  `cookieDomainRewrite: "localhost"` (applied to `/_api`) rewrites
`Set-Cookie` domain attributes so the browser stores portal cookies against
`localhost` and sends them back on subsequent proxied requests — this is what
makes authentication work through the proxy.

Because the proxy runs server-side, the browser's same-origin policy is not an
issue.  All you need to do is make sure you have an active portal session in the
same browser profile before making authenticated API calls.

> Note: the proxy configuration only applies during `npm run dev`.  Production
> builds are deployed to Power Pages and call the portal APIs directly — no proxy
> is involved.

---

## Build

```bash
npm run build
```

Output is a single `dist/index.html` with all JS/CSS inlined.  This file is
uploaded to Dataverse as a web resource and served by Power Pages.

---

## Deployment

See the main project docs and the `dataverse-deploy` skill for the full
deployment procedure.  The short version:

```bash
# From the repository root
pac pcf push   # (not applicable — this is a web resource, not a PCF control)
# Upload dist/index.html via pac or the Power Pages Studio
```
