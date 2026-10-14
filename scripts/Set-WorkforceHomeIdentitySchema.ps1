#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Applies the A9 workforce-home identity schema (external-access-platform-r3 task 044, design note a9-bff-design.md
    sections 4 and 5) to one Dataverse environment. Dry run by default; -Apply writes; -Verify checks (read-only);
    -Rollback removes it again (dry run unless combined with -Apply). Idempotent.

.DESCRIPTION
    A workforce user who signs in with their own company account (a "home token") is told apart from the A8 guest
    contacts and from the Spaarke-tenant workforce by a third identity plane, and is found by (tenant id, object id).
    This script adds, and only adds, exactly:

      (a) OPTION   sprk_identityplane (global choice, created by Set-ContactIdentityBindingSchema.ps1) gains
                   WorkforceHome = 100000002. The existing values 100000000 / 100000001 are never touched. If 100000002
                   is already taken by a different label the run STOPS (it never picks another value).
      (b) COLUMNS  all Text(36):
                     contact.sprk_externaltenantid     (FIELD-SECURED)  the home tenant id (tid) of a WorkforceHome contact
                     systemuser.sprk_hometenantid      (FIELD-SECURED)  the home tenant id of a licensed user's company account
                     systemuser.sprk_homeobjectid      (FIELD-SECURED)  the home object id (oid) of that account
                     systemuser.sprk_hometenantidkey   (NOT secured)    mirror of sprk_hometenantid, carries the key
                     systemuser.sprk_homeobjectidkey   (NOT secured)    mirror of sprk_homeobjectid, carries the key
      (c) KEY      One alternate key sprk_HomeIdentityUniqueKey on systemuser (sprk_hometenantidkey,
                   sprk_homeobjectidkey) - the mirrors. Dataverse refuses an alternate key on a field-secured column, so
                   the key sits on unsecured mirrors exactly as contact.sprk_externalobjectidkey does (owner round 4,
                   B2); owner decision 2026-10-14, option A. The script waits for the key's index to reach Active; a
                   Failed index STOPS the run (the script never deletes data).
      (d) FLS      The three secured columns get a permission row in the two EXISTING identity-link profiles, exactly
                   as sprk_externalobjectid has: "Spaarke Identity Link Readers" (Read) and "Spaarke Identity Link
                   Writers" (Read + Create + Update). Both profiles must already exist (created by
                   Set-ContactIdentityBindingSchema.ps1); this script never creates or substitutes a profile. Writer
                   and reader membership is ensured as in the reference script (members before the columns are secured).
      (e) SOLUTION Adds the new components to -SolutionUniqueName (default SpaarkeCore, as the reference script, so the
                   components land where the existing identity-binding columns did).
      (f) PUBLISH  contact, systemuser, sprk_identityplane.

    Nothing is deleted or altered in (a)-(f): an existing column with the wrong length, or a mirror that is field-secured,
    is reported FAIL and left as is.

    PLATFORM-RULE PREFLIGHT (no network): -Apply STOPS, before any write, if the key would sit on a field-secured column.

    ROLLBACK (-Rollback). Removes, in this order: the field-permission rows of the three secured columns in the two
    identity-link profiles; the alternate key (waits until it is gone); the five columns; the WorkforceHome option value;
    then publishes. It REFUSES, with nothing written, when any contact or systemuser row holds a value in any of the five
    columns, or any contact uses plane 100000002 (the values would be destroyed). It is a dry run unless -Apply is also given.
    It never touches the profiles themselves, their members, the global choice or any column this script did not add.
    NOTE: the secured columns are read with the operator's own identity; run it as a System Administrator, who reads every
    secured column. The unsecured mirrors and the plane column are checked as well.

    ORDER. Apply this schema BEFORE the BFF build that reads these columns ships (task 046), or the home sign-in fails
    closed with a missing-column error.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids whose Dataverse application users must be members of the writer profile - the BFF managed
    identity and, while it is still an application user, the BFF app registration. Required for -Apply and -Verify
    (not for -Rollback).

.PARAMETER SolutionUniqueName
    The unmanaged solution that carries the components. Default SpaarkeCore (as Set-ContactIdentityBindingSchema.ps1).

.PARAMETER Apply
    Write mode. Without it the script never writes. With -Rollback it executes the rollback.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply or -Rollback.

.PARAMETER Rollback
    Remove the schema this script adds. Dry run unless -Apply is also given.

.EXAMPLE
    .\Set-WorkforceHomeIdentitySchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c
    Dry run: every step's present / missing state; zero writes.

.EXAMPLE
    .\Set-WorkforceHomeIdentitySchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Apply

.EXAMPLE
    .\Set-WorkforceHomeIdentitySchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify

.EXAMPLE
    .\Set-WorkforceHomeIdentitySchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Rollback
    Rollback dry run: what would be removed, and whether it would be refused. Add -Apply to execute it.

.NOTES
    spaarke-SPA-external-access-platform-r3 task 044. Design: projects/spaarke-SPA-external-access-platform-r3/notes/a9-bff-design.md
    sections 4, 5, 8. Auth: the operator's own az CLI identity (System Administrator in the environment). No secrets.
    Copied in structure from scripts/Set-ContactIdentityBindingSchema.ps1.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [string[]]$BffApplicationIds = @(),
    [string]$SolutionUniqueName = 'SpaarkeCore',
    [switch]$Apply,
    [switch]$Verify,
    [switch]$Rollback
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
if ($Rollback -and $Verify) { throw '-Rollback and -Verify are separate modes.' }
if (($Apply -or $Verify) -and -not $Rollback -and $BffApplicationIds.Count -eq 0) {
    throw '-BffApplicationIds is required for -Apply and -Verify: the writer profile must name the BFF application user(s) explicitly.'
}
# A single comma-joined string (pwsh -File) is split, never sent as one id.
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$Api = "$EnvironmentUrl/api/data/v9.2"

