# Deployment Verification Guide

> **Last Updated**: April 5, 2026
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: New
> **Purpose**: Consolidated verification steps for every deployment type -- what to check after deploying BFF API, PCF controls, code pages, web resources, or infrastructure
> **Audience**: Developer, Operator

---

## Prerequisites

- [ ] Access to the target environment (Azure, Dataverse, or local)
- [ ] `curl` or equivalent HTTP client for endpoint testing
- [ ] `az` CLI (for Azure resources) or `pac` CLI (for Dataverse)
- [ ] Browser with dev tools for UI verification

## Quick Reference

| Deployment | Primary Verification | Skill |
|------------|---------------------|-------|
| BFF API | `curl /healthz` + endpoint 401 check | `bff-deploy` |
| PCF Control | `pac solution list` + browser version footer | `pcf-deploy` |
| Code Page | Dialog opens + expected content renders | `code-page-deploy` |
| Web Resource | Open in browser + check version | `dataverse-deploy` |
| Azure Infrastructure | `az resource list` + component health | `azure-deploy` |

---

## BFF API Verification

**Source skill**: `.claude/skills/bff-deploy/SKILL.md`

### Step 1: Package Size Check

After running `.\scripts\Deploy-BffApi.ps1`, verify the output:

```
[2/4] Creating deployment package...
  Package created: ~61 MB          <-- MUST be 55-65 MB
```

**If package is < 40 MB**: The deployment is incomplete (missing DLLs). Delete `publish/` directory and re-run the script.

### Step 2: Health Check

```bash
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz
# Expected: Healthy
```

**Retries**: The deploy script automatically retries the health check up to 6 times after a 10-second wait.

### Step 3: Ping Endpoint

```bash
curl https://spe-api-dev-67e2xz.azurewebsites.net/ping
# Expected JSON: { "service": "Spe.Bff.Api", "status": "ok", "timestamp": "..." }
```

### Step 4: Endpoint Registration Check (CRITICAL)

**This is the key verification that catches silent failures.** A health check passing does NOT guarantee all endpoints registered.

```bash
# Unauthenticated test -- any auth-protected endpoint should return 401
curl -s -o /dev/null -w "%{http_code}" https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/test/preview-url
# Expected: 401
```

**Interpretation**:
- **401** = Route is registered and requires auth -- deployment succeeded
- **404** = Route did NOT register -- incomplete package, redeploy
- **500** = App has DI or startup errors -- check Application Insights logs

### Step 5: Deployment Center Verification (if deployment seems stuck)

If the code behavior doesn't match what was deployed:

1. Azure Portal -> App Services -> `spe-api-dev-67e2xz` -> **Deployment Center** -> **Logs**
2. Verify a new entry exists with current timestamp
3. If no new entry, deployment did not register -- use Kudu Zip Push Deploy as fallback

### BFF API Verification Checklist

- [ ] Package size shows 55-65 MB
- [ ] `/healthz` returns `Healthy` (HTTP 200)
- [ ] `/ping` returns JSON with `status: ok`
- [ ] Changed endpoints return 401 (not 404) without auth
- [ ] Deployment Center shows new entry with current timestamp

---

## PCF Control Verification

**Source skill**: `.claude/skills/pcf-deploy/SKILL.md`

### Step 1: Solution List Check

```bash
pac solution list | grep -i "{SolutionName}"
```

**Expected**: Solution appears with the version you just deployed (e.g., `1.2.3`).

### Step 2: Version Propagation Check

The version must appear in **all 5 locations** before deployment. Verify after deployment:

```bash
# Check source manifest
grep 'version=' src/client/pcf/{ControlName}/control/ControlManifest.Input.xml

# Check solution XML
grep '<Version>' src/client/pcf/{ControlName}/Solution/solution.xml

# Check pack.ps1
grep '$version' src/client/pcf/{ControlName}/Solution/pack.ps1
```

All three must show the same version number.

### Step 3: Browser Cache Bust

PCF controls are cached aggressively by Dataverse and the browser:

1. **Hard refresh**: `Ctrl+Shift+R` (Chrome/Edge) or `Cmd+Shift+R` (Mac)
2. **Incognito mode**: Open the form in a private window to bypass browser cache
3. **Clear Dataverse cache**: If still stale, run `pac solution delete --solution-name {SolutionName}` and reimport

### Step 4: Version Footer Verification

Every PCF control MUST display its version in the UI (e.g., `v1.2.3 - Built 2026-04-05`):

1. Open the Dataverse form containing the control
2. Locate the version footer (typically bottom of the control)
3. Verify it matches the version you deployed

**If the version footer is stale**: `ControlManifest.Input.xml` version was not incremented -- this is the #1 cause of "deployment succeeded but nothing changed."

### Step 5: Bundle Size Verification

```bash
ls -la src/client/pcf/{ControlName}/out/controls/{ControlName}/bundle.js
```

**Expected sizes**:
- With platform libraries (React 16 + Fluent v9 host-provided): 200-500 KB
- Without platform libraries: > 5 MB (indicates configuration issue)

### Step 6: Shared Library Dist Verification (if shared components modified)

