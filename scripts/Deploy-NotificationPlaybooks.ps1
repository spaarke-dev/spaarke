<#
.SYNOPSIS
    Deploys the 7 notification playbooks to a Dataverse environment: creates a playbook that is missing, and brings an
    existing one's node rows to the repo definition IN PLACE.

.DESCRIPTION
    Source: projects/spaarke-daily-update-service/notes/playbooks/notification-*.json (the repo definition is the truth).

    For each definition:
      1. Lint C (scripts/common/Assert-PlaybookFetchXmlShape.ps1 - the BFF's own FetchXmlShapeValidator) and an
         executorType check on every node. A failing definition is never written.
      2. Playbook absent (by name, sprk_playbooktype = 2) -> created by Deploy-Playbook.ps1.
      3. Playbook present -> SYNC, never delete-and-recreate (ids, sprk_lastrundate and the run history stay):
           - each repo node is matched to the live row of the same sprk_name and PATCHed (sprk_configjson,
             sprk_executortype, sprk_executionorder, sprk_outputvariable, sprk_isactive = true); a repo node with no
             live row is created;
           - sprk_dependsonjson is rewritten from the repo dependsOn names;
           - the playbook row's sprk_description and sprk_configjson are set to the repo playbook block (this removes the
             stale `nodes` copy older deploys wrote into the playbook blob - the orchestrator never read it);
           - every node is READ BACK and compared with the definition; any difference fails the script.
         A live node that the definition does not name, or two live nodes with one name, stops the sync for that
         playbook before any write (removing rows is an owner decision, not this script's).
      With -RecordPath, the before and after state of every playbook and node is written there as JSON
      (<file>.before.json / <file>.after.json) - the per-node before/after record ISS-018 (D-79) asks for.

    History: until ISS-018 (#1452) this script passed -PlaybookDefinitionPath, a parameter Deploy-Playbook.ps1 does not
    have, and took no -DataverseUrl (docs/procedures/production-release.md passes one), so it never deployed anything;
    Deploy-Playbook.ps1 also skips a playbook that exists, so an existing environment's nodes could not be updated.

.PARAMETER DataverseUrl
    Target environment, e.g. https://spaarkedev1.crm.dynamics.com. Defaults to $env:DATAVERSE_URL.

.PARAMETER DryRun
    Lint and show what would change (per node: create / update / unchanged); write nothing.

.PARAMETER RecordPath
    Folder for the before/after JSON records.

.PARAMETER Only
    Restrict to these definition file names (e.g. notification-tasks-overdue.json).

.EXAMPLE
    .\Deploy-NotificationPlaybooks.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -DryRun
    .\Deploy-NotificationPlaybooks.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -RecordPath .\iss018-records
#>
[CmdletBinding()]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [switch]$DryRun,
    [string]$RecordPath,
    [string[]]$Only
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$playbookDir = Join-Path $scriptDir '..\projects\spaarke-daily-update-service\notes\playbooks'
$deployScript = Join-Path $scriptDir 'Deploy-Playbook.ps1'
. (Join-Path $scriptDir 'common\Assert-PlaybookFetchXmlShape.ps1')

if (-not $DataverseUrl) { throw 'DataverseUrl is required (parameter or $env:DATAVERSE_URL).' }
$DataverseUrl = $DataverseUrl.TrimEnd('/')
$ApiBase = "$DataverseUrl/api/data/v9.2"
$NotificationPlaybookType = 2

$playbooks = @(
    'notification-tasks-overdue.json',
    'notification-tasks-due-soon.json',
    'notification-new-documents.json',
    'notification-new-emails.json',
    'notification-new-events.json',
    'notification-matter-activity.json',
    'notification-work-assignments.json'
)
if ($Only) { $playbooks = @($playbooks | Where-Object { $_ -in $Only }) }
if ($RecordPath -and -not (Test-Path -LiteralPath $RecordPath)) { New-Item -ItemType Directory -Path $RecordPath | Out-Null }

function Get-Headers {
    $token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv 2>$null
    if (-not $token) { throw "Failed to acquire a Dataverse token for $DataverseUrl. Run 'az login' first." }
    return @{
        'Authorization'    = "Bearer $token"
        'Accept'           = 'application/json'
        'Content-Type'     = 'application/json; charset=utf-8'
        'OData-MaxVersion' = '4.0'
        'OData-Version'    = '4.0'
    }
}

function Invoke-Dv {
    param([string]$Method, [string]$Path, $Body)
    $params = @{ Uri = "$ApiBase/$Path"; Method = $Method; Headers = $script:Headers }
    if ($null -ne $Body) {
        $params['Body'] = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 64 -Compress))
    }
    if ($Method -eq 'Post') {
        $response = Invoke-WebRequest @params
        $location = [string]($response.Headers['OData-EntityId'] | Select-Object -First 1)
        if ($location -match '\(([0-9a-fA-F-]{36})\)') { return $Matches[1] }
        throw "POST $Path returned no record id."
    }
    return Invoke-RestMethod @params
}

