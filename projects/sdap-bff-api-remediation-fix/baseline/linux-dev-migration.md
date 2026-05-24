# Linux Dev BFF Migration — Migration Record

> **Task**: 019 (pre-Phase-2 infra)
> **Date**: 2026-05-24
> **Driver**: Phase 1 Finding 1 — dev was Windows; demo + prod are Linux; need consistency for "shelf" canonical pattern + customer-deployment fidelity
> **Outcome**: ✅ SUCCESS — `spaarke-bff-dev` (Linux) provisioned, app deployed, all auth flows verified via existing UAMI

---

## What changed

| | Before | After |
|---|---|---|
| Dev App Service | `spe-api-dev-67e2xz` (Windows, `kind: app`, P2v3) | + `spaarke-bff-dev` (Linux, `kind: app,linux`, P2v3) |
| Dev Resource Group | `spe-infrastructure-westus2` (legacy) | + `rg-spaarke-dev` (canonical naming) |
| App Service Plan | `spe-plan-dev-67e2xz` (Windows P2v3) | + `spaarke-dev-plan` (Linux P2v3) |
| Subscription | `484bc857-3802-427f-9ea5-ca47b43db0f0` | (same) |
| UAMI used | `mi-bff-api-dev` (lives in old RG) | (same — attached cross-RG to new App Service) |
| Cross-platform auth wiring | Dataverse App User + Graph app roles + Exchange policy bound to UAMI principal | (unchanged — UAMI is reused) |
| Old Windows dev | Active | Still active (parallel run; decommission later) |

**Parallel-running state**: both `spe-api-dev-67e2xz` (Windows) and `spaarke-bff-dev` (Linux) are healthy and serving. Operator decides cutover timing.

---

## Why this was needed

design.md §2.4 assumed all BFF App Services are Linux. Phase 1 (task 017) discovered dev was actually Windows — a deviation from the canonical pattern codified in `infrastructure/bicep/modules/app-service.bicep` (hardcoded `kind: 'app,linux'` + `linuxFxVersion: 'DOTNETCORE|8.0'`). The Bicep template IS the canonical pattern; dev being Windows was an accident.

Other envs:
- `spaarke-bff-demo`: Linux (`kind: app,linux`, `reserved: true`)
- `spaarke-bff-prod`: Linux (`kind: app,linux`, `reserved: true`) — exists but not actively used yet
- Customer deployments would follow `production-release.md` → Linux App Service

Aligning dev to Linux ensures: test fidelity (dev behavior matches prod), eliminates the Windows-specific FAILURE-MODES G-2 file-lock incident at the dev tier, matches the canonical "shelf" template.

---

## Critical enabler: User-Assigned Managed Identity (UAMI)

The BFF uses a **User-Assigned Managed Identity** (`mi-bff-api-dev`, clientId `5967251e-171c-46fe-a6c2-ef843c90309d`) for ALL outbound auth (Graph, Dataverse, Key Vault). The UAMI is a separate Azure resource that survives App Service replacement.

This made the migration far simpler than expected:
- ✅ UAMI attached cross-RG to new App Service (`az webapp identity assign --identities $UAMI_ID`)
- ✅ All cross-platform registrations (Dataverse Application User, Graph app role grants, Key Vault role assignments, Exchange ApplicationAccessPolicy) bound to UAMI principal — **NOT** to App Service identity
- ✅ Zero re-registration needed
- ✅ Auth verified at first deploy via `/healthz/dataverse` → 200 "Dataverse connection successful"

**Per docs/guides/auth-deployment-setup.md note**: that runbook says "system-assigned managed identity" — that's stale documentation drift from auth-r2. Code at `GraphClientFactory.cs:64` reads `Graph:ManagedIdentity:ClientId` (UAMI-aware). Doc fix flagged as separate small follow-up commit.

---

## What I did (provisioning steps)

```bash
# 1. Resource group
az group create --name rg-spaarke-dev --location westus2 --subscription 484bc857-...

# 2. App Service Plan (Linux P2v3)
az appservice plan create --name spaarke-dev-plan --resource-group rg-spaarke-dev \
  --location westus2 --sku P2v3 --is-linux

# 3. App Service (Linux .NET 8)
az webapp create --name spaarke-bff-dev --resource-group rg-spaarke-dev \
  --plan spaarke-dev-plan --runtime "DOTNETCORE:8.0"

# 4. Attach UAMI (cross-RG)
az webapp identity assign --name spaarke-bff-dev --resource-group rg-spaarke-dev \
  --identities "/subscriptions/484bc857-.../resourceGroups/spe-infrastructure-westus2/providers/Microsoft.ManagedIdentity/userAssignedIdentities/mi-bff-api-dev"

# 5. siteConfig (httpsOnly, alwaysOn, http2, minTls 1.2, ftps disabled, healthCheck /healthz)
az webapp update --name spaarke-bff-dev --resource-group rg-spaarke-dev --https-only true
az webapp config set --name spaarke-bff-dev --resource-group rg-spaarke-dev \
  --always-on true --http20-enabled true --min-tls-version 1.2 --ftps-state Disabled
az webapp config set --name spaarke-bff-dev --resource-group rg-spaarke-dev \
  --generic-configurations '{"healthCheckPath":"/healthz"}'

# 6. App Settings (173 of 175 — skipped 2 colon-keys: AzureStorage:ConnectionString, PowerPages:BaseUrl)
# Applied via PUT to /config/appsettings REST endpoint with bearer token

# 7. Deploy via parameterized script
.\scripts\Deploy-BffApi.ps1 -ResourceGroupName rg-spaarke-dev -AppServiceName spaarke-bff-dev -Environment dev
```

