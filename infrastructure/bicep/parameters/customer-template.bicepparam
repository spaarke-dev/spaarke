// infrastructure/bicep/parameters/customer-template.bicepparam
// Template for onboarding new customers - COPY and customize for each customer
//
// +----------------------------------------------------------------------+
// |  HOW TO USE THIS TEMPLATE                                            |
// |                                                                      |
// |  1. Copy this file:                                                  |
// |     cp customer-template.bicepparam {customer-id}-customer.bicepparam|
// |                                                                      |
// |  2. Replace all REPLACE_* placeholders with actual values            |
// |                                                                      |
// |  3. Deploy:                                                          |
// |     az deployment sub create \                                       |
// |       --location westus2 \                                           |
// |       --template-file infrastructure/bicep/customer.bicep \          |
// |       --parameters {customer-id}-customer.bicepparam                 |
// |                                                                      |
// |  OR use the provisioning script (RECOMMENDED):                       |
// |     ./scripts/Provision-Customer.ps1 \                               |
// |       -CustomerId {customer-id} \                                    |
// |       -CustomerName "Customer Display Name" \                        |
// |       -DataverseUrl "https://org.crm.dynamics.com"                   |
// +----------------------------------------------------------------------+
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

// Customer identifier - lowercase, alphanumeric, 3-10 characters.
// Used in resource naming (sprk-{id}-*) and Key Vault secret prefixes.
// Examples: 'contoso', 'acme', 'fabrikam'
// NOTE: max length is 10 chars (enforced by customer.bicep @maxLength(10)).
param customerId = 'replaceme'

// ============================================================================
// ENVIRONMENT
// ============================================================================

// Environment tier for this customer deployment
param environmentName = 'prod'

// Azure region (should match customer's region preference)
param location = 'westus2'

// ============================================================================
// SHARED PLATFORM REFERENCES
// ============================================================================

// Platform Key Vault name - from platform.bicep deployment outputs.
// Get from: az deployment sub show -n platform-prod \
//   --query properties.outputs.keyVaultName.value -o tsv
param platformKeyVaultName = 'sprk-platform-prod-kv'

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
// REDIS
// ============================================================================

// Basic tier (C0) for cost-optimized customers; Standard for HA;
// Premium required for VNet injection + RDB persistence.
// See infrastructure/bicep/modules/redis.bicep for capabilities per SKU.
param redisSku = 'Basic'
param redisCapacity = 0

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
