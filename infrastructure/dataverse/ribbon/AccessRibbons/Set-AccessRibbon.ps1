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

      DRY RUN (default; no Dataverse call, nothing written outside -WorkDir): merges the template into 142's CHECKED-IN
        exports - ProjectRibbons/Entities/sprk_Project/RibbonDiff.xml, MatterRibbons/Entities/sprk_Matter/RibbonDiff.xml
        and, once checked in, WorkAssignmentRibbons/Entities/sprk_workassignment/RibbonDiff.xml - and prints, per
        entity, the commands before and after and the Access menu. FAILS if a command would be lost, or if the menu is
        not exactly the expected one.

      -Apply (LIVE WRITE - the main session runs it): records the LIVE command list of each main form (before), exports
        the dedicated ribbon solution -SolutionName (never SpaarkeCore - the ribbon-edit skill), unpacks it, checks the
        work-assignment RibbonDiff.xml into WorkAssignmentRibbons/Entities/sprk_workassignment/ BEFORE editing it (task
        142 UX (f)), merges each entity with Merge-AccessRibbon.ps1, packs, imports with publish, then runs -Verify
        against the recorded before-list.

      -Verify (read-only): reads each main form's EFFECTIVE ribbon (RetrieveEntityRibbon, Form) and checks that every
        command of the before-list is still present, that Update Access and Remove Secure are present with their
        access_ribbon.js functions, that Make Secure is present exactly when -SecureTransitionDeployed is given, and
        (task 114) that the platform's form Share command carries sprk.Access.<entity>.ShareAllowed.EnableRule, which
        calls access_ribbon.js isShareAllowed. Exit 0 = PASS.

    THE SHARE COMMAND ON RESTRICTED RECORDS (task 114, owner round 67 amendment 4(a)). The platform's own form Share
    command is hidden on a Restricted record (sprk_accesspermission = Restricted), so sharing goes through Manage Access
    "+ User", which refuses a user flagged external there. The command is the PLATFORM's: -Apply reads it from the live
    effective ribbon (the Command of the Mscrm.Form.<entity>.Share button - never assumed) and Merge-AccessRibbon.ps1
    appends the rule to that copy, keeping every platform rule. The dry run uses fixtures/share-command.dry-run-sample.xml,
    a stand-in that only exercises the transformation. Needs access_ribbon.js 1.6.0 (isShareAllowed) published first.

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

