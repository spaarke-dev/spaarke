# tests/scripts/Set-CorePairFieldSecurity.Tests.ps1
# ---------------------------------------------------------------------------
# field-mapping-server-write-path-r1 task 044: scripts/Set-CorePairFieldSecurity.ps1 runs here WHOLE (exit codes included) in a child
# pwsh against Set-CorePairFieldSecurity.Harness.ps1, a test double of `az`, Invoke-RestMethod and Start-Sleep. No environment, no
# token, no network. Proven:
#   1. a dry run writes nothing and lists every step; -Apply creates the profile, adds its one member, secures each cell and grants
#      the reader FIRST, then both writer profiles, adds the profile to the solution and publishes both tables once; a second -Apply
#      writes nothing; -Verify passes on the applied state;
#   2. -Verify fails (exit 1) on each drift: a default team without the reader profile (a seventh business unit: the teams are
#      queried, never counted), a BFF app user off the BFF writer profile, the ontology writer off its profile, another profile
#      (the D-136 one included) granting a cell, an unsecured cell, a writer or reader permission with the wrong rights or missing,
#      a stranger on the new profile, a stray column on it;
#   3. the readers check (r): an identity with neither System Administrator nor a reading profile fails (r1); a live read AS an
#      identity that returns a half empty fails (r2); a server that accepts a made-up caller id fails the control (r2); with no row
#      holding both halves (r2) is informational;
#   4. -Apply refuses before any write when a precondition fails; a failed grant reverts that column and publishes; a failed revert
#      prints RECOVERY REQUIRED NOW; -Apply with -Verify throws;
#   5. must not fire: the platform's System Administrator profile rows, and a non-administrator BFF user that reads through its profile.
#
# Pester 5 (CI pins 5.7.1):  Import-Module Pester -RequiredVersion 5.7.1 ; Invoke-Pester tests/scripts/Set-CorePairFieldSecurity.Tests.ps1
# ---------------------------------------------------------------------------

