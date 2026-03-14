// infrastructure/bicep/parameters/customer-template.bicepparam
// Template for onboarding new customers — COPY and customize for each customer
//
// ┌──────────────────────────────────────────────────────────────────────┐
// │  HOW TO USE THIS TEMPLATE                                           │
// │                                                                      │
// │  1. Copy this file:                                                  │
// │     cp customer-template.bicepparam {customer-id}-customer.bicepparam│
// │                                                                      │
// │  2. Replace all REPLACE_* placeholders with actual values            │
// │                                                                      │
// │  3. Deploy:                                                          │
// │     az deployment group create \                                     │
// │       --resource-group rg-spaarke-platform-prod \                    │
// │       --template-file infrastructure/bicep/stacks/customer.bicep \   │
// │       --parameters {customer-id}-customer.bicepparam                 │
// │                                                                      │
// │  OR use the provisioning script (RECOMMENDED):                       │
// │     ./scripts/Provision-Customer.ps1 \                               │
// │       -CustomerId {customer-id} \                                    │
// │       -CustomerName "Customer Display Name" \                        │
// │       -DataverseUrl "https://org.crm.dynamics.com"                   │
// └──────────────────────────────────────────────────────────────────────┘
//
// Constraints:
//   - FR-06: Demo and real customers use this SAME template (no special-casing)
//   - FR-08: No secrets in this file — all secrets via Key Vault references
//   - FR-11: All resource names follow sprk_/spaarke- naming standard
//
// See also: demo-customer.bicepparam (working example of a completed template)

using '../stacks/customer.bicep'

// ============================================================================
// CUSTOMER IDENTITY (REQUIRED — must customize)
// ============================================================================

// Customer identifier — lowercase, alphanumeric, 3-20 characters.
// Used in resource naming (sprk-{id}-*) and Key Vault secret prefixes.
// Examples: 'contoso', 'acme', 'fabrikam'
param customerId = 'REPLACE_CUSTOMER_ID'

// Customer display name — used in tags, documentation, and admin dashboards.
// Examples: 'Contoso Legal', 'Acme Corporation'
param customerName = 'REPLACE_CUSTOMER_NAME'

// ============================================================================
// SHARED PLATFORM REFERENCES (REQUIRED — from platform.bicep outputs)
// ============================================================================

// Shared Key Vault name — customer secrets stored with customer-{id} prefix.
// Get value from platform deployment:
//   az deployment sub show -n platform-prod \
//     --query properties.outputs.keyVaultName.value -o tsv
param sharedKeyVaultName = 'REPLACE_SHARED_KEYVAULT_NAME'

// Shared AI Search endpoint — customer indexes created in shared service.
// Get value from platform deployment:
//   az deployment sub show -n platform-prod \
//     --query properties.outputs.aiSearchEndpoint.value -o tsv
param sharedAiSearchEndpoint = 'REPLACE_SHARED_SEARCH_ENDPOINT'

// ============================================================================
// DATAVERSE (REQUIRED — must customize)
// ============================================================================

// Customer's Dataverse environment URL.
// Created via Provision-Customer.ps1 or Power Platform Admin API.
// Get from: Power Platform Admin Center > Environments > [Environment] > Details
// Example: 'https://contoso-spaarke.crm.dynamics.com'
param dataverseUrl = 'https://REPLACE_ORG.crm.dynamics.com'

// SPE Container ID — populated after SPE provisioning step.
// Leave empty initially; update after ContainerType creation via API.
// The provisioning script handles this automatically.
param speContainerId = ''

// ============================================================================
// TAGS (customize as needed)
// ============================================================================

// Tags for cost tracking, compliance, and resource management.
// Ensure 'customer' matches customerId above.
param tags = {
  customer: 'REPLACE_CUSTOMER_ID'
  customerName: 'REPLACE_CUSTOMER_NAME'
  environment: 'prod'
  application: 'spaarke'
  deploymentModel: 'model1'
  managedBy: 'bicep'
}
