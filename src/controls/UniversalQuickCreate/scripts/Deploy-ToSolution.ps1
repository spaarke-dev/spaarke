# Deploy UniversalDocumentUpload PCF Control to Universal Quick Create Solution
# This script deploys the control with the correct sprk_ publisher prefix

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "PCF Control Deployment to Solution" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Step 1: Check auth
Write-Host "`n[Step 1] Checking authentication..." -ForegroundColor Yellow
pac auth list

# Step 2: Deploy control using pac pcf push with correct publisher
Write-Host "`n[Step 2] Deploying PCF control with sprk publisher prefix..." -ForegroundColor Yellow
Write-Host "NOTE: This will initially deploy to Default solution" -ForegroundColor Gray

Set-Location "C:\code_files\spaarke\src\controls\UniversalQuickCreate"

# Disable Directory.Packages.props
if (Test-Path "C:\code_files\spaarke\Directory.Packages.props") {
    Move-Item "C:\code_files\spaarke\Directory.Packages.props" "C:\code_files\spaarke\Directory.Packages.props.disabled" -Force
    Write-Host "Disabled Directory.Packages.props" -ForegroundColor Gray
}

# Deploy control
pac pcf push --publisher-prefix sprk

# Restore Directory.Packages.props
if (Test-Path "C:\code_files\spaarke\Directory.Packages.props.disabled") {
    Move-Item "C:\code_files\spaarke\Directory.Packages.props.disabled" "C:\code_files\spaarke\Directory.Packages.props" -Force
    Write-Host "Restored Directory.Packages.props" -ForegroundColor Gray
}

# Step 3: Add control to UniversalQuickCreate solution
Write-Host "`n[Step 3] Adding control to 'UniversalQuickCreate' solution..." -ForegroundColor Yellow

# Get the control component ID
Write-Host "Querying for the deployed control..." -ForegroundColor Gray

$fetchXml = @"
<fetch top='1'>
  <entity name='customcontrol'>
    <attribute name='customcontrolid' />
    <attribute name='name' />
    <filter>
      <condition attribute='name' operator='eq' value='Spaarke.Controls.UniversalDocumentUpload' />
    </filter>
  </entity>
</fetch>
"@

# Note: pac data query might not be available, so we'll use the web UI approach instead
Write-Host "`nManual step required:" -ForegroundColor Yellow
Write-Host "1. Go to Power Apps maker portal (make.powerapps.com)" -ForegroundColor White
Write-Host "2. Open 'Universal Quick Create' solution" -ForegroundColor White
Write-Host "3. Click 'Add existing' -> 'More' -> 'Developer' -> 'Custom control'" -ForegroundColor White
Write-Host "4. Search for 'UniversalDocumentUpload'" -ForegroundColor White
Write-Host "5. Select it and click 'Add'" -ForegroundColor White
Write-Host "6. Publish all customizations" -ForegroundColor White

Write-Host "`nDeployment complete!" -ForegroundColor Green
Write-Host "Bundle.js should now have sprk_ prefix" -ForegroundColor Green
