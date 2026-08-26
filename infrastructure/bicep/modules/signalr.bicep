// infrastructure/bicep/modules/signalr.bicep
// Azure SignalR Service module — per-customer realtime notifications spine.
//
// Feature-gated per ADR-032 (BFF Null-Object Kill-Switch Pattern):
//   - When the customer.bicep caller passes signalrEnabled=true, this module is invoked
//     and a real SignalR resource is provisioned.
//   - When signalrEnabled=false, the caller SKIPS invocation entirely (module never
//     deploys) AND the downstream BFF DI container resolves the Null-Object variant of
//     the SignalR client per ADR-032 P3 (Fail-fast Null-Object). No dangling resources.
//
// Design source of truth:
//   projects/customer-provisioning-orchestration-r1/design.md §7.1 (naming) + §7.2 (SKU + purpose)
//   Naming: sprk-{customerId}-{env}-signalr  (e.g., sprk-acme-prod-signalr)
//   Default SKU: Free_F1 (20 concurrent connections; 20K messages/day) — safe scaffold.
//              : Production customers requiring realtime bump to Standard_S1 (1K concurrent
//                connections; ~$48.30/mo/unit per notes/pricing-research-2026-08-12.md).
//
// Related ADRs:
//   - ADR-032: feature-gating pattern (this module represents the "resource present" branch)
//   - ADR-034: user-record-membership realtime pattern
//
// Authentication:
//   - When AAD auth is added downstream (per ADR-028 all outbound = DefaultAzureCredential /
//     Managed Identity), a role assignment for the BFF UAMI (SignalR App Server role
//     420fcaa2-552c-430f-98ca-3264be4806c7) is granted at the SignalR scope. This module
//     accepts an optional bffPrincipalId to wire that RBAC atomically at provisioning time
//     (mirrors membership-topic.bicep + cosmos-db.bicep pattern).
//   - Local auth via keys is DISABLED by default (disableLocalAuth: true) to force MI-only.

// ============================================================================
// PARAMETERS
// ============================================================================

@description('Name of the SignalR Service resource. Convention: sprk-{customerId}-{env}-signalr per design.md §7.1.')
param signalrName string

@description('Location for the SignalR resource. Defaults to the resource-group location.')
param location string = resourceGroup().location

@description('SignalR SKU. Default Free_F1 (20 conns / 20K messages/day) — safe scaffold; bump to Standard_S1 for production realtime.')
@allowed(['Free_F1', 'Standard_S1', 'Premium_P1'])
param signalrSku string = 'Free_F1'

@description('SKU capacity (units). Free tier fixed at 1; Standard/Premium scale 1..100.')
@minValue(1)
@maxValue(100)
param signalrCapacity int = 1

@description('Service mode. Default: Default (both server + serverless clients supported); Serverless disables the server-managed hub for pure REST/upstream flows.')
@allowed(['Default', 'Serverless', 'Classic'])
param serviceMode string = 'Default'

@description('Principal ID of the BFF App Service Managed Identity (granted "SignalR App Server" role at the SignalR scope). Empty string skips the assignment — operator must grant manually.')
param bffPrincipalId string = ''

@description('Tags applied to the SignalR resource for cost tracking.')
param tags object = {}

// ============================================================================
// VARIABLES — built-in Azure role definition IDs
// ============================================================================

// SignalR App Server — grants the BFF App Service (via MI) the right to acquire
// AAD access tokens for the SignalR REST APIs (negotiate + send).
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/networking#signalr-app-server
var signalrAppServerRoleId = '420fcaa2-552c-430f-98ca-3264be4806c7'

// ============================================================================
// SIGNALR SERVICE
// ============================================================================

resource signalr 'Microsoft.SignalRService/signalR@2024-03-01' = {
  name: signalrName
  location: location
  tags: tags
  sku: {
    name: signalrSku
    capacity: signalrCapacity
  }
  kind: 'SignalR'
  properties: {
    // Force MI-only auth — no shared-access keys in the data path per ADR-028.
    disableLocalAuth: true
    // Aad-only auth via disableAadAuth=false is the default; explicit for clarity.
    disableAadAuth: false
    // TLS 1.2+ enforced by service default; no legacy client compat needed here.
    tls: {
      clientCertEnabled: false
    }
    features: [
      {
        flag: 'ServiceMode'
        value: serviceMode
      }
      {
        // EnableConnectivityLogs — surfaces client connect/disconnect diagnostics into
        // App Insights when a diagnostic setting is attached (App Insights wiring lives
        // in monitoring.bicep — this flag only enables the source-side emission).
        flag: 'EnableConnectivityLogs'
        value: 'True'
      }
      {
        flag: 'EnableMessagingLogs'
        value: 'False'
      }
    ]
    // Public network access enabled — App Service consumes this over the public endpoint
    // (matches the current stamp's connectivity model; private endpoint hookup lives in
    // private-endpoints.bicep when the customer opts in).
    publicNetworkAccess: 'Enabled'
  }
}

// ============================================================================
// RBAC — BFF Managed Identity → SignalR App Server (only when bffPrincipalId supplied)
// ============================================================================
// Pattern matches cosmos-db.bicep + membership-topic.bicep: conditional on non-empty
// principal ID; idempotent guid() name based on scope + principal + role.

resource bffSignalrAppServerAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(bffPrincipalId)) {
  scope: signalr
  name: guid(signalr.id, bffPrincipalId, signalrAppServerRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', signalrAppServerRoleId)
    principalId: bffPrincipalId
    principalType: 'ServicePrincipal'
    description: 'BFF App Service MI negotiates + sends via SignalR REST APIs (ADR-034 realtime spine)'
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output signalrName string = signalr.name
output signalrId string = signalr.id
output signalrHostName string = signalr.properties.hostName
output signalrSku string = signalr.sku.name
