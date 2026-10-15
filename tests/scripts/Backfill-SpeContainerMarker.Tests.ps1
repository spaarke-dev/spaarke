# tests/scripts/Backfill-SpeContainerMarker.Tests.ps1
# ---------------------------------------------------------------------------
# GitHub issue #1889: per-record (secure) SPE containers created before the customer marker shipped (task 227d)
# carry no marker, and the BFF's SpeContainerOwnershipGuard refuses them forever. scripts/
# Backfill-SpeContainerBusinessUnitStamp.ps1 -CustomerId marks them. These tests prove, with no live call:
#   1. the pure per-container decision (Get-SpeContainerMarkerDecision): each outcome, a case-differing id is
#      MarkerForeign, an underivable container is never Mark, a blank -CustomerId means no marker pass, a container the
#      run stamps from records alone is MarkNextRun (never Mark), an earlier-bound unclaimed container is UnmarkedNoClaim;
#   2. Get-SpeContainerBinding tells an ABSENT marker from an AMBIGUOUS one (the backfill must not write over the latter);
#   3. the whole script, end to end against an in-memory Dataverse + Graph (child pwsh with `az` and
#      Invoke-RestMethod shadowed): dry run writes nothing; -Apply marks only derivable, undisputed, unmarked containers;
#      it is idempotent; -MaxWritesPerRun is honoured; a read-back failure is a failure; -Verify fails on unmarked /
#      foreign / malformed; -RevertManifest removes a marker only where it is still this run's; an old stamp-only
#      manifest still reverts; a blank or malformed -CustomerId is refused before any call; without -CustomerId nothing
#      about the marker is read or written; no container is ever deleted. SAFETY (cross-customer exposure): a FOREIGN or
#      MISMATCH stamp and a -Bind conflicting with the stamp are never marked; a record claim alone never marks the
#      container in the pass that stamps it (MARK-NEXT-RUN, -Verify fails, the next -Apply marks it); an unclaimed
#      container stamped by an earlier -Bind is UNMARKED-NO-CLAIM; the marker's manifest row is on disk before its write.
#
# Pester 3.4:  Import-Module Pester -RequiredVersion 3.4.0 ; Invoke-Pester tests/scripts/Backfill-SpeContainerMarker.Tests.ps1
# Run under PowerShell 7 (the script is a pwsh script and the harness child is `pwsh`).
# ---------------------------------------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$script:Backfill = Join-Path $repoRoot 'scripts/Backfill-SpeContainerBusinessUnitStamp.ps1'
. (Join-Path $repoRoot 'scripts/common/SpeContainerBinding.ps1')

$script:Root = '11111111-1111-1111-1111-111111111111'
$script:Unit2 = '22222222-2222-2222-2222-222222222222'
$script:ForeignUnit = '99999999-9999-9999-9999-999999999999'   # a unit this environment's hierarchy does not hold
$script:Cust = 'acme'

