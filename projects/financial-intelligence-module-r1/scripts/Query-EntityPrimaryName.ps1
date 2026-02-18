param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [string]$EntityLogicalName = "sprk_document"
)

$token = az account get-access-token --resource "$EnvironmentUrl" --query accessToken -o tsv
$headers = @{
    "Authorization"    = "Bearer $token"
    "Accept"           = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
}

$uri = "$EnvironmentUrl/api/data/v9.2/EntityDefinitions(LogicalName='$EntityLogicalName')?`$select=PrimaryNameAttribute,PrimaryIdAttribute"
$resp = Invoke-RestMethod -Uri $uri -Headers $headers
Write-Host "Entity: $EntityLogicalName"
Write-Host "Primary Name Attribute: $($resp.PrimaryNameAttribute)"
Write-Host "Primary ID Attribute: $($resp.PrimaryIdAttribute)"
