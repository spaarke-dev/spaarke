#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Creates the Assigned-To provenance ledger, sprk_assignedaccess, under the Spaarke publisher in SpaarkeCore
    (unified-access-control-r2 task 142, GitHub #1065; owner round 2 item 5 + Q5). Dry run by default; -Apply writes;
    -Verify checks. Idempotent.

.DESCRIPTION
    WHY. The Assigned-To auto-grants are ADDED to the grant-access list and an operator's removal must STICK. A grant row
    cannot carry that provenance (a manual /grant lands on the same row; a POA share has no row at all), so the
    materializer (Services/ExternalAccess/AssignedAccessMaterializer.cs) records one ledger row per (root, source field,
    subject): Granted, Shared, CoveredByExisting, PendingConfirmation, Skipped, Declined, Adopted or Revoked.

    The script provisions, in order (schema doc: src/solutions/SpaarkeCore/entities/sprk_assignedaccess/entity-schema.md):
      (a) TABLE      sprk_assignedaccess (Organization-owned, primary name sprk_name), created with the
                     MSCRM.SolutionUniqueName header so it lands under the Spaarke publisher — never the default one.
      (b) COLUMNS    sprk_ledgerkey (Text 200), sprk_sourcefield (Text 100), sprk_state (local choice, 8 values),
                     sprk_reason (Text 100), sprk_grantedlevel (Whole number), sprk_grantedexpiry (Date Only).
      (c) LOOKUPS    root: sprk_project / sprk_matter / sprk_workassignment (Delete = Cascade: a deleted root takes its
                     ledger rows); subject: sprk_subjectcontact (contact) / sprk_subjectorganization (sprk_organization)
                     / sprk_subjectsystemuser (systemuser) / sprk_subjectteam (team — task 158 r1, owner round 30: a TEAM
                     a secure parent's share was passed on to); grant: sprk_externalrecordaccess (Delete = RemoveLink).
                     Navigation properties = schema names (sprk_Project, sprk_SubjectContact, …) — what the BFF binds.
      (d) KEY        sprk_AssignedAccessLedgerKey on the BFF-computed single string sprk_ledgerkey
                     ({root}:{rootId}:{sourceField}:{contact|organization}:{subjectId}) — uniqueness without relying on a
                     key over three NULLABLE typed lookups. Waits until the index is Active.
      (e) PUBLISH    sprk_assignedaccess.
      (f) PRIVILEGES read-only census: no role other than System Administrator / System Customizer may hold Create, Write
                     or Delete on the table (only the BFF application user writes it); users read it through the BFF.
      (g) DATA       a data query selecting every column the BFF's LedgerSelect names answers.

    TASK 158 r1 (owner round 30). The same ledger records the PROVENANCE of each share a secure matter / project passes
    on to a secure work assignment or project filed under it: one row per (filed record, parent, principal), source
    field `inherited:{parentTable}:{parentId}`, subject sprk_subjectsystemuser (a user) or sprk_subjectteam (a team),
    the mask written in sprk_grantedlevel. The parent's unshare removes only an inherited share still unmodified.

    ⚠️ DEPLOY ORDER — BINDING. A BFF build carrying task 142 reads the ledger on EVERY materialization (the sync route,
    the L1 writers, the 5-minute job). In an environment without the table the read fails and every materialization
    reports `ledger-unreadable` and writes NOTHING (fail closed) — no outage of existing access, but no auto-grants.
    A BFF build carrying task 158 r1 also reads the inherited-share rows WITH sprk_subjectteam on every /share-user,
    /unshare-user and secure-root-inheritance pass, and before every /unsecure-project of a work assignment or project:
    without the column those reads fail, every such fan-out answers children_incomplete and passes nothing on, and such
    an unsecure stops before revoking anything (fail closed). Run -Apply, then -Verify (exit 0), then deploy the BFF.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER SolutionUniqueName
    The unmanaged solution that carries the components. Default SpaarkeCore.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-AssignedAccessLedgerSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
.EXAMPLE
    .\Set-AssignedAccessLedgerSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply
.EXAMPLE
    .\Set-AssignedAccessLedgerSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify

.NOTES
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

# ── Constants (AssignedAccessStore.LedgerSelect / BuildCreatePayload) ─────────────────────────────────────────
$Table = 'sprk_assignedaccess'
$EntitySet = 'sprk_assignedaccesses'
$KeySchemaName = 'sprk_AssignedAccessLedgerKey'
$States = [ordered]@{
    100000000 = 'Granted'; 100000001 = 'Shared'; 100000002 = 'Covered by existing'; 100000003 = 'Pending confirmation'
    100000004 = 'Skipped'; 100000005 = 'Declined'; 100000006 = 'Adopted'; 100000007 = 'Revoked'
}
$StringColumns = @(
    @{ Name = 'sprk_ledgerkey';   Schema = 'sprk_LedgerKey';   Max = 200; Label = 'Ledger Key';   Description = 'BFF-computed uniqueness key: {root}:{rootId}:{sourceField}:{contact|organization|systemuser|team}:{subjectId}. Carries the alternate key. Written only by the BFF.' }
    @{ Name = 'sprk_sourcefield'; Schema = 'sprk_SourceField'; Max = 100; Label = 'Source Field'; Description = 'The "Assigned *" column (logical name) that named the subject, or inherited:{parentTable}:{parentId} for a share a secure parent passed on (task 158).' }
    @{ Name = 'sprk_reason';      Schema = 'sprk_Reason';      Max = 100; Label = 'Reason';       Description = 'Why the row is in its state (skip reason, how an assignment ended, raised-from level and date). A stable code, never free text.' }
)
$Lookups = @(
    @{ Name = 'sprk_project';              Schema = 'sprk_Project';              Target = 'sprk_project';              Rel = 'sprk_sprk_project_sprk_assignedaccess_project';                Delete = 'Cascade';    Label = 'Project' }
    @{ Name = 'sprk_matter';               Schema = 'sprk_Matter';               Target = 'sprk_matter';               Rel = 'sprk_sprk_matter_sprk_assignedaccess_matter';                  Delete = 'RemoveLink'; Label = 'Matter' }
    @{ Name = 'sprk_workassignment';       Schema = 'sprk_WorkAssignment';       Target = 'sprk_workassignment';       Rel = 'sprk_sprk_workassignment_sprk_assignedaccess_workassignment';  Delete = 'RemoveLink'; Label = 'Work Assignment' }
    @{ Name = 'sprk_subjectcontact';       Schema = 'sprk_SubjectContact';       Target = 'contact';                   Rel = 'sprk_contact_sprk_assignedaccess_subjectcontact';             Delete = 'RemoveLink'; Label = 'Subject Contact' }
    @{ Name = 'sprk_subjectorganization';  Schema = 'sprk_SubjectOrganization';  Target = 'sprk_organization';         Rel = 'sprk_sprk_organization_sprk_assignedaccess_subjectorg';       Delete = 'RemoveLink'; Label = 'Subject Organization' }
    @{ Name = 'sprk_subjectsystemuser';    Schema = 'sprk_SubjectSystemUser';    Target = 'systemuser';                Rel = 'sprk_systemuser_sprk_assignedaccess_subjectsystemuser';       Delete = 'RemoveLink'; Label = 'Subject User' }
    # Task 158 r1 (owner round 30): the TEAM a secure parent's share was passed on to (inherited-share provenance).
    @{ Name = 'sprk_subjectteam';          Schema = 'sprk_SubjectTeam';          Target = 'team';                      Rel = 'sprk_team_sprk_assignedaccess_subjectteam';                   Delete = 'RemoveLink'; Label = 'Subject Team' }
    @{ Name = 'sprk_externalrecordaccess'; Schema = 'sprk_ExternalRecordAccess'; Target = 'sprk_externalrecordaccess'; Rel = 'sprk_sprk_externalrecordaccess_sprk_assignedaccess_grant';    Delete = 'RemoveLink'; Label = 'Grant' }
)
$AllowedWriterRoles = @('System Administrator', 'System Customizer')
# Microsoft platform roles Dataverse grants on every new table. Allowed only while every holder is an application user
# (no person, no team) — checked below, never assumed from the name.
$PlatformServiceRoles = @('Service Writer', 'Service Deleter')

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
$solutionHeader = @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName }
function Invoke-DvGet([string]$Path) { Invoke-RestMethod -Uri "$Api/$Path" -Headers $headers -Method Get }
function Invoke-DvWrite([string]$Method, [string]$Path, $Body, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $json = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 20 -Compress }
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method $Method -Body $json
}
function Try-DvGet([string]$Path) { try { Invoke-DvGet $Path } catch { $null } }
function Wait-DvRead([string]$Path, [string]$What, [int]$Attempts = 18, [int]$DelaySeconds = 10) {
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

# ── (a) TABLE ───────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(a) Table $Table"
$entityPath = "EntityDefinitions(LogicalName='$Table')?`$select=LogicalName,MetadataId,OwnershipType,EntitySetName,SchemaName"
$entity = Try-DvGet $entityPath
if ($entity) {
    if ($entity.EntitySetName -ne $EntitySet) { Report 'FAIL' "$Table entity set is '$($entity.EntitySetName)' — the BFF addresses '$EntitySet'" }
    elseif ($entity.OwnershipType -ne 'OrganizationOwned') { Report 'FAIL' "$Table is $($entity.OwnershipType); expected OrganizationOwned" }
    else { Report 'OK' "$Table ($EntitySet, OrganizationOwned)" }
} elseif ($Verify) { Report 'MISSING' "table $Table" }
elseif ($IsDryRun) { Report 'WOULD' "create table $Table (OrganizationOwned, primary name sprk_name)" }
else {
    Invoke-DvWrite POST 'EntityDefinitions' @{
        '@odata.type'         = 'Microsoft.Dynamics.CRM.EntityMetadata'
        SchemaName            = 'sprk_assignedaccess'
        DisplayName           = (New-Label 'Assigned Access')
        DisplayCollectionName = (New-Label 'Assigned Access')
        Description           = (New-Label 'Provenance of the Assigned-To auto-grants (task 142): one row per record, Assigned column and named contact/organization. Written only by the BFF.')
        OwnershipType         = 'OrganizationOwned'
        IsActivity            = $false
        HasNotes              = $false
        HasActivities         = $false
        PrimaryNameAttribute  = 'sprk_name'
        Attributes            = @(@{
            '@odata.type' = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'
            SchemaName    = 'sprk_Name'
            IsPrimaryName = $true
            MaxLength     = 200
            FormatName    = @{ Value = 'Text' }
            RequiredLevel = @{ Value = 'None' }
            DisplayName   = (New-Label 'Name')
            Description   = (New-Label 'Readable label: source field and subject.')
        })
    } $solutionHeader | Out-Null
    Report 'DONE' "created table $Table"
    $entity = Wait-DvRead $entityPath "$Table metadata"
}

# ── (b) COLUMNS ─────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(b) Columns"
function Ensure-Attribute([string]$Name, [string]$Describe, [hashtable]$Body) {
    $path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Name')?`$select=LogicalName,AttributeType"
    if (-not $entity) { if ($Verify) { Report 'MISSING' "$Table.$Name" } else { Report 'WOULD' "create $Table.$Name ($Describe)" }; return }
    if (Try-DvGet $path) { Report 'OK' "$Table.$Name ($Describe)"; return }
    if ($Verify) { Report 'MISSING' "$Table.$Name"; return }
    if ($IsDryRun) { Report 'WOULD' "create $Table.$Name ($Describe)"; return }
    Invoke-DvWrite POST "EntityDefinitions(LogicalName='$Table')/Attributes" $Body $solutionHeader | Out-Null
    Report 'DONE' "created $Table.$Name"
    Wait-DvRead $path "$Table.$Name metadata" | Out-Null
}

foreach ($c in $StringColumns) {
    Ensure-Attribute $c.Name "Text $($c.Max)" @{
        '@odata.type' = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'; SchemaName = $c.Schema; MaxLength = $c.Max
        FormatName = @{ Value = 'Text' }; RequiredLevel = @{ Value = 'None' }
        DisplayName = (New-Label $c.Label); Description = (New-Label $c.Description)
    }
}

Ensure-Attribute 'sprk_state' 'Choice (8 states)' @{
    '@odata.type' = 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata'; SchemaName = 'sprk_State'; RequiredLevel = @{ Value = 'None' }
    DisplayName = (New-Label 'State'); Description = (New-Label 'The Assigned-To decision for this subject (AssignedAccessState).')
    OptionSet = @{
        '@odata.type' = 'Microsoft.Dynamics.CRM.OptionSetMetadata'; IsGlobal = $false; OptionSetType = 'Picklist'
        Options = @($States.Keys | ForEach-Object { @{ Value = [int]$_; Label = (New-Label $States[$_]) } })
    }
}

Ensure-Attribute 'sprk_grantedlevel' 'Whole number' @{
    '@odata.type' = 'Microsoft.Dynamics.CRM.IntegerAttributeMetadata'; SchemaName = 'sprk_GrantedLevel'; RequiredLevel = @{ Value = 'None' }
    MinValue = 0; MaxValue = 2147483647; Format = 'None'
    DisplayName = (New-Label 'Granted Level'); Description = (New-Label 'The grant level or share mask the BFF last wrote — compared to detect a manual change ("modified").')
}

Ensure-Attribute 'sprk_grantedexpiry' 'Date Only' @{
    '@odata.type' = 'Microsoft.Dynamics.CRM.DateTimeAttributeMetadata'; SchemaName = 'sprk_GrantedExpiry'; RequiredLevel = @{ Value = 'None' }
    Format = 'DateOnly'; DateTimeBehavior = @{ Value = 'DateOnly' }
    DisplayName = (New-Label 'Granted Expiry'); Description = (New-Label 'The expiry the BFF last wrote on the grant.')
}

# ── (c) LOOKUPS ─────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(c) Lookups"
foreach ($l in $Lookups) {
    $path = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$($l.Name)')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=LogicalName,Targets"
    $existing = if ($entity) { Try-DvGet $path } else { $null }
    if ($existing) {
        $targets = @($existing.Targets)
        if ($targets.Count -eq 1 -and $targets[0] -eq $l.Target) { Report 'OK' "$Table.$($l.Name) -> $($l.Target)" }
        else { Report 'FAIL' "$Table.$($l.Name) targets [$($targets -join ', ')], expected $($l.Target)" }
        continue
    }
    if ($Verify) { Report 'MISSING' "$Table.$($l.Name)"; continue }
    if ($IsDryRun -or -not $entity) { Report 'WOULD' "create $Table.$($l.Name) -> $($l.Target) (Delete $($l.Delete))"; continue }
    Invoke-DvWrite POST 'RelationshipDefinitions' @{
        '@odata.type'        = 'Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata'
        SchemaName           = $l.Rel
        ReferencedEntity     = $l.Target
        ReferencingEntity    = $Table
        CascadeConfiguration = @{ Assign = 'NoCascade'; Delete = $l.Delete; Merge = 'NoCascade'; Reparent = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade' }
        AssociatedMenuConfiguration = @{ Behavior = 'DoNotDisplay'; Group = 'Details'; Order = 10000 }
        Lookup               = @{
            '@odata.type' = 'Microsoft.Dynamics.CRM.LookupAttributeMetadata'
            SchemaName    = $l.Schema
            DisplayName   = (New-Label $l.Label)
            RequiredLevel = @{ Value = 'None' }
        }
    } $solutionHeader | Out-Null
    Report 'DONE' "created $Table.$($l.Name) -> $($l.Target)"
    Wait-DvRead $path "$Table.$($l.Name) metadata" | Out-Null
}

# ── (d) ALTERNATE KEY ───────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(d) Alternate key"
$keyPath = "EntityDefinitions(LogicalName='$Table')/Keys?`$select=SchemaName,KeyAttributes,EntityKeyIndexStatus"
$keys = if ($entity) { @((Try-DvGet $keyPath).value) } else { @() }
$key = $keys | Where-Object { $_.SchemaName -eq $KeySchemaName } | Select-Object -First 1
if ($key) {
    if (@($key.KeyAttributes) -join ',' -ne 'sprk_ledgerkey') { Report 'FAIL' "$KeySchemaName is on [$($key.KeyAttributes -join ', ')], expected sprk_ledgerkey" }
    elseif ($key.EntityKeyIndexStatus -ne 'Active') { Report 'FAIL' "$KeySchemaName index is $($key.EntityKeyIndexStatus), not Active (re-run, or check the system job)" }
    else { Report 'OK' "$KeySchemaName on sprk_ledgerkey (Active)" }
} elseif ($Verify) { Report 'MISSING' "alternate key $KeySchemaName" }
elseif ($IsDryRun -or -not $entity) { Report 'WOULD' "create alternate key $KeySchemaName on sprk_ledgerkey" }
else {
    Invoke-DvWrite POST "EntityDefinitions(LogicalName='$Table')/Keys" @{
        '@odata.type' = 'Microsoft.Dynamics.CRM.EntityKeyMetadata'
        SchemaName    = $KeySchemaName
        DisplayName   = (New-Label 'Ledger Key')
        KeyAttributes = @('sprk_ledgerkey')
    } $solutionHeader | Out-Null
    Report 'DONE' "created alternate key $KeySchemaName"
    for ($i = 1; $i -le 30; $i++) {
        $k = @((Try-DvGet $keyPath).value) | Where-Object { $_.SchemaName -eq $KeySchemaName } | Select-Object -First 1
        if ($k -and $k.EntityKeyIndexStatus -eq 'Active') { Report 'OK' "$KeySchemaName index Active"; break }
        if ($k -and $k.EntityKeyIndexStatus -eq 'Failed') { Report 'FAIL' "$KeySchemaName index FAILED"; break }
        Write-Host "    waiting for the key index ($i/30)..."; Start-Sleep -Seconds 10
    }
}

# ── (d2) SOLUTION ───────────────────────────────────────────────────────────────────────────────────────────
# The components above are created with the MSCRM.SolutionUniqueName header, but a component created before that
# header was used (or in another solution) is not in it -- so -Verify checks membership instead of assuming it.
Write-Host "`n(d2) Solution components"
$solution = @((Invoke-DvGet "solutions?`$select=solutionid,uniquename&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
if (-not $solution) { Report 'FAIL' "solution '$SolutionUniqueName' not found" }
elseif (-not $entity) { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "$Table and its components in $SolutionUniqueName (after the table exists)" }
else {
    $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid
    $components = [System.Collections.Generic.List[object]]::new()
    $components.Add(@{ Id = $entity.MetadataId; Type = 1; Label = "table $Table" })
    $attrIds = @{}
    foreach ($a in @((Invoke-DvGet "EntityDefinitions(LogicalName='$Table')/Attributes?`$select=LogicalName,MetadataId").value)) { $attrIds[$a.LogicalName] = $a.MetadataId }
    foreach ($name in @('sprk_name', 'sprk_state', 'sprk_grantedlevel', 'sprk_grantedexpiry') + @($StringColumns | ForEach-Object { $_.Name }) + @($Lookups | ForEach-Object { $_.Name })) {
        if ($attrIds[$name]) { $components.Add(@{ Id = $attrIds[$name]; Type = 2; Label = "$Table.$name"; TableId = $entity.MetadataId }) }
    }
    foreach ($l in $Lookups) {
        $rel = Try-DvGet "RelationshipDefinitions(SchemaName='$($l.Rel)')?`$select=MetadataId"
        if ($rel) { $components.Add(@{ Id = $rel.MetadataId; Type = 10; Label = "relationship $($l.Rel)"; TableId = $entity.MetadataId }) }
    }
    $keyRow = @((Invoke-DvGet "EntityDefinitions(LogicalName='$Table')/Keys?`$select=SchemaName,MetadataId").value) | Where-Object { $_.SchemaName -eq $KeySchemaName } | Select-Object -First 1
    if ($keyRow) { $components.Add(@{ Id = $keyRow.MetadataId; Type = 14; Label = "key $KeySchemaName"; TableId = $entity.MetadataId }) }

    foreach ($c in $components) {
        $how = Test-DvInSolution -Membership $membership -ComponentId $c.Id -TableMetadataId $c['TableId']
        if ($how -eq 'Direct') { Report 'OK' "$($c.Label) in $SolutionUniqueName"; continue }
        if ($how -eq 'ViaTable') { Report 'OK' "$($c.Label) in $SolutionUniqueName (its table includes subcomponents)"; continue }
        if ($Verify) { Report 'MISSING' "$($c.Label) in $SolutionUniqueName"; continue }
        if ($IsDryRun) { Report 'WOULD' "add $($c.Label) to $SolutionUniqueName"; continue }
        # A table is added WITH its subcomponents (rootcomponentbehavior 0), so its columns, relationships and key follow.
        $body = @{ ComponentId = $c.Id; ComponentType = $c.Type; SolutionUniqueName = $SolutionUniqueName; AddRequiredComponents = $false }
        if ($c.Type -eq 1) { $body.DoNotIncludeSubcomponents = $false }
        Invoke-DvWrite POST 'AddSolutionComponent' $body | Out-Null
        Report 'DONE' "added $($c.Label) to $SolutionUniqueName"
        if ($c.Type -eq 1) { $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid }
    }
}

# ── (e) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($Apply) {
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
    Write-Host "`nPublished $Table." -ForegroundColor Green
}

# ── (f) PRIVILEGES (read-only census) ────────────────────────────────────────────────────────────────────────
Write-Host "`n(f) Who may write the ledger"
$privs = if ($entity) { Try-DvGet "privileges?`$select=privilegeid,name&`$filter=endswith(name,'sprk_assignedaccess')" } else { $null }
if (-not $privs) { if ($entity) { Report 'FAIL' 'the table privileges could not be read' } else { Report 'WOULD' 'after -Apply: only System Administrator / System Customizer hold Create/Write/Delete' } }
else {
    foreach ($p in @($privs.value)) {
        $holders = @((Try-DvGet "roleprivilegescollection?`$select=roleid&`$filter=privilegeid eq $($p.privilegeid)").value)
        $roles = foreach ($h in $holders) { Try-DvGet "roles($($h.roleid))?`$select=roleid,name" }
        $roles = @($roles | Where-Object { $_ })
        $roleNames = @($roles | ForEach-Object { $_.name } | Sort-Object -Unique)
        $writer = $p.name -match '^prv(Create|Write|Delete)'
        $unexpected = @(foreach ($r in $roles) {
            if ($r.name -in $AllowedWriterRoles) { continue }
            if ($r.name -in $PlatformServiceRoles) {
                # A platform service role passes only if no person and no team holds it.
                $userRead = Try-DvGet "roles($($r.roleid))/systemuserroles_association?`$select=applicationid"
                $teamRead = Try-DvGet "roles($($r.roleid))/teamroles_association?`$select=teamid"
                if (-not $userRead -or -not $teamRead) { "$($r.name) (its holders could not be read)"; continue }
                $people = @(@($userRead.value) | Where-Object { -not $_.applicationid })
                if ($people.Count -eq 0 -and @($teamRead.value).Count -eq 0) { continue }
                "$($r.name) (held by a person or a team)"; continue
            }
            $r.name
        })
        $unexpected = @($unexpected | Sort-Object -Unique)
        if ($writer -and $unexpected.Count -gt 0) { Report 'FAIL' "$($p.name) held by non-admin role(s): $($unexpected -join ', ') — only the BFF writes the ledger" }
        else { Report 'OK' "$($p.name): $(if ($roleNames.Count) { $roleNames -join ', ' } else { '(no role)' })" }
    }
}

# ── (g) DATA query (what AssignedAccessStore.LedgerSelect needs) ──────────────────────────────────────────────
Write-Host "`n(g) Data query"
# Task 158 r1: the inherited-share select adds _sprk_subjectteam_value (AssignedAccessStore.InheritedLedgerSelect).
$select = 'sprk_assignedaccessid,sprk_ledgerkey,sprk_sourcefield,_sprk_project_value,_sprk_matter_value,_sprk_workassignment_value,' +
          '_sprk_subjectcontact_value,_sprk_subjectorganization_value,_sprk_subjectsystemuser_value,_sprk_externalrecordaccess_value,' +
          'sprk_state,sprk_reason,sprk_grantedlevel,sprk_grantedexpiry,_sprk_subjectteam_value'
$dataPath = "$EntitySet`?`$select=$select&`$top=1"
if ($Apply) { Wait-DvRead $dataPath "$Table in data queries" | Out-Null; Report 'OK' 'the BFF ledger select answers' }
elseif (Try-DvGet $dataPath) { Report 'OK' 'the BFF ledger select answers (task 142 reads will not 400)' }
elseif ($Verify) { Report 'MISSING' 'the BFF ledger select FAILS — do NOT deploy task 142''s BFF here' }
else { Report 'WOULD' 'after -Apply, the BFF ledger select answers' }

if ($Verify) {
    if ($gaps.Count -gt 0) {
        Write-Host "`nVERIFY FAILED — $($gaps.Count) gap(s):" -ForegroundColor Red
        $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "`nVERIFY PASSED — the ledger is provisioned; the task-142 BFF may be deployed." -ForegroundColor Green
    exit 0
}

Write-Host ("`n{0}" -f $(if ($IsDryRun) { 'Dry run complete — nothing was written. Re-run with -Apply.' } else { 'Apply complete. Run -Verify next.' }))
