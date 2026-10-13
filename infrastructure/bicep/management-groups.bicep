// =============================================================================
// management-groups.bicep — Spaarke's management-group hierarchy (ADR-027, G36)
// =============================================================================
// customer-provisioning-orchestration-r1 task 262 (owner approval 2026-10-09: "create the management group + policy").
//
// ADR-027: "MUST use Azure Management Groups for common policy across customer subscriptions" — one subscription per
// customer makes hand-applied policy unscalable. The smallest hierarchy that satisfies it:
//
//   Tenant Root Group
//   └── spaarke-environments   "Spaarke Environments"  (AZURE-RESOURCE-NAMING-CONVENTION.md; Spaarke's own subscriptions)
//       └── spaarke-customers  "Spaarke Customers"     (every customer subscription; customer-policy.bicep is assigned HERE)
//
// The ids are mirrored in scripts/provisioning-prereqs/spaarke-constants.yaml `management_groups` (PRQ-S-06 checks a
// customer subscription's parent against it). tests/scripts/Management-Groups.Tests.ps1 fails when the two disagree.
//
// Subscriptions are NOT declared here: moving one is an explicit, listed operator step
// (scripts/provisioning/Deploy-ManagementGroups.ps1), never a side effect of re-deploying the hierarchy.
//
// Deploy (tenant scope — the caller needs Microsoft.Resources/deployments/write at "/", see the script):
//   az deployment tenant create --location westus2 --template-file infrastructure/bicep/management-groups.bicep
// =============================================================================

targetScope = 'tenant'

@description('Id (name) of the management group that holds Spaarke\'s environment subscriptions. Lowercase-hyphenated; immutable once created.')
param environmentsGroupId string = 'spaarke-environments'

@description('Display name of the environments group (AZURE-RESOURCE-NAMING-CONVENTION.md "Azure Subscription Organization").')
param environmentsGroupDisplayName string = 'Spaarke Environments'

@description('Id (name) of the management group every customer subscription joins; the common customer policy is assigned to it.')
param customersGroupId string = 'spaarke-customers'

@description('Display name of the customers group.')
param customersGroupDisplayName string = 'Spaarke Customers'

// The Tenant Root Group's id is the tenant id.
var rootGroupResourceId = tenantResourceId('Microsoft.Management/managementGroups', tenant().tenantId)

resource environments 'Microsoft.Management/managementGroups@2023-04-01' = {
  name: environmentsGroupId
  properties: {
    displayName: environmentsGroupDisplayName
    details: {
      parent: {
        id: rootGroupResourceId
      }
    }
  }
}

resource customers 'Microsoft.Management/managementGroups@2023-04-01' = {
  name: customersGroupId
  properties: {
    displayName: customersGroupDisplayName
    details: {
      parent: {
        id: environments.id
      }
    }
  }
}

output environmentsGroupResourceId string = environments.id
output customersGroupResourceId string = customers.id
