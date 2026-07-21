# Task 010 — SWA static config + deploy workflow (notes)

## Facts read from the repo (not assumed)
- **Build command**: `npm run build` — the ONLY build script in `src/client/external-spa/package.json` (no separate `build:prod`). Vite production build (IIFE) per `vite.config.ts`.
- **Build output dir**: `dist` (`build.outDir: 'dist'` in `vite.config.ts`). Assets emit under `dist/assets/` (`entryFileNames: 'assets/app.js'`, `assetFileNames: 'assets/[name].[ext]'`).
- **No `public/` dir** and no `publicDir` override → Vite does NOT copy the root `staticwebapp.config.json` into `dist`. Workflow stages it explicitly before upload.

## Deliverables
- `src/client/external-spa/staticwebapp.config.json` (source of truth) — `navigationFallback` → `/index.html` excluding `/assets/*` + asset extensions; `globalHeaders` with `Referrer-Policy: no-referrer-or-same-origin`, `Content-Security-Policy: frame-ancestors 'self'`, `X-Content-Type-Options: nosniff`. No `X-Frame-Options` (avoids CSP conflict, FR-04 negative case).
- `.github/workflows/deploy-external-spa.yml` — finalized existing task-003 scaffold: added a "Stage SWA runtime config into build output" step (`cp staticwebapp.config.json dist/`) so the config reaches the deployed content root (upload uses `app_location: dist`, `skip_app_build: true`).

## SWA deployment token secret
- `AZURE_SWA_TOKEN_EXTERNAL_SPA_DEV` (GitHub repository secret; referenced, not hardcoded).

## Framing decision (FR-04)
- `frame-ancestors 'self'` chosen for Phase 1 standalone SWA — explicitly owns the framing decision (denies external embedding). Teams-embedding host allow-listing is a future change, deliberately not speculated here.

## Not in scope / untouched
- `scripts/Deploy-ExternalWorkspaceSpa.ps1` NOT deleted (Power Pages decommission is Phase 3).
- Workflow trigger left as `workflow_dispatch` (push/path auto-deploy trigger belongs to task 014's deploy/parity work).
- No `/api` reverse-proxy route — BFF is a separate origin (`VITE_BFF_API_URL`).

## Validation
- JSON parses cleanly (node require). YAML parses cleanly (PyYAML safe_load); no tabs.
