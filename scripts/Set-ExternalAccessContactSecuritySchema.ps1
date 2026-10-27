#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Field-secures the contact's access-level and identity bookkeeping columns, and adds three new BFF-only columns, so that
    only the BFF (and, for the access level, Standing Grant Administrators) writes them. Dry run by default; -Apply
    writes; -Verify checks every profile x column pair. Idempotent. In the pattern of
    scripts/Set-ExternalInviteMarkerSchema.ps1 and scripts/Set-ExternalInviteCodeHashSchema.ps1.

.DESCRIPTION
    THE RULE. Access-level and identity bookkeeping columns on contact are field-secured; only the BFF and Standing Grant
    Administrators write them, and every user keeps reading the ones a person needs to see.

    The script provisions, in order:
      (a) NEW COLUMNS   Created SECURED (IsSecured = true in the create request), each in the first -SolutionUniqueNames
                        solution:
                          contact.sprk_externalinvitedemailhash  single line of text, max 100, auditing off
                          contact.sprk_externalinvitedtenantid   single line of text, max 36 (a GUID), auditing on
                          contact.sprk_externalaccessremovedon   date and time (UserLocal, returned in UTC), auditing on
                        No BFF code reads or writes them yet; they are created here so a later BFF can.
      (b) PROFILES      The EXISTING "Spaarke BFF-Managed Field Readers" / "Writers", "Spaarke Identity Link Readers" /
                        "Writers" and "Standing Grant Administrators" profiles. NEVER created here (SpaarkeMaster ships
                        them; provisioning's H7b populates them). A missing profile is FAIL in every mode. Membership is
                        REPORTED, never written: every -BffApplicationIds application user must be a member of both
                        writer profiles, and any other member of a writer profile (a human, another application user,
                        any team) is FAIL. Standing Grant Administrators' members are an operator decision: reported only.
      (c) FLS           Every column in $Grants becomes field-secured. An existing column is secured and granted in the
                        same pass (Dataverse accepts a field permission only on a secured column), so the time in which
                        the column is secured but not yet granted is a few seconds.
      (d) GRANTS        Exactly the rows in $Grants (read / create / update; 4 = allowed, 0 = not). A row that exists with
                        other values is FAIL, never changed here (an operator decides). Any OTHER profile that can read,
                        create or update one of these columns is FAIL — the platform's System Administrator profile
                        excepted.
      (e) SOLUTIONS     Every column and every profile is a component of EVERY -SolutionUniqueNames solution (default
                        SpaarkeCore AND SpaarkeMaster). contact sits in SpaarkeMaster with rootcomponentbehavior = 1, so
                        a column travels only when it is added explicitly.
      (f) PUBLISH       contact.
      (g) PROOF READS   -Verify only. The BFF trusts an EMPTY value of a secured column only once it has proven its own
                        read of the column (SecuredColumnReadProof: the System Administrator role by its template id, or a field security
                        profile granting Read, held directly or through a team). Each -BffApplicationIds application
                        user must be able to READ every platform table that proof reads — systemuser (WhoAmI), role,
                        systemuserroles, fieldpermission, systemuserprofiles, teamprofiles, teammembership — or every
                        such empty read is refused (fail closed). The proof's own queries are run AS that user
                        (MSCRMCallerID); the operator running -Verify needs the act-on-behalf-of privilege for it.
      (h) STANDING GRANT -Verify only, read-only. Each -BffApplicationIds application user may update
                        contact.sprk_standinggrant ONLY through its DIRECT membership of Standing Grant Administrators
                        (the person removal clears the standing grant), and no other profile it holds, directly or through
                        a team, grants create or update on it. The team leg is one server-side join. The membership is
                        added by a provisioning step; this script never writes it, so a missing one is FAIL here.
                        (scripts/common/StandingGrantWriterInvariant.ps1; the profile names are S19's, pinned by
                        StandingGrantVerifyAgreementTests.)

    WHO READS WHAT (the BFF application user must keep reading every column):
      sprk_accesspermissiongrant      every user (BFF-Managed Field Readers, 4/0/0); the BFF reads through the
                                      BFF-Managed Field Writers profile (4/0/4) and Standing Grant Administrators (4/4/4).
                                      The BFF proves its read before it trusts an EMPTY value (SubjectStandingGrantReader,
                                      SecuredColumnReadProof): an unproven empty level confers nothing.
      sprk_externalaccessremovedon    every user (Readers 4/0/0); the BFF reads and writes through Writers (4/4/4).
      sprk_externalinvitedemailhash   the BFF only (Writers 4/4/4; no reader profile).
      sprk_externalinvitedtenantid    the BFF only (Writers 4/4/4; no reader profile).
      sprk_identityplane and the five sprk_identitycollision* columns
                                      every user (Identity Link Readers 4/0/0); the BFF through Identity Link Writers
                                      (4/4/4). Provisioning's H7b S18 verifies this lock (SecureRecordSetupProcedure
                                      .IdentityLinkColumns).

    DEPLOY ORDER. Run -Apply, then -Verify (exit 0), in each environment BEFORE deploying a BFF that writes the new
    columns. Customer stamps receive the columns, the field security and the profile permissions through the SpaarkeMaster
    import (H6) after a SpaarkeMaster version bump and release export.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids whose Dataverse application users must be members of both writer profiles — the BFF managed
    identity and, while it is still an application user, the BFF app registration. Required for -Apply and -Verify.

