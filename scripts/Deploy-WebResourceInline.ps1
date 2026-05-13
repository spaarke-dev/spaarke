# Inline web resource deploy helper - no build, just upload + publish
# Used for solutions whose dedicated scripts force a rebuild we want to skip.
param(
    [Parameter(Mandatory)]
    [string]$DataverseUrl,
    [Parameter(Mandatory)]
    [string]$WebResourceName,
    [Parameter(Mandatory)]
    [string]$FilePath,
    [string]$DisplayName = $WebResourceName,
    [int]$WebResourceType = 1  # 1 = HTML
)

$ErrorActionPreference = 'Stop'
$orgUrl = $DataverseUrl.TrimEnd('/')

if (-not (Test-Path $FilePath)) {
    Write-Host "FAIL  $WebResourceName - file not found: $FilePath" -ForegroundColor Red
    exit 1
}

$accessToken = az account get-access-token --resource $orgUrl --query accessToken -o tsv
if ([string]::IsNullOrEmpty($accessToken)) {
    Write-Host "FAIL  $WebResourceName - could not acquire token (run az login)" -ForegroundColor Red
    exit 1
}

$apiUrl  = "$orgUrl/api/data/v9.2"
$headers = @{
    'Authorization'    = "Bearer $accessToken"
    'Content-Type'     = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Accept'           = 'application/json'
}

$fileBytes   = [System.IO.File]::ReadAllBytes($FilePath)
$fileContent = [Convert]::ToBase64String($fileBytes)
$sizeKb      = [math]::Round($fileBytes.Length / 1KB)

$searchUrl = "$apiUrl/webresourceset?`$filter=name eq '$WebResourceName'"
$existing  = Invoke-RestMethod -Uri $searchUrl -Headers $headers -Method Get

if ($existing.value.Count -gt 0) {
    $wrId = $existing.value[0].webresourceid
    Write-Host "UPDATE $WebResourceName ($sizeKb KB) -> $wrId" -ForegroundColor Cyan
    $body = @{ content = $fileContent } | ConvertTo-Json
    Invoke-RestMethod -Uri "$apiUrl/webresourceset($wrId)" -Headers $headers -Method Patch -Body $body | Out-Null
} else {
    Write-Host "CREATE $WebResourceName ($sizeKb KB)" -ForegroundColor Green
    $createHeaders          = $headers.Clone()
    $createHeaders['Prefer'] = 'return=representation'
    $createBody = @{
        name            = $WebResourceName
        displayname     = $DisplayName
        webresourcetype = $WebResourceType
        content         = $fileContent
    } | ConvertTo-Json
    $created = Invoke-RestMethod -Uri "$apiUrl/webresourceset" -Headers $createHeaders -Method Post -Body $createBody
    $wrId = $created.webresourceid
    Write-Host "       Created: $wrId" -ForegroundColor Green
}

$publishXml = "<importexportxml><webresources><webresource>{$wrId}</webresource></webresources></importexportxml>"
$publishBody = @{ ParameterXml = $publishXml } | ConvertTo-Json
Invoke-RestMethod -Uri "$apiUrl/PublishXml" -Headers $headers -Method Post -Body $publishBody | Out-Null
Write-Host "       Published: $WebResourceName" -ForegroundColor Green
