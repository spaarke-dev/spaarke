$token = az account get-access-token --resource "https://spaarkedev1.crm.dynamics.com" --query "accessToken" -o tsv

$headers = @{
    'Authorization' = "Bearer $token"
    'Accept' = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
}

$baseUrl = "https://spaarkedev1.crm.dynamics.com/api/data/v9.2"

Write-Host "Listing all Application Users in Dataverse" -ForegroundColor Cyan
Write-Host ""

# List all application users (islicensed=false typically indicates app users)
$query = "systemusers?`$filter=applicationid ne null&`$select=systemuserid,fullname,applicationid,azureactivedirectoryobjectid,isdisabled&`$orderby=fullname"

try {
    $result = Invoke-RestMethod -Uri "$baseUrl/$query" -Headers $headers -Method Get
    
    if ($result.value.Count -eq 0) {
        Write-Host "No application users found" -ForegroundColor Yellow
    }
    else {
        Write-Host "Found $($result.value.Count) application user(s):" -ForegroundColor Green
        Write-Host ""
        
        foreach ($user in $result.value) {
            Write-Host "Name: $($user.fullname)" -ForegroundColor Yellow
            Write-Host "  User ID: $($user.systemuserid)" -ForegroundColor Gray
            Write-Host "  App ID: $($user.applicationid)" -ForegroundColor Gray
            Write-Host "  Azure AD Object ID: $($user.azureactivedirectoryobjectid)" -ForegroundColor Gray
            Write-Host "  Disabled: $($user.isdisabled)" -ForegroundColor Gray
            
            # Check if this is the BFF API
            if ($user.fullname -match "BFF|API|Spaarke" -or $user.applicationid -eq "1e40baad-e065-4aea-a8d4-4b7ab273458c") {
                Write-Host "  >>> THIS IS LIKELY THE BFF API APP USER <<<" -ForegroundColor Cyan
                
                # Get roles for this user
                $userId = $user.systemuserid
                $rolesQuery = "systemusers($userId)/systemuserroles_association?`$select=roleid,name"
                $rolesResult = Invoke-RestMethod -Uri "$baseUrl/$rolesQuery" -Headers $headers -Method Get
                
                Write-Host "  Security Roles:" -ForegroundColor Cyan
                if ($rolesResult.value.Count -eq 0) {
                    Write-Host "    ❌ NO ROLES ASSIGNED" -ForegroundColor Red
                }
                else {
                    foreach ($role in $rolesResult.value) {
                        Write-Host "    • $($role.name)" -ForegroundColor Green
                    }
                }
            }
            
            Write-Host ""
        }
    }
}
catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
}
