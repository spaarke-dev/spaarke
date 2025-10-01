# Test script to create a document in Dataverse and trigger the plugin
# This will trigger the DocumentEventPlugin which queues to Service Bus

$env = "https://spaarkedev1.crm.dynamics.com/"

Write-Host "Creating test document in Dataverse..." -ForegroundColor Cyan

# Using Power Platform CLI with FetchXml to create via PAC
$createJson = @{
    "sprk_documentname" = "Test Document - E2E Flow $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    "sprk_filetype" = "pdf"
    "sprk_filesize" = 1024
    "statuscode" = 1
    "statecode" = 0
} | ConvertTo-Json

Write-Host "Document data: $createJson" -ForegroundColor Yellow

# Use direct Web API call since pac doesn't have create command
$token = (pac auth list --json | ConvertFrom-Json | Where-Object { $_.IsActive -eq $true }).Token

if (-not $token) {
    Write-Host "Getting access token..." -ForegroundColor Yellow
    # Trigger auth
    pac auth list
}

Write-Host "`nNote: pac CLI doesn't support direct record creation." -ForegroundColor Yellow
Write-Host "Please create a document manually in the Power Apps interface at:" -ForegroundColor Yellow
Write-Host "https://make.powerapps.com/environments/default/tables/sprk_document/data" -ForegroundColor Cyan
Write-Host "`nOr use the Model-Driven App to create a Document record." -ForegroundColor Yellow
Write-Host "`nAfter creating the document, check:" -ForegroundColor Yellow
Write-Host "1. Service Bus queue for the message" -ForegroundColor White
Write-Host "2. API logs for processing confirmation" -ForegroundColor White