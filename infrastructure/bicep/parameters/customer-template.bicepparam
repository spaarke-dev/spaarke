// infrastructure/bicep/parameters/customer-template.bicepparam
// Reference parameter set for customer.bicep — documents its parameters and sensible values.
//
// Customer stamps are deployed ONLY by the L2 control plane: handler H2a (ArmDeploymentRunner) sends a
// computed parameter payload for each run; it does not read this file (owner D19, task 249). Start a
// new customer with the `/provision-environment` skill. This file is kept as a compile-checked reference
// (the "Validate Bicep Infrastructure" workflow builds it) and for one-off local what-if experiments:
//     az deployment sub what-if --location westus2 \
//       --template-file infrastructure/bicep/customer.bicep \
//       --parameters infrastructure/bicep/parameters/customer-template.bicepparam
//
// Constraints:
//   - FR-06: Demo and real customers use this SAME template (no special-casing)
//   - FR-08: No secrets in this file - all secrets via Key Vault references
//   - FR-11: All resource names follow sprk_/spaarke- naming standard
//
// See also: demo-customer.bicepparam (working example of a completed template)

using '../customer.bicep'

// ============================================================================
// CUSTOMER IDENTITY (REQUIRED - must customize)
// ============================================================================

// Customer identifier - lowercase letters and digits, starting with a letter, 3-8 characters.
// Used in resource naming (sprk-{id}-*) and Key Vault secret prefixes.
// Examples: 'contoso', 'acme', 'fabrikam'
// NOTE: max length is 8 chars (customer.bicep @maxLength(8) — the limit comes from the Key Vault name).
param customerId = 'replace'

// ============================================================================
// ENVIRONMENT
// ============================================================================

// Environment tier for this customer deployment
param environmentName = 'prod'

// Azure region (should match customer's region preference)
param location = 'westus2'

// ============================================================================
// STORAGE
// ============================================================================

// Standard_LRS for cost-optimized; upgrade to Standard_GRS for geo-redundancy
param storageSku = 'Standard_LRS'

// ============================================================================
// SERVICE BUS
// ============================================================================

param serviceBusSku = 'Standard'

// ============================================================================
// TAGS (customize as needed)
// ============================================================================

// Tags for cost tracking, compliance, and resource management.
// Ensure 'customer' matches customerId above.
param tags = {
  customer: 'replaceme'
  environment: 'prod'
  application: 'spaarke'
  managedBy: 'bicep'
}
