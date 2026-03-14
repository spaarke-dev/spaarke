// infrastructure/bicep/parameters/platform-prod.bicepparam
// Production environment parameters for shared platform infrastructure
//
// Deploys shared resources to rg-spaarke-platform-prod:
//   - All resources in westus2 except Azure OpenAI (westus3 for model availability)
//   - App Service Plan (P1v3) with BFF API + staging slot
//   - Azure OpenAI (GPT-4o, GPT-4o-mini, text-embedding-3-large) — westus3
//   - Azure AI Search (Standard2, 2 replicas)
//   - Document Intelligence (S0)
//   - Key Vault (Standard)
//   - Monitoring (App Insights + Log Analytics, 180-day retention)
//
// Usage:
//   az deployment sub create \
//     --location westus2 \
//     --template-file infrastructure/bicep/platform.bicep \
//     --parameters infrastructure/bicep/parameters/platform-prod.bicepparam
//
// Constraints:
//   - FR-08: No secrets in this file — all secrets via Key Vault references
//   - FR-11: All resource names follow sprk_/spaarke- naming standard
//   - ADR-001: Single App Service, no Azure Functions

using '../platform.bicep'

// ============================================================================
// ENVIRONMENT
// ============================================================================

param environmentName = 'prod'
param location = 'westus2'

// Azure OpenAI deployed to westus3 — GPT-4o and embedding models have
// better quota availability in this region (Option A from deployment analysis)
param openAiLocation = 'westus3'

// ============================================================================
// COMPUTE
// ============================================================================

// App Service Plan — P1v3 for production workloads
// Supports deployment slots (zero-downtime swap), autoscaling, and adequate
// CPU/memory for BFF API serving multiple customers
param appServicePlanSku = 'P1v3'

// ============================================================================
// AI SERVICES
// ============================================================================

// Azure OpenAI model deployments — capacity tuned to westus3 quota limits
// GPT-4o: 50K TPM (westus3 max available), GPT-4o-mini: 120K, embeddings: 200K
param openAiDeployments = [
  {
    name: 'gpt-4o'
    model: 'gpt-4o'
    version: '2024-08-06'
    capacity: 50
  }
  {
    name: 'gpt-4o-mini'
    model: 'gpt-4o-mini'
    version: '2024-07-18'
    capacity: 120
  }
  {
    name: 'text-embedding-3-large'
    model: 'text-embedding-3-large'
    version: '1'
    capacity: 200
  }
]

// Azure AI Search — Standard with 2 replicas for high availability
// standard2 unavailable in westus2 (capacity constraint); standard provides
// sufficient capacity for initial production with multi-index, semantic, and vector search
param aiSearchSku = 'standard'
param aiSearchReplicaCount = 2

// ============================================================================
// MONITORING
// ============================================================================

// 180-day log retention for production compliance and troubleshooting
param logRetentionDays = 180

// ============================================================================
// TAGS
// ============================================================================

param tags = {
  environment: 'prod'
  application: 'spaarke'
  deploymentModel: 'model1'
  managedBy: 'bicep'
  costCenter: 'platform'
}
