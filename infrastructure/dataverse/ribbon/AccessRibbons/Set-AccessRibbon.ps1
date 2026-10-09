<#
.SYNOPSIS
    The "Access" group on the project, matter and work-assignment main forms - dry run (default), -Apply, -Verify -
    including task 150's Make Secure / Remove Secure.

.DESCRIPTION
    unified-access-control-r2 task 150 (UX amendment; round 26 item 2(d)). Task 142 owns the ONE Access group
    (access-group.template.xml), its ONE per-entity generator (Merge-AccessRibbon.ps1) and the ONE command script
    (sprk_/scripts/access_ribbon.js). Task 142 left the import as manual README steps; this script runs those steps for
    the three entities with the same before/after command-list check, so the change is one dry run, one apply and one
    verify, like every other schema step in this project.

      DRY RUN (default; nothing written outside -WorkDir): merges the template into 142's CHECKED-IN
        exports - ProjectRibbons/Entities/sprk_Project/RibbonDiff.xml, MatterRibbons/Entities/sprk_Matter/RibbonDiff.xml
        and, once checked in, WorkAssignmentRibbons/Entities/sprk_workassignment/RibbonDiff.xml - and prints, per
        entity, the commands before and after and the Access menu. FAILS if a command would be lost, or if the menu is
        not exactly the expected one. Without -EnvironmentUrl it makes no Dataverse call and merges the Share stand-ins
        in fixtures/; WITH -EnvironmentUrl it reads the platform's Share commands from that environment's live
        effective ribbons (RetrieveEntityRibbon - read-only, exactly what -Apply reads) and merges THEM, so a Share
        button the live ribbon does not have fails the dry run as it would fail -Apply.

      -Apply (LIVE WRITE - the main session runs it): records the LIVE command list of each main form (before), exports
        the dedicated ribbon solution -SolutionName (never SpaarkeCore - the ribbon-edit skill), unpacks it, checks the
        work-assignment RibbonDiff.xml into WorkAssignmentRibbons/Entities/sprk_workassignment/ BEFORE editing it (task
        142 UX (f)), merges each entity with Merge-AccessRibbon.ps1, packs, imports with publish, then runs -Verify
        against the recorded before-list. THE EFFECTIVE RIBBON LAGS THE PUBLISH: on spaarkedev1 (2026-10-07) the verify
        run right after "Published All Customizations" reported partial FAILs that differed per entity, and a read-only
        -Verify 75 s later PASSED on all three. So -Apply retries its verify with a bounded backoff (15, 30, 45, 60 and
        30 s - about 3 minutes in all) and reports VERIFY FAILED only when the last attempt still fails. The read-only
        -Verify mode does not retry: run it again after a few minutes if it is run straight after an import.

      -Verify (read-only): reads each main form's EFFECTIVE ribbon (RetrieveEntityRibbon, Form) and checks that every
        command of the before-list is still present, that Update Access and Remove Secure are present with their
        access_ribbon.js functions, that Make Secure is present exactly when -SecureTransitionDeployed is given, and
        (task 114) that every platform form Share command carries sprk.Access.<entity>.ShareAllowed.EnableRule (calling
        access_ribbon.js isShareAllowed) and every grid / subgrid Share command carries
        sprk.Access.<entity>.ShareAllowedSelection.EnableRule (calling isShareAllowedForSelection). Exit 0 = PASS.

    THE SHARE COMMAND ON RESTRICTED RECORDS (task 114, owner round 67 amendment 4(a)). The platform's own form Share
    command is hidden on a Restricted record (sprk_accesspermission = Restricted), so sharing goes through Manage Access
    "+ User", which refuses a user flagged external there. The command is the PLATFORM's: -Apply reads it from the live
    effective ribbon (the Command of each Share BUTTON below - the command is never assumed) and Merge-AccessRibbon.ps1
    appends the rule to that copy, keeping every platform rule. The dry run without -EnvironmentUrl uses
    fixtures/share-command.dry-run-sample.xml, a stand-in that only exercises the transformation. Needs
    access_ribbon.js 1.6.0 (isShareAllowed) published first.

    WHERE THE PLATFORM'S SHARE IS (read from spaarkedev1's live effective ribbons, 2026-10-07, all three entities; there
    is NO Mscrm.Form.<entity>.Share or Mscrm.HomepageGrid.<entity>.Share button - the first version assumed those ids):
      form  Mscrm.Form.<entity>.Permissions.Sharing            -> Mscrm.SharePrimaryRecordRefresh (display rule
            Mscrm.HideInLegacyRibbon: THE Unified Interface command-bar Share; required)
      form  Mscrm.Form.<entity>.Permissions.SharingNonRefresh  -> Mscrm.SharePrimaryRecord (inside the Permissions
            flyout, whose command is Mscrm.HideOnModern: legacy client only; ruled too when present)
      grid  Mscrm.HomepageGrid.<entity>.Sharing                -> Mscrm.ShareSelectedRecord (required)
      grid  Mscrm.SubGrid.<entity>.Sharing                     -> Mscrm.ShareSelectedRecord (when present)
    Not ruled: the Permissions.Grant* buttons (column-security "secured fields" sharing, not record access) and
    Chart.Share (a chart). Mscrm.SharePrimaryRecordRefresh also carries the platform's Mscrm.CollabNotEnabled rule
    (XrmCore.Commands.Share.showLegacyShareAndEmailALink): where the platform's collaboration Share is on, the platform
    itself disables this button - that experience is not a ribbon command and cannot be ruled through RibbonDiff, and
    the server-side RestrictedExternalShareRemover is the backstop either way.

    MAKE SECURE IS RELEASE-GATED (acceptance (b); owner R3b / F7). The confirmation the user accepts says the record's
    related records AND its files follow it, so it ships only where all of this is deployed, in the same release: task
    148's provisioning transition (merged with task 150 in integ/uac-r2-batch4), round 26 item 3's file relocation (task
    166's DocumentContainerRelocator, wired into provisioning's Make Secure path when 166 merges), and that relocation's
    scheduled backstop (round 46 item 2: task 147's SecureChildReconciliationJob, every 2 minutes, also settling the
    pending Make Secure relocations, registered with its writes on - wired at integration). Pass
    -SecureTransitionDeployed only when the target environment's BFF carries all three. Without it Make Secure is
    withheld (and -Verify FAILS if it is present), while Update Access and Remove Secure ship.

    Order (README "Deployment"; release order round 60 item 2): the BFF first; then the DEFAULT-TEAM part of task 144's
    migration (scripts/Migrate-SecureRecordsToNamedOwnerTeam.ps1 dry run, then -Apply - a live gate before this script,
    task 150 round 53 item 2: access_ribbon.js 1.5.0 hides Make Secure on a record the retired default team owns, which
    is isolated already, and only that migration moves it). That part is complete when the dry run's plan has no
    MIGRATE rows and the retired default team no longer holds the Secure Record Owner role. Then the web resources
    (sprk_/scripts/access_ribbon.js 1.5.0, assignedaccess_postsave.js, bff_auth.js); then this script. The migration's
    full -Verify (exit 0) runs AFTER this script: it also fails on NOT-ISOLATED rows (user-owned legacy records, records
    owned by a team outside the Secure Record business unit, flagged records left before the owner move), and Make
    Secure's "finish" from this ribbon is what settles them.

