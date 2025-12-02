# Create Dataverse records using Web API
# This works with any PAC CLI version

$dataverseUrl = "https://spaarkedev1.api.crm.dynamics.com"

Write-Host "Getting access token from PAC CLI..." -ForegroundColor Cyan

# Get access token from PAC auth
$tokenJson = pac auth list --json | ConvertFrom-Json
$activeProfile = $tokenJson | Where-Object { $_.IsDefault -eq $true } | Select-Object -First 1

if (-not $activeProfile) {
    Write-Host "Error: No active PAC auth profile found" -ForegroundColor Red
    exit 1
}

# Use PAC to get token (this requires calling az or using MSAL)
Write-Host "Using interactive authentication..." -ForegroundColor Yellow

# Get token using Azure CLI (since PAC CLI uses Azure AD)
try {
    $token = az account get-access-token --resource "$dataverseUrl" --query accessToken -o tsv

    if ([string]::IsNullOrEmpty($token)) {
        Write-Host "Error: Failed to get access token" -ForegroundColor Red
        exit 1
    }

    Write-Host "✅ Access token obtained" -ForegroundColor Green
}
catch {
    Write-Host "Error getting token: $_" -ForegroundColor Red
    exit 1
}

# Function to create Dataverse record
function New-DataverseRecord {
    param(
        [string]$EntitySetName,
        [hashtable]$Data,
        [string]$Token
    )

    $headers = @{
        "Authorization" = "Bearer $Token"
        "Content-Type" = "application/json"
        "Accept" = "application/json"
        "OData-MaxVersion" = "4.0"
        "OData-Version" = "4.0"
    }

    $body = $Data | ConvertTo-Json -Depth 10
    $uri = "$dataverseUrl/api/data/v9.2/$EntitySetName"

    try {
        $response = Invoke-RestMethod -Uri $uri -Method Post -Headers $headers -Body $body
        return $response
    }
    catch {
        Write-Host "Error creating record: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Response: $($_.ErrorDetails.Message)" -ForegroundColor Red
        throw
    }
}

# Step 1: Create External Service Config
Write-Host "`n[Task 2.1] Creating External Service Config..." -ForegroundColor Yellow

try {
    $config = @{
        "sprk_name" = "SDAP_BFF_API"
        "sprk_baseurl" = "https://spe-api-dev-67e2xz.azurewebsites.net/api"
        "sprk_isenabled" = $true
        "sprk_authtype" = 1
        "sprk_tenantid" = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
        "sprk_clientid" = "1e40baad-e065-4aea-a8d4-4b7ab273458c"
        "sprk_clientsecret" = "~Ac8Q~JGnsrvNEODvFo8qmtKbgj1PmwmJ6GVUaJj"
        "sprk_scope" = "https://spe-api-dev-67e2xz.azurewebsites.net/.default"
        "sprk_timeout" = 300
        "sprk_retrycount" = 3
        "sprk_retrydelay" = 1000
    }

    $result = New-DataverseRecord -EntitySetName "sprk_externalserviceconfigs" -Data $config -Token $token
    Write-Host "  ✅ External Service Config created" -ForegroundColor Green
}
catch {
    Write-Host "  ⚠️  External Service Config may already exist or error occurred" -ForegroundColor Yellow
}

# Step 2: Create Custom API
Write-Host "`n[Task 2.3] Creating Custom API..." -ForegroundColor Yellow

try {
    $customApi = @{
        "uniquename" = "sprk_GetFilePreviewUrl"
        "name" = "Get File Preview URL"
        "displayname" = "Get File Preview URL"
        "description" = "Server-side proxy for getting SharePoint Embedded preview URLs"
        "bindingtype" = 1
        "boundentitylogicalname" = "sprk_document"
        "isfunction" = $true
        "isprivate" = $false
        "allowedcustomprocessingsteptype" = 0
    }

    $apiResult = New-DataverseRecord -EntitySetName "customapis" -Data $customApi -Token $token
    $customApiId = $apiResult.customapiid

    Write-Host "  ✅ Custom API created with ID: $customApiId" -ForegroundColor Green
    Write-Host "  Save this ID for parameter creation!" -ForegroundColor Cyan

    # Export ID to file for later use
    $customApiId | Out-File "custom-api-id.txt"
}
catch {
    Write-Host "  ⚠️  Custom API may already exist or error occurred" -ForegroundColor Yellow
}

Write-Host "`nCompleted Web API operations!" -ForegroundColor Green
Write-Host "Next: Register plugin assembly using 'pac tool prt'" -ForegroundColor Cyan
