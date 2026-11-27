# tests/scripts/Test-NativeCreateSurfaces.Tests.ps1
# ---------------------------------------------------------------------------
# field-mapping-server-write-path-r1 task 051 (FR-24). Proves, with no live call:
#   1. the -InputJson judge: a fully closed capture passes (exit 0); each failing capture exits non-zero and NAMES the failing
#      table (Assign still shown, quick create on, platform New still shown on a redirected table, the redirect button missing,
#      a table not observed);
#   2. a "kept" table with the platform New shown is reported, never a failure (owner decision OQ-1 / OQ-2);
#   3. the table list is the sprk_ tables of RecordTableRules.cs, so a table added to the write path cannot be forgotten here;
#   4. Merge-NativeCreateRibbon.ps1: hides Assign x3, is idempotent, keeps every earlier command, hides New only where redirected,
#      hides the HOME-grid New only on a kept table that says so (owner OQ-1), and never leaves -Out half-written;
#   5. r1: -Verify fails and names a table whose subgrid New rule passes no table (the script then never consults its redirect), a
#      redirected table whose home-grid replacement button is missing, and a budget / KPI assessment whose home-grid New is shown;
#   7. task 066: the KPI subgrid has ONE create ("+ New"): -Verify fails and names a KPI ribbon that still shows "Add KPI Assessment"
#      (must-fire) and passes without it (must-not-fire); the merge removes the button, its command and its labels from a fresh
#      export; no SpaarkeMaster ribbon carries a retired button.
#   6. r1: the SpaarkeMaster ribbons agree with the config (every redirected table hides its three platform New buttons and carries
#      its launcher buttons; the home-grid New is the launcher on the seven redirected tables).
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
            if ($e.alsoPresent) { $present += @($e.alsoPresent) }
            if ($e.nativeNew -eq 'redirected') {
                $present += "sprk.NativeCreate.$($e.table).Form.New"
                if ($e.gridButtonId) { $present += $e.gridButtonId }
            }
            $tables[$e.table] = [ordered]@{ quickCreate = 'off'; present = $present; newRule = $(if ($e.subgridRule -eq $true) { 'table' } else { 'absent' }) }
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

    It 'fails and names the table when its subgrid New rule passes no table (the shipped defect)' {
        $c = New-ClosedCapture
        $c.tables['sprk_todo'].newRule = 'no-table'
        $r = Invoke-Verify $c
        $r.Exit | Should -Be 1
        $r.Out | Should -Match 'FAIL sprk_todo: the subgrid New rule sprk.SecureChild.sprk_todo.NativeNewAllowed.EnableRule passes no table'
        $r.Out | Should -Match 'NOT MET - 1 of 23 table\(s\): sprk_todo'
    }

    It 'fails and names the table when its subgrid New rule is missing or was not observed' {
        $c = New-ClosedCapture
        $c.tables['sprk_event'].newRule = 'absent'
        $c.tables['sprk_document'].Remove('newRule')
        $r = Invoke-Verify $c
        $r.Exit | Should -Be 1
        $r.Out | Should -Match 'FAIL sprk_event: the subgrid New rule sprk.SecureChild.sprk_event.NativeNewAllowed.EnableRule is missing'
        $r.Out | Should -Match 'FAIL sprk_document: the subgrid New rule was not observed'
    }

    It 'does not look at the subgrid rule of a table the script does not serve (must not fire)' {
        $c = New-ClosedCapture
        $c.tables['sprk_matter'].newRule = 'no-table'
        $c.tables['sprk_servicerequest'].Remove('newRule')
        (Invoke-Verify $c).Exit | Should -Be 0
    }

    It 'fails when a redirected table has lost its home-grid replacement button' {
        $c = New-ClosedCapture
        $c.tables['sprk_event'].present = @('sprk.NativeCreate.sprk_event.Form.New')
        $r = Invoke-Verify $c
        $r.Exit | Should -Be 1
        $r.Out | Should -Match 'FAIL sprk_event: .*the redirect button is missing: sprk.Event.NewWizard.Grid.Button'
    }

    It 'fails when the platform home-grid New is shown on a redirected table, a report card, a budget or a KPI assessment' {
        foreach ($t in 'sprk_matter', 'sprk_reportcard', 'sprk_budget', 'sprk_kpiassessment') {
            $c = New-ClosedCapture
            $c.tables[$t].present += "Mscrm.HomepageGrid.$t.NewRecord"
            $r = Invoke-Verify $c
            $r.Exit | Should -Be 1
            $r.Out | Should -Match "FAIL $($t): platform New still shown: Mscrm.HomepageGrid.$($t).NewRecord"
        }
    }

    It 'fails and names the table when Add Memo is not on its ribbon (it was not live on dev)' {
        $c = New-ClosedCapture
        $c.tables['sprk_event'].present = @($c.tables['sprk_event'].present | Where-Object { $_ -ne 'Spaarke.Event.AddMemo.Button' })
        $r = Invoke-Verify $c
        $r.Exit | Should -Be 1
        $r.Out | Should -Match 'FAIL sprk_event: .*Spaarke.Event.AddMemo.Button'
        $r.Out | Should -Match 'NOT MET - 1 of 23 table\(s\): sprk_event'
    }

    It 'MUST FIRE: fails and names the KPI assessment table when "Add KPI Assessment" is still on its ribbon (task 066)' {
        $c = New-ClosedCapture
        $c.tables['sprk_kpiassessment'].present += 'sprk.matter.subgrid.kpi.AddKpiButton'
        $r = Invoke-Verify $c
        $r.Exit | Should -Be 1
        $r.Out | Should -Match 'FAIL sprk_kpiassessment: a retired Spaarke button is still shown: sprk.matter.subgrid.kpi.AddKpiButton'
        $r.Out | Should -Match 'NOT MET - 1 of 23 table\(s\): sprk_kpiassessment'
    }

    It 'MUST NOT FIRE: passes the KPI assessment table without the button, and ignores that id on a table that does not retire it (task 066)' {
        $c = New-ClosedCapture
        @($c.tables['sprk_kpiassessment'].present) | Should -Not -Contain 'sprk.matter.subgrid.kpi.AddKpiButton'
        $c.tables['sprk_matter'].present += 'sprk.matter.subgrid.kpi.AddKpiButton'
        $r = Invoke-Verify $c
        $r.Exit | Should -Be 0
        $r.Out | Should -Match 'PASS \(23 tables\)'
    }

    It 'fails a report card whose form New is not the wizard launcher' {
        $c = New-ClosedCapture
        $c.tables['sprk_reportcard'].present = @()
        $r = Invoke-Verify $c
        $r.Exit | Should -Be 1
        $r.Out | Should -Match 'sprk_reportcard: the redirect button is missing: sprk.NativeCreate.sprk_reportcard.Form.New'
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
        $c.tables['sprk_budget'].present += 'Mscrm.Form.sprk_budget.NewRecord'   # a kept table's FORM New is reported, not judged
        $c.tables['sprk_memo'].present += 'Mscrm.HomepageGrid.sprk_memo.NewRecord'   # OQ-1: memo New stays native
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
        ($redirected -join ',') | Should -Be 'sprk_communication,sprk_document,sprk_event,sprk_matter,sprk_project,sprk_reportcard,sprk_todo,sprk_workassignment'
        # Owner OQ-1 (2026-11-26): budget and KPI assessment keep the form / subgrid New and lose only the home-grid New; memo and analysis keep all New.
        $homeHidden = @($cfg.tables | Where-Object { $_.homeGridNew -eq 'hidden' } | ForEach-Object { $_.table } | Sort-Object)
        ($homeHidden -join ',') | Should -Be 'sprk_budget,sprk_kpiassessment'
        # Task 066: the KPI subgrid keeps ONE create (+ New); the second button is retired, and only the KPI assessment retires it.
        $retired = @($cfg.tables | Where-Object { $_.mustBeAbsent } | ForEach-Object { $_.table })
        ($retired -join ',') | Should -Be 'sprk_kpiassessment'
        @(($cfg.tables | Where-Object { $_.table -eq 'sprk_kpiassessment' }).mustBeAbsent) | Should -Be @('sprk.matter.subgrid.kpi.AddKpiButton')
        @($cfg.tables | Where-Object { $_.alsoPresent } | ForEach-Object { $_.table }) | Should -Be @('sprk_event')
        foreach ($kept in 'sprk_memo', 'sprk_analysis') { ($cfg.tables | Where-Object { $_.table -eq $kept }).nativeNew | Should -Be 'kept' }
        foreach ($excluded in 'sprk_invoicesubmission', 'sprk_invoiceapproval', 'sprk_externalidentifier', 'sprk_invoicecorrection', 'sprk_signal', 'sprk_decisionrecord', 'sprk_budgetrevision', 'sprk_servicerequest') {
            ($cfg.tables | Where-Object { $_.table -eq $excluded }).nativeNew | Should -Be 'kept'
        }
    }
}

