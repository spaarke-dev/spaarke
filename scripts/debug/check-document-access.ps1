$token = az account get-access-token --resource "https://spaarkedev1.crm.dynamics.com" --query "accessToken" -o tsv

$headers = @{
    'Authorization' = "Bearer $token"
    'Accept' = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
}

$documentId = "dbf58906-12ec-f011-8406-7c1e520aa4df"
$ownerId = "1d02f31c-1872-f011-b4cb-7c1e52671ad0"
$baseUrl = "https://spaarkedev1.crm.dynamics.com/api/data/v9.2"

Write-Host "Checking Document Access" -ForegroundColor Cyan
Write-Host ""

# Get document owner details
Write-Host "Document Owner:" -ForegroundColor Yellow
$ownerQuery = "systemusers($ownerId)?`$select=systemuserid,fullname,domainname"
try {
    $owner = Invoke-RestMethod -Uri "$baseUrl/$ownerQuery" -Headers $headers -Method Get
    Write-Host "  Name: $($owner.fullname)" -ForegroundColor Green
    Write-Host "  Domain: $($owner.domainname)" -ForegroundColor Green
}
catch {
    Write-Host "  Could not retrieve owner details" -ForegroundColor Red
}
Write-Host ""

# Get current user (from token)
Write-Host "Current User (from your Azure CLI token):" -ForegroundColor Yellow
$whoAmI = Invoke-RestMethod -Uri "$baseUrl/WhoAmI" -Headers $headers -Method Get
$currentUserId = $whoAmI.UserId

$currentUserQuery = "systemusers($currentUserId)?`$select=systemuserid,fullname,domainname"
$currentUser = Invoke-RestMethod -Uri "$baseUrl/$currentUserQuery" -Headers $headers -Method Get
Write-Host "  Name: $($currentUser.fullname)" -ForegroundColor Green
Write-Host "  Domain: $($currentUser.domainname)" -ForegroundColor Green
Write-Host "  User ID: $currentUserId" -ForegroundColor Green
Write-Host ""

# Check access using RetrievePrincipalAccess
Write-Host "Checking access rights..." -ForegroundColor Yellow
$accessRequest = @{
    Target = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.sprk_document"
        "sprk_documentid" = $documentId
    }
} | ConvertTo-Json -Depth 10

try {
    $accessResult = Invoke-RestMethod `
        -Uri "$baseUrl/RetrievePrincipalAccess" `
        -Headers $headers `
        -Method Post `
        -Body $accessRequest `
        -ContentType "application/json"
    
    Write-Host "✅ Access Rights: $($accessResult.AccessRights)" -ForegroundColor Green
    
    # Decode access rights (bitwise enum)
    $rights = [int]$accessResult.AccessRights
    Write-Host "  Rights breakdown:" -ForegroundColor Gray
    if ($rights -band 1) { Write-Host "    • Read" -ForegroundColor Gray }
    if ($rights -band 2) { Write-Host "    • Write" -ForegroundColor Gray }
    if ($rights -band 4) { Write-Host "    • Append" -ForegroundColor Gray }
    if ($rights -band 16) { Write-Host "    • AppendTo" -ForegroundColor Gray }
    if ($rights -band 32) { Write-Host "    • Delete" -ForegroundColor Gray }
    if ($rights -band 65536) { Write-Host "    • Share" -ForegroundColor Gray }
    if ($rights -band 131072) { Write-Host "    • Assign" -ForegroundColor Gray }
}
catch {
    Write-Host "❌ Access check failed" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
}
