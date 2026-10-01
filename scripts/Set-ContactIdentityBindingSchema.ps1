#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Applies the identity-binding schema of unified-access-control-r2 task 141 to one Dataverse environment:
    the oid-binding uniqueness key, the plane + collision columns, the backfill, field-level security, and the
    solution components. Dry run by default; -Apply writes; -Verify checks. Idempotent.

.DESCRIPTION
    The BFF binds a person to a contact by Entra oid in contact.sprk_externalobjectid (ONE field for both the
    workforce and CIAM planes). This script provisions what that needs, in the order task 141 makes binding:

      (a) NORMALISE  Every stored sprk_externalobjectid must parse as a GUID; values not in lowercase "D" form are
                     rewritten to it. A value that does not parse STOPS the run (an operator decides what it was).
      (b) UNIQUE     Alternate key sprk_ExternalObjectIdKey on contact(sprk_externalobjectid) — exactly one contact per
                     oid, enforced by the platform's unique index. NULLs are not enforced (Microsoft Learn,
                     "Define alternate keys": uniqueness is not enforced for null values), so unbound contacts are
                     unaffected. Refuses to create the key while duplicates exist.
                     ⛔ BLOCKED with (d): Dataverse refuses an alternate key on a field-secured column, and (d)
                     secures this same column. -Apply stops at the PLATFORM-RULE PREFLIGHT until the owner decides
                     under CLAUDE.md §6.5 which mechanism moves (task 141 notes §9). Dry run and -Verify report it.
      (c) COLUMNS    Global choices sprk_identityplane (External / Workforce) and sprk_identitycollisionreason; on contact:
                     sprk_identityplane, sprk_identitycollisionon, sprk_identitycollisionoid, sprk_identitycollisionplane,
                     sprk_identitycollisionreason (the FIRST colliding party) and sprk_identitycollisionparties (EVERY
                     party, JSON). BACKFILL: every bound contact with no plane is marked External —
                     every writer before task 141 was the CIAM path (6 of 6 in spaarkedev1, 2026-09-30).
                     Also a system view "Contacts with Identity Collisions" so an operator can see every open flag.
      (d) FLS        contact.sprk_externalobjectid and systemuser.sprk_primarycontact become field-secured:
                       * "Spaarke Identity Link Readers"  — Read; associated with EVERY business unit's default team
                         (so every user keeps reading both fields: useInlineTodoCreate.ts and TrackingFieldTrio read
                         them as the signed-in user, and a secured column is HIDDEN from anyone without Read).
                       * "Spaarke Identity Link Writers"  — Read + Create + Update; the BFF application user(s),
                         associated EXPLICITLY (never relying on System Administrator — D-13 app users may lack it).
                     Members are associated BEFORE the columns are secured, so the BFF never loses Read.
                     ⛔ contact.sprk_externalobjectid: BLOCKED with (b) — see above.
      (e) SOLUTION   Adds every component to -SolutionUniqueName (default SpaarkeCore, which owns sprk_externalobjectid).
      (f) PUBLISH    contact, systemuser.

    ⚠️ DEPLOY ORDER. The BFF build from task 141 SELECTS sprk_identityplane and the flag columns. Apply (a)–(c)
    BEFORE deploying that BFF, or every binding read fails with binding_column_missing (fail closed: CIAM and
    Type-2 sign-ins are denied until the columns exist).

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
$Api = "$EnvironmentUrl/api/data/v9.2"

# ── Constants (the BFF reads these exact names: ContactBindingDecision / DataverseContactIdentityStore) ─────────
$KeySchemaName = 'sprk_ExternalObjectIdKey'
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
}
$SecuredFields = @(
    @{ Entity = 'contact'; Attribute = 'sprk_externalobjectid' },
    @{ Entity = 'systemuser'; Attribute = 'sprk_primarycontact' }
)
$KeyAttributes = @('sprk_externalobjectid')   # step (b): the columns of the contact alternate key

