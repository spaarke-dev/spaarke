<#
.SYNOPSIS
    Creates or updates the spaarke-invoices AI Search index for the Finance Intelligence Module.

.DESCRIPTION
    This script deploys the invoice search index schema to Azure AI Search using the REST API.
    The index supports hybrid search (keyword + vector + semantic ranking) for invoice documents
    with typed financial metadata fields for range queries and faceting.

    Index naming convention: spaarke-invoices (base name)
    At runtime, the InvoiceIndexingJobHandler appends a tenant suffix: spaarke-invoices-{tenantId}

    Schema: infrastructure/ai-search/invoice-index-schema.json
    Vector: text-embedding-3-large (3072 dimensions), HNSW with cosine metric
    Semantic: invoice-semantic config (content title, vendorName + invoiceNumber keywords)

.PARAMETER SubscriptionId
    Azure subscription ID. Defaults to the current subscription.

.PARAMETER ResourceGroup
    Resource group containing the Azure AI Search service.

.PARAMETER SearchServiceName
    Name of the Azure AI Search service.

.PARAMETER IndexName
    Name of the index to create. Defaults to 'spaarke-invoices'.
    For tenant-specific indexes, pass 'spaarke-invoices-{tenantId}'.

.PARAMETER ApiVersion
    Azure AI Search REST API version. Defaults to '2024-07-01'.

.EXAMPLE
    # Deploy base index (dev environment)
    .\deploy-invoice-index.ps1 -ResourceGroup "spe-infrastructure-westus2" -SearchServiceName "spaarke-search-dev"

.EXAMPLE
    # Deploy tenant-specific index
    .\deploy-invoice-index.ps1 -ResourceGroup "spe-infrastructure-westus2" -SearchServiceName "spaarke-search-dev" -IndexName "spaarke-invoices-a221a95e"

.NOTES
    Finance Intelligence Module R1 - Task 030
    Prerequisites:
    - Azure CLI installed and logged in (az login)
    - Appropriate permissions on the Azure AI Search service (Contributor or Search Service Contributor)
    - The Azure AI Search service must have semantic search enabled (standard tier)
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$SubscriptionId,

    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup = "spe-infrastructure-westus2",

    [Parameter(Mandatory = $true)]
    [string]$SearchServiceName = "spaarke-search-dev",

    [Parameter(Mandatory = $false)]
    [string]$IndexName = "spaarke-invoices",

    [Parameter(Mandatory = $false)]
    [string]$ApiVersion = "2024-07-01"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Deploy Invoice AI Search Index ===" -ForegroundColor Cyan
Write-Host "Resource Group: $ResourceGroup"
Write-Host "Search Service: $SearchServiceName"
Write-Host "Index Name: $IndexName"
Write-Host "API Version: $ApiVersion"
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

# Read the index schema
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$schemaPath = Join-Path $scriptDir "invoice-index-schema.json"

if (-not (Test-Path $schemaPath)) {
    Write-Error "Index schema not found at $schemaPath"
    exit 1
}

Write-Host "Loading index schema from $schemaPath..."
$schemaJson = Get-Content $schemaPath -Raw

# Parse and update the index name if different from default
$schema = $schemaJson | ConvertFrom-Json
if ($schema.name -ne $IndexName) {
    Write-Host "Overriding index name: $($schema.name) -> $IndexName"
    $schema.name = $IndexName
    $schemaJson = $schema | ConvertTo-Json -Depth 10
}

# Check if index already exists
$checkUrl = "$searchEndpoint/indexes/$IndexName`?api-version=$ApiVersion"
$headers = @{
    "Content-Type" = "application/json"
    "api-key" = $adminKey
}

$indexExists = $false
try {
    $existing = Invoke-RestMethod -Uri $checkUrl -Method Get -Headers $headers
    $indexExists = $true
    Write-Host "Index '$IndexName' already exists ($(($existing.fields | Measure-Object).Count) fields). Updating..." -ForegroundColor Yellow
}
catch {
    if ($_.Exception.Response.StatusCode -eq 404 -or $_.Exception.Message -match "404") {
        Write-Host "Index '$IndexName' does not exist. Creating..." -ForegroundColor Green
    }
    else {
        Write-Error "Failed to check index existence: $_"
        exit 1
    }
}

