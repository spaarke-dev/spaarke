#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Adds the invite-code hash — contact.sprk_externalinvitecodehash, "External invite code hash", a single line of text
    (max 100) — and locks it with field-level security so ONLY the BFF can read or write it (unified-access-control-r2
    task 185; owner round 98, 2026-10-13). Dry run by default; -Apply writes; -Verify checks. Idempotent. A copy of
    scripts/Set-ExternalInviteMarkerSchema.ps1's structure (task 184).

.DESCRIPTION
    WHY. Task 184's pending-invite marker (contact.sprk_externalinvitedon) lets the CIAM plane's first-login email bind
    (E11) bind a contact THIS environment invited. A CIAM account carries no proof that its holder owns the email, so the
    marker alone binds whichever CIAM user signs in with that email first (the #1801 review's F1, account squatting).
    Task 185 adds the proof: the invite email carries a single-use 256-bit code, the contact stores only its salted
    SHA-256 hash (owner round 98, Q1/Q7), and E11's marker leg binds only when the sign-in presents the code whose hash
    matches. The bind clears the hash in the same write as the oid; the marker is never cleared (184). A re-invite
    replaces both in one PATCH.

    Field security is the point, and it is STRICTER than the marker's:
      - nobody but the BFF may write the hash (a user who could write it could set a code of their own and bind any
        invited contact to any CIAM account);
      - nobody but the BFF may READ it either. The hash is salted and the code has 256 bits, but the reader profile is
        held by every business unit's default team (H7b), and the BFF is the only party that ever compares it. So the
        reader profile gets NO read, create or update on this column (unlike the marker, where it reads).
    Auditing is OFF: an audit row would keep the old hashes after the bind clears them. Advanced Find is off too.

    The script provisions, in order:
      (0) MARKER     contact.sprk_externalinvitedon (task 184) must already exist — the code means nothing without it.
                     Missing = FAIL in every mode, and -Apply writes nothing (run Set-ExternalInviteMarkerSchema.ps1 first).
      (a) COLUMN     contact.sprk_externalinvitecodehash (schema sprk_ExternalInviteCodeHash): String, Format Text,
                     MaxLength 100 (the stored value is 69 characters; the room is for a v2), RequiredLevel None,
                     IsAuditEnabled false, IsValidForAdvancedFind false.
      (b) PROFILES   The EXISTING "Spaarke BFF-Managed Field Readers" and "Spaarke BFF-Managed Field Writers" profiles of
                     tasks 133 / 146 / 184. NEVER created here: provisioning's H7b creates and populates them on a new
                     stamp. A missing profile is FAIL in every mode. Membership is REPORTED, never written: every
                     -BffApplicationIds application user must be a writer member, and any OTHER writer member — a human,
                     another application user, any team — is FAIL (the writer profile's membership IS the lock).
      (c) FLS        The column becomes field-secured. The writer profile gets read=4 create=4 update=4. The reader profile
                     gets NOTHING: -Apply never writes a reader permission row, and a reader row with ANY read, create or
                     update is FAIL. Any OTHER profile that can read, create or update the column is FAIL; the platform's
                     System Administrator profile is reported, not failed.
      (d) SOLUTION   The column is a component of EVERY -SolutionUniqueNames solution (default SpaarkeCore AND SpaarkeMaster).
                     Every environment, customer stamps included, imports only the CI-built SpaarkeMaster, and contact sits
                     in SpaarkeMaster with rootcomponentbehavior = 1 (no subcomponents) — so the column travels only when it
                     is added explicitly. A column counts as included when added directly, or when its table is in the
                     solution with rootcomponentbehavior = 0. Both profiles must be in every listed solution too (their
                     field-permission rows travel with them); a profile missing from one is added by -Apply.
      (e) PUBLISH    contact.

    ⚠️ DEPLOY ORDER. A BFF carrying task 185 WRITES this column on every invite and READS it on every marker-leg bind.
      - DEVELOPMENT: run this script (-Apply, then -Verify exit 0) BEFORE deploying that BFF.
      - CUSTOMER STAMPS get the column only through the SpaarkeMaster import, which scripts/Deploy-Release.ps1 runs in
        Phase 3, after the BFF (Phase 2).

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
    .\Set-ExternalInviteCodeHashSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c
    Dry run: every step's present / missing state; zero writes.

.EXAMPLE
    .\Set-ExternalInviteCodeHashSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Apply

.EXAMPLE
    .\Set-ExternalInviteCodeHashSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify

.NOTES
    unified-access-control-r2 task 185 (design: projects/unified-access-control-r2/notes/185-invite-code-design.md §3).
    The column name, table, type, length, auditing and field security are pinned against this script by
    ExternalInviteCodeHashSchemaAgreementTests. Run it inside PowerShell
    (`& ./scripts/Set-ExternalInviteCodeHashSchema.ps1 ...`): `pwsh -File` passes the comma list as ONE string. Auth: the
    operator's own az CLI identity (System Administrator in the environment). No secrets.
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

# ── Constants (the BFF uses these exact names; pinned by ExternalInviteCodeHashSchemaAgreementTests) ─────────────
$Table = 'contact'
$Column = 'sprk_externalinvitecodehash'
$ColumnSchemaName = 'sprk_ExternalInviteCodeHash'
$DisplayName = 'External invite code hash'
$AttributeType = 'String'
$Format = 'Text'
$MaxLength = 100
$AuditEnabled = $false
$MarkerColumn = 'sprk_externalinvitedon'
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

# ── (0) MARKER (task 184) ───────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(0) $Table.$MarkerColumn (task 184's pending-invite marker)"
$marker = Try-DvGet "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$MarkerColumn')?`$select=LogicalName,IsSecured"
if ($marker) { Report 'OK' "$Table.$MarkerColumn exists (field-secured: $($marker.IsSecured))" }
else {
    Report 'FAIL' "$Table.$MarkerColumn does not exist — run scripts/Set-ExternalInviteMarkerSchema.ps1 -Apply first; the code hash means nothing without the marker"
    if ($Apply) { throw "Refusing to -Apply: $Table.$MarkerColumn (task 184) is missing. Nothing was written." }
}

# ── (a) COLUMN ──────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(a) $Table.$Column"
$attrPath = "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.StringAttributeMetadata?`$select=LogicalName,AttributeType,FormatName,MaxLength,IsSecured,IsAuditEnabled,IsValidForAdvancedFind,MetadataId"
$attr = Try-DvGet $attrPath
if ($attr) {
    if ($attr.AttributeType -ne $AttributeType -or $attr.FormatName.Value -ne $Format -or $attr.MaxLength -ne $MaxLength) {
        Report 'FAIL' "$Table.$Column exists as $($attr.AttributeType) / $($attr.FormatName.Value) / max $($attr.MaxLength) — expected $AttributeType / $Format / max $MaxLength"
    } else { Report 'OK' "$Table.$Column ($AttributeType, $Format, max $MaxLength)" }
} elseif ($Verify) { Report 'MISSING' "$Table.$Column" }
elseif ($IsDryRun) { Report 'WOULD' "create $Table.$Column ($AttributeType, $Format, max $MaxLength, auditing off)" }
else {
    Invoke-DvWrite POST "EntityDefinitions(LogicalName='$Table')/Attributes" @{
        '@odata.type'          = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'
        SchemaName             = $ColumnSchemaName
        DisplayName            = (New-Label $DisplayName)
        Description            = (New-Label 'Salted SHA-256 hash of the single-use code in this contact''s latest external portal invite. Read and written by the BFF only (field-secured; the BFF-Managed reader profile has no access). Written with sprk_externalinvitedon by every invite, which replaces any earlier code; cleared by the first CIAM sign-in that presents the code and binds. Never audited. unified-access-control-r2 task 185, owner round 98.')
        RequiredLevel          = @{ Value = 'None' }
        MaxLength              = $MaxLength
        FormatName             = @{ Value = $Format }
        IsAuditEnabled         = @{ Value = $AuditEnabled; CanBeChanged = $true; ManagedPropertyLogicalName = 'canmodifyauditsettings' }
        IsValidForAdvancedFind = @{ Value = $false; CanBeChanged = $true; ManagedPropertyLogicalName = 'canmodifysearchsettings' }
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

# The writer profile's membership IS the lock: a human — or a team — in it could read the hash or set a code of their
# own, and E11 would then bind the contact to whoever presents that code. So the BFF must be a member and nobody else
# may be, in every mode.
if ($writerId) {
    $writerUsers = @((Invoke-DvGet "fieldsecurityprofiles($writerId)/systemuserprofiles_association?`$select=systemuserid,fullname,applicationid").value)
    $memberIds = @($writerUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    foreach ($u in $bffUsers) {
        if ($u.systemuserid.ToString().ToLowerInvariant() -in $memberIds) { Report 'OK' "$WriterProfileName member: app user '$($u.fullname)'" }
        else { Report 'FAIL' "$WriterProfileName does not have app user '$($u.fullname)' — the BFF could not read or write $Column (H7b populates the profile)" }
    }
    $allowedWriters = @($bffUsers | ForEach-Object { $_.systemuserid.ToString().ToLowerInvariant() })
    $strangers = @($writerUsers | Where-Object { $_.systemuserid.ToString().ToLowerInvariant() -notin $allowedWriters })
    foreach ($m in $strangers) {
        $kind = if ($m.applicationid) { "application user (appId $($m.applicationid)) not in -BffApplicationIds" } else { 'a HUMAN user' }
        Report 'FAIL' "$WriterProfileName member '$($m.fullname)' ($($m.systemuserid)) is $kind — only the BFF may read or write $Column"
    }
    $writerTeams = @((Invoke-DvGet "fieldsecurityprofiles($writerId)/teamprofiles_association?`$select=teamid,name").value)
    foreach ($tm in $writerTeams) {
        Report 'FAIL' "$WriterProfileName has team '$($tm.name)' ($($tm.teamid)) as a member — every member of it could read or write $Column"
    }
    if ($strangers.Count -eq 0 -and $writerTeams.Count -eq 0) { Report 'OK' "$WriterProfileName has no member besides the BFF application user(s)" }
}
if ($readerId) {
    $readerTeams = @((Invoke-DvGet "fieldsecurityprofiles($readerId)/teamprofiles_association?`$select=teamid").value)
    Report 'INFO' "$ReaderProfileName has $($readerTeams.Count) team member(s) (H7b associates every business unit's default team) — which is why it gets no access to $Column"
}

# ── (c) FIELD-LEVEL SECURITY + AUDITING ─────────────────────────────────────────────────────────────────────
Write-Host "`n(c) Field-level security and auditing"
if (-not $attr) { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "secure $Table.$Column, turn its auditing off and grant the writer profile (after the column exists)" }
else {
    $needsPut = $false
    if ($attr.IsSecured -eq $Secured) { Report 'OK' "$Table.$Column is field-secured" }
    elseif ($Verify) { Report 'MISSING' "$Table.$Column is NOT field-secured — any user with contact Read could read the hash and any user with contact Write could set a code" }
    elseif ($IsDryRun) { Report 'WOULD' "secure $Table.$Column" }
    else { $needsPut = $true }

    if ($attr.IsAuditEnabled.Value -eq $AuditEnabled) { Report 'OK' "$Table.$Column auditing is off" }
    elseif ($Verify) { Report 'FAIL' "$Table.$Column auditing is ON — audit rows would keep old hashes after the bind clears them" }
    elseif ($IsDryRun) { Report 'WOULD' "turn auditing off on $Table.$Column" }
    else { $needsPut = $true }

    if ($attr.IsValidForAdvancedFind.Value -eq $false) { Report 'OK' "$Table.$Column is not valid for Advanced Find" }
    elseif ($Verify) { Report 'FAIL' "$Table.$Column is valid for Advanced Find" }
    elseif ($IsDryRun) { Report 'WOULD' "turn Advanced Find off on $Table.$Column" }
    else { $needsPut = $true }

    if ($needsPut) {
        $typed = Invoke-DvGet "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')"
        $typed.IsSecured = $Secured
        $typed.IsAuditEnabled.Value = $AuditEnabled
        $typed.IsValidForAdvancedFind.Value = $false
        Invoke-DvWrite PUT "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')" $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
        Report 'DONE' "secured $Table.$Column, auditing off, Advanced Find off"
    }

    # The reader profile gets NOTHING on this column: Read = 0 as well (unlike the marker). A permission row that is
    # absent grants nothing; -Apply never writes one. A row with ANY access is FAIL in every mode, never "fixed" here —
    # someone granted it on purpose, and an operator decides.
    foreach ($spec in @(@{ Id = $readerId; Name = $ReaderProfileName; Read = 0; Create = 0; Update = 0 }, @{ Id = $writerId; Name = $WriterProfileName; Read = 4; Create = 4; Update = 4 })) {
        if (-not $spec.Id) { Report 'FAIL' "$($spec.Name) permission on $Table.$Column (the profile does not exist)"; continue }
        $grantsNothing = $spec.Read -eq 0 -and $spec.Create -eq 0 -and $spec.Update -eq 0
        $perm = @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $($spec.Id) and entityname eq '$Table' and attributelogicalname eq '$Column'").value) | Select-Object -First 1
        if ($perm) {
            if ($perm.canread -eq $spec.Read -and $perm.cancreate -eq $spec.Create -and $perm.canupdate -eq $spec.Update) { Report 'OK' "$($spec.Name) on $Table.$Column (read=$($spec.Read) create=$($spec.Create) update=$($spec.Update))" }
            else { Report 'FAIL' "$($spec.Name) on $Table.$Column is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=$($spec.Read) create=$($spec.Create) update=$($spec.Update)" }
            continue
        }
        if ($grantsNothing) { Report 'OK' "$($spec.Name) has no permission on $Table.$Column"; continue }
        if ($Verify) { Report 'MISSING' "$($spec.Name) on $Table.$Column"; continue }
        if ($IsDryRun) { Report 'WOULD' "grant $($spec.Name) on $Table.$Column (read=$($spec.Read) create=$($spec.Create) update=$($spec.Update))"; continue }
        # Securing a column propagates asynchronously: a grant right after it may be refused 0x8004f508 "... is NOT
        # secured for entity fieldpermission". Retry that one error; anything else throws.
        for ($attempt = 1; ; $attempt++) {
            try {
                Invoke-DvWrite POST 'fieldpermissions' @{
                    entityname = $Table; attributelogicalname = $Column
                    canread = $spec.Read; cancreate = $spec.Create; canupdate = $spec.Update
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

    # Any OTHER profile with read, create or update on the hash is FAIL; the platform's System Administrator profile
    # (full access by design) is reported.
    $holders = @((Invoke-DvGet "fieldpermissions?`$select=canread,canupdate,cancreate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$Table' and attributelogicalname eq '$Column' and (canread eq 4 or canupdate eq 4 or cancreate eq 4)").value)
    foreach ($w in $holders | Where-Object { $_.fieldsecurityprofileid.name -ne $WriterProfileName }) {
        $what = "profile '$($w.fieldsecurityprofileid.name)' has read=$($w.canread) create=$($w.cancreate) update=$($w.canupdate) on $Table.$Column"
        if ($w.fieldsecurityprofileid.name -eq 'System Administrator') { Report 'INFO' "$what (the platform default)" }
        else { Report 'FAIL' "$what — only the BFF may read or write it" }
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
    if ($gaps.Count -eq 0) { Write-Host "`nVERIFY PASS: $Table.$Column is in place, field-secured, unaudited, in every listed solution, and only the BFF can read or write it." -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
if ($IsDryRun) { Write-Host "`nDRY RUN complete — nothing was written. Re-run with -Apply." -ForegroundColor Cyan }
