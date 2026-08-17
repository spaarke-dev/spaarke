// infrastructure/bicep/modules/key-vault.bicep
// Azure Key Vault module for Spaarke infrastructure
// Hardened: network restrictions, secret rotation, least-privilege RBAC, audit logging

// Vault name. Default (caller-supplied) follows canonical `sprk-{env}-kv` per
// AZURE-RESOURCE-NAMING-CONVENTION.md § "KV-Secret & Resource Naming Standard" (R3).
// Dev exception: `spaarke-spekvcert` is a DO-NOT-RENAME live dev-artifact per
// projects/customer-provisioning-orchestration-r1/notes/naming-exception-registry.md
// (owner directive #3, 2026-08-15 · FR-35 · §7.9 R3). Do not rename the dev vault.
@description('Name of the Key Vault. Canonical: sprk-{env}-kv. Dev exception spaarke-spekvcert per naming-exception-registry.md.')
param keyVaultName string

@description('Location for the Key Vault')
param location string = resourceGroup().location

@description('Enable soft delete')
param enableSoftDelete bool = true

@description('Soft delete retention days')
param softDeleteRetentionDays int = 90

@description('Enable purge protection')
param enablePurgeProtection bool = true

@description('SKU for Key Vault')
@allowed(['standard', 'premium'])
param sku string = 'standard'

@description('Principal IDs to grant Key Vault access')
param accessPolicies array = []

@description('Tags for the resource')
param tags object = {}

// ============================================================================
// NETWORK HARDENING PARAMETERS
// ============================================================================

@description('Enable network restrictions (VNet rules + deny by default)')
param enableNetworkRestrictions bool = false

@description('Subnet IDs allowed to access Key Vault (e.g., snet-app for App Service)')
param allowedSubnetIds array = []

@description('Specific IP addresses or CIDR ranges allowed (e.g., developer IPs for debugging)')
param allowedIpRanges array = []

// ============================================================================
// RBAC PARAMETERS
// ============================================================================

@description('Principal ID of the App Service managed identity (granted Key Vault Secrets User). HISTORICALLY the per-customer BFF App Service SystemAssigned MI; task 029 removed the SystemAssigned MI from `modules/app-service.bicep` and task 030 migrates every UAMI-consuming caller to `userAssignedIdentityPrincipalId` below. This param is retained for callers that still supply a legacy principal (interim per plan.md §3 — remove post-Phase F acceptance).')
param appServicePrincipalId string = ''

@description('Principal ID of the per-customer User-Assigned Managed Identity (from `modules/uami.bicep`, task 028) granted Key Vault Secrets User (built-in role `4633458b-17de-408a-b874-0445c86b69e6`). Task 030 canonical target per ADR-028 (all outbound = DefaultAzureCredential over UAMI) + T5 structural fix (UAMI is stable across slot swaps). Empty default skips the assignment (caller-side wiring — see `customer.bicep` / `stacks/*.bicep`). Set principalType=ServicePrincipal on the emitted assignment.')
param userAssignedIdentityPrincipalId string = ''

@description('Principal IDs granted Key Vault Administrator role (deployment pipelines, admins)')
param adminPrincipalIds array = []

// ============================================================================
// DIAGNOSTIC SETTINGS PARAMETERS
// ============================================================================

@description('Log Analytics workspace ID for audit logs (empty to skip)')
param logAnalyticsWorkspaceId string = ''

// ============================================================================
// KEY VAULT
// ============================================================================

// Pre-compute VNet rules and IP rules as variables (Bicep for-expressions
// cannot be used inline within ternary expressions)
var vnetRules = [for subnetId in allowedSubnetIds: {
  id: subnetId
  ignoreMissingVnetServiceEndpoint: false
}]

