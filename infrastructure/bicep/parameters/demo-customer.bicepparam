// infrastructure/bicep/parameters/demo-customer.bicepparam
// Demo customer parameters — first customer to validate provisioning process
//
// FR-06: Demo uses IDENTICAL template as real customers (no special-casing).
// This file follows the exact same pattern as any production customer.
//
// Usage:
//   az deployment sub create \
//     --location westus2 \
//     --template-file infrastructure/bicep/customer.bicep \
//     --parameters infrastructure/bicep/parameters/demo-customer.bicepparam
//
// OR via provisioning script:
//   ./scripts/Provision-Customer.ps1 -CustomerId demo -ParameterFile demo-customer.bicepparam
//
// Constraints:
//   - FR-08: No secrets in this file — all secrets via Key Vault references
//   - FR-11: All resource names follow sprk_/spaarke- naming standard
//   - FR-06: Demo parity — identical to real customer deployments

using '../customer.bicep'

// ============================================================================
// CUSTOMER IDENTITY (REQUIRED)
// ============================================================================

// Customer identifier — used in resource naming (sprk-demo-*) and resource group
param customerId = 'demo'

// ============================================================================
// ENVIRONMENT
// ============================================================================

param environmentName = 'prod'
param location = 'westus2'

// ============================================================================
// SHARED PLATFORM REFERENCES
// ============================================================================

// Platform Key Vault name — from platform.bicep deployment outputs
// Get from: az deployment sub show -n platform-prod --query properties.outputs.keyVaultName.value
param platformKeyVaultName = 'sprk-platform-prod-kv'

// ============================================================================
// STORAGE
// ============================================================================

// Standard_LRS for demo (upgrade to GRS for production customers)
param storageSku = 'Standard_LRS'

// ============================================================================
// SERVICE BUS
// ============================================================================

param serviceBusSku = 'Standard'

// ============================================================================
// REDIS
// ============================================================================

// Basic tier for demo (upgrade to Standard/Premium for production customers)
param redisSku = 'Basic'
param redisCapacity = 0

// ============================================================================
// TAGS
// ============================================================================

param tags = {
  customer: 'demo'
  environment: 'prod'
  application: 'spaarke'
  managedBy: 'bicep'
  purpose: 'demo-validation'
}
