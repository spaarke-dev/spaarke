// infrastructure/bicep/modules/openai.bicep
// Azure OpenAI module for Spaarke AI services
// Task 046: Network hardening + capacity planning
//
// Deployment strategy:
//   1. Deploy with disablePublicNetworkAccess=false (default) alongside private endpoints
//   2. Validate private endpoint connectivity from App Service VNet integration
//   3. Set disablePublicNetworkAccess=true to lock down
//
// Model upgrade strategy (documented per task 046 step 5):
//   - gpt-4o: Pin to specific version. Upgrade by adding new deployment name
//     (e.g. gpt-4o-2025) then switching app config, then removing old deployment.
//   - gpt-4o-mini: Same pin-and-rotate strategy.
//   - text-embedding-3-large: Version locked. Changing embedding model requires
//     full re-indexing of AI Search. Plan ~2h downtime window for re-index.
//   - text-embedding-3-small: DEPRECATED and removed from Bicep. Migration to
//     text-embedding-3-large (3072 dims) is complete. Do not re-add.
//   - PTU evaluation: At >100K TPM sustained usage, evaluate Provisioned
//     Throughput Units for cost savings. Current beta scale (~200 analyses/day)
//     does not justify PTU commitment.

@description('Name of the Azure OpenAI resource')
param openAiName string

@description('Location for the resource')
param location string = resourceGroup().location

@description('SKU for Azure OpenAI')
param sku string = 'S0'

@description('Disable public network access (enable after private endpoint validation)')
param disablePublicNetworkAccess bool = false

@description('Allowed IP ranges when public access is enabled (empty = allow all when public)')
param allowedIpRanges array = []

@description('Model deployments to create. Capacity is in thousands of tokens per minute (TPM).')
param deployments array = [
  {
    name: 'gpt-4o'
    model: 'gpt-4o'
    version: '2024-08-06'
    capacity: 150
  }
  {
    name: 'gpt-4o-mini'
    model: 'gpt-4o-mini'
    version: '2024-07-18'
    capacity: 200 // Minimum 200 TPM for beta scale (~200 analyses/day)
  }
  {
    name: 'text-embedding-3-large'
    model: 'text-embedding-3-large'
    version: '1'
    capacity: 350
  }
  // NOTE: text-embedding-3-small has been removed (deprecated).
  // Migration to text-embedding-3-large (3072 dims) is complete.
  // See docs/guides/AI-EMBEDDING-STRATEGY.md for rationale.
]

@description('Tags for the resource')
param tags object = {}

// ============================================================================
// OPENAI ACCOUNT
// ============================================================================

resource openAi 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: openAiName
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: sku
  }
  properties: {
    customSubDomainName: openAiName
    publicNetworkAccess: disablePublicNetworkAccess ? 'Disabled' : 'Enabled'
    networkAcls: {
      defaultAction: disablePublicNetworkAccess ? 'Deny' : (empty(allowedIpRanges) ? 'Allow' : 'Deny')
      ipRules: [for ip in allowedIpRanges: {
        value: ip
      }]
    }
    // Disable local API key auth when using managed identity + private endpoint
    // Uncomment after validating managed identity auth end-to-end:
    // disableLocalAuth: true
  }
}

// ============================================================================
// MODEL DEPLOYMENTS
// ============================================================================

@batchSize(1)
resource modelDeployments 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = [for deployment in deployments: {
  parent: openAi
  name: deployment.name
  sku: {
    name: 'Standard'
    capacity: deployment.capacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: deployment.model
      version: deployment.version
    }
    raiPolicyName: 'Microsoft.Default'
  }
}]

// ============================================================================
// OUTPUTS
// ============================================================================

output openAiId string = openAi.id
output openAiName string = openAi.name
output openAiEndpoint string = openAi.properties.endpoint
output publicNetworkAccess string = openAi.properties.publicNetworkAccess
#disable-next-line outputs-should-not-contain-secrets
output openAiKey string = openAi.listKeys().key1
