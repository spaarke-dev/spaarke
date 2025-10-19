# Update Web Resource - sprk_subgrid_commands.js v2.1.0
# This script uploads the updated Web Resource to Dataverse

$webResourcePath = "c:\code_files\spaarke\src\controls\UniversalQuickCreate\UniversalQuickCreateSolution\src\WebResources\sprk_subgrid_commands.js"
$webResourceName = "sprk_subgrid_commands"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Update Web Resource v2.1.0" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if file exists
if (-not (Test-Path $webResourcePath)) {
    Write-Host "ERROR: Web Resource file not found at: $webResourcePath" -ForegroundColor Red
    exit 1
}

Write-Host "✓ Found Web Resource file" -ForegroundColor Green
Write-Host ""

# Get file content
$fileContent = Get-Content $webResourcePath -Raw
$base64Content = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($fileContent))

Write-Host "Connecting to Dataverse..." -ForegroundColor Yellow

# Use PAC to get connection info
$authList = pac auth list 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Not authenticated. Run: pac auth create" -ForegroundColor Red
    exit 1
}

Write-Host "✓ Connected" -ForegroundColor Green
Write-Host ""

# Update Web Resource using pac CLI
Write-Host "Uploading Web Resource: $webResourceName" -ForegroundColor Yellow

# Create temp JSON file for update
$tempJson = @"
{
    "name": "$webResourceName",
    "displayname": "Subgrid Commands v2.1.0",
    "webresourcetype": 3,
    "content": "$base64Content"
}
"@

$tempFile = [System.IO.Path]::GetTempFileName()
$tempJson | Out-File -FilePath $tempFile -Encoding utf8

try {
    # Use pac data update
    pac data update --entity-logical-name webresource --id "name=$webResourceName" --data-file $tempFile

    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Web Resource uploaded successfully" -ForegroundColor Green
        Write-Host ""
        Write-Host "Publishing customizations..." -ForegroundColor Yellow
        pac solution publish
        Write-Host "✓ Published" -ForegroundColor Green
    } else {
        Write-Host "ERROR: Failed to upload Web Resource" -ForegroundColor Red
        exit 1
    }
} finally {
    Remove-Item $tempFile -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Web Resource Update Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "1. Hard refresh your browser (Ctrl+Shift+R)"
Write-Host "2. Open Matter record"
Write-Host "3. Click 'Quick Create: Document' button"
Write-Host "4. Form Dialog should open (not Custom Page)"
Write-Host ""
