// =============================================================================
// customer-policy.bicep — common policy for every customer subscription (ADR-027, G36)
// =============================================================================
// customer-provisioning-orchestration-r1 task 262. Deployed at the `spaarke-customers` management group
// (management-groups.bicep), so every customer subscription inherits it on joining the group.
//
// RULES (binding for every change to this file):
//   1. BUILT-IN definitions only (`/providers/Microsoft.Authorization/policyDefinitions/{guid}`); no custom definition
//      or initiative. Each id below was read with `az policy definition show` on 2026-10-09 (version noted).
//   2. Nothing here may break an in-flight deployment: effect `Audit` where the definition has an effect parameter;
//      a Deny-only built-in is assigned with enforcementMode `DoNotEnforce` (compliance is evaluated and reported, the
//      deny is never applied). Raising any assignment to Deny is a separate, owner-approved change made only after the
//      compliance view shows every customer stamp compliant. ⚠️ The resource-group tag assignments can NEVER be
//      enforced as they stand: H2a pre-creates rg-spaarke-{customerId}-{env} with NO tags (ArmDeploymentRunner
//      resource-group ensure, a CreateOrUpdate before the template deploy) and customer.bicep adds them a moment
//      later — an enforced "require a tag" would refuse that pre-create and fail H2a on every run.
//   3. Only conditions infrastructure/bicep/customer.bicep already satisfies: the regions it deploys to
//      (location westus2, openAiLocation westus3, contentSafetyLocation westus; ACS is `global`, which the
//      allowed-locations built-in exempts), the tags it sets on its resource group (customer, environment, application,
//      managedBy — never `component`, which it does not set), storage HTTPS-only + TLS 1.2
//      (modules/storage-account.bicep), Key Vault RBAC + purge protection (modules/key-vault.bicep).
//   4. Every assignment carries a non-compliance message naming what to fix.
//   5. Management-group-scope assignment names are at most 24 characters.
//
// Deploy (Deploy-ManagementGroups.ps1 -Apply does this):
//   az deployment mg create --management-group-id spaarke-customers --location westus2 \
//     --template-file infrastructure/bicep/customer-policy.bicep
// =============================================================================

targetScope = 'managementGroup'

@description('Regions a customer stamp deploys to: customer.bicep location (westus2), openAiLocation (westus3) and contentSafetyLocation (westus). Add a region here BEFORE a stamp is deployed there, or the audit reports it.')
param allowedLocations array = [
  'westus2'
  'westus3'
  'westus'
]

@description('Tags every customer resource group must carry. Only tags customer.bicep sets on rg-spaarke-{customerId}-{env} (param `tags`).')
param requiredResourceGroupTags array = [
  'customer'
  'environment'
  'managedBy'
]

// Built-in definition ids (read 2026-10-09 with `az policy definition show --name <id>`).
var builtIn = {
  allowedLocations: 'e56962a6-4747-49cd-b67b-bf8b01975c4c' // Allowed locations — v1.1.0, effect Audit|Deny|Disabled
  allowedResourceGroupLocations: 'e765b5de-1225-4ba3-bd56-1ac6695af988' // Allowed locations for resource groups — v1.1.0, Audit|Deny|Disabled
  requireResourceGroupTag: '96670d01-0a4d-4649-9c89-2d3abc0a5025' // Require a tag on resource groups — v1.0.0, deny only (no effect parameter)
  storageSecureTransfer: '404c3081-a854-4457-ae30-26a93ef643f9' // Secure transfer to storage accounts should be enabled — v2.0.0, Audit|Deny|Disabled
  storageMinimumTls: 'fe83a0eb-a853-422d-aac2-1bffd182c5d0' // Storage accounts should have the specified minimum TLS version — v1.0.0, Audit|Deny|Disabled
  keyVaultRbac: '12d4fa5e-1f9f-4c21-97a9-b99b3c6611b5' // Azure Key Vault should use RBAC permission model — v1.0.1, Audit|Deny|Disabled
  keyVaultPurgeProtection: '0b60c0b2-2dc2-4e1c-b5c9-abbed971de53' // Key vaults should have deletion protection enabled — v2.1.0, Audit|Deny|Disabled
}

var locationList = join(allowedLocations, ', ')