BeforeAll {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:ScriptPath = Join-Path $repoRoot 'scripts/Set-CorePairFieldSecurity.ps1'
    $script:HarnessPath = Join-Path $PSScriptRoot 'Set-CorePairFieldSecurity.Harness.ps1'
    $script:Work = Join-Path ([System.IO.Path]::GetTempPath()) ('cpf-tests-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $script:Work | Out-Null
    $script:N = 0

    $script:CoreId = '169feee6-7a77-4d00-9e27-ffc27bee6262'
    $script:Cells = @('sprk_signal.sprk_corerecordtype', 'sprk_signal.sprk_corerecordid', 'sprk_decisionrecord.sprk_corerecordtype', 'sprk_decisionrecord.sprk_corerecordid')
    $script:BffApp1 = '5967251e-171c-46fe-a6c2-ef843c90309d'
    $script:BffApp2 = '1e40baad-e065-4aea-a8d4-4b7ab273458c'
    $script:OntApp = '69040982-612e-469e-a85f-26d5172367c5'

    function New-Perm([string]$ProfileId, [string]$Cell, [int]$Read = 4, [int]$Write = 4) {
        $t, $c = $Cell -split '\.'
        @{ profileid = $ProfileId; entityname = $t; attributelogicalname = $c; canread = $Read; cancreate = $Write; canupdate = $Write }
    }

    # The state before -Apply on dev: the four cells unsecured, no core pair profile, six default teams on the reader profile.
    function New-Scenario([switch]$Applied) {
        $teams = @(1..6 | ForEach-Object { @{ teamid = "t$_"; name = "Team $_"; bu = "BU $_" } })
        $s = @{
            attrs        = @{}
            profiles     = @(
                @{ id = 'p-reader'; name = 'Spaarke BFF-Managed Field Readers'; users = @(); teams = @($teams | ForEach-Object teamid) },
                @{ id = 'p-bff'; name = 'Spaarke BFF-Managed Field Writers'; users = @('u-bff1', 'u-bff2'); teams = @() },
                @{ id = 'p-d136'; name = 'Spaarke Ontology Writer Field Writers'; users = @('u-ont'); teams = @() },
                @{ id = 'p-sa'; name = 'System Administrator'; users = @(); teams = @() })
            perms        = @((New-Perm 'p-d136' 'sprk_signal.sprk_createdbyperson'), (New-Perm 'p-reader' 'sprk_signal.sprk_createdbyperson' 4 0))
            defaultTeams = $teams
            users        = @(
                @{ systemuserid = 'u-bff1'; fullname = 'bff one'; applicationid = $script:BffApp1; teams = @('t1') },
                @{ systemuserid = 'u-bff2'; fullname = 'bff two'; applicationid = $script:BffApp2; teams = @('t1') },
                @{ systemuserid = 'u-ont'; fullname = 'ontology writer'; applicationid = $script:OntApp; teams = @('t1') },
                @{ systemuserid = 'u-human'; fullname = 'a person'; applicationid = $null; teams = @('t2') })
            sysAdmins    = @('u-bff1', 'u-bff2')
            rows         = @{ sprk_signal = @{ both = @('s1', 's2', 's3'); typeOnly = 0; idOnly = 0 }; sprk_decisionrecord = @{ both = @('d1'); typeOnly = 0; idOnly = 0 } }
            masked       = @{}
            solutionComponents = @()
        }
        foreach ($c in $script:Cells) { $s.attrs[$c] = @{ IsSecured = [bool]$Applied } }
        if ($Applied) {
            $s.profiles += @{ id = $script:CoreId; name = 'Spaarke Ontology Writer Core Pair Writers'; users = @('u-ont'); teams = @() }
            foreach ($c in $script:Cells) { $s.perms += @((New-Perm 'p-reader' $c 4 0), (New-Perm 'p-bff' $c), (New-Perm $script:CoreId $c)) }
            $s.solutionComponents = @($script:CoreId)
        }
        $s
    }

    function Invoke-Cpf([hashtable]$Scenario, [string[]]$ScriptArgs = @()) {
        $script:N++
        $dir = Join-Path $script:Work "run$($script:N)"
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $scn = Join-Path $dir 'scenario.json'
        ($Scenario | ConvertTo-Json -Depth 10) | Set-Content -LiteralPath $scn -Encoding UTF8
        $log = Join-Path $dir 'requests.jsonl'
        $env:CPF_SCENARIO = $scn; $env:CPF_LOG = $log
        $cmd = ". '$($script:HarnessPath)'; try { & '$($script:ScriptPath)' -EnvironmentUrl https://example.invalid -BffApplicationIds $($script:BffApp1),$($script:BffApp2) -OntologyWriterApplicationId $($script:OntApp) $($ScriptArgs -join ' '); `$c = `$LASTEXITCODE } catch { Write-Host (`"THROWN: `" + `$_.Exception.Message); `$c = 1 }; exit `$c"
        $out = (& pwsh -NoProfile -NonInteractive -Command $cmd 2>&1 | Out-String)
        $exit = $LASTEXITCODE
        $requests = if (Test-Path -LiteralPath $log) { @(Get-Content -LiteralPath $log | ForEach-Object { $_ | ConvertFrom-Json }) } else { @() }
        [pscustomobject]@{ Exit = $exit; Out = $out; Requests = $requests; Writes = @($requests | Where-Object { $_.Method -ne 'Get' }) }
    }
}

AfterAll {
    Remove-Item -LiteralPath $script:Work -Recurse -Force -ErrorAction SilentlyContinue
}

