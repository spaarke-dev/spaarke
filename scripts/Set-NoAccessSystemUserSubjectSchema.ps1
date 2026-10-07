#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Adds the No Access list's third subject — sprk_subjectsystemuser, a lookup to systemuser — to sprk_noaccessentry
    (unified-access-control-r2 task 143; owner round 2 Q4, N1 accepted as recommended). Dry run by default; -Apply
    writes; -Verify checks. Idempotent.

.DESCRIPTION
    WHY. The owner's rule (round 2 Q4): the No Access list applies to INTERNAL users on secure records. Until now an
    entry could name only a contact or an organization, so an internal user was walled only through a linked contact —
    which only 1 of 8 dev users had (session 27). This column lets an entry name the systemuser directly.

    The script provisions, in order:
      (a) COLUMN     sprk_subjectsystemuser (schema sprk_SubjectSystemUser) on sprk_noaccessentry: a lookup to systemuser
                     through a 1:N relationship sprk_systemuser_sprk_noaccessentry_subjectsystemuser whose cascade is
                     NoCascade for Assign/Share/Unshare/Reparent/Merge and RemoveLink for Delete — the same RemoveLink
                     the contact and organization subjects use (schema doc), so deleting a user never cascades into or
                     blocks on the list; an entry left without a subject is malformed and denies nothing.
      (b) SOLUTION   the lookup and its relationship are in -SolutionUniqueName (SpaarkeCore).
      (c) PUBLISH    sprk_noaccessentry.
    Then it waits until the column answers a DATA query (Dataverse propagates metadata asynchronously).

    ⚠️ DEPLOY ORDER — BINDING. A BFF build carrying task 143 SELECTS _sprk_subjectsystemuser_value on every No Access
    read (NoAccessListReader.RowSelect). In an environment without the column every such read 400s, fails CLOSED, and
    denies every queried record — an outage on the contact AND systemuser planes. Run -Apply, then -Verify (exit 0),
    then deploy the BFF. Never the reverse.

    Not done here (other tasks): putting the field on the entry form (task 154, which also registers the post-save
    script), and the access-administrator role that holds Read/Write on the table (owner O2; tasks 154/064).

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER SolutionUniqueName
    The unmanaged solution that carries the components. Default SpaarkeCore.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-NoAccessSystemUserSubjectSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: present / missing for each step; zero writes.

.EXAMPLE
    .\Set-NoAccessSystemUserSubjectSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply

.EXAMPLE
    .\Set-NoAccessSystemUserSubjectSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify

.NOTES
    unified-access-control-r2 task 143 (#1066). Code: src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/NoAccessListReader.cs
    (RowSelect names _sprk_subjectsystemuser_value). Schema doc: src/solutions/SpaarkeCore/entities/sprk_noaccessentry/entity-schema.md.
    Auth: the operator's own az CLI identity (System Administrator in the environment). No secrets.
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

# ── Constants (NoAccessListReader reads _sprk_subjectsystemuser_value) ─────────────────────────────────────
$Table = 'sprk_noaccessentry'
$EntitySet = 'sprk_noaccessentries'
$Column = 'sprk_subjectsystemuser'
$ColumnSchemaName = 'sprk_SubjectSystemUser'
$TargetEntity = 'systemuser'
$RelationshipSchemaName = 'sprk_systemuser_sprk_noaccessentry_subjectsystemuser'
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
# Metadata writes propagate asynchronously (task 141 live run 2026-10-02): poll until the read answers, or give up loudly.
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
    $color = switch ($State) { 'OK' { 'Green' } 'MISSING' { 'Yellow' } 'WOULD' { 'Cyan' } 'DONE' { 'Green' } default { 'Red' } }
    Write-Host ("  {0,-8} {1}" -f $State, $What) -ForegroundColor $color
    if ($State -in 'MISSING', 'FAIL') { $gaps.Add($What) }
}

$org = (Invoke-DvGet 'organizations?$select=name').value[0].name
Write-Host "Environment : $EnvironmentUrl (org '$org')"
Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))
Write-Host "Solution    : $SolutionUniqueName"

if (-not (Try-DvGet "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName")) {
    throw "$Table does not exist in this environment. Create the table first (entity-schema.md, Deployment section)."
}

