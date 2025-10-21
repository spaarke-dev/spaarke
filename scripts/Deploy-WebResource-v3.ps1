# Deploy Web Resource to Dataverse
# Updates sprk_subgrid_commands.js to v3.0.0

param(
    [Parameter(Mandatory=$false)]
    [string]$FilePath = "C:\code_files\spaarke\sprk_subgrid_commands.js",

    [Parameter(Mandatory=$false)]
    [string]$WebResourceName = "sprk_subgrid_commands"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Deploy Web Resource v3.0.0" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Verify file exists
if (-not (Test-Path $FilePath)) {
    Write-Host "ERROR: File not found: $FilePath" -ForegroundColor Red
    exit 1
}

# Step 2: Read and verify version
$content = Get-Content $FilePath -Raw
if ($content -match '@version\s+([\d.]+)') {
    $version = $matches[1]
    Write-Host "File Version: $version" -ForegroundColor Green

    if ($version -ne "3.0.0") {
        Write-Host "WARNING: Expected version 3.0.0, found $version" -ForegroundColor Yellow
    }
} else {
    Write-Host "WARNING: Could not find version in file" -ForegroundColor Yellow
}

# Step 3: Check if uses navigateTo (Custom Page)
if ($content -match 'Xrm\.Navigation\.navigateTo') {
    Write-Host "✓ Uses Custom Page approach (navigateTo)" -ForegroundColor Green
} else {
    Write-Host "✗ WARNING: Does not use navigateTo!" -ForegroundColor Red
}

# Step 4: Check Custom Page reference
if ($content -match 'sprk_documentuploaddialog_e52db') {
    Write-Host "✓ References Custom Page: sprk_documentuploaddialog_e52db" -ForegroundColor Green
} else {
    Write-Host "✗ WARNING: Custom Page reference not found!" -ForegroundColor Red
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "MANUAL DEPLOYMENT REQUIRED" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "The pac CLI does not support web resource deployment directly." -ForegroundColor Yellow
Write-Host "Please follow these steps:" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. Open Power Apps Maker Portal:" -ForegroundColor White
Write-Host "   https://make.powerapps.com" -ForegroundColor Cyan
Write-Host ""
Write-Host "2. Select environment: SPAARKE DEV 1" -ForegroundColor White
Write-Host ""
Write-Host "3. Navigate to: Solutions → UniversalQuickCreate" -ForegroundColor White
Write-Host ""
Write-Host "4. Search for: $WebResourceName" -ForegroundColor White
Write-Host ""
Write-Host "5. Click the web resource → Edit → Upload file" -ForegroundColor White
Write-Host ""
Write-Host "6. Select file:" -ForegroundColor White
Write-Host "   $FilePath" -ForegroundColor Cyan
Write-Host ""
Write-Host "7. Click Save → Publish" -ForegroundColor White
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "ALTERNATIVE: Use Dataverse API" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "If you have PowerShell Dataverse module installed:" -ForegroundColor Yellow
Write-Host ""
Write-Host "# Connect to Dataverse" -ForegroundColor Gray
Write-Host 'Connect-CrmOnline -ServerUrl "https://spaarkedev1.crm.dynamics.com"' -ForegroundColor Gray
Write-Host ""
Write-Host "# Find Web Resource" -ForegroundColor Gray
Write-Host '$wr = Get-CrmRecords -EntityLogicalName webresource -FilterAttribute name -FilterOperator eq -FilterValue "sprk_subgrid_commands"' -ForegroundColor Gray
Write-Host ""
Write-Host "# Update Content" -ForegroundColor Gray
Write-Host '$content = Get-Content "' + $FilePath + '" -Raw' -ForegroundColor Gray
Write-Host '$base64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($content))' -ForegroundColor Gray
Write-Host 'Set-CrmRecord -EntityLogicalName webresource -Id $wr.CrmRecords[0].webresourceid -Fields @{content=$base64}' -ForegroundColor Gray
Write-Host ""
Write-Host "# Publish" -ForegroundColor Gray
Write-Host 'Publish-CrmCustomization' -ForegroundColor Gray
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Open Power Apps in browser
$openBrowser = Read-Host "Open Power Apps Maker Portal in browser? (y/n)"
if ($openBrowser -eq 'y') {
    Start-Process "https://make.powerapps.com"
    Write-Host "Opening browser..." -ForegroundColor Green
}

# Copy file path to clipboard
$FilePath | Set-Clipboard
Write-Host ""
Write-Host "✓ File path copied to clipboard!" -ForegroundColor Green
Write-Host ""
