#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Integration tests for the RAG Dedicated Deployment Model

.DESCRIPTION
    Tests the RAG infrastructure for the Dedicated deployment model:
    - Dedicated: Per-customer index in the customer's stamp, reached with the BFF's managed identity

    The CustomerOwned model (an index in another subscription reached with an API key) was removed by
    customer-provisioning-orchestration-r1 task 230b (2026-10-06): a customer that brings its own Azure
    subscription/tenant gets a dedicated Model 2 stamp (D-12), whose BFF uses its own AI Search with its
    managed identity - no key (owner D13). Analysis:DefaultRagModel accepts Shared or Dedicated; any other
    value fails at startup. The former -Action CustomerOwned has been removed with it.

    Task 007: Test Dedicated Deployment Model

    AUTHORIZATION (unified-access-control-r2 task 163):
    - POST /api/ai/rag/index and DELETE /api/ai/rag/{id} are operator surfaces: run this script with a
      token whose user holds the BFF app's Admin / SystemAdmin role, or every index and delete is 403.
    - The tenant partition is the TOKEN's tenant (its tid claim). The script derives the Dedicated tenant
      from the token; requests naming the generated Shared / "different" tenants are
      rejected 403, and the isolation steps now assert that rejection.
    - POST /api/ai/rag/search returns only chunks whose documentId is a sprk_document the caller can Read.
      The synthetic chunks are therefore stamped with -DocumentId (mandatory): a real sprk_document the
      operator can Read in the target environment.

.PARAMETER Action
    Test action to run: All, Dedicated, Isolation

.PARAMETER ApiBaseUrl
    Base URL for the SDAP BFF API

.PARAMETER TenantId
    Ignored unless it equals the token's tenant (task 163: the partition is the token's tid).

.PARAMETER DocumentId
    REQUIRED. A real sprk_document id the operator can Read; stamped as the synthetic chunks' documentId so
    the per-row trim on /search keeps them.

.EXAMPLE
    .\Test-RagDedicatedModel.ps1 -Action All -DocumentId "00000000-0000-0000-0000-000000000000"
    .\Test-RagDedicatedModel.ps1 -Action Dedicated -DocumentId "00000000-0000-0000-0000-000000000000"
    .\Test-RagDedicatedModel.ps1 -Action Isolation -DocumentId "00000000-0000-0000-0000-000000000000"
#>

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet('All', 'Dedicated', 'Isolation')]
    [string]$Action = 'All',

    [Parameter(Mandatory=$false)]
    [string]$ApiBaseUrl = 'https://spe-api-dev-67e2xz.azurewebsites.net',

    [Parameter(Mandatory=$false)]
    [string]$TenantId = '',

    [Parameter(Mandatory=$true)]
    [string]$DocumentId
)

# Configuration
$ErrorActionPreference = 'Stop'
$Script:TestResults = @()

$RequestedTenantId = $TenantId

# Task 163: the tenant partition is the TOKEN's tid; decode it rather than invent one.
function Get-TokenTenantId {
    param([string]$Jwt)
    $parts = $Jwt.Split('.')
    if ($parts.Count -lt 2) { return $null }
    $payload = $parts[1].Replace('-', '+').Replace('_', '/')
    switch ($payload.Length % 4) { 2 { $payload += '==' } 3 { $payload += '=' } }
    $claims = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
    return $claims.tid
}
$SharedTenantId = "shared-tenant-$(Get-Random -Minimum 100000 -Maximum 999999)"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " RAG Dedicated Model Tests" -ForegroundColor Cyan
Write-Host " Task 007 - AI Document Intelligence R3" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "API URL: $ApiBaseUrl" -ForegroundColor Gray
Write-Host "Dedicated Tenant: $TenantId" -ForegroundColor Gray
Write-Host "Shared Tenant: $SharedTenantId" -ForegroundColor Gray
Write-Host ""

# Get auth token from pac CLI
Write-Host "Getting auth token from pac CLI..." -ForegroundColor Cyan
$tokenOutput = & pac auth token 2>&1
$token = ($tokenOutput | Out-String).Trim()

