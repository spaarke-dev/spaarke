<#
.SYNOPSIS
    Creates or updates the playbook-embeddings AI Search index using bearer token authentication.

.DESCRIPTION
    This script deploys the playbook embeddings index schema to Azure AI Search using the REST API
    with Azure CLI bearer token authentication (no admin keys required).

    The index supports vector search for playbook semantic matching using text-embedding-3-large
    (3072 dimensions) with HNSW algorithm and cosine metric.

    Index name: playbook-embeddings
    Schema: infrastructure/ai-search/playbook-embeddings.json
    Vector: text-embedding-3-large (3072 dimensions), HNSW with cosine metric

.PARAMETER SearchServiceName
    Name of the Azure AI Search service. Defaults to 'spaarke-search-dev'.

.PARAMETER SchemaPath
    Path to the playbook embeddings index schema JSON file. Defaults to the co-located
    playbook-embeddings.json in infrastructure/ai-search/ relative to repo root.

.PARAMETER ApiVersion
    Azure AI Search REST API version. Defaults to '2024-07-01'.

.EXAMPLE
    # Deploy using defaults (spaarke-search-dev)
    .\Create-PlaybookEmbeddingsIndex.ps1

.EXAMPLE
    # Deploy to a different search service
    .\Create-PlaybookEmbeddingsIndex.ps1 -SearchServiceName "spaarke-search-staging"

.NOTES
    SprkChat Platform Enhancement R2 - Task R2-003
    Prerequisites:
    - Azure CLI installed and logged in (az login)
    - Appropriate RBAC role on the Azure AI Search service (Search Service Contributor or Contributor)
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$SearchServiceName = "spaarke-search-dev",

    [Parameter(Mandatory = $false)]
    [string]$SchemaPath,

    [Parameter(Mandatory = $false)]
    [string]$ApiVersion = "2024-07-01"
)

$ErrorActionPreference = "Stop"

# -------------------------------------------------------------------
# Resolve schema path (default: infrastructure/ai-search/playbook-embeddings.json)
# -------------------------------------------------------------------
if (-not $SchemaPath) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $repoRoot = Split-Path -Parent $scriptDir
    $SchemaPath = Join-Path $repoRoot "infrastructure" "ai-search" "playbook-embeddings.json"
}

# -------------------------------------------------------------------
# Banner
# -------------------------------------------------------------------
Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Deploy Playbook Embeddings AI Search Index" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Search Service : $SearchServiceName"
Write-Host "  Schema Path    : $SchemaPath"
Write-Host "  API Version    : $ApiVersion"
Write-Host ""

# -------------------------------------------------------------------
# Step 1: Validate schema file exists
# -------------------------------------------------------------------
if (-not (Test-Path $SchemaPath)) {
    Write-Error "Index schema not found at: $SchemaPath"
    exit 1
}

Write-Host "[1/5] Loading index schema from $SchemaPath..." -ForegroundColor Yellow
$schemaJson = Get-Content $SchemaPath -Raw
$schema = $schemaJson | ConvertFrom-Json

$IndexName = $schema.name
$expectedFieldCount = ($schema.fields | Measure-Object).Count
Write-Host "       Index Name  : $IndexName"
Write-Host "       Schema loaded: $expectedFieldCount fields, $(($schema.vectorSearch.profiles | Measure-Object).Count) vector profile(s)"
Write-Host ""

# -------------------------------------------------------------------
# Step 2: Acquire bearer token via Azure CLI
# -------------------------------------------------------------------
Write-Host "[2/5] Acquiring bearer token via Azure CLI..." -ForegroundColor Yellow

$tokenJson = az account get-access-token --resource "https://search.azure.com" 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to acquire bearer token. Ensure you are logged in with 'az login' and have access to the search service."
    Write-Host "  Error: $tokenJson" -ForegroundColor Red
    exit 1
}

$tokenObj = $tokenJson | ConvertFrom-Json
$bearerToken = $tokenObj.accessToken