Describe 'Set-CorePairFieldSecurity.ps1 (stubbed Dataverse)' {

    Context 'baseline' {
        It 'a dry run lists every step, reports the row counts, exits 0 and writes nothing' {
            $r = Invoke-Cpf (New-Scenario)
            $r.Exit | Should -Be 0
            $r.Writes.Count | Should -Be 0
            $r.Out | Should -Match 'WOULD\s+create profile .Spaarke Ontology Writer Core Pair Writers. \(169feee6-7a77-4d00-9e27-ffc27bee6262\)'
            foreach ($c in $script:Cells) { $r.Out | Should -Match ("WOULD\s+secure " + [regex]::Escape($c)) }
            $r.Out | Should -Match 'sprk_signal : 3 row\(s\) hold both halves, 0 the type only, 0 the id only'
            $r.Out | Should -Match 'DRY RUN complete'
        }

        It '-Apply creates the profile, secures each cell then grants the reader first, adds it to the solution and publishes both tables once' {
            $r = Invoke-Cpf (New-Scenario) @('-Apply')
            $r.Exit | Should -Be 0 -Because $r.Out
            $r.Out | Should -Match 'PASS:'
            $w = @($r.Writes)
            $w[0].Uri | Should -Match '/fieldsecurityprofiles$'
            ($w[0].Body | ConvertFrom-Json).fieldsecurityprofileid | Should -Be $script:CoreId
            $w[1].Uri | Should -Match 'fieldsecurityprofiles\(169feee6-7a77-4d00-9e27-ffc27bee6262\)/systemuserprofiles_association/\$ref$'
            $puts = @(for ($i = 0; $i -lt $w.Count; $i++) { if ($w[$i].Method -eq 'Put') { $i } })
            $puts.Count | Should -Be 4
            foreach ($i in $puts) {
                $cell = [uri]::UnescapeDataString($w[$i].Uri) -replace "^.*LogicalName='([^']+)'\)/Attributes\(LogicalName='([^']+)'\)$", '$1.$2'
                ($w[$i].Body | ConvertFrom-Json).IsSecured | Should -BeTrue
                $grants = @($w[($i + 1)..($i + 3)] | ForEach-Object { $_.Body | ConvertFrom-Json })
                $grants | ForEach-Object { "$($_.entityname).$($_.attributelogicalname)" } | Should -Be @($cell, $cell, $cell)
                $grants[0].'fieldsecurityprofileid@odata.bind' | Should -Be '/fieldsecurityprofiles(p-reader)'
                $grants[0].cancreate | Should -Be 0
                $grants[1].'fieldsecurityprofileid@odata.bind' | Should -Be '/fieldsecurityprofiles(p-bff)'
                $grants[2].'fieldsecurityprofileid@odata.bind' | Should -Be "/fieldsecurityprofiles($($script:CoreId))"
                @($grants[1], $grants[2]) | ForEach-Object { "$($_.canread)/$($_.cancreate)/$($_.canupdate)" } | Should -Be @('4/4/4', '4/4/4')
            }
            @($w | Where-Object { $_.Uri -match 'AddSolutionComponent$' }).Count | Should -Be 1
            $publish = @($w | Where-Object { $_.Uri -match 'PublishXml$' })
            $publish.Count | Should -Be 1
            $publish[0].Body | Should -Match '<entity>sprk_signal</entity><entity>sprk_decisionrecord</entity>'
            $w[-1].Uri | Should -Match 'PublishXml$'
        }

        It '-Verify passes on the applied state and writes nothing; a second -Apply writes nothing' {
            $v = Invoke-Cpf (New-Scenario -Applied) @('-Verify')
            $v.Exit | Should -Be 0 -Because $v.Out
            $v.Writes.Count | Should -Be 0
            $v.Out | Should -Match '\(r2\) BFF app user .bff one. reads both halves of sprk_signal s1'
            $v.Out | Should -Match '\(r2\) ontology writer .ontology writer. reads both halves of sprk_decisionrecord d1'
            $a = Invoke-Cpf (New-Scenario -Applied) @('-Apply')
            $a.Exit | Should -Be 0 -Because $a.Out
            $a.Writes.Count | Should -Be 0
            $a.Out | Should -Match 'Nothing written; no publish'
        }

        It 'every live read is made AS the identity, after a refused control read as a made-up caller' {
            $v = Invoke-Cpf (New-Scenario -Applied) @('-Verify')
            $reads = @($v.Requests | Where-Object { $_.Caller })
            $reads[0].Caller | Should -Be '00000000-0000-0000-0000-0000000044ff'
            @($reads | Select-Object -Skip 1 | ForEach-Object Caller | Sort-Object -Unique) | Should -Be @('u-bff1', 'u-bff2', 'u-ont')
        }
    }

    Context '-Verify fires on each drift (exit 1)' {
        It '<name>' -ForEach @(
            @{ name = 'a seventh business unit whose default team lacks the reader profile'; edit = { param($s) $s.defaultTeams += @{ teamid = 't7'; name = 'Team 7'; bu = 'BU 7' } }; expect = "\(p2\) default team 'Team 7'" }
            @{ name = 'a BFF app user off the BFF writer profile'; edit = { param($s) ($s.profiles | Where-Object id -eq 'p-bff').users = @('u-bff1') }; expect = "\(p3\) BFF app user 'bff two' is NOT on" }
            @{ name = 'a human on the BFF writer profile'; edit = { param($s) ($s.profiles | Where-Object id -eq 'p-bff').users += 'u-human' }; expect = "member 'a person' is a HUMAN user" }
            @{ name = 'the ontology writer off its profile'; edit = { param($s) ($s.profiles | Where-Object id -eq '169feee6-7a77-4d00-9e27-ffc27bee6262').users = @() }; expect = "MISSING\s+member: app user 'ontology writer'" }
            @{ name = 'a stranger on the core pair writer profile'; edit = { param($s) ($s.profiles | Where-Object id -eq '169feee6-7a77-4d00-9e27-ffc27bee6262').users += 'u-human' }; expect = "Core Pair Writers member 'a person' is a HUMAN user" }
            @{ name = 'a stray column on the core pair writer profile'; edit = { param($s) $s.perms += (New-Perm '169feee6-7a77-4d00-9e27-ffc27bee6262' 'sprk_signal.sprk_firmreason') }; expect = 'also grants sprk_signal.sprk_firmreason' }
            @{ name = 'the D-136 profile granting a cell'; edit = { param($s) $s.perms += (New-Perm 'p-d136' 'sprk_decisionrecord.sprk_corerecordid') }; expect = "profile 'Spaarke Ontology Writer Field Writers' also grants sprk_decisionrecord.sprk_corerecordid" }
            @{ name = 'another profile granting read only'; edit = { param($s) $s.profiles += @{ id = 'p-x'; name = 'Some Readers'; users = @(); teams = @() }; $s.perms += (New-Perm 'p-x' 'sprk_signal.sprk_corerecordtype' 4 0) }; expect = "profile 'Some Readers' also grants sprk_signal.sprk_corerecordtype" }
            @{ name = 'an unsecured cell'; edit = { param($s) $s.attrs['sprk_decisionrecord.sprk_corerecordtype'].IsSecured = $false }; expect = 'MISSING\s+sprk_decisionrecord.sprk_corerecordtype is NOT field-secured' }
            @{ name = 'the BFF writer without Create'; edit = { param($s) ($s.perms | Where-Object { $_.profileid -eq 'p-bff' -and $_.attributelogicalname -eq 'sprk_corerecordid' -and $_.entityname -eq 'sprk_signal' }).cancreate = 0 }; expect = 'Field Writers on sprk_signal.sprk_corerecordid is read=4 create=0 update=4' }
            @{ name = 'the core pair writer without Read'; edit = { param($s) ($s.perms | Where-Object { $_.profileid -eq '169feee6-7a77-4d00-9e27-ffc27bee6262' -and $_.attributelogicalname -eq 'sprk_corerecordtype' -and $_.entityname -eq 'sprk_signal' }).canread = 0 }; expect = 'Core Pair Writers on sprk_signal.sprk_corerecordtype is read=0' }
            @{ name = 'the reader permission missing on a cell'; edit = { param($s) $s.perms = @($s.perms | Where-Object { -not ($_.profileid -eq 'p-reader' -and $_.entityname -eq 'sprk_decisionrecord' -and $_.attributelogicalname -eq 'sprk_corerecordid') }) }; expect = 'MISSING\s+Spaarke BFF-Managed Field Readers on sprk_decisionrecord.sprk_corerecordid' }
            @{ name = 'the reader granted Update'; edit = { param($s) ($s.perms | Where-Object { $_.profileid -eq 'p-reader' -and $_.entityname -eq 'sprk_signal' -and $_.attributelogicalname -eq 'sprk_corerecordid' }).canupdate = 4 }; expect = 'Readers on sprk_signal.sprk_corerecordid is read=4 create=0 update=4' }
            @{ name = 'the profile missing from the solution'; edit = { param($s) $s.solutionComponents = @() }; expect = "MISSING\s+'Spaarke Ontology Writer Core Pair Writers' in SpaarkeMaster" }
            @{ name = '(r1) a BFF app user with neither System Administrator nor a reading profile'; edit = { param($s) $s.sysAdmins = @('u-bff2'); ($s.profiles | Where-Object id -eq 'p-reader').teams = @('t2', 't3', 't4', 't5', 't6'); $s.defaultTeams = @($s.defaultTeams | Where-Object teamid -ne 't1'); $s.perms | Where-Object profileid -eq 'p-bff' | ForEach-Object { $_.canread = 0 } }; expect = "\(r1\) BFF app user 'bff one' holds neither System Administrator nor a profile" }
            @{ name = '(r2) a live read AS the ontology writer returns the pair empty'; edit = { param($s) $s.masked = @{ 'u-ont' = $true } }; expect = "\(r2\) ontology writer 'ontology writer' reads sprk_signal s1 with a half missing" }
            @{ name = '(r2) a server that accepts a made-up caller id'; edit = { param($s) $s.acceptAnyCaller = $true }; expect = 'impersonation is not honoured' }
        ) {
            $s = New-Scenario -Applied
            & $edit $s
            $r = Invoke-Cpf $s @('-Verify')
            $r.Exit | Should -Be 1 -Because $r.Out
            $r.Out | Should -Match $expect
            $r.Writes.Count | Should -Be 0
        }
    }

    Context 'must not fire' {
        It 'the platform System Administrator profile rows on the cells do not fail -Verify' {
            $s = New-Scenario -Applied
            foreach ($c in $script:Cells) { $s.perms += (New-Perm 'p-sa' $c) }
            $r = Invoke-Cpf $s @('-Verify')
            $r.Exit | Should -Be 0 -Because $r.Out
        }

        It 'a BFF app user without System Administrator that reads through its writer profile passes (r1)' {
            $s = New-Scenario -Applied
            $s.sysAdmins = @()
            $r = Invoke-Cpf $s @('-Verify')
            $r.Exit | Should -Be 0 -Because $r.Out
            $r.Out | Should -Match "\(r1\) BFF app user 'bff one' reads every cell through its field security profiles"
        }

        It 'with no row holding both halves, the live read is informational and -Verify still passes' {
            $s = New-Scenario -Applied
            $s.rows = @{ sprk_signal = @{ both = @(); typeOnly = 0; idOnly = 0 }; sprk_decisionrecord = @{ both = @(); typeOnly = 0; idOnly = 0 } }
            $r = Invoke-Cpf $s @('-Verify')
            $r.Exit | Should -Be 0 -Because $r.Out
            $r.Out | Should -Match 'INFO\s+\(r2\) no sprk_signal row holds both halves'
        }
    }

    Context 'refusals and recovery' {
        It '-Apply refuses before any write when a default team lacks the reader profile' {
            $s = New-Scenario
            $s.defaultTeams += @{ teamid = 't7'; name = 'Team 7'; bu = 'BU 7' }
            $r = Invoke-Cpf $s @('-Apply')
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'REFUSED'
            $r.Writes.Count | Should -Be 0
        }

        It '-Apply stops before securing any column when the core pair writer profile already has a stranger' {
            $s = New-Scenario
            $s.profiles += @{ id = $script:CoreId; name = 'Spaarke Ontology Writer Core Pair Writers'; users = @('u-ont', 'u-human'); teams = @() }
            $r = Invoke-Cpf $s @('-Apply')
            $r.Exit | Should -Be 1
            $r.Out | Should -Match "REFUSED: 'Spaarke Ontology Writer Core Pair Writers' is not as SpaarkeMaster declares it"
            @($r.Writes | Where-Object { $_.Method -eq 'Put' -or $_.Uri -match 'fieldpermissions$' }).Count | Should -Be 0
        }

        It 'a failed grant reverts that column, publishes both tables and exits 1' {
            $s = New-Scenario
            $s.failGrant = "$($script:CoreId)|sprk_signal.sprk_corerecordid"
            $r = Invoke-Cpf $s @('-Apply')
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'REVERTED sprk_signal.sprk_corerecordid is NOT field-secured again'
            $r.Out | Should -Not -Match 'PASS:'
            $puts = @($r.Writes | Where-Object Method -eq 'Put')
            ($puts[-1].Body | ConvertFrom-Json).IsSecured | Should -BeFalse
            [uri]::UnescapeDataString($puts[-1].Uri) | Should -Match "LogicalName='sprk_signal'\)/Attributes\(LogicalName='sprk_corerecordid'\)$"
            @($r.Writes | Where-Object { $_.Uri -match 'PublishXml$' }).Count | Should -Be 1
            @($puts | Where-Object { [uri]::UnescapeDataString($_.Uri) -match "sprk_decisionrecord" }).Count | Should -Be 0 -Because 'the run stops at the failed cell'
        }

        It 'a failed revert prints RECOVERY REQUIRED NOW' {
            $s = New-Scenario
            $s.failGrant = 'p-reader|sprk_signal.sprk_corerecordtype'
            $s.failRevert = $true
            $r = Invoke-Cpf $s @('-Apply')
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'RECOVERY REQUIRED NOW - sprk_signal.sprk_corerecordtype'
        }

        It '-Apply with -Verify throws before any request' {
            $r = Invoke-Cpf (New-Scenario) @('-Apply', '-Verify')
            $r.Exit | Should -Be 1
            $r.Out | Should -Match 'separate modes'
            $r.Requests.Count | Should -Be 0
        }
    }
}
