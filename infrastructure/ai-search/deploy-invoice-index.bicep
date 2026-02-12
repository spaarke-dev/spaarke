// infrastructure/ai-search/deploy-invoice-index.bicep
// Deploys the spaarke-invoices AI Search index using a deployment script.
//
// Azure Bicep does not natively support AI Search index definitions (only the
// search service resource itself via Microsoft.Search/searchServices). This
// template uses a Microsoft.Resources/deploymentScripts resource to call the
// Azure AI Search REST API and create/update the index schema.
//
// For direct deployment without Bicep, use the companion PowerShell script:
//   ./deploy-invoice-index.ps1
//
// Finance Intelligence Module R1 — Task 030

@description('Name of the existing Azure AI Search service')
param searchServiceName string

@description('Resource group containing the AI Search service')
param searchServiceResourceGroup string = resourceGroup().name

@description('Name of the invoice search index')
param indexName string = 'spaarke-invoices'

@description('Location for the deployment script resource')
param location string = resourceGroup().location

@description('Azure AI Search REST API version')
param apiVersion string = '2024-07-01'

@description('User-assigned managed identity resource ID for the deployment script')
param scriptIdentityId string

@description('Tags for resources')
param tags object = {}

// Reference the existing AI Search service to get its admin key
resource searchService 'Microsoft.Search/searchServices@2023-11-01' existing = {
  name: searchServiceName
  scope: resourceGroup(searchServiceResourceGroup)
}

// Deploy the invoice index using a deployment script
resource deployInvoiceIndex 'Microsoft.Resources/deploymentScripts@2023-08-01' = {
  name: 'deploy-invoice-index-${uniqueString(indexName)}'
  location: location
  tags: tags
  kind: 'AzurePowerShell'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${scriptIdentityId}': {}
    }
  }
  properties: {
    azPowerShellVersion: '11.0'
    retentionInterval: 'PT1H'
    timeout: 'PT10M'
    arguments: '-SearchServiceName ${searchServiceName} -IndexName ${indexName} -ApiVersion ${apiVersion}'
    environmentVariables: [
      {
        name: 'SEARCH_ADMIN_KEY'
        secureValue: searchService.listAdminKeys().primaryKey
      }
    ]
    scriptContent: '''
      param(
        [string]$SearchServiceName,
        [string]$IndexName,
        [string]$ApiVersion
      )

      $ErrorActionPreference = "Stop"

      $adminKey = $env:SEARCH_ADMIN_KEY
      $searchEndpoint = "https://$SearchServiceName.search.windows.net"

      # Invoice index schema definition
      $schema = @{
        name = $IndexName
        fields = @(
          @{ name = "id"; type = "Edm.String"; key = $true; searchable = $false; filterable = $true; sortable = $false; facetable = $false }
          @{ name = "content"; type = "Edm.String"; searchable = $true; filterable = $false; sortable = $false; facetable = $false; analyzer = "standard.lucene" }
          @{ name = "contentVector"; type = "Collection(Edm.Single)"; searchable = $true; filterable = $false; sortable = $false; facetable = $false; dimensions = 3072; vectorSearchProfile = "invoice-vector-profile" }
          @{ name = "chunkIndex"; type = "Edm.Int32"; searchable = $false; filterable = $true; sortable = $true; facetable = $false }
          @{ name = "invoiceId"; type = "Edm.String"; searchable = $false; filterable = $true; sortable = $false; facetable = $false }
          @{ name = "documentId"; type = "Edm.String"; searchable = $false; filterable = $true; sortable = $false; facetable = $false }
          @{ name = "matterId"; type = "Edm.String"; searchable = $false; filterable = $true; sortable = $false; facetable = $false }
          @{ name = "projectId"; type = "Edm.String"; searchable = $false; filterable = $true; sortable = $false; facetable = $false }
          @{ name = "vendorOrgId"; type = "Edm.String"; searchable = $false; filterable = $true; sortable = $false; facetable = $false }
          @{ name = "vendorName"; type = "Edm.String"; searchable = $true; filterable = $true; sortable = $true; facetable = $true; analyzer = "standard.lucene" }
          @{ name = "invoiceNumber"; type = "Edm.String"; searchable = $true; filterable = $true; sortable = $false; facetable = $false; analyzer = "standard.lucene" }
          @{ name = "invoiceDate"; type = "Edm.DateTimeOffset"; searchable = $false; filterable = $true; sortable = $true; facetable = $false }
          @{ name = "totalAmount"; type = "Edm.Double"; searchable = $false; filterable = $true; sortable = $true; facetable = $false }
          @{ name = "currency"; type = "Edm.String"; searchable = $false; filterable = $true; sortable = $false; facetable = $true }
          @{ name = "documentType"; type = "Edm.String"; searchable = $false; filterable = $true; sortable = $false; facetable = $true }
          @{ name = "tenantId"; type = "Edm.String"; searchable = $false; filterable = $true; sortable = $false; facetable = $true }
          @{ name = "indexedAt"; type = "Edm.DateTimeOffset"; searchable = $false; filterable = $false; sortable = $true; facetable = $false }
        )
        vectorSearch = @{
          algorithms = @(
            @{
              name = "invoice-hnsw"
              kind = "hnsw"
              hnswParameters = @{
                m = 4
                efConstruction = 400
                efSearch = 500
                metric = "cosine"
              }
            }
          )
          profiles = @(
            @{
              name = "invoice-vector-profile"
              algorithm = "invoice-hnsw"
            }
          )
        }
        semantic = @{
          configurations = @(
            @{
              name = "invoice-semantic"
              prioritizedFields = @{
                titleField = @{ fieldName = "content" }
                prioritizedContentFields = @(
                  @{ fieldName = "content" }
                )
                prioritizedKeywordsFields = @(
                  @{ fieldName = "vendorName" }
                  @{ fieldName = "invoiceNumber" }
                )
              }
            }
          )
        }
      }

      $schemaJson = $schema | ConvertTo-Json -Depth 10
      $putUrl = "$searchEndpoint/indexes/$IndexName`?api-version=$ApiVersion"

      $headers = @{
        "Content-Type" = "application/json"
        "api-key" = $adminKey
      }

      try {
        $response = Invoke-RestMethod -Uri $putUrl -Method Put -Headers $headers -Body $schemaJson
        Write-Host "Index '$IndexName' deployed successfully with $($response.fields.Count) fields."

        $DeploymentScriptOutputs = @{
          indexName = $response.name
          fieldCount = $response.fields.Count
          endpoint = $searchEndpoint
        }
      }
      catch {
        Write-Error "Failed to deploy index '$IndexName': $_"
        throw
      }
    '''
  }
}

output indexName string = indexName
output searchEndpoint string = 'https://${searchServiceName}.search.windows.net'
output deploymentScriptName string = deployInvoiceIndex.name
