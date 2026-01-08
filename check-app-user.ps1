# Check if the BFF API service principal exists as an application user in Dataverse

$token = az account get-access-token --resource "https://spaarkedev1.crm.dynamics.com" --query "accessToken" -o tsv

$headers = @{
    'Authorization' = "Bearer $token"
    'Accept' = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
}

$appId = "1e40baad-e065-4aea-a8d4-4b7ab273458c"

# Query for application user
$filter = "azureactivedirectoryobjectid eq '$appId'"
$url = "https://spaarkedev1.crm.dynamics.com/api/data/v9.2/systemusers?`$filter=$filter&`$select=systemuserid,fullname,applicationid,isdisabled"

Write-Host "Checking for application user with App ID: $appId" -ForegroundColor Cyan
Write-Host ""

try {
    $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Get -ErrorAction Stop

    if ($response.value.Count -gt 0) {
        Write-Host "✓ Application user EXISTS in Dataverse:" -ForegroundColor Green
        $response.value | ConvertTo-Json -Depth 3

        $userId = $response.value[0].systemuserid

        # Get security roles
        Write-Host ""
        Write-Host "Checking security roles for user..." -ForegroundColor Cyan
        $rolesUrl = "https://spaarkedev1.crm.dynamics.com/api/data/v9.2/systemusers($userId)/systemuserroles_association?`$select=name,roleid"
        $rolesResponse = Invoke-RestMethod -Uri $rolesUrl -Headers $headers -Method Get

        if ($rolesResponse.value.Count -gt 0) {
            Write-Host "✓ Security roles assigned:" -ForegroundColor Green
            $rolesResponse.value | Format-Table name, roleid
        } else {
            Write-Host "✗ NO SECURITY ROLES ASSIGNED!" -ForegroundColor Red
            Write-Host "  The application user needs at least 'System Administrator' or custom role with permissions to sprk_* tables" -ForegroundColor Yellow
        }
    } else {
        Write-Host "✗ Application user NOT FOUND in Dataverse!" -ForegroundColor Red
        Write-Host ""
        Write-Host "To create an application user:" -ForegroundColor Yellow
        Write-Host "1. Go to Power Platform admin center: https://admin.powerplatform.microsoft.com/" -ForegroundColor Gray
        Write-Host "2. Select environment: spaarkedev1" -ForegroundColor Gray
        Write-Host "3. Settings > Users + permissions > Application users" -ForegroundColor Gray
        Write-Host "4. + New app user" -ForegroundColor Gray
        Write-Host "5. Select app: $appId" -ForegroundColor Gray
        Write-Host "6. Assign security role: System Administrator (or custom role)" -ForegroundColor Gray
    }
} catch {
    Write-Host "ERROR:" -ForegroundColor Red
    Write-Host $_.Exception.Message
}