if (-not $bearerToken) {
    Write-Error "Bearer token is empty. Check Azure CLI authentication."
    exit 1
}

$tokenExpiry = $tokenObj.expiresOn
Write-Host "       Token acquired (expires: $tokenExpiry)"
Write-Host ""

# -------------------------------------------------------------------
# Construct headers and endpoint
# -------------------------------------------------------------------
$searchEndpoint = "https://$SearchServiceName.search.windows.net"
$indexUrl = "$searchEndpoint/indexes/$IndexName`?api-version=$ApiVersion"

$headers = @{
    "Content-Type"  = "application/json"
    "Authorization" = "Bearer $bearerToken"
}

# -------------------------------------------------------------------
# Step 3: Check if index already exists
# -------------------------------------------------------------------
Write-Host "[3/5] Checking if index '$IndexName' already exists..." -ForegroundColor Yellow

$indexExists = $false
try {
    $existing = Invoke-RestMethod -Uri $indexUrl -Method Get -Headers $headers
    $indexExists = $true
    $existingFieldCount = ($existing.fields | Measure-Object).Count
    Write-Host "       Index exists with $existingFieldCount fields. Will update." -ForegroundColor Yellow
}
catch {
    $statusCode = $null
    if ($_.Exception.Response) {
        $statusCode = [int]$_.Exception.Response.StatusCode
    }
    if ($statusCode -eq 404 -or $_.Exception.Message -match "404") {
        Write-Host "       Index does not exist. Will create." -ForegroundColor Green
    }
    else {
        Write-Error "Failed to check index existence: $($_.Exception.Message)"
        exit 1
    }
}
Write-Host ""

# -------------------------------------------------------------------
# Step 4: PUT the index schema (create or update)
# -------------------------------------------------------------------
$action = if ($indexExists) { "Updating" } else { "Creating" }
Write-Host "[4/5] $action index '$IndexName'..." -ForegroundColor Yellow

try {
    $response = Invoke-RestMethod -Uri $indexUrl -Method Put -Headers $headers -Body $schemaJson

    Write-Host "       $action succeeded!" -ForegroundColor Green
}
catch {
    Write-Error "Failed to deploy index: $($_.Exception.Message)"
    Write-Host ""
    if ($_.Exception.Response) {
        try {
            $stream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($stream)
            $errorBody = $reader.ReadToEnd()
            Write-Host "  Response body: $errorBody" -ForegroundColor Red
        }
        catch {
            # Ignore error reading response body
        }
    }
    Write-Host ""
    Write-Host "Common issues:" -ForegroundColor Yellow
    Write-Host "  - 403 Forbidden: Ensure your Azure CLI identity has 'Search Service Contributor' RBAC role"
    Write-Host "  - Vector dimensions mismatch: Ensure text-embedding-3-large is configured (3072 dimensions)"
    Write-Host "  - Index locked: Delete and recreate if vector field changes are needed"
    exit 1
}
Write-Host ""

# -------------------------------------------------------------------
# Step 5: Verify the index by reading it back
# -------------------------------------------------------------------
Write-Host "[5/5] Verifying index '$IndexName'..." -ForegroundColor Yellow

try {
    $verified = Invoke-RestMethod -Uri $indexUrl -Method Get -Headers $headers
}
catch {
    Write-Error "Index was created but verification GET failed: $($_.Exception.Message)"
    exit 1
}

$verifiedFieldCount = ($verified.fields | Measure-Object).Count
$verifiedVectorProfiles = ($verified.vectorSearch.profiles | Measure-Object).Count
$verifiedAlgorithms = ($verified.vectorSearch.algorithms | Measure-Object).Count

# Validate field count
$fieldCountOk = $verifiedFieldCount -eq $expectedFieldCount
$fieldCountStatus = if ($fieldCountOk) { "PASS" } else { "FAIL" }
$fieldCountColor = if ($fieldCountOk) { "Green" } else { "Red" }

