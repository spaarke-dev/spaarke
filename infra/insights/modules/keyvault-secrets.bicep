// =====================================================================
// keyvault-secrets.bicep
// Grants the per-tenant UAMI 'Key Vault Secrets User' RBAC on the
// EXISTING Key Vault (sprkspaarkedev-aif-kv in Spaarke Dev).
// Per D-27 / ADR-024: no SAS keys, no new ClientSecretCredential — the
// UAMI reads secrets via Managed Identity + RBAC. The Function App's
// app settings use Key Vault references (@Microsoft.KeyVault(...))
// resolved at app start, not direct secret materials.
//
// Phase 1 shell: this module ONLY grants RBAC. Specific secret URIs
// (OpenAI endpoint, Service Bus connection, etc.) are wired into the
// Function App's app settings by callers in a future task when the
// SPE-upload consumer ships (D-P8 / task 050).
// =====================================================================

@description('Name of existing Key Vault.')
param keyVaultName string

@description('Object (principal) ID of the UAMI to grant access to.')
param uamiPrincipalId string

// Role: Key Vault Secrets User — read secret values (data-plane).
// GUID is well-known: 4633458b-17de-408a-b874-0445c86b69e6
var kvSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource keyVault 'Microsoft.KeyVault/vaults@2024-04-01-preview' existing = {
  name: keyVaultName
}

resource kvSecretsUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  // Deterministic GUID so re-deployment is idempotent.
  name: guid(keyVault.id, uamiPrincipalId, kvSecretsUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: uamiPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output keyVaultId string = keyVault.id
output keyVaultUri string = keyVault.properties.vaultUri
