<#
.SYNOPSIS
    Apply Compose R1 Dataverse customizations (Spike #3 §7.2 OI-1 + OI-2).

.DESCRIPTION
    Two schema-side changes required by Spike #3 (`notes/spikes/spike-3-spe-checkout-promotion.md`)
    that gate Phase 2 ComposeDocumentService (task 022) and Phase 5 heartbeat sweeper (task 052):

      OI-1: Alternate Key on sprk_document(sprk_graphitemid)
            - Required for deterministic Save-promotion idempotency under concurrent calls
              (spike §5 algorithm: probe-then-create + race-aware retry hinges on this).
            - Without the key, two concurrent POSTs CAN create two rows with the same
              sprk_graphitemid, breaking the Promote-on-Save invariant.

      OI-2: Add sprk_lastheartbeatutc field (DateTime, nullable) to sprk_document
            - Required for the 3-min heartbeat + 15-min stale-sweep mechanism locked in
              spike §4. Distinct field (NOT sprk_checkedoutdate) so UX "checked out since"
              is not clobbered every 3 minutes (spike §2.3).

    The script is idempotent: each operation checks current state and skips if already applied.

    Mirrors the established two-half pattern from scripts/Deploy-SystemWorkspaceLayouts.ps1
    (Round-8 Wave 2a, task 108) — direct Dataverse metadata Web API via az CLI token; no PAC CLI.

.PARAMETER DataverseUrl
    The Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER WhatIf
    Dry-run mode. Prints what would be added without writing to Dataverse.

.PARAMETER SkipAlternateKey
    Skip OI-1 (Alternate Key). Useful if applying OI-2 only.

.PARAMETER SkipHeartbeatField
    Skip OI-2 (sprk_lastheartbeatutc field). Useful if applying OI-1 only.

.EXAMPLE
    .\Deploy-ComposeDataverseCustomizations.ps1 -WhatIf
    # Dry-run against dev — no writes; prints what would be added.

.EXAMPLE
    .\Deploy-ComposeDataverseCustomizations.ps1
    # Apply both OI-1 + OI-2 in dev. Idempotent — re-running is a no-op.

.NOTES
    Project: spaarkeai-compose-r1
    Task: 010 (Spike #3 §7.2 OI-1 + OI-2 fold-ins)
    Created: 2026-06-29
    Author: spaarke-dev (Claude Opus 4.7 sub-agent)

    REVIEW BEFORE RUNNING. Schema changes to sprk_document are hot-path
    (14 active projects touch BFF; 8 touch SpaarkeAi). The Alternate Key
    adds a uniqueness constraint that COULD break existing data if any
    sprk_document rows duplicate sprk_graphitemid. The script's pre-check
    block warns if duplicates are detected; do NOT run -Force until reviewed.
#>

[CmdletBinding(SupportsShouldProcess = $false)]
param(
    [Parameter(Mandatory = $false)]
    [string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com',

    [Parameter(Mandatory = $false)]
    [switch]$WhatIf,

    [Parameter(Mandatory = $false)]
    [switch]$SkipAlternateKey,

    [Parameter(Mandatory = $false)]
    [switch]$SkipHeartbeatField
)

$ErrorActionPreference = 'Stop'
$AZ = 'C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd'

# ============================================================================
# Helper Functions (mirror Deploy-SystemWorkspaceLayouts.ps1 patterns)
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

function Test-AlternateKeyExists {
    param([string]$Token, [string]$BaseUrl, [string]$EntityLogicalName, [string]$KeyAttributeName)
    try {
        # Query the entity's Keys collection; check if any key targets the given attribute
        $resp = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Keys" `
            -Method 'GET'
        if ($null -eq $resp.value) { return $false }
        foreach ($key in $resp.value) {
            if ($key.KeyAttributes -contains $KeyAttributeName) { return $true }
        }
        return $false
    }
    catch {
        Write-Host "    Warning: alternate-key existence check failed: $($_.Exception.Message)" -ForegroundColor Yellow
        return $false
    }
}

function Test-DuplicateGraphItemIds {
    param([string]$Token, [string]$BaseUrl)
    # Quick pre-check: find any sprk_graphitemid values that appear more than once.
    # If duplicates exist, adding the alternate key would fail. Operator must reconcile first.
    Write-Host '    Scanning sprk_document for duplicate sprk_graphitemid values...' -ForegroundColor Gray
    try {
        # Note: Dataverse Web API does not support GROUP BY HAVING via OData $apply, so we
        # use a heuristic: pull the top 5000 non-null graphitemid values and count client-side.
        # For 5000+ document tenants, the operator should run a SQL-style audit separately.
        $url = "sprk_documents?`$filter=sprk_graphitemid ne null&`$select=sprk_graphitemid&`$top=5000"
        $resp = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint $url -Method 'GET'
        if ($null -eq $resp.value -or $resp.value.Count -eq 0) {
            Write-Host '    No sprk_document rows with sprk_graphitemid found — safe to add key.' -ForegroundColor Green
            return $false
        }
        $groups = $resp.value | Group-Object -Property sprk_graphitemid | Where-Object { $_.Count -gt 1 }
        if ($groups.Count -gt 0) {
            Write-Host "    WARNING: Found $($groups.Count) duplicate sprk_graphitemid value(s):" -ForegroundColor Red
            $groups | ForEach-Object { Write-Host "      - $($_.Name) ($($_.Count) rows)" -ForegroundColor Red }
            return $true
        }
        Write-Host "    No duplicates among $($resp.value.Count) row(s) scanned." -ForegroundColor Green
        if ($resp.value.Count -eq 5000) {
            Write-Host '    (Scan limited to first 5000 rows — run a Power Apps Advanced Find or XrmToolBox audit for larger volumes.)' -ForegroundColor Yellow
        }
        return $false
    }
    catch {
        Write-Host "    Pre-check query failed: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host '    Cannot verify duplicate-free state; proceed with caution.' -ForegroundColor Yellow
        return $false
    }
}

# ============================================================================
# OI-1: Alternate Key on sprk_document(sprk_graphitemid)
# ============================================================================

function Add-GraphItemIdAlternateKey {
    param([string]$Token, [string]$BaseUrl, [switch]$DryRun)

    $entity = 'sprk_document'
    $keyName = 'sprk_graphitemid'
    $schemaKeyName = 'sprk_graphitemid_uk'

    Write-Host "  Checking $entity.alternate-key($keyName)..." -ForegroundColor Gray

    if (Test-AlternateKeyExists -Token $Token -BaseUrl $BaseUrl -EntityLogicalName $entity -KeyAttributeName $keyName) {
        Write-Host '    Already exists — no schema change needed.' -ForegroundColor Green
        return @{ Created = $false; AlreadyPresent = $true }
    }

    # Pre-check: warn on duplicates before attempting to add the key
    $hasDuplicates = Test-DuplicateGraphItemIds -Token $Token -BaseUrl $BaseUrl
    if ($hasDuplicates -and -not $DryRun) {
        throw "Cannot add alternate key: duplicate sprk_graphitemid values detected. Reconcile data first."
    }

    if ($DryRun) {
        Write-Host "    [WhatIf] Would add alternate key '$schemaKeyName' on attribute $keyName" -ForegroundColor Yellow
        return @{ Created = $true; DryRun = $true }
    }

    Write-Host "    Adding alternate key '$schemaKeyName' on $entity.$keyName..." -ForegroundColor Yellow

    $keyDef = @{
        '@odata.type'   = 'Microsoft.Dynamics.CRM.EntityKeyMetadata'
        'KeyAttributes' = @($keyName)
        'SchemaName'    = $schemaKeyName
        'DisplayName'   = (New-Label -Text 'SPE Drive-Item ID (Unique)')
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions(LogicalName='$entity')/Keys" `
        -Method 'POST' -Body $keyDef | Out-Null

    Write-Host "    Created alternate key. Indexing may take 1-5 minutes; promote-on-Save tests may be flaky during that window." -ForegroundColor Green
    return @{ Created = $true }
}

# ============================================================================
# OI-2: sprk_lastheartbeatutc field on sprk_document
# ============================================================================

function Add-LastHeartbeatUtcField {
    param([string]$Token, [string]$BaseUrl, [switch]$DryRun)

    $entity = 'sprk_document'
    $attr = 'sprk_lastheartbeatutc'
    Write-Host "  Checking $entity.$attr..." -ForegroundColor Gray

    if (Test-AttributeExists -Token $Token -BaseUrl $BaseUrl -EntityLogicalName $entity -AttributeLogicalName $attr) {
        Write-Host '    Already exists — no schema change needed.' -ForegroundColor Green
        return @{ Created = $false; AlreadyPresent = $true }
    }

    if ($DryRun) {
        Write-Host "    [WhatIf] Would add DateTime column $attr (DisplayName='Last Heartbeat UTC', nullable, default null)" -ForegroundColor Yellow
        return @{ Created = $true; DryRun = $true }
    }

    Write-Host "    Adding DateTime column $attr..." -ForegroundColor Yellow

    $attributeDef = @{
        '@odata.type'   = 'Microsoft.Dynamics.CRM.DateTimeAttributeMetadata'
        'SchemaName'    = 'sprk_LastHeartbeatUtc'
        'RequiredLevel' = @{ 'Value' = 'None' }
        'DisplayName'   = (New-Label -Text 'Last Heartbeat UTC')
        'Description'   = (New-Label -Text 'Set by ComposeHeartbeatService while a Compose session is actively editing this document. Sweeper compares against UtcNow-15min to detect orphan locks. Distinct from sprk_checkedoutdate (which is the immutable lock-acquired timestamp).')
        'Format'        = 'DateAndTime'
        'DateTimeBehavior' = @{ 'Value' = 'UserLocal' }
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions(LogicalName='$entity')/Attributes" `
        -Method 'POST' -Body $attributeDef | Out-Null

    Write-Host "    Created $attr on $entity." -ForegroundColor Green
    return @{ Created = $true }
}

# ============================================================================
# Main
# ============================================================================

Write-Host '========================================='
Write-Host 'Compose R1 Dataverse Customizations'
Write-Host "Target  : $DataverseUrl"
Write-Host "Mode    : $(if ($WhatIf) {'WhatIf (dry-run)'} else {'Live'})"
Write-Host "OI-1    : $(if ($SkipAlternateKey) {'SKIP'} else {'Verify + Add if missing'})"
Write-Host "OI-2    : $(if ($SkipHeartbeatField) {'SKIP'} else {'Verify + Add if missing'})"
Write-Host '========================================='

Write-Host '[1/3] Authenticating...'
$token = Get-DataverseToken -EnvironmentUrl $DataverseUrl
Write-Host '      OK' -ForegroundColor Green

$results = @()

# OI-1
Write-Host ''
Write-Host '[2/3] OI-1: Alternate Key on sprk_document(sprk_graphitemid)'
if ($SkipAlternateKey) {
    Write-Host '      Skipped (-SkipAlternateKey)' -ForegroundColor Yellow
    $results += [PSCustomObject]@{ Customization = 'OI-1 alternate-key'; Status = 'skipped' }
} else {
    $oi1 = Add-GraphItemIdAlternateKey -Token $token -BaseUrl $DataverseUrl -DryRun:$WhatIf
    $status = if ($oi1.AlreadyPresent) { 'already-present' } elseif ($oi1.DryRun) { 'would-create' } else { 'created' }
    $results += [PSCustomObject]@{ Customization = 'OI-1 alternate-key'; Status = $status }

    if ($oi1.Created -and -not $WhatIf) {
        Write-Host '      Waiting 10s for index to begin building...' -ForegroundColor Gray
        Start-Sleep -Seconds 10
    }
}

# OI-2
Write-Host ''
Write-Host '[3/3] OI-2: sprk_lastheartbeatutc field on sprk_document'
if ($SkipHeartbeatField) {
    Write-Host '      Skipped (-SkipHeartbeatField)' -ForegroundColor Yellow
    $results += [PSCustomObject]@{ Customization = 'OI-2 heartbeat-field'; Status = 'skipped' }
} else {
    $oi2 = Add-LastHeartbeatUtcField -Token $token -BaseUrl $DataverseUrl -DryRun:$WhatIf
    $status = if ($oi2.AlreadyPresent) { 'already-present' } elseif ($oi2.DryRun) { 'would-create' } else { 'created' }
    $results += [PSCustomObject]@{ Customization = 'OI-2 heartbeat-field'; Status = $status }
}

Write-Host ''
Write-Host '========================================='
Write-Host 'Summary' -ForegroundColor Cyan
Write-Host '========================================='
$results | Format-Table -AutoSize

Write-Host ''
Write-Host '========================================='
Write-Host 'Done.' -ForegroundColor Green
Write-Host '========================================='
Write-Host ''
Write-Host 'Next steps:' -ForegroundColor Cyan
Write-Host '  1. Verify OI-1 in Power Apps maker portal (Tables > sprk_document > Keys) OR'
Write-Host '     re-run this script — it is idempotent.'
Write-Host '  2. Verify OI-2 in maker portal (Tables > sprk_document > Columns > Last Heartbeat UTC).'
Write-Host '  3. Add both customizations to the project solution (sprk_compose_r1) and export'
Write-Host '     for downstream environment promotion.'
Write-Host '  4. Unblock tasks 022 (ComposeDocumentService.PromoteEphemeralAsync) + 052'
Write-Host '     (StaleCheckoutSweeperHostedService).'
