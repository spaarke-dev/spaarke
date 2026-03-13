// infrastructure/bicep/modules/key-vault.bicep
// Azure Key Vault module for Spaarke infrastructure
// Hardened: network restrictions, secret rotation, least-privilege RBAC, audit logging

@description('Name of the Key Vault')
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

@description('Principal ID of the App Service managed identity (granted Key Vault Secrets User)')
param appServicePrincipalId string = ''

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
resource appServiceSecretsRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(appServicePrincipalId)) {
  name: guid(keyVault.id, appServicePrincipalId, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: appServicePrincipalId
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