If any files in `@spaarke/ui-components` were modified:

```bash
# Verify dist timestamps are NEWER than src timestamps
stat -c '%y' src/client/shared/Spaarke.UI.Components/dist/components/YourComponent/YourComponent.js
stat -c '%y' src/client/shared/Spaarke.UI.Components/src/components/YourComponent/YourComponent.tsx
```

**If dist is older than src**: Shared lib was not recompiled. Run `npm run build` in the shared lib directory, then rebuild the PCF.

### PCF Verification Checklist

- [ ] `pac solution list` shows the new version
- [ ] All 5 version locations match
- [ ] Bundle size is 200-500 KB (with platform libraries)
- [ ] Version footer in UI matches deployed version (after hard refresh)
- [ ] Shared lib `dist/` is newer than source (if shared components modified)
- [ ] Expected behavior changes are visible in the control

---

## Code Page Verification

**Source skill**: `.claude/skills/code-page-deploy/SKILL.md`

### Step 1: Build Output Verification (MANDATORY)

**Code pages have two build pipelines. Verify the final deployable output exists and is recent.**

**Webpack code pages** (`src/client/code-pages/`):
```bash
ls -la src/client/code-pages/{PageName}/out/sprk_{pagename}.html
# Must be recent timestamp and NOT just bundle.js (inline step must have run)
```

**Vite code pages** (`src/solutions/`):
```bash
ls -la src/solutions/{PageName}/dist/index.html
# Must contain inlined JS/CSS (single self-contained file)
```

### Step 2: Cache Clearing Verification

**Vite and Webpack cache stale shared library code.** Before every build, confirm cache was cleared:

```bash
# Should not exist at build start
ls src/solutions/{PageName}/node_modules/.vite/ 2>&1 || echo "Cache cleared"
ls src/client/code-pages/{PageName}/out/ 2>&1 || echo "Cache cleared"
```

### Step 3: Bundle Content Verification (MANDATORY)

**After building, verify expected changes are in the bundle**:

```bash
# Search for a known string from your change
grep -oP '.{10}your_expected_string.{10}' src/client/code-pages/{PageName}/out/sprk_{pagename}.html
grep -oP '.{10}your_expected_string.{10}' src/solutions/{PageName}/dist/index.html
```

**If the string is NOT found**: Cache was stale. Clear and rebuild.

### Step 4: Upload Verification

After uploading to Dataverse via maker portal:

1. Navigate to **Solutions** -> **Default Solution** -> **Web Resources**
2. Find `sprk_{pagename}` web resource
3. Verify the **Modified On** timestamp is recent
4. Click **Publish**

### Step 5: Dialog Functional Test

1. Open a Dataverse form with the PCF that opens the code page
2. Trigger the dialog (click button, etc.)
3. Verify the dialog:
   - Loads without blank white page
   - Shows expected content (not cached old version)
   - Matches the expected theme (light/dark passed via URL params)
   - Auth initializes correctly (no console errors)

### Code Page Verification Checklist

- [ ] Deployable HTML file exists with recent timestamp
- [ ] Webpack code pages: `build-webresource.ps1` ran (single self-contained HTML, not separate `bundle.js`)
- [ ] Vite code pages: `dist/index.html` contains inlined JS/CSS
- [ ] Expected change strings verified via `grep` on built output
- [ ] Web resource in Dataverse shows recent Modified On timestamp
- [ ] Published after upload
- [ ] Dialog loads without blank page
- [ ] Content matches expected version (no browser cache)
- [ ] No console errors during auth initialization

---

## Web Resource Verification (General)

**Source skill**: `.claude/skills/dataverse-deploy/SKILL.md`

### Step 1: Solution Component Count

After solution import:

```bash
pac solution list | grep -i "{SolutionName}"
```

Then in Power Apps maker portal, open the solution and verify the component count is > 0.

**If solution imports but shows 0 components**: `Customizations.xml` has empty component sections. Use `pac pcf push --publisher-prefix sprk` as fallback.

### Step 2: Publish Customizations

```bash
pac solution publish
```

### Step 3: Browser Verification

1. Open the form or page using the web resource
2. Hard refresh (`Ctrl+Shift+R`)
3. Verify new behavior is present

### Web Resource Verification Checklist

- [ ] `pac solution list` shows the new version
- [ ] Solution shows > 0 components in portal
- [ ] Customizations published
- [ ] New behavior visible after hard refresh

---

## Infrastructure Verification

**Source skill**: `.claude/skills/azure-deploy/SKILL.md`

### Step 1: Deployment Success

```bash
# List resources in the target resource group
az resource list --resource-group spe-infrastructure-westus2 --output table
```

### Step 2: Expected Resources Present

For the AI Foundry stack, verify these resources exist:

