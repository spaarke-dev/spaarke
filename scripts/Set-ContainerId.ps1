# Set the DefaultContainerId in Azure App Service app settings

param(
    [Parameter(Mandatory)][string]$ContainerId,
    [Parameter(Mandatory)][string]$AppServiceName,
    [Parameter(Mandatory)][string]$ResourceGroup,
    [string]$SettingName = "EmailProcessing__DefaultContainerId"
)

Write-Host "Setting $SettingName to: $ContainerId"
Write-Host "App Service: $AppServiceName  Resource Group: $ResourceGroup"

az webapp config appsettings set `
    --name $AppServiceName `
    --resource-group $ResourceGroup `
    --settings "$SettingName=$ContainerId" `
    --output none

# Verify
$result = az webapp config appsettings list `
    --name $AppServiceName `
    --resource-group $ResourceGroup `
    --query "[?name=='$SettingName'].value" `
    --output tsv

Write-Host "Verified value: $result"