function ConvertTo-CanonicalJson {
    # One text form for comparing a definition config with the stored sprk_configjson.
    param($Value)
    if ($null -eq $Value) { return '' }
    if ($Value -is [string]) {
        if ([string]::IsNullOrWhiteSpace($Value)) { return '' }
        $Value = $Value | ConvertFrom-Json -Depth 64
    }
    return ($Value | ConvertTo-Json -Depth 64 -Compress)
}

function Get-LiveState {
    param([string]$PlaybookId)
    $playbook = Invoke-Dv -Method Get -Path "sprk_analysisplaybooks($PlaybookId)?`$select=sprk_name,sprk_description,sprk_configjson,sprk_lastrundate"
    $nodes = (Invoke-Dv -Method Get -Path ("sprk_playbooknodes?`$select=sprk_playbooknodeid,sprk_name,sprk_configjson,sprk_dependsonjson," +
        "sprk_outputvariable,sprk_executionorder,sprk_executortype,sprk_isactive&`$filter=_sprk_playbookid_value eq $PlaybookId")).value
    return [pscustomobject]@{
        playbook = [pscustomobject]@{
            id = $PlaybookId; name = $playbook.sprk_name; description = $playbook.sprk_description
            configjson = $playbook.sprk_configjson; lastrundate = $playbook.sprk_lastrundate
        }
        nodes = @($nodes | Sort-Object sprk_executionorder | ForEach-Object {
            [pscustomobject]@{
                id = $_.sprk_playbooknodeid; name = $_.sprk_name; executortype = $_.sprk_executortype
                executionorder = $_.sprk_executionorder; outputvariable = $_.sprk_outputvariable; isactive = $_.sprk_isactive
                dependsonjson = $_.sprk_dependsonjson; configjson = $_.sprk_configjson
            }
        })
    }
}

