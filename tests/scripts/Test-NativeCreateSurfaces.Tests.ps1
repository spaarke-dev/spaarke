# tests/scripts/Test-NativeCreateSurfaces.Tests.ps1
# ---------------------------------------------------------------------------
# field-mapping-server-write-path-r1 task 051 (FR-24). Proves, with no live call:
#   1. the -InputJson judge: a fully closed capture passes (exit 0); each failing capture exits non-zero and NAMES the failing
#      table (Assign still shown, quick create on, platform New still shown on a redirected table, the redirect button missing,
#      a table not observed);
#   2. a "kept" table with the platform New shown is reported, never a failure (owner decision OQ-1 / OQ-2);
#   3. the table list is the sprk_ tables of RecordTableRules.cs, so a table added to the write path cannot be forgotten here;
#   4. Merge-NativeCreateRibbon.ps1: hides Assign x3, is idempotent, keeps every earlier command, hides New only where redirected.
#
# Pester 5+ (the dash assertion syntax). Run:  Invoke-Pester tests/scripts/Test-NativeCreateSurfaces.Tests.ps1
# ---------------------------------------------------------------------------

BeforeAll {
    $script:repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:verify = Join-Path $repoRoot 'scripts/Test-NativeCreateSurfaces.ps1'
    $script:merge = Join-Path $repoRoot 'infrastructure/dataverse/ribbon/NativeCreateRibbons/Merge-NativeCreateRibbon.ps1'
    $script:configPath = Join-Path $repoRoot 'infrastructure/dataverse/ribbon/NativeCreateRibbons/native-create-surfaces.json'
    $script:cfg = Get-Content -Raw $configPath | ConvertFrom-Json

    # A closed capture: every table exactly as the config expects.
    function New-ClosedCapture {
        $tables = [ordered]@{}
        foreach ($e in $cfg.tables) {
            $present = @()
            if ($e.nativeNew -eq 'redirected') { $present += "sprk.NativeCreate.$($e.table).Form.New" }
            $tables[$e.table] = [ordered]@{ quickCreate = 'off'; present = $present }
        }
        return @{ tables = $tables }
    }
    function Invoke-Verify($capture) {
        $file = Join-Path $TestDrive ([guid]::NewGuid().ToString('N') + '.json')
        $capture | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $file -Encoding utf8
        $out = & pwsh -NoProfile -File $verify -InputJson $file 2>&1 | Out-String
        return [pscustomobject]@{ Exit = $LASTEXITCODE; Out = $out }
    }
}

Describe 'Test-NativeCreateSurfaces -InputJson' {
    It 'passes (exit 0) when every table is closed' {
        $r = Invoke-Verify (New-ClosedCapture)
        $r.Exit | Should -Be 0
        $r.Out | Should -Match 'PASS \(23 tables\)'
    }

    It 'fails and names the table when an Assign button is still shown' {
        $c = New-ClosedCapture
        $c.tables['sprk_todo'].present += 'Mscrm.HomepageGrid.sprk_todo.Assign'
        $r = Invoke-Verify $c
        $r.Exit | Should -Be 1
        $r.Out | Should -Match 'FAIL sprk_todo: Assign still shown: Mscrm.HomepageGrid.sprk_todo.Assign'
        $r.Out | Should -Match 'NOT MET - 1 of 23 table\(s\): sprk_todo'
    }

    It 'fails and names the table when quick create is on' {
        $c = New-ClosedCapture
        $c.tables['sprk_project'].quickCreate = 'on'
        $r = Invoke-Verify $c
        $r.Exit | Should -Be 1
        $r.Out | Should -Match 'sprk_project: quick create is on'
    }

    It 'fails when the platform New is still shown on a redirected table' {
        $c = New-ClosedCapture
        $c.tables['sprk_matter'].present += 'Mscrm.Form.sprk_matter.NewRecord'
        $r = Invoke-Verify $c
        $r.Exit | Should -Be 1
        $r.Out | Should -Match 'sprk_matter: platform New still shown: Mscrm.Form.sprk_matter.NewRecord'
    }

    It 'fails when a redirected table has lost its replacement button' {
        $c = New-ClosedCapture
        $c.tables['sprk_communication'].present = @()
        $r = Invoke-Verify $c
        $r.Exit | Should -Be 1
        $r.Out | Should -Match 'sprk_communication: the redirect button is missing'
    }

    It 'fails an invoice table whose subgrid New is shown (hidden outright)' {
        $c = New-ClosedCapture
        $c.tables['sprk_invoice'].present += 'Mscrm.SubGrid.sprk_invoice.NewRecord'
        $r = Invoke-Verify $c
        $r.Exit | Should -Be 1
        $r.Out | Should -Match 'sprk_invoice: platform New still shown'
    }

    It 'treats a table that was not observed as a failure, never a pass' {
        $c = New-ClosedCapture
        $c.tables.Remove('sprk_signal')
        $r = Invoke-Verify $c
        $r.Exit | Should -Be 1
        $r.Out | Should -Match 'UNKNOWN sprk_signal: not observed'
    }

    It 'reports but does not fail a kept table that still shows the platform New (must not fire)' {
        $c = New-ClosedCapture
        $c.tables['sprk_servicerequest'].present += 'Mscrm.Form.sprk_servicerequest.NewRecord'
        $c.tables['sprk_reportcard'].present += 'Mscrm.HomepageGrid.sprk_reportcard.NewRecord'
        $r = Invoke-Verify $c
        $r.Exit | Should -Be 0
        $r.Out | Should -Match 'platform New shown'
    }

    It 'exits 2 with no mode and with both modes' {
        & pwsh -NoProfile -File $verify 2>&1 | Out-Null
        $LASTEXITCODE | Should -Be 2
        & pwsh -NoProfile -File $verify -Verify -InputJson x.json -EnvironmentUrl https://x.crm.dynamics.com 2>&1 | Out-Null
        $LASTEXITCODE | Should -Be 2
    }
}