| Resource Type | Name Pattern | Example |
|---------------|--------------|---------|
| Storage Account | `sprk{customer}{env}aifsa` | `sprkspaarkedevaifsa` |
| Key Vault | `sprk{customer}{env}-aif-kv` | `sprkspaarkedev-aif-kv` |
| Log Analytics | `sprk{customer}{env}-aif-logs` | `sprkspaarkedev-aif-logs` |
| App Insights | `sprk{customer}{env}-aif-insights` | `sprkspaarkedev-aif-insights` |
| ML Workspace (Hub) | `sprk{customer}{env}-aif-hub` | `sprkspaarkedev-aif-hub` |
| ML Workspace (Project) | `sprk{customer}{env}-aif-proj` | `sprkspaarkedev-aif-proj` |

### Step 3: App Service Health

```bash
# Verify App Service is running
az webapp show --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --query state
# Expected: "Running"
```

### Step 4: Key Vault Access

```bash
# Verify Key Vault is accessible
az keyvault show --name sprkspaarkedev-aif-kv --query properties.provisioningState
# Expected: "Succeeded"
```

### Step 5: AI Services

```bash
# Verify OpenAI deployments
az cognitiveservices account deployment list \
  --name spaarke-openai-dev \
  --resource-group spe-infrastructure-westus2 \
  -o table

# Verify AI Search indexes
az search index list \
  --service-name spaarke-search-dev \
  --resource-group spe-infrastructure-westus2 \
  -o table
```

### Step 6: Bicep Deployment Verification

For Bicep-based deployments:

```bash
# Check deployment status
az deployment sub show --name bicep-{env}-{timestamp} --query properties.provisioningState
# Expected: "Succeeded"

# Review deployment outputs
az deployment sub show --name bicep-{env}-{timestamp} --query properties.outputs
```

### Infrastructure Verification Checklist

- [ ] All expected resources exist in target resource group
- [ ] App Service state is `Running`
- [ ] Key Vault provisioning state is `Succeeded`
- [ ] OpenAI model deployments are listed
- [ ] AI Search indexes exist
- [ ] Bicep deployment shows `Succeeded`
- [ ] API `/healthz` endpoint returns `Healthy` (cross-verifies end-to-end connectivity)

---

## Post-Deployment Smoke Tests

Regardless of component type, run these smoke tests after any deployment that affects the end-user experience:

### API Smoke Test

```bash
# Health
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz
# Ping
curl https://spe-api-dev-67e2xz.azurewebsites.net/ping
```

### End-to-End Smoke Test

1. Open Dataverse form in browser (hard refresh)
2. Trigger a workflow that exercises the deployed component
3. Verify:
   - No console errors
   - Expected UI elements render
   - Network requests succeed (check DevTools Network tab for 4xx/5xx)
   - Expected behavior is observed

---

## Troubleshooting

| Symptom | Likely Cause | Fix |
|---------|--------------|-----|
| BFF `/healthz` passes but endpoints return 404 | Incomplete zip package (< 40 MB) | Re-run `Deploy-BffApi.ps1`, verify package is 55-65 MB |
| BFF deployment succeeds but old code runs | Deployment didn't register | Check Deployment Center logs; use Kudu Zip Push Deploy |
| PCF shows old version after hard refresh | `ControlManifest.Input.xml` version not incremented | Increment version, rebuild, redeploy (or use `pac solution delete` then reimport) |
| PCF changes not visible despite rebuild | Shared lib `dist/` is stale | Recompile shared lib: `cd src/client/shared/Spaarke.UI.Components && npm run build` |
| Code page dialog shows blank white page | `build-webresource.ps1` not run -- HTML references external `bundle.js` | Run inline step, re-upload |
| Code page shows old version | Vite/Webpack cache stale | `rm -rf dist/ node_modules/.vite/ .vite/` then rebuild |
| Solution imports but shows 0 components | Empty `<CustomControls />` in Customizations.xml | Use `pac pcf push --publisher-prefix sprk` fallback |
| Bicep deployment fails with `AuthorizationFailed` | Missing Contributor role | Verify role assignment on subscription |
| App Service returns 500 after deploy | DI scope mismatch or missing config | Check `/healthz` response body; review Application Insights logs |
| CORS errors in browser | Missing allowed origin in App Settings | Add origin to `Cors:AllowedOrigins:N` via `az webapp config appsettings set` |

---

## Related

- [CONFIGURATION-MATRIX.md](CONFIGURATION-MATRIX.md) -- Configuration settings reference
- [ENVIRONMENT-DEPLOYMENT-GUIDE.md](ENVIRONMENT-DEPLOYMENT-GUIDE.md) -- Full environment deployment procedures
- [PCF-DEPLOYMENT-GUIDE.md](PCF-DEPLOYMENT-GUIDE.md) -- Detailed PCF deployment workflow
- [bff-deploy skill](../../.claude/skills/bff-deploy/SKILL.md) -- BFF API deployment procedure
- [pcf-deploy skill](../../.claude/skills/pcf-deploy/SKILL.md) -- PCF control deployment procedure
- [code-page-deploy skill](../../.claude/skills/code-page-deploy/SKILL.md) -- Code page deployment procedure
- [dataverse-deploy skill](../../.claude/skills/dataverse-deploy/SKILL.md) -- General Dataverse operations
- [azure-deploy skill](../../.claude/skills/azure-deploy/SKILL.md) -- Azure infrastructure operations

---

*Last updated: April 5, 2026*
