# Create New Container Type for OBO Flow
# Owner: PCF App (so it can register BFF API immediately)

param(
    [string]$OwningAppId = "170c98e1-d486-4355-bcbe-170454e0207c",
    [string]$OwningAppSecret = "~Ac8Q~JGnsrvNEODvFo8qmtKbgj1PmwmJ6GVUaJj",
    [string]$BffApiAppId = "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    [string]$TenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    [string]$DisplayName = "Spaarke Document Storage (OBO)",
    [string]$Description = "Container type for document storage via OBO flow - owned by PCF app"
)

Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "CREATE NEW CONTAINER TYPE" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "This will create a NEW container type owned by PCF app," -ForegroundColor White
Write-Host "allowing immediate registration of BFF API." -ForegroundColor White
Write-Host ""
Write-Host "Owning App:  $OwningAppId (PCF)" -ForegroundColor Gray
Write-Host "Guest App:   $BffApiAppId (BFF API)" -ForegroundColor Gray
Write-Host "Display Name: $DisplayName" -ForegroundColor Gray
Write-Host ""

# Step 1: Get Graph token for PCF app
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

    Write-Host "✅ Got Graph access token" -ForegroundColor Green
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
    Write-Host "Body: $containerTypeBody" -ForegroundColor Gray
    Write-Host ""

    $containerType = Invoke-RestMethod -Uri $createUri `
        -Method Post `
        -Headers $headers `
        -Body $containerTypeBody `
        -ErrorAction Stop

    Write-Host "✅ CONTAINER TYPE CREATED!" -ForegroundColor Green
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
        scope = "https://spaarke.sharepoint.com/.default"
        grant_type = "client_credentials"
    }

    $spTokenResponse = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" `
        -Method Post `
        -Body $spTokenBody `
        -ErrorAction Stop

    $spToken = $spTokenResponse.access_token

    Write-Host "✅ Got SharePoint access token" -ForegroundColor Green
    Write-Host ""

    # Step 4: Register both PCF app and BFF API with new container type
    Write-Host "Step 4: Registering applications with new container type..." -ForegroundColor Yellow
    Write-Host "  - Owning App (PCF): Full permissions" -ForegroundColor Gray
    Write-Host "  - Guest App (BFF API): WriteContent delegated" -ForegroundColor Gray

    $registrationBody = @{
        value = @(
            @{
                appId = $OwningAppId
                delegated = @("full")
                appOnly = @("full")
            },
            @{
                appId = $BffApiAppId
                delegated = @("WriteContent", "ReadContent")
                appOnly = @()
            }
        )
    } | ConvertTo-Json -Depth 3

    $spHeaders = @{
        "Authorization" = "Bearer $spToken"
        "Content-Type" = "application/json"
        "Accept" = "application/json"
    }

    $regUri = "https://spaarke.sharepoint.com/_api/v2.1/storageContainerTypes/$newContainerTypeId/applicationPermissions"

    Write-Host "Calling: PUT $regUri" -ForegroundColor Gray
    Write-Host ""

    $regResponse = Invoke-RestMethod -Uri $regUri `
        -Method Put `
        -Headers $spHeaders `
        -Body $registrationBody `
        -ErrorAction Stop

    Write-Host "✅ APPLICATIONS REGISTERED!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Registered Applications:" -ForegroundColor Cyan
    foreach ($app in $regResponse.value) {
        Write-Host "  - App ID: $($app.appId)" -ForegroundColor White
        Write-Host "    Delegated: $($app.delegated -join ', ')" -ForegroundColor Green
        Write-Host "    App-Only: $($app.appOnly -join ', ')" -ForegroundColor Gray
        Write-Host ""
    }

    # Step 5: Create a test container
    Write-Host "Step 5: Creating test container..." -ForegroundColor Yellow

    $testContainerBody = @{
        displayName = "Spaarke Inc (New)"
        description = "Test container for OBO flow"
        containerTypeId = $newContainerTypeId
    } | ConvertTo-Json

    $containerUri = "https://graph.microsoft.com/beta/storage/fileStorage/containers"

    $testContainer = Invoke-RestMethod -Uri $containerUri `
        -Method Post `
        -Headers $headers `
        -Body $testContainerBody `
        -ErrorAction Stop

    Write-Host "✅ TEST CONTAINER CREATED!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Container ID:   $($testContainer.id)" -ForegroundColor Yellow
    Write-Host "Display Name:   $($testContainer.displayName)" -ForegroundColor White
    Write-Host "Status:         $($testContainer.status)" -ForegroundColor Green
    Write-Host ""

    # Final summary
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "SUCCESS - READY TO TEST!" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "NEW CONTAINER TYPE ID:" -ForegroundColor Yellow
    Write-Host "  $newContainerTypeId" -ForegroundColor White
    Write-Host ""
    Write-Host "NEW CONTAINER ID:" -ForegroundColor Yellow
    Write-Host "  $($testContainer.id)" -ForegroundColor White
    Write-Host ""
    Write-Host "NEXT STEPS:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "1. Update PCF Control configuration (if needed):" -ForegroundColor White
    Write-Host "   - Use new container ID in Quick Create form" -ForegroundColor Gray
    Write-Host ""
    Write-Host "2. Test OBO file upload:" -ForegroundColor White
    Write-Host "   PUT /api/obo/containers/$($testContainer.id)/files/test.txt" -ForegroundColor Gray
    Write-Host ""
    Write-Host "3. Expected result: HTTP 200 OK (no 403!)" -ForegroundColor White
    Write-Host ""
    Write-Host "4. Grant yourself permissions on new container:" -ForegroundColor White
    Write-Host "   POST /containers/$($testContainer.id)/permissions" -ForegroundColor Gray
    Write-Host "   (Add yourself as owner)" -ForegroundColor Gray
    Write-Host ""

    # Save configuration
    $config = @{
        ContainerTypeId = $newContainerTypeId
        ContainerId = $testContainer.id
        OwningAppId = $OwningAppId
        BffApiAppId = $BffApiAppId
        CreatedDateTime = $containerType.createdDateTime
    }

    $configPath = "c:\code_files\spaarke\scripts\new-container-type-config.json"
    $config | ConvertTo-Json -Depth 3 | Out-File -FilePath $configPath -Encoding UTF8

    Write-Host "Configuration saved to: $configPath" -ForegroundColor Gray
    Write-Host ""

} catch {
    Write-Host "❌ ERROR" -ForegroundColor Red
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
    Write-Host "   - PCF app needs FileStorageContainer.Selected (app-only)" -ForegroundColor Gray
    Write-Host "   - Check Azure Portal > App Registrations > API Permissions" -ForegroundColor Gray
    Write-Host ""
    Write-Host "2. Missing SharePoint permissions:" -ForegroundColor White
    Write-Host "   - PCF app needs Container.Selected (app-only)" -ForegroundColor Gray
    Write-Host "   - Should already have this based on earlier confirmation" -ForegroundColor Gray
    Write-Host ""
    Write-Host "3. Admin consent not granted:" -ForegroundColor White
    Write-Host "   - Check permissions show 'Granted' status" -ForegroundColor Gray
    Write-Host ""
}
