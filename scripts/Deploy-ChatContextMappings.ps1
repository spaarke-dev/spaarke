<#
.SYNOPSIS
    Deploy chat context mapping seed data to Dataverse.

.DESCRIPTION
    Creates sprk_aichatcontextmap records from data/chat-context-mappings.json.
    Resolves playbook names to GUIDs dynamically from Dataverse.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.EXAMPLE
    .\scripts\Deploy-ChatContextMappings.ps1
    .\scripts\Deploy-ChatContextMappings.ps1 -EnvironmentUrl "https://myorg.crm.dynamics.com"
#>
param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"

# --- Auth ---
Write-Host "`n=== Deploy Chat Context Mappings ===" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl"

$tokenResponse = az account get-access-token --resource $EnvironmentUrl 2>$null | ConvertFrom-Json
if (-not $tokenResponse) {
    Write-Error "Failed to get access token. Run 'az login' first."
    return
}
$token = $tokenResponse.accessToken
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type"  = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
}
$apiBase = "$EnvironmentUrl/api/data/v9.2"

# --- Load seed data ---
$seedFile = Join-Path $PSScriptRoot "..\data\chat-context-mappings.json"
if (-not (Test-Path $seedFile)) {
    Write-Error "Seed data not found: $seedFile"
    return
}
$seedData = Get-Content $seedFile -Raw | ConvertFrom-Json
Write-Host "Loaded $($seedData.mappings.Count) mappings from seed data"

# --- Resolve playbook names to GUIDs ---
Write-Host "`nStep 1: Resolving playbook names to GUIDs..." -ForegroundColor Yellow

$playbookCache = @{}
foreach ($mapping in $seedData.mappings) {
    $name = $mapping.playbookName
    if ($playbookCache.ContainsKey($name)) { continue }

    $filter = "`$filter=sprk_name eq '$name' and statecode eq 0&`$select=sprk_analysisplaybookid,sprk_name&`$top=1"
    $response = Invoke-RestMethod -Uri "$apiBase/sprk_analysisplaybooks?$filter" -Headers $headers -Method Get

    if ($response.value.Count -eq 0) {
        Write-Warning "Playbook '$name' not found in Dataverse. Skipping mappings that reference it."
        $playbookCache[$name] = $null
    } else {
        $playbookId = $response.value[0].sprk_analysisplaybookid
        $playbookCache[$name] = $playbookId
        Write-Host "  Resolved '$name' -> $playbookId" -ForegroundColor Green
    }
}

# --- Create mapping records ---
Write-Host "`nStep 2: Creating context mapping records..." -ForegroundColor Yellow

$created = 0
$skipped = 0

foreach ($mapping in $seedData.mappings) {
    $playbookId = $playbookCache[$mapping.playbookName]
    if (-not $playbookId) {
        Write-Warning "  Skipping '$($mapping.name)' — playbook not found"
        $skipped++
        continue
    }

    # Check if mapping already exists
    $filter = "`$filter=sprk_entitytype eq '$($mapping.entityType)' and sprk_pagetype eq $($mapping.pageType) and _sprk_playbookid_value eq '$playbookId' and statecode eq 0"
    $topParam = '&$top=1'
    $queryUri = "$apiBase/sprk_aichatcontextmaps?$filter$topParam"
    $existing = Invoke-RestMethod -Uri $queryUri -Headers $headers -Method Get

    if ($existing.value.Count -gt 0) {
        Write-Host "  Already exists: '$($mapping.name)' — skipping" -ForegroundColor DarkGray
        $skipped++
        continue
    }

    # Create the mapping record
    $body = @{
        "sprk_name"        = $mapping.name
        "sprk_entitytype"  = $mapping.entityType
        "sprk_pagetype"    = $mapping.pageType
        "sprk_sortorder"   = $mapping.sortOrder
        "sprk_isdefault"   = $mapping.isDefault
        "sprk_isactive"    = $mapping.isActive
        "sprk_description" = $mapping.description
        "sprk_PlaybookId@odata.bind" = "/sprk_analysisplaybooks($playbookId)"
    } | ConvertTo-Json

    try {
        Invoke-RestMethod -Uri "$apiBase/sprk_aichatcontextmaps" -Headers $headers -Method Post -Body $body | Out-Null
        Write-Host "  Created: '$($mapping.name)'" -ForegroundColor Green
        $created++
    } catch {
        $errMsg = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $errJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($errJson.error.message) { $errMsg = $errJson.error.message }
        }
        Write-Warning "  Failed to create '$($mapping.name)': $errMsg"
        Write-Host "    Body: $body" -ForegroundColor DarkGray
        $skipped++
    }
}

# --- Summary ---
Write-Host "`n=== Deployment Complete ===" -ForegroundColor Cyan
Write-Host "Created: $created | Skipped: $skipped | Total: $($seedData.mappings.Count)"
