// infrastructure/bicep/parameters/platform-controlplane-dev.bicepparam
//
// Dev-environment parameter file for platform-controlplane.bicep (L2 control-plane
// infrastructure). Deploys to rg-spaarke-platform-dev.
//
// Created as fix-at-discovery during Phase C'' Wave G-2 dispatch (2026-08-19):
//   task 122 added `adminDataverseEnvironmentUrl` as a REQUIRED param on
//   platform-controlplane.bicep (Path X registry client -- Worker crash-loops if
//   missing per DataverseEnvironmentRegistryOptions.Validate / NFR-05).
//   Discovered concurrently: the live-ceremony runbook was pointing at
//   stacks/dev.bicepparam which actually targets model2-full.bicep -- wrong file.
//   This bicepparam is the correct one to pass to platform-controlplane.bicep.
//
// Usage:
//   az deployment sub create `
//     --location westus2 `
//     --template-file infrastructure/bicep/platform-controlplane.bicep `
//     --parameters infrastructure/bicep/parameters/platform-controlplane-dev.bicepparam
//
// (Note: platform-controlplane.bicep uses targetScope = 'subscription'.)

using '../platform-controlplane.bicep'

// ============================================================================
// ENVIRONMENT
// ============================================================================

param environmentName = 'dev'
param location = 'westus2'

// ============================================================================
// PLAN SKU
// ============================================================================

// Dev: P1v3 (matches live dev + is the module's minimum -- Basic/Standard
// disallowed per platform-controlplane.bicep param constraint).
param appServicePlanSku = 'P1v3'

// ============================================================================
// LOGGING
// ============================================================================

// Dev: 180-day retention parity with prod (NFR-11 operator-audit requirement
// applies in dev too; log cost is negligible relative to compute).
param logRetentionDays = 180

// ============================================================================
// SERVICE BUS (cross-RG reference)
// ============================================================================

// Live dev value: spaarke-servicebus-dev in the legacy SharePointEmbedded RG
// (per AZURE-RESOURCE-NAMING-CONVENTION.md "Dev Environment (DO NOT RENAME)").
// Both are the module defaults but stated explicitly here for clarity.
param serviceBusNamespaceName = 'spaarke-servicebus-dev'
param serviceBusResourceGroupName = 'SharePointEmbedded'

// ============================================================================
// PATH X: L2 REGISTRY CLIENT (task 122 / task 112)
// ============================================================================

// REQUIRED: the admin Dataverse environment hosting the sprk_dataverseenvironment
// registry table. Worker fails fast (NFR-05) at boot if missing. spaarkedev1 is
// the dev admin org per DS-8 § Path X. Staging/prod bicepparam files must set
// their own admin-org URLs (never inherit dev).
param adminDataverseEnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com'

// ============================================================================
// TASK 153 (Wave G-5): H12c SHARED-PLATFORM OPENAI ENDPOINT
// ============================================================================

// Stable canonical secret name (same value in every environment) -- the
// module default already matches; stated explicitly here for clarity,
// parity with bffApiClientSecretName below.
param azureOpenAiEndpointSecretName = 'AzureOpenAI-Endpoint'

// ============================================================================
// TENANT + APP-REG (optional for dev logging surface)
// ============================================================================

// Dev tenant ID for JWT bearer authority. Empty defaults to subscription
// tenant ID, which is correct in single-tenant dev. Setting explicitly here
// documents the value and makes drift-detection easier.
param jwtTenantId = 'a221a95e-6abc-4434-aecc-e48338a1b2f2'

// Dev L2 app-reg client ID for logging + config surface (bearer audience is
// derived from environmentName, so this is informational only).
param controlPlaneAppRegClientId = ''

// ============================================================================
// SIDECAR IMAGE (customer-provisioning-orchestration-r1 Wave H-3, 2026-08-21)
// ============================================================================
// Points the Worker sitecontainer at the platform ACR image built by
// scripts/provisioning/build-provisioning-sidecar.yml (or the manual
// `az acr build --registry sprkcontrolplanedevacr --image provisioning-sidecar:latest`
// first-push per Wave H-3 Step 5). Flipping this off the MCR-placeholder default
// auto-computes sidecarAuthType='UserAssigned' via the module's ternary default;
// the userManagedIdentityClientId now unconditionally set on the sitecontainer
// resource (controlplane-worker-app-service.bicep Wave H-3 fix-at-discovery)
// resolves the ACR pull via the shared control-plane UAMI's AcrPull grant.
param acrImageTag = 'sprkcontrolplanedevacr.azurecr.io/provisioning-sidecar:latest'

// ============================================================================
// TAGS (defaults are fine for dev)
// ============================================================================