.PARAMETER SolutionUniqueNames
    The unmanaged solutions that must carry the columns and profiles. Default SpaarkeCore, SpaarkeMaster.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-ExternalAccessContactSecuritySchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com `
        -BffApplicationIds <bff-uami-client-id>[,<bff-app-registration-id>]
    Dry run: every step's present / missing state; zero writes.

.EXAMPLE
    .\Set-ExternalAccessContactSecuritySchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com `
        -BffApplicationIds <bff-uami-client-id>[,<bff-app-registration-id>] -Apply

.EXAMPLE
    .\Set-ExternalAccessContactSecuritySchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com `
        -BffApplicationIds <bff-uami-client-id>[,<bff-app-registration-id>] -Verify

.NOTES
    The column names are the BFF's own constants (ContactBindingDecision.ExternalInvitedEmailHashColumn,
    ExternalInvitedTenantIdColumn, ExternalAccessRemovedOnColumn; SubjectStandingGrantReader.BaselineAttribute;
    ContactIdentityStore's flag columns). ExternalAccessContactSecuritySchemaAgreementTests pins $Grants and $NewColumns
    against those constants, the SpaarkeMaster Entity.xml (IsSecured) and FieldSecurityProfiles.xml. Run it inside
    PowerShell (`& ./scripts/Set-ExternalAccessContactSecuritySchema.ps1 ...`): `pwsh -File` passes the comma list as ONE
    string. Auth: the operator's own az CLI identity (System Administrator in the environment). No secrets.
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
    throw '-BffApplicationIds is required for -Apply and -Verify: both writer profiles must name the BFF application user(s) explicitly.'
}
# A single comma-joined string (pwsh -File) is split, never sent as one id.
$BffApplicationIds = @($BffApplicationIds | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$SolutionUniqueNames = @($SolutionUniqueNames | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants ─────────────────────────────────────────────────────────────────────────────────────────────────
$Table = 'contact'
$ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
$WriterProfileName = 'Spaarke BFF-Managed Field Writers'
$LinkReaderProfileName = 'Spaarke Identity Link Readers'
$LinkWriterProfileName = 'Spaarke Identity Link Writers'
$StandingGrantProfileName = 'Standing Grant Administrators'
$SystemAdministratorProfileName = 'System Administrator'
# (h) The standing grant: provisioning's S19 owns its lock; -Verify reads only who of the BFF may update it.
$StandingGrantColumn = 'sprk_standinggrant'
$Secured = $true

# The columns this script creates. Existing columns in $Grants are only secured and granted.
$NewColumns = @(
    @{ Column = 'sprk_externalinvitedemailhash'; SchemaName = 'sprk_ExternalInvitedEmailHash'; AttributeType = 'String'; MaxLength = 100; AuditEnabled = $false; AdvancedFind = $false; DisplayName = 'External invited email hash'; Description = 'Salted SHA-256 hash of the email address the latest external portal invite of this contact was sent to (versioned, salt and hash stored together; never the address). Read and written by the BFF only (field-secured; no reader profile). Never audited.' }
    @{ Column = 'sprk_externalinvitedtenantid'; SchemaName = 'sprk_ExternalInvitedTenantId'; AttributeType = 'String'; MaxLength = 36; AuditEnabled = $true; AdvancedFind = $true; DisplayName = 'External invited tenant id'; Description = 'The Entra tenant id (GUID) the latest external portal invite named for this contact, when it named one. Read and written by the BFF only (field-secured; no reader profile).' }
    @{ Column = 'sprk_externalaccessremovedon'; SchemaName = 'sprk_ExternalAccessRemovedOn'; AttributeType = 'DateTime'; MaxLength = 0; AuditEnabled = $true; AdvancedFind = $true; DisplayName = 'External access removed on'; Description = 'When an administrator removed all external access for this person (UTC). Set and cleared by the BFF only (field-secured; every user may read it, only the BFF writes it). While it is set the person has no external access, whatever the status of the contact.' }
)

# The lock: one row per profile x column (read / create / update; 4 = allowed, 0 = not). Every column here is secured;
# no other profile (System Administrator excepted) may read, create or update it.
$Grants = @(
    @{ Column = 'sprk_accesspermissiongrant'; Profile = $StandingGrantProfileName; Read = 4; Create = 4; Update = 4 }
    @{ Column = 'sprk_accesspermissiongrant'; Profile = $ReaderProfileName; Read = 4; Create = 0; Update = 0 }
    @{ Column = 'sprk_accesspermissiongrant'; Profile = $WriterProfileName; Read = 4; Create = 0; Update = 4 }
    @{ Column = 'sprk_identityplane'; Profile = $LinkReaderProfileName; Read = 4; Create = 0; Update = 0 }
    @{ Column = 'sprk_identityplane'; Profile = $LinkWriterProfileName; Read = 4; Create = 4; Update = 4 }
    @{ Column = 'sprk_identitycollisionon'; Profile = $LinkReaderProfileName; Read = 4; Create = 0; Update = 0 }
    @{ Column = 'sprk_identitycollisionon'; Profile = $LinkWriterProfileName; Read = 4; Create = 4; Update = 4 }
    @{ Column = 'sprk_identitycollisionoid'; Profile = $LinkReaderProfileName; Read = 4; Create = 0; Update = 0 }
    @{ Column = 'sprk_identitycollisionoid'; Profile = $LinkWriterProfileName; Read = 4; Create = 4; Update = 4 }
    @{ Column = 'sprk_identitycollisionplane'; Profile = $LinkReaderProfileName; Read = 4; Create = 0; Update = 0 }
    @{ Column = 'sprk_identitycollisionplane'; Profile = $LinkWriterProfileName; Read = 4; Create = 4; Update = 4 }
    @{ Column = 'sprk_identitycollisionreason'; Profile = $LinkReaderProfileName; Read = 4; Create = 0; Update = 0 }
    @{ Column = 'sprk_identitycollisionreason'; Profile = $LinkWriterProfileName; Read = 4; Create = 4; Update = 4 }
    @{ Column = 'sprk_identitycollisionparties'; Profile = $LinkReaderProfileName; Read = 4; Create = 0; Update = 0 }
    @{ Column = 'sprk_identitycollisionparties'; Profile = $LinkWriterProfileName; Read = 4; Create = 4; Update = 4 }
    @{ Column = 'sprk_externalinvitedemailhash'; Profile = $WriterProfileName; Read = 4; Create = 4; Update = 4 }
    @{ Column = 'sprk_externalinvitedtenantid'; Profile = $WriterProfileName; Read = 4; Create = 4; Update = 4 }
    @{ Column = 'sprk_externalaccessremovedon'; Profile = $ReaderProfileName; Read = 4; Create = 0; Update = 0 }
    @{ Column = 'sprk_externalaccessremovedon'; Profile = $WriterProfileName; Read = 4; Create = 4; Update = 4 }
)
$Columns = @($Grants | ForEach-Object { $_.Column } | Select-Object -Unique)
$ProfileNames = @($Grants | ForEach-Object { $_.Profile } | Select-Object -Unique)
# Writer profiles whose ONLY members may be the BFF application user(s).
$BffOnlyProfiles = @($WriterProfileName, $LinkWriterProfileName)

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
function Get-AttrPath([string]$Column) {
    "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')?`$select=LogicalName,AttributeType,IsSecured,MetadataId"
}

