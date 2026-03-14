<#
.SYNOPSIS
    Adds 3072-dimension vector fields to the existing spaarke-knowledge-index.

.DESCRIPTION
    This script updates the existing Azure AI Search index to add:
    - contentVector3072 (3072 dimensions)
    - documentVector3072 (3072 dimensions)
    - hnsw-knowledge-3072 algorithm
    - knowledge-vector-profile-3072 profile

    The existing 1536-dimension fields are preserved for backward compatibility
    during the embedding migration period.

.PARAMETER SubscriptionId
    Azure subscription ID. Defaults to the current subscription.

.PARAMETER ResourceGroup
    Resource group containing the Azure AI Search service.

.PARAMETER SearchServiceName
    Name of the Azure AI Search service.

.PARAMETER IndexName
    Name of the index to update. Defaults to 'spaarke-knowledge-index'.

.EXAMPLE
    .\Update-KnowledgeIndex-3072Vectors.ps1 -ResourceGroup "spe-infrastructure-westus2" -SearchServiceName "spaarke-search-dev"

.NOTES
    Task 051 - Phase 5b: Schema Migration
    Prerequisites:
    - Azure CLI installed and logged in (az login)
    - Appropriate permissions on the Azure AI Search service
    - Index exists with current schema
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$SubscriptionId,

    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup = "spe-infrastructure-westus2",

    [Parameter(Mandatory = $true)]
    [string]$SearchServiceName = "spaarke-search-dev",

    [Parameter(Mandatory = $false)]
    [string]$IndexName = "spaarke-knowledge-index"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Update Knowledge Index with 3072-dim Vector Fields ===" -ForegroundColor Cyan
Write-Host "Resource Group: $ResourceGroup"
Write-Host "Search Service: $SearchServiceName"
Write-Host "Index Name: $IndexName"
Write-Host ""

# Set subscription if provided
if ($SubscriptionId) {
    Write-Host "Setting subscription to $SubscriptionId..."
    az account set --subscription $SubscriptionId
}

# Get the search service admin key
Write-Host "Retrieving search service admin key..."
$adminKey = az search admin-key show `
    --resource-group $ResourceGroup `
    --service-name $SearchServiceName `
    --query "primaryKey" -o tsv

if (-not $adminKey) {
    Write-Error "Failed to retrieve admin key. Ensure you have access to the search service."
    exit 1
}

# Get search service endpoint
$searchEndpoint = "https://$SearchServiceName.search.windows.net"
Write-Host "Search Endpoint: $searchEndpoint"
Write-Host ""

# Read the migration schema
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$schemaPath = Join-Path $scriptDir "spaarke-knowledge-index-migration.json"

if (-not (Test-Path $schemaPath)) {
    Write-Error "Migration schema not found at $schemaPath"
    exit 1
}

Write-Host "Loading migration schema from $schemaPath..."
$schemaJson = Get-Content $schemaPath -Raw

# Update the index using REST API
$updateUrl = "$searchEndpoint/indexes/$IndexName`?api-version=2024-07-01"

Write-Host "Updating index at $updateUrl..."
Write-Host ""

$headers = @{
    "Content-Type" = "application/json"
    "api-key" = $adminKey
}

try {
    $response = Invoke-RestMethod -Uri $updateUrl -Method Put -Headers $headers -Body $schemaJson
    Write-Host "Index updated successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Updated fields:"
    $response.fields | Where-Object { $_.name -like "*3072*" } | ForEach-Object {
        Write-Host "  - $($_.name): $($_.dimensions) dimensions"
    }
    Write-Host ""
    Write-Host "Vector search profiles:"
    $response.vectorSearch.profiles | ForEach-Object {
        Write-Host "  - $($_.name) -> $($_.algorithm)"
    }
}
catch {
    Write-Error "Failed to update index: $_"
    Write-Host ""
    Write-Host "If the error mentions 'cannot modify existing vector fields', the fields may already exist."
    Write-Host "Verify by checking the index schema in the Azure portal."
    exit 1
}

Write-Host ""
Write-Host "=== Next Steps ===" -ForegroundColor Yellow
Write-Host "1. Verify the index in Azure Portal: $searchEndpoint"
Write-Host "2. Create the text-embedding-3-large deployment in Azure OpenAI (if not already done)"
Write-Host "3. Run Task 052: Create EmbeddingMigrationService"
Write-Host "4. Run Task 053: Execute embedding migration"
Write-Host ""
