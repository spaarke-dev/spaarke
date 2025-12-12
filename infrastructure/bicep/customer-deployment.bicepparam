// infrastructure/bicep/customer-deployment.bicepparam
// Customer-specific parameter file for Spaarke deployment (Model 2: Customer-Hosted)
//
// USAGE:
//   1. Copy this file to a secure location (not checked into source control)
//   2. Replace all placeholders with customer-specific values
//   3. Deploy: az deployment sub create --location eastus --template-file stacks/model2-full.bicep --parameters customer-deployment.bicepparam
//
// See: projects/ai-document-intelligence-r1/docs/environment-variables.md for mapping

using 'stacks/model2-full.bicep'

// =============================================================================
// REQUIRED: Customer Identity
// =============================================================================

// Customer identifier (lowercase, alphanumeric, 3-10 chars)
// Used in all resource names: rg-spaarke-{customerId}-{environment}
param customerId = '#{CUSTOMER_ID}#'

// Deployment environment
// Allowed: 'dev', 'staging', 'prod'
param environment = '#{ENVIRONMENT}#'

// Azure region for deployment
// Recommended: Same region as customer's Power Platform environment
param location = '#{AZURE_REGION}#'

// =============================================================================
// REQUIRED: Power Platform Integration
// =============================================================================

// Dataverse environment URL
// Format: https://{org}.crm.dynamics.com
// Find: Power Platform admin center > Environments > Details
param dataverseUrl = '#{DATAVERSE_URL}#'

// SharePoint Embedded Container Type ID
// Created during SPE provisioning (leave empty for new deployments)
param containerTypeId = '#{SPE_CONTAINER_TYPE_ID}#'

// =============================================================================
// OPTIONAL: Resource Sizing
// =============================================================================

// App Service Plan SKU
// Options: 'B1' (dev/test), 'S1' (small prod), 'P1v3' (high perf)
// Default: 'B1' for cost optimization
param appServiceSku = '#{APP_SERVICE_SKU:B1}#'

// Azure AI Search SKU
// Options: 'basic' (dev), 'standard' (prod), 'standard2' (high volume)
// Default: 'basic' for cost optimization
param aiSearchSku = '#{AI_SEARCH_SKU:basic}#'

// =============================================================================
// OPTIONAL: Resource Tags
// =============================================================================

// Custom tags applied to all resources
// Override to add cost center, owner, project codes
param tags = {
  customer: '#{CUSTOMER_ID}#'
  environment: '#{ENVIRONMENT}#'
  application: 'spaarke'
  deploymentModel: 'model2'
  managedBy: 'bicep'
  costCenter: '#{COST_CENTER:unassigned}#'
  owner: '#{OWNER_EMAIL:unassigned}#'
}
