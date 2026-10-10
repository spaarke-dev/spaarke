# tests/scripts/Upgrade-LegacyRecordShareMasks.Tests.ps1
# ---------------------------------------------------------------------------
# unified-access-control-r2 task 179 (round 90 verifier finding F1): the legacy share-mask upgrade must SKIP every share
# the BFF wrote and still tracks in its sprk_assignedaccess ledger (state Shared: inherited from a secure parent, or
# Assigned-To). Upgrading one would leave the ledger's GrantedLevel at 23, so the parent's unshare (or an ended
# assignment) would read the share as "changed since" and KEEP Collaborate + Share instead of removing it.
#
# Proves, on seeded rows: an inherited and an Assigned-To share are skipped and reported; a plain legacy share is
# upgraded; a dry run writes nothing; a ledger read that fails writes nothing (exit 1).
#
# Technique: the script calls `exit`, so it runs in a CHILD pwsh process (as Auth-V4-Operator-Script-Gates.Tests.ps1
# does). The child shadows `az` (the token) and `Invoke-RestMethod` (Dataverse) with plain functions, which win over
# the application and the cmdlet in PowerShell's command resolution, and records every ModifyAccess body.
#
# Run (Pester 3.4):  Import-Module Pester -RequiredVersion 3.4.0 ; Invoke-Pester tests/scripts/Upgrade-LegacyRecordShareMasks.Tests.ps1
# ---------------------------------------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$script:Upgrade = (Join-Path $repoRoot 'scripts/Upgrade-LegacyRecordShareMasks.ps1') -replace '\\', '/'

$script:Matter = 'aaaaaaaa-0000-0000-0000-00000000000a'
$script:WorkAssignment = 'bbbbbbbb-0000-0000-0000-00000000000b'
$script:Inherited = '11111111-0000-0000-0000-000000000001'   # inherited on W from M (ledger Shared)
$script:Assigned = '22222222-0000-0000-0000-000000000002'    # Assigned-To share on M (ledger Shared)
$script:Plain = '33333333-0000-0000-0000-000000000003'       # a hand-made legacy share on M (no ledger row)