# -- Constants (design note a9-bff-design.md sections 4 and 5; the BFF reads these exact names) ----------------------
$PlaneOptionSet = 'sprk_identityplane'
$WorkforceHomeValue = 100000002
$WorkforceHomeLabel = 'WorkforceHome'
$ExistingPlaneValues = @(100000000, 100000001)      # never touched
$ReaderProfileName = 'Spaarke Identity Link Readers'
$WriterProfileName = 'Spaarke Identity Link Writers'
$KeyEntity = 'systemuser'
$KeySchemaName = 'sprk_HomeIdentityUniqueKey'
$KeyAttributes = @('sprk_hometenantidkey', 'sprk_homeobjectidkey')   # the MIRRORS - never the secured columns
$ColumnLength = 36

# One entry per column this script adds. Secured = field-secured by (d); the key mirrors are NEVER secured.
$Columns = @(
    @{ Entity = 'contact'; Set = 'contacts'; IdName = 'contactid'; Logical = 'sprk_externaltenantid'; Schema = 'sprk_ExternalTenantId'
       Display = 'External Tenant Id'; Secured = $true
       Description = 'Home tenant id (tid) of a WorkforceHome contact: the Entra tenant of the company account the person signs in with. Written once with the binding (sprk_externalobjectid) by the BFF only (field-secured). A WorkforceHome contact is found by plane + this + the oid. A9, task 044.' },
    @{ Entity = 'systemuser'; Set = 'systemusers'; IdName = 'systemuserid'; Logical = 'sprk_hometenantid'; Schema = 'sprk_HomeTenantId'
       Display = 'Home Tenant Id'; Secured = $true
       Description = 'Home tenant id (tid) of the licensed user''s company account, linked to this user by the BFF after a two-sign-in proof. Written by the BFF only (field-secured). Looked up by this pair, never by the key mirrors. A9, task 044.' },
    @{ Entity = 'systemuser'; Set = 'systemusers'; IdName = 'systemuserid'; Logical = 'sprk_homeobjectid'; Schema = 'sprk_HomeObjectId'
       Display = 'Home Object Id'; Secured = $true
       Description = 'Home object id (oid) of the licensed user''s company account, linked to this user by the BFF after a two-sign-in proof. Written by the BFF only (field-secured). Looked up by this pair, never by the key mirrors. A9, task 044.' },
    @{ Entity = 'systemuser'; Set = 'systemusers'; IdName = 'systemuserid'; Logical = 'sprk_hometenantidkey'; Schema = 'sprk_HomeTenantIdKey'
       Display = 'Home Tenant Id (uniqueness key)'; Secured = $false
       Description = 'Mirror of sprk_hometenantid that carries the alternate key (Dataverse cannot key a field-secured column). Written by the BFF with the same value, in the same request, as the secured column. NOTHING resolves by it - it carries no identity; editing it can only block a link, never redirect a sign-in. A9, task 044.' },
    @{ Entity = 'systemuser'; Set = 'systemusers'; IdName = 'systemuserid'; Logical = 'sprk_homeobjectidkey'; Schema = 'sprk_HomeObjectIdKey'
       Display = 'Home Object Id (uniqueness key)'; Secured = $false
       Description = 'Mirror of sprk_homeobjectid that carries the alternate key (Dataverse cannot key a field-secured column). Written by the BFF with the same value, in the same request, as the secured column. NOTHING resolves by it - it carries no identity; editing it can only block a link, never redirect a sign-in. A9, task 044.' }
)
$SecuredFields = @($Columns | Where-Object { $_.Secured } | ForEach-Object { @{ Entity = $_.Entity; Attribute = $_.Logical } })

# -- PLATFORM-RULE PREFLIGHT (no network; runs before auth) --------------------------------------------------------
# Dataverse refuses an alternate key on a field-secured column: "Attributes must not have field-level security applied"
# (https://learn.microsoft.com/en-us/power-apps/developer/data-platform/define-alternate-keys-entity) and
# "Columns that have the Enable column security property enabled can't be used as an alternate key"
# (https://learn.microsoft.com/en-us/power-apps/maker/data-platform/define-alternate-keys-reference-records).
# The key therefore sits on the unsecured mirrors. This check keeps any later edit from putting a key column and
# column security on one column. -Apply STOPS here, before any write.
$PlatformConflicts = @()
$PlatformConflicts += @($SecuredFields | Where-Object { $_.Entity -eq $KeyEntity -and $KeyAttributes -contains $_.Attribute } |
    ForEach-Object { "$KeyEntity.$($_.Attribute) is both an alternate-key column (step c) and field-secured (step d); Dataverse allows only one" })
$PlatformConflicts += @($KeyAttributes | Where-Object { $k = $_; -not @($Columns | Where-Object { $_.Entity -eq $KeyEntity -and $_.Logical -eq $k }) } |
    ForEach-Object { "$KeyEntity.$_ is a key column but is not one of the columns this script defines" })
if ($Apply -and -not $Rollback -and $PlatformConflicts.Count -gt 0) {
    foreach ($c in $PlatformConflicts) { Write-Host "  BLOCKED  $c" -ForegroundColor Red }
    throw ('BLOCKED: the workforce-home schema asks Dataverse for an alternate key on a field-secured column, which ' +
        'the platform refuses. The key belongs on the unsecured mirrors (task 044, owner option A 2026-10-14). Nothing was written.')
}

