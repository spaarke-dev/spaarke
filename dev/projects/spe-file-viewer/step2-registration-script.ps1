# Step 2: Custom API Registration Script
# This script automates the registration of the GetFilePreviewUrl Custom API

#Requires -Modules Microsoft.Xrm.Data.PowerShell

# ============================================================================
# CONFIGURATION
# ============================================================================

# Azure AD / Service Principal Configuration
$config = @{
    TenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
    ClientId = "1e40baad-e065-4aea-a8d4-4b7ab273458c"
    # Retrieved from KeyVault: spaarke-spekvcert/BFF-API-ClientSecret
    ClientSecret = "~Ac8Q~JGnsrvNEODvFo8qmtKbgj1PmwmJ6GVUaJj"

    # Environment Configuration
    DataverseUrl = "https://spaarkedev1.api.crm.dynamics.com"
    BffApiUrl = "https://spe-api-dev-67e2xz.azurewebsites.net"
    BffApiScope = "https://spe-api-dev-67e2xz.azurewebsites.net/.default"

    # Plugin Assembly Path
    PluginDllPath = "c:\code_files\spaarke\src\dataverse\Spaarke.CustomApiProxy\Plugins\Spaarke.Dataverse.CustomApiProxy\bin\Release\net462\Spaarke.Dataverse.CustomApiProxy.dll"
}

# ============================================================================
# VALIDATE PREREQUISITES
# ============================================================================

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Step 2: Custom API Registration" -ForegroundColor Cyan
Write-Host "============================================`n" -ForegroundColor Cyan

# Check if client secret is set
if ([string]::IsNullOrEmpty($config.ClientSecret)) {
    Write-Host "❌ Client Secret not set!" -ForegroundColor Red
    Write-Host "`nPlease retrieve the client secret from KeyVault and paste it in this script:" -ForegroundColor Yellow
    Write-Host "  az keyvault secret show --vault-name spaarke-spekvcert --name BFF-API-ClientSecret --query `"value`" -o tsv`n" -ForegroundColor Gray
    exit 1
}

# Check if plugin DLL exists
if (-not (Test-Path $config.PluginDllPath)) {
    Write-Host "❌ Plugin DLL not found at: $($config.PluginDllPath)" -ForegroundColor Red
    Write-Host "`nPlease build the plugin first (Step 1, Task 1.4)`n" -ForegroundColor Yellow
    exit 1
}

Write-Host "✅ Prerequisites validated" -ForegroundColor Green
Write-Host "  - Client secret: [REDACTED]" -ForegroundColor Gray
Write-Host "  - Plugin DLL: $($config.PluginDllPath)" -ForegroundColor Gray
Write-Host ""

# ============================================================================
# TASK 2.1: CREATE EXTERNAL SERVICE CONFIG
# ============================================================================

Write-Host "[Task 2.1] Creating External Service Config..." -ForegroundColor Yellow

try {
    # Connect to Dataverse (interactive login)
    Write-Host "  Connecting to Dataverse (SPAARKE DEV 1)..." -ForegroundColor Gray
    $conn = Get-CrmConnection -InteractiveMode

    # Check if config already exists
    $existingConfig = Get-CrmRecords -conn $conn -EntityLogicalName "sprk_externalserviceconfig" `
        -FilterAttribute "sprk_name" -FilterOperator "eq" -FilterValue "SDAP_BFF_API"

    if ($existingConfig.CrmRecords.Count -gt 0) {
        Write-Host "  ⚠️  External Service Config 'SDAP_BFF_API' already exists (ID: $($existingConfig.CrmRecords[0].sprk_externalserviceconfigid))" -ForegroundColor Yellow
        Write-Host "  Skipping creation. To recreate, delete the existing record first.`n" -ForegroundColor Gray
    }
    else {
        # Create new config
        $serviceConfig = @{
            "sprk_name" = "SDAP_BFF_API"
            "sprk_baseurl" = "$($config.BffApiUrl)/api"
            "sprk_isenabled" = $true
            "sprk_authtype" = 1  # ClientCredentials (app-only)
            "sprk_tenantid" = $config.TenantId
            "sprk_clientid" = $config.ClientId
            "sprk_clientsecret" = $config.ClientSecret
            "sprk_scope" = $config.BffApiScope
            "sprk_timeout" = 300  # 5 minutes
            "sprk_retrycount" = 3
            "sprk_retrydelay" = 1000  # 1 second
        }

        $configId = New-CrmRecord -conn $conn -EntityLogicalName "sprk_externalserviceconfig" -Fields $serviceConfig
        Write-Host "  ✅ External Service Config created (ID: $configId)`n" -ForegroundColor Green
    }
}
catch {
    Write-Host "  ❌ Error creating External Service Config: $($_.Exception.Message)`n" -ForegroundColor Red
    exit 1
}

