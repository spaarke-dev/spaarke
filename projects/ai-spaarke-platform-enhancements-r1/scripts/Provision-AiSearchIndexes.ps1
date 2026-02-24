<#
.SYNOPSIS
    Provisions the knowledge-index and discovery-index in Azure AI Search (spaarke-search-dev).

.DESCRIPTION
    Creates or updates the two AI Search indexes required by the AI Platform Foundation (AIPL-016).
    Uses the Azure AI Search REST API via az rest (API version 2024-07-01).

    - knowledge-index: 512-token chunks, 1536-dim vectors (text-embedding-3-small), semantic config
    - discovery-index: 1024-token chunks, 1536-dim vectors, adds entityMentions field

    Both indexes:
    - Enforce tenant isolation: tenantId is filterable + facetable (ADR-014)
    - Use HNSW (cosine, m=4, efConstruction=400, efSearch=500)
    - Have semantic configurations prioritizing content + sectionTitle

.PARAMETER SearchServiceName
    Azure AI Search service name. Default: spaarke-search-dev

.PARAMETER ResourceGroup
    Azure resource group name. Default: spe-infrastructure-westus2

.PARAMETER ApiVersion
    Azure AI Search REST API version. Default: 2024-07-01

.PARAMETER WhatIf
    Show what would be done without actually creating indexes.

.EXAMPLE
    # Provision both indexes (requires az login)
    pwsh .\Provision-AiSearchIndexes.ps1

.EXAMPLE
    # Preview without creating
    pwsh .\Provision-AiSearchIndexes.ps1 -WhatIf

.EXAMPLE
    # Target a different environment
    pwsh .\Provision-AiSearchIndexes.ps1 -SearchServiceName spaarke-search-prod -ResourceGroup spe-infrastructure-prod-westus2

.NOTES
    Prerequisites:
    - az CLI installed and authenticated (az login)
    - Contributor or Search Service Contributor role on the AI Search service
    - Azure AI Search API version 2024-07-01 or later (required for semantic defaultConfiguration)

    Index definitions are in: infrastructure/ai-search/knowledge-index.json
                              infrastructure/ai-search/discovery-index.json

    Related tasks: AIPL-016 (this task), AIPL-013 (RagIndexingPipeline), AIPL-015 (KnowledgeBaseEndpoints)

    IMPORTANT: Do NOT create indexes from application startup code. This script is the
               authoritative provisioning path. (constraint from AIPL-016)
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $SearchServiceName = "spaarke-search-dev",
    [string] $ResourceGroup = "spe-infrastructure-westus2",
    [string] $ApiVersion = "2024-07-01"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Resolve paths relative to repo root (script is in projects/.../scripts/, repo root is 4 levels up)
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "../../../")
$InfraDir = Join-Path $RepoRoot "infrastructure/ai-search"

$KnowledgeIndexFile = Join-Path $InfraDir "knowledge-index.json"
$DiscoveryIndexFile = Join-Path $InfraDir "discovery-index.json"

Write-Host ""
Write-Host "=== Provision-AiSearchIndexes ===" -ForegroundColor Cyan
Write-Host "  Search Service : $SearchServiceName"
Write-Host "  Resource Group : $ResourceGroup"
Write-Host "  API Version    : $ApiVersion"
Write-Host "  Repo Root      : $RepoRoot"
Write-Host ""

# --- Validate prerequisites ---

if (-not (Test-Path $KnowledgeIndexFile)) {
    Write-Error "knowledge-index.json not found at: $KnowledgeIndexFile"
    exit 1
}

if (-not (Test-Path $DiscoveryIndexFile)) {
    Write-Error "discovery-index.json not found at: $DiscoveryIndexFile"
    exit 1
}

# Verify az CLI is available
if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Error "Azure CLI (az) is not installed or not on PATH. Install from https://aka.ms/installazurecliwindows"
    exit 1
}

# Verify az is logged in
$accountInfo = az account show 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Not logged into Azure CLI. Run: az login"
    exit 1
}

$account = $accountInfo | ConvertFrom-Json
Write-Host "  Subscription   : $($account.name) ($($account.id))" -ForegroundColor DarkGray
Write-Host ""

# Compute dry-run flag from WhatIfPreference (ActionPreference enum, not a switch)
$isDryRun = ($WhatIfPreference -eq [System.Management.Automation.ActionPreference]::Continue)

# --- Helper function ---

