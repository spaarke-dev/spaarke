<#
.SYNOPSIS
    Seed system workspace layouts into Dataverse (sprk_workspacelayout, isSystem=true).

.DESCRIPTION
    Round-8 Wave 2a (task 108). Operator decision Option B: system workspaces
    become real sprk_workspacelayout Dataverse records flagged isSystem=true so
    they flow through the existing widget_load -> WorkspaceLayoutWidget ->
    LegalWorkspaceApp embed pipeline rather than being hard-coded frontend
    entries (WorkspaceHomeTab).

    The script is split into two halves:

      1. Schema half (idempotent). Ensures the sprk_issystem Boolean column
         exists on sprk_workspacelayout. If missing, adds it via the
         Dataverse metadata Web API. This is the one schema change required
         to support Option B; if a future task adds the column through the
         maker portal the script no-ops.

      2. Data half (idempotent). For each layout defined in
         scripts/system-layouts.json:

            a. Query sprk_workspacelayouts for an existing record with the
               same (sprk_name, sprk_issystem=true).
            b. If found: skip (default) or PATCH if -Force is supplied.
            c. If not found: POST a new record with isSystem=true.

    Path B (direct Dataverse Web API) is chosen over Path A (BFF POST) because:
      - isSystem is an admin-level field and the BFF's POST /api/workspace/layouts
        endpoint deliberately does not expose it to non-admin callers.
      - Seeding is a one-time admin task, not a runtime user operation.
      - Direct Web API is the same pattern Deploy-SpaarkeAi.ps1 uses for web
        resource updates -- consistency with existing scripts.

.PARAMETER DataverseUrl
    The Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER LayoutsFile
    Path to the JSON sidecar describing the layouts to seed.
    Default: <scriptDir>/system-layouts.json

.PARAMETER WhatIf
    Dry-run mode. Prints what would be added / created / skipped / replaced
    without writing to Dataverse.

.PARAMETER Force
    When a record with the same (name, isSystem=true) already exists, replace
    sprk_layouttemplateid + sprk_sectionsjson + sprk_isdefault on that record
    (PATCH). Default behavior is to skip.

.PARAMETER SkipSchema
    Skip the schema half (sprk_issystem column add). Useful when re-running
    against an environment where the column has already been provisioned.

.EXAMPLE
    .\Deploy-SystemWorkspaceLayouts.ps1 -WhatIf
    # Dry-run against dev (no writes).

.EXAMPLE
    .\Deploy-SystemWorkspaceLayouts.ps1
    # Create the 4 layouts in dev. Idempotent -- re-running is a no-op.

.EXAMPLE
    .\Deploy-SystemWorkspaceLayouts.ps1 -Force
    # Refresh sectionsJson + layoutTemplateId on existing records.

.NOTES
    Project: spaarke-ai-platform-unification-r3
    Round / Wave: R8 Wave 2a
    Task: 108 -- Build + run Deploy-SystemWorkspaceLayouts.ps1
    Created: 2026-05-22
    Author: spaarke-dev (Claude Opus 4.7)
#>

[CmdletBinding(SupportsShouldProcess = $false)]
param(
    [Parameter(Mandatory = $false)]
    [string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com',

    [Parameter(Mandatory = $false)]
    [string]$LayoutsFile = $null,

    [Parameter(Mandatory = $false)]
    [switch]$WhatIf,

    [Parameter(Mandatory = $false)]
    [switch]$Force,

    [Parameter(Mandatory = $false)]
    [switch]$SkipSchema
)

$ErrorActionPreference = 'Stop'
$AZ = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd'

# ============================================================================
# Helper Functions (mirror Create-ScopeCapabilityFields.ps1 patterns)
# ============================================================================

function Get-DataverseToken {
    param([string]$EnvironmentUrl)

    Write-Host '  Acquiring Dataverse access token...' -ForegroundColor Gray
    $tokenResult = & $AZ account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>&1
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($tokenResult)) {
        throw "Failed to acquire token. Run 'az login' first. Detail: $tokenResult"
    }
    return $tokenResult.Trim()
}