# ============================================================================
# TASK 2.2: REGISTER PLUGIN ASSEMBLY (MANUAL)
# ============================================================================

Write-Host "[Task 2.2] Register Plugin Assembly" -ForegroundColor Yellow
Write-Host "  ⚠️  This step must be done manually using Plugin Registration Tool (PRT)" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Steps:" -ForegroundColor Gray
Write-Host "    1. Launch PRT: pac tool prt" -ForegroundColor Gray
Write-Host "    2. Connect to SPAARKE DEV 1 environment" -ForegroundColor Gray
Write-Host "    3. Register → Register New Assembly" -ForegroundColor Gray
Write-Host "    4. Browse to: $($config.PluginDllPath)" -ForegroundColor Gray
Write-Host "    5. Isolation Mode: Sandbox" -ForegroundColor Gray
Write-Host "    6. Location: Database" -ForegroundColor Gray
Write-Host "    7. Click 'Register Selected Plugins'" -ForegroundColor Gray
Write-Host ""
Write-Host "  Press Enter when plugin assembly is registered..." -ForegroundColor Cyan
Read-Host

# ============================================================================
# TASK 2.3: CREATE CUSTOM API
# ============================================================================

Write-Host "[Task 2.3] Creating Custom API..." -ForegroundColor Yellow

try {
    # Check if Custom API already exists
    $existingApi = Get-CrmRecords -conn $conn -EntityLogicalName "customapi" `
        -FilterAttribute "uniquename" -FilterOperator "eq" -FilterValue "sprk_GetFilePreviewUrl"

    if ($existingApi.CrmRecords.Count -gt 0) {
        $customApiId = $existingApi.CrmRecords[0].customapiid
        Write-Host "  ⚠️  Custom API 'sprk_GetFilePreviewUrl' already exists (ID: $customApiId)" -ForegroundColor Yellow
        Write-Host "  Using existing Custom API for parameter creation.`n" -ForegroundColor Gray
    }
    else {
        # Create Custom API
        $customApi = @{
            "uniquename" = "sprk_GetFilePreviewUrl"
            "name" = "Get File Preview URL"
            "displayname" = "Get File Preview URL"
            "description" = "Server-side proxy for getting SharePoint Embedded preview URLs"
            "bindingtype" = 1  # Entity
            "boundentitylogicalname" = "sprk_document"
            "isfunction" = $true
            "isprivate" = $false
            "allowedcustomprocessingsteptype" = 0  # None (sync only)
        }

        $customApiId = New-CrmRecord -conn $conn -EntityLogicalName "customapi" -Fields $customApi
        Write-Host "  ✅ Custom API created (ID: $customApiId)`n" -ForegroundColor Green
    }
}
catch {
    Write-Host "  ❌ Error creating Custom API: $($_.Exception.Message)`n" -ForegroundColor Red
    exit 1
}

# ============================================================================
# TASK 2.4: CREATE CUSTOM API OUTPUT PARAMETERS
# ============================================================================

Write-Host "[Task 2.4] Creating Custom API Output Parameters..." -ForegroundColor Yellow

$parameters = @(
    @{ UniqueName = "PreviewUrl"; DisplayName = "Preview URL"; Description = "Ephemeral preview URL (expires in ~10 minutes)"; Type = 10 },
    @{ UniqueName = "FileName"; DisplayName = "File Name"; Description = "File name for display"; Type = 10 },
    @{ UniqueName = "FileSize"; DisplayName = "File Size"; Description = "File size in bytes"; Type = 6 },
    @{ UniqueName = "ContentType"; DisplayName = "Content Type"; Description = "MIME type"; Type = 10 },
    @{ UniqueName = "ExpiresAt"; DisplayName = "Expires At"; Description = "When the preview URL expires (UTC)"; Type = 8 },
    @{ UniqueName = "CorrelationId"; DisplayName = "Correlation ID"; Description = "Request tracking ID for tracing and debugging"; Type = 10 }
)

