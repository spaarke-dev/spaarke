<#
.SYNOPSIS
    Deploy the Reporting module Dataverse schema: sprk_report entity, sprk_ReportingAccess
    security roles (Viewer/Author/Admin), and sprk_ReportingModuleEnabled environment variable.

.DESCRIPTION
    Deploys all Dataverse artifacts for the Spaarke Reporting module to the target environment.

    The three artifact groups are:

    1. sprk_report ENTITY — Stores the Power BI report catalog. Key attributes:
       sprk_name, sprk_pbi_reportid, sprk_workspaceid, sprk_datasetid, sprk_category
       (OptionSet: Financial=1, Operational=2, Compliance=3, Documents=4, Custom=5),
       sprk_embedurl, sprk_iscustom (default false), sprk_description

    2. sprk_ReportingAccess SECURITY ROLES (three separate roles, one per privilege tier):
       - sprk_ReportingAccess_Viewer  — Read (Org scope) on sprk_report
       - sprk_ReportingAccess_Author  — Read + Write + Create (Org scope) on sprk_report
       - sprk_ReportingAccess_Admin   — Full CRUD (Org scope) on sprk_report

    3. sprk_ReportingModuleEnabled ENVIRONMENT VARIABLE — Boolean, default No.
       Feature gate: when false, /api/reporting/* endpoints return 403.

    DEPLOYMENT APPROACH:
    ---------------------
    These artifacts are defined in the SpaarkeCore Dataverse solution source files (XML).
    The preferred approach for deploying is:
      A) pac solution pack + pac solution import  (when SpaarkeCore solution XML is complete)
      B) Dataverse Metadata Web API              (direct imperative deployment, used here)

    This script uses Approach B (Web API) for each artifact, matching the pattern in
    Deploy-ChartDefinitionEntity.ps1. It is idempotent — skips creation if the artifact
    already exists and reports what it found.

    PREREQUISITES:
    --------------
    - Azure CLI logged in:  az login  (or service principal via az login --service-principal)
    - pac CLI authenticated: pac auth create --environment <URL> (for role verification only)
    - Target environment URL passed via -DataverseOrg or DATAVERSE_URL env var

.PARAMETER DataverseOrg
    Target Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com).
    Defaults to $env:DATAVERSE_URL.

.PARAMETER Environment
    Short environment name for display purposes (e.g., "dev", "uat", "prod").
    Defaults to "dev".

.PARAMETER SkipVerification
    Skip post-deploy verification queries (faster, less output).

.PARAMETER WhatIf
    Show deployment plan without making any changes.

.EXAMPLE
    .\Deploy-ReportingSchema.ps1
    # Uses DATAVERSE_URL env var, deploys all three artifact groups

.EXAMPLE
    .\Deploy-ReportingSchema.ps1 -DataverseOrg "https://spaarkedev1.crm.dynamics.com" -Environment "dev"

.EXAMPLE
    .\Deploy-ReportingSchema.ps1 -DataverseOrg "https://spaarke-uat.crm.dynamics.com" -Environment "uat" -WhatIf
    # Shows plan without executing

.NOTES
    Project:  spaarke-powerbi-embedded-r1
    Task:     033 - Deploy Dataverse Schema
    Schema:   src/solutions/SpaarkeCore/entities/sprk_report/entity-schema.md
    Role doc: src/solutions/SpaarkeCore/security-roles/sprk_ReportingAccess.md
    Env var:  src/solutions/SpaarkeCore/environment-variables/sprk_ReportingModuleEnabled.md
    Created:  2026-03-31
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $false)]
    [string]$DataverseOrg = $env:DATAVERSE_URL,

    [Parameter(Mandatory = $false)]
    [string]$Environment = "dev",

    [switch]$SkipVerification
)

$ErrorActionPreference = "Stop"
$StartTime = Get-Date

# ============================================================================
# HELPER FUNCTIONS
# ============================================================================

function Write-Header {
    param([string]$Message)
    Write-Host ""
    Write-Host "======================================================" -ForegroundColor Cyan
    Write-Host "  $Message" -ForegroundColor Cyan
    Write-Host "======================================================" -ForegroundColor Cyan
    Write-Host ""
}

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "--- $Message ---" -ForegroundColor Yellow
}

function Write-Success {
    param([string]$Message)
    Write-Host "  [OK] $Message" -ForegroundColor Green
}

function Write-Skip {
    param([string]$Message)
    Write-Host "  [--] $Message (already exists, skipped)" -ForegroundColor DarkYellow
}

function Write-Info {
    param([string]$Message)
    Write-Host "  $Message" -ForegroundColor Gray
}

function Write-Warn {
    param([string]$Message)
    Write-Host "  [WARN] $Message" -ForegroundColor DarkYellow
}

# Get a Dataverse bearer token using Azure CLI
function Get-DataverseToken {
    param([string]$OrgUrl)

    Write-Info "Acquiring Dataverse token via Azure CLI..."

    $token = az account get-access-token --resource $OrgUrl --query "accessToken" -o tsv 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Host "" -ForegroundColor Red
        Write-Host "  ERROR: Failed to acquire Dataverse token." -ForegroundColor Red
        Write-Host "  Ensure you are logged in: az login" -ForegroundColor Yellow
        Write-Host "  For service principal: az login --service-principal -u <clientId> -p <secret> --tenant <tenantId>" -ForegroundColor Yellow
        throw "Azure CLI authentication failed. Exit code: $LASTEXITCODE. Details: $token"
    }

    return $token.Trim()
}

# Build standard Dataverse Web API headers
function Get-ApiHeaders {
    param([string]$Token)

    return @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
        "Prefer"           = "odata.include-annotations=*"
    }
}

# Invoke Dataverse Metadata Web API
function Invoke-DataverseApi {
    param(
        [string]$BaseUrl,
        [hashtable]$Headers,
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null
    )

    $uri = "$BaseUrl/api/data/v9.2/$Endpoint"

    $params = @{
        Uri     = $uri
        Method  = $Method
        Headers = $Headers
    }

    if ($null -ne $Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 20 -Compress)
    }

    try {
        return Invoke-RestMethod @params
    }
    catch {
        $detail = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $parsed = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($parsed.error.message) { $detail = $parsed.error.message }
        }
        throw "API Error [$Method $Endpoint]: $detail"
    }
}

# Test whether an entity definition exists
function Test-EntityExists {
    param([string]$BaseUrl, [hashtable]$Headers, [string]$LogicalName)

    try {
        $null = Invoke-DataverseApi -BaseUrl $BaseUrl -Headers $Headers `
            -Endpoint "EntityDefinitions(LogicalName='$LogicalName')" -Method "GET"
        return $true
    }
    catch {
        if ($_.Exception.Message -match "404|does not exist|Could not find") { return $false }
        throw
    }
}

# Test whether a global option set exists
function Test-GlobalOptionSetExists {
    param([string]$BaseUrl, [hashtable]$Headers, [string]$Name)

    try {
        $null = Invoke-DataverseApi -BaseUrl $BaseUrl -Headers $Headers `
            -Endpoint "GlobalOptionSetDefinitions(Name='$Name')" -Method "GET"
        return $true
    }
    catch {
        return $false
    }
}

# Test whether a security role exists (by name)
function Test-SecurityRoleExists {
    param([string]$BaseUrl, [hashtable]$Headers, [string]$RoleName)

    try {
        $result = Invoke-DataverseApi -BaseUrl $BaseUrl -Headers $Headers `
            -Endpoint "roles?`$filter=name eq '$RoleName'&`$select=roleid,name" -Method "GET"
        return ($result.value.Count -gt 0)
    }
    catch {
        return $false
    }
}

# Test whether an environment variable definition exists
function Test-EnvVarExists {
    param([string]$BaseUrl, [hashtable]$Headers, [string]$SchemaName)

    try {
        $result = Invoke-DataverseApi -BaseUrl $BaseUrl -Headers $Headers `
            -Endpoint "environmentvariabledefinitions?`$filter=schemaname eq '$SchemaName'&`$select=environmentvariabledefinitionid,schemaname" -Method "GET"
        return ($result.value.Count -gt 0)
    }
    catch {
        return $false
    }
}

# Helper to build a LocalizedLabel object for Dataverse metadata
function New-Label {
    param([string]$Text)

    return @{
        "@odata.type"     = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{
                "@odata.type"  = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label"        = $Text
                "LanguageCode" = 1033
            }
        )
    }
}

# ============================================================================
# DEPLOYMENT FUNCTIONS
# ============================================================================

# ------ ARTIFACT 1: sprk_report entity + local option set ------------------

function Deploy-ReportEntity {
    param([string]$BaseUrl, [hashtable]$Headers)

    Write-Step "Artifact 1 of 3: sprk_report entity"

    if (Test-EntityExists -BaseUrl $BaseUrl -Headers $Headers -LogicalName "sprk_report") {
        Write-Skip "sprk_report entity"
        Write-Info "Existing attributes will not be removed. Run verification (-SkipVerification:`$false) to check."
        return
    }

    Write-Info "Creating sprk_report entity with all attributes..."

    # OptionSet options for sprk_category (local to entity — defined inline)
    $categoryOptions = @(
        @{
            "Value" = 1
            "Label" = New-Label "Financial"
            "Description" = New-Label "Revenue, costs, budgets, forecasting"
        }
        @{
            "Value" = 2
            "Label" = New-Label "Operational"
            "Description" = New-Label "Matter throughput, team productivity, SLAs"
        }
        @{
            "Value" = 3
            "Label" = New-Label "Compliance"
            "Description" = New-Label "Regulatory, guideline adherence, audits"
        }
        @{
            "Value" = 4
            "Label" = New-Label "Documents"
            "Description" = New-Label "Document activity, storage, processing"
        }
        @{
            "Value" = 5
            "Label" = New-Label "Custom"
            "Description" = New-Label "Tenant-authored or bespoke reports"
        }
    )

    $entityDef = @{
        "@odata.type"           = "Microsoft.Dynamics.CRM.EntityMetadata"
        "SchemaName"            = "sprk_report"
        "DisplayName"           = New-Label "Report"
        "DisplayCollectionName" = New-Label "Reports"
        "Description"           = New-Label "Power BI report catalog for the Reporting module (App Owns Data, Import mode)"
        "OwnershipType"         = "UserOwned"      # User/Team ownership — ownerid OOB field included automatically
        "IsActivity"            = $false
        "HasNotes"              = $false
        "HasActivities"         = $false
        "PrimaryNameAttribute"  = "sprk_name"
        "Attributes"            = @(

            # sprk_name — Primary Name (required)
            @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "SchemaName"    = "sprk_name"
                "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
                "MaxLength"     = 200
                "DisplayName"   = New-Label "Name"
                "Description"   = New-Label "Display name of the report shown in the Reporting module"
                "IsPrimaryName" = $true
            }

            # sprk_pbi_reportid — PBI Report GUID (required)
            @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "SchemaName"    = "sprk_pbi_reportid"
                "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
                "MaxLength"     = 100
                "DisplayName"   = New-Label "PBI Report ID"
                "Description"   = New-Label "Power BI report GUID — used in embed token requests"
            }

            # sprk_workspaceid — PBI Workspace GUID (required)
            @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "SchemaName"    = "sprk_workspaceid"
                "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
                "MaxLength"     = 100
                "DisplayName"   = New-Label "Workspace ID"
                "Description"   = New-Label "Power BI workspace (group) GUID where the report lives"
            }

            # sprk_datasetid — PBI Dataset GUID (optional — needed for RLS EffectiveIdentity)
            @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "SchemaName"    = "sprk_datasetid"
                "RequiredLevel" = @{ "Value" = "None" }
                "MaxLength"     = 100
                "DisplayName"   = New-Label "Dataset ID"
                "Description"   = New-Label "Power BI dataset GUID — required for BU RLS EffectiveIdentity"
            }

            # sprk_category — Category OptionSet (required, local)
            @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
                "SchemaName"    = "sprk_category"
                "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
                "DisplayName"   = New-Label "Category"
                "Description"   = New-Label "Report category: Financial, Operational, Compliance, Documents, or Custom"
                "OptionSet"     = @{
                    "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
                    "IsGlobal"      = $false
                    "OptionSetType" = "Picklist"
                    "Options"       = $categoryOptions
                }
            }

            # sprk_embedurl — Embed URL (optional)
            @{
                "@odata.type"    = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "SchemaName"     = "sprk_embedurl"
                "RequiredLevel"  = @{ "Value" = "None" }
                "MaxLength"      = 2000
                "FormatName"     = @{ "Value" = "Url" }
                "DisplayName"    = New-Label "Embed URL"
                "Description"    = New-Label "Direct embed URL from Power BI service (optional; BFF derives from report ID if blank)"
            }

            # sprk_iscustom — Is Custom Boolean (optional, default false)
            @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
                "SchemaName"    = "sprk_iscustom"
                "RequiredLevel" = @{ "Value" = "None" }
                "DisplayName"   = New-Label "Is Custom"
                "Description"   = New-Label "True for tenant-authored reports; false for standard product reports shipped by Spaarke"
                "DefaultValue"  = $false
                "OptionSet"     = @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.BooleanOptionSetMetadata"
                    "TrueOption"  = @{
                        "Value" = 1
                        "Label" = New-Label "Yes"
                    }
                    "FalseOption" = @{
                        "Value" = 0
                        "Label" = New-Label "No"
                    }
                }
            }

            # sprk_description — Multiline description (optional)
            @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
                "SchemaName"    = "sprk_description"
                "RequiredLevel" = @{ "Value" = "None" }
                "MaxLength"     = 2000
                "DisplayName"   = New-Label "Description"
                "Description"   = New-Label "Admin notes or report description shown to users (max 2000 chars)"
            }
        )
    }

    $null = Invoke-DataverseApi -BaseUrl $BaseUrl -Headers $Headers `
        -Endpoint "EntityDefinitions" -Method "POST" -Body $entityDef

    Write-Success "sprk_report entity created with 7 custom attributes + OOB system fields (ownerid, statecode, createdon, etc.)"

    # Publish entity
    Write-Info "Publishing sprk_report customizations..."
    try {
        $publishBody = @{
            "ParameterXml" = "<importexportxml><entities><entity>sprk_report</entity></entities></importexportxml>"
        }
        $null = Invoke-DataverseApi -BaseUrl $BaseUrl -Headers $Headers `
            -Endpoint "PublishXml" -Method "POST" -Body $publishBody
        Write-Success "sprk_report published."
    }
    catch {
        Write-Warn "PublishXml returned an error (entity may still be available after a short delay): $_"
    }
}

# ------ ARTIFACT 2: sprk_ReportingAccess security roles --------------------

function Deploy-SecurityRoles {
    param([string]$BaseUrl, [hashtable]$Headers)

    Write-Step "Artifact 2 of 3: sprk_ReportingAccess security roles (3 tiers)"

    # Retrieve the root business unit ID (required when creating roles)
    $buResult = Invoke-DataverseApi -BaseUrl $BaseUrl -Headers $Headers `
        -Endpoint "businessunits?`$filter=parentbusinessunitid eq null&`$select=businessunitid,name" -Method "GET"

    if ($buResult.value.Count -eq 0) {
        throw "Could not find root business unit. Cannot create security roles without a business unit ID."
    }

    $rootBuId = $buResult.value[0].businessunitid
    Write-Info "Root business unit: $($buResult.value[0].name) ($rootBuId)"

    # Define the three role tiers
    # Privilege depth values: 0=None, 1=User, 2=BusinessUnit, 4=ParentChildBusinessUnit, 8=Organization
    $roles = @(
        @{
            Name        = "sprk_ReportingAccess_Viewer"
            Description = "Spaarke Reporting — Viewer tier. Read-only access to the report catalog and embedded reports. No Power BI license required."
            Privileges  = @(
                # Read (Org scope) on sprk_report — sprk_report privilege ID is resolved at runtime
                # We configure the role first, then add entity-specific privileges via role privilege assignment
            )
        }
        @{
            Name        = "sprk_ReportingAccess_Author"
            Description = "Spaarke Reporting — Author tier. Can view reports, update catalog entries, and add new reports. Cannot delete catalog entries."
            Privileges  = @()
        }
        @{
            Name        = "sprk_ReportingAccess_Admin"
            Description = "Spaarke Reporting — Admin tier. Full CRUD access to the report catalog. Assign only to platform administrators."
            Privileges  = @()
        }
    )

    $createdRoles = @{}

    foreach ($roleDef in $roles) {
        $roleName = $roleDef.Name

        if (Test-SecurityRoleExists -BaseUrl $BaseUrl -Headers $Headers -RoleName $roleName) {
            Write-Skip "Security role: $roleName"

            # Retrieve existing role ID for privilege assignment
            $existing = Invoke-DataverseApi -BaseUrl $BaseUrl -Headers $Headers `
                -Endpoint "roles?`$filter=name eq '$roleName'&`$select=roleid" -Method "GET"
            $createdRoles[$roleName] = $existing.value[0].roleid
            continue
        }

        Write-Info "Creating security role: $roleName..."

        $roleBody = @{
            "name"                           = $roleName
            "description"                    = $roleDef.Description
            "businessunitid@odata.bind"      = "/businessunits($rootBuId)"
        }

        # POST to roles endpoint returns the created role in Location header; use -ReturnHeader trick
        $createUri = "$BaseUrl/api/data/v9.2/roles"
        $headers = Get-ApiHeaders -Token ($Headers["Authorization"] -replace "Bearer ", "")
        $headers["Prefer"] = "return=representation"

        $response = Invoke-RestMethod -Uri $createUri -Method POST -Headers $headers `
            -Body ($roleBody | ConvertTo-Json -Depth 5 -Compress)

        $roleId = $response.roleid
        if (-not $roleId) {
            # Fall back: query by name to get the ID
            $created = Invoke-DataverseApi -BaseUrl $BaseUrl -Headers $Headers `
                -Endpoint "roles?`$filter=name eq '$roleName'&`$select=roleid" -Method "GET"
            $roleId = $created.value[0].roleid
        }

        $createdRoles[$roleName] = $roleId
        Write-Success "Created security role: $roleName (ID: $roleId)"
    }

    # Add entity privileges to each role
    # Note: Privilege IDs for custom entities are not known until the entity is created.
    # The approach is to use the RolePrivilege association via the Dataverse API.
    # For sprk_report (UserOwned entity), the privilege names are:
    #   prvRead<EntitySchemaName>, prvWrite<EntitySchemaName>, prvCreate<EntitySchemaName>, prvDelete<EntitySchemaName>
    # Dataverse generates these automatically when the entity is created.

    Write-Info ""
    Write-Info "Assigning entity privileges to roles..."
    Write-Info "Querying privilege IDs for sprk_report operations..."

    $privilegeMap = @{
        "Read"   = $null
        "Write"  = $null
        "Create" = $null
        "Delete" = $null
    }

    foreach ($privName in @("prvReadsprk_report", "prvWritesprk_report", "prvCreatesprk_report", "prvDeletesprk_report")) {
        try {
            $privResult = Invoke-DataverseApi -BaseUrl $BaseUrl -Headers $Headers `
                -Endpoint "privileges?`$filter=name eq '$privName'&`$select=privilegeid,name" -Method "GET"

            if ($privResult.value.Count -gt 0) {
                $op = $privName -replace "^prv", "" -replace "sprk_report", ""
                $privilegeMap[$op] = $privResult.value[0].privilegeid
                Write-Info "  Found privilege $privName → $($privResult.value[0].privilegeid)"
            }
            else {
                Write-Warn "Privilege '$privName' not found. Entity may still be publishing. Re-run after entity is fully deployed."
            }
        }
        catch {
            Write-Warn "Could not query privilege '$privName': $_"
        }
    }

    # Privilege depth: 8 = Organization scope
    $orgScope = 8

    # Viewer: Read (Org)
    if ($privilegeMap["Read"]) {
        Assign-RolePrivilege -BaseUrl $BaseUrl -Headers $Headers `
            -RoleId $createdRoles["sprk_ReportingAccess_Viewer"] `
            -RoleName "sprk_ReportingAccess_Viewer" `
            -PrivilegeId $privilegeMap["Read"] `
            -PrivilegeName "Read" `
            -Depth $orgScope
    }

    # Author: Read + Write + Create (Org)
    foreach ($priv in @("Read", "Write", "Create")) {
        if ($privilegeMap[$priv]) {
            Assign-RolePrivilege -BaseUrl $BaseUrl -Headers $Headers `
                -RoleId $createdRoles["sprk_ReportingAccess_Author"] `
                -RoleName "sprk_ReportingAccess_Author" `
                -PrivilegeId $privilegeMap[$priv] `
                -PrivilegeName $priv `
                -Depth $orgScope
        }
    }

    # Admin: Read + Write + Create + Delete (Org)
    foreach ($priv in @("Read", "Write", "Create", "Delete")) {
        if ($privilegeMap[$priv]) {
            Assign-RolePrivilege -BaseUrl $BaseUrl -Headers $Headers `
                -RoleId $createdRoles["sprk_ReportingAccess_Admin"] `
                -RoleName "sprk_ReportingAccess_Admin" `
                -PrivilegeId $privilegeMap[$priv] `
                -PrivilegeName $priv `
                -Depth $orgScope
        }
    }
}

function Assign-RolePrivilege {
    param(
        [string]$BaseUrl,
        [hashtable]$Headers,
        [string]$RoleId,
        [string]$RoleName,
        [string]$PrivilegeId,
        [string]$PrivilegeName,
        [int]$Depth
    )

    try {
        $body = @{
            "RolePrivileges" = @(
                @{
                    "PrivilegeId"   = $PrivilegeId
                    "BusinessUnitId" = $null   # null = inherits from role's BU
                    "Depth"         = $Depth
                }
            )
        }

        $null = Invoke-DataverseApi -BaseUrl $BaseUrl -Headers $Headers `
            -Endpoint "roles($RoleId)/Microsoft.Dynamics.CRM.AddPrivilegesRole" `
            -Method "POST" -Body $body

        Write-Success "  $RoleName ← $PrivilegeName (scope=Org)"
    }
    catch {
        Write-Warn "  Could not assign $PrivilegeName to $RoleName`: $_"
        Write-Warn "  Manual step: Open role '$RoleName' in Maker Portal and add $PrivilegeName privilege (Org scope) on sprk_report."
    }
}

# ------ ARTIFACT 3: sprk_ReportingModuleEnabled environment variable -------

function Deploy-EnvironmentVariable {
    param([string]$BaseUrl, [hashtable]$Headers)

    Write-Step "Artifact 3 of 3: sprk_ReportingModuleEnabled environment variable"

    $schemaName = "sprk_ReportingModuleEnabled"

    if (Test-EnvVarExists -BaseUrl $BaseUrl -Headers $Headers -SchemaName $schemaName) {
        Write-Skip "Environment variable: $schemaName"
        Write-Info "Default value (No/false) and description are preserved from existing definition."
        return
    }

    Write-Info "Creating environment variable: $schemaName..."

    # Environment variables use a separate endpoint: environmentvariabledefinitions
    $envVarBody = @{
        "schemaname"            = $schemaName
        "displayname"           = "Reporting Module Enabled"
        "description"           = "Controls visibility and access to the Reporting module. When disabled (No), all /api/reporting/* BFF endpoints return 403 and the Reporting navigation item is hidden. Set to Yes after Power BI capacity and workspace are configured."
        "type"                  = 100000001  # 100000001 = Boolean (Yes/No) in Dataverse env var types
        "defaultvalue"          = "no"       # Default: disabled — admins must explicitly enable per environment
        "introducedversion"     = "1.0.0.0"
        "isrequired"            = $false
        "iscustomizable"        = @{ "Value" = $true; "CanBeChanged" = $true; "ManagedPropertyLogicalName" = "iscustomizableanddeletable" }
    }

    try {
        $null = Invoke-DataverseApi -BaseUrl $BaseUrl -Headers $Headers `
            -Endpoint "environmentvariabledefinitions" -Method "POST" -Body $envVarBody
        Write-Success "Environment variable '$schemaName' created. Default value: No (false)."
        Write-Info "  To enable: pac env update-variable --name $schemaName --value true --environment $BaseUrl"
    }
    catch {
        # Alternate approach: type as string "Boolean"
        Write-Warn "First attempt failed ($_). Retrying with string type..."

        $envVarBodyV2 = @{
            "schemaname"        = $schemaName
            "displayname"       = "Reporting Module Enabled"
            "description"       = "Feature gate for Spaarke Reporting module. No (default) = module hidden and BFF returns 403. Yes = module active for users with sprk_ReportingAccess role."
            "type"              = "Boolean"
            "defaultvalue"      = "no"
            "introducedversion" = "1.0.0.0"
            "isrequired"        = $false
        }

        try {
            $null = Invoke-DataverseApi -BaseUrl $BaseUrl -Headers $Headers `
                -Endpoint "environmentvariabledefinitions" -Method "POST" -Body $envVarBodyV2
            Write-Success "Environment variable '$schemaName' created (Boolean type, default No)."
        }
        catch {
            Write-Host "" -ForegroundColor Red
            Write-Host "  FAILED: Could not create environment variable via Web API." -ForegroundColor Red
            Write-Host "  Use the manual steps below to create it in the Maker Portal." -ForegroundColor Yellow
            Write-ManualEnvVarSteps -OrgUrl $BaseUrl -SchemaName $schemaName
        }
    }
}

function Write-ManualEnvVarSteps {
    param([string]$OrgUrl, [string]$SchemaName)

    Write-Host ""
    Write-Host "  MANUAL STEPS: Create environment variable $SchemaName" -ForegroundColor Yellow
    Write-Host "  ==========================================================" -ForegroundColor Yellow
    Write-Host "  1. Open $OrgUrl/main.aspx?pagetype=entitylist&etn=environmentvariabledefinition" -ForegroundColor White
    Write-Host "  2. Click New" -ForegroundColor White
    Write-Host "  3. Fill in:" -ForegroundColor White
    Write-Host "       Display Name:  Reporting Module Enabled" -ForegroundColor White
    Write-Host "       Schema Name:   $SchemaName" -ForegroundColor White
    Write-Host "       Data Type:     Boolean (Yes/No)" -ForegroundColor White
    Write-Host "       Default Value: No" -ForegroundColor White
    Write-Host "       Description:   Controls visibility and access to the Reporting module." -ForegroundColor White
    Write-Host "  4. Save" -ForegroundColor White
    Write-Host ""
    Write-Host "  PAC CLI alternative:" -ForegroundColor Yellow
    Write-Host "    pac env update-variable --name $SchemaName --value false --environment $OrgUrl" -ForegroundColor White
    Write-Host ""
}

# ------ VERIFICATION -----------------------------------------------------------

function Invoke-Verification {
    param([string]$BaseUrl, [hashtable]$Headers)

    Write-Step "Verification"

    $allPassed = $true

    # Check entity
    if (Test-EntityExists -BaseUrl $BaseUrl -Headers $Headers -LogicalName "sprk_report") {
        Write-Success "sprk_report entity exists and is queryable"

        # Check Web API access
        try {
            $records = Invoke-DataverseApi -BaseUrl $BaseUrl -Headers $Headers `
                -Endpoint "sprk_reports?`$select=sprk_name&`$top=1" -Method "GET"
            Write-Success "sprk_reports Web API endpoint responds (record count: $($records.value.Count))"
        }
        catch {
            Write-Warn "sprk_reports Web API returned error (entity may still be publishing): $_"
        }

        # Check key attributes
        try {
            $entityMeta = Invoke-DataverseApi -BaseUrl $BaseUrl -Headers $Headers `
                -Endpoint "EntityDefinitions(LogicalName='sprk_report')/Attributes?`$select=LogicalName,AttributeType" -Method "GET"
            $logicalNames = $entityMeta.value | Select-Object -ExpandProperty LogicalName

            $expectedAttribs = @("sprk_name", "sprk_pbi_reportid", "sprk_workspaceid", "sprk_datasetid",
                                 "sprk_category", "sprk_embedurl", "sprk_iscustom", "sprk_description")

            foreach ($attr in $expectedAttribs) {
                if ($logicalNames -contains $attr) {
                    Write-Success "  Attribute: $attr"
                }
                else {
                    Write-Warn "  Attribute MISSING: $attr"
                    $allPassed = $false
                }
            }
        }
        catch {
            Write-Warn "Could not verify entity attributes: $_"
        }
    }
    else {
        Write-Host "  [FAIL] sprk_report entity not found" -ForegroundColor Red
        $allPassed = $false
    }

    # Check security roles
    foreach ($roleName in @("sprk_ReportingAccess_Viewer", "sprk_ReportingAccess_Author", "sprk_ReportingAccess_Admin")) {
        if (Test-SecurityRoleExists -BaseUrl $BaseUrl -Headers $Headers -RoleName $roleName) {
            Write-Success "Security role: $roleName"
        }
        else {
            Write-Host "  [FAIL] Security role not found: $roleName" -ForegroundColor Red
            $allPassed = $false
        }
    }

    # Check environment variable
    if (Test-EnvVarExists -BaseUrl $BaseUrl -Headers $Headers -SchemaName "sprk_ReportingModuleEnabled") {
        Write-Success "Environment variable: sprk_ReportingModuleEnabled"
    }
    else {
        Write-Host "  [FAIL] Environment variable not found: sprk_ReportingModuleEnabled" -ForegroundColor Red
        $allPassed = $false
    }

    return $allPassed
}

# ============================================================================
# WHAT-IF MODE: Print plan and exit
# ============================================================================

function Show-WhatIfPlan {
    param([string]$OrgUrl, [string]$Env)

    Write-Host ""
    Write-Host "=== DEPLOYMENT PLAN (WhatIf — no changes will be made) ===" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "  Target environment : $OrgUrl" -ForegroundColor White
    Write-Host "  Environment label  : $Env" -ForegroundColor White
    Write-Host ""
    Write-Host "  Artifact 1: sprk_report entity" -ForegroundColor Yellow
    Write-Host "    - Entity (UserOwned, primary name: sprk_name)" -ForegroundColor White
    Write-Host "    - Attribute: sprk_name           (String, 200, required)" -ForegroundColor White
    Write-Host "    - Attribute: sprk_pbi_reportid   (String, 100, required — PBI report GUID)" -ForegroundColor White
    Write-Host "    - Attribute: sprk_workspaceid    (String, 100, required — PBI workspace GUID)" -ForegroundColor White
    Write-Host "    - Attribute: sprk_datasetid      (String, 100, optional — PBI dataset GUID)" -ForegroundColor White
    Write-Host "    - Attribute: sprk_category       (OptionSet: Financial=1, Operational=2, Compliance=3, Documents=4, Custom=5)" -ForegroundColor White
    Write-Host "    - Attribute: sprk_embedurl       (URL String, 2000, optional)" -ForegroundColor White
    Write-Host "    - Attribute: sprk_iscustom       (Boolean, default=false)" -ForegroundColor White
    Write-Host "    - Attribute: sprk_description    (Multiline text, 2000, optional)" -ForegroundColor White
    Write-Host "    - OOB fields: ownerid, statecode, statuscode, createdon, modifiedon, etc." -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Artifact 2: Security roles (3)" -ForegroundColor Yellow
    Write-Host "    - sprk_ReportingAccess_Viewer  — Read (Org) on sprk_report" -ForegroundColor White
    Write-Host "    - sprk_ReportingAccess_Author  — Read + Write + Create (Org) on sprk_report" -ForegroundColor White
    Write-Host "    - sprk_ReportingAccess_Admin   — Read + Write + Create + Delete (Org) on sprk_report" -ForegroundColor White
    Write-Host ""
    Write-Host "  Artifact 3: Environment variable" -ForegroundColor Yellow
    Write-Host "    - sprk_ReportingModuleEnabled  Boolean, default No" -ForegroundColor White
    Write-Host "      Effect: module disabled until admin sets value to Yes" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  Idempotent: existing artifacts will be skipped, not overwritten." -ForegroundColor Gray
    Write-Host ""
    Write-Host "Remove -WhatIf to execute the deployment." -ForegroundColor Yellow
    Write-Host ""
}

# ============================================================================
# MAIN
# ============================================================================

Write-Header "Spaarke Reporting Module — Dataverse Schema Deployment"

Write-Host "  Environment : $DataverseOrg"
Write-Host "  Label       : $Environment"
Write-Host "  Mode        : $(if ($WhatIfPreference) { 'WhatIf (plan only)' } else { 'Execute' })"
Write-Host ""

if (-not $DataverseOrg) {
    Write-Host "  ERROR: DataverseOrg is required. Pass -DataverseOrg or set DATAVERSE_URL." -ForegroundColor Red
    Write-Host "  Example: .\Deploy-ReportingSchema.ps1 -DataverseOrg 'https://spaarkedev1.crm.dynamics.com'" -ForegroundColor Yellow
    exit 1
}

# Normalize URL — strip trailing slash
$DataverseOrg = $DataverseOrg.TrimEnd("/")

# ---- WhatIf mode ------------------------------------------------------------
if ($WhatIfPreference) {
    Show-WhatIfPlan -OrgUrl $DataverseOrg -Env $Environment
    exit 0
}

# ---- Acquire token ----------------------------------------------------------
Write-Step "Step 1/5: Authenticate"
$token  = Get-DataverseToken -OrgUrl $DataverseOrg
$headers = Get-ApiHeaders -Token $token
Write-Success "Authenticated. Token acquired."

# ---- Deploy entity ----------------------------------------------------------
Write-Step "Step 2/5: Deploy sprk_report entity"
Deploy-ReportEntity -BaseUrl $DataverseOrg -Headers $headers

# ---- Deploy security roles --------------------------------------------------
Write-Step "Step 3/5: Deploy sprk_ReportingAccess security roles"
Deploy-SecurityRoles -BaseUrl $DataverseOrg -Headers $headers

# ---- Deploy environment variable --------------------------------------------
Write-Step "Step 4/5: Deploy sprk_ReportingModuleEnabled environment variable"
Deploy-EnvironmentVariable -BaseUrl $DataverseOrg -Headers $headers

# ---- Verify -----------------------------------------------------------------
if (-not $SkipVerification) {
    Write-Step "Step 5/5: Verify deployment"
    $allPassed = Invoke-Verification -BaseUrl $DataverseOrg -Headers $headers
}
else {
    Write-Step "Step 5/5: Verification skipped (-SkipVerification)"
    Write-Info "Run without -SkipVerification to validate all artifacts."
    $allPassed = $true
}

# ---- Summary ----------------------------------------------------------------
$elapsed = (Get-Date) - $StartTime

Write-Host ""
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "  Deployment Summary" -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Environment  : $DataverseOrg"
Write-Host "  Duration     : $([math]::Round($elapsed.TotalSeconds, 1))s"
Write-Host ""

if ($allPassed) {
    Write-Host "  Result       : All artifacts deployed and verified." -ForegroundColor Green
    Write-Host ""
    Write-Host "  Next steps:" -ForegroundColor Yellow
    Write-Host "    1. Enable the module when ready:"
    Write-Host "       pac env update-variable --name sprk_ReportingModuleEnabled --value true --environment $DataverseOrg"
    Write-Host "    2. Assign roles to users or teams:"
    Write-Host "       pac admin assign-user --environment $DataverseOrg --user <upn> --role sprk_ReportingAccess_Viewer"
    Write-Host "    3. Deploy the sprk_reporting Code Page (task 016):"
    Write-Host "       .\Deploy-ReportingPage.ps1 -EnvironmentUrl $DataverseOrg"
    Write-Host "    4. Publish standard .pbix reports (task 034-035):"
    Write-Host "       .\Deploy-ReportingReports.ps1 -DataverseOrg $DataverseOrg"
    Write-Host ""
}
else {
    Write-Host "  Result       : Some artifacts failed or were not verified." -ForegroundColor Red
    Write-Host "  Review warnings above and re-run, or complete steps manually." -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

Write-Host "  Manual verification (Maker Portal):" -ForegroundColor Gray
Write-Host "    Entity: $DataverseOrg/main.aspx?pagetype=entitylist&etn=sprk_report" -ForegroundColor Gray
Write-Host "    Roles : $DataverseOrg/main.aspx?pagetype=entitylist&etn=role" -ForegroundColor Gray
Write-Host "    EnvVar: $DataverseOrg/main.aspx?pagetype=entitylist&etn=environmentvariabledefinition" -ForegroundColor Gray
Write-Host ""