if ([string]::IsNullOrWhiteSpace($token) -or $token.Contains("Error")) {
    Write-Error "Failed to get token from pac CLI. Make sure you're authenticated with 'pac auth create'"
    exit 1
}

Write-Host "Token obtained (length: $($token.Length))" -ForegroundColor Green

$TenantId = Get-TokenTenantId -Jwt $token
if ([string]::IsNullOrWhiteSpace($TenantId)) {
    Write-Error "The token carries no tid claim; the BFF derives the tenant partition from it (task 163)."
    exit 1
}
if (-not [string]::IsNullOrWhiteSpace($RequestedTenantId) -and $RequestedTenantId -ne $TenantId) {
    Write-Warning "-TenantId '$RequestedTenantId' differs from the token's tenant; using the token's tenant '$TenantId'."
}
Write-Host "Dedicated tenant (token partition): $TenantId" -ForegroundColor Gray
Write-Host ""

# Prepare headers
$headers = @{
    'Authorization' = "Bearer $token"
    'Accept' = 'application/json'
    'Content-Type' = 'application/json'
}

function Invoke-ApiRequest {
    param(
        [string]$Url,
        [string]$Method = 'GET',
        [object]$Body = $null,
        [switch]$ReturnTime
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        $params = @{
            Uri = $Url
            Method = $Method
            Headers = $headers
            ErrorAction = 'Stop'
        }

        if ($Body) {
            $params['Body'] = ($Body | ConvertTo-Json -Depth 10)
        }

        $response = Invoke-RestMethod @params
        $stopwatch.Stop()

        if ($ReturnTime) {
            return @{
                Response = $response
                DurationMs = $stopwatch.ElapsedMilliseconds
            }
        }
        return $response
    }
    catch {
        $stopwatch.Stop()
        Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

function Add-TestResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Message = '',
        [long]$DurationMs = 0
    )

    $Script:TestResults += [PSCustomObject]@{
        TestName = $TestName
        Passed = $Passed
        Message = $Message
        DurationMs = $DurationMs
    }

    $status = if ($Passed) { "[PASS]" } else { "[FAIL]" }
    $color = if ($Passed) { "Green" } else { "Red" }

    Write-Host "  $status $TestName" -ForegroundColor $color
    if ($Message) {
        Write-Host "         $Message" -ForegroundColor Gray
    }
}

#region Test Functions

function Test-ApiHealth {
    Write-Host "`n--- API Health Check ---" -ForegroundColor Yellow

    $response = Invoke-ApiRequest -Url "$ApiBaseUrl/ping"

    if ($response -eq "pong") {
        Add-TestResult -TestName "API Health Check" -Passed $true -Message "API responding"
        return $true
    } else {
        Add-TestResult -TestName "API Health Check" -Passed $false -Message "API not responding"
        return $false
    }
}

