# Test-SpeFullFlow.ps1
# Comprehensive test: Create container → Upload file → List → Download → Verify

param(
    [string]$BaseUrl = "http://localhost:5073",
    [string]$ContainerTypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06"
)

Write-Host "`n=== SPE Full Flow Test ===" -ForegroundColor Yellow
Write-Host "Base URL: $BaseUrl" -ForegroundColor Gray
Write-Host "Container Type: $ContainerTypeId" -ForegroundColor Gray
Write-Host ""

$testPassed = $true

# Test 1: Create Container
Write-Host "[1/6] Creating new container..." -ForegroundColor Cyan
try {
    $createBody = @{
        containerTypeId = $ContainerTypeId
        displayName = "Full Flow Test $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        description = "End-to-end test container"
    } | ConvertTo-Json

    $container = Invoke-RestMethod -Uri "$BaseUrl/api/containers" `
        -Method POST `
        -Body $createBody `
        -ContentType "application/json" `
        -SkipCertificateCheck

    $containerId = $container.id
    Write-Host "  ✓ Container created: $containerId" -ForegroundColor Green
    Write-Host "  Display Name: $($container.displayName)" -ForegroundColor Gray
} catch {
    Write-Host "  ✗ FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Start-Sleep -Seconds 3

# Test 2: Get Container Drive
Write-Host "`n[2/6] Getting container drive..." -ForegroundColor Cyan
try {
    $drive = Invoke-RestMethod -Uri "$BaseUrl/api/containers/$containerId/drive" `
        -Method GET `
        -SkipCertificateCheck

    $driveId = $drive.id
    Write-Host "  ✓ Drive retrieved: $driveId" -ForegroundColor Green
} catch {
    Write-Host "  ✗ FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Start-Sleep -Seconds 2

# Test 3: Upload File using BFF API endpoint
Write-Host "`n[3/6] Uploading test file..." -ForegroundColor Cyan
$fileName = "test-file-$(Get-Date -Format 'HHmmss').txt"
$fileContent = "Hello from SDAP Task 2.5 Test!`nTimestamp: $(Get-Date)`nContainer ID: $containerId"

try {
    # Upload via BFF API endpoint
    $uploadUrl = "$BaseUrl/api/drives/$driveId/upload?fileName=$fileName"

    $uploadResponse = Invoke-RestMethod -Uri $uploadUrl `
        -Method PUT `
        -Body $fileContent `
        -ContentType "text/plain" `
        -SkipCertificateCheck

    $itemId = $uploadResponse.id
    Write-Host "  ✓ File uploaded: $fileName" -ForegroundColor Green
    Write-Host "  Item ID: $itemId" -ForegroundColor Gray
} catch {
    Write-Host "  ✗ FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Response: $($_.ErrorDetails.Message)" -ForegroundColor Red
    $testPassed = $false
}

Start-Sleep -Seconds 3

# Test 4: List Drive Contents
Write-Host "`n[4/6] Listing drive contents..." -ForegroundColor Cyan
try {
    $items = Invoke-RestMethod -Uri "$BaseUrl/api/drives/$driveId/children" `
        -Method GET `
        -SkipCertificateCheck

    $itemCount = @($items).Count
    Write-Host "  ✓ Found $itemCount items in drive" -ForegroundColor Green

    if ($itemCount -gt 0) {
        Write-Host "  Files:" -ForegroundColor Gray
        foreach ($item in $items) {
            Write-Host "    - $($item.name) (Size: $($item.size) bytes)" -ForegroundColor Gray
            if ($item.name -eq $fileName) {
                $itemId = $item.id
                Write-Host "      ✓ Our test file found!" -ForegroundColor Green
            }
        }
    }
} catch {
    Write-Host "  ✗ FAILED: $($_.Exception.Message)" -ForegroundColor Red
    $testPassed = $false
}

# Test 5: Get File Metadata
if ($itemId) {
    Write-Host "`n[5/6] Getting file metadata..." -ForegroundColor Cyan
    try {
        $metadata = Invoke-RestMethod -Uri "$BaseUrl/api/drives/$driveId/items/$itemId" `
            -Method GET `
            -SkipCertificateCheck

        Write-Host "  ✓ Metadata retrieved" -ForegroundColor Green
        Write-Host "  Name: $($metadata.name)" -ForegroundColor Gray
        Write-Host "  Size: $($metadata.size) bytes" -ForegroundColor Gray
        Write-Host "  Created: $($metadata.createdDateTime)" -ForegroundColor Gray
        Write-Host "  Modified: $($metadata.lastModifiedDateTime)" -ForegroundColor Gray
    } catch {
        Write-Host "  ✗ FAILED: $($_.Exception.Message)" -ForegroundColor Red
        $testPassed = $false
    }
} else {
    Write-Host "`n[5/6] Get file metadata..." -ForegroundColor Cyan
    Write-Host "  ⚠ SKIPPED - No item ID available" -ForegroundColor Yellow
}

# Test 6: Download and Verify File
if ($itemId) {
    Write-Host "`n[6/6] Downloading and verifying file..." -ForegroundColor Cyan
    try {
        $downloadPath = "downloaded-$fileName"
        Invoke-RestMethod -Uri "$BaseUrl/api/drives/$driveId/items/$itemId/content" `
            -Method GET `
            -OutFile $downloadPath `
            -SkipCertificateCheck

        $downloadedContent = Get-Content $downloadPath -Raw

        if ($downloadedContent -like "*Hello from SDAP Task 2.5 Test*") {
            Write-Host "  ✓ File downloaded and content verified!" -ForegroundColor Green
            Write-Host "  Downloaded content:" -ForegroundColor Gray
            Write-Host "  ---" -ForegroundColor Gray
            $downloadedContent -split "`n" | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
            Write-Host "  ---" -ForegroundColor Gray

            # Cleanup
            Remove-Item $downloadPath -ErrorAction SilentlyContinue
        } else {
            Write-Host "  ✗ Content verification FAILED" -ForegroundColor Red
            Write-Host "  Expected content with 'Hello from SDAP Task 2.5 Test'" -ForegroundColor Red
            Write-Host "  Got: $downloadedContent" -ForegroundColor Red
            $testPassed = $false
        }
    } catch {
        Write-Host "  ✗ FAILED: $($_.Exception.Message)" -ForegroundColor Red
        $testPassed = $false
    }
} else {
    Write-Host "`n[6/6] Download and verify file..." -ForegroundColor Cyan
    Write-Host "  ⚠ SKIPPED - No item ID available" -ForegroundColor Yellow
}

# Summary
Write-Host "`n=== Test Summary ===" -ForegroundColor Yellow
if ($testPassed) {
    Write-Host "✓ ALL TESTS PASSED!" -ForegroundColor Green
} else {
    Write-Host "✗ Some tests failed" -ForegroundColor Red
}

Write-Host ""
Write-Host "Test Results:" -ForegroundColor Cyan
Write-Host "  Container ID: $containerId" -ForegroundColor Gray
Write-Host "  Drive ID: $driveId" -ForegroundColor Gray
if ($itemId) {
    Write-Host "  File ID: $itemId" -ForegroundColor Gray
    Write-Host "  File Name: $fileName" -ForegroundColor Gray
}
Write-Host ""

if ($testPassed) {
    Write-Host "🎉 Task 2.5 SPE integration is fully functional!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Verified operations:" -ForegroundColor Cyan
    Write-Host "  ✓ Container creation" -ForegroundColor Gray
    Write-Host "  ✓ Drive retrieval" -ForegroundColor Gray
    Write-Host "  ✓ File upload" -ForegroundColor Gray
    Write-Host "  ✓ File listing" -ForegroundColor Gray
    Write-Host "  ✓ File metadata retrieval" -ForegroundColor Gray
    Write-Host "  ✓ File download" -ForegroundColor Gray
    Write-Host "  ✓ Content verification" -ForegroundColor Gray
}
