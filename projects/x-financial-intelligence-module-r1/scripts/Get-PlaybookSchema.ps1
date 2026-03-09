<#
.SYNOPSIS
    Retrieves the actual field schema for sprk_analysisplaybook entity.

.DESCRIPTION
    Queries Dataverse metadata API to list all fields and their types in the
    sprk_analysisplaybook entity to determine correct field names for playbook creation.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to 'https://spaarkedev1.crm.dynamics.com'.

.EXAMPLE
    .\Get-PlaybookSchema.ps1

.NOTES
    Finance Intelligence Module R1 - Schema Discovery
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " Get sprk_analysisplaybook Schema" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

# Acquire bearer token
Write-Host "Acquiring bearer token..." -ForegroundColor Yellow
$tenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
$tokenJson = az account get-access-token --resource "$EnvironmentUrl" --tenant $tenantId 2>&1 | Out-String

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to acquire token."
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

Write-Host "Token acquired. Querying entity metadata..." -ForegroundColor Yellow
Write-Host ""

# Query entity metadata for sprk_analysisplaybook
$metadataUrl = "$apiUrl/EntityDefinitions(LogicalName='sprk_analysisplaybook')/Attributes?`$select=LogicalName,AttributeType,DisplayName,SchemaName,MaxLength,IsCustomAttribute"

try {
    $attributes = Invoke-RestMethod -Uri $metadataUrl -Method Get -Headers $headers

    Write-Host "✅ Found $($attributes.value.Count) fields in sprk_analysisplaybook:" -ForegroundColor Green
    Write-Host ""
    Write-Host "Custom Fields:" -ForegroundColor Cyan
    Write-Host ""

    $customFields = $attributes.value | Where-Object { $_.IsCustomAttribute -eq $true } | Sort-Object LogicalName

    $customFields | ForEach-Object {
        $displayName = if ($_.DisplayName.UserLocalizedLabel) { $_.DisplayName.UserLocalizedLabel.Label } else { "(no display name)" }
        $maxLength = if ($_.MaxLength) { " (max: $($_.MaxLength))" } else { "" }

        Write-Host "  $($_.LogicalName)" -ForegroundColor Yellow
        Write-Host "    Type: $($_.AttributeType)$maxLength"
        Write-Host "    Display: $displayName"
        Write-Host "    Schema: $($_.SchemaName)"
        Write-Host ""
    }

    Write-Host ""
    Write-Host "System Fields (sample):" -ForegroundColor Cyan
    Write-Host ""

    $systemFields = $attributes.value | Where-Object { $_.IsCustomAttribute -eq $false } | Sort-Object LogicalName | Select-Object -First 10

    $systemFields | ForEach-Object {
        Write-Host "  $($_.LogicalName) ($($_.AttributeType))"
    }

    Write-Host ""
    Write-Host "=========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Total fields: $($attributes.value.Count)" -ForegroundColor Yellow
    Write-Host "Custom fields: $($customFields.Count)" -ForegroundColor Yellow
    Write-Host ""

} catch {
    Write-Error "Failed to query metadata: $_"
}