# ── PLATFORM-RULE PREFLIGHT (no network; runs before auth) ─────────────────────────────────────────────────
# Dataverse refuses an alternate key on a field-secured column: "Attributes must not have field-level security
# applied" (https://learn.microsoft.com/en-us/power-apps/developer/data-platform/define-alternate-keys-entity) and
# "Columns that have the Enable column security property enabled can't be used as an alternate key"
# (https://learn.microsoft.com/en-us/power-apps/maker/data-platform/define-alternate-keys-reference-records).
# Steps (b) and (d) as designed put BOTH on contact.sprk_externalobjectid, so -Apply could deliver at most one of
# them: the key (and FLS fails — anyone with contact Write can put their own oid on a contact that holds grants)
# or FLS (and the key fails — every contact creation denies contact_create_unavailable). Which one the platform
# keeps is an OWNER DECISION under CLAUDE.md §6.5 (task 141 verifier finding 1; notes/task-141-identity-binding.md
# §9). Until it is made, -Apply STOPS here, before any write, so a partial schema cannot be left behind.
$PlatformConflicts = @($SecuredFields | Where-Object { $_.Entity -eq 'contact' -and $KeyAttributes -contains $_.Attribute } |
    ForEach-Object { "contact.$($_.Attribute) is both an alternate-key column (step b) and field-secured (step d); Dataverse allows only one" })
