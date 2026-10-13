#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Adds the pending-invite marker — contact.sprk_externalinvitedon, "External invite sent on", a UTC date-and-time — and
    locks it with field-level security so ONLY the BFF can write it (unified-access-control-r2 task 184; owner decision
    round 96, 2026-10-13: the no-schema rule lifted for this one column). Dry run by default; -Apply writes; -Verify checks.
    Idempotent. In the pattern of scripts/Set-ChildRecordCreatorPersonSchema.ps1 (task 146).

.DESCRIPTION
    WHY. A CIAM user can obtain a token for ANY customer's audience (the portal SPA client is pre-authorized for every
    customer — provisioning spike S1), so the CIAM sign-in's first-login email bind (E11) must bind only a contact THIS
    environment means: one with an active contact-level grant, or one with a pending invite. Before task 184 the invite left
    no evidence of itself on the contact: it bound the oid in the same request, so a contact the invite could not bind —
    or one whose person already had a CIAM account — looked exactly like a contact somebody had typed in.

    The BFF's invite flow (/invite and /invite-and-grant, InviteExternalUserEndpoint.ProvisionAsync) writes the column
    BEFORE it creates the CIAM account, on every path that provisions (a new account, or "ExistingAccount" when the person
    already has one). A re-invite overwrites it; nothing clears it. "Pending" = set AND no oid binding
    (sprk_externalobjectid). The CIAM plane's E11 (ContactBindingDecision) reads it in its OWN query, so a BFF deployed
    before this script reads "no invite evidence" there and the grant evidence still works. The column is NOT a binding:
    sprk_identityplane is untouched, and task 141's "plane marker with no oid = Unreadable" contract stands.

    Field security is the point. A user who could set this column on any contact could open that contact to an email bind
    by whichever CIAM user signs in with its email. So the column is secured, the reader profile may only read it, and the
    writer profile — whose ONLY members are the BFF application user(s) — may write it.

    The script provisions, in order:
      (a) COLUMN     contact.sprk_externalinvitedon (schema sprk_ExternalInvitedOn): DateTime, Format DateAndTime,
                     DateTimeBehavior UserLocal (stored and returned in UTC; a form shows it in the viewer's time zone — the
                     behavior of the sibling sprk_identitycollisionon), RequiredLevel None.
      (b) PROFILES   The EXISTING "Spaarke BFF-Managed Field Readers" (Read) and "Spaarke BFF-Managed Field Writers"
                     (Read + Create + Update) profiles of tasks 133 / 146. NEVER created here: provisioning's H7b creates and
                     populates them on a new stamp. A missing profile is FAIL in every mode. Membership is REPORTED, never
                     written: every -BffApplicationIds application user must be a writer member, and any OTHER writer member —
                     a human, another application user, any team — is FAIL (the writer profile's membership IS the lock).
      (c) FLS        The column becomes field-secured; the reader profile gets read=4 create=0 update=0, the writer profile
                     read=4 create=4 update=4. Any OTHER profile (besides the platform's System Administrator profile) that
                     can create or update the column is FAIL.
      (d) SOLUTION   The column is a component of EVERY -SolutionUniqueNames solution (default SpaarkeCore AND SpaarkeMaster).
                     Every environment, customer stamps included, imports only the CI-built SpaarkeMaster, and contact sits
                     in SpaarkeMaster with rootcomponentbehavior = 1 (no subcomponents) — so the column travels only when it
                     is added explicitly. A column counts as included when added directly, or when its table is in the
                     solution with rootcomponentbehavior = 0. Both profiles must be in every listed solution too (their
                     field-permission rows travel with them); a profile missing from one is added by -Apply.
      (e) PUBLISH    contact.

    ⚠️ DEPLOY ORDER. A BFF carrying task 184 WRITES this column on every invite.
      - DEVELOPMENT: run this script (-Apply, then -Verify exit 0) BEFORE deploying that BFF.
      - CUSTOMER STAMPS get the column only through the SpaarkeMaster import, and scripts/Deploy-Release.ps1 runs Phase 2
        (the BFF) before Phase 3 (the SpaarkeMaster import). Until Phase 3 completes, the BFF logs an error per invite
        and:
          * a NEW-account invite still succeeds (its CIAM create and oid bind happen in the same request; it needs no
            marker);
          * an invite of a person who ALREADY has a CIAM account ("ExistingAccount"), and an invite whose oid write
            failed, answer a retryable 503 sdap.access.invite.invite_marker_not_written and send NO email — the marker
            is those contacts' only evidence. Retry after the import.

    ⚠️ RELEASE. Stamps receive the column and its profile permissions only through a SpaarkeMaster version bump and release
    export after -Apply has run in the development environment.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids whose Dataverse application users must be members of the writer profile — the BFF managed
    identity (dev: 5967251e-171c-46fe-a6c2-ef843c90309d) and, while it is still an application user, the BFF app
    registration (dev: 1e40baad-e065-4aea-a8d4-4b7ab273458c). Required for -Apply and -Verify.

.PARAMETER SolutionUniqueNames
    The unmanaged solutions that must carry the column. Default SpaarkeCore, SpaarkeMaster.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-ExternalInviteMarkerSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c
    Dry run: every step's present / missing state; zero writes.

.EXAMPLE
    .\Set-ExternalInviteMarkerSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Apply

.EXAMPLE
    .\Set-ExternalInviteMarkerSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify

.NOTES
    unified-access-control-r2 task 184. Code: src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ContactBindingDecision.cs
    (ExternalInvitedOnColumn) — the column name, table, type and field security are pinned against this script by
    ExternalInviteMarkerSchemaAgreementTests. Run it inside PowerShell (`& ./scripts/Set-ExternalInviteMarkerSchema.ps1 ...`):
    `pwsh -File` passes the comma list as ONE string. Auth: the operator's own az CLI identity (System Administrator in the
    environment). No secrets.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [string[]]$BffApplicationIds = @(),
    [string[]]$SolutionUniqueNames = @('SpaarkeCore', 'SpaarkeMaster'),
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
$SolutionUniqueNames = @($SolutionUniqueNames | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (the BFF uses these exact names: ContactBindingDecision.ExternalInvitedOnColumn) ──────────────────
$Table = 'contact'
$Column = 'sprk_externalinvitedon'
$ColumnSchemaName = 'sprk_ExternalInvitedOn'
$DisplayName = 'External invite sent on'
$AttributeType = 'DateTime'
$Format = 'DateAndTime'
$Behavior = 'UserLocal'
$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
$WriterProfileName = 'Spaarke BFF-Managed Field Writers'
$Secured = $true

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
# Metadata writes propagate asynchronously: poll a read until it answers, or give up loudly — never treat "not visible
# yet" as "absent".
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
Write-Host "Solutions   : $($SolutionUniqueNames -join ', ')"
Write-Host "Column      : $Table.$Column"

# ── (a) COLUMN ──────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(a) $Table.$Column"
$attrPath = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.DateTimeAttributeMetadata?`$select=LogicalName,AttributeType,Format,DateTimeBehavior,IsSecured,MetadataId"
$attr = Try-DvGet $attrPath
if ($attr) {
    if ($attr.AttributeType -ne $AttributeType -or $attr.Format -ne $Format -or $attr.DateTimeBehavior.Value -ne $Behavior) {
        Report 'FAIL' "$Table.$Column exists as $($attr.AttributeType) / $($attr.Format) / $($attr.DateTimeBehavior.Value) — expected $AttributeType / $Format / $Behavior"
    } else { Report 'OK' "$Table.$Column ($AttributeType, $Format, $Behavior)" }
} elseif ($Verify) { Report 'MISSING' "$Table.$Column" }
elseif ($IsDryRun) { Report 'WOULD' "create $Table.$Column ($AttributeType, $Format, $Behavior)" }
else {
    Invoke-DvWrite POST "EntityDefinitions(LogicalName='$Table')/Attributes" @{
        '@odata.type'    = 'Microsoft.Dynamics.CRM.DateTimeAttributeMetadata'
        SchemaName       = $ColumnSchemaName
        DisplayName      = (New-Label $DisplayName)
        Description      = (New-Label 'When the BFF invite flow last invited this contact to the external portal (UTC). Written by the BFF only (field-secured), BEFORE the CIAM account is created; a re-invite overwrites it; never cleared. Set while the contact has no external oid binding = a pending invite, which lets the first CIAM sign-in bind this contact. unified-access-control-r2 task 184, owner round 96.')
        RequiredLevel    = @{ Value = 'None' }
        Format           = $Format
        DateTimeBehavior = @{ Value = $Behavior }
    } @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueNames[0] } | Out-Null
    Report 'DONE' "created $Table.$Column"
    $attr = Wait-DvRead $attrPath "$Table.$Column metadata"
    Wait-DvRead "contacts?`$select=$Column&`$top=1" "$Table.$Column in data queries" | Out-Null
}

# ── (b) PROFILES (existing only) + MEMBERS (reported) ───────────────────────────────────────────────────────
Write-Host "`n(b) Field security profiles (existing; never created here) and their members (reported, never written)"
function Find-Profile([string]$Name) {
    $p = @((Invoke-DvGet "fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$Name'").value) | Select-Object -First 1
    if ($p) { Report 'OK' "profile '$Name'"; return $p.fieldsecurityprofileid }
    Report 'FAIL' "profile '$Name' does not exist — provisioning's H7b (or tasks 133 / 146) creates it; this script never does"
    return $null
}
$readerId = Find-Profile $ReaderProfileName
$writerId = Find-Profile $WriterProfileName

$bffUsers = @()
foreach ($appId in $BffApplicationIds) {
    $u = @((Invoke-DvGet "systemusers?`$select=systemuserid,fullname&`$filter=applicationid eq $appId").value) | Select-Object -First 1
    if (-not $u) { Report 'FAIL' "no application user for appId $appId in this environment"; continue }
    $bffUsers += $u
}

# The writer profile's membership IS the lock: a human — or a team — in it could mark any contact invited, and E11 would
# then email-bind it. So the BFF must be a member and nobody else may be, in every mode.
if ($writerId) {
    $writerUsers = @((Invoke-DvGet "fieldsecurityprofiles($writerId)/systemuserprofiles_association?`$select=systemuserid,fullname,applicationid").value)
    $memberIds = @($writerUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    foreach ($u in $bffUsers) {
        if ($u.systemuserid.ToString().ToLowerInvariant() -in $memberIds) { Report 'OK' "$WriterProfileName member: app user '$($u.fullname)'" }
        else { Report 'FAIL' "$WriterProfileName does not have app user '$($u.fullname)' — the BFF could not write $Column (H7b populates the profile)" }
    }
    $allowedWriters = @($bffUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    $strangers = @($writerUsers | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowedWriters })
    foreach ($m in $strangers) {
        $kind = if ($m.applicationid) { "application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a HUMAN user' }
        Report 'FAIL' "$WriterProfileName member '$($m.fullname)' ($($m.systemuserid)) is $kind — only the BFF may write $Column"
    }
    $writerTeams = @((Invoke-DvGet "fieldsecurityprofiles($writerId)/teamprofiles_association?`$select=teamid,name").value)
    foreach ($tm in $writerTeams) {
        Report 'FAIL' "$WriterProfileName has team '$($tm.name)' ($($tm.teamid)) as a member — every member of it could write $Column"
    }
    if ($strangers.Count -eq 0 -and $writerTeams.Count -eq 0) { Report 'OK' "$WriterProfileName has no member besides the BFF application user(s)" }
}
if ($readerId) {
    $readerTeams = @((Invoke-DvGet "fieldsecurityprofiles($readerId)/teamprofiles_association?`$select=teamid").value)
    Report 'INFO' "$ReaderProfileName has $($readerTeams.Count) team member(s) (H7b associates every business unit's default team)"
}

# ── (c) FIELD-LEVEL SECURITY ────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(c) Field-level security"
if (-not $attr) { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "secure $Table.$Column and grant both profiles (after the column exists)" }
else {
    if ($attr.IsSecured -eq $Secured) { Report 'OK' "$Table.$Column is field-secured" }
    elseif ($Verify) { Report 'MISSING' "$Table.$Column is NOT field-secured — any user with contact Write could mark a contact invited" }
    elseif ($IsDryRun) { Report 'WOULD' "secure $Table.$Column" }
    else {
        $typed = Invoke-DvGet "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')"
        $typed.IsSecured = $Secured
        Invoke-DvWrite PUT "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')" $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
        Report 'DONE' "secured $Table.$Column"
    }

    foreach ($spec in @(@{ Id = $readerId; Name = $ReaderProfileName; Create = 0; Update = 0 }, @{ Id = $writerId; Name = $WriterProfileName; Create = 4; Update = 4 })) {
        if (-not $spec.Id) { Report 'FAIL' "$($spec.Name) permission on $Table.$Column (the profile does not exist)"; continue }
        $perm = @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $($spec.Id) and entityname eq '$Table' and attributelogicalname eq '$Column'").value) | Select-Object -First 1
        if ($perm) {
            if ($perm.canread -eq 4 -and $perm.cancreate -eq $spec.Create -and $perm.canupdate -eq $spec.Update) { Report 'OK' "$($spec.Name) on $Table.$Column" }
            else { Report 'FAIL' "$($spec.Name) on $Table.$Column is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$($spec.Create) update=$($spec.Update)" }
            continue
        }
        if ($Verify) { Report 'MISSING' "$($spec.Name) on $Table.$Column"; continue }
        if ($IsDryRun) { Report 'WOULD' "grant $($spec.Name) on $Table.$Column (read=4 create=$($spec.Create) update=$($spec.Update))"; continue }
        # Securing a column propagates asynchronously: a grant right after it may be refused 0x8004f508 "... is NOT
        # secured for entity fieldpermission". Retry that one error; anything else throws.
        for ($attempt = 1; ; $attempt++) {
            try {
                Invoke-DvWrite POST 'fieldpermissions' @{
                    entityname = $Table; attributelogicalname = $Column
                    canread = 4; cancreate = $spec.Create; canupdate = $spec.Update
                    'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($($spec.Id))"
                } | Out-Null
                break
            } catch {
                $notYetSecured = "$($_.ErrorDetails.Message) $($_.Exception.Message)" -match '0x8004f508'
                if (-not $notYetSecured -or $attempt -ge 12) { throw }
                Write-Host "    $Table.$Column not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
                Start-Sleep -Seconds 10
            }
        }
        Report 'DONE' "granted $($spec.Name) on $Table.$Column"
    }

    $writers = @((Invoke-DvGet "fieldpermissions?`$select=canupdate,cancreate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$Table' and attributelogicalname eq '$Column' and (canupdate eq 4 or cancreate eq 4)").value)
    foreach ($w in $writers | Where-Object { $_.fieldsecurityprofileid.name -notin $WriterProfileName, 'System Administrator' }) {
        Report 'FAIL' "profile '$($w.fieldsecurityprofileid.name)' can also write $Table.$Column — only the BFF may"
    }
}

# ── (d) SOLUTIONS ───────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(d) Solution components"
$tableId = (Invoke-DvGet "EntityDefinitions(LogicalName='$Table')?`$select=MetadataId").MetadataId
foreach ($solutionName in $SolutionUniqueNames) {
    $solution = @((Invoke-DvGet "solutions?`$select=solutionid,uniquename&`$filter=uniquename eq '$solutionName'").value) | Select-Object -First 1
    if (-not $solution) { Report 'FAIL' "solution '$solutionName' not found"; continue }

    # Paged, and a table in the solution with rootcomponentbehavior = 0 ("include subcomponents") carries its columns
    # implicitly (scripts/common/DataverseSolutionMembership.ps1). contact in SpaarkeMaster is rootcomponentbehavior = 1,
    # so there the column must be a DIRECT component.
    $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid

    $components = [System.Collections.Generic.List[object]]::new()
    if ($attr) { $components.Add(@{ Id = $attr.MetadataId; Type = 2; Label = "$Table.$Column"; TableId = $tableId }) }
    else { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "add $Table.$Column to $solutionName (after the column exists)" }
    foreach ($p in @(@{ Id = $readerId; Name = $ReaderProfileName }, @{ Id = $writerId; Name = $WriterProfileName })) {
        if ($p.Id) { $components.Add(@{ Id = $p.Id; Type = 70; Label = "field security profile '$($p.Name)'" }) }
    }

    foreach ($c in $components) {
        $how = Test-DvInSolution -Membership $membership -ComponentId $c.Id -TableMetadataId $c['TableId']
        if ($how -eq 'Direct') { Report 'OK' "$($c.Label) in $solutionName"; continue }
        if ($how -eq 'ViaTable') { Report 'OK' "$($c.Label) in $solutionName (its table includes subcomponents)"; continue }
        if ($Verify) { Report 'MISSING' "$($c.Label) in $solutionName"; continue }
        if ($IsDryRun) { Report 'WOULD' "add $($c.Label) to $solutionName"; continue }
        Invoke-DvWrite POST 'AddSolutionComponent' @{ ComponentId = $c.Id; ComponentType = $c.Type; SolutionUniqueName = $solutionName; AddRequiredComponents = $false } | Out-Null
        Report 'DONE' "added $($c.Label) to $solutionName"
    }
}

# ── (e) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($Apply) {
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
    Write-Host "`nPublished $Table." -ForegroundColor Green
    Write-Host 'Next: a SpaarkeMaster version bump and release export, so that stamps receive the column and its profile permissions.' -ForegroundColor Cyan
}

if ($Verify) {
    if ($gaps.Count -eq 0) { Write-Host "`nVERIFY PASS: $Table.$Column is in place, field-secured, in every listed solution, and only the BFF can write it." -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
if ($IsDryRun) { Write-Host "`nDRY RUN complete — nothing was written. Re-run with -Apply." -ForegroundColor Cyan }
