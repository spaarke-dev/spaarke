// infrastructure/bicep/parameters/customer-template.bicepparam
// Template for Model 2 customer deployment - COPY and customize for each customer

using '../stacks/model2-full.bicep'

// ============================================================================
// CUSTOMER-SPECIFIC VALUES (REQUIRED - Must be customized)
// ============================================================================

// Customer identifier (lowercase, alphanumeric, 3-10 chars)
// Example: 'contoso', 'acme', 'fabrikam'
param customerId = 'REPLACE_CUSTOMER_ID'

// Customer's Dataverse environment URL
// Get from: Power Platform Admin Center > Environments > [Environment] > Details
param dataverseUrl = 'https://REPLACE_ORG.crm.dynamics.com'

// SPE ContainerType ID (created via Setup-SPE-ContainerType.ps1)
// Leave empty initially, update after ContainerType creation
param containerTypeId = ''

// ============================================================================
// DEPLOYMENT OPTIONS (Can customize based on customer needs)
// ============================================================================

// Environment tier
param environment = 'prod'

// Azure region (should match customer's region preference)
param location = 'eastus'

// App Service SKU (B1 for small, S1 for medium, P1v3 for large)
param appServiceSku = 'B1'

// AI Search SKU (basic for small, standard for medium/large)
param aiSearchSku = 'basic'

// ============================================================================
// TAGS (Customize as needed)
// ============================================================================

param tags = {
  customer: 'REPLACE_CUSTOMER_ID'
  customerName: 'REPLACE_CUSTOMER_NAME'
  environment: 'prod'
  application: 'spaarke'
  deploymentModel: 'model2'
  managedBy: 'bicep'
  deployedDate: 'REPLACE_DEPLOY_DATE'
}
