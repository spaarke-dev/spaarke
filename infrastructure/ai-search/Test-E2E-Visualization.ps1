<#
.SYNOPSIS
    E2E tests for the Visualization API with 3072-dim vectors.

.DESCRIPTION
    Tests the VisualizationService against the Azure AI Search index:
    - Query with 3072-dim document vectors
    - Verify file type handling
    - Verify orphan file detection
    - Test similarity scoring
    - Performance measurement

.PARAMETER SearchServiceName
    Name of the Azure AI Search service.

.PARAMETER IndexName
    Name of the index. Defaults to 'spaarke-knowledge-index-v2'.

.EXAMPLE
    .\Test-E2E-Visualization.ps1 -SearchServiceName "spaarke-search-dev"
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$SearchServiceName = "spaarke-search-dev",

    [Parameter(Mandatory = $false)]
    [string]$IndexName = "spaarke-knowledge-index-v2",

    [Parameter(Mandatory = $false)]
    [string]$TenantId = "test-tenant-e2e",

    [Parameter(Mandatory = $false)]
    [string]$ResourceGroup = "spe-infrastructure-westus2"
)

$ErrorActionPreference = "Stop"
$testResults = @()
$passCount = 0
$failCount = 0

function Write-TestResult($testName, $passed, $message = "", $duration = $null) {
    $result = @{
        Test = $testName
        Passed = $passed
        Message = $message
        Duration = $duration
    }
    $script:testResults += $result

    if ($passed) {
        $script:passCount++
        $durationStr = if ($duration) { " (${duration}ms)" } else { "" }
        Write-Host "  [PASS] $testName$durationStr" -ForegroundColor Green
        if ($message) { Write-Host "         $message" -ForegroundColor Gray }
    } else {
        $script:failCount++
        Write-Host "  [FAIL] $testName" -ForegroundColor Red
        Write-Host "         $message" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " E2E Visualization Tests" -ForegroundColor Cyan
Write-Host " Index: $IndexName" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Get admin key
Write-Host "Setup: Retrieving credentials..."
$adminKey = az search admin-key show `
    --resource-group $ResourceGroup `
    --service-name $SearchServiceName `
    --query "primaryKey" -o tsv

if (-not $adminKey) {
    Write-Error "Failed to retrieve admin key"
    exit 1
}

$searchEndpoint = "https://$SearchServiceName.search.windows.net"
$headers = @{
    "Content-Type" = "application/json"
    "api-key" = $adminKey
}

Write-Host "Setup: Complete"
Write-Host ""

# ============================================================
# TEST 1: Verify Index Has Documents
# ============================================================
Write-Host "--- Test Suite 1: Index Verification ---" -ForegroundColor Yellow

$countUrl = "$searchEndpoint/indexes/$IndexName/docs/`$count?api-version=2024-07-01"
$count = Invoke-RestMethod -Uri $countUrl -Headers $headers -Method GET

if ($count -gt 0) {
    Write-TestResult "Index contains documents" $true "$count documents found"
} else {
    Write-TestResult "Index contains documents" $false "No documents in index"
}

# ============================================================
# TEST 2: File Type Verification
# ============================================================
Write-Host ""
Write-Host "--- Test Suite 2: File Type Display ---" -ForegroundColor Yellow

$fileTypes = @(
    @{ type = "pdf"; expectedIcon = "DocumentPdf" },
    @{ type = "docx"; expectedIcon = "DocumentText" },
    @{ type = "xlsx"; expectedIcon = "Table" },
    @{ type = "msg"; expectedIcon = "Mail" },
    @{ type = "zip"; expectedIcon = "FolderZip" }
)

foreach ($ft in $fileTypes) {
    $searchBody = @{
        search = "*"
        filter = "fileType eq '$($ft.type)' and tenantId eq '$TenantId'"
        select = "id,fileName,fileType,documentId"
        top = 1
    } | ConvertTo-Json

    $searchUrl = "$searchEndpoint/indexes/$IndexName/docs/search?api-version=2024-07-01"
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $result = Invoke-RestMethod -Uri $searchUrl -Method POST -Headers $headers -Body $searchBody
    $sw.Stop()

    if ($result.value.Count -gt 0) {
        Write-TestResult "File type '$($ft.type)' found" $true $result.value[0].fileName $sw.ElapsedMilliseconds
    } else {
        Write-TestResult "File type '$($ft.type)' found" $false "No documents with fileType=$($ft.type)"
    }
}

# ============================================================
# TEST 3: Orphan File Detection
# ============================================================
Write-Host ""
Write-Host "--- Test Suite 3: Orphan File Detection ---" -ForegroundColor Yellow

# Search for documents without documentId (orphan files)
$orphanSearchBody = @{
    search = "*"
    filter = "tenantId eq '$TenantId'"
    select = "id,fileName,fileType,documentId,speFileId"
    top = 100
} | ConvertTo-Json

$result = Invoke-RestMethod -Uri $searchUrl -Method POST -Headers $headers -Body $orphanSearchBody

$orphanFiles = @($result.value | Where-Object { $null -eq $_.documentId -or $_.documentId -eq "" })
$regularFiles = @($result.value | Where-Object { $null -ne $_.documentId -and $_.documentId -ne "" })

if ($orphanFiles.Count -gt 0) {
    Write-TestResult "Orphan files detected" $true "$($orphanFiles.Count) orphan files found"
    foreach ($orphan in $orphanFiles) {
        Write-Host "           - $($orphan.fileName) (speFileId: $($orphan.speFileId))" -ForegroundColor Gray
    }
} else {
    Write-TestResult "Orphan files detected" $false "No orphan files in test data"
}

if ($regularFiles.Count -gt 0) {
    Write-TestResult "Regular files detected" $true "$($regularFiles.Count) regular files found"
} else {
    Write-TestResult "Regular files detected" $false "No regular files in test data"
}

# Verify orphan files have speFileId but no documentId
foreach ($orphan in $orphanFiles) {
    $hasSpefile = $null -ne $orphan.speFileId -and $orphan.speFileId -ne ""
    $noDocId = $null -eq $orphan.documentId -or $orphan.documentId -eq ""

    if ($hasSpefile -and $noDocId) {
        Write-TestResult "Orphan '$($orphan.fileName)' has speFileId" $true "speFileId: $($orphan.speFileId)"
    } else {
        Write-TestResult "Orphan '$($orphan.fileName)' structure" $false "Invalid orphan structure"
    }
}

# ============================================================
# TEST 4: Vector Search (3072-dim)
# ============================================================
Write-Host ""
Write-Host "--- Test Suite 4: Vector Search (3072-dim) ---" -ForegroundColor Yellow

# Get a source document's vector
$sourceSearchBody = @{
    search = "*"
    filter = "tenantId eq '$TenantId' and documentId ne null"
    select = "id,fileName,documentId,documentVector3072"
    top = 1
} | ConvertTo-Json

$sourceResult = Invoke-RestMethod -Uri $searchUrl -Method POST -Headers $headers -Body $sourceSearchBody

if ($sourceResult.value.Count -gt 0) {
    $sourceDoc = $sourceResult.value[0]
    $sourceVector = $sourceDoc.documentVector3072

    Write-TestResult "Source document retrieved" $true "Using: $($sourceDoc.fileName)"

    if ($sourceVector -and $sourceVector.Count -eq 3072) {
        Write-TestResult "3072-dim vector present" $true "Vector has $($sourceVector.Count) dimensions"

        # Perform vector similarity search
        $vectorSearchBody = @{
            search = "*"
            filter = "tenantId eq '$TenantId'"
            select = "id,fileName,fileType,documentId"
            top = 5
            vectorQueries = @(
                @{
                    kind = "vector"
                    vector = $sourceVector
                    fields = "documentVector3072"
                    k = 5
                }
            )
        } | ConvertTo-Json -Depth 10

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $vectorResult = Invoke-RestMethod -Uri $searchUrl -Method POST -Headers $headers -Body $vectorSearchBody
        $sw.Stop()

        if ($vectorResult.value.Count -gt 0) {
            Write-TestResult "Vector search returns results" $true "$($vectorResult.value.Count) similar documents found" $sw.ElapsedMilliseconds

            # Check that source document is most similar to itself
            $firstResult = $vectorResult.value[0]
            if ($firstResult.id -eq $sourceDoc.id) {
                Write-TestResult "Self-similarity is highest" $true "Source doc ranked first"
            } else {
                Write-TestResult "Self-similarity is highest" $false "Source doc not ranked first"
            }
        } else {
            Write-TestResult "Vector search returns results" $false "No results from vector search"
        }
    } else {
        Write-TestResult "3072-dim vector present" $false "Vector missing or wrong dimensions: $($sourceVector.Count)"
    }
} else {
    Write-TestResult "Source document retrieved" $false "No documents found for vector search test"
}

# ============================================================
# TEST 5: Performance Benchmarks
# ============================================================
Write-Host ""
Write-Host "--- Test Suite 5: Performance ---" -ForegroundColor Yellow

$iterations = 5
$searchTimes = @()

for ($i = 0; $i -lt $iterations; $i++) {
    $perfSearchBody = @{
        search = "*"
        filter = "tenantId eq '$TenantId'"
        select = "id,fileName,fileType"
        top = 10
    } | ConvertTo-Json

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $null = Invoke-RestMethod -Uri $searchUrl -Method POST -Headers $headers -Body $perfSearchBody
    $sw.Stop()
    $searchTimes += $sw.ElapsedMilliseconds
}

$avgTime = [math]::Round(($searchTimes | Measure-Object -Average).Average, 0)
$maxTime = ($searchTimes | Measure-Object -Maximum).Maximum

if ($avgTime -lt 500) {
    Write-TestResult "Search latency < 500ms" $true "Average: ${avgTime}ms, Max: ${maxTime}ms"
} else {
    Write-TestResult "Search latency < 500ms" $false "Average: ${avgTime}ms (exceeds threshold)"
}

# Vector search performance
if ($sourceVector) {
    $vectorTimes = @()
    for ($i = 0; $i -lt 3; $i++) {
        $vectorSearchBody = @{
            search = "*"
            filter = "tenantId eq '$TenantId'"
            select = "id,fileName"
            top = 10
            vectorQueries = @(
                @{
                    kind = "vector"
                    vector = $sourceVector
                    fields = "documentVector3072"
                    k = 10
                }
            )
        } | ConvertTo-Json -Depth 10

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $null = Invoke-RestMethod -Uri $searchUrl -Method POST -Headers $headers -Body $vectorSearchBody
        $sw.Stop()
        $vectorTimes += $sw.ElapsedMilliseconds
    }

    $avgVectorTime = [math]::Round(($vectorTimes | Measure-Object -Average).Average, 0)

    if ($avgVectorTime -lt 1000) {
        Write-TestResult "Vector search latency < 1000ms" $true "Average: ${avgVectorTime}ms"
    } else {
        Write-TestResult "Vector search latency < 1000ms" $false "Average: ${avgVectorTime}ms (exceeds threshold)"
    }
}

# ============================================================
# TEST 6: Edge Cases
# ============================================================
Write-Host ""
Write-Host "--- Test Suite 6: Edge Cases ---" -ForegroundColor Yellow

# Empty result set
$emptySearchBody = @{
    search = "*"
    filter = "tenantId eq 'non-existent-tenant-xyz'"
    select = "id,fileName"
    top = 10
} | ConvertTo-Json

$emptyResult = Invoke-RestMethod -Uri $searchUrl -Method POST -Headers $headers -Body $emptySearchBody
if ($emptyResult.value.Count -eq 0) {
    Write-TestResult "Empty result handling" $true "Non-existent tenant returns 0 results"
} else {
    Write-TestResult "Empty result handling" $false "Unexpected results for non-existent tenant"
}

# Special characters in search
$specialSearchBody = @{
    search = "contract AND 'ABC'"
    filter = "tenantId eq '$TenantId'"
    select = "id,fileName"
    top = 10
} | ConvertTo-Json

try {
    $specialResult = Invoke-RestMethod -Uri $searchUrl -Method POST -Headers $headers -Body $specialSearchBody
    Write-TestResult "Special characters in query" $true "Query executed without error"
} catch {
    Write-TestResult "Special characters in query" $false "Query failed: $_"
}

# ============================================================
# SUMMARY
# ============================================================
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Test Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Passed: $passCount" -ForegroundColor Green
Write-Host "  Failed: $failCount" -ForegroundColor $(if ($failCount -gt 0) { "Red" } else { "Green" })
Write-Host "  Total:  $($passCount + $failCount)"
Write-Host ""

if ($failCount -eq 0) {
    Write-Host "All E2E tests passed!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "Some tests failed. Review output above." -ForegroundColor Yellow
    exit 1
}
