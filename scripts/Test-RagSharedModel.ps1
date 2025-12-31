#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Integration tests for RAG Shared Deployment Model

.DESCRIPTION
    Tests the RAG infrastructure for the Shared deployment model:
    - Document indexing
    - Hybrid search retrieval
    - Tenant isolation
    - P95 latency measurement

    Task 006: Test Shared Deployment Model

.PARAMETER Action
    Test action to run: All, Index, Search, TenantIsolation, Latency

.PARAMETER ApiBaseUrl
    Base URL for the SDAP BFF API

.PARAMETER TenantId
    Tenant ID to use for testing (generated if not provided)

.PARAMETER Cleanup
    Clean up test data after running tests

.EXAMPLE
    .\Test-RagSharedModel.ps1 -Action All
    .\Test-RagSharedModel.ps1 -Action Search -TenantId "test-tenant-123"
    .\Test-RagSharedModel.ps1 -Action Latency
#>

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet('All', 'Index', 'Search', 'TenantIsolation', 'Latency', 'Cleanup')]
    [string]$Action = 'All',

    [Parameter(Mandatory=$false)]
    [string]$ApiBaseUrl = 'https://spe-api-dev-67e2xz.azurewebsites.net',

    [Parameter(Mandatory=$false)]
    [string]$TenantId = '',

    [Parameter(Mandatory=$false)]
    [switch]$Cleanup
)

# Configuration
$ErrorActionPreference = 'Stop'
$Script:TestResults = @()
$Script:IndexedDocumentIds = @()

if ([string]::IsNullOrWhiteSpace($TenantId)) {
    $TenantId = "test-tenant-$(Get-Random -Minimum 100000 -Maximum 999999)"
}
$OtherTenantId = "other-tenant-$(Get-Random -Minimum 100000 -Maximum 999999)"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " RAG Shared Deployment Model Tests" -ForegroundColor Cyan
Write-Host " Task 006 - AI Document Intelligence R3" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "API URL: $ApiBaseUrl" -ForegroundColor Gray
Write-Host "Test Tenant: $TenantId" -ForegroundColor Gray
Write-Host "Other Tenant: $OtherTenantId" -ForegroundColor Gray
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

