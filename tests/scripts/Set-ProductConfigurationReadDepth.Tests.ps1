# tests/scripts/Set-ProductConfigurationReadDepth.Tests.ps1
# ---------------------------------------------------------------------------
# scripts/Set-ProductConfigurationReadDepth.ps1 refuses a config whose Global list names platform service configuration
# (its one list, $PlatformServiceTables, which ProductConfigurationReadRoleGuardTests reads from the script). These tests
# prove, with no live call:
#   1. Get-PlatformServiceViolations fires on each platform table in any letter case and not on a product table;
#   2. the script refuses such a config in the dry run, with -Apply and with -Verify, before it connects;
#   3. must not fire: the real config passes the check (the run then stops at the next, offline check: no role XML).
#
# Pester 3.4:  Import-Module Pester -RequiredVersion 3.4.0 ; Invoke-Pester tests/scripts/Set-ProductConfigurationReadDepth.Tests.ps1
# ---------------------------------------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$script:ScriptPath = Join-Path $repoRoot 'scripts/Set-ProductConfigurationReadDepth.ps1'
$script:ConfigDir = Join-Path $repoRoot 'config'
$script:Work = Join-Path ([System.IO.Path]::GetTempPath()) ("prodcfgread-tests-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $script:Work | Out-Null

# Pull the function under test out of the script (it cannot be dot-sourced: it runs on load).
$errs = $null; $tokens = $null
$script:Ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errs)
$fn = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-PlatformServiceViolations' }, $true) | Select-Object -First 1
Invoke-Expression $fn.Extent.Text

# The script's own list, read from its one assignment (the same line the ArchTest guard parses).
$assign = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and $n.Left.Extent.Text -eq '$PlatformServiceTables' }, $true)
$script:PlatformTables = @(Invoke-Expression $assign[0].Right.Extent.Text)

# A config directory: the given config plus the three exclusion sources the script reads beside it.
function New-ConfigDir([string]$Name, [scriptblock]$Mutate) {
    $dir = Join-Path $script:Work $Name
    New-Item -ItemType Directory -Path $dir | Out-Null
    foreach ($f in 'restricted-content-owner-role.json', 'customer-configuration-owner-role.json', 'secure-record-owner-role.json') {
        Copy-Item -LiteralPath (Join-Path $script:ConfigDir $f) -Destination $dir
    }
    $config = Get-Content -LiteralPath (Join-Path $script:ConfigDir 'product-configuration-read-role.json') -Raw | ConvertFrom-Json
    if ($Mutate) { & $Mutate $config }
    $path = Join-Path $dir 'product-configuration-read-role.json'
    $config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $path -Encoding UTF8
    $path
}

# Runs the script in a child process; never reaches the network for any case below (the checks under test run first).
function Invoke-Script([string]$ConfigPath, [string[]]$ModeArgs) {
    $emptyRoles = Join-Path $script:Work ('no-roles-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $emptyRoles | Out-Null
    $output = & pwsh -NoProfile -NonInteractive -File $script:ScriptPath -EnvironmentUrl 'https://example.invalid' `
        -ConfigPath $ConfigPath -RolesXmlDir $emptyRoles @ModeArgs 2>&1 | Out-String
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

Describe 'Set-ProductConfigurationReadDepth: platform service configuration' {

    It 'holds exactly the three platform tables' {
        (@($script:PlatformTables | Sort-Object) -join ',') | Should Be 'sprk_aimodeldeployment,sprk_dataverseenvironment,sprk_externalserviceconfig'
    }

    It 'Get-PlatformServiceViolations fires on each platform table, in any letter case' {
        foreach ($t in $script:PlatformTables) {
            $rows = @([pscustomobject]@{ logicalName = $t.ToUpperInvariant() }, [pscustomobject]@{ logicalName = 'sprk_aitooltype' })
            $found = @(Get-PlatformServiceViolations $rows $script:PlatformTables)
            $found.Count | Should Be 1
            $found[0] | Should Match ([regex]::Escape($t.ToUpperInvariant()))
        }
    }

    It 'Get-PlatformServiceViolations does not fire on product configuration (must not fire)' {
        $rows = @([pscustomobject]@{ logicalName = 'sprk_aitooltype' }, [pscustomobject]@{ logicalName = 'sprk_gridconfiguration' })
        @(Get-PlatformServiceViolations $rows $script:PlatformTables).Count | Should Be 0
    }

    foreach ($mode in @(@{ Name = 'dry run'; Args = @() }, @{ Name = '-Apply'; Args = @('-Apply') }, @{ Name = '-Verify'; Args = @('-Verify') })) {
        It "refuses a config that lists sprk_externalserviceconfig ($($mode.Name))" {
            $config = New-ConfigDir ('platform-' + $mode.Name.Trim('-').Replace(' ', '')) {
                param($c)
                $c.tables += [pscustomobject]@{
                    logicalName = 'sprk_externalserviceconfig'; privilegeName = 'prvReadsprk_ExternalServiceConfig'
                    reason = 'seeded by the test'; evidence = 'seeded by the test'
                }
            }
            $run = Invoke-Script $config $mode.Args
            $run.ExitCode | Should Not Be 0
            $run.Output | Should Match 'platform service configuration'
            $run.Output | Should Not Match 'Role XML not found'
        }
    }

    It 'passes the real config through the check (must not fire); the run stops at the next offline check' {
        $config = New-ConfigDir 'real' $null
        $run = Invoke-Script $config @('-Verify')
        $run.ExitCode | Should Not Be 0
        $run.Output | Should Not Match 'platform service configuration'
        $run.Output | Should Match 'Role XML not found'
    }
}

Remove-Item -LiteralPath $script:Work -Recurse -Force -ErrorAction SilentlyContinue
