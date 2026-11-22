#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Adds the three private-message context columns on sprk_communication: sprk_privatecontextmatter (-> sprk_matter),
    sprk_privatecontextproject (-> sprk_project) and sprk_privatecontextworkassignment (-> sprk_workassignment), each a
    non-filing lookup with a referential 1:N relationship (field-mapping-server-write-path-r1 task 056b).
    Dry run by default; -Apply writes; -Verify checks. Idempotent.

.DESCRIPTION
    A private message is a Direct message (the pair's Direct thread, the Direct team as owner, the stamped pair) that also
    names ONE core record it is about. The reference decides where the message is SHOWN to its two participants; it never
    decides who READS it. So these lookups must never file, share or reparent anything: each relationship below is
    referential only. Design: projects/field-mapping-server-write-path-r1/notes/task-056-private-message-design.md, 3.1 and C4.

    The script provisions, per column, in order:
      (a) COLUMN    on sprk_communication: a lookup (not required) to the core table, through its own 1:N relationship
                    sprk_<table>_sprk_communication_privatecontext. Display name "Private context (Matter)" and so on.
                    Cascade: Assign, Share, Unshare, Reparent = NoCascade; Delete = RemoveLink; Merge = NoCascade (Dataverse
                    accepts NoCascade there, as for sprk_directrecipient). A cascading Share or Reparent would push the record's
                    shares onto private messages. Associated menu: DoNotDisplay (cosmetic: the relationship is not listed on the
                    record's forms; row security already limits the rows to the pair).
                    An existing column with a correct cascade is left as it is. An existing relationship with a wrong cascade or
                    menu is REPORTED in a dry run and corrected only with -Apply (a metadata update of that relationship).
      (b) SOLUTION  Each lookup and its relationship are in -SolutionUniqueName (default SpaarkeCore).
      (c) PUBLISH   sprk_communication.
      (d) NAMES     Prints the Web API navigation property and the read column from live metadata.

    -Verify reads each column AND each relationship's cascade and menu back from the live metadata. It exits 1 when a column
    or relationship is missing, a column does not target exactly its core table, Assign / Share / Unshare / Reparent / Merge is
    not NoCascade, Delete is not RemoveLink, the menu is not DoNotDisplay, or a component is not in the solution.

    NOT done here: field-level security for the columns. scripts/Set-ThreadFlagFieldSecurity.ps1 secures them together with
    the other BFF-owned message columns (it refuses until these three exist).

    LIVE ORDER (the owner's go for each environment): THIS SCRIPT (-Apply, then -Verify) ->
    scripts/Set-ThreadFlagFieldSecurity.ps1 (-Apply, then -Verify) -> the BFF build that writes them. A BFF that finds the
    columns missing refuses a private send with a 409 that names this script.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER SolutionUniqueName
    The unmanaged solution that carries the components. Default SpaarkeCore.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-PrivateContextSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: each step's present / missing / wrong state; zero writes.

.EXAMPLE
    .\Set-PrivateContextSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply

.EXAMPLE
    .\Set-PrivateContextSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify

.NOTES
    field-mapping-server-write-path-r1 task 056b (projects/field-mapping-server-write-path-r1/tasks/056-private-messages-in-record-thread.poml).
    Pattern: scripts/Set-DirectRecipientSchema.ps1. Auth: the operator's own az CLI identity. No secrets.
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
$Specs = @(
    @{ Column = 'sprk_privatecontextmatter'; SchemaName = 'sprk_PrivateContextMatter'; Target = 'sprk_matter'; Relationship = 'sprk_matter_sprk_communication_privatecontext'; Display = 'Private context (Matter)' }
    @{ Column = 'sprk_privatecontextproject'; SchemaName = 'sprk_PrivateContextProject'; Target = 'sprk_project'; Relationship = 'sprk_project_sprk_communication_privatecontext'; Display = 'Private context (Project)' }
    @{ Column = 'sprk_privatecontextworkassignment'; SchemaName = 'sprk_PrivateContextWorkAssignment'; Target = 'sprk_workassignment'; Relationship = 'sprk_workassignment_sprk_communication_privatecontext'; Display = 'Private context (Work assignment)' }
)
$ExpectedCascade = [ordered]@{ Assign = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade'; Merge = 'NoCascade'; Delete = 'RemoveLink' }
$ExpectedMenuBehavior = 'DoNotDisplay'

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
# What a relationship's cascade and menu get wrong (empty = correct). Each entry reads "Assign=Cascade (expected NoCascade)".
function Get-RelationshipDrift($Rel) {
    $drift = @($ExpectedCascade.Keys | Where-Object { $Rel.CascadeConfiguration.$_ -ne $ExpectedCascade[$_] } |
            ForEach-Object { "$_=$($Rel.CascadeConfiguration.$_) (expected $($ExpectedCascade[$_]))" })
    if ($Rel.AssociatedMenuConfiguration.Behavior -ne $ExpectedMenuBehavior) {
        $drift += "AssociatedMenu=$($Rel.AssociatedMenuConfiguration.Behavior) (expected $ExpectedMenuBehavior)"
    }
    $drift
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

$tableId = $null
$state = @{}   # Column -> @{ Attr; Rel }

# ── (a) COLUMNS + RELATIONSHIPS ─────────────────────────────────────────────────────────────────────────────
foreach ($spec in $Specs) {
    $Column = $spec.Column
    $RelationshipSchemaName = $spec.Relationship
    Write-Host "`n(a) $Table.$Column"
    $attrPath = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=LogicalName,SchemaName,Targets,MetadataId"
    $relPath = "RelationshipDefinitions(SchemaName='$RelationshipSchemaName')/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?`$select=SchemaName,ReferencedEntity,ReferencingEntity,ReferencingAttribute,ReferencingEntityNavigationPropertyName,CascadeConfiguration,AssociatedMenuConfiguration,MetadataId"

    $attr = Try-DvGet $attrPath
    $justCreated = $false
    if ($attr) {
        $targets = @($attr.Targets)
        if ($targets.Count -eq 1 -and $targets[0] -eq $spec.Target) { Report 'OK' "$Table.$Column (lookup -> $($spec.Target))" }
        else { Report 'FAIL' "$Table.$Column exists but targets [$($targets -join ', ')]; expected exactly $($spec.Target)" }
    } elseif ($Verify) { Report 'MISSING' "$Table.$Column" }
    elseif ($IsDryRun) { Report 'WOULD' "create $Table.$Column (lookup -> $($spec.Target), relationship $RelationshipSchemaName; cascade NoCascade, Delete RemoveLink; menu $ExpectedMenuBehavior)" }
    elseif (Try-DvGet $relPath) { Report 'FAIL' "$Table.$Column does not exist but relationship $RelationshipSchemaName does; it was created another way, resolve it by hand" }
    else {
        Invoke-DvWrite POST 'RelationshipDefinitions' @{
            '@odata.type'               = 'Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata'
            SchemaName                  = $RelationshipSchemaName
            ReferencedEntity            = $spec.Target
            ReferencingEntity           = $Table
            CascadeConfiguration        = @{ Assign = 'NoCascade'; Delete = 'RemoveLink'; Merge = 'NoCascade'; Reparent = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade' }
            AssociatedMenuConfiguration = @{ Behavior = $ExpectedMenuBehavior; Group = 'Details'; Order = 10000 }
            Lookup                      = @{
                '@odata.type' = 'Microsoft.Dynamics.CRM.LookupAttributeMetadata'
                SchemaName    = $spec.SchemaName
                DisplayName   = (New-Label $spec.Display)
                Description   = (New-Label "The $($spec.Target) a private (Direct) message is about. Shows the message to its two participants in the record's conversation; never files, shares or reparents it and never decides who reads it. Written by the BFF at create only. field-mapping-server-write-path-r1 task 056.")
                RequiredLevel = @{ Value = 'None' }
            }
        } @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName } | Out-Null
        Report 'DONE' "created $Table.$Column"
        $justCreated = $true
        $attr = Wait-DvRead $attrPath "$Table.$Column metadata"
        $set = (Invoke-DvGet "EntityDefinitions(LogicalName='$Table')?`$select=EntitySetName").EntitySetName
        Wait-DvRead "$set`?`$select=_$($Column)_value&`$top=1" "$Table.$Column in data queries" | Out-Null
    }

    $rel = if ($justCreated) { Wait-DvRead $relPath "relationship $RelationshipSchemaName metadata" } else { Try-DvGet $relPath }
    if ($rel) {
        $drift = @(Get-RelationshipDrift $rel)
        if ($rel.ReferencingAttribute -ne $Column -or $rel.ReferencedEntity -ne $spec.Target -or $rel.ReferencingEntity -ne $Table) {
            Report 'FAIL' "$RelationshipSchemaName is $($rel.ReferencingEntity).$($rel.ReferencingAttribute) -> $($rel.ReferencedEntity), not $Table.$Column -> $($spec.Target)"
        }
        elseif ($drift.Count -eq 0) { Report 'OK' "$RelationshipSchemaName cascade (Assign/Share/Unshare/Reparent/Merge NoCascade; Delete RemoveLink) and menu $ExpectedMenuBehavior" }
        elseif ($Verify) { Report 'FAIL' "$RelationshipSchemaName cascade or menu: $($drift -join '; ')" }
        elseif ($IsDryRun) { Report 'WOULD' "correct $RelationshipSchemaName (-Apply updates it): $($drift -join '; ')" }
        else {
            # Metadata update: read the whole relationship, change only the cascade and the menu, write it back.
            $full = Invoke-DvGet "RelationshipDefinitions($($rel.MetadataId))/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
            foreach ($k in $ExpectedCascade.Keys) { $full.CascadeConfiguration.$k = $ExpectedCascade[$k] }
            $full.AssociatedMenuConfiguration.Behavior = $ExpectedMenuBehavior
            Invoke-DvWrite PUT "RelationshipDefinitions($($rel.MetadataId))" $full @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
            $rel = Try-DvGet $relPath
            $after = if ($rel) { @(Get-RelationshipDrift $rel) } else { @('relationship unreadable after the update') }
            if ($after.Count -eq 0) { Report 'DONE' "corrected $RelationshipSchemaName (was: $($drift -join '; '))" }
            else { Report 'FAIL' "$RelationshipSchemaName still wrong after the update: $($after -join '; ')" }
        }
    } elseif ($attr) { Report 'FAIL' "$Table.$Column exists but relationship $RelationshipSchemaName was not found; it was created another way, check its cascade by hand" }
    elseif ($Verify) { Report 'MISSING' "relationship $RelationshipSchemaName" }

    $state[$Column] = @{ Attr = $attr; Rel = $rel }
}

# ── (b) SOLUTION ────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(b) Solution components"
$solution = @((Invoke-DvGet "solutions?`$select=solutionid,uniquename&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
if (-not $solution) { Report 'FAIL' "solution '$SolutionUniqueName' not found" }
else {
    $tableId = (Invoke-DvGet "EntityDefinitions(LogicalName='$Table')?`$select=MetadataId").MetadataId
    $components = [System.Collections.Generic.List[object]]::new()
    foreach ($spec in $Specs) {
        $s = $state[$spec.Column]
        if ($s.Attr) { $components.Add(@{ Id = $s.Attr.MetadataId; Type = 2; Label = "$Table.$($spec.Column)"; TableId = $tableId }) }
        if ($s.Rel) { $components.Add(@{ Id = $s.Rel.MetadataId; Type = 10; Label = "relationship $($spec.Relationship)"; TableId = $tableId }) }
    }
    if ($components.Count -eq 0) { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "add the columns and relationships to $SolutionUniqueName (after they exist)" }
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
Write-Host "`n(d) Web API names (live metadata)"
foreach ($spec in $Specs) {
    $s = $state[$spec.Column]
    if (-not $s.Rel) { continue }
    $nav = $s.Rel.ReferencingEntityNavigationPropertyName
    Report 'INFO' "$($spec.Column): schema $($s.Attr.SchemaName); navigation property $nav (bind: `"$nav@odata.bind`": `"/<entity set of $($spec.Target)>(<id>)`"); read column _$($spec.Column)_value"
}

if ($Verify -or $Apply) {
    if ($gaps.Count -eq 0) {
        Write-Host "`n$(if ($Verify) { 'VERIFY' } else { 'APPLY' }) PASS: $Table carries the three private-context lookups with referential relationships." -ForegroundColor Green
        exit 0
    }
    Write-Host "`n$(if ($Verify) { 'VERIFY' } else { 'APPLY' }) FAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
if ($gaps.Count -gt 0) {
    Write-Host "`nDRY RUN found $($gaps.Count) problem(s) -Apply cannot fix; nothing was written:" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "`nDRY RUN complete; nothing was written. Re-run with -Apply." -ForegroundColor Cyan
