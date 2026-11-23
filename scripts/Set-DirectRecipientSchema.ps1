#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Adds the Direct message recipient column — sprk_directrecipient (schema sprk_DirectRecipient), a lookup to
    systemuser on sprk_communication (field-mapping-server-write-path-r1 task 054a piece 2; owner option A, 2026-11-09).
    Dry run by default; -Apply writes; -Verify checks. Idempotent.

.DESCRIPTION
    A Direct message records its own two participants: sprk_sentby and sprk_directrecipient. The BFF stamps both at
    create on Direct messages only (send, respond, inbound join, new Direct thread). The audience that
    unified-access-control-r2 task 198 grants is exactly this pair; it is never read from the thread.

    The script provisions, in order:
      (a) COLUMN    sprk_directrecipient on sprk_communication: a lookup to systemuser through the 1:N relationship
                    sprk_systemuser_sprk_communication_directrecipient. Cascade: NoCascade for Assign, Share, Unshare,
                    Reparent and Merge; RemoveLink for Delete. Nothing about the user moves or shares the message.
      (b) SOLUTION  The lookup and its relationship are in -SolutionUniqueName (default SpaarkeCore).
      (c) PUBLISH   sprk_communication.
      (d) NAMES     Prints the Web API navigation property and the read column from live metadata.

    NOT done here: field-level security for the column. Task 054b secures it, together with sprk_sentby and the thread
    lookup, as a separate live step that needs the owner's go.

    ⚠️ DEPLOY ORDER. A BFF build that stamps this column on Direct messages fails those creates until (a) has run.
    Apply this script, and see -Verify exit 0, in an environment BEFORE deploying such a BFF to it.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER SolutionUniqueName
    The unmanaged solution that carries the components. Default SpaarkeCore.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-DirectRecipientSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: each step's present / missing state; zero writes.

.EXAMPLE
    .\Set-DirectRecipientSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply

.EXAMPLE
    .\Set-DirectRecipientSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify

.NOTES
    field-mapping-server-write-path-r1 task 054 (projects/field-mapping-server-write-path-r1/tasks/054-direct-message-owner-team.poml).
    Pattern: scripts/Set-RecordCreatorPersonSchema.ps1. Auth: the operator's own az CLI identity. No secrets.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [string]$SolutionUniqueName = 'SpaarkeCore',
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants ───────────────────────────────────────────────────────────────────────────────────────────────
$Table = 'sprk_communication'
$Column = 'sprk_directrecipient'
$ColumnSchemaName = 'sprk_DirectRecipient'
$TargetEntity = 'systemuser'
$RelationshipSchemaName = 'sprk_systemuser_sprk_communication_directrecipient'
$ExpectedCascade = [ordered]@{ Assign = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade'; Merge = 'NoCascade'; Delete = 'RemoveLink' }

# ── Auth + helpers ──────────────────────────────────────────────────────────────────────────────────────────
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
# Metadata writes propagate asynchronously: poll a read until it answers, or give up loudly.
function Wait-DvRead([string]$Path, [string]$What, [int]$Attempts = 12, [int]$DelaySeconds = 10) {
    for ($i = 1; $i -le $Attempts; $i++) {
        $r = Try-DvGet $Path
        if ($r) { return $r }
        Write-Host "    waiting for $What to become readable ($i/$Attempts)..."
        Start-Sleep -Seconds $DelaySeconds
    }
    throw "$What was created but is still not readable after $($Attempts * $DelaySeconds)s; re-run the script (it is idempotent)."
}
function New-Label([string]$Text) {
    @{ '@odata.type' = 'Microsoft.Dynamics.CRM.Label'; LocalizedLabels = @(@{ '@odata.type' = 'Microsoft.Dynamics.CRM.LocalizedLabel'; Label = $Text; LanguageCode = 1033 }) }
}

$IsDryRun = -not $Apply.IsPresent
. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')
$gaps = [System.Collections.Generic.List[string]]::new()
function Report([string]$State, [string]$What) {
    $color = switch ($State) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } 'INFO' { 'Gray' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $State, $What) -ForegroundColor $color
    if ($State -in 'MISSING', 'FAIL') { $gaps.Add($What) }
}

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))
Write-Host "Solution    : $SolutionUniqueName"

