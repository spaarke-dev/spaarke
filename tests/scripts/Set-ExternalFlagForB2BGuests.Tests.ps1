# tests/scripts/Set-ExternalFlagForB2BGuests.Tests.ps1
# ---------------------------------------------------------------------------
# spaarkeai-word-add-in-r1 task 126 (owner 2026-10-13, ADR-028 A8/A9): on a Model 1 stamp the customer's licensed
# staff are B2B guests, so the external-flag step must never mark them external. Proves: a mode is required; the
# customer's guests are never written; another directory's guest is set unless already Yes (No is the column default,
# not a decision); an unresolvable or inconsistent guest is reported, never written; -InternalUserIds confirms staff;
# a wrong customer id is refused; each PATCH carries the row's ETag; -Verify fails while anything is left;
# -AllGuestsExternal keeps the task 114 behaviour.
#
# Technique (as Set-AiSpendLimit.Tests.ps1): Pester 3.4. `az` and `Invoke-RestMethod` are shadowed by global functions
# that serve an in-memory Dataverse systemuser table (paged) and a domain -> tenant table, and record every PATCH.
# ---------------------------------------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$script:Sut = Join-Path $repoRoot 'scripts/Set-ExternalFlagForB2BGuests.ps1'
$script:Env = 'https://unit.crm.dynamics.com'
$script:Stamp = 'a221a95e-0000-0000-0000-000000000001'      # the stamp's own directory (Spaarke tenant)
$script:Customer = 'bc3aa7f4-3ca3-47e6-84e7-fea35f5c245b'
$script:OtherFirm = '11111111-1111-1111-1111-111111111111'
$script:OtherFirm2 = '22222222-2222-2222-2222-222222222222'

function New-Guest([string]$Id, [string]$Upn, [string]$Mail, $Value) {
    [pscustomobject]@{ systemuserid = $Id; fullname = $Id; domainname = $Upn; internalemailaddress = $Mail
        sprk_isexternal = $Value; isdisabled = $false; '@odata.etag' = "W/`"etag-$Id`"" }
}

function Set-Shadows([object[]]$Users, [hashtable]$Tenants, [int]$PageSize = 1000) {
    $global:T126Users = $Users
    $global:T126Tenants = $Tenants
    $global:T126PageSize = $PageSize
    $global:T126Patches = [System.Collections.Generic.List[string]]::new()
    $global:T126IfMatch = [System.Collections.Generic.List[string]]::new()
    $payload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("{`"tid`":`"$script:Stamp`"}")).TrimEnd('=')
    $global:T126Jwt = "eyJhbGciOiJub25lIn0.$payload.sig"
    Set-Item -Path function:global:az -Value { $global:LASTEXITCODE = 0; $global:T126Jwt }
    Set-Item -Path function:global:Invoke-RestMethod -Value {
        param([string]$Uri, $Headers, [string]$Method = 'Get', $Body)
        if ($Uri -match "findTenantInformationByDomainName\(domainName='([^']+)'\)") {
            $t = $global:T126Tenants[$Matches[1]]
            if (-not $t) { throw "404 Not Found: $($Matches[1])" }
            return [pscustomobject]@{ tenantId = $t }
        }
        if ($Uri -like '*organizations*') { return [pscustomobject]@{ value = @([pscustomobject]@{ name = 'unit' }) } }
        if ($Method -eq 'Patch') {
            $id = [regex]::Match($Uri, 'systemusers\(([^)]+)\)').Groups[1].Value
            $global:T126Patches.Add($id)
            $global:T126IfMatch.Add("$($Headers['If-Match'])")
            ($global:T126Users | Where-Object { $_.systemuserid -eq $id }).sprk_isexternal = $true
            return $null
        }
        $rows = @($global:T126Users)
        if ($Uri -like '*eq null*') { $rows = @($rows | Where-Object { $_.sprk_isexternal -ne $true }) }
        $skip = if ($Uri -match '&page=(\d+)$') { [int]$Matches[1] } else { 0 }
        $page = @($rows | Select-Object -Skip $skip -First $global:T126PageSize)
        $result = @{ value = $page }
        if ($skip + $global:T126PageSize -lt $rows.Count) {
            $base = $Uri -replace '&page=\d+$', ''
            $result['@odata.nextLink'] = "$base&page=$($skip + $global:T126PageSize)"
        }
        return [pscustomobject]$result
    }
}

