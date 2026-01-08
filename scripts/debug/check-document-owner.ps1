$token = az account get-access-token --resource "https://spaarkedev1.crm.dynamics.com" --query "accessToken" -o tsv

$headers = @{
    'Authorization' = "Bearer $token"
    'Accept' = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
}

$documentId = "46ee229e-14ec-f811-8486-7c1e520aa4df"
$currentUserId = "1d02f31c-1872-f011-b4cb-7c1e52671ad0"  # Ralph Schroeder
$baseUrl = "https://spaarkedev1.crm.dynamics.com/api/data/v9.2"

Write-Host "Checking Document Access for AI Analysis" -ForegroundColor Cyan
Write-Host ""

# Get document details
try {
    $doc = Invoke-RestMethod -Uri "$baseUrl/sprk_documents($documentId)?`$select=sprk_documentid,sprk_documentname,_ownerid_value" -Headers $headers -Method Get
    
    Write-Host "Document Details:" -ForegroundColor Yellow
    Write-Host "  ID: $($doc.sprk_documentid)" -ForegroundColor Gray
    Write-Host "  Name: $($doc.sprk_documentname)" -ForegroundColor Gray
    Write-Host "  Owner ID: $($doc._ownerid_value)" -ForegroundColor Gray
    Write-Host ""
    
    # Get owner name
    $owner = Invoke-RestMethod -Uri "$baseUrl/systemusers($($doc._ownerid_value))?`$select=fullname" -Headers $headers -Method Get
    Write-Host "  Owner Name: $($owner.fullname)" -ForegroundColor Gray
    Write-Host ""
    
    # Check if current user is the owner
    if ($doc._ownerid_value -eq $currentUserId) {
        Write-Host "✅ Current user (Ralph Schroeder) IS the owner" -ForegroundColor Green
        Write-Host ""
        Write-Host "This is unexpected - owner should have Read access." -ForegroundColor Yellow
        Write-Host "The authorization check might be failing for another reason." -ForegroundColor Yellow
    }
    else {
        Write-Host "❌ Current user (Ralph Schroeder) is NOT the owner" -ForegroundColor Red
        Write-Host ""
        Write-Host "Current User ID: $currentUserId" -ForegroundColor Yellow
        Write-Host "Document Owner ID: $($doc._ownerid_value)" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "The user needs to be granted access to this document to analyze it." -ForegroundColor Yellow
    }
}
catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
}
