# Deploy-IndexSchemas.ps1
# Deploys AI Search index schema files to the target Azure AI Search service.
# Adding a new field to an existing index is a non-breaking schema update (PUT is idempotent).
#
# Usage:
#   .\Deploy-IndexSchemas.ps1
#   .\Deploy-IndexSchemas.ps1 -SearchServiceName spaarke-search-staging -DryRun
#   .\Deploy-IndexSchemas.ps1 -Indexes knowledge,discovery
#
# Prerequisites:
#   - az login (authenticated to Azure with access to Key Vault spaarke-spekvcert)
#   - infrastructure/ai-search/*.json schema files present
#
# Indexes deployed:
#   - spaarke-knowledge-index-v2  (infrastructure/ai-search/spaarke-knowledge-index-v2.json)
#   - discovery-index             (infrastructure/ai-search/discovery-index.json)
#   - spaarke-records-index       (infrastructure/ai-search/spaarke-records-index.json)
#
# Security field (AIPU2-005):
#   All three indexes carry a privilege_group_ids field (Collection(Edm.String), filterable=true,
#   searchable=false, retrievable=true). The AI pipeline uses it to restrict search results to
#   documents the requesting user's Entra groups are permitted to access:
#     $filter=privilege_group_ids/any(g: g eq '{entra-group-object-id}')

param(
    [string]$SearchServiceName = "spaarke-search-dev",
    [string]$KeyVaultName      = "spaarke-spekvcert",
    [string]$KeyVaultSecretName = "AiSearch--AdminKey",
    [string]$ApiVersion        = "2024-07-01",
    # Comma-separated subset to deploy: knowledge,discovery,records  (default: all three)
    [string]$Indexes           = "knowledge,discovery,records",
    [switch]$DryRun            = $false
)

$ErrorActionPreference = "Stop"

# Resolve repo root relative to this script so it works from any working directory
$RepoRoot   = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$SchemaDir  = Join-Path $RepoRoot "infrastructure\ai-search"

$IndexMap = @{
    knowledge = @{ File = "spaarke-knowledge-index-v2.json"; IndexName = "spaarke-knowledge-index" }
    discovery = @{ File = "discovery-index.json";            IndexName = "discovery-index"          }
    records   = @{ File = "spaarke-records-index.json";      IndexName = "spaarke-records-index"    }
}

$SelectedKeys = $Indexes -split "," | ForEach-Object { $_.Trim().ToLower() }

Write-Host "=== Deploy AI Search Index Schemas ===" -ForegroundColor Cyan
Write-Host "Service : $SearchServiceName"
Write-Host "Indexes : $($SelectedKeys -join ', ')"
Write-Host "Mode    : $(if ($DryRun) { 'DRY RUN' } else { 'LIVE' })"
Write-Host ""

# Retrieve admin key from Key Vault (never stored in code or committed files)
Write-Host "Retrieving admin key from Key Vault '$KeyVaultName'..." -ForegroundColor Gray
$adminKey = az keyvault secret show `
    --vault-name $KeyVaultName `
    --name $KeyVaultSecretName `
    --query value -o tsv 2>&1

if ($LASTEXITCODE -ne 0 -or -not $adminKey) {
    Write-Error "Failed to retrieve admin key from Key Vault. Ensure 'az login' is current and you have Get permission on the secret."
    exit 1
}
Write-Host "  Admin key retrieved." -ForegroundColor Green
Write-Host ""

$BaseUrl = "https://$SearchServiceName.search.windows.net"
$Headers = @{
    "api-key"      = $adminKey
    "Content-Type" = "application/json"
}

$Results = @()

foreach ($key in $SelectedKeys) {
    if (-not $IndexMap.ContainsKey($key)) {
        Write-Warning "Unknown index key '$key' — skipping. Valid keys: knowledge, discovery, records"
        continue
    }

    $entry     = $IndexMap[$key]
    $schemaFile = Join-Path $SchemaDir $entry.File
    $indexName  = $entry.IndexName

    Write-Host "--- $indexName ---" -ForegroundColor Cyan

    if (-not (Test-Path $schemaFile)) {
        Write-Error "Schema file not found: $schemaFile"
        $Results += [PSCustomObject]@{ Index = $indexName; Status = "ERROR - file not found" }
        continue
    }

    $schemaJson = Get-Content $schemaFile -Raw

    if ($DryRun) {
        Write-Host "  [DRY RUN] Would PUT $BaseUrl/indexes/$indexName`?api-version=$ApiVersion" -ForegroundColor Yellow
        Write-Host "  [DRY RUN] Schema file: $schemaFile" -ForegroundColor Yellow
        $Results += [PSCustomObject]@{ Index = $indexName; Status = "DRY RUN (skipped)" }
        continue
    }

    $putUrl = "$BaseUrl/indexes/$indexName`?api-version=$ApiVersion"

    Write-Host "  Deploying schema..." -ForegroundColor Gray
    try {
        $response = Invoke-RestMethod `
            -Uri $putUrl `
            -Method Put `
            -Headers $Headers `
            -Body $schemaJson `
            -ContentType "application/json"

        Write-Host "  Schema deployed successfully." -ForegroundColor Green
        $Results += [PSCustomObject]@{ Index = $indexName; Status = "OK" }
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        Write-Host "  ERROR deploying schema (HTTP $statusCode): $_" -ForegroundColor Red
        $Results += [PSCustomObject]@{ Index = $indexName; Status = "ERROR - HTTP $statusCode" }
        continue
    }

    # Verify the privilege_group_ids field is present in the deployed index
    Write-Host "  Verifying privilege_group_ids field..." -ForegroundColor Gray
    try {
        $getUrl  = "$BaseUrl/indexes/$indexName`?api-version=$ApiVersion"
        $indexed = Invoke-RestMethod -Uri $getUrl -Method Get -Headers $Headers
        $field   = $indexed.fields | Where-Object { $_.name -eq "privilege_group_ids" }

        if ($field) {
            Write-Host "  privilege_group_ids verified: type=$($field.type), filterable=$($field.filterable), searchable=$($field.searchable)" -ForegroundColor Green
        }
        else {
            Write-Warning "  privilege_group_ids field NOT found in deployed index — schema may not have applied."
        }
    }
    catch {
        Write-Warning "  Could not verify field (GET index failed): $_"
    }

    # Smoke-test: confirm the field accepts lambda filter expressions
    Write-Host "  Running filter smoke-test..." -ForegroundColor Gray
    try {
        $searchUrl  = "$BaseUrl/indexes/$indexName/docs/search`?api-version=$ApiVersion"
        $filterBody = '{"search": "*", "filter": "privilege_group_ids/any(g: g eq ''smoke-test-group-id'')", "top": 0}'
        $searchResp = Invoke-RestMethod -Uri $searchUrl -Method Post -Headers $Headers -Body $filterBody -ContentType "application/json"
        Write-Host "  Filter smoke-test passed (HTTP 200)." -ForegroundColor Green
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -eq 400) {
            Write-Warning "  Filter smoke-test returned HTTP 400 — field may not be filterable or index is still updating."
        }
        else {
            Write-Host "  Filter smoke-test HTTP $statusCode (non-fatal — index may be empty)." -ForegroundColor Gray
        }
    }

    Write-Host ""
}

Write-Host "=== Summary ===" -ForegroundColor Cyan
$Results | Format-Table -AutoSize