# -- Auth + helpers ------------------------------------------------------------------------------------------------
$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    'Content-Type'     = 'application/json; charset=utf-8'
}
function Invoke-DvGet([string]$Path) { Invoke-RestMethod -Uri "$Api/$Path" -Headers $headers -Method Get }
function Invoke-DvWrite([string]$Method, [string]$Path, $Body, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $json = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 20 -Compress }
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method $Method -Body $json
}
function Try-DvGet([string]$Path) { try { Invoke-DvGet $Path } catch { $null } }
# Dataverse metadata writes propagate asynchronously: a column created a moment ago can still 404 on the next read.
# Poll a read until it answers, or give up loudly - never treat "not visible yet" as "absent".
function Wait-DvRead([string]$Path, [string]$What, [int]$Attempts = 12, [int]$DelaySeconds = 10) {
    for ($i = 1; $i -le $Attempts; $i++) {
        $r = Try-DvGet $Path
        if ($r) { return $r }
        Write-Host "    waiting for $What to become readable ($i/$Attempts)..."
        Start-Sleep -Seconds $DelaySeconds
    }
    throw "$What was created but is still not readable after $($Attempts * $DelaySeconds)s; re-run the script (it is idempotent)."
}
# Poll a condition (a script block returning a truthy value when met) - for reads that are not a plain GET.
function Wait-DvCondition([scriptblock]$Test, [string]$What, [int]$Attempts = 12, [int]$DelaySeconds = 10) {
    for ($i = 1; $i -le $Attempts; $i++) {
        $r = & $Test
        if ($r) { return $r }
        Write-Host "    waiting for $What ($i/$Attempts)..."
        Start-Sleep -Seconds $DelaySeconds
    }
    throw "Timed out after $($Attempts * $DelaySeconds)s waiting for $What; re-run the script (it is idempotent)."
}
function New-Label([string]$Text) {
    @{ '@odata.type' = 'Microsoft.Dynamics.CRM.Label'; LocalizedLabels = @(@{ '@odata.type' = 'Microsoft.Dynamics.CRM.LocalizedLabel'; Label = $Text; LanguageCode = 1033 }) }
}
function Get-AllPages([string]$Path) {
    $rows = @(); $next = "$Api/$Path"
    while ($next) {
        $page = Invoke-RestMethod -Uri $next -Headers ($headers + @{ Prefer = 'odata.maxpagesize=5000' }) -Method Get
        $rows += @($page.value); $next = $page.'@odata.nextLink'
    }
    return $rows
}
# The state of one column: $null when absent; otherwise its MetadataId, IsSecured, whether it is a string column, MaxLength.
function Get-ColumnState([string]$Entity, [string]$Logical) {
    $base = Try-DvGet "EntityDefinitions(LogicalName='$Entity')/Attributes(LogicalName='$Logical')?`$select=LogicalName,IsSecured,MetadataId"
    if (-not $base) { return $null }
    $str = Try-DvGet "EntityDefinitions(LogicalName='$Entity')/Attributes(LogicalName='$Logical')/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=MaxLength"
    [pscustomobject]@{
        MetadataId = $base.MetadataId
        IsSecured  = [bool]$base.IsSecured
        IsString   = [bool]$str
        MaxLength  = $(if ($str) { [int]$str.MaxLength } else { $null })
    }
}
function Get-KeyList([string]$Entity) {
    @((Invoke-DvGet "EntityDefinitions(LogicalName='$Entity')/Keys?`$select=SchemaName,KeyAttributes,EntityKeyIndexStatus,MetadataId").value)
}
function Find-Key($Keys, [string[]]$Attributes) {
    $want = (@($Attributes) | Sort-Object) -join ','
    $Keys | Where-Object { ((@($_.KeyAttributes) | Sort-Object) -join ',') -eq $want } | Select-Object -First 1
}
function Get-OptionLabel($Option) { $Option.Label.UserLocalizedLabel.Label }

$IsDryRun = -not $Apply.IsPresent
. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')
$gaps = [System.Collections.Generic.List[string]]::new()
function Report([string]$State, [string]$What) {
    $color = switch ($State) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $State, $What) -ForegroundColor $color
    if ($State -in 'MISSING', 'FAIL') { $gaps.Add($What) }
}

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
$modeText = if ($Verify) { 'VERIFY (read-only)' }
            elseif ($Rollback) { if ($IsDryRun) { 'ROLLBACK DRY RUN (no writes)' } else { 'ROLLBACK APPLY' } }
            elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }
Write-Host "Mode        : $modeText"
if (-not $Rollback) { Write-Host "Solution    : $SolutionUniqueName" }