function Invoke-UpgradeChild {
    param([switch]$Apply, [switch]$LedgerFails)

    $dir = Join-Path ([System.IO.Path]::GetTempPath()) ("lrsm-{0}" -f [guid]::NewGuid())
    New-Item -ItemType Directory -Path $dir | Out-Null
    $seed = @{
        ledgerFails = [bool]$LedgerFails
        poa = @(
            @{ table = 'sprk_workassignment'; objectid = $script:WorkAssignment; principalid = $script:Inherited; principaltypecode = 'systemuser'; accessrightsmask = 23 }
            @{ table = 'sprk_matter'; objectid = $script:Matter; principalid = $script:Inherited; principaltypecode = 'systemuser'; accessrightsmask = 23 }
            @{ table = 'sprk_matter'; objectid = $script:Matter; principalid = $script:Assigned; principaltypecode = 'systemuser'; accessrightsmask = 65559 }
            @{ table = 'sprk_matter'; objectid = $script:Matter; principalid = $script:Plain; principaltypecode = 'systemuser'; accessrightsmask = 23 }
        )
        ledger = @(
            @{ sprk_sourcefield = "inherited:sprk_matter:$($script:Matter)"; _sprk_subjectsystemuser_value = $script:Inherited; _sprk_workassignment_value = $script:WorkAssignment }
            @{ sprk_sourcefield = 'sprk_assignedattorney1'; _sprk_subjectsystemuser_value = $script:Assigned; _sprk_matter_value = $script:Matter }
        )
    }
    $seedPath = Join-Path $dir 'seed.json'
    $writesPath = Join-Path $dir 'writes.json'
    $reportPath = Join-Path $dir 'report.json'
    $seed | ConvertTo-Json -Depth 6 | Set-Content -Path $seedPath -Encoding UTF8

    $applyArg = if ($Apply) { '-Apply' } else { '' }
    $child = @"
`$ErrorActionPreference = 'Stop'
`$global:Seed = Get-Content -Raw '$($seedPath -replace '\\','/')' | ConvertFrom-Json
`$global:Writes = [System.Collections.Generic.List[object]]::new()
function az { `$global:LASTEXITCODE = 0; 'fake-token' }
function Invoke-RestMethod {
    param([string]`$Uri, [string]`$Method = 'GET', `$Headers, `$Body)
    if (`$Method -eq 'POST' -and `$Uri -like '*/ModifyAccess') {
        `$b = `$Body | ConvertFrom-Json
        `$global:Writes.Add([pscustomobject]@{ Target = `$b.Target.'@odata.id'; Principal = `$b.PrincipalAccess.Principal.'@odata.id'; AccessMask = `$b.PrincipalAccess.AccessMask })
        `$rec = ([regex]::Match(`$b.Target.'@odata.id', '\(([0-9a-f-]+)\)')).Groups[1].Value
        `$usr = ([regex]::Match(`$b.PrincipalAccess.Principal.'@odata.id', '\(([0-9a-f-]+)\)')).Groups[1].Value
        `$mask = 0
        foreach (`$n in `$b.PrincipalAccess.AccessMask.Split(',')) { `$mask = `$mask -bor (@{ ReadAccess = 1; WriteAccess = 2; AppendAccess = 4; AppendToAccess = 16; DeleteAccess = 65536; ShareAccess = 262144 }[`$n]) }
        foreach (`$r in `$global:Seed.poa) { if (`$r.objectid -eq `$rec -and `$r.principalid -eq `$usr) { `$r.accessrightsmask = `$mask } }
        return @{}
    }
    if (`$Uri -like '*/sprk_assignedaccesses*') {
        if (`$global:Seed.ledgerFails) { throw 'simulated: the ledger read failed (503)' }
        return [pscustomobject]@{ value = @(`$global:Seed.ledger) }
    }
    if (`$Uri -like '*principalobjectaccessset*objectid eq*') {
        `$rec = ([regex]::Match(`$Uri, 'objectid eq ([0-9a-f-]+)')).Groups[1].Value
        `$usr = ([regex]::Match(`$Uri, 'principalid eq ([0-9a-f-]+)')).Groups[1].Value
        return [pscustomobject]@{ value = @(`$global:Seed.poa | Where-Object { `$_.objectid -eq `$rec -and `$_.principalid -eq `$usr } | ForEach-Object { [pscustomobject]@{ accessrightsmask = `$_.accessrightsmask } }) }
    }
    if (`$Uri -like '*principalobjectaccessset*') {
        `$table = ([regex]::Match(`$Uri, "objecttypecode eq '([a-z_]+)'")).Groups[1].Value
        return [pscustomobject]@{ value = @(`$global:Seed.poa | Where-Object { `$_.table -eq `$table } | ForEach-Object {
            [pscustomobject]@{ objectid = `$_.objectid; principalid = `$_.principalid; principaltypecode = `$_.principaltypecode; accessrightsmask = `$_.accessrightsmask } }) }
    }
    throw "unexpected call: `$Method `$Uri"
}
& '$($script:Upgrade)' -ReportPath '$($reportPath -replace '\\','/')' $applyArg *> `$null
`$code = `$LASTEXITCODE
`$global:Writes | ConvertTo-Json -Depth 4 | Set-Content -Path '$($writesPath -replace '\\','/')' -Encoding UTF8
exit `$code
"@
    $childPath = Join-Path $dir 'child.ps1'
    Set-Content -Path $childPath -Value $child -Encoding UTF8

    & pwsh -NoProfile -File $childPath *> $null
    $exitCode = $LASTEXITCODE
    $writes = if (Test-Path $writesPath) { @(Get-Content -Raw $writesPath | ConvertFrom-Json) } else { @() }
    $report = if (Test-Path $reportPath) { Get-Content -Raw $reportPath | ConvertFrom-Json } else { $null }
    Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
    return [pscustomobject]@{ ExitCode = $exitCode; Writes = @($writes | Where-Object { $_ }); Report = $report }
}

Describe 'Upgrade-LegacyRecordShareMasks.ps1 skips ledger-owned shares (task 179, round 90 F1)' {

    It 'on -Apply upgrades only the shares the BFF does not track, and reports the inherited and Assigned-To ones as skipped' {
        $r = Invoke-UpgradeChild -Apply

        $r.ExitCode | Should Be 0
        $r.Writes.Count | Should Be 2
        @($r.Writes | Where-Object { $_.Principal -eq "systemusers($($script:Plain))" -and $_.Target -eq "sprk_matters($($script:Matter))" }).Count | Should Be 1
        @($r.Writes | Where-Object { $_.Principal -eq "systemusers($($script:Inherited))" -and $_.Target -eq "sprk_matters($($script:Matter))" }).Count | Should Be 1
        @($r.Writes | Where-Object { $_.Target -eq "sprk_workassignments($($script:WorkAssignment))" }).Count | Should Be 0
        @($r.Writes | Where-Object { $_.Principal -eq "systemusers($($script:Assigned))" }).Count | Should Be 0

        $skipped = @($r.Report.skippedLedgerOwned)
        @($skipped | Where-Object { $_.Class -eq 'skipped-inherited' -and $_.RecordId -eq $script:WorkAssignment }).Count | Should Be 1
        @($skipped | Where-Object { $_.Class -eq 'skipped-assigned-to' -and $_.PrincipalId -eq $script:Assigned }).Count | Should Be 1
        $r.Report.afterCounts.'skipped-inherited' | Should Be 1
    }

    It 'on a dry run writes nothing and lists the same skips' {
        $r = Invoke-UpgradeChild

        $r.ExitCode | Should Be 0
        $r.Writes.Count | Should Be 0
        @($r.Report.skippedLedgerOwned).Count | Should Be 2
        @($r.Report.toUpgrade).Count | Should Be 2
    }

    It 'when the ledger cannot be read, writes nothing and exits 1 (never "no ledger row")' {
        $r = Invoke-UpgradeChild -Apply -LedgerFails

        $r.ExitCode | Should Be 1
        $r.Writes.Count | Should Be 0
    }
}
