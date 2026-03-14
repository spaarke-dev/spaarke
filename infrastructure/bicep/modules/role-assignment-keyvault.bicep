// infrastructure/bicep/modules/role-assignment-keyvault.bicep
// Assigns an RBAC role to a principal on a specific Key Vault.
// Used by parent stacks to wire App Service managed identity after both
// Key Vault and App Service are deployed.

@description('Name of the existing Key Vault')
param keyVaultName string

@description('Principal ID to assign the role to')
param principalId string

@description('Built-in role definition GUID (e.g., Key Vault Secrets User)')
param roleDefinitionId string

@description('Type of principal')
@allowed(['ServicePrincipal', 'User', 'Group'])
param principalType string = 'ServicePrincipal'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, principalId, roleDefinitionId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleDefinitionId)
    principalId: principalId
    principalType: principalType
  }
}

output roleAssignmentId string = roleAssignment.id
