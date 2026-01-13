# Set the DefaultContainerId in Azure App Service
$containerId = "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
Write-Host "Setting EmailProcessing__DefaultContainerId to: $containerId"

az webapp config appsettings set `
    --name spe-api-dev-67e2xz `
    --resource-group spe-infrastructure-westus2 `
    --settings "EmailProcessing__DefaultContainerId=$containerId" `
    --output none

# Verify
$result = az webapp config appsettings list `
    --name spe-api-dev-67e2xz `
    --resource-group spe-infrastructure-westus2 `
    --query "[?name=='EmailProcessing__DefaultContainerId'].value" `
    --output tsv

Write-Host "Verified value: $result"