# ── 1. The pure decision ────────────────────────────────────────────────────────────────────────────────────────
Describe 'Get-SpeContainerMarkerDecision' {
    It 'is Mark for a derivable, undisputed container that was ALREADY stamped at the first read and whose marker is absent' {
        Get-SpeContainerMarkerDecision -CustomerId $script:Cust -Derivable $true -StampState 'Stamped' -Marker $null | Should Be 'Mark'
    }
    It 'is Mark for an unstamped container only when an explicit -Bind names it' {
        Get-SpeContainerMarkerDecision -CustomerId $script:Cust -Derivable $true -StampState 'Unstamped' -Marker $null -BindNamed $true | Should Be 'Mark'
    }
    It 'is MarkNextRun - never Mark - for an unstamped container derivable from records alone (a customer-writable claim)' {
        Get-SpeContainerMarkerDecision -CustomerId $script:Cust -Derivable $true -StampState 'Unstamped' -Marker $null | Should Be 'MarkNextRun'
        Get-SpeContainerMarkerDecision -CustomerId $script:Cust -Derivable $true -StampState 'Unstamped' -Marker $null -BindNamed $false | Should Be 'MarkNextRun'
    }
    It 'is AlreadyMarked when the marker equals the customer id (trimmed)' {
        Get-SpeContainerMarkerDecision -CustomerId $script:Cust -Derivable $true -StampState 'Stamped' -Marker 'acme' | Should Be 'AlreadyMarked'
        Get-SpeContainerMarkerDecision -CustomerId '  acme ' -Derivable $true -StampState 'Stamped' -Marker 'acme' | Should Be 'AlreadyMarked'
    }
    It 'is MarkerForeign for a different value' {
        Get-SpeContainerMarkerDecision -CustomerId $script:Cust -Derivable $true -StampState 'Stamped' -Marker 'other-customer' | Should Be 'MarkerForeign'
    }
    It 'is MarkerForeign - not AlreadyMarked - for a case-differing customer id (ordinal compare)' {
        Get-SpeContainerMarkerDecision -CustomerId 'ACME' -Derivable $true -StampState 'Stamped' -Marker 'acme' | Should Be 'MarkerForeign'
        Get-SpeContainerMarkerDecision -CustomerId 'acme' -Derivable $true -StampState 'Stamped' -Marker 'ACME' | Should Be 'MarkerForeign'
    }
    It 'is MarkerMalformed for an ambiguous marker, never Mark' {
        Get-SpeContainerMarkerDecision -CustomerId $script:Cust -Derivable $true -StampState 'Stamped' -Marker $null -MarkerMalformed $true | Should Be 'MarkerMalformed'
    }
    It 'is SkipUnderivable for an underivable container that is not stamped for this environment - never Mark' {
        foreach ($stamp in 'Unstamped', 'Mismatch', 'Foreign', 'Malformed') {
            foreach ($marker in @($null, 'acme', 'other')) {
                Get-SpeContainerMarkerDecision -CustomerId $script:Cust -Derivable $false -StampState $stamp -Marker $marker | Should Be 'SkipUnderivable'
            }
        }
        Get-SpeContainerMarkerDecision -CustomerId $script:Cust -Derivable $false -StampState 'Unstamped' -Marker $null -MarkerMalformed $true | Should Be 'SkipUnderivable'
        # stamped for this environment, no claim, but already marked for this customer: nothing to say
        Get-SpeContainerMarkerDecision -CustomerId $script:Cust -Derivable $false -StampState 'Stamped' -Marker 'acme' | Should Be 'SkipUnderivable'
    }
    It 'is UnmarkedNoClaim - never Mark - for a container stamped for this environment that no record claims and that is not marked for this customer' {
        Get-SpeContainerMarkerDecision -CustomerId $script:Cust -Derivable $false -StampState 'Stamped' -Marker $null | Should Be 'UnmarkedNoClaim'
        Get-SpeContainerMarkerDecision -CustomerId $script:Cust -Derivable $false -StampState 'Stamped' -Marker 'other' | Should Be 'UnmarkedNoClaim'
        Get-SpeContainerMarkerDecision -CustomerId $script:Cust -Derivable $false -StampState 'Stamped' -Marker 'ACME' | Should Be 'UnmarkedNoClaim'
        Get-SpeContainerMarkerDecision -CustomerId $script:Cust -Derivable $false -StampState 'Stamped' -Marker $null -MarkerMalformed $true | Should Be 'UnmarkedNoClaim'
    }
    It 'is SkipDisputedStamp for a MISMATCH / FOREIGN / MALFORMED stamp - never Mark, even with -Bind' {
        foreach ($stamp in 'Mismatch', 'Foreign', 'Malformed') {
            foreach ($bind in $true, $false) {
                Get-SpeContainerMarkerDecision -CustomerId $script:Cust -Derivable $true -StampState $stamp -Marker $null -BindNamed $bind | Should Be 'SkipDisputedStamp'
            }
        }
    }
    It 'runs no marker pass (returns nothing) when -CustomerId is absent or blank' {
        Get-SpeContainerMarkerDecision -CustomerId $null -Derivable $true -StampState 'Stamped' -Marker $null | Should BeNullOrEmpty
        Get-SpeContainerMarkerDecision -CustomerId '' -Derivable $true -StampState 'Stamped' -Marker $null | Should BeNullOrEmpty
        Get-SpeContainerMarkerDecision -CustomerId '   ' -Derivable $true -StampState 'Stamped' -Marker $null | Should BeNullOrEmpty
    }
    It 'only ever answers one of the eight outcomes, and answers Mark only on basis (a) Stamped or (b) -Bind' {
        $allowed = 'Mark', 'MarkNextRun', 'AlreadyMarked', 'MarkerForeign', 'MarkerMalformed', 'SkipUnderivable', 'UnmarkedNoClaim', 'SkipDisputedStamp'
        foreach ($d in $true, $false) { foreach ($st in 'Unstamped', 'Stamped', 'Mismatch', 'Foreign', 'Malformed') {
            foreach ($m in @($null, 'acme', 'x')) { foreach ($mm in $true, $false) { foreach ($b in $true, $false) {
                $r = Get-SpeContainerMarkerDecision -CustomerId 'acme' -Derivable $d -StampState $st -Marker $m -MarkerMalformed $mm -BindNamed $b
                $allowed -contains $r | Should Be $true
                if ($r -eq 'Mark') { $d | Should Be $true; ($st -eq 'Stamped' -or $b) | Should Be $true }
            } } }
        } }
    }
}

# ── 2. Absent vs ambiguous marker in the shared reader ──────────────────────────────────────────────────────────────
Describe 'Get-SpeContainerBinding marker reading' {
    function Set-FakeContainer {
        param([hashtable]$Props)
        $cp = [ordered]@{}
        foreach ($k in $Props.Keys) { $cp[$k] = @{ value = $Props[$k]; isSearchable = $false } }
        $global:FakeContainer = [pscustomobject]@{ id = 'b!x'; containerTypeId = '33333333-3333-3333-3333-333333333333'; customProperties = [pscustomobject]$cp }
    }
    function Invoke-SpeGraph { param($Token, $Method, $Uri, $Body) return $global:FakeContainer }

    It 'reads an absent marker as Marker $null, not malformed' {
        Set-FakeContainer @{ spaarkeBusinessUnitId = $script:Unit2 }
        $b = Get-SpeContainerBinding -Token t -ContainerId 'b!x'
        $b.Marker | Should BeNullOrEmpty
        $b.MarkerMalformed | Should Be $false
    }
    It 'reads one marker as its trimmed value' {
        Set-FakeContainer @{ spaarkeCustomerId = ' acme ' }
        $b = Get-SpeContainerBinding -Token t -ContainerId 'b!x'
        $b.Marker | Should BeExactly 'acme'
        $b.MarkerMalformed | Should Be $false
    }
    It 'reads two marker entries as malformed (Marker $null)' {
        Set-FakeContainer @{ spaarkeCustomerId = 'a'; ' spaarkeCustomerId' = 'b' }
        $b = Get-SpeContainerBinding -Token t -ContainerId 'b!x'
        $b.Marker | Should BeNullOrEmpty
        $b.MarkerMalformed | Should Be $true
    }
    It 'reads one blank marker as malformed' {
        Set-FakeContainer @{ spaarkeCustomerId = '   ' }
        $b = Get-SpeContainerBinding -Token t -ContainerId 'b!x'
        $b.MarkerMalformed | Should Be $true
    }
}

