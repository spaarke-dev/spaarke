---
description: Deploy Azure infrastructure (Bicep) and configure Key Vault Secrets. For BFF API use bff-deploy; for Office Add-ins use office-addins-deploy.
tags: [deploy, azure, infrastructure, bicep, key-vault]
techStack: [azure, bicep, key-vault, azure-cli]
appliesTo: ["infrastructure/**", "deploy to azure", "deploy infrastructure", "bicep deploy", "key vault", "azure resource"]
alwaysApply: false
exemplar: infrastructure/bicep/stacks/ai-foundry-stack.bicep
last-reviewed: 2026-05-17
---

# Azure Deploy

> **Category**: Operations
> **Last Reviewed**: 2026-05-17
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2c — refactored: removed stale BFF deploy section [defer to `bff-deploy`]; extracted Office Add-ins to new `office-addins-deploy` skill; fixed 2 broken workflow filename references; renumbered Key Vault from Type 4 to Type 2 since only Infrastructure + Key Vault remain in scope)
> **Exemplar rationale**: `infrastructure/bicep/stacks/ai-foundry-stack.bicep` is a comprehensive Bicep stack demonstrating module composition, parameter passing, and Key Vault wiring.

---

## Quick Reference — Scope of THIS skill

| Deployment Type | Tool | Where to Find |
|-----------------|------|---------------|
| **Azure Infrastructure (Bicep)** | Azure CLI + Bicep | THIS SKILL |
| **Key Vault Secrets** | Azure CLI + Bicep | THIS SKILL |
| BFF API to App Service | `Deploy-BffApi.ps1` + hash-verify | `bff-deploy` skill (the canonical authority — incident-grounded with May 2026 silent-failure hardening) |
| Office Add-ins to SWA | SWA CLI + `Deploy-OfficeAddins.ps1` | `office-addins-deploy` skill |
| Dataverse / PCF / solutions | PAC CLI | `dataverse-deploy`, `pcf-deploy` skills |
| Power Pages Code Site | PAC CLI + Vite build | `power-page-deploy` skill |

**For Dataverse deployments (PCF, solutions, web resources)**: Use the `dataverse-deploy` skill instead.

**Required Reading**: Load [azure-deployment.md](../../constraints/azure-deployment.md) for required App Settings before deploying.

---

## Prerequisites

### Required Tools

```powershell
# Verify installations
az --version          # Azure CLI 2.50+
az bicep version      # Bicep CLI 0.39+
dotnet --version      # .NET SDK 8.0+
```

### Required Access

| Resource | Access Level |
|----------|--------------|
| Azure Subscription | Contributor role |
| Azure AD | Application Administrator (for app registrations) |

### Authentication

```powershell
# Login to Azure
az login

# Set subscription
az account set --subscription "Spaarke SPE Subscription 1"

# Verify
az account show --query "{Name:name, Id:id}" -o table
```

---

## Environment Reference

### Subscriptions & Resource Groups

| Environment | Subscription | Resource Group |
|-------------|--------------|----------------|
| Dev | Spaarke SPE Subscription 1 | `spe-infrastructure-westus2` |
| Prod | (TBD) | `rg-spaarke-{customer}-prod` |

### Azure Resource Endpoints (Dev)

| Service | Endpoint |
|---------|----------|
| BFF API | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| Azure OpenAI | `https://spaarke-openai-dev.openai.azure.com/` |
| AI Search | `https://spaarke-search-dev.search.windows.net/` |
| Document Intelligence | `https://westus2.api.cognitive.microsoft.com/` |
| Key Vault | `https://sprkspaarkedev-aif-kv.vault.azure.net/` |
| AI Foundry Hub | `sprkspaarkedev-aif-hub` |
| AI Foundry Project | `sprkspaarkedev-aif-proj` |

### Key Vault Secrets

| Secret Name | Purpose |
|-------------|---------|
| `openai-api-key` | Azure OpenAI API key |
| `search-admin-key` | AI Search admin key |
| `docintel-api-key` | Document Intelligence key |
| `redis-connection` | Redis connection string |
| `appinsights-key` | Application Insights key |
| `BFF-API-ClientSecret` | BFF app registration client secret — retained for OBO only (Graph/Dataverse app-only use MI per ADR-028) |
| `Communication-WebhookSigningKey` | HMAC-SHA256 key for Communication webhooks (48-byte base64) |
| `EmailProcessing-WebhookSigningKey` | HMAC-SHA256 key for Email webhooks (48-byte base64) |

