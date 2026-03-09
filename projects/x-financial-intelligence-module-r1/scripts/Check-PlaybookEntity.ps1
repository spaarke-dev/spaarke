<#
.SYNOPSIS
    Checks if the sprk_aiplaybook entity exists in Dataverse and finds its correct entity set name.

.DESCRIPTION
    Queries Dataverse metadata to locate the AI Playbook entity and determine the correct
    Web API entity set name for creating records.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to 'https://spaarkedev1.crm.dynamics.com'.

.EXAMPLE
    .\Check-PlaybookEntity.ps1

.NOTES
    Finance Intelligence Module R1 - Troubleshooting
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Check AI Playbook Entity" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Acquire bearer token
Write-Host "Acquiring bearer token..." -ForegroundColor Yellow
$tenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
$tokenJson = az account get-access-token --resource "$EnvironmentUrl" --tenant $tenantId 2>&1 | Out-String

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to acquire token. Run: az login --tenant $tenantId"
    exit 1
}

$tokenObj = $tokenJson | ConvertFrom-Json
$bearerToken = $tokenObj.accessToken

$headers = @{
    "Authorization" = "Bearer $bearerToken"
    "Content-Type"  = "application/json"
    "Accept"        = "application/json"
}

$apiUrl = "$EnvironmentUrl/api/data/v9.2"

Write-Host "Searching for AI Playbook entity..." -ForegroundColor Yellow
Write-Host ""

# Search for entities with 'playbook' in the name
$metadataUrl = "$apiUrl/EntityDefinitions?`$filter=contains(LogicalName,'playbook')&`$select=LogicalName,EntitySetName,DisplayName,SchemaName"

try {
    $entities = Invoke-RestMethod -Uri $metadataUrl -Method Get -Headers $headers

    if ($entities.value.Count -eq 0) {
        Write-Host "❌ No entities found with 'playbook' in the name." -ForegroundColor Red
        Write-Host ""
        Write-Host "Searching for entities with 'ai' in the name..." -ForegroundColor Yellow

        $metadataUrl = "$apiUrl/EntityDefinitions?`$filter=contains(LogicalName,'ai')&`$select=LogicalName,EntitySetName,DisplayName,SchemaName"
        $entities = Invoke-RestMethod -Uri $metadataUrl -Method Get -Headers $headers
    }

    if ($entities.value.Count -eq 0) {
        Write-Host "❌ No AI-related entities found." -ForegroundColor Red
        Write-Host ""
        Write-Host "The sprk_aiplaybook entity does not exist in this environment." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Options:" -ForegroundColor Cyan
        Write-Host "  1. Create the entity manually in Dataverse (recommended)"
        Write-Host "  2. Use the existing msdyn_aiplaybook entity (if available)"
        Write-Host "  3. Create playbook records manually via Dataverse UI"
        Write-Host ""
        exit 0
    }

    Write-Host "✅ Found $($entities.value.Count) entity/entities:" -ForegroundColor Green
    Write-Host ""

    $entities.value | ForEach-Object {
        Write-Host "  Entity:" -ForegroundColor Cyan
        Write-Host "    Logical Name: $($_.LogicalName)"
        Write-Host "    Entity Set Name: $($_.EntitySetName)"
        Write-Host "    Schema Name: $($_.SchemaName)"
        if ($_.DisplayName.UserLocalizedLabel) {
            Write-Host "    Display Name: $($_.DisplayName.UserLocalizedLabel.Label)"
        }
        Write-Host ""
    }

    # Check if sprk_aiplaybook exists
    $aiPlaybook = $entities.value | Where-Object { $_.LogicalName -eq "sprk_aiplaybook" }

    if ($aiPlaybook) {
        Write-Host "✅ sprk_aiplaybook entity exists!" -ForegroundColor Green
        Write-Host "   Use entity set name: $($aiPlaybook.EntitySetName)" -ForegroundColor Green
        Write-Host ""
    } else {
        Write-Host "⚠️ sprk_aiplaybook entity not found." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Available alternatives:" -ForegroundColor Cyan
        $entities.value | ForEach-Object {
            Write-Host "  - $($_.LogicalName) (entity set: $($_.EntitySetName))"
        }
        Write-Host ""
    }

} catch {
    Write-Error "Failed to query metadata: $_"
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