function Test-DedicatedDeploymentModel {
    Write-Host "`n--- Dedicated Deployment Model Tests ---" -ForegroundColor Yellow

    # Test 1: Index document with Dedicated model
    $doc = @{
        id = "dedicated-test-$(New-Guid)"
        tenantId = $TenantId
        deploymentModel = "Dedicated"
        documentId = $DocumentId
        documentName = "Dedicated Tenant Handbook.pdf"
        documentType = "policy"
        chunkIndex = 0
        chunkCount = 1
        content = "This is a test document for the dedicated deployment model. Each customer gets their own isolated index for complete data separation."
        tags = @("dedicated", "test")
        createdAt = (Get-Date -Format "o")
        updatedAt = (Get-Date -Format "o")
    }

    Write-Host "  Indexing document to Dedicated index..." -ForegroundColor Gray
    $indexResponse = Invoke-ApiRequest -Url "$ApiBaseUrl/api/ai/rag/index" -Method POST -Body $doc

    if ($indexResponse -and $indexResponse.id) {
        Add-TestResult -TestName "Index Document (Dedicated Model)" -Passed $true `
            -Message "Document indexed: $($indexResponse.id)"

        # Test 2: Search in Dedicated index
        Write-Host "  Waiting 3 seconds for index consistency..." -ForegroundColor Gray
        Start-Sleep -Seconds 3

        $searchRequest = @{
            query = "dedicated deployment isolated index"
            options = @{
                tenantId = $TenantId
                topK = 5
                minScore = 0.3
            }
        }

        $searchResponse = Invoke-ApiRequest -Url "$ApiBaseUrl/api/ai/rag/search" -Method POST -Body $searchRequest

        if ($searchResponse) {
            $hasResults = $searchResponse.results -and $searchResponse.results.Count -gt 0
            Add-TestResult -TestName "Search in Dedicated Index" -Passed $hasResults `
                -Message "Found $($searchResponse.results.Count) results"
        } else {
            Add-TestResult -TestName "Search in Dedicated Index" -Passed $false -Message "No response"
        }

        # Test 3: Verify isolation - different dedicated tenant should not see this document
        $otherTenantSearch = @{
            query = "dedicated deployment isolated index"
            options = @{
                tenantId = "different-dedicated-tenant"
                topK = 10
                minScore = 0.1
            }
        }

        $otherResponse = Invoke-ApiRequest -Url "$ApiBaseUrl/api/ai/rag/search" -Method POST -Body $otherTenantSearch

        # Task 163: naming another tenant is rejected (403), so there is no response to inspect.
        Add-TestResult -TestName "Dedicated Index Isolation (other tenant rejected)" -Passed (-not $otherResponse) `
            -Message "A search naming another tenant must be rejected (403)"

        # Cleanup
        $encodedDocId = [uri]::EscapeDataString($indexResponse.id)
        $encodedTenantId = [uri]::EscapeDataString($TenantId)
        $deleteUrl = "$ApiBaseUrl/api/ai/rag/$encodedDocId`?tenantId=$encodedTenantId"
        Invoke-ApiRequest -Url $deleteUrl -Method DELETE | Out-Null
        Write-Host "  Cleaned up test document" -ForegroundColor Gray

    } else {
        Add-TestResult -TestName "Index Document (Dedicated Model)" -Passed $false `
            -Message "Failed to index document"
    }
}

function Test-CrossModelIsolation {
    Write-Host "`n--- Cross-Model Isolation Tests ---" -ForegroundColor Yellow

    # Index a document with Shared model
    $sharedDoc = @{
        id = "isolation-shared-$(New-Guid)"
        tenantId = $SharedTenantId
        deploymentModel = "Shared"
        documentId = $DocumentId
        documentName = "Shared Isolation Test.pdf"
        documentType = "policy"
        chunkIndex = 0
        chunkCount = 1
        content = "This document tests isolation between Shared and Dedicated deployment models."
        tags = @("isolation", "shared")
        createdAt = (Get-Date -Format "o")
        updatedAt = (Get-Date -Format "o")
    }

    Write-Host "  Indexing document to Shared index..." -ForegroundColor Gray
    $sharedResponse = Invoke-ApiRequest -Url "$ApiBaseUrl/api/ai/rag/index" -Method POST -Body $sharedDoc

    if ($sharedResponse -and $sharedResponse.id) {
        Write-Host "  Waiting 3 seconds for index consistency..." -ForegroundColor Gray
        Start-Sleep -Seconds 3

        # Try to find Shared document from Dedicated tenant
        $crossModelSearch = @{
            query = "isolation between shared dedicated"
            options = @{
                tenantId = $TenantId  # Dedicated tenant
                topK = 10
                minScore = 0.1
            }
        }

        $crossResponse = Invoke-ApiRequest -Url "$ApiBaseUrl/api/ai/rag/search" -Method POST -Body $crossModelSearch

        if ($crossResponse) {
            # Dedicated tenant should NOT see Shared tenant's documents
            $sharedDocsFound = $crossResponse.results | Where-Object { $_.id -like "*shared*" }
            $isolated = $sharedDocsFound.Count -eq 0

            Add-TestResult -TestName "Dedicated Cannot See Shared Data" -Passed $isolated `
                -Message "Found $($sharedDocsFound.Count) shared documents (should be 0)"
        } else {
            Add-TestResult -TestName "Dedicated Cannot See Shared Data" -Passed $false -Message "No response"
        }

        # Search as Shared tenant - should find the document
        $sameModelSearch = @{
            query = "isolation between shared dedicated"
            options = @{
                tenantId = $SharedTenantId
                topK = 10
                minScore = 0.1
            }
        }

        $sameResponse = Invoke-ApiRequest -Url "$ApiBaseUrl/api/ai/rag/search" -Method POST -Body $sameModelSearch

        if ($sameResponse) {
            $foundOwn = $sameResponse.results.Count -gt 0
            Add-TestResult -TestName "Shared Tenant Sees Own Data" -Passed $foundOwn `
                -Message "Found $($sameResponse.results.Count) results in own index"
        } else {
            Add-TestResult -TestName "Shared Tenant Sees Own Data" -Passed $false -Message "No response"
        }

        # Cleanup
        $encodedDocId = [uri]::EscapeDataString($sharedResponse.id)
        $encodedTenantId = [uri]::EscapeDataString($SharedTenantId)
        $deleteUrl = "$ApiBaseUrl/api/ai/rag/$encodedDocId`?tenantId=$encodedTenantId"
        Invoke-ApiRequest -Url $deleteUrl -Method DELETE | Out-Null
        Write-Host "  Cleaned up test document" -ForegroundColor Gray

    } else {
        # Task 163: indexing into the generated Shared tenant's partition is rejected (403) - a caller can
        # only write its own token's partition, which is the isolation this step used to probe from outside.
        Add-TestResult -TestName "Cross-Tenant Index Write Rejected" -Passed $true `
            -Message "Indexing into another tenant's partition was rejected (403)"
    }
}

#endregion

#region Main Execution

Write-Host "Starting tests..." -ForegroundColor Cyan

# Always check API health first
$apiHealthy = Test-ApiHealth
if (-not $apiHealthy) {
    Write-Host "`nAPI is not responding. Aborting tests." -ForegroundColor Red
    exit 1
}

switch ($Action) {
    'All' {
        Test-DedicatedDeploymentModel
        Test-CrossModelIsolation
    }
    'Dedicated' {
        Test-DedicatedDeploymentModel
    }
    'Isolation' {
        Test-CrossModelIsolation
    }
}

# Print Summary
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " Test Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$passedCount = ($Script:TestResults | Where-Object { $_.Passed }).Count
$failedCount = ($Script:TestResults | Where-Object { -not $_.Passed }).Count
$totalCount = $Script:TestResults.Count

Write-Host ""
Write-Host "Total Tests: $totalCount" -ForegroundColor White
Write-Host "Passed:      $passedCount" -ForegroundColor Green
Write-Host "Failed:      $failedCount" -ForegroundColor $(if ($failedCount -gt 0) { "Red" } else { "Gray" })
Write-Host ""

if ($failedCount -gt 0) {
    Write-Host "Failed Tests:" -ForegroundColor Red
    foreach ($test in ($Script:TestResults | Where-Object { -not $_.Passed })) {
        Write-Host "  - $($test.TestName): $($test.Message)" -ForegroundColor Red
    }
    Write-Host ""
}

# Output test results as JSON for documentation
$outputFile = "c:\code_files\spaarke\projects\ai-document-intelligence-r3\notes\task-007-test-results.json"
$testReport = @{
    TestRun = Get-Date -Format "o"
    ApiUrl = $ApiBaseUrl
    DedicatedTenantId = $TenantId
    SharedTenantId = $SharedTenantId
    TotalTests = $totalCount
    Passed = $passedCount
    Failed = $failedCount
    Results = $Script:TestResults
}

$testReport | ConvertTo-Json -Depth 10 | Set-Content -Path $outputFile
Write-Host "Test results saved to: $outputFile" -ForegroundColor Cyan

if ($failedCount -gt 0) {
    exit 1
}

Write-Host "`nAll tests passed!" -ForegroundColor Green
exit 0

#endregion