# =====================================================================================================================
# ROLLBACK
# =====================================================================================================================
if ($Rollback) {
    $tableIds = @{}
    foreach ($e in 'contact', 'systemuser') { $tableIds[$e] = (Invoke-DvGet "EntityDefinitions(LogicalName='$e')?`$select=MetadataId").MetadataId }

    Write-Host "`nRollback preconditions (read-only)"
    $present = @{}
    foreach ($col in $Columns) { $present["$($col.Entity).$($col.Logical)"] = Get-ColumnState $col.Entity $col.Logical }

    $blockers = [System.Collections.Generic.List[string]]::new()
    foreach ($col in $Columns) {
        if (-not $present["$($col.Entity).$($col.Logical)"]) { continue }
        $rows = Get-AllPages "$($col.Set)?`$select=$($col.IdName)&`$filter=$($col.Logical) ne null"
        if ($rows.Count -gt 0) { $blockers.Add("$($col.Entity).$($col.Logical) holds a value on $($rows.Count) row(s)") }
    }
    if ($present['contact.sprk_externaltenantid']) {
        # The plane column is unsecured: it is a second, independent witness that a WorkforceHome contact exists.
        $planeRows = Get-AllPages "contacts?`$select=contactid&`$filter=sprk_identityplane eq $WorkforceHomeValue"
        if ($planeRows.Count -gt 0) { $blockers.Add("$($planeRows.Count) contact(s) use sprk_identityplane = $WorkforceHomeValue ($WorkforceHomeLabel)") }
    } else {
        $planeProbe = Try-DvGet "contacts?`$select=contactid&`$filter=sprk_identityplane eq $WorkforceHomeValue&`$top=1"
        if ($planeProbe -and @($planeProbe.value).Count -gt 0) { $blockers.Add("a contact uses sprk_identityplane = $WorkforceHomeValue ($WorkforceHomeLabel)") }
    }
    if ($blockers.Count -gt 0) {
        foreach ($b in $blockers) { Report 'FAIL' "ROLLBACK REFUSED - $b" }
        throw 'ROLLBACK REFUSED: removing the schema would destroy data. Nothing was written. Clear or move the values first (an operator decides).'
    }
    Report 'OK' 'no contact or systemuser row holds a value in any of the five columns, and no contact uses plane WorkforceHome'

    # R1 - field-permission rows of the three secured columns (our two profiles only).
    Write-Host "`nR1 Field-permission rows"
    foreach ($f in $SecuredFields) {
        $rows = @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$($f.Entity)' and attributelogicalname eq '$($f.Attribute)'").value)
        $others = @($rows | Where-Object { $_.fieldsecurityprofileid.name -notin $ReaderProfileName, $WriterProfileName })
        foreach ($o in $others) { Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' also holds a permission on $($f.Entity).$($f.Attribute) - remove it by hand, then re-run" }
        if ($others.Count -gt 0) { throw 'ROLLBACK STOPPED: another profile holds a permission on a column to be removed. Nothing further was written.' }
        if ($rows.Count -eq 0) { Report 'OK' "no permission rows on $($f.Entity).$($f.Attribute)"; continue }
        foreach ($r in $rows) {
            if ($IsDryRun) { Report 'WOULD' "delete $($r.fieldsecurityprofileid.name) permission on $($f.Entity).$($f.Attribute)"; continue }
            Invoke-DvWrite DELETE "fieldpermissions($($r.fieldpermissionid))" $null | Out-Null
            Report 'DONE' "deleted $($r.fieldsecurityprofileid.name) permission on $($f.Entity).$($f.Attribute)"
        }
    }

    # R2 - the alternate key (must go before its columns). Deleting a key is asynchronous: wait until it is gone.
    Write-Host "`nR2 Alternate key"
    $key = Find-Key (Get-KeyList $KeyEntity) $KeyAttributes
    if (-not $key) { Report 'OK' "no alternate key on $KeyEntity($($KeyAttributes -join ', '))" }
    elseif ($IsDryRun) { Report 'WOULD' "delete alternate key $($key.SchemaName)" }
    else {
        Invoke-DvWrite DELETE "EntityDefinitions($($tableIds[$KeyEntity]))/Keys($($key.MetadataId))" $null | Out-Null
        Wait-DvCondition { -not (Find-Key (Get-KeyList $KeyEntity) $KeyAttributes) } "alternate key $($key.SchemaName) to be removed" 36 10 | Out-Null
        Report 'DONE' "deleted alternate key $($key.SchemaName) (read back: gone)"
    }

    # R3 - the five columns (secured first, then the mirrors).
    Write-Host "`nR3 Columns"
    foreach ($col in @($Columns | Where-Object { $_.Secured }) + @($Columns | Where-Object { -not $_.Secured })) {
        $id = "$($col.Entity).$($col.Logical)"
        $s = $present[$id]
        if (-not $s) { Report 'OK' "$id is already absent"; continue }
        if ($IsDryRun) { Report 'WOULD' "delete $id"; continue }
        Invoke-DvWrite DELETE "EntityDefinitions($($tableIds[$col.Entity]))/Attributes($($s.MetadataId))" $null | Out-Null
        Wait-DvCondition { -not (Get-ColumnState $col.Entity $col.Logical) } "$id to be removed" | Out-Null
        Report 'DONE' "deleted $id (read back: gone)"
    }

    # R4 - the option value (only ours: the label must be WorkforceHome).
    Write-Host "`nR4 Option value"
    $planeSet = Try-DvGet "GlobalOptionSetDefinitions(Name='$PlaneOptionSet')"
    $opt = if ($planeSet) { @($planeSet.Options) | Where-Object { [int]$_.Value -eq $WorkforceHomeValue } | Select-Object -First 1 } else { $null }
    if (-not $opt) { Report 'OK' "option $WorkforceHomeValue is already absent from $PlaneOptionSet" }
    elseif ((Get-OptionLabel $opt) -ne $WorkforceHomeLabel) { Report 'FAIL' "option $WorkforceHomeValue on $PlaneOptionSet is labelled '$(Get-OptionLabel $opt)', not '$WorkforceHomeLabel' - not ours, left in place" }
    elseif ($IsDryRun) { Report 'WOULD' "delete option $WorkforceHomeValue '$WorkforceHomeLabel' from $PlaneOptionSet" }
    else {
        Invoke-DvWrite POST 'DeleteOptionValue' @{ OptionSetName = $PlaneOptionSet; Value = $WorkforceHomeValue } | Out-Null
        Wait-DvCondition { $s = Try-DvGet "GlobalOptionSetDefinitions(Name='$PlaneOptionSet')"; $s -and (@($s.Options | ForEach-Object { [int]$_.Value }) -notcontains $WorkforceHomeValue) } "option $WorkforceHomeValue to be removed" | Out-Null
        Report 'DONE' "deleted option $WorkforceHomeValue from $PlaneOptionSet (read back: gone)"
    }

    if ($Apply) {
        Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = '<importexportxml><entities><entity>contact</entity><entity>systemuser</entity></entities><optionsets><optionset>sprk_identityplane</optionset></optionsets></importexportxml>' } | Out-Null
        Write-Host "`nPublished contact, systemuser and $PlaneOptionSet." -ForegroundColor Green
        if ($gaps.Count -gt 0) { Write-Host "`nROLLBACK finished with $($gaps.Count) problem(s):" -ForegroundColor Red; $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }; exit 1 }
        Write-Host 'ROLLBACK complete.' -ForegroundColor Green
    } else {
        Write-Host "`nROLLBACK DRY RUN complete - nothing was written. Re-run with -Rollback -Apply." -ForegroundColor Cyan
    }
    exit 0
}

