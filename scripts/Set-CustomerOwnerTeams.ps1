#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Sets up ONE customer's top business unit (a direct child of the root, never the root and never Secure Record): the
    declared memberless owner teams, each with its own role holding Read at User (Basic) depth on exactly the tables its
    config file declares, and the directly assigned 'Billing Rule Reader' role (Read at Deep on sprk_policy only), with the
    org setting it needs. Dry run by default.

.DESCRIPTION
    The Spaarke root business unit holds only Spaarke product data; customer records and customer configuration belong to
    the customer's own top business unit, the direct child of the root that provisioning H10 creates (customerBusinessUnitId,
    a sibling of Secure Record) (owner round 136). Dataverse refuses to assign a row to a principal whose roles lack Read on
    the table (0x80040299), and a unit's default team is not an acceptable owner (a role on it reaches every member). So each
    customer top unit holds these declared, memberless owner teams, one per owner config file in $DeclaredRoleFiles:

      restricted-content-owner-role.json      'Spaarke Restricted Content Owners' / 'Restricted Content Owner'
          Owner rounds 131 (the 086 ruling) and 136 (unified-access-control-r2 task 194b R4b-2/R4b-2c): an ordinary row of
          a hard-deny table is owned by this team in place of a business unit's default team. The BFF finds it from the
          answer itself (it walks up to the root's direct child) and refuses the write
          (record_owner_restricted_content_team_unresolved) while the team is missing, ambiguous or has a member.

      customer-configuration-owner-role.json  'Spaarke Customer Configuration Owners' / 'Customer Configuration Owner'
          R4b-3b (field-mapping #2399, owner shape A): customer configuration rows (the declared configuration tables) are
          owned by this team. The BFF does not resolve it. Field-mapping's seeder (its task 004) and provisioning's H12b
          seeder (FR-39) WILL own new configuration rows to this team; until they ship, rows stay with the seeding identity
          and an operator re-owns them to this team after setup. Users keep reading the rows through their own roles
          (Spaarke Core User, Deep).

    Every customer top unit needs both set up BEFORE a BFF with the rule, or a seeder, writes its rows.

    The same run also sets up one role that NO team holds (ebilling-r1 task 089d, option R; owner 2026-11-04 "use direct
    role approach"):

      billing-rule-readers-role.json          (no team) / 'Billing Rule Reader'
          Read at Deep on sprk_policy only, isinherited 0, assigned DIRECTLY to each of the customer's child-unit billing
          admins. The copy assigned is the customer TOP unit's, never the root's, Secure Record's or the admin's own unit's:
          a role's depth is measured from the business unit of the copy a principal holds, so a holder reads every rule
          owned in the customer's top unit and the units below it, and none in Secure Record (a sibling). Assigning another
          unit's copy to a user needs the org setting EnableOwnershipAcrossBusinessUnits (matrix business units).
          -AssignBillingRuleReader assigns the right copy; the admin UI offers the user's own unit's copy first, which
          reaches nothing above that unit.

    A config file without 'assignment' (or with 'team') declares a memberless owner team at Basic; 'assignment': 'users'
    declares a directly assigned role at Deep. No other combination is accepted, and no file may ask for Global.

    THE ROLES. Dataverse creates a role in the root business unit and copies it into every child unit; the privileges are
    the root role's, and a principal's depth is measured from the unit of the copy it holds. So each role is created once,
    in the root (a role is configuration, not a record). The customer unit's COPY is assigned to that unit's owner team, or,
    for 'Billing Rule Reader', to the billing admins. The root copy itself is assigned to nobody.

    THE ORG SETTING. With a config whose assignment is 'users', the run reads organization.orgdborgsettings and requires
    EnableOwnershipAcrossBusinessUnits = true. -Verify fails while it is off. -Apply sets it as its own, separately logged
    step (an org-wide write): it reads orgdborgsettings again immediately before the write and stops, writing nothing, if
    it differs from the read at the start of the run; it changes that one element, writes the rest back as read (from that
    fresh read), prints the old and new value, and reads it back to check that nothing else changed.

    MODES
      (default)  Dry run. Reads everything and prints the plan: the org setting, each declared team, the role, and the
                 users -AssignBillingRuleReader names. Zero writes.
      -Apply     First, the org setting (above), if it is off. Then, for each declared team: creates the team in the named
                 customer unit if it is missing (teamtype 0), creates its role in the root if it is missing, adds each
                 declared Read privilege at Basic, takes off every other privilege of THAT role (the platform injects some
                 on create and on every AddPrivilegesRole; setup guide SECURE-PROJECT-ENVIRONMENT-SETUP.md section 5.4),
                 assigns the customer unit's copy of the role to the team. For the directly assigned role: creates it in
                 the root with isinherited 0 if it is missing (or sets isinherited to 0), adds Read at Deep, takes off
                 every other privilege, and assigns the customer unit's copy to each user -AssignBillingRuleReader names.
                 Then it reads everything back and checks it as -Verify does. It never removes a member, moves a team,
                 takes a role off a principal or takes another role off a team: each of those is drift an owner decides,
                 and -Apply refuses before writing anything (the org setting included).
                 A dry run exits 0 with the plan, or exits 1 with the OWNER DECISION NEEDED summary when any drift is
                 one -Apply refuses (a member, another role, the wrong team type, a default team, a role assigned
                 elsewhere, a team holding the directly assigned role, its holder without the marker or homed outside the
                 customer's units, a user -AssignBillingRuleReader names who fails the same checks): such a plan cannot be
                 applied as it stands.
      -Verify    Read-only. Prints, per declared team, its business unit, its member count and each privilege its role
                 holds with the depth; for the directly assigned role, its isinherited, privileges and depths, every holder
                 with its home unit, and the org setting. Exits 1 and names each drift of the NAMED customer unit: the team
                 or role missing; the team not an Owner team or a default team; any member; a team of the name anywhere in
                 the root or Secure Record; any other role on the team; an owner role (or a copy) assigned to anything but a
                 team of its name in a customer top unit; a declared privilege missing or at another depth; any privilege
                 outside the file. For the directly assigned role also: the org setting off; isinherited not 0; any team
                 holding any copy; a user holding the root copy, the Secure Record copy or a copy below a customer top unit;
                 a holder of this unit's copy without the holderPrivilege (the billing-admin marker) or homed outside this
                 customer's top unit and the units below it. A holder of ANOTHER customer top unit's copy who is homed
                 outside that unit and the units below it is also HOLDER UNIT drift. Every OTHER direct child of the root
                 that lacks a team, or whose copy of the directly assigned role is held by users homed in that unit's own
                 subtree, is listed as information only (that unit's own run checks it). Exits 0 when the named unit has
                 no drift.

    REFUSED: a customer unit that is the root, that is the Secure Record unit, or that is not a direct child of the root;
    no business unit, or more than one, named -SecureBusinessUnitName (Secure Record exists by this step, H7b, so a name
    that matches nothing is a wrong name and would let the Secure Record unit pass as a customer unit); two config files that
    declare the same team or the same role; two owner configs that declare the same table (the directly assigned role's
    table may also be an owner team's: it reads, it never owns); -AssignBillingRuleReader with -Verify, or without exactly
    one directly assigned role among the configs.

    FAIL CLOSED: every read that fails stops the run (exit 1); nothing is decided on a partial read.

    Auth is the operator's own az CLI identity. No secrets in this script or the repository.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER CustomerBusinessUnitName
    The customer's top business unit, by name (exactly one match). Give this or -CustomerBusinessUnitId.

.PARAMETER CustomerBusinessUnitId
    The customer's top business unit, by id (provisioning's customerBusinessUnitId). Give this or -CustomerBusinessUnitName.

.PARAMETER SecureBusinessUnitName
    The Secure Record business unit (SecureRecord:BusinessUnitName), refused as a customer unit. Default 'Secure Record'.

.PARAMETER Apply
    Write mode. Without it the script never writes. Needs the owner's go for the environment.

.PARAMETER WhatIf
    Forces a dry run even when -Apply is passed.

.PARAMETER Verify
    Read-only check with a pass/fail exit code. Cannot be combined with -Apply or -AssignBillingRuleReader.

.PARAMETER AssignBillingRuleReader
    systemuserids of billing admins to assign the customer top unit's copy of 'Billing Rule Reader' to. Each must be homed
    in the customer's top unit or a unit below it and must hold the config's holderPrivilege (the billing-admin marker);
    otherwise the run refuses before writing anything. With -Apply it assigns; without it (or with -WhatIf) it prints the
    plan only.

.PARAMETER ConfigPaths
    The codified sets. Defaults to every file in $DeclaredRoleFiles under config/ at the repository root.

.EXAMPLE
    .\Set-CustomerOwnerTeams.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -CustomerBusinessUnitName 'Spaarke Business Unit 1'
    Dry run: the plan for the org setting, every declared team and the role, with no writes.

.EXAMPLE
    .\Set-CustomerOwnerTeams.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -CustomerBusinessUnitId <id> -AssignBillingRuleReader <systemuserid> -Apply
    Assigns the customer top unit's copy of 'Billing Rule Reader' to that billing admin (after the checks), then reads back.

.EXAMPLE
    .\Set-CustomerOwnerTeams.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -CustomerBusinessUnitId <id> -Verify
    Exit 0 = no drift in that unit; exit 1 = drift (each named) or a read failed.
#>

[CmdletBinding(DefaultParameterSetName = 'ByName')]
param(
    [Parameter(Mandatory)]
    [string]$EnvironmentUrl,

    [Parameter(Mandatory, ParameterSetName = 'ByName')]
    [string]$CustomerBusinessUnitName,

    [Parameter(Mandatory, ParameterSetName = 'ById')]
    [guid]$CustomerBusinessUnitId,

    [string]$SecureBusinessUnitName = 'Secure Record',

    [switch]$Apply,

    [switch]$WhatIf,

    [switch]$Verify,

    [guid[]]$AssignBillingRuleReader,

    [string[]]$ConfigPaths
)

# THE declared role files, one config file each. RestrictedContentOwnerRoleGuardTests requires this array (its uncommented
# entries) to EQUAL the guard's declared set: no declared file missing, no other file added.
$DeclaredRoleFiles = @(
    'restricted-content-owner-role.json',
    'customer-configuration-owner-role.json',
    'billing-rule-readers-role.json'
)

# 089d (option R): the org setting a directly assigned role from another unit's copy needs (matrix business units).
$CrossUnitOrgSetting = 'EnableOwnershipAcrossBusinessUnits'

$ErrorActionPreference = 'Stop'
if ($Apply -and $Verify) {
    Write-Host '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' -ForegroundColor Red
    exit 1
}
if ($Verify -and $AssignBillingRuleReader) {
    Write-Host '-Verify is read-only; -AssignBillingRuleReader is a dry run or -Apply.' -ForegroundColor Red
    exit 1
}
$IsDryRun = (-not $Apply.IsPresent) -or $WhatIf.IsPresent
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$Api = "$EnvironmentUrl/api/data/v9.2"
if (-not $ConfigPaths -or $ConfigPaths.Count -eq 0) {
    $ConfigPaths = @($DeclaredRoleFiles | ForEach-Object { Join-Path $PSScriptRoot '..' 'config' $_ })
}

function Read-Config([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Config not found: $Path" }
    $c = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($c.schemaVersion -ne 1) { throw "Unsupported schemaVersion '$($c.schemaVersion)' in $Path" }
    # 089d (option R): 'assignment' absent or 'team' = a memberless owner team (Read at Basic); 'users' = a role assigned
    # directly to users (Read at Deep, the customer top unit's copy), never to a team.
    $assignment = if ($null -eq $c.assignment) { 'team' } else { [string]$c.assignment }
    $c | Add-Member -NotePropertyName assignment -NotePropertyValue $assignment -Force
    if ($c.access -ne 'Read') {
        throw "Config $Path asks for access '$($c.access)'. Only Read is supported, by design."
    }
    foreach ($gone in 'membership', 'memberPrivilege') {
        if ($null -ne $c.$gone) { throw "Config $Path names '$gone'. No declared team has members (089d option R replaced the member team with a directly assigned role)." }
    }
    if ($assignment -eq 'team') {
        if ($c.depth -ne 'Basic') { throw "Config $Path declares a memberless owner team at depth '$($c.depth)'. Only Basic is supported, by design." }
        foreach ($field in 'holderPrivilege', 'isinherited', 'requiresOrgSetting') {
            if ($null -ne $c.$field) { throw "Config $Path declares an owner team with '$field' (a directly assigned role's field)." }
        }
        if ([string]::IsNullOrWhiteSpace($c.teamName)) { throw "Config $Path has no 'teamName'." }
    }
    elseif ($assignment -eq 'users') {
        if ($c.depth -ne 'Deep') {
            throw "Config $Path declares a directly assigned role at depth '$($c.depth)'. Only Deep is supported: Global and the root's copy reach Secure Record (D-123), Business Unit misses the child units."
        }
        if ($null -ne $c.teamName) { throw "Config $Path declares a directly assigned role with a teamName. No team holds it (089d option R)." }
        if ("$($c.isinherited)" -ne '0') { throw "Config $Path declares isinherited '$($c.isinherited)'. Only 0 (Team privileges only) is supported." }
        if ([string]::IsNullOrWhiteSpace($c.holderPrivilege)) { throw "Config $Path declares a directly assigned role with no holderPrivilege (the privilege every holder must hold)." }
        if ($c.requiresOrgSetting -ne $CrossUnitOrgSetting) { throw "Config $Path names requiresOrgSetting '$($c.requiresOrgSetting)'. A directly assigned role from the customer top unit's copy needs '$CrossUnitOrgSetting'." }
    }
    else { throw "Config $Path names assignment '$assignment'. Only 'team' (or absent) and 'users' are supported." }
    if ($c.businessUnit -ne 'customer') {
        throw "Config $Path names business unit '$($c.businessUnit)'. Only 'customer' (each customer's top business unit) is supported, by design (owner round 136)."
    }
    if ([string]::IsNullOrWhiteSpace($c.roleName)) { throw "Config $Path has no 'roleName'." }
    $t = @($c.tables)
    if ($t.Count -eq 0) { throw "Config lists no tables: $Path" }
    foreach ($key in 'logicalName', 'privilegeName') {
        $dupes = @($t | Group-Object $key | Where-Object Count -gt 1)
        if ($dupes.Count -gt 0) { throw "Config $Path lists a $key twice: $(($dupes | ForEach-Object Name) -join ', ')" }
    }
    foreach ($e in $t) {
        foreach ($field in 'logicalName', 'privilegeName', 'reason', 'evidence') {
            if ([string]::IsNullOrWhiteSpace($e.$field)) { throw "Config $Path entry '$($e.logicalName)' has no '$field'." }
        }
    }
    return $c
}

function Q([string]$Value) { $Value -replace "'", "''" }

# 089d: one element of organization.orgdborgsettings ($null when absent). Fail closed on XML that does not parse.
function Get-OrgSettingValue([string]$Xml, [string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Xml)) { return $null }
    $doc = [xml]$Xml
    $node = $doc.SelectSingleNode("/OrgSettings/$Name")
    if ($null -eq $node) { return $null }
    return $node.InnerText
}

# 089d: orgdborgsettings with ONE element set to $Value, every other element as read.
function Set-OrgSettingValue([string]$Xml, [string]$Name, [string]$Value) {
    $doc = if ([string]::IsNullOrWhiteSpace($Xml)) { [xml]'<OrgSettings />' } else { [xml]$Xml }
    if ($null -eq $doc.DocumentElement -or $doc.DocumentElement.Name -ne 'OrgSettings') { throw 'orgdborgsettings is not an <OrgSettings> document; nothing is written.' }
    $node = $doc.SelectSingleNode("/OrgSettings/$Name")
    if ($null -eq $node) { $node = $doc.DocumentElement.AppendChild($doc.CreateElement($Name)) }
    $node.InnerText = $Value
    return $doc.DocumentElement.OuterXml
}

# 089d: every element of orgdborgsettings as name -> value (to prove the write changed one element only).
function Get-OrgSettingMap([string]$Xml) {
    $map = [ordered]@{}
    if ([string]::IsNullOrWhiteSpace($Xml)) { return $map }
    foreach ($n in @(([xml]$Xml).DocumentElement.ChildNodes | Where-Object { $_.NodeType -eq 'Element' })) { $map[$n.Name] = $n.InnerText }
    return $map
}

try {
    # ── The declared roles ──────────────────────────────────────────────────────────────────────────────────
    $defs = @(foreach ($path in $ConfigPaths) {
            $c = Read-Config $path
            [pscustomobject]@{ Path = $path; File = (Split-Path $path -Leaf); Config = $c; Wanted = @(); WantedIds = @() }
        })
    $teamDefs = @($defs | Where-Object { $_.Config.assignment -eq 'team' })
    $userDefs = @($defs | Where-Object { $_.Config.assignment -eq 'users' })
    $dupes = @($teamDefs | Group-Object { $_.Config.teamName } | Where-Object Count -gt 1)
    if ($dupes.Count -gt 0) { throw "Two config files declare the same teamName '$($dupes[0].Name)'. Each team and role is declared once." }
    $dupes = @($defs | Group-Object { $_.Config.roleName } | Where-Object Count -gt 1)
    if ($dupes.Count -gt 0) { throw "Two config files declare the same roleName '$($dupes[0].Name)'. Each team and role is declared once." }
    # Two OWNER teams may not declare the same table (whose would its rows be?). A directly assigned role only reads, so its
    # table may also be an owner team's (089d: sprk_policy is the restricted-content team's too).
    $tableDupes = @($teamDefs |
        ForEach-Object { $d = $_; @($d.Config.tables) | ForEach-Object { [pscustomobject]@{ Table = $_.logicalName; File = $d.File } } } |
        Group-Object Table | Where-Object Count -gt 1)
    if ($tableDupes.Count -gt 0) { throw "A table is declared by two owner roles: $(($tableDupes | ForEach-Object { "$($_.Name) ($(($_.Group | ForEach-Object File) -join ', '))" }) -join '; ')." }
    if ($AssignBillingRuleReader -and $userDefs.Count -ne 1) {
        throw "-AssignBillingRuleReader needs exactly one directly assigned role among the configs; found $($userDefs.Count)."
    }

    $token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
    if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
    $headers = @{
        Authorization      = "Bearer $token"
        Accept             = 'application/json'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
        'Content-Type'     = 'application/json; charset=utf-8'
    }
    function Invoke-DvGet([string]$RelativePath) { Invoke-RestMethod -Uri "$Api/$RelativePath" -Headers $headers -Method Get }
    # Every page of a collection (fail closed: a page that cannot be read stops the run, never a partial answer).
    function Get-DvAll([string]$RelativePath) {
        $rows = [System.Collections.Generic.List[object]]::new()
        $page = Invoke-DvGet $RelativePath
        while ($true) {
            foreach ($r in @($page.value)) { $rows.Add($r) }
            $next = $page.'@odata.nextLink'
            if (-not $next) { break }
            $page = Invoke-RestMethod -Uri $next -Headers $headers -Method Get
        }
        return $rows.ToArray()
    }
    function Invoke-DvPost([string]$RelativePath, $Body) {
        $json = $Body | ConvertTo-Json -Depth 6
        Invoke-RestMethod -Uri "$Api/$RelativePath" -Headers $headers -Method Post -Body ([Text.Encoding]::UTF8.GetBytes($json))
    }
    function Invoke-DvPatch([string]$RelativePath, $Body) {
        $json = $Body | ConvertTo-Json -Depth 6
        Invoke-RestMethod -Uri "$Api/$RelativePath" -Headers ($headers + @{ 'If-Match' = '*' }) -Method Patch -Body ([Text.Encoding]::UTF8.GetBytes($json))
    }

    $org = @((Invoke-DvGet 'organizations?$select=organizationid,name,orgdborgsettings').value)[0]
    Write-Host "Environment : $EnvironmentUrl (org '$($org.name)')"
    foreach ($d in $defs) {
        $what = if ($d.Config.assignment -eq 'users') { "role '$($d.Config.roleName)', assigned to users" } else { "'$($d.Config.teamName)'" }
        Write-Host "Config      : $($d.Path) ($what, $(@($d.Config.tables).Count) tables)"
    }
    Write-Host ("Mode        : {0}" -f $(if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }))

    # ── The root, the Secure Record unit and the customer's top unit ─────────────────────────────────────────
    $units = @(Get-DvAll 'businessunits?$select=businessunitid,name,_parentbusinessunitid_value')
    $roots = @($units | Where-Object { -not $_._parentbusinessunitid_value })
    if ($roots.Count -ne 1) { throw "Expected exactly one root business unit; found $($roots.Count)." }
    $rootId = $roots[0].businessunitid
    Write-Host "Root BU     : $($roots[0].name) ($rootId)"

    # FAIL CLOSED (R4b-4): the Secure Record unit exists by this step (H7b), so a name that matches no unit is a wrong
    # -SecureBusinessUnitName (a non-default SecureRecord:BusinessUnitName) and would let Secure Record pass as a customer unit.
    $secureUnits = @($units | Where-Object { $_.name -eq $SecureBusinessUnitName })
    if ($secureUnits.Count -eq 0) { throw "No business unit is named '$SecureBusinessUnitName'. The Secure Record unit exists by this step (H7b); pass its name (SecureRecord:BusinessUnitName) as -SecureBusinessUnitName." }
    if ($secureUnits.Count -gt 1) { throw "More than one business unit is named '$SecureBusinessUnitName'; the Secure Record unit cannot be told apart." }
    $secureId = $secureUnits[0].businessunitid
    if (-not $secureId) { throw "The business unit named '$SecureBusinessUnitName' has no id." }
    Write-Host "Secure BU   : $SecureBusinessUnitName ($secureId)"

    $found = if ($PSCmdlet.ParameterSetName -eq 'ById') {
        @($units | Where-Object { $_.businessunitid -eq $CustomerBusinessUnitId.ToString() })
    }
    else { @($units | Where-Object { $_.name -eq $CustomerBusinessUnitName }) }
    $named = if ($PSCmdlet.ParameterSetName -eq 'ById') { "id $CustomerBusinessUnitId" } else { "'$CustomerBusinessUnitName'" }
    if ($found.Count -ne 1) { throw "Expected exactly one business unit $named; found $($found.Count)." }
    $customer = $found[0]
    $customerId = $customer.businessunitid
    if ($customerId -eq $rootId) { throw "Refused: $named is the ROOT business unit. The root holds only Spaarke product data; customer records go to the customer's own top unit (owner round 136)." }
    if ($customerId -eq $secureId) { throw "Refused: $named is the Secure Record business unit, never a customer unit." }
    if ($customer._parentbusinessunitid_value -ne $rootId) {
        throw "Refused: $named is not a direct child of the root (its parent is $($customer._parentbusinessunitid_value)). The teams live in the customer's TOP unit; the BFF walks up to it from any unit below."
    }
    Write-Host "Customer BU : $($customer.name) ($customerId)"

    # Every customer top unit: a direct child of the root that is not the Secure Record unit.
    $customerTops = @($units | Where-Object { $_._parentbusinessunitid_value -eq $rootId -and $_.businessunitid -ne $secureId })
    $customerTopIds = @($customerTops | ForEach-Object businessunitid)

    # 089d: the units no declared team may sit in: the root, Secure Record and every unit below Secure Record.
    $forbiddenUnits = @{ $rootId = 'the ROOT business unit'; $secureId = 'the Secure Record business unit' }
    $grew = $true
    while ($grew) {
        $grew = $false
        foreach ($u in $units) {
            if (-not $forbiddenUnits.ContainsKey($u.businessunitid) -and $u._parentbusinessunitid_value -and
                $u._parentbusinessunitid_value -ne $rootId -and $forbiddenUnits.ContainsKey($u._parentbusinessunitid_value)) {
                $forbiddenUnits[$u.businessunitid] = "'$($u.name)', below the Secure Record business unit"
                $grew = $true
            }
        }
    }

    # 089d: one unit's subtree (the unit and every unit below it), from the units already read (no extra call).
    function Get-UnitSubtree([string]$TopUnitId) {
        $tree = @{ $TopUnitId = $true }
        $grew = $true
        while ($grew) {
            $grew = $false
            foreach ($u in $units) {
                if (-not $tree.ContainsKey($u.businessunitid) -and $u._parentbusinessunitid_value -and $tree.ContainsKey($u._parentbusinessunitid_value)) {
                    $tree[$u.businessunitid] = $true
                    $grew = $true
                }
            }
        }
        return $tree
    }
    # The customer's own subtree: where a holder of the role is homed.
    $customerSubtree = Get-UnitSubtree $customerId
    # 089d R pass-2 K1: the tree must be the shape the isolation rests on. Secure Record sits directly under the root and outside
    # every customer subtree (D-123); the root is never inside one. Runs in every mode, before any write.
    $misplaced = @($customerSubtree.Keys | Where-Object { $forbiddenUnits.ContainsKey($_) })
    if ($misplaced.Count -gt 0) {
        throw "The business-unit tree is not the expected shape: $(($misplaced | ForEach-Object { $forbiddenUnits[$_] }) -join '; ') sits inside the subtree of $($customer.name) ($customerId). Secure Record must be a direct child of the root and outside every customer subtree (D-123). Nothing was written."
    }
    $unitName = @{}
    foreach ($u in $units) { $unitName[$u.businessunitid] = $u.name }

    # ── The org setting (089d option R): read always when a role is assigned to users ───────────────────────
    $orgSettingValue = $null
    $orgSettingOn = $true
    if ($userDefs.Count -gt 0) {
        if (-not $org.organizationid) { throw 'The organization row has no organizationid; the org setting cannot be read.' }
        $orgSettingValue = Get-OrgSettingValue ([string]$org.orgdborgsettings) $CrossUnitOrgSetting
        $orgSettingOn = "$orgSettingValue" -ieq 'true'
        $shown = if ($null -eq $orgSettingValue) { '(absent: off)' } else { "'$orgSettingValue'" }
        Write-Host "Org setting : $CrossUnitOrgSetting = $shown on organization '$($org.name)' ($($org.organizationid))"
    }
    $orgDrift = [System.Collections.Generic.List[string]]::new()
    if (-not $orgSettingOn) {
        $orgDrift.Add("ORG SETTING: $CrossUnitOrgSetting is $(if ($null -eq $orgSettingValue) { 'absent (off)' } else { "'$orgSettingValue'" }), expected true. Assigning the customer top unit's copy of a role to a user homed in a child unit needs it.")
    }

    # ── Each table's Read privilege, from METADATA ──────────────────────────────────────────────────────────
    foreach ($d in $defs) {
        $d.Wanted = @(foreach ($t in @($d.Config.tables)) {
                $privs = @((Invoke-DvGet "EntityDefinitions(LogicalName='$($t.logicalName)')/Privileges").value |
                    Where-Object { $_.PrivilegeType -eq 'Read' })
                if ($privs.Count -ne 1) { throw "Table '$($t.logicalName)': expected one Read privilege in metadata, found $($privs.Count)." }
                if ($privs[0].Name -cne $t.privilegeName) {
                    throw "Table '$($t.logicalName)' ($($d.File)): metadata names its Read privilege '$($privs[0].Name)', the config says '$($t.privilegeName)'. Fix the config (casing follows the schema name)."
                }
                [pscustomobject]@{ Table = $t.logicalName; Name = $privs[0].Name; Id = $privs[0].PrivilegeId.ToString().ToLowerInvariant() }
            })
        $d.WantedIds = @($d.Wanted | ForEach-Object Id)
    }

    # The privilege drift of one role against its file (shared by the owner roles and the directly assigned role).
    function Get-PrivilegeDrift($d, $held, $r) {
        $config = $d.Config
        $heldById = @{}
        foreach ($h in $held) { $heldById[$h.PrivilegeId.ToString().ToLowerInvariant()] = $h }
        foreach ($w in $d.Wanted) {
            $h = $heldById[$w.Id]
            if ($null -eq $h) { $r.Add("PRIVILEGE MISSING: $($w.Name) ($($w.Table)).") }
            elseif ($h.Depth -ne $config.depth) { $r.Add("PRIVILEGE DEPTH: $($w.Name) ($($w.Table)) at $($h.Depth), expected $($config.depth).") }
        }
        foreach ($o in @($held | Where-Object { $d.WantedIds -notcontains $_.PrivilegeId.ToString().ToLowerInvariant() } | Sort-Object PrivilegeName)) {
            $r.Add("PRIVILEGE OUTSIDE THE FILE: '$($config.roleName)' holds $($o.PrivilegeName) at $($o.Depth).")
        }
    }

    # Adds each declared privilege at the file's depth and takes every other privilege off the role (both kinds of role).
    function Set-RolePrivileges($d, [string]$roleId, $held) {
        $config = $d.Config
        $heldById = @{}
        foreach ($h in $held) { $heldById[$h.PrivilegeId.ToString().ToLowerInvariant()] = $h }
        $toAdd = @($d.Wanted | Where-Object { $null -eq $heldById[$_.Id] -or $heldById[$_.Id].Depth -ne $config.depth })
        if ($toAdd.Count -gt 0) {
            foreach ($w in @($toAdd | Where-Object { $null -ne $heldById[$_.Id] })) {
                # Setting a declared privilege to its declared depth: take it off, then add it at that depth below.
                Invoke-DvPost "roles($roleId)/Microsoft.Dynamics.CRM.RemovePrivilegeRole" @{
                    Privilege = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.privilege'; privilegeid = $w.Id }
                } | Out-Null
            }
            Invoke-DvPost "roles($roleId)/Microsoft.Dynamics.CRM.AddPrivilegesRole" @{
                Privileges = @($toAdd | ForEach-Object {
                        @{ '@odata.type' = 'Microsoft.Dynamics.CRM.RolePrivilege'; Depth = $config.depth; PrivilegeId = $_.Id; PrivilegeName = $_.Name; BusinessUnitId = $rootId }
                    })
            } | Out-Null
            Write-Host "ADDED at $($config.depth) to '$($config.roleName)': $(($toAdd | ForEach-Object Name) -join ', ')" -ForegroundColor Green
        }

        # After AddPrivilegesRole, which re-injects platform privileges: everything outside the file comes off this role.
        $now = @((Invoke-DvGet "RetrieveRolePrivilegesRole(RoleId=@p)?@p=$roleId").RolePrivileges)
        $outside = @($now | Where-Object { $d.WantedIds -notcontains $_.PrivilegeId.ToString().ToLowerInvariant() })
        foreach ($o in $outside) {
            Invoke-DvPost "roles($roleId)/Microsoft.Dynamics.CRM.RemovePrivilegeRole" @{
                Privilege = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.privilege'; privilegeid = $o.PrivilegeId.ToString() }
            } | Out-Null
        }
        if ($outside.Count -gt 0) { Write-Host "REMOVED from '$($config.roleName)' outside the file: $(($outside | ForEach-Object PrivilegeName) -join ', ')" -ForegroundColor Green }
    }

    # ── Read the current state of one declared owner team ───────────────────────────────────────────────────
    function Get-State($d) {
        $config = $d.Config
        $s = [ordered]@{ Team = $null; Members = @(); TeamRoles = @(); Role = $null; Copies = @(); Copy = $null; Assigned = @(); Held = @(); AllTeams = @() }
        # Every team of this name, wherever it is: the named unit's is checked; the others decide which assignments are allowed.
        $s.AllTeams = @(Get-DvAll "teams?`$select=teamid,name,teamtype,isdefault,_businessunitid_value&`$filter=name eq '$(Q $config.teamName)'")
        $teams = @($s.AllTeams | Where-Object { $_._businessunitid_value -eq $customerId })
        if ($teams.Count -gt 1) { throw "Expected at most one team named '$($config.teamName)' in $($customer.name); found $($teams.Count). Resolve it by hand (owner decision)." }
        if ($teams.Count -eq 1) {
            $s.Team = $teams[0]
            $s.Members = @(Get-DvAll "teams($($s.Team.teamid))/teammembership_association?`$select=systemuserid,fullname")
            $s.TeamRoles = @(Get-DvAll "teams($($s.Team.teamid))/teamroles_association?`$select=roleid,name")
        }

        $roles = @((Invoke-DvGet "roles?`$select=roleid,name,_parentrootroleid_value&`$filter=name eq '$(Q $config.roleName)' and _businessunitid_value eq $rootId").value)
        if ($roles.Count -gt 1) { throw "Expected at most one role named '$($config.roleName)' in the root business unit; found $($roles.Count)." }
        if ($roles.Count -eq 1) {
            $s.Role = $roles[0]
            $roleId = $s.Role.roleid
            # A role created in the root business unit is copied into every child unit; a team holds its own unit's copy.
            # Every copy's assignments count.
            $s.Copies = @(Get-DvAll "roles?`$select=roleid,_businessunitid_value&`$filter=_parentrootroleid_value eq $roleId")
            $s.Copy = @($s.Copies | Where-Object { $_._businessunitid_value -eq $customerId -and $_.roleid -ne $roleId }) | Select-Object -First 1
            $roleIds = @($roleId) + @($s.Copies | ForEach-Object roleid | Where-Object { $_ -ne $roleId })
            $s.Assigned = foreach ($id in ($roleIds | Select-Object -Unique)) {
                foreach ($tm in @(Get-DvAll "roles($id)/teamroles_association?`$select=teamid,name")) {
                    [pscustomobject]@{ Kind = 'team'; Id = $tm.teamid; Name = $tm.name; RoleId = $id }
                }
                foreach ($u in @(Get-DvAll "roles($id)/systemuserroles_association?`$select=systemuserid,fullname")) {
                    [pscustomobject]@{ Kind = 'user'; Id = $u.systemuserid; Name = $u.fullname; RoleId = $id }
                }
            }
            $s.Assigned = @($s.Assigned)
            $s.Held = @((Invoke-DvGet "RetrieveRolePrivilegesRole(RoleId=@p)?@p=$roleId").RolePrivileges)
        }
        return [pscustomobject]$s
    }

    function Get-Drift($d, $s) {
        $config = $d.Config
        $r = [System.Collections.Generic.List[string]]::new()
        # 089d: a team of this name in the root or in Secure Record (or below it) is drift wherever this run's unit is.
        foreach ($t in @($s.AllTeams | Where-Object { $forbiddenUnits.ContainsKey($_._businessunitid_value) })) {
            $r.Add("TEAM PLACE: a team named '$($config.teamName)' ($($t.teamid)) sits in $($forbiddenUnits[$t._businessunitid_value]); declared teams live only in a customer's top unit.")
        }
        if ($null -eq $s.Team) { $r.Add("TEAM MISSING: no team named '$($config.teamName)' in $($customer.name).") }
        else {
            if ($s.Team.teamtype -ne 0) { $r.Add("TEAM TYPE: '$($config.teamName)' is teamtype $($s.Team.teamtype), expected 0 (Owner).") }
            if ($s.Team.isdefault) { $r.Add("TEAM DEFAULT: '$($config.teamName)' is a business unit's default team.") }
            if ($s.Members.Count -gt 0) { $r.Add("TEAM MEMBERS: '$($config.teamName)' has $($s.Members.Count) member(s); it must have none.") }
            $roleIds = @(@($s.Role | ForEach-Object roleid) + @($s.Copies | ForEach-Object roleid))
            $otherRoles = @($s.TeamRoles | Where-Object { $roleIds -notcontains $_.roleid })
            foreach ($o in $otherRoles) { $r.Add("TEAM ROLE: '$($config.teamName)' holds another role '$($o.name)' ($($o.roleid)).") }
        }
        if ($null -eq $s.Role) { $r.Add("ROLE MISSING: no role named '$($config.roleName)' in the root business unit.") }
        else {
            if ($null -eq $s.Copy) { $r.Add("ROLE COPY MISSING: '$($config.roleName)' has no copy in $($customer.name).") }
            # Allowed: a team of this name, an Owner non-default team in a customer top unit, holding THAT unit's copy.
            $copyByUnit = @{}
            foreach ($c in $s.Copies) { $copyByUnit[$c._businessunitid_value] = $c.roleid }
            $allowed = @{}
            foreach ($tm in @($s.AllTeams | Where-Object { $_.teamtype -eq 0 -and -not $_.isdefault })) {
                if ($customerTopIds -contains $tm._businessunitid_value) {
                    $allowed["$($tm.teamid)|$($copyByUnit[$tm._businessunitid_value])"] = $true
                }
            }
            foreach ($a in @($s.Assigned | Where-Object { -not ($_.Kind -eq 'team' -and $allowed.ContainsKey("$($_.Id)|$($_.RoleId)")) })) {
                $r.Add("ROLE ASSIGNED ELSEWHERE: '$($config.roleName)' ($($a.RoleId)) is assigned to $($a.Kind) '$($a.Name)' ($($a.Id)).")
            }
            if ($s.Team -and $s.Copy -and -not @($s.Assigned | Where-Object { $_.Kind -eq 'team' -and $_.Id -eq $s.Team.teamid -and $_.RoleId -eq $s.Copy.roleid })) {
                $r.Add("ROLE NOT ASSIGNED: the $($customer.name) copy of '$($config.roleName)' is not assigned to '$($config.teamName)'.")
            }
            Get-PrivilegeDrift $d $s.Held $r
        }
        return , $r
    }

    function Write-State($d, $s) {
        $config = $d.Config
        Write-Host ''
        Write-Host "== $($config.teamName) ($($d.File))"
        Write-Host "Declared    : Read at $($config.depth) on $((@($config.tables) | ForEach-Object logicalName) -join ', '); memberless"
        if ($s.Team) {
            Write-Host "Team        : $($s.Team.name) ($($s.Team.teamid)) teamtype=$($s.Team.teamtype) isdefault=$($s.Team.isdefault) bu=$($customer.name) ($($s.Team._businessunitid_value)) members=$($s.Members.Count) roles=$($s.TeamRoles.Count)"
        }
        else { Write-Host "Team        : (none named '$($config.teamName)' in $($customer.name))" }
        if ($s.Role) {
            $copy = if ($s.Copy) { $s.Copy.roleid } else { '(none)' }
            Write-Host "Role        : $($s.Role.name) ($($s.Role.roleid)) customer-unit copy=$copy copies=$($s.Copies.Count) assignments=$($s.Assigned.Count) privileges=$($s.Held.Count)"
            foreach ($h in @($s.Held | Sort-Object PrivilegeName)) { Write-Host "Privilege   : $($h.PrivilegeName) at $($h.Depth)" }
        }
        else { Write-Host "Role        : (none named '$($config.roleName)' in the root business unit)" }
        # Information only: the other customer top units that do not have the team yet (each is set up by its own run).
        foreach ($u in @($customerTops | Where-Object { $_.businessunitid -ne $customerId } | Sort-Object name)) {
            $has = @($s.AllTeams | Where-Object { $_._businessunitid_value -eq $u.businessunitid -and $_.teamtype -eq 0 -and -not $_.isdefault })
            if ($has.Count -eq 0) { Write-Host "INFO        : customer top unit '$($u.name)' ($($u.businessunitid)) has no '$($config.teamName)' team (not this run's unit; not a failure)." }
        }
    }

    function Write-Drift($r) {
        if ($r.Count -eq 0) { Write-Host 'Drift       : none' -ForegroundColor Green }
        foreach ($line in $r) { Write-Host "DRIFT       : $line" -ForegroundColor Yellow }
    }

    # What -Apply would do for one declared owner team (the lines a dry run prints).
    function Get-Plan($d, $s) {
        $config = $d.Config
        $p = [System.Collections.Generic.List[string]]::new()
        if ($null -eq $s.Team) { $p.Add("create the Owner team '$($config.teamName)' in $($customer.name) ($customerId) (teamtype 0), with no members") }
        if ($null -eq $s.Role) { $p.Add("create the role '$($config.roleName)' in the root business unit (Dataverse copies it into every child unit; the root copy is assigned to nobody)") }
        $missing = @($d.Wanted | Where-Object { $w = $_; $null -eq $s.Role -or -not @($s.Held | Where-Object { $_.PrivilegeId.ToString().ToLowerInvariant() -eq $w.Id -and $_.Depth -eq $config.depth }) })
        if ($missing.Count -gt 0) { $p.Add("add Read at $($config.depth) to '$($config.roleName)': $(($missing | ForEach-Object Name) -join ', ')") }
        $p.Add("take off every privilege of '$($config.roleName)' outside $($d.File), including any the platform injects (setup guide 5.4)")
        if ($null -eq $s.Team -or $null -eq $s.Copy -or -not @($s.Assigned | Where-Object { $_.Kind -eq 'team' -and $_.Id -eq $s.Team.teamid -and $_.RoleId -eq $s.Copy.roleid })) {
            $p.Add("assign the $($customer.name) copy of '$($config.roleName)' to '$($config.teamName)'")
        }
        $p.Add("read '$($config.teamName)' back and check it as -Verify does")
        return , $p
    }

    function Invoke-ApplyTeam($d, $s) {
        $config = $d.Config
        $createdTeam = $false
        if ($null -eq $s.Team) {
            $me = (Invoke-DvGet 'WhoAmI').UserId
            Invoke-DvPost 'teams' @{
                name                         = $config.teamName
                teamtype                     = 0
                'businessunitid@odata.bind'  = "/businessunits($customerId)"
                'administratorid@odata.bind' = "/systemusers($me)"
                description                  = "Memberless owner team for the tables of config/$($d.File) in this customer business unit. MUST NEVER have a member or any other role."
            } | Out-Null
            $createdTeam = $true
            Write-Host "CREATED team '$($config.teamName)' in $($customer.name)." -ForegroundColor Green
        }
        if ($null -eq $s.Role) {
            Invoke-DvPost 'roles' @{
                name                        = $config.roleName
                'businessunitid@odata.bind' = "/businessunits($rootId)"
                description                 = "Read at Basic on the tables of config/$($d.File) only, so each customer unit's memberless $($config.teamName) team can own their rows. Its copies are assigned to those teams only. MUST NOT be granted to any user or any other team."
            } | Out-Null
            Write-Host "CREATED role '$($config.roleName)'." -ForegroundColor Green
        }

        $s = Get-State $d
        if ($null -eq $s.Team -or $null -eq $s.Role) { throw "'$($config.teamName)' or '$($config.roleName)' could not be read back after it was created." }
        if ($null -eq $s.Copy) { throw "'$($config.roleName)' has no copy in $($customer.name) to assign. Nothing further was written for it." }

        if ($createdTeam -and $s.Members.Count -gt 0) {
            # Only a team THIS run created: a member it has now was added by the create itself, never by a person.
            Invoke-DvPost "teams($($s.Team.teamid))/Microsoft.Dynamics.CRM.RemoveMembersTeam" @{
                Members = @($s.Members | ForEach-Object { @{ '@odata.type' = 'Microsoft.Dynamics.CRM.systemuser'; systemuserid = $_.systemuserid } })
            } | Out-Null
            Write-Host "REMOVED the $($s.Members.Count) member(s) the create added to '$($config.teamName)'." -ForegroundColor Green
        }

        Set-RolePrivileges $d $s.Role.roleid $s.Held

        if (-not @($s.Assigned | Where-Object { $_.Kind -eq 'team' -and $_.Id -eq $s.Team.teamid -and $_.RoleId -eq $s.Copy.roleid })) {
            $ref = @{ '@odata.id' = "$Api/roles($($s.Copy.roleid))" }
            Invoke-DvPost "teams($($s.Team.teamid))/teamroles_association/`$ref" $ref | Out-Null
            Write-Host "ASSIGNED the $($customer.name) copy of '$($config.roleName)' to '$($config.teamName)'." -ForegroundColor Green
        }
    }

    # ── 089d (option R): the directly assigned role ─────────────────────────────────────────────────────────
    # Where a copy of the role lives, as -Verify judges an assignment of it.
    function Get-CopyPlace([string]$UnitId) {
        if ($UnitId -eq $customerId) { return 'this' }
        if ($UnitId -eq $rootId) { return 'root' }
        if ($forbiddenUnits.ContainsKey($UnitId)) { return 'secure' }
        if ($customerTopIds -contains $UnitId) { return 'othertop' }
        return 'below'
    }

    # The subtree of another customer top unit, from the units already read (cached per run).
    $otherTopSubtrees = @{}
    function Get-OtherTopSubtree([string]$TopUnitId) {
        if (-not $otherTopSubtrees.ContainsKey($TopUnitId)) { $otherTopSubtrees[$TopUnitId] = Get-UnitSubtree $TopUnitId }
        return $otherTopSubtrees[$TopUnitId]
    }

    function Get-RoleState($d) {
        $config = $d.Config
        $s = [ordered]@{ Role = $null; Copies = @(); Copy = $null; TeamHolders = @(); UserHolders = @(); Holders = @(); HoldersWithout = @(); Held = @() }
        $roles = @((Invoke-DvGet "roles?`$select=roleid,name,isinherited,_parentrootroleid_value&`$filter=name eq '$(Q $config.roleName)' and _businessunitid_value eq $rootId").value)
        if ($roles.Count -gt 1) { throw "Expected at most one role named '$($config.roleName)' in the root business unit; found $($roles.Count)." }
        if ($roles.Count -eq 1) {
            $s.Role = $roles[0]
            $roleId = $s.Role.roleid
            $s.Copies = @(Get-DvAll "roles?`$select=roleid,isinherited,_businessunitid_value&`$filter=_parentrootroleid_value eq $roleId")
            if (-not @($s.Copies | Where-Object { $_.roleid -eq $roleId })) {
                $s.Copies = @([pscustomobject]@{ roleid = $roleId; isinherited = $s.Role.isinherited; _businessunitid_value = $rootId }) + $s.Copies
            }
            $s.Copy = @($s.Copies | Where-Object { $_._businessunitid_value -eq $customerId -and $_.roleid -ne $roleId }) | Select-Object -First 1
            # Every copy's holders: a team holding ANY copy is drift, and so is a user holding a copy outside a customer top unit.
            $s.TeamHolders = @(foreach ($c in $s.Copies) {
                    foreach ($tm in @(Get-DvAll "roles($($c.roleid))/teamroles_association?`$select=teamid,name,_businessunitid_value")) {
                        [pscustomobject]@{ Id = $tm.teamid; Name = $tm.name; RoleId = $c.roleid; CopyUnit = $c._businessunitid_value }
                    }
                })
            $s.UserHolders = @(foreach ($c in $s.Copies) {
                    foreach ($u in @(Get-DvAll "roles($($c.roleid))/systemuserroles_association?`$select=systemuserid,fullname,_businessunitid_value")) {
                        [pscustomobject]@{ Id = $u.systemuserid; Name = $u.fullname; Home = [string]$u._businessunitid_value; RoleId = $c.roleid; CopyUnit = $c._businessunitid_value }
                    }
                })
            $s.Holders = @($s.UserHolders | Where-Object { (Get-CopyPlace $_.CopyUnit) -eq 'this' })
            # Each holder of THIS unit's copy must hold the holderPrivilege (the billing-admin marker) through its own roles or teams.
            $s.HoldersWithout = @(foreach ($h in $s.Holders) {
                    $privs = @((Invoke-DvGet "systemusers($($h.Id))/Microsoft.Dynamics.CRM.RetrieveUserPrivileges()").RolePrivileges)
                    if (-not @($privs | Where-Object { $_.PrivilegeName -eq $config.holderPrivilege })) { $h }
                })
            $s.Held = @((Invoke-DvGet "RetrieveRolePrivilegesRole(RoleId=@p)?@p=$roleId").RolePrivileges)
        }
        return [pscustomobject]$s
    }

    # One condition for the plan and for -Apply: the root role or any copy has isinherited other than 0. Copies follow the
    # root, so the fix is one PATCH of the root role.
    function Test-InheritedDrift($s) {
        return ("$($s.Role.isinherited)" -ne '0') -or (@($s.Copies | Where-Object { "$($_.isinherited)" -ne '0' }).Count -gt 0)
    }

    function Get-RoleDrift($d, $s) {
        $config = $d.Config
        $r = [System.Collections.Generic.List[string]]::new()
        if ($null -eq $s.Role) { $r.Add("ROLE MISSING: no role named '$($config.roleName)' in the root business unit."); return , $r }
        # The root role's value is the role's; a copy is named only when it differs from a root role that is right.
        if ("$($s.Role.isinherited)" -ne '0') {
            $r.Add("ROLE INHERITED: '$($config.roleName)' ($($s.Role.roleid)) has isinherited '$($s.Role.isinherited)', expected 0 (Team privileges only).")
        }
        else {
            foreach ($c in @($s.Copies | Where-Object { "$($_.isinherited)" -ne '0' })) {
                $r.Add("ROLE INHERITED: the '$($unitName[[string]$c._businessunitid_value])' copy of '$($config.roleName)' ($($c.roleid)) has isinherited '$($c.isinherited)', expected 0 (Team privileges only).")
            }
        }
        if ($null -eq $s.Copy) { $r.Add("ROLE COPY MISSING: '$($config.roleName)' has no copy in $($customer.name).") }
        foreach ($t in $s.TeamHolders) {
            $r.Add("ROLE HELD BY TEAM: team '$($t.Name)' ($($t.Id)) holds the '$($unitName[[string]$t.CopyUnit])' copy of '$($config.roleName)' ($($t.RoleId)); no team may hold it.")
        }
        foreach ($u in $s.UserHolders) {
            switch (Get-CopyPlace $u.CopyUnit) {
                'root' { $r.Add("ROLE ASSIGNED ELSEWHERE: user '$($u.Name)' ($($u.Id)) holds the ROOT copy of '$($config.roleName)' ($($u.RoleId)); Deep from the root reaches Secure Record. The root copy is assigned to nobody.") }
                'secure' { $r.Add("ROLE ASSIGNED ELSEWHERE: user '$($u.Name)' ($($u.Id)) holds the '$($unitName[[string]$u.CopyUnit])' copy of '$($config.roleName)' ($($u.RoleId)), in or below Secure Record.") }
                'below' { $r.Add("ROLE ASSIGNED ELSEWHERE: user '$($u.Name)' ($($u.Id)) holds the '$($unitName[[string]$u.CopyUnit])' copy of '$($config.roleName)' ($($u.RoleId)), a unit below a customer top unit; it reaches nothing above that unit. Assign the customer top unit's copy (-AssignBillingRuleReader).") }
                'othertop' {
                    # Another customer's copy is that customer's to check, but a holder homed outside THAT unit's subtree
                    # (for example one of this customer's users) reads that customer's rules: drift here too.
                    if (-not (Get-OtherTopSubtree ([string]$u.CopyUnit)).ContainsKey([string]$u.Home)) {
                        $r.Add("HOLDER UNIT: user '$($u.Name)' ($($u.Id)) holds the '$($unitName[[string]$u.CopyUnit])' copy of '$($config.roleName)' ($($u.RoleId)), another customer top unit's, but is homed in '$($unitName[[string]$u.Home])' ($($u.Home)), outside that unit and the units below it.")
                    }
                }
            }
        }
        foreach ($h in $s.HoldersWithout) {
            $r.Add("HOLDER PRIVILEGE: '$($config.roleName)' holder '$($h.Name)' ($($h.Id)) does not hold $($config.holderPrivilege); only billing admins may hold it.")
        }
        # A holder homed outside this customer's units (another customer's unit, the root, Secure Record) would read this
        # customer's rules.
        foreach ($h in @($s.Holders | Where-Object { -not $customerSubtree.ContainsKey($_.Home) })) {
            $r.Add("HOLDER UNIT: '$($config.roleName)' holder '$($h.Name)' ($($h.Id)) is homed in '$($unitName[$h.Home])' ($($h.Home)), outside $($customer.name) and the units below it.")
        }
        Get-PrivilegeDrift $d $s.Held $r
        return , $r
    }

    function Write-RoleState($d, $s) {
        $config = $d.Config
        Write-Host ''
        Write-Host "== $($config.roleName) ($($d.File))"
        Write-Host "Declared    : Read at $($config.depth) on $((@($config.tables) | ForEach-Object logicalName) -join ', '); isinherited $($config.isinherited); assigned to users only (no team), the $($customer.name) copy, each holder holding $($config.holderPrivilege) and homed in $($customer.name) or below"
        if ($s.Role) {
            $copy = if ($s.Copy) { $s.Copy.roleid } else { '(none)' }
            Write-Host "Role        : $($s.Role.name) ($($s.Role.roleid)) isinherited=$($s.Role.isinherited) customer-unit copy=$copy copies=$($s.Copies.Count) privileges=$($s.Held.Count)"
            foreach ($h in @($s.Held | Sort-Object PrivilegeName)) { Write-Host "Privilege   : $($h.PrivilegeName) at $($h.Depth)" }
            Write-Host "Holders     : $($s.Holders.Count) of the $($customer.name) copy; team holders (any copy): $($s.TeamHolders.Count); root-copy holders: $(@($s.UserHolders | Where-Object { (Get-CopyPlace $_.CopyUnit) -eq 'root' }).Count)"
            foreach ($h in $s.Holders) {
                $marker = if (@($s.HoldersWithout | Where-Object Id -eq $h.Id)) { 'no' } else { 'yes' }
                Write-Host "Holder      : '$($h.Name)' ($($h.Id)) homed in '$($unitName[$h.Home])' ($($h.Home)); $($config.holderPrivilege): $marker"
            }
            # Another customer top unit's copy, held by users homed in THAT unit's subtree: that unit's own run checks them.
            # A holder homed anywhere else is HOLDER UNIT drift (Get-RoleDrift), not information.
            foreach ($g in @($s.UserHolders | Where-Object { (Get-CopyPlace $_.CopyUnit) -eq 'othertop' -and (Get-OtherTopSubtree ([string]$_.CopyUnit)).ContainsKey([string]$_.Home) } | Group-Object CopyUnit)) {
                Write-Host "INFO        : $($g.Count) user(s) hold the '$($unitName[[string]$g.Name])' copy, homed in that unit or below (another customer top unit; that unit's own run checks them, not a failure here)."
            }
        }
        else {
            Write-Host "Role        : (none named '$($config.roleName)' in the root business unit)"
            Write-Host 'Holders     : none (no copy of the role exists, so no user and no team holds one)'
        }
    }

    function Get-RolePlan($d, $s, $candidates) {
        $config = $d.Config
        $candidates = @($candidates | Where-Object { $null -ne $_ })
        $p = [System.Collections.Generic.List[string]]::new()
        if ($null -eq $s.Role) {
            $p.Add("create the role '$($config.roleName)' in the root business unit with isinherited 0 (Dataverse copies it into every child unit; the root copy is assigned to nobody)")
        }
        elseif (Test-InheritedDrift $s) { $p.Add("set isinherited to 0 (Team privileges only) on '$($config.roleName)'") }
        $missing = @($d.Wanted | Where-Object { $w = $_; $null -eq $s.Role -or -not @($s.Held | Where-Object { $_.PrivilegeId.ToString().ToLowerInvariant() -eq $w.Id -and $_.Depth -eq $config.depth }) })
        if ($missing.Count -gt 0) { $p.Add("add Read at $($config.depth) to '$($config.roleName)': $(($missing | ForEach-Object Name) -join ', ')") }
        $p.Add("take off every privilege of '$($config.roleName)' outside $($d.File), including any the platform injects (setup guide 5.4)")
        foreach ($c in @($candidates | Where-Object { -not $_.Refusal -and -not $_.AlreadyHolds })) {
            $p.Add("assign the $($customer.name) copy of '$($config.roleName)' to user '$($c.Name)' ($($c.Id)), homed in '$($unitName[$c.Home])', holding $($config.holderPrivilege)")
        }
        foreach ($c in @($candidates | Where-Object { $_.AlreadyHolds })) { $p.Add("(nothing to do) user '$($c.Name)' ($($c.Id)) already holds the $($customer.name) copy") }
        $p.Add("read '$($config.roleName)' back and check it as -Verify does")
        if ($candidates.Count -eq 0) {
            $p.Add("(per billing admin, later) for each of $($customer.name)'s child-unit billing admins, re-run with -AssignBillingRuleReader <systemuserid> (dry run, then -Apply), then -Verify. Do not assign it in the admin UI: that offers the copy in the user's own unit")
        }
        return , $p
    }

    # The users -AssignBillingRuleReader names, each checked as -Verify checks a holder (fail closed: a user not found stops the run).
    function Get-AssignCandidates($d, $s) {
        $config = $d.Config
        return @(foreach ($id in @($AssignBillingRuleReader | Select-Object -Unique)) {
                $u = Invoke-DvGet "systemusers($id)?`$select=systemuserid,fullname,_businessunitid_value"
                $homeUnit = [string]$u._businessunitid_value
                $privs = @((Invoke-DvGet "systemusers($id)/Microsoft.Dynamics.CRM.RetrieveUserPrivileges()").RolePrivileges)
                $refusal = if (-not $customerSubtree.ContainsKey($homeUnit)) {
                    "ASSIGN REFUSED: user '$($u.fullname)' ($id) is homed in '$($unitName[$homeUnit])' ($homeUnit), outside $($customer.name) and the units below it."
                }
                elseif (-not @($privs | Where-Object { $_.PrivilegeName -eq $config.holderPrivilege })) {
                    "ASSIGN REFUSED: user '$($u.fullname)' ($id) does not hold $($config.holderPrivilege); only billing admins may hold '$($config.roleName)'."
                }
                else { $null }
                $already = $null -ne $s.Copy -and @($s.Holders | Where-Object { $_.Id -eq $u.systemuserid }).Count -gt 0
                [pscustomobject]@{ Id = $u.systemuserid; Name = $u.fullname; Home = $homeUnit; Refusal = $refusal; AlreadyHolds = $already }
            })
    }

    function Invoke-ApplyRole($d, $s, $candidates) {
        $config = $d.Config
        if ($null -eq $s.Role) {
            Invoke-DvPost 'roles' @{
                name                        = $config.roleName
                'businessunitid@odata.bind' = "/businessunits($rootId)"
                isinherited                 = 0
                description                 = "Read at $($config.depth) on the tables of config/$($d.File) only. Assigned DIRECTLY to billing admins, and only the copy in each customer's top business unit (scripts/Set-CustomerOwnerTeams.ps1 -AssignBillingRuleReader). MUST NOT be assigned to any team, and its root copy to nobody."
            } | Out-Null
            Write-Host "CREATED role '$($config.roleName)' (isinherited 0)." -ForegroundColor Green
            $s = Get-RoleState $d
            if ($null -eq $s.Role) { throw "'$($config.roleName)' could not be read back after it was created." }
        }
        $roleId = $s.Role.roleid
        if (Test-InheritedDrift $s) {
            Invoke-DvPatch "roles($roleId)" @{ isinherited = 0 } | Out-Null
            Write-Host "SET isinherited 0 (Team privileges only) on '$($config.roleName)' ($roleId)." -ForegroundColor Green
        }
        Set-RolePrivileges $d $roleId $s.Held
        $s = Get-RoleState $d
        if ($null -eq $s.Copy) { throw "'$($config.roleName)' has no copy in $($customer.name) to assign. Nothing further was written for it." }
        foreach ($c in @($candidates | Where-Object { $null -ne $_ -and -not $_.Refusal -and -not @($s.Holders | Where-Object Id -eq $_.Id) })) {
            $ref = @{ '@odata.id' = "$Api/roles($($s.Copy.roleid))" }
            Invoke-DvPost "systemusers($($c.Id))/systemuserroles_association/`$ref" $ref | Out-Null
            Write-Host "ASSIGNED the $($customer.name) copy of '$($config.roleName)' ($($s.Copy.roleid)) to user '$($c.Name)' ($($c.Id))." -ForegroundColor Green
        }
    }

    # ── Every declared team's and role's state and drift ────────────────────────────────────────────────────
    if ($orgDrift.Count -gt 0) { Write-Host ''; Write-Host '== Org setting'; Write-Drift $orgDrift }
    $results = @(foreach ($d in $teamDefs) {
            $s = Get-State $d
            $r = Get-Drift $d $s
            Write-State $d $s
            Write-Drift $r
            [pscustomobject]@{ Def = $d; State = $s; Drift = $r; Kind = 'team'; Candidates = @() }
        })
    $results += @(foreach ($d in $userDefs) {
            $s = Get-RoleState $d
            $r = Get-RoleDrift $d $s
            Write-RoleState $d $s
            Write-Drift $r
            $candidates = @()
            if ($AssignBillingRuleReader) { $candidates = @(Get-AssignCandidates $d $s) }
            foreach ($c in $candidates) {
                $line = if ($c.Refusal) { $c.Refusal } elseif ($c.AlreadyHolds) { "already holds the $($customer.name) copy" } else { 'checks pass (homed in this customer, holds the marker)' }
                Write-Host "Assign      : '$($c.Name)' ($($c.Id)) homed in '$($unitName[$c.Home])': $line" -ForegroundColor $(if ($c.Refusal) { 'Red' } else { 'Gray' })
            }
            [pscustomobject]@{ Def = $d; State = $s; Drift = $r; Kind = 'users'; Candidates = $candidates }
        })
    $driftCount = $orgDrift.Count + ($results | ForEach-Object { $_.Drift.Count } | Measure-Object -Sum).Sum
    $assignRefusals = @($results | ForEach-Object { $_.Candidates } | Where-Object { $_.Refusal } | ForEach-Object Refusal)
    $assignPending = @($results | ForEach-Object { $_.Candidates } | Where-Object { -not $_.Refusal -and -not $_.AlreadyHolds })

    # ── Verify ──────────────────────────────────────────────────────────────────────────────────────────────
    if ($Verify) {
        if ($driftCount -eq 0) {
            $pass = @()
            if ($teamDefs.Count -gt 0) { $pass += "each declared owner team is memberless and holds only that unit's copy of its role, which holds Read at Basic on exactly its declared tables; no declared team sits in the root or Secure Record." }
            if ($userDefs.Count -gt 0) { $pass += "$CrossUnitOrgSetting is on; each directly assigned role has isinherited 0 and holds Read at its declared depth on exactly its declared tables; no team holds it; its root copy is assigned to nobody; every holder of the $($customer.name) copy holds its holderPrivilege and is homed in $($customer.name) or below; every holder of another customer top unit's copy is homed in that unit or below." }
            Write-Host "`nVERIFY PASS: in $($customer.name) $($pass -join ' ')" -ForegroundColor Green
            exit 0
        }
        Write-Host "`nVERIFY FAIL: $driftCount drift finding(s) above." -ForegroundColor Red
        exit 1
    }

    # ── What -Apply may and may not change ──────────────────────────────────────────────────────────────────
    # Owner decisions, never changed here: refuse before anything is written (the org setting included).
    $ownerOnly = @(@($results | ForEach-Object { $_.Drift } | Where-Object { $_ -match '^(TEAM PLACE|TEAM TYPE|TEAM DEFAULT|TEAM MEMBERS|TEAM ROLE|ROLE ASSIGNED ELSEWHERE|ROLE HELD BY TEAM|HOLDER PRIVILEGE|HOLDER UNIT):' }) + $assignRefusals)
    Write-Host ''
    if ($ownerOnly.Count -gt 0) {
        Write-Host "OWNER DECISION NEEDED: -Apply never changes these; it refuses while they stand:" -ForegroundColor Red
        foreach ($line in $ownerOnly) { Write-Host "  $line" -ForegroundColor Red }
    }
    if ($driftCount -eq 0 -and $assignPending.Count -eq 0 -and $ownerOnly.Count -eq 0) { Write-Host 'Nothing to do.' -ForegroundColor Green; exit 0 }
    $step = 0
    Write-Host ("PLAN ({0}):" -f $(if ($IsDryRun) { 'dry run, nothing written' } else { 'applying' })) -ForegroundColor Cyan
    if ($orgDrift.Count -gt 0) {
        $step++
        Write-Host ("  {0}. ORG-WIDE WRITE, its own step: set {1} to true in organization '{2}' ({3}).orgdborgsettings (now {4}); every other setting is written back as read" -f $step, $CrossUnitOrgSetting, $org.name, $org.organizationid, $(if ($null -eq $orgSettingValue) { 'absent' } else { "'$orgSettingValue'" })) -ForegroundColor Cyan
    }
    foreach ($x in @($results | Where-Object { $_.Drift.Count -gt 0 -or @($_.Candidates).Count -gt 0 })) {
        $lines = if ($x.Kind -eq 'users') { Get-RolePlan $x.Def $x.State $x.Candidates } else { Get-Plan $x.Def $x.State }
        foreach ($line in $lines) { $step++; Write-Host ("  {0}. {1}" -f $step, $line) -ForegroundColor Cyan }
    }
    if ($userDefs.Count -gt 0) {
        Write-Host "  Not changed: nothing is written in the root business unit except a role definition the plan creates there (Dataverse creates roles in the root; its root copy is assigned to nobody), and nothing in $SecureBusinessUnitName." -ForegroundColor Cyan
    }
    if ($IsDryRun) {
        if ($ownerOnly.Count -gt 0) {
            # R4b-4 (B-K4): a plan -Apply refuses is not a plan to run; the dry run fails with the owner-decision summary.
            Write-Host "`nDRY RUN: no writes. OWNER DECISION NEEDED: $($ownerOnly.Count) finding(s) above; -Apply refuses until an owner settles them. Exit 1." -ForegroundColor Red
            exit 1
        }
        Write-Host "`nDRY RUN: no writes. Re-run with -Apply (owner go), then -Verify." -ForegroundColor Cyan
        exit 0
    }
    if ($ownerOnly.Count -gt 0) { throw 'Refused: the drift above is an owner decision. Nothing was written.' }

    # ── Apply ───────────────────────────────────────────────────────────────────────────────────────────────
    if ($orgDrift.Count -gt 0) {
        # 089d (option R): an ORG-WIDE write, logged as its own step. One element changes; the rest is written back as read.
        # The first read is from the start of the run, before every other read. Read again immediately before the write, and
        # stop if anyone changed it in between. The new document and the read-back comparison are built from the fresh read.
        $fresh = @((Invoke-DvGet 'organizations?$select=organizationid,name,orgdborgsettings').value)[0]
        if ([string]$fresh.organizationid -ne [string]$org.organizationid) { throw 'The organization row read again before the org-setting write is a different row; nothing was written.' }
        $before = [string]$fresh.orgdborgsettings
        if ($before -cne [string]$org.orgdborgsettings) {
            throw "orgdborgsettings changed since this run first read it (someone else wrote it during the run). Nothing was written, the org setting included; re-run to plan from the current value."
        }
        $after = Set-OrgSettingValue $before $CrossUnitOrgSetting 'true'
        Write-Host "ORG SETTING : writing organization '$($org.name)' ($($org.organizationid)).orgdborgsettings: $CrossUnitOrgSetting $(if ($null -eq $orgSettingValue) { '(absent)' } else { "'$orgSettingValue'" }) -> 'true'." -ForegroundColor Yellow
        Invoke-DvPatch "organizations($($org.organizationid))" @{ orgdborgsettings = $after } | Out-Null
        $readBack = [string]@((Invoke-DvGet 'organizations?$select=organizationid,name,orgdborgsettings').value)[0].orgdborgsettings
        $was = Get-OrgSettingMap $before
        $now = Get-OrgSettingMap $readBack
        if ("$($now[$CrossUnitOrgSetting])" -ine 'true') { throw "$CrossUnitOrgSetting reads back as '$($now[$CrossUnitOrgSetting])' after the write; nothing further was written." }
        $changed = @(@($was.Keys) + @($now.Keys) | Select-Object -Unique | Where-Object { $_ -ne $CrossUnitOrgSetting -and "$($was[$_])" -cne "$($now[$_])" })
        if ($changed.Count -gt 0) { throw "orgdborgsettings changed beyond $CrossUnitOrgSetting after the write: $($changed -join ', '). Nothing further was written; an owner must look at it." }
        Write-Host "SET $CrossUnitOrgSetting = true (read back; no other orgdborgsettings element changed)." -ForegroundColor Green
    }
    foreach ($x in @($results | Where-Object { $_.Kind -eq 'team' -and $_.Drift.Count -gt 0 })) { Invoke-ApplyTeam $x.Def $x.State }
    foreach ($x in @($results | Where-Object { $_.Kind -eq 'users' -and ($_.Drift.Count -gt 0 -or @($_.Candidates).Count -gt 0) })) { Invoke-ApplyRole $x.Def $x.State $x.Candidates }

    $left = 0
    if ($userDefs.Count -gt 0) {
        $v = Get-OrgSettingValue ([string]@((Invoke-DvGet 'organizations?$select=organizationid,name,orgdborgsettings').value)[0].orgdborgsettings) $CrossUnitOrgSetting
        if ("$v" -ine 'true') { Write-Host "DRIFT       : ORG SETTING: $CrossUnitOrgSetting reads '$v'." -ForegroundColor Red; $left++ }
    }
    foreach ($d in $teamDefs) {
        $s = Get-State $d
        Write-State $d $s
        $r = Get-Drift $d $s
        foreach ($line in $r) { Write-Host "DRIFT       : $line" -ForegroundColor Red }
        $left += $r.Count
    }
    foreach ($d in $userDefs) {
        $s = Get-RoleState $d
        Write-RoleState $d $s
        $r = Get-RoleDrift $d $s
        foreach ($line in $r) { Write-Host "DRIFT       : $line" -ForegroundColor Red }
        foreach ($id in @($AssignBillingRuleReader)) {
            if ($id -and -not @($s.Holders | Where-Object { $_.Id -eq $id.ToString() })) { Write-Host "DRIFT       : user $id does not hold the $($customer.name) copy after the assignment." -ForegroundColor Red; $left++ }
        }
        $left += $r.Count
    }
    if ($left -gt 0) { throw "Read-back still shows $left drift finding(s); the setup is not complete." }
    Write-Host "`nAPPLIED and read back: no drift. Dataverse caches principal privileges; re-run -Verify and probe across 3 polls (setup guide section 7)." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host "FAILED (fail closed, exit 1): $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