function Sync-Playbook {
    param($Definition, [string]$PlaybookId, [string]$File)

    $before = Get-LiveState -PlaybookId $PlaybookId
    if ($RecordPath) { $before | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath (Join-Path $RecordPath "$File.before.json") -Encoding utf8 }

    $repoNames = @($Definition.nodes | ForEach-Object { [string]$_.name })
    $duplicates = @($before.nodes | Group-Object name | Where-Object Count -gt 1 | ForEach-Object Name)
    if ($duplicates.Count -gt 0) { throw "$File : live playbook has more than one node named: $($duplicates -join ', '). Nothing written." }
    $extra = @($before.nodes | Where-Object { $_.name -notin $repoNames } | ForEach-Object name)
    if ($extra.Count -gt 0) { throw "$File : live nodes not in the definition: $($extra -join ', '). Removing them is an owner decision. Nothing written." }

    $liveByName = @{}
    foreach ($n in $before.nodes) { $liveByName[[string]$n.name] = $n }
    $idByName = @{}
    $order = 0
    foreach ($node in $Definition.nodes) {
        $order++
        $name = [string]$node.name
        $desired = @{
            sprk_name = $name
            sprk_configjson = (ConvertTo-CanonicalJson $node.configJson)
            sprk_executortype = [int]$node.executorType
            sprk_executionorder = $order
            sprk_outputvariable = [string]$node.outputVariable
            sprk_isactive = $true
        }
        $live = $liveByName[$name]
        if ($null -eq $live) {
            Write-Host "    + create node '$name' (executorType $($desired.sprk_executortype))" -ForegroundColor Yellow
            if (-not $DryRun) {
                $desired['sprk_playbookid@odata.bind'] = "sprk_analysisplaybooks($PlaybookId)"
                $idByName[$name] = Invoke-Dv -Method Post -Path 'sprk_playbooknodes' -Body $desired
            }
            continue
        }

        $idByName[$name] = $live.id
        $changed = @()
        if ((ConvertTo-CanonicalJson $live.configjson) -ne $desired.sprk_configjson) { $changed += 'configjson' }
        if ($live.executortype -ne $desired.sprk_executortype) { $changed += "executortype $($live.executortype)->$($desired.sprk_executortype)" }
        if ($live.executionorder -ne $order) { $changed += "executionorder $($live.executionorder)->$order" }
        if ($live.outputvariable -ne $desired.sprk_outputvariable) { $changed += "outputvariable $($live.outputvariable)->$($desired.sprk_outputvariable)" }
        if (-not $live.isactive) { $changed += 'isactive false->true' }
        if ($changed.Count -eq 0) {
            Write-Host "    = node '$name' unchanged" -ForegroundColor Gray
        } else {
            Write-Host "    ~ update node '$name' ($($changed -join '; '))" -ForegroundColor Yellow
            if (-not $DryRun) { Invoke-Dv -Method Patch -Path "sprk_playbooknodes($($live.id))" -Body $desired | Out-Null }
        }
    }

    if ($DryRun) {
        Write-Host "    (dry run: dependsOn, playbook description/config and read-back skipped)" -ForegroundColor Gray
        return
    }

    foreach ($node in $Definition.nodes) {
        $deps = @($node.dependsOn | Where-Object { $_ } | ForEach-Object {
            $key = [string]$_
            if (-not $idByName.ContainsKey($key)) { throw "$File : node '$($node.name)' depends on '$key', which the definition does not define." }
            $idByName[$key]
        })
        $json = if ($deps.Count -eq 0) { $null } else { ConvertTo-Json -InputObject @($deps) -Compress }
        Invoke-Dv -Method Patch -Path "sprk_playbooknodes($($idByName[[string]$node.name]))" -Body @{ sprk_dependsonjson = $json } | Out-Null
    }

    Invoke-Dv -Method Patch -Path "sprk_analysisplaybooks($PlaybookId)" -Body @{
        sprk_description = [string]$Definition.playbook.description
        sprk_configjson = (ConvertTo-CanonicalJson $Definition.playbook.sprk_configjson)
    } | Out-Null

    # Read back and compare every node with the definition.
    $after = Get-LiveState -PlaybookId $PlaybookId
    if ($RecordPath) { $after | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath (Join-Path $RecordPath "$File.after.json") -Encoding utf8 }
    $mismatches = @()
    foreach ($node in $Definition.nodes) {
        $live = $after.nodes | Where-Object name -eq ([string]$node.name)
        if ($null -eq $live) { $mismatches += "$($node.name): missing after sync"; continue }
        if ((ConvertTo-CanonicalJson $live.configjson) -ne (ConvertTo-CanonicalJson $node.configJson)) { $mismatches += "$($node.name): configjson differs" }
        if ($live.executortype -ne [int]$node.executorType) { $mismatches += "$($node.name): executortype $($live.executortype)" }
        if (-not $live.isactive) { $mismatches += "$($node.name): inactive" }
        $expectedDeps = @($node.dependsOn | Where-Object { $_ } | ForEach-Object { $idByName[[string]$_] })
        $liveDeps = if ($live.dependsonjson) { @($live.dependsonjson | ConvertFrom-Json) } else { @() }
        if ((@($liveDeps) -join ',') -ne ($expectedDeps -join ',')) { $mismatches += "$($node.name): dependsOn differs" }
    }
    if (@($after.nodes).Count -ne @($Definition.nodes).Count) { $mismatches += "node count $(@($after.nodes).Count) <> $(@($Definition.nodes).Count)" }
    if ((ConvertTo-CanonicalJson $after.playbook.configjson) -ne (ConvertTo-CanonicalJson $Definition.playbook.sprk_configjson)) { $mismatches += 'playbook configjson differs' }
    if ($mismatches.Count -gt 0) { throw "$File : read-back differs from the definition: $($mismatches -join ' | ')" }
    Write-Host "    read-back: all $(@($after.nodes).Count) nodes and the playbook row equal the definition" -ForegroundColor Green
}