# =====================================================================================================================
# FORWARD: dry run / apply / verify
# =====================================================================================================================
Write-Host "`nPlatform rules"
foreach ($c in $PlatformConflicts) { Report 'FAIL' "BLOCKED - $c (the key belongs on the unsecured mirrors, task 044)" }
if ($PlatformConflicts.Count -eq 0) { Report 'OK' "no alternate-key column is field-secured (key on $KeyEntity mirrors $($KeyAttributes -join ', '); FLS on the three secured columns)" }

# -- Prerequisites: everything this script builds on must already exist, or it STOPS before any write -----------------
Write-Host "`nPrerequisites"
$prereqFailed = $false
$planeSet = Try-DvGet "GlobalOptionSetDefinitions(Name='$PlaneOptionSet')"
if (-not $planeSet) {
    Report 'FAIL' "global choice $PlaneOptionSet does not exist - run Set-ContactIdentityBindingSchema.ps1 first (STOP and escalate; no other choice is used)"
    $prereqFailed = $true
} else {
    Report 'OK' "global choice $PlaneOptionSet"
    $presentValues = @($planeSet.Options | ForEach-Object { [int]$_.Value })
    foreach ($v in $ExistingPlaneValues) {
        if ($presentValues -contains $v) { Report 'OK' "existing option $v unchanged" }
        else { Report 'FAIL' "existing option $v is missing from $PlaneOptionSet - this script never re-creates it; investigate"; $prereqFailed = $true }
    }
    $taken = @($planeSet.Options) | Where-Object { [int]$_.Value -eq $WorkforceHomeValue } | Select-Object -First 1
    if ($taken -and (Get-OptionLabel $taken) -ne $WorkforceHomeLabel) {
        Report 'FAIL' "option value $WorkforceHomeValue on $PlaneOptionSet is already taken by '$(Get-OptionLabel $taken)' - STOP and escalate (root CLAUDE.md section 6); another value is NOT picked"
        $prereqFailed = $true
    }
}
$profileIds = @{}
foreach ($name in $ReaderProfileName, $WriterProfileName) {
    $p = @((Invoke-DvGet "fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$name'").value) | Select-Object -First 1
    if ($p) { Report 'OK' "profile '$name'"; $profileIds[$name] = $p.fieldsecurityprofileid }
    else {
        Report 'FAIL' "profile '$name' not found - run Set-ContactIdentityBindingSchema.ps1 first (STOP and escalate; no other profile is used and none is created here)"
        $profileIds[$name] = $null; $prereqFailed = $true
    }
}
$readerId = $profileIds[$ReaderProfileName]; $writerId = $profileIds[$WriterProfileName]
$bffUsers = @()
foreach ($appId in $BffApplicationIds) {
    $u = @((Invoke-DvGet "systemusers?`$select=systemuserid,fullname&`$filter=applicationid eq $appId").value) | Select-Object -First 1
    if (-not $u) { Report 'FAIL' "no application user for appId $appId in this environment"; $prereqFailed = $true; continue }
    $bffUsers += $u
}
if ($Apply -and $prereqFailed) { throw 'A prerequisite is missing or taken (FAIL rows above). Nothing was written.' }

# -- (a) OPTION ----------------------------------------------------------------------------------------------------
Write-Host "`n(a) Option value on $PlaneOptionSet"
if ($planeSet) {
    $opt = @($planeSet.Options) | Where-Object { [int]$_.Value -eq $WorkforceHomeValue } | Select-Object -First 1
    if ($opt) { Report 'OK' "option $WorkforceHomeValue '$WorkforceHomeLabel' on $PlaneOptionSet" }
    elseif ($Verify) { Report 'MISSING' "option $WorkforceHomeValue '$WorkforceHomeLabel' on $PlaneOptionSet" }
    elseif ($IsDryRun) { Report 'WOULD' "add option $WorkforceHomeValue '$WorkforceHomeLabel' to $PlaneOptionSet" }
    else {
        Invoke-DvWrite POST 'InsertOptionValue' @{ OptionSetName = $PlaneOptionSet; Value = $WorkforceHomeValue; Label = (New-Label $WorkforceHomeLabel) } | Out-Null
        $planeSet = Wait-DvCondition {
            $s = Try-DvGet "GlobalOptionSetDefinitions(Name='$PlaneOptionSet')"
            if ($s -and (@($s.Options | ForEach-Object { [int]$_.Value }) -contains $WorkforceHomeValue)) { $s }
        } "option $WorkforceHomeValue on $PlaneOptionSet to be readable"
        $back = @($planeSet.Options) | Where-Object { [int]$_.Value -eq $WorkforceHomeValue } | Select-Object -First 1
        if ((Get-OptionLabel $back) -eq $WorkforceHomeLabel) { Report 'DONE' "added option $WorkforceHomeValue '$WorkforceHomeLabel' to $PlaneOptionSet (read back)" }
        else { Report 'FAIL' "option $WorkforceHomeValue read back as '$(Get-OptionLabel $back)', expected '$WorkforceHomeLabel'" }
    }
} else { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "option $WorkforceHomeValue '$WorkforceHomeLabel' (after $PlaneOptionSet exists)" }

