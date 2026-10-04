#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Adds the contact-typed grant issuer column — sprk_externalrecordaccess.sprk_grantedbycontact, a lookup to contact —
    and locks it with field-level security so ONLY the BFF can write it (unified-access-control-r2 task 140, #1063;
    owner C4 / Q2, session 27). Dry run by default; -Apply writes; -Verify checks. Idempotent.

.DESCRIPTION
    WHY. Task 140 lets a contact holding Collaborate or Full Access grant colleagues of their own organization access to
    a record from the external SPA. The grant row must record WHO issued it, because a contact may change or revoke only
    the grants it issued ("no proxy revocation"). sprk_grantedby is a SYSTEMUSER lookup and cannot reference a contact,
    so a contact-issued grant had no issuer column at all. This adds one; sprk_grantedby stays empty on a
    contact-issued row.

    The BFF reads the column on EVERY grant-row read (ExternalGrantLifecycle.RowSelect) and writes it on every contact
    grant (GrantExternalAccessEndpoint.BuildGrantPayload) — and clears it when an internal user changes a contact-issued
    row, so the contact can no longer revoke a decision somebody else made.

    The script provisions, in order:
      (a) COLUMN     sprk_grantedbycontact (schema sprk_GrantedByContact) on sprk_externalrecordaccess: a lookup to
                     contact through the 1:N relationship sprk_contact_sprk_externalrecordaccess_grantedbycontact, cascade
                     NoCascade for Assign/Share/Unshare/Reparent/Merge and RemoveLink for Delete (nothing about the
                     issuing contact may move, share or delete a grant). The navigation property is the PascalCase schema
                     name, sprk_GrantedByContact — the bind name the BFF uses (asserted below). The target solution's
                     publisher must carry the sprk prefix (asserted before the create).
      (b) PROFILES   the EXISTING "Spaarke BFF-Managed Field Readers" (every business unit's default team: everyone keeps
                     READING the issuer — Manage Access shows it) and "Spaarke BFF-Managed Field Writers" (the BFF
                     application user(s) ONLY) — created here if absent (task 133's script creates them first in dev).
                     Members are associated BEFORE the column is secured. Any other writer-profile member is FAIL in
                     every mode (reported, never removed).
      (c) FLS        the column becomes field-secured; readers read=4, writers read=4 create=4 update=4; any other
                     profile that can create or update it (besides System Administrator) is FAIL. Without the lock, any
                     user with Write on a grant row could name a contact as its issuer, and that contact could then
                     revoke the grant from the external SPA.
      (d) SOLUTION   the lookup, its relationship and both profiles are in -SolutionUniqueName (SpaarkeCore), decided
                     by the shared scripts/common/DataverseSolutionMembership.ps1: paged, and a column or relationship
                     counts as included when its table is in the solution with rootcomponentbehavior = 0 (as
                     sprk_externalrecordaccess is in SpaarkeCore on spaarkedev1 -- it then has no row of its own, and
                     AddSolutionComponent on it is a no-op).
      (e) PUBLISH    sprk_externalrecordaccess.

    ⚠️ DEPLOY ORDER. A BFF build carrying task 140 SELECTS this column on every grant-row read (/grant, /revoke,
    /invite-and-grant, /set-record-share-expiry, the Assigned-To materializer, the contact routes). Dataverse answers 400
    to a $select naming an unknown column, so those routes FAIL until (a) has run. Apply this script — and see -Verify
    exit 0 — in an environment BEFORE deploying such a BFF (or the task-140 TrackingFieldTrio PCF) to it.

    PREFIX SAFETY (FAILURE-MODES AP-13). Never create this column with mcp__dataverse__update_table: it has no publisher
    or solution parameter. This script writes under the MSCRM.SolutionUniqueName header and asserts the sprk_ prefix.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids whose Dataverse application users must be members of the writer profile — the BFF
    managed identity (dev: 5967251e-171c-46fe-a6c2-ef843c90309d) and, while it is still an application user, the BFF
    app registration (dev: 1e40baad-e065-4aea-a8d4-4b7ab273458c). Required for -Apply and -Verify.

.PARAMETER SolutionUniqueName
    The unmanaged solution that carries the components. Default SpaarkeCore (it already owns sprk_externalrecordaccess).

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Deploy-ExternalRecordAccessContactGrantor.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
    Dry run: every step's present / missing state; zero writes.

.EXAMPLE
    .\Deploy-ExternalRecordAccessContactGrantor.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Apply

.EXAMPLE
    .\Deploy-ExternalRecordAccessContactGrantor.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify

.NOTES
    unified-access-control-r2 task 140 (#1063). Code: src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/
    ExternalGrantLifecycle.cs (GrantedByContactNavigationProperty, RowSelect) — the names are pinned against this script
    by tests/Spaarke.ArchTests/ContactGrantGuardTests.cs (TheIssuerColumnAgreesWithTheSchemaScript); the solution step
    by SchemaScriptSolutionMembershipGuardTests. Schema doc: src/solutions/SpaarkeCore/entities/sprk_externalrecordaccess/
    entity-schema.md. Shape: scripts/Set-RecordCreatorPersonSchema.ps1 (task 133). Auth: the operator's own az CLI
    identity (System Administrator in the environment). No secrets.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [string[]]$BffApplicationIds = @(),
    [string]$SolutionUniqueName = 'SpaarkeCore',
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
if (($Apply -or $Verify) -and $BffApplicationIds.Count -eq 0) {
    throw '-BffApplicationIds is required for -Apply and -Verify: the writer profile must name the BFF application user(s) explicitly.'
}
# A single comma-joined string (pwsh -File) is split, never sent as one id.
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (the BFF uses these exact names: ExternalGrantLifecycle.GrantedByContactNavigationProperty / RowSelect) ──
$Column = 'sprk_grantedbycontact'
$ColumnSchemaName = 'sprk_GrantedByContact'
$ExpectedNavigationProperty = 'sprk_GrantedByContact'
$TargetEntity = 'contact'
$Tables = @('sprk_externalrecordaccess')
$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
$WriterProfileName = 'Spaarke BFF-Managed Field Writers'
$Secured = $true
$ExpectedCascade = [ordered]@{ Assign = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade'; Merge = 'NoCascade'; Delete = 'RemoveLink' }
function RelationshipSchemaName([string]$Table) { "sprk_contact_$($Table)_grantedbycontact" }

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
# Counts every row a query matches, following @odata.nextLink past Dataverse's 5,000-row page (task 133 r1, verifier
# finding 7: the informational count stopped at the first page).
function Measure-DvRows([string]$Path) {
    $h = $headers.Clone(); $h['Prefer'] = 'odata.maxpagesize=5000'
    $count = 0; $next = "$Api/$Path"
    while ($next) {
        $page = Invoke-RestMethod -Uri $next -Headers $h -Method Get
        $count += @($page.value).Count
        $next = $page.'@odata.nextLink'
    }
    $count
}
function Invoke-DvWrite([string]$Method, [string]$Path, $Body, [hashtable]$Extra = @{}) {
    $h = $headers.Clone(); foreach ($k in $Extra.Keys) { $h[$k] = $Extra[$k] }
    $json = if ($null -eq $Body) { $null } else { $Body | ConvertTo-Json -Depth 20 -Compress }
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method $Method -Body $json
}
function Try-DvGet([string]$Path) { try { Invoke-DvGet $Path } catch { $null } }
# Metadata writes propagate asynchronously (task 141 live run 2026-10-02): poll a read until it answers, or give up
# loudly — never treat "not visible yet" as "absent".
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
Write-Host "`n(a) $Column on sprk_externalrecordaccess"
$attrs = @{}
foreach ($t in $Tables) {
    $attrPath = "EntityDefinitions(LogicalName='$t')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=LogicalName,SchemaName,Targets,IsSecured,MetadataId"
    $attr = Try-DvGet $attrPath
    if ($attr) {
        $targets = @($attr.Targets)
        if (-not $attr.LogicalName.StartsWith('sprk_')) { Report 'FAIL' "$t.$($attr.LogicalName) does not carry the sprk_ prefix (AP-13)" }
        elseif ($targets.Count -eq 1 -and $targets[0] -eq $TargetEntity) { Report 'OK' "$t.$Column (lookup -> $TargetEntity)" }
        else { Report 'FAIL' "$t.$Column exists but targets [$($targets -join ', ')] — expected exactly $TargetEntity" }
        $attrs[$t] = $attr
    } elseif ($Verify) { Report 'MISSING' "$t.$Column" }
    elseif ($IsDryRun) { Report 'WOULD' "create $t.$Column (lookup -> $TargetEntity, relationship $(RelationshipSchemaName $t))" }
    else {
        $solutionRow = @((Invoke-DvGet "solutions?`$select=uniquename&`$expand=publisherid(`$select=customizationprefix)&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
        if (-not $solutionRow -or $solutionRow.publisherid.customizationprefix -ne 'sprk') {
            throw "Solution '$SolutionUniqueName' is not published under the 'sprk' prefix — refusing to create a column (AP-13)."
        }
        Invoke-DvWrite POST 'RelationshipDefinitions' @{
            '@odata.type'        = 'Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata'
            SchemaName           = (RelationshipSchemaName $t)
            ReferencedEntity     = $TargetEntity
            ReferencingEntity    = $t
            ReferencingEntityNavigationPropertyName = $ExpectedNavigationProperty
            CascadeConfiguration = @{ Assign = 'NoCascade'; Delete = 'RemoveLink'; Merge = 'NoCascade'; Reparent = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade' }
            AssociatedMenuConfiguration = @{ Behavior = 'DoNotDisplay'; Group = 'Details'; Order = 10000 }
            Lookup               = @{
                '@odata.type' = 'Microsoft.Dynamics.CRM.LookupAttributeMetadata'
                SchemaName    = $ColumnSchemaName
                DisplayName   = (New-Label 'Granted By (Contact)')
                Description   = (New-Label 'The CONTACT who issued this grant from the external SPA (contact-side Grant Access). Written by the BFF only (field-secured); sprk_grantedby (systemuser) stays empty on such a grant. A contact may change or revoke only the grants it issued. unified-access-control-r2 task 140, owner C4 / Q2.')
                RequiredLevel = @{ Value = 'None' }
            }
        } @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName } | Out-Null
        Report 'DONE' "created $t.$Column"
        $attrs[$t] = Wait-DvRead $attrPath "$t.$Column metadata"
        Wait-DvRead "sprk_externalrecordaccesses?`$select=_$($Column)_value&`$top=1" "$t.$Column in data queries" | Out-Null
    }

    # The relationship: its target, its cascade, and the navigation property the BFF binds (sprk_GrantedByContact@odata.bind).
    $rel = Try-DvGet "RelationshipDefinitions(SchemaName='$(RelationshipSchemaName $t)')/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?`$select=SchemaName,ReferencedEntity,ReferencingEntity,ReferencingAttribute,ReferencingEntityNavigationPropertyName,CascadeConfiguration,MetadataId"
    if ($rel) {
        $bad = @($ExpectedCascade.Keys | Where-Object { $rel.CascadeConfiguration.$_ -ne $ExpectedCascade[$_] } |
            ForEach-Object { "$_=$($rel.CascadeConfiguration.$_) (expected $($ExpectedCascade[$_]))" })
        if ($rel.ReferencingAttribute -ne $Column) { Report 'FAIL' "$(RelationshipSchemaName $t) references $($rel.ReferencingAttribute), not $Column" }
        elseif ($rel.ReferencedEntity -ne $TargetEntity) { Report 'FAIL' "$(RelationshipSchemaName $t) references $($rel.ReferencedEntity), not $TargetEntity" }
        elseif ($rel.ReferencingEntityNavigationPropertyName -ne $ExpectedNavigationProperty) { Report 'FAIL' "$(RelationshipSchemaName $t) navigation property is $($rel.ReferencingEntityNavigationPropertyName); the BFF binds $ExpectedNavigationProperty" }
        elseif ($bad.Count -gt 0) { Report 'FAIL' "$(RelationshipSchemaName $t) cascade: $($bad -join '; ')" }
        else { Report 'OK' "$(RelationshipSchemaName $t): -> $TargetEntity, navigation $ExpectedNavigationProperty, no Assign/Share cascade, Delete RemoveLink" }
    } elseif ($attrs[$t]) { Report 'FAIL' "$t.$Column exists but relationship $(RelationshipSchemaName $t) was not found — it was created another way; check its cascade and navigation property by hand" }
    elseif ($Verify) { Report 'MISSING' "relationship $(RelationshipSchemaName $t)" }
}

# ── (b) PROFILES + MEMBERS ──────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(b) Field security profiles and members"
function Ensure-Profile([string]$Name, [string]$Description) {
    $p = @((Invoke-DvGet "fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$Name'").value) | Select-Object -First 1
    if ($p) { Report 'OK' "profile '$Name'"; return $p.fieldsecurityprofileid }
    if ($Verify) { Report 'MISSING' "profile '$Name'"; return $null }
    if ($IsDryRun) { Report 'WOULD' "create profile '$Name'"; return $null }
    $created = Invoke-DvWrite POST 'fieldsecurityprofiles' @{ name = $Name; description = $Description } @{ Prefer = 'return=representation' }
    Report 'DONE' "created profile '$Name'"
    return $created.fieldsecurityprofileid
}
$readerId = Ensure-Profile $ReaderProfileName 'Read on columns ONLY the BFF writes (task 133 sprk_createdbyperson, task 150 sprk_issecure, task 140 sprk_externalrecordaccess.sprk_grantedbycontact). Associated with EVERY business unit default team, so every user keeps reading them — a secured column is HIDDEN from anyone without Read.'
$writerId = Ensure-Profile $WriterProfileName 'Read/Create/Update on columns ONLY the BFF writes (task 133 sprk_createdbyperson, task 150 sprk_issecure, task 140 sprk_grantedbycontact). Members: the BFF application user(s) ONLY.'

$bffUsers = @()
foreach ($appId in $BffApplicationIds) {
    $u = @((Invoke-DvGet "systemusers?`$select=systemuserid,fullname&`$filter=applicationid eq $appId").value) | Select-Object -First 1
    if (-not $u) { Report 'FAIL' "no application user for appId $appId in this environment"; continue }
    $bffUsers += $u
}
$defaultTeams = @((Invoke-DvGet 'teams?$select=teamid,name&$filter=isdefault eq true').value)
function Ensure-Member([string]$ProfileId, [string]$ProfileName, [string]$Nav, [string]$Set, $Id, [string]$Label) {
    if (-not $ProfileId) { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "$ProfileName member: $Label"; return }
    $key = if ($Set -eq 'teams') { 'teamid' } else { 'systemuserid' }
    $existing = @((Invoke-DvGet "fieldsecurityprofiles($ProfileId)/$Nav`?`$select=$key").value)
    if ($existing | Where-Object { $_.$key -eq $Id }) { Report 'OK' "$ProfileName member: $Label"; return }
    if ($Verify) { Report 'MISSING' "$ProfileName member: $Label"; return }
    if ($IsDryRun) { Report 'WOULD' "add $Label to $ProfileName"; return }
    Invoke-DvWrite POST "fieldsecurityprofiles($ProfileId)/$Nav/`$ref" @{ '@odata.id' = "$Api/$Set($Id)" } | Out-Null
    Report 'DONE' "added $Label to $ProfileName"
}
foreach ($u in $bffUsers) { Ensure-Member $writerId $WriterProfileName 'systemuserprofiles_association' 'systemusers' $u.systemuserid "app user '$($u.fullname)'" }
foreach ($t in $defaultTeams) { Ensure-Member $readerId $ReaderProfileName 'teamprofiles_association' 'teams' $t.teamid "default team '$($t.name)'" }

# The writer profile's membership IS the lock (task 133 r1, verifier finding 7): a human — or a team — added to it could
# name any contact as a grant's issuer, and that contact could then revoke the grant from the external SPA. So every member that is not one of
# the -BffApplicationIds users is FAIL, in every mode. Reported, never removed: who belongs there is an operator decision.
if ($writerId) {
    $allowedWriters = @($bffUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    $writerUsers = @((Invoke-DvGet "fieldsecurityprofiles($writerId)/systemuserprofiles_association?`$select=systemuserid,fullname,applicationid").value)
    foreach ($m in $writerUsers | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowedWriters }) {
        $kind = if ($m.applicationid) { "application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a HUMAN user' }
        Report 'FAIL' "$WriterProfileName member '$($m.fullname)' ($($m.systemuserid)) is $kind — only the BFF may write $Column"
    }
    $writerTeams = @((Invoke-DvGet "fieldsecurityprofiles($writerId)/teamprofiles_association?`$select=teamid,name").value)
    foreach ($tm in $writerTeams) {
        Report 'FAIL' "$WriterProfileName has team '$($tm.name)' ($($tm.teamid)) as a member — every member of it could write $Column"
    }
    if (($writerUsers | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowedWriters }).Count -eq 0 -and $writerTeams.Count -eq 0) {
        Report 'OK' "$WriterProfileName has no member besides the BFF application user(s)"
    }
}

# ── (c) FIELD-LEVEL SECURITY ────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(c) Field-level security"
foreach ($t in $Tables) {
    if (-not $attrs[$t]) { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "secure $t.$Column and grant both profiles (after the column exists)"; continue }

    if ($attrs[$t].IsSecured -eq $Secured) { Report 'OK' "$t.$Column is field-secured" }
    elseif ($Verify) { Report 'MISSING' "$t.$Column is NOT field-secured — any user with Write on a grant could name a contact as its issuer, letting that contact revoke it" }
    elseif ($IsDryRun) { Report 'WOULD' "secure $t.$Column" }
    else {
        $typed = Invoke-DvGet "EntityDefinitions(LogicalName='$t')/Attributes(LogicalName='$Column')"
        $typed.IsSecured = $Secured
        Invoke-DvWrite PUT "EntityDefinitions(LogicalName='$t')/Attributes(LogicalName='$Column')" $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
        Report 'DONE' "secured $t.$Column"
    }

    foreach ($spec in @(@{ Id = $readerId; Name = $ReaderProfileName; Create = 0; Update = 0 }, @{ Id = $writerId; Name = $WriterProfileName; Create = 4; Update = 4 })) {
        if (-not $spec.Id) { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "$($spec.Name) permission on $t.$Column"; continue }
        $perm = @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $($spec.Id) and entityname eq '$t' and attributelogicalname eq '$Column'").value) | Select-Object -First 1
        if ($perm) {
            if ($perm.canread -eq 4 -and $perm.cancreate -eq $spec.Create -and $perm.canupdate -eq $spec.Update) { Report 'OK' "$($spec.Name) on $t.$Column" }
            else { Report 'FAIL' "$($spec.Name) on $t.$Column is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$($spec.Create) update=$($spec.Update)" }
            continue
        }
        if ($Verify) { Report 'MISSING' "$($spec.Name) on $t.$Column"; continue }
        if ($IsDryRun) { Report 'WOULD' "grant $($spec.Name) on $t.$Column (read=4 create=$($spec.Create) update=$($spec.Update))"; continue }
        # Securing a column propagates asynchronously: a grant right after it may be refused 0x8004f508 "... is NOT
        # secured for entity fieldpermission" (task 141 live run 2026-10-02). Retry that one error; anything else throws.
        for ($attempt = 1; ; $attempt++) {
            try {
                Invoke-DvWrite POST 'fieldpermissions' @{
                    entityname = $t; attributelogicalname = $Column
                    canread = 4; cancreate = $spec.Create; canupdate = $spec.Update
                    'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($($spec.Id))"
                } | Out-Null
                break
            } catch {
                $notYetSecured = "$($_.ErrorDetails.Message) $($_.Exception.Message)" -match '0x8004f508'
                if (-not $notYetSecured -or $attempt -ge 12) { throw }
                Write-Host "    $t.$Column not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
                Start-Sleep -Seconds 10
            }
        }
        Report 'DONE' "granted $($spec.Name) on $t.$Column"
    }

    $writers = @((Invoke-DvGet "fieldpermissions?`$select=canupdate,cancreate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$t' and attributelogicalname eq '$Column' and (canupdate eq 4 or cancreate eq 4)").value)
    foreach ($w in $writers | Where-Object { $_.fieldsecurityprofileid.name -notin $WriterProfileName, 'System Administrator' }) {
        Report 'FAIL' "profile '$($w.fieldsecurityprofileid.name)' can also write $t.$Column — only the BFF may"
    }
}

# ── (d) SOLUTION ────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(d) Solution components"
$solution = @((Invoke-DvGet "solutions?`$select=solutionid,uniquename&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
if (-not $solution) { Report 'FAIL' "solution '$SolutionUniqueName' not found" }
else {
    # Membership through the ONE shared helper (scripts/common/DataverseSolutionMembership.ps1): paged, and a column or
    # relationship of a table in the solution with rootcomponentbehavior = 0 ("include subcomponents") counts as in it.
    # sprk_externalrecordaccess IS such a table in SpaarkeCore on spaarkedev1 (solutioncomponent
    # 26217b50-6b28-f111-88b5-7ced8d1dc988, rootcomponentbehavior 0): its new column and relationship get no row of their
    # own and AddSolutionComponent on them is a no-op, so an own objectid check would report them MISSING forever and
    # -Verify (gate G-140-1) could never pass (batch-4 live gates 2026-10-03, the 133/143 false negative).
    $components = [System.Collections.Generic.List[object]]::new()
    foreach ($t in $Tables) {
        $tableId = (Invoke-DvGet "EntityDefinitions(LogicalName='$t')?`$select=MetadataId").MetadataId
        if ($attrs[$t]) { $components.Add(@{ Id = $attrs[$t].MetadataId; Type = 2; Label = "$t.$Column"; TableId = $tableId }) }
        $rel = Try-DvGet "RelationshipDefinitions(SchemaName='$(RelationshipSchemaName $t)')?`$select=MetadataId"
        if ($rel) { $components.Add(@{ Id = $rel.MetadataId; Type = 10; Label = "relationship $(RelationshipSchemaName $t)"; TableId = $tableId }) }
    }
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

# ── (e) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($Apply) {
    $entities = ($Tables | ForEach-Object { "<entity>$_</entity>" }) -join ''
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities>$entities</entities></importexportxml>" } | Out-Null
    Write-Host "`nPublished $($Tables -join ', ')." -ForegroundColor Green
}

if ($Verify) {
    if ($gaps.Count -eq 0) { Write-Host "`nVERIFY PASS: sprk_grantedbycontact is in place and only the BFF can write it." -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
if ($IsDryRun) { Write-Host "`nDRY RUN complete — nothing was written. Re-run with -Apply." -ForegroundColor Cyan }