# Validate vector config
$vectorOk = $false
$vectorDetails = "none"
if ($verified.vectorSearch -and $verified.vectorSearch.algorithms) {
    $hnswAlgo = $verified.vectorSearch.algorithms | Where-Object { $_.kind -eq "hnsw" } | Select-Object -First 1
    if ($hnswAlgo) {
        $metric = $hnswAlgo.hnswParameters.metric
        $vectorField = $verified.fields | Where-Object { $_.dimensions -eq 3072 } | Select-Object -First 1
        if ($metric -eq "cosine" -and $vectorField) {
            $vectorOk = $true
            $vectorDetails = "HNSW, cosine, $($vectorField.dimensions) dimensions"
        }
        else {
            $vectorDetails = "HNSW found but metric=$metric, 3072-dim field=$(if($vectorField){'present'}else{'missing'})"
        }
    }
}
$vectorStatus = if ($vectorOk) { "PASS" } else { "FAIL" }
$vectorColor = if ($vectorOk) { "Green" } else { "Red" }

# -------------------------------------------------------------------
# Print Summary
# -------------------------------------------------------------------
Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Deployment Summary" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Index Name     : $($verified.name)"
Write-Host "  Endpoint       : $searchEndpoint"
Write-Host ""

Write-Host "  --- Verification ---" -ForegroundColor Cyan
Write-Host ""
Write-Host "  [$fieldCountStatus] Field Count: $verifiedFieldCount (expected $expectedFieldCount)" -ForegroundColor $fieldCountColor
Write-Host "  [$vectorStatus] Vector Config: $vectorDetails" -ForegroundColor $vectorColor
Write-Host ""

# Print field listing
Write-Host "  --- Fields ($verifiedFieldCount) ---" -ForegroundColor Cyan
Write-Host ""
$verified.fields | ForEach-Object {
    $attrs = @()
    if ($_.key) { $attrs += "KEY" }
    if ($_.searchable) { $attrs += "searchable" }
    if ($_.filterable) { $attrs += "filterable" }
    if ($_.sortable) { $attrs += "sortable" }
    if ($_.facetable) { $attrs += "facetable" }
    if ($_.dimensions) { $attrs += "$($_.dimensions)d" }
    $attrStr = if ($attrs.Count -gt 0) { " [$($attrs -join ', ')]" } else { "" }
    Write-Host "    $($_.name): $($_.type)$attrStr"
}

# Print vector search details
Write-Host ""
Write-Host "  --- Vector Search ---" -ForegroundColor Cyan
Write-Host ""
Write-Host "    Algorithms ($verifiedAlgorithms):"
$verified.vectorSearch.algorithms | ForEach-Object {
    Write-Host "      - $($_.name) ($($_.kind), metric: $($_.hnswParameters.metric))"
    Write-Host "        m=$($_.hnswParameters.m), efConstruction=$($_.hnswParameters.efConstruction), efSearch=$($_.hnswParameters.efSearch)"
}
Write-Host "    Profiles ($verifiedVectorProfiles):"
$verified.vectorSearch.profiles | ForEach-Object {
    Write-Host "      - $($_.name) -> $($_.algorithm)"
}

# -------------------------------------------------------------------
# Final result
# -------------------------------------------------------------------
Write-Host ""
$allPassed = $fieldCountOk -and $vectorOk
if ($allPassed) {
    Write-Host "=========================================" -ForegroundColor Green
    Write-Host " ALL VERIFICATIONS PASSED" -ForegroundColor Green
    Write-Host "=========================================" -ForegroundColor Green
}
else {
    Write-Host "=========================================" -ForegroundColor Red
    Write-Host " SOME VERIFICATIONS FAILED - Review above" -ForegroundColor Red
    Write-Host "=========================================" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Verify the index in Azure Portal: $searchEndpoint"
Write-Host "  2. PlaybookEmbeddingService.cs is ready to index and search playbooks (Task R2-003)"
Write-Host "  3. Wire embedding pipeline trigger (Task R2-016)"
Write-Host ""