# -- (b) COLUMNS ---------------------------------------------------------------------------------------------------
Write-Host "`n(b) Columns (Text $ColumnLength)"
$state = @{}
foreach ($col in $Columns) {
    $id = "$($col.Entity).$($col.Logical)"
    $s = Get-ColumnState $col.Entity $col.Logical
    if (-not $s) {
        if ($Verify) { Report 'MISSING' $id }
        elseif ($IsDryRun) { Report 'WOULD' "create $id (Text $ColumnLength, $(if ($col.Secured) { 'field-secured by step d' } else { 'NOT field-secured' }))" }
        else {
            Invoke-DvWrite POST "EntityDefinitions(LogicalName='$($col.Entity)')/Attributes" @{
                '@odata.type' = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'
                SchemaName    = $col.Schema
                DisplayName   = (New-Label $col.Display)
                Description   = (New-Label $col.Description)
                RequiredLevel = @{ Value = 'None' }
                MaxLength     = $ColumnLength
                FormatName    = @{ Value = 'Text' }
            } | Out-Null
            # The typed StringAttributeMetadata read can lag the base attribute read: wait for both, or IsString reads false.
            $s = Wait-DvCondition { $x = Get-ColumnState $col.Entity $col.Logical; if ($x -and $x.IsString) { $x } } "$id metadata to become readable"
            # The data endpoint can lag the metadata endpoint: later steps select the column, so wait for that too.
            Wait-DvRead "$($col.Set)?`$select=$($col.IdName),$($col.Logical)&`$top=1" "$id in data queries" | Out-Null
            Report 'DONE' "created $id (read back: Text, MaxLength $($s.MaxLength))"
        }
    } else {
        Report 'OK' $id
    }
    if ($s) {
        if (-not $s.IsString) { Report 'FAIL' "$id exists but is not a text column - this script never alters an existing column" }
        elseif ($s.MaxLength -ne $ColumnLength) { Report 'FAIL' "$id has MaxLength $($s.MaxLength), expected $ColumnLength - this script never alters an existing column" }
        if (-not $col.Secured -and $s.IsSecured) { Report 'FAIL' "$id is field-secured - a key mirror must NOT be secured (Dataverse cannot key a secured column); remove its column security" }
    }
    $state[$id] = $s
}

# -- (c) ALTERNATE KEY (on the unsecured mirrors) ----------------------------------------------------------------
Write-Host "`n(c) Alternate key on $KeyEntity ($($KeyAttributes -join ', '))"
$mirrorsReadable = -not ($KeyAttributes | Where-Object { -not $state["$KeyEntity.$_"] })
$keys = Get-KeyList $KeyEntity
foreach ($k in $keys | Where-Object { @($_.KeyAttributes) | Where-Object { $_ -in 'sprk_hometenantid', 'sprk_homeobjectid' } }) {
    Report 'FAIL' "alternate key $($k.SchemaName) includes a field-secured home column - Dataverse will refuse its column security; the key belongs on the mirrors"
}
$key = Find-Key $keys $KeyAttributes
function Wait-KeyActive {
    for ($i = 1; $i -le 60; $i++) {
        $k = Find-Key (Get-KeyList $KeyEntity) $KeyAttributes
        if ($k -and $k.EntityKeyIndexStatus -eq 'Active') { return $k }
        if ($k -and $k.EntityKeyIndexStatus -eq 'Failed') {
            throw "Alternate key $KeySchemaName index status is Failed. STOP and escalate with the key's async job error (existing data conflict?); do not delete data."
        }
        Write-Host "    waiting for $KeySchemaName index to reach Active (status $(if ($k) { $k.EntityKeyIndexStatus } else { 'not yet listed' }); $i/60)..."
        Start-Sleep -Seconds 10
    }
    throw "Alternate key $KeySchemaName did not reach Active within 10 minutes; re-run the script (it is idempotent) or -Verify."
}
if ($key) {
    if ($key.EntityKeyIndexStatus -eq 'Active') { Report 'OK' "$($key.SchemaName) is Active" }
    elseif ($Verify -or $IsDryRun) { Report 'FAIL' "$($key.SchemaName) index status is $($key.EntityKeyIndexStatus) (not Active)" }
    else { $key = Wait-KeyActive; Report 'DONE' "$($key.SchemaName) reached Active" }
} elseif ($Verify) { Report 'MISSING' "alternate key on $KeyEntity($($KeyAttributes -join ', '))" }
elseif ($IsDryRun -or -not $mirrorsReadable) { Report 'WOULD' "create alternate key $KeySchemaName on $KeyEntity($($KeyAttributes -join ', '))" }
else {
    # Refuse to create the key while two users would share a mirror pair (the index would fail).
    $mirrored = Get-AllPages "systemusers?`$select=systemuserid,$($KeyAttributes -join ',')&`$filter=$($KeyAttributes[0]) ne null and $($KeyAttributes[1]) ne null"
    $dupes = @($mirrored | Group-Object { "$($_.($KeyAttributes[0]))|$($_.($KeyAttributes[1]))".ToLowerInvariant() } | Where-Object Count -gt 1)
    foreach ($d in $dupes) { Report 'FAIL' "mirror pair $($d.Name) is carried by $($d.Count) users: $(($d.Group | ForEach-Object systemuserid) -join ', ')" }
    if ($dupes.Count -gt 0) { throw 'Duplicate mirror pairs found; the uniqueness key cannot be created until an operator resolves them. Nothing further was written.' }
    Invoke-DvWrite POST "EntityDefinitions(LogicalName='$KeyEntity')/Keys" @{
        SchemaName    = $KeySchemaName
        DisplayName   = (New-Label 'Home Identity (unique)')
        KeyAttributes = $KeyAttributes
    } | Out-Null
    Report 'DONE' "created $KeySchemaName (the index builds asynchronously)"
    $key = Wait-KeyActive
    Report 'DONE' "$($key.SchemaName) reached Active (read back)"
}

