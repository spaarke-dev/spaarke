# Azure Resource Naming Convention

> **Version**: 1.0  
> **Date**: December 5, 2025  
> **Status**: Proposed

## Overview

This document establishes a consistent naming convention for all Azure resources in the Spaarke deployment package. It addresses the legacy mix of `spe`, `sdap`, and `spaarke` prefixes found in existing resources.

---

## Current State: Legacy Names Audit

### Azure Resources (Production)

| Resource Type | Current Name | Location | Issue |
|--------------|--------------|----------|-------|
| Key Vault | `spaarke-spekvcert` | Azure Portal | Mixed `spaarke` + `spe` |
| App Registration | `spe-bff-api` | Entra ID | Uses legacy `spe` prefix |
| API Scope | `api://spe-bff-api/user_impersonation` | Entra ID | Uses legacy `spe` prefix |
| Service Bus Namespace | `spaarke-servicebus-dev` | Azure Portal | Uses `spaarke` prefix |
| Service Bus Queue | `sdap-jobs` | Azure Portal | Uses legacy `sdap` prefix |
| Service Bus Queue | `document-events` | Azure Portal | Good - descriptive name |
| Redis Instance | `sdap-dev:` | Config | Uses legacy `sdap` prefix |
| Storage Container | Various | Azure Portal | Mixed naming |

### Code References

| Location | Current Reference | Impact |
|----------|-------------------|--------|
| `appsettings.json` | `spaarke-spekvcert.vault.azure.net` | Key Vault URL |
| `appsettings.json` | `sdap-dev:` Redis instance | Redis key prefix |
| PCF Controls | `api://spe-bff-api/user_impersonation` | OAuth scope |
| Service Bus config | `sdap-jobs` queue | Queue name |
| Bicep modules | `sdap-jobs` | Infrastructure as Code |

---

## Proposed Naming Convention

### Principles

1. **Consistency**: All resources use the `sprk` or `spaarke` prefix
2. **Environment-aware**: Environment suffix for isolation (`-dev`, `-staging`, `-prod`)
3. **Purpose-clear**: Resource type and function obvious from name
4. **Length-conscious**: Stay within Azure naming limits

### Standard Format

```
{prefix}-{component}-{env}
```

Where:
- **prefix**: `sprk` (short) or `spaarke` (full) based on resource type limits
- **component**: What the resource does
- **env**: `dev`, `staging`, `prod`

### Resource Naming Matrix

| Resource Type | Max Length | Convention | Example Dev | Example Prod |
|--------------|------------|------------|-------------|--------------|
| Resource Group | 90 | `rg-spaarke-{purpose}-{env}` | `rg-spaarke-shared-dev` | `rg-spaarke-shared-prod` |
| Key Vault | 24 | `sprk-{purpose}-{env}-kv` | `sprk-shared-dev-kv` | `sprk-shared-prod-kv` |
| App Service | 60 | `sprk-{app}-{env}` | `sprk-bff-dev` | `sprk-bff-prod` |
| App Service Plan | 40 | `sprk-{tier}-{env}-plan` | `sprk-shared-dev-plan` | `sprk-shared-prod-plan` |
| App Registration | 120 | `spaarke-{purpose}-{env}` | `spaarke-bff-api-dev` | `spaarke-bff-api-prod` |
| API Scope URI | N/A | `api://{appId}/user_impersonation` | Use GUID (see below) |
| Service Bus | 50 | `sprk-{purpose}-{env}-sb` | `sprk-shared-dev-sb` | `sprk-shared-prod-sb` |
| Queue | 260 | `{purpose}` | `document-processing` | `document-processing` |
| Redis | 63 | `sprk-{purpose}-{env}` | `sprk-cache-dev` | `sprk-cache-prod` |
| Redis Instance | N/A | `sprk-{env}:` | `sprk-dev:` | `sprk-prod:` |
| Storage Account | 24 | `sprk{purpose}{env}sa` | `sprkshareddevsa` | `sprksharedprodsa` |
| Application Insights | 255 | `sprk-{app}-{env}-insights` | `sprk-bff-dev-insights` | `sprk-bff-prod-insights` |
| Log Analytics | 63 | `sprk-{purpose}-{env}-logs` | `sprk-shared-dev-logs` | `sprk-shared-prod-logs` |

---

## Migration Plan: Legacy to New Names

### Phase 1: New Deployments Only (Immediate)

Update Bicep/IaC to use new naming convention. Existing resources remain unchanged.

**Updated Bicep Variables:**
```bicep
// infrastructure/bicep/stacks/model1-shared.bicep
var resourceGroupName = 'rg-spaarke-shared-${environment}'
var baseName = 'sprk${environment}'

// Key Vault - was: spaarke-spekvcert
var keyVaultName = 'sprk-shared-${environment}-kv'

// Service Bus - was: spaarke-servicebus-dev
var serviceBusName = 'sprk-${environment}-sb'

// Queue names - was: sdap-jobs
var queueNames = [
  'document-processing'   // was: sdap-jobs
  'document-indexing'
  'ai-indexing'
]

// Redis instance name - was: sdap-dev:
var redisInstanceName = 'sprk-${environment}:'
```