.PARAMETER EnvironmentUrl
    The Dataverse org URL (https://<org>.crm.dynamics.com). Required for -Apply and -Verify.

.PARAMETER SolutionName
    The unique name of the dedicated ribbon solution holding the three entities (ribbon only). Required for -Apply.

.PARAMETER SecureTransitionDeployed
    Include Make Secure (see above). With -Apply it is CHECKED, not taken on trust (round 46 item 2, batch-4
    integration): the script reads GET {BffBaseUrl}/api/admin/jobs/secure-child-reconciliation/status (read-only,
    SystemAdmin) and refuses unless the job is enabled every 2 minutes and its latest completed run settled the
    pending Make Secure file relocations in WRITE mode (SecureTransitionBackstopCheck.ps1). Requires -BffBaseUrl and
    -ApiScope.

.PARAMETER BffBaseUrl
    -Apply -SecureTransitionDeployed only: the target environment's BFF (https://<app>.azurewebsites.net).

.PARAMETER ApiScope
    -Apply -SecureTransitionDeployed only: the BFF's API scope for `az account get-access-token --scope`
    (api://<id>/.default). The Azure CLI login must be a user the BFF's SystemAdmin policy admits.

.PARAMETER BeforeList
    -Verify only: the JSON -Apply wrote (before.json). Without it -Verify checks only the Access commands.

.PARAMETER WorkDir
    Where merged files, the export and before.json go. Default: a new folder under the temp directory.

.EXAMPLE
    pwsh ./Set-AccessRibbon.ps1 -SecureTransitionDeployed                                   # dry run, checked-in exports
    pwsh ./Set-AccessRibbon.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -SolutionName SpaarkeAccessRibbons `
        -SecureTransitionDeployed -BffBaseUrl https://<app>.azurewebsites.net -ApiScope api://<id>/.default -Apply
    pwsh ./Set-AccessRibbon.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -SecureTransitionDeployed `
        -Verify -BeforeList <WorkDir>/before.json
#>
[CmdletBinding()]
param(
    [string] $EnvironmentUrl,
    [string] $SolutionName,
    [switch] $SecureTransitionDeployed,
    [string] $BffBaseUrl,
    [string] $ApiScope,
    [switch] $Apply,
    [switch] $Verify,
    [string] $BeforeList,
    [string] $WorkDir
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..' '..' '..' '..' 'scripts' 'lib' 'Publish-SolutionComponents.ps1')

if ($Apply -and $Verify) { throw 'Use -Apply OR -Verify (-Apply runs -Verify itself).' }
if (($Apply -or $Verify) -and -not $EnvironmentUrl) { throw '-EnvironmentUrl is required for -Apply and -Verify.' }
if ($Apply -and -not $SolutionName) { throw '-SolutionName (the dedicated ribbon solution) is required for -Apply.' }
if ($Apply -and $SolutionName -eq 'SpaarkeCore') { throw 'Never import a ribbon through SpaarkeCore (ribbon-edit skill).' }
if ($Apply -and $SecureTransitionDeployed -and (-not $BffBaseUrl -or -not $ApiScope)) {
    throw '-Apply -SecureTransitionDeployed needs -BffBaseUrl and -ApiScope: Make Secure ships only after its file backstop is checked (round 46 item 2).'
}
. (Join-Path $PSScriptRoot 'SecureTransitionBackstopCheck.ps1')
if ($EnvironmentUrl) { $EnvironmentUrl = $EnvironmentUrl.TrimEnd('/') }

$ribbonRoot = Split-Path -Parent $PSScriptRoot
$merge = Join-Path $PSScriptRoot 'Merge-AccessRibbon.ps1'
if (-not $WorkDir) {
    $WorkDir = Join-Path ([System.IO.Path]::GetTempPath()) ("access-ribbon-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

# The three main forms and where 142's checked-in export of each lives (the work assignment's is checked in by -Apply).
$entities = @(
    @{ Logical = 'sprk_project'; CheckedIn = Join-Path $ribbonRoot 'ProjectRibbons/Entities/sprk_Project/RibbonDiff.xml' }
    @{ Logical = 'sprk_matter'; CheckedIn = Join-Path $ribbonRoot 'MatterRibbons/Entities/sprk_Matter/RibbonDiff.xml' }
    @{ Logical = 'sprk_workassignment'; CheckedIn = Join-Path $ribbonRoot 'WorkAssignmentRibbons/Entities/sprk_workassignment/RibbonDiff.xml' }
)

function Get-ExpectedMenu([bool] $withMakeSecure) {
    if ($withMakeSecure) { return @('UpdateAccess', 'MakeSecure', 'RemoveSecure') }
    return @('UpdateAccess', 'RemoveSecure')
}

# The commands of a RibbonDiff and the Access menu it renders, read back from a merged file.
function Get-MergedShape([string] $path, [string] $logical) {
    [xml] $doc = Get-Content -Raw -LiteralPath $path
    $commands = @($doc.SelectNodes('//*[local-name()="CommandDefinition"]') | ForEach-Object { $_.GetAttribute('Id') })
    $menu = @($doc.SelectNodes("//*[local-name()='Button' and starts-with(@Id, 'sprk.Access.$logical.Form.')]") |
        ForEach-Object { $_.GetAttribute('Id').Substring("sprk.Access.$logical.Form.".Length) })
    return @{ Commands = $commands; Menu = $menu }
}

function Invoke-Merge([string] $source, [string] $logical, [string] $out, [string] $shareCommandXml, [string] $gridShareCommandXml) {
    $mergeArgs = @{
        ExportedRibbonDiff = $source; Entity = $logical; Out = $out
        ShareCommandXml = $shareCommandXml; GridShareCommandXml = $gridShareCommandXml
    }
    if ($SecureTransitionDeployed) { $mergeArgs.SecureTransitionDeployed = $true }
    & $merge @mergeArgs   # throws when a pre-existing command would be lost (142's check)

    # Task 114: EVERY platform Share command handed to the merge must come out of it carrying its rule.
    [xml] $merged = Get-Content -Raw -LiteralPath $out
    foreach ($pair in @(
            @{ File = $shareCommandXml; Rule = "sprk.Access.$logical.ShareAllowed.EnableRule"; What = 'form' }
            @{ File = $gridShareCommandXml; Rule = "sprk.Access.$logical.ShareAllowedSelection.EnableRule"; What = 'grid' })) {
        foreach ($commandId in @(Get-CommandIdsIn $pair.File)) {
            $rule = $merged.SelectSingleNode(
                "//*[local-name()='CommandDefinition' and @Id='$commandId']/*[local-name()='EnableRules']/*[local-name()='EnableRule' and @Id='$($pair.Rule)']")
            if (-not $rule) {
                throw "${logical}: the platform's $($pair.What) Share command $commandId does not carry $($pair.Rule) after the merge."
            }
        }
    }

    $shape = Get-MergedShape $out $logical
    $expected = Get-ExpectedMenu $SecureTransitionDeployed.IsPresent
    if (($shape.Menu -join ',') -ne ($expected -join ',')) {
        throw "${logical}: the Access menu is '$($shape.Menu -join ', ')', expected '$($expected -join ', ')'."
    }
}

# -- Live reads (RetrieveEntityRibbon) ----------------------------------------------------------------------------

function Get-DvToken {
    $token = & az account get-access-token --resource "$EnvironmentUrl/" --query accessToken -o tsv 2>$null
    if (-not $token) { throw "No token for $EnvironmentUrl. Run: az login" }
    return $token
}

function Expand-RibbonXml([string] $base64) {
    Add-Type -AssemblyName System.IO.Compression
    $bytes = [Convert]::FromBase64String($base64)
    $stream = New-Object System.IO.MemoryStream(, $bytes)
    try {
        # RetrieveEntityRibbon returns a ZIP package holding RibbonXml.xml; older docs describe GZip - accept both.
        $zip = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Read)
        $reader = New-Object System.IO.StreamReader($zip.Entries[0].Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose(); $zip.Dispose() }
    }
    catch [System.IO.InvalidDataException] {
        $stream = New-Object System.IO.MemoryStream(, $bytes)
        $gzip = New-Object System.IO.Compression.GZipStream($stream, [System.IO.Compression.CompressionMode]::Decompress)
        $reader = New-Object System.IO.StreamReader($gzip)
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
}

function Get-LiveFormRibbon([string] $logical, [string] $token, [string] $location = 'Form') {
    $uri = "$EnvironmentUrl/api/data/v9.2/RetrieveEntityRibbon(EntityName='$logical'," +
        "RibbonLocationFilter=Microsoft.Dynamics.CRM.RibbonLocationFilters'$location')"
    $response = Invoke-RestMethod -Uri $uri -Method Get -Headers @{
        Authorization = "Bearer $token"; Accept = 'application/json'; 'OData-Version' = '4.0'; 'OData-MaxVersion' = '4.0'
    }
    [xml] $ribbon = Expand-RibbonXml $response.CompressedEntityXml
    return $ribbon
}

# Task 114 fix (2026-10-07): the platform's Share BUTTONS, as spaarkedev1's live effective ribbons have them (see the
# header, "WHERE THE PLATFORM'S SHARE IS"). The button ids are the platform's stable ribbon ids; the COMMAND each points at
# is always read from the live ribbon, never assumed. A required button the live ribbon lacks fails -Apply (nothing is
# written), the read-only dry run and -Verify.
function Get-FormShareButtons([string] $logical) {
    @(
        @{ Id = "Mscrm.Form.$logical.Permissions.Sharing"; Required = $true }            # the UCI command-bar Share
        @{ Id = "Mscrm.Form.$logical.Permissions.SharingNonRefresh"; Required = $false } # legacy Permissions flyout
    )
}

function Get-GridShareButtons([string] $logical) {
    @(
        @{ Id = "Mscrm.HomepageGrid.$logical.Sharing"; Required = $true }
        @{ Id = "Mscrm.SubGrid.$logical.Sharing"; Required = $false }
    )
}

# The distinct Commands of the given Share buttons in a live ribbon. Throws for a missing REQUIRED button; a missing
# optional one is reported.
function Get-LiveShareCommandIds([xml] $ribbon, [object[]] $buttons, [string] $logical) {
    $commandIds = @()
    foreach ($b in $buttons) {
        $button = $ribbon.SelectSingleNode("//*[local-name()='Button' and @Id='$($b.Id)']")
        if (-not $button) {
            if ($b.Required) { throw "${logical}: the live ribbon has no $($b.Id) button; nothing was written." }
            Write-Warning "${logical}: no $($b.Id) button in the live ribbon; it is not ruled."
            continue
        }
        $commandIds += $button.GetAttribute('Command')
    }
    return @($commandIds | Select-Object -Unique)
}

# Writes the live CommandDefinition of each command id to $outPath inside <$wrapper> for Merge-AccessRibbon.ps1.
function Save-LiveCommandDefinitions([xml] $ribbon, [string[]] $commandIds, [string] $wrapper, [string] $logical, [string] $outPath) {
    $definitions = foreach ($commandId in $commandIds) {
        $command = $ribbon.SelectSingleNode("//*[local-name()='CommandDefinition' and @Id='$commandId']")
        if (-not $command) { throw "${logical}: the Share command '$commandId' has no definition in the live ribbon; nothing was written." }
        $command.OuterXml
    }
    Set-Content -LiteralPath $outPath -Value ("<$wrapper>" + ($definitions -join '') + "</$wrapper>") -Encoding utf8
}

# Task 114: the platform's FORM Share command(s), from the live form ribbon, for Merge-AccessRibbon.ps1 -ShareCommandXml.
function Save-LiveShareCommand([xml] $ribbon, [string] $logical, [string] $outPath) {
    $commandIds = Get-LiveShareCommandIds $ribbon (Get-FormShareButtons $logical) $logical
    Save-LiveCommandDefinitions $ribbon $commandIds 'ShareCommands' $logical $outPath
    Write-Host "${logical}: the platform's form Share command(s): $($commandIds -join ', ') (copied from the live ribbon to $outPath)"
}

# Task 114 follow-up: the platform's GRID and SUBGRID Share command(s), from the live ribbon (location 'All'), for
# Merge-AccessRibbon.ps1 -GridShareCommandXml (one definition per distinct command).
function Save-LiveGridShareCommands([xml] $ribbon, [string] $logical, [string] $outPath) {
    $commandIds = Get-LiveShareCommandIds $ribbon (Get-GridShareButtons $logical) $logical
    Save-LiveCommandDefinitions $ribbon $commandIds 'GridShareCommands' $logical $outPath
    Write-Host "${logical}: the platform's grid Share command(s): $($commandIds -join ', ') (copied to $outPath)"
}

function Get-CommandIdsIn([string] $path) {
    if (-not $path -or -not (Test-Path -LiteralPath $path)) { return @() }
    [xml] $doc = Get-Content -Raw -LiteralPath $path
    @($doc.SelectNodes('//*[local-name()="CommandDefinition"]') | ForEach-Object { $_.GetAttribute('Id') } | Select-Object -Unique)
}

function Get-LiveCommandIds([xml] $ribbon) {
    @($ribbon.SelectNodes('//*[local-name()="CommandDefinition"]') | ForEach-Object { $_.GetAttribute('Id') }) |
        Sort-Object -Unique
}

function Test-LiveAccessGroup([string] $logical, [xml] $ribbon, [string[]] $before) {
    $failures = New-Object System.Collections.Generic.List[string]
    $ids = @(Get-LiveCommandIds $ribbon)
    if ($ids.Count -eq 0) { $failures.Add('the effective form ribbon could not be read (no command definitions)') }
    Write-Host "${logical}: $($ids.Count) commands on the effective form ribbon"

    $expectedCommands = @{
        "sprk.Access.$logical.UpdateAccess.Command" = 'Spaarke.Access.Ribbon.updateAccess'
        "sprk.Access.$logical.RemoveSecure.Command" = 'Spaarke.Access.Ribbon.removeSecure'
    }
    if ($SecureTransitionDeployed) {
        $expectedCommands["sprk.Access.$logical.MakeSecure.Command"] = 'Spaarke.Access.Ribbon.makeSecure'
    }
    elseif ($ids -contains "sprk.Access.$logical.MakeSecure.Command") {
        $failures.Add("Make Secure is present, but -SecureTransitionDeployed was not given (acceptance (b)).")
    }

    foreach ($command in $expectedCommands.Keys) {
        $node = $ribbon.SelectSingleNode("//*[local-name()='CommandDefinition' and @Id='$command']")
        if (-not $node) { $failures.Add("missing command $command"); continue }
        $function = $node.SelectSingleNode(".//*[local-name()='JavaScriptFunction' and @FunctionName='$($expectedCommands[$command])']")
        if (-not $function -or $function.GetAttribute('Library') -ne '$webresource:sprk_/scripts/access_ribbon.js') {
            $failures.Add("$command does not call $($expectedCommands[$command]) in sprk_/scripts/access_ribbon.js")
        }
    }

    foreach ($command in @($before | Where-Object { $_ })) {
        if ($command -notlike 'sprk.Access.*' -and $ids -notcontains $command) {
            $failures.Add("a command present before the import is gone: $command")
        }
    }

    # Task 114: every platform form Share command carries the ShareAllowed rule, and the rule calls isShareAllowed.
    $shareRuleId = "sprk.Access.$logical.ShareAllowed.EnableRule"
    $foundShare = 0
    foreach ($b in (Get-FormShareButtons $logical)) {
        $shareButton = $ribbon.SelectSingleNode("//*[local-name()='Button' and @Id='$($b.Id)']")
        if (-not $shareButton) {
            if ($b.Required) { $failures.Add("no $($b.Id) button on the effective form ribbon") }
            continue
        }
        $foundShare++
        $shareCommandId = $shareButton.GetAttribute('Command')
        $reference = $ribbon.SelectSingleNode(
            "//*[local-name()='CommandDefinition' and @Id='$shareCommandId']/*[local-name()='EnableRules']/*[local-name()='EnableRule' and @Id='$shareRuleId']")
        if (-not $reference) { $failures.Add("the form Share command $shareCommandId ($($b.Id)) does not carry $shareRuleId") }
    }
    if ($foundShare -gt 0) {
        $ruleFunction = $ribbon.SelectSingleNode(
            "//*[local-name()='EnableRule' and @Id='$shareRuleId']/*[local-name()='CustomRule' and @FunctionName='Spaarke.Access.Ribbon.isShareAllowed']")
        if (-not $ruleFunction -or $ruleFunction.GetAttribute('Library') -ne '$webresource:sprk_/scripts/access_ribbon.js') {
            $failures.Add("$shareRuleId does not call Spaarke.Access.Ribbon.isShareAllowed in sprk_/scripts/access_ribbon.js")
        }
    }

    return , $failures
}

# Task 114 follow-up: every grid Share button the live ribbon has carries ShareAllowedSelection, which calls
# isShareAllowedForSelection with the selected ids and the entity name.
function Test-LiveGridShareRule([string] $logical, [xml] $ribbon) {
    $failures = New-Object System.Collections.Generic.List[string]
    $ruleId = "sprk.Access.$logical.ShareAllowedSelection.EnableRule"
    $found = 0
    foreach ($b in (Get-GridShareButtons $logical)) {
        $button = $ribbon.SelectSingleNode("//*[local-name()='Button' and @Id='$($b.Id)']")
        if (-not $button) {
            if ($b.Required) { $failures.Add("no $($b.Id) button on the effective ribbon") }
            continue
        }
        $found++
        $commandId = $button.GetAttribute('Command')
        $reference = $ribbon.SelectSingleNode(
            "//*[local-name()='CommandDefinition' and @Id='$commandId']/*[local-name()='EnableRules']/*[local-name()='EnableRule' and @Id='$ruleId']")
        if (-not $reference) { $failures.Add("the grid Share command $commandId ($($b.Id)) does not carry $ruleId") }
    }
    if ($found -gt 0) {
        $function = $ribbon.SelectSingleNode(
            "//*[local-name()='EnableRule' and @Id='$ruleId']/*[local-name()='CustomRule' and @FunctionName='Spaarke.Access.Ribbon.isShareAllowedForSelection']")
        if (-not $function -or $function.GetAttribute('Library') -ne '$webresource:sprk_/scripts/access_ribbon.js') {
            $failures.Add("$ruleId does not call Spaarke.Access.Ribbon.isShareAllowedForSelection in sprk_/scripts/access_ribbon.js")
        }
        elseif (@($function.SelectNodes("*[local-name()='CrmParameter']") | ForEach-Object { $_.GetAttribute('Value') }) -join ',' -ne
            'SelectedControlSelectedItemIds,SelectedEntityTypeName') {
            $failures.Add("$ruleId does not pass SelectedControlSelectedItemIds and SelectedEntityTypeName")
        }
    }
    return , $failures
}

# -- Modes --------------------------------------------------------------------------------------------------------

function Invoke-Verify([System.Collections.IDictionary] $beforeByEntity) {
    $token = Get-DvToken
    $failed = 0
    foreach ($e in $entities) {
        $ribbon = Get-LiveFormRibbon $e.Logical $token
        $before = if ($beforeByEntity -and $beforeByEntity.Contains($e.Logical)) { $beforeByEntity[$e.Logical] } else { @() }
        $failures = Test-LiveAccessGroup $e.Logical $ribbon $before
        foreach ($f in (Test-LiveGridShareRule $e.Logical (Get-LiveFormRibbon $e.Logical $token 'All'))) { $failures.Add($f) }
        if ($failures.Count -eq 0) {
            Write-Host "[PASS] $($e.Logical): Access group as expected$(if (@($before).Count) { "; all $(@($before).Count) before-commands present" })"
        }
        else {
            $failed++
            foreach ($f in $failures) { Write-Host "[FAIL] $($e.Logical): $f" -ForegroundColor Red }
        }
    }
    return $failed
}

if ($Verify) {
    $beforeByEntity = $null
    if ($BeforeList) {
        $beforeByEntity = @{}
        (Get-Content -Raw -LiteralPath $BeforeList | ConvertFrom-Json).PSObject.Properties |
            ForEach-Object { $beforeByEntity[$_.Name] = @($_.Value) }
    }
    $failed = Invoke-Verify $beforeByEntity
    if ($failed -gt 0) { Write-Host "VERIFY FAILED on $failed entit$(if ($failed -eq 1) { 'y' } else { 'ies' })." -ForegroundColor Red; exit 1 }
    Write-Host 'VERIFY PASSED.' -ForegroundColor Green
    exit 0
}

if (-not $Apply) {
    $dryRunToken = $null
    if ($EnvironmentUrl) {
        Write-Host "DRY RUN - 142's checked-in exports, with the LIVE Share commands of $EnvironmentUrl (RetrieveEntityRibbon, read-only; nothing is written to Dataverse). Output: $WorkDir"
        $dryRunToken = Get-DvToken
    }
    else {
        Write-Host "DRY RUN - 142's checked-in exports and the fixtures/ Share stand-ins; no Dataverse call. Output: $WorkDir"
    }
    $missing = @()
    foreach ($e in $entities) {
        # Task 114: the platform's Share commands - read from the live ribbons (exactly what -Apply reads, so a missing
        # Share button fails here too), or the stand-ins that only exercise the transformation.
        if ($dryRunToken) {
            $dryRunShareCommand = Join-Path $WorkDir "$($e.Logical).share-command.xml"
            Save-LiveShareCommand (Get-LiveFormRibbon $e.Logical $dryRunToken) $e.Logical $dryRunShareCommand
            $dryRunGridShareCommand = Join-Path $WorkDir "$($e.Logical).grid-share-commands.xml"
            Save-LiveGridShareCommands (Get-LiveFormRibbon $e.Logical $dryRunToken 'All') $e.Logical $dryRunGridShareCommand
        }
        else {
            $dryRunShareCommand = Join-Path $PSScriptRoot 'fixtures/share-command.dry-run-sample.xml'
            $dryRunGridShareCommand = Join-Path $PSScriptRoot 'fixtures/grid-share-command.dry-run-sample.xml'
        }
        if (-not (Test-Path -LiteralPath $e.CheckedIn)) {
            $missing += $e.Logical
            Write-Host "[SKIP] $($e.Logical): no checked-in export at $($e.CheckedIn) - -Apply exports it fresh and checks it in (task 142 UX (f))." -ForegroundColor Yellow
            continue
        }
        Invoke-Merge $e.CheckedIn $e.Logical (Join-Path $WorkDir "$($e.Logical).RibbonDiff.xml") $dryRunShareCommand $dryRunGridShareCommand
    }
    Write-Host ("DRY RUN PASSED{0}." -f $(if ($missing.Count) { " (not checked in yet: $($missing -join ', '))" } else { '' })) -ForegroundColor Green
    exit 0
}

# -Apply (LIVE WRITE)
if ($SecureTransitionDeployed) {
    # Round 46 item 2: Make Secure ships only where its file backstop runs with its writes on. Read-only.
    $bffToken = & az account get-access-token --scope $ApiScope --query accessToken -o tsv 2>$null
    if (-not $bffToken) { throw "No token for $ApiScope. Run: az login (as a user the BFF's SystemAdmin policy admits)." }
    $status = Invoke-RestMethod -Method Get -Uri "$($BffBaseUrl.TrimEnd('/'))/api/admin/jobs/secure-child-reconciliation/status" `
        -Headers @{ Authorization = "Bearer $bffToken" }
    $backstop = @(Test-SecureTransitionBackstop -Status $status)
    if ($backstop.Count -gt 0) {
        $backstop | ForEach-Object { Write-Host "REFUSED: $_" -ForegroundColor Red }
        throw 'Make Secure is not shipped: its file backstop is not running with writes on (round 46 item 2). Nothing was written.'
    }
    Write-Host 'Make Secure file backstop: secure-child reconciliation enabled every 2 minutes, latest run settled relocations in write mode.' -ForegroundColor Green
}
Write-Host "APPLY to $EnvironmentUrl through solution '$SolutionName'. Make Secure: $(if ($SecureTransitionDeployed) { 'INCLUDED' } else { 'withheld' })."
$token = Get-DvToken
$beforeByEntity = [ordered] @{}
$shareCommandFiles = @{}
$gridShareCommandFiles = @{}
foreach ($e in $entities) {
    $liveRibbon = Get-LiveFormRibbon $e.Logical $token
    $beforeByEntity[$e.Logical] = @(Get-LiveCommandIds $liveRibbon)
    Write-Host "Before ($($e.Logical)): $($beforeByEntity[$e.Logical] -join ', ')"
    # Task 114: the platform's own Share command, from the LIVE ribbon (throws, writing nothing, when it is not there).
    $shareCommandFiles[$e.Logical] = Join-Path $WorkDir "$($e.Logical).share-command.xml"
    Save-LiveShareCommand $liveRibbon $e.Logical $shareCommandFiles[$e.Logical]
    # Task 114 follow-up: the platform's grid / subgrid Share command(s), from the LIVE ribbon (location 'All').
    $gridShareCommandFiles[$e.Logical] = Join-Path $WorkDir "$($e.Logical).grid-share-commands.xml"
    Save-LiveGridShareCommands (Get-LiveFormRibbon $e.Logical $token 'All') $e.Logical $gridShareCommandFiles[$e.Logical]
}
$beforePath = Join-Path $WorkDir 'before.json'
$beforeByEntity | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $beforePath -Encoding utf8
Write-Host "Recorded the live before-list: $beforePath"

$exportZip = Join-Path $WorkDir "$SolutionName.zip"
$unpacked = Join-Path $WorkDir 'unpacked'
# `pac` must resolve to the Power Platform CLI executable. Under Git Bash the PATH can put a bash shim named `pac`
# first; PowerShell cannot run it and leaves $LASTEXITCODE untouched, so a pack/import that never ran looked successful
# (task 154 dev apply, 2026-10-08).
$pacExe = (Get-Command pac -CommandType Application -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in '.cmd', '.exe', '.bat' } | Select-Object -First 1).Source
if (-not $pacExe) { throw 'pac CLI not found: need pac.cmd or pac.exe on PATH.' }
& $pacExe solution export --environment $EnvironmentUrl --name $SolutionName --path $exportZip --overwrite
if ($LASTEXITCODE -ne 0) { throw "pac solution export failed ($LASTEXITCODE)." }
& $pacExe solution unpack --zipfile $exportZip --folder $unpacked --packagetype Unmanaged --allowDelete true
if ($LASTEXITCODE -ne 0) { throw "pac solution unpack failed ($LASTEXITCODE)." }

foreach ($e in $entities) {
    $entityDir = Get-ChildItem -LiteralPath (Join-Path $unpacked 'Entities') -Directory |
        Where-Object { $_.Name -ieq $e.Logical } | Select-Object -First 1
    if (-not $entityDir) { throw "The solution '$SolutionName' does not hold $($e.Logical) (Entities/$($e.Logical) missing)." }
    $ribbonDiff = Join-Path $entityDir.FullName 'RibbonDiff.xml'
    if (-not (Test-Path -LiteralPath $ribbonDiff)) { throw "No RibbonDiff.xml for $($e.Logical) in the export." }

    if ($e.Logical -eq 'sprk_workassignment') {
        # Task 142 UX (f): the work-assignment ribbon is checked in as EXPORTED, before any edit. Commit it.
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $e.CheckedIn) | Out-Null
        Copy-Item -LiteralPath $ribbonDiff -Destination $e.CheckedIn -Force
        Write-Host "Checked in the exported work-assignment ribbon: $($e.CheckedIn) (commit it)."
    }

    Invoke-Merge $ribbonDiff $e.Logical $ribbonDiff $shareCommandFiles[$e.Logical] $gridShareCommandFiles[$e.Logical]
}

$packed = Join-Path $WorkDir "$SolutionName.merged.zip"
& $pacExe solution pack --zipfile $packed --folder $unpacked --packagetype Unmanaged
if ($LASTEXITCODE -ne 0) { throw "pac solution pack failed ($LASTEXITCODE)." }
# Task 130 (D-83): import without a tenant-wide publish, then publish only this ribbon solution's components.
Invoke-ScopedSolutionImport -EnvironmentUrl $EnvironmentUrl -ZipPath $packed -SolutionUniqueName $SolutionName -PacExe $pacExe -ImportArgs @() `
    -Context (Get-DataverseApiContext -EnvironmentUrl $EnvironmentUrl) | Out-Null

# The effective ribbon (RetrieveEntityRibbon) lags the publish by up to a minute or more, so a verify run straight after
# the import can fail on rules that are in fact applied (see the header). Retry with a bounded backoff; only the last
# attempt's failures decide.
$retryDelaysSeconds = @(15, 30, 45, 60, 30)
$failed = Invoke-Verify $beforeByEntity
$attempt = 1
foreach ($delay in $retryDelaysSeconds) {
    if ($failed -eq 0) { break }
    Write-Host ("Verify attempt $attempt of $($retryDelaysSeconds.Count + 1) failed on $failed entit$(if ($failed -eq 1) { 'y' } else { 'ies' }); " +
        "the effective ribbon can lag the publish - retrying in $delay s.") -ForegroundColor Yellow
    Start-Sleep -Seconds $delay
    $attempt++
    $failed = Invoke-Verify $beforeByEntity
}
if ($failed -gt 0) {
    Write-Host "APPLIED, but VERIFY FAILED on $failed entities after $attempt attempts over about 3 minutes - see above." -ForegroundColor Red
    exit 1
}
Write-Host 'APPLIED and VERIFIED. Then run the ui-tests (task 150 POML) on the three forms.' -ForegroundColor Green
exit 0
