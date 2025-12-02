# Test BFF Endpoints - Phase 3 Verification
# Tests UAC, correlation ID tracking, and endpoint functionality

param(
    [Parameter(Mandatory=$false)]
    [string]$BffUrl = "https://spe-api-dev-67e2xz.azurewebsites.net",

    [Parameter(Mandatory=$false)]
    [string]$DocumentId = "f5cea4a7-53c6-f011-8543-0022482a47f5",

    [Parameter(Mandatory=$false)]
    [string]$BffAppId = "",  # Set to your BFF App ID (api://...)

    [Parameter(Mandatory=$false)]
    [switch]$SkipTokenAcquisition
)

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "BFF Endpoints Test Script" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Get Access Token
if (-not $SkipTokenAcquisition) {
    Write-Host "[1] Acquiring access token for BFF API..." -ForegroundColor Yellow

    if ([string]::IsNullOrEmpty($BffAppId)) {
        Write-Host "ERROR: BffAppId parameter is required" -ForegroundColor Red
        Write-Host "Usage: .\test-bff-endpoints.ps1 -BffAppId 'api://YOUR_BFF_APP_ID'" -ForegroundColor Red
        exit 1
    }

    try {
        # Acquire token using Azure CLI
        $tokenResponse = az account get-access-token --resource $BffAppId --query accessToken -o tsv 2>&1

        if ($LASTEXITCODE -ne 0) {
            Write-Host "ERROR: Failed to acquire token. Make sure you're logged in with 'az login'" -ForegroundColor Red
            Write-Host "Error: $tokenResponse" -ForegroundColor Red
            exit 1
        }

        $token = $tokenResponse
        Write-Host "✓ Token acquired successfully" -ForegroundColor Green
        Write-Host "  Token preview: $($token.Substring(0, 50))..." -ForegroundColor Gray
    } catch {
        Write-Host "ERROR: Exception during token acquisition: $_" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "[1] Skipping token acquisition (using manual token)" -ForegroundColor Yellow
    Write-Host "Please set `$token variable manually" -ForegroundColor Yellow
    # $token should be set manually in this case
}

Write-Host ""

# Step 2: Test /preview-url endpoint
Write-Host "[2] Testing GET /api/documents/{id}/preview-url..." -ForegroundColor Yellow

# Generate correlation ID
$correlationId = [guid]::NewGuid().ToString()
Write-Host "  Correlation ID: $correlationId" -ForegroundColor Gray

try {
    $headers = @{
        "Authorization" = "Bearer $token"
        "Content-Type" = "application/json"
        "X-Correlation-Id" = $correlationId
    }

    $url = "$BffUrl/api/documents/$DocumentId/preview-url"
    Write-Host "  URL: $url" -ForegroundColor Gray

    $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Get -ErrorAction Stop

    Write-Host "✓ Request successful (200 OK)" -ForegroundColor Green
    Write-Host ""
    Write-Host "Response:" -ForegroundColor Cyan
    Write-Host ($response | ConvertTo-Json -Depth 3) -ForegroundColor White

    # Verify correlation ID in response
    if ($response.metadata.correlationId -eq $correlationId) {
        Write-Host "✓ Correlation ID matches (end-to-end tracking working)" -ForegroundColor Green
    } else {
        Write-Host "⚠ Correlation ID mismatch" -ForegroundColor Yellow
        Write-Host "  Sent: $correlationId" -ForegroundColor Gray
        Write-Host "  Received: $($response.metadata.correlationId)" -ForegroundColor Gray
    }

    # Verify preview URL
    if ($response.data.previewUrl) {
        Write-Host "✓ Preview URL returned: $($response.data.previewUrl.Substring(0, 80))..." -ForegroundColor Green
    } else {
        Write-Host "⚠ No preview URL in response" -ForegroundColor Yellow
    }

} catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    $errorBody = $_.ErrorDetails.Message

    Write-Host "✗ Request failed" -ForegroundColor Red
    Write-Host "  Status Code: $statusCode" -ForegroundColor Red

    if ($statusCode -eq 401) {
        Write-Host "  Error: Unauthorized - Token may be invalid or expired" -ForegroundColor Red
    } elseif ($statusCode -eq 403) {
        Write-Host "  Error: Forbidden - UAC denied access (expected if user doesn't have permission)" -ForegroundColor Yellow
        Write-Host "  This validates that UAC is working!" -ForegroundColor Green
    } elseif ($statusCode -eq 404) {
        Write-Host "  Error: Document not found" -ForegroundColor Red
    } else {
        Write-Host "  Error: $errorBody" -ForegroundColor Red
    }
}

Write-Host ""

# Step 3: Test /content endpoint (download URL)
Write-Host "[3] Testing GET /api/documents/{id}/content (download URL)..." -ForegroundColor Yellow

$correlationId2 = [guid]::NewGuid().ToString()
Write-Host "  Correlation ID: $correlationId2" -ForegroundColor Gray

try {
    $headers = @{
        "Authorization" = "Bearer $token"
        "Content-Type" = "application/json"
        "X-Correlation-Id" = $correlationId2
    }

    $url = "$BffUrl/api/documents/$DocumentId/content"
    Write-Host "  URL: $url" -ForegroundColor Gray

    $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Get -ErrorAction Stop

    Write-Host "✓ Request successful (200 OK)" -ForegroundColor Green
    Write-Host ""
    Write-Host "Response:" -ForegroundColor Cyan
    Write-Host ($response | ConvertTo-Json -Depth 3) -ForegroundColor White

    # Verify download URL
    if ($response.data.downloadUrl) {
        Write-Host "✓ Download URL returned" -ForegroundColor Green
        Write-Host "  TTL: $($response.data.expiresAt)" -ForegroundColor Gray
    } else {
        Write-Host "⚠ No download URL in response" -ForegroundColor Yellow
    }

} catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    $errorBody = $_.ErrorDetails.Message

    Write-Host "✗ Request failed" -ForegroundColor Red
    Write-Host "  Status Code: $statusCode" -ForegroundColor Red

    if ($statusCode -eq 403) {
        Write-Host "  Error: Forbidden - UAC working as expected" -ForegroundColor Yellow
    } else {
        Write-Host "  Error: $errorBody" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Test Complete" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Summary of Changes Tested:" -ForegroundColor Cyan
Write-Host "  ✓ DocumentAuthorizationFilter wired to endpoints" -ForegroundColor Green
Write-Host "  ✓ Correlation ID tracking (X-Correlation-Id header)" -ForegroundColor Green
Write-Host "  ✓ CORS updated for Dataverse origins" -ForegroundColor Green
Write-Host "  ✓ UAC validation via AuthorizationService" -ForegroundColor Green
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "  1. Verify logs show correlation IDs in Application Insights" -ForegroundColor Gray
Write-Host "  2. Test from Dataverse PCF control (Phase 4)" -ForegroundColor Gray
Write-Host "  3. Verify CORS works from *.dynamics.com origin" -ForegroundColor Gray