Write-Host "`n=== Deploying Notification Playbooks ===" -ForegroundColor Cyan
Write-Host "  Source:      $playbookDir" -ForegroundColor Gray
Write-Host "  Environment: $DataverseUrl" -ForegroundColor Gray
Write-Host "  Count:       $($playbooks.Count)" -ForegroundColor Gray
if ($DryRun) { Write-Host '  Mode:        DRY RUN' -ForegroundColor Yellow }
Write-Host ''

$script:Headers = Get-Headers
$succeeded = 0
$failed = 0

foreach ($file in $playbooks) {
    $path = Join-Path $playbookDir $file
    Write-Host "  $file" -ForegroundColor White
    try {
        if (-not (Test-Path -LiteralPath $path)) { throw 'definition file not found' }
        $definition = Get-Content -LiteralPath $path -Raw -Encoding utf8 | ConvertFrom-Json -Depth 64
        Assert-PlaybookFetchXmlShape -Definition $definition -Source $file
        $untyped = @($definition.nodes | Where-Object { $null -eq $_.executorType } | ForEach-Object name)
        if ($untyped.Count -gt 0) { throw "nodes without executorType: $($untyped -join ', ')" }

        $name = ([string]$definition.playbook.name).Replace("'", "''")
        $existing = @((Invoke-Dv -Method Get -Path ("sprk_analysisplaybooks?`$select=sprk_analysisplaybookid&`$filter=" +
            [uri]::EscapeDataString("sprk_name eq '$name' and sprk_playbooktype eq $NotificationPlaybookType"))).value)
        if ($existing.Count -gt 1) { throw "more than one notification playbook named '$($definition.playbook.name)'" }

        if ($existing.Count -eq 0) {
            Write-Host "    playbook absent -> create with Deploy-Playbook.ps1" -ForegroundColor Yellow
            $deployArgs = @{ DefinitionFile = $path; DataverseUrl = $DataverseUrl }
            if ($DryRun) { $deployArgs['DryRun'] = $true }
            & $deployScript @deployArgs
        } else {
            Sync-Playbook -Definition $definition -PlaybookId $existing[0].sprk_analysisplaybookid -File $file
        }
        $succeeded++
        Write-Host "  OK: $file" -ForegroundColor Green
    } catch {
        $failed++
        Write-Host "  FAILED: $file - $($_.Exception.Message)" -ForegroundColor Red
    }
    Write-Host ''
}

Write-Host '=== Deployment Complete ===' -ForegroundColor Cyan
Write-Host "  Succeeded: $succeeded / $($playbooks.Count)" -ForegroundColor $(if ($failed -eq 0) { 'Green' } else { 'Yellow' })
if ($failed -gt 0) {
    Write-Host "  Failed:    $failed" -ForegroundColor Red
    exit 1
}
