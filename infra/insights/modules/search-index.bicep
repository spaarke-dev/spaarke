// =====================================================================
// search-index.bicep
// Creates / updates the spaarke-insights-index on an EXISTING Azure AI
// Search service via a deploymentScript (Azure CLI). Azure has no
// native Bicep resource for Search *indexes* — only the service — so
// the canonical IaC pattern is a deployment script that PUTs the
// schema via the REST API.
//
// Auth model: uses the search service's admin key, retrieved at
// deployment time via listAdminKeys(). The key never leaves the
// deployment pipeline (it's only injected into the script's env vars,
// not written to template outputs).
//
// Per SPEC §3.4: schema includes artifactType discriminator, tenantId
// first-class, 3072-dim contentVector, vectorFilterMode-preFilter
// friendly (all filter fields are filterable=true).
// =====================================================================

@description('Name of existing Azure AI Search service.')
param searchServiceName string

@description('Resource group of the search service (if different from current).')
param searchServiceResourceGroup string = resourceGroup().name

@description('Azure region for the deployment script container.')
param location string = resourceGroup().location

@description('Common resource tags.')
param tags object = {}

@description('Schema JSON content for the spaarke-insights-index. Pass via loadJsonContent() from main.bicep.')
param indexSchema object

@description('Search REST API version to use for the PUT.')
param searchApiVersion string = '2024-07-01'

@description('Force a re-run on every deployment by including utcNow as a script arg.')
param forceUpdateTag string = utcNow()

// Reference the existing search service for admin key retrieval.
resource searchService 'Microsoft.Search/searchServices@2024-03-01-preview' existing = {
  name: searchServiceName
  scope: resourceGroup(searchServiceResourceGroup)
}

// Deployment script needs its own UAMI to run in ACI; it's transient
// (used only by the script container) so it lives with this module.
resource deployScriptUami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'insights-search-deploy-uami'
  location: location
  tags: tags
}

// Deployment script: PUT the index schema. Idempotent — PUT either
// creates or updates the index in place.
resource createIndexScript 'Microsoft.Resources/deploymentScripts@2023-08-01' = {
  name: 'deploy-spaarke-insights-index'
  location: location
  tags: tags
  kind: 'AzureCLI'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${deployScriptUami.id}': {}
    }
  }
  properties: {
    azCliVersion: '2.61.0'
    timeout: 'PT10M'
    retentionInterval: 'PT1H'
    cleanupPreference: 'OnSuccess'
    forceUpdateTag: forceUpdateTag
    environmentVariables: [
      {
        name: 'SEARCH_SERVICE_NAME'
        value: searchServiceName
      }
      {
        name: 'SEARCH_API_VERSION'
        value: searchApiVersion
      }
      {
        name: 'INDEX_NAME'
        value: indexSchema.name
      }
      {
        name: 'INDEX_SCHEMA_JSON'
        value: string(indexSchema)
      }
      {
        name: 'SEARCH_ADMIN_KEY'
        secureValue: searchService.listAdminKeys().primaryKey
      }
    ]
    scriptContent: '''
      set -e
      echo "Deploying index: $INDEX_NAME to service: $SEARCH_SERVICE_NAME (api-version=$SEARCH_API_VERSION)"
      # Canonicalize: strip any keys whose name starts with "_comment_" anywhere
      # in the tree. Azure Search rejects unknown top-level properties on
      # IndexDefinition, but we want to keep human-readable comments in the
      # source-controlled schema file.
      echo "$INDEX_SCHEMA_JSON" | jq 'walk(if type == "object" then with_entries(select(.key | startswith("_comment_") | not)) else . end)' > /tmp/schema.json
      # Use `az rest` (always available in the AzureCLI deploymentScript container).
      # az rest returns non-zero on HTTP 4xx/5xx, so `set -e` will fail us out
      # automatically on a bad PUT. On success, az rest emits the index body
      # which we capture for verification + the output document.
      az rest \
        --method PUT \
        --uri "https://$SEARCH_SERVICE_NAME.search.windows.net/indexes/$INDEX_NAME?api-version=$SEARCH_API_VERSION" \
        --headers "Content-Type=application/json" "api-key=$SEARCH_ADMIN_KEY" \
        --body @/tmp/schema.json \
        --skip-authorization-header \
        --output json > /tmp/resp.json
      echo "PUT succeeded. Response:"
      cat /tmp/resp.json
      jq -n \
        --arg indexName "$INDEX_NAME" \
        --arg etag "$(jq -r '."@odata.etag"' /tmp/resp.json)" \
        '{status:"success", indexName:$indexName, etag:$etag}' \
        > $AZ_SCRIPTS_OUTPUT_PATH
    '''
  }
}

output indexName string = indexSchema.name
output deploymentResult object = createIndexScript.properties.outputs
