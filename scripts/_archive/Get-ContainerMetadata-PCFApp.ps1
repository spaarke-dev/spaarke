# ARCHIVED 2026-03-14: Used retired PCF app (170c98e1). Use Get-ContainerMetadata.ps1 instead.
# Get container metadata using app credentials

param(
    [Parameter(Mandatory)][string]$ContainerId,
    [Parameter(Mandatory)][string]$ClientId,
    # Retrieve from Key Vault: az keyvault secret show --vault-name <name> --name <secret> --query value -o tsv
    [Parameter(Mandatory)][string]$ClientSecret,
    [Parameter(Mandatory)][string]$TenantId
)

Write-Host "=== Getting Container Metadata ===" -ForegroundColor Cyan
Write-Host ""

# Get token for PCF app (app-only with .default scope)
Write-Host "Getting access token for PCF app..." -ForegroundColor Yellow

$tokenBody = @{
    client_id = $ClientId
    client_secret = $ClientSecret
    scope = "https://graph.microsoft.com/.default"
    grant_type = "client_credentials"
}

try {
    $tokenResponse = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" `
        -Method Post `
        -Body $tokenBody `
        -ErrorAction Stop

    $accessToken = $tokenResponse.access_token

    Write-Host "✅ Got access token" -ForegroundColor Green
    Write-Host ""

    # Query container metadata
    Write-Host "Querying container metadata..." -ForegroundColor Yellow

    $headers = @{
        "Authorization" = "Bearer $accessToken"
        "Accept" = "application/json"
    }

    $uri = "https://graph.microsoft.com/beta/storage/fileStorage/containers/$ContainerId"

    $response = Invoke-RestMethod -Uri $uri -Headers $headers -ErrorAction Stop

    Write-Host "✅ Got container metadata" -ForegroundColor Green
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "CONTAINER METADATA" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Container ID:        $($response.id)" -ForegroundColor White
    Write-Host "Display Name:        $($response.displayName)" -ForegroundColor White
    Write-Host "Container Type ID:   $($response.containerTypeId)" -ForegroundColor Yellow
    Write-Host "Created:             $($response.createdDateTime)" -ForegroundColor Gray
    Write-Host "Status:              $($response.status)" -ForegroundColor Gray
    Write-Host ""

    # Check if containerTypeId is available
    if ($response.containerTypeId) {
        Write-Host "✅ Container Type ID: $($response.containerTypeId)" -ForegroundColor Green
        Write-Host ""
        Write-Host "Now we need to find which application owns this container type." -ForegroundColor Yellow
        Write-Host "This requires SharePoint admin permissions." -ForegroundColor Yellow
    } else {
        Write-Host "⚠️  Container Type ID not returned in response" -ForegroundColor Yellow
        Write-Host "Full response:" -ForegroundColor Gray
        $response | ConvertTo-Json -Depth 5
    }

} catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red

    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $reader.BaseStream.Position = 0
        $reader.DiscardBufferedData()
        $responseBody = $reader.ReadToEnd()
        Write-Host "Response body: $responseBody" -ForegroundColor Gray
    }
}
