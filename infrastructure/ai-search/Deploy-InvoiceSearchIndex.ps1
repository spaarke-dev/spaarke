<#
.SYNOPSIS
    Creates or updates the spaarke-invoices-{TenantId} AI Search index using bearer token authentication.

.DESCRIPTION
    This script deploys the invoice search index schema to Azure AI Search using the REST API
    with Azure CLI bearer token authentication (no admin keys required).

    The index supports hybrid search (keyword + vector + semantic ranking) for invoice documents
    with typed financial metadata fields for range queries and faceting.

    Index naming convention: spaarke-invoices-{TenantId}
    Schema: infrastructure/ai-search/invoice-index-schema.json
    Vector: text-embedding-3-large (3072 dimensions), HNSW with cosine metric
    Semantic: invoice-semantic config (content title, vendorName + invoiceNumber keywords)

.PARAMETER SearchServiceName
    Name of the Azure AI Search service. Defaults to 'spaarke-search-dev'.

.PARAMETER TenantId
    Tenant identifier appended to the index name. Defaults to 'dev'.
    The resulting index name will be: spaarke-invoices-{TenantId}

.PARAMETER SchemaPath
    Path to the invoice index schema JSON file. Defaults to the co-located
    invoice-index-schema.json relative to this script.

.PARAMETER ApiVersion
    Azure AI Search REST API version. Defaults to '2024-07-01'.

.EXAMPLE
    # Deploy dev tenant index (defaults)
    .\Deploy-InvoiceSearchIndex.ps1

.EXAMPLE
    # Deploy tenant-specific index
    .\Deploy-InvoiceSearchIndex.ps1 -TenantId "a221a95e-1234-5678-9abc-def012345678"

.EXAMPLE
    # Deploy to a different search service
    .\Deploy-InvoiceSearchIndex.ps1 -SearchServiceName "spaarke-search-staging" -TenantId "staging"

.NOTES
    Finance Intelligence Module R1 - Task 031
    Prerequisites:
    - Azure CLI installed and logged in (az login)
    - Appropriate RBAC role on the Azure AI Search service (Search Service Contributor or Contributor)
    - The Azure AI Search service must have semantic search enabled (standard tier)
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$SearchServiceName = "spaarke-search-dev",

    [Parameter(Mandatory = $false)]
    [string]$TenantId = "dev",

    [Parameter(Mandatory = $false)]
    [string]$SchemaPath,

    [Parameter(Mandatory = $false)]
    [string]$ApiVersion = "2024-07-01"
)

$ErrorActionPreference = "Stop"

# -------------------------------------------------------------------
# Derive index name and resolve schema path
# -------------------------------------------------------------------
$IndexName = "spaarke-invoices-$TenantId"

if (-not $SchemaPath) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $SchemaPath = Join-Path $scriptDir "invoice-index-schema.json"
}

# -------------------------------------------------------------------
# Banner
# -------------------------------------------------------------------
Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Deploy Invoice AI Search Index" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Search Service : $SearchServiceName"
Write-Host "  Tenant ID      : $TenantId"
Write-Host "  Index Name     : $IndexName"
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

# Override the index name in the schema to match the tenant-specific name
if ($schema.name -ne $IndexName) {
    Write-Host "       Overriding index name: '$($schema.name)' -> '$IndexName'"
    $schema.name = $IndexName
    $schemaJson = $schema | ConvertTo-Json -Depth 10
}

$expectedFieldCount = ($schema.fields | Measure-Object).Count
Write-Host "       Schema loaded: $expectedFieldCount fields, $(($schema.vectorSearch.profiles | Measure-Object).Count) vector profile(s), $(($schema.semantic.configurations | Measure-Object).Count) semantic config(s)"
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
    Write-Host "  - Semantic search not enabled: Upgrade search service to standard tier with semantic search"
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
$verifiedSemanticConfigs = ($verified.semantic.configurations | Measure-Object).Count
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

# Validate semantic config
$semanticOk = $false
$semanticDetails = "none"
if ($verified.semantic -and $verified.semantic.configurations) {
    $invoiceSemantic = $verified.semantic.configurations | Where-Object { $_.name -eq "invoice-semantic" }
    if ($invoiceSemantic) {
        $semanticOk = $true
        $titleField = $invoiceSemantic.prioritizedFields.titleField.fieldName
        $contentFields = ($invoiceSemantic.prioritizedFields.prioritizedContentFields | ForEach-Object { $_.fieldName }) -join ", "
        $keywordFields = ($invoiceSemantic.prioritizedFields.prioritizedKeywordsFields | ForEach-Object { $_.fieldName }) -join ", "
        $semanticDetails = "title=$titleField, content=[$contentFields], keywords=[$keywordFields]"
    }
}
$semanticStatus = if ($semanticOk) { "PASS" } else { "FAIL" }
$semanticColor = if ($semanticOk) { "Green" } else { "Red" }

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
Write-Host "  [$semanticStatus] Semantic Config: $semanticDetails" -ForegroundColor $semanticColor
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

# Print semantic config details
Write-Host ""
Write-Host "  --- Semantic Configuration ---" -ForegroundColor Cyan
Write-Host ""
Write-Host "    Configurations ($verifiedSemanticConfigs):"
$verified.semantic.configurations | ForEach-Object {
    Write-Host "      - $($_.name)"
    Write-Host "        Title: $($_.prioritizedFields.titleField.fieldName)"
    $cFields = ($_.prioritizedFields.prioritizedContentFields | ForEach-Object { $_.fieldName }) -join ", "
    Write-Host "        Content: $cFields"
    $kFields = ($_.prioritizedFields.prioritizedKeywordsFields | ForEach-Object { $_.fieldName }) -join ", "
    Write-Host "        Keywords: $kFields"
}

# -------------------------------------------------------------------
# Final result
# -------------------------------------------------------------------
Write-Host ""
$allPassed = $fieldCountOk -and $vectorOk -and $semanticOk
if ($allPassed) {
    Write-Host "=========================================" -ForegroundColor Green
    Write-Host " ALL VERIFICATIONS PASSED" -ForegroundColor Green
    Write-Host "=========================================" -ForegroundColor Green
}
else {
    Write-Host "=========================================" -ForegroundColor Red
    Write-Host " SOME VERIFICATIONS FAILED — Review above" -ForegroundColor Red
    Write-Host "=========================================" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Verify the index in Azure Portal: $searchEndpoint"
Write-Host "  2. Implement InvoiceIndexingJobHandler to index invoice documents (Task 032)"
Write-Host "  3. Implement InvoiceSearchService for hybrid search queries (Task 033)"
Write-Host ""