# ── 3. The whole script, end to end against an in-memory world ─────────────────────────────────────────────────────
# The harness runs in a CHILD pwsh (the script calls `exit`): `az` and Invoke-RestMethod are shadowed by functions
# (function beats cmdlet/application in command resolution) serving the world in $World JSON; every Graph PATCH / DELETE
# and every call is recorded; the world is saved back on exit.
$script:Harness = @'
param([string]$WorldPath, [string]$ScriptPath, [string]$ArgsJson)
$ErrorActionPreference = 'Stop'
$global:W = Get-Content -Raw $WorldPath | ConvertFrom-Json -AsHashtable
$global:CallLog = Join-Path (Split-Path $WorldPath) 'calls.log'
$global:Dv = 'https://fake.crm.dynamics.com/api/data/v9.2/'
$global:Gr = 'https://graph.microsoft.com/beta/storage/fileStorage/containers'
function Save-World { $global:W | ConvertTo-Json -Depth 12 | Set-Content -Path $WorldPath -Encoding UTF8 }
function az { Add-Content $global:CallLog "az $($args -join ' ')"; $global:LASTEXITCODE = 0; if ($args -contains 'keyvault') { 'fake-secret' } else { 'fake-dv-token' } }
function Invoke-RestMethod {
    param([string]$Uri, [string]$Method = 'Get', $Headers, $ContentType, $Body, $ErrorAction)
    $m = $Method.ToUpperInvariant()
    Add-Content $global:CallLog "$m $Uri"
    if ($Uri -like 'https://login.microsoftonline.com/*') { return [pscustomobject]@{ access_token = 'graph-token' } }
    if ($Uri.StartsWith($global:Dv)) {
        $rel = $Uri.Substring($global:Dv.Length)
        if ($rel -like 'sprk_speenvironments(*') { return [pscustomobject]@{ sprk_tenantid = 'tenant-1' } }
        $rows = if ($rel -like 'businessunits?*') { $global:W.units }
            elseif ($rel -like 'sprk_specontainertypeconfigs?*') { $global:W.configs }
            elseif ($rel -like 'sprk_matters?*') { $global:W.matters }
            elseif ($rel -like 'sprk_projects?*') { $global:W.projects }
            elseif ($rel -like 'sprk_workassignments?*') { $global:W.workassignments }
            elseif ($rel -like 'sprk_speauditlogs?*') { $global:W.audits }
            else { throw "FAKE: unexpected Dataverse call $rel" }
        return [pscustomobject]@{ value = @($rows | ForEach-Object { [pscustomobject]$_ }) }
    }
    if ($Uri.StartsWith($global:Gr)) {
        $rest = $Uri.Substring($global:Gr.Length)
        if ($rest.StartsWith('?')) {
            return [pscustomobject]@{ value = @($global:W.containers.Keys | Sort-Object | ForEach-Object { [pscustomobject]@{ id = $_; displayName = $global:W.containers[$_].name } }) }
        }
        $segs = ($rest.Substring(1) -split '\?')[0] -split '/'
        $id = [uri]::UnescapeDataString($segs[0])
        $c = $global:W.containers[$id]
        if ($m -eq 'DELETE') { $global:W.deletes = @($global:W.deletes) + @($id); return }
        if ($m -eq 'PATCH') {
            $global:W.patches = @($global:W.patches) + @(@{ id = $id; body = $Body })
            $patch = $Body | ConvertFrom-Json -AsHashtable
            foreach ($k in @($patch.Keys)) {
                if ($k -ieq 'spaarkeCustomerId' -and @($global:W.throwMarkerPatchFor) -contains $id) { throw "FAKE 500 on the marker PATCH of $id" }
                if ($k -ieq 'spaarkeCustomerId' -and @($global:W.dropMarkerPatchFor) -contains $id) { continue }
                if ($null -eq $patch[$k]) { foreach ($e in @($c.props.Keys)) { if ($e -ceq $k) { $c.props.Remove($e) } } }
                else { $c.props[$k] = $patch[$k].value }
            }
            return
        }
        if (-not $c) { throw "FAKE 404 $id" }
        $cp = [ordered]@{}
        foreach ($k in $c.props.Keys) { $cp[$k] = @{ value = $c.props[$k]; isSearchable = $false } }
        return [pscustomobject]@{ id = $id; containerTypeId = $global:W.typeId; customProperties = [pscustomobject]$cp }
    }
    throw "FAKE: unexpected call $m $Uri"
}
$a = $ArgsJson | ConvertFrom-Json -AsHashtable
try { & $ScriptPath @a } finally { }
$code = $LASTEXITCODE
Save-World
exit $code
'@