# ── (a) COLUMN ──────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(a) $Table.$Column"
$attrPath = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=LogicalName,SchemaName,Targets,MetadataId"
$attr = Try-DvGet $attrPath
if ($attr) {
    $targets = @($attr.Targets)
    if ($targets.Count -eq 1 -and $targets[0] -eq $TargetEntity) { Report 'OK' "$Table.$Column (lookup -> $TargetEntity)" }
    else { Report 'FAIL' "$Table.$Column exists but targets [$($targets -join ', ')]; expected exactly $TargetEntity" }
} elseif ($Verify) { Report 'MISSING' "$Table.$Column" }
elseif ($IsDryRun) { Report 'WOULD' "create $Table.$Column (lookup -> $TargetEntity, relationship $RelationshipSchemaName)" }
else {
    Invoke-DvWrite POST 'RelationshipDefinitions' @{
        '@odata.type'               = 'Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata'
        SchemaName                  = $RelationshipSchemaName
        ReferencedEntity            = $TargetEntity
        ReferencingEntity           = $Table
        CascadeConfiguration        = @{ Assign = 'NoCascade'; Delete = 'RemoveLink'; Merge = 'NoCascade'; Reparent = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade' }
        AssociatedMenuConfiguration = @{ Behavior = 'DoNotDisplay'; Group = 'Details'; Order = 10000 }
        Lookup                      = @{
            '@odata.type' = 'Microsoft.Dynamics.CRM.LookupAttributeMetadata'
            SchemaName    = $ColumnSchemaName
            DisplayName   = (New-Label 'Direct Recipient')
            Description   = (New-Label 'The person a Direct (1:1) message was sent to. With Sent By, the two participants of the message. Stamped by the BFF at create on Direct messages only; empty on every other message. field-mapping-server-write-path-r1 task 054.')
            RequiredLevel = @{ Value = 'None' }
        }
    } @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName } | Out-Null
    Report 'DONE' "created $Table.$Column"
    $attr = Wait-DvRead $attrPath "$Table.$Column metadata"
    $set = (Invoke-DvGet "EntityDefinitions(LogicalName='$Table')?`$select=EntitySetName").EntitySetName
    Wait-DvRead "$set`?`$select=_$($Column)_value&`$top=1" "$Table.$Column in data queries" | Out-Null
}

$rel = Try-DvGet "RelationshipDefinitions(SchemaName='$RelationshipSchemaName')/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?`$select=SchemaName,ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntityNavigationPropertyName,CascadeConfiguration,MetadataId"
if ($rel) {
    $bad = @($ExpectedCascade.Keys | Where-Object { $rel.CascadeConfiguration.$_ -ne $ExpectedCascade[$_] } |
        ForEach-Object { "$_=$($rel.CascadeConfiguration.$_) (expected $($ExpectedCascade[$_]))" })
    if ($rel.ReferencingAttribute -ne $Column) { Report 'FAIL' "$RelationshipSchemaName references $($rel.ReferencingAttribute), not $Column" }
    elseif ($bad.Count -gt 0) { Report 'FAIL' "$RelationshipSchemaName cascade: $($bad -join '; ')" }
    else { Report 'OK' "$RelationshipSchemaName cascade (no Assign/Share/Reparent cascade; Delete RemoveLink)" }
} elseif ($attr) { Report 'FAIL' "$Table.$Column exists but relationship $RelationshipSchemaName was not found; it was created another way, check its cascade by hand" }
elseif ($Verify) { Report 'MISSING' "relationship $RelationshipSchemaName" }

# ── (b) SOLUTION ────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(b) Solution components"
$solution = @((Invoke-DvGet "solutions?`$select=solutionid,uniquename&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
if (-not $solution) { Report 'FAIL' "solution '$SolutionUniqueName' not found" }
else {
    $tableId = (Invoke-DvGet "EntityDefinitions(LogicalName='$Table')?`$select=MetadataId").MetadataId
    $components = [System.Collections.Generic.List[object]]::new()
    if ($attr) { $components.Add(@{ Id = $attr.MetadataId; Type = 2; Label = "$Table.$Column"; TableId = $tableId }) }
    if ($rel) { $components.Add(@{ Id = $rel.MetadataId; Type = 10; Label = "relationship $RelationshipSchemaName"; TableId = $tableId }) }
    if ($components.Count -eq 0) { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "add the column and relationship to $SolutionUniqueName (after they exist)" }
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

# ── (c) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($Apply) {
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
    Write-Host "`nPublished $Table." -ForegroundColor Green
}

# ── (d) NAMES (live metadata) ───────────────────────────────────────────────────────────────────────────────
if ($rel) {
    Write-Host "`n(d) Web API names (live metadata)"
    Report 'INFO' "logical name       : $Column"
    Report 'INFO' "schema name        : $($attr.SchemaName)"
    Report 'INFO' "navigation property: $($rel.ReferencingEntityNavigationPropertyName)  (bind: `"$($rel.ReferencingEntityNavigationPropertyName)@odata.bind`": `"/systemusers(<id>)`")"
    Report 'INFO' "read column        : _$($Column)_value"
}

if ($Verify) {
    if ($gaps.Count -eq 0) { Write-Host "`nVERIFY PASS: $Table.$Column is in place." -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
if ($IsDryRun) { Write-Host "`nDRY RUN complete; nothing was written. Re-run with -Apply." -ForegroundColor Cyan }