var ipRules = [for ip in allowedIpRanges: {
  value: ip
}]

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: sku
    }
    enableSoftDelete: enableSoftDelete
    softDeleteRetentionInDays: softDeleteRetentionDays
    enablePurgeProtection: enablePurgeProtection
    enableRbacAuthorization: true
    accessPolicies: [for policy in accessPolicies: {
      tenantId: subscription().tenantId
      objectId: policy.objectId
      permissions: {
        secrets: policy.?secretPermissions ?? ['get', 'list']
        certificates: policy.?certificatePermissions ?? ['get', 'list']
        keys: policy.?keyPermissions ?? ['get', 'list']
      }
    }]
    networkAcls: enableNetworkRestrictions ? {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
      virtualNetworkRules: vnetRules
      ipRules: ipRules
    } : {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// ============================================================================
// SECRET ROTATION POLICY
// Configures auto-notification before secret expiry so rotation can be
// triggered (via Event Grid or manual process). Secrets are set with a
// 365-day expiry and notify 30 days before expiration.
// ============================================================================

// SECRET ROTATION POLICY CONFIGURATION
// Key Vault secret rotation policies are applied per-secret at creation time
// (not declaratively via Bicep). Post-deployment scripts should use these values:
//
//   Secrets requiring rotation:
//     redis-connection-string, servicebus-connection-string,
//     storage-connection-string, openai-api-key, aisearch-admin-key,
//     communication-webhook-secret
//
//   Expiry: 365 days | Notify: 30 days before expiry
//
//   Post-deployment script example:
//     az keyvault secret set --vault-name $vaultName --name $secretName \
//       --value $value --expires $(date -u -d "+365 days" +%Y-%m-%dT%H:%M:%SZ)
//
//   For automated rotation:
//     1. Subscribe to Microsoft.KeyVault.SecretNearExpiry via Event Grid
//     2. Trigger an Azure Function or Logic App to rotate + update the secret
//     3. For beta: diagnostic logs + Log Analytics alert on SecretNearExpiry
//        events provides visibility without requiring a rotation Function

// ============================================================================
// RBAC ROLE ASSIGNMENTS (Least Privilege)
// ============================================================================

// Built-in role definition IDs
// Key Vault Secrets User: read secrets only (for App Service runtime)
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
// Key Vault Administrator: full management (for deployment pipelines)
var keyVaultAdministratorRoleId = '00482a5a-887f-4fb3-b363-3b7fe8e74483'

// Grant App Service managed identity "Key Vault Secrets User" (read-only secrets)
// interim per plan.md §3 — remove post-Phase F acceptance (UAMI grants proven).
// Historically granted the SystemAssigned MI on the per-customer BFF App Service; task 029
// removed the SystemAssigned MI from `modules/app-service.bicep` so callers no longer have
// a live SystemAssigned principal to pass. The canonical Phase-C grant is the sibling
// `uamiSecretsRole` below (UAMI principal, task 030). This block stays wired for any
// residual caller that still supplies a legacy principal (interim safety net) and for
// idempotent re-deploys against environments provisioned pre-Phase-C.
resource appServiceSecretsRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(appServicePrincipalId)) {
  name: guid(keyVault.id, appServicePrincipalId, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: appServicePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Grant User-Assigned Managed Identity "Key Vault Secrets User" (read-only secrets).
// Canonical Phase-C grant per task 030 + ADR-028: every BFF outbound (KV secret reads for
// startup + runtime `@Microsoft.KeyVault(...)` references) authenticates as the UAMI. The
// UAMI's principalId is stable across App Service slot swaps + delete/recreate (T5 structural
// fix), so this assignment does NOT drift the way a SystemAssigned-MI-tied grant would.
// principalType=ServicePrincipal avoids Azure Graph propagation-delay "principal not found"
// deploy failures. Idempotent guid() name allows safe re-deploy.
resource uamiSecretsRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(userAssignedIdentityPrincipalId)) {
  name: guid(keyVault.id, userAssignedIdentityPrincipalId, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: userAssignedIdentityPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Grant admin principals "Key Vault Administrator" (for CI/CD pipelines, ops team)
resource adminRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in adminPrincipalIds: {
  name: guid(keyVault.id, principalId, keyVaultAdministratorRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultAdministratorRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}]

// ============================================================================
// DIAGNOSTIC SETTINGS (Audit Logging)
// ============================================================================

resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (logAnalyticsWorkspaceId != '') {
  name: '${keyVaultName}-diagnostics'
  scope: keyVault
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        categoryGroup: 'audit'
        enabled: true
        retentionPolicy: {
          enabled: true
          days: 90
        }
      }
      {
        categoryGroup: 'allLogs'
        enabled: true
        retentionPolicy: {
          enabled: true
          days: 30
        }
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
        retentionPolicy: {
          enabled: true
          days: 30
        }
      }
    ]
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output keyVaultId string = keyVault.id
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