Describe 'native-create-surfaces.json against the secure-child script and the SpaarkeMaster ribbons' {
    It 'marks subgridRule on exactly the tables sprk_secure_child_ribbon.js serves (ns.TABLES)' {
        $js = Get-Content -Raw (Join-Path $repoRoot 'src/client/webresources/js/sprk_secure_child_ribbon.js')
        $list = [regex]::Match($js, 'ns\.TABLES\s*=\s*\[(?<l>[^\]]*)\]').Groups['l'].Value
        $served = @([regex]::Matches($list, 'sprk_[a-z]+') | ForEach-Object { $_.Value } | Sort-Object -Unique)
        $marked = @($cfg.tables | Where-Object { $_.subgridRule -eq $true } | ForEach-Object { $_.table } | Sort-Object)
        ($marked -join ',') | Should -Be ($served -join ',')
    }

    It 'every SpaarkeMaster ribbon carries what the config says (hides, launcher buttons, the home-grid New)' {
        $entities = Join-Path $repoRoot 'src/dataverse/solutions/SpaarkeMaster/Entities'
        foreach ($e in $cfg.tables) {
            $dir = Get-ChildItem $entities -Directory | Where-Object { $_.Name.ToLower() -eq $e.table } | Select-Object -First 1
            $dir | Should -Not -BeNullOrEmpty -Because "$($e.table) has a SpaarkeMaster entity folder"
            [xml]$x = Get-Content -Raw (Join-Path $dir.FullName 'RibbonDiff.xml')
            $hides = @($x.SelectNodes('//*[local-name()="HideCustomAction"]') | ForEach-Object { $_.GetAttribute('Location') })
            $ids = @($x.SelectNodes('//*[@Id]') | ForEach-Object { $_.GetAttribute('Id') })
            foreach ($s in 'Form', 'HomepageGrid', 'SubGrid') { $hides | Should -Contain "Mscrm.$s.$($e.table).Assign" }
            if ($e.nativeNew -eq 'redirected') {
                foreach ($loc in "Mscrm.HomepageGrid.$($e.table).NewRecord", "Mscrm.Form.$($e.table).NewRecord", "Mscrm.Form.$($e.table).SaveAndNew") { $hides | Should -Contain $loc }
                $ids | Should -Contain "sprk.NativeCreate.$($e.table).Form.New"
                if ($e.gridButtonId) { $ids | Should -Contain $e.gridButtonId }
            }
            foreach ($retiredId in @($e.mustBeAbsent)) { if ($retiredId) { $ids | Should -Not -Contain $retiredId -Because "$retiredId is retired (task 066)" } }
            if ($e.nativeNew -eq 'kept' -and $e.homeGridNew -eq 'hidden') { $hides | Should -Contain "Mscrm.HomepageGrid.$($e.table).NewRecord" }
            if ($e.nativeNew -eq 'kept' -and -not $e.homeGridNew) { @($hides | Where-Object { $_ -match '^Mscrm.(Form|HomepageGrid)..*.(NewRecord|SaveAndNew)$' }).Count | Should -Be 0 }   # the analysis ribbon already hides its SUBGRID New (AnalysisRibbons)
        }
    }

    It 'every SpaarkeMaster secure-child rule passes its own table (the shipped defect: seven tables passed none)' {
        $entities = Join-Path $repoRoot 'src/dataverse/solutions/SpaarkeMaster/Entities'
        $missing = @()
        foreach ($e in @($cfg.tables | Where-Object { $_.subgridRule -eq $true })) {
            $dir = Get-ChildItem $entities -Directory | Where-Object { $_.Name.ToLower() -eq $e.table } | Select-Object -First 1
            [xml]$x = Get-Content -Raw (Join-Path $dir.FullName 'RibbonDiff.xml')
            foreach ($rule in 'NativeNewAllowed', 'NewAvailable') {
                $node = $x.SelectSingleNode("//*[local-name()='EnableRule' and @Id='sprk.SecureChild.$($e.table).$rule.EnableRule' and *]")
                $carries = $node -and $node.SelectSingleNode(".//*[local-name()='StringParameter' and @Value='$($e.table)']")
                if (-not $carries) { $missing += "$($e.table).$rule" }
            }
        }
        $missing | Should -BeNullOrEmpty
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

    It 'hides ONLY the home-grid New on a kept table that says homeGridNew = hidden, with no replacement (owner OQ-1)' {
        & $merge -RibbonDiff $seed -Entity sprk_budget -Out $seed | Out-Null
        [xml]$x = Get-Content -Raw $seed
        $hides = @($x.SelectNodes('//*[local-name()="HideCustomAction"]') | ForEach-Object { $_.GetAttribute('Location') })
        $hides | Should -Contain 'Mscrm.HomepageGrid.sprk_budget.NewRecord'
        @($hides | Where-Object { $_ -like 'Mscrm.Form.*New*' -or $_ -like 'Mscrm.Form.*SaveAndNew' -or $_ -like 'Mscrm.SubGrid.*New*' }).Count | Should -Be 0
        $x.SelectSingleNode('//*[starts-with(@Id,"sprk.NativeCreate.")]') | Should -BeNullOrEmpty
    }

    It 'gives the report card its form button and no home-grid button' {
        & $merge -RibbonDiff $seed -Entity sprk_reportcard -Out $seed | Out-Null
        [xml]$x = Get-Content -Raw $seed
        $x.SelectSingleNode('//*[@Id="sprk.NativeCreate.sprk_reportcard.Form.New"]') | Should -Not -BeNullOrEmpty
        $x.SelectSingleNode('//*[@Id="sprk.NativeCreate.sprk_reportcard.Grid.New"]') | Should -BeNullOrEmpty
        $hides = @($x.SelectNodes('//*[local-name()="HideCustomAction"]') | ForEach-Object { $_.GetAttribute('Location') })
        $hides | Should -Contain 'Mscrm.HomepageGrid.sprk_reportcard.NewRecord'
        $x.SelectSingleNode('//*[local-name()="JavaScriptFunction" and @FunctionName="Spaarke.Commands.Wizards.openCreateReportCardFromForm"]') | Should -Not -BeNullOrEmpty
    }

    It 'writes -Out only after its checks, through a temporary file that does not linger (K1)' {
        # The earlier script wrote -Out first and checked afterwards, so a failed check left a checked-nothing file in place.
        $text = Get-Content -Raw $merge
        $save = $text.IndexOf('$ribbon.Save($writer)')
        $firstThrow = $text.IndexOf('throw "The merge dropped')
        $move = $text.IndexOf('Move-Item -LiteralPath $temp -Destination $Out')
        $save | Should -BeGreaterThan 0
        $firstThrow | Should -BeGreaterThan $save
        $move | Should -BeGreaterThan $firstThrow
        $text | Should -Not -Match 'XmlWriter\]::Create\(\$Out'
        & $merge -RibbonDiff $seed -Entity sprk_todo -Out $seed | Out-Null
        @(Get-ChildItem (Split-Path $seed) -Filter '*.tmp').Count | Should -Be 0
    }

    It 'MUST FIRE (task 066): removes a retired button, its command and its labels from a fresh export and keeps every other command' {
        @'
<?xml version="1.0" encoding="utf-8"?>
<RibbonDiffXml>
  <CustomActions>
    <CustomAction Id="sprk.matter.subgrid.kpi.AddKpiButton.CustomAction" Location="Mscrm.SubGrid.sprk_kpiassessment.MainTab.Management.Controls._children" Sequence="10">
      <CommandUIDefinition><Button Id="sprk.matter.subgrid.kpi.AddKpiButton" Command="sprk.matter.subgrid.kpi.AddKpiButton.Command" LabelText="$LocLabels:sprk.matter.subgrid.kpi.AddKpiButton.LabelText" TemplateAlias="o1" /></CommandUIDefinition>
    </CustomAction>
  </CustomActions>
  <Templates><RibbonTemplates Id="Mscrm.Templates"></RibbonTemplates></Templates>
  <CommandDefinitions>
    <CommandDefinition Id="sprk.Existing.Command"><EnableRules /><DisplayRules /><Actions /></CommandDefinition>
    <CommandDefinition Id="sprk.matter.subgrid.kpi.AddKpiButton.Command"><EnableRules /><DisplayRules /><Actions /></CommandDefinition>
  </CommandDefinitions>
  <RuleDefinitions><TabDisplayRules /><DisplayRules /><EnableRules /></RuleDefinitions>
  <LocLabels>
    <LocLabel Id="sprk.matter.subgrid.kpi.AddKpiButton.LabelText"><Titles><Title description="+ Add KPI" languagecode="1033" /></Titles></LocLabel>
    <LocLabel Id="sprk.Existing.LabelText"><Titles><Title description="Keep" languagecode="1033" /></Titles></LocLabel>
  </LocLabels>
</RibbonDiffXml>
'@ | Set-Content -LiteralPath $seed -Encoding utf8
        & $merge -RibbonDiff $seed -Entity sprk_kpiassessment -Out $seed | Out-Null
        [xml]$x = Get-Content -Raw $seed
        $x.SelectSingleNode('//*[@Id="sprk.matter.subgrid.kpi.AddKpiButton"]') | Should -BeNullOrEmpty
        $x.SelectSingleNode('//*[@Id="sprk.matter.subgrid.kpi.AddKpiButton.CustomAction"]') | Should -BeNullOrEmpty
        $x.SelectSingleNode('//*[@Id="sprk.matter.subgrid.kpi.AddKpiButton.Command"]') | Should -BeNullOrEmpty
        $x.SelectSingleNode('//*[starts-with(@Id,"sprk.matter.subgrid.kpi.AddKpiButton")]') | Should -BeNullOrEmpty
        $x.SelectSingleNode('//*[@Id="sprk.Existing.Command"]') | Should -Not -BeNullOrEmpty
        $x.SelectSingleNode('//*[@Id="sprk.Existing.LabelText"]') | Should -Not -BeNullOrEmpty
        # Idempotent: a second run changes nothing.
        $first = (Get-FileHash $seed).Hash
        & $merge -RibbonDiff $seed -Entity sprk_kpiassessment -Out $seed | Out-Null
        (Get-FileHash $seed).Hash | Should -Be $first
    }

    It 'MUST NOT FIRE (task 066): leaves the KPI-named nodes of a table that does not retire them untouched' {
        @'
<?xml version="1.0" encoding="utf-8"?>
<RibbonDiffXml>
  <CustomActions />
  <Templates><RibbonTemplates Id="Mscrm.Templates"></RibbonTemplates></Templates>
  <CommandDefinitions>
    <CommandDefinition Id="sprk.matter.subgrid.kpi.AddKpiButton.Command"><EnableRules /><DisplayRules /><Actions /></CommandDefinition>
  </CommandDefinitions>
  <RuleDefinitions><TabDisplayRules /><DisplayRules /><EnableRules /></RuleDefinitions>
  <LocLabels />
</RibbonDiffXml>
'@ | Set-Content -LiteralPath $seed -Encoding utf8
        & $merge -RibbonDiff $seed -Entity sprk_todo -Out $seed | Out-Null
        [xml]$x = Get-Content -Raw $seed
        $x.SelectSingleNode('//*[@Id="sprk.matter.subgrid.kpi.AddKpiButton.Command"]') | Should -Not -BeNullOrEmpty
    }

    It 'rejects a table that is not in the config' {
        { & $merge -RibbonDiff $seed -Entity sprk_nothere -Out $seed } | Should -Throw
    }
}
