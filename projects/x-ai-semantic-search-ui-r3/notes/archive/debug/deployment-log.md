# Deployment Log — SemanticSearch Code Page

## Build #1 — 2026-02-25

| Field | Value |
|-------|-------|
| **Timestamp** | 2026-02-25 10:15 |
| **Build Tool** | webpack 5.105.2 + esbuild-loader |
| **Build Time** | ~9.3s |
| **bundle.js** | 1,186,955 bytes (1.13 MiB) |
| **sprk_semanticsearch.html** | 1,187,488 bytes (1.16 MiB) |
| **Build Warnings** | 3 (asset size limit — expected for bundled code page) |
| **Build Errors** | 0 |

### Build Commands

```bash
cd src/client/code-pages/SemanticSearch
npm run build          # webpack production build → out/bundle.js
pwsh -File build-webresource.ps1   # inline JS → out/sprk_semanticsearch.html
```

### Verification

- [x] npm run build exits with code 0
- [x] out/bundle.js exists (1.13 MiB)
- [x] build-webresource.ps1 exits successfully
- [x] out/sprk_semanticsearch.html exists (1.16 MiB)
- [x] HTML is self-contained (no external .js or .css references)
- [x] JS is inlined within `<script>` tag

### Deployment to Dataverse

**Status**: Build ready — awaiting manual upload

**Deploy instructions**:
1. Open https://make.powerapps.com → Environment: SPAARKE DEV 1
2. Navigate to Solutions → SpaarkeCore (or appropriate solution)
3. Add existing → Web resource → `sprk_semanticsearch` (or create new)
4. Name: `sprk_semanticsearch`
5. Display Name: `Semantic Search`
6. Type: Webpage (HTML)
7. Upload: `src/client/code-pages/SemanticSearch/out/sprk_semanticsearch.html`
8. Save → Publish All Customizations

**Post-deployment verification**:
- Navigate to: `https://spaarkedev1.crm.dynamics.com/WebResources/sprk_semanticsearch`
- Verify: Search input renders
- Verify: Domain tabs render (Documents, Matters, Projects, Invoices)
- Verify: No JavaScript console errors

---

## BFF API Deployment — 2026-02-25

| Field | Value |
|-------|-------|
| **Timestamp** | 2026-02-25 10:30 |
| **Target** | spe-api-dev-67e2xz (Azure App Service) |
| **Package Size** | 64.76 MB |
| **Deploy Method** | scripts/Deploy-BffApi.ps1 |
| **Health Check** | GET /healthz → 200 OK |
| **Ping** | GET /ping → "pong" |
| **Unit Tests** | 177/177 passed (SemanticSearch + RecordSearch) |

### New/Enhanced Endpoints Deployed

- `POST /api/ai/search` — enhanced with `scope=all` and `entityTypes` filter
- `POST /api/ai/search/records` — new entity record search endpoint
- `GET /healthz` — health check (verified 200)
- `GET /ping` — liveness check (verified pong)

### Notes

- Deploy script health check initially timed out (app needed longer startup) but API confirmed healthy after manual check
- All 177 project-related unit tests pass; pre-existing failures in unrelated tests (ClauseComparisonHandler, OfficeEndpoints, etc.) are not from this project

---

*Log maintained by task-execute skill during Task 070/072 execution.*