function Invoke-Merge([string] $source, [string] $logical, [string] $out, [string] $shareCommandXml) {
    $mergeArgs = @{ ExportedRibbonDiff = $source; Entity = $logical; Out = $out; ShareCommandXml = $shareCommandXml }
    if ($SecureTransitionDeployed) { $mergeArgs.SecureTransitionDeployed = $true }
    & $merge @mergeArgs   # throws when a pre-existing command would be lost (142's check)

    # Task 114: the platform's Share command must come out of the merge carrying the ShareAllowed rule.
    [xml] $merged = Get-Content -Raw -LiteralPath $out
    $shareRule = $merged.SelectSingleNode(
        "//*[local-name()='CommandDefinition' and not(starts-with(@Id, 'sprk.'))]/*[local-name()='EnableRules']/*[local-name()='EnableRule' and @Id='sprk.Access.$logical.ShareAllowed.EnableRule']")
    if (-not $shareRule) {
        throw "${logical}: the platform's Share command does not carry sprk.Access.$logical.ShareAllowed.EnableRule after the merge."
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

function Get-LiveFormRibbon([string] $logical, [string] $token) {
    $uri = "$EnvironmentUrl/api/data/v9.2/RetrieveEntityRibbon(EntityName='$logical'," +
        "RibbonLocationFilter=Microsoft.Dynamics.CRM.RibbonLocationFilters'Form')"
    $response = Invoke-RestMethod -Uri $uri -Method Get -Headers @{
        Authorization = "Bearer $token"; Accept = 'application/json'; 'OData-Version' = '4.0'; 'OData-MaxVersion' = '4.0'
    }
    [xml] $ribbon = Expand-RibbonXml $response.CompressedEntityXml
    return $ribbon
}

# Task 114: the platform's form Share command as the live ribbon has it - the Command of the Mscrm.Form.<entity>.Share
# button, never an assumed id - written to $outPath for Merge-AccessRibbon.ps1 -ShareCommandXml.
function Save-LiveShareCommand([xml] $ribbon, [string] $logical, [string] $outPath) {
    $button = $ribbon.SelectSingleNode("//*[local-name()='Button' and @Id='Mscrm.Form.$logical.Share']")
    if (-not $button) { throw "${logical}: the live form ribbon has no Mscrm.Form.$logical.Share button; nothing was written." }
    $commandId = $button.GetAttribute('Command')
    $command = $ribbon.SelectSingleNode("//*[local-name()='CommandDefinition' and @Id='$commandId']")
    if (-not $command) { throw "${logical}: the Share button's command '$commandId' has no definition in the live ribbon; nothing was written." }
    Set-Content -LiteralPath $outPath -Value $command.OuterXml -Encoding utf8
    Write-Host "${logical}: the platform's Share command is '$commandId' (copied from the live ribbon to $outPath)"
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

    # Task 114: the platform's Share command carries the ShareAllowed rule, and the rule calls isShareAllowed.
    $shareButton = $ribbon.SelectSingleNode("//*[local-name()='Button' and @Id='Mscrm.Form.$logical.Share']")
    $shareRuleId = "sprk.Access.$logical.ShareAllowed.EnableRule"
    if (-not $shareButton) {
        $failures.Add("no Mscrm.Form.$logical.Share button on the effective form ribbon")
    }
    else {
        $shareCommandId = $shareButton.GetAttribute('Command')
        $reference = $ribbon.SelectSingleNode(
            "//*[local-name()='CommandDefinition' and @Id='$shareCommandId']/*[local-name()='EnableRules']/*[local-name()='EnableRule' and @Id='$shareRuleId']")
        if (-not $reference) { $failures.Add("the Share command $shareCommandId does not carry $shareRuleId") }
        $ruleFunction = $ribbon.SelectSingleNode(
            "//*[local-name()='EnableRule' and @Id='$shareRuleId']/*[local-name()='CustomRule' and @FunctionName='Spaarke.Access.Ribbon.isShareAllowed']")
        if (-not $ruleFunction -or $ruleFunction.GetAttribute('Library') -ne '$webresource:sprk_/scripts/access_ribbon.js') {
            $failures.Add("$shareRuleId does not call Spaarke.Access.Ribbon.isShareAllowed in sprk_/scripts/access_ribbon.js")
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
    Write-Host "DRY RUN - 142's checked-in exports; no Dataverse call. Output: $WorkDir"
    # Task 114: a stand-in for the platform's Share command (the transformation only; -Apply merges the LIVE command).
    $dryRunShareCommand = Join-Path $PSScriptRoot 'fixtures/share-command.dry-run-sample.xml'
    $missing = @()
    foreach ($e in $entities) {
        if (-not (Test-Path -LiteralPath $e.CheckedIn)) {
            $missing += $e.Logical
            Write-Host "[SKIP] $($e.Logical): no checked-in export at $($e.CheckedIn) - -Apply exports it fresh and checks it in (task 142 UX (f))." -ForegroundColor Yellow
            continue
        }
        Invoke-Merge $e.CheckedIn $e.Logical (Join-Path $WorkDir "$($e.Logical).RibbonDiff.xml") $dryRunShareCommand
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
foreach ($e in $entities) {
    $liveRibbon = Get-LiveFormRibbon $e.Logical $token
    $beforeByEntity[$e.Logical] = @(Get-LiveCommandIds $liveRibbon)
    Write-Host "Before ($($e.Logical)): $($beforeByEntity[$e.Logical] -join ', ')"
    # Task 114: the platform's own Share command, from the LIVE ribbon (throws, writing nothing, when it is not there).
    $shareCommandFiles[$e.Logical] = Join-Path $WorkDir "$($e.Logical).share-command.xml"
    Save-LiveShareCommand $liveRibbon $e.Logical $shareCommandFiles[$e.Logical]
}
$beforePath = Join-Path $WorkDir 'before.json'
$beforeByEntity | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $beforePath -Encoding utf8
Write-Host "Recorded the live before-list: $beforePath"

$exportZip = Join-Path $WorkDir "$SolutionName.zip"
$unpacked = Join-Path $WorkDir 'unpacked'
& pac solution export --environment $EnvironmentUrl --name $SolutionName --path $exportZip --overwrite
if ($LASTEXITCODE -ne 0) { throw "pac solution export failed ($LASTEXITCODE)." }
& pac solution unpack --zipfile $exportZip --folder $unpacked --packagetype Unmanaged --allowDelete true
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

    Invoke-Merge $ribbonDiff $e.Logical $ribbonDiff $shareCommandFiles[$e.Logical]
}

$packed = Join-Path $WorkDir "$SolutionName.merged.zip"
& pac solution pack --zipfile $packed --folder $unpacked --packagetype Unmanaged
if ($LASTEXITCODE -ne 0) { throw "pac solution pack failed ($LASTEXITCODE)." }
& pac solution import --environment $EnvironmentUrl --path $packed --publish-changes
if ($LASTEXITCODE -ne 0) { throw "pac solution import failed ($LASTEXITCODE)." }

$failed = Invoke-Verify $beforeByEntity
if ($failed -gt 0) { Write-Host "APPLIED, but VERIFY FAILED on $failed entities - see above." -ForegroundColor Red; exit 1 }
Write-Host 'APPLIED and VERIFIED. Then run the ui-tests (task 150 POML) on the three forms.' -ForegroundColor Green
exit 0
