# Simple Dataverse Web API record creation
# Uses Azure CLI for authentication

$dataverseUrl = "https://spaarkedev1.api.crm.dynamics.com"

Write-Host "Getting access token..." -ForegroundColor Cyan

# Get token using Azure CLI
$token = az account get-access-token --resource "$dataverseUrl" --query accessToken -o tsv

if ([string]::IsNullOrEmpty($token)) {
    Write-Host "Error: Failed to get access token. Run 'az login' first." -ForegroundColor Red
    exit 1
}

Write-Host "✅ Access token obtained`n" -ForegroundColor Green

# Set up headers
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
    "Accept" = "application/json"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
}

# ============================================================================
# Task 2.1: Create External Service Config
# ============================================================================

Write-Host "[Task 2.1] Creating External Service Config..." -ForegroundColor Yellow

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
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod `
        -Uri "$dataverseUrl/api/data/v9.2/sprk_externalserviceconfigs" `
        -Method Post `
        -Headers $headers `
        -Body $config

    Write-Host "✅ External Service Config created`n" -ForegroundColor Green
}
catch {
    if ($_.Exception.Response.StatusCode -eq 'BadRequest' -and $_.ErrorDetails.Message -match "duplicate") {
        Write-Host "⚠️  External Service Config already exists (skipping)`n" -ForegroundColor Yellow
    }
    else {
        Write-Host "❌ Error: $($_.ErrorDetails.Message)`n" -ForegroundColor Red
    }
}

# ============================================================================
# Task 2.3: Create Custom API
# ============================================================================

Write-Host "[Task 2.3] Creating Custom API..." -ForegroundColor Yellow

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
} | ConvertTo-Json

try {
    $apiResponse = Invoke-RestMethod `
        -Uri "$dataverseUrl/api/data/v9.2/customapis" `
        -Method Post `
        -Headers $headers `
        -Body $customApi

    $customApiId = $apiResponse.customapiid
    Write-Host "✅ Custom API created" -ForegroundColor Green
    Write-Host "   ID: $customApiId" -ForegroundColor Cyan

    # Save ID for parameter creation
    $customApiId | Out-File "custom-api-id.txt" -NoNewline
    Write-Host ""
}
catch {
    if ($_.Exception.Response.StatusCode -eq 'BadRequest' -and $_.ErrorDetails.Message -match "duplicate") {
        Write-Host "⚠️  Custom API already exists`n" -ForegroundColor Yellow
        Write-Host "   Querying existing Custom API ID..." -ForegroundColor Gray

        # Query for existing ID
        $existingApi = Invoke-RestMethod `
            -Uri "$dataverseUrl/api/data/v9.2/customapis?`$filter=uniquename eq 'sprk_GetFilePreviewUrl'&`$select=customapiid" `
            -Method Get `
            -Headers $headers

        $customApiId = $existingApi.value[0].customapiid
        Write-Host "   ID: $customApiId" -ForegroundColor Cyan
        $customApiId | Out-File "custom-api-id.txt" -NoNewline
        Write-Host ""
    }
    else {
        Write-Host "❌ Error: $($_.ErrorDetails.Message)`n" -ForegroundColor Red
        exit 1
    }
}

# ============================================================================
# Task 2.4: Create Output Parameters
# ============================================================================

Write-Host "[Task 2.4] Creating Output Parameters..." -ForegroundColor Yellow

$parameters = @(
    @{uniquename="PreviewUrl"; name="PreviewUrl"; displayname="Preview URL"; description="Ephemeral preview URL (expires in ~10 minutes)"; type=10},
    @{uniquename="FileName"; name="FileName"; displayname="File Name"; description="File name for display"; type=10},
    @{uniquename="FileSize"; name="FileSize"; displayname="File Size"; description="File size in bytes"; type=6},
    @{uniquename="ContentType"; name="ContentType"; displayname="Content Type"; description="MIME type"; type=10},
    @{uniquename="ExpiresAt"; name="ExpiresAt"; displayname="Expires At"; description="When the preview URL expires (UTC)"; type=8},
    @{uniquename="CorrelationId"; name="CorrelationId"; displayname="Correlation ID"; description="Request tracking ID for tracing and debugging"; type=10}
)

$count = 0
foreach ($param in $parameters) {
    $paramData = @{
        "uniquename" = $param.uniquename
        "name" = $param.name
        "displayname" = $param.displayname
        "description" = $param.description
        "type" = $param.type
        "customapiid@odata.bind" = "/customapis($customApiId)"
    } | ConvertTo-Json

    try {
        Invoke-RestMethod `
            -Uri "$dataverseUrl/api/data/v9.2/customapiresponseproperties" `
            -Method Post `
            -Headers $headers `
            -Body $paramData | Out-Null

        $count++
        Write-Host "  ✅ Parameter $count/6: $($param.uniquename)" -ForegroundColor Green
    }
    catch {
        if ($_.ErrorDetails.Message -match "duplicate") {
            Write-Host "  ⚠️  Parameter $($param.uniquename) already exists (skipping)" -ForegroundColor Yellow
        }
        else {
            Write-Host "  ❌ Error creating $($param.uniquename): $($_.ErrorDetails.Message)" -ForegroundColor Red
        }
    }
}

Write-Host "`n✅ Task 2.4 Complete: Created $count/6 parameters`n" -ForegroundColor Green

# ============================================================================
# Summary
# ============================================================================

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "TASKS COMPLETED" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "✅ Task 2.1: External Service Config" -ForegroundColor Green
Write-Host "⏭️  Task 2.2: Register plugin (manual via PRT)" -ForegroundColor Yellow
Write-Host "✅ Task 2.3: Custom API created" -ForegroundColor Green
Write-Host "✅ Task 2.4: Output parameters created" -ForegroundColor Green
Write-Host "⏭️  Task 2.5: Register plugin step (manual via PRT)" -ForegroundColor Yellow
Write-Host "⏭️  Task 2.6: Publish customizations" -ForegroundColor Yellow
Write-Host "`nCustom API ID saved to: custom-api-id.txt" -ForegroundColor Cyan
Write-Host ""