**Full environment reference**: See `docs/guides/ENVIRONMENT-DEPLOYMENT-GUIDE.md` → Environment Configuration
**Auth-specific runbook**: See [`docs/guides/auth-deployment-setup.md`](../../../docs/guides/auth-deployment-setup.md) — 10 sections including §5 MI Graph permission grants, §6 Dataverse Application User, §7 Exchange ApplicationAccessPolicy
**Canonical auth ADR**: [`ADR-028`](../../adr/ADR-028-spaarke-auth-architecture.md) — function-based contract, MI for outbound, HMAC webhooks, named API keys

---

## Deployment Type 1: Azure Infrastructure (Bicep)

### When to Use

- Creating new Azure resources
- Deploying AI Foundry stack
- Setting up customer environments

### Bicep Stack Locations

| Stack | Path | Purpose |
|-------|------|---------|
| AI Foundry | `infrastructure/bicep/stacks/ai-foundry-stack.bicep` | AI Hub, Project, Storage, KV |
| Model 1 Shared | `infrastructure/bicep/stacks/model1-shared.bicep` | Shared infrastructure |
| Model 2 Full | `infrastructure/bicep/stacks/model2-full.bicep` | Full customer deployment |

### Deploy Infrastructure

```powershell
# Navigate to infrastructure directory
cd infrastructure/bicep

# Deploy AI Foundry stack
az deployment group create `
  --resource-group spe-infrastructure-westus2 `
  --template-file stacks/ai-foundry-stack.bicep `
  --parameters customerId=spaarke environment=dev location=westus2
```

### Verify Deployment

```powershell
# List deployed resources
az resource list `
  --resource-group spe-infrastructure-westus2 `
  --output table
```

### Expected Resources (AI Foundry Stack)

| Resource Type | Name Pattern | Purpose |
|---------------|--------------|---------|
| Storage Account | `sprk{customer}{env}aifsa` | AI Foundry storage |
| Key Vault | `sprk{customer}{env}-aif-kv` | Secrets |
| Log Analytics | `sprk{customer}{env}-aif-logs` | Monitoring |
| App Insights | `sprk{customer}{env}-aif-insights` | APM |
| ML Workspace (Hub) | `sprk{customer}{env}-aif-hub` | AI Foundry Hub |
| ML Workspace (Project) | `sprk{customer}{env}-aif-proj` | AI Foundry Project |

---

## Deployment Type 2: Key Vault Secrets

### Store Secrets

```powershell
# Store OpenAI API key
az keyvault secret set `
  --vault-name sprkspaarkedev-aif-kv `
  --name openai-api-key `
  --value "YOUR-API-KEY"

# Store from Azure resource (auto-retrieve)
az keyvault secret set `
  --vault-name sprkspaarkedev-aif-kv `
  --name openai-api-key `
  --value "$(az cognitiveservices account keys list --name spaarke-openai-dev --resource-group spe-infrastructure-westus2 --query key1 -o tsv)"
```

### Retrieve Secrets

```powershell
# Get secret value
az keyvault secret show `
  --vault-name sprkspaarkedev-aif-kv `
  --name openai-api-key `
  --query value -o tsv
```

---

## Verification Procedures

### API Health Check

```powershell
# Check API is running
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz
# Expected: "Healthy"

curl https://spe-api-dev-67e2xz.azurewebsites.net/ping
# Expected: "pong"
```

### Azure Resource Verification

```powershell
# List resources in resource group
az resource list --resource-group spe-infrastructure-westus2 -o table

# Check specific resource
az webapp show --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --query state
```

### AI Services Verification

```powershell
# Check OpenAI deployments
az cognitiveservices account deployment list `
  --name spaarke-openai-dev `
  --resource-group spe-infrastructure-westus2 `
  -o table

# Check AI Search index
az search index list `
  --service-name spaarke-search-dev `
  --resource-group spe-infrastructure-westus2 `
  -o table
```

---

## Error Handling