# -- (d) FIELD-LEVEL SECURITY ---------------------------------------------------------------------------------------
Write-Host "`n(d) Field-level security"
$defaultTeams = @((Invoke-DvGet 'teams?$select=teamid,name&$filter=isdefault eq true').value)
function Ensure-Member([string]$ProfileId, [string]$ProfileName, [string]$Nav, [string]$Set, $Id, [string]$Label) {
    if (-not $ProfileId) { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "$ProfileName member: $Label"; return }
    $existing = @((Invoke-DvGet "fieldsecurityprofiles($ProfileId)/$Nav`?`$select=$(if ($Set -eq 'teams') { 'teamid' } else { 'systemuserid' })").value)
    $key = if ($Set -eq 'teams') { 'teamid' } else { 'systemuserid' }
    if ($existing | Where-Object { $_.$key -eq $Id }) { Report 'OK' "$ProfileName member: $Label"; return }
    if ($Verify) { Report 'MISSING' "$ProfileName member: $Label"; return }
    if ($IsDryRun) { Report 'WOULD' "add $Label to $ProfileName"; return }
    Invoke-DvWrite POST "fieldsecurityprofiles($ProfileId)/$Nav/`$ref" @{ '@odata.id' = "$Api/$Set($Id)" } | Out-Null
    Report 'DONE' "added $Label to $ProfileName"
}
# Members first, so the BFF keeps Read from the moment the columns are secured.
foreach ($u in $bffUsers) { Ensure-Member $writerId $WriterProfileName 'systemuserprofiles_association' 'systemusers' $u.systemuserid "app user '$($u.fullname)'" }
foreach ($t in $defaultTeams) { Ensure-Member $readerId $ReaderProfileName 'teamprofiles_association' 'teams' $t.teamid "default team '$($t.name)'" }

foreach ($f in $SecuredFields) {
    $id = "$($f.Entity).$($f.Attribute)"
    $s = $state[$id]
    if (-not $s) {
        Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "secure $id and grant both identity-link profiles on it (after the column exists)"
        continue
    }
    if ($s.IsSecured) { Report 'OK' "$id is field-secured" }
    elseif ($Verify) { Report 'MISSING' "$id is NOT field-secured - anyone with Write could change whose identity it names" }
    elseif ($IsDryRun) { Report 'WOULD' "secure $id" }
    else {
        $typed = Invoke-DvGet "EntityDefinitions(LogicalName='$($f.Entity)')/Attributes(LogicalName='$($f.Attribute)')"
        $typed.IsSecured = $true
        Invoke-DvWrite PUT "EntityDefinitions(LogicalName='$($f.Entity)')/Attributes(LogicalName='$($f.Attribute)')" $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
        $back = Wait-DvCondition { $c = Try-DvGet "EntityDefinitions(LogicalName='$($f.Entity)')/Attributes(LogicalName='$($f.Attribute)')?`$select=IsSecured"; $c -and $c.IsSecured } "$id to read back as secured"
        Report 'DONE' "secured $id (read back)"
    }

    foreach ($spec in @(@{ Id = $readerId; Name = $ReaderProfileName; Create = 0; Update = 0 }, @{ Id = $writerId; Name = $WriterProfileName; Create = 4; Update = 4 })) {
        if (-not $spec.Id) { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "$($spec.Name) permission on $id"; continue }
        $perm = @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $($spec.Id) and entityname eq '$($f.Entity)' and attributelogicalname eq '$($f.Attribute)'").value) | Select-Object -First 1
        if ($perm) {
            if ($perm.canread -eq 4 -and $perm.cancreate -eq $spec.Create -and $perm.canupdate -eq $spec.Update) { Report 'OK' "$($spec.Name) on $id" }
            else { Report 'FAIL' "$($spec.Name) on $id is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$($spec.Create) update=$($spec.Update)" }
            continue
        }
        if ($Verify) { Report 'MISSING' "$($spec.Name) on $id"; continue }
        if ($IsDryRun) { Report 'WOULD' "grant $($spec.Name) on $id (read=4 create=$($spec.Create) update=$($spec.Update))"; continue }
        # Securing a column propagates asynchronously: the grant after securing can be refused 0x8004f508
        # "... is NOT secured for entity fieldpermission". Retry that one error; anything else propagates.
        for ($attempt = 1; ; $attempt++) {
            try {
                Invoke-DvWrite POST 'fieldpermissions' @{
                    entityname = $f.Entity; attributelogicalname = $f.Attribute
                    canread = 4; cancreate = $spec.Create; canupdate = $spec.Update
                    'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($($spec.Id))"
                } | Out-Null
                break
            } catch {
                $notYetSecured = "$($_.ErrorDetails.Message) $($_.Exception.Message)" -match '0x8004f508'
                if (-not $notYetSecured -or $attempt -ge 12) { throw }
                Write-Host "    $id not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
                Start-Sleep -Seconds 10
            }
        }
        $back = @((Invoke-DvGet "fieldpermissions?`$select=canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $($spec.Id) and entityname eq '$($f.Entity)' and attributelogicalname eq '$($f.Attribute)'").value) | Select-Object -First 1
        if ($back -and $back.canread -eq 4 -and $back.cancreate -eq $spec.Create -and $back.canupdate -eq $spec.Update) { Report 'DONE' "granted $($spec.Name) on $id (read back)" }
        else { Report 'FAIL' "granted $($spec.Name) on $id but the read-back does not match read=4 create=$($spec.Create) update=$($spec.Update)" }
    }

    # Nobody else may write: any other profile granting create/update (besides the platform's System
    # Administrator profile) is reported - the lock is only as strong as its narrowest writer list.
    if ($s.IsSecured -or $Apply) {
        $writers = @((Invoke-DvGet "fieldpermissions?`$select=canupdate,cancreate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$($f.Entity)' and attributelogicalname eq '$($f.Attribute)' and (canupdate eq 4 or cancreate eq 4)").value)
        foreach ($w in $writers | Where-Object { $_.fieldsecurityprofileid.name -notin $WriterProfileName, 'System Administrator' }) {
            Report 'FAIL' "profile '$($w.fieldsecurityprofileid.name)' can also write $id"
        }
    }
}