# Create or update the index using REST API (PUT is idempotent - creates or updates)
$putUrl = "$searchEndpoint/indexes/$IndexName`?api-version=$ApiVersion"

Write-Host ""
Write-Host "Deploying index schema..."

try {
    $response = Invoke-RestMethod -Uri $putUrl -Method Put -Headers $headers -Body $schemaJson

    if ($indexExists) {
        Write-Host "Index '$IndexName' updated successfully!" -ForegroundColor Green
    }
    else {
        Write-Host "Index '$IndexName' created successfully!" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "Index fields ($(($response.fields | Measure-Object).Count)):" -ForegroundColor Cyan
    $response.fields | ForEach-Object {
        $attrs = @()
        if ($_.key) { $attrs += "KEY" }
        if ($_.searchable) { $attrs += "searchable" }
        if ($_.filterable) { $attrs += "filterable" }
        if ($_.sortable) { $attrs += "sortable" }
        if ($_.facetable) { $attrs += "facetable" }
        if ($_.dimensions) { $attrs += "$($_.dimensions)d" }
        $attrStr = if ($attrs.Count -gt 0) { " [$($attrs -join ', ')]" } else { "" }
        Write-Host "  - $($_.name): $($_.type)$attrStr"
    }

    Write-Host ""
    Write-Host "Vector search configuration:" -ForegroundColor Cyan
    $response.vectorSearch.algorithms | ForEach-Object {
        Write-Host "  Algorithm: $($_.name) ($($_.kind), metric: $($_.hnswParameters.metric))"
        Write-Host "    m=$($_.hnswParameters.m), efConstruction=$($_.hnswParameters.efConstruction), efSearch=$($_.hnswParameters.efSearch)"
    }
    $response.vectorSearch.profiles | ForEach-Object {
        Write-Host "  Profile: $($_.name) -> $($_.algorithm)"
    }

    Write-Host ""
    Write-Host "Semantic configuration:" -ForegroundColor Cyan
    $response.semantic.configurations | ForEach-Object {
        Write-Host "  Config: $($_.name)"
        Write-Host "    Title: $($_.prioritizedFields.titleField.fieldName)"
        $contentFields = $_.prioritizedFields.prioritizedContentFields | ForEach-Object { $_.fieldName }
        Write-Host "    Content: $($contentFields -join ', ')"
        $keywordFields = $_.prioritizedFields.prioritizedKeywordsFields | ForEach-Object { $_.fieldName }
        Write-Host "    Keywords: $($keywordFields -join ', ')"
    }
}
catch {
    Write-Error "Failed to deploy index: $_"
    Write-Host ""
    if ($_.Exception.Response) {
        try {
            $errorBody = $_.Exception.Response.Content.ReadAsStringAsync().Result
            Write-Host "Response body: $errorBody" -ForegroundColor Red
        }
        catch {
            # Ignore error reading response body
        }
    }
    Write-Host "Common issues:" -ForegroundColor Yellow
    Write-Host "  - Semantic search not enabled: Upgrade search service to standard tier with semantic search"
    Write-Host "  - Vector dimensions mismatch: Ensure text-embedding-3-large is configured (3072 dimensions)"
    Write-Host "  - Index locked: Delete and recreate if vector field changes are needed"
    exit 1
}

Write-Host ""
Write-Host "=== Deployment Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Verify the index in Azure Portal: $searchEndpoint"
Write-Host "  2. For tenant-specific indexes, re-run with: -IndexName 'spaarke-invoices-{tenantId}'"
Write-Host "  3. Implement InvoiceIndexingJobHandler to index invoice documents"
Write-Host "  4. Implement InvoiceSearchService for hybrid search queries"
Write-Host ""