| Error | Cause | Solution |
|-------|-------|----------|
| `AuthorizationFailed` | Insufficient permissions | Verify Contributor role on subscription |
| `ResourceNotFound` | Wrong resource group or name | Check resource names in Environment Reference |
| `DeploymentFailed` | Bicep template error | Check `az deployment group show --name {deployment}` for details |
| `KeyVault access denied` | Missing Key Vault policy | Add access policy for your identity |
| `App Service 503` | API not started | Check Application Insights logs |
| `App Service 500` after deploy | DI scope mismatch or startup error | Check `/healthz` response body for error details |
| CLI deploy succeeds but old code runs | Deployment didn't register | Check Deployment Center logs; use Kudu as fallback |

### Deployment Not Taking Effect (Rare)

**Symptom**: `az webapp deploy` reports success, but the API still runs old code.

**Diagnosis**:
1. Check **Deployment Center** → **Logs** in Azure Portal
2. If no new entry with current timestamp, deployment didn't register

**Solution** (in order of preference):
1. **Restart the App Service** - sometimes forces reload of new deployment
2. **Try alternative CLI command**: `az webapp deployment source config-zip`
3. **Use Kudu Zip Push Deploy** as manual fallback (see Option C above)
4. After any method, verify with `curl https://{app}.azurewebsites.net/healthz`

**Root Cause**: This is rare and may indicate Azure-side caching or deployment slot issues. For consistent deployments, use GitHub Actions (Option A).

### Check Deployment Logs

```powershell
# API logs (streaming)
az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2

# Deployment logs
az webapp log deployment show --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
```

---

## Naming Conventions

| Resource Type | Pattern | Example |
|---------------|---------|---------|
| Resource Group | `rg-spaarke-{customer}-{env}` | `rg-spaarke-contoso-prod` |
| App Service | `sprk-{app}-{env}` | `sprk-bff-dev` |
| Key Vault | `sprk{customer}{env}-{purpose}-kv` | `sprkspaarkedev-aif-kv` |
| Storage Account | `sprk{customer}{env}sa` | `sprkspaarkedevsa` |
| Azure OpenAI | `spaarke-openai-{env}` | `spaarke-openai-dev` |
| AI Search | `spaarke-search-{env}` | `spaarke-search-dev` |

**Full naming conventions**: See `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md`

---

## Related Skills

| Skill | Use For |
|-------|---------|
| `dataverse-deploy` | Dataverse solutions, PCF controls, web resources |
| `ribbon-edit` | Ribbon customizations (uses solution export/import) |
| `ci-cd` | CI/CD pipeline status and automated deployment workflows |

---

## CI/CD Integration

### Automated Deployments via GitHub Actions

This skill documents **manual** Azure Infrastructure + Key Vault Secrets work. For automated deployments, see the GitHub Actions workflows:

| Workflow | Trigger | What It Deploys |
|----------|---------|-----------------|
| `.github/workflows/deploy-infrastructure.yml` | Manual trigger (`workflow_dispatch`) | Azure Infrastructure (Bicep stacks) |
| `.github/workflows/deploy-bff-api.yml` | Auto (after CI passes on master) OR manual | BFF API — see `bff-deploy` skill |
| `.github/workflows/deploy-office-addins.yml` | Manual trigger | Office Add-ins SWA — see `office-addins-deploy` skill |
| `.github/workflows/deploy-platform.yml` | Manual trigger | Cross-platform deployment orchestrator |
| `.github/workflows/deploy-promote.yml` | Manual trigger | Promote a deployed artifact between environments |
| `.github/workflows/deploy-slot-swap.yml` | After CI green | Slot swap for App Service blue-green deploys |

**Note**: Earlier docs referenced `deploy-staging.yml` and `deploy-to-azure.yml` — these workflow files do NOT exist. The actual workflow names are listed above (verified 2026-05-17).

### When to Use Manual vs Automated

| Scenario | Use |
|----------|-----|
| Routine infrastructure update | Automated (`deploy-infrastructure.yml` workflow_dispatch) |
| Emergency hotfix on Bicep | Manual deployment (this skill) |
| First-time infrastructure stand-up | Manual deployment (this skill) |
| Debugging deployment issues | Manual deployment (this skill) |
| BFF API deploy | `bff-deploy` skill (DO NOT do BFF deploys via this skill) |

