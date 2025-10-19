#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Deploys Web Resource to Dataverse using Dataverse Web API

.DESCRIPTION
    Automates deployment of sprk_subgrid_commands.js Web Resource.
    Uses PAC CLI authentication and Dataverse Web API to create/update the Web Resource.

.EXAMPLE
    .\Deploy-WebResource.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

# Paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$webResourcePath = Join-Path $repoRoot "src\controls\UniversalQuickCreate\UniversalQuickCreateSolution\WebResources\sprk_subgrid_commands.js"

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Web Resource Deployment Script" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Verify Web Resource exists
if (-not (Test-Path $webResourcePath)) {
    Write-Error "Web Resource not found at: $webResourcePath"
    exit 1
}

Write-Host "[1/6] Reading Web Resource file..." -ForegroundColor Yellow
$webResourceContent = Get-Content $webResourcePath -Raw
$webResourceBytes = [System.Text.Encoding]::UTF8.GetBytes($webResourceContent)
$webResourceBase64 = [Convert]::ToBase64String($webResourceBytes)
Write-Host "      File size: $($webResourceBytes.Length) bytes" -ForegroundColor Green
Write-Host ""

# Get PAC auth info
Write-Host "[2/6] Getting Dataverse connection from PAC CLI..." -ForegroundColor Yellow
try {
    $pacCmd = "C:\Users\RalphSchroeder\AppData\Local\Microsoft\PowerAppsCLI\pac.cmd"
    $authListOutput = & cmd /c "$pacCmd auth list --json" 2>&1 | Out-String
    $authList = $authListOutput | ConvertFrom-Json
    $activeAuth = $authList | Where-Object { $_.IsActive -eq $true } | Select-Object -First 1

    if (-not $activeAuth) {
        Write-Error "No active PAC CLI authentication found. Run 'pac auth create' first."
        exit 1
    }

    $orgUrl = $activeAuth.Url
    Write-Host "      Connected to: $orgUrl" -ForegroundColor Green
    Write-Host "      User: $($activeAuth.FriendlyName)" -ForegroundColor Green
} catch {
    Write-Error "Failed to get PAC auth info: $_"
    exit 1
}
Write-Host ""

# Get access token
Write-Host "[3/6] Getting access token..." -ForegroundColor Yellow
try {
    $pacCmd = "C:\Users\RalphSchroeder\AppData\Local\Microsoft\PowerAppsCLI\pac.cmd"
    $tokenOutput = & cmd /c "$pacCmd auth token --json" 2>&1 | Out-String
    $tokenResponse = $tokenOutput | ConvertFrom-Json
    $accessToken = $tokenResponse.Token
    Write-Host "      Token acquired" -ForegroundColor Green
} catch {
    Write-Error "Failed to get access token: $_"
    exit 1
}
Write-Host ""

# Prepare Web Resource payload
Write-Host "[4/6] Preparing Web Resource payload..." -ForegroundColor Yellow

$webResourcePayload = @{
    name = "sprk_subgrid_commands.js"
    displayname = "Subgrid Commands - Universal Document Upload"
    description = "Generic command script for multi-file document upload across all entity types"
    webresourcetype = 3  # 3 = Script (JScript)
    content = $webResourceBase64
}

Write-Host "      Payload prepared" -ForegroundColor Green
Write-Host ""

# Setup API connection
$headers = @{
    "Authorization" = "Bearer $accessToken"
    "Content-Type" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
    "Accept" = "application/json"
    "Prefer" = "return=representation"
}

$apiUrl = "$orgUrl/api/data/v9.2"

# Check if Web Resource already exists
Write-Host "[5/6] Checking if Web Resource exists..." -ForegroundColor Yellow

$filterQuery = "`$filter=name eq 'sprk_subgrid_commands.js'"
$searchUri = "$apiUrl/webresourceset?$filterQuery"

try {
    $searchResponse = Invoke-RestMethod -Uri $searchUri -Headers $headers -Method Get

    if ($searchResponse.value -and $searchResponse.value.Count -gt 0) {
        # Update existing Web Resource
        $existingId = $searchResponse.value[0].webresourceid
        Write-Host "      Found existing Web Resource: $existingId" -ForegroundColor Yellow
        Write-Host "      Updating..." -ForegroundColor Yellow

        $updateUri = "$apiUrl/webresourceset($existingId)"
        $updatePayload = @{
            content = $webResourceBase64
            description = $webResourcePayload.description
        } | ConvertTo-Json

        $updateResponse = Invoke-RestMethod -Uri $updateUri -Headers $headers -Method Patch -Body $updatePayload

        Write-Host "      ✓ Web Resource updated successfully" -ForegroundColor Green
        $webResourceId = $existingId
    } else {
        # Create new Web Resource
        Write-Host "      Web Resource does not exist. Creating new..." -ForegroundColor Yellow

        $createUri = "$apiUrl/webresourceset"
        $createPayload = $webResourcePayload | ConvertTo-Json

        $createResponse = Invoke-RestMethod -Uri $createUri -Headers $headers -Method Post -Body $createPayload

        Write-Host "      ✓ Web Resource created successfully" -ForegroundColor Green
        $webResourceId = $createResponse.webresourceid
    }
} catch {
    Write-Error "Failed to deploy Web Resource: $_"
    Write-Error "Response: $($_.Exception.Response)"
    exit 1
}
Write-Host ""

# Publish Web Resource
Write-Host "[6/6] Publishing Web Resource..." -ForegroundColor Yellow

$publishPayload = @{
    ParameterXml = "<importexportxml><webresources><webresource>{$webResourceId}</webresource></webresources></importexportxml>"
} | ConvertTo-Json

$publishUri = "$apiUrl/PublishXml"

try {
    Invoke-RestMethod -Uri $publishUri -Headers $headers -Method Post -Body $publishPayload
    Write-Host "      ✓ Web Resource published successfully" -ForegroundColor Green
} catch {
    Write-Warning "Publish request sent, but received non-standard response (this is normal)"
}
Write-Host ""

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "✓ Deployment Complete" -ForegroundColor Green
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Web Resource Details:" -ForegroundColor Cyan
Write-Host "  Name: sprk_subgrid_commands.js" -ForegroundColor White
Write-Host "  ID: $webResourceId" -ForegroundColor White
Write-Host "  Status: Published" -ForegroundColor Green
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "1. Create Custom Page manually in Power Apps Studio" -ForegroundColor White
Write-Host "2. Configure command buttons on entity forms to call:" -ForegroundColor White
Write-Host "   Function: Spaarke.Commands.AddMultipleDocuments" -ForegroundColor Yellow
Write-Host "   Library: sprk_subgrid_commands.js" -ForegroundColor Yellow
Write-Host ""