$IsDryRun = -not $Apply.IsPresent
. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')
. (Join-Path $PSScriptRoot 'common/StandingGrantWriterInvariant.ps1')
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
Write-Host "Columns     : $($Columns | ForEach-Object { "$Table.$_" } | Join-String -Separator ', ')"

# ── (a) NEW COLUMNS (created secured) ─────────────────────────────────────────────────────────────────────────
Write-Host "`n(a) New columns"
$attrs = @{}
foreach ($spec in $NewColumns) {
    $c = $spec.Column
    $typePath = if ($spec.AttributeType -eq 'DateTime') {
        "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$c')/Microsoft.Dynamics.CRM.DateTimeAttributeMetadata?`$select=LogicalName,AttributeType,Format,DateTimeBehavior,IsSecured,IsAuditEnabled,MetadataId"
    } else {
        "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$c')/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=LogicalName,AttributeType,FormatName,MaxLength,IsSecured,IsAuditEnabled,MetadataId"
    }
    $what = if ($spec.AttributeType -eq 'DateTime') { 'DateTime, DateAndTime, UserLocal' } else { "String, Text, max $($spec.MaxLength)" }
    $attr = Try-DvGet $typePath
    if ($attr) {
        $shapeOk = if ($spec.AttributeType -eq 'DateTime') {
            $attr.AttributeType -eq 'DateTime' -and $attr.Format -eq 'DateAndTime' -and $attr.DateTimeBehavior.Value -eq 'UserLocal'
        } else {
            $attr.AttributeType -eq 'String' -and $attr.FormatName.Value -eq 'Text' -and $attr.MaxLength -eq $spec.MaxLength
        }
        if ($shapeOk) { Report 'OK' "$Table.$c ($what)" } else { Report 'FAIL' "$Table.$c exists with another type or length — expected $what" }
        if ($attr.IsAuditEnabled.Value -ne $spec.AuditEnabled) { Report 'FAIL' "$Table.$c auditing is $($attr.IsAuditEnabled.Value); expected $($spec.AuditEnabled)" }
        $attrs[$c] = $attr
        continue
    }
    if ($Verify) { Report 'MISSING' "$Table.$c"; continue }
    if ($IsDryRun) { Report 'WOULD' "create $Table.$c ($what, field-secured at creation, auditing $(if ($spec.AuditEnabled) { 'on' } else { 'off' }))"; continue }

    $body = @{
        SchemaName             = $spec.SchemaName
        DisplayName            = (New-Label $spec.DisplayName)
        Description            = (New-Label $spec.Description)
        RequiredLevel          = @{ Value = 'None' }
        IsSecured              = $Secured
        IsAuditEnabled         = @{ Value = $spec.AuditEnabled; CanBeChanged = $true; ManagedPropertyLogicalName = 'canmodifyauditsettings' }
        IsValidForAdvancedFind = @{ Value = $spec.AdvancedFind; CanBeChanged = $true; ManagedPropertyLogicalName = 'canmodifysearchsettings' }
    }
    if ($spec.AttributeType -eq 'DateTime') {
        $body['@odata.type'] = 'Microsoft.Dynamics.CRM.DateTimeAttributeMetadata'
        $body['Format'] = 'DateAndTime'
        $body['DateTimeBehavior'] = @{ Value = 'UserLocal' }
    } else {
        $body['@odata.type'] = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'
        $body['MaxLength'] = $spec.MaxLength
        $body['FormatName'] = @{ Value = 'Text' }
    }
    Invoke-DvWrite POST "EntityDefinitions(LogicalName='$Table')/Attributes" $body @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueNames[0] } | Out-Null
    Report 'DONE' "created $Table.$c (field-secured)"
    $attrs[$c] = Wait-DvRead $typePath "$Table.$c metadata"
    Wait-DvRead "contacts?`$select=$c&`$top=1" "$Table.$c in data queries" | Out-Null
}
foreach ($c in $Columns | Where-Object { -not $attrs.ContainsKey($_) -and $_ -notin @($NewColumns | ForEach-Object { $_.Column }) }) {
    $attr = Try-DvGet (Get-AttrPath $c)
    if ($attr) { $attrs[$c] = $attr } else { Report 'FAIL' "$Table.$c does not exist — scripts/Set-ContactIdentityBindingSchema.ps1 (plane and collision columns) or the SpaarkeMaster import creates it" }
}