### Trigger Automated Infrastructure Deployment

```powershell
# Trigger infrastructure deployment manually
gh workflow run deploy-infrastructure.yml

# Monitor deployment progress
gh run watch

# View deployment status
gh run list --workflow=deploy-infrastructure.yml --limit 5
```

### Check Deployment Status

```powershell
# View recent deployments
gh run list --workflow=deploy-infrastructure.yml

# View specific run details
gh run view {run-id}

# Download deployment artifacts
gh run download {run-id}
```

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| `docs/guides/ENVIRONMENT-DEPLOYMENT-GUIDE.md` | Comprehensive deployment reference |
| `docs/guides/CUSTOMER-DEPLOYMENT-GUIDE.md` | Customer-facing setup guide |
| `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` | Naming standards |

---

## Operator Notes

- **Avoid discovery queries** - Use endpoint values from Environment Reference above
- **Operational commands OK** - Deployments, secret management, configuration are permitted
- **For Dataverse** - Always use `dataverse-deploy` skill instead of this one
- **Check health first** - Before troubleshooting, verify `/healthz` endpoint
- **Key Vault references** - Use `@Microsoft.KeyVault(SecretUri=...)` syntax in App Settings
- **Dev deploys** - Use deployment scripts for quick iteration; GitHub Actions for production releases
- **Verify deployments** - After manual deploy, check `/healthz` and Deployment Center logs
- **If CLI deploy fails silently** - Try restart first, then Kudu as last resort

### Deployment Scripts (Preferred for Dev)

| Component | Script | Usage |
|-----------|--------|-------|
| **BFF API** | `.\scripts\Deploy-BffApi.ps1` | Full build+deploy in ~1 min |
| **Office Add-ins** | `.\scripts\Deploy-OfficeAddins.ps1` | Full build+deploy in ~30 sec |

Both scripts support `-SkipBuild` flag to deploy existing builds faster.
- **500 errors after deploy** - Check `/healthz` response body for DI scope or startup errors. For BFF-specific errors → use `bff-deploy` skill.

### Out of Scope (defer to other skills)

- **BFF API deploys** → `bff-deploy` skill (canonical authority; includes May 2026 silent-failure hardening with hash-verify + auto-recover)
- **Office Add-ins / SWA deploys** → `office-addins-deploy` skill (extracted from this skill 2026-05-17)
- **Dataverse PCF / solutions** → `dataverse-deploy` / `pcf-deploy` skills
- **Power Pages Code Sites** → `power-page-deploy` skill

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| `az deployment group create` succeeds but resource doesn't appear in portal | Wrong resource group OR provider not registered for the subscription | Verify `az account show` matches expected subscription. Run `az provider register --namespace <namespace>` for first-time use of a new provider. |
| Bicep deploy "succeeds" with `provisioningState: Succeeded` but child resources failed | Top-level deployment doesn't always surface child failures | Always check `az deployment group show --query properties.outputs` AND examine the child operations: `az deployment operation group list --resource-group <rg> --name <deployment>`. |
| Key Vault secret stored but App Settings reference returns null | App Service's managed identity doesn't have `Get` permission on the Key Vault, OR the reference syntax `@Microsoft.KeyVault(SecretUri=...)` has a typo | Grant the App Service's system-assigned MI Key Vault Secrets User role. Verify reference syntax matches exactly (including the literal `@` and proper VaultName/SecretName fields). Test with `az webapp config appsettings list` to see resolved values. |
| AI Foundry stack deploy fails with "Hub not found" mid-deploy | Resource dependencies aren't ordered correctly in the Bicep | The `ai-foundry-stack.bicep` example wires Hub → Project → Connections in correct order. Don't shortcut to deploy Project before Hub exists. |
| Workflow `deploy-staging.yml` or `deploy-to-azure.yml` doesn't exist | Earlier docs referenced these by hopeful name; they were never created | Use the actual workflows listed in `## CI/CD Integration` (verified 2026-05-17). Fixed in this skill version. |
| Deployment in slot 'staging' but production users still on old version | Slot swap step not run after staging validation | Slot deploys to `staging` are intentional — explicit `az webapp deployment slot swap` step is required to promote. Use `deploy-slot-swap.yml` workflow for this. |