# ── (a) COLUMN ──────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(a) $Table.$Column"
$attrPath = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=LogicalName,Targets,MetadataId"
$attr = Try-DvGet $attrPath
if ($attr) {
    $targets = @($attr.Targets)
    if ($targets.Count -eq 1 -and $targets[0] -eq $TargetEntity) { Report 'OK' "$Table.$Column (lookup -> $TargetEntity)" }
    else { Report 'FAIL' "$Table.$Column exists but targets [$($targets -join ', ')] — expected exactly $TargetEntity" }
} elseif ($Verify) { Report 'MISSING' "$Table.$Column" }
elseif ($IsDryRun) { Report 'WOULD' "create $Table.$Column (lookup -> $TargetEntity, relationship $RelationshipSchemaName)" }
else {
    Invoke-DvWrite POST 'RelationshipDefinitions' @{
        '@odata.type'        = 'Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata'
        SchemaName           = $RelationshipSchemaName
        ReferencedEntity     = $TargetEntity
        ReferencingEntity    = $Table
        CascadeConfiguration = @{ Assign = 'NoCascade'; Delete = 'RemoveLink'; Merge = 'NoCascade'; Reparent = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade' }
        AssociatedMenuConfiguration = @{ Behavior = 'DoNotDisplay'; Group = 'Details'; Order = 10000 }
        Lookup               = @{
            '@odata.type' = 'Microsoft.Dynamics.CRM.LookupAttributeMetadata'
            SchemaName    = $ColumnSchemaName
            DisplayName   = (New-Label 'Subject User')
            Description   = (New-Label 'The INTERNAL user this entry walls off (task 143, owner Q4). Exactly one of Subject User, Subject Contact or Subject Organization may be set; an entry with none or more than one denies nothing. On a secure record it removes the user''s direct shares and hides the record in Teams/SPA.')
            RequiredLevel = @{ Value = 'None' }
        }
    } @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName } | Out-Null
    Report 'DONE' "created $Table.$Column"
    $attr = Wait-DvRead $attrPath "$Table.$Column metadata"
}

$rel = Try-DvGet "RelationshipDefinitions(SchemaName='$RelationshipSchemaName')/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?`$select=SchemaName,ReferencingAttribute,CascadeConfiguration,MetadataId"
if ($rel) {
    $bad = @($ExpectedCascade.Keys | Where-Object { $rel.CascadeConfiguration.$_ -ne $ExpectedCascade[$_] } |
        ForEach-Object { "$_=$($rel.CascadeConfiguration.$_) (expected $($ExpectedCascade[$_]))" })
    if ($rel.ReferencingAttribute -ne $Column) { Report 'FAIL' "$RelationshipSchemaName references $($rel.ReferencingAttribute), not $Column" }
    elseif ($bad.Count -gt 0) { Report 'FAIL' "$RelationshipSchemaName cascade: $($bad -join '; ')" }
    else { Report 'OK' "$RelationshipSchemaName cascade (Delete RemoveLink; nothing else cascades)" }
} elseif ($attr) { Report 'FAIL' "$Table.$Column exists but relationship $RelationshipSchemaName was not found — it was created another way; check its cascade by hand" }
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

# ── (c) PUBLISH, then prove the column answers a DATA query (what the BFF's select needs) ──────────────────────
if ($Apply) {
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
    Write-Host "`nPublished $Table." -ForegroundColor Green
}

Write-Host "`n(c) Data query"
$dataPath = "$EntitySet`?`$select=sprk_noaccessentryid,_$($Column)_value&`$top=1"
if ($Apply) { Wait-DvRead $dataPath "$Table.$Column in data queries" | Out-Null; Report 'OK' "a data query selecting _$($Column)_value answers" }
elseif (Try-DvGet $dataPath) { Report 'OK' "a data query selecting _$($Column)_value answers (the BFF's RowSelect will not 400)" }
elseif ($Verify) { Report 'MISSING' "a data query selecting _$($Column)_value FAILS — do NOT deploy task 143's BFF here" }
else { Report 'WOULD' "after -Apply, a data query selecting _$($Column)_value answers" }

if ($Verify) {
    if ($gaps.Count -eq 0) { Write-Host "`nVERIFY PASS: $Table.$Column is in place; task 143's BFF may be deployed." -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
if ($IsDryRun) { Write-Host "`nDRY RUN complete — nothing was written. Re-run with -Apply." -ForegroundColor Cyan }