if ($Apply -and $PlatformConflicts.Count -gt 0) {
    foreach ($c in $PlatformConflicts) { Write-Host "  BLOCKED  $c" -ForegroundColor Red }
    throw ('BLOCKED: the identity-binding schema asks Dataverse for an alternate key on a field-secured column, which ' +
        'the platform refuses. An owner decision is pending under CLAUDE.md §6.5 (unified-access-control-r2 task 141, ' +
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
foreach ($c in $PlatformConflicts) { Report 'FAIL' "BLOCKED — $c (owner decision pending, task 141 notes §9)" }
if ($PlatformConflicts.Count -eq 0) { Report 'OK' 'no alternate-key column is field-secured' }

# ── (a) NORMALISE ───────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(a) Stored oids"
$bound = Get-AllPages 'contacts?$select=contactid,sprk_externalobjectid&$filter=sprk_externalobjectid ne null'
$bad = @(); $toNormalise = @()
foreach ($c in $bound) {
    $g = [guid]::Empty
    if (-not [guid]::TryParse($c.sprk_externalobjectid.Trim(), [ref]$g) -or $g -eq [guid]::Empty) { $bad += $c; continue }
    if ($c.sprk_externalobjectid -cne $g.ToString('D')) { $toNormalise += [pscustomobject]@{ Id = $c.contactid; From = $c.sprk_externalobjectid; To = $g.ToString('D') } }
}
Report 'OK' "$($bound.Count) contact(s) carry an oid"
foreach ($b in $bad) { Report 'FAIL' "contact $($b.contactid) carries '$($b.sprk_externalobjectid)', which is not a GUID — an operator must decide what it was" }
if ($bad.Count -gt 0) { throw 'Unparseable oids found; fix them before applying the binding schema.' }
$dupes = $bound | Group-Object { ([guid]$_.sprk_externalobjectid.Trim()).ToString('D') } | Where-Object Count -gt 1
foreach ($d in $dupes) { Report 'FAIL' "oid $($d.Name) is carried by $($d.Count) contacts: $(($d.Group | ForEach-Object contactid) -join ', ')" }
if ($dupes) { throw 'Duplicate oids found; the uniqueness key cannot be created until an operator resolves them.' }
foreach ($n in $toNormalise) {
    if ($Verify) { Report 'FAIL' "contact $($n.Id) oid '$($n.From)' is not lowercase D form" }
    elseif ($IsDryRun) { Report 'WOULD' "normalise contact $($n.Id): '$($n.From)' -> '$($n.To)'" }
    else { Invoke-DvWrite PATCH "contacts($($n.Id))" @{ sprk_externalobjectid = $n.To } @{ 'If-Match' = '*' } | Out-Null; Report 'DONE' "normalised contact $($n.Id)" }
}
if ($toNormalise.Count -eq 0) { Report 'OK' 'every oid is lowercase D form' }

# ── (b) UNIQUE ──────────────────────────────────────────────────────────────────────────────────────────────
Write-Host "`n(b) Uniqueness key"
$keys = @((Invoke-DvGet "EntityDefinitions(LogicalName='contact')/Keys?`$select=SchemaName,KeyAttributes,EntityKeyIndexStatus,MetadataId").value)
$key = $keys | Where-Object { @($_.KeyAttributes) -join ',' -eq 'sprk_externalobjectid' } | Select-Object -First 1
if ($key) {
    if ($key.EntityKeyIndexStatus -eq 'Active') { Report 'OK' "$($key.SchemaName) is Active" }
    else { Report 'FAIL' "$($key.SchemaName) index status is $($key.EntityKeyIndexStatus) (not Active)" }
} elseif ($Verify) { Report 'MISSING' "alternate key on contact(sprk_externalobjectid)" }
elseif ($IsDryRun) { Report 'WOULD' "create alternate key $KeySchemaName on contact(sprk_externalobjectid)" }
else {
    Invoke-DvWrite POST "EntityDefinitions(LogicalName='contact')/Keys" @{
        SchemaName    = $KeySchemaName
        DisplayName   = (New-Label 'External Object ID (unique)')
        KeyAttributes = @('sprk_externalobjectid')
    } | Out-Null
    Report 'DONE' "created $KeySchemaName (the index builds asynchronously — re-run -Verify until Active)"
}

# ── (c) COLUMNS + backfill + view ───────────────────────────────────────────────────────────────────────────
Write-Host "`n(c) Plane and collision columns"
function Ensure-GlobalOptionSet([string]$Name, [string]$Display, $Options) {
    $existing = Try-DvGet "GlobalOptionSetDefinitions(Name='$Name')"
    if ($existing) { Report 'OK' "global choice $Name"; return }
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
       Body = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata'; 'GlobalOptionSet@odata.bind' = "/GlobalOptionSetDefinitions(Name='$PlaneOptionSet')" } },
    @{ Logical = 'sprk_identitycollisionon'; Schema = 'sprk_IdentityCollisionOn'; Display = 'Identity Collision On'
       Description = 'When an identity collision was flagged on this contact. Empty = no open collision. Cleared by the identity-link reconciliation job once an operator resolves it.'
       Body = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.DateTimeAttributeMetadata'; Format = 'DateAndTime'; DateTimeBehavior = @{ Value = 'UserLocal' } } },
    @{ Logical = 'sprk_identitycollisionoid'; Schema = 'sprk_IdentityCollisionOid'; Display = 'Identity Collision Oid'
       Description = 'The Entra object id of the identity that collided with this contact.'
       Body = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.StringAttributeMetadata'; MaxLength = 100; FormatName = @{ Value = 'Text' } } },
    @{ Logical = 'sprk_identitycollisionplane'; Schema = 'sprk_IdentityCollisionPlane'; Display = 'Identity Collision Plane'
       Description = 'The sign-in plane of the colliding identity.'
       Body = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata'; 'GlobalOptionSet@odata.bind' = "/GlobalOptionSetDefinitions(Name='$PlaneOptionSet')" } },
    @{ Logical = 'sprk_identitycollisionreason'; Schema = 'sprk_IdentityCollisionReason'; Display = 'Identity Collision Reason'
       Description = 'Why the binding was refused. See SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md, identity collisions.'
       Body = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.PicklistAttributeMetadata'; 'GlobalOptionSet@odata.bind' = "/GlobalOptionSetDefinitions(Name='$ReasonOptionSet')" } },
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
    $fetch = "<fetch version='1.0' mapping='logical'><entity name='contact'><attribute name='fullname'/><attribute name='emailaddress1'/><attribute name='sprk_externalobjectid'/><attribute name='sprk_identityplane'/><attribute name='sprk_identitycollisionon'/><attribute name='sprk_identitycollisionreason'/><attribute name='sprk_identitycollisionoid'/><attribute name='sprk_identitycollisionplane'/><attribute name='sprk_identitycollisionparties'/><attribute name='contactid'/><order attribute='sprk_identitycollisionon' descending='true'/><filter type='and'><condition attribute='sprk_identitycollisionon' operator='not-null'/></filter></entity></fetch>"
    $layout = "<grid name='resultset' object='2' jump='fullname' select='1' icon='1' preview='1'><row name='result' id='contactid'><cell name='fullname' width='200'/><cell name='emailaddress1' width='200'/><cell name='sprk_identitycollisionreason' width='220'/><cell name='sprk_identitycollisionon' width='150'/><cell name='sprk_identitycollisionoid' width='250'/><cell name='sprk_identitycollisionplane' width='120'/><cell name='sprk_identitycollisionparties' width='300'/><cell name='sprk_externalobjectid' width='250'/><cell name='sprk_identityplane' width='120'/></row></grid>"
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
        Invoke-DvWrite POST 'fieldpermissions' @{
            entityname = $f.Entity; attributelogicalname = $f.Attribute
            canread = 4; cancreate = $spec.Create; canupdate = $spec.Update
            'fieldsecurityprofileid@odata.bind' = "/fieldsecurityprofiles($($spec.Id))"
        } | Out-Null
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
    foreach ($os in $PlaneOptionSet, $ReasonOptionSet) {
        $m = Try-DvGet "GlobalOptionSetDefinitions(Name='$os')?`$select=MetadataId"; if ($m) { $components.Add(@{ Id = $m.MetadataId; Type = 9; Label = "choice $os" }) }
    }
    foreach ($col in $columns) {
        $m = Try-DvGet "EntityDefinitions(LogicalName='contact')/Attributes(LogicalName='$($col.Logical)')?`$select=MetadataId"; if ($m) { $components.Add(@{ Id = $m.MetadataId; Type = 2; Label = "contact.$($col.Logical)" }) }
    }
    foreach ($f in $SecuredFields) {
        $m = Try-DvGet "EntityDefinitions(LogicalName='$($f.Entity)')/Attributes(LogicalName='$($f.Attribute)')?`$select=MetadataId"; if ($m) { $components.Add(@{ Id = $m.MetadataId; Type = 2; Label = "$($f.Entity).$($f.Attribute)" }) }
    }
    $k = @((Invoke-DvGet "EntityDefinitions(LogicalName='contact')/Keys?`$select=MetadataId,KeyAttributes").value) | Where-Object { @($_.KeyAttributes) -join ',' -eq 'sprk_externalobjectid' } | Select-Object -First 1
    if ($k) { $components.Add(@{ Id = $k.MetadataId; Type = 14; Label = "key $KeySchemaName" }) }
    foreach ($profileId in $readerId, $writerId) { if ($profileId) { $components.Add(@{ Id = $profileId; Type = 70; Label = "field security profile $profileId" }) } }
    $v = @((Invoke-DvGet "savedqueries?`$select=savedqueryid&`$filter=returnedtypecode eq 'contact' and name eq '$ViewName'").value) | Select-Object -First 1
    if ($v) { $components.Add(@{ Id = $v.savedqueryid; Type = 26; Label = "view '$ViewName'" }) }

    $inSolution = @((Invoke-DvGet "solutioncomponents?`$select=objectid&`$filter=_solutionid_value eq $($solution.solutionid)").value | ForEach-Object { $_.objectid.ToString().ToLowerInvariant() })
    foreach ($c in $components) {
        if ($inSolution -contains $c.Id.ToString().ToLowerInvariant()) { Report 'OK' "$($c.Label) in $SolutionUniqueName"; continue }
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
