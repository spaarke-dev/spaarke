// infrastructure/bicep/ai-foundry.bicepparam
// Parameter file for standalone AI Foundry deployment
//
// USAGE:
//   az deployment group create \
//     --resource-group rg-spaarke-{customer}-{env} \
//     --template-file stacks/ai-foundry-stack.bicep \
//     --parameters ai-foundry.bicepparam

using 'stacks/ai-foundry-stack.bicep'

// =============================================================================
// REQUIRED: Customer Identity
// =============================================================================

param customerId = '#{CUSTOMER_ID}#'
param environment = '#{ENVIRONMENT}#'
param location = '#{AZURE_REGION:eastus}#'

// =============================================================================
// OPTIONAL: AI Foundry Configuration
// =============================================================================

// SKU: 'Basic' for dev/test, 'Standard' for production
param aiFoundrySku = '#{AI_FOUNDRY_SKU:Basic}#'

// Network access: 'Enabled' for dev, consider 'Disabled' for production with private endpoints
param publicNetworkAccess = '#{PUBLIC_NETWORK_ACCESS:Enabled}#'

// =============================================================================
// OPTIONAL: Connect Existing Resources
// =============================================================================

// Resource ID of existing Azure OpenAI resource
// Format: /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.CognitiveServices/accounts/{name}
param openAiResourceId = '#{OPENAI_RESOURCE_ID:}#'

// Resource ID of existing AI Search resource
// Format: /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Search/searchServices/{name}
param aiSearchResourceId = '#{AI_SEARCH_RESOURCE_ID:}#'

// =============================================================================
// OPTIONAL: Tags
// =============================================================================

param tags = {
  customer: '#{CUSTOMER_ID}#'
  environment: '#{ENVIRONMENT}#'
  application: 'spaarke'
  component: 'ai-foundry'
  managedBy: 'bicep'
  costCenter: '#{COST_CENTER:unassigned}#'
  owner: '#{OWNER_EMAIL:unassigned}#'
}
