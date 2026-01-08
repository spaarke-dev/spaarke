$token = az account get-access-token --resource "https://spaarkedev1.crm.dynamics.com" --query "accessToken" -o tsv

$headers = @{
    'Authorization' = "Bearer $token"
    'Accept' = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
}

$oid = "c74ac1af-ff3b-46fb-83e7-3063616e959c"
$baseUrl = "https://spaarkedev1.crm.dynamics.com/api/data/v9.2"

Write-Host "Looking for Dataverse user with Azure AD Object ID: $oid" -ForegroundColor Cyan
Write-Host ""

# Query for user by azureactivedirectoryobjectid
$query = "systemusers?`$filter=azureactivedirectoryobjectid eq '$oid'&`$select=systemuserid,fullname,domainname,azureactivedirectoryobjectid,isdisabled"

try {
    $result = Invoke-RestMethod -Uri "$baseUrl/$query" -Headers $headers -Method Get
    
    if ($result.value.Count -eq 0) {
        Write-Host "❌ NO USER FOUND with this Azure AD Object ID" -ForegroundColor Red
        Write-Host ""
        Write-Host "This means the token's 'oid' claim doesn't map to any Dataverse user." -ForegroundColor Yellow
        Write-Host "The AiAuthorizationService won't be able to check permissions." -ForegroundColor Yellow
    }
    else {
        $user = $result.value[0]
        Write-Host "✅ FOUND USER" -ForegroundColor Green
        Write-Host "User ID: $($user.systemuserid)" -ForegroundColor Yellow
        Write-Host "Name: $($user.fullname)" -ForegroundColor Yellow
        Write-Host "Domain: $($user.domainname)" -ForegroundColor Yellow
        Write-Host "Disabled: $($user.isdisabled)" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "❌ Error querying Dataverse: $($_.Exception.Message)" -ForegroundColor Red
}