function New-World {
    param([string[]]$Only)
    $keys = if ($Only) { $Only } else { 'cA', 'cB', 'cC', 'cD', 'cE', 'cF', 'cG', 'cH', 'cI' }
    $stamp = 'spaarkeBusinessUnitId'; $marker = 'spaarkeCustomerId'
    $all = [ordered]@{
        # derivable from the root unit's default container; unstamped, unmarked -> stamped, then MARK-NEXT-RUN (not marked in that pass)
        'cA' = @{ name = 'unit default'; props = @{} }
        # the #1889 case: secure project container, correctly stamped, no marker -> mark only
        'cB' = @{ name = 'secure project (pre-marker)'; props = @{ $stamp = $script:Unit2 } }
        'cC' = @{ name = 'already marked'; props = @{ $stamp = $script:Unit2; $marker = 'acme' } }
        'cD' = @{ name = 'marker differs by case'; props = @{ $stamp = $script:Unit2; $marker = 'ACME' } }
        'cE' = @{ name = 'two marker entries'; props = @{ $stamp = $script:Unit2; $marker = 'a'; (' ' + $marker) = 'b' } }
        # no authoritative record claims it (another environment's, in a shared type) -> never marked
        'cF' = @{ name = 'underivable unstamped'; props = @{} }
        # secure project container stamped with a unit the records do not derive -> MISMATCH, disputed
        'cG' = @{ name = 'mismatched stamp'; props = @{ $stamp = $script:Root } }
        # stamped for this environment by an earlier -Bind, claimed by no record -> UNMARKED-NO-CLAIM, never marked automatically
        'cH' = @{ name = 'underivable stamped'; props = @{ $stamp = $script:Unit2 } }
        # a secure project claims it, but the stamp names a unit this environment does not know -> FOREIGN, disputed
        'cI' = @{ name = 'foreign stamp'; props = @{ $stamp = $script:ForeignUnit } }
    }
    $containers = @{}
    foreach ($k in $keys) { $containers["b!$k"] = $all[$k] }
    $proj = @(); $matters = @(); $work = @(); $n = 0
    foreach ($k in $keys) {
        $n++; $guid = '{0:d8}-aaaa-aaaa-aaaa-aaaaaaaaaaaa' -f $n
        switch ($k) {
            'cB' { $proj += @{ sprk_projectid = $guid; sprk_containerid = "b!$k"; _owningbusinessunit_value = $script:Unit2 } }
            'cE' { $proj += @{ sprk_projectid = $guid; sprk_containerid = "b!$k"; _owningbusinessunit_value = $script:Unit2 } }
            'cG' { $proj += @{ sprk_projectid = $guid; sprk_containerid = "b!$k"; _owningbusinessunit_value = $script:Unit2 } }
            'cI' { $proj += @{ sprk_projectid = $guid; sprk_containerid = "b!$k"; _owningbusinessunit_value = $script:Unit2 } }
            'cC' { $matters += @{ sprk_matterid = $guid; sprk_containerid = "b!$k"; _owningbusinessunit_value = $script:Unit2 } }
            'cD' { $work += @{ sprk_workassignmentid = $guid; sprk_containerid = "b!$k"; _owningbusinessunit_value = $script:Unit2 } }
        }
    }
    $rootContainer = if ($keys -contains 'cA') { 'b!cA' } else { $null }
    return @{
        typeId = '33333333-3333-3333-3333-333333333333'
        units = @(
            @{ businessunitid = $script:Root; _parentbusinessunitid_value = $null; sprk_containerid = $rootContainer; name = 'Root' },
            @{ businessunitid = $script:Unit2; _parentbusinessunitid_value = $script:Root; sprk_containerid = $null; name = 'Unit2' })
        configs = @(@{
            sprk_specontainertypeconfigid = '44444444-4444-4444-4444-444444444444'; sprk_name = 'cfg'
            sprk_containertypeid = '33333333-3333-3333-3333-333333333333'; sprk_owningappid = '55555555-5555-5555-5555-555555555555'
            sprk_keyvaultsecretname = 'spe-owning-app-test'; _sprk_environment_value = '66666666-6666-6666-6666-666666666666'
            _sprk_businessunit_value = $script:Root })
        projects = $proj; matters = $matters; workassignments = $work; audits = @()
        containers = $containers; patches = @(); deletes = @(); dropMarkerPatchFor = @(); throwMarkerPatchFor = @()
    }
}

function Invoke-Backfill {
    param([hashtable]$World, [hashtable]$ScriptArgs, [string]$Dir)
    if (-not $Dir) { $Dir = Join-Path $TestDrive ([guid]::NewGuid().ToString('N')) }
    New-Item -ItemType Directory -Path $Dir -Force | Out-Null
    $worldPath = Join-Path $Dir 'world.json'
    if ($World) { $World | ConvertTo-Json -Depth 12 | Set-Content -Path $worldPath -Encoding UTF8 }
    $harness = Join-Path $Dir 'harness.ps1'
    Set-Content -Path $harness -Value $script:Harness -Encoding UTF8
    $a = @{ EnvironmentUrl = 'https://fake.crm.dynamics.com'; KeyVaultName = 'fake-vault'; LogPath = (Join-Path $Dir 'run.log') }
    foreach ($k in $ScriptArgs.Keys) { $a[$k] = $ScriptArgs[$k] }
    $out = & pwsh -NoProfile -File $harness -WorldPath $worldPath -ScriptPath $script:Backfill -ArgsJson ($a | ConvertTo-Json -Compress) *>&1 | Out-String
    $exit = $LASTEXITCODE
    $manifest = $null
    if ($out -match 'Reversal manifest: (?<p>.+?)\s+\(undo') {
        # The script writes its manifest under scripts/logs (git-ignored): move it beside the world so no test leaves files behind.
        $manifest = Join-Path $Dir ('manifest-' + [guid]::NewGuid().ToString('N') + '.csv')
        Move-Item -Path $Matches['p'] -Destination $manifest -Force
    }
    $callsPath = Join-Path $Dir 'calls.log'
    return [pscustomobject]@{
        Exit = $exit; Out = $out; Dir = $Dir; Manifest = $manifest
        World = $(if (Test-Path $worldPath) { Get-Content -Raw $worldPath | ConvertFrom-Json } else { $null })
        Calls = $(if (Test-Path $callsPath) { @(Get-Content $callsPath) } else { @() })
        Log = $(if (Test-Path (Join-Path $Dir 'run.log')) { Get-Content -Raw (Join-Path $Dir 'run.log') } else { '' })
    }
}

