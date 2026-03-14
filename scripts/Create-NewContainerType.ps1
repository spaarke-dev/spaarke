# Create New Container Type for SPE Document Storage
# Owner: BFF API app (performs all server-side Graph operations)
# Creates container type, registers owning app, and optionally creates a test container

param(
    [Parameter(Mandatory)][string]$OwningAppId,
    # Retrieve from Key Vault: az keyvault secret show --vault-name <name> --name <secret> --query value -o tsv
    [Parameter(Mandatory)][string]$OwningAppSecret,
    [Parameter(Mandatory)][string]$TenantId,
    [Parameter(Mandatory)][string]$SharePointDomain,  # e.g., "spaarke.sharepoint.com"
    [string]$DisplayName = "Spaarke Document Storage",
    [string]$Description = "Container type for document storage - owned by BFF API app",
    [switch]$CreateTestContainer = $false
)

Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "CREATE NEW CONTAINER TYPE" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "This will create a NEW container type owned by the BFF API app." -ForegroundColor White
Write-Host ""
Write-Host "Owning App:   $OwningAppId (BFF API)" -ForegroundColor Gray
Write-Host "Display Name: $DisplayName" -ForegroundColor Gray
Write-Host "SP Domain:    $SharePointDomain" -ForegroundColor Gray
Write-Host ""

# Step 1: Get Graph token for owning app
Write-Host "Step 1: Getting Graph API access token..." -ForegroundColor Yellow

$tokenBody = @{
    client_id = $OwningAppId
    client_secret = $OwningAppSecret
    scope = "https://graph.microsoft.com/.default"
    grant_type = "client_credentials"
}