var fixedAssignments = [
  {
    name: 'sprk-locations'
    displayName: 'Spaarke customers: resources in Spaarke regions (audit)'
    definition: builtIn.allowedLocations
    enforcementMode: 'Default'
    parameters: {
      effect: { value: 'Audit' }
      listOfAllowedLocations: { value: allowedLocations }
    }
    message: 'Spaarke customer resources are deployed only to ${locationList}. Move the resource, or add the region to allowedLocations in infrastructure/bicep/customer-policy.bicep before deploying there.'
  }
  {
    name: 'sprk-rg-locations'
    displayName: 'Spaarke customers: resource groups in Spaarke regions (audit)'
    definition: builtIn.allowedResourceGroupLocations
    enforcementMode: 'Default'
    parameters: {
      effect: { value: 'Audit' }
      listOfAllowedLocations: { value: allowedLocations }
    }
    message: 'Spaarke customer resource groups are created only in ${locationList} (customer.bicep `location`). Recreate the group in an allowed region, or add the region to allowedLocations in infrastructure/bicep/customer-policy.bicep.'
  }
  {
    name: 'sprk-st-https'
    displayName: 'Spaarke customers: storage accepts HTTPS only (audit)'
    definition: builtIn.storageSecureTransfer
    enforcementMode: 'Default'
    parameters: {
      effect: { value: 'Audit' }
    }
    message: 'Set supportsHttpsTrafficOnly = true on the storage account (infrastructure/bicep/modules/storage-account.bicep sets it).'
  }
  {
    name: 'sprk-st-tls12'
    displayName: 'Spaarke customers: storage minimum TLS 1.2 (audit)'
    definition: builtIn.storageMinimumTls
    enforcementMode: 'Default'
    parameters: {
      effect: { value: 'Audit' }
      minimumTlsVersion: { value: 'TLS1_2' }
    }
    message: 'Set minimumTlsVersion = TLS1_2 on the storage account (infrastructure/bicep/modules/storage-account.bicep sets it).'
  }
  {
    name: 'sprk-kv-rbac'
    displayName: 'Spaarke customers: Key Vault uses the RBAC permission model (audit)'
    definition: builtIn.keyVaultRbac
    enforcementMode: 'Default'
    parameters: {
      effect: { value: 'Audit' }
    }
    message: 'Set enableRbacAuthorization = true on the key vault; Spaarke grants vault access with Azure RBAC, never access policies (infrastructure/bicep/modules/key-vault.bicep).'
  }
  {
    name: 'sprk-kv-purgeprotect'
    displayName: 'Spaarke customers: Key Vault soft delete + purge protection (audit)'
    definition: builtIn.keyVaultPurgeProtection
    enforcementMode: 'Default'
    parameters: {
      effect: { value: 'Audit' }
    }
    message: 'Enable soft delete and purge protection on the key vault (infrastructure/bicep/modules/key-vault.bicep defaults both to true). Purge protection cannot be turned off once on.'
  }
]

// One assignment per required tag. The built-in has no effect parameter (deny only), so it is assigned with
// enforcementMode DoNotEnforce: the compliance view reports a missing tag; no deployment is ever refused.
var tagAssignments = [
  for tag in requiredResourceGroupTags: {
    name: take('sprk-rgtag-${toLower(tag)}', 24)
    displayName: 'Spaarke customers: resource group tag \'${tag}\' (report only)'
    definition: builtIn.requireResourceGroupTag
    enforcementMode: 'DoNotEnforce'
    parameters: {
      tagName: { value: tag }
    }
    message: 'Spaarke customer resource groups carry the \'${tag}\' tag (customer.bicep param `tags`). Add it to the resource group.'
  }
]

var assignments = concat(fixedAssignments, tagAssignments)

resource policyAssignments 'Microsoft.Authorization/policyAssignments@2024-04-01' = [
  for a in assignments: {
    name: a.name
    properties: {
      displayName: a.displayName
      description: 'Common policy for every Spaarke customer subscription (ADR-027; infrastructure/bicep/customer-policy.bicep, task 262).'
      policyDefinitionId: tenantResourceId('Microsoft.Authorization/policyDefinitions', a.definition)
      enforcementMode: a.enforcementMode
      parameters: a.parameters
      nonComplianceMessages: [
        {
          message: a.message
        }
      ]
      metadata: {
        source: 'infrastructure/bicep/customer-policy.bicep'
        deployedBy: 'scripts/provisioning/Deploy-ManagementGroups.ps1'
      }
    }
  }
]

output assignmentIds array = [for (a, i) in assignments: policyAssignments[i].id]
