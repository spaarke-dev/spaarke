# Script to create Dataverse entities for Custom API Proxy
# Run this with: powershell -ExecutionPolicy Bypass -File CreateEntities.ps1

Write-Host "Creating External Service Configuration entity..." -ForegroundColor Green

# Create the External Service Configuration table
pac table create `
  --name "External Service Configuration" `
  --plural-name "External Service Configurations" `
  --schema-name "sprk_externalserviceconfig" `
  --description "Configuration for external APIs accessed via Custom API Proxy" `
  --primary-name "sprk_name" `
  --environment spaarkedev1

Write-Host "`nExternal Service Configuration entity created!" -ForegroundColor Green

# Note: Due to PAC CLI limitations, we'll need to add columns via the maker portal or additional commands
Write-Host "`nNext steps:" -ForegroundColor Yellow
Write-Host "1. Navigate to https://make.powerapps.com" -ForegroundColor Yellow
Write-Host "2. Select the spaarkedev1 environment" -ForegroundColor Yellow
Write-Host "3. Go to Tables and find 'External Service Configuration'" -ForegroundColor Yellow
Write-Host "4. Add the required columns as documented in PHASE-2-DATAVERSE-FOUNDATION.md" -ForegroundColor Yellow
