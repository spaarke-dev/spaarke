<#
.SYNOPSIS
    Custom create / wizard ribbon buttons hide for a user without the Create privilege on the table they create -
    dry run (default), -Apply, -Verify.

.DESCRIPTION
    unified-access-control-r2 task 180 (owner round 89 item 2). Every Spaarke ribbon command whose action calls a create
    launcher (create-launchers.json: function -> table) carries the display rule sprk.CreatePrivilege.<table>.DisplayRule
    (EntityPrivilegeRule Create, depth Basic = "holds Create at any depth"). Dataverse evaluates the rule; no script.
    The platform's own New buttons are untouched (the platform already rules them).

      DRY RUN (default; nothing written): runs Merge-CreatePrivilegeRule.ps1 -Check over every checked-in ribbon source
        that calls a launcher (git grep, so a new source is found without a list) and FAILS if a create command or its
        rule definition is missing. With -EnvironmentUrl it also reads each host table's EFFECTIVE ribbon
        (RetrieveEntityRibbon 'All', read-only) and lists the live create commands and whether each is ruled yet, and
        lists the live create MODERN COMMANDS (see below).

      -Apply (LIVE WRITE - the main session runs it): records each host table's live command list (before.json);
        exports every solution in -Solutions (fresh - never a checked-in copy: those lag the environment, and an
        unmanaged ribbon import REPLACES the table's whole ribbon customisation); refuses, writing nothing, when an
        export's RibbonDiff lacks a command, rule, custom action, hide action or label the environment holds for that
        table (../Test-RibbonExportCurrent.ps1: a stale or partial export would delete it) or when a live create
        command's table is in none of the exports; merges the rule into every exported
        RibbonDiff; then packs and imports each solution with publish, and verifies (with the same bounded retry as
        Set-AccessRibbon.ps1 - the effective ribbon lags the publish).

      -Verify (read-only): for every host table, every command in the effective ribbon that calls a launcher carries
        its table's rule, and the rule is exactly EntityPrivilegeRule EntityName=<table> PrivilegeType=Create
        PrivilegeDepth=Basic. With -BeforeList, every command present before the import is still present. It also FAILS
        on every unruled create modern command (below). Exit 0 = PASS.

    MODERN COMMANDS (appaction). A command made in the modern command designer is an appaction row, not ribbon XML:
    RetrieveEntityRibbon does not return it and RibbonDiff cannot rule it. Every mode with -EnvironmentUrl therefore also
    reads the unmanaged, active appactions whose onclickeventjavascriptfunctionname is a launcher, and reports each one:
      hidden   - hidden = true: not shown;
      formula  - visibilitytype = Formula: a Power Fx visibility rule in a command component library. Its text cannot be
                 read back by script, so -Verify passes it and prints a manual check
                 (Visible = DataSourceInfo(<table>, DataSourceInfo.CreatePermission));
      UNRULED  - shown to every user: -Verify FAILS.
    A Power Fx rule is authored in the command designer (the formula is compiled into a component library .msapp that
    no supported API writes), so -Apply cannot add one. Hiding or deleting a customisation someone made is an owner
    decision: -Apply hides EXACTLY the appactions listed in create-launchers.json "hideAppActions" (by appactionid; the
    row's uniquename must match and it must be unmanaged and call a launcher, else nothing is written), sets
    hidden = true, publishes the app (PublishXml, appmodules) and reads the row back. It changes no other appaction.
    The dry run prints the planned hide. Owner decision 2026-10-09: hide the Matter Management "New Document".

    Never run -Apply at the same time as another ribbon import into the same tables (Set-AccessRibbon.ps1,
    Deploy-SecureChildNewCommands.ps1): each one exports, merges and re-imports the whole ribbon of a table, so two
    interleaved runs lose one run's change. Run them one after another. This script and Deploy-SecureChildNewCommands.ps1
    both refuse an export that lacks, or holds different content for, anything the environment holds (Test-RibbonExportCurrent.ps1); Set-AccessRibbon.ps1
    exports fresh itself.

.PARAMETER EnvironmentUrl
    The Dataverse org URL (https://<org>.crm.dynamics.com). Required for -Apply and -Verify.

.PARAMETER Solutions
    -Apply: the dedicated ribbon solutions to export, merge and import, in order. Default (spaarkedev1, 2026-10-09):
    SpaarkeAccessRibbons (sprk_matter, sprk_project, sprk_workassignment), SpaarkeSecureChildRibbons (sprk_event,
    sprk_document and seven other child tables), AnalysisRibbons (sprk_analysis) and EmailRibbons (email). Never
    SpaarkeCore (ribbon-edit skill).

.PARAMETER BeforeList
    -Verify only: the before.json -Apply wrote.

.PARAMETER WorkDir
    Where the exports and before.json go. Default: a new folder under the temp directory.

.EXAMPLE
    pwsh ./Set-CreatePrivilegeRibbon.ps1                                                         # dry run, sources only
    pwsh ./Set-CreatePrivilegeRibbon.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com    # + live read
    pwsh ./Set-CreatePrivilegeRibbon.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply
    pwsh ./Set-CreatePrivilegeRibbon.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify -BeforeList <WorkDir>/before.json
#>
[CmdletBinding()]
param(
    [string] $EnvironmentUrl,
    [string[]] $Solutions = @('SpaarkeAccessRibbons', 'SpaarkeSecureChildRibbons', 'AnalysisRibbons', 'EmailRibbons'),
    [switch] $Apply,
    [switch] $Verify,
    [string] $BeforeList,
    [string] $WorkDir
)

$ErrorActionPreference = 'Stop'

if ($Apply -and $Verify) { throw 'Use -Apply OR -Verify (-Apply runs -Verify itself).' }
if (($Apply -or $Verify) -and -not $EnvironmentUrl) { throw '-EnvironmentUrl is required for -Apply and -Verify.' }
if ($Apply -and ($Solutions -contains 'SpaarkeCore')) { throw 'Never import a ribbon through SpaarkeCore (ribbon-edit skill).' }
if ($EnvironmentUrl) { $EnvironmentUrl = $EnvironmentUrl.TrimEnd('/') }

$merge = Join-Path $PSScriptRoot 'Merge-CreatePrivilegeRule.ps1'
$config = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'create-launchers.json') | ConvertFrom-Json
$launchers = @{}
$config.launchers.PSObject.Properties | ForEach-Object { $launchers[$_.Name] = $_.Value }
$hosts = @($config.hosts)
if (-not $WorkDir) {
    $WorkDir = Join-Path ([System.IO.Path]::GetTempPath()) ("create-privilege-ribbon-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null

# -- Live reads -----------------------------------------------------------------------------------------------------

function Get-DvToken {
    $token = & az account get-access-token --resource "$EnvironmentUrl/" --query accessToken -o tsv 2>$null
    if (-not $token) { throw "No token for $EnvironmentUrl. Run: az login" }
    return $token
}

function Invoke-Dv([string] $relative, [string] $token) {
    Invoke-RestMethod -Uri "$EnvironmentUrl/api/data/v9.2/$relative" -Method Get -Headers @{
        Authorization = "Bearer $token"; Accept = 'application/json'; 'OData-Version' = '4.0'; 'OData-MaxVersion' = '4.0'
    }
}

function Expand-RibbonXml([string] $base64) {
    Add-Type -AssemblyName System.IO.Compression
    $bytes = [Convert]::FromBase64String($base64)
    $stream = New-Object System.IO.MemoryStream(, $bytes)
    try {
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

# The EFFECTIVE ribbon of a table (form, home grid and subgrid).
function Get-LiveRibbon([string] $table, [string] $token) {
    $response = Invoke-Dv ("RetrieveEntityRibbon(EntityName='$table'," +
        "RibbonLocationFilter=Microsoft.Dynamics.CRM.RibbonLocationFilters'All')") $token
    [xml] $ribbon = Expand-RibbonXml $response.CompressedEntityXml
    return $ribbon
}

function Get-LiveCommandIds([xml] $ribbon) {
    @($ribbon.SelectNodes('//*[local-name()="CommandDefinition"]') | ForEach-Object { $_.GetAttribute('Id') }) | Sort-Object -Unique
}

# The unmanaged, active MODERN commands (appaction) that call a launcher, with their visibility state.
function Get-CreateAppActions([string] $token) {
    $select = 'appactionid,uniquename,buttonlabeltext,contextvalue,onclickeventjavascriptfunctionname,hidden,visibilitytype,' +
        'visibilityformulacomponentlibrary,visibilityformulafunctionname,modifiedon'
    $filter = [uri]::EscapeDataString('ismanaged eq false and statecode eq 0 and onclickeventjavascriptfunctionname ne null')
    $uri = "appactions?`$select=$select&`$filter=$filter"
    $rows = @()
    while ($uri) {
        $page = Invoke-Dv $uri $token
        $rows += @($page.value)
        $next = $page.'@odata.nextLink'
        $uri = if ($next) { $next.Substring($next.IndexOf('/api/data/v9.2/') + '/api/data/v9.2/'.Length) } else { $null }
    }
    foreach ($a in $rows) {
        $function = $a.onclickeventjavascriptfunctionname
        if (-not $launchers.ContainsKey($function)) { continue }
        $state = if ($a.hidden) { 'hidden' }
        elseif ($a.visibilitytype -eq 1 -and $a.visibilityformulafunctionname) { 'formula' }
        else { 'UNRULED' }
        [pscustomobject] @{
            Id = $a.appactionid; UniqueName = $a.uniquename; Label = $a.buttonlabeltext; Context = $a.contextvalue; Function = $function
            Table = $launchers[$function]; State = $state; Modified = $a.modifiedon
            Formula = "$($a.visibilityformulacomponentlibrary)/$($a.visibilityformulafunctionname)"
        }
    }
}

# The owner-decided hides (create-launchers.json hideAppActions), each checked against the live row. Returns objects
# with Id, UniqueName, AppModuleId, Hidden, Problem (a reason it must not be written; empty when it may be).
function Get-PlannedAppActionHides([string] $token) {
    foreach ($h in @($config.hideAppActions)) {
        if (-not $h) { continue }
        $row = $null
        try {
            $row = Invoke-Dv ("appactions($($h.appactionid))?`$select=uniquename,ismanaged,hidden,statecode," +
                'onclickeventjavascriptfunctionname,_appmoduleid_value') $token
        }
        catch {
            $status = $null
            if ($_.Exception.Response) { $status = [int] $_.Exception.Response.StatusCode }
            if ($status -ne 404) {
                throw "hideAppActions $($h.appactionid): reading the appaction failed ($(if ($status) { "HTTP $status" } else { 'no HTTP response' })): $($_.Exception.Message)"
            }
        }
        $problem = if (-not $row) { 'not found (HTTP 404)' }
        elseif ($row.uniquename -ne $h.uniquename) { "uniquename is '$($row.uniquename)', expected '$($h.uniquename)'" }
        elseif ($row.ismanaged) { 'managed (not ours to change)' }
        elseif (-not $launchers.ContainsKey($row.onclickeventjavascriptfunctionname)) { "calls '$($row.onclickeventjavascriptfunctionname)', not a create launcher" }
        else { '' }
        [pscustomobject] @{
            Id = $h.appactionid; UniqueName = $h.uniquename; AppModuleId = $(if ($row) { $row._appmoduleid_value } else { $null })
            Hidden = [bool] ($row -and $row.hidden); Problem = $problem
        }
    }
}

# -Verify's appaction part: FAIL lines for unruled ones; a manual-check line for a formula.
function Test-CreateAppActions([string] $token) {
    $failures = New-Object System.Collections.Generic.List[string]
    foreach ($a in @(Get-CreateAppActions $token)) {
        $what = "modern command '$($a.Label)' ($($a.UniqueName)) on $($a.Context) calls $($a.Function) (creates $($a.Table))"
        switch ($a.State) {
            'hidden' { Write-Host "[PASS] appaction: $what - hidden" }
            'formula' {
                Write-Host ("[PASS] appaction: $what - Power Fx visibility $($a.Formula). MANUAL CHECK: the formula is " +
                    "Visible = DataSourceInfo(<$($a.Table) data source>, DataSourceInfo.CreatePermission)") -ForegroundColor Yellow
            }
            default { $failures.Add("$what is shown to every user: no Create-privilege visibility (owner decision; README 'Modern commands')") }
        }
    }
    return , $failures
}

# Every command of a ribbon document that calls a launcher: Command, Function, Table, Ruled (references its rule).
function Get-CreateCommands([System.Xml.XmlNode] $ribbon) {
    foreach ($command in @($ribbon.SelectNodes('//*[local-name()="CommandDefinition"]'))) {
        $function = @($command.SelectNodes('./*[local-name()="Actions"]//*[local-name()="JavaScriptFunction"]') |
            ForEach-Object { $_.GetAttribute('FunctionName') } | Where-Object { $launchers.ContainsKey($_) }) | Select-Object -First 1
        if (-not $function) { continue }
        $table = $launchers[$function]
        $ruleId = [string]::Format($config.ruleIdFormat, $table)
        $ruled = [bool] $command.SelectSingleNode("./*[local-name()='DisplayRules']/*[local-name()='DisplayRule' and @Id='$ruleId']")
        [pscustomobject] @{ Command = $command.GetAttribute('Id'); Function = $function; Table = $table; RuleId = $ruleId; Ruled = $ruled }
    }
}

function Test-LiveTable([string] $table, [xml] $ribbon, [string[]] $before) {
    $failures = New-Object System.Collections.Generic.List[string]
    $ids = @(Get-LiveCommandIds $ribbon)
    if ($ids.Count -eq 0) { $failures.Add('the effective ribbon could not be read (no command definitions)') }
    $creates = @(Get-CreateCommands $ribbon)
    foreach ($c in $creates) {
        if (-not $c.Ruled) { $failures.Add("$($c.Command) ($($c.Function)) does not carry $($c.RuleId)"); continue }
        # The DEFINITION (under RuleDefinitions), not a command's reference to it.
        $rule = $ribbon.SelectSingleNode(
            "//*[local-name()='RuleDefinitions']/*[local-name()='DisplayRules']/*[local-name()='DisplayRule' and @Id='$($c.RuleId)']")
        $privilege = if ($rule) { @($rule.ChildNodes | Where-Object { $_.NodeType -eq 'Element' }) } else { @() }
        if ($privilege.Count -ne 1 -or $privilege[0].LocalName -ne 'EntityPrivilegeRule' -or
            $privilege[0].GetAttribute('EntityName') -ne $c.Table -or $privilege[0].GetAttribute('PrivilegeType') -ne 'Create' -or
            $privilege[0].GetAttribute('PrivilegeDepth') -ne 'Basic' -or $privilege[0].HasAttribute('InvertResult')) {
            $failures.Add("$($c.RuleId) is not exactly EntityPrivilegeRule EntityName=$($c.Table) PrivilegeType=Create PrivilegeDepth=Basic")
        }
    }
    foreach ($command in @($before | Where-Object { $_ })) {
        if ($ids -notcontains $command) { $failures.Add("a command present before the import is gone: $command") }
    }
    return [pscustomobject] @{ Creates = $creates; Failures = $failures }
}

function Invoke-Verify([System.Collections.IDictionary] $beforeByTable) {
    $token = Get-DvToken
    $failed = 0
    foreach ($table in $hosts) {
        $before = if ($beforeByTable -and $beforeByTable.Contains($table)) { @($beforeByTable[$table]) } else { @() }
        $result = Test-LiveTable $table (Get-LiveRibbon $table $token) $before
        if ($result.Failures.Count -eq 0) {
            $names = ($result.Creates | ForEach-Object { "$($_.Command) -> $($_.Table)" }) -join '; '
            Write-Host "[PASS] ${table}: $($result.Creates.Count) create command(s) ruled$(if ($names) { " ($names)" })$(if ($before.Count) { "; all $($before.Count) before-commands present" })"
        }
        else {
            $failed++
            foreach ($f in $result.Failures) { Write-Host "[FAIL] ${table}: $f" -ForegroundColor Red }
        }
    }
    foreach ($f in (Test-CreateAppActions $token)) { $failed++; Write-Host "[FAIL] appaction: $f" -ForegroundColor Red }
    return $failed
}

# -- Modes ----------------------------------------------------------------------------------------------------------

if ($Verify) {
    $beforeByTable = $null
    if ($BeforeList) {
        $beforeByTable = @{}
        (Get-Content -Raw -LiteralPath $BeforeList | ConvertFrom-Json).PSObject.Properties |
            ForEach-Object { $beforeByTable[$_.Name] = @($_.Value) }
    }
    $failed = Invoke-Verify $beforeByTable
    if ($failed -gt 0) { Write-Host "VERIFY FAILED on $failed table(s) or modern command(s)." -ForegroundColor Red; exit 1 }
    Write-Host 'VERIFY PASSED.' -ForegroundColor Green
    exit 0
}

if (-not $Apply) {
    # The checked-in sources: every tracked XML outside projects/ that calls a launcher.
    $repoRoot = (& git -C $PSScriptRoot rev-parse --show-toplevel).Trim()
    $grepArgs = @('-C', $repoRoot, 'grep', '-l', '-F')
    foreach ($name in $launchers.Keys) { $grepArgs += @('-e', "FunctionName=`"$name`"") }
    $grepArgs += @('--', '*.xml', ':!projects/**')
    $sources = @(& git @grepArgs | Where-Object { $_ } | ForEach-Object { Join-Path $repoRoot $_ })
    Write-Host "DRY RUN - $($sources.Count) checked-in ribbon source(s) that call a create launcher; nothing is written."
    $rows = @(& $merge -Path $sources -Check)
    $rows | Sort-Object Status, File | Format-Table -AutoSize Status, Table, Command, @{ n = 'File'; e = { $_.File.Substring($repoRoot.Length + 1) } } |
        Out-String -Width 250 | Write-Host
    $missing = @($rows | Where-Object Status -eq 'missing')

    if ($EnvironmentUrl) {
        Write-Host "Live create commands on $EnvironmentUrl (RetrieveEntityRibbon 'All', read-only):"
        $token = Get-DvToken
        foreach ($table in $hosts) {
            foreach ($c in @(Get-CreateCommands (Get-LiveRibbon $table $token))) {
                Write-Host ("  {0,-22} {1,-48} -> {2,-22} {3}" -f $table, $c.Command, $c.Table, $(if ($c.Ruled) { 'ruled' } else { 'NOT ruled yet' }))
            }
        }
        Write-Host 'Live create MODERN commands (appaction, unmanaged, active; read-only):'
        $appActions = @(Get-CreateAppActions $token)
        if ($appActions.Count -eq 0) { Write-Host '  (none)' }
        foreach ($a in $appActions) {
            Write-Host ("  {0,-22} '{1}' {2} -> {3}  {4} (modified {5})" -f $a.Context, $a.Label, $a.UniqueName, $a.Table, $a.State, $a.Modified)
        }
        Write-Host 'Planned -Apply hides (create-launchers.json hideAppActions; owner decision):'
        foreach ($h in @(Get-PlannedAppActionHides $token)) {
            $plan = if ($h.Problem) { "REFUSED: $($h.Problem)" } elseif ($h.Hidden) { 'already hidden (no write)' } else { 'WILL SET hidden = true, publish the app, read back' }
            Write-Host "  $($h.Id) $($h.UniqueName): $plan"
            if ($h.Problem) { $missing += "hideAppActions $($h.Id): $($h.Problem)" }
        }
    }
    if ($missing.Count -gt 0) { Write-Host "DRY RUN FAILED: $($missing.Count) problem(s) - a checked-in command or rule definition missing the Create privilege rule, or a hideAppActions entry that does not match the live row (see above)." -ForegroundColor Red; exit 1 }
    Write-Host 'DRY RUN PASSED: every checked-in create command carries its Create privilege rule.' -ForegroundColor Green
    exit 0
}

# -Apply (LIVE WRITE)
Write-Host "APPLY to $EnvironmentUrl through solution(s): $($Solutions -join ', '). Work folder: $WorkDir"
$token = Get-DvToken
$pacExe = (Get-Command pac -CommandType Application -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in '.cmd', '.exe', '.bat' } | Select-Object -First 1).Source
if (-not $pacExe) { throw 'pac CLI not found: need pac.cmd or pac.exe on PATH.' }

# 1. Before: the live command list and the live create commands of every host table.
$beforeByTable = [ordered] @{}
$liveCreateTables = New-Object System.Collections.Generic.HashSet[string]
foreach ($table in $hosts) {
    $ribbon = Get-LiveRibbon $table $token
    $beforeByTable[$table] = @(Get-LiveCommandIds $ribbon)
    if (@(Get-CreateCommands $ribbon).Count -gt 0) { [void] $liveCreateTables.Add($table) }
}
$beforePath = Join-Path $WorkDir 'before.json'
$beforeByTable | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $beforePath -Encoding utf8
Write-Host "Recorded the live before-list: $beforePath. Tables with live create commands: $(@($liveCreateTables) -join ', ')"
$plannedHides = @(Get-PlannedAppActionHides $token)
foreach ($h in $plannedHides) {
    if ($h.Problem) { throw "hideAppActions $($h.Id) ($($h.UniqueName)): $($h.Problem). Nothing was written." }
}
$hideIds = @($plannedHides | ForEach-Object Id)
foreach ($a in @(Get-CreateAppActions $token | Where-Object State -eq 'UNRULED')) {
    if ($hideIds -contains $a.Id) { continue }
    Write-Host ("Modern command '$($a.Label)' ($($a.UniqueName)) creates $($a.Table), is shown to every user and is not in " +
        "hideAppActions: -Apply does not change it; the verify below FAILS on it (owner decision needed).") -ForegroundColor Yellow
}

# 2. Export every solution FIRST and check each export against the environment. Nothing is imported unless all pass.
$unpackedBySolution = [ordered] @{}
$covered = New-Object System.Collections.Generic.HashSet[string]
foreach ($solution in $Solutions) {
    $zip = Join-Path $WorkDir "$solution.zip"
    $unpacked = Join-Path $WorkDir "$solution.unpacked"
    & $pacExe solution export --environment $EnvironmentUrl --name $solution --path $zip --overwrite
    if ($LASTEXITCODE -ne 0) { throw "pac solution export $solution failed ($LASTEXITCODE). Nothing was imported." }
    & $pacExe solution unpack --zipfile $zip --folder $unpacked --packagetype Unmanaged --allowDelete true
    if ($LASTEXITCODE -ne 0) { throw "pac solution unpack $solution failed ($LASTEXITCODE). Nothing was imported." }
    $unpackedBySolution[$solution] = $unpacked

    $entitiesDir = Join-Path $unpacked 'Entities'
    $ribbonDiffs = if (Test-Path -LiteralPath $entitiesDir) { @(Get-ChildItem -LiteralPath $entitiesDir -Recurse -Filter 'RibbonDiff.xml') } else { @() }
    $lost = @(& (Join-Path (Split-Path -Parent $PSScriptRoot) 'Test-RibbonExportCurrent.ps1') -EnvironmentUrl $EnvironmentUrl `
        -Token $token -UnpackedDir $unpacked)
    if ($lost.Count -gt 0) {
        throw "${solution}: the export lacks, or holds an older version of, what the environment holds ($($lost -join '; ')); importing it would delete them. Nothing was imported."
    }
    foreach ($ribbonDiff in $ribbonDiffs) { [void] $covered.Add($ribbonDiff.Directory.Name.ToLowerInvariant()) }
}
$uncovered = @($liveCreateTables | Where-Object { -not $covered.Contains($_) })
if ($uncovered.Count -gt 0) {
    throw "Live create commands on table(s) in none of the solutions: $($uncovered -join ', '). Add a solution holding them to -Solutions. Nothing was imported."
}

# 3. Merge (every exported RibbonDiff; idempotent, a table in two solutions gets the same result in both).
foreach ($solution in $unpackedBySolution.Keys) {
    $ribbonDiffs = @(Get-ChildItem -LiteralPath (Join-Path $unpackedBySolution[$solution] 'Entities') -Recurse -Filter 'RibbonDiff.xml' -ErrorAction SilentlyContinue)
    if ($ribbonDiffs.Count -eq 0) { continue }
    $rows = @(& $merge -Path ($ribbonDiffs | ForEach-Object FullName))
    foreach ($r in $rows) { Write-Host "  $solution  $($r.Status.PadRight(7)) $($r.Command) -> $($r.Table)" }
}

# 4. Pack and import each solution, with publish.
foreach ($solution in $unpackedBySolution.Keys) {
    $packed = Join-Path $WorkDir "$solution.merged.zip"
    & $pacExe solution pack --zipfile $packed --folder $unpackedBySolution[$solution] --packagetype Unmanaged
    if ($LASTEXITCODE -ne 0) { throw "pac solution pack $solution failed ($LASTEXITCODE)." }
    & $pacExe solution import --environment $EnvironmentUrl --path $packed --publish-changes
    if ($LASTEXITCODE -ne 0) { throw "pac solution import $solution failed ($LASTEXITCODE). Solutions before it in -Solutions were imported; re-run -Apply (idempotent)." }
}

# 4b. The owner-decided appaction hides: PATCH hidden = true where it is not yet, then ALWAYS publish the app(s) of the
# listed hides (a re-run heals a publish that failed after the PATCH), then read every row back.
if ($plannedHides.Count -gt 0) {
    $writeHeaders = @{
        Authorization = "Bearer $token"; Accept = 'application/json'; 'Content-Type' = 'application/json'
        'OData-Version' = '4.0'; 'OData-MaxVersion' = '4.0'; 'If-Match' = '*'
    }
    foreach ($h in $plannedHides) {
        if ($h.Hidden) { Write-Host "appaction $($h.UniqueName): already hidden (no PATCH; the app is still published below)"; continue }
        Invoke-RestMethod -Method Patch -Uri "$EnvironmentUrl/api/data/v9.2/appactions($($h.Id))" -Headers $writeHeaders `
            -Body '{"hidden":true}' | Out-Null
        Write-Host "appaction $($h.UniqueName): hidden = true written"
    }
    $appModules = (@($plannedHides | ForEach-Object AppModuleId | Where-Object { $_ } | Select-Object -Unique) |
        ForEach-Object { "<appmodule>$_</appmodule>" }) -join ''
    $publish = @{ ParameterXml = "<importexportxml><appmodules>$appModules</appmodules></importexportxml>" } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri "$EnvironmentUrl/api/data/v9.2/PublishXml" -Headers $writeHeaders -Body $publish | Out-Null
    Write-Host "Published app module(s): $appModules"
    foreach ($h in $plannedHides) {
        $back = Invoke-Dv "appactions($($h.Id))?`$select=hidden,uniquename" $token
        if (-not $back.hidden) { throw "appaction $($h.UniqueName): hidden did not read back as true." }
        Write-Host "appaction $($h.UniqueName): hidden = true (read back)" -ForegroundColor Green
    }
}

# 5. Verify, retrying while the effective ribbon catches up with the publish.
$retryDelaysSeconds = @(15, 30, 45, 60, 30)
$failed = Invoke-Verify $beforeByTable
$attempt = 1
foreach ($delay in $retryDelaysSeconds) {
    if ($failed -eq 0) { break }
    Write-Host "Verify attempt $attempt failed on $failed table(s) or modern command(s); the effective ribbon can lag the publish - retrying in $delay s." -ForegroundColor Yellow
    Start-Sleep -Seconds $delay
    $attempt++
    $failed = Invoke-Verify $beforeByTable
}
if ($failed -gt 0) {
    Write-Host "APPLIED, but VERIFY FAILED on $failed table(s) or modern command(s) after $attempt attempts - see above." -ForegroundColor Red
    exit 1
}
Write-Host 'APPLIED and VERIFIED. Then run the live gate: a read-only role sees no custom create command (README).' -ForegroundColor Green
exit 0