# -- (e) SOLUTION --------------------------------------------------------------------------------------------------
Write-Host "`n(e) Solution components"
$solution = @((Invoke-DvGet "solutions?`$select=solutionid,uniquename&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
if (-not $solution) { Report 'FAIL' "solution '$SolutionUniqueName' not found" }
else {
    $components = [System.Collections.Generic.List[object]]::new()
    $tableIds = @{}
    foreach ($e in 'contact', 'systemuser') { $tableIds[$e] = (Invoke-DvGet "EntityDefinitions(LogicalName='$e')?`$select=MetadataId").MetadataId }
    $m = Try-DvGet "GlobalOptionSetDefinitions(Name='$PlaneOptionSet')?`$select=MetadataId"
    if ($m) { $components.Add(@{ Id = $m.MetadataId; Type = 9; Label = "choice $PlaneOptionSet" }) }
    foreach ($col in $Columns) {
        $s = $state["$($col.Entity).$($col.Logical)"]
        if ($s) { $components.Add(@{ Id = $s.MetadataId; Type = 2; Label = "$($col.Entity).$($col.Logical)"; TableId = $tableIds[$col.Entity] }) }
    }
    $k = Find-Key (Get-KeyList $KeyEntity) $KeyAttributes
    if ($k) { $components.Add(@{ Id = $k.MetadataId; Type = 14; Label = "key $KeySchemaName"; TableId = $tableIds[$KeyEntity] }) }
    foreach ($profileId in $readerId, $writerId) { if ($profileId) { $components.Add(@{ Id = $profileId; Type = 70; Label = "field security profile $profileId" }) } }

    $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid
    foreach ($c in $components) {
        $how = Test-DvInSolution -Membership $membership -ComponentId $c.Id -TableMetadataId $c['TableId']
        if ($how -eq 'Direct') { Report 'OK' "$($c.Label) in $SolutionUniqueName"; continue }
        if ($how -eq 'ViaTable') { Report 'OK' "$($c.Label) in $SolutionUniqueName (its table includes subcomponents)"; continue }
        if ($Verify) { Report 'MISSING' "$($c.Label) in $SolutionUniqueName"; continue }
        if ($IsDryRun) { Report 'WOULD' "add $($c.Label) to $SolutionUniqueName"; continue }
        Invoke-DvWrite POST 'AddSolutionComponent' @{ ComponentId = $c.Id; ComponentType = $c.Type; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false } | Out-Null
        Report 'DONE' "added $($c.Label) to $SolutionUniqueName"
    }
}

# -- (f) PUBLISH ---------------------------------------------------------------------------------------------------
if ($Apply) {
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = '<importexportxml><entities><entity>contact</entity><entity>systemuser</entity></entities><optionsets><optionset>sprk_identityplane</optionset></optionsets></importexportxml>' } | Out-Null
    Write-Host "`nPublished contact, systemuser and $PlaneOptionSet." -ForegroundColor Green
}

if ($Verify) {
    if ($gaps.Count -eq 0) { Write-Host "`nVERIFY PASS: the workforce-home identity schema is complete." -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
if ($IsDryRun) {
    if ($gaps.Count -gt 0) { Write-Host "`nDRY RUN found $($gaps.Count) problem(s) that -Apply would not fix:" -ForegroundColor Red; $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red } }
    Write-Host "`nDRY RUN complete - nothing was written. Re-run with -Apply." -ForegroundColor Cyan
    exit 0
}
if ($gaps.Count -gt 0) {
    Write-Host "`nAPPLY finished with $($gaps.Count) problem(s) (FAIL rows above):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "`nAPPLY complete. Run -Verify next." -ForegroundColor Green
