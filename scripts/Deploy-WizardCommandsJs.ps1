# Focused deployment of sprk_wizard_commands.js (Type 3 = JScript).
# Use this when only the JS bridge needs updating and you do NOT want to
# touch the wizard HTML web resources that the full Deploy-WizardCodePages.ps1
# also publishes. Mirrors the same create/update + AddSolutionComponent + PublishXml
# flow as the full script, just for the single file.
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [string]$SolutionName = 'spaarke_core'
)

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$ErrorActionPreference = 'Stop'
$apiUrl  = "$DataverseUrl/api/data/v9.2"
$wrName  = 'sprk_wizard_commands'
$wrFile  = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\client\webresources\js\sprk_wizard_commands.js'

if (-not (Test-Path $wrFile)) {
    Write-Error "Source file not found: $wrFile"
    exit 1
}

Write-Host "[AUTH] Acquiring access token for $DataverseUrl..."
$accessToken = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if ([string]::IsNullOrEmpty($accessToken)) {
    Write-Error "Failed to acquire access token. Run 'az login' first."
    exit 1
}

$headers = @{
    'Authorization'    = "Bearer $accessToken"
    'Content-Type'     = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Accept'           = 'application/json'
}

$fileBytes   = [System.IO.File]::ReadAllBytes($wrFile)
$fileContent = [Convert]::ToBase64String($fileBytes)
$fileSizeKb  = [math]::Round((Get-Item $wrFile).Length / 1KB, 1)

Write-Host "[CHECK] Looking up $wrName..."
$search = Invoke-RestMethod -Uri "$apiUrl/webresourceset?`$filter=name eq '$wrName'" -Headers $headers -Method Get

if ($search.value.Count -gt 0) {
    $id = $search.value[0].webresourceid
    Write-Host "[UPDATE] $wrName ($fileSizeKb KB) -> $id" -ForegroundColor Cyan
    $body = @{ content = $fileContent } | ConvertTo-Json
    Invoke-RestMethod -Uri "$apiUrl/webresourceset($id)" -Headers $headers -Method Patch -Body $body | Out-Null
} else {
    Write-Host "[CREATE] $wrName ($fileSizeKb KB)" -ForegroundColor Green
    $createBody = @{
        name            = $wrName
        displayname     = 'Wizard Command Handlers'
        webresourcetype = 3
        content         = $fileContent
    } | ConvertTo-Json
    $createHeaders = $headers.Clone()
    $createHeaders['Prefer'] = 'return=representation'
    $created = Invoke-RestMethod -Uri "$apiUrl/webresourceset" -Headers $createHeaders -Method Post -Body $createBody
    $id = $created.webresourceid

    Write-Host "[SOLUTION] Adding $wrName to $SolutionName..."
    $addBody = @{
        ComponentId           = $id
        ComponentType         = 61
        SolutionUniqueName    = $SolutionName
        AddRequiredComponents = $false
    } | ConvertTo-Json
    Invoke-RestMethod -Uri "$apiUrl/AddSolutionComponent" -Headers $headers -Method Post -Body $addBody | Out-Null
}

Write-Host "[PUBLISH] Publishing $wrName..."
$publishXml  = "<importexportxml><webresources><webresource>{$id}</webresource></webresources></importexportxml>"
$publishBody = @{ ParameterXml = $publishXml } | ConvertTo-Json
Invoke-RestMethod -Uri "$apiUrl/PublishXml" -Headers $headers -Method Post -Body $publishBody | Out-Null

Write-Host ''
Write-Host "Done. $wrName updated and published." -ForegroundColor Green
Write-Host "Verify with: Hard refresh form, then in console: typeof Spaarke?.Commands?.Wizards?.openDocumentUploadWizard"