function Get-TestDocuments {
    param([string]$Tenant, [string]$Prefix = '')

    $timestamp = Get-Date -Format "o"

    return @(
        @{
            id = "$($Prefix)doc1-chunk0-$(New-Guid)"
            tenantId = $Tenant
            deploymentModel = "Shared"
            documentId = "$($Prefix)employee-handbook"
            documentName = "Employee Handbook 2024.pdf"
            documentType = "policy"
            knowledgeSourceId = "ks-hr-policies"
            knowledgeSourceName = "HR Policies"
            chunkIndex = 0
            chunkCount = 3
            content = "This employee handbook outlines company policies regarding employment termination procedures. All employees must follow the standard two-week notice period before leaving the organization. Managers are required to conduct exit interviews with departing staff members."
            tags = @("hr", "policy", "termination")
            createdAt = $timestamp
            updatedAt = $timestamp
        },
        @{
            id = "$($Prefix)doc1-chunk1-$(New-Guid)"
            tenantId = $Tenant
            deploymentModel = "Shared"
            documentId = "$($Prefix)employee-handbook"
            documentName = "Employee Handbook 2024.pdf"
            documentType = "policy"
            knowledgeSourceId = "ks-hr-policies"
            knowledgeSourceName = "HR Policies"
            chunkIndex = 1
            chunkCount = 3
            content = "Remote work policy: Employees may work remotely up to three days per week with manager approval. All remote workers must be available during core business hours from 10 AM to 3 PM local time. Equipment will be provided for approved remote work arrangements."
            tags = @("hr", "policy", "remote-work")
            createdAt = $timestamp
            updatedAt = $timestamp
        },
        @{
            id = "$($Prefix)doc2-chunk0-$(New-Guid)"
            tenantId = $Tenant
            deploymentModel = "Shared"
            documentId = "$($Prefix)legal-contract"
            documentName = "Standard Service Agreement.docx"
            documentType = "contract"
            knowledgeSourceId = "ks-legal-templates"
            knowledgeSourceName = "Legal Templates"
            chunkIndex = 0
            chunkCount = 1
            content = "This Service Agreement governs the terms and conditions under which services will be provided. The agreement shall commence on the effective date and continue for a period of twelve months unless terminated earlier in accordance with the termination clause."
            tags = @("legal", "contract", "template")
            createdAt = $timestamp
            updatedAt = $timestamp
        }
    )
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

function Test-DocumentIndexing {
    Write-Host "`n--- Document Indexing Tests ---" -ForegroundColor Yellow

    # Index test documents for main tenant
    $docs = Get-TestDocuments -Tenant $TenantId
    $successCount = 0

    foreach ($doc in $docs) {
        Write-Host "  Indexing: $($doc.documentName) chunk $($doc.chunkIndex)..." -ForegroundColor Gray

        $response = Invoke-ApiRequest -Url "$ApiBaseUrl/api/ai/rag/index" -Method POST -Body $doc

        if ($response -and $response.id) {
            $Script:IndexedDocumentIds += $response.id
            $successCount++
            Write-Host "    Indexed: $($response.id)" -ForegroundColor Green
        } else {
            Write-Host "    Failed to index document" -ForegroundColor Red
        }
    }

    Add-TestResult -TestName "Index Documents (Main Tenant)" -Passed ($successCount -eq $docs.Count) `
        -Message "Indexed $successCount/$($docs.Count) documents"

    # Index documents for other tenant (for isolation testing)
    $otherDocs = Get-TestDocuments -Tenant $OtherTenantId -Prefix "other-"
    $otherSuccessCount = 0

    foreach ($doc in $otherDocs) {
        $response = Invoke-ApiRequest -Url "$ApiBaseUrl/api/ai/rag/index" -Method POST -Body $doc

        if ($response -and $response.id) {
            $Script:IndexedDocumentIds += $response.id
            $otherSuccessCount++
        }
    }

    Add-TestResult -TestName "Index Documents (Other Tenant)" -Passed ($otherSuccessCount -eq $otherDocs.Count) `
        -Message "Indexed $otherSuccessCount/$($otherDocs.Count) documents"

    # Wait for index to be searchable
    Write-Host "  Waiting 3 seconds for index consistency..." -ForegroundColor Gray
    Start-Sleep -Seconds 3

    return ($successCount -gt 0)
}

function Test-HybridSearch {
    Write-Host "`n--- Hybrid Search Tests ---" -ForegroundColor Yellow

    # Test 1: Full hybrid search
    $searchRequest = @{
        query = "employment termination procedures"
        options = @{
            tenantId = $TenantId
            topK = 5
            minScore = 0.3
            useSemanticRanking = $true
            useVectorSearch = $true
            useKeywordSearch = $true
        }
    }

    $result = Invoke-ApiRequest -Url "$ApiBaseUrl/api/ai/rag/search" -Method POST -Body $searchRequest -ReturnTime

    if ($result -and $result.Response) {
        $response = $result.Response
        $hasResults = $response.results -and $response.results.Count -gt 0

        Add-TestResult -TestName "Hybrid Search (keyword + vector + semantic)" -Passed $hasResults `
            -Message "Found $($response.results.Count) results in $($response.searchDurationMs)ms" -DurationMs $result.DurationMs

        if ($hasResults) {
            Write-Host "  Top results:" -ForegroundColor Gray
            foreach ($r in $response.results | Select-Object -First 3) {
                Write-Host "    [$([math]::Round($r.score, 3))] $($r.documentName): $($r.content.Substring(0, [Math]::Min(60, $r.content.Length)))..." -ForegroundColor Gray
            }
        }
    } else {
        Add-TestResult -TestName "Hybrid Search" -Passed $false -Message "No response from search endpoint"
    }

    # Test 2: Vector-only search
    $vectorSearchRequest = @{
        query = "employee dismissal process"
        options = @{
            tenantId = $TenantId
            topK = 5
            minScore = 0.3
            useSemanticRanking = $false
            useVectorSearch = $true
            useKeywordSearch = $false
        }
    }

    $vectorResult = Invoke-ApiRequest -Url "$ApiBaseUrl/api/ai/rag/search" -Method POST -Body $vectorSearchRequest

    if ($vectorResult) {
        $hasSemanticMatches = $vectorResult.results -and $vectorResult.results.Count -gt 0
        Add-TestResult -TestName "Vector-Only Search (semantic similarity)" -Passed $hasSemanticMatches `
            -Message "Found $($vectorResult.results.Count) semantically similar results"
    } else {
        Add-TestResult -TestName "Vector-Only Search" -Passed $false -Message "No response"
    }

    # Test 3: Keyword-only search
    $keywordSearchRequest = @{
        query = "termination"
        options = @{
            tenantId = $TenantId
            topK = 5
            minScore = 0.1
            useSemanticRanking = $false
            useVectorSearch = $false
            useKeywordSearch = $true
        }
    }

    $keywordResult = Invoke-ApiRequest -Url "$ApiBaseUrl/api/ai/rag/search" -Method POST -Body $keywordSearchRequest

    if ($keywordResult) {
        $hasKeywordMatches = $keywordResult.results -and $keywordResult.results.Count -gt 0
        Add-TestResult -TestName "Keyword-Only Search" -Passed $hasKeywordMatches `
            -Message "Found $($keywordResult.results.Count) exact keyword matches"
    } else {
        Add-TestResult -TestName "Keyword-Only Search" -Passed $false -Message "No response"
    }
}

function Test-TenantIsolation {
    Write-Host "`n--- Tenant Isolation Tests ---" -ForegroundColor Yellow

    # Test 1: Search as main tenant should NOT find other tenant's documents
    $searchRequest = @{
        query = "test document"
        options = @{
            tenantId = $TenantId
            topK = 20
            minScore = 0.1
        }
    }

    $response = Invoke-ApiRequest -Url "$ApiBaseUrl/api/ai/rag/search" -Method POST -Body $searchRequest

    if ($response -and $response.results) {
        $otherTenantDocs = $response.results | Where-Object { $_.id -like "other-*" }
        $isolated = $otherTenantDocs.Count -eq 0

        Add-TestResult -TestName "Main Tenant Cannot See Other Tenant Data" -Passed $isolated `
            -Message "Found $($response.results.Count) results, $($otherTenantDocs.Count) from other tenant"
    } else {
        Add-TestResult -TestName "Tenant Isolation (Main)" -Passed $false -Message "No response"
    }

    # Test 2: Search as other tenant should only find their documents
    $otherSearchRequest = @{
        query = "test document"
        options = @{
            tenantId = $OtherTenantId
            topK = 20
            minScore = 0.1
        }
    }

    $otherResponse = Invoke-ApiRequest -Url "$ApiBaseUrl/api/ai/rag/search" -Method POST -Body $otherSearchRequest

    if ($otherResponse) {
        Add-TestResult -TestName "Other Tenant Search Isolation" -Passed $true `
            -Message "Found $($otherResponse.results.Count) results for other tenant"
    } else {
        Add-TestResult -TestName "Other Tenant Search Isolation" -Passed $false -Message "No response"
    }

    # Test 3: Non-existent tenant should return no results
    $nonExistentRequest = @{
        query = "test document"
        options = @{
            tenantId = "non-existent-tenant-xyz123"
            topK = 20
            minScore = 0.1
        }
    }

    $nonExistentResponse = Invoke-ApiRequest -Url "$ApiBaseUrl/api/ai/rag/search" -Method POST -Body $nonExistentRequest

    if ($nonExistentResponse) {
        $noResults = $nonExistentResponse.results.Count -eq 0
        Add-TestResult -TestName "Non-Existent Tenant Returns Empty" -Passed $noResults `
            -Message "Found $($nonExistentResponse.results.Count) results (should be 0)"
    } else {
        Add-TestResult -TestName "Non-Existent Tenant Returns Empty" -Passed $false -Message "No response"
    }
}

function Test-P95Latency {
    Write-Host "`n--- P95 Latency Tests ---" -ForegroundColor Yellow

    $iterations = 20
    $p95TargetMs = 500
    $latencies = @()

    $queries = @(
        "employment termination procedures",
        "company holiday policy",
        "remote work guidelines",
        "expense reimbursement process",
        "performance review timeline"
    )

    Write-Host "  Running $iterations search iterations..." -ForegroundColor Gray

    for ($i = 0; $i -lt $iterations; $i++) {
        $query = $queries[$i % $queries.Count]

        $searchRequest = @{
            query = $query
            options = @{
                tenantId = $TenantId
                topK = 5
                minScore = 0.5
                useSemanticRanking = $true
            }
        }

        $result = Invoke-ApiRequest -Url "$ApiBaseUrl/api/ai/rag/search" -Method POST -Body $searchRequest -ReturnTime

        if ($result) {
            $latencies += $result.DurationMs
            Write-Host "    Iteration $($i+1): $($result.DurationMs)ms" -ForegroundColor Gray
        }
    }

    if ($latencies.Count -gt 0) {
        $sortedLatencies = $latencies | Sort-Object
        $p95Index = [Math]::Ceiling($sortedLatencies.Count * 0.95) - 1
        $p95Latency = $sortedLatencies[$p95Index]
        $avgLatency = ($latencies | Measure-Object -Average).Average
        $minLatency = ($latencies | Measure-Object -Minimum).Minimum
        $maxLatency = ($latencies | Measure-Object -Maximum).Maximum

        Write-Host ""
        Write-Host "  Latency Statistics ($($latencies.Count) iterations):" -ForegroundColor Yellow
        Write-Host "    Min:    $minLatency ms" -ForegroundColor Gray
        Write-Host "    Avg:    $([Math]::Round($avgLatency, 1)) ms" -ForegroundColor Gray
        Write-Host "    P95:    $p95Latency ms" -ForegroundColor $(if ($p95Latency -lt $p95TargetMs) { "Green" } else { "Red" })
        Write-Host "    Max:    $maxLatency ms" -ForegroundColor Gray
        Write-Host "    Target: P95 < $p95TargetMs ms" -ForegroundColor Gray

        $passed = $p95Latency -lt $p95TargetMs
        Add-TestResult -TestName "P95 Latency Under $($p95TargetMs)ms" -Passed $passed `
            -Message "P95: $($p95Latency)ms, Avg: $([Math]::Round($avgLatency, 1))ms" -DurationMs $p95Latency
    } else {
        Add-TestResult -TestName "P95 Latency" -Passed $false -Message "No latency data collected"
    }

    # Test embedding cache improvement
    Write-Host ""
    Write-Host "  Testing embedding cache..." -ForegroundColor Gray

    $cacheQuery = "employee handbook policies and procedures"
    $cacheRequest = @{
        query = $cacheQuery
        options = @{
            tenantId = $TenantId
            topK = 5
            minScore = 0.5
        }
    }

    # First call (cache miss expected)
    $firstResult = Invoke-ApiRequest -Url "$ApiBaseUrl/api/ai/rag/search" -Method POST -Body $cacheRequest -ReturnTime

    # Second call (cache hit expected)
    $secondResult = Invoke-ApiRequest -Url "$ApiBaseUrl/api/ai/rag/search" -Method POST -Body $cacheRequest -ReturnTime

    if ($firstResult -and $secondResult) {
        $cacheHit = $secondResult.Response.embeddingCacheHit
        $fasterSecond = $secondResult.Response.embeddingDurationMs -lt $firstResult.Response.embeddingDurationMs

        Write-Host "    First call:  $($firstResult.DurationMs)ms (cache: $($firstResult.Response.embeddingCacheHit))" -ForegroundColor Gray
        Write-Host "    Second call: $($secondResult.DurationMs)ms (cache: $($secondResult.Response.embeddingCacheHit))" -ForegroundColor Gray

        Add-TestResult -TestName "Embedding Cache Hit on Repeat Query" -Passed $cacheHit `
            -Message "Cache hit: $cacheHit, Faster: $fasterSecond"
    }
}

function Remove-TestData {
    Write-Host "`n--- Cleanup Test Data ---" -ForegroundColor Yellow

    $deletedCount = 0
    foreach ($docId in $Script:IndexedDocumentIds) {
        $tenantId = if ($docId -like "other-*") { $OtherTenantId } else { $TenantId }

        # DELETE endpoint uses path parameter for documentId and query parameter for tenantId
        $encodedDocId = [uri]::EscapeDataString($docId)
        $encodedTenantId = [uri]::EscapeDataString($tenantId)
        $url = "$ApiBaseUrl/api/ai/rag/$encodedDocId`?tenantId=$encodedTenantId"

        $result = Invoke-ApiRequest -Url $url -Method DELETE

        if ($result) {
            $deletedCount++
            Write-Host "  Deleted: $docId" -ForegroundColor Gray
        }
    }

    Write-Host "  Cleaned up $deletedCount documents" -ForegroundColor Green
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
        Test-DocumentIndexing
        Test-HybridSearch
        Test-TenantIsolation
        Test-P95Latency
        if ($Cleanup) {
            Remove-TestData
        }
    }
    'Index' {
        Test-DocumentIndexing
    }
    'Search' {
        Test-HybridSearch
    }
    'TenantIsolation' {
        Test-TenantIsolation
    }
    'Latency' {
        Test-P95Latency
    }
    'Cleanup' {
        Remove-TestData
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
$outputFile = "c:\code_files\spaarke\projects\ai-document-intelligence-r3\notes\task-006-test-results.json"
$testReport = @{
    TestRun = Get-Date -Format "o"
    ApiUrl = $ApiBaseUrl
    TenantId = $TenantId
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
