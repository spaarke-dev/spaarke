// infrastructure/bicep/modules/controlplane-subscription-rbac.bicep
//
// L2 CONTROL-PLANE subscription-scope RBAC -- Contributor for the fleet-scoped
// provisioning UAMI on the deployment subscription.
//
// PURPOSE
//   Grants the control-plane UAMI (shared by .Api and .Worker per DS-3 §3)
//   Contributor at SUBSCRIPTION scope. H2a's ArmDeploymentRunner requires it
//   for BOTH of its ARM operations (Sprk.Provisioning.ControlPlane.Core/
//   Handlers/BicepInfraDeploy/ArmDeploymentRunner.cs):
//     - resource-group ensure (GetSubscriptionResource().CreateOrUpdate on
//       rg-spaarke-{customer} -- an RG create is a subscription-scope write)
//     - subscription-scope ARM deployments (WhatIfAtSubscriptionScopeAsync +
//       CreateOrUpdateAsync of the compiled customer/model1-shared templates)
//   Its own RequestFailedException guidance says to "verify the control-plane
//   UAMI has Contributor RBAC at the customer subscription scope"
//   (ArmDeploymentRunner.cs:162) -- but nothing ever granted it.
//
// AUDIT REFERENCE (post-authoring-audit-2026-08-20.md, Wave G-8 Batch 2)
//   - Defect #2: this grant existed NOWHERE in infrastructure/** or the
//     provisioning scripts -- H2a would 403 on its first live RG-ensure.
//     Fix option (a) chosen: Bicep-managed (auditable + idempotent) over a
//     manual Deploy-ControlPlane.ps1 pre-req.
//
// WHY A MODULE (vs an inline resource in platform-controlplane.bicep)
//   platform-controlplane.bicep already has targetScope='subscription', but a
//   role assignment's resource NAME must be a deterministic
//   guid(scope, principalId, roleId) -- and the UAMI principalId there is a
//   MODULE OUTPUT (runtime value), which Bicep rejects in a resource name
//   (BCP120: names must be calculable at the start of the deployment).
//   Inside this module the principalId is a PARAM, evaluated when the nested
//   deployment starts, so the guid() name is legal. Same mechanism-forced
//   module split as controlplane-sb-queue.bicep / controlplane-sb-rbac.bicep
//   (theirs was BCP165 cross-RG; this one is BCP120 runtime-name).
//
// SCOPE / TENANCY NOTE
//   Grants on the DEPLOYING subscription (subscription().id of the
//   platform-controlplane stack). This covers Model 1 shared and any Model 2
//   stamp provisioned into the SAME fleet subscription. A Model 2 dedicated
//   stamp in a DIFFERENT customer subscription needs an equivalent grant in
//   that subscription (out of scope here -- the H0/H1 onboarding path for
//   foreign subscriptions owns it).

targetScope = 'subscription'

@description('Principal ID of the fleet-scoped control-plane UAMI (from modules/uami.bicep outputs) granted Contributor on this subscription. Pass empty to skip the grant (what-if isolation only -- a real deploy always needs it).')
param principalId string = ''

// ============================================================================
// VARIABLES -- built-in Azure role definition IDs
// ============================================================================

// Contributor -- RG-ensure + sub-scope ARM deploys (H2a / ArmDeploymentRunner)
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/privileged#contributor
var contributorRoleId = 'b24988ac-6180-42a0-ab88-20f7382dd24c'

// ============================================================================
// RBAC -- control-plane UAMI -> Contributor at subscription scope.
// Deterministic guid() name (idempotent, no-op over a matching manual grant).
// ============================================================================

resource controlPlaneSubscriptionContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(principalId)) {
  name: guid(subscription().id, principalId, contributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', contributorRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
    description: 'L2 control-plane UAMI ensures customer RGs + runs subscription-scope ARM deployments (H2a ArmDeploymentRunner) -- Wave G-8 Batch 2, audit defect #2'
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output subscriptionId string = subscription().subscriptionId
output contributorRoleAssignmentName string = !empty(principalId) ? guid(subscription().id, principalId, contributorRoleId) : ''
