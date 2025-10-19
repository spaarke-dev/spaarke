#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Quick utility to test SDAP BFF API endpoints and get Container/Drive IDs

.DESCRIPTION
    Calls Spe.Bff.Api endpoints to:
    - List containers
    - Get drive IDs from containers
    - Test file operations
    Uses pac auth token for authentication

.EXAMPLE
    .\Test-SdapBffApi.ps1 -Action ListContainers
    .\Test-SdapBffApi.ps1 -Action GetDrive -ContainerId "abc123"
#>

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet('ListContainers', 'GetDrive', 'Ping', 'ListFiles', 'UserInfo')]
    [string]$Action = 'ListContainers',

    [Parameter(Mandatory=$false)]
    [string]$ContainerId,

    [Parameter(Mandatory=$false)]
    [string]$DriveId,

    [Parameter(Mandatory=$false)]
    [string]$ApiBaseUrl = 'https://spe-api-dev-67e2xz.azurewebsites.net',

    [Parameter(Mandatory=$false)]
    [string]$ContainerTypeId = '8a6ce34c-6055-4681-8f87-2f4f9f921c06'
)

# Get auth token from pac CLI
Write-Host "Getting auth token from pac CLI..." -ForegroundColor Cyan

# Run pac auth token and capture output
$tokenOutput = & pac auth token 2>&1
$token = ($tokenOutput | Out-String).Trim()

if ([string]::IsNullOrWhiteSpace($token) -or $token.Contains("Error")) {
    Write-Error "Failed to get token from pac CLI. Make sure you're authenticated with 'pac auth create'"
    Write-Host "Token output: $tokenOutput" -ForegroundColor Yellow
    exit 1
}

Write-Host "Token obtained (length: $($token.Length))" -ForegroundColor Green

# Prepare headers
$headers = @{
    'Authorization' = "Bearer $token"
    'Accept' = 'application/json'
    'Content-Type' = 'application/json'
}

function Invoke-ApiRequest {
    param(
        [string]$Url,
        [string]$Method = 'GET',
        [object]$Body = $null
    )

    Write-Host "`nCalling: $Method $Url" -ForegroundColor Yellow

    try {
        $params = @{
            Uri = $Url
            Method = $Method
            Headers = $headers
            ErrorAction = 'Stop'
        }

        if ($Body) {
            $params['Body'] = ($Body | ConvertTo-Json -Depth 10)
        }

        $response = Invoke-RestMethod @params

        Write-Host "✓ Success!" -ForegroundColor Green
        return $response

    } catch {
        Write-Host "✗ Error: $($_.Exception.Message)" -ForegroundColor Red

        if ($_.ErrorDetails.Message) {
            Write-Host "Details: $($_.ErrorDetails.Message)" -ForegroundColor Red
        }

        return $null
    }
}

function Format-Output {
    param([object]$Data)

    if ($null -eq $Data) {
        return
    }

    Write-Host "`n=== RESPONSE ===" -ForegroundColor Cyan
    $Data | ConvertTo-Json -Depth 10 | Write-Host
    Write-Host "================`n" -ForegroundColor Cyan
}

# Execute requested action
switch ($Action) {
    'Ping' {
        $response = Invoke-ApiRequest -Url "$ApiBaseUrl/ping"
        Format-Output $response
    }

    'UserInfo' {
        $response = Invoke-ApiRequest -Url "$ApiBaseUrl/api/me"
        Format-Output $response

        if ($response) {
            Write-Host "`nUser: $($response.displayName) ($($response.userPrincipalName))" -ForegroundColor Green
        }
    }

    'ListContainers' {
        Write-Host "`nListing containers for ContainerTypeId: $ContainerTypeId" -ForegroundColor Cyan
        $url = "$ApiBaseUrl/api/containers?containerTypeId=$ContainerTypeId"
        $response = Invoke-ApiRequest -Url $url

        if ($response -and $response.value) {
            Write-Host "`n=== CONTAINERS ===" -ForegroundColor Cyan
            Write-Host "Found $($response.value.Count) container(s):`n" -ForegroundColor Green

            foreach ($container in $response.value) {
                Write-Host "  Container ID: $($container.id)" -ForegroundColor Yellow
                Write-Host "  Display Name: $($container.displayName)" -ForegroundColor White
                Write-Host "  Description:  $($container.description)" -ForegroundColor Gray
                Write-Host "  Created:      $($container.createdDateTime)" -ForegroundColor Gray
                Write-Host ""

                # Automatically get drive for each container
                Write-Host "  Getting Drive ID..." -ForegroundColor Cyan
                $driveUrl = "$ApiBaseUrl/api/containers/$($container.id)/drive"
                $driveResponse = Invoke-ApiRequest -Url $driveUrl

                if ($driveResponse -and $driveResponse.id) {
                    Write-Host "  → Drive ID: $($driveResponse.id)" -ForegroundColor Green
                } else {
                    Write-Host "  → Drive ID: Not available" -ForegroundColor Red
                }
                Write-Host "  " + ("-" * 60)
                Write-Host ""
            }

            # Output for easy copy-paste
            if ($response.value.Count -gt 0) {
                $firstContainer = $response.value[0]
                Write-Host "`n=== QUICK COPY (First Container) ===" -ForegroundColor Magenta
                Write-Host "Container ID: $($firstContainer.id)"

                $driveUrl = "$ApiBaseUrl/api/containers/$($firstContainer.id)/drive"
                $driveResponse = Invoke-ApiRequest -Url $driveUrl
                if ($driveResponse -and $driveResponse.id) {
                    Write-Host "Drive ID:     $($driveResponse.id)"
                    Write-Host "`nUse these values in your Quick Create form:" -ForegroundColor Cyan
                    Write-Host "  sprk_containerid: Select container '$($firstContainer.displayName)'"
                    Write-Host "  sprk_graphdriveid: $($driveResponse.id)"
                }
            }
        } else {
            Write-Host "`nNo containers found or error occurred" -ForegroundColor Red
        }
    }

    'GetDrive' {
        if ([string]::IsNullOrWhiteSpace($ContainerId)) {
            Write-Error "ContainerId parameter is required for GetDrive action"
            exit 1
        }

        $url = "$ApiBaseUrl/api/containers/$ContainerId/drive"
        $response = Invoke-ApiRequest -Url $url
        Format-Output $response

        if ($response -and $response.id) {
            Write-Host "`nDrive ID for container '$ContainerId': $($response.id)" -ForegroundColor Green
        }
    }

    'ListFiles' {
        if ([string]::IsNullOrWhiteSpace($DriveId)) {
            Write-Error "DriveId parameter is required for ListFiles action"
            exit 1
        }

        $url = "$ApiBaseUrl/api/drives/$DriveId/children"
        $response = Invoke-ApiRequest -Url $url
        Format-Output $response

        if ($response -and $response.value) {
            Write-Host "`nFound $($response.value.Count) item(s) in drive:" -ForegroundColor Green
            foreach ($item in $response.value) {
                Write-Host "  - $($item.name) (ID: $($item.id))" -ForegroundColor White
            }
        }
    }
}

Write-Host "`n✓ Script complete" -ForegroundColor Green
