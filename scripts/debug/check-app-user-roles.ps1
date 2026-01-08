$token = az account get-access-token --resource "https://spaarkedev1.crm.dynamics.com" --query "accessToken" -o tsv

$headers = @{
    'Authorization' = "Bearer $token"
    'Accept' = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
}

$appId = "1e40baad-e065-4aea-a8d4-4b7ab273458c"  # BFF API App ID
$baseUrl = "https://spaarkedev1.crm.dynamics.com/api/data/v9.2"

Write-Host "Checking security roles for BFF API Application User" -ForegroundColor Cyan
Write-Host "App ID: $appId" -ForegroundColor Gray
Write-Host ""

# Find the application user
$userQuery = "systemusers?`$filter=azureactivedirectoryobjectid eq $appId&`$select=systemuserid,fullname,isdisabled"
try {
    $userResult = Invoke-RestMethod -Uri "$baseUrl/$userQuery" -Headers $headers -Method Get
    
    if ($userResult.value.Count -eq 0) {
        Write-Host "❌ Application User NOT FOUND" -ForegroundColor Red
        Write-Host "The service principal needs to be added as an application user in Dataverse" -ForegroundColor Yellow
        exit 1
    }
    
    $userId = $userResult.value[0].systemuserid
    $userName = $userResult.value[0].fullname
    $disabled = $userResult.value[0].isdisabled
    
    Write-Host "✅ Found Application User" -ForegroundColor Green
    Write-Host "User ID: $userId" -ForegroundColor Yellow
    Write-Host "Name: $userName" -ForegroundColor Yellow
    Write-Host "Disabled: $disabled" -ForegroundColor Yellow
    Write-Host ""
    
    # Get security roles
    Write-Host "Security Roles Assigned:" -ForegroundColor Cyan
    $rolesQuery = "systemusers($userId)/systemuserroles_association?`$select=roleid,name"
    $rolesResult = Invoke-RestMethod -Uri "$baseUrl/$rolesQuery" -Headers $headers -Method Get
    
    if ($rolesResult.value.Count -eq 0) {
        Write-Host "❌ NO SECURITY ROLES ASSIGNED" -ForegroundColor Red
        Write-Host ""
        Write-Host "The application user needs at least one security role with:" -ForegroundColor Yellow
        Write-Host "  - Read privilege on sprk_documents entity" -ForegroundColor Yellow
        Write-Host "  - Read privilege on sprk_analysisplaybooks entity" -ForegroundColor Yellow
        Write-Host "  - Read privilege on sprk_analysisactions entity" -ForegroundColor Yellow
    }
    else {
        foreach ($role in $rolesResult.value) {
            Write-Host "  • $($role.name) (ID: $($role.roleid))" -ForegroundColor Green
        }
    }
}
catch {
    Write-Host "❌ Error querying Dataverse: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        Write-Host "Response: $responseBody" -ForegroundColor Gray
    }
}