Describe 'native-create-surfaces.json' {
    It 'lists exactly the sprk_ tables of RecordTableRules.All' {
        $cs = Get-Content -Raw (Join-Path $repoRoot 'src/server/api/Sprk.Bff.Api/Services/RecordCreation/RecordTableRules.cs')
        $rules = @([regex]::Matches($cs, 'Table = "(sprk_[a-z]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        $listed = @($cfg.tables | ForEach-Object { $_.table } | Sort-Object -Unique)
        ($listed -join ',') | Should -Be ($rules -join ',')
    }

    It 'expects quick create off and Assign hidden everywhere, and redirects only user-creatable tables' {
        @($cfg.tables | Where-Object { $_.quickCreate -ne 'off' -or $_.assign -ne 'hidden' }).Count | Should -Be 0
        $redirected = @($cfg.tables | Where-Object { $_.nativeNew -eq 'redirected' } | ForEach-Object { $_.table } | Sort-Object)
        ($redirected -join ',') | Should -Be 'sprk_communication,sprk_document,sprk_event,sprk_matter,sprk_project,sprk_todo,sprk_workassignment'
        foreach ($excluded in 'sprk_invoicesubmission', 'sprk_invoiceapproval', 'sprk_externalidentifier', 'sprk_invoicecorrection', 'sprk_signal', 'sprk_decisionrecord', 'sprk_budgetrevision', 'sprk_servicerequest') {
            ($cfg.tables | Where-Object { $_.table -eq $excluded }).nativeNew | Should -Be 'kept'
        }
    }
}

Describe 'Merge-NativeCreateRibbon' {
    BeforeEach {
        $script:seed = Join-Path $TestDrive 'RibbonDiff.xml'
        @'
<?xml version="1.0" encoding="utf-8"?>
<RibbonDiffXml>
  <CustomActions />
  <Templates><RibbonTemplates Id="Mscrm.Templates"></RibbonTemplates></Templates>
  <CommandDefinitions>
    <CommandDefinition Id="sprk.Existing.Command"><EnableRules /><DisplayRules /><Actions /></CommandDefinition>
  </CommandDefinitions>
  <RuleDefinitions><TabDisplayRules /><DisplayRules /><EnableRules /></RuleDefinitions>
  <LocLabels />
</RibbonDiffXml>
'@ | Set-Content -LiteralPath $seed -Encoding utf8
    }

    It 'hides the three Assign buttons, redirects New, keeps the existing command' {
        & $merge -RibbonDiff $seed -Entity sprk_todo -Out $seed | Out-Null
        [xml]$x = Get-Content -Raw $seed
        $hides = @($x.SelectNodes('//*[local-name()="HideCustomAction"]') | ForEach-Object { $_.GetAttribute('Location') })
        foreach ($loc in 'Mscrm.Form.sprk_todo.Assign', 'Mscrm.HomepageGrid.sprk_todo.Assign', 'Mscrm.SubGrid.sprk_todo.Assign',
            'Mscrm.HomepageGrid.sprk_todo.NewRecord', 'Mscrm.Form.sprk_todo.NewRecord', 'Mscrm.Form.sprk_todo.SaveAndNew') {
            $hides | Should -Contain $loc
        }
        $x.SelectSingleNode('//*[@Id="sprk.Existing.Command"]') | Should -Not -BeNullOrEmpty
        $x.SelectSingleNode('//*[@Id="sprk.NativeCreate.sprk_todo.New.Command"]') | Should -Not -BeNullOrEmpty
        $x.SelectSingleNode('//*[@Id="sprk.CreatePrivilege.sprk_todo.DisplayRule"]') | Should -Not -BeNullOrEmpty
    }

    It 'is idempotent' {
        & $merge -RibbonDiff $seed -Entity sprk_communication -Out $seed | Out-Null
        $first = (Get-FileHash $seed).Hash
        & $merge -RibbonDiff $seed -Entity sprk_communication -Out $seed | Out-Null
        (Get-FileHash $seed).Hash | Should -Be $first
    }

    It 'hides Assign but leaves the platform New alone on a kept table (must not fire)' {
        & $merge -RibbonDiff $seed -Entity sprk_servicerequest -Out $seed | Out-Null
        [xml]$x = Get-Content -Raw $seed
        $hides = @($x.SelectNodes('//*[local-name()="HideCustomAction"]') | ForEach-Object { $_.GetAttribute('Location') })
        $hides | Should -Contain 'Mscrm.Form.sprk_servicerequest.Assign'
        @($hides | Where-Object { $_ -like '*NewRecord*' -or $_ -like '*SaveAndNew*' }).Count | Should -Be 0
        $x.SelectSingleNode('//*[starts-with(@Id,"sprk.NativeCreate.")]') | Should -BeNullOrEmpty
    }

    It 'rejects a table that is not in the config' {
        { & $merge -RibbonDiff $seed -Entity sprk_nothere -Out $seed } | Should -Throw
    }
}
