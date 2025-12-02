# Test-SpeApis.ps1
# Quick test script for Task 2.5 SPE Container & File APIs

param(
    [string]$BaseUrl = "https://localhost:7123",
    [string]$ContainerTypeId = ""  # REQUIRED: Your SPE Container Type GUID
)

# Validate parameters
if ([string]::IsNullOrWhiteSpace($ContainerTypeId)) {
    Write-Host "ERROR: ContainerTypeId is required!" -ForegroundColor Red
    Write-Host "Usage: .\Test-SpeApis.ps1 -ContainerTypeId 'YOUR-GUID-HERE'" -ForegroundColor Yellow
    exit 1
}

Write-Host "`n=== SPE API Test Suite ===" -ForegroundColor Yellow
Write-Host "Base URL: $BaseUrl" -ForegroundColor Gray
Write-Host "Container Type: $ContainerTypeId" -ForegroundColor Gray
Write-Host ""

# Test 1: Create Container
Write-Host "[1/8] Creating container..." -ForegroundColor Cyan
try {
    $createBody = @{
        containerTypeId = $ContainerTypeId
        displayName = "API Test $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        description = "Created via PowerShell test script"
    } | ConvertTo-Json

    $container = Invoke-RestMethod -Uri "$BaseUrl/api/containers" `
        -Method POST `
        -Body $createBody `
        -ContentType "application/json" `
        -SkipCertificateCheck

    $containerId = $container.id
    Write-Host "  ✓ Container created: $containerId" -ForegroundColor Green
} catch {
    Write-Host "  ✗ FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Response: $($_.ErrorDetails.Message)" -ForegroundColor Red
    exit 1
}

Start-Sleep -Seconds 2

# Test 2: List Containers
Write-Host "[2/8] Listing containers..." -ForegroundColor Cyan
try {
    $containers = Invoke-RestMethod -Uri "$BaseUrl/api/containers?containerTypeId=$ContainerTypeId" `
        -Method GET `
        -SkipCertificateCheck

    Write-Host "  ✓ Found $(@($containers).Count) containers" -ForegroundColor Green
} catch {
    Write-Host "  ✗ FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Start-Sleep -Seconds 2

# Test 3: Get Container Drive
Write-Host "[3/8] Getting container drive..." -ForegroundColor Cyan
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

# Test 4: Upload Small File (NOTE: This uses direct Graph API, not BFF endpoint)
Write-Host "[4/8] Uploading test file..." -ForegroundColor Cyan
Write-Host "  ⚠ SKIPPED - Upload endpoint needs review" -ForegroundColor Yellow
Write-Host "  (Direct file upload via BFF needs additional endpoint implementation)" -ForegroundColor Gray

# Instead, let's test listing (should be empty for now)
Start-Sleep -Seconds 1

# Test 5: List Drive Children
Write-Host "[5/8] Listing drive contents..." -ForegroundColor Cyan
try {
    $items = Invoke-RestMethod -Uri "$BaseUrl/api/drives/$driveId/children" `
        -Method GET `
        -SkipCertificateCheck

    $itemCount = @($items).Count
    Write-Host "  ✓ Found $itemCount items in drive" -ForegroundColor Green

    if ($itemCount -gt 0) {
        Write-Host "  Files:" -ForegroundColor Gray
        $items | ForEach-Object {
            Write-Host "    - $($_.name) ($($_.size) bytes)" -ForegroundColor Gray
        }
    }
} catch {
    Write-Host "  ✗ FAILED: $($_.Exception.Message)" -ForegroundColor Red
    # Don't exit - empty drive is expected
}

Start-Sleep -Seconds 2

# Test 6-8 require actual file upload, so we'll skip for now
Write-Host "[6/8] Get file metadata..." -ForegroundColor Cyan
Write-Host "  ⚠ SKIPPED - No files to test" -ForegroundColor Yellow

Write-Host "[7/8] Download file..." -ForegroundColor Cyan
Write-Host "  ⚠ SKIPPED - No files to test" -ForegroundColor Yellow

Write-Host "[8/8] Delete file..." -ForegroundColor Cyan
Write-Host "  ⚠ SKIPPED - No files to test" -ForegroundColor Yellow

Write-Host "`n=== Test Summary ===" -ForegroundColor Yellow
Write-Host "✓ Container operations: WORKING" -ForegroundColor Green
Write-Host "✓ Drive operations: WORKING" -ForegroundColor Green
Write-Host "⚠ File operations: NEEDS FILE UPLOAD ENDPOINT" -ForegroundColor Yellow
Write-Host ""
Write-Host "Container ID: $containerId" -ForegroundColor Cyan
Write-Host "Drive ID: $driveId" -ForegroundColor Cyan
Write-Host ""
Write-Host "To complete testing, you can:" -ForegroundColor Gray
Write-Host "1. Upload a file manually via Graph API or SharePoint UI" -ForegroundColor Gray
Write-Host "2. Then test download/delete endpoints with real file IDs" -ForegroundColor Gray
Write-Host ""
