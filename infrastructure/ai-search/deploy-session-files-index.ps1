<#
.SYNOPSIS
    Creates or updates the spaarke-session-files AI Search index for the
    Spaarke AI Platform Unification R5 chat-driven Summarize vertical slice.

.DESCRIPTION
    Deploys the session-files search index schema to Azure AI Search using the
    REST API. The index hosts session-scoped chunked file content for the R5
    chat agent's "Summarize a Document" capability.

    Per ADR-014, every document in this index MUST carry both `tenantId` AND
    `sessionId` for tenant isolation + session scoping. Both fields are
    filterable + facetable.

    The PUT REST verb is idempotent — re-running this script is safe and is
    the canonical mechanism for creating/updating the index.

    Schema: infrastructure/ai-search/spaarke-session-files.json
    Vector: text-embedding-3-large (3072 dimensions), HNSW with cosine metric
    Semantic: session-files-semantic-config (fileName title, content + tags)

.PARAMETER SubscriptionId
    Azure subscription ID. Defaults to the current subscription.

.PARAMETER ResourceGroup
    Resource group containing the Azure AI Search service.
    Default: 'spe-infrastructure-westus2' (Spaarke Dev convention).

.PARAMETER SearchServiceName
    Name of the Azure AI Search service.
    Default: 'spaarke-search-dev' (Spaarke Dev convention).

.PARAMETER IndexName
    Name of the index to create. Defaults to 'spaarke-session-files'.

.PARAMETER ApiVersion
    Azure AI Search REST API version. Defaults to '2024-07-01'.

.EXAMPLE
    # Deploy to Spaarke Dev (default)
    .\deploy-session-files-index.ps1

.EXAMPLE
    # Deploy to a custom search service
    .\deploy-session-files-index.ps1 -ResourceGroup "my-rg" -SearchServiceName "my-search"

.NOTES
    Spaarke AI Platform Unification R5 - Task 001 (D1-01)
    Prerequisites:
    - Azure CLI installed and logged in (az login)
    - Appropriate permissions on the Azure AI Search service
      (Contributor or Search Service Contributor)
    - The Azure AI Search service must have semantic search enabled
      (standard tier or higher)
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$SubscriptionId,

    [Parameter(Mandatory = $false)]
    [string]$ResourceGroup = "spe-infrastructure-westus2",

    [Parameter(Mandatory = $false)]
    [string]$SearchServiceName = "spaarke-search-dev",

    [Parameter(Mandatory = $false)]
    [string]$IndexName = "spaarke-session-files",

    [Parameter(Mandatory = $false)]
    [string]$ApiVersion = "2024-07-01"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Deploy Session-Files AI Search Index (R5 D1-01) ===" -ForegroundColor Cyan
Write-Host "Resource Group:   $ResourceGroup"
Write-Host "Search Service:   $SearchServiceName"
Write-Host "Index Name:       $IndexName"
Write-Host "API Version:      $ApiVersion"
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
$schemaPath = Join-Path $scriptDir "spaarke-session-files.json"

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

# Check if index already exists (informational only; PUT is idempotent)
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

# Create or update the index (PUT is idempotent — creates or updates)
$putUrl = "$searchEndpoint/indexes/$IndexName`?api-version=$ApiVersion"

Write-Host ""
Write-Host "Deploying index schema..."

try {
    $putResponse = Invoke-RestMethod -Uri $putUrl -Method Put -Headers $headers -Body $schemaJson

    if ($indexExists) {
        Write-Host "Index '$IndexName' updated successfully!" -ForegroundColor Green
    }
    else {
        Write-Host "Index '$IndexName' created successfully!" -ForegroundColor Green
    }

    # Re-fetch via GET to get the canonical post-deploy schema. PUT-update may return
    # a sparse response on existing-index updates; GET always returns the full shape.
    $response = Invoke-RestMethod -Uri $checkUrl -Method Get -Headers $headers

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

    # Verify ADR-014 invariant: both tenantId AND sessionId fields exist + filterable
    $tenantField = $response.fields | Where-Object { $_.name -eq 'tenantId' }
    $sessionField = $response.fields | Where-Object { $_.name -eq 'sessionId' }
    Write-Host ""
    Write-Host "ADR-014 isolation invariant:" -ForegroundColor Cyan
    if ($tenantField -and $tenantField.filterable -and $sessionField -and $sessionField.filterable) {
        Write-Host "  [OK] tenantId + sessionId both present and filterable" -ForegroundColor Green
    }
    else {
        Write-Error "  ADR-014 invariant FAILED: tenantId+sessionId not both filterable on '$IndexName'"
        exit 1
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
    Write-Host "  - Index locked: If you're changing vector field shape, delete the existing index first"
    exit 1
}

Write-Host ""
Write-Host "=== Deployment Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Verify the index in Azure Portal at $searchEndpoint"
Write-Host "  2. Run the smoke test: empty docs/search returns []"
Write-Host "  3. Proceed to R5 tasks 002 (RagSearchOptions sessionId) + 003 (RagIndexingPipeline)"
Write-Host ""