function Remove-Shadows {
    Remove-Item -Path function:global:az -ErrorAction SilentlyContinue
    Remove-Item -Path function:global:Invoke-RestMethod -ErrorAction SilentlyContinue
}

function Invoke-Sut([hashtable]$Extra) {
    $report = Join-Path $TestDrive ("r{0}.json" -f [guid]::NewGuid())
    $params = @{ EnvironmentUrl = $script:Env; ReportPath = $report } + $Extra
    & $script:Sut @params 6>$null | Out-Null
    $exit = $global:LASTEXITCODE
    @{ exit = $exit; report = (Get-Content $report -Raw | ConvertFrom-Json) }
}

function Get-Row($Result, [string]$Id) { $Result.report.guests | Where-Object { $_.systemuserid -eq $Id } }

$script:Dirs = @{ 'deweycheatham.com' = 'bc3aa7f4-3ca3-47e6-84e7-fea35f5c245b'; 'otherfirm.com' = '11111111-1111-1111-1111-111111111111' }

Describe 'Set-ExternalFlagForB2BGuests.ps1 (task 126, Model 1)' {
    AfterEach { Remove-Shadows }

    It 'refuses to run without a mode, with both modes, and -InternalUserIds outside Model 1' {
        Set-Shadows @() @{}
        { & $script:Sut -EnvironmentUrl $script:Env } | Should Throw 'Choose exactly one mode'
        { & $script:Sut -EnvironmentUrl $script:Env -AllGuestsExternal -CustomerTenantIds $script:Customer } | Should Throw 'Choose exactly one mode'
        { & $script:Sut -EnvironmentUrl $script:Env -AllGuestsExternal -InternalUserIds $script:OtherFirm } | Should Throw 'Model 1 run'
    }

    It 'refuses the stamp''s own directory or the empty GUID as a customer directory' {
        Set-Shadows @() @{}
        { & $script:Sut -EnvironmentUrl $script:Env -CustomerTenantIds $script:Stamp } | Should Throw 'stamp''s own directory'
        { & $script:Sut -EnvironmentUrl $script:Env -CustomerTenantIds ([guid]::Empty) } | Should Throw 'empty GUID'
    }

    It 'a dry run writes nothing' {
        Set-Shadows @(
            (New-Guest 'cust' 'ralph_deweycheatham.com#EXT#@spaarke.onmicrosoft.com' $null $null),
            (New-Guest 'outside' 'eve_otherfirm.com#EXT#@spaarke.onmicrosoft.com' $null $null)
        ) $script:Dirs

        $r = Invoke-Sut @{ CustomerTenantIds = $script:Customer }

        $global:T126Patches.Count | Should Be 0
        (Get-Row $r 'outside').action | Should Be 'set Yes'
        @($r.report.internalUserIds).Count | Should Be 0   # never [""] when the list is not passed
        @($r.report.customerTenantIds) -join ',' | Should Be $script:Customer
        $r.report.noCustomerMatch | Should Be $false
    }

    It 'never writes the customer''s guests; sets another directory''s guest whether blank or No; reports the rest' {
        Set-Shadows @(
            (New-Guest 'cust' 'ralph_deweycheatham.com#EXT#@spaarke.onmicrosoft.com' 'ralph@deweycheatham.com' $null),
            (New-Guest 'custNo' 'ann_deweycheatham.com#EXT#@spaarke.onmicrosoft.com' $null $false),
            (New-Guest 'custYes' 'bob_deweycheatham.com#EXT#@spaarke.onmicrosoft.com' $null $true),
            (New-Guest 'outside' 'eve_otherfirm.com#EXT#@spaarke.onmicrosoft.com' 'eve@otherfirm.com' $null),
            (New-Guest 'outsideNo' 'new_otherfirm.com#EXT#@spaarke.onmicrosoft.com' $null $false),
            (New-Guest 'outsideYes' 'kim_otherfirm.com#EXT#@spaarke.onmicrosoft.com' $null $true),
            (New-Guest 'unknown' 'zed_nowhere.example#EXT#@spaarke.onmicrosoft.com' $null $null),
            (New-Guest 'unknownNo' 'amy_nowhere.example#EXT#@spaarke.onmicrosoft.com' $null $false),
            (New-Guest 'unknownYes' 'joy_nowhere.example#EXT#@spaarke.onmicrosoft.com' $null $true)
        ) $script:Dirs

        $r = Invoke-Sut @{ CustomerTenantIds = $script:Customer; Apply = $true }

        (@($global:T126Patches) | Sort-Object) -join ',' | Should Be 'outside,outsideNo'
        (Get-Row $r 'cust').class | Should Be 'customer'
        (Get-Row $r 'custNo').action | Should Be 'none'
        (Get-Row $r 'custYes').action | Should Match '^review: customer staff flagged external'
        (Get-Row $r 'outsideYes').action | Should Be 'none (already Yes)'
        (Get-Row $r 'unknown').class | Should Be 'unresolved'
        (Get-Row $r 'unknown').action | Should Match '^review'
        (Get-Row $r 'unknownNo').action | Should Match '^review'   # No is the default, not a decision
        (Get-Row $r 'unknownYes').action | Should Be 'none (already Yes)'
        $r.exit | Should Be 1   # three guests are left for review, so -Apply does not report PASS
    }

    It 'each PATCH carries the listed row''s ETag, never *' {
        Set-Shadows @(
            (New-Guest 'cust' 'ralph_deweycheatham.com#EXT#@spaarke.onmicrosoft.com' $null $null),
            (New-Guest 'outside' 'eve_otherfirm.com#EXT#@spaarke.onmicrosoft.com' $null $null)
        ) $script:Dirs

        $r = Invoke-Sut @{ CustomerTenantIds = $script:Customer; Apply = $true }

        @($global:T126IfMatch) -join ',' | Should Be 'W/"etag-outside"'
        $r.exit | Should Be 0
    }

    It '-InternalUserIds confirms a guest as customer staff (never written, review cleared)' {
        $sub = 'aaaaaaaa-0000-0000-0000-000000000001'; $unk = 'aaaaaaaa-0000-0000-0000-000000000002'
        Set-Shadows @(
            (New-Guest 'cust' 'ralph_deweycheatham.com#EXT#@spaarke.onmicrosoft.com' $null $null),
            (New-Guest $sub 'sue_otherfirm.com#EXT#@spaarke.onmicrosoft.com' $null $false),
            (New-Guest $unk 'zed_nowhere.example#EXT#@spaarke.onmicrosoft.com' $null $null)
        ) $script:Dirs

        $r = Invoke-Sut @{ CustomerTenantIds = $script:Customer; InternalUserIds = @($sub, $unk.ToUpperInvariant()); Apply = $true }

        $global:T126Patches.Count | Should Be 0
        (Get-Row $r $sub).class | Should Be 'customer'
        (Get-Row $r $unk).class | Should Be 'customer'
        $r.exit | Should Be 0
    }

    It 'refuses -Apply when no guest belongs to the customer''s directories but some belong to others' {
        Set-Shadows @(
            (New-Guest 'a' 'ralph_deweycheatham.com#EXT#@spaarke.onmicrosoft.com' $null $null),
            (New-Guest 'b' 'eve_otherfirm.com#EXT#@spaarke.onmicrosoft.com' $null $null)
        ) $script:Dirs

        $r = Invoke-Sut @{ CustomerTenantIds = $script:OtherFirm2; Apply = $true }   # a wrong id

        $global:T126Patches.Count | Should Be 0
        $r.exit | Should Be 1
        ($r.report.failures -join ' ') | Should Match '^refused'
    }

    It 'a re-run with nothing to write passes even when no guest belongs to the customer yet' {
        Set-Shadows @(
            (New-Guest 'b' 'eve_otherfirm.com#EXT#@spaarke.onmicrosoft.com' $null $true)
        ) $script:Dirs

        $r = Invoke-Sut @{ CustomerTenantIds = $script:Customer; Apply = $true }

        $global:T126Patches.Count | Should Be 0
        $r.exit | Should Be 0
    }

    It 'treats a guest whose two addresses resolve to different directories as unresolved (never written)' {
        Set-Shadows @(
            (New-Guest 'mixed' 'joe_deweycheatham.com#EXT#@spaarke.onmicrosoft.com' 'joe@otherfirm.com' $null)
        ) $script:Dirs

        $r = Invoke-Sut @{ CustomerTenantIds = $script:Customer; Apply = $true }

        $global:T126Patches.Count | Should Be 0
        (Get-Row $r 'mixed').class | Should Be 'unresolved'
    }

    It 'splits the user name at the LAST underscore and matches #ext# in any case' {
        Set-Shadows @(
            (New-Guest 'under' 'first_last_deweycheatham.com#EXT#@spaarke.onmicrosoft.com' $null $null),
            (New-Guest 'lower' 'eve_otherfirm.com#ext#@spaarke.onmicrosoft.com' $null $null)
        ) $script:Dirs

        $r = Invoke-Sut @{ CustomerTenantIds = $script:Customer }

        (Get-Row $r 'under').class | Should Be 'customer'
        (Get-Row $r 'lower').class | Should Be 'other'
    }

    It 'reads every page of guests' {
        Set-Shadows @(
            (New-Guest 'p1' 'ralph_deweycheatham.com#EXT#@spaarke.onmicrosoft.com' $null $null),
            (New-Guest 'p2' 'ann_deweycheatham.com#EXT#@spaarke.onmicrosoft.com' $null $null),
            (New-Guest 'p3' 'eve_otherfirm.com#EXT#@spaarke.onmicrosoft.com' $null $null)
        ) $script:Dirs -PageSize 1

        $r = Invoke-Sut @{ CustomerTenantIds = $script:Customer; Apply = $true }

        @($r.report.guests).Count | Should Be 3
        @($global:T126Patches) -join ',' | Should Be 'p3'
    }

    It 'accepts several customer directories' {
        Set-Shadows @(
            (New-Guest 'second' 'amy_seconddir.com#EXT#@spaarke.onmicrosoft.com' $null $null)
        ) @{ 'seconddir.com' = $script:OtherFirm2 }

        $r = Invoke-Sut @{ CustomerTenantIds = @($script:Customer, $script:OtherFirm2); Apply = $true }

        $global:T126Patches.Count | Should Be 0
        (Get-Row $r 'second').class | Should Be 'customer'
    }

    It '-Verify passes only when nothing is left to set or review' {
        Set-Shadows @(
            (New-Guest 'cust' 'ralph_deweycheatham.com#EXT#@spaarke.onmicrosoft.com' $null $false),
            (New-Guest 'outside' 'eve_otherfirm.com#EXT#@spaarke.onmicrosoft.com' $null $true)
        ) $script:Dirs
        (Invoke-Sut @{ CustomerTenantIds = $script:Customer; Verify = $true }).exit | Should Be 0

        $global:T126Users[1].sprk_isexternal = $false   # an outsider at the column default
        (Invoke-Sut @{ CustomerTenantIds = $script:Customer; Verify = $true }).exit | Should Be 1

        $global:T126Users[1].sprk_isexternal = $true
        $global:T126Users[0].sprk_isexternal = $true    # customer staff flagged external
        (Invoke-Sut @{ CustomerTenantIds = $script:Customer; Verify = $true }).exit | Should Be 1
        $global:T126Patches.Count | Should Be 0
    }

    It '-AllGuestsExternal keeps the task 114 behaviour: every blank or No guest is set' {
        Set-Shadows @(
            (New-Guest 'a' 'ralph_deweycheatham.com#EXT#@spaarke.onmicrosoft.com' $null $null),
            (New-Guest 'b' 'eve_otherfirm.com#EXT#@spaarke.onmicrosoft.com' $null $false),
            (New-Guest 'c' 'kim_otherfirm.com#EXT#@spaarke.onmicrosoft.com' $null $true)
        ) @{}

        $r = Invoke-Sut @{ AllGuestsExternal = $true; Apply = $true }

        (@($global:T126Patches) | Sort-Object) -join ',' | Should Be 'a,b'
        $r.exit | Should Be 0
    }
}
