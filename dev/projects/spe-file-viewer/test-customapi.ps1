# Test Custom API via PowerShell
# Usage: .\test-customapi.ps1 -DocumentId "your-document-guid"

param(
    [Parameter(Mandatory=$false)]
    [string]$DocumentId
)

$dataverseUrl = "https://spaarkedev1.api.crm.dynamics.com"

# If no DocumentId provided, try to find one
if ([string]::IsNullOrEmpty($DocumentId)) {
    Write-Host "No DocumentId provided. Searching for a document record..." -ForegroundColor Yellow

    $token = az account get-access-token --resource $dataverseUrl --query accessToken -o tsv

    $headers = @{
        "Authorization" = "Bearer $token"
        "Accept" = "application/json"
        "OData-MaxVersion" = "4.0"
        "OData-Version" = "4.0"
    }

    try {
        $docsQuery = "$dataverseUrl/api/data/v9.2/sprk_documents?`$select=sprk_documentid,sprk_name&`$top=1"
        $docsResponse = Invoke-RestMethod -Uri $docsQuery -Method Get -Headers $headers

        if ($docsResponse.value.Count -eq 0) {
            Write-Host "No document records found. Please create a document first." -ForegroundColor Red
            exit 1
        }

        $DocumentId = $docsResponse.value[0].sprk_documentid
        $docName = $docsResponse.value[0].sprk_name

        Write-Host "Found document: $docName" -ForegroundColor Green
        Write-Host "Document ID: $DocumentId" -ForegroundColor Cyan
        Write-Host ""
    }
    catch {
        Write-Host "Error finding documents: $($_.ErrorDetails.Message)" -ForegroundColor Red
        exit 1
    }
}

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Testing Custom API: sprk_GetFilePreviewUrl" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Document ID: $DocumentId" -ForegroundColor Cyan
Write-Host ""

# Get fresh token
$token = az account get-access-token --resource $dataverseUrl --query accessToken -o tsv

$headers = @{
    "Authorization" = "Bearer $token"
    "Accept" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
}

# Call the Custom API
$apiUrl = "$dataverseUrl/api/data/v9.2/sprk_documents($DocumentId)/Microsoft.Dynamics.CRM.sprk_GetFilePreviewUrl()"

Write-Host "Calling Custom API..." -ForegroundColor Yellow

try {
    $response = Invoke-RestMethod -Uri $apiUrl -Method Get -Headers $headers

    Write-Host ""
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "SUCCESS!" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Preview URL:" -ForegroundColor Cyan
    Write-Host "  $($response.PreviewUrl)" -ForegroundColor White
    Write-Host ""
    Write-Host "File Name: $($response.FileName)" -ForegroundColor Cyan
    Write-Host "File Size: $($response.FileSize) bytes" -ForegroundColor Cyan
    Write-Host "Content Type: $($response.ContentType)" -ForegroundColor Cyan
    Write-Host "Expires At: $($response.ExpiresAt)" -ForegroundColor Cyan
    Write-Host "Correlation ID: $($response.CorrelationId)" -ForegroundColor Cyan
    Write-Host ""

    Write-Host "Full Response:" -ForegroundColor Gray
    $response | ConvertTo-Json -Depth 3 | Write-Host -ForegroundColor Gray

    # Offer to open preview URL
    Write-Host ""
    $open = Read-Host "Open preview URL in browser? (y/n)"
    if ($open -eq "y") {
        Start-Process $response.PreviewUrl
    }
}
catch {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Red
    Write-Host "ERROR" -ForegroundColor Red
    Write-Host "============================================" -ForegroundColor Red
    Write-Host ""

    if ($_.ErrorDetails.Message) {
        $errorObj = $_.ErrorDetails.Message | ConvertFrom-Json
        Write-Host "Error Code: $($errorObj.error.code)" -ForegroundColor Red
        Write-Host "Error Message: $($errorObj.error.message)" -ForegroundColor Red
        Write-Host ""
        Write-Host "Full Error Details:" -ForegroundColor Gray
        $errorObj | ConvertTo-Json -Depth 5 | Write-Host -ForegroundColor Gray
    } else {
        Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    }

    Write-Host ""
    Write-Host "Troubleshooting Steps:" -ForegroundColor Yellow
    Write-Host "1. Check Plugin Trace Logs in XrmToolBox" -ForegroundColor Yellow
    Write-Host "2. Verify External Service Config record exists" -ForegroundColor Yellow
    Write-Host "3. Check that BFF API is running" -ForegroundColor Yellow
    Write-Host "4. Verify document record has a file in SharePoint Embedded" -ForegroundColor Yellow
}
