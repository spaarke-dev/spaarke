#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Applies the identity-binding schema of unified-access-control-r2 task 141 to one Dataverse environment:
    the uniqueness mirror and its key, the plane + collision columns, the backfill, field-level security, and the
    solution components. Dry run by default; -Apply writes; -Verify checks. Idempotent.

.DESCRIPTION
    The BFF binds a person to a contact by Entra oid in contact.sprk_externalobjectid (ONE field for both the
    workforce and CIAM planes). Dataverse refuses an alternate key on a field-secured column, so — owner round 4
    item 4 (B2), 2026-10-01 — field-level security STAYS on sprk_externalobjectid and the platform's "exactly one
    contact per oid" lives on a separate, UNSECURED mirror column, contact.sprk_externalobjectidkey, which the BFF
    writes with the same oid in the same request as every bind and create. Every read that decides who a contact
    IS keeps using the secured column; the mirror carries no identity. This script provisions it, in the order task
    141 makes binding:

      (a) MIRROR     Creates contact.sprk_externalobjectidkey (Text 100, NOT field-secured). Every stored
                     sprk_externalobjectid must parse as a GUID (a value that does not STOPS the run — an operator
                     decides what it was); a binding not in lowercase "D" form is rewritten to it, and every binding
                     is COPIED into the mirror (6 bindings in spaarkedev1, 2026-09-30). A mirror carrying an oid its
                     own binding does not (squatted or half-cleared) is reported FAIL and never written — resolve it
                     per SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md §6.5.3.
      (b) UNIQUE     Alternate key sprk_ExternalObjectIdUniqueKey on contact(sprk_externalobjectidkey) — the mirror —
                     enforced by the platform's unique index, which counts rows of every state. NULLs are not enforced
                     (Microsoft Learn, "Define alternate keys"), so unbound contacts are unaffected. Refuses to create
                     the key while two contacts would share a mirror value, and reports FAIL for any alternate key on
                     the field-secured binding column (it would block step (d)).
      (c) COLUMNS    Global choices sprk_identityplane (External / Workforce) and sprk_identitycollisionreason; on contact:
                     sprk_identityplane, sprk_identitycollisionon, sprk_identitycollisionoid, sprk_identitycollisionplane,
                     sprk_identitycollisionreason (the FIRST colliding party) and sprk_identitycollisionparties (EVERY
                     party, JSON). BACKFILL: every bound contact with no plane is marked External —
                     every writer before task 141 was the CIAM path (6 of 6 in spaarkedev1, 2026-09-30).
                     Also a system view "Contacts with Identity Collisions" so an operator can see every open flag.
      (d) FLS        contact.sprk_externalobjectid and systemuser.sprk_primarycontact become field-secured (the mirror
                     does NOT — the platform-rule preflight below and a -Verify check keep it that way):
                       * "Spaarke Identity Link Readers"  — Read; associated with EVERY business unit's default team
                         (so every user keeps reading both fields: useInlineTodoCreate.ts and TrackingFieldTrio read
                         them as the signed-in user, and a secured column is HIDDEN from anyone without Read).
                       * "Spaarke Identity Link Writers"  — Read + Create + Update; the BFF application user(s),
                         associated EXPLICITLY (never relying on System Administrator — D-13 app users may lack it).
                     Members are associated BEFORE the columns are secured, so the BFF never loses Read.
      (e) SOLUTION   Adds every component to -SolutionUniqueName (default SpaarkeCore, which owns sprk_externalobjectid).
      (f) PUBLISH    contact, systemuser.

    WHAT THE UNSECURED MIRROR ALLOWS. A user with Write on contact can set sprk_externalobjectidkey. That can only
    DENY SERVICE to one identity — the index then refuses its bind or create, the BFF denies contact_key_conflict
    and flags the holder (reason "Key mirror held by another contact") — and is visible. It can never make a
    contact resolve as someone else: nothing resolves or binds by the mirror. The binding itself stays field-secured.

    ⚠️ DEPLOY ORDER. The BFF build from task 141 SELECTS sprk_identityplane, the mirror and the flag columns. Apply
    (a)–(c) BEFORE deploying that BFF, or every binding read fails with binding_column_missing (fail closed: CIAM and
    Type-2 sign-ins are denied until the columns exist).

    ⚠️ EVERY ENVIRONMENT THE BFF RECONCILES needs this schema: its own Dataverse:ServiceUrl, DATAVERSE_URL, and every
    ACTIVE sprk_dataverseenvironment row (the identity-link reconciliation job's provisioning targets). An
    environment without it is reported by every job run as a failed environment.

    ⚠️ A new business unit created later needs its default team added to the reader profile (re-run -Apply).

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids whose Dataverse application users must be members of the writer profile — the BFF
    managed identity (dev: 5967251e-171c-46fe-a6c2-ef843c90309d) and, while it is still an application user, the
    BFF app registration (dev: 1e40baad-e065-4aea-a8d4-4b7ab273458c). Required for -Apply and -Verify.

.PARAMETER SolutionUniqueName
    The unmanaged solution that carries the components. Default SpaarkeCore.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-ContactIdentityBindingSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c
    Dry run: every step's present / missing state; zero writes.

.EXAMPLE
    .\Set-ContactIdentityBindingSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Apply

.EXAMPLE
    .\Set-ContactIdentityBindingSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify

.NOTES
    unified-access-control-r2 task 141. Contract: projects/unified-access-control-r2/notes/141-link-contract.md.
    Auth: the operator's own az CLI identity (System Administrator in the environment). No secrets.
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

# ── Constants (the BFF reads these exact names: ContactBindingDecision / DataverseContactIdentityStore) ─────────
$BindingColumn = 'sprk_externalobjectid'            # the binding — FIELD-SECURED by (d)
$MirrorColumn = 'sprk_externalobjectidkey'          # the uniqueness mirror — NEVER secured (B2)
$KeySchemaName = 'sprk_ExternalObjectIdUniqueKey'
$PlaneOptionSet = 'sprk_identityplane'
$ReasonOptionSet = 'sprk_identitycollisionreason'
$ReaderProfileName = 'Spaarke Identity Link Readers'
$WriterProfileName = 'Spaarke Identity Link Writers'
$ViewName = 'Contacts with Identity Collisions'
$PlaneOptions = [ordered]@{ 100000000 = 'External'; 100000001 = 'Workforce' }
$ReasonOptions = [ordered]@{
    100000000 = 'Bound to a different oid'
    100000001 = 'Email carried by more than one contact'
    100000002 = 'Oid carried by more than one contact'
    100000003 = 'Linked to another user'
    100000004 = 'Binding unreadable'
    100000005 = 'Guest email matches a contact'
    100000006 = 'Linked contact bound to a different oid'
    100000007 = 'Link and binding name different contacts'
    100000008 = 'Linked contact inactive'
    100000009 = 'Guest linked to an unbound contact'
    100000010 = 'Invite matches a workforce contact'
    100000011 = 'Key mirror held by another contact'
}
$SecuredFields = @(
    @{ Entity = 'contact'; Attribute = $BindingColumn },
    @{ Entity = 'systemuser'; Attribute = 'sprk_primarycontact' }
)
$KeyAttributes = @($MirrorColumn)   # step (b): the columns of the contact alternate key — the MIRROR (B2)

# ── PLATFORM-RULE PREFLIGHT (no network; runs before auth) ─────────────────────────────────────────────────
# Dataverse refuses an alternate key on a field-secured column: "Attributes must not have field-level security
# applied" (https://learn.microsoft.com/en-us/power-apps/developer/data-platform/define-alternate-keys-entity) and
# "Columns that have the Enable column security property enabled can't be used as an alternate key"
# (https://learn.microsoft.com/en-us/power-apps/maker/data-platform/define-alternate-keys-reference-records).
# Owner round 4 item 4 (B2) resolved the original overlap by moving the key to the unsecured mirror; this check
# keeps any later edit from putting the two on one column again. -Apply STOPS here, before any write.
$PlatformConflicts = @($SecuredFields | Where-Object { $_.Entity -eq 'contact' -and $KeyAttributes -contains $_.Attribute } |
    ForEach-Object { "contact.$($_.Attribute) is both an alternate-key column (step b) and field-secured (step d); Dataverse allows only one" })
if ($Apply -and $PlatformConflicts.Count -gt 0) {
    foreach ($c in $PlatformConflicts) { Write-Host "  BLOCKED  $c" -ForegroundColor Red }
    throw ('BLOCKED: the identity-binding schema asks Dataverse for an alternate key on a field-secured column, which ' +
        'the platform refuses. The key belongs on the unsecured mirror (owner round 4 item 4, B2; ' +
        'notes/task-141-identity-binding.md §9). Nothing was written.')
}

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
# Dataverse metadata writes propagate asynchronously: a column or global choice created a moment ago can still 404
# on the next read (live run 2026-10-02: the mirror column was invisible right after its create, so the copy and the
# key were skipped). Poll a read until it answers, or give up loudly — never treat "not visible yet" as "absent".
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
function Get-AllPages([string]$Path) {
    $rows = @(); $next = "$Api/$Path"
    while ($next) {
        $page = Invoke-RestMethod -Uri $next -Headers ($headers + @{ Prefer = 'odata.maxpagesize=5000' }) -Method Get
        $rows += @($page.value); $next = $page.'@odata.nextLink'
    }
    return $rows
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

Write-Host "`nPlatform rules"
foreach ($c in $PlatformConflicts) { Report 'FAIL' "BLOCKED — $c (the key belongs on the unsecured mirror, task 141 notes §9)" }
if ($PlatformConflicts.Count -eq 0) { Report 'OK' "no alternate-key column is field-secured (key on contact.$MirrorColumn; FLS on contact.$BindingColumn)" }

# ── (a) MIRROR + NORMALISE + COPY ───────────────────────────────────────────────────────────────────────────
Write-Host "`n(a) Uniqueness mirror and stored oids"
$mirrorAttr = Try-DvGet "EntityDefinitions(LogicalName='contact')/Attributes(LogicalName='$MirrorColumn')?`$select=LogicalName,IsSecured,MetadataId"
if ($mirrorAttr) {
    Report 'OK' "contact.$MirrorColumn"
    if ($mirrorAttr.IsSecured) { Report 'FAIL' "contact.$MirrorColumn is field-secured — Dataverse cannot key a secured column; remove its column security" }
} elseif ($Verify) { Report 'MISSING' "contact.$MirrorColumn" }
elseif ($IsDryRun) { Report 'WOULD' "create contact.$MirrorColumn (Text 100, NOT field-secured)" }
else {
    Invoke-DvWrite POST "EntityDefinitions(LogicalName='contact')/Attributes" @{
        '@odata.type' = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'
        SchemaName    = 'sprk_ExternalObjectIdKey'
        DisplayName   = (New-Label 'External Object ID (uniqueness key)')
        Description   = (New-Label 'Mirror of sprk_externalobjectid that carries the alternate key (Dataverse cannot key a field-secured column). Written by the BFF with the same oid, in the same request, as every bind and create. NOTHING resolves by it — it carries no identity. A value here that the binding does not carry is a squat or a half-cleared binding: see SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md §6.5.3. Task 141, owner round 4 item 4 (B2).')
        RequiredLevel = @{ Value = 'None' }
        MaxLength     = 100
        FormatName    = @{ Value = 'Text' }
    } | Out-Null
    Report 'DONE' "created contact.$MirrorColumn"
    $mirrorAttr = Wait-DvRead "EntityDefinitions(LogicalName='contact')/Attributes(LogicalName='$MirrorColumn')?`$select=LogicalName,IsSecured,MetadataId" "contact.$MirrorColumn metadata"
    # The data endpoint can lag the metadata endpoint: the copy below selects the column, so wait for that too.
    Wait-DvRead "contacts?`$select=contactid,$MirrorColumn&`$top=1" "contact.$MirrorColumn in data queries" | Out-Null
}
$mirrorReadable = [bool]$mirrorAttr
$select = if ($mirrorReadable) { "contactid,$BindingColumn,$MirrorColumn" } else { "contactid,$BindingColumn" }

$bound = Get-AllPages "contacts?`$select=$select&`$filter=$BindingColumn ne null"
$bad = @(); $toWrite = @()
foreach ($c in $bound) {
    $g = [guid]::Empty
    if (-not [guid]::TryParse($c.$BindingColumn.Trim(), [ref]$g) -or $g -eq [guid]::Empty) { $bad += $c; continue }
    $d = $g.ToString('D')
    $mirror = if ($mirrorReadable) { $c.$MirrorColumn } else { $null }
    if ($c.$BindingColumn -cne $d -or $mirror -cne $d) {
        $toWrite += [pscustomobject]@{ Id = $c.contactid; From = $c.$BindingColumn; Mirror = $mirror; To = $d }
    }
}
Report 'OK' "$($bound.Count) contact(s) carry an oid"
foreach ($b in $bad) { Report 'FAIL' "contact $($b.contactid) carries '$($b.$BindingColumn)', which is not a GUID — an operator must decide what it was" }
if ($bad.Count -gt 0) { throw 'Unparseable oids found; fix them before applying the binding schema.' }
$dupes = $bound | Group-Object { ([guid]$_.$BindingColumn.Trim()).ToString('D') } | Where-Object Count -gt 1
foreach ($d in $dupes) { Report 'FAIL' "oid $($d.Name) is carried by $($d.Count) contacts: $(($d.Group | ForEach-Object contactid) -join ', ')" }
if ($dupes) { throw 'Duplicate oids found; the uniqueness key cannot be created until an operator resolves them.' }

# A mirror its own binding does not carry: squatted, or an operator cleared the binding and not the mirror. Never
# written by this script — the operator decides (guide §6.5.3). Checked BEFORE the copy so a copy cannot collide.
$orphans = @()
if ($mirrorReadable) {
    $mirrored = Get-AllPages "contacts?`$select=contactid,$BindingColumn,$MirrorColumn&`$filter=$MirrorColumn ne null"
    foreach ($m in $mirrored) {
        $mg = [guid]::Empty; $bg = [guid]::Empty
        $mirrorOk = [guid]::TryParse($m.$MirrorColumn.Trim(), [ref]$mg)
        $bindingOk = $m.$BindingColumn -and [guid]::TryParse($m.$BindingColumn.Trim(), [ref]$bg)
        if (-not ($mirrorOk -and $bindingOk -and $mg -eq $bg)) { $orphans += $m }
    }
}
foreach ($o in $orphans) {
    Report 'FAIL' "contact $($o.contactid) holds '$($o.$MirrorColumn)' in $MirrorColumn but its binding is '$($o.$BindingColumn)' — a squatted or half-cleared mirror; resolve per guide §6.5.3"
}
$clash = @($toWrite | Where-Object { $t = $_; @($orphans | Where-Object { $_.contactid -ne $t.Id -and ([string]$_.$MirrorColumn).Trim() -ieq $t.To }).Count -gt 0 })
if ($clash.Count -gt 0) { throw "A mirror value held by another contact blocks copying $($clash.Count) binding(s); resolve the FAIL rows above first." }

foreach ($n in $toWrite) {
    $what = if ($n.From -cne $n.To) { "normalise '$($n.From)' -> '$($n.To)' and copy into $MirrorColumn" } else { "copy '$($n.To)' into $MirrorColumn (was '$($n.Mirror)')" }
    if ($Verify) { Report 'FAIL' "contact $($n.Id): $what" }
    elseif ($IsDryRun -or -not $mirrorReadable) { Report 'WOULD' "contact $($n.Id): $what" }
    else {
        Invoke-DvWrite PATCH "contacts($($n.Id))" @{ $BindingColumn = $n.To; $MirrorColumn = $n.To } @{ 'If-Match' = '*' } | Out-Null
        Report 'DONE' "contact $($n.Id): $what"
    }
}
if ($toWrite.Count -eq 0) { Report 'OK' "every oid is lowercase D form and mirrored into $MirrorColumn" }

# ── (b) UNIQUE ──────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(b) Uniqueness key (on the mirror)"
$keys = @((Invoke-DvGet "EntityDefinitions(LogicalName='contact')/Keys?`$select=SchemaName,KeyAttributes,EntityKeyIndexStatus,MetadataId").value)
foreach ($k in $keys | Where-Object { @($_.KeyAttributes) -contains $BindingColumn }) {
    Report 'FAIL' "alternate key $($k.SchemaName) includes the field-secured $BindingColumn — Dataverse will refuse its column security; remove that key (the key belongs on $MirrorColumn)"
}
$key = $keys | Where-Object { @($_.KeyAttributes) -join ',' -eq $MirrorColumn } | Select-Object -First 1
if ($key) {
    if ($key.EntityKeyIndexStatus -eq 'Active') { Report 'OK' "$($key.SchemaName) is Active" }
    else { Report 'FAIL' "$($key.SchemaName) index status is $($key.EntityKeyIndexStatus) (not Active)" }
} elseif ($Verify) { Report 'MISSING' "alternate key on contact($MirrorColumn)" }
elseif ($IsDryRun -or -not $mirrorReadable) { Report 'WOULD' "create alternate key $KeySchemaName on contact($MirrorColumn)" }
else {
    Invoke-DvWrite POST "EntityDefinitions(LogicalName='contact')/Keys" @{
        SchemaName    = $KeySchemaName
        DisplayName   = (New-Label 'External Object ID (unique)')
        KeyAttributes = @($MirrorColumn)
    } | Out-Null
    Report 'DONE' "created $KeySchemaName (the index builds asynchronously — re-run -Verify until Active)"
}

# ── (c) COLUMNS + backfill + view ───────────────────────────────────────────────────────────────────────────
Write-Host "`n(c) Plane and collision columns"
function Ensure-GlobalOptionSet([string]$Name, [string]$Display, $Options) {
    $existing = Try-DvGet "GlobalOptionSetDefinitions(Name='$Name')"
    if ($existing) {
        Report 'OK' "global choice $Name"
        # A choice created by an earlier run may lack an option added since (e.g. 100000011, B2): add it.
        $present = @($existing.Options | ForEach-Object { [int]$_.Value })
        foreach ($value in $Options.Keys | Where-Object { $present -notcontains [int]$_ }) {
            if ($Verify) { Report 'MISSING' "option $value '$($Options[$value])' in $Name"; continue }
            if ($IsDryRun) { Report 'WOULD' "add option $value '$($Options[$value])' to $Name"; continue }
            Invoke-DvWrite POST 'InsertOptionValue' @{ OptionSetName = $Name; Value = $value; Label = (New-Label $Options[$value]) } | Out-Null
            Report 'DONE' "added option $value to $Name"
        }
        return
    }
    if ($Verify) { Report 'MISSING' "global choice $Name"; return }
    if ($IsDryRun) { Report 'WOULD' "create global choice $Name ($($Options.Count) options)"; return }
    Invoke-DvWrite POST 'GlobalOptionSetDefinitions' @{
        '@odata.type' = 'Microsoft.Dynamics.CRM.OptionSetMetadata'
        Name          = $Name
        DisplayName   = (New-Label $Display)
        IsGlobal      = $true
        OptionSetType = 'Picklist'
        Options       = @($Options.Keys | ForEach-Object { @{ Value = $_; Label = (New-Label $Options[$_]) } })
    } | Out-Null
    Report 'DONE' "created global choice $Name"
}
Ensure-GlobalOptionSet $PlaneOptionSet 'Identity Plane' $PlaneOptions
Ensure-GlobalOptionSet $ReasonOptionSet 'Identity Collision Reason' $ReasonOptions

$columns = @(
    @{ Logical = 'sprk_identityplane'; Schema = 'sprk_IdentityPlane'; Display = 'Identity Plane'
       Description = 'Which sign-in plane wrote sprk_externalobjectid: External (CIAM) or Workforce. Written by the BFF only, always together with the oid (task 141).'
       Body = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata'; }; OptionSet = $PlaneOptionSet },
    @{ Logical = 'sprk_identitycollisionon'; Schema = 'sprk_IdentityCollisionOn'; Display = 'Identity Collision On'
       Description = 'When an identity collision was flagged on this contact. Empty = no open collision. Cleared by the identity-link reconciliation job once an operator resolves it.'
       Body = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.DateTimeAttributeMetadata'; Format = 'DateAndTime'; DateTimeBehavior = @{ Value = 'UserLocal' } } },
    @{ Logical = 'sprk_identitycollisionoid'; Schema = 'sprk_IdentityCollisionOid'; Display = 'Identity Collision Oid'
       Description = 'The Entra object id of the identity that collided with this contact.'
       Body = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'; MaxLength = 100; FormatName = @{ Value = 'Text' } } },
    @{ Logical = 'sprk_identitycollisionplane'; Schema = 'sprk_IdentityCollisionPlane'; Display = 'Identity Collision Plane'
       Description = 'The sign-in plane of the colliding identity.'
       Body = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata'; }; OptionSet = $PlaneOptionSet },
    @{ Logical = 'sprk_identitycollisionreason'; Schema = 'sprk_IdentityCollisionReason'; Display = 'Identity Collision Reason'
       Description = 'Why the binding was refused. See SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md, identity collisions.'
       Body = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata'; }; OptionSet = $ReasonOptionSet },
    @{ Logical = 'sprk_identitycollisionparties'; Schema = 'sprk_IdentityCollisionParties'; Display = 'Identity Collision Parties'
       Description = 'EVERY identity that collided with this contact (JSON: oid, plane, reason, time), the first one being the four Identity Collision columns. Written by the BFF; the reconciliation job drops a party once its collision no longer holds and clears the flag when none does. Do not edit by hand: an unreadable value keeps the flag until an operator clears it.'
       Body = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.MemoAttributeMetadata'; Format = 'TextArea'; MaxLength = 4000 } }
)
foreach ($col in $columns) {
    $existing = Try-DvGet "EntityDefinitions(LogicalName='contact')/Attributes(LogicalName='$($col.Logical)')?`$select=LogicalName,MetadataId"
    if ($existing) { Report 'OK' "contact.$($col.Logical)"; continue }
    if ($Verify) { Report 'MISSING' "contact.$($col.Logical)"; continue }
    if ($IsDryRun) { Report 'WOULD' "create contact.$($col.Logical)"; continue }
    $body = $col.Body.Clone()
    if ($col.OptionSet) {
        # The metadata API binds a global choice by its MetadataId GUID only; Name='...' is refused with
        # "Guid should contain 32 digits with 4 dashes" (live run 2026-10-02). Resolve it (with the propagation wait,
        # since the choice may have been created moments ago in step (c)).
        $os = Wait-DvRead "GlobalOptionSetDefinitions(Name='$($col.OptionSet)')?`$select=MetadataId" "global choice $($col.OptionSet)"
        $body['GlobalOptionSet@odata.bind'] = "/GlobalOptionSetDefinitions($($os.MetadataId))"
    }
    $body.SchemaName = $col.Schema
    $body.DisplayName = New-Label $col.Display
    $body.Description = New-Label $col.Description
    $body.RequiredLevel = @{ Value = 'None' }
    Invoke-DvWrite POST "EntityDefinitions(LogicalName='contact')/Attributes" $body | Out-Null
    Report 'DONE' "created contact.$($col.Logical)"
}

$planeExists = [bool](Try-DvGet "EntityDefinitions(LogicalName='contact')/Attributes(LogicalName='sprk_identityplane')?`$select=LogicalName")
if ($planeExists) {
    $unmarked = Get-AllPages 'contacts?$select=contactid&$filter=sprk_externalobjectid ne null and sprk_identityplane eq null'
    if ($unmarked.Count -eq 0) { Report 'OK' 'every bound contact carries a plane' }
    elseif ($Verify) { Report 'FAIL' "$($unmarked.Count) bound contact(s) carry no plane" }
    elseif ($IsDryRun) { Report 'WOULD' "backfill $($unmarked.Count) bound contact(s) as External (every pre-141 writer was CIAM)" }
    else {
        foreach ($u in $unmarked) { Invoke-DvWrite PATCH "contacts($($u.contactid))" @{ sprk_identityplane = 100000000 } @{ 'If-Match' = '*' } | Out-Null }
        Report 'DONE' "backfilled $($unmarked.Count) bound contact(s) as External"
    }
} else {
    Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "backfill of $($bound.Count) bound contact(s) as External (after the column exists)"
}

$view = @((Invoke-DvGet "savedqueries?`$select=savedqueryid,name&`$filter=returnedtypecode eq 'contact' and name eq '$ViewName'").value) | Select-Object -First 1
if ($view) { Report 'OK' "view '$ViewName'" }
elseif ($Verify) { Report 'MISSING' "view '$ViewName'" }
elseif ($IsDryRun -or -not $planeExists) { Report 'WOULD' "create view '$ViewName' (open identity collisions, newest first)" }
else {
    $fetch = "<fetch version='1.0' mapping='logical'><entity name='contact'><attribute name='fullname'/><attribute name='emailaddress1'/><attribute name='sprk_externalobjectid'/><attribute name='sprk_externalobjectidkey'/><attribute name='sprk_identityplane'/><attribute name='sprk_identitycollisionon'/><attribute name='sprk_identitycollisionreason'/><attribute name='sprk_identitycollisionoid'/><attribute name='sprk_identitycollisionplane'/><attribute name='sprk_identitycollisionparties'/><attribute name='contactid'/><order attribute='sprk_identitycollisionon' descending='true'/><filter type='and'><condition attribute='sprk_identitycollisionon' operator='not-null'/></filter></entity></fetch>"
    $layout = "<grid name='resultset' object='2' jump='fullname' select='1' icon='1' preview='1'><row name='result' id='contactid'><cell name='fullname' width='200'/><cell name='emailaddress1' width='200'/><cell name='sprk_identitycollisionreason' width='220'/><cell name='sprk_identitycollisionon' width='150'/><cell name='sprk_identitycollisionoid' width='250'/><cell name='sprk_identitycollisionplane' width='120'/><cell name='sprk_identitycollisionparties' width='300'/><cell name='sprk_externalobjectid' width='250'/><cell name='sprk_externalobjectidkey' width='250'/><cell name='sprk_identityplane' width='120'/></row></grid>"
    Invoke-DvWrite POST 'savedqueries' @{ name = $ViewName; returnedtypecode = 'contact'; querytype = 0; fetchxml = $fetch; layoutxml = $layout
        description = 'Every contact with an open identity collision (task 141). Resolve per SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md; the reconciliation job clears the flag.' } | Out-Null
    Report 'DONE' "created view '$ViewName'"
}

# ── (d) FIELD-LEVEL SECURITY ────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(d) Field-level security"
function Ensure-Profile([string]$Name, [string]$Description) {
    $p = @((Invoke-DvGet "fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=name eq '$Name'").value) | Select-Object -First 1
    if ($p) { Report 'OK' "profile '$Name'"; return $p.fieldsecurityprofileid }
    if ($Verify) { Report 'MISSING' "profile '$Name'"; return $null }
    if ($IsDryRun) { Report 'WOULD' "create profile '$Name'"; return $null }
    $created = Invoke-DvWrite POST 'fieldsecurityprofiles' @{ name = $Name; description = $Description } @{ Prefer = 'return=representation' }
    Report 'DONE' "created profile '$Name'"
    return $created.fieldsecurityprofileid
}
$readerId = Ensure-Profile $ReaderProfileName 'Read on contact.sprk_externalobjectid and systemuser.sprk_primarycontact for every user (associated with every business unit default team). Without Read a secured column is HIDDEN. Task 141.'
$writerId = Ensure-Profile $WriterProfileName 'Read/Create/Update on contact.sprk_externalobjectid and systemuser.sprk_primarycontact. Members: the BFF application user(s) ONLY — every binding and link is written by the BFF. Task 141.'

# Members first, so the BFF keeps Read from the moment the columns are secured.
$bffUsers = @()
foreach ($appId in $BffApplicationIds) {
    $u = @((Invoke-DvGet "systemusers?`$select=systemuserid,fullname&`$filter=applicationid eq $appId").value) | Select-Object -First 1
    if (-not $u) { Report 'FAIL' "no application user for appId $appId in this environment"; continue }
    $bffUsers += $u
}
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
foreach ($u in $bffUsers) { Ensure-Member $writerId $WriterProfileName 'systemuserprofiles_association' 'systemusers' $u.systemuserid "app user '$($u.fullname)'" }
foreach ($t in $defaultTeams) { Ensure-Member $readerId $ReaderProfileName 'teamprofiles_association' 'teams' $t.teamid "default team '$($t.name)'" }

foreach ($f in $SecuredFields) {
    $attr = Invoke-DvGet "EntityDefinitions(LogicalName='$($f.Entity)')/Attributes(LogicalName='$($f.Attribute)')?`$select=LogicalName,IsSecured,MetadataId"
    if ($attr.IsSecured) { Report 'OK' "$($f.Entity).$($f.Attribute) is field-secured" }
    elseif ($Verify) { Report 'MISSING' "$($f.Entity).$($f.Attribute) is NOT field-secured — anyone with Write can change whose grants a caller inherits" }
    elseif ($IsDryRun) { Report 'WOULD' "secure $($f.Entity).$($f.Attribute)" }
    else {
        $typed = Invoke-DvGet "EntityDefinitions(LogicalName='$($f.Entity)')/Attributes(LogicalName='$($f.Attribute)')"
        $typed.IsSecured = $true
        Invoke-DvWrite PUT "EntityDefinitions(LogicalName='$($f.Entity)')/Attributes(LogicalName='$($f.Attribute)')" $typed @{ 'MSCRM.MergeLabels' = 'true' } | Out-Null
        Report 'DONE' "secured $($f.Entity).$($f.Attribute)"
    }

    foreach ($spec in @(@{ Id = $readerId; Name = $ReaderProfileName; Create = 0; Update = 0 }, @{ Id = $writerId; Name = $WriterProfileName; Create = 4; Update = 4 })) {
        if (-not $spec.Id) { Report $(if ($Verify) { 'MISSING' } else { 'WOULD' }) "$($spec.Name) permission on $($f.Entity).$($f.Attribute)"; continue }
        $perm = @((Invoke-DvGet "fieldpermissions?`$select=fieldpermissionid,canread,cancreate,canupdate&`$filter=_fieldsecurityprofileid_value eq $($spec.Id) and entityname eq '$($f.Entity)' and attributelogicalname eq '$($f.Attribute)'").value) | Select-Object -First 1
        if ($perm) {
            if ($perm.canread -eq 4 -and $perm.cancreate -eq $spec.Create -and $perm.canupdate -eq $spec.Update) { Report 'OK' "$($spec.Name) on $($f.Entity).$($f.Attribute)" }
            else { Report 'FAIL' "$($spec.Name) on $($f.Entity).$($f.Attribute) is read=$($perm.canread) create=$($perm.cancreate) update=$($perm.canupdate); expected read=4 create=$($spec.Create) update=$($spec.Update)" }
            continue
        }
        if ($Verify) { Report 'MISSING' "$($spec.Name) on $($f.Entity).$($f.Attribute)"; continue }
        if ($IsDryRun) { Report 'WOULD' "grant $($spec.Name) on $($f.Entity).$($f.Attribute) (read=4 create=$($spec.Create) update=$($spec.Update))"; continue }
        # Securing a column propagates asynchronously: the FIRST grant after securing may succeed while the second is
        # refused 0x8004f508 "... is NOT secured for entity fieldpermission" (live run 2026-10-02, on both columns).
        # Retry that one error; anything else propagates.
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
                Write-Host "    $($f.Entity).$($f.Attribute) not yet seen as secured by the field-permission service; retrying ($attempt/12)..."
                Start-Sleep -Seconds 10
            }
        }
        Report 'DONE' "granted $($spec.Name) on $($f.Entity).$($f.Attribute)"
    }

    # Nobody else may write: any other profile granting create/update (besides the platform's System
    # Administrator profile) is reported — the lock is only as strong as its narrowest writer list.
    $writers = @((Invoke-DvGet "fieldpermissions?`$select=canupdate,cancreate&`$expand=fieldsecurityprofileid(`$select=name)&`$filter=entityname eq '$($f.Entity)' and attributelogicalname eq '$($f.Attribute)' and (canupdate eq 4 or cancreate eq 4)").value)
    foreach ($w in $writers | Where-Object { $_.fieldsecurityprofileid.name -notin $WriterProfileName, 'System Administrator' }) {
        Report 'FAIL' "profile '$($w.fieldsecurityprofileid.name)' can also write $($f.Entity).$($f.Attribute)"
    }
}

# ── (e) SOLUTION ────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(e) Solution components"
$solution = @((Invoke-DvGet "solutions?`$select=solutionid,uniquename&`$filter=uniquename eq '$SolutionUniqueName'").value) | Select-Object -First 1
if (-not $solution) { Report 'FAIL' "solution '$SolutionUniqueName' not found" }
else {
    $components = [System.Collections.Generic.List[object]]::new()
    $contactTableId = (Invoke-DvGet "EntityDefinitions(LogicalName='contact')?`$select=MetadataId").MetadataId
    foreach ($os in $PlaneOptionSet, $ReasonOptionSet) {
        $m = Try-DvGet "GlobalOptionSetDefinitions(Name='$os')?`$select=MetadataId"; if ($m) { $components.Add(@{ Id = $m.MetadataId; Type = 9; Label = "choice $os" }) }
    }
    foreach ($col in $columns) {
        $m = Try-DvGet "EntityDefinitions(LogicalName='contact')/Attributes(LogicalName='$($col.Logical)')?`$select=MetadataId"; if ($m) { $components.Add(@{ Id = $m.MetadataId; Type = 2; Label = "contact.$($col.Logical)"; TableId = $contactTableId }) }
    }
    if ($mirrorAttr) { $components.Add(@{ Id = $mirrorAttr.MetadataId; Type = 2; Label = "contact.$MirrorColumn"; TableId = $contactTableId }) }
    foreach ($f in $SecuredFields) {
        $m = Try-DvGet "EntityDefinitions(LogicalName='$($f.Entity)')/Attributes(LogicalName='$($f.Attribute)')?`$select=MetadataId"; if ($m) { $components.Add(@{ Id = $m.MetadataId; Type = 2; Label = "$($f.Entity).$($f.Attribute)"; TableId = (Invoke-DvGet "EntityDefinitions(LogicalName='$($f.Entity)')?`$select=MetadataId").MetadataId }) }
    }
    $k = @((Invoke-DvGet "EntityDefinitions(LogicalName='contact')/Keys?`$select=MetadataId,KeyAttributes").value) | Where-Object { @($_.KeyAttributes) -join ',' -eq $MirrorColumn } | Select-Object -First 1
    if ($k) { $components.Add(@{ Id = $k.MetadataId; Type = 14; Label = "key $KeySchemaName"; TableId = $contactTableId }) }
    foreach ($profileId in $readerId, $writerId) { if ($profileId) { $components.Add(@{ Id = $profileId; Type = 70; Label = "field security profile $profileId" }) } }
    $v = @((Invoke-DvGet "savedqueries?`$select=savedqueryid&`$filter=returnedtypecode eq 'contact' and name eq '$ViewName'").value) | Select-Object -First 1
    if ($v) { $components.Add(@{ Id = $v.savedqueryid; Type = 26; Label = "view '$ViewName'" }) }

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

# ── (f) PUBLISH ─────────────────────────────────────────────────────────────────────────────────────────────
if ($Apply) {
    Invoke-DvWrite POST 'PublishXml' @{ ParameterXml = '<importexportxml><entities><entity>contact</entity><entity>systemuser</entity></entities></importexportxml>' } | Out-Null
    Write-Host "`nPublished contact and systemuser." -ForegroundColor Green
}

if ($Verify) {
    if ($gaps.Count -eq 0) { Write-Host "`nVERIFY PASS: the identity-binding schema is complete." -ForegroundColor Green; exit 0 }
    Write-Host "`nVERIFY FAIL ($($gaps.Count)):" -ForegroundColor Red
    $gaps | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}
if ($IsDryRun) { Write-Host "`nDRY RUN complete — nothing was written. Re-run with -Apply." -ForegroundColor Cyan }