# ── (b) PROFILES (existing only) + MEMBERS (reported) ───────────────────────────────────────────────────────
Write-Host "`n(b) Field security profiles (existing; never created here) and their members (reported, never written)"
$profileIds = @{}
foreach ($name in $ProfileNames) {
    $p = @((Invoke-DvGet "fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$name'").value)
    if ($p.Count -eq 1) { Report 'OK' "profile '$name'"; $profileIds[$name] = $p[0].fieldsecurityprofileid }
    elseif ($p.Count -gt 1) { Report 'FAIL' "$($p.Count) profiles are named '$name' — exactly one is expected" }
    else { Report 'FAIL' "profile '$name' does not exist — SpaarkeMaster ships it and provisioning's H7b populates it; this script never creates one" }
}

$bffUsers = @()
foreach ($appId in $BffApplicationIds) {
    $u = @((Invoke-DvGet "systemusers?`$select=systemuserid,fullname&`$filter=applicationid eq $appId").value) | Select-Object -First 1
    if (-not $u) { Report 'FAIL' "no application user for appId $appId in this environment"; continue }
    $bffUsers += $u
}
$allowed = @($bffUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })

# A writer profile's membership IS the lock: the BFF application user(s) must be members, and nobody else may be.
foreach ($name in $BffOnlyProfiles) {
    $id = $profileIds[$name]
    if (-not $id) { continue }
    $users = @((Invoke-DvGet "fieldsecurityprofiles($id)/systemuserprofiles_association?`$select=systemuserid,fullname,applicationid").value)
    $memberIds = @($users | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    foreach ($u in $bffUsers) {
        if ($u.systemuserid.ToString().ToLowerInvariant() -in $memberIds) { Report 'OK' "$name member: app user '$($u.fullname)'" }
        else { Report 'FAIL' "$name does not have app user '$($u.fullname)' — the BFF could not read or write its columns (H7b populates the profile)" }
    }
    $strangers = @($users | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowed })
    foreach ($m in $strangers) {
        $kind = if ($m.applicationid) { "application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a human user' }
        Report 'FAIL' "$name member '$($m.fullname)' ($($m.systemuserid)) is $kind — only the BFF may write its columns"
    }
    $teams = @((Invoke-DvGet "fieldsecurityprofiles($id)/teamprofiles_association?`$select=teamid,name").value)
    foreach ($tm in $teams) { Report 'FAIL' "$name has team '$($tm.name)' ($($tm.teamid)) as a member — only the BFF may write its columns" }
    if ($strangers.Count -eq 0 -and $teams.Count -eq 0) { Report 'OK' "$name has no member besides the BFF application user(s)" }
}
foreach ($name in @($ReaderProfileName, $LinkReaderProfileName)) {
    $id = $profileIds[$name]
    if (-not $id) { continue }
    $teams = @((Invoke-DvGet "fieldsecurityprofiles($id)/teamprofiles_association?`$select=teamid").value)
    Report 'INFO' "$name has $($teams.Count) team member(s) (H7b associates every business unit's default team)"
}
if ($profileIds[$StandingGrantProfileName]) {
    $id = $profileIds[$StandingGrantProfileName]
    $users = @((Invoke-DvGet "fieldsecurityprofiles($id)/systemuserprofiles_association?`$select=systemuserid").value)
    $teams = @((Invoke-DvGet "fieldsecurityprofiles($id)/teamprofiles_association?`$select=teamid").value)
    Report 'INFO' "$StandingGrantProfileName has $($users.Count) user and $($teams.Count) team member(s) (an operator decision; never changed here)"
}

# ── (c) FIELD-LEVEL SECURITY + (d) GRANTS, per column ───────────────────────────────────────────────────────
Write-Host "`n(c) Field-level security and (d) grants"
foreach ($c in $Columns) {
    $attr = $attrs[$c]
    $rows = @($Grants | Where-Object { $_.Column -eq $c })
    if (-not $attr) {
        Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "secure $Table.$c and grant $($rows.Count) profile(s) (after the column exists)"
        continue
    }

    if ($attr.IsSecured -eq $Secured) { Report 'OK' "$Table.$c is field-secured" }
    elseif ($Verify) { Report 'MISSING' "$Table.$c is NOT field-secured" }
    elseif ($IsDryRun) { Report 'WOULD' "secure $Table.$c" }
    else {
        $typed = Invoke-DvGet "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$c')"
        $typed.IsSecured = $Secured
        Invoke-DvWrite PUT "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$c')" $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
        Report 'DONE' "secured $Table.$c"
    }

    foreach ($g in $rows) {
        $id = $profileIds[$g.Profile]
        if (-not $id) { Report 'FAIL' "$($g.Profile) permission on $Table.$c (the profile does not exist)"; continue }
        $perm = @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $id and entityname eq '$Table' and attributelogicalname eq '$c'").value) | Select-Object -First 1
        $expected = "read=$($g.Read) create=$($g.Create) update=$($g.Update)"
        if ($perm) {
            if ($perm.canread -eq $g.Read -and $perm.cancreate -eq $g.Create -and $perm.canupdate -eq $g.Update) { Report 'OK' "$($g.Profile) on $Table.$c ($expected)" }
            else { Report 'FAIL' "$($g.Profile) on $Table.$c is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected $expected" }
            continue
        }
        if ($Verify) { Report 'MISSING' "$($g.Profile) on $Table.$c ($expected)"; continue }
        if ($IsDryRun) { Report 'WOULD' "grant $($g.Profile) on $Table.$c ($expected)"; continue }
        # Securing a column propagates asynchronously: a grant right after it may be refused 0x8004f508 "... is NOT
        # secured for entity fieldpermission". Retry that one error; anything else throws.
        for ($attempt = 1; ; $attempt++) {
            try {
                Invoke-DvWrite POST 'fieldpermissions' @{
                    entityname = $Table; attributelogicalname = $c
                    canread = $g.Read; cancreate = $g.Create; canupdate = $g.Update
                    'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($id)"
                } | Out-Null
                break
            } catch {
                $notYetSecured = "$($_.ErrorDetails.Message) $($_.Exception.Message)" -match '0x8004f508'
                if (-not $notYetSecured -or $attempt -ge 12) { throw }
                Write-Host "    $Table.$c not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
                Start-Sleep -Seconds 10
            }
        }
        Report 'DONE' "granted $($g.Profile) on $Table.$c ($expected)"
    }

    # No other profile may read, create or update the column (System Administrator is the platform's own profile).
    $listed = @($rows | ForEach-Object { $_.Profile }) + $SystemAdministratorProfileName
    $others = @((Invoke-DvGet "fieldpermissions?`$select=canread,cancreate,canupdate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$Table' and attributelogicalname eq '$c' and (canread eq 4 or canupdate eq 4 or cancreate eq 4)").value)
    foreach ($o in $others | Where-Object { $_.fieldsecurityprofileid.name -notin $listed }) {
        Report 'FAIL' "profile '$($o.fieldsecurityprofileid.name)' also has access to $Table.$c (read=$($o.canread) create=$($o.cancreate) update=$($o.canupdate)) — only the listed profiles may"
    }
}

# ── (e) SOLUTIONS ───────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(e) Solution components"
$tableId = (Invoke-DvGet "EntityDefinitions(LogicalName='$Table')?`$select=MetadataId").MetadataId
foreach ($solutionName in $SolutionUniqueNames) {
    $solution = @((Invoke-DvGet "solutions?`$select=solutionid,uniquename&`$filter=uniquename eq '$solutionName'").value) | Select-Object -First 1
    if (-not $solution) { Report 'FAIL' "solution '$solutionName' not found"; continue }

    # Paged, and a table in the solution with rootcomponentbehavior = 0 ("include subcomponents") carries its columns
    # implicitly (scripts/common/DataverseSolutionMembership.ps1).
    $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid

    $components = [System.Collections.Generic.List[object]]::new()
    foreach ($c in $Columns) {
        if ($attrs[$c]) { $components.Add(@{ Id = $attrs[$c].MetadataId; Type = 2; Label = "$Table.$c"; TableId = $tableId }) }
        else { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "add $Table.$c to $solutionName (after the column exists)" }
    }
    foreach ($name in $ProfileNames) {
        if ($profileIds[$name]) { $components.Add(@{ Id = $profileIds[$name]; Type = 70; Label = "field security profile '$name'" }) }
    }

    foreach ($comp in $components) {
        $how = Test-DvInSolution -Membership $membership -ComponentId $comp.Id -TableMetadataId $comp['TableId']
        if ($how -eq 'Direct') { Report 'OK' "$($comp.Label) in $solutionName"; continue }
        if ($how -eq 'ViaTable') { Report 'OK' "$($comp.Label) in $solutionName (its table includes subcomponents)"; continue }
        if ($Verify) { Report 'MISSING' "$($comp.Label) in $solutionName"; continue }
        if ($IsDryRun) { Report 'WOULD' "add $($comp.Label) to $solutionName"; continue }
        Invoke-DvWrite POST 'AddSolutionComponent' @{ ComponentId = $comp.Id; ComponentType = $comp.Type; SolutionUniqueName = $solutionName; AddRequiredComponents = $false } | Out-Null
        Report 'DONE' "added $($comp.Label) to $solutionName"
    }
}

# ── (g) PROOF READS (verify only) ───────────────────────────────────────────────────────────────────────────
# The BFF's readability proof (SecuredColumnReadProof) reads these platform tables as the BFF application user. Each of its
# queries is run here AS that user (MSCRMCallerID), read-only; a refused read is FAIL.
$ProofTables = @('systemuser', 'role', 'systemuserroles', 'fieldpermission', 'systemuserprofiles', 'teamprofiles', 'teammembership')
function Invoke-DvGetAs([string]$Path, [string]$SystemUserId) {
    $h = $headers.Clone(); $h['MSCRMCallerID'] = $SystemUserId
    Invoke-RestMethod -Uri "$Api/$Path" -Headers $h -Method Get
}
if ($Verify) {
    Write-Host "`n(g) Proof reads, as each BFF application user ($($ProofTables -join ', '))"
    foreach ($u in $bffUsers) {
        $sid = $u.systemuserid.ToString().ToLowerInvariant()
        $member = "<filter><condition attribute=`"systemuserid`" operator=`"eq`" value=`"$sid`" /></filter>"
        $fieldPermission = "<fetch top=`"1`"><entity name=`"fieldpermission`"><attribute name=`"attributelogicalname`" />" +
            "<filter><condition attribute=`"entityname`" operator=`"eq`" value=`"$Table`" /><condition attribute=`"canread`" operator=`"eq`" value=`"4`" /></filter>"
        $proofReads = @(
            @{ What = 'systemuser (WhoAmI)'; Path = 'WhoAmI' }
            @{ What = 'role + systemuserroles'; Path = 'roles?fetchXml=' + [uri]::EscapeDataString(
                "<fetch top=`"1`"><entity name=`"role`"><attribute name=`"roleid`" /><filter><condition attribute=`"roletemplateid`" operator=`"eq`" value=`"627090ff-40a3-4053-8790-584edc5be201`" /></filter>" +
                "<link-entity name=`"systemuserroles`" from=`"roleid`" to=`"roleid`" link-type=`"inner`">$member</link-entity></entity></fetch>") }
            @{ What = 'fieldpermission + systemuserprofiles'; Path = 'fieldpermissions?fetchXml=' + [uri]::EscapeDataString(
                $fieldPermission + "<link-entity name=`"systemuserprofiles`" from=`"fieldsecurityprofileid`" to=`"fieldsecurityprofileid`" link-type=`"inner`">$member</link-entity></entity></fetch>") }
            @{ What = 'fieldpermission + teamprofiles + teammembership'; Path = 'fieldpermissions?fetchXml=' + [uri]::EscapeDataString(
                $fieldPermission + "<link-entity name=`"teamprofiles`" from=`"fieldsecurityprofileid`" to=`"fieldsecurityprofileid`" link-type=`"inner`">" +
                "<link-entity name=`"teammembership`" from=`"teamid`" to=`"teamid`" link-type=`"inner`">$member</link-entity></link-entity></entity></fetch>") }
        )
        foreach ($r in $proofReads) {
            try {
                $answer = Invoke-DvGetAs $r.Path $sid
                if ($r.Path -eq 'WhoAmI' -and "$($answer.UserId)".ToLowerInvariant() -ne $sid) {
                    Report 'FAIL' "$($u.fullname): WhoAmI as this user answered $($answer.UserId), not $sid — the reads below were not made as it"
                    continue
                }
                Report 'OK' "$($u.fullname) reads $($r.What)"
            }
            catch {
                Report 'FAIL' "$($u.fullname) cannot read $($r.What) ($($_.Exception.Message)) — every empty read of a secured column it must prove would be refused. Grant its security role Read on these tables (or run -Verify as a user who may act on behalf of another user)."
            }
        }
    }
}

# ── (h) STANDING GRANT WRITER INVARIANT (verify only, read-only) ────────────────────────────────────────────
if ($Verify) {
    Write-Host "`n(h) Standing grant: each BFF application user updates $Table.$StandingGrantColumn only through its direct '$StandingGrantProfileName' membership"
    $sgaId = $profileIds[$StandingGrantProfileName]
    if (-not $sgaId) { Report 'FAIL' "the invariant cannot be checked: profile '$StandingGrantProfileName' was not found (see (b))" }
    else {
        foreach ($u in $bffUsers) {
            $queries = Get-StandingGrantWriterQuery -SystemUserId $u.systemuserid -Table $Table -Column $StandingGrantColumn
            try {
                $findings = Get-StandingGrantWriterFinding -UserName "app user '$($u.fullname)'" `
                    -StandingGrantProfileId $sgaId -StandingGrantProfileName $StandingGrantProfileName `
                    -SystemAdministratorProfileName $SystemAdministratorProfileName -Column "$Table.$StandingGrantColumn" `
                    -DirectProfiles @((Invoke-DvGet $queries.DirectProfiles).value) `
                    -TeamProfiles @((Invoke-DvGet $queries.TeamProfiles).value) `
                    -WriterPermissions @((Invoke-DvGet $queries.Writers).value)
                foreach ($f in $findings) { Report $f.State $f.What }
            }
            catch {
                Report 'FAIL' "app user '$($u.fullname)': the standing-grant writer check could not be read ($($_.Exception.Message)); it is not assumed to pass"
            }
        }
    }
}

# ── (f) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($Apply) {
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
    Write-Host "`nPublished $Table." -ForegroundColor Green
    Write-Host 'Next: -Verify (must exit 0), then a SpaarkeMaster version bump and release export, so that stamps receive the columns and their profile permissions.' -ForegroundColor Cyan
}

if ($Verify) {
    if ($gaps.Count -eq 0) { Write-Host "`nVERIFY PASS: every column is in place, field-secured, granted exactly as listed, in every listed solution, only the listed profiles can access it, every BFF application user reads the tables its readability proof reads, and updates the standing grant only through its direct Standing Grant Administrators membership." -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
if ($IsDryRun) { Write-Host "`nDRY RUN complete — nothing was written. Re-run with -Apply." -ForegroundColor Cyan }