---

## App Settings handling

Source: 175 App Settings copied from `spe-api-dev-67e2xz`.

**Skipped (2)**: Colon-separator keys forbidden on Linux App Service:
- `AzureStorage:ConnectionString` — duplicate of `__`-style sibling
- `PowerPages:BaseUrl` — duplicate of `__`-style sibling

Both had `__` siblings already present, so functionality preserved.

**Misdirected references reviewed (2)**:
- `Communication__WebhookNotificationUrl` = `https://spe-api-dev-67e2xz.azurewebsites.net/api/communications/incoming-webhook` → **left as-is** during parallel run. Graph webhooks keep hitting Windows dev. At cutover: update this setting + re-PATCH Graph subscription `notificationUrl` (operator action).
- `Analysis__AgentService__Endpoint` = AI Foundry endpoint in `spe-infrastructure-westus2` RG → no change needed (cross-RG call is fine).

---

## Deploy timing observations

- First-boot on brand-new Linux App Service: **~13 minutes** wall-clock total
- `Deploy-BffApi.ps1` timed out at its own 10-min healthcheck window
- Auto-recover (stop → Kudu zipdeploy → start) was triggered; succeeded but slow
- Docker logs show site started at T+13min: `Site startup probe succeeded after 10.0167645 seconds. Site is running with deployment version: 31f11202-...`

**Implication for bff-deploy SKILL**: The 120s health-check window in the script is sized for warm-restart cold starts. First deploy to a brand-new App Service takes substantially longer (container image pull + first-time .NET assembly JIT). Subsequent deploys to this App Service will be normal speed (~3-5 min). No script change needed; first deploy is one-time.

---

## Verification performed

| Check | Result | Notes |
|---|---|---|
| `/healthz` | 200 in 397ms | Anonymous health endpoint |
| `/ping` | 200 + "pong" in 280ms | Anonymous warmup endpoint |
| `/api/documents/test/preview-url` (no auth) | 401 | Route registered + auth required (correct) |
| `/healthz/dataverse` | 200 + "Dataverse connection successful" | **UAMI → Dataverse verified end-to-end** |
| App Settings count | 173 (started from 175; 2 colon-keys skipped, both had `__` siblings) | All Key Vault refs preserved |
| Old Windows dev still healthy | 200 | Parallel run maintained |

---

## Cost impact

- **+1 App Service Plan (Linux P2v3)** at ~$285/month
- Total: ~$285/month additional UNTIL old Windows dev is decommissioned
- Reversibility: HIGH (delete `rg-spaarke-dev` removes everything; no impact to old dev)

---

## What's NOT done (operator-owned cutover items)

These don't block parallel running but must complete before decommissioning Windows dev:

1. **Update `Communication__WebhookNotificationUrl`** on new dev to its own URL (`https://spaarke-bff-dev.azurewebsites.net/api/communications/incoming-webhook`)
2. **PATCH Graph subscription notificationUrl** to point at new dev (via Graph API call against existing subscription IDs)
3. **Update `Cors__AllowedOrigins`** if any client surfaces reference the old hostname
4. **Update GitHub workflow** `deploy-bff-api.yml` to default to new env (currently still targets `spe-api-dev-67e2xz`)
5. **Update any external test scripts / monitoring** that hit `spe-api-dev-67e2xz.azurewebsites.net`
6. **24-48h bake on new dev** before decommissioning old
7. **Delete old**: `spe-api-dev-67e2xz` App Service + `spe-plan-dev-67e2xz` App Service Plan (UAMI stays — it's used elsewhere)

Optional follow-on: rename `spe-infrastructure-westus2` RG content to fit new naming convention (separate cleanup project).

---

## Documentation drift discovered (separate fix)

`docs/guides/auth-deployment-setup.md` line 50 says:
> "App Service (BFF API) ... Must have a system-assigned managed identity enabled"

Actual implementation (since auth-r2 Phase C):
- Code reads `Graph:ManagedIdentity:ClientId` (UAMI clientId)
- Production uses User-Assigned MI (`mi-bff-api-dev`), not System-Assigned

This is auth-r2 documentation drift. Customer following the runbook would set up the wrong MI type. Will be fixed as a separate small commit.
