param(
    [string]$DataverseUrl = $env:DATAVERSE_URL
)

if (-not $DataverseUrl) {
    Write-Error "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
    exit 1
}

$token = (az account get-access-token --resource $DataverseUrl --query accessToken -o tsv)
$headers = @{
    'Authorization' = "Bearer $token"
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
}
$url = "$DataverseUrl/api/data/v9.2/EntityDefinitions(LogicalName='sprk_document')/Attributes"
$response = Invoke-RestMethod -Uri $url -Headers $headers -Method Get
# Get detailed attribute info including type
$response.value | Where-Object { $_.LogicalName -like 'sprk_email*' -or $_.LogicalName -eq 'sprk_attachments' } | Select-Object LogicalName, @{N='DisplayName';E={$_.DisplayName.UserLocalizedLabel.Label}}, AttributeType, @{N='MaxLength';E={$_.MaxLength}} | Format-Table -AutoSize