try {
    $graphTokenResponse = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" `
        -Method Post `
        -Body $tokenBody `
        -ErrorAction Stop

    $graphToken = $graphTokenResponse.access_token

    Write-Host "Got Graph access token" -ForegroundColor Green
    Write-Host ""

    # Step 2: Create container type via Graph API
    Write-Host "Step 2: Creating container type via Graph API..." -ForegroundColor Yellow

    $containerTypeBody = @{
        displayName = $DisplayName
        description = $Description
        owningApplicationId = $OwningAppId
    } | ConvertTo-Json

    $headers = @{
        "Authorization" = "Bearer $graphToken"
        "Content-Type" = "application/json"
        "Accept" = "application/json"
    }

    $createUri = "https://graph.microsoft.com/beta/storage/fileStorage/containerTypes"

    Write-Host "Calling: POST $createUri" -ForegroundColor Gray
    Write-Host ""

    $containerType = Invoke-RestMethod -Uri $createUri `
        -Method Post `
        -Headers $headers `
        -Body $containerTypeBody `
        -ErrorAction Stop

    Write-Host "CONTAINER TYPE CREATED!" -ForegroundColor Green
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "NEW CONTAINER TYPE DETAILS" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Container Type ID: $($containerType.id)" -ForegroundColor Yellow
    Write-Host "Display Name:      $($containerType.displayName)" -ForegroundColor White
    Write-Host "Owner App:         $($containerType.owningApplicationId)" -ForegroundColor White
    Write-Host "Created:           $($containerType.createdDateTime)" -ForegroundColor Gray
    Write-Host ""

    $newContainerTypeId = $containerType.id

    # Step 3: Get SharePoint token
    Write-Host "Step 3: Getting SharePoint access token..." -ForegroundColor Yellow

    $spTokenBody = @{
        client_id = $OwningAppId
        client_secret = $OwningAppSecret
        scope = "https://$SharePointDomain/.default"
        grant_type = "client_credentials"
    }

    $spTokenResponse = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" `
        -Method Post `
        -Body $spTokenBody `
        -ErrorAction Stop

    $spToken = $spTokenResponse.access_token

    Write-Host "Got SharePoint access token" -ForegroundColor Green
    Write-Host ""

    # Step 4: Register owning app with container type (full permissions)
    Write-Host "Step 4: Registering owning app with container type..." -ForegroundColor Yellow
    Write-Host "  - Owning App: Full delegated + Full appOnly permissions" -ForegroundColor Gray

    $registrationBody = @{
        value = @(
            @{
                appId = $OwningAppId
                delegated = @("full")
                appOnly = @("full")
            }
        )
    } | ConvertTo-Json -Depth 3

    $spHeaders = @{
        "Authorization" = "Bearer $spToken"
        "Content-Type" = "application/json"
        "Accept" = "application/json"
    }

    $regUri = "https://$SharePointDomain/_api/v2.1/storageContainerTypes/$newContainerTypeId/applicationPermissions"

    Write-Host "Calling: PUT $regUri" -ForegroundColor Gray
    Write-Host ""

    $regResponse = Invoke-RestMethod -Uri $regUri `
        -Method Put `
        -Headers $spHeaders `
        -Body $registrationBody `
        -ErrorAction Stop

    Write-Host "APPLICATION REGISTERED!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Registered Application:" -ForegroundColor Cyan
    foreach ($app in $regResponse.value) {
        Write-Host "  - App ID: $($app.appId)" -ForegroundColor White
        Write-Host "    Delegated: $($app.delegated -join ', ')" -ForegroundColor Green
        Write-Host "    App-Only: $($app.appOnly -join ', ')" -ForegroundColor Gray
        Write-Host ""
    }

    # Step 5: Optionally create a test container
    if ($CreateTestContainer) {
        Write-Host "Step 5: Creating test container..." -ForegroundColor Yellow

        $testContainerBody = @{
            displayName = "$DisplayName - Test"
            description = "Test container for validation"
            containerTypeId = $newContainerTypeId
        } | ConvertTo-Json

        $testContainer = Invoke-RestMethod -Uri "https://graph.microsoft.com/beta/storage/fileStorage/containers" `
            -Method Post `
            -Headers $headers `
            -Body $testContainerBody `
            -ErrorAction Stop

        Write-Host "TEST CONTAINER CREATED!" -ForegroundColor Green
        Write-Host ""
        Write-Host "Container ID:   $($testContainer.id)" -ForegroundColor Yellow
        Write-Host "Display Name:   $($testContainer.displayName)" -ForegroundColor White
        Write-Host "Status:         $($testContainer.status)" -ForegroundColor Green
        Write-Host ""
    }

    # Final summary
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "SUCCESS" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Container Type ID: $newContainerTypeId" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "NEXT STEPS:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "1. Store container type ID in Key Vault:" -ForegroundColor White
    Write-Host "   az keyvault secret set --vault-name <name> --name 'Spe--ContainerTypeId' --value '$newContainerTypeId'" -ForegroundColor Gray
    Write-Host ""
    Write-Host "2. Create a container for the root business unit:" -ForegroundColor White
    Write-Host "   .\New-BusinessUnitContainer.ps1 -ContainerTypeId '$newContainerTypeId' ..." -ForegroundColor Gray
    Write-Host ""
    Write-Host "3. Test file upload via BFF API:" -ForegroundColor White
    Write-Host "   PUT /api/containers/{containerId}/files/test.txt" -ForegroundColor Gray
    Write-Host ""

    # Save configuration
    $config = @{
        ContainerTypeId = $newContainerTypeId
        OwningAppId = $OwningAppId
        CreatedDateTime = $containerType.createdDateTime
    }

    $configPath = Join-Path $PSScriptRoot "new-container-type-config.json"
    $config | ConvertTo-Json -Depth 3 | Out-File -FilePath $configPath -Encoding UTF8

    Write-Host "Configuration saved to: $configPath" -ForegroundColor Gray
    Write-Host ""

} catch {
    Write-Host "ERROR" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red

    if ($_.Exception.Response) {
        try {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $reader.BaseStream.Position = 0
            $reader.DiscardBufferedData()
            $responseBody = $reader.ReadToEnd()
            Write-Host ""
            Write-Host "Response body:" -ForegroundColor Yellow
            Write-Host $responseBody -ForegroundColor Gray

            try {
                $errorJson = $responseBody | ConvertFrom-Json
                Write-Host ""
                Write-Host "Error Details:" -ForegroundColor Yellow
                Write-Host "  Code:    $($errorJson.error.code)" -ForegroundColor Red
                Write-Host "  Message: $($errorJson.error.message)" -ForegroundColor Red
            } catch {
                # Not JSON
            }
        } catch {
            Write-Host "Could not read error response" -ForegroundColor Gray
        }
    }

    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "TROUBLESHOOTING" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Common Issues:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "1. Missing Graph permissions:" -ForegroundColor White
    Write-Host "   - Owning app needs FileStorageContainer.Selected (app-only)" -ForegroundColor Gray
    Write-Host "   - Check Azure Portal > App Registrations > API Permissions" -ForegroundColor Gray
    Write-Host ""
    Write-Host "2. Missing SharePoint permissions:" -ForegroundColor White
    Write-Host "   - Owning app needs Container.Selected (app-only)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "3. Admin consent not granted:" -ForegroundColor White
    Write-Host "   - Check permissions show 'Granted' status" -ForegroundColor Gray
    Write-Host ""
}
