# Get container metadata to find owning application
$containerId = "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"

Write-Host "Getting Graph API token..." -ForegroundColor Gray
$token = az account get-access-token --resource "https://graph.microsoft.com" --query accessToken -o tsv

if (-not $token) {
    Write-Host "Failed to get token" -ForegroundColor Red
    exit 1
}

Write-Host "Querying container metadata..." -ForegroundColor Gray
$headers = @{
    "Authorization" = "Bearer $token"
    "Accept" = "application/json"
}

$uri = "https://graph.microsoft.com/beta/storage/fileStorage/containers/$containerId"

try {
    $response = Invoke-RestMethod -Uri $uri -Headers $headers -ErrorAction Stop

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

    if ($response.containerTypeId) {
        $containerTypeId = $response.containerTypeId
        Write-Host "Container Type ID confirmed: $containerTypeId" -ForegroundColor Green
        Write-Host ""

        # Now query the container type to get owning application
        Write-Host "Querying container type to find owning application..." -ForegroundColor Yellow

        # Try via SharePoint API
        Write-Host "Getting SharePoint token..." -ForegroundColor Gray
        $spToken = az account get-access-token --resource "https://spaarke.sharepoint.com" --query accessToken -o tsv

        if ($spToken) {
            $spUri = "https://spaarke-admin.sharepoint.com/_api/v2.1/storageContainerTypes/$containerTypeId"

            try {
                $spHeaders = @{
                    "Authorization" = "Bearer $spToken"
                    "Accept" = "application/json"
                }

                $spResponse = Invoke-RestMethod -Uri $spUri -Headers $spHeaders -ErrorAction Stop

                Write-Host ""
                Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
                Write-Host "CONTAINER TYPE INFORMATION" -ForegroundColor Cyan
                Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
                Write-Host ""
                Write-Host "Container Type ID:   $($spResponse.id)" -ForegroundColor White
                Write-Host "Display Name:        $($spResponse.displayName)" -ForegroundColor White
                Write-Host "Owning App ID:       $($spResponse.owningApplicationId)" -ForegroundColor Yellow
                Write-Host "Created:             $($spResponse.createdDateTime)" -ForegroundColor Gray
                Write-Host ""

                # Get app details
                if ($spResponse.owningApplicationId) {
                    $owningAppId = $spResponse.owningApplicationId

                    Write-Host "Querying owning application details..." -ForegroundColor Yellow

                    $appJson = az ad app show --id $owningAppId 2>&1

                    if ($LASTEXITCODE -eq 0) {
                        $app = $appJson | ConvertFrom-Json

                        Write-Host ""
                        Write-Host "APPLICATION DETAILS" -ForegroundColor Cyan
                        Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
                        Write-Host "App Display Name:    $($app.displayName)" -ForegroundColor White
                        Write-Host "App ID (Client ID):  $($app.appId)" -ForegroundColor White
                        Write-Host "Object ID:           $($app.id)" -ForegroundColor Gray
                        Write-Host ""
                    }
                }

            } catch {
                Write-Host "Could not query SharePoint container type API: $($_.Exception.Message)" -ForegroundColor Yellow
                Write-Host "Container Type ID is: $containerTypeId" -ForegroundColor Yellow
            }
        }
    }

} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
}