function Invoke-AiSearchIndexUpsert {
    param(
        [string] $IndexName,
        [string] $SchemaFile,
        [string] $ServiceName,
        [string] $RG,
        [string] $ApiVer,
        [bool]   $DryRun
    )

    $baseUrl = "https://$ServiceName.search.windows.net"
    $indexUri = "$baseUrl/indexes/${IndexName}?api-version=${ApiVer}&allowIndexDowntime=false"

    Write-Host "[Index: $IndexName]" -ForegroundColor Yellow

    # Step 1: Check if index exists
    Write-Host "  Checking if index exists..." -NoNewline
    $existsOutput = az search index show `
        --service-name $ServiceName `
        --resource-group $RG `
        --name $IndexName 2>&1

    $exists = ($LASTEXITCODE -eq 0)
    if ($exists) {
        Write-Host " EXISTS" -ForegroundColor Green
    } else {
        Write-Host " NOT FOUND (will create)" -ForegroundColor DarkYellow
    }

    # Step 2: Create or update via az rest (PUT)
    if ($DryRun) {
        Write-Host "  [WhatIf] Would PUT $indexUri" -ForegroundColor DarkGray
        Write-Host "  [WhatIf] Schema file: $SchemaFile" -ForegroundColor DarkGray
        return $true
    }

    Write-Host "  Provisioning index via REST API..."

    # Get admin API key
    $adminKeyOutput = az search admin-key show `
        --service-name $ServiceName `
        --resource-group $RG `
        --query "primaryKey" -o tsv 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Error "  Failed to retrieve admin key for $ServiceName. Check permissions."
        return $false
    }

    $adminKey = $adminKeyOutput.Trim()

    # PUT the index schema via Invoke-RestMethod (avoids az rest Azure AD injection + & shell escaping issues)
    try {
        $schemaBody = Get-Content $SchemaFile -Raw
        $null = Invoke-RestMethod `
            -Method PUT `
            -Uri $indexUri `
            -Headers @{ "api-key" = $adminKey } `
            -ContentType "application/json" `
            -Body $schemaBody
        Write-Host "  Provisioned successfully." -ForegroundColor Green
        return $true
    } catch {
        Write-Warning "  PUT failed. Response: $($_.Exception.Message)"
        return $false
    }
}

# --- Provision knowledge-index ---

$knowledgeOk = Invoke-AiSearchIndexUpsert `
    -IndexName "knowledge-index" `
    -SchemaFile $KnowledgeIndexFile `
    -ServiceName $SearchServiceName `
    -RG $ResourceGroup `
    -ApiVer $ApiVersion `
    -DryRun $isDryRun

Write-Host ""

# --- Provision discovery-index ---

$discoveryOk = Invoke-AiSearchIndexUpsert `
    -IndexName "discovery-index" `
    -SchemaFile $DiscoveryIndexFile `
    -ServiceName $SearchServiceName `
    -RG $ResourceGroup `
    -ApiVer $ApiVersion `
    -DryRun $isDryRun

Write-Host ""

# --- Verification (only when not WhatIf) ---

if (-not $isDryRun) {

    Write-Host "=== Verification ===" -ForegroundColor Cyan

    foreach ($idxName in @("knowledge-index", "discovery-index")) {
        Write-Host ""
        Write-Host "[Verify: $idxName]" -ForegroundColor Yellow

        $verifyOutput = az search index show `
            --service-name $SearchServiceName `
            --resource-group $ResourceGroup `
            --name $idxName 2>&1

        if ($LASTEXITCODE -eq 0) {
            $indexDef = $verifyOutput | ConvertFrom-Json
            $fieldNames = $indexDef.fields | ForEach-Object { $_.name }
            $requiredFields = @("id", "documentId", "tenantId", "content", "contentVector", "sectionTitle", "documentType", "pageNumber", "chunkIndex", "indexedAt")

            $missingFields = $requiredFields | Where-Object { $_ -notin $fieldNames }

            if ($missingFields.Count -eq 0) {
                Write-Host "  All required fields present." -ForegroundColor Green
            } else {
                Write-Warning "  Missing fields: $($missingFields -join ', ')"
            }

            # Check tenantId facetable (ADR-014)
            $tenantField = $indexDef.fields | Where-Object { $_.name -eq "tenantId" }
            if ($tenantField -and $tenantField.filterable -and $tenantField.facetable) {
                Write-Host "  tenantId: filterable=true, facetable=true (ADR-014 compliant)" -ForegroundColor Green
            } else {
                Write-Warning "  tenantId missing filterable or facetable! ADR-014 violation."
            }

            # Check semantic config
            $semanticConfigs = $indexDef.semantic.configurations | ForEach-Object { $_.name }
            if ($semanticConfigs.Count -gt 0) {
                Write-Host "  Semantic configs: $($semanticConfigs -join ', ')" -ForegroundColor Green
            } else {
                Write-Warning "  No semantic configuration found."
            }

            # Check entityMentions for discovery-index
            if ($idxName -eq "discovery-index") {
                $entityField = $indexDef.fields | Where-Object { $_.name -eq "entityMentions" }
                if ($entityField) {
                    Write-Host "  entityMentions field present (discovery-index specific)" -ForegroundColor Green
                } else {
                    Write-Warning "  entityMentions field missing from discovery-index."
                }
            }

        } else {
            Write-Warning "  Index not found after provisioning. Manual check required."
        }
    }
}

Write-Host ""

# --- Summary ---

Write-Host "=== Summary ===" -ForegroundColor Cyan

if ($isDryRun) {
    Write-Host "WhatIf mode: No changes were made." -ForegroundColor DarkYellow
} else {
    $allOk = $knowledgeOk -and $discoveryOk
    if ($allOk) {
        Write-Host "Both indexes provisioned successfully." -ForegroundColor Green
        Write-Host ""
        Write-Host "Next steps:"
        Write-Host "  - AIPL-013: RagIndexingPipeline can now index documents to both indexes"
        Write-Host "  - AIPL-015: KnowledgeBaseEndpoints can now query both indexes"
        Write-Host "  - AIPL-018: Phase 2 API deploy unblocked"
    } else {
        Write-Warning "One or more indexes failed to provision. Review errors above."
        exit 1
    }
}