foreach ($param in $parameters) {
    try {
        # Check if parameter already exists
        $existingParam = Get-CrmRecords -conn $conn -EntityLogicalName "customapiresponseproperty" `
            -FilterAttribute "uniquename" -FilterOperator "eq" -FilterValue $param.UniqueName `
            -Fields "customapiid"

        $exists = $false
        foreach ($record in $existingParam.CrmRecords) {
            if ($record.customapiid_Property.Value.Id -eq $customApiId) {
                $exists = $true
                break
            }
        }

        if ($exists) {
            Write-Host "  ⚠️  Parameter '$($param.UniqueName)' already exists, skipping" -ForegroundColor Yellow
        }
        else {
            $paramFields = @{
                "uniquename" = $param.UniqueName
                "name" = $param.UniqueName
                "displayname" = $param.DisplayName
                "description" = $param.Description
                "type" = $param.Type
                "customapiid@odata.bind" = "/customapis($customApiId)"
            }

            $paramId = New-CrmRecord -conn $conn -EntityLogicalName "customapiresponseproperty" -Fields $paramFields
            Write-Host "  ✅ Created parameter: $($param.UniqueName)" -ForegroundColor Green
        }
    }
    catch {
        Write-Host "  ❌ Error creating parameter '$($param.UniqueName)': $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""

# ============================================================================
# TASK 2.5: REGISTER PLUGIN STEP (MANUAL)
# ============================================================================

Write-Host "[Task 2.5] Register Plugin Step" -ForegroundColor Yellow
Write-Host "  ⚠️  This step must be done manually using Plugin Registration Tool (PRT)" -ForegroundColor Yellow
Write-Host ""
Write-Host "  Steps:" -ForegroundColor Gray
Write-Host "    1. In PRT, expand 'Spaarke.Dataverse.CustomApiProxy' assembly" -ForegroundColor Gray
Write-Host "    2. Right-click 'GetFilePreviewUrlPlugin' → 'Register New Step'" -ForegroundColor Gray
Write-Host "    3. Message: sprk_GetFilePreviewUrl" -ForegroundColor Gray
Write-Host "    4. Primary Entity: sprk_document" -ForegroundColor Gray
Write-Host "    5. Event Pipeline Stage: Main Operation (30)" -ForegroundColor Gray
Write-Host "    6. Execution Mode: Synchronous" -ForegroundColor Gray
Write-Host "    7. Click 'Register New Step'" -ForegroundColor Gray
Write-Host ""
Write-Host "  Press Enter when plugin step is registered..." -ForegroundColor Cyan
Read-Host

# ============================================================================
# TASK 2.6: PUBLISH CUSTOMIZATIONS
# ============================================================================

Write-Host "[Task 2.6] Publishing Customizations..." -ForegroundColor Yellow

try {
    Publish-CrmAllCustomization -conn $conn
    Write-Host "  ✅ All customizations published!`n" -ForegroundColor Green
}
catch {
    Write-Host "  ❌ Error publishing customizations: $($_.Exception.Message)`n" -ForegroundColor Red
    exit 1
}

# ============================================================================
# VALIDATION
# ============================================================================

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "VALIDATION CHECKLIST" -ForegroundColor Cyan
Write-Host "============================================`n" -ForegroundColor Cyan

# Verify External Service Config
$config = Get-CrmRecords -conn $conn -EntityLogicalName "sprk_externalserviceconfig" `
    -FilterAttribute "sprk_name" -FilterOperator "eq" -FilterValue "SDAP_BFF_API"
$configCheck = $config.CrmRecords.Count -gt 0
Write-Host "  [$(if($configCheck){'✅'}else{'❌'})] External Service Config 'SDAP_BFF_API' exists" -ForegroundColor $(if($configCheck){'Green'}else{'Red'})

# Verify Custom API
$api = Get-CrmRecords -conn $conn -EntityLogicalName "customapi" `
    -FilterAttribute "uniquename" -FilterOperator "eq" -FilterValue "sprk_GetFilePreviewUrl"
$apiCheck = $api.CrmRecords.Count -gt 0
Write-Host "  [$(if($apiCheck){'✅'}else{'❌'})] Custom API 'sprk_GetFilePreviewUrl' exists" -ForegroundColor $(if($apiCheck){'Green'}else{'Red'})

# Verify Output Parameters
if ($apiCheck) {
    $params = Get-CrmRecords -conn $conn -EntityLogicalName "customapiresponseproperty" `
        -FilterAttribute "customapiid" -FilterOperator "eq" -FilterValue $api.CrmRecords[0].customapiid
    $paramCount = $params.CrmRecords.Count
    $paramCheck = $paramCount -eq 6
    Write-Host "  [$(if($paramCheck){'✅'}else{'❌'})] Output Parameters: $paramCount/6" -ForegroundColor $(if($paramCheck){'Green'}else{'Yellow'})
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "NEXT STEPS" -ForegroundColor Cyan
Write-Host "============================================`n" -ForegroundColor Cyan
Write-Host "1. Test the Custom API using browser console (see STEP-2 documentation)" -ForegroundColor Gray
Write-Host "2. Proceed to Step 3: PCF Control Development`n" -ForegroundColor Gray

Write-Host "Script completed!" -ForegroundColor Green
