# tests/scripts/Master-Ci-Verdict.Tests.ps1
# ---------------------------------------------------------------------------
# scripts/ci/master-ci-verdict.ps1 decides whether the router-master and tier2-master quality alerts
# open, stay open or close. These tests run the real script against a fake `gh` (a PowerShell function
# that shadows the gh executable and serves canned runs / jobs / issues), so no network call is made.
#
# Must-fire controls (each fails if the guard it protects is removed):
#   - a job the OPEN alert lists as failing, absent from the whole window -> still `failing`
#     ("not executed since"), never `ok` (the false "Recovered" of a path-gated job aging out)
#   - the same for a Tier 2 shard
#   - a newer queued / in-progress master run -> `ok` is held back as `skip`; `failing` is not
# Must-not-fire controls: no open alert -> ok; the listed job executed and passed in the window -> ok.
#
# Pester 5+:  Invoke-Pester tests/scripts/Master-Ci-Verdict.Tests.ps1
# ---------------------------------------------------------------------------

BeforeAll {
    $script:Verdict = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' 'scripts' 'ci' 'master-ci-verdict.ps1')).Path

    # Fixture state the fake gh serves. Set per test by Use-Fixture.
    $global:FakeGh = @{}

    function global:gh {
        $global:LASTEXITCODE = 0
        $a = $args -join ' '
        $f = $global:FakeGh
        if ($a -match 'actions/runs/(\d+)/jobs') {
            # `gh api --jq '.jobs[] | {...}'` prints one JSON object per line.
            return @($f.Jobs[[string]$Matches[1]] | ForEach-Object { $_ | ConvertTo-Json -Compress })
        }
        if ($a -match 'status=(queued|in_progress)') {
            return (@{ workflow_runs = @($f.Pending | Where-Object { $_.status -eq $Matches[1] }) } | ConvertTo-Json -Depth 5 -Compress)
        }
        if ($a -match 'status=completed') {
            return (@{ workflow_runs = @($f.Runs) } | ConvertTo-Json -Depth 5 -Compress)
        }
        if ($a -match 'actions/runs/(\d+)$') {
            return ($f.Runs | Where-Object { [string]$_.id -eq $Matches[1] } | Select-Object -First 1 | ConvertTo-Json -Depth 5 -Compress)
        }
        if ($a -match '^issue list') { return '[]' }
        $global:LASTEXITCODE = 1
        return "fake gh: unexpected call: $a"
    }

    function New-Run([long] $Id, [datetime] $Created) {
        @{
            id = $Id; workflow_id = 7; head_branch = 'master'; event = 'push'; status = 'completed'; conclusion = 'success'
            created_at = $Created.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'); head_sha = ('a' * 40)
            head_commit = @{ message = "commit $Id" }; html_url = "https://example.test/runs/$Id"
        }
    }

    function New-Job([string] $Name, [string] $Conclusion = 'success') {
        @{ name = $Name; conclusion = $Conclusion; html_url = "https://example.test/job/$([uri]::EscapeDataString($Name))" }
    }

    # Two completed master runs; every job in `$Jobs` ran in the newest one (the window never sees any other job).
    function Use-Fixture($Jobs, $Pending = @()) {
        $now = (Get-Date).ToUniversalTime()
        $global:FakeGh = @{
            Runs    = @((New-Run 200 $now.AddHours(-5)), (New-Run 100 $now.AddHours(-6)))
            Jobs    = @{ '200' = $Jobs; '100' = $Jobs }
            Pending = $Pending
        }
    }

    function Invoke-Verdict([string] $RouterBody = '', [string] $Tier2Body = '') {
        $r = Join-Path $TestDrive 'router-alert.md'; $t = Join-Path $TestDrive 'tier2-alert.md'
        Set-Content -Path $r -Value $RouterBody; Set-Content -Path $t -Value $Tier2Body
        $env:GITHUB_OUTPUT = $null
        $text = (& $script:Verdict -RunId 200 -Repo 'o/r' -RouterAlertBodyPath $r -Tier2AlertBodyPath $t) | Out-String
        $res = @{}
        foreach ($m in [regex]::Matches($text, '(?ms)^---- (?<k>\w+)\r?\n(?<v>.*?)(?=^---- |\z)')) { $res[$m.Groups['k'].Value] = $m.Groups['v'].Value.Trim() }
        return $res
    }
}