function Invoke-DataverseApi {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Endpoint,
        [string]$Method = 'GET',
        [object]$Body = $null,
        [hashtable]$ExtraHeaders = $null
    )

    $headers = @{
        'Authorization'    = "Bearer $Token"
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
        'Accept'           = 'application/json'
        'Content-Type'     = 'application/json; charset=utf-8'
    }
    if ($ExtraHeaders) { $ExtraHeaders.GetEnumerator() | ForEach-Object { $headers[$_.Key] = $_.Value } }

    $uri = "$BaseUrl/api/data/v9.2/$Endpoint"
    $params = @{
        Uri     = $uri
        Method  = $Method
        Headers = $headers
    }
    if ($null -ne $Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 20 -Compress:$false)
    }

    try {
        return Invoke-RestMethod @params
    }
    catch {
        $msg = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            try {
                $errJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction Stop
                if ($errJson.error.message) { $msg = $errJson.error.message }
            } catch { }
        }
        throw "Dataverse API error ($Method $Endpoint): $msg"
    }
}

function New-Label {
    param([string]$Text)
    return @{
        '@odata.type'     = 'Microsoft.Dynamics.CRM.Label'
        'LocalizedLabels' = @(
            @{
                '@odata.type'  = 'Microsoft.Dynamics.CRM.LocalizedLabel'
                'Label'        = $Text
                'LanguageCode' = 1033
            }
        )
    }
}

function Test-AttributeExists {
    param([string]$Token, [string]$BaseUrl, [string]$EntityLogicalName, [string]$AttributeLogicalName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')" `
            -Method 'GET' | Out-Null
        return $true
    }
    catch { return $false }
}