function Get-Prop { param($Run, [string]$Container, [string]$Name) return $Run.World.containers.$Container.props.$Name }
function Get-StatLine { param($Run, [string]$Name) if ($Run.Out -match "(?m)^$Name\s*:\s*(?<v>\d+)") { return [int]$Matches['v'] }; return $null }

Describe 'Backfill-SpeContainerBusinessUnitStamp.ps1 -CustomerId (issue #1889)' {
    It 'DRY RUN with -CustomerId lists what it would mark and writes nothing' {
        $r = Invoke-Backfill (New-World) @{ CustomerId = $script:Cust }
        $r.Exit | Should Be 0
        @($r.World.patches).Count | Should Be 0
        @($r.World.deletes).Count | Should Be 0
        Get-StatLine $r 'ToMark' | Should Be 1             # cB: already stamped, derivable
        Get-StatLine $r 'MarkNextRun' | Should Be 1        # cA: would be stamped this run, marked only on the next
        Get-StatLine $r 'UnmarkedNoClaim' | Should Be 1    # cH: stamped by an earlier -Bind, no record claims it
        Get-StatLine $r 'Marked' | Should Be 0
        Get-StatLine $r 'AlreadyMarked' | Should Be 1      # cC
        Get-StatLine $r 'MarkerForeign' | Should Be 1      # cD (case differs)
        Get-StatLine $r 'MarkerMalformed' | Should Be 1    # cE
        $r.Out | Should Match 'MARK-NEXT-RUN b!cA'
        $r.Out | Should Match 'UNMARKED-NO-CLAIM b!cH'
        $r.Out | Should Match 'MARKER-FOREIGN b!cD'
        $r.Out | Should Match 'MARKER-MALFORMED b!cE'
    }

    It '-Apply marks ONLY containers already stamped for this environment and derivable - never underivable, mismatched, foreign, malformed, or one it stamps itself' {
        $r = Invoke-Backfill (New-World) @{ CustomerId = $script:Cust; Apply = $true }
        $r.Exit | Should Be 0
        Get-Prop $r 'b!cA' 'spaarkeBusinessUnitId' | Should BeExactly $script:Root     # stamped from records ...
        Get-Prop $r 'b!cA' 'spaarkeCustomerId' | Should BeNullOrEmpty                  # ... but NOT marked in the same pass
        Get-Prop $r 'b!cB' 'spaarkeCustomerId' | Should BeExactly 'acme'
        Get-Prop $r 'b!cC' 'spaarkeCustomerId' | Should BeExactly 'acme'
        Get-Prop $r 'b!cD' 'spaarkeCustomerId' | Should BeExactly 'ACME'   # never overwritten
        Get-Prop $r 'b!cE' 'spaarkeCustomerId' | Should BeExactly 'a'      # never overwritten
        Get-Prop $r 'b!cF' 'spaarkeCustomerId' | Should BeNullOrEmpty      # underivable
        Get-Prop $r 'b!cG' 'spaarkeCustomerId' | Should BeNullOrEmpty      # MISMATCH stamp
        Get-Prop $r 'b!cH' 'spaarkeCustomerId' | Should BeNullOrEmpty      # stamped, no claim
        Get-Prop $r 'b!cI' 'spaarkeCustomerId' | Should BeNullOrEmpty      # FOREIGN stamp
        Get-Prop $r 'b!cF' 'spaarkeBusinessUnitId' | Should BeNullOrEmpty
        Get-StatLine $r 'Marked' | Should Be 1
        @($r.World.deletes).Count | Should Be 0
        # exactly: cA stamp + cB marker
        @($r.World.patches).Count | Should Be 2
        (@($r.World.patches | Where-Object { $_.id -in 'b!cF', 'b!cG', 'b!cH', 'b!cI', 'b!cD', 'b!cE', 'b!cC' })).Count | Should Be 0
        (@($r.World.patches | Where-Object { $_.body -match 'spaarkeCustomerId' })).Count | Should Be 1
    }

    It 'a container with a FOREIGN stamp that a secure project claims is never marked, and is listed' {
        $r = Invoke-Backfill (New-World -Only 'cI') @{ CustomerId = $script:Cust; Apply = $true }
        $r.Exit | Should Be 0
        @($r.World.patches).Count | Should Be 0
        (@($r.World.patches | Where-Object { $_.body -match 'spaarkeCustomerId' })).Count | Should Be 0
        $r.Out | Should Match 'FOREIGN b!cI'
        Get-Prop $r 'b!cI' 'spaarkeBusinessUnitId' | Should BeExactly $script:ForeignUnit
        $verify = Invoke-Backfill $null @{ CustomerId = $script:Cust; Verify = $true } $r.Dir
        $verify.Exit | Should Be 1
    }

    It 'a container with a MISMATCH stamp is never marked, and is listed' {
        $r = Invoke-Backfill (New-World -Only 'cG') @{ CustomerId = $script:Cust; Apply = $true }
        $r.Exit | Should Be 0
        @($r.World.patches).Count | Should Be 0
        $r.Out | Should Match 'MISMATCH b!cG'
        Get-Prop $r 'b!cG' 'spaarkeCustomerId' | Should BeNullOrEmpty
    }

    It 'a stamped container plus a -Bind that conflicts with its stamp is never marked' {
        $r = Invoke-Backfill (New-World -Only 'cB') @{ CustomerId = $script:Cust; Apply = $true; Bind = @("b!cB=$($script:Root)") }
        $r.Exit | Should Be 1                                  # BIND-CONFLICT fails an apply
        @($r.World.patches).Count | Should Be 0
        $r.Out | Should Match 'BIND-CONFLICT b!cB'
        Get-Prop $r 'b!cB' 'spaarkeCustomerId' | Should BeNullOrEmpty
    }

    It 'a container the run stamps from records alone is NOT marked in that pass (MARK-NEXT-RUN), -Verify fails, and the next -Apply marks it' {
        $first = Invoke-Backfill (New-World -Only 'cA') @{ CustomerId = $script:Cust; Apply = $true }
        $first.Exit | Should Be 0
        @($first.World.patches).Count | Should Be 1
        $first.World.patches[0].body | Should Match 'spaarkeBusinessUnitId'
        $first.World.patches[0].body | Should Not Match 'spaarkeCustomerId'
        Get-Prop $first 'b!cA' 'spaarkeBusinessUnitId' | Should BeExactly $script:Root
        Get-Prop $first 'b!cA' 'spaarkeCustomerId' | Should BeNullOrEmpty
        $first.Out | Should Match 'MARK-NEXT-RUN b!cA'
        $first.Out | Should Match 'stamped this run from records'
        $first.Out | Should Match 'UNMARKED b!cA'
        Get-StatLine $first 'Marked' | Should Be 0
        Get-StatLine $first 'MarkNextRun' | Should Be 1
        # the manifest holds the stamp row only - no marker row
        @(Import-Csv $first.Manifest | Where-Object { $_.Kind -ceq 'marker' }).Count | Should Be 0

        $verify = Invoke-Backfill $null @{ CustomerId = $script:Cust; Verify = $true } $first.Dir
        $verify.Exit | Should Be 1
        $verify.Out | Should Match 'UNMARKED b!cA'

        $second = Invoke-Backfill $null @{ CustomerId = $script:Cust; Apply = $true } $first.Dir
        $second.Exit | Should Be 0
        Get-Prop $second 'b!cA' 'spaarkeCustomerId' | Should BeExactly 'acme'
        @($second.World.patches).Count | Should Be 2            # the first run's stamp + this run's marker
        Get-StatLine $second 'Marked' | Should Be 1

        $final = Invoke-Backfill $null @{ CustomerId = $script:Cust; Verify = $true } $first.Dir
        $final.Exit | Should Be 0
    }

    It 'a DRY RUN lists the unstamped derivable container as MARK-NEXT-RUN and -Verify fails on it' {
        $r = Invoke-Backfill (New-World -Only 'cA') @{ CustomerId = $script:Cust; Verify = $true }
        $r.Exit | Should Be 1
        @($r.World.patches).Count | Should Be 0
        $r.Out | Should Match 'MARK-NEXT-RUN b!cA'
    }

    It 'an explicit -Bind names an unstamped container: it is stamped AND marked in the same pass' {
        $w = New-World -Only 'cF'
        $r = Invoke-Backfill $w @{ CustomerId = $script:Cust; Apply = $true; Bind = @("b!cF=$($script:Unit2)") }
        $r.Exit | Should Be 0
        Get-Prop $r 'b!cF' 'spaarkeBusinessUnitId' | Should BeExactly $script:Unit2
        Get-Prop $r 'b!cF' 'spaarkeCustomerId' | Should BeExactly 'acme'
        @($r.World.patches).Count | Should Be 2
        $r.Out | Should Not Match 'MARK-NEXT-RUN'
    }

    It 'a container stamped by an earlier -Bind with no claim is UNMARKED-NO-CLAIM: no PATCH, -Verify fails; a matching -Bind then marks it' {
        $r = Invoke-Backfill (New-World -Only 'cH') @{ CustomerId = $script:Cust; Apply = $true }
        $r.Exit | Should Be 0
        @($r.World.patches).Count | Should Be 0
        $r.Out | Should Match "UNMARKED-NO-CLAIM b!cH"
        $r.Out | Should Match "-Bind 'b!cH=$($script:Unit2)'"
        $verify = Invoke-Backfill $null @{ CustomerId = $script:Cust; Verify = $true } $r.Dir
        $verify.Exit | Should Be 1
        $verify.Out | Should Match 'UNMARKED-NO-CLAIM b!cH'

        $bound = Invoke-Backfill $null @{ CustomerId = $script:Cust; Apply = $true; Bind = @("b!cH=$($script:Unit2)") } $r.Dir
        $bound.Exit | Should Be 0
        Get-Prop $bound 'b!cH' 'spaarkeCustomerId' | Should BeExactly 'acme'
        $ok = Invoke-Backfill $null @{ CustomerId = $script:Cust; Verify = $true; Bind = @("b!cH=$($script:Unit2)") } $r.Dir
        $ok.Exit | Should Be 0
    }

    It 'an unclaimed stamped container already marked for this customer is not listed' {
        $w = New-World -Only 'cH'
        $w.containers['b!cH'].props['spaarkeCustomerId'] = 'acme'
        $r = Invoke-Backfill $w @{ CustomerId = $script:Cust; Verify = $true }
        $r.Out | Should Not Match 'UNMARKED-NO-CLAIM'
        @($r.World.patches).Count | Should Be 0
    }

    It 'WRITE-AHEAD: the marker manifest row is on disk even when the marker PATCH throws' {
        $w = New-World -Only 'cB'
        $w.throwMarkerPatchFor = @('b!cB')
        $r = Invoke-Backfill $w @{ CustomerId = $script:Cust; Apply = $true }
        $r.Exit | Should Be 1
        Get-StatLine $r 'Failed' | Should Be 1
        Get-StatLine $r 'Marked' | Should Be 0
        Get-Prop $r 'b!cB' 'spaarkeCustomerId' | Should BeNullOrEmpty
        $r.Manifest | Should Not BeNullOrEmpty
        $rows = @(Import-Csv $r.Manifest)
        $rows.Count | Should Be 1
        $rows[0].Kind | Should BeExactly 'marker'
        $rows[0].ContainerId | Should BeExactly 'b!cB'
        $rows[0].NewMarker | Should BeExactly 'acme'
        $r.Out | Should Match 'UNMARKED b!cB'
    }

    It 'marks a container that is already stamped but unmarked (the marker does not depend on the stamp)' {
        $r = Invoke-Backfill (New-World -Only 'cB') @{ CustomerId = $script:Cust; Apply = $true }
        $r.Exit | Should Be 0
        Get-Prop $r 'b!cB' 'spaarkeCustomerId' | Should BeExactly 'acme'
        @($r.World.patches).Count | Should Be 1
        $r.World.patches[0].body | Should Not Match 'spaarkeBusinessUnitId'
    }

    It 'is idempotent: a second -Apply run finds nothing to mark and writes nothing' {
        $first = Invoke-Backfill (New-World -Only 'cB') @{ CustomerId = $script:Cust; Apply = $true }
        $second = Invoke-Backfill $null @{ CustomerId = $script:Cust; Apply = $true } $first.Dir
        $second.Exit | Should Be 0
        Get-StatLine $second 'ToMark' | Should Be 0
        Get-StatLine $second 'AlreadyMarked' | Should Be 1
        @($second.World.patches).Count | Should Be 1   # still only the first run's write
    }

    It '-WhatIf forces a dry run even with -Apply' {
        $r = Invoke-Backfill (New-World -Only 'cB') @{ CustomerId = $script:Cust; Apply = $true; WhatIf = $true }
        @($r.World.patches).Count | Should Be 0
        Get-StatLine $r 'ToMark' | Should Be 1
    }

    It '-MaxWritesPerRun bounds the marker writes' {
        $w = New-World -Only 'cB', 'cC'
        # two unmarked derivable containers: make cC unmarked too
        $w.containers['b!cC'].props.Remove('spaarkeCustomerId')
        $r = Invoke-Backfill $w @{ CustomerId = $script:Cust; Apply = $true; MaxWritesPerRun = 1 }
        @($r.World.patches).Count | Should Be 1
        Get-StatLine $r 'Marked' | Should Be 1
        Get-StatLine $r 'ToMark' | Should Be 2
    }

    It 'a marker write Graph accepted but did not apply is a FAILURE (read-back), not a success' {
        $w = New-World -Only 'cB'
        $w.dropMarkerPatchFor = @('b!cB')
        $r = Invoke-Backfill $w @{ CustomerId = $script:Cust; Apply = $true }
        $r.Exit | Should Be 1
        Get-StatLine $r 'Marked' | Should Be 0
        Get-StatLine $r 'Failed' | Should Be 1
        $r.Out | Should Match 'UNMARKED b!cB'
    }

    It 'writes the manifest row BEFORE the write, with Kind = marker' {
        $r = Invoke-Backfill (New-World -Only 'cB') @{ CustomerId = $script:Cust; Apply = $true }
        $r.Manifest | Should Not BeNullOrEmpty
        $rows = @(Import-Csv $r.Manifest)
        $rows.Count | Should Be 1
        $rows[0].Kind | Should BeExactly 'marker'
        $rows[0].NewMarker | Should BeExactly 'acme'
        $rows[0].ContainerId | Should BeExactly 'b!cB'
    }

    It '-Verify with -CustomerId exits 1 while a derivable container is unmarked; without it the same world exits 0' {
        $w = New-World -Only 'cB'
        $with = Invoke-Backfill $w @{ CustomerId = $script:Cust; Verify = $true }
        $with.Exit | Should Be 1
        $with.Out | Should Match 'UNMARKED b!cB'
        @($with.World.patches).Count | Should Be 0
        $without = Invoke-Backfill $w @{ Verify = $true }
        $without.Exit | Should Be 0
    }

    It '-Verify with -CustomerId exits 1 for MARKER-FOREIGN (a different id, and a case-differing marker) and MARKER-MALFORMED' {
        $foreign = Invoke-Backfill (New-World -Only 'cC') @{ CustomerId = 'zeta'; Verify = $true }   # marker is acme
        $foreign.Exit | Should Be 1
        $foreign.Out | Should Match 'MARKER-FOREIGN b!cC'
        $case = Invoke-Backfill (New-World -Only 'cD') @{ CustomerId = $script:Cust; Verify = $true }   # marker is ACME
        $case.Exit | Should Be 1
        $case.Out | Should Match 'MARKER-FOREIGN b!cD'
        $malformed = Invoke-Backfill (New-World -Only 'cE') @{ CustomerId = $script:Cust; Verify = $true }
        $malformed.Exit | Should Be 1
        $malformed.Out | Should Match 'MARKER-MALFORMED b!cE'
    }

    It '-Verify with -CustomerId exits 0 when every derivable container is stamped and marked' {
        $r = Invoke-Backfill (New-World -Only 'cC') @{ CustomerId = $script:Cust; Verify = $true }
        $r.Exit | Should Be 0
    }

    It 'a case-differing existing marker is never written over in -Apply' {
        $r = Invoke-Backfill (New-World -Only 'cD') @{ CustomerId = $script:Cust; Apply = $true }
        @($r.World.patches).Count | Should Be 0
        Get-Prop $r 'b!cD' 'spaarkeCustomerId' | Should BeExactly 'ACME'
    }

    It 'trims -CustomerId before comparing it' {
        $r = Invoke-Backfill (New-World -Only 'cC') @{ CustomerId = '  acme '; Verify = $true }
        $r.Exit | Should Be 0
    }

    It 'refuses a blank -CustomerId with exit 2 before any az, Dataverse or Graph call' {
        $r = Invoke-Backfill (New-World) @{ CustomerId = '   '; Apply = $true }
        $r.Exit | Should Be 2
        $r.Calls.Count | Should Be 0
        @($r.World.patches).Count | Should Be 0
    }

    foreach ($bad in 'Acme', 'acme-prod', 'ab', 'abcdefghi', '1abc', 'ac me') {
        It "refuses -CustomerId '$bad' (not the BFF's customerId format) with exit 2 before any az, Dataverse or Graph call" {
            $r = Invoke-Backfill (New-World) @{ CustomerId = $bad; Apply = $true }
            $r.Exit | Should Be 2
            $r.Calls.Count | Should Be 0
            @($r.World.patches).Count | Should Be 0
            $r.Out | Should Match 'does not match'
        }
    }

    foreach ($good in 'abc', 'abcdefg1') {
        It "accepts the boundary -CustomerId '$good'" {
            $r = Invoke-Backfill (New-World -Only 'cB') @{ CustomerId = $good }
            $r.Exit | Should Be 0
            Get-StatLine $r 'ToMark' | Should Be 1
        }
    }

    It 'without -CustomerId it behaves as before: no marker is read-for-write or written, and the summary has no marker counts' {
        $r = Invoke-Backfill (New-World -Only 'cA', 'cB') @{ Apply = $true }
        $r.Exit | Should Be 0
        Get-Prop $r 'b!cA' 'spaarkeCustomerId' | Should BeNullOrEmpty
        Get-Prop $r 'b!cB' 'spaarkeCustomerId' | Should BeNullOrEmpty
        Get-Prop $r 'b!cA' 'spaarkeBusinessUnitId' | Should BeExactly $script:Root     # the stamp still lands
        Get-StatLine $r 'ToMark' | Should BeNullOrEmpty
        $r.Out | Should Not Match 'MARKER-'
        (Import-Csv $r.Manifest)[0].PSObject.Properties.Name -contains 'Kind' | Should Be $false
        @($r.World.patches | Where-Object { $_.body -match 'spaarkeCustomerId' }).Count | Should Be 0
    }

    It '-RevertManifest removes a marker this run wrote, leaves the stamp, and skips a marker changed since' {
        $apply = Invoke-Backfill (New-World -Only 'cB') @{ CustomerId = $script:Cust; Apply = $true }
        $apply.Manifest | Should Not BeNullOrEmpty

        # a later deliberate change must not be clobbered
        $changed = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $changed | Out-Null
        $wChanged = Get-Content -Raw (Join-Path $apply.Dir 'world.json') | ConvertFrom-Json -AsHashtable
        $wChanged.containers['b!cB'].props['spaarkeCustomerId'] = 'someone-else'
        $skip = Invoke-Backfill $wChanged @{ RevertManifest = $apply.Manifest; Apply = $true } $changed
        $skip.Exit | Should Be 0
        Get-Prop $skip 'b!cB' 'spaarkeCustomerId' | Should BeExactly 'someone-else'
        @($skip.World.patches).Count | Should Be 1   # only the original apply's patch (carried in the world); no new write

        # still this run's value: removed; the stamp stays
        $revert = Invoke-Backfill $null @{ RevertManifest = $apply.Manifest; Apply = $true } $apply.Dir
        $revert.Exit | Should Be 0
        Get-Prop $revert 'b!cB' 'spaarkeCustomerId' | Should BeNullOrEmpty
        Get-Prop $revert 'b!cB' 'spaarkeBusinessUnitId' | Should BeExactly $script:Unit2
        @($revert.World.deletes).Count | Should Be 0
    }

    It '-RevertManifest dry run (no -Apply) removes nothing' {
        $apply = Invoke-Backfill (New-World -Only 'cB') @{ CustomerId = $script:Cust; Apply = $true }
        $dry = Invoke-Backfill $null @{ RevertManifest = $apply.Manifest } $apply.Dir
        Get-Prop $dry 'b!cB' 'spaarkeCustomerId' | Should BeExactly 'acme'
        @($dry.World.patches).Count | Should Be 1
    }

    It 'an OLD stamp-only manifest (no Kind column) still reverts its stamp' {
        $w = New-World -Only 'cB'
        $dir = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $dir | Out-Null
        $old = Join-Path $dir 'old-manifest.csv'
        Set-Content -Path $old -Encoding UTF8 -Value @(
            'ContainerId,ConfigId,PreviousStamp,NewStamp,WrittenAtUtc',
            "b!cB,44444444-4444-4444-4444-444444444444,,$($script:Unit2),2026-10-01T00:00:00.0000000Z")
        $r = Invoke-Backfill $w @{ RevertManifest = $old; Apply = $true } $dir
        $r.Exit | Should Be 0
        Get-Prop $r 'b!cB' 'spaarkeBusinessUnitId' | Should BeNullOrEmpty
    }

    It 'the script contains no container-delete call' {
        $text = Get-Content -Raw $script:Backfill
        $text | Should Not Match "(?i)'Delete'"
        $text | Should Not Match '(?i)-Method\s+Delete'
    }
}