### Phase 2: Configuration Updates (Next Sprint)

Update application configuration to support both old and new names:

**appsettings.json - Environment Variable Override:**
```json
{
  "Redis": {
    "InstanceName": "${REDIS_INSTANCE_NAME:sprk-dev:}"
  },
  "ConnectionStrings": {
    "ServiceBus": "${SERVICEBUS_CONNECTION_STRING}"
  },
  "Jobs": {
    "QueueName": "${JOB_QUEUE_NAME:document-processing}"
  }
}
```

### Phase 3: App Registration Scope (Requires Planning)

**Critical**: The `api://spe-bff-api/user_impersonation` scope is hard-coded in PCF controls.

**Options:**
1. **Keep existing** - App registration friendly name can stay `spe-bff-api`
2. **Use App ID URI** - Change to `api://{client-id}/user_impersonation` (GUID-based)
3. **Add alias** - Add new scope while keeping old one (transition period)

**Recommendation:** Use Application ID GUID instead of friendly name:

```typescript
// PCF msalConfig.ts - BEFORE
scopes: ['api://spe-bff-api/user_impersonation']

// PCF msalConfig.ts - AFTER (use environment variable)
scopes: [`api://${process.env.BFF_API_CLIENT_ID}/user_impersonation`]
```

---

## Customer Deployment (Model 2) Naming

For customer-hosted deployments, use customer prefix:

```
{customer}-sprk-{component}-{env}
```

**Example (Contoso):**
| Resource | Name |
|----------|------|
| Resource Group | `rg-contoso-sprk-prod` |
| Key Vault | `contoso-sprk-kv` |
| App Service | `contoso-sprk-bff` |
| Service Bus | `contoso-sprk-sb` |
| Redis | `contoso-sprk-cache` |

---

## Files Requiring Updates

### High Priority (Affects Build/Deploy)

| File | Current Reference | Update To |
|------|-------------------|-----------|
| `src/server/api/Sprk.Bff.Api/appsettings.json` | `spaarke-spekvcert` | `sprk-shared-{env}-kv` |
| `src/server/api/Sprk.Bff.Api/appsettings.json` | `sdap-dev:` | `sprk-{env}:` |
| `src/server/api/Sprk.Bff.Api/appsettings.json` | `sdap-jobs` | `document-processing` |
| `infrastructure/bicep/modules/service-bus.bicep` | `sdap-jobs` default | `document-processing` |
| `infrastructure/bicep/stacks/*.bicep` | Various | New naming |
| `docker-compose.yml` | `sdap-servicebus-emulator` | `sprk-servicebus-emulator` |
| `docker-compose.yml` | `sdap-network` | `sprk-network` |

### Medium Priority (Documentation)

| File | Notes |
|------|-------|
| `docs/reference/articles/SDAP-ARCHITECTURE-GUIDE-FULL-VERSION.md` | Update all resource names |
| `docs/reference/articles/AUTHENTICATION-ARCHITECTURE-GUIDE-FULL-VERSION.md` | Update Key Vault refs |
| `docs/ai-knowledge/guides/DATAVERSE-AUTHENTICATION-GUIDE.md` | Update Key Vault refs |

### Lower Priority (PCF Controls - Requires Testing)

| File | Current | Notes |
|------|---------|-------|
| `src/client/pcf/*/services/auth/msalConfig.ts` | `api://spe-bff-api/` | Consider using App ID GUID |
| `src/client/pcf/*/services/auth/MsalAuthProvider.ts` | Comments only | Documentation updates |

---

## Environment Variable Strategy

To support flexible naming across environments, use environment variables:

```bash
# App Service Configuration
KEY_VAULT_NAME=sprk-shared-prod-kv
REDIS_INSTANCE_NAME=sprk-prod:
SERVICEBUS_NAMESPACE=sprk-prod-sb
JOB_QUEUE_NAME=document-processing
BFF_API_CLIENT_ID=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
```

**appsettings.Production.json:**
```json
{
  "Redis": {
    "InstanceName": "${REDIS_INSTANCE_NAME}"
  },
  "ConnectionStrings": {
    "ServiceBus": "@Microsoft.KeyVault(SecretUri=https://${KEY_VAULT_NAME}.vault.azure.net/secrets/ServiceBus-ConnectionString)"
  }
}
```

---

## Next Steps

1. **Immediate**: Update Bicep modules to use new naming (new deployments)
2. **Sprint 3**: Update appsettings to use environment variables
3. **Sprint 4**: Plan PCF scope migration strategy
4. **Future**: Deprecate legacy resource names in existing environments

---

## Reference: Azure Naming Limits

| Resource | Max Length | Valid Characters |
|----------|-----------|------------------|
| Resource Group | 90 | Alphanumerics, underscores, hyphens, periods, parentheses |
| Key Vault | 24 | Alphanumerics and hyphens (start with letter) |
| Storage Account | 24 | Lowercase letters and numbers only |
| App Service | 60 | Alphanumerics and hyphens |
| Service Bus | 50 | Alphanumerics and hyphens (start with letter) |
| Redis | 63 | Alphanumerics and hyphens |
