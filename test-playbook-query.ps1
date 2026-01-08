# Test the exact query that PlaybookService.GetByNameAsync uses

$token = az account get-access-token --resource "https://spaarkedev1.crm.dynamics.com" --query "accessToken" -o tsv

$headers = @{
    'Authorization' = "Bearer $token"
    'Accept' = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
}

$select = "sprk_analysisplaybookid,sprk_name,sprk_description,sprk_ispublic,_ownerid_value,createdon,modifiedon"
$filter = "sprk_name eq 'Document Profile'"
$url = "https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_analysisplaybooks?`$select=$select&`$filter=$filter&`$top=1"

Write-Host "Testing query:" -ForegroundColor Cyan
Write-Host $url
Write-Host ""

try {
    $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Get -ErrorAction Stop
    Write-Host "SUCCESS! Response:" -ForegroundColor Green
    $response | ConvertTo-Json -Depth 5
} catch {
    Write-Host "ERROR:" -ForegroundColor Red
    Write-Host $_.Exception.Message
    if ($_.Exception.Response) {
        Write-Host "Status Code:" $_.Exception.Response.StatusCode.value__
    }
}