AfterAll {
    Remove-Item function:global:gh -ErrorAction SilentlyContinue
    Remove-Variable -Name FakeGh -Scope Global -ErrorAction SilentlyContinue
}

Describe 'router-master' {
    BeforeEach { Use-Fixture @((New-Job 'Router'), (New-Job 'Tier 1 (Blocking) / Arch Tests')) }

    It 'is ok when no alert is open and every job passed' {
        (Invoke-Verdict).router_status | Should -Be 'ok'
    }

    It 'MUST FIRE: a job the open alert lists as failing, absent from the whole window, stays failing' {
        $body = "- Job ``Tier 1 (Blocking) / Auth Smoke`` concluded **failure** on its most recent master run, ``abc`` ([log](x))."
        $r = Invoke-Verdict -RouterBody $body
        $r.router_status | Should -Be 'failing'
        $r.router_summary | Should -Match 'Tier 1 \(Blocking\) / Auth Smoke'
        $r.router_summary | Should -Match 'not executed since'
    }

    It 'keeps a carried-over job failing on the next evaluation (our own wording is parseable)' {
        $first = Invoke-Verdict -RouterBody "- Job ``Tier 1 (Blocking) / Auth Smoke`` concluded **failure** on its most recent master run."
        (Invoke-Verdict -RouterBody $first.router_summary).router_status | Should -Be 'failing'
    }

    It 'MUST NOT FIRE: a listed job that executed and passed in the window no longer holds the alert' {
        $body = "- Job ``Tier 1 (Blocking) / Arch Tests`` concluded **failure** on its most recent master run."
        (Invoke-Verdict -RouterBody $body).router_status | Should -Be 'ok'
    }

    It 'is failing when a job in the window failed, with no alert open' {
        Use-Fixture @((New-Job 'Router'), (New-Job 'Tier 1 (Blocking) / Compile (Debug)' 'failure'))
        (Invoke-Verdict).router_status | Should -Be 'failing'
    }
}

Describe 'tier2-master/full-unit-tests' {
    BeforeEach { Use-Fixture @((New-Job 'Router')) }

    It 'MUST FIRE: a shard the open alert lists as failing, with no shard run in the window, stays failing' {
        $r = Invoke-Verdict -Tier2Body "- Shard ``api-seam`` verdict **fail** on ``abc`` ([log](x))."
        $r.tier2_status | Should -Be 'failing'
        $r.tier2_summary | Should -Match 'api-seam'
        $r.tier2_summary | Should -Match 'not executed since'
    }

    It 'MUST NOT FIRE: no open alert and no shard in the window leaves the alert alone (skip)' {
        (Invoke-Verdict).tier2_status | Should -Be 'skip'
    }
}

Describe 'a newer master run still running' {
    It 'holds ok back as skip' {
        $newer = (New-Run 300 (Get-Date).ToUniversalTime().AddMinutes(-10)); $newer.status = 'in_progress'; $newer.conclusion = $null
        Use-Fixture @((New-Job 'Router'), (New-Job 'Tier 1 (Blocking) / Arch Tests')) @($newer)
        $r = Invoke-Verdict
        $r.router_status | Should -Be 'skip'
        $r.note | Should -Match 'Not closing router-master'
    }

    It 'does not hold failing back' {
        $newer = (New-Run 300 (Get-Date).ToUniversalTime().AddMinutes(-10)); $newer.status = 'queued'; $newer.conclusion = $null
        Use-Fixture @((New-Job 'Router' 'failure')) @($newer)
        (Invoke-Verdict).router_status | Should -Be 'failing'
    }

    It 'ignores a pending run older than the evaluated one' {
        $older = (New-Run 50 (Get-Date).ToUniversalTime().AddHours(-7)); $older.status = 'in_progress'
        Use-Fixture @((New-Job 'Router')) @($older)
        (Invoke-Verdict).router_status | Should -Be 'ok'
    }
}
