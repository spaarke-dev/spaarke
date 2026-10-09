// infrastructure/bicep/modules/controlplane-subscription-rbac.bicep
//
// CUSTOMER-SUBSCRIPTION PREREQUISITE -- Owner for the L2 control-plane UAMI on ONE customer's subscription.
//
// T228 (owner D4, 2026-09-30; grant decided by the owner 2026-10-06): every customer has its OWN Azure subscription
// (ADR-027), created by the operator before the run. H1 checks it, H2a ensures rg-spaarke-{customerId}-{env} in it and
// deploys customer.bicep at subscription scope -- which writes ROLE ASSIGNMENTS (the stamp UAMI's Key Vault / AI Search /
// Cosmos / Content Safety / ACS grants, the L2 -> BFF grants). Contributor cannot write role assignments, so the L2
// identity holds Owner on the customer's subscription -- and on no other. H1 registers resource providers and lists the
// subscription's resource groups (one-customer check) with the same grant.
//
// HOW IT IS APPLIED (prereqs.yaml PRQ-S-04 -- an OPERATOR step, once per customer subscription; never by L2 itself):
//   az deployment sub create --subscription <customer-subscription-id> --location <region> \
//     --template-file infrastructure/bicep/modules/controlplane-subscription-rbac.bicep \
//     --parameters principalId=<L2 control-plane UAMI principal id>
// Deterministic guid() name: re-applying is a no-op, and a matching manual grant is adopted.
//
// HISTORY: until T228 platform-controlplane.bicep deployed this module with Contributor on the PLATFORM ("fleet")
// subscription, on the assumption that stamps lived there. Under D-12 no stamp does, so that grant is no longer deployed
// (an existing assignment stays until an owner removes it). The module split itself was forced by BCP120 (a role
// assignment's guid() name cannot use a runtime module output); here principalId is a plain parameter.

targetScope = 'subscription'

@description('Principal (object) id of the L2 control-plane UAMI (shared by the L2 Api and Worker). Required.')
@minLength(36)
param principalId string

// Owner -- customer.bicep writes role assignments (Contributor cannot); owner decision 2026-10-06 (T228).
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/privileged#owner
var ownerRoleId = '8e3af657-a8ff-443c-a75c-2fe8c4bcb635'

resource controlPlaneSubscriptionOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, principalId, ownerRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', ownerRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
    description: 'L2 control plane deploys this customer stamp (H1 checks, H2a RG + customer.bicep incl. its role assignments) -- T228, prereqs.yaml PRQ-S-04'
  }
}

output subscriptionId string = subscription().subscriptionId
output ownerRoleAssignmentName string = controlPlaneSubscriptionOwner.name