function Add-IsSystemAttribute {
    param([string]$Token, [string]$BaseUrl, [switch]$DryRun)

    $entity = 'sprk_workspacelayout'
    $attr   = 'sprk_issystem'
    Write-Host "  Checking $entity.$attr..." -ForegroundColor Gray

    if (Test-AttributeExists -Token $Token -BaseUrl $BaseUrl -EntityLogicalName $entity -AttributeLogicalName $attr) {
        Write-Host "    Already exists - no schema change needed." -ForegroundColor Green
        return @{ Created = $false }
    }

    if ($DryRun) {
        Write-Host "    [WhatIf] Would add Boolean column $attr (DisplayName='Is System', Default=false)" -ForegroundColor Yellow
        return @{ Created = $true; DryRun = $true }
    }

    Write-Host "    Adding Boolean column $attr..." -ForegroundColor Yellow

    $attributeDef = @{
        '@odata.type'   = 'Microsoft.Dynamics.CRM.BooleanAttributeMetadata'
        'SchemaName'    = 'sprk_IsSystem'
        'RequiredLevel' = @{ 'Value' = 'None' }
        'DisplayName'   = (New-Label -Text 'Is System')
        'Description'   = (New-Label -Text 'True for system-provided workspace layouts seeded by Deploy-SystemWorkspaceLayouts.ps1. System layouts are read-only and non-deletable by end users.')
        'DefaultValue'  = $false
        'OptionSet'     = @{
            '@odata.type'  = 'Microsoft.Dynamics.CRM.BooleanOptionSetMetadata'
            'TrueOption'   = @{
                '@odata.type' = 'Microsoft.Dynamics.CRM.OptionMetadata'
                'Value'       = 1
                'Label'       = (New-Label -Text 'Yes')
            }
            'FalseOption'  = @{
                '@odata.type' = 'Microsoft.Dynamics.CRM.OptionMetadata'
                'Value'       = 0
                'Label'       = (New-Label -Text 'No')
            }
        }
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions(LogicalName='$entity')/Attributes" `
        -Method 'POST' -Body $attributeDef | Out-Null

    Write-Host "    Created sprk_issystem on $entity" -ForegroundColor Green
    return @{ Created = $true }
}

function Find-SystemLayout {
    param([string]$Token, [string]$BaseUrl, [string]$Name)

    $filterName = $Name.Replace("'", "''")
    $url = "sprk_workspacelayouts?`$filter=sprk_name eq '$filterName' and sprk_issystem eq true&`$select=sprk_workspacelayoutid,sprk_name,sprk_layouttemplateid,sprk_sectionsjson,sprk_isdefault,sprk_sortorder,sprk_issystem"
    try {
        $resp = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint $url -Method 'GET'
        if ($resp.value.Count -gt 0) { return $resp.value[0] }
    }
    catch {
        # If the filter fails because the column was just created and Dataverse
        # hasn't propagated it, surface a clear message.
        throw "Lookup failed for '$Name'. If sprk_issystem was just created, retry in 30 seconds. Underlying error: $($_.Exception.Message)"
    }
    return $null
}

function Build-SectionsJson {
    param([string]$SectionId, [string]$LayoutTemplateId)

    # single-column template = 4 stacked rows of 1 slot.
    # Place the section in row-1 slot 0. Remaining rows empty (filtered out
    # by the dynamic-config builder on the client).
    if ($LayoutTemplateId -eq 'single-column') {
        $rows = @(
            @{ id = 'row-1'; columns = '1fr'; columnsSmall = '1fr'; sections = @($SectionId) }
            @{ id = 'row-2'; columns = '1fr'; columnsSmall = '1fr'; sections = @('') }
            @{ id = 'row-3'; columns = '1fr'; columnsSmall = '1fr'; sections = @('') }
            @{ id = 'row-4'; columns = '1fr'; columnsSmall = '1fr'; sections = @('') }
        )
    }
    else {
        throw "Unsupported layoutTemplateId '$LayoutTemplateId'. Only 'single-column' is supported in this seed."
    }

    $obj = @{ schemaVersion = 1; rows = $rows; scope = 'my' }
    return ($obj | ConvertTo-Json -Depth 10 -Compress)
}

function Get-StringHash {
    param([string]$Value)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $hashBytes = $sha.ComputeHash($bytes)
    return ([BitConverter]::ToString($hashBytes)).Replace('-', '').Substring(0, 12).ToLower()
}

# ============================================================================
# Main
# ============================================================================

Write-Host '========================================='
Write-Host 'System Workspace Layout Seed'
Write-Host "Target  : $DataverseUrl"
Write-Host "Mode    : $(if ($WhatIf) {'WhatIf (dry-run)'} else {'Live'})"
Write-Host "Force   : $Force"
Write-Host "Schema  : $(if ($SkipSchema) {'SKIP'} else {'Verify + Add if missing'})"
Write-Host '========================================='

# -- Resolve sidecar path --
if ([string]::IsNullOrWhiteSpace($LayoutsFile)) {
    $scriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
    $LayoutsFile = Join-Path $scriptDir 'system-layouts.json'
}

if (-not (Test-Path $LayoutsFile)) {
    throw "Layouts file not found: $LayoutsFile"
}

Write-Host "[1/4] Loading layouts from $LayoutsFile..."
$layoutsDoc = Get-Content -Raw -Path $LayoutsFile | ConvertFrom-Json
$layouts    = $layoutsDoc.layouts
if ($null -eq $layouts -or $layouts.Count -eq 0) {
    throw "No layouts found in $LayoutsFile"
}
Write-Host "      Loaded $($layouts.Count) layout(s): $($layouts.name -join ', ')" -ForegroundColor Green

# -- Acquire token --
Write-Host '[2/4] Authenticating...'
$token = Get-DataverseToken -EnvironmentUrl $DataverseUrl
Write-Host '      OK' -ForegroundColor Green

# -- Schema half --
Write-Host '[3/4] Schema verification...'
if ($SkipSchema) {
    Write-Host '      Skipped (-SkipSchema)' -ForegroundColor Yellow
} else {
    $schemaResult = Add-IsSystemAttribute -Token $token -BaseUrl $DataverseUrl -DryRun:$WhatIf
    if ($schemaResult.Created -and -not $WhatIf) {
        Write-Host '      Waiting 5s for metadata to settle...' -ForegroundColor Gray
        Start-Sleep -Seconds 5
    }
}

# -- Data half --
Write-Host '[4/4] Seeding system layouts...'

$summary = @()
$idx = 0
foreach ($layout in $layouts) {
    $idx++
    $tag = "[$idx/$($layouts.Count)] $($layout.name)"
    Write-Host ''
    Write-Host "  $tag" -ForegroundColor Cyan

    $sectionsJson = Build-SectionsJson -SectionId $layout.sectionId -LayoutTemplateId $layout.layoutTemplateId
    $sectionsHash = Get-StringHash -Value $sectionsJson

    # Look up existing
    if ($WhatIf -and -not $SkipSchema -and $schemaResult.Created) {
        Write-Host '    [WhatIf] sprk_issystem would not yet exist - skipping lookup.' -ForegroundColor Yellow
        $existing = $null
    } else {
        $existing = $null
        try { $existing = Find-SystemLayout -Token $token -BaseUrl $DataverseUrl -Name $layout.name }
        catch {
            if ($WhatIf) {
                Write-Host "    [WhatIf] Lookup error (likely column not yet present): $($_.Exception.Message)" -ForegroundColor Yellow
            } else { throw }
        }
    }

    if ($null -ne $existing) {
        $existingId = $existing.sprk_workspacelayoutid
        if ($Force) {
            Write-Host "    Existing record found: $existingId" -ForegroundColor Yellow
            if ($WhatIf) {
                Write-Host '    [WhatIf] Would PATCH sectionsJson + layoutTemplateId + isDefault.' -ForegroundColor Yellow
                $summary += [PSCustomObject]@{ Name = $layout.name; Id = $existingId; Action = 'would-replace'; Hash = $sectionsHash }
            } else {
                $patchBody = @{
                    sprk_layouttemplateid = $layout.layoutTemplateId
                    sprk_sectionsjson     = $sectionsJson
                    sprk_isdefault        = [bool]$layout.isDefault
                    sprk_sortorder        = [int]$layout.sortOrder
                }
                Invoke-DataverseApi -Token $token -BaseUrl $DataverseUrl `
                    -Endpoint "sprk_workspacelayouts($existingId)" -Method 'PATCH' -Body $patchBody | Out-Null
                Write-Host "    Replaced: $existingId (sectionsHash=$sectionsHash)" -ForegroundColor Green
                $summary += [PSCustomObject]@{ Name = $layout.name; Id = $existingId; Action = 'replaced'; Hash = $sectionsHash }
            }
        } else {
            Write-Host "    Existing record found: $existingId - skipping (use -Force to replace)." -ForegroundColor Yellow
            $summary += [PSCustomObject]@{ Name = $layout.name; Id = $existingId; Action = 'skipped'; Hash = $sectionsHash }
        }
        continue
    }

    # Create new
    $createBody = @{
        sprk_name             = $layout.name
        sprk_layouttemplateid = $layout.layoutTemplateId
        sprk_sectionsjson     = $sectionsJson
        sprk_isdefault        = [bool]$layout.isDefault
        sprk_sortorder        = [int]$layout.sortOrder
        sprk_issystem         = $true
    }

    if ($WhatIf) {
        Write-Host "    [WhatIf] Would POST new record (section=$($layout.sectionId), template=$($layout.layoutTemplateId), default=$($layout.isDefault), sort=$($layout.sortOrder), sectionsHash=$sectionsHash)" -ForegroundColor Yellow
        $summary += [PSCustomObject]@{ Name = $layout.name; Id = '<would-create>'; Action = 'would-create'; Hash = $sectionsHash }
        continue
    }

    $extraHeaders = @{ 'Prefer' = 'return=representation' }
    $created = Invoke-DataverseApi -Token $token -BaseUrl $DataverseUrl `
        -Endpoint 'sprk_workspacelayouts' -Method 'POST' -Body $createBody -ExtraHeaders $extraHeaders
    $newId = $created.sprk_workspacelayoutid
    Write-Host "    Created: $newId (sectionsHash=$sectionsHash)" -ForegroundColor Green
    $summary += [PSCustomObject]@{ Name = $layout.name; Id = $newId; Action = 'created'; Hash = $sectionsHash }
}

Write-Host ''
Write-Host '========================================='
Write-Host 'Seed Summary' -ForegroundColor Cyan
Write-Host '========================================='
$summary | Format-Table -AutoSize Name, Id, Action, Hash

# -- Verification --
if (-not $WhatIf -and -not $SkipSchema) {
    Write-Host ''
    Write-Host 'Verification: querying sprk_issystem eq true...'
    try {
        $verify = Invoke-DataverseApi -Token $token -BaseUrl $DataverseUrl `
            -Endpoint "sprk_workspacelayouts?`$filter=sprk_issystem eq true&`$select=sprk_workspacelayoutid,sprk_name,sprk_layouttemplateid,sprk_isdefault,sprk_sortorder" -Method 'GET'
        Write-Host "  Found $($verify.value.Count) system layout(s)." -ForegroundColor Green
        $verify.value | Sort-Object sprk_sortorder | Format-Table -AutoSize sprk_name, sprk_layouttemplateid, sprk_isdefault, sprk_sortorder, sprk_workspacelayoutid
    }
    catch {
        Write-Host "  Verification query failed (column may need more time to settle): $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

Write-Host '========================================='
Write-Host 'Done.' -ForegroundColor Green
Write-Host '========================================='
